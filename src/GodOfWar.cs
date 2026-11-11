using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;

namespace Cloud90.GameLink
{
    internal sealed class GodOfWarHost : GameHost
    {
        readonly string id;
        readonly GodOfWarProfile profile;
        Process process;
        Inbox inbox;
        Link link;
        System.Threading.Timer timer;
        GamePanicHotkey hotkey;
        readonly object gate=new object();
        string epoch="", identity="", lastMessage="";
        uint lastCharacter;
        DateTime shieldUntil;
        readonly Dictionary<string,string> results=new Dictionary<string,string>();
        public GodOfWarHost(string id) { this.id=id;profile=new GodOfWarProfile(id); }
        public override string Id { get {return id;} }
        public override string Title { get {return id=="gow1"?"God of War I":"God of War II";} }
        public override string FolderButton { get {return "Carpeta de PCSX2…";} }
        public override string FolderPrompt { get {return "Selecciona la carpeta que contiene pcsx2-qt.exe";} }
        public override string Folder(Settings s) {return s.Pcsx2Dir;}
        public override bool UseFolder(Settings s,string dir) {if(Running||!File.Exists(Path.Combine(dir,"pcsx2-qt.exe")))return false;s.Pcsx2Dir=dir;return true;}
        public override void FindFolder(Settings s) {}
        public override string Problem(Settings s) {return File.Exists(Path.Combine(s.Pcsx2Dir,"pcsx2-qt.exe"))?"":"Selecciona la carpeta de PCSX2";}
        public override bool Running {get {return process!=null&&!process.HasExited;}}
        public override bool AnotherRunning() {return Process.GetProcessesByName("pcsx2-qt").Length>0;}
        public override Inbox Prepare(Settings s)
        {
            if(Running)return inbox;
            if(Problem(s)!="")throw new InvalidOperationException(Problem(s));
            if(AnotherRunning())throw new InvalidOperationException("Cierra PCSX2 antes de abrirlo desde Game Link");
            if(inbox!=null)inbox.Dispose();
            string dir=Path.Combine(Settings.DataDir,id);Directory.CreateDirectory(dir);
            inbox=new Inbox(Path.Combine(dir,"inbox.json"),Guid.NewGuid().ToString("N"),true);return inbox;
        }
        public override void Start(Settings s,Link connection,Action exited)
        {
            if(Environment.GetEnvironmentVariable("CLOUD90_GOW_EXPERIMENTAL")!="1")throw new InvalidOperationException("Perfil experimental sin validación completa. Para pruebas locales establece CLOUD90_GOW_EXPERIMENTAL=1 antes de abrir Game Link.");
            if(AnotherRunning())throw new InvalidOperationException("Cierra PCSX2 primero");
            using(var dialog=new OpenFileDialog {Title="Selecciona tu imagen de "+Title,Filter="Imágenes PS2|*.iso;*.chd",CheckFileExists=true}) {
                if(dialog.ShowDialog()!=DialogResult.OK)throw new OperationCanceledException("No se seleccionó el juego");
                link=connection;identity="";lastCharacter=0;epoch="";shieldUntil=DateTime.MinValue;results.Clear();
                Block("Esperando PCSX2. Activa Enable PINE en sus ajustes avanzados (slot 28011).");
                process=Process.Start(new ProcessStartInfo(Path.Combine(s.Pcsx2Dir,"pcsx2-qt.exe"),"\""+dialog.FileName+"\"") {WorkingDirectory=s.Pcsx2Dir,UseShellExecute=false});
                process.EnableRaisingEvents=true;
                process.Exited+=delegate {lock(gate) {if(timer!=null)timer.Dispose();if(hotkey!=null)hotkey.Dispose();Block("PCSX2 cerrado");inbox.Connected(false);}exited();};
                hotkey=new GamePanicHotkey(process.Id,delegate {link.SetPaused(!link.Paused);});
                timer=new System.Threading.Timer(delegate {Poll();},null,500,500);
            }
        }
        void Message(string value) {if(value==lastMessage)return;lastMessage=value;ReportStatus(value);Log.Write(Title+": "+value);}
        void Block(string message) {if(link==null)return;link.OnModLine("state 0");link.SetCapabilities(new string[0]);link.SetContext("");epoch="";lastCharacter=0;shieldUntil=DateTime.MinValue;Message(message);}
        internal static bool ValidMeter(float value,float maximum) {return !float.IsNaN(value)&&!float.IsInfinity(value)&&!float.IsNaN(maximum)&&!float.IsInfinity(maximum)&&maximum>=1&&maximum<=1000&&value>0&&value<=maximum;}
        void Poll()
        {
            if(!Monitor.TryEnter(gate))return;
            try {
                if(!Running)return;
                using(var pine=new Pine(28011)) {
                    if(pine.Status()!=0) {Block("PCSX2 en pausa o sin juego");return;}
                    string serial=pine.Text(12),crc=pine.Text(13);
                    if(!profile.Identity(serial,crc)) {Block("Revisión no compatible: se requiere "+profile.Serial+" / "+profile.Crc);return;}
                    uint player,character;
                    if(!profile.Character(pine,out player,out character)) {Block("Esperando una partida controlable con Kratos vivo");return;}
                    if(epoch==""||lastCharacter!=character||identity!=serial+crc) {epoch=Guid.NewGuid().ToString("N");shieldUntil=DateTime.MinValue;}
                    lastCharacter=character;identity=serial+crc;
                    link.SetContext(epoch);link.SetCapabilities(profile.Capabilities(pine));link.OnModLine("state 1");
                    Message(Title+" USA conectado · recursos y mejoras experimentales. No cargar estados durante la conexión.");
                    foreach(var item in inbox.LocalPending()) {
                        string cached;
                        if(results.TryGetValue(item.Id,out cached)) {link.OnModLine("ack "+item.Id+" "+cached);continue;}
                        if(results.Count>=2048) {link.OnModLine("ack "+item.Id+" reinicia la conexión de prueba");continue;}
                        // Reserve before a write: losing its reply must never repeat an increment.
                        results[item.Id]="resultado incierto; no se repite";
                        string result="efecto no compatible";
                        if(item.Epoch!=epoch||(DateTime.UtcNow-item.Sent).TotalSeconds>=2)result="comando vencido";
                        else if(link.Paused)result="en pausa";
                        else if(pine.Status()!=0||pine.Read32(profile.PlayerAddress)!=player||pine.Read32(profile.ModeAddress)!=11||pine.Read32(player)!=character||pine.Read32(character+4)!=profile.Vtable)result="partida cambió";
                        else if(GodOfWarEffects.Policy(item.Effect,item.Safe,item.AllowDestructive,DateTime.UtcNow<shieldUntil)!=null)result=GodOfWarEffects.Policy(item.Effect,item.Safe,item.AllowDestructive,DateTime.UtcNow<shieldUntil);
                        else if(item.Effect=="shield") {shieldUntil=DateTime.UtcNow.AddMilliseconds(Math.Max(1000,Math.Min(30000,item.Ms)));result="ok";}
                        else if(item.Effect=="orbs_add"||item.Effect=="orbs_remove") {
                            uint current=profile.Orbs(pine);
                            if(current<=profile.OrbsMaximum) {
                                uint target=GodOfWarEffects.OrbsTarget(item.Effect,current,profile.OrbsMaximum);
                                if(profile.IsTwo)pine.Write32(profile.OrbsAddress,target);else pine.Write16(profile.OrbsAddress,(ushort)target);
                                result=profile.Orbs(pine)==target?"ok":"resultado incierto; no se repite";
                            } else result="contador de orbes no compatible";
                        } else if(item.Effect=="amulet_max") {
                            if(profile.IsTwo) {float current=pine.ReadFloat(0x00335844);if(!float.IsNaN(current)&&current>=0&&current<=100)result=GodOfWarEffects.SetFloat(pine,0x00335844,100);}
                        }
                        else if(item.Effect=="heal"||item.Effect=="damage"||item.Effect=="heal_max"||item.Effect=="kill") {
                            float current=pine.ReadFloat(character+0x178),maximum=pine.ReadFloat(character+0x17c);
                            if(ValidMeter(current,maximum))result=GodOfWarEffects.SetFloat(pine,character+0x178,GodOfWarEffects.HealthTarget(item.Effect,current,maximum));
                        } else if(item.Effect=="magic_add"||item.Effect=="magic_remove"||item.Effect=="magic_max"||item.Effect=="magic_empty") {
                            float maximum=profile.MagicMaximum(pine),current=pine.ReadFloat(profile.MagicAddress);
                            if(maximum>0&&!float.IsNaN(current)&&current>=0&&current<=maximum)result=GodOfWarEffects.SetFloat(pine,profile.MagicAddress,GodOfWarEffects.MagicTarget(item.Effect,current,maximum));
                            else result="magia no disponible";
                        } else if(item.Effect=="rage_max"||item.Effect=="rage_empty") {
                            float current=pine.ReadFloat(profile.RageAddress);
                            if(!float.IsNaN(current)&&current>=0&&current<=100)result=GodOfWarEffects.SetFloat(pine,profile.RageAddress,item.Effect=="rage_max"?100:0);
                        } else if(item.Effect=="blades_min"||item.Effect=="blades_max"||item.Effect=="levels_min"||item.Effect=="levels_max") {
                            if(profile.IsTwo) {results[item.Id]="mejoras de II no compatibles";link.OnModLine("ack "+item.Id+" "+results[item.Id]);continue;}
                            var targets=GodOfWarEffects.UpgradeTargets(item.Effect);
                            bool valid=true;foreach(var target in targets)if(pine.Read32(target.Key)>(target.Key==0x0076D808?5u:2u))valid=false;
                            if(valid) {
                                result="ok";
                                foreach(var target in targets) {
                                    pine.Write32(target.Key,target.Value);
                                    if(pine.Read32(target.Key)!=target.Value)result="resultado incierto; no se repite";
                                }
                            } else result="mejoras no disponibles en este contexto";
                        }
                        results[item.Id]=result;link.OnModLine("ack "+item.Id+" "+result);
                    }
                }
            } catch(Exception e) {Block("Sin conexión con PCSX2: "+e.Message);}
            finally {Monitor.Exit(gate);}
        }
        public override void Uninstall(Settings s) {if(Running)throw new InvalidOperationException("Cierra PCSX2 primero");if(inbox!=null)inbox.Dispose();Message("No hay archivos del juego que desinstalar");}
    }
}
