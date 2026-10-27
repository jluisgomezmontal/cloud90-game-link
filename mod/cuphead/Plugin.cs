using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;

using UnityEngine;

namespace Cloud90.Cuphead
{
    [BepInPlugin("live.cloud90.cuphead", "Cloud90 Cuphead", "1.1.0")]
    [DefaultExecutionOrder(10000)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        readonly EffectState effects = new EffectState();
        readonly List<Ack> acks = new List<Ack>();
        readonly Dictionary<string, Ack> seen = new Dictionary<string, Ack>();
        readonly Queue<string> seenOrder = new Queue<string>();
        readonly string boot = Guid.NewGuid().ToString("N");
        string session, folder, epoch = "", reason = "juego no disponible";
        AbstractPlayerController player;
        Level level;
        int generation;
        float nextPoll;
        bool connected, localPaused, wasPlayable;
        Harmony harmony;
        bool previousBackground;
        long lastHeartbeat, lastStatusWrite;
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        static long Now { get { return (long)GetTickCount64(); } }
        static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        public bool Live { get { return connected && !localPaused && IsPlayable(); } }
        public bool Has(string effect) { return Live && effects.Active(effect); }

        void Awake()
        {
            Instance = this;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            harmony = new Harmony("live.cloud90.cuphead");
            try { harmony.PatchAll(typeof(Plugin).Assembly); Logger.LogInfo("Cloud90 Cuphead bridge 2 ready"); }
            catch (Exception e) { Logger.LogError("Unsupported game hooks: " + e); enabled = false; harmony.UnpatchSelf(); }
        }
        bool IsPlayable()
        {
            return level != null && player != null && level.Started && !level.Ending &&
                player.levelStarted && !player.levelEnded && !player.IsDead &&
                !PlayerManager.Multiplayer && PauseManager.state != PauseManager.State.Paused &&
                !Level.IsChessBoss && !Level.IsGraveyard && !Level.IsInBossesHub &&
                (level.LevelType == Level.Type.Battle || level.LevelType == Level.Type.Platforming);
        }
        void Update()
        {
            try
            {
                RestoreZoom();
                Level currentLevel = Level.Current;
                AbstractPlayerController currentPlayer = currentLevel == null || !PlayerManager.DoesPlayerExist(PlayerId.PlayerOne)
                    ? null : PlayerManager.GetPlayer(PlayerId.PlayerOne);
                if (currentLevel != level || currentPlayer != player)
                {
                    level = currentLevel; player = currentPlayer;
                    epoch = boot + "-" + (++generation); ClearEffects(); effects.Context(epoch);
                }
                bool playable = IsPlayable();
                if (!playable && PauseManager.state != PauseManager.State.Paused) ClearEffects();
                if (wasPlayable && !playable && (player == null || player.IsDead || level == null || level.Ending))
                { epoch = boot + "-" + (++generation); ClearEffects(); effects.Context(epoch); }
                wasPlayable = playable;
                if (Input.GetKeyDown(KeyCode.F9)) { localPaused = !localPaused; ClearEffects(); }
                if (Live) effects.Step(Time.unscaledDeltaTime);
                TickTrolls(Time.unscaledDeltaTime);
                if (Time.realtimeSinceStartup >= nextPoll)
                {
                    nextPoll = Time.realtimeSinceStartup + 0.1f;
                    Poll(); WriteStatus();
                }
            }
            catch (Exception e) { connected = false; ClearEffects(); Logger.LogWarning("Bridge stopped: " + e.Message); }
        }
        void Bind()
        {
            string root = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90GameLink"), "cuphead");
            string active = Path.Combine(root, "active.json");
            if (!File.Exists(active) || new FileInfo(active).Length > 1024) return;
            Launch launch = BridgeJson.Read<Launch>(ReadShared(active));
            if (launch == null || !Regex.IsMatch(launch.session ?? "", "^[a-f0-9]{32}$")) return;
            string candidate = Path.Combine(root, launch.session);
            string inboxPath = Path.Combine(candidate, "inbox.json");
            if (!File.Exists(inboxPath)) return;
            InboxMessage inbox = BridgeJson.Read<InboxMessage>(ReadShared(inboxPath));
            if (inbox == null || inbox.session != launch.session || !EffectState.Fresh(inbox.heartbeatMs, Now)) return;
            session = launch.session; folder = candidate; ClearEffects(); seen.Clear(); seenOrder.Clear(); acks.Clear();
            Logger.LogInfo("Bound local session " + session);
        }
        void Poll()
        {
            if (session == null) Bind();
            if (session == null) return;
            string path = Path.Combine(folder, "inbox.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) { connected = false; ClearEffects(); return; }
            InboxMessage inbox;
            try { inbox = BridgeJson.Read<InboxMessage>(ReadShared(path)); }
            catch (IOException) { if (!EffectState.Fresh(lastHeartbeat, Now)) { connected = false; ClearEffects(); } return; }
            catch { connected = false; ClearEffects(); return; }
            if (inbox != null && inbox.session == session) { lastHeartbeat = inbox.heartbeatMs; liveNoDeath = inbox.noDeath; liveDestructive = inbox.allowDestructive; }
            connected = inbox != null && inbox.session == session && EffectState.Fresh(inbox.heartbeatMs, Now) && !inbox.paused;
            if (!connected) ClearEffects();
            if (inbox == null || !EffectState.Fresh(inbox.heartbeatMs, Now))
            {
                // A restarted Game Link creates a new session while Cuphead can remain open.
                Bind();
                return;
            }
            reason = PlayerManager.Multiplayer ? "cooperativo aun no compatible" :
                PauseManager.state == PauseManager.State.Paused ? "juego en pausa" :
                !IsPlayable() ? "fuera de combate compatible" : localPaused ? "panico F9 activo" :
                !connected ? "esperando conexion de Game Link" : "listo";
            if (inbox == null || inbox.session != session || inbox.effects == null) return;
            if (inbox.effects.Length > 20) return;
            foreach (Command command in inbox.effects)
            {
                if (command == null || string.IsNullOrEmpty(command.id)) continue;
                if (seen.ContainsKey(command.id)) continue;
                string result = EffectState.Reject(command, epoch, Now);
                if (result == null) {
                    try { result = Live ? Apply(command) : reason; }
                    catch (Exception e) { ClearEffects(); result = "fallo al aplicar efecto"; Logger.LogWarning(e); }
                }
                Ack ack = new Ack { id = command.id, result = result ?? "ok" };
                seen.Add(command.id, ack); seenOrder.Enqueue(command.id); if (result != Deferred) acks.Add(ack);
                while (seenOrder.Count > 2000) seen.Remove(seenOrder.Dequeue());
                while (acks.Count > 128) acks.RemoveAt(0);
                Logger.LogInfo("Effect " + command.effect + " " + command.id + ": " + ack.result);
            }
        }
        string Apply(Command c)
        {
            if (effects.Active("shield") && EffectState.Troll(c.effect)) return "escudo activo";
            PlayerStatsManager stats = player.stats;
            switch (c.effect)
            {
                case "heal":
                    string healingRefusal = EffectState.HealRefusal(stats.Health, stats.HealthMax);
                    if (healingRefusal != null) return healingRefusal;
                    stats.SetHealth(Math.Min(stats.HealthMax, stats.Health + 1)); return null;
                case "damage":
                    return Hurt(c.safe);
                case "super_add":
                    if (!stats.CanGainSuperMeter || stats.SuperMeter >= stats.SuperMeterMax) return "energia no disponible";
                    stats.AddEx(); return null;
                case "no_dash": case "no_jump":
                    if (!(player is LevelPlayerController)) return "efecto solo terrestre";
                    effects.Set(c.effect, c.ms); return null;
                case "invincible": case "no_shoot": case "invert_controls": case "slow": case "super_speed": case "shield":
                    effects.Set(c.effect, c.ms); return null;
                default: return ApplyTroll(c);
            }
        }
        void WriteStatus()
        {
            if (session == null) return;
            Status status = new Status {
                session = session, epoch = epoch, pid = Process.GetCurrentProcess().Id, ready = true,
                heartbeatMs = Now, inPlay = Live, paused = localPaused, reason = reason,
                dlc = DLCManager.DLCEnabled(),
                health = player == null ? 0 : player.stats.Health,
                healthMax = player == null ? 0 : player.stats.HealthMax,
                character = player == null ? "none" : player.stats.isChalice ? "Ms. Chalice" : "Cuphead/Mugman",
                context = level == null ? "menu" : level.CurrentLevel.ToString(), acks = acks.ToArray(), activeEffects = effects.Names(), capabilities = Capabilities()
            };
            string target = Path.Combine(folder, "status.json"), temp = target + ".tmp";
            try
            {
                File.WriteAllText(temp, BridgeJson.Write(status));
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                lastStatusWrite = Now;
            }
            catch (IOException) { if (!EffectState.Fresh(lastStatusWrite, Now)) { connected = false; ClearEffects(); } }
        }
        void OnDestroy() { connected = false; ClearEffects(); Application.runInBackground = previousBackground; if (harmony != null) harmony.UnpatchSelf(); if (Instance == this) Instance = null; }
        public float SpeedFactor { get { return Has("slow") ? 0.5f : Has("super_speed") ? 1.5f : 1f; } }
        public static bool BlockButton(Rewired.Player input, int action)
        {
            Plugin p = Instance;
            return p != null && p.Live && p.player.input.actions == input &&
                ((action == 3 && p.Has("no_shoot")) || (action == 7 && p.Has("no_dash")) || (action == 2 && p.Has("no_jump")));
        }
        public static bool IsTarget(Component component) { return Instance != null && Instance.Live && Instance.player != null && component.gameObject == Instance.player.gameObject; }
    }
    [HarmonyPatch(typeof(Rewired.Player), "GetButton", new Type[] { typeof(int) })]
    static class HeldPatch { static bool Prefix(Rewired.Player __instance, int actionId, ref bool __result) { if (!Plugin.BlockButton(__instance, actionId)) return true; __result = false; return false; } }
    [HarmonyPatch(typeof(Rewired.Player), "GetButtonDown", new Type[] { typeof(int) })]
    static class DownPatch
    {
        static bool Prefix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (Plugin.BlockButton(__instance, actionId)) { __result = false; return false; }
            if (Plugin.Instance != null && Plugin.Instance.AutoJump(__instance, actionId)) { __result = true; return false; }
            return true;
        }
    }
    // Plane CheckBasic also resumes firing from held duration, bypassing GetButtonDown.
    [HarmonyPatch(typeof(Rewired.Player), "GetButtonTimePressed", new Type[] { typeof(int) })]
    static class HeldTimePatch { static bool Prefix(Rewired.Player __instance, int actionId, ref float __result) { if (!Plugin.BlockButton(__instance, actionId)) return true; __result = 0f; return false; } }
    [HarmonyPatch(typeof(Rewired.Player), "GetAxis", new Type[] { typeof(int) })]
    static class AxisPatch { static void Postfix(Rewired.Player __instance, int actionId, ref float __result) { var p = Plugin.Instance; if (p != null && p.Live && actionId <= 1 && PlayerManager.GetFirst().input.actions == __instance && p.Has("invert_controls")) __result = -__result; } }
    [HarmonyPatch(typeof(PlayerDamageReceiver), "TakeDamage")]
    static class DamagePatch { static bool Prefix(PlayerDamageReceiver __instance) { return !Plugin.IsTarget(__instance) || !Plugin.Instance.Has("invincible"); } }
    [HarmonyPatch(typeof(LevelPlayerMotor), "HandleWalking")]
    static class SpeedPatch { static void Postfix(LevelPlayerMotor __instance, LevelPlayerMotor.VelocityManager ___velocityManager) { if (Plugin.IsTarget(__instance)) Plugin.Instance.GroundMovement(__instance, ___velocityManager); } }
    [HarmonyPatch(typeof(PlanePlayerMotor), "Move")]
    static class PlaneSpeedPatch
    {
        static void Prefix(PlanePlayerMotor __instance, out float[] __state)
        {
            __state = null;
            if (!Plugin.IsTarget(__instance)) return;
            __state = new[] { __instance.properties.speed, __instance.properties.shrunkSpeed };
            __instance.properties.speed *= Plugin.Instance.SpeedFactor;
            __instance.properties.shrunkSpeed *= Plugin.Instance.SpeedFactor;
        }
        static void Finalizer(PlanePlayerMotor __instance, float[] __state) { if (__state != null) { __instance.properties.speed = __state[0]; __instance.properties.shrunkSpeed = __state[1]; } }
    }
}
