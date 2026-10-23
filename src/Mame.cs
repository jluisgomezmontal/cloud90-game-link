// Cloud90 Game Link, MAME (PLAN-KOF2002.md): finding MAME and the player's
// own ROMs, installing the cloud90 plugin, and running the game with it.
// The plugin reads <work>\inbox.lua and answers in <work>\acks.txt, which
// is read here as it grows.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Cloud90.GameLink
{
    internal static class MameFolder
    {
        public static bool IsMame(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "mame.exe"));
        }

        public static string Find()
        {
            foreach (string dir in new string[] { @"C:\MAME", @"C:\mame", @"D:\MAME", @"C:\Program Files\MAME", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MAME") })
                if (IsMame(dir)) return dir;
            return "";
        }

        // What's missing to run a set, or "" (the zips by name: MAME checks
        // their contents itself, and says so in its own error).
        public static string Missing(string dir, string set, string bios)
        {
            string roms = Path.Combine(dir, "roms");
            if (!File.Exists(Path.Combine(roms, set + ".zip"))) return "falta tu ROM " + set + ".zip en " + roms;
            if (bios.Length > 0 && !File.Exists(Path.Combine(roms, bios + ".zip"))) return "falta el BIOS " + bios + ".zip (para MAME) en " + roms;
            return "";
        }
    }

    internal static class MamePlugin
    {
        static string Embedded(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        // The plugin's two files (tests give their own).
        internal static Func<string, string> FileText = Embedded;

        public static void Install(string mame)
        {
            string dir = Path.Combine(mame, "plugins", "cloud90");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "init.lua"), FileText("cloud90-mame-init.lua"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "plugin.json"), FileText("cloud90-mame-plugin.json"), new UTF8Encoding(false));
            Log.Write("plugin de MAME instalado en " + dir);
        }

        public static void Uninstall(string mame)
        {
            string dir = Path.Combine(mame, "plugins", "cloud90");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Log.Write("plugin de MAME desinstalado");
        }
    }

    internal sealed class MameProcess
    {
        // One folder per set, so each game's tab has its own inbox.
        public static string WorkDir(string set)
        {
            string dir = Path.Combine(Settings.DataDir, "mame", set);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string InboxPath(string set) { return Path.Combine(WorkDir(set), "inbox.lua"); }
        static string AcksPath(string set) { return Path.Combine(WorkDir(set), "acks.txt"); }

        string set = "";
        Process process;
        Timer tail;
        long readTo;
        readonly StringBuilder partial = new StringBuilder();
        public event Action<string> GameLine;
        public event Action Exited;

        public bool Running { get { return process != null && !process.HasExited; } }

        public static bool AnotherRunning() { return Process.GetProcessesByName("mame").Length > 0; }

        // More arguments for MAME (tests: no window, a script that plays).
        internal static string ExtraArgs = "";

        public void Start(string mame, string set)
        {
            this.set = set;
            File.WriteAllText(AcksPath(set), "");
            readTo = 0;
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(mame, "mame.exe"), set + " -plugin cloud90 -skip_gameinfo " + ExtraArgs);
            info.WorkingDirectory = mame;
            info.UseShellExecute = false;
            info.EnvironmentVariables["CLOUD90_DIR"] = WorkDir(set).Replace('\\', '/');
            process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.Exited += delegate
            {
                if (tail != null) tail.Dispose();
                ReadAcks();
                Action handler = Exited;
                if (handler != null) handler();
            };
            process.Start();
            tail = new Timer(delegate { ReadAcks(); }, null, 100, 100);
            Log.Write("MAME abierto: " + set);
        }

        readonly object gate = new object();

        void ReadAcks()
        {
            lock (gate)
            {
                try
                {
                    using (FileStream f = new FileStream(AcksPath(set), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (f.Length <= readTo) return;
                        f.Seek(readTo, SeekOrigin.Begin);
                        byte[] bytes = new byte[f.Length - readTo];
                        int n = f.Read(bytes, 0, bytes.Length);
                        readTo += n;
                        partial.Append(Encoding.UTF8.GetString(bytes, 0, n));
                    }
                }
                catch (IOException) { return; }
                string text = partial.ToString();
                int end = text.LastIndexOf('\n');
                if (end < 0) return;
                partial.Remove(0, end + 1);
                foreach (string raw in text.Substring(0, end).Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    Action<string> handler = GameLine;
                    if (handler != null) handler(line);
                }
            }
        }
    }
}
