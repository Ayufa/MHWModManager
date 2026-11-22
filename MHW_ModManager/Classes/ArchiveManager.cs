using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SevenZip;
using SharpCompress.Readers;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

public static class ArchiveManager {

    // ------------------------------------------------------------------
    // Index helpers (the core of the large-mod optimisation)
    // ------------------------------------------------------------------

    // Key for matching an archive entry to an ArchiveFile: exact CRC + exact
    // (slash-fixed) path, exactly the predicate the original linear
    // FirstOrDefault(x => x.crc == crc && x.path == path) used -- just O(1).
    static string ArchiveKey(uint crc, string path) {
        return crc.ToString("X8") + "|" + path;
    }

    public static Dictionary<string, ArchiveFile> BuildArchiveFileIndex(ModInfo mod) {
        var index = new Dictionary<string, ArchiveFile>();
        foreach (var file in mod.archiveFiles) {
            string key = ArchiveKey(file.crc, file.path);
            if (!index.ContainsKey(key))
                index.Add(key, file);
        }
        return index;
    }

    // Builds, once per operation:
    //   sameFileIndex: installedPath (case-insensitive) -> mods with that file
    //   crcIndex:      file CRC -> mods containing a file with that CRC
    // Used instead of rescanning all mods x all files for every installed file.
    public static void BuildModFileIndexes(IEnumerable<ModInfo> allMods,
        out Dictionary<string, List<ModInfo>> sameFileIndex,
        out Dictionary<uint, List<ModInfo>> crcIndex) {
        sameFileIndex = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
        crcIndex = new Dictionary<uint, List<ModInfo>>();
        foreach (var mod in allMods) {
            if (mod.archiveFiles == null) continue;
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenCrcs = new HashSet<uint>();
            foreach (var file in mod.archiveFiles) {
                if (file.isDir) continue;
                if (seenPaths.Add(file.installedPath)) {
                    List<ModInfo> list;
                    if (!sameFileIndex.TryGetValue(file.installedPath, out list))
                        sameFileIndex[file.installedPath] = list = new List<ModInfo>();
                    list.Add(mod);
                }
                if (seenCrcs.Add(file.crc)) {
                    List<ModInfo> list;
                    if (!crcIndex.TryGetValue(file.crc, out list))
                        crcIndex[file.crc] = list = new List<ModInfo>();
                    list.Add(mod);
                }
            }
        }
    }

    // Throttled progress reporter: at most one report per 100 ms, plus the
    // final one. Keeps the UI responsive without flooding it per file/byte.
    class ProgressThrottle {
        readonly IProgress<OperationProgress> progress;
        readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        public ProgressThrottle(IProgress<OperationProgress> progress) { this.progress = progress; }
        public void Report(OperationProgress value, bool force = false) {
            if (progress == null) return;
            if (!force && watch.ElapsedMilliseconds < 100) return;
            watch.Restart();
            progress.Report(value);
        }
    }

    // ------------------------------------------------------------------
    // Archive scanning
    // ------------------------------------------------------------------

    public static ArchiveFile[] GetArchiveFiles(ModInfo modInfo) {
        try {
            List<ArchiveFile> fileList = new List<ArchiveFile>();
            using (Stream stream = File.OpenRead(modInfo.modPath)) {

                modInfo.fileSize = stream.Length / 1048576f;


                if (modInfo.sevZip) {
                    using (var extractor = new SevenZipExtractor(stream)) {
                        var files = extractor.ArchiveFileData;
                        foreach (var file in files) {
                            fileList.Add(new ArchiveFile() {
                                path = file.FileName.FixSlashes(),
                                crc = file.Crc,
                                isDir = file.IsDirectory,
                                size = file.Size / 1048576f,
                                directory = file.IsDirectory ? file.FileName.FixSlashes() : ""
                            });
                        }
                    }
                } else {
                    using (var reader = ReaderFactory.Open(stream)) {
                        while (reader.MoveToNextEntry()) {
                            fileList.Add(new ArchiveFile() {
                                path = reader.Entry.Key.FixSlashes(),
                                crc = (uint)reader.Entry.Crc,
                                isDir = reader.Entry.IsDirectory,
                                size = reader.Entry.Size / 1048576f,
                                directory = reader.Entry.IsDirectory ? reader.Entry.Key.FixSlashes() : ""
                            });
                        }
                    }
                }
            }

            // Link parents via a directory lookup built once. The original
            // used fileList.Find(...) per file -- O(n^2) on archives with
            // many files -- and appended synthetic parents while iterating.
            //
            // v1.47 tree fix: the parent chain is now built explicitly,
            // segment by segment (EnsureFolderChain). The previous
            // one-level lookup relied on Path.GetDirectoryName() plus
            // re-processing synthetic parents, and on directory entries
            // matching parentDir strings exactly (case-sensitive). If any
            // level failed to link, that whole subtree never became a
            // top-level-reachable node and the file hierarchy simply did
            // not appear in the tree. Now EVERY intermediate folder is
            // guaranteed to exist as a node in fileList (explicit archive
            // directory entry when present, otherwise a synthesized one),
            // every entry's parent points at a node that is itself in
            // fileList, and folder identity is case-insensitive (one node
            // per distinct folder path).
            var dirLookup = new Dictionary<string, ArchiveFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList) {
                file.modBelong = modInfo;
                if (file.isDir && !dirLookup.ContainsKey(file.path))
                    dirLookup.Add(file.path, file);
            }

            // Snapshot: EnsureFolderChain appends synthesized folders to
            // fileList while we iterate.
            var entries = new List<ArchiveFile>(fileList);
            foreach (var file in entries) {
                string parentDir = ParentDirOf(file.path);
                if (string.IsNullOrEmpty(parentDir))
                    continue;

                ArchiveFile parent = EnsureFolderChain(parentDir, fileList, dirLookup, modInfo);
                if (parent == null || parent == file)
                    continue;

                file.parent = parent;
                if (!parent.children.Contains(file))
                    parent.children.Add(file);
            }

            // Directory sizes in ONE bottom-up pass: deepest directories
            // first, so when a directory is totalled its sub-directories
            // already carry their totals. Total work is O(edges) = O(n).
            // (The original ran children.Sum(...) in two redundant full
            // loops and still only summed direct children.)
            foreach (var dir in fileList.Where(x => x.isDir)
                                        .OrderByDescending(x => Depth(x.path))) {
                float sum = 0;
                foreach (var child in dir.children)
                    sum += child.size;
                dir.size = sum;
            }

            return fileList.ToArray();
        } catch (Exception ex) {
            Console.WriteLine(modInfo.modPath + " : " + ex.Message);
            return null;
        }
    }

    static int Depth(string path) {
        int depth = 0;
        foreach (char c in path)
            if (c == '\\') depth++;
        return depth;
    }

    // Parent folder of a slash-fixed archive path, computed by string
    // position instead of Path.GetDirectoryName (whose result depends on
    // the OS directory separator; archive paths are always '\'-separated
    // after FixSlashes).
    public static string ParentDirOf(string path) {
        if (string.IsNullOrEmpty(path)) return "";
        int idx = path.LastIndexOf('\\');
        return idx < 0 ? "" : path.Substring(0, idx);
    }

    // Returns the folder node for `folderPath`, creating it -- and every
    // missing ancestor above it -- when the archive has no explicit
    // directory entry for it. Created folders are added to `fileList`
    // (so they become part of mod.archiveFiles and of the tree) and are
    // linked to their own parent before being returned, so the chain
    // always terminates at a top-level folder with parent == null.
    public static ArchiveFile EnsureFolderChain(string folderPath, List<ArchiveFile> fileList,
        Dictionary<string, ArchiveFile> dirLookup, ModInfo modInfo) {
        ArchiveFile folder;
        if (dirLookup.TryGetValue(folderPath, out folder))
            return folder;

        folder = new ArchiveFile() {
            isDir = true,
            directory = folderPath,
            path = folderPath,
            modBelong = modInfo
        };
        dirLookup.Add(folderPath, folder);
        fileList.Add(folder);

        string parentDir = ParentDirOf(folderPath);
        if (!string.IsNullOrEmpty(parentDir)) {
            ArchiveFile parent = EnsureFolderChain(parentDir, fileList, dirLookup, modInfo);
            if (parent != null && parent != folder) {
                folder.parent = parent;
                if (!parent.children.Contains(folder))
                    parent.children.Add(folder);
            }
        }
        return folder;
    }

    // ------------------------------------------------------------------
    // Install
    // ------------------------------------------------------------------

    public static void InstallSelected(ModInfo mod, bool refresh = true) {
        InstallSelected(mod, refresh, null, CancellationToken.None, null);
    }

    // progress: throttled progress reports (may be null).
    // token:    cancellation for the whole operation.
    // prompt:   overwrite prompt callback (file, index, fileCount) -> decision.
    //           When null, the original modal dialog is shown directly (only
    //           valid on the UI thread; MainForm passes a callback that
    //           marshals the dialog to the UI thread while installing in the
    //           background).
    public static void InstallSelected(ModInfo mod, bool refresh,
        IProgress<OperationProgress> progress, CancellationToken token,
        Func<ArchiveFile, int, int, OverwriteDecision> prompt) {
        try {
            if (!File.Exists(mod.modPath)) {
                MainForm.AppendModLog(Lang.T("\nInstall Failed!\n     Mod archive not found!"), Color.Crimson, true);
                return;
            }

            bool skipWarnings = false;
            bool overwrite = false;
            int count = -1;
            int fileCounter = 0;

            // Built ONCE per install (was: linear scans per file).
            var archiveIndex = BuildArchiveFileIndex(mod);
            Dictionary<string, List<ModInfo>> sameFileIndex;
            Dictionary<uint, List<ModInfo>> crcIndex;
            BuildModFileIndexes(MainForm.instance.modsData.modInfos, out sameFileIndex, out crcIndex);

            var modsToRefresh = new HashSet<ModInfo>();
            var reporter = new ProgressThrottle(progress);

            using (Stream stream = File.OpenRead(mod.modPath)) {


                if (mod.sevZip) {
                    using (var extractor = new SevenZipExtractor(stream)) {

                        int fileCount = extractor.ArchiveFileData.Count(x => !x.IsDirectory);

                        // Single sequential pass in archive order (the order
                        // SevenZipExtractor.ExtractFile(index, ...) requires
                        // to stay a single decompression pass). ExtractFiles()
                        // to a directory can't be used here: files must land
                        // at installedPath (modDir-relative), not at their
                        // raw archive paths. Writes use a 1 MiB buffered
                        // FileStream with SequentialScan instead of the
                        // default 4 KiB File.OpenWrite.
                        foreach (var fileData in extractor.ArchiveFileData) {
                            token.ThrowIfCancellationRequested();
                            count++;
                            if (fileData.IsDirectory) continue;
                            fileCounter++;

                            ArchiveFile matchFile;
                            if (!archiveIndex.TryGetValue(ArchiveKey(fileData.Crc, fileData.FileName.FixSlashes()), out matchFile))
                                matchFile = null;

                            reporter.Report(new OperationProgress {
                                Operation = "Installing", CurrentFile = fileData.FileName,
                                FilesDone = fileCounter, FilesTotal = fileCount
                            });

                            if (matchFile && matchFile.belongingNode.Checked) {

                                if (File.Exists(matchFile.installedPath)) {
                                    if (!matchFile.installedNotMatching)
                                        continue;

                                    AskOverwrite(prompt, ref skipWarnings, ref overwrite, fileCounter, fileCount, matchFile, crcIndex);

                                    if (!overwrite)
                                        continue;

                                    File.Delete(matchFile.installedPath);
                                    CrcCache.Invalidate(matchFile.installedPath);
                                }

                                foreach (var m in matchFile.FindModsWithSameFile(sameFileIndex, crcIndex))
                                    modsToRefresh.Add(m);

                                Directory.CreateDirectory(Path.GetDirectoryName(matchFile.installedPath));
                                using (var fileStream = new FileStream(matchFile.installedPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan)) {
                                    extractor.ExtractFile(count, fileStream);
                                }
                                CrcCache.Invalidate(matchFile.installedPath);
                            }
                        }
                    }
                } else {
                    using (var reader = ReaderFactory.Open(stream)) {

                        int fileCount = mod.archiveFiles.Count(x => !x.isDir);

                        while (reader.MoveToNextEntry()) {
                            token.ThrowIfCancellationRequested();
                            count++;
                            if (reader.Entry.IsDirectory) continue;
                            fileCounter++;

                            uint fileCRC = (uint)reader.Entry.Crc;
                            ArchiveFile matchFile;
                            if (!archiveIndex.TryGetValue(ArchiveKey(fileCRC, reader.Entry.Key.FixSlashes()), out matchFile))
                                matchFile = null;

                            reporter.Report(new OperationProgress {
                                Operation = "Installing", CurrentFile = reader.Entry.Key,
                                FilesDone = fileCounter, FilesTotal = fileCount
                            });

                            if (matchFile && matchFile.belongingNode.Checked) {

                                if (File.Exists(matchFile.installedPath)) {
                                    if (!matchFile.installedNotMatching)
                                        continue;

                                    AskOverwrite(prompt, ref skipWarnings, ref overwrite, fileCounter, fileCount, matchFile, crcIndex);

                                    if (!overwrite)
                                        continue;
                                }

                                foreach (var m in matchFile.FindModsWithSameFile(sameFileIndex, crcIndex))
                                    modsToRefresh.Add(m);

                                Directory.CreateDirectory(Path.GetDirectoryName(matchFile.installedPath));
                                reader.WriteEntryToFile(matchFile.installedPath, new SharpCompress.Common.ExtractionOptions() {
                                    PreserveFileTime = true, PreserveAttributes = false, ExtractFullPath = true, Overwrite = true
                                });
                                CrcCache.Invalidate(matchFile.installedPath);
                            }
                        }
                    }
                }
            }

            reporter.Report(new OperationProgress {
                Operation = "Installing", CurrentFile = "",
                FilesDone = fileCounter, FilesTotal = fileCounter
            }, force: true);

            // Only the mods actually affected are re-checked (as before),
            // but now with one shared CRC memo for the whole pass: the same
            // installed path is hashed at most once, and unchanged files
            // are served from the stamp-validated CrcCache.
            var crcMemo = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var refreshMod in modsToRefresh) {
                refreshMod.CheckIfInstalled(crcMemo, token);
            }



            if (refresh) {
                MainForm.RunOnUi(() => {
                    MainForm.SaveData();
                    MainForm.RefreshListView();
                    MainForm.RefreshTreeView();

                    MainForm.instance.richBoxMod.AppendText("\n");
                    MainForm.instance.richBoxMod.AppendText(Lang.T("Install Succesful!"), Color.LawnGreen, true);
                });
            }

        } catch (OperationCanceledException) {
            MainForm.AppendModLog(Lang.T("\nInstall Cancelled."), Color.Orange, true);
        } catch (Exception ex) {
            Console.WriteLine(ex);

            MainForm.AppendModLog("\n", Color.White, false);
            MainForm.AppendModLog(Lang.T("Install Failed! Please copy and paste this error\n     and send it to the mod author please!"), Color.Crimson, true);
            MainForm.AppendModLog(ex.ToString(), Color.Salmon, true);
        }
    }

    static void AskOverwrite(Func<ArchiveFile, int, int, OverwriteDecision> prompt,
        ref bool skipWarnings, ref bool overwrite, int index, int fileCount,
        ArchiveFile matchFile, Dictionary<uint, List<ModInfo>> crcIndex) {
        if (skipWarnings) return;

        OverwriteDecision decision = prompt != null
            ? prompt(matchFile, index, fileCount)
            : ShowOverwriteDialog(matchFile, index, fileCount, crcIndex);

        overwrite = decision == OverwriteDecision.Overwrite || decision == OverwriteDecision.OverwriteSkipWarnings;
        if (decision == OverwriteDecision.OverwriteSkipWarnings || decision == OverwriteDecision.SkipSkipWarnings)
            skipWarnings = true;
    }

    // The original overwrite dialog. Must run on the UI thread.
    public static OverwriteDecision ShowOverwriteDialog(ArchiveFile matchFile, int index, int fileCount) {
        return ShowOverwriteDialog(matchFile, index, fileCount, null);
    }

    public static OverwriteDecision ShowOverwriteDialog(ArchiveFile matchFile, int index, int fileCount,
        Dictionary<uint, List<ModInfo>> crcIndex) {
        InputBoxForm warnDialogue = new InputBoxForm();
        warnDialogue.richTextBox1.WordWrap = true;
        warnDialogue.YesNoSetup(Lang.F("Overwrite file? ({0} of {1})", index, fileCount), "");

        warnDialogue.richTextBox1.AppendText(matchFile.path, Color.Cyan, true);
        var actMods = matchFile.FindActualCrcMod(crcIndex);
        if (actMods.Count() > 0) {
            warnDialogue.richTextBox1.AppendText(Lang.T("The file is from:"), Color.WhiteSmoke, true);
            foreach (var rMod in actMods) {
                warnDialogue.richTextBox1.AppendText(rMod.shortPath, Color.Orange, true);
            }
        } else {
            warnDialogue.richTextBox1.AppendText(Lang.T("It is unknown what mod this file belongs to."), Color.Coral, true);
        }

        warnDialogue.richTextBox1.SelectAll();
        warnDialogue.richTextBox1.SelectionAlignment = HorizontalAlignment.Center;


        var result = warnDialogue.ShowDialog();

        if (result == DialogResult.OK) return OverwriteDecision.OverwriteSkipWarnings;
        if (result == DialogResult.Ignore) return OverwriteDecision.SkipSkipWarnings;
        return result == DialogResult.Yes ? OverwriteDecision.Overwrite : OverwriteDecision.Skip;
    }


    public static void UninstallSelected(ModInfo mod, bool refresh = true, bool checkForMatchingCRC = false) {
        UninstallSelected(mod, refresh, checkForMatchingCRC, null, CancellationToken.None);
    }

    public static void UninstallSelected(ModInfo mod, bool refresh, bool checkForMatchingCRC,
        IProgress<OperationProgress> progress, CancellationToken token) {
        var reporter = new ProgressThrottle(progress);
        int total = mod.archiveFiles.Count(x => !x.isDir);
        int done = 0;

        foreach (var file in mod.archiveFiles) {
            token.ThrowIfCancellationRequested();
            if (!file.isDir) {
                done++;
                reporter.Report(new OperationProgress {
                    Operation = "Uninstalling", CurrentFile = file.path,
                    FilesDone = done, FilesTotal = total
                });
            }
            if (!file.isDir && file.belongingNode.Checked) {
                if (File.Exists(file.installedPath)) {
                    if (checkForMatchingCRC) {
                        uint fileCRC = file.GetInstalledCRC();
                        if (file.crc != fileCRC)
                            goto finish;
                    }

                    RecycleManager.DeleteNoWarn(file.installedPath);
                    CrcCache.Invalidate(file.installedPath);
                    file.installedCRC = 0;
                }

                finish:
                file.installed = false;
            }
        }

        reporter.Report(new OperationProgress {
            Operation = "Uninstalling", CurrentFile = "", FilesDone = done, FilesTotal = total
        }, force: true);

        if (refresh) {
            MainForm.RunOnUi(() => {
                MainForm.SaveData();
                MainForm.RefreshListView();
                MainForm.RefreshTreeView();
            });
        }
    }


    public static void DeleteEmptyDirs(string startLocation) {
        foreach (var directory in Directory.GetDirectories(startLocation)) {
            DeleteEmptyDirs(directory);
            if (Directory.GetFiles(directory).Length == 0 &&
                Directory.GetDirectories(directory).Length == 0) {
                Directory.Delete(directory, false);
            }
        }
    }


}
