using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;
using UICancelOption = Microsoft.VisualBasic.FileIO.UICancelOption;
using System.Runtime.InteropServices;

public partial class MainForm : Form {


    public const float programVersion = 1.47f;

    public static MainForm instance;
    public ModsData modsData;

    // --- Background operation plumbing (large-mod optimisation) ---
    // Archive scanning / installing / uninstalling / CRC re-checks used to
    // run entirely on the UI thread, so Windows marked the window
    // "Not Responding" on large mods. They now run on a worker thread via
    // RunOperationAsync, with throttled progress and cancellation.
    CancellationTokenSource opCts;
    bool opRunning;
    long lastProgressUiMs;

    // Runs `action` on the UI thread (blocking) when called from a worker.
    public static void RunOnUi(Action action) {
        if (instance == null) { action(); return; }
        if (instance.InvokeRequired)
            instance.Invoke(action);
        else
            action();
    }

    // Thread-safe append to the mod info box (ArchiveManager calls this
    // from worker threads).
    public static void AppendModLog(string text, Color color, bool addNewLine) {
        RunOnUi(() => instance.richBoxMod.AppendText(text, color, addNewLine));
    }

    async Task RunOperationAsync(string operationName, Action<IProgress<OperationProgress>, CancellationToken> work) {
        if (opRunning) return;
        opRunning = true;
        opCts = new CancellationTokenSource();
        SetOperationUi(operationName, true);

        var progress = new Progress<OperationProgress>(ReportProgress);
        try {
            await Task.Run(() => work(progress, opCts.Token));
        } catch (OperationCanceledException) {
            AppendModLog(Lang.F("{0} cancelled.", Lang.T(operationName)), Color.Orange, true);
        } catch (Exception ex) {
            Console.WriteLine(ex);
            AppendModLog(Lang.F("{0} failed: {1}", Lang.T(operationName), ex.Message), Color.Crimson, true);
        } finally {
            SetOperationUi(operationName, false);
            opRunning = false;
            opCts.Dispose();
            opCts = null;
        }
    }

    void ReportProgress(OperationProgress value) {
        // Second throttle layer on the UI side (~50 ms) so the progress
        // bar never becomes its own bottleneck.
        long now = Environment.TickCount;
        if (now - lastProgressUiMs < 50 && value.Percent < 100) return;
        lastProgressUiMs = now;

        progressBarOperation.Maximum = Math.Max(1, value.FilesTotal);
        progressBarOperation.Value = Math.Min(progressBarOperation.Maximum, value.FilesDone);
        labelProgress.Text = value.FilesTotal > 0
            ? Lang.F("{0}: {1} / {2} files  {3}", Lang.T(value.Operation), value.FilesDone, value.FilesTotal, value.CurrentFile)
            : Lang.F("{0}: {1}", Lang.T(value.Operation), value.CurrentFile);
    }

    void SetOperationUi(string operationName, bool running) {
        panelProgress.Visible = running;
        if (running) {
            progressBarOperation.Value = 0;
            labelProgress.Text = Lang.T(operationName) + "...";
        }
        buttonInstallSelected.Enabled = !running;
        buttonUninstallSelected.Enabled = !running;
        buttonRescanCache.Enabled = !running;
        buttonRescanInstall.Enabled = !running;
        buttonCheckAll.Enabled = !running;
        buttonUncheckAll.Enabled = !running;
    }

    private void buttonCancelOperation_Click(object sender, EventArgs e) {
        if (opCts != null) opCts.Cancel();
    }

    // Overwrite prompt that marshals the modal dialog to the UI thread
    // while the install itself runs in the background.
    OverwriteDecision PromptOverwriteOnUi(ArchiveFile file, int index, int fileCount) {
        OverwriteDecision decision = OverwriteDecision.Skip;
        RunOnUi(() => { decision = ArchiveManager.ShowOverwriteDialog(file, index, fileCount); });
        return decision;
    }

    public MainForm() {
        instance = this;
        InitializeComponent();
        // v1.47: one font family that renders English and Japanese with
        // the same metrics, applied to every control (and the context
        // menu, which is not part of the control tree).
        Lang.ApplyFont(this);
        contextList.Font = new Font("Yu Gothic UI", 9F);
        CaptureOriginalToolTips();
        Init();
        ApplyLanguage();
    }

    // Original (English) tooltip texts, captured before the first
    // ApplyLanguage() so language switches can always translate from the
    // English source instead of stacking translations.
    readonly Dictionary<Control, string> originalToolTips = new Dictionary<Control, string>();
    void CaptureOriginalToolTips() {
        foreach (var c in new Control[] { buttonRescanCache, buttonRescanInstall, buttonDeleteNotFound, checkBoxIgnoreTopFiles, buttonSaveLoadout, buttonLoadLoadout }) {
            string t = tip.GetToolTip(c);
            if (!string.IsNullOrEmpty(t)) originalToolTips[c] = t;
        }
    }

    bool langSwitching;
    private void comboBoxLanguage_SelectedIndexChanged(object sender, EventArgs e) {
        if (langSwitching) return;
        Lang.SetJapanese(comboBoxLanguage.SelectedIndex == 1);
        ApplyLanguage();
        RefreshListView();
        RefreshTreeView();
    }

    // Applies the current Lang choice to every user-visible string on
    // this form. Called at startup and on every language switch; no
    // restart is required.
    void ApplyLanguage() {
        langSwitching = true;
        try {
            comboBoxLanguage.SelectedIndex = Lang.Japanese ? 1 : 0;
        } finally { langSwitching = false; }

        labelLanguage.Text = Lang.T("Language:");
        label2.Text = Lang.T("Game Directory:");
        label1.Text = Lang.T("Mod Folder:");
        checkBoxIgnoreTopFiles.Text = Lang.T("Ignore Top-Level Files");
        buttonSaveLoadout.Text = Lang.T("Save Loadout");
        buttonLoadLoadout.Text = Lang.T("Load Loadout");

        olvColumnName.Text = Lang.T("Name");
        olvColumnInstalled.Text = Lang.T("Status");
        olvColumnSize.Text = Lang.T("Size");
        olvColumnDate.Text = Lang.T("Date Created");
        olvColumnCat.Text = Lang.T("Category");
        olvModList.EmptyListMsg = Lang.T("Drag mod archives here or place them in folders and hit rescan cache to add them to mod cache.");

        deleteModArchiveToolStripMenuItem.Text = Lang.T("Delete Mod Archive And Uninstall");
        deleteModArchiveWithoutUninstallingToolStripMenuItem.Text = Lang.T("Delete Mod Archive Without Uninstalling");
        installAllFilesToolStripMenuItem.Text = Lang.T("Install ALL Files");
        uninstallAllFilesToolStripMenuItem.Text = Lang.T("Uninstall ALL Files");
        openLocationToolStripMenuItem.Text = Lang.T("Open Location");
        reloadArchiveToolStripMenuItem.Text = Lang.T("Reload Archive");

        buttonRescanCache.Text = Lang.T("Re-Scan Cache");
        buttonDeleteNotFound.Text = Lang.T("Remove Missing Archives");
        buttonRescanInstall.Text = Lang.T("Re-Scan Installations");
        buttonCheckAll.Text = Lang.T("Check All Files");
        buttonUncheckAll.Text = Lang.T("Uncheck All Files");
        label3.Text = Lang.T("Mod Archive Info:");
        label4.Text = Lang.T("Archive File Info:");
        buttonInstallSelected.Text = Lang.T("Install Selected Files");
        buttonUninstallSelected.Text = Lang.T("Uninstall Selected Files");
        buttonCancelOperation.Text = Lang.T("Cancel");
        if (linkNewUpdate.Visible && (linkNewUpdate.Text == "New Update Available!" || linkNewUpdate.Text == "新しい更新があります！"))
            linkNewUpdate.Text = Lang.T("New Update Available!");

        foreach (var pair in originalToolTips)
            tip.SetToolTip(pair.Key, Lang.T(pair.Value));

        // The game-folder ✓/X tooltip follows the current validation
        // state and the newly applied language.
        UpdateGameDirToolTip();
    }


    void Init() {
        string zipPath = Serializer.GetMMDataFolder() + "7z.dll";
        SevenZip.SevenZipBase.SetLibraryPath(zipPath);

        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;

        LoadData();

        olvColumnSize.AspectToStringConverter = (object sizeFloat) => ModInfo.SizeSuffixer((float)sizeFloat);

        labelTitle.Text = $"MHW Mod Managaer V{programVersion} - By: BoltMan";

        NexusUpdateChecker.CheckForNewVersion();
    }

    void LoadData() {
        modsData = Serializer.LoadObj(Serializer.GetDataFilePath()) as ModsData;
        if (!modsData) {
            modsData = new ModsData();
        } else {
            SaveUpdater.UpdateSaveData(modsData);
        }

        //modsData.modInfos = new List<ModInfo>();

        textBoxGameDir.Text = modsData.gameDir;
        textBoxModFolder.Text = modsData.modDir;

        checkBoxIgnoreTopFiles.Checked = !modsData.useTopLevelFiles;
    }

    private void textBoxModFolder_TextChanged(object sender, EventArgs e) {
        textBoxModFolder.Text = textBoxModFolder.Text.FixSlashes().Replace("\\", "");
        modsData.modDir = textBoxModFolder.Text;
    }

    private async void Form1_Shown(object sender, EventArgs e) {
        await RunOperationAsync("Scanning mod cache", (progress, token) =>
            ModCache.RescanModCacheFolder(progress, token));
        RefreshListView();

        GetSelectedMod();
        // Build the hierarchy for the initially selected mod explicitly;
        // previously this only happened if ItemSelectionChanged fired.
        RefreshTreeView();
        olvModList.Focus();
    }


    public static void RefreshListView() {
        instance.olvModList.BeginUpdate();

        var oldSel = instance.olvModList.SelectedObject;

        instance.olvModList.Items.Clear();
        instance.olvModList.SetObjects(instance.modsData.modInfos, true);

        //
        //foreach(ModInfo mod in instance.modsData.modInfos) {
        //    if(!mod.archiveExists) {
        //        instance.olvModList.GetItem(instance.olvModList.IndexOf(mod)).ForeColor = Color.Orange;
        //    }
        //}


        instance.olvModList.EndUpdate();

        if (oldSel != null) {
            instance.olvModList.SelectedObject = oldSel;
            instance.olvModList.EnsureModelVisible(oldSel);
        }
    }


    private void olvModList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e) {
        RefreshTreeView();
        richBoxFile.Clear();
    }

    private void treeViewFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod || e.Node == null) return;

        ArchiveFile selFile = selMod.archiveFiles.FirstOrDefault(x => x.belongingNode == e.Node);
        if (!selFile) return;

        selFile.SetInfo(richBoxFile, e.Node);
        //richBoxFile;
    }

    public static void RefreshTreeView() {
        // v1.47 tree fix: the original code called BeginUpdate() and then
        // had several ways to never reach EndUpdate() -- an early
        // `if (!selMod) return;` and any exception from SetInfo /
        // RecurseTreeView / the Check* passes (e.g. a mod in the list
        // whose archiveFiles is null after a failed parse). Each of those
        // left the TreeView cleared AND update-locked, so selecting a mod
        // showed no file hierarchy at all. Now EndUpdate() is guaranteed
        // (finally), the info pane and the status pass are individually
        // guarded, and a null archive list is handled.
        var tree = instance.treeViewFiles;
        tree.BeginUpdate();
        try {
            tree.Nodes.Clear();

            ModInfo selMod = instance.GetSelectedMod();
            if (!selMod) return;

            instance.labelSelectedMod.Text = selMod.modName;

            try { selMod.SetInfo(instance.richBoxMod); } catch (Exception ex) { Console.WriteLine(ex); }

            if (selMod.archiveFiles == null) return;

            var topFiles = selMod.archiveFiles.Where(x => !x.parent).ToArray();
            var visited = new HashSet<ArchiveFile>();
            foreach (ArchiveFile file in topFiles) {
                RecurseTreeView(file, null, visited);
            }

            try {
                if (selMod.installed || selMod.partiallyInstalled) {
                    CheckTreeFilesInstalled(topFiles);
                } else {
                    CheckTreeFilesNotInstalled(topFiles);
                }
            } catch (Exception ex) { Console.WriteLine(ex); }
        } catch (Exception ex) {
            Console.WriteLine(ex);
        } finally {
            tree.EndUpdate();
        }
    }

    static void CheckTreeFilesInstalled(IEnumerable<ArchiveFile> topFiles) {
        foreach (var file in topFiles) {
            // A file that is not reachable from the tree roots has no node;
            // skip its styling instead of throwing (which used to abort
            // the whole RefreshTreeView between Begin/EndUpdate).
            if (file.belongingNode == null) { CheckTreeFilesInstalled(file.children); continue; }
            file.belongingNode.Checked = file.GetInstalledStatus();
            if (!file.isDir && file.isTopFile && !instance.modsData.useTopLevelFiles) {
                file.belongingNode.ForeColor = Color.DarkGray;
                file.belongingNode.ToolTipText = Lang.T("Ignored because it is a top-level file and the \"ignore top-level files\" option is on.");

            } else if (file.installed) {
                if (file.installedNotMatching) {
                    file.belongingNode.ForeColor = Color.Orange;
                    file.belongingNode.ToolTipText = Lang.T("The file exists in the folder but it is not from this mod. (Checksums don't match)\n" +
                        "   Select this file to see what mod the file actually belongs to.");
                } else {
                    file.belongingNode.ForeColor = Color.LawnGreen;
                    file.belongingNode.ToolTipText = Lang.T("This file is installed in the folder and the checksum matches this mod.");
                }
            } else {
                file.belongingNode.ForeColor = Color.LightYellow;
                file.belongingNode.ToolTipText = Lang.T("This file isn't found at all in the folder.");
            }
            CheckTreeFilesInstalled(file.children);
        }
    }

    static void CheckTreeFilesNotInstalled(IEnumerable<ArchiveFile> topFiles) {
        foreach (var file in topFiles) {
            if (file.belongingNode == null) { CheckTreeFilesNotInstalled(file.children); continue; }
            file.belongingNode.Checked = instance.modsData.useTopLevelFiles || !file.isTopFile;
            if (!file.isDir && File.Exists(file.installedPath)) {
                file.belongingNode.ToolTipText = Lang.T("File exists from another mod.");
                file.belongingNode.ForeColor = Color.Coral;
            } else {
                if (file.belongingNode.Checked) {
                    file.belongingNode.ToolTipText = Lang.T("File does not exist in the mod folder and is ready to be installed.");
                    file.belongingNode.ForeColor = Color.WhiteSmoke;
                } else {
                    file.belongingNode.ToolTipText = Lang.T("File is ignored because it is a top-level file and the \"ignore top-level files\" option is on.");
                    file.belongingNode.ForeColor = Color.DarkGray;
                }
            }

            CheckTreeFilesNotInstalled(file.children);
        }
    }

    static void RecurseTreeView(ArchiveFile pFile, TreeNode pNode = null, HashSet<ArchiveFile> visited = null) {
        // Cycle/duplicate guard: parent/children graphs loaded from old
        // save data could revisit a node; never recurse forever.
        if (visited != null && !visited.Add(pFile)) return;

        TreeNode node = new TreeNode(Path.GetFileName(pFile.path));
        pFile.belongingNode = node;

        if (pNode == null) {
            // One unreadable installed file must not blank the whole
            // tree: guard the status probe per root node.
            try { pFile.GetInstalled(); } catch (Exception ex) { Console.WriteLine(ex); }
            instance.treeViewFiles.Nodes.Add(node);
        } else {
            pNode.Nodes.Add(node);
        }

        foreach (ArchiveFile childFile in pFile.children) {
            RecurseTreeView(childFile, node, visited);
        }

        node.Expand();
    }

    public ModInfo GetSelectedMod() {
        if (olvModList.GetItemCount() == 0) {
            return null;
        }

        ModInfo selMod = olvModList.SelectedObject as ModInfo;

        if (!selMod) {
            olvModList.SelectedIndex = 0;
            selMod = olvModList.GetModelObject(0) as ModInfo;
        }

        return selMod;
    }




    private void treeViewFiles_AfterCheck(object sender, TreeViewEventArgs e) {
        if (e.Action == TreeViewAction.Unknown) return;
        treeViewFiles.BeginUpdate();


        RecurseAfterCheckDown(e.Node);

        if (e.Node.Parent != null)
            RecurseAfterCheckUp(e.Node.Parent);

        treeViewFiles.SelectedNode = e.Node;

        treeViewFiles.EndUpdate();
    }

    void RecurseAfterCheckUp(TreeNode node) {
        bool check = true;
        foreach (TreeNode childNode in node.Nodes) {
            if (!childNode.Checked)
                check = false;
        }
        node.Checked = check;

        if (node.Parent != null)
            RecurseAfterCheckUp(node.Parent);
    }
    void RecurseAfterCheckDown(TreeNode node) {
        foreach (TreeNode childNode in node.Nodes) {
            childNode.Checked = node.Checked;
            RecurseAfterCheckDown(childNode);
        }
    }


    private async void buttonRescanCache_Click(object sender, EventArgs e) {
        await RunOperationAsync("Scanning mod cache", (progress, token) =>
            ModCache.RescanModCacheFolder(progress, token));

        olvModList.Refresh();
        RefreshTreeView();
        RefreshListView();
    }

    private async void buttonRescanInstall_Click(object sender, EventArgs e) {
        await RunOperationAsync("Re-scanning installations", (progress, token) => {
            ModCache.ReCheckMods(modsData.modInfos, token);

            // One CRC memo shared by every mod in this pass: a file shared
            // by several mods is hashed once, and unchanged files are
            // served from the stamp-validated CrcCache instead of disk.
            var crcMemo = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            int done = 0;
            foreach (var mod in modsData.modInfos) {
                token.ThrowIfCancellationRequested();
                mod.CheckIfInstalled(crcMemo, token);
                done++;
                ((IProgress<OperationProgress>)progress).Report(new OperationProgress {
                    Operation = "Re-scanning installations", CurrentFile = mod.modName,
                    FilesDone = done, FilesTotal = modsData.modInfos.Count
                });
            }
        });

        SaveData();

        olvModList.Refresh();
        RefreshTreeView();
    }


    //✔ ✓
    // v1.48: a folder only counts as the game folder when it directly
    // contains MonsterHunterWorld.exe (GameDirValidator). Before, any
    // existing folder got the green check and was adopted as gameDir.
    // Invalid input still does NOT touch modsData.gameDir, so a typo or a
    // pasted wrong path never retargets installs away from the last valid
    // game folder.
    bool gameDirValid;
    void UpdateGameDirToolTip() {
        tip.SetToolTip(labelGameDirExists, Lang.T(gameDirValid
            ? "Game folder (MonsterHunterWorld.exe found)"
            : "Not a game folder (MonsterHunterWorld.exe not found)"));
    }

    private void textBoxGameDir_TextChanged(object sender, EventArgs e) {
        gameDirValid = GameDirValidator.IsGameDirectory(textBoxGameDir.Text);
        if (gameDirValid) {
            labelGameDirExists.ForeColor = Color.LawnGreen;
            labelGameDirExists.Text = "✓";
            modsData.gameDir = textBoxGameDir.Text;
        } else {
            labelGameDirExists.ForeColor = Color.Tomato;
            labelGameDirExists.Text = "X";
        }
        UpdateGameDirToolTip();
    }



    private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
        SaveData();
    }

    public static void SaveData() {
        Serializer.SaveObj(instance.modsData, Serializer.GetDataFilePath());
    }


    private void olvModList_DragEnter(object sender, DragEventArgs e) {
        e.Effect = DragDropEffects.Move;
    }

    private async void olvModList_DragDrop(object sender, DragEventArgs e) {
        string modCacheFolder = Serializer.GetModsCacheFolder();
        Directory.CreateDirectory(modCacheFolder);

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];

        if (files.Length == 0)
            return;

        string[] modFiles = Directory.GetFiles(modCacheFolder, "*", SearchOption.AllDirectories);
        string lastAdded = null;


        foreach (string file in files) {
            bool isDir = Directory.Exists(file);
            string fileName = Path.GetFileName(file);

            if (isDir) {
                FileSystem.MoveDirectory(file, modCacheFolder + fileName, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
            } else {
                if (!modFiles.Contains(file) && !File.Exists(modCacheFolder + fileName) && File.Exists(file)) {
                    File.Move(file, modCacheFolder + fileName);
                }
                lastAdded = modCacheFolder + fileName;
            }

        }

        await RunOperationAsync("Scanning mod cache", (progress, token) =>
            ModCache.RescanModCacheFolder(progress, token));

        if (lastAdded != null) {
            olvModList.SelectedObject = modsData.modInfos.FindLast(x => x.modPath.ToLower() == lastAdded.ToLower());
            olvModList.EnsureModelVisible(olvModList.SelectedObject);
        }


    }

    private void deleteModArchiveToolStripMenuItem_Click(object sender, EventArgs e) {
        uninstallAllFilesToolStripMenuItem_Click(sender, e);

        deleteModArchiveWithoutUninstallingToolStripMenuItem_Click(sender, e);
    }

    private void contextList_Opening(object sender, CancelEventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) {
            e.Cancel = true;
            return;
        }

        openLocationToolStripMenuItem.Enabled = File.Exists(selMod.modPath);


        installAllFilesToolStripMenuItem.Enabled = !selMod.installed && selMod.archiveExists;
    }

    private void deleteModArchiveWithoutUninstallingToolStripMenuItem_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        if (File.Exists(selMod.modPath)) {
            RecycleManager.DeleteNoWarn(selMod.modPath);
        }

        modsData.modInfos.Remove(selMod);

        RefreshListView();
    }

    private async void uninstallAllFilesToolStripMenuItem_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        await RunOperationAsync("Uninstalling", (progress, token) =>
            ArchiveManager.UninstallSelected(selMod, true, false, progress, token));
    }

    private async void installAllFilesToolStripMenuItem_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        buttonCheckAll_Click(null, null);
        await RunOperationAsync("Installing", (progress, token) =>
            ArchiveManager.InstallSelected(selMod, true, progress, token, PromptOverwriteOnUi));
        if (Directory.Exists(Serializer.GetGameModFolder()))
            ArchiveManager.DeleteEmptyDirs(Serializer.GetGameModFolder());
    }

    private void olvModList_AfterSorting(object sender, BrightIdeasSoftware.AfterSortingEventArgs e) {
        var obj = olvModList.SelectedObject;
        if (obj != null)
            olvModList.EnsureModelVisible(obj);
    }

    private async void buttonUninstallSelected_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        await RunOperationAsync("Uninstalling", (progress, token) =>
            ArchiveManager.UninstallSelected(selMod, true, false, progress, token));
        if (Directory.Exists(Serializer.GetGameModFolder()))
            ArchiveManager.DeleteEmptyDirs(Serializer.GetGameModFolder());
    }

    private async void buttonInstallSelected_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        await RunOperationAsync("Installing", (progress, token) =>
            ArchiveManager.InstallSelected(selMod, true, progress, token, PromptOverwriteOnUi));
    }

    private void buttonUncheckAll_Click(object sender, EventArgs e) {
        foreach (TreeNode node in treeViewFiles.Nodes) {
            CheckAll(false);
        }
    }
    private void buttonCheckAll_Click(object sender, EventArgs e) {
        foreach (TreeNode node in treeViewFiles.Nodes) {
            CheckAll(true);
        }
    }

    void CheckAll(bool state) {

        var mod = GetSelectedMod();
        if (!mod) return;

        foreach (var file in mod.archiveFiles) {
            if (!file.isTopFile || modsData.useTopLevelFiles) {
                file.belongingNode.Checked = state;
            }
        }
    }

    private void openLocationToolStripMenuItem_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        if (File.Exists(selMod.modPath)) {
            string argument = "/select, \"" + selMod.modPath + "\"";

            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
    }

    private void buttonDeleteNotFound_Click(object sender, EventArgs e) {
        modsData.modInfos.RemoveAll(mod => !File.Exists(mod.modPath));

        RefreshListView();
    }

    private async void reloadArchiveToolStripMenuItem_Click(object sender, EventArgs e) {
        ModInfo selMod = GetSelectedMod();
        if (!selMod) return;

        await RunOperationAsync("Reloading archive", (progress, token) =>
            ModCache.ReCheckMods(new ModInfo[] { selMod }, token));

        RefreshListView();
        RefreshTreeView();
    }

    private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
        System.Diagnostics.Process.Start("https://www.nexusmods.com/monsterhunterworld/mods/372?tab=files");
    }


    private void buttonSaveLoadout_Click(object sender, EventArgs e) {
        LoadoutManager.SaveLoadout();
    }

    private void buttonLoadLoadout_Click(object sender, EventArgs e) {
        LoadoutManager.LoadLoadout();
    }

    private void linkCredits_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e) {
        var credBox = InputBoxForm.OKBOX("Credits");
        credBox.TitleLabel.ForeColor = Color.White;

        credBox.richTextBox1.Height += 20;
        credBox.Height += 25;

        credBox.richTextBox1.Font = new Font("Yu Gothic UI", 12);
        credBox.panelBG.BorderStyle = BorderStyle.Fixed3D;

        credBox.richTextBox1.AppendText("BoltMan: Project Creator", Color.MediumSlateBlue, true);
        credBox.richTextBox1.AppendText("UncleClapton: Repo Manager/Contributor", Color.RoyalBlue, true);
        credBox.richTextBox1.AppendText("", Color.RoyalBlue, true);
        credBox.richTextBox1.AppendText("You can contribute @ https://github.com/UncleClapton/MHWModManager", Color.Orange, false);

        credBox.richTextBox1.CenterText();

        credBox.ShowDialog();
    }

    //window managemnet
    private void buttonClose_Click(object sender, EventArgs e) {
        Close();
    }

    private void button1_Click(object sender, EventArgs e) {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private void button2_Click(object sender, EventArgs e) {
        WindowState = FormWindowState.Minimized;
    }

    private void checkBoxUseTopFiles_CheckedChanged(object sender, EventArgs e) {
        modsData.useTopLevelFiles = !checkBoxIgnoreTopFiles.Checked;

        olvModList.Refresh();
        RefreshTreeView();
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int left, top, right, bottom;

        public RECT(Rectangle rc) {
            this.left = rc.Left;
            this.top = rc.Top;
            this.right = rc.Right;
            this.bottom = rc.Bottom;
        }

        public Rectangle ToRectangle() {
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS {
        public IntPtr hWnd, hWndInsertAfter;
        public int x, y, cx, cy, flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS {
        public RECT rgrc0, rgrc1, rgrc2;
        public WINDOWPOS lppos;
    }


    protected override void WndProc(ref Message m) {
        const uint WM_NCHITTEST = 0x0084;
        const uint WM_MOUSEMOVE = 0x0200;
        const uint HTLEFT = 10;
        const uint HTRIGHT = 11;
        const uint HTBOTTOMRIGHT = 17;
        const uint HTBOTTOM = 15;
        const uint HTBOTTOMLEFT = 16;
        const uint HTTOP = 12;
        const uint HTTOPLEFT = 13;
        const uint HTTOPRIGHT = 14;

        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;
        const int WM_NCCALCSIZE = 0x83;

        const int RESIZE_HANDLE_SIZE = 10;
        bool handled = false;
        if (m.Msg == WM_NCHITTEST || m.Msg == WM_MOUSEMOVE) {
            Size formSize = this.Size;
            Point screenPoint = new Point(m.LParam.ToInt32());
            Point clientPoint = this.PointToClient(screenPoint);

            Dictionary<uint, Rectangle> boxes = new Dictionary<uint, Rectangle>() {
            {HTBOTTOMLEFT, new Rectangle(0, formSize.Height - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE)},
            {HTBOTTOM, new Rectangle(RESIZE_HANDLE_SIZE, formSize.Height - RESIZE_HANDLE_SIZE, formSize.Width - 2*RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE)},
            {HTBOTTOMRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, formSize.Height - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE)},
            {HTRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, formSize.Height - 2*RESIZE_HANDLE_SIZE)},
            {HTTOPRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, 0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE) },
            {HTTOP, new Rectangle(RESIZE_HANDLE_SIZE, 0, formSize.Width - 2*RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE) },
            {HTTOPLEFT, new Rectangle(0, 0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE) },
            {HTLEFT, new Rectangle(0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, formSize.Height - 2*RESIZE_HANDLE_SIZE) }
        };

            foreach (KeyValuePair<uint, Rectangle> hitBox in boxes) {
                if (hitBox.Value.Contains(clientPoint)) {
                    m.Result = (IntPtr)hitBox.Key;
                    handled = true;
                    break;
                }
            }
        }

        if (m.Msg == WM_NCCALCSIZE) {
            if (m.WParam.Equals(IntPtr.Zero)) {
                RECT rc = (RECT)m.GetLParam(typeof(RECT));
                Rectangle r = rc.ToRectangle();
                //r.Inflate(0, 0);
                Marshal.StructureToPtr(new RECT(r), m.LParam, true);
            } else {
                NCCALCSIZE_PARAMS csp = (NCCALCSIZE_PARAMS)m.GetLParam(typeof(NCCALCSIZE_PARAMS));
                Rectangle r = csp.rgrc0.ToRectangle();
                //r.Inflate(0,0);
                csp.rgrc0 = new RECT(r);
                Marshal.StructureToPtr(csp, m.LParam, true);
            }
            m.Result = IntPtr.Zero;
            handled = true;
        }


        if (!handled)
            base.WndProc(ref m);
    }

    
}
