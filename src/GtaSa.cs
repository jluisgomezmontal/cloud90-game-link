using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace Cloud90.GameLink
{
    internal static class GtaSaInstaller
    {
        internal const string GameHash = "a559aa772fd136379155efa71f00c47aad34bbfeae6196b0fe1047d0645cbd26";
        internal const string LoaderHash = "63b465dbc1bf9c5ab4e33c3ccad099d9edb702f2c51587ac8d4f196497396437";
        const string Manifest = "cloud90-gtasa-install.json";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        internal static bool IsGame(string root) { return !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "gta_sa.exe")); }
        internal static string Problem(string root)
        {
            if (!IsGame(root)) return "no encontré GTA San Andreas: selecciona la carpeta con gta_sa.exe";
            if (CupheadInstaller.HashFile(Path.Combine(root, "gta_sa.exe")) != GameHash) return "ejecutable no compatible: requiere la edición clásica 1.0 US validada";
            if (File.Exists(Path.Combine(root, "samp.dll")) || File.Exists(Path.Combine(root, "moonloader.asi"))) return "usa una instalación de un jugador sin SA-MP ni MoonLoader";
            return "";
        }
        static byte[] Resource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Falta el paquete GTA San Andreas en esta compilación");
                using (var output = new MemoryStream()) { stream.CopyTo(output); return output.ToArray(); }
            }
        }
        internal static Dictionary<string, string> Owned(string root)
        {
            string file = CupheadInstaller.SafePath(root, Manifest);
            var owned = File.Exists(file) ? Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) : new Dictionary<string, string>();
            foreach (string key in owned.Keys)
                if (key != "dinput8.dll" && key != "Cloud90.GTASA.asi" && key != "Cloud90.GTASA.LICENSE.txt") throw new InvalidDataException("Manifiesto GTA invalido");
            return owned;
        }
        internal static void Install(string root)
        {
            if (Process.GetProcessesByName("gta_sa").Length != 0) throw new InvalidOperationException("Cierra GTA San Andreas primero");
            string problem = Problem(root); if (problem.Length != 0) throw new InvalidOperationException(problem);
            var files = new Dictionary<string, byte[]> { { "dinput8.dll", Resource("GTASA.dinput8.dll") }, { "Cloud90.GTASA.asi", Resource("Cloud90.GTASA.asi") }, { "Cloud90.GTASA.LICENSE.txt", Resource("GTASA.Notices.txt") } };
            if (CupheadInstaller.Hash(files["dinput8.dll"]) != LoaderHash) throw new InvalidDataException("ASI Loader invalido");
            var owned = Owned(root);
            foreach (var file in files)
            {
                string path = CupheadInstaller.SafePath(root, file.Key);
                if (File.Exists(path) && CupheadInstaller.HashFile(path) != CupheadInstaller.Hash(file.Value) && (!owned.ContainsKey(file.Key) || owned[file.Key] != CupheadInstaller.HashFile(path)))
                    throw new IOException("Archivo existente incompatible: " + file.Key);
            }
            foreach (var file in files)
            {
                string path = CupheadInstaller.SafePath(root, file.Key);
                // Identical pre-existing files belong to the user, unless already recorded.
                if (File.Exists(path) && CupheadInstaller.HashFile(path) == CupheadInstaller.Hash(file.Value)) continue;
                File.WriteAllBytes(path, file.Value); owned[file.Key] = CupheadInstaller.Hash(file.Value);
                File.WriteAllText(CupheadInstaller.SafePath(root, Manifest), Json.Serialize(owned));
            }
        }
        internal static void Uninstall(string root)
        {
            if (Process.GetProcessesByName("gta_sa").Length != 0) throw new InvalidOperationException("Cierra GTA San Andreas primero");
            var owned = Owned(root);
            foreach (var file in owned)
            {
                string path = CupheadInstaller.SafePath(root, file.Key);
                if (File.Exists(path) && CupheadInstaller.HashFile(path) != file.Value) throw new IOException("Archivo modificado: " + file.Key);
            }
            bool sharedLoader = false;
            foreach (string file in Directory.GetFiles(root, "*.asi", SearchOption.AllDirectories))
                if (!string.Equals(file, Path.Combine(root, "Cloud90.GTASA.asi"), StringComparison.OrdinalIgnoreCase)) sharedLoader = true;
            foreach (var file in owned)
            {
                if (file.Key == "dinput8.dll" && sharedLoader) continue;
                string path = CupheadInstaller.SafePath(root, file.Key); if (File.Exists(path)) File.Delete(path);
            }
            string manifest = CupheadInstaller.SafePath(root, Manifest); if (File.Exists(manifest)) File.Delete(manifest);
        }
    }
    internal sealed class GtaSaHost : GameHost
    {
        static readonly string[] KnownEffects = { "heal", "armour", "money_add", "wanted_clear", "wanted_set", "repair_vehicle", "drunk_controls", "invert_controls", "no_shoot", "remove_weapon", "remove_ammo", "launch", "invert_steering", "stuck_accelerator", "burst_tyres", "ice_floor", "spin_vehicle", "engine_off", "no_jump", "freeze_feet", "dense_fog", "camera_shake", "spawn_enemies", "spawn_pursuer", "spawn_obstacles" };
        Inbox inbox;
        Process game;
        Timer timer;
        string session, folder;
        readonly object gate = new object();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        readonly HashSet<string> replies = new HashSet<string>();
        bool localPaused;
        bool? lastPlaying;
        string lastStatus;
        DateTime startedAt;
        void Progress(string text) { if (lastStatus == text) return; lastStatus = text; ReportStatus(text); Log.Write("GTA San Andreas: " + text); }
        void State(Link link, bool playing) { if (lastPlaying == playing) return; lastPlaying = playing; link.OnModLine(playing ? "state 1" : "state 0"); }
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public override string Id { get { return "gtasa"; } }
        public override string Title { get { return "GTA San Andreas"; } }
        public override string FolderButton { get { return "Carpeta de GTA…"; } }
        public override string FolderPrompt { get { return "GTA San Andreas clásico (gta_sa.exe)"; } }
        public override string Problem(Settings s) { return GtaSaInstaller.Problem(s.GtaSaDir); }
        public override string Folder(Settings s) { return s.GtaSaDir; }
        public override bool UseFolder(Settings s, string dir) { if (Running || !GtaSaInstaller.IsGame(dir)) return false; s.GtaSaDir = dir; return true; }
        public override void FindFolder(Settings s)
        {
            if (GtaSaInstaller.IsGame(s.GtaSaDir)) return;
            foreach (var drive in DriveInfo.GetDrives())
            { string path = Path.Combine(drive.RootDirectory.FullName, "Grand Theft Auto San Andreas"); if (GtaSaInstaller.IsGame(path)) { s.GtaSaDir = path; return; } }
        }
        public override bool Running { get { return game != null && !game.HasExited; } }
        public override bool AnotherRunning() { return Process.GetProcessesByName("gta_sa").Length != 0; }
        public override Inbox Prepare(Settings s)
        {
            if (Running) return inbox;
            GtaSaInstaller.Install(s.GtaSaDir);
            if (inbox != null) inbox.Dispose();
            session = Guid.NewGuid().ToString("N"); folder = Path.Combine(Settings.DataDir, "gtasa", session); Directory.CreateDirectory(folder);
            inbox = new Inbox(Path.Combine(folder, "inbox.json"), session); return inbox;
        }
        public override void Start(Settings s, Link link, Action exited)
        {
            if (AnotherRunning()) throw new InvalidOperationException("Cierra GTA y ábrelo desde Game Link");
            if (timer != null) timer.Dispose(); if (game != null) game.Dispose(); replies.Clear(); localPaused = false;
            link.SetContext(""); link.SetCapabilities(new string[0]); lastPlaying = null; State(link, false);
            var info = new ProcessStartInfo(Path.Combine(s.GtaSaDir, "gta_sa.exe")) { WorkingDirectory = s.GtaSaDir, UseShellExecute = false };
            info.EnvironmentVariables["CLOUD90_GTASA_SESSION"] = session; info.EnvironmentVariables["CLOUD90_GTASA_DIRECTORY"] = folder;
            game = Process.Start(info);
            startedAt = DateTime.UtcNow; lastStatus = null;
            Progress("Esperando a GTA. Completa los diálogos de inicio y carga una partida.");
            timer = new Timer(delegate {
                lock (gate)
                {
                    if (!Running) { timer.Dispose(); inbox.Connected(false); exited(); return; }
                    try
                    {
                        string path = Path.Combine(folder, "status.json");
                        if (!File.Exists(path) || new FileInfo(path).Length > 65536) {
                            State(link, false);
                            if ((DateTime.UtcNow - startedAt).TotalSeconds > 45) Progress("GTA aún no confirma la integración. Revisa su ventana y el registro.");
                            return;
                        }
                        string text;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
                        var status = json.Deserialize<Dictionary<string, object>>(text);
                        ulong now = GetTickCount64(), beat = Convert.ToUInt64(status["heartbeatMs"]);
                        string bridge = (string)status["bridgeVersion"];
                        if ((string)status["session"] != session || (bridge != "cloud90-gtasa-1" && bridge != "cloud90-gtasa-2" && bridge != "cloud90-gtasa-3") || Convert.ToInt32(status["pid"]) != game.Id || now < beat || now - beat > 2000) {
                            State(link, false); Progress("Sin respuesta reciente de GTA. Revisa si hay un menú o diálogo abierto."); return;
                        }
                        link.SetContext((string)status["epoch"]);
                        var capabilities = new List<string>(); object rawCapabilities;
                        if (status.TryGetValue("capabilities", out rawCapabilities) && rawCapabilities is System.Collections.IEnumerable)
                            foreach (object capability in (System.Collections.IEnumerable)rawCapabilities) {
                                string name = capability as string;
                                if (name != null && Array.IndexOf(KnownEffects, name) >= 0 && !capabilities.Contains(name)) capabilities.Add(name);
                            }
                        capabilities.Sort(StringComparer.Ordinal); link.SetCapabilities(capabilities.ToArray());
                        State(link, (bool)status["inPlay"]);
                        Progress((bool)status["inPlay"] ? "CJ listo: partida activa para recibir efectos." : "Integración detectada. Carga una partida y sal de menús y cinemáticas.");
                        bool paused = (bool)status["paused"]; if (paused != localPaused) { localPaused = paused; link.OnModLine(paused ? "pause 1" : "pause 0"); }
                        foreach (Dictionary<string, object> ack in (System.Collections.IEnumerable)status["acks"])
                        { string id = (string)ack["id"]; if (replies.Add(id)) link.OnModLine("ack " + id + " " + (string)ack["result"]); }
                    }
                    catch (Exception error) { State(link, false); Progress("No se pudo leer la respuesta de GTA: " + error.Message); }
                }
            }, null, 250, 250);
        }
        public override void Uninstall(Settings s) { if (Running || AnotherRunning()) throw new InvalidOperationException("Cierra GTA primero"); if (inbox != null) inbox.Dispose(); GtaSaInstaller.Uninstall(s.GtaSaDir); }
    }
}
