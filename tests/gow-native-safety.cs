using System;
using System.IO;
using System.Threading;
namespace Cloud90.GameLink {
    class GowNativeSafety {
        static int Main(string[] args) {
            try {
                var profile=new GodOfWarProfile(args[0]);string directory=args[1],log=args[2];
                DateTime deadline=DateTime.UtcNow.AddSeconds(40);bool ready=false;
                while(DateTime.UtcNow<deadline&&!ready) {
                    try {using(var p=new Pine(28012)) {
                        if(p.Status()==0&&profile.Identity(p.Text(12),p.Text(13))) {
                            uint player=p.Read32(profile.PlayerAddress);
                            if(GodOfWarProfile.Pointer(player)) {uint ch=p.Read32(player);ready=GodOfWarProfile.Pointer(ch)&&p.Read32(ch+4)==profile.Vtable;}
                        }
                    }}catch(IOException){}catch(System.Net.Sockets.SocketException){}
                    if(!ready)Thread.Sleep(250);
                }
                if(!ready)throw new Exception("Native title/profile did not initialize");
                var guard=new Pcsx2LogGuard(log);
                if(!guard.Refresh())throw new Exception("Dedicated native log not ready: "+guard.Message);
                using(var p=new Pine(28012)) {
                    uint player,character,mode=p.Read32(profile.ModeAddress);
                    bool eligible=profile.Character(p,out player,out character);
                    if(mode!=11&&eligible)throw new Exception("Non-gameplay context was accepted");
                    Console.WriteLine("Observed native context "+mode+"; effects eligible="+eligible);
                }
                Console.WriteLine("Native identity and dedicated log verified; no gameplay values written.");
                using(var p=new Pine(28012))p.Call(new byte[]{9,1});
                deadline=DateTime.UtcNow.AddSeconds(15);string saved=null;
                while(DateTime.UtcNow<deadline&&saved==null) {
                    foreach(string file in Directory.GetFiles(directory,"*.p2s")) {
                        try {using(var check=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None))if(check.Length>0)saved=file;}catch(IOException){}
                    }
                    if(saved==null)Thread.Sleep(250);
                }
                if(saved==null)throw new Exception("Isolated test state was not created");
                if(!guard.Refresh())throw new Exception("Saving alone unexpectedly latched the guard");
                Console.WriteLine("Created a test state in isolated data; now requesting its reload.");
                using(var p=new Pine(28012))p.Call(new byte[]{10,1});
                deadline=DateTime.UtcNow.AddSeconds(12);
                while(DateTime.UtcNow<deadline&&!guard.Latched) {guard.Refresh();Thread.Sleep(100);}
                if(!guard.Latched)throw new Exception("Native save-state load was not detected");
                Console.WriteLine("PASS: native load latched the guard: "+guard.Message);
                return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
