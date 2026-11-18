using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Cloud90.GameLink
{
    internal sealed class Tlou2GiftPipe : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint processId);

        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        NamedPipeClientStream pipe;
        StreamReader reader;
        StreamWriter writer;
        Task<string> nextLine;
        Inbox.Item pending;
        int sequence, pendingSequence, connection;
        long lastState, lastMessage, nextPing, pendingUntil, nextSpawn;
        bool welcomed, ready;

        public string Epoch { get; private set; }

        public bool Poll(int gamePid, string session, Inbox commands, Link link)
        {
            try
            {
                if (pipe == null && !Connect(gamePid, session)) return false;
                for (int i = 0; i < 32 && nextLine.IsCompleted; i++)
                {
                    string line = nextLine.GetAwaiter().GetResult();
                    if (line == null) throw new IOException("Gift Spawner cerró el canal.");
                    ReadMessage(json.Deserialize<Dictionary<string, object>>(line), link);
                    lastMessage = clock.ElapsedMilliseconds;
                    nextLine = reader.ReadLineAsync();
                }

                long now = clock.ElapsedMilliseconds;
                if (now - lastMessage >= 7000) throw new IOException("Gift Spawner dejó de responder.");
                if (now >= nextPing)
                {
                    Send(new { t = "ping", seq = ++sequence });
                    nextPing = now + 2000;
                }
                if (pending != null && now >= pendingUntil)
                    Complete(link, "resultado incierto; el juego no confirmó la aparición");
                bool available = welcomed && ready && now - lastState <= 2500;
                if (!available || pending != null) return available;

                foreach (var item in commands.LocalPending())
                {
                    var remaining = 8000 - (DateTime.UtcNow - item.Sent).TotalMilliseconds;
                    if (item.Effect != "spawn_runner" || item.Safe || item.Epoch != Epoch ||
                        remaining < 1000 || now < nextSpawn)
                    {
                        link.OnModLine("ack " + item.Id + " invocación no disponible");
                        continue;
                    }

                    pending = item;
                    pendingSequence = ++sequence;
                    pendingUntil = now + (long)remaining;
                    nextSpawn = now + 30000;
                    string viewer = string.IsNullOrWhiteSpace(item.By) ? "Cloud90" : item.By.Trim();
                    Send(new { t = "spawn", seq = pendingSequence, presets = new[] { "Runner" },
                        count = 1, ttlMs = (int)Math.Min(5000, remaining),
                        source = item.IsTest ? "test" : "gift", test = item.IsTest,
                        viewer = viewer, label = item.IsTest ? "Cloud90" : viewer });
                    break;
                }
                return available;
            }
            catch (Exception)
            {
                if (pending != null) Complete(link, "resultado incierto; se perdió la conexión con Gift Spawner");
                Dispose();
                throw;
            }
        }

        bool Connect(int gamePid, string session)
        {
            pipe = new NamedPipeClientStream(".", "TLOU2GiftSpawner", PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            try { pipe.Connect(40); }
            catch (TimeoutException) { Dispose(); return false; }
            uint serverPid;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverPid) || serverPid != (uint)gamePid)
                throw new IOException("El canal de Gift Spawner no pertenece al juego seleccionado.");
            var utf8 = new UTF8Encoding(false);
            reader = new StreamReader(pipe, utf8, false, 1024, true);
            writer = new StreamWriter(pipe, utf8, 1024, true) { AutoFlush = true, NewLine = "\n" };
            Epoch = session + "_gift_" + (++connection);
            lastMessage = clock.ElapsedMilliseconds;
            nextPing = lastMessage + 2000;
            lastState = -10000;
            Send(new { t = "hello", seq = ++sequence, protocol = 1, bridge = "Cloud90 Game Link", version = "1.0" });
            nextLine = reader.ReadLineAsync();
            return true;
        }

        void ReadMessage(Dictionary<string, object> message, Link link)
        {
            switch (Value(message, "t") as string)
            {
                case "welcome":
                    if (!Equals(Value(message, "protocol"), 1) ||
                        !Equals(Value(message, "mod"), "1.2.0") ||
                        !Equals(Value(message, "build"), "t2r-pc-1.6.0"))
                        throw new IOException("Versión de Gift Spawner incompatible con esta vista previa.");
                    welcomed = true;
                    break;
                case "state":
                    var cap = Value(message, "aliveCap") as int?;
                    var alive = Value(message, "alive") as int?;
                    ready = Equals(Value(message, "gameReady"), true) &&
                        !Equals(Value(message, "panic"), true) &&
                        !Equals(Value(message, "spawnsEnabled"), false) &&
                        Equals(Value(message, "queued"), 0) && alive.HasValue && cap.HasValue &&
                        alive.Value >= 0 && alive.Value < Math.Min(2, cap.Value);
                    lastState = clock.ElapsedMilliseconds;
                    break;
                case "spawned":
                    if (Matches(message))
                        Complete(link, Equals(Value(message, "result"), "ok") &&
                            Equals(Value(message, "preset"), "Runner") ? "ok" : "el juego no confirmó un corredor");
                    break;
                case "ack":
                    if (Matches(message) && Equals(Value(message, "accepted"), 0))
                        Complete(link, "Gift Spawner rechazó la invocación");
                    break;
                case "error":
                    if (Matches(message)) Complete(link, "Gift Spawner devolvió un error");
                    break;
            }
        }

        bool Matches(Dictionary<string, object> message)
        {
            return pending != null && Equals(Value(message, "ref"), pendingSequence);
        }

        static object Value(Dictionary<string, object> message, string key)
        {
            object value;
            return message.TryGetValue(key, out value) ? value : null;
        }

        void Complete(Link link, string result)
        {
            var item = pending;
            pending = null;
            link.OnModLine("ack " + item.Id + " " + result);
        }

        void Send(object message) { writer.WriteLine(json.Serialize(message)); }

        public void Dispose()
        {
            if (pipe != null) pipe.Dispose();
            if (nextLine != null)
                nextLine.ContinueWith(task => { var ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
            if (reader != null) reader.Dispose();
            pipe = null;
            reader = null;
            writer = null;
            nextLine = null;
            pending = null;
            welcomed = ready = false;
            Epoch = "";
        }
    }
}
