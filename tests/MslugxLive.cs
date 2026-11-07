using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
namespace Cloud90.GameLink {
    public static class MslugxLive {
        public static void Run(string server,string mame,string driver,string pluginPath) {
            var settings=new Settings {Server=server,MameDir=mame};settings.Tokens["mslugx"]="c90gl_test";
            var host=new MslugxHost();var link=new Link(delegate{return settings;},"mslugx","Metal Slug X");
            link.Status+=delegate(string s){Console.WriteLine("LINK "+s);};
            host.Status+=delegate(string s){Console.WriteLine("GAME "+s);};
            MameProcess.ExtraArgs="-video none -sound none -nothrottle -pluginspath \""+pluginPath+"\" -autoboot_script \""+driver+"\" -seconds_to_run 140";
            bool exited=false;
            try {
                link.UseInbox(host.Prepare(settings));link.Start();
                host.Start(settings,link,delegate{link.OnGameExited();exited=true;});
                DateTime until=DateTime.UtcNow.AddSeconds(35);
                while(!exited && DateTime.UtcNow<until)Thread.Sleep(100);
                string acks=File.ReadAllText(Path.Combine(MameProcess.WorkDir("mslugx"),"acks.txt"));
                if(!acks.Contains("hello cloud90-mslugx-1 mslugx") || !acks.Contains("ack e-42 ok"))throw new Exception("MSX WebSocket-to-MAME did not return a successful game ACK");
                Console.WriteLine("PASS full chain: mslugx token and hello -> mslugx MAME -> JSON inbox -> effect -> ACK -> WebSocket");
            } finally {
                link.Stop();var p=(MameProcess)typeof(ModernMameHost).GetField("game",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);
                if(p.Running) {using(var child=Process.GetProcessById(p.ProcessId)){child.Kill();child.WaitForExit(5000);}}
            }
        }
    }
}
