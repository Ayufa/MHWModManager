using System;
using System.IO;

// Verifies the v1.48 game-folder validation (GameDirValidator, compiled
// in from the app itself) against real folders on disk.
static class Program {
    static int failures = 0;

    static void Check(string name, string path, bool expected) {
        bool actual = GameDirValidator.IsGameDirectory(path);
        bool ok = actual == expected;
        if (!ok) failures++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: IsGameDirectory = {actual} (expected {expected})");
    }

    static int Main() {
        string root = Path.Combine(Path.GetTempPath(), "gamedirharness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            // (a) empty folder -> invalid
            string empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(empty);
            Check("a) empty folder", empty, false);

            // (b) folder with MonsterHunterWorld.exe -> valid
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(game);
            File.WriteAllBytes(Path.Combine(game, "MonsterHunterWorld.exe"), new byte[0]);
            Check("b) MonsterHunterWorld.exe present", game, true);

            // (c) case variant monsterhunterworld.EXE -> valid
            string gameCase = Path.Combine(root, "game-case");
            Directory.CreateDirectory(gameCase);
            File.WriteAllBytes(Path.Combine(gameCase, "monsterhunterworld.EXE"), new byte[0]);
            Check("c) case variant monsterhunterworld.EXE", gameCase, true);

            // (d) exe only in a subfolder -> invalid (direct child only)
            string parent = Path.Combine(root, "parent");
            string sub = Path.Combine(parent, "Monster Hunter World");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, "MonsterHunterWorld.exe"), new byte[0]);
            Check("d) exe only in subfolder", parent, false);

            // (e) nonexistent path -> invalid
            Check("e) nonexistent path", Path.Combine(root, "does-not-exist"), false);

            // (f) folder with a different exe -> invalid
            string other = Path.Combine(root, "other");
            Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other, "notepad.exe"), new byte[0]);
            Check("f) different exe (notepad.exe)", other, false);

            // extras: null / empty / whitespace / trailing separator on a valid folder
            Check("extra) null path", null, false);
            Check("extra) empty path", "", false);
            Check("extra) whitespace path", "   ", false);
            Check("extra) valid folder + trailing separator", game + Path.DirectorySeparatorChar, true);
        } finally {
            try { Directory.Delete(root, true); } catch { }
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
