// CTR keeps its launcher, saves and original runtime. Cloud90's runtime is
// installed beside the original by the separately built GPL module package.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace Cloud90.GameLink
{
    internal sealed class CtrHost : GameHost
    {
        Process process;
        Inbox inbox;
        string session, inboxPath;
        public override string Id { get { return "ctrnf"; } }
        public override string Title { get { return "CTR Nitro-Fueled"; } }
        public override string FolderButton { get { return "Carpeta de CTR…"; } }
        public override string FolderPrompt { get { return "El portable de CTR con run.py y bin (no el dump PS4)"; } }
        internal static bool IsPortable(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "run.py")) &&
                File.Exists(Path.Combine(dir, "bin", "sacredfire.exe")) && File.Exists(Path.Combine(dir, "python", "python.exe"));
        }
        internal static string Runtime(string dir) { return Path.Combine(dir, "bin", "sacredfire-cloud90.exe"); }
        public override string Problem(Settings s)
        {
            if (!IsPortable(s.CtrDir)) return "no encontré CTR: elige la carpeta del portable";
            string manifest = Path.Combine(s.CtrDir, "bin", "cloud90-ctr.json");
            string wrapper = Path.Combine(s.CtrDir, "cloud90_run.py");
            if (!File.Exists(manifest) || !File.Exists(Runtime(s.CtrDir)) || !File.Exists(wrapper))
                return "instala el módulo CTR Cloud90 compatible (bin/sacredfire-cloud90.exe y cloud90-ctr.json)";
            try {
                var m = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest));
                if (m["bridgeVersion"] != "cloud90-ctr-1" || m["gameImageSha256"] != "9952a20911931dcd0a54fe19135117d96934d17c43b7ced103a4aa7fda783c31")
                    return "módulo CTR no compatible; instala la versión Cloud90";
                using (var sha = SHA256.Create()) using (var f = File.OpenRead(Runtime(s.CtrDir))) {
                    string hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (hash != m["runtimeSha256"]) return "el runtime CTR cambió; reinstala el módulo compatible";
                }
                using (var sha = SHA256.Create()) using (var f = File.OpenRead(wrapper)) {
                    string hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (hash != m["wrapperSha256"]) return "el iniciador CTR cambió; reinstala el módulo compatible";
                }
            } catch (Exception) { return "manifiesto CTR inválido; reinstala el módulo"; }
            return "";
        }
        public override bool UseFolder(Settings s, string dir) { if (Running || !IsPortable(dir)) return false; s.CtrDir = dir; return true; }
        public override void FindFolder(Settings s) { }
        public override string Folder(Settings s) { return s.CtrDir; }
        public override Inbox Prepare(Settings s)
        {
            if (Running) return inbox;
            if (inbox != null) inbox.Dispose();
            session = Guid.NewGuid().ToString("N");
            string dir = Path.Combine(Settings.DataDir, "ctrnf", session);
            Directory.CreateDirectory(dir);
            inboxPath = Path.Combine(dir, "inbox.json");
            inbox = new Inbox(inboxPath, session);
            return inbox;
        }
        public override bool Running { get { return process != null && !process.HasExited; } }
        public override bool AnotherRunning() { return Process.GetProcessesByName("sacredfire").Length > 0 || Process.GetProcessesByName("sacredfire-cloud90").Length > 0; }
        public override void Start(Settings s, Link link, Action exited)
        {
            string problem = Problem(s);
            if (problem.Length > 0) throw new InvalidOperationException(problem);
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(s.CtrDir, "python", "python.exe"));
            info.Arguments = "-X utf8 \"" + Path.Combine(s.CtrDir, "cloud90_run.py") + "\"";
            info.WorkingDirectory = s.CtrDir;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables["BB_PROBE"] = Runtime(s.CtrDir);
            info.EnvironmentVariables["BB_ONLINE"] = "0";
            info.EnvironmentVariables["CLOUD90_CTR_SESSION"] = session;
            info.EnvironmentVariables["CLOUD90_CTR_INBOX"] = inboxPath;
            // Use the same prepared game selected in this portable's launcher.
            // run.py understands BB_GAME_DIR; launcher.ini stores game_dir.
            string ini = Path.Combine(s.CtrDir, "launcher.ini");
            if (File.Exists(ini)) foreach (string line in File.ReadAllLines(ini)) {
                if (line.StartsWith("game_dir=")) info.EnvironmentVariables["BB_GAME_DIR"] = line.Substring(9).Trim();
            }
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            DataReceivedEventHandler output = delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) return;
                int at = e.Data.IndexOf("CLOUD90 ", StringComparison.Ordinal);
                if (at >= 0) link.OnModLine(e.Data.Substring(at + 8).Trim());
            };
            process.OutputDataReceived += output;
            process.ErrorDataReceived += output;
            process.Exited += delegate { inbox.Connected(false); exited(); };
            process.Start();
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        public override void Uninstall(Settings s)
        {
            if (Running || AnotherRunning()) throw new InvalidOperationException("Cierra CTR primero");
            if (Problem(s).Length != 0) throw new InvalidOperationException("El módulo cambió; no se eliminará automáticamente");
            if (inbox != null) inbox.Dispose();
            File.Delete(Runtime(s.CtrDir));
            File.Delete(Path.Combine(s.CtrDir, "cloud90_run.py"));
            File.Delete(Path.Combine(s.CtrDir, "bin", "cloud90-ctr.json"));
        }
    }
}
