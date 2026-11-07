using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Cloud90.GameLink
{
    internal sealed class MvscHost : GameHost
    {
        readonly MameProcess game = new MameProcess();
        Inbox inbox;
        string session, lastStatus;
        Link link;
        Timer watch;
        GamePanicHotkey hotkey;
        DateTime lastPulse;
        bool sawPulse, stale;
        Action onExit;
        readonly object gate = new object();
        void Progress(string message) {if(lastStatus==message)return;lastStatus=message;ReportStatus(message);Log.Write("Marvel vs. Capcom: "+message);}
        static readonly HashSet<string> Known = new HashSet<string>(new[] { "heal", "damage", "rival_heal", "power_add", "power_empty", "rival_power", "timer_add", "timer_remove", "timer_freeze", "invert_controls", "no_punch", "no_kick", "no_jump", "auto_jump", "tunnel_vision", "blackout", "slow_motion", "shield" });
        public MvscHost()
        {
            game.GameLine += OnLine;
            game.Exited += delegate {
                if (watch != null) watch.Dispose(); if (hotkey != null) hotkey.Dispose();
                if (inbox != null) inbox.Connected(false);
                Action handler=onExit;if(handler!=null)handler();
            };
        }
        public override string Id { get { return "mvsc"; } }
        public override string Title { get { return "Marvel vs. Capcom"; } }
        public override string FolderButton { get { return "Carpeta de MAME…"; } }
        public override string FolderPrompt { get { return "MAME 0.289 con mvscu.zip y mvsc.zip en roms"; } }
        public override string Folder(Settings s) { return s.MameDir; }
        public override string Problem(Settings s)
        {
            if (!MameFolder.IsMame(s.MameDir)) return "no encontré MAME: selecciona la carpeta con mame.exe";
            string missing=MameFolder.Missing(s.MameDir,"mvscu","");
            if(missing.Length>0)return missing;
            if(!File.Exists(Path.Combine(s.MameDir,"roms","mvsc.zip")))return "falta mvsc.zip, el set padre compartido, en roms";
            return "";
        }
        public override bool UseFolder(Settings s,string dir) {if(Running||!MameFolder.IsMame(dir))return false;s.MameDir=dir;return true;}
        public override void FindFolder(Settings s) {if(!MameFolder.IsMame(s.MameDir))s.MameDir=MameFolder.Find();}
        public override bool Running {get{return game.Running;}}
        public override bool AnotherRunning(){return MameProcess.AnotherRunning();}
        public override Inbox Prepare(Settings s)
        {
            if(Running)return inbox;
            string problem=Problem(s);if(problem.Length>0)throw new InvalidOperationException(problem);
            if(AnotherRunning())throw new InvalidOperationException("Cierra MAME antes de actualizar el plugin");
            MamePlugin.Install(s.MameDir);
            if(inbox!=null)inbox.Dispose();
            session=Guid.NewGuid().ToString("N");
            inbox=new Inbox(Path.Combine(MameProcess.WorkDir(Id),"inbox.json"),session,true);
            return inbox;
        }
        internal static string Audit(string mame,string arguments)
        {
            var info=new ProcessStartInfo(Path.Combine(mame,"mame.exe"),arguments) {WorkingDirectory=mame,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=new Process()){p.StartInfo=info;p.Start();
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(20000)){p.Kill();throw new IOException("MAME tardó demasiado en auditar la ROM");}
                string text=output.Result+errors.Result;
                if(p.ExitCode!=0)throw new InvalidOperationException("La auditoría de mvscu falló. Conserva mvscu.zip, mvsc.zip y las dependencias que pide MAME.\n"+text.Substring(0,Math.Min(text.Length,1800)));
                return text;
            }
        }
        public override void Start(Settings s,Link link,Action exited)
        {
            if(AnotherRunning())throw new InvalidOperationException("Cierra MAME y abre Marvel vs. Capcom desde Game Link");
            Progress("Comprobando MAME y la revisión USA…");
            if(!Audit(s.MameDir,"-version").StartsWith("0.289 ",StringComparison.Ordinal))throw new InvalidOperationException("Esta integración requiere MAME 0.289");
            Audit(s.MameDir,"mvscu -rompath \""+Path.Combine(s.MameDir,"roms")+"\" -verifyroms");
            this.link=link;onExit=exited;sawPulse=false;stale=false;lastPulse=DateTime.UtcNow;
            link.SetContext("");link.SetCapabilities(new string[0]);link.OnModLine("state 0");
            game.Start(s.MameDir,"mvscu",Id,session);
            hotkey=new GamePanicHotkey(game.ProcessId,delegate{link.SetPaused(!link.Paused);});
            if(watch!=null)watch.Dispose();
            watch=new Timer(delegate {
                lock(gate){
                    if(!Running)return;
                    if((DateTime.UtcNow-lastPulse).TotalSeconds>4 && !stale){
                        stale=true;link.OnModLine("state 0");link.SetContext("");link.SetCapabilities(new string[0]);
                        link.SetPaused(true);Progress(sawPulse?"MAME dejó de responder. Revisa su ventana y reanuda los efectos cuando conecte.":"Esperando al plugin. Revisa que MAME 0.289 haya abierto la revisión USA.");
                    }
                }
            },null,500,500);
        }
        void OnLine(string line)
        {
            lock(gate){
                if(link==null)return;
                if(!line.StartsWith("mvsc_state ",StringComparison.Ordinal)){link.OnModLine(line);return;}
                string[] part=line.Split(new[]{' '},5);
                if(part.Length<4||part[1]!=session||!Regex.IsMatch(part[2],"^[a-f0-9_]{1,100}$")||(part[3]!="0"&&part[3]!="1"))return;
                string[] caps=part.Length<5||part[4].Length==0?new string[0]:part[4].Split(',');
                if(caps.Length>Known.Count)return;
                foreach(string id in caps)if(!Known.Contains(id))return;
                lastPulse=DateTime.UtcNow;sawPulse=true;stale=false;
                link.SetContext(part[2]);link.SetCapabilities(caps);link.OnModLine(part[3]=="1"?"state 1":"state 0");
                Progress(part[3]=="1"?"Combate individual listo para recibir efectos.":"Inserta créditos, pulsa Start y juega como P1 contra CPU.");
            }
        }
        public override void Uninstall(Settings s)
        {
            if(AnotherRunning())throw new InvalidOperationException("Cierra MAME primero");
            if(inbox!=null)inbox.Dispose();MamePlugin.Uninstall(s.MameDir);
        }
    }
}
