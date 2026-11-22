# MHWModManager (Optimized)

A simple to use (hopefully) robust generic mod management tool for managing the files installed by mod archives.

**This repository is an optimized copy of [UncleClapton/MHWModManager](https://github.com/UncleClapton/MHWModManager)** (originally created by BoltMan / BoltTheMan, Nexus Mods mod 372), focused on one problem: **installing large mods — many files and/or large total size — used to take very long or make the window appear to freeze ("Not Responding")**. The original code is MIT licensed (see [LICENSE](LICENSE)); all credit for the original program goes to its authors.

## What was optimized (v1.46)

- **No more UI-thread freezes**: archive scanning, installing, uninstalling and CRC re-checks now run on a background thread (`Task.Run` + UI marshalling). The window stays responsive during large installs.
- **Progress + cancellation**: a progress bar with operation name, files-done/total count and current file, throttled to ~100 ms updates, plus a Cancel button for install/scan/uninstall operations.
- **O(n²) lookups eliminated**:
  - Archive entries are matched through a dictionary keyed by (CRC, path) built once per install, instead of a linear `FirstOrDefault` scan per file.
  - "Which other mods contain this same file / CRC" is answered from `installedPath -> mods` and `CRC -> mods` indexes built once per install, instead of rescanning *all mods × all their files for every file*.
  - Archive parent-directory linking uses a directory dictionary instead of `List.Find` per file.
  - Directory sizes are computed in a single bottom-up pass (deepest first) instead of two redundant full loops.
- **CRC checks no longer re-hash everything**: installed-file CRC32s go through a stamp-validated cache (size + last-write time) plus a per-operation memo, so a path shared by several mods is hashed at most once per pass and unchanged files are not re-hashed at all. `installedNotMatching` semantics are unchanged.
- **Cheaper path handling**: `ArchiveFile.GetDirectPath()` / `installedPath` are cached and no longer allocate several `ToLower()` copies of the path on every access.
- **Buffered I/O**: extraction writes and CRC reads use 1 MiB buffered `FileStream`s with `FileOptions.SequentialScan` (SevenZip extraction stays a single sequential pass in archive order, which is the order its extractor requires).

All existing features, file formats and save-data compatibility are unchanged.

## 日本語まとめ

大型MOD（ファイル数が多い／容量が大きいMOD）を入れるとインストールが極端に遅い・ウィンドウが固まる問題を直した最適化版です。処理をバックグラウンド化して進捗バーとキャンセルを追加し、ファイル照合を辞書引き（O(1)）に変え、CRC再計算をキャッシュ化しました。元の機能・セーブデータ互換はそのままです。原作は [UncleClapton/MHWModManager](https://github.com/UncleClapton/MHWModManager)（作者 BoltMan）で、ライセンスは MIT です。

## Binaries and Releases

Upstream releases can be found [on the nexus mods website.][mmnexus-releases]
This optimized copy is distributed as source; build it yourself with the setup below (no binary release is published unless a verified Windows build is attached to a GitHub Release).

## Development Setup

1. Clone repository
2. Ensure [Visual Studio IDE and .NET Framework 4.6.1][msft-vsdl] is installed (the project targets .NET Framework 4.8)
2. Open solution in Visual Studio

(it's that simple)

## Upstream

This is a standalone copy, not a GitHub fork. The original repository is kept as the `upstream` remote for reference only; no pull requests or issues are sent upstream from this copy.

[mmnexus-releases]: https://www.nexusmods.com/monsterhunterworld/mods/372?tab=files
[msft-vsdl]: https://visualstudio.microsoft.com/
