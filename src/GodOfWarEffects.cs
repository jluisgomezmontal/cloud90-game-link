using System;
using System.Collections.Generic;

namespace Cloud90.GameLink
{
    // Shared policy/math; UpgradeTargets below belongs only to GoW I USA.
    // Per-game resource addresses belong to GodOfWarProfile.
    internal static class GodOfWarEffects
    {
        internal static readonly string[] Capabilities = {
            "heal", "damage", "heal_max", "kill", "magic_add", "magic_remove",
            "magic_max", "magic_empty", "rage_max", "rage_empty", "blades_min",
            "blades_max", "levels_min", "levels_max", "shield", "orbs_add", "orbs_remove", "amulet_max"
        };
        internal static bool Troll(string id) {
            return id=="damage"||id=="kill"||id=="magic_remove"||id=="magic_empty"||id=="rage_empty"||id=="blades_min"||id=="levels_min"||id=="orbs_remove";
        }
        internal static bool Destructive(string id) {
            return id=="kill"||id=="blades_min"||id=="blades_max"||id=="levels_min"||id=="levels_max"||id=="orbs_add"||id=="orbs_remove";
        }
        internal static uint OrbsTarget(string effect,uint current,uint maximum) {
            if(current>maximum||maximum>999999)throw new ArgumentOutOfRangeException("current");
            if(effect=="orbs_add")return Math.Min(maximum,current+500);
            if(effect=="orbs_remove")return current>500?current-500:0;
            throw new ArgumentException("effect");
        }
        internal static string Policy(string effect,bool noDeath,bool destructive,bool shield) {
            if(Array.IndexOf(Capabilities,effect)<0)return "efecto no compatible";
            if(effect=="kill"&&noDeath)return "Modo sin morir activo";
            if(Destructive(effect)&&!destructive)return "habilita efectos destructivos";
            if(Troll(effect)&&shield)return "escudo activo";
            return null;
        }
        internal static float HealthTarget(string effect,float current,float maximum) {
            if(!GodOfWarHost.ValidMeter(current,maximum))throw new InvalidOperationException("vida no disponible");
            if(effect=="kill")return 0;
            if(effect=="heal_max")return maximum;
            if(effect=="heal")return Math.Min(maximum,current+maximum*0.25f);
            if(effect=="damage")return Math.Max(1,current-maximum*0.15f);
            throw new ArgumentException("effect");
        }
        internal static float MagicTarget(string effect,float current,float maximum) {
            if(float.IsNaN(current)||float.IsInfinity(current)||current<0||current>maximum||float.IsNaN(maximum)||maximum<100||maximum>200)throw new InvalidOperationException("magia no disponible");
            if(effect=="magic_max")return maximum;
            if(effect=="magic_empty")return 0;
            if(effect=="magic_add")return Math.Min(maximum,current+maximum*0.25f);
            if(effect=="magic_remove")return Math.Max(0,current-maximum*0.25f);
            throw new ArgumentException("effect");
        }
        internal static KeyValuePair<uint,uint>[] UpgradeTargets(string effect) {
            bool maximum=effect.EndsWith("_max",StringComparison.Ordinal);
            var result=new List<KeyValuePair<uint,uint>>();
            if(effect=="levels_max"||effect=="levels_min") {
                // Published raw level codes: five powers, then Blades of Chaos.
                for(uint address=0x0076D7F4;address<=0x0076D804;address+=4)
                    result.Add(new KeyValuePair<uint,uint>(address,maximum?2u:0u));
            } else if(effect!="blades_min"&&effect!="blades_max")throw new ArgumentException("effect");
            result.Add(new KeyValuePair<uint,uint>(0x0076D808,maximum?5u:1u));
            return result.ToArray();
        }
        internal static string SetFloat(Pine pine,uint address,float value) {
            pine.WriteFloat(address,value);
            float actual=pine.ReadFloat(address);
            return !float.IsNaN(actual)&&Math.Abs(actual-value)<0.01f?"ok":"resultado incierto; no se repite";
        }
    }
}
