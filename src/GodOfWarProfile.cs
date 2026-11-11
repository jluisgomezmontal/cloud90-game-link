using System;
using System.Collections.Generic;

namespace Cloud90.GameLink
{
    internal sealed class GodOfWarProfile
    {
        internal readonly string Id, Serial, Crc;
        internal readonly uint PlayerAddress, ModeAddress, Vtable, MagicAddress, RageAddress;
        internal bool IsTwo {get {return Id=="gow2";}}
        internal uint OrbsAddress {get {return IsTwo?0x00335850u:0x00302d28u;}}
        internal uint OrbsMaximum {get {return IsTwo?999999u:60000u;}}
        internal uint Orbs(IPineMemory p) {uint value=p.Read32(OrbsAddress);return IsTwo?value:value&65535;}
        internal GodOfWarProfile(string id) {
            if(id!="gow1"&&id!="gow2")throw new ArgumentException("id");
            Id=id;
            Serial=IsTwo?"SCUS-97481":"SCUS-97399";Crc=IsTwo?"2F123FD8":"D6385328";
            PlayerAddress=IsTwo?0x002d8734u:0x0029cd58u;
            ModeAddress=IsTwo?0x002d8ec0u:0x0029e560u;
            Vtable=IsTwo?0x002f1440u:0x002ba528u;
            MagicAddress=IsTwo?0x00335840u:0x00302d1cu;
            RageAddress=IsTwo?0x00335848u:0x00302d20u;
        }
        internal bool Identity(string serial,string crc) {return serial==Serial&&string.Equals(crc,Crc,StringComparison.OrdinalIgnoreCase);}
        internal static bool Pointer(uint value) {return value>=0x100000&&value<0x01fffc00&&(value&3)==0;}
        internal bool Character(IPineMemory p,out uint player,out uint character) {
            player=0;character=0;
            if(IsTwo&&(p.Read32(0x001bc0dc)!=0x8c428734||p.Read32(0x00213760)!=0xc4400178||p.Read32(0x001d9250)!=0x90655867))return false;
            player=p.Read32(PlayerAddress);character=0;
            if(p.Read32(ModeAddress)!=11||!Pointer(player))return false;
            character=p.Read32(player);
            if(!Pointer(character)||p.Read32(character+4)!=Vtable)return false;
            if(!IsTwo&&(character<0x00795800||character>=0x0079b800))return false;
            return GodOfWarHost.ValidMeter(p.ReadFloat(character+0x178),p.ReadFloat(character+0x17c));
        }
        internal float MagicMaximum(IPineMemory p) {
            int level;
            if(!IsTwo) {
                level=(int)((p.Read32(0x00302d34)>>24)&255);
                if(level<1||level>5)return 0;
                float max=p.ReadFloat(0x003028f4+(uint)level*4);
                return FiniteMaximum(max)?max:0;
            }
            level=(int)((p.Read32(0x00335864)>>24)&255);
            if(level<1||level>5)return 0;
            uint config=p.Read32(0x002d870c);
            if(!Pointer(config))return 0;
            // ELF 001D9248..001D9278: signed packed relative pointer, then int16[level].
            uint field=config+4;
            long table=(long)field+((int)p.Read32(field)>>12);
            long entry=table+level*2;
            if(entry<0x100000||entry>0x01fffffc||(entry&1)!=0)return 0;
            uint word=p.Read32((uint)entry&~3u);
            int maximum=(short)((word>>((int)(entry&2)*8))&65535);
            return FiniteMaximum(maximum)?maximum:0;
        }
        static bool FiniteMaximum(float v) {return !float.IsNaN(v)&&!float.IsInfinity(v)&&v>=100&&v<=200;}
        internal string[] Capabilities(IPineMemory p) {
            var caps=new List<string>{"heal","damage","heal_max","kill","shield"};
            if(MagicMaximum(p)>0)caps.AddRange(new[]{"magic_add","magic_remove","magic_max","magic_empty"});
            float rage=p.ReadFloat(RageAddress);
            if(!float.IsNaN(rage)&&rage>=0&&rage<=100)caps.AddRange(new[]{"rage_max","rage_empty"});
            if(Orbs(p)<=OrbsMaximum)caps.AddRange(new[]{"orbs_add","orbs_remove"});
            if(IsTwo) {float amulet=p.ReadFloat(0x00335844);if(!float.IsNaN(amulet)&&amulet>=0&&amulet<=100)caps.Add("amulet_max");}
            // II's upgrade structures are independent and not mapped yet.
            if(!IsTwo)caps.AddRange(new[]{"blades_min","blades_max","levels_min","levels_max"});
            return caps.ToArray();
        }
    }
}
