using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Stamp-validated CRC32 cache for installed files.
//
// The original code re-hashed every installed file from disk on every
// CheckIfInstalled() call (which ran after every install, for every
// refreshed mod), making large mods slower the more often they were
// touched. A cached CRC is only reused while the file's size and
// last-write time are unchanged, so a file that was actually replaced
// is always re-hashed. Correctness (installedNotMatching) is unchanged.
//
// The cache is process-local and static on purpose: it is not part of
// the serialized save data, so save-format compatibility is untouched.
public static class CrcCache {
    class Entry {
        public long Size;
        public long MtimeTicks;
        public uint Crc;
    }

    static readonly Dictionary<string, Entry> cache =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new object();

    // CRC of the file at `path`, computing it (buffered, sequential scan)
    // only when there is no stamp-matching cache entry.
    public static uint GetCrc(string path) {
        var info = new FileInfo(path);
        long size = info.Length;
        long mtime = info.LastWriteTimeUtc.Ticks;

        lock (gate) {
            Entry hit;
            if (cache.TryGetValue(path, out hit) && hit.Size == size && hit.MtimeTicks == mtime)
                return hit.Crc;
        }

        uint crc = ComputeCrc(path);

        lock (gate) {
            cache[path] = new Entry { Size = size, MtimeTicks = mtime, Crc = crc };
        }
        return crc;
    }

    // Same lookup, but the caller supplies a per-operation memo dictionary
    // so that within a single install/scan pass the same installed path is
    // never hashed (or even stat'ed) twice.
    public static uint GetCrc(string path, IDictionary<string, uint> memo) {
        uint memoHit;
        if (memo != null && memo.TryGetValue(path, out memoHit))
            return memoHit;

        uint crc = GetCrc(path);
        if (memo != null)
            memo[path] = crc;
        return crc;
    }

    public static void Invalidate(string path) {
        lock (gate) cache.Remove(path);
    }

    static uint ComputeCrc(string path) {
        var crc32 = new Crc32();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                       1024 * 1024, FileOptions.SequentialScan)) {
            return BitConverter.ToUInt32(crc32.ComputeHash(fs).Reverse().ToArray(), 0);
        }
    }
}
