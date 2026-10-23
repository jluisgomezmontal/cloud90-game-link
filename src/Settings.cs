// Cloud90 Hades Link: where things are kept between sessions.
// %LOCALAPPDATA%\Cloud90HadesLink\link.ini (server, Hades folder, token
// sealed with DPAPI for this Windows user) and link.log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cloud90.HadesLink
{
    internal sealed class Settings
    {
        public const string DefaultServer = "https://backcloud90devstreamkit.onrender.com";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Cloud90HadesLink");

        public string Server = DefaultServer;
        public string HadesDir = "";
        public string Token = "";

        public static string DataDir
        {
            get
            {
                // CLOUD90_HADES_LINK_DIR: tests keep their log and settings apart.
                string dir = Environment.GetEnvironmentVariable("CLOUD90_HADES_LINK_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90HadesLink");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string FilePath { get { return Path.Combine(DataDir, "link.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FilePath)) return s;
            foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key == "server" && value.Length > 0) s.Server = value;
                else if (key == "hades_dir") s.HadesDir = value;
                else if (key == "token_protected") s.Token = Unseal(value);
            }
            return s;
        }

        public void Save()
        {
            List<string> lines = new List<string>();
            if (Server != DefaultServer) lines.Add("server=" + Server);
            if (HadesDir.Length > 0) lines.Add("hades_dir=" + HadesDir);
            if (Token.Length > 0) lines.Add("token_protected=" + Seal(Token));
            File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
        }

        static string Seal(string text)
        {
            byte[] sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(sealedBytes);
        }

        static string Unseal(string base64)
        {
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(base64), Entropy, DataProtectionScope.CurrentUser);
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
