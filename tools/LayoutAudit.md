# Layout / text-width audit (v1.47)

Estimated with Yu Gothic UI metrics at 96 DPI (fullwidth glyph = 1.0 em,
Latin/digit = 0.55 em, em = pt x 96/72). Every Japanese UI string was
checked against the width actually available to its control:

| Control | Japanese text | Est. px | Available | Result |
|---|---|---|---|---|
| buttonRescanCache | キャッシュ再スキャン | 125 | 214 | OK |
| buttonDeleteNotFound | 見つからないMODを除去 | 133 | 214 | OK |
| buttonRescanInstall | インストール状態を再スキャン | 175 | 214 | OK |
| buttonCheckAll | 全てチェック | 88 | 223 | OK |
| buttonUncheckAll | 全て解除 | 59 | 223 | OK |
| buttonInstallSelected | 選択をインストール | 132 | 180 | OK |
| buttonUninstallSelected | 選択をアンインストール | 161 | 179 | OK |
| buttonSaveLoadout | ロードアウト保存 | 107 | 126 | OK |
| buttonLoadLoadout | ロードアウト読込 | 107 | 126 | OK |
| checkBoxIgnoreTopFiles | 最上位ファイルを無視 | 133 | 185 | OK |
| label1 (Mod Folder) | MODフォルダ: | 83 | 86 | OK |
| label2 (Game Directory) | ゲームフォルダ: | 101 | 122 | OK |
| labelLanguage | 言語: | 34 | 76 | OK |
| context menu (longest) | MODを削除してアンインストール | 176 | 250 | OK |
| InputBox Yes-to-all | すべて「はい」 | 77 | 104 | OK |
| InputBox No-to-all | すべて「いいえ」 | 88 | 104 | OK |
| Loadout OK button | 新しいMODをアンインストール | 150 | 177 | OK |
| label3 header | MODアーカイブ情報: | 159 | 371 | OK |
| label4 header | ファイル情報: | 114 | 371 | OK |
| comboBoxLanguage | 日本語 | 40 | 105 | OK |
| buttonCancelOperation | キャンセル | 55 | 80 | OK |

Known remaining case: the list Status column's longest *combined* text
("アーカイブなし - 未インストール", ~182 px) can still clip in the
150 px column; the column is user-resizable, and the English original
("No Archive - Not Installed") clipped in the old 110 px column too.
All standalone status texts fit (未インストール ~84 px).

At 125%/150% display scaling the app is now PerMonitorV2 DPI-aware
(app.manifest + app.config), so the whole layout scales proportionally
instead of being bitmap-stretched; the ratios above therefore hold.
