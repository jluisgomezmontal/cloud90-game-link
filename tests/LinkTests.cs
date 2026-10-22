// Checks for the Link's own parts, compiled with its sources by
// tests/link-test.ps1 (no Hades needed).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Cloud90.HadesLink
{
    public static class LinkTests
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (!ok) { failures++; Console.WriteLine("FAIL: " + what); }
        }

        public const string HostileName = "Ana\" } os.exit() --\n]]ñ\U0001F600";

        public static string HostileNameBase64 { get { return Convert.ToBase64String(Encoding.UTF8.GetBytes(HostileName)); } }

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

        // Against tests/fake-server.js: connects, says hello, gets an effect
        // into the inbox, and answers it as the mod would.
        public static int Protocol(string server, string inboxPath)
        {
            Settings s = new Settings();
            s.Server = server;
            s.Token = "c90gl_test";
            Link link = new Link(delegate { return s; });
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
            Check(text.Contains("effect = \"slow\"") && text.Contains("ms = 15000") && text.Contains("safe = true"), "the effect reached the inbox");
            link.OnModLine("ack e-42 escudo activo");
            Thread.Sleep(1500);
            Check(!File.ReadAllText(inboxPath, Encoding.UTF8).Contains("e-42"), "acked effects leave the inbox");
            return failures;
        }
    }
}
