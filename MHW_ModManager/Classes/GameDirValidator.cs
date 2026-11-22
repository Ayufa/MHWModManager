using System;
using System.IO;

// ----------------------------------------------------------------------
// Game folder validation (v1.48)
//
// Before v1.48 the game-directory textbox accepted ANY existing folder:
// textBoxGameDir_TextChanged only checked Directory.Exists, so a random
// folder (Desktop, Downloads, ...) got the green check mark and was
// adopted as modsData.gameDir, retargeting every install there.
//
// A folder now only counts as the game folder when it directly contains
// the game executable, MonsterHunterWorld.exe -- the distinctive marker
// of a Monster Hunter: World / Iceborne install. Direct child only: an
// exe buried in a subfolder does not make its parent a game folder.
// ----------------------------------------------------------------------
public static class GameDirValidator {

    public const string GameExeName = "MonsterHunterWorld.exe";

    public static bool IsGameDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try {
            if (!Directory.Exists(path))
                return false;

            // GetFiles without a SearchOption is top-directory only.
            foreach (string file in Directory.GetFiles(path)) {
                if (string.Equals(Path.GetFileName(file), GameExeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        } catch {
            // Unreadable / racing-removed folders are simply not valid.
            return false;
        }

        return false;
    }
}
