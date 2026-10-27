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
        public long expiresMs;
    }
    [Serializable] public sealed class InboxMessage
    {
        public string session;
        public long heartbeatMs;
        public bool paused;
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
        public string bridgeVersion = "cloud90-cuphead-1";
    }
    [Serializable] public sealed class Launch { public string session; }

    public sealed class EffectState
    {
        readonly Dictionary<string, float> timers = new Dictionary<string, float>();
        public string Epoch { get; private set; }
        public bool Active(string effect) { return timers.ContainsKey(effect); }
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
            timers[effect] = Math.Max(0.1f, Math.Min(milliseconds, effect == "shield" ? 60000 : 15000) / 1000f);
        }
        public static string Reject(Command command, string epoch, long now)
        {
            if (command == null || string.IsNullOrEmpty(command.id) || command.id.Length > 80) return "comando invalido";
            if (command.epoch != epoch || string.IsNullOrEmpty(epoch)) return "intento anterior";
            if (command.expiresMs <= now || command.expiresMs - now > 10000) return "comando caducado";
            if (!string.IsNullOrEmpty(command.arg)) return "argumento no admitido";
            if (command.ms < 0 || command.ms > (command.effect == "shield" ? 60000 : 15000)) return "duracion invalida";
            return null;
        }
        public static bool Fresh(long heartbeat, long now) { return heartbeat > 0 && heartbeat <= now && now - heartbeat <= 3000; }
        public static string HealRefusal(int health, int maximum)
        {
            if (health >= maximum) return "vida completa";
            // Native SetHealth(9) unlocks the DLC HP9 Steam achievement.
            if (health >= 8) return "curacion a 9 HP no compatible";
            return null;
        }
    }
}
