using System;
namespace Cloud90.GameLink {
    class GowProbe {
        static int Main() {
            try { using(var pine=new Pine(28011)) {
                Console.WriteLine("Version: "+pine.Text(8));
                Console.WriteLine("Title: "+pine.Text(11));
                Console.WriteLine("Serial: "+pine.Text(12));
                Console.WriteLine("UUID: "+pine.Text(13));
                Console.WriteLine("Status: "+pine.Status());
                if(pine.Text(12)!="SCUS-97399" || !string.Equals(pine.Text(13),"D6385328",StringComparison.OrdinalIgnoreCase))return 2;
                uint player=pine.Read32(0x0029CD58);
                Console.WriteLine("Player: "+player.ToString("X8"));
                Console.WriteLine("Scene state: "+pine.Read32(0x0029E560));
                if(player>=0x100000 && player<0x01fffffc) {
                    uint character=pine.Read32(player); Console.WriteLine("Character: "+character.ToString("X8"));
                    if(character>=0x00795800 && character<0x0079B800) {
                        Console.WriteLine("Vtable: "+pine.Read32(character+4).ToString("X8"));
                        Console.WriteLine("Health: "+pine.ReadFloat(character+0x178));
                        Console.WriteLine("Max health: "+pine.ReadFloat(character+0x17c));
                        Console.WriteLine("Magic: "+pine.ReadFloat(0x00302D1C));
                    }
                }
            } return 0; } catch(Exception e) {Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
