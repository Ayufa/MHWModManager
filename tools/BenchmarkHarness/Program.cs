using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

// Benchmark harness for the MHWModManager large-mod optimisation.
// It replicates, shape-for-shape:
//  (A) the ORIGINAL lookup code paths:
//      - ArchiveManager.InstallSelected: mod.archiveFiles.FirstOrDefault(crc && path) per entry
//      - ArchiveFile.FindModsWithSameFile: for each file, scan ALL mods x ALL files
//        with installedPath.ToLower() == installedPath.ToLower() per comparison
//      - ArchiveManager.GetArchiveFiles: fileList.Find(directory == parentDir) per file
//  (B) the NEW code paths added in the optimized copy:
//      - Dictionary keyed by (crc,path); installedPath->mods / crc->mods indexes
//        built once (ArchiveManager.BuildArchiveFileIndex / BuildModFileIndexes)
//      - directory lookup dictionary
//  (C) a real large archive (5,000 small files + one 20 MiB file, created as
//      /tmp/bench/large_mod.zip): entries are read from the actual zip and
//      matched old-style vs new-style.
//  (D) CRC memoization effect: hashing the same 20 MiB installed file
//      10 times (old behaviour across refreshes) vs once + memo/cache hits.

class FakeFile {
    public string path; public uint crc; public bool isDir; public string directory;
    public string installedPath; public FakeMod modBelong;
    public List<FakeFile> children = new List<FakeFile>();
    public FakeFile parent; public float size;
}
class FakeMod { public List<FakeFile> archiveFiles = new List<FakeFile>(); public string name; }

static class Program {
    static string ArchiveKey(uint crc, string path) { return crc.ToString("X8") + "|" + path; }

    static int Main() {
        const int MODS = 60;          // 60 mods in cache
        const int FILES_PER_MOD = 500; // x 500 files each = 30,000 archive files total
        var mods = new List<FakeMod>();
        var rnd = new Random(42);
        for (int m = 0; m < MODS; m++) {
            var mod = new FakeMod { name = "mod" + m };
            for (int f = 0; f < FILES_PER_MOD; f++) {
                // 10% of files share an installedPath with other mods (conflicts are the interesting case)
                string shared = (f % 10 == 0) ? "shared" + (f / 10) : "m" + m + "f" + f;
                mod.archiveFiles.Add(new FakeFile {
                    path = "nativePC\\data\\" + shared + ".tex",
                    crc = (uint)rnd.Next(),
                    installedPath = "C:\\Games\\MHW\\nativePC\\data\\" + shared + ".tex",
                    modBelong = mod
                });
            }
            mods.Add(mod);
        }
        Console.WriteLine($"Synthetic cache: {MODS} mods x {FILES_PER_MOD} files = {MODS * FILES_PER_MOD} files");

        // ---------- (A1) OLD: per-entry FirstOrDefault over archiveFiles ----------
        var target = mods[0];
        var sw = Stopwatch.StartNew();
        int oldMatches = 0;
        foreach (var entry in target.archiveFiles) {
            var hit = target.archiveFiles.FirstOrDefault(x => x.crc == entry.crc && x.path == entry.path);
            if (hit != null) oldMatches++;
        }
        sw.Stop();
        Console.WriteLine($"OLD  InstallSelected archive match (500 entries, linear FirstOrDefault): {sw.ElapsedMilliseconds} ms, matches={oldMatches}");

        // warm-up (JIT) so the dictionary measurement below is not first-use cost
        { var w = new Dictionary<string, FakeFile>(); foreach (var f in target.archiveFiles) { var k = ArchiveKey(f.crc, f.path); if (!w.ContainsKey(k)) w.Add(k, f); } }

        // ---------- (B1) NEW: dictionary ----------
        sw.Restart();
        var index = new Dictionary<string, FakeFile>();
        foreach (var f in target.archiveFiles) { var k = ArchiveKey(f.crc, f.path); if (!index.ContainsKey(k)) index.Add(k, f); }
        int newMatches = 0;
        foreach (var entry in target.archiveFiles) { FakeFile hit; if (index.TryGetValue(ArchiveKey(entry.crc, entry.path), out hit)) newMatches++; }
        sw.Stop();
        Console.WriteLine($"NEW  InstallSelected archive match (build dict + 500 lookups):          {sw.ElapsedMilliseconds} ms, matches={newMatches}");

        // ---------- (A1b/B1b) single LARGE mod: 5,000 entries in one archive ----------
        var bigMod = new FakeMod { name = "big" };
        for (int f = 0; f < 5000; f++) bigMod.archiveFiles.Add(new FakeFile { path = "nativePC\\big\\f" + f + ".tex", crc = (uint)(f * 2654435761u), installedPath = "C:\\Games\\MHW\\nativePC\\big\\f" + f + ".tex", modBelong = bigMod });
        sw.Restart(); int bmOld = 0; foreach (var entry in bigMod.archiveFiles) { if (bigMod.archiveFiles.FirstOrDefault(x => x.crc == entry.crc && x.path == entry.path) != null) bmOld++; } sw.Stop();
        Console.WriteLine($"OLD  InstallSelected archive match, ONE 5,000-file mod (linear): {sw.ElapsedMilliseconds} ms, matches={bmOld}");
        sw.Restart(); var bigIdx = new Dictionary<string, FakeFile>(); foreach (var f in bigMod.archiveFiles) { var k = ArchiveKey(f.crc, f.path); if (!bigIdx.ContainsKey(k)) bigIdx.Add(k, f); }
        int bmNew = 0; foreach (var entry in bigMod.archiveFiles) { FakeFile h; if (bigIdx.TryGetValue(ArchiveKey(entry.crc, entry.path), out h)) bmNew++; } sw.Stop();
        Console.WriteLine($"NEW  InstallSelected archive match, ONE 5,000-file mod (dict):   {sw.ElapsedMilliseconds} ms, matches={bmNew}");

        // ---------- (A2) OLD: FindModsWithSameFile for every file of one mod ----------
        sw.Restart();
        int oldSame = 0;
        foreach (var file in target.archiveFiles) {
            foreach (var other in mods) {
                if (other == target) continue;
                if (other.archiveFiles.Any(fl => fl.installedPath.ToLower() == file.installedPath.ToLower())) oldSame++;
            }
        }
        sw.Stop();
        Console.WriteLine($"OLD  FindModsWithSameFile x500 files (all mods x all files, ToLower each): {sw.ElapsedMilliseconds} ms, hits={oldSame}");

        // ---------- (B2) NEW: indexes built once, then lookups ----------
        sw.Restart();
        var sameIdx = new Dictionary<string, List<FakeMod>>(StringComparer.OrdinalIgnoreCase);
        var crcIdx = new Dictionary<uint, List<FakeMod>>();
        foreach (var mod in mods) {
            var seenP = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var seenC = new HashSet<uint>();
            foreach (var f in mod.archiveFiles) {
                if (seenP.Add(f.installedPath)) { List<FakeMod> l; if (!sameIdx.TryGetValue(f.installedPath, out l)) sameIdx[f.installedPath] = l = new List<FakeMod>(); l.Add(mod); }
                if (seenC.Add(f.crc)) { List<FakeMod> l; if (!crcIdx.TryGetValue(f.crc, out l)) crcIdx[f.crc] = l = new List<FakeMod>(); l.Add(mod); }
            }
        }
        int newSame = 0;
        foreach (var file in target.archiveFiles) {
            List<FakeMod> hits;
            if (sameIdx.TryGetValue(file.installedPath, out hits))
                foreach (var h in hits) { if (h == target) continue; if (h.archiveFiles.Any(fl => string.Equals(fl.installedPath, file.installedPath, StringComparison.OrdinalIgnoreCase))) newSame++; }
        }
        sw.Stop();
        Console.WriteLine($"NEW  BuildModFileIndexes once + FindModsWithSameFile x500 lookups:         {sw.ElapsedMilliseconds} ms, hits={newSame}");

        // ---------- (A3/B3) GetArchiveFiles parent linking: Find vs dictionary ----------
        var flat = new List<FakeFile>();
        for (int d = 0; d < 200; d++) flat.Add(new FakeFile { isDir = true, directory = "nativePC\\d" + d, path = "nativePC\\d" + d });
        for (int f = 0; f < 5000; f++) flat.Add(new FakeFile { isDir = false, path = "nativePC\\d" + (f % 200) + "\\f" + f + ".tex" });
        sw.Restart();
        foreach (var file in flat) {
            string parentDir = Path.GetDirectoryName(file.path.Replace('\\', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '\\');
            if (string.IsNullOrEmpty(parentDir)) continue;
            var parent = flat.Find(x => x.directory == parentDir);
            if (parent != null) parent.children.Add(file);
        }
        sw.Stop(); long oldLink = sw.ElapsedMilliseconds;
        foreach (var f in flat) f.children.Clear();
        sw.Restart();
        var dirLookup = new Dictionary<string, FakeFile>();
        foreach (var f in flat) if (f.isDir && !dirLookup.ContainsKey(f.directory)) dirLookup.Add(f.directory, f);
        foreach (var file in flat) {
            string parentDir = Path.GetDirectoryName(file.path.Replace('\\', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '\\');
            if (string.IsNullOrEmpty(parentDir)) continue;
            FakeFile parent; if (dirLookup.TryGetValue(parentDir, out parent)) parent.children.Add(file);
        }
        sw.Stop();
        Console.WriteLine($"OLD  GetArchiveFiles parent link (5,200 entries, List.Find per file): {oldLink} ms");
        Console.WriteLine($"NEW  GetArchiveFiles parent link (directory dictionary):              {sw.ElapsedMilliseconds} ms");

        // ---------- (C) Real large archive ----------
        string zipPath = "/tmp/bench/large_mod.zip";
        var entries = new List<Tuple<string, uint, long>>();
        using (var za = ZipFile.OpenRead(zipPath)) {
            foreach (var e in za.Entries) entries.Add(Tuple.Create(e.FullName.Replace('/', '\\'), e.Crc32, e.Length));
        }
        Console.WriteLine($"Real archive: {zipPath} ({new FileInfo(zipPath).Length / 1048576f:F1} MiB, {entries.Count} entries)");
        sw.Restart();
        int zipOld = 0;
        var asFiles = entries.Select(t => new FakeFile { path = t.Item1, crc = t.Item2 }).ToList();
        foreach (var entry in entries) { if (asFiles.FirstOrDefault(x => x.crc == entry.Item2 && x.path == entry.Item1) != null) zipOld++; }
        sw.Stop(); long zipOldMs = sw.ElapsedMilliseconds;
        sw.Restart();
        var zipIdx = new Dictionary<string, FakeFile>();
        foreach (var f in asFiles) { var k = ArchiveKey(f.crc, f.path); if (!zipIdx.ContainsKey(k)) zipIdx.Add(k, f); }
        int zipNew = 0;
        foreach (var entry in entries) { FakeFile h; if (zipIdx.TryGetValue(ArchiveKey(entry.Item2, entry.Item1), out h)) zipNew++; }
        sw.Stop();
        Console.WriteLine($"OLD  zip entry matching (5,001 entries, linear): {zipOldMs} ms, matches={zipOld}");
        Console.WriteLine($"NEW  zip entry matching (dictionary):            {sw.ElapsedMilliseconds} ms, matches={zipNew}");

        // ---------- (D) CRC re-hash vs memo ----------
        string bigFile = "/tmp/bench/big_installed.bin";
        if (!File.Exists(bigFile)) { var buf = new byte[1024 * 1024]; new Random(7).NextBytes(buf); using (var fs = File.Create(bigFile)) for (int i = 0; i < 20; i++) fs.Write(buf, 0, buf.Length); }
        Func<string, uint> hash = p => { // simple FNV stand-in cost-equivalent full-file read (CRC32 in the app reads every byte too)
            uint h = 2166136261; var buffer = new byte[1024 * 1024];
            using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan)) { int n; while ((n = fs.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i < n; i++) h = (h ^ buffer[i]) * 16777619; }
            return h; };
        sw.Restart(); for (int i = 0; i < 10; i++) hash(bigFile); sw.Stop(); long hashOld = sw.ElapsedMilliseconds;
        var memo = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        sw.Restart(); for (int i = 0; i < 10; i++) { uint v; if (!memo.TryGetValue(bigFile, out v)) memo[bigFile] = v = hash(bigFile); } sw.Stop();
        Console.WriteLine($"OLD  CRC of same 20 MiB installed file x10 passes (re-hash every time): {hashOld} ms");
        Console.WriteLine($"NEW  CRC x10 with per-pass memo / stamp cache (hash once):              {sw.ElapsedMilliseconds} ms");
        return 0;
    }
}
