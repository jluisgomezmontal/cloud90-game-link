// Checks for the Link's own parts, compiled with its sources by
// tests/link-test.ps1 (no Hades needed).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Cloud90.GameLink
{
    public static class LinkTests
    {
        static int failures;
        static string ReadShared(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
                }
                catch (IOException) { if (attempt >= 20) throw; Thread.Sleep(10); }
            }
        }

        static void Check(bool ok, string what)
        {
            if (!ok) { failures++; Console.WriteLine("FAIL: " + what); }
        }

        public static int StartupWindow()
        {
            int before = failures;
            Thread thread = new Thread(delegate () {
                try {
                    using (MainForm form = new MainForm()) {
                        var games = new List<GamePanel>();
                        Action<System.Windows.Forms.Control> visit = null;
                        visit = delegate(System.Windows.Forms.Control control) {
                            if (control is GamePanel) games.Add((GamePanel)control);
                            foreach (System.Windows.Forms.Control child in control.Controls) visit(child);
                        };
                        visit(form);
                        Check(games.Count == 6, "main window constructs all six library games");
                        foreach (string id in new[] { "hades", "kof2002", "mslug3", "ctrnf", "cuphead", "gtasa" })
                            Check(games.Exists(delegate(GamePanel game) { return game.GameId == id; }), "library game: " + id);
                    }
                } catch (Exception error) { Check(false, "main window startup: " + error.GetType().Name); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(); thread.Join();
            return failures - before;
        }

        public const string HostileName = "Ana\" } os.exit() --\n]]ñ\U0001F600";

        public static string HostileNameBase64 { get { return Convert.ToBase64String(Encoding.UTF8.GetBytes(HostileName)); } }

        public static int NetworkSecurity()
        {
            Check(Link.SocketUri("https://cloud90.dev").ToString() == "wss://cloud90.dev/api/game-link/ws", "HTTPS becomes WSS");
            Check(Link.SocketUri("http://127.0.0.1:39090").Port == 39090, "local HTTP test endpoint is allowed");
            foreach (string value in new string[] { "http://example.com", "ws://example.com", "https://user:password@example.com", "https://example.com?token=secret", "file:///C:/test" })
            {
                bool rejected = false;
                try { Link.SocketUri(value); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "unsafe endpoint rejected: " + value);
            }
            return failures;
        }

        public static int CtrInbox(string folder)
        {
            int before = failures;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "inbox.json");
            using (Inbox inbox = new Inbox(path, "session-123")) {
                var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                Check((bool)root["paused"], "CTR starts paused until Cloud90 welcomes the connection");
                inbox.Connected(true);
                Check(inbox.Add(new Inbox.Item { Id = "test-1", Effect = "give_item", Arg = "missile", Ms = 1, Sent = DateTime.UtcNow }), "CTR queues a valid effect");
                Check(!inbox.Add(new Inbox.Item { Id = "test-1", Effect = "give_item" }), "CTR refuses duplicate pending IDs");
                Check(!inbox.Add(new Inbox.Item { Id = "bad\nack fake ok", Effect = "give_item" }), "CTR refuses line injection in IDs");
                root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                Check(!(bool)root["paused"] && (string)root["session"] == "session-123", "CTR envelope carries session and connection state");
                Check(File.ReadAllText(path).Contains("missile"), "CTR delivers the selected weapon");
                inbox.Connected(false);
                Check(inbox.Take("test-1") == null, "CTR clears pending commands on disconnect");
                root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                Check((bool)root["paused"], "CTR disconnect pauses the runtime");
            }
            Check(!CtrHost.IsPortable(folder), "CTR rejects a folder containing only an inbox");
            Directory.CreateDirectory(Path.Combine(folder, "bin"));
            Directory.CreateDirectory(Path.Combine(folder, "python"));
            File.WriteAllText(Path.Combine(folder, "run.py"), "");
            File.WriteAllText(Path.Combine(folder, "python", "python.exe"), "");
            File.WriteAllText(Path.Combine(folder, "bin", "sacredfire.exe"), "");
            File.WriteAllText(CtrHost.Runtime(folder), "");
            File.WriteAllText(Path.Combine(folder, "cloud90_run.py"), "");
            Settings settings = new Settings();settings.CtrDir = folder;
            CtrHost host = new CtrHost();
            Check(host.Problem(settings).Contains("cliente"), "CTR requires the original client");
            File.WriteAllText(CtrHost.Launcher(folder), "");
            File.WriteAllText(Path.Combine(folder, "bin", "cloud90-ctr.json"),
                "{\"bridgeVersion\":\"cloud90-ctr-1\",\"gameImageSha256\":\"9952a20911931dcd0a54fe19135117d96934d17c43b7ced103a4aa7fda783c31\"}");
            Check(host.Problem(settings).Contains("actualiza el módulo"), "CTR rejects old wrappers before opening the client");
            return failures - before;
        }

        public static int OversizedProtocol(string server)
        {
            Settings s = new Settings();
            s.Server = server;
            s.Tokens["kof2002"] = "c90gl_test";
            Link link = new Link(delegate { return s; }, "kof2002", "KOF 2002");
            bool rejected = false;
            link.Status += delegate (string status) { if (status.Contains("demasiado grande")) Volatile.Write(ref rejected, true); };
            link.Start();
            try {
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                while (!Volatile.Read(ref rejected) && DateTime.UtcNow < deadline) Thread.Sleep(50);
                Check(Volatile.Read(ref rejected), "oversized fragmented messages are rejected");
            }
            finally { link.Stop(); }
            return failures;
        }

        // Inbox text for a viewer name that tries to break out of the string.
        public static string WriteHostileInbox(string path)
        {
            Inbox inbox = new Inbox(path);
            Inbox.Item item = new Inbox.Item();
            item.Id = "abc-1";
            item.Effect = "heal";
            item.Ms = 15000;
            item.By = HostileName;
            item.Safe = true;
            item.Sent = DateTime.UtcNow;
            inbox.Add(item);
            Inbox.Item old = new Inbox.Item();
            old.Id = "old-1";
            old.Effect = "damage";
            old.By = "x";
            old.Sent = DateTime.UtcNow - TimeSpan.FromMinutes(1);
            inbox.Add(old);
            List<Inbox.Item> expired = inbox.Expire(TimeSpan.FromSeconds(10));
            Check(expired.Count == 1 && expired[0].Id == "old-1", "old effects expire");
            Check(inbox.Take("abc-1") != null && inbox.Take("abc-1") == null, "taken once");
            inbox.Add(item);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public static int Installer(string root)
        {
            string scripts = Path.Combine(root, "Content", "Scripts");
            Directory.CreateDirectory(scripts);
            Directory.CreateDirectory(Path.Combine(root, "x64"));
            File.WriteAllText(Path.Combine(root, "x64", "Hades.exe"), "");
            string original = "Import \"Something.lua\"\r\nfunction Foo() end\r\n";
            File.WriteAllText(Path.Combine(scripts, "RoomManager.lua"), original, new UTF8Encoding(true));
            Check(HadesFolder.IsHades(root), "a Hades folder is recognized");
            ModInstaller.ModText = delegate { return "-- mod"; };
            ModInstaller.Install(root);
            ModInstaller.Install(root);  // twice: one Import
            string patched = File.ReadAllText(Path.Combine(scripts, "RoomManager.lua"), Encoding.UTF8);
            int first = patched.IndexOf("Import \"Cloud90.lua\"");
            Check(first > 0 && patched.IndexOf("Import \"Cloud90.lua\"", first + 1) < 0, "one Import line");
            Check(File.Exists(Path.Combine(scripts, "Cloud90.lua")), "mod copied");
            Check(File.ReadAllText(Path.Combine(root, "x64", "steam_appid.txt")) == "1145360", "steam_appid.txt");
            ModInstaller.Uninstall(root);
            string restored = File.ReadAllText(Path.Combine(scripts, "RoomManager.lua"), Encoding.UTF8);
            Check(restored.TrimEnd() == original.TrimEnd(), "uninstall leaves the script as it was");
            Check(!File.Exists(Path.Combine(scripts, "Cloud90.lua")), "mod removed");
            return failures;
        }

        // MAME: the plugin goes in plugins\cloud90 and comes out whole; what's
        // missing is said by name; tokens are kept per game.
        public static int Mame(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "roms"));
            File.WriteAllText(Path.Combine(root, "mame.exe"), "");
            Check(MameFolder.IsMame(root), "a MAME folder is recognized");
            Check(MameFolder.Missing(root, "kof2002", "neogeo").Contains("kof2002.zip"), "the ROM is asked for");
            File.WriteAllText(Path.Combine(root, "roms", "kof2002.zip"), "");
            Check(MameFolder.Missing(root, "kof2002", "neogeo").Contains("neogeo.zip"), "then the BIOS");
            File.WriteAllText(Path.Combine(root, "roms", "neogeo.zip"), "");
            Check(MameFolder.Missing(root, "kof2002", "neogeo") == "", "nothing missing");
            MamePlugin.FileText = delegate (string name) { return "-- " + name; };
            MamePlugin.Install(root);
            Check(File.ReadAllText(Path.Combine(root, "plugins", "cloud90", "init.lua")) == "-- cloud90-mame-init.lua", "plugin init.lua");
            Check(File.Exists(Path.Combine(root, "plugins", "cloud90", "plugin.json")), "plugin.json");
            MamePlugin.Uninstall(root);
            Check(!Directory.Exists(Path.Combine(root, "plugins", "cloud90")), "plugin removed");

            Settings s = new Settings();
            s.Tokens["hades"] = "c90gl_a";
            s.Tokens["kof2002"] = "c90gl_b";
            s.MameDir = root;
            s.Save();
            Settings back = Settings.Load();
            Check(back.TokenFor("hades") == "c90gl_a" && back.TokenFor("kof2002") == "c90gl_b" && back.MameDir == root, "tokens per game survive a restart");
            return failures;
        }

        // Real MAME + KOF 2002 against tests/fake-server.js: the app installs
        // the plugin, opens the game (no window, a script plays it), and the
        // effect the server sends once a round is on is applied and acked.
        public static int MameEndToEnd(string server, string mame, string driver)
        {
            Settings s = new Settings();
            s.Server = server;
            s.MameDir = mame;
            s.Tokens["kof2002"] = "c90gl_test";
            Link link = new Link(delegate { return s; }, "kof2002", "KOF 2002");
            MamePlugin.FileText = delegate (string name)
            {
                string dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(driver)), "..", "mame", "plugins", "cloud90");
                return File.ReadAllText(Path.Combine(dir, name == "cloud90-mame-init.lua" ? "init.lua" : "plugin.json"));
            };
            MamePlugin.Install(mame);
            link.UseInbox(new Inbox(MameProcess.InboxPath("kof2002")));
            MameProcess.ExtraArgs = "-video none -sound none -nothrottle -autoboot_script \"" + driver + "\" -seconds_to_run 90";
            MameProcess game = new MameProcess();
            StringBuilder lines = new StringBuilder();
            game.GameLine += delegate (string line) { lock (lines) lines.AppendLine(line); link.OnModLine(line); };
            bool exited = false;
            game.Exited += delegate { exited = true; };
            link.Start();
            game.Start(mame, "kof2002");
            DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(150);
            while (!exited && DateTime.UtcNow < limit)
            {
                lock (lines) if (lines.ToString().Contains("ack e-42")) break;
                Thread.Sleep(250);
            }
            Thread.Sleep(1500);
            string got;
            lock (lines) got = lines.ToString();
            Console.WriteLine(got);
            Check(got.Contains("hello 1.1.0 kof2002"), "the plugin said hello");
            Check(got.Contains("state 1"), "a round came");
            Check(got.Contains("ack e-42 ok"), "the server's effect was applied");
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("mame")) { try { p.Kill(); } catch (Exception) { } }
            MamePlugin.Uninstall(mame);
            return failures;
        }

        // The real server with a made-up token: the TLS handshake has to work
        // (the server then refuses the token). Returns what the Link said.
        public static string RealServer(string server)
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            Settings s = new Settings();
            s.Server = server;
            s.Tokens["hades"] = "c90gl_not_a_real_token";
            Link link = new Link(delegate { return s; }, "hades", "Hades");
            StringBuilder said = new StringBuilder();
            link.Status += delegate (string text) { lock (said) said.AppendLine(text); };
            link.Start();
            Thread.Sleep(9000);
            lock (said) return said.ToString();
        }

        // Against tests/fake-server.js: connects, says hello, gets an effect
        // into the inbox, and answers it as the mod would.
        public static int CtrProtocol(string server, string inboxPath)
        { return JsonProtocol(server, inboxPath, "ctrnf"); }
        public static int CupheadProtocol(string server, string inboxPath)
        { return JsonProtocol(server, inboxPath, "cuphead"); }
        static int JsonProtocol(string server, string inboxPath, string game)
        {
            int before = failures;
            Settings s = new Settings();
            s.Server = server;
            s.Tokens[game] = "c90gl_test";
            Link link = new Link(delegate { return s; }, game, game);
            using (Inbox inbox = new Inbox(inboxPath, "protocol-session")) {
                inbox.SetContext("attempt-1");
                link.UseInbox(inbox);
                if (game == "cuphead") { link.SetContext("attempt-1"); link.SetCapabilities(new[] { "heal", "ice_floor" }); }
                else { link.OnModLine("capabilities give_item,fall_void,chaos"); link.OnModLine("context attempt-1"); }
                link.Start();
                Thread.Sleep(1500);
                link.OnModLine("state 1");
                DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                string text = "";
                while (DateTime.UtcNow < limit) {
                    text = ReadShared(inboxPath);
                    if (text.Contains("e-42")) break;
                    Thread.Sleep(50);
                }
                Check(game == "cuphead" ? text.Contains("heal") && text.Contains("\"safe\":true") && text.Contains("attempt-1") : text.Contains("give_item") && text.Contains("missile"), game + " WebSocket command reaches JSON inbox with the expected safety fields");
                Check(text.Contains("protocol-session") && text.Contains("expiresMs"), "CTR command has its session and expiry");
                if (game == "cuphead" || game == "ctrnf") Check(text.Contains("\"noDeath\":false") && text.Contains("\"allowDestructive\":true"), "Cuphead welcome and per-command permissions reach the native bridge");
                link.OnModLine("ack e-42 ok");
                if (game == "cuphead" || game == "ctrnf") link.SetContext("attempt-2");
                Thread.Sleep(500);
                Check(!ReadShared(inboxPath).Contains("e-42"), game + " native acknowledgement clears the command");
                if (game == "cuphead" || game == "ctrnf") Check(ReadShared(inboxPath).Contains("\"noDeath\":true") && ReadShared(inboxPath).Contains("\"allowDestructive\":false"), "Cuphead permission revocation reaches a running bridge");
                link.Stop();
            }
            return failures - before;
        }

        public static int Protocol(string server, string inboxPath)
        {
            Settings s = new Settings();
            s.Server = server;
            s.Tokens["kof2002"] = "c90gl_test";
            Link link = new Link(delegate { return s; }, "kof2002", "KOF 2002");
            Inbox inbox = new Inbox(inboxPath);
            link.UseInbox(inbox);
            link.Start();
            Thread.Sleep(1500);
            link.OnModLine("state 1");
            DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            string text = "";
            while (DateTime.UtcNow < limit)
            {
                text = File.ReadAllText(inboxPath, Encoding.UTF8);
                if (text.Contains("\"e-42\"")) break;
                Thread.Sleep(200);
            }
            Check(text.Contains("effect = \"slow\"") && text.Contains("ms = 15000") && text.Contains("safe = true") && text.Contains("arg = \"HeavyMelee\""), "the effect reached the inbox");
            Check(text.Contains("by = " + Inbox.LuaString("Ana \u00f1\U0001F600")), "UTF-8 survives a split inside a multibyte character");
            link.OnModLine("ack e-42 escudo activo");
            Thread.Sleep(1500);
            Check(!File.ReadAllText(inboxPath, Encoding.UTF8).Contains("e-42"), "acked effects leave the inbox");
            link.Stop();
            return failures;
        }
    }
}
