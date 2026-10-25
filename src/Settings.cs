// Cloud90 Game Link: what's kept between sessions.
// %LOCALAPPDATA%\Cloud90GameLink\link.ini (server, each game's folder, each
// game's token sealed with DPAPI for this Windows user) and link.log. The
// first time, Cloud90 Hades Link's settings are brought over.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cloud90.GameLink
{
    internal sealed class Settings
    {
        public const string DefaultServer = "https://backcloud90devstreamkit.onrender.com";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Cloud90GameLink");
        static readonly byte[] HadesLinkEntropy = Encoding.UTF8.GetBytes("Cloud90HadesLink");

        public string Server = DefaultServer;
        public string HadesDir = "";
        public string MameDir = "";
        public string CtrDir = "";
        // game id ("hades", "kof2002") -> its token
        public readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();

        public string TokenFor(string game)
        {
            string t;
            return Tokens.TryGetValue(game, out t) ? t : "";
        }

        // CLOUD90_GAME_LINK_DIR: tests keep their log and settings apart.
        public static string DataDir
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("CLOUD90_GAME_LINK_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90GameLink");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string FilePath { get { return Path.Combine(DataDir, "link.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FilePath))
            {
                s.ImportHadesLink();
                return s;
            }
            foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key == "server" && value.Length > 0) s.Server = value;
                else if (key == "hades_dir") s.HadesDir = value;
                else if (key == "mame_dir") s.MameDir = value;
                else if (key == "ctr_dir") s.CtrDir = value;
                else if (key.StartsWith("token_protected."))
                {
                    string token = Unseal(value, Entropy);
                    if (token.Length > 0) s.Tokens[key.Substring("token_protected.".Length)] = token;
                }
            }
            return s;
        }

        // Cloud90 Hades Link 1.x kept its Hades folder and token here.
        void ImportHadesLink()
        {
            if (Environment.GetEnvironmentVariable("CLOUD90_GAME_LINK_DIR") != null) return;
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90HadesLink", "link.ini");
            if (!File.Exists(old)) return;
            foreach (string raw in File.ReadAllLines(old, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("hades_dir=")) HadesDir = line.Substring(10);
                else if (line.StartsWith("server=")) Server = line.Substring(7);
                else if (line.StartsWith("token_protected="))
                {
                    string token = Unseal(line.Substring(16), HadesLinkEntropy);
                    if (token.Length > 0) Tokens["hades"] = token;
                }
            }
            Save();
        }

        public void Save()
        {
            List<string> lines = new List<string>();
            if (Server != DefaultServer) lines.Add("server=" + Server);
            if (HadesDir.Length > 0) lines.Add("hades_dir=" + HadesDir);
            if (MameDir.Length > 0) lines.Add("mame_dir=" + MameDir);
            if (CtrDir.Length > 0) lines.Add("ctr_dir=" + CtrDir);
            foreach (KeyValuePair<string, string> t in Tokens)
                if (t.Value.Length > 0) lines.Add("token_protected." + t.Key + "=" + Seal(t.Value));
            File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
        }

        static string Seal(string text)
        {
            byte[] sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(sealedBytes);
        }

        static string Unseal(string base64, byte[] entropy)
        {
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(base64), entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                return "";  // another user's or a broken value: ask for the token again
            }
        }
    }

    internal static class Log
    {
        static readonly object Gate = new object();
        public static event Action<string> Line;

        public static void Write(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + " " + text;
            lock (Gate)
            {
                try { File.AppendAllText(Path.Combine(Settings.DataDir, "link.log"), line + Environment.NewLine, Encoding.UTF8); }
                catch (IOException) { }
            }
            Action<string> handler = Line;
            if (handler != null) handler(line);
        }

        public static void Reset()
        {
            try { File.WriteAllText(Path.Combine(Settings.DataDir, "link.log"), "", Encoding.UTF8); }
            catch (IOException) { }
        }
    }
}
