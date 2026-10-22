// Cloud90 Hades Link: the connection to Cloud90, in the same protocol as
// Nebula's link (PLAN-GAME-LINK.md): a WebSocket to /api/game-link/ws with
// the game's token, "hello", then effects in and acks/state out.
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Cloud90.HadesLink
{
    internal sealed class Link
    {
        public const string Game = "hades";
        public const string Version = "cloud90-hades-link-1.0.0";
        static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(20);
        // An effect the game doesn't answer in this long is refused.
        static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(10);
        static readonly Regex EffectName = new Regex("^[a-z0-9_]{1,40}$");

        readonly Func<Settings> settings;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly object gate = new object();
        ClientWebSocket socket;
        CancellationTokenSource stop;
        int generation;

        Inbox inbox;
        bool paused;
        bool inPlay;

        public event Action<string> Status;  // for the window

        public Link(Func<Settings> settings) { this.settings = settings; }

        public bool Paused { get { return paused; } }

        // The mod's inbox once Hades is found; effects wait for it.
        public void UseInbox(Inbox value) { inbox = value; inbox.SetPaused(paused); }

        public void Start()
        {
            lock (gate)
            {
                if (stop != null) stop.Cancel();
                stop = new CancellationTokenSource();
                int mine = ++generation;
                CancellationToken token = stop.Token;
                Task.Run(() => Run(mine, token));
            }
        }

        void SetStatus(string text)
        {
            Log.Write(text);
            Action<string> handler = Status;
            if (handler != null) handler(text);
        }

        async Task Run(int mine, CancellationToken cancel)
        {
            TimeSpan backoff = TimeSpan.FromSeconds(2);
            while (!cancel.IsCancellationRequested && mine == generation)
            {
                Settings s = settings();
                if (s.Token.Length == 0)
                {
                    SetStatus("sin token: pégalo con «Pegar token»");
                    return;
                }
                string error = "conexión perdida";
                int closeCode = 0;
                DateTime connectedAt = DateTime.MinValue;
                ClientWebSocket ws = new ClientWebSocket();
                try
                {
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + s.Token);
                    Uri uri = new Uri(s.Server.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://") + "/api/game-link/ws");
                    SetStatus("conectando…");
                    await ws.ConnectAsync(uri, cancel);
                    socket = ws;
                    connectedAt = DateTime.UtcNow;
                    await Send(new Dictionary<string, object> { { "type", "hello" }, { "game", Game }, { "version", Version } });
                    Task pinger = Ping(ws, cancel);
                    await Receive(ws, cancel);
                    if (ws.CloseStatus.HasValue)
                    {
                        closeCode = (int)ws.CloseStatus.Value;
                        if (!string.IsNullOrEmpty(ws.CloseStatusDescription)) error = ws.CloseStatusDescription;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { error = e.InnerException != null ? e.InnerException.Message : e.Message; }
                finally
                {
                    socket = null;
                    ws.Dispose();
                }
                // A connection that held for a while starts the waits over;
                // being refused (token, access, version) waits a minute.
                if (connectedAt != DateTime.MinValue && DateTime.UtcNow - connectedAt > TimeSpan.FromSeconds(30)) backoff = TimeSpan.FromSeconds(2);
                if (closeCode == 4401 || closeCode == 4403 || closeCode == 4426 || closeCode == 4400) backoff = TimeSpan.FromSeconds(60);
                SetStatus("desconectado: " + error + "; reintento en " + (int)backoff.TotalSeconds + " s");
                try { await Task.Delay(backoff, cancel); }
                catch (OperationCanceledException) { return; }
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
            }
        }

        async Task Ping(ClientWebSocket ws, CancellationToken cancel)
        {
            try
            {
                while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                {
                    await Task.Delay(PingEvery, cancel);
                    await Send(new Dictionary<string, object> { { "type", "ping" } });
                    // Effects the game didn't answer: refused, as Nebula would.
                    if (inbox != null)
                        foreach (Inbox.Item old in inbox.Expire(AckTimeout))
                            await Ack(old.Id, false, "no disponible ahora");
                }
            }
            catch (Exception) { }
        }

        async Task Receive(ClientWebSocket ws, CancellationToken cancel)
        {
            byte[] buffer = new byte[16384];
            while (ws.State == WebSocketState.Open)
            {
                StringBuilder message = new StringBuilder();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    message.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                } while (!r.EndOfMessage);
                await Handle(message.ToString());
            }
        }

        async Task Handle(string text)
        {
            Dictionary<string, object> msg;
            try { msg = json.Deserialize<Dictionary<string, object>>(text); }
            catch (Exception) { return; }
            string type = Str(msg, "type");
            if (type == "welcome")
            {
                SetStatus("conectado a Cloud90");
                await SendState();
            }
            else if (type == "pause")
            {
                bool p = !(msg.ContainsKey("paused") && msg["paused"] is bool && !(bool)msg["paused"]);
                SetPaused(p);
                await SendState();
            }
            else if (type == "effect")
            {
                string id = Str(msg, "id");
                string effect = Str(msg, "effect");
                string reason = null;
                if (!EffectName.IsMatch(effect)) reason = "efecto desconocido";
                else if (paused) reason = "en pausa";
                else if (inbox == null || !inPlay) reason = "no disponible ahora";
                if (reason != null)
                {
                    await Ack(id, false, reason);
                    return;
                }
                int ms = 0;
                if (msg.ContainsKey("ms") && msg["ms"] != null) int.TryParse(msg["ms"].ToString(), out ms);
                Inbox.Item item = new Inbox.Item();
                item.Id = id;
                item.Effect = effect;
                item.Ms = Math.Max(0, Math.Min(ms, 120000));
                item.By = Str(msg, "by");
                item.Safe = msg.ContainsKey("safe") && msg["safe"] is bool && (bool)msg["safe"];
                item.Sent = DateTime.UtcNow;
                inbox.Add(item);
            }
        }

        static string Str(Dictionary<string, object> msg, string key)
        {
            object v;
            return msg.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        // The mod's lines, from Hades' console.
        public void OnModLine(string line)
        {
            if (line.StartsWith("ack "))
            {
                string rest = line.Substring(4);
                int space = rest.IndexOf(' ');
                if (space <= 0) return;
                string id = rest.Substring(0, space);
                string result = rest.Substring(space + 1).Trim();
                if (inbox != null) inbox.Take(id);
                bool ok = result == "ok";
                Log.Write("efecto " + id + ": " + result);
                Fire(Ack(id, ok, ok ? null : result));
            }
            else if (line.StartsWith("state "))
            {
                inPlay = line.EndsWith("1");
                Fire(SendState());
            }
            else
            {
                Log.Write("mod: " + line);
            }
        }

        public void OnGameExited()
        {
            inPlay = false;
            Fire(SendState());
        }

        // The Link's panic button and the dashboard's pause.
        public void SetPaused(bool value)
        {
            paused = value;
            if (inbox != null) inbox.SetPaused(value);
            SetStatus(value ? "efectos en pausa" : "efectos activos");
            Fire(SendState());
        }

        static void Fire(Task t) { t.ContinueWith(delegate (Task x) { var unused = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }

        Task Ack(string id, bool ok, string reason)
        {
            Dictionary<string, object> m = new Dictionary<string, object> { { "type", "ack" }, { "id", id }, { "ok", ok } };
            if (!ok) m["reason"] = reason ?? "no disponible ahora";
            return Send(m);
        }

        Task SendState()
        {
            return Send(new Dictionary<string, object> { { "type", "state" }, { "paused", paused }, { "inPlay", inPlay } });
        }

        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        async Task Send(Dictionary<string, object> message)
        {
            ClientWebSocket ws = socket;
            if (ws == null || ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(message));
            await sendLock.WaitAsync();
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
            catch (Exception) { }
            finally { sendLock.Release(); }
        }
    }
}
