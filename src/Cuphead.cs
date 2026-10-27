using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Cloud90.GameLink
{
    internal static class CupheadInstaller
    {
        internal const string GameHash = "d0eafb33fedb0eedcd5eda6d2423cc7b9fcb2e60c40e92814a0ff785e2d9cbed";
        internal const string LoaderHash = "6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755";
        const string ManifestName = "cloud90-cuphead-install.json";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string HashFile(string path) { return Hash(File.ReadAllBytes(path)); }
        internal static bool IsGame(string path) { return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "Cuphead.exe")) && File.Exists(Path.Combine(path, "Cuphead_Data", "Managed", "Assembly-CSharp.dll")); }
        internal static string Problem(string path)
        {
            if (!IsGame(path)) return "no encontré Cuphead: selecciona su carpeta de Steam";
            if (HashFile(Path.Combine(path, "Cuphead_Data", "Managed", "Assembly-CSharp.dll")) != GameHash) return "build de Cuphead no compatible; requiere Steam build 20241530";
            byte[] exe = File.ReadAllBytes(Path.Combine(path, "Cuphead.exe"));
            if (exe.Length < 64) return "Cuphead.exe invalido";
            int pe = BitConverter.ToInt32(exe, 60);
            if (pe < 0 || pe + 6 > exe.Length || BitConverter.ToUInt16(exe, pe + 4) != 0x8664) return "requiere Cuphead de 64 bits";
            return "";
        }
        static byte[] Resource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Falta el paquete Cuphead; usa el release completo de Game Link");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }
        internal static string SafePath(string root, string name)
        {
            string full = Path.GetFullPath(Path.Combine(root, name));
            if (name.IndexOf(':') >= 0 || Path.IsPathRooted(name) || !full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta fuera de Cuphead");
            for (string p = full; p != null && p.Length >= Path.GetFullPath(root).Length; p = Path.GetDirectoryName(p))
            {
                try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ruta enlazada no admitida"); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return full;
        }
        static Dictionary<string, string> Owned(string root)
        {
            string path = SafePath(root, ManifestName);
            return File.Exists(path) ? Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : new Dictionary<string, string>();
        }
        internal static void Install(string root)
        {
            if (Process.GetProcessesByName("Cuphead").Length != 0) throw new InvalidOperationException("Cierra Cuphead antes de instalar o actualizar el plugin");
            string problem = Problem(root); if (problem.Length > 0) throw new InvalidOperationException(problem);
            byte[] archive = Resource("Cuphead.BepInEx.zip");
            if (Hash(archive) != LoaderHash) throw new InvalidDataException("Paquete BepInEx invalido");
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Length == 0) continue;
                    SafePath(root, entry.FullName);
                    using (var input = entry.Open()) using (var output = new MemoryStream()) { input.CopyTo(output); files.Add(entry.FullName, output.ToArray()); }
                }
            files.Add("BepInEx/plugins/Cloud90/Cloud90.Cuphead.dll", Resource("Cloud90.Cuphead.dll"));
            files.Add("BepInEx/plugins/Cloud90/Newtonsoft.Json.dll", Resource("Cuphead.Newtonsoft.Json.dll"));
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (name.StartsWith("Cuphead.License.", StringComparison.Ordinal))
                    files.Add("BepInEx/plugins/Cloud90/licenses/" + name.Substring("Cuphead.License.".Length), Resource(name));
            var owned = Owned(root);
            // Validate every destination before changing any file.
            foreach (var file in files)
            {
                string path = SafePath(root, file.Key);
                if (File.Exists(path) && HashFile(path) != Hash(file.Value) && (!owned.ContainsKey(file.Key) || HashFile(path) != owned[file.Key]))
                    throw new IOException("Archivo existente incompatible, no se modificó: " + file.Key);
            }
            foreach (var file in files)
            {
                string path = SafePath(root, file.Key);
                if (File.Exists(path) && HashFile(path) == Hash(file.Value)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, file.Value); owned[file.Key] = Hash(file.Value);
            }
            File.WriteAllText(SafePath(root, ManifestName), Json.Serialize(owned));
        }
        internal static void Uninstall(string root)
        {
            if (Process.GetProcessesByName("Cuphead").Length != 0) throw new InvalidOperationException("Cierra Cuphead primero");
            RemoveOwned(root);
        }
        internal static void RemoveOwned(string root)
        {
            var owned = Owned(root);
            foreach (var file in owned)
            {
                string path = SafePath(root, file.Key);
                if (File.Exists(path) && HashFile(path) != file.Value) throw new IOException("Un archivo cambió; no se eliminó: " + file.Key);
            }
            bool sharedLoader = false;
            foreach (string kind in new[] { "plugins", "patchers" })
            {
                string directory = Path.Combine(root, "BepInEx", kind);
                if (!Directory.Exists(directory)) continue;
                foreach (string dll in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
                {
                    string relative = dll.Substring(Path.GetFullPath(root).TrimEnd('\\').Length + 1).Replace('\\', '/');
                    if (!relative.StartsWith("BepInEx/plugins/Cloud90/", StringComparison.OrdinalIgnoreCase)) sharedLoader = true;
                }
            }
            foreach (var file in owned)
            {
                if (sharedLoader && !file.Key.Replace('\\', '/').StartsWith("BepInEx/plugins/Cloud90/", StringComparison.OrdinalIgnoreCase)) continue;
                string path = SafePath(root, file.Key); if (File.Exists(path)) File.Delete(path);
            }
            string manifest = SafePath(root, ManifestName); if (File.Exists(manifest)) File.Delete(manifest);
        }
        internal static string Find()
        {
            var roots = new List<string>();
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                string root = Registry.GetValue(key, key.StartsWith("HKEY_CURRENT_USER") ? "SteamPath" : "InstallPath", "") as string;
                if (!string.IsNullOrEmpty(root)) roots.Add(root);
            }
            foreach (string root in roots.ToArray())
            {
                string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(file)) foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\"")) roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (string root in roots) { string game = Path.Combine(root, "steamapps", "common", "Cuphead"); if (IsGame(game)) return game; }
            return "";
        }
    }
    internal sealed class CupheadHost : GameHost
    {
        Inbox inbox;
        string session, folder;
        Process game;
        Timer timer;
        bool starting, localPaused;
        DateTime deadline;
        readonly object gate = new object();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly HashSet<string> replies = new HashSet<string>();
        string lastReason;
        bool? lastInPlay;
        static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public override string Id { get { return "cuphead"; } }
        public override string Title { get { return "Cuphead"; } }
        public override string FolderButton { get { return "Carpeta de Cuphead…"; } }
        public override string FolderPrompt { get { return "Cuphead en Steam (Cuphead.exe y Cuphead_Data)"; } }
        public override string Problem(Settings s) { return CupheadInstaller.Problem(s.CupheadDir); }
        public override bool UseFolder(Settings s, string dir) { if (Running || !CupheadInstaller.IsGame(dir)) return false; s.CupheadDir = dir; return true; }
        public override void FindFolder(Settings s) { if (!CupheadInstaller.IsGame(s.CupheadDir)) s.CupheadDir = CupheadInstaller.Find(); }
        public override string Folder(Settings s) { return s.CupheadDir; }
        public override bool Running { get { return starting || (game != null && !game.HasExited); } }
        public override bool AnotherRunning() { return Process.GetProcessesByName("Cuphead").Length != 0; }
        public override Inbox Prepare(Settings s)
        {
            if (Running) return inbox;
            CupheadInstaller.Install(s.CupheadDir);
            if (inbox != null) inbox.Dispose();
            session = Guid.NewGuid().ToString("N");
            folder = Path.Combine(Settings.DataDir, "cuphead", session); Directory.CreateDirectory(folder);
            inbox = new Inbox(Path.Combine(folder, "inbox.json"), session);
            File.WriteAllText(Path.Combine(Settings.DataDir, "cuphead", "active.json"), json.Serialize(new { session = session }));
            return inbox;
        }
        public override void Start(Settings s, Link link, Action exited)
        {
            if (AnotherRunning()) throw new InvalidOperationException("Cierra Cuphead y ábrelo desde Game Link");
            if (game != null) { game.Dispose(); game = null; }
            starting = true; deadline = DateTime.UtcNow.AddSeconds(60); replies.Clear(); localPaused = false; lastInPlay = null; lastReason = null;
            try { Process.Start(new ProcessStartInfo("steam://rungameid/268910") { UseShellExecute = true }); }
            catch { starting = false; throw; }
            timer = new Timer(delegate {
                lock (gate)
                {
                    try
                    {
                        if (game != null && game.HasExited) { starting = false; timer.Dispose(); inbox.Connected(false); exited(); return; }
                        if (starting && DateTime.UtcNow > deadline) throw new IOException("El plugin Cuphead no confirmó el arranque");
                        string statusPath = Path.Combine(folder, "status.json");
                        if (!File.Exists(statusPath) || new FileInfo(statusPath).Length > 65536) { if (starting && DateTime.UtcNow > deadline) throw new IOException("El plugin Cuphead no confirmó el arranque"); return; }
                        var status = json.Deserialize<Dictionary<string, object>>(ReadShared(statusPath));
                        long now = (long)GetTickCount64(), heartbeat = Convert.ToInt64(status["heartbeatMs"]);
                        if ((string)status["session"] != session || (string)status["bridgeVersion"] != "cloud90-cuphead-2" || now < heartbeat || now - heartbeat > 3000)
                        { lastInPlay = false; link.OnModLine("state 0"); return; }
                        if (game == null)
                        {
                            var candidate = Process.GetProcessById(Convert.ToInt32(status["pid"]));
                            if (!string.Equals(candidate.MainModule.FileName, Path.Combine(s.CupheadDir, "Cuphead.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Proceso del plugin no corresponde a Cuphead");
                            game = candidate; starting = false; Log.Write("Cuphead: plugin listo, DLC " + status["dlc"]);
                        }
                        link.SetContext((string)status["epoch"]);
                        object capabilityValue;
                        var available = new List<string>();
                        if (status.TryGetValue("capabilities", out capabilityValue) && capabilityValue is System.Collections.IEnumerable)
                            foreach (object effect in (System.Collections.IEnumerable)capabilityValue)
                                if (effect is string && Regex.IsMatch((string)effect, "^[a-z0-9_]{1,40}$") && available.Count < 80) available.Add((string)effect);
                        available.Sort(StringComparer.Ordinal);
                        link.SetCapabilities(available.ToArray());
                        bool inPlay = (bool)status["inPlay"];
                        if (lastInPlay != inPlay) { lastInPlay = inPlay; link.OnModLine(inPlay ? "state 1" : "state 0"); }
                        bool paused = (bool)status["paused"];
                        if (paused != localPaused) { localPaused = paused; link.OnModLine(paused ? "pause 1" : "pause 0"); }
                        string reason = (string)status["reason"];
                        if (reason != lastReason) { lastReason = reason; Log.Write("Cuphead: " + reason); }
                        object raw;
                        if (status.TryGetValue("acks", out raw) && raw is System.Collections.IEnumerable)
                            foreach (Dictionary<string, object> ack in (System.Collections.IEnumerable)raw)
                            {
                                string id = (string)ack["id"];
                                if (replies.Add(id)) link.OnModLine("ack " + id + " " + (string)ack["result"]);
                            }
                        if (replies.Count > 2000) replies.Clear();
                    }
                    catch (IOException) { if (starting && DateTime.UtcNow > deadline) { starting = false; timer.Dispose(); link.OnGameExited(); exited(); Log.Write("Cuphead: no respondió el plugin; revisa BepInEx/LogOutput.log"); } }
                    catch (Exception e) { lastInPlay = false; link.OnModLine("state 0"); Log.Write("Cuphead: " + e.Message); }
                }
            }, null, 200, 250);
        }
        public override void Uninstall(Settings s) { if (Running || AnotherRunning()) throw new InvalidOperationException("Cierra Cuphead primero"); if (inbox != null) inbox.Dispose(); CupheadInstaller.Uninstall(s.CupheadDir); }
    }
}
