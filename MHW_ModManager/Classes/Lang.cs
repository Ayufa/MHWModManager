using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// ----------------------------------------------------------------------
// UI language support (v1.47)
//
// Two languages: English (default) and Japanese. The table below is keyed
// by the original English string, so any string that has no translation
// yet simply falls back to English -- the app can never show a blank or a
// resource-key by accident.
//
// The choice is persisted with the mechanism the app already ships but
// never used, Properties.Settings (user scope, "UILanguage"). It is NOT
// part of the mods save data (mods.db), whose format is untouched.
// First run (no saved value): Japanese is preselected when the Windows
// UI culture is Japanese, English otherwise. After that the saved choice
// is always honoured until the user changes it in the selector.
// ----------------------------------------------------------------------
public static class Lang {
    public static bool Japanese { get; private set; }

    public static string Code => Japanese ? "ja" : "en";

    static readonly Dictionary<string, string> ja = new Dictionary<string, string> {
        // MainForm labels / buttons
        { "Game Directory:", "ゲームフォルダ:" },
        { "Mod Folder:", "MODフォルダ:" },
        { "Ignore Top-Level Files", "最上位ファイルを無視" },
        { "Save Loadout", "ロードアウト保存" },
        { "Load Loadout", "ロードアウト読込" },
        { "Language:", "言語:" },
        { "Re-Scan Cache", "キャッシュ再スキャン" },
        { "Remove Missing Archives", "見つからないMODを除去" },
        { "Re-Scan Installations", "インストール状態を再スキャン" },
        { "Check All Files", "全てチェック" },
        { "Uncheck All Files", "全て解除" },
        { "Mod Archive Info:", "MODアーカイブ情報:" },
        { "Archive File Info:", "ファイル情報:" },
        { "Install Selected Files", "選択をインストール" },
        { "Uninstall Selected Files", "選択をアンインストール" },
        { "Cancel", "キャンセル" },
        // List columns / empty-list message
        { "Name", "名前" },
        { "Status", "状態" },
        { "Size", "サイズ" },
        { "Date Created", "作成日" },
        { "Category", "カテゴリ" },
        { "Drag mod archives here or place them in folders and hit rescan cache to add them to mod cache.",
          "MODアーカイブをここにドラッグするか、フォルダに入れて「キャッシュ再スキャン」を押すとMODキャッシュに追加されます。" },
        // Context menu
        { "Delete Mod Archive And Uninstall", "MODを削除してアンインストール" },
        { "Delete Mod Archive Without Uninstalling", "アンインストールせずMODを削除" },
        { "Install ALL Files", "全ファイルをインストール" },
        { "Uninstall ALL Files", "全ファイルをアンインストール" },
        { "Open Location", "保存場所を開く" },
        { "Reload Archive", "アーカイブを再読込" },
        // Tooltips
        { "Scan through the mod cache folders for new mods you've added.", "MODキャッシュのフォルダをスキャンして、追加したMODを探します。" },
        { "Scan through the mod install folder and check the files against the mod cache.", "インストール先フォルダをスキャンして、MODキャッシュと照合します。" },
        { "When enabled, files at the root of the archive will be ignored. This is good for ignoring files like Readme's that arent in any folders.",
          "有効にすると、アーカイブ直下のファイルを無視します。フォルダに入っていない Readme などを無視するのに便利です。" },
        { "Game folder (MonsterHunterWorld.exe found)", "ゲームフォルダです（MonsterHunterWorld.exe が見つかりました）" },
        { "Not a game folder (MonsterHunterWorld.exe not found)", "ゲームフォルダではありません（MonsterHunterWorld.exe が見つかりません）" },
        { "Saves the current state of your installed mods to a file\nso you can load it later after changing up your mod list.\nThis is good is good for if you have a certain set of mods that you\nonly want to use sometimes or whatever other purpose you can think of.",
          "現在のMODの状態をファイルに保存します。\nMOD一覧を変えた後に読み込んで戻せます。\nたまにだけ使うMODの組み合わせなどを保存しておくのに便利です。" },
        { "This simply loads the state of your mods from a previously saved loadout file.\r\nIt will give appropriate warnings if new mods have been added or if some archives are missing.",
          "保存済みロードアウトからMODの状態を読み込みます。\r\n新しいMODが追加されていたり、アーカイブが見つからない場合は警告します。" },
        // Update link
        { "New Update Available!", "新しい更新があります！" },
        { "You Have the Latest Version", "最新版です" },
        { "Check For Updates", "更新を確認" },
        // Operations / progress
        { "Scanning mod cache", "キャッシュをスキャン中" },
        { "Re-scanning installations", "インストール状態を再スキャン中" },
        { "Installing", "インストール中" },
        { "Uninstalling", "アンインストール中" },
        { "Reloading archive", "アーカイブを再読込中" },
        { "{0} cancelled.", "{0}をキャンセルしました。" },
        { "{0} failed: {1}", "{0}に失敗しました: {1}" },
        { "{0}: {1} / {2} files  {3}", "{0}: {1} / {2} ファイル  {3}" },
        { "{0}: {1}", "{0}: {1}" },
        // Tree node tooltips
        { "Ignored because it is a top-level file and the \"ignore top-level files\" option is on.",
          "最上位ファイルで、「最上位ファイルを無視」が有効なため無視されています。" },
        { "The file exists in the folder but it is not from this mod. (Checksums don't match)\n   Select this file to see what mod the file actually belongs to.",
          "フォルダにファイルはありますが、このMODのものではありません（チェックサム不一致）。\n   ファイルを選択すると、実際の出所MODが表示されます。" },
        { "This file is installed in the folder and the checksum matches this mod.",
          "フォルダにインストール済みで、チェックサムがこのMODと一致します。" },
        { "This file isn't found at all in the folder.", "フォルダにファイルがありません。" },
        { "File exists from another mod.", "別のMODのファイルが存在します。" },
        { "File does not exist in the mod folder and is ready to be installed.",
          "MODフォルダに無く、インストール可能な状態です。" },
        { "File is ignored because it is a top-level file and the \"ignore top-level files\" option is on.",
          "最上位ファイルで、「最上位ファイルを無視」が有効なため無視されています。" },
        // ModInfo
        { "Installed", "インストール済" },
        { "Conflicting", "競合" },
        { "Partial", "一部のみ" },
        { "Not Installed", "未インストール" },
        { "No Archive - ", "アーカイブなし - " },
        { "Path: ", "パス: " },
        { "Size: ", "サイズ: " },
        { "Mods that change files also included in this archive: \n", "このアーカイブと同ファイルを変えるMOD:\n" },
        // ArchiveFile.SetInfo
        { "Status: ", "状態: " },
        { "Install Location: ", "インストール先: " },
        { "Installed CRC: ", "インストール済CRC: " },
        { "Mods that also change this file: \n", "このファイルも変えるMOD:\n" },
        // ArchiveManager log / dialogs
        { "\nInstall Failed!\n     Mod archive not found!", "\nインストール失敗！\n     MODアーカイブが見つかりません！" },
        { "Install Succesful!", "インストール成功！" },
        { "\nInstall Cancelled.", "\nインストールをキャンセルしました。" },
        { "Install Failed! Please copy and paste this error\n     and send it to the mod author please!",
          "インストール失敗！このエラーをコピーして\n     MOD作者に送ってください！" },
        { "Overwrite file? ({0} of {1})", "ファイルを上書きしますか？ ({0} / {1})" },
        { "The file is from:", "このファイルの出所:" },
        { "It is unknown what mod this file belongs to.", "どのMODのファイルか不明です。" },
        // InputBoxForm
        { "Yes", "はい" },
        { "No", "いいえ" },
        { "Yes to all", "すべて「はい」" },
        { "No to all", "すべて「いいえ」" },
        // LoadoutManager
        { "Mod Loadout File (.mlf)", "MODロードアウト (.mlf)" },
        { "Input Mod Loadout Save Location", "ロードアウトの保存先" },
        { "Couldnt Find: {0}", "見つかりません: {0}" },
        { "Archive Not Loaded: {0}", "アーカイブ未読込: {0}" },
        { "New Files Were Added!", "新しいファイルが追加されています！" },
        { "Uninstall New Mods", "新しいMODをアンインストール" },
        { "\nSince this loadout was created, new mod archives have been added.\nWhat would you like to do about this?",
          "\nこのロードアウト作成後に新しいMODアーカイブが追加されました。\nどうしますか？" },
        { "Leave As Is", "そのまま" },
        { "Update Loadout With Changes", "変更でロードアウトを更新" },
        { "Completed With Warnings", "警告付きで完了" },
        { "The loadout was restored but there were some issues.\n\n", "ロードアウトは復元されましたが問題がありました。\n\n" },
        { "Update Loadout With These Removed", "これらを除いてロードアウトを更新" },
        { "Success", "成功" },
        { "\nMod loadout state succesfully restored.", "\nロードアウトを復元しました。" },
        { "Failure", "失敗" },
        { "An error occured while trying to restore your mod loadout.", "ロードアウトの復元中にエラーが発生しました。" },
        { "Heres the error:\n\n", "エラー内容:\n\n" },
    };

    // Translate an English UI string. Unknown strings stay English.
    public static string T(string en) {
        if (en == null) return null;
        if (!Japanese) return en;
        string s;
        return ja.TryGetValue(en, out s) ? s : en;
    }

    // Translate a format string, then format it.
    public static string F(string enFormat, params object[] args) {
        return string.Format(T(enFormat), args);
    }

    // Pick between an English and a Japanese literal (for strings whose
    // English form is built dynamically, e.g. with plural handling).
    public static string S(string en, string jaText) {
        return Japanese ? jaText : en;
    }

    // Load the persisted choice. Call once at startup (Program.Main).
    public static void Load() {
        string saved = null;
        try { saved = MHW_ModManager.Properties.Settings.Default.UILanguage; } catch { }
        if (saved == "ja") {
            Japanese = true;
        } else if (saved == "en") {
            Japanese = false;
        } else {
            // First run: preselect Japanese only on a Japanese Windows.
            Japanese = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja",
                                     StringComparison.OrdinalIgnoreCase);
            Save();
        }
    }

    public static void SetJapanese(bool value) {
        Japanese = value;
        Save();
    }

    static void Save() {
        try {
            MHW_ModManager.Properties.Settings.Default.UILanguage = Japanese ? "ja" : "en";
            MHW_ModManager.Properties.Settings.Default.Save();
        } catch {
            // Settings storage must never break startup; the choice then
            // simply lasts for this session only.
        }
    }

    // Swap a control tree to a font family that renders English and
    // Japanese with the same metrics (Yu Gothic UI is standard on
    // Windows 10/11; if it is missing, Windows font substitution picks a
    // Japanese-capable fallback). Size and style are preserved.
    public static void ApplyFont(System.Windows.Forms.Control root) {
        foreach (System.Windows.Forms.Control c in root.Controls) {
            try {
                var f = c.Font;
                if (f != null && f.Name != "Yu Gothic UI")
                    c.Font = new System.Drawing.Font("Yu Gothic UI", f.Size, f.Style, f.Unit);
            } catch { }
            ApplyFont(c);
        }
    }
}
