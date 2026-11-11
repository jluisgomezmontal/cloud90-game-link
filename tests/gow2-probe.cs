using System;
namespace Cloud90.GameLink {
    class Gow2Probe {
        static int Main() {
            try {using(var p=new Pine(28011)) {
                Console.WriteLine(p.Text(8)+" / "+p.Text(11)+" / "+p.Text(12)+" / "+p.Text(13));
                if(p.Text(12)!="SCUS-97481"||!string.Equals(p.Text(13),"2F123FD8",StringComparison.OrdinalIgnoreCase))return 2;
                uint player=p.Read32(0x2d8734);Console.WriteLine("Player="+player.ToString("X8")+" Mode="+p.Read32(0x2d8ec0));
                if(player>=0x100000&&player<0x1fffc00) {
                    uint ch=p.Read32(player);Console.WriteLine("Character="+ch.ToString("X8"));
                    if(ch>=0x100000&&ch<0x1fffc00) Console.WriteLine("VTable="+p.Read32(ch+4).ToString("X8")+" HP="+p.ReadFloat(ch+0x178)+" Max="+p.ReadFloat(ch+0x17c));
                }
                for(uint a=0x335830;a<0x335880;a+=4)Console.WriteLine(a.ToString("X8")+" "+p.Read32(a).ToString("X8")+" "+p.ReadFloat(a));
                uint config=p.Read32(0x2d870c);
                Console.WriteLine("Config="+config.ToString("X8"));
                if(config>=0x100000&&config<0x1fffc00) {
                    uint field=config+4,table=(uint)((int)field+((int)p.Read32(field)>>12));
                    int level=(int)((p.Read32(0x335864)>>24)&255);
                    Console.WriteLine("Magic level="+level+" Table="+table.ToString("X8"));
                    if(table<0x1ffff00)for(uint i=0;i<16;i+=4)Console.WriteLine("Max table="+p.Read32((table+i)&~3u).ToString("X8"));
                    Console.WriteLine("Thresholds="+p.ReadFloat(config+0x224)+"/"+p.ReadFloat(config+0x228));
                }
            }return 0;}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
