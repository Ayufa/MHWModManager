using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public class ModCache {

    public static void RescanModCacheFolder() {
        RescanModCacheFolder(null, CancellationToken.None);
    }

    public static void RescanModCacheFolder(IProgress<OperationProgress> progress, CancellationToken token) {
        Directory.CreateDirectory(Serializer.GetModsCacheFolder());

        string[] modFiles = Directory.GetFiles(Serializer.GetModsCacheFolder(), "*", SearchOption.AllDirectories);
        CheckAddNewMods(modFiles, token);

        MainForm.RunOnUi(() => {
            MainForm.SaveData();
            MainForm.RefreshListView();
        });
    }


    public static void CheckAddNewMods(string[] paths) {
        CheckAddNewMods(paths, CancellationToken.None);
    }

    public static void CheckAddNewMods(string[] paths, CancellationToken token) {
        IEnumerable<string> newMods = paths.Where(filePath => !MainForm.instance.modsData.modInfos.Exists(mI => mI.modPath == filePath));

        foreach (string file in newMods) {
            token.ThrowIfCancellationRequested();
            ModInfo matchMod = MainForm.instance.modsData.modInfos.Find(x => x.modName == Path.GetFileName(file));

            if (matchMod) {
                matchMod.modPath = file;

            } else {
                ModInfo tMod = new ModInfo(file);
                if (tMod.archiveFiles != null) {
                    MainForm.instance.modsData.modInfos.Add(tMod);
                    foreach (var archFile in tMod.archiveFiles) {
                        if (!archFile.parent)
                            archFile.GetInstalled();
                    }
                }
            }
        }
    }

    public static void ReCheckMods(IEnumerable<ModInfo> modInfos) {
        ReCheckMods(modInfos, CancellationToken.None);
    }

    public static void ReCheckMods(IEnumerable<ModInfo> modInfos, CancellationToken token) {
        foreach (var mod in modInfos) {
            token.ThrowIfCancellationRequested();
            if (!mod.archiveExists)
                continue;

            mod.archiveFiles = ArchiveManager.GetArchiveFiles(mod);

            if (mod.archiveFiles != null) {
                foreach (var archFile in mod.archiveFiles) {
                    if (!archFile.parent)
                        archFile.GetInstalled();
                }
            }
        }
    }

}

