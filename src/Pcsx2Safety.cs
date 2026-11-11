using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Cloud90.GameLink
{
    internal sealed class Pcsx2LogGuard
    {
        readonly string path;
        long offset;
        string fragment="";
        DateTime created;
        internal bool Latched {get;private set;}
        internal int Generation {get;private set;}
        internal string Message {get;private set;}
        internal Pcsx2LogGuard(string path) {this.path=path;Message="Esperando el registro de PCSX2";}
        internal bool Refresh() {
            if(Latched)return false;
            try {
                var info=new FileInfo(path);
                if(!info.Exists||info.Length==0) {Message="Esperando el registro de PCSX2; los efectos siguen suspendidos";return false;}
                if(offset>0&&(info.Length<offset||info.CreationTimeUtc!=created))return Latch("El registro de PCSX2 cambió. Reinicia el juego desde Game Link");
                if(info.Length-offset>1024*1024)return Latch("Registro de PCSX2 sin verificar. Reinicia el juego desde Game Link");
                created=info.CreationTimeUtc;
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    long remaining=file.Length-offset;
                    if(remaining<0||remaining>1024*1024)return Latch("El registro de PCSX2 cambió durante la lectura. Reinicia el juego desde Game Link");
                    file.Position=offset;byte[] bytes=new byte[(int)remaining];
                    int read=0;while(read<bytes.Length){int n=file.Read(bytes,read,bytes.Length-read);if(n==0)break;read+=n;}
                    offset+=read;string text=fragment+Encoding.UTF8.GetString(bytes,0,read);
                    // PCSX2 SaveState.cpp, SysState_ComponentFreezeIn: these are load-only messages.
                    if(Regex.IsMatch(text,@"\bLoading\s+(?:SPU2|GS)\b|\b(?:loading|loaded)\s+(?:save\s*state|savestate)\b",RegexOptions.IgnoreCase))
                        return Latch("Se detectó una carga de estado. Efectos suspendidos: reinicia el juego desde Game Link");
                    int newline=text.LastIndexOf('\n');
                    string complete=newline<0?"":text.Substring(0,newline+1);
                    if(Regex.IsMatch(complete,@"\(VMManager\)\s+(?:Pausing|Resuming)|ELF Loading:|ELF changed",RegexOptions.IgnoreCase))Generation++;
                    fragment=newline<0?text:text.Substring(newline+1);
                    if(fragment.Length>4096)return Latch("Registro de PCSX2 incompleto. Reinicia el juego desde Game Link");
                }
                Message="";return true;
            } catch(IOException) {Message="No se puede verificar el registro de PCSX2; efectos suspendidos";return false;}
              catch(UnauthorizedAccessException) {Message="No se puede leer el registro de PCSX2; efectos suspendidos";return false;}
        }
        bool Latch(string message) {Latched=true;Message=message;return false;}
    }
    internal sealed class GodOfWarContextGate
    {
        string key="";int observations;
        internal string Epoch {get;private set;}
        internal bool Observe(string identity,uint player,uint character) {
            string next=identity+":"+player+":"+character;
            if(next!=key){key=next;observations=0;Epoch="";}
            observations=Math.Min(3,observations+1);
            if(observations<3)return false;
            if(string.IsNullOrEmpty(Epoch))Epoch=Guid.NewGuid().ToString("N");
            return true;
        }
        internal void Reset() {key="";observations=0;Epoch="";}
    }
}
