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
        public GodOfWarHost(string id) { this.id=id; }
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
            if(id=="gow2")throw new InvalidOperationException("God of War II está pendiente de perfil y validación. No se aplicarán direcciones de God of War I.");
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
                    if(serial!="SCUS-97399"||!string.Equals(crc,"D6385328",StringComparison.OrdinalIgnoreCase)) {Block("Revisión no compatible: se requiere SCUS-97399 / D6385328");return;}
                    // Narrow profile: normal play, verified hero pool/vtable and finite health.
                    uint player=pine.Read32(0x0029CD58);
                    if(pine.Read32(0x0029E560)!=11||player<0x100000||player>0x01fffffc) {Block("Esperando una partida controlable");return;}
                    uint character=pine.Read32(player);
                    if(character<0x00795800||character>=0x0079b800||pine.Read32(character+4)!=0x002ba528) {Block("Kratos no está disponible");return;}
                    float health=pine.ReadFloat(character+0x178),max=pine.ReadFloat(character+0x17c);
                    if(!ValidMeter(health,max)) {Block("Kratos muerto o medidor no compatible");return;}
                    if(epoch==""||lastCharacter!=character||identity!=serial+crc) {epoch=Guid.NewGuid().ToString("N");shieldUntil=DateTime.MinValue;}
                    lastCharacter=character;identity=serial+crc;
                    link.SetContext(epoch);link.SetCapabilities(new[]{"heal","damage","magic_add","magic_remove","shield"});link.OnModLine("state 1");
                    Message("God of War I USA conectado · perfil experimental: vida y magia. No cargar estados durante la conexión.");
                    foreach(var item in inbox.LocalPending()) {
                        string cached;
                        if(results.TryGetValue(item.Id,out cached)) {link.OnModLine("ack "+item.Id+" "+cached);continue;}
                        if(results.Count>=2048) {link.OnModLine("ack "+item.Id+" reinicia la conexión de prueba");continue;}
                        // Reserve before a write: losing its reply must never repeat an increment.
                        results[item.Id]="resultado incierto; no se repite";
                        string result="efecto no compatible";
                        if(item.Epoch!=epoch||(DateTime.UtcNow-item.Sent).TotalSeconds>=2)result="comando vencido";
                        else if(link.Paused)result="en pausa";
                        else if(pine.Status()!=0||pine.Read32(0x0029CD58)!=player||pine.Read32(0x0029E560)!=11)result="partida cambió";
                        else if((item.Effect=="damage"||item.Effect=="magic_remove")&&DateTime.UtcNow<shieldUntil)result="escudo activo";
                        else if(item.Effect=="shield") {shieldUntil=DateTime.UtcNow.AddMilliseconds(Math.Max(1000,Math.Min(30000,item.Ms)));result="ok";}
                        else if(item.Effect=="heal"||item.Effect=="damage") {
                            float current=pine.ReadFloat(character+0x178),maximum=pine.ReadFloat(character+0x17c);
                            if(ValidMeter(current,maximum)) {float target=item.Effect=="heal"?Math.Min(maximum,current+maximum*0.25f):Math.Max(1,current-maximum*0.15f);pine.WriteFloat(character+0x178,target);result="ok";}
                        } else if(item.Effect=="magic_add"||item.Effect=="magic_remove") {
                            int level=(int)((pine.Read32(0x00302D34)>>24)&255);
                            if(level>=1&&level<=5) {
                                float maximum=pine.ReadFloat(0x003028F4+(uint)level*4),current=pine.ReadFloat(0x00302D1C);
                                if(!float.IsNaN(current)&&current>=0&&current<=maximum&&maximum>=100&&maximum<=200) {pine.WriteFloat(0x00302D1C,item.Effect=="magic_add"?Math.Min(maximum,current+maximum*0.25f):Math.Max(0,current-maximum*0.25f));result="ok";}
                            } else result="magia no desbloqueada";
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
