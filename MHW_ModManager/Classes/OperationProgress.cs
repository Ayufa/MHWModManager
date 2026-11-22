using System;

// Progress snapshot for a long-running archive operation (scan / install /
// uninstall / CRC re-check). Reported from a background thread through
// IProgress<OperationProgress>; MainForm marshals it to the UI thread.
// Reporting is throttled by the producers (ArchiveManager / ModCache) to at
// most one report per ~100 ms (plus a final report), never per byte.
public class OperationProgress {
    public string Operation;
    public string CurrentFile;
    public int FilesDone;
    public int FilesTotal;
    public long BytesDone;
    public long BytesTotal;

    public int Percent {
        get {
            if (FilesTotal > 0)
                return (int)Math.Min(100, FilesDone * 100L / FilesTotal);
            if (BytesTotal > 0)
                return (int)Math.Min(100, BytesDone * 100L / BytesTotal);
            return 0;
        }
    }
}

// Result of the "file already exists with a different CRC" prompt.
// Mirrors the original CheckWarnDialogue DialogResult mapping:
//   Yes -> Overwrite, No -> Skip,
//   OK  -> Overwrite + don't ask again, Ignore -> Skip + don't ask again.
public enum OverwriteDecision {
    Overwrite,
    Skip,
    OverwriteSkipWarnings,
    SkipSkipWarnings
}
