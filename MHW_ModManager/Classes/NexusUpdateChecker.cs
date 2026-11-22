using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;


public class NexusUpdateChecker {

    public static async void CheckForNewVersion() {

        string url = @"https://raw.githubusercontent.com/UncleClapton/MHWModManager/master/VERSION";

        //api access not released yet
        //string url = @"https://api.nexusmods.com/v1/games/monsterhunterworld/mods/372";
        string content = string.Empty;

        try {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            request.ContentType = "application/json";
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                content = reader.ReadToEnd();
            }

            float versionNum = float.Parse(content);

            if(versionNum <= MainForm.instance.modsData.version) {
                MainForm.instance.linkNewUpdate.Text = Lang.T("You Have the Latest Version");
                MainForm.instance.linkNewUpdate.LinkColor = System.Drawing.Color.LawnGreen;
            } else {
                MainForm.instance.linkNewUpdate.Text = Lang.S($"New Update V{versionNum} Available!", $"新しい更新 V{versionNum} があります！");
            }
            MainForm.instance.linkNewUpdate.Visible = true;

            Console.WriteLine("OnlineVersion: " + versionNum);
            Console.WriteLine("LocalVersion: " + MainForm.programVersion);
        } catch (Exception ex){
            Console.WriteLine("UpdateCheckFailed\n" + ex.Message);
            MainForm.instance.linkNewUpdate.Text = Lang.T("Check For Updates");
            MainForm.instance.linkNewUpdate.LinkColor = System.Drawing.Color.PaleTurquoise;
            MainForm.instance.linkNewUpdate.Visible = true;
        }
    }

}

