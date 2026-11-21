using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Cloud90.GameLink
{
    internal sealed class Tlou2GiftPipe : IDisposable
    {
        internal const string Version = "cloud90-game-link-tlou2-3";
        internal const string OptionPrefix = "enemy_ambush:";
        internal const string AllyOptionPrefix = "ally_support:";
        readonly Random spawnDistance = new Random();

        sealed class Preset
        {
            internal readonly string Name, Faction;
            internal Preset(string name, string faction) { Name = name; Faction = faction; }
        }

        static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>
        {
            { "runner", new Preset("Runner", "infected") },
            { "clicker", new Preset("Clicker", "infected") },
            { "clicker_armored", new Preset("Clicker (armored)", "infected") },
            { "stalker", new Preset("Stalker", "infected") },
            { "shambler", new Preset("Shambler", "infected") },
            { "bloater", new Preset("Bloater", "infected") },
            { "crawler", new Preset("Crawler", "infected") },
            { "wlf_rifle", new Preset("WLF soldier - rifle", "wlf") },
            { "wlf_pistol", new Preset("WLF soldier - pistol", "wlf") },
            { "wlf_machete", new Preset("WLF soldier - machete", "wlf") },
            { "seraphite_bow", new Preset("Seraphite - bow", "seraphite") },
            { "seraphite_hammer", new Preset("Seraphite - hammer", "seraphite") },
            { "seraphite_brute", new Preset("Seraphite brute - sledgehammer", "seraphite") },
            { "rattler_shotgun", new Preset("Rattler - shotgun (helmet)", "rattler") },
            { "rattler_rifle", new Preset("Rattler - rifle (armor vest)", "rattler") },
            { "rattler_axe", new Preset("Rattler - axe", "rattler") },
            { "wlf_dog", new Preset("WLF dog", "wlf") },
            { "rat_king", new Preset("Rat King", "infected") },
            { "rat_king_stalker", new Preset("Rat King stalker", "infected") },
            { "rat_king_bloater", new Preset("Bloater (Rat King)", "infected") },
            { "enemy_abby", new Preset("Enemy - Abby (WLF)", "wlf") },
            { "enemy_ellie", new Preset("Enemy - Ellie (WLF)", "wlf") },
            { "seraphite_brute_boss", new Preset("Seraphite brute - boss", "seraphite") },
            { "seraphite_archer_boss", new Preset("Seraphite archer - boss", "seraphite") },
            { "ally_joel", new Preset("Ally - Joel", "ally") },
            { "ally_lev", new Preset("Ally - Lev", "ally") },
            { "ally_tommy", new Preset("Ally - Tommy", "ally") },
            { "ally_dina", new Preset("Ally - Dina", "ally") },
            { "ally_jesse", new Preset("Ally - Jesse", "ally") },
            { "ally_jerry", new Preset("Ally - Jerry", "ally") },
            { "ally_yara", new Preset("Ally - Yara", "ally") },
            { "ally_manny", new Preset("Ally - Manny", "ally") },
            { "ally_mel", new Preset("Ally - Mel", "ally") },
            { "ally_nora", new Preset("Ally - Nora", "ally") },
            { "ally_owen", new Preset("Ally - Owen", "ally") },
            { "ally_abby", new Preset("Ally - Abby", "ally") },
            { "ally_ellie", new Preset("Ally - Ellie", "ally") },
            { "ally_alice", new Preset("Ally - dog (Alice)", "ally") },
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint processId);

        // Gift Spawner 1.2.0 pushes state on changes and every 10 s while idle.
        const long StateTimeoutMs = 12000;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        NamedPipeClientStream pipe;
        StreamReader reader;
        StreamWriter writer;
        Task<string> nextLine;
        Inbox.Item pending;
        string pendingPreset;
        readonly HashSet<string> availableOptions = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> sentIds = new HashSet<string>(StringComparer.Ordinal);
        readonly Queue<string> recentIds = new Queue<string>();
        int sequence, pendingSequence, connection;
        long lastState, lastMessage, nextPing, pendingUntil, nextSpawn;
        bool welcomed, gameReady, canSpawn;

        public string Epoch { get; private set; }

        internal string[] Capabilities()
        {
            var enabled = availableOptions.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var result = new List<string>();
            if (canSpawn && availableOptions.Contains("runner")) result.Add("spawn_runner");
            if (enabled.Length != 0)
            {
                if (enabled.Any(id => Presets[id].Faction != "ally")) result.Add("enemy_ambush");
                if (canSpawn && pending == null) result.Add("enemy_ambush_ready");
                result.AddRange(enabled.Select(id =>
                    (Presets[id].Faction == "ally" ? AllyOptionPrefix : OptionPrefix) + id));
            }
            return result.ToArray();
        }

        public bool Poll(int gamePid, string session, Inbox commands, Link link)
        {
            try
            {
                if (pipe == null && !Connect(gamePid)) return false;
                for (int i = 0; i < 32 && nextLine.IsCompleted; i++)
                {
                    string line = nextLine.GetAwaiter().GetResult();
                    if (line == null) throw new IOException("Gift Spawner cerró el canal.");
                    ReadMessage(json.Deserialize<Dictionary<string, object>>(line), link);
                    lastMessage = clock.ElapsedMilliseconds;
                    nextLine = reader.ReadLineAsync();
                }

                long now = clock.ElapsedMilliseconds;
                if (now - lastMessage >= 7000) throw new IOException("Gift Spawner dejó de responder.");
                if (now >= nextPing)
                {
                    Send(new { t = "ping", seq = ++sequence });
                    nextPing = now + 2000;
                }
                if (pending != null && now >= pendingUntil)
                    Complete(link, "resultado incierto; el juego no confirmó la aparición");
                bool inPlay = CampaignReady(now);
                if (pending != null && (!inPlay || link.Paused || !commands.LocalConnected))
                    Complete(link, "resultado incierto; invocación interrumpida");
                if (!inPlay)
                {
                    Epoch = "";
                    return false;
                }
                if (string.IsNullOrEmpty(Epoch)) Epoch = session + "_gift_" + (++connection);
                if (!canSpawn || pending != null || link.Paused) return true;

                foreach (var item in commands.LocalPending())
                {
                    var remaining = 8000 - (DateTime.UtcNow - item.Sent).TotalMilliseconds;
                    string option = item.Effect == "spawn_runner" ? "runner" :
                        (item.Effect == "enemy_ambush" || item.Effect == "ally_support") ? item.Arg : "";
                    if (link.Paused || !commands.LocalConnected) break;
                    string reason = RejectionReason(item, option, now, remaining, commands.LocalNoDeath);
                    if (reason != null)
                    {
                        link.OnModLine("ack " + item.Id + " " + reason);
                        continue;
                    }

                    pending = item;
                    pendingPreset = Presets[option].Name;
                    pendingSequence = ++sequence;
                    pendingUntil = now + (long)remaining;
                    if (item.Effect == "spawn_runner" && !(item.IsTest && link.ImmediateTlou2Preview))
                        nextSpawn = now + 30000;
                    sentIds.Add(item.Id);
                    recentIds.Enqueue(item.Id);
                    if (recentIds.Count > 200) sentIds.Remove(recentIds.Dequeue());
                    string viewer = string.IsNullOrWhiteSpace(item.By) ? "Cloud90" : item.By.Trim();
                    Send(new { t = "spawn", seq = pendingSequence, presets = new[] { pendingPreset },
                        count = 1, distance = spawnDistance.Next(5, 11), ttlMs = (int)Math.Min(5000, remaining),
                        source = item.IsTest ? "test" : "gift", test = item.IsTest,
                        viewer = viewer, label = item.IsTest ? "Cloud90" : viewer });
                    break;
                }
                return inPlay;
            }
            catch (Exception)
            {
                if (pending != null) Complete(link, "resultado incierto; se perdió la conexión con Gift Spawner");
                Dispose();
                throw;
            }
        }

        string RejectionReason(Inbox.Item item, string option, long now, double remaining, bool noDeath)
        {
            if (sentIds.Contains(item.Id)) return "la orden ya se envió";
            if (item.Epoch != Epoch) return "la partida cambió antes de invocar";
            if (remaining < 1000) return "la orden de invocación venció";
            if (item.Effect != "ally_support" && (item.Safe || noDeath)) return "la protección Sin morir está activa";
            if (!availableOptions.Contains(option) ||
                (Presets[option].Faction == "ally") != (item.Effect == "ally_support"))
                return "Gift Spawner no ofrece esta invocación";
            if (item.Effect == "spawn_runner" && now < nextSpawn)
                return "espera " + ((nextSpawn - now + 999) / 1000) + " s entre invocaciones";
            return null;
        }

        bool Connect(int gamePid)
        {
            pipe = new NamedPipeClientStream(".", "TLOU2GiftSpawner", PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            try { pipe.Connect(40); }
            catch (TimeoutException) { Dispose(); return false; }
            uint serverPid;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverPid) || serverPid != (uint)gamePid)
                throw new IOException("El canal de Gift Spawner no pertenece al juego seleccionado.");
            var utf8 = new UTF8Encoding(false);
            reader = new StreamReader(pipe, utf8, false, 1024, true);
            writer = new StreamWriter(pipe, utf8, 1024, true) { AutoFlush = true, NewLine = "\n" };
            lastMessage = clock.ElapsedMilliseconds;
            nextPing = lastMessage + 2000;
            lastState = -10000;
            Send(new { t = "hello", seq = ++sequence, protocol = 1, bridge = "Cloud90 Game Link", version = "1.0" });
            nextLine = reader.ReadLineAsync();
            return true;
        }

        void ReadMessage(Dictionary<string, object> message, Link link)
        {
            switch (Value(message, "t") as string)
            {
                case "welcome":
                    if (!Equals(Value(message, "protocol"), 1) ||
                        !Equals(Value(message, "mod"), "1.2.0") ||
                        !Equals(Value(message, "build"), "t2r-pc-1.6.0"))
                        throw new IOException("Versión de Gift Spawner incompatible con esta vista previa.");
                    welcomed = true;
                    availableOptions.Clear();
                    var advertised = Value(message, "presets") as System.Collections.IEnumerable;
                    if (advertised != null)
                        foreach (var entry in advertised.OfType<Dictionary<string, object>>())
                            foreach (var option in Presets)
                                if (Equals(Value(entry, "id"), option.Value.Name) &&
                                    Equals(Value(entry, "faction"), option.Value.Faction))
                                    availableOptions.Add(option.Key);
                    break;
                case "state":
                    var cap = Value(message, "aliveCap") as int?;
                    int alive = (Value(message, "alive") as int?) ?? -1;
                    gameReady = Equals(Value(message, "gameReady"), true);
                    if (!gameReady)
                    {
                        if (pending != null) Complete(link, "resultado incierto; cambió la partida");
                        Epoch = "";
                    }
                    canSpawn = gameReady &&
                        !Equals(Value(message, "panic"), true) &&
                        !Equals(Value(message, "spawnsEnabled"), false) &&
                        Equals(Value(message, "queued"), 0) && cap.HasValue &&
                        alive >= 0 && alive < cap.Value;
                    lastState = clock.ElapsedMilliseconds;
                    break;
                case "spawned":
                    if (Matches(message))
                        Complete(link, !Equals(Value(message, "preset"), pendingPreset)
                            ? "resultado incierto; el juego informó otro enemigo"
                            : Equals(Value(message, "result"), "ok") ? "ok" : "el juego rechazó la aparición");
                    break;
                case "ack":
                    if (Matches(message) && Equals(Value(message, "accepted"), 0))
                        Complete(link, "Gift Spawner rechazó la invocación");
                    break;
                case "error":
                    if (Matches(message)) Complete(link, "Gift Spawner devolvió un error");
                    break;
            }
        }

        internal bool CampaignReady(long now) { return welcomed && gameReady && now - lastState <= StateTimeoutMs; }

        bool Matches(Dictionary<string, object> message)
        {
            return pending != null && Equals(Value(message, "ref"), pendingSequence);
        }

        static object Value(Dictionary<string, object> message, string key)
        {
            object value;
            return message.TryGetValue(key, out value) ? value : null;
        }

        void Complete(Link link, string result)
        {
            var item = pending;
            pending = null;
            pendingPreset = null;
            link.OnModLine("ack " + item.Id + " " + result);
        }

        void Send(object message) { writer.WriteLine(json.Serialize(message)); }

        public void Dispose()
        {
            if (pipe != null) pipe.Dispose();
            if (nextLine != null)
                nextLine.ContinueWith(task => { var ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
            if (reader != null) reader.Dispose();
            pipe = null;
            reader = null;
            writer = null;
            nextLine = null;
            pending = null;
            pendingPreset = null;
            availableOptions.Clear();
            welcomed = gameReady = canSpawn = false;
            Epoch = "";
        }
    }
}
