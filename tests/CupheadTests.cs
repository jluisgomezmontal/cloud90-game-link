using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Cloud90.Cuphead;

namespace Cloud90.GameLink
{
    public static class CupheadTests
    {
        static int failed;
        static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failed++; }
        public static int Run(string root)
        {
            var state = new EffectState(); state.Context("attempt-1"); state.Set("slow", 5000); state.Step(2);
            Check(state.Active("slow"), "effect remains until its gameplay duration expires");
            state.Set("super_speed", 5000); Check(!state.Active("slow") && state.Active("super_speed"), "speed effects replace rather than multiply");
            state.Context("attempt-2"); Check(!state.Active("super_speed"), "retry clears every effect");
            state.Set("shield", 30000); state.Step(20); Check(state.Active("shield"), "shield supports the catalog duration");
            state.Clear(); Check(!state.Active("shield"), "panic/disconnect clears effects");
            var c = new Command { id = "gift-1", effect = "heal", epoch = "old", expiresMs = 10000 };
            Check(EffectState.Reject(c, "new", 5000) == "intento anterior", "old attempt cannot affect a new player");
            c.epoch = "new"; c.expiresMs = 4999; Check(EffectState.Reject(c, "new", 5000) == "comando caducado", "expired effects rejected");
            c.expiresMs = 9000; c.ms = 16000; Check(EffectState.Reject(c, "new", 5000) == "duracion invalida", "duration bounded");
            Check(!EffectState.Fresh(1000, 4001) && !EffectState.Fresh(5000, 4000) && EffectState.Fresh(1000, 3000), "heartbeat fails closed after timeout and for future clocks");
            Check(EffectState.HealRefusal(3, 4) == null && EffectState.HealRefusal(4, 4) == "vida completa", "healing respects Ms. Chalice's actual maximum");
            Check(EffectState.HealRefusal(8, 9) != null && EffectState.HealRefusal(7, 9) == null, "Cloud90 healing cannot trigger the native nine-HP achievement");
            Check(EffectState.ValidArgument("spawn_enemies", "2") && !EffectState.ValidArgument("spawn_enemies", "100") && !EffectState.ValidArgument("kill", "yes"), "effect arguments restrict spawn counts and destructive commands");
            var lethal = new Command { effect="kill",safe=false,allowDestructive=true };
            Check(EffectState.DestructiveRefusal(lethal,false,true,false) == null && EffectState.DestructiveRefusal(lethal,true,true,false) != null && EffectState.DestructiveRefusal(lethal,false,false,false) != null && EffectState.DestructiveRefusal(lethal,false,true,true) != null, "destructive actions require current permission, no-death off and no shield");
            lethal.allowDestructive=false;
            Check(EffectState.DestructiveRefusal(lethal,false,true,false) != null, "old commands cannot acquire a newly enabled permission");
            Check(EffectState.IceVelocity(300,0,0.1f) == 265 && EffectState.IceVelocity(300,-300,0.1f) == 230 && EffectState.IceVelocity(0,300,100) == 300, "ice brakes gradually, reverses gradually and never overshoots");
            state.Set("ice_floor",100000); state.Step(8.1f);
            Check(!state.Active("ice_floor"), "ice duration cannot exceed eight seconds");
            Directory.CreateDirectory(root);
            bool traversal = false; try { CupheadInstaller.SafePath(root, "../outside"); } catch (InvalidDataException) { traversal = true; }
            Check(traversal, "installer rejects traversal");
            string original = Path.Combine(root, "other-mod.dll"), own = Path.Combine(root, "cloud90.dll");
            File.WriteAllText(original, "pre-existing mod"); File.WriteAllText(own, "owned");
            var json = new JavaScriptSerializer();
            File.WriteAllText(Path.Combine(root, "cloud90-cuphead-install.json"), json.Serialize(new Dictionary<string,string> { { "cloud90.dll", CupheadInstaller.HashFile(own) } }));
            CupheadInstaller.RemoveOwned(root);
            Check(File.Exists(original) && !File.Exists(own), "uninstall removes only owned files and preserves existing mods");
            File.WriteAllText(own, "modified after installation");
            File.WriteAllText(Path.Combine(root, "cloud90-cuphead-install.json"), json.Serialize(new Dictionary<string,string> { { "cloud90.dll", CupheadInstaller.Hash(new byte[] { 1 }) } }));
            bool conflict = false;
            try { CupheadInstaller.RemoveOwned(root); } catch (IOException) { conflict = true; }
            Check(conflict && File.Exists(own) && File.Exists(original), "uninstall aborts without deleting modified or unrelated files");
            string shared = Path.Combine(root, "shared");
            string ownPlugin = Path.Combine(shared, "BepInEx", "plugins", "Cloud90", "Cloud90.Cuphead.dll");
            string otherPlugin = Path.Combine(shared, "BepInEx", "plugins", "Other.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(ownPlugin));
            File.WriteAllText(ownPlugin, "cloud90"); File.WriteAllText(otherPlugin, "another mod");
            string loader = Path.Combine(shared, "winhttp.dll"); File.WriteAllText(loader, "loader");
            File.WriteAllText(Path.Combine(shared, "cloud90-cuphead-install.json"), json.Serialize(new Dictionary<string,string> {
                { "winhttp.dll", CupheadInstaller.HashFile(loader) },
                { "BepInEx/plugins/Cloud90/Cloud90.Cuphead.dll", CupheadInstaller.HashFile(ownPlugin) }
            }));
            CupheadInstaller.RemoveOwned(shared);
            Check(!File.Exists(ownPlugin) && File.Exists(otherPlugin) && File.Exists(loader), "uninstall retains the shared loader required by other plugins");
            string path = Path.Combine(root, "inbox.json");
            using (var inbox = new Inbox(path, "session"))
            {
                inbox.SetContext("first"); inbox.Connected(true);
                inbox.Add(new Inbox.Item { Id = "first-command", Effect = "damage", Safe = true, Sent = DateTime.UtcNow });
                inbox.SetContext("second");
                string text = File.ReadAllText(path);
                Check(text.Contains("\"safe\":true") && text.Contains("\"epoch\":\"first\""), "JSON preserves no-death and the command's original attempt");
                inbox.Add(new Inbox.Item { Id = "late-command", Effect = "heal", Epoch = "first", Sent = DateTime.UtcNow });
                var late = Array.Find(json.Deserialize<InboxMessage>(File.ReadAllText(path)).effects, delegate(Command command) { return command.id == "late-command"; });
                Check(late != null && late.epoch == "first", "an in-flight remote command cannot acquire the newer local attempt");
                Check(!inbox.Add(new Inbox.Item { Id = "first-command", Effect = "damage", Sent = DateTime.UtcNow }), "duplicate command is not queued twice");
            }
            var settings = new Settings { CupheadDir = root }; settings.Tokens["cuphead"] = "c90gl_test"; settings.Save();
            var loaded = Settings.Load(); Check(loaded.CupheadDir == root && loaded.TokenFor("cuphead") == "c90gl_test", "Cuphead folder and sealed token survive restart");
            return failed;
        }
    }
}
