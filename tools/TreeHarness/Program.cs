using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

// Verifies the v1.47 file-hierarchy (tree) fix in ArchiveManager.GetArchiveFiles.
// The linking code below is transcribed shape-for-shape from the app:
//   OLD = v1.46 one-level Path.GetDirectoryName lookup (dictionary variant)
//   NEW = v1.47 EnsureFolderChain (segment-by-segment folder synthesis)
// Path.GetDirectoryName is emulated with Windows semantics (archive paths
// are '\'-separated after FixSlashes), because this harness runs on Linux
// where Path.GetDirectoryName would not split on '\'.
//
// Test archives are REAL zip files written to disk and read back, in two
// flavours: with explicit directory entries and without any.

class AF {
    public string path; public bool isDir; public string directory = "";
    public AF parent; public List<AF> children = new List<AF>();
}

static class Program {
    static string FixSlashes(string str) {
        string r = str.Replace('/', '\\');
        if (r.Length > 0 && r[r.Length - 1] == '\\') r = r.Substring(0, r.Length - 1);
        return r;
    }
    static string GetDirectoryNameWin(string p) {
        int i = p.LastIndexOf('\\');
        return i < 0 ? "" : p.Substring(0, i);
    }
    static string ParentDirOf(string p) { // v1.47 helper, verbatim logic
        if (string.IsNullOrEmpty(p)) return "";
        int idx = p.LastIndexOf('\\');
        return idx < 0 ? "" : p.Substring(0, idx);
    }

    // ---- OLD (v1.46) ----
    static List<AF> OldLink(List<AF> fileList) {
        var dirLookup = new Dictionary<string, AF>();
        foreach (var f in fileList)
            if (f.isDir && !dirLookup.ContainsKey(f.directory)) dirLookup.Add(f.directory, f);
        var pending = new List<AF>(fileList);
        for (int i = 0; i < pending.Count; i++) {
            var file = pending[i];
            string parentDir = GetDirectoryNameWin(file.path);
            if (string.IsNullOrEmpty(parentDir)) continue;
            AF parent;
            if (!dirLookup.TryGetValue(parentDir, out parent)) {
                parent = new AF { isDir = true, directory = parentDir, path = FixSlashes(parentDir) };
                dirLookup.Add(parentDir, parent); fileList.Add(parent); pending.Add(parent);
            }
            file.parent = parent; parent.children.Add(file);
        }
        return fileList;
    }

    // ---- NEW (v1.47) ----
    static AF EnsureFolderChain(string folderPath, List<AF> fileList, Dictionary<string, AF> dirLookup) {
        AF folder;
        if (dirLookup.TryGetValue(folderPath, out folder)) return folder;
        folder = new AF { isDir = true, directory = folderPath, path = folderPath };
        dirLookup.Add(folderPath, folder); fileList.Add(folder);
        string parentDir = ParentDirOf(folderPath);
        if (!string.IsNullOrEmpty(parentDir)) {
            AF parent = EnsureFolderChain(parentDir, fileList, dirLookup);
            if (parent != null && parent != folder) {
                folder.parent = parent;
                if (!parent.children.Contains(folder)) parent.children.Add(folder);
            }
        }
        return folder;
    }
    static List<AF> NewLink(List<AF> fileList) {
        var dirLookup = new Dictionary<string, AF>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fileList)
            if (f.isDir && !dirLookup.ContainsKey(f.path)) dirLookup.Add(f.path, f);
        var entries = new List<AF>(fileList);
        foreach (var file in entries) {
            string parentDir = ParentDirOf(file.path);
            if (string.IsNullOrEmpty(parentDir)) continue;
            AF parent = EnsureFolderChain(parentDir, fileList, dirLookup);
            if (parent == null || parent == file) continue;
            file.parent = parent;
            if (!parent.children.Contains(file)) parent.children.Add(file);
        }
        return fileList;
    }

    static List<AF> ReadZip(string zipPath) {
        var list = new List<AF>();
        using (var za = ZipFile.OpenRead(zipPath)) {
            foreach (var e in za.Entries) {
                bool isDir = e.FullName.EndsWith("/");
                list.Add(new AF {
                    path = FixSlashes(e.FullName), isDir = isDir,
                    directory = isDir ? FixSlashes(e.FullName) : ""
                });
            }
        }
        return list;
    }

    static void MakeZip(string zipPath, string[] entries) {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var za = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
            foreach (var name in entries) {
                var e = za.CreateEntry(name);
                if (!name.EndsWith("/")) {
                    using (var w = new StreamWriter(e.Open())) w.Write("data:" + name);
                }
            }
        }
    }

    static int Reachable(List<AF> all, out int roots, out List<string> lines) {
        var tops = all.Where(x => x.parent == null).ToList();
        roots = tops.Count;
        var collected = new List<string>();
        var seen = new HashSet<AF>();
        void Rec(AF f, int d) {
            if (!seen.Add(f)) { collected.Add(new string(' ', d * 2) + "!!CYCLE " + f.path); return; }
            collected.Add(new string(' ', d * 2) + f.path);
            foreach (var c in f.children) Rec(c, d + 1);
        }
        foreach (var t in tops) Rec(t, 0);
        lines = collected;
        return seen.Count;
    }

    static bool failures = false;
    static void Case(string label, string[] zipEntries, int expectFiles) {
        string zip = Path.Combine(Path.GetTempPath(), "treeharness_" + label.Replace(' ', '_') + ".zip");
        MakeZip(zip, zipEntries);
        foreach (var mode in new[] { "OLD", "NEW" }) {
            var raw = ReadZip(zip);
            var linked = mode == "OLD" ? OldLink(raw) : NewLink(raw);
            int reach = Reachable(linked, out int roots, out var lines);
            bool ok = reach == linked.Count;
            Console.WriteLine($"[{label}] {mode}: entries={zipEntries.Length} nodes={linked.Count} roots={roots} reachable={reach}/{linked.Count} -> {(ok ? "OK" : "BROKEN")}");
            if (mode == "NEW") {
                foreach (var l in lines) Console.WriteLine("   " + l);
                int files = linked.Count(x => !x.isDir);
                if (files != expectFiles) { Console.WriteLine($"   !! expected {expectFiles} file nodes, got {files}"); ok = false; }
                if (!ok) failures = true;
            }
        }
        Console.WriteLine();
    }

    static void Main() {
        // 1: explicit directory entries at every level
        Case("with-dirs", new[] {
            "nativePC/", "nativePC/xxx/", "nativePC/xxx/yyy/",
            "nativePC/xxx/yyy/file.bin", "nativePC/xxx/a.txt", "nativePC/top.bin" }, 3);
        // 2: NO directory entries at all (very common for zip mods)
        Case("without-dirs", new[] {
            "nativePC/xxx/yyy/file.bin", "nativePC/xxx/a.txt", "nativePC/top.bin" }, 3);
        // 3: partial directory entries (only the top folder exists)
        Case("partial-dirs", new[] {
            "nativePC/", "nativePC/xxx/yyy/file.bin", "nativePC/xxx/yyy/zzz/deep.bin" }, 2);
        // 4: directory entry whose case differs from the files' folders
        //    (old case-sensitive lookup splits this into two branches)
        Case("case-mismatch", new[] {
            "NativePC/", "NativePC/XXX/", "nativePC/xxx/yyy/file.bin" }, 1);
        // 5: deep nesting, no dirs
        Case("deep-no-dirs", new[] {
            "a/b/c/d/e/f.bin", "a/b/c/d/e/g.bin", "a/b/h.bin" }, 3);

        Console.WriteLine(failures ? "RESULT: FAIL" : "RESULT: ALL NEW-LINK CASES PASS");
        Environment.Exit(failures ? 1 : 0);
    }
}
