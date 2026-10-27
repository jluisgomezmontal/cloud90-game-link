// Explicit opt-in smoke test with the user's installed Cuphead and a loopback server.
using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading;

namespace Cloud90.GameLink
{
    internal static class CupheadLive
    {
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[1] == "--installer") return InstallerSmoke(args[0]);
            if (args.Length != 2 || !args[1].StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) return 2;
            // Do not load or persist the user's production settings or tokens.
            Environment.SetEnvironmentVariable("CLOUD90_GAME_LINK_DIR", null);
            var settings = new Settings { CupheadDir = args[0], Server = args[1] };
            settings.Tokens["cuphead"] = "c90gl_test";
            var host = new CupheadHost();
            var link = new Link(delegate { return settings; }, "cuphead", "Cuphead");
            link.Status += Console.WriteLine;
            Inbox inbox = null;
            var done = new ManualResetEvent(false);
            Log.Line += delegate(string line) {
                Console.WriteLine(line);
                if (line.Contains("efecto cuphead-live-1: ok")) ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(5000); done.Set(); });
            };
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; done.Set(); };
            try
            {
                inbox = host.Prepare(settings);
                link.UseInbox(inbox);
                link.Start();
                host.Start(settings, link, delegate { done.Set(); });
                Console.WriteLine("Enter a solo level. This test uses only the local fake server. Ctrl+C stops it.");
                done.WaitOne(TimeSpan.FromMinutes(10));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { link.Stop(); if (inbox != null) inbox.Dispose(); done.Dispose(); }
        }
        static int InstallerSmoke(string gameDir)
        {
            string root = Path.Combine(Path.GetTempPath(), "c90-cuphead-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Cuphead_Data", "Managed"));
            try
            {
                string managed = Path.Combine("Cuphead_Data", "Managed", "Assembly-CSharp.dll");
                File.Copy(Path.Combine(gameDir, "Cuphead.exe"), Path.Combine(root, "Cuphead.exe"));
                File.Copy(Path.Combine(gameDir, managed), Path.Combine(root, managed));
                string exeHash = CupheadInstaller.HashFile(Path.Combine(root, "Cuphead.exe"));
                string plugin = Path.Combine(root, "BepInEx", "plugins", "Cloud90", "Cloud90.Cuphead.dll");
                string loader = Path.Combine(root, "winhttp.dll");
                string manifest = Path.Combine(root, "cloud90-cuphead-install.json");
                CupheadInstaller.Install(root);
                if (!File.Exists(plugin) || !File.Exists(loader) || !File.Exists(Path.Combine(Path.GetDirectoryName(plugin), "licenses", "Newtonsoft.Json.txt"))) throw new Exception("Full package was not installed");
                CupheadInstaller.Uninstall(root);
                if (File.Exists(plugin) || File.Exists(loader) || File.Exists(manifest)) throw new Exception("Fresh install did not uninstall fully");
                Console.WriteLine("PASS: full clean install and owned-file uninstall");

                CupheadInstaller.Install(root);
                string other = Path.Combine(root,"BepInEx","plugins","Other.dll");
                File.WriteAllText(other,"pre-existing third-party mod");
                CupheadInstaller.Uninstall(root);
                if (!File.Exists(loader) || !File.Exists(other) || File.Exists(plugin)) throw new Exception("Shared loader or other mod was damaged");
                CupheadInstaller.Install(root);
                var owned = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest));
                if (owned.ContainsKey("winhttp.dll")) throw new Exception("Claimed a pre-existing loader");
                CupheadInstaller.Uninstall(root);
                if (!File.Exists(loader) || !File.Exists(other)) throw new Exception("Pre-existing files removed");
                Console.WriteLine("PASS: compatible pre-existing loader and third-party plugin preserved");

                File.WriteAllText(plugin,"foreign plugin bytes");
                bool conflict = false;
                try { CupheadInstaller.Install(root); } catch (IOException) { conflict = true; }
                if (!conflict || File.ReadAllText(plugin)!="foreign plugin bytes" || File.Exists(manifest)) throw new Exception("Conflicting files were overwritten");
                if (CupheadInstaller.HashFile(Path.Combine(root,"Cuphead.exe"))!=exeHash || CupheadInstaller.HashFile(Path.Combine(root,managed))!=CupheadInstaller.GameHash) throw new Exception("Game files changed");
                Console.WriteLine("PASS: conflicting plugin rejected before writes; game files unchanged");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                string target = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                if (!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("c90-cuphead-install-",StringComparison.Ordinal)) throw new IOException("Unsafe cleanup path");
                Directory.Delete(target,true);
            }
        }
    }
}
