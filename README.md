# MHWModManager (Optimized)

A simple to use (hopefully) robust generic mod management tool for managing the files installed by mod archives.

**This repository is an optimized copy of [UncleClapton/MHWModManager](https://github.com/UncleClapton/MHWModManager)** (originally created by BoltMan / BoltTheMan, Nexus Mods mod 372), focused on one problem: **installing large mods — many files and/or large total size — used to take very long or make the window appear to freeze ("Not Responding")**. The original code is MIT licensed (see [LICENSE](LICENSE)); all credit for the original program goes to its authors.

## What's new in v1.48

- **Bug fix — any existing folder was accepted as the game folder**: the Game Directory check only tested that the folder exists, so an unrelated folder (Desktop, Downloads, ...) got the green ✓ and was adopted as the install target. A folder now only counts as the game folder when it **directly contains `MonsterHunterWorld.exe`** (case-insensitive, no subfolder search). The ✓/✗ mark now has a tooltip explaining the result ("Game folder (MonsterHunterWorld.exe found)" / "Not a game folder (MonsterHunterWorld.exe not found)"). An invalid path is never adopted: the previously saved game folder stays in effect, exactly as before. Save-data format is unchanged.
- **修正 — 存在するフォルダなら何でもゲームフォルダとして認識されていた問題**：ゲームフォルダの判定が「フォルダが存在するか」だけだったため、無関係なフォルダ（デスクトップやダウンロード等）でも緑の✓が付いてインストール先に採用されてしまいました。今後はフォルダの**直下に `MonsterHunterWorld.exe` がある場合のみ**ゲームフォルダとして認識します（大文字小文字は区別なし、サブフォルダは対象外）。✓/✗に理由のツールチップも追加。無効なパスは今まで通り採用されず、前回のゲームフォルダが維持されます。セーブデータ形式は変更なし。

## What's new in v1.47

- **Japanese UI (日本語)**: a **Language selector** in the "Mod Folder" row at the top of the window (right side, next to the Loadout buttons) switches the whole UI between **English** and **日本語** — instantly, no restart needed. The choice is saved in the app's user settings (`UILanguage`) and restored on the next launch. On first run, Japanese is preselected on a Japanese Windows; English otherwise. Mod/file names and paths are never translated. Save-data format (`mods.db`) is unchanged.
- **Text overlap fixes (layout structure unchanged)**: the app is now PerMonitorV2 DPI-aware (app manifest + config), so at 125%/150% display scaling the layout scales instead of being bitmap-stretched. Concrete fixes: the "Game Directory:" label overlapped the ✓/✗ status box by 2 px (moved apart); the game-directory text box was 2296 px wide inside a 1470 px panel and got clipped (now anchored to the panel's right edge); panel header labels were too short for their fonts (21→24 px, 24→27 px); the progress label now ellipsizes instead of clipping; the dialog buttons in the popup form were widened for longer texts; a minimum window size (1000×650) prevents panels from being crushed into each other. All controls are rendered in Yu Gothic UI, which draws English and Japanese with the same metrics (the log boxes previously used Century Gothic, which has no Japanese glyphs).
- **Bug fix — file hierarchy (tree) not showing for a selected mod**: `RefreshTreeView` could leave the tree cleared and update-locked (`BeginUpdate()` without a matching `EndUpdate()` on early-return/exception paths), and archive parent-folder linking relied on one-level, case-sensitive directory matching. Folder chains are now synthesized segment-by-segment for every nesting level (archives without explicit directory entries included), and the tree refresh is exception-guarded so one bad entry can no longer blank the whole tree.

The window layout, the way the app is used, its behaviour and the save-data format are otherwise **unchanged**.

### 日本語（v1.47の変更点）

- **日本語UI対応**：画面上部「MODフォルダ」の行の右側（ロードアウトボタンの隣）にある**言語セレクタ**で、UI全体を **English / 日本語** に切り替えできます。再起動は不要で、選択はユーザー設定に保存され次回起動時も維持されます。初回起動時のみ、日本語版Windowsでは日本語が preselect されます。MOD名・ファイル名・パスは翻訳されません。セーブデータ形式は変更なし。
- **文字の重なり修正（レイアウト構成は不変）**：PerMonitorV2 DPI対応になり、125%/150%拡大でもレイアウトがそのまま拡大されるようになりました。「ゲームフォルダ」ラベルと✓/✗表示の2pxの重なり、パネル幅を超えて切れていた入力欄、フォントに対して低すぎた見出しラベル、はみ出す進捗ラベル、狭すぎたダイアログボタンなどを修正。最小ウィンドウサイズも追加しました。フォントは英字と日本語を同じ寸法で描ける Yu Gothic UI に統一。
- **不具合修正（選択したMODの階層が表示されない）**：ツリー更新が途中で止まるとツリーが空のまま固まる問題と、アーカイブのフォルダ紐付けが1階層・大文字小文字依存だった問題を修正。全階層のフォルダを自動合成し、ツリー表示が失敗しても空にならないようにしました。

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
This optimized copy publishes Windows builds (built on GitHub Actions, windows-latest) as GitHub Releases: download `MHWModManager_v<version>.zip`, extract it, and run `MHW_ModManager.exe` from the extracted folder (the exe needs the bundled `ModManagerData\7z.dll` beside it).

## Development Setup

1. Clone repository
2. Ensure [Visual Studio IDE and .NET Framework 4.6.1][msft-vsdl] is installed (the project targets .NET Framework 4.8)
2. Open solution in Visual Studio

(it's that simple)

## Upstream

This is a standalone copy, not a GitHub fork. The original repository is kept as the `upstream` remote for reference only; no pull requests or issues are sent upstream from this copy.

[mmnexus-releases]: https://www.nexusmods.com/monsterhunterworld/mods/372?tab=files
[msft-vsdl]: https://visualstudio.microsoft.com/
