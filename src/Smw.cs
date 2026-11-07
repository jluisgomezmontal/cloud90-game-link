using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Cloud90.GameLink
{
    internal static class SmwRuntime
    {
        internal const string Exe = "SuperMarioWorldSNESRecomp.exe";
        internal const string ExeHash = "fb1540efa35cb92f899a34fc7422fe1d4f1d6dea116ceed1f409c0f7c2cb7586";
        internal const string RomHash = "0838e531fe22c077528febe14cb3ff7c492f1f5fa8de354192bdff7137c27f5b";
        internal static bool IsGame(string dir) { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, Exe)); }
        internal static string Rom(string dir)
        {
            string cfg = Path.Combine(dir, "rom.cfg");
            if (!File.Exists(cfg)) return "";
            string path = File.ReadAllText(cfg).Trim();
            if (path.Length == 0 || path.IndexOf('"') >= 0 || path.IndexOf('\n') >= 0) return "";
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(dir, path));
        }
        internal static string Problem(string dir)
        {
            try {
                if (!IsGame(dir)) return "no encontré Super Mario World: elige la carpeta del runtime";
                if (CupheadInstaller.HashFile(Path.Combine(dir, Exe)) != ExeHash) return "Runtime incompatible: requiere la build 0.15.6 validada por Cloud90";
                string rom = Rom(dir);
                if (!File.Exists(rom)) return "Abre el juego normalmente, selecciona tu ROM USA y ciérralo";
                if (CupheadInstaller.HashFile(rom) != RomHash) return "ROM incompatible: requiere Super Mario World USA original, sin parches ni cabecera";
                string state = Path.Combine(dir, "mods", "preloaded", "state.toml");
                if (File.Exists(state)) {
                    foreach (string feature in Regex.Split(File.ReadAllText(state), @"\[\[feature\]\]"))
                        if ((feature.Contains("super-mario-world.gameplay.native-coop") || feature.Contains("super-mario-world.smash64.captain-falcon")) && Regex.IsMatch(feature, @"(?m)^\s*enabled\s*=\s*true\s*(?:#.*)?$"))
                            return "Desactiva Native Co-op y Captain Falcon en Mods y cierra el juego";
                }
                return "";
            } catch (Exception error) { return "No se pudo validar Super Mario World: " + error.Message; }
        }
        internal static string Script(string session)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cloud90.Smw.lua")) {
                if (stream == null) throw new InvalidOperationException("Falta el módulo SMW en esta compilación");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return "CLOUD90_SMW_SESSION=" + Inbox.LuaString(session) + ";\n" + reader.ReadToEnd();
            }
        }
    }

    // Bounded newline JSON replies; UTF-8 source is hex-encoded by the runtime's
    // documented eval protocol. Viewer/server strings are never executed as code.
    internal sealed class SmwClient : IDisposable
    {
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 16 };
        internal SmwClient(int port)
        {
            tcp = new TcpClient();
            var pending = tcp.BeginConnect(IPAddress.Loopback, port, null, null);
            try { if (!pending.AsyncWaitHandle.WaitOne(500)) throw new IOException("SMW no responde"); tcp.EndConnect(pending); }
            catch { tcp.Close(); throw; }
            finally { pending.AsyncWaitHandle.Close(); }
            tcp.NoDelay = true; tcp.ReceiveTimeout = tcp.SendTimeout = 1500; stream = tcp.GetStream();
        }
        internal Dictionary<string, object> Command(string command)
        {
            byte[] request = Encoding.ASCII.GetBytes(command + "\n");
            if (command.IndexOf('\n') >= 0 || request.Length >= 131072) throw new InvalidDataException("Petición SMW inválida");
            stream.Write(request, 0, request.Length);
            using (var data = new MemoryStream()) {
                while (data.Length < 65536) {
                    int b = stream.ReadByte();
                    if (b < 0) throw new EndOfStreamException("SMW cerró la conexión");
                    if (b == 10) {
                        var result = json.Deserialize<Dictionary<string, object>>(new UTF8Encoding(false, true).GetString(data.ToArray()));
                        if (result == null || !result.ContainsKey("ok") || !(result["ok"] is bool) || !(bool)result["ok"]) {
                            object detail;
                            string reason = result != null && result.TryGetValue("error", out detail) ? Convert.ToString(detail) : "respuesta inválida";
                            if (reason.Length > 200) reason = reason.Substring(0, 200);
                            throw new InvalidDataException("La interfaz Lua rechazó el comando: " + reason);
                        }
                        return result;
                    }
                    data.WriteByte((byte)b);
                }
                throw new InvalidDataException("Respuesta SMW demasiado grande");
            }
        }
        internal string Eval(string source)
        {
            string hex = BitConverter.ToString(Encoding.UTF8.GetBytes(source)).Replace("-", "");
            var reply = Command("eval " + hex);
            var values = reply["values"] as ArrayList;
            if (values == null || values.Count != 1 || !(values[0] is string)) throw new InvalidDataException("Respuesta Lua inválida");
            return (string)values[0];
        }
        public void Dispose() { tcp.Close(); }
    }

    internal sealed class SmwHost : GameHost
    {
        Inbox inbox;
        Process game;
        Timer timer;
        SmwClient client;
        GamePanicHotkey hotkey;
        string session, lastStatus;
        bool? lastPlay;
        long lastFrame = -1;
        DateTime lastAdvance;
        readonly object gate = new object();
        public override string Id { get { return "smw"; } }
        public override string Title { get { return "Super Mario World"; } }
        public override string FolderButton { get { return "Carpeta de Super Mario World…"; } }
        public override string FolderPrompt { get { return "La carpeta con SuperMarioWorldSNESRecomp.exe"; } }
        public override string Problem(Settings s) { return SmwRuntime.Problem(s.SmwDir); }
        public override string Folder(Settings s) { return s.SmwDir; }
        public override bool UseFolder(Settings s, string dir) { if (Running || !SmwRuntime.IsGame(dir)) return false; s.SmwDir = dir; return true; }
        public override void FindFolder(Settings s)
        {
            if (SmwRuntime.IsGame(s.SmwDir)) return;
            foreach (DriveInfo drive in DriveInfo.GetDrives()) {
                string dir = Path.Combine(drive.RootDirectory.FullName, "SuperMarioWorld");
                if (SmwRuntime.IsGame(dir)) { s.SmwDir = dir; return; }
            }
        }
        public override bool Running { get { return game != null && !game.HasExited; } }
        public override bool AnotherRunning() { return Process.GetProcessesByName("SuperMarioWorldSNESRecomp").Length > 0; }
        public override Inbox Prepare(Settings s)
        {
            if (Running) return inbox;
            string problem = Problem(s); if (problem.Length > 0) throw new InvalidOperationException(problem);
            if (inbox != null) inbox.Dispose();
            session = Guid.NewGuid().ToString("N");
            string dir = Path.Combine(Settings.DataDir, "smw"); Directory.CreateDirectory(dir);
            // No file is installed into the runtime. This inbox is owned by Game Link.
            inbox = new Inbox(Path.Combine(dir, "inbox.json"), session);
            return inbox;
        }
        void Progress(string text) { if (text == lastStatus) return; lastStatus = text; ReportStatus(text); Log.Write("SMW: " + text); }
        void State(Link link, bool value) { if (lastPlay == value) return; lastPlay = value; link.OnModLine(value ? "state 1" : "state 0"); }
        public override void Start(Settings s, Link link, Action exited)
        {
            if (AnotherRunning()) throw new InvalidOperationException("Cierra Super Mario World primero");
            string problem = Problem(s); if (problem.Length > 0) throw new InvalidOperationException(problem);
            if (timer != null) timer.Dispose(); if (client != null) { client.Dispose(); client = null; }
            if (game != null) game.Dispose();
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            link.SetContext(""); link.SetCapabilities(new string[0]); lastPlay = null; State(link, false);
            lastStatus = null; lastFrame = -1; lastAdvance = DateTime.UtcNow;
            var info = new ProcessStartInfo(Path.Combine(s.SmwDir, SmwRuntime.Exe)) { WorkingDirectory = s.SmwDir, UseShellExecute = false };
            info.Arguments = "\"" + SmwRuntime.Rom(s.SmwDir) + "\"";
            info.EnvironmentVariables["SNESRECOMP_LUA_PORT"] = port.ToString();
            info.EnvironmentVariables["SNESRECOMP_NO_LAUNCHER"] = "1";
            info.EnvironmentVariables.Remove("SNESRECOMP_LUA_PAUSED");
            // Never inherit a development/netplay session from the parent shell.
            foreach (string key in new List<string>(EnvironmentNames(info)))
                if (key.StartsWith("SNES_NET_", StringComparison.Ordinal) || key == "SNESRECOMP_FORCE_TURBO") info.EnvironmentVariables.Remove(key);
            game = Process.Start(info);
            hotkey = new GamePanicHotkey(game.Id, delegate { link.SetPaused(!link.Paused); });
            Progress("Abriendo SMW. Selecciona 1 Player y entra en un nivel.");
            timer = new Timer(delegate {
                if (!Monitor.TryEnter(gate)) return;
                try {
                    if (!Running) {
                        timer.Dispose(); if (client != null) { client.Dispose(); client = null; }
                        if (hotkey != null) hotkey.Dispose(); inbox.Connected(false); exited(); return;
                    }
                    if (client == null) {
                        // Reject a port occupied by a different local process.
                        if (!SmwTcpOwner.IsOwner(port, game.Id)) {
                            if ((DateTime.UtcNow-lastAdvance).TotalSeconds>30) Progress("SMW aún no abre su interfaz local. Revisa la ventana del juego y el registro.");
                            return;
                        }
                        client = new SmwClient(port);
                        if (client.Eval(SmwRuntime.Script(session)) != "cloud90-smw-1") throw new InvalidDataException("Módulo SMW incompatible");
                    }
                    var status = client.Command("status");
                    long frame = Convert.ToInt64(status["frame"]);
                    if (frame != lastFrame) { lastAdvance = DateTime.UtcNow; lastFrame = frame; }
                    bool stalled = (DateTime.UtcNow - lastAdvance).TotalSeconds > 1;
                    // Host pause freezes Lua callbacks: clear before the first resumed frame.
                    if (stalled || (bool)status["paused"]) client.Eval("cloud90_smw.stop(); return 'stopped'");
                    string result = client.Eval(inbox.SmwPoll(session, stalled || (bool)status["paused"]));
                    string[] parts = result.Split('|');
                    if (parts.Length != 5 || parts[0] != session || !Regex.IsMatch(parts[1], "^[a-f0-9_]{1,100}$")) throw new InvalidDataException("Estado SMW inválido");
                    string[] caps = parts[3].Length == 0 ? new string[0] : parts[3].Split(',');
                    foreach (string id in caps) if (!Regex.IsMatch(id, "^[a-z0-9_]{1,40}$")) throw new InvalidDataException("Capacidad SMW inválida");
                    if (caps.Length > 40) throw new InvalidDataException("Demasiadas capacidades SMW");
                    link.SetContext(parts[1]); link.SetCapabilities(caps);
                    State(link, parts[2] == "1" && !stalled && !(bool)status["paused"]);
                    foreach (string ack in parts[4].Split(';')) {
                        int at = ack.IndexOf(':'); if (at < 1) continue;
                        string id = ack.Substring(0, at);
                        if (Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$")) link.OnModLine("ack " + id + " " + ack.Substring(at+1));
                    }
                    Progress(lastPlay == true ? "Mario listo para recibir efectos." : "Entra en un nivel individual y sal de pausa, menús o transiciones.");
                } catch (Exception error) {
                    State(link, false); link.SetCapabilities(new string[0]); link.SetContext("");
                    if (client != null) { client.Dispose(); client = null; }
                    // A fresh Lua session on reconnect invalidates any stale effects.
                    inbox.SetPaused(true); link.SetPaused(true);
                    Progress("SMW sin conexión: " + error.Message + ". Reanuda los efectos cuando vuelva a conectar.");
                } finally { Monitor.Exit(gate); }
            }, null, 200, 200);
        }
        static IEnumerable<string> EnvironmentNames(ProcessStartInfo info) { foreach (string key in info.EnvironmentVariables.Keys) yield return key; }
        public override void Uninstall(Settings s)
        {
            if (Running || AnotherRunning()) throw new InvalidOperationException("Cierra Super Mario World primero");
            if (inbox != null) inbox.Dispose();
            // Integration exists only in the game's Lua VM; no game files to delete.
        }
    }

    internal static class SmwTcpOwner
    {
        [DllImport("iphlpapi.dll", SetLastError=true)] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int kind, uint reserved);
        internal static bool IsOwner(int port, int pid)
        {
            int size=0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (size < 4 || size > 4*1024*1024) return false;
            IntPtr table=Marshal.AllocHGlobal(size);
            try {
                if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0)!=0) return false;
                int count=Marshal.ReadInt32(table);
                for (int i=0;i<count;i++) {
                    int offset=4+i*24;
                    if (offset+24>size) return false;
                    int raw=Marshal.ReadInt32(table,offset+8);
                    int localPort=((raw&255)<<8)|((raw>>8)&255);
                    if (localPort==port && Marshal.ReadInt32(table,offset+20)==pid) return true;
                }
                return false;
            } finally { Marshal.FreeHGlobal(table); }
        }
    }

    // F9 is normally SMW's load-state shortcut. Consume both key edges only for
    // this game window, while preserving Shift+F9 and all other applications.
    internal sealed class GamePanicHotkey : IDisposable
    {
        delegate IntPtr Hook(int code, IntPtr wp, IntPtr lp);
        readonly Hook callback;
        IntPtr handle;
        bool held;
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string module);
        internal GamePanicHotkey(int pid, Action toggle)
        {
            callback=delegate(int code, IntPtr wp, IntPtr lp) {
                if (code>=0 && Marshal.ReadInt32(lp)==0x78) {
                    uint foreground; GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
                    bool down=wp==new IntPtr(0x100) || wp==new IntPtr(0x104);
                    bool up=wp==new IntPtr(0x101) || wp==new IntPtr(0x105);
                    if (held && up) { held=false; return new IntPtr(1); }
                    bool modifiers = GetAsyncKeyState(0x10)<0 || GetAsyncKeyState(0x11)<0 || GetAsyncKeyState(0x12)<0;
                    if (foreground==(uint)pid && (held || !modifiers)) {
                        if (down && !held) { held=true; toggle(); }
                        return new IntPtr(1);
                    }
                }
                return CallNextHookEx(handle,code,wp,lp);
            };
            handle=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
            if (handle==IntPtr.Zero) Log.Write("Game Link: F9 no disponible; usa el botón Pausar efectos de Game Link.");
        }
        public void Dispose() { if (handle!=IntPtr.Zero) { UnhookWindowsHookEx(handle); handle=IntPtr.Zero; } }
    }
}
