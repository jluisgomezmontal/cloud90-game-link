// Cloud90 Game Link: one game's connection to Cloud90, in the same protocol as
// Nebula's link (PLAN-GAME-LINK.md): a WebSocket to /api/game-link/ws with
// the game's token, "hello", then effects in and acks/state out.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Cloud90.GameLink
{
    internal sealed class Link
    {
        public const string Version = "cloud90-game-link-1.3.1";
        // "hades", "kof2002": the token and the hello are this game's.
        public readonly string Game;
        readonly string title;
        static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(20);
        // An effect the game doesn't answer in this long is refused.
        static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(10);
        static readonly Regex EffectName = new Regex("^[a-z0-9_]{1,40}$");
        static readonly Regex ArgValue = new Regex("^[A-Za-z0-9_]{0,40}$");
        internal const int MaxMessageBytes = 64 * 1024;
        static readonly Encoding WireEncoding = new UTF8Encoding(false, true);

        readonly Func<Settings> settings;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxMessageBytes, RecursionLimit = 32 };
        readonly object gate = new object();
        ClientWebSocket socket;
        CancellationTokenSource stop;
        volatile int generation;

        Inbox inbox;
        bool paused;
        bool inPlay;
        bool welcomed;  // this connection got Cloud90's "welcome"
        string gameContext = "";
        string[] capabilities = null;
        bool noDeath = true, allowDestructive;

        public event Action<string> Status;  // for the window

        public Link(Func<Settings> settings, string game, string title)
        {
            this.settings = settings;
            Game = game;
            this.title = title;
        }

        public bool Paused { get { return paused; } }

        // The game's inbox once the game is found; effects wait for it.
        public void UseInbox(Inbox value)
        {
            if (inbox != null && !ReferenceEquals(inbox, value)) inbox.Dispose();
            inbox = value; inbox.SetPaused(paused); inbox.Connected(welcomed);
            if (Game == "cuphead" || Game == "ctrnf") { inbox.SetContext(gameContext); inbox.SetPolicy(noDeath, allowDestructive); }
        }
        public void SetCapabilities(string[] value)
        {
            if (Game != "cuphead" && Game != "ctrnf") return;
            string[] next = value ?? new string[0];
            if (capabilities != null && string.Join(",", capabilities) == string.Join(",", next)) return;
            capabilities = next;
            Fire(SendState());
        }
        void ApplyPolicy(Dictionary<string, object> message)
        {
            if (Game != "cuphead" && Game != "ctrnf") return;
            noDeath = !message.ContainsKey("noDeath") || !(message["noDeath"] is bool) || (bool)message["noDeath"];
            allowDestructive = message.ContainsKey("allowDestructive") && message["allowDestructive"] is bool && (bool)message["allowDestructive"];
            if (inbox != null) inbox.SetPolicy(noDeath, allowDestructive);
        }
        public void SetContext(string epoch)
        {
            if ((Game != "cuphead" && Game != "ctrnf") || epoch == gameContext) return;
            gameContext = epoch ?? "";
            if (inbox != null) inbox.SetContext(gameContext);
            Fire(SendState());
        }

        public void Start()
        {
            lock (gate)
            {
                if (stop != null) stop.Cancel();
                stop = new CancellationTokenSource();
                int mine = ++generation;
                CancellationTokenSource lifetime = stop;
                Task.Run(async delegate {
                    try { await Run(mine, lifetime.Token); }
                    finally {
                        lock (gate) { if (ReferenceEquals(stop, lifetime)) stop = null; }
                        lifetime.Dispose();
                    }
                });
            }
        }

        public void Stop()
        {
            if (inbox != null) inbox.Connected(false);
            lock (gate) { if (stop != null) stop.Cancel(); }
        }

        internal static Uri SocketUri(string server)
        {
            Uri origin;
            if (!Uri.TryCreate(server, UriKind.Absolute, out origin) ||
                (origin.Scheme != "https" && origin.Scheme != "http" && origin.Scheme != "wss" && origin.Scheme != "ws") ||
                origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
                throw new ArgumentException("Direccion de servidor invalida");
            bool secure = origin.Scheme == "https" || origin.Scheme == "wss";
            if (!secure && !origin.IsLoopback)
                throw new ArgumentException("El servidor remoto requiere HTTPS para proteger el token");
            UriBuilder builder = new UriBuilder(origin);
            builder.Scheme = secure ? "wss" : "ws";
            builder.Port = origin.IsDefaultPort ? -1 : origin.Port;
            builder.Path = origin.AbsolutePath.TrimEnd('/') + "/api/game-link/ws";
            return builder.Uri;
        }

        void SetStatus(string text)
        {
            Log.Write(title + ": " + text);
            Action<string> handler = Status;
            if (handler != null) handler(text);
        }

        async Task Run(int mine, CancellationToken cancel)
        {
            TimeSpan backoff = TimeSpan.FromSeconds(2);
            while (!cancel.IsCancellationRequested && mine == generation)
            {
                Settings s = settings();
                string token = s.TokenFor(Game);
                if (token.Length == 0)
                {
                    SetStatus("sin token: pégalo con «Pegar token»");
                    return;
                }
                string error = "conexión perdida";
                int closeCode = 0;
                DateTime connectedAt = DateTime.MinValue;
                ClientWebSocket ws = new ClientWebSocket();
                CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                Task pinger = null;
                try
                {
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
                    Uri uri = SocketUri(s.Server);
                    SetStatus("conectando…");
                    welcomed = false;
                    await ws.ConnectAsync(uri, cancel);
                    lock (gate) {
                        if (mine != generation || cancel.IsCancellationRequested) return;
                        socket = ws;
                    }
                    connectedAt = DateTime.UtcNow;
                    await Send(new Dictionary<string, object> { { "type", "hello" }, { "game", Game }, { "version", Version } });
                    pinger = Ping(ws, connection.Token);
                    await Receive(ws, connection.Token);
                    if (ws.CloseStatus.HasValue && ws.CloseStatus.Value != WebSocketCloseStatus.Empty)
                    {
                        closeCode = (int)ws.CloseStatus.Value;
                        if (!string.IsNullOrEmpty(ws.CloseStatusDescription)) error = ws.CloseStatusDescription;
                    }
                    // .NET Framework's WebSocket drops Cloud90's own close codes
                    // (4401 token, 4403 access, 4426 version): closed before the
                    // welcome is one of those.
                    if (!welcomed && closeCode == 0)
                    {
                        closeCode = 4401;
                        error = "Cloud90 rechazó la conexión: revisa tu token (genera uno nuevo en Juegos › " + title + "), tu acceso o actualiza el Link";
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { error = e.InnerException != null ? e.InnerException.Message : e.Message; }
                finally
                {
                    if (mine == generation) {
                        welcomed = false;
                        if (inbox != null) inbox.Connected(false);
                    }
                    connection.Cancel();
                    // The Windows Framework compiler is C# 5 (no await in
                    // finally). Cancellation ends this connection's ping loop;
                    // observe its completion without sending through a new socket.
                    if (pinger != null) Fire(pinger);
                    lock (gate) { if (ReferenceEquals(socket, ws)) socket = null; }
                    ws.Dispose();
                    connection.Dispose();
                }
                // A connection that held for a while starts the waits over;
                // being refused (token, access, version) waits a minute.
                if (connectedAt != DateTime.MinValue && DateTime.UtcNow - connectedAt > TimeSpan.FromSeconds(30)) backoff = TimeSpan.FromSeconds(2);
                if (closeCode == 4401 || closeCode == 4403 || closeCode == 4426 || closeCode == 4400) backoff = TimeSpan.FromSeconds(60);
                // Too many attempts or bad tokens from this IP: the server
                // keeps refusing for a while, so don't knock every few seconds.
                if (error.Contains("(429)"))
                {
                    error = "Cloud90 pide esperar (demasiados intentos desde esta red)";
                    backoff = TimeSpan.FromSeconds(60);
                }
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
                    await SendTo(ws, new Dictionary<string, object> { { "type", "ping" } }, cancel);
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
                using (MemoryStream message = new MemoryStream())
                {
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        if (r.MessageType != WebSocketMessageType.Text || message.Length + r.Count > MaxMessageBytes)
                            throw new InvalidDataException("Mensaje de servidor invalido o demasiado grande");
                        message.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);
                    cancel.ThrowIfCancellationRequested();
                    await Handle(WireEncoding.GetString(message.ToArray()));
                }
            }
        }

        async Task Handle(string text)
        {
            Dictionary<string, object> msg;
            try { msg = json.Deserialize<Dictionary<string, object>>(text); }
            catch (Exception) { return; }
            if (msg == null) return;
            string type = Str(msg, "type");
            if (type == "welcome")
            {
                ApplyPolicy(msg);
                welcomed = true;
                if (inbox != null) inbox.Connected(true);
                SetStatus("conectado a Cloud90");
                await SendState();
            }
            else if (type == "policy") { ApplyPolicy(msg); }
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
                else if ((Game == "cuphead" || (Game == "ctrnf" && capabilities != null && Array.IndexOf(capabilities, "fall_void") >= 0)) && (string.IsNullOrEmpty(gameContext) || Str(msg, "epoch") != gameContext)) reason = "intento anterior";
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
                if (Game == "cuphead" || Game == "ctrnf") item.Epoch = Str(msg, "epoch");
                item.Ms = Math.Max(0, Math.Min(ms, 120000));
                item.By = Str(msg, "by");
                item.Safe = msg.ContainsKey("safe") && msg["safe"] is bool && (bool)msg["safe"];
                item.AllowDestructive = msg.ContainsKey("allowDestructive") && msg["allowDestructive"] is bool && (bool)msg["allowDestructive"];
                string arg = Str(msg, "arg");
                item.Arg = ArgValue.IsMatch(arg) ? arg : "";
                item.Sent = DateTime.UtcNow;
                if (!inbox.Add(item)) await Ack(id, false, "fila llena o identificador invalido");
            }
        }

        static string Str(Dictionary<string, object> msg, string key)
        {
            object v;
            return msg.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        // The game side's lines (Hades' console, MAME's acks.txt).
        public void OnModLine(string line)
        {
            if (line.StartsWith("ack "))
            {
                string rest = line.Substring(4);
                int space = rest.IndexOf(' ');
                if (space <= 0) return;
                string id = rest.Substring(0, space);
                string result = rest.Substring(space + 1).Trim();
                if (inbox != null && inbox.Take(id) == null && (Game == "ctrnf" || Game == "cuphead")) return;
                bool ok = result == "ok";
                Log.Write(title + ": efecto " + id + ": " + result);
                Fire(Ack(id, ok, ok ? null : result));
            }
            else if (line == "pause 1" || line == "pause 0")
            {
                SetPaused(line.EndsWith("1"));
            }
            else if (Game == "ctrnf" && line.StartsWith("context "))
            {
                string value = line.Substring(8);
                if (System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]{1,100}$")) SetContext(value);
            }
            else if (Game == "ctrnf" && line.StartsWith("capabilities "))
            {
                string[] values = line.Substring(13).Split(',');
                bool valid = values.Length <= 80;
                foreach (string value in values) if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9_]{1,40}$")) valid = false;
                SetCapabilities(valid ? values : new string[0]);
            }
            else if (line.StartsWith("state "))
            {
                inPlay = line.EndsWith("1");
                Fire(SendState());
            }
            else
            {
                Log.Write(title + ": juego: " + line);
            }
        }

        public void OnGameExited()
        {
            inPlay = false;
            if (inbox != null) inbox.Connected(false);
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
            var message = new Dictionary<string, object> { { "type", "state" }, { "paused", paused }, { "inPlay", inPlay } };
            if (Game == "cuphead" || Game == "ctrnf") message["epoch"] = gameContext;
            if ((Game == "cuphead" || Game == "ctrnf") && capabilities != null) message["capabilities"] = capabilities;
            return Send(message);
        }

        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        Task Send(Dictionary<string, object> message)
        {
            ClientWebSocket ws;
            lock (gate) { ws = socket; }
            return SendTo(ws, message, CancellationToken.None);
        }

        async Task SendTo(ClientWebSocket ws, Dictionary<string, object> message, CancellationToken cancel)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(message));
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                bool acquired = false;
                try {
                    await sendLock.WaitAsync(timeout.Token);
                    acquired = true;
                    if (ws.State == WebSocketState.Open)
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
                }
                catch (Exception) { }
                finally { if (acquired) sendLock.Release(); }
            }
        }
    }
}
