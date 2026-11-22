using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[Serializable]
public class ArchiveFile {
    static ModsData modsData => MainForm.instance.modsData;

    public string path;
    public uint crc;
    public bool isDir;
    public string directory;
    public ArchiveFile parent;
    public List<ArchiveFile> children = new List<ArchiveFile>();
    public ModInfo modBelong;
    public TreeNode belongingNode;

    public float size;
    public string sizeSuffixed => ModInfo.SizeSuffixer(size);


    public bool isTopFile => !isDir && (!parent || (parent.isTopFolder && parent.children.Any(x => x.isDir)));
    public bool isTopFolder => isDir && !parent;

    // Cached results of GetDirectPath()/installedPath. These were recomputed
    // (with several full-string ToLower() copies) on *every* access, and they
    // are accessed inside the hottest loops in the program (per file, per mod).
    // NonSerialized: not part of the save format.
    [NonSerialized] string _directPathCache;
    [NonSerialized] string _directPathModDir;
    [NonSerialized] string _installedPathCache;

    public bool installed = false;
    public uint installedCRC;
    public bool installedNotMatching => installed && installedCRC != crc;

    public string GetDirectPath() {
        string modDir = modsData.modDir;
        if (_directPathCache != null && _directPathModDir == modDir)
            return _directPathCache;

        string result;
        if (string.Equals(path.FixSlashes(), modDir.FixSlashes(), StringComparison.OrdinalIgnoreCase)) {
            result = "";
        } else {
            // Case-insensitive IndexOf: same semantics as the old
            // path.ToLower().IndexOf(modDir.ToLower()) without the allocations.
            int idxModFolder = path.IndexOf(modDir, StringComparison.OrdinalIgnoreCase);
            int subIdx = idxModFolder + modDir.Length + 1;
            result = idxModFolder < 0 || subIdx > path.Length ? path : path.Substring(subIdx);
        }

        _directPathCache = result;
        _directPathModDir = modDir;
        _installedPathCache = null;
        return result;
    }
    public string installedPath {
        get {
            if (_installedPathCache == null)
                _installedPathCache = $"{modBelong.gameModDir}\\{GetDirectPath()}";
            return _installedPathCache;
        }
    }

    public bool GetInstalledStatus() {
        if (isDir) {
            return children.All(x => x.GetInstalledStatus());
        } else {
            return installed && !installedNotMatching;
        }
    }

    public void GetInstalled() {
        GetInstalled(null, CancellationToken.None);
    }

    // crcMemo (optional, shared by the caller for a whole operation) makes
    // sure the same installed path is hashed at most once per pass, and
    // CrcCache additionally skips re-hashing files whose size+mtime are
    // unchanged since the last check.
    public void GetInstalled(IDictionary<string, uint> crcMemo, CancellationToken token) {
        if (isDir) {
            foreach (var child in children) {
                token.ThrowIfCancellationRequested();
                child.GetInstalled(crcMemo, token);
            }
            installed = children.All(x => x.installed);
        } else {
            if (installed = File.Exists(installedPath)) {
                installedCRC = crcMemo != null
                    ? CrcCache.GetCrc(installedPath, crcMemo)
                    : CrcCache.GetCrc(installedPath);
            }
        }
    }

    public uint GetInstalledCRC() {
        return CrcCache.GetCrc(installedPath);
    }

    public List<ModInfo> FindModsWithSameFile() {
        return FindModsWithSameFile(null, null);
    }

    // Indexed variant: `sameFileIndex` maps installedPath (case-insensitive)
    // to the mods containing a file with that path, and `crcIndex` maps a
    // file CRC to the mods containing a file with that CRC. Both are built
    // once per operation by ArchiveManager.BuildModFileIndexes(). The old
    // implementation rescanned *all mods x all their files* (with a ToLower()
    // allocation per comparison) for *every single file* -- O(mods x files^2)
    // overall, which is what made large installs crawl.
    public List<ModInfo> FindModsWithSameFile(IDictionary<string, List<ModInfo>> sameFileIndex,
                                              IDictionary<uint, List<ModInfo>> crcIndex) {
        List<ModInfo> sameMods = new List<ModInfo>();

        if (!isDir) {
            if (sameFileIndex != null) {
                List<ModInfo> hits;
                if (sameFileIndex.TryGetValue(installedPath, out hits)) {
                    foreach (var otherMod in hits) {
                        if (otherMod == modBelong) continue;
                        // Preserve the original top-level-file filter.
                        if (otherMod.archiveFiles.Any(file => !file.isDir && (!file.isTopFile || modsData.useTopLevelFiles)
                                && string.Equals(file.installedPath, installedPath, StringComparison.OrdinalIgnoreCase)))
                            sameMods.Add(otherMod);
                    }
                }
            } else {
                foreach (var otherMod in modsData.modInfos) {
                    if (otherMod == modBelong) continue;
                    if (otherMod.archiveFiles == null) continue;

                    if (otherMod.archiveFiles.Any(file => !file.isDir && (!file.isTopFile || modsData.useTopLevelFiles) && file.installedPath.ToLower() == installedPath.ToLower())) {
                        sameMods.Add(otherMod);
                    }
                }
            }
        }

        sameMods.AddRange(FindActualCrcMod(crcIndex));

        return sameMods;
    }

    public IEnumerable<ModInfo> FindActualCrcMod() {
        return FindActualCrcMod(null);
    }

    public IEnumerable<ModInfo> FindActualCrcMod(IDictionary<uint, List<ModInfo>> crcIndex) {
        if (!installedNotMatching) return new ModInfo[0];

        if (crcIndex != null) {
            List<ModInfo> hits;
            return crcIndex.TryGetValue(installedCRC, out hits) ? hits : (IEnumerable<ModInfo>)new ModInfo[0];
        }

        return modsData.modInfos.Where(mod => mod.archiveFiles != null && mod.archiveFiles.Any(file => file.crc == installedCRC));
    }

    public void SetInfo(RichTextBox textBox, TreeNode node) {
        textBox.Clear();

        textBox.AppendText(Lang.T("Path: ")); textBox.AppendText(path, node.ForeColor, true);
        textBox.AppendText(Lang.T("Status: ") + node.ToolTipText, Color.WhiteSmoke, true);
        textBox.AppendText(Lang.T("Install Location: ")); textBox.AppendText(installedPath, Color.LightYellow, true);
        textBox.AppendText(Lang.T("Size: ")); textBox.AppendText(sizeSuffixed, Color.AliceBlue, true);

        if (!isDir) {
            textBox.AppendText("CRC: "); textBox.AppendText(crc.ToString("X"), Color.Cyan, true);

            if (installedNotMatching) {
                textBox.AppendText(Lang.T("Installed CRC: ")); textBox.AppendText(installedCRC.ToString("X"), node.ForeColor, true);
            }

        }
        textBox.AppendText("\n");

        var actMods = FindActualCrcMod();
        if (actMods != null && actMods.Count() > 0) {
            string plural = actMods.Count() > 1 ? "s" : "";
            textBox.AppendText(Lang.S($"Mod{plural} that this file is from:", "このファイルの出所MOD:"), Color.WhiteSmoke, true);
            foreach (var mod in actMods) {
                textBox.AppendText("    " + mod.shortPath, Color.Coral, true);
            }
            textBox.AppendText("\n");
        }

        var sameMods = FindModsWithSameFile().Distinct();
        if (sameMods.Count() > 0) {
            textBox.AppendText(Lang.T("Mods that also change this file: \n"));

            foreach (var mod in sameMods) {
                textBox.AppendText("    " + mod.shortPath, Color.Orange, true);
            }
        }
    }

    public static implicit operator bool(ArchiveFile value) => value != null;
}
