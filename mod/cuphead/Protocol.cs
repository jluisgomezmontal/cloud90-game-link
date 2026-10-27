// Cloud90's local bridge. No game assemblies are required by the policy tests.
using System;
using System.Collections.Generic;

namespace Cloud90.Cuphead
{
    [Serializable] public sealed class Command
    {
        public string id, effect, arg, epoch;
        public int ms;
        public bool safe;
        public bool allowDestructive;
        public long expiresMs;
    }
    [Serializable] public sealed class InboxMessage
    {
        public string session;
        public long heartbeatMs;
        public bool paused;
        public bool noDeath = true, allowDestructive;
        public Command[] effects;
    }
    [Serializable] public sealed class Ack { public string id, result; }
    [Serializable] public sealed class Status
    {
        public string session, epoch, reason, character, context;
        public int pid, health, healthMax;
        public bool ready, inPlay, dlc, paused;
        public long heartbeatMs;
        public Ack[] acks;
        public string[] activeEffects;
        public string[] capabilities;
        public string bridgeVersion = "cloud90-cuphead-2";
    }
    [Serializable] public sealed class Launch { public string session; }

    public sealed class EffectState
    {
        readonly Dictionary<string, float> timers = new Dictionary<string, float>();
        public string Epoch { get; private set; }
        public bool Active(string effect) { return timers.ContainsKey(effect); }
        public void Remove(string effect) { timers.Remove(effect); }
        public void Clear() { timers.Clear(); }
        public string[] Names() { var names = new List<string>(timers.Keys); return names.ToArray(); }
        public void Context(string epoch) { if (Epoch != epoch) { Clear(); Epoch = epoch; } }
        public void Step(float dt)
        {
            foreach (string key in new List<string>(timers.Keys))
            {
                timers[key] -= Math.Max(0, dt);
                if (timers[key] <= 0) timers.Remove(key);
            }
        }
        public void Set(string effect, int milliseconds)
        {
            if (effect == "slow") timers.Remove("super_speed");
            if (effect == "super_speed") timers.Remove("slow");
            timers[effect] = Math.Max(0.1f, Math.Min(milliseconds, MaxMilliseconds(effect)) / 1000f);
        }
        public static string Reject(Command command, string epoch, long now)
        {
            if (command == null || string.IsNullOrEmpty(command.id) || command.id.Length > 80) return "comando invalido";
            if (command.epoch != epoch || string.IsNullOrEmpty(epoch)) return "intento anterior";
            if (command.expiresMs <= now || command.expiresMs - now > 10000) return "comando caducado";
            if (!ValidArgument(command.effect, command.arg)) return "argumento no admitido";
            if (command.ms < 0 || command.ms > (MaxMilliseconds(command.effect))) return "duracion invalida";
            return null;
        }
        public static int MaxMilliseconds(string effect)
        {
            switch (effect) {
                case "shield": return 60000;
                case "ice_floor": case "auto_jump": case "camera_zoom": return 8000;
                case "wind_push": return 5000;
                case "burn": return 6000;
                case "petrify": return 3000;
                case "weak_shots": return 10000;
                default: return 15000;
            }
        }
        public static bool Fresh(long heartbeat, long now) { return heartbeat > 0 && heartbeat <= now && now - heartbeat <= 3000; }
        public static bool ValidArgument(string effect, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return true;
            return effect == "wind_push" ? arg == "left" || arg == "right" : effect == "spawn_enemies" && (arg == "1" || arg == "2");
        }
        public static bool Destructive(string effect) { return effect == "kill" || effect == "restart_level"; }
        public static bool Troll(string effect)
        {
            return effect == "damage" || effect == "no_shoot" || effect == "no_dash" || effect == "no_jump" || effect == "invert_controls" || effect == "slow" ||
                effect == "ice_floor" || effect == "wind_push" || effect == "explosion" || effect == "burn" || Destructive(effect) ||
                effect == "spawn_enemies" || effect == "petrify" || effect == "super_drain" || effect == "auto_jump" || effect == "weak_shots" || effect == "camera_zoom";
        }
        public static string DestructiveRefusal(Command c, bool noDeath, bool permission, bool shield)
        {
            if (shield) return "escudo activo";
            if (c.safe || noDeath) return "modo sin morir";
            return c.allowDestructive && permission ? null : "acciones destructivas desactivadas";
        }
        public static float IceVelocity(float previous, float desired, float delta)
        {
            float step = Math.Max(0, delta) * (Math.Abs(desired) < 0.01f ? 350f : 700f);
            float distance = desired - previous;
            return Math.Abs(distance) <= step ? desired : previous + Math.Sign(distance) * step;
        }
        public static string HealRefusal(int health, int maximum)
        {
            if (health >= maximum) return "vida completa";
            // Native SetHealth(9) unlocks the DLC HP9 Steam achievement.
            if (health >= 8) return "curacion a 9 HP no compatible";
            return null;
        }
    }
}
