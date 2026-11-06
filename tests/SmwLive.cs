using System;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace Cloud90.GameLink {
    public static class SmwLive {
        static void Check(bool value,string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS SMW native: "+label); }
        static string Hex(string s) { return BitConverter.ToString(Encoding.UTF8.GetBytes(s)).Replace("-",""); }
        static void Step(SmwClient c,int n) {
            long start=Convert.ToInt64(c.Command("pause")["frame"]);
            c.Command("step "+n);
            DateTime until=DateTime.UtcNow.AddSeconds(15);
            while (Convert.ToInt64(c.Command("status")["frame"])<start+n) { if (DateTime.UtcNow>until) throw new TimeoutException("step"); Thread.Sleep(5); }
        }
        public static void Run(string folder,string rom,string enterLevel) {
            var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var info=new ProcessStartInfo(Path.Combine(folder,SmwRuntime.Exe),"\""+rom+"\"") {WorkingDirectory=folder,UseShellExecute=false};
            info.EnvironmentVariables["SNESRECOMP_LUA_PORT"]=port.ToString(); info.EnvironmentVariables["SNESRECOMP_LUA_PAUSED"]="1"; info.EnvironmentVariables["SNESRECOMP_NO_LAUNCHER"]="1";
            using(var p=Process.Start(info)) {
                SmwClient c=null;
                try {
                    DateTime until=DateTime.UtcNow.AddSeconds(30);
                    while(c==null) {
                        if(p.HasExited) throw new Exception("game exited");
                        try {c=new SmwClient(port);} catch { if(DateTime.UtcNow>until) throw; Thread.Sleep(100); }
                    }
                    Check(SmwTcpOwner.IsOwner(port,p.Id),"TCP endpoint belongs to the launched game");
                    c.Command("run "+Hex(File.ReadAllText(enterLevel)));
                    until=DateTime.UtcNow.AddSeconds(45);
                    while((bool)c.Command("status")["running"]) {if(DateTime.UtcNow>until) throw new TimeoutException("enter level"); Thread.Sleep(20);}
                    Check(c.Eval("return mainmemory.read_u8(0x100)")=="20","entered a level through normal controller inputs");
                    Check(c.Eval(SmwRuntime.Script("abc123"))=="cloud90-smw-1","embedded Lua bridge loads in the actual restricted VM");
                    string state=c.Eval("return cloud90_smw.poll('abc123',true,false,false,{})");
                    Check(state.Contains("|1|"),"gameplay status and capabilities");
                    string epoch=state.Split('|')[1];
                    using(var inbox=new Inbox(Path.Combine(folder,"native-test-inbox.json"),"abc123")) {
                        inbox.Connected(true); inbox.SetPolicy(false,false); inbox.SetContext(epoch);
                        inbox.Add(new Inbox.Item {Id="native-power",Effect="fire_flower",Ms=1000,Epoch=epoch,Sent=DateTime.UtcNow});
                        state=c.Eval(inbox.SmwPoll("abc123",false));
                        Check(state.Contains("native-power:ok"),"C# inbox reaches Lua and returns an acknowledgement");
                        Check(c.Eval("return mainmemory.read_u8(0x19)")=="3","fire power applied in native WRAM"); inbox.Take("native-power");
                        inbox.Add(new Inbox.Item {Id="native-life",Effect="life_add",Ms=1000,Epoch=epoch,Sent=DateTime.UtcNow});
                        string before=c.Eval("return mainmemory.read_u8(0xdbe)");
                        c.Eval(inbox.SmwPoll("abc123",false)); c.Eval(inbox.SmwPoll("abc123",false));
                        Check(int.Parse(c.Eval("return mainmemory.read_u8(0xdbe)"))==int.Parse(before)+1,"duplicate retries apply a life exactly once"); inbox.Take("native-life");
                        inbox.Add(new Inbox.Item {Id="native-fire",Effect="fire_stream",Ms=1000,Epoch=epoch,Sent=DateTime.UtcNow});
                        state=c.Eval(inbox.SmwPoll("abc123",false)); Check(state.Contains("native-fire:ok"),"native projectile stream accepted"); inbox.Take("native-fire");
                        Step(c,30); state=c.Eval(inbox.SmwPoll("abc123",false));
                        Check(state.Split('|')[1]==epoch,"context remains stable during ordinary game frames");
                        Check(c.Eval("return game.command('fire_stream_status')").Contains("rate=30"),"projectile stream runs in native game");
                        inbox.SetPaused(true); c.Eval(inbox.SmwPoll("abc123",false));
                        Check(c.Eval("return game.command('fire_stream_status')").Contains("rate=0"),"panic resets native projectiles");
                        inbox.SetPaused(false); inbox.SetPolicy(true,false);
                        inbox.Add(new Inbox.Item {Id="native-safe",Effect="small",Ms=1000,Epoch=epoch,Sent=DateTime.UtcNow});
                        Check(c.Eval(inbox.SmwPoll("abc123",false)).Contains("modo sin morir"),"current no-death policy enforced inside the game"); inbox.Take("native-safe");
                        inbox.SetPolicy(false,false);
                        inbox.Add(new Inbox.Item {Id="native-old",Effect="cape",Ms=1000,Epoch="old",Sent=DateTime.UtcNow});
                        Check(c.Eval(inbox.SmwPoll("abc123",false)).Contains("intento anterior"),"old-attempt commands refused in native VM");
                        inbox.Take("native-old");
                        Check(c.Eval("cloud90_smw.poll('abc123',true,false,false,{{id='watchdog',effect='invincible',ms=5000,epoch=cloud90_smw.epoch}}); return 'ok'")=="ok","watchdog effect started");
                        Step(c,125);
                        Check(c.Eval("return tostring(next(cloud90_smw.active)==nil and not cloud90_smw.connected)")=="true","lost-heartbeat watchdog clears effects");
                    }
                } finally {if(c!=null)c.Dispose(); if(!p.HasExited){p.Kill();p.WaitForExit(5000);}}
            }
        }
    }
}
