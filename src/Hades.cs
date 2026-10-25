// Cloud90 Game Link, Hades: finding the game, installing the mod, the inbox
// the game side reads (shared with MAME), and running Hades to read its
// console.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Cloud90.GameLink
{
    internal static class HadesFolder
    {
        public const string SteamAppId = "1145360";

        public static bool IsHades(string dir)
        {
            return !string.IsNullOrEmpty(dir) &&
                   File.Exists(Path.Combine(dir, "Content", "Scripts", "RoomManager.lua")) &&
                   File.Exists(Path.Combine(dir, "x64", "Hades.exe"));
        }

        // Every Steam library (steamapps\libraryfolders.vdf), its Hades.
        public static string Find()
        {
            List<string> libraries = new List<string>();
            foreach (string key in new string[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                object path = Registry.GetValue(key, key.StartsWith("HKEY_CURRENT_USER") ? "SteamPath" : "InstallPath", null);
                if (path is string && ((string)path).Length > 0) libraries.Add(((string)path).Replace('/', '\\'));
            }
            foreach (string steam in libraries.ToArray())
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                if (drive.DriveType == DriveType.Fixed) libraries.Add(Path.Combine(drive.Name, "SteamLibrary"));
            foreach (string library in libraries)
            {
                string dir = Path.Combine(library, "steamapps", "common", "Hades");
                if (IsHades(dir)) return dir;
            }
            return "";
        }
    }

    // Puts Cloud90.lua next to the game's scripts and an Import of it at the
    // end of RoomManager.lua (as the community's mod importer does), with a
    // backup; Uninstall leaves the game as it was. A game update replaces
    // RoomManager.lua, so it's checked on every start.
    internal static class ModInstaller
    {
        const string ModFile = "Cloud90.lua";
        const string ImportLine = "Import \"Cloud90.lua\" -- Cloud90 Hades Link";
        const string Target = "RoomManager.lua";
        const string Backup = "RoomManager.lua.cloud90-backup";

        static string Scripts(string hades) { return Path.Combine(hades, "Content", "Scripts"); }

        public static string Inbox(string hades) { return Path.Combine(Scripts(hades), "Cloud90Inbox.lua"); }

        static string EmbeddedMod()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModFile))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        // The mod's text (tests give their own).
        internal static Func<string> ModText = EmbeddedMod;

        public static void Install(string hades)
        {
            string scripts = Scripts(hades);
            File.WriteAllText(Path.Combine(scripts, ModFile), ModText(), new UTF8Encoding(false));
            string target = Path.Combine(scripts, Target);
            string text = File.ReadAllText(target, Encoding.UTF8);
            if (text.IndexOf(ImportLine, StringComparison.Ordinal) < 0)
            {
                File.Copy(target, Path.Combine(scripts, Backup), true);
                File.WriteAllText(target, text.TrimEnd() + "\r\n\r\n" + ImportLine + "\r\n", new UTF8Encoding(true));
            }
            // Without it the game restarts itself through Steam, and its
            // console would no longer reach the Link.
            string appId = Path.Combine(hades, "x64", "steam_appid.txt");
            if (!File.Exists(appId)) File.WriteAllText(appId, HadesFolder.SteamAppId);
            Log.Write("mod instalado en " + scripts);
        }

        public static void Uninstall(string hades)
        {
            string scripts = Scripts(hades);
            string target = Path.Combine(scripts, Target);
            if (File.Exists(target))
            {
                string text = File.ReadAllText(target, Encoding.UTF8);
                int at = text.IndexOf(ImportLine, StringComparison.Ordinal);
                if (at >= 0) File.WriteAllText(target, text.Substring(0, at).TrimEnd() + "\r\n", new UTF8Encoding(true));
            }
            foreach (string name in new string[] { ModFile, "Cloud90Inbox.lua", Backup })
            {
                string path = Path.Combine(scripts, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Log.Write("mod desinstalado");
        }
    }

    // What the mod reads: the control flags and the effects not answered yet,
    // as a Lua table. Written whole, through a temporary file, so the game
    // never reads half of it. Viewer names are escaped byte by byte.
    internal sealed class Inbox : IDisposable
    {
        public sealed class Item
        {
            public string Id;
            public string Effect;
            public int Ms;
            public string By;
            public bool Safe;
            // The streamer's pick for the effect (an enemy's name), or "".
            public string Arg = "";
            public DateTime Sent;
        }

        readonly string path;
        readonly object gate = new object();
        readonly List<Item> items = new List<Item>();
        bool paused;
        readonly string session;
        readonly System.Threading.Timer heartbeat;
        bool connected;
        bool disposed;
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public Inbox(string path) { this.path = path; Write(); }
        public Inbox(string path, string session)
        {
            this.path = path;
            this.session = session;
            Write();
            heartbeat = new System.Threading.Timer(delegate { lock (gate) { if (!disposed) Write(); } }, null, 500, 500);
        }
        public void Connected(bool value)
        {
            if (session == null) return;
            lock (gate) { connected = value; if (!value) items.Clear(); Write(); }
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; connected = false; items.Clear(); Write(); }
            if (heartbeat != null) heartbeat.Dispose();
        }

        public bool Add(Item item)
        {
            lock (gate) {
                if (disposed) return false;
                if (session != null && (items.Count >= 20 || !Regex.IsMatch(item.Id ?? "", "^[A-Za-z0-9_-]{1,80}$") || items.Exists(delegate(Item i) { return i.Id == item.Id; }))) return false;
                items.Add(item); Write(); return true;
            }
        }

        public Item Take(string id)
        {
            lock (gate)
            {
                Item found = items.Find(delegate (Item i) { return i.Id == id; });
                if (found != null) { items.Remove(found); Write(); }
                return found;
            }
        }

        // Effects the game didn't answer in time (it's closed or stuck).
        public List<Item> Expire(TimeSpan after)
        {
            lock (gate)
            {
                DateTime limit = DateTime.UtcNow - after;
                List<Item> old = items.FindAll(delegate (Item i) { return i.Sent < limit; });
                if (old.Count > 0) { items.RemoveAll(delegate (Item i) { return i.Sent < limit; }); Write(); }
                return old;
            }
        }

        public void SetPaused(bool value) { lock (gate) { paused = value; if (value) items.Clear(); Write(); } }

        public static string LuaString(string s)
        {
            StringBuilder b = new StringBuilder("\"");
            foreach (byte c in Encoding.UTF8.GetBytes(s ?? ""))
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-' || c == '.')
                    b.Append((char)c);
                else
                    b.Append('\\').Append(((int)c).ToString("000"));
            }
            return b.Append('"').ToString();
        }

        void Write()
        {
            if (session != null) { WriteJson(); return; }
            StringBuilder b = new StringBuilder("return {\n  control = { paused = ");
            b.Append(paused ? "true" : "false").Append(" },\n  effects = {\n");
            foreach (Item i in items)
            {
                b.Append("    { id = ").Append(LuaString(i.Id))
                 .Append(", effect = ").Append(LuaString(i.Effect))
                 .Append(", ms = ").Append(i.Ms)
                 .Append(", by = ").Append(LuaString(i.By))
                 .Append(", safe = ").Append(i.Safe ? "true" : "false")
                 .Append(", arg = ").Append(LuaString(i.Arg)).Append(" },\n");
            }
            b.Append("  },\n}\n");
            try
            {
                string temp = path + ".tmp";
                File.WriteAllText(temp, b.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException e)
            {
                Log.Write("no se pudo escribir el buzón: " + e.Message);
            }
        }

        void WriteJson()
        {
            List<object> effects = new List<object>();
            ulong now = GetTickCount64();
            foreach (Item i in items)
            {
                double remaining = 10000 - (DateTime.UtcNow - i.Sent).TotalMilliseconds;
                if (remaining <= 0) continue;
                effects.Add(new { id = i.Id, effect = i.Effect, ms = i.Ms, arg = i.Arg, expiresMs = now + (ulong)remaining });
            }
            string text = new JavaScriptSerializer().Serialize(new { session = session, heartbeatMs = now, paused = paused || !connected, effects = effects });
            try {
                string temp = path + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } catch (IOException e) { Log.Write("CTR: no se pudo escribir el buzón: " + e.Message); }
        }
    }

    // Hades.exe run by the Link, its console read line by line for the
    // mod's "CLOUD90 …" lines.
    internal sealed class HadesProcess
    {
        Process process;
        public event Action<string> ModLine;
        public event Action Exited;

        public bool Running { get { return process != null && !process.HasExited; } }

        public static bool AnotherRunning()
        {
            return Process.GetProcessesByName("Hades").Length > 0;
        }

        public void Start(string hades, bool vulkan)
        {
            string dir = Path.Combine(hades, vulkan ? "x64Vk" : "x64");
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(dir, "Hades.exe"));
            info.WorkingDirectory = dir;
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;
            process.Exited += delegate { Action handler = Exited; if (handler != null) handler(); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Log.Write("Hades abierto (" + (vulkan ? "Vulkan" : "DirectX") + ")");
        }

        void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            int at = e.Data.IndexOf("CLOUD90 ", StringComparison.Ordinal);
            if (at < 0) return;
            Action<string> handler = ModLine;
            if (handler != null) handler(e.Data.Substring(at + 8).Trim());
        }
    }
}
