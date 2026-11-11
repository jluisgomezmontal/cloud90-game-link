using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Cloud90.GameLink
{
    internal abstract class SteamModHost : GameHost
    {
        protected abstract string Plugin { get; }
        protected abstract string SteamAppId { get; }
        protected abstract string ProcessName { get; }
        protected abstract string BridgeVersion { get; }
        protected abstract void SetFolder(Settings settings, string path);
        protected virtual string PluginFolder(string root) { return root; }
        protected virtual bool ReportsPause { get { return false; } }
        string OwnedHash { get { return "cloud90-" + Id + "-owned.txt"; } }
        string DataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90GameLink", Id); } }
        protected string Executable(string path) { return Path.Combine(path, ProcessName + ".exe"); }
        protected bool IsGame(string path) { return !string.IsNullOrEmpty(path) && File.Exists(Executable(path)); }
        protected static bool HasPackage(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceInfo(name) != null; }
        static readonly Regex SessionId = new Regex("^[a-f0-9]{32}$");
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        Timer timer;
        Inbox inbox;
        string session, directory, reported;
        int pid;
        bool? playing;
        bool localPaused;
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public override bool UseFolder(Settings settings, string dir)
        {
            if (Running || !IsGame(dir)) return false;
            SetFolder(settings, dir);
            return true;
        }
        public override void FindFolder(Settings settings)
        {
            if (IsGame(Folder(settings))) return;
            var libraries = new List<string>();
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                var path = Registry.GetValue(key, key.StartsWith("HKEY_CURRENT_USER") ? "SteamPath" : "InstallPath", "") as string;
                if (string.IsNullOrEmpty(path)) continue;
                libraries.Add(path);
                string list = Path.Combine(path, "steamapps", "libraryfolders.vdf");
                if (File.Exists(list))
                    foreach (Match match in Regex.Matches(File.ReadAllText(list), "\"path\"\\s+\"([^\"]+)\""))
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (string library in libraries)
            {
                string steamapps = Path.Combine(library, "steamapps");
                string manifest = Path.Combine(steamapps, "appmanifest_" + SteamAppId + ".acf");
                if (!File.Exists(manifest)) continue;
                var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (!match.Success || match.Groups[1].Value != Path.GetFileName(match.Groups[1].Value)) continue;
                string path = Path.Combine(steamapps, "common", match.Groups[1].Value);
                if (IsGame(path)) { SetFolder(settings, path); return; }
            }
        }
        public override bool Running { get { return timer != null; } }
        public override bool AnotherRunning()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var process in processes) process.Dispose();
            return processes.Length != 0;
        }

        void Install(string root)
        {
            root = PluginFolder(root);
            string path = CupheadInstaller.SafePath(root, Plugin);
            string manifest = CupheadInstaller.SafePath(root, OwnedHash);
            byte[] payload;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(Plugin))
            using (var copy = new MemoryStream()) { resource.CopyTo(copy); payload = copy.ToArray(); }
            string hash = CupheadInstaller.Hash(payload);
            if (File.Exists(path))
            {
                string oldHash = CupheadInstaller.HashFile(path);
                if (oldHash == hash) return;
                if (!File.Exists(manifest) || File.ReadAllText(manifest) != oldHash)
                    throw new IOException("Retira la versión anterior de " + Plugin + " antes de instalar esta vista previa.");
                string replacement = CupheadInstaller.SafePath(root, Plugin + ".tmp");
                using (var stream = new FileStream(replacement, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(payload, 0, payload.Length);
                try { File.Replace(replacement, path, null); }
                finally { if (File.Exists(replacement)) File.Delete(replacement); }
            }
            else
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(payload, 0, payload.Length);
            }
            File.WriteAllText(manifest, hash);
        }

        public override Inbox Prepare(Settings settings)
        {
            if (Running) return inbox;
            if (AnotherRunning()) throw new InvalidOperationException("Cierra " + Title + " primero.");
            string problem = Problem(settings);
            if (problem.Length != 0) throw new InvalidOperationException(problem);
            Install(Folder(settings));
            if (inbox != null) inbox.Dispose();
            session = Guid.NewGuid().ToString("N");
            directory = Path.Combine(DataRoot, session);
            Directory.CreateDirectory(directory);
            inbox = new Inbox(Path.Combine(directory, "inbox.json"), session);
            return inbox;
        }

        public override void Start(Settings settings, Link link, Action exited)
        {
            if (AnotherRunning()) throw new InvalidOperationException("Cierra " + Title + " primero.");
            string pointer = Path.Combine(DataRoot, "active-session.txt");
            File.WriteAllText(pointer, session, new UTF8Encoding(false));
            try { using (Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true })) {} }
            catch { ClearPointer(pointer); throw; }
            pid = 0;
            reported = null;
            playing = null;
            localPaused = false;
            Publish(link, "", false, new string[0]);
            Progress("Abre una partida desde Steam.");
            var watch = Stopwatch.StartNew();
            timer = new Timer(delegate
            {
                if (!Monitor.TryEnter(watch)) return;
                try
                {
                    if (pid == 0)
                    {
                        foreach (var process in Process.GetProcessesByName(ProcessName))
                        {
                            using (process)
                            {
                                if (!process.HasExited && pid == 0 &&
                                    string.Equals(process.MainModule.FileName, Executable(Folder(settings)), StringComparison.OrdinalIgnoreCase))
                                    pid = process.Id;
                            }
                        }
                    }
                    if (pid == 0 && watch.Elapsed.TotalMinutes < 3) return;
                    if (pid == 0 || !ProcessAlive(pid))
                    {
                        timer.Dispose(); timer = null;
                        inbox.Connected(false);
                        ClearPointer(pointer);
                        exited();
                        return;
                    }
                    if (!ReadStatus(link))
                    {
                        Publish(link, "", false, new string[0]);
                        Progress("Esperando respuesta del mod de " + Title + ".");
                    }
                }
                catch (Exception error)
                {
                    Publish(link, "", false, new string[0]);
                    Progress("No se pudo leer " + Title + ": " + error.Message);
                }
                finally { Monitor.Exit(watch); }
            }, null, 250, 250);
        }

        static bool ProcessAlive(int processId)
        {
            try { using (var process = Process.GetProcessById(processId)) return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }
        void ClearPointer(string path)
        {
            if (File.Exists(path) && File.ReadAllText(path) == session) File.Delete(path);
        }
        void Progress(string text)
        {
            if (text == reported) return;
            reported = text;
            ReportStatus(text);
            Log.Write(Title + ": " + text);
        }
        void Publish(Link link, string epoch, bool active, string[] capabilities)
        {
            link.SetContext(epoch);
            link.SetCapabilities(capabilities);
            if (playing == active) return;
            playing = active;
            link.OnModLine(active ? "state 1" : "state 0");
        }
        bool ReadStatus(Link link)
        {
            string path = Path.Combine(directory, "status.json");
            if (!File.Exists(path)) return false;
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                if (stream.Length > 65536) return false;
                text = reader.ReadToEnd();
            }
            var status = json.Deserialize<Dictionary<string, object>>(text);
            ulong now = GetTickCount64(), beat = Convert.ToUInt64(status["heartbeatMs"]);
            string epoch = (string)status["epoch"];
            if ((string)status["session"] != session || !SessionId.IsMatch(session) ||
                (string)status["bridgeVersion"] != BridgeVersion || Convert.ToInt32(status["pid"]) != pid ||
                !epoch.StartsWith(session + "_", StringComparison.Ordinal) || now < beat || now - beat > 2000) return false;
            bool active = (bool)status["inPlay"];
            var capabilities = new List<string>();
            foreach (object effect in (System.Collections.IEnumerable)status["capabilities"])
            {
                string name = effect as string;
                if (name != null && Regex.IsMatch(name, "^[a-z0-9_]{1,40}$")) capabilities.Add(name);
            }
            if (ReportsPause)
            {
                bool nextPause = (bool)status["paused"];
                if (localPaused != nextPause)
                {
                    localPaused = nextPause;
                    link.OnModLine(nextPause ? "pause 1" : "pause 0");
                }
            }
            if (!ReportsPause) Publish(link, epoch, active, capabilities.ToArray());
            foreach (Dictionary<string, object> ack in (System.Collections.IEnumerable)status["acks"])
                link.OnModLine("ack " + (string)ack["id"] + " " + (string)ack["result"]);
            if (ReportsPause) Publish(link, epoch, active, capabilities.ToArray());
            Progress(active ? Title + " listo para recibir efectos." : Title + " detectado. Entra en una partida controlable.");
            return true;
        }
        public override void Uninstall(Settings settings)
        {
            if (Running || AnotherRunning()) throw new InvalidOperationException("Cierra " + Title + " primero.");
            string root = PluginFolder(Folder(settings));
            string manifest = CupheadInstaller.SafePath(root, OwnedHash);
            string path = CupheadInstaller.SafePath(root, Plugin);
            if (!File.Exists(manifest)) return;
            if (File.Exists(path))
            {
                if (CupheadInstaller.HashFile(path) != File.ReadAllText(manifest))
                    throw new IOException("El mod de " + Title + " fue modificado. Retíralo manualmente.");
                File.Delete(path);
            }
            File.Delete(manifest);
        }
    }
}
