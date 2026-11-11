using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System.Text.Json;

namespace Celeste.Mod.Cloud90;

public sealed class Cloud90Module : EverestModule {
    private const string BridgeVersion = "cloud90-celeste-1";
    private static readonly Dictionary<string, int> Cooldowns = new() {
        ["dash_refill"] = 10, ["stamina_refill"] = 10, ["shield"] = 20,
        ["dash_remove"] = 30, ["wind"] = 45, ["short_dash"] = 45,
        ["mirror_controls"] = 60, ["retry_room"] = 120, ["spawn_chaser"] = 90, ["rescue_spring"] = 60
    };
    private readonly Dictionary<string, long> cooldowns = new();
    private readonly HashSet<string> seen = new();
    private readonly Queue<object> acks = new();
    private readonly List<(string Id, Level Level, Entity Entity)> pendingEntities = new();
    private string? directory;
    private string session = "", epoch = "", room = "";
    private Level? lastLevel;
    private Player? lastPlayer;
    private int generation;
    private long pollAt, shieldUntil, windUntil, mirrorUntil, shortUntil, dashLockUntil, chaserUntil, springUntil;
    private int windDirection;
    private bool shortUsed, shortenedCurrentDash;
    private bool localPaused, pausePressed, pauseAwaitingSync;
    private Entity? chaser, spring;

    public override void Load() {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud90GameLink", "celeste");
        try {
            string pointer = Path.Combine(root, "active-session.txt");
            if (File.Exists(pointer) && new FileInfo(pointer).Length == 32) {
                session = File.ReadAllText(pointer);
                if (session.Length == 32 && session.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) {
                    directory = Path.Combine(root, session);
                }
            }
        } catch (IOException) {
            // Celeste can still run when the desktop client is absent.
        }
        On.Monocle.Engine.Update += Update;
        On.Celeste.Player.Update += UpdatePlayer;
        On.Celeste.Player.RefillDash += RefillDash;
    }

    public override void Unload() {
        On.Monocle.Engine.Update -= Update;
        On.Celeste.Player.Update -= UpdatePlayer;
        On.Celeste.Player.RefillDash -= RefillDash;
        ClearEffects();
    }

    private static Player? ActivePlayer(Level? level) {
        if (level == null || level.Paused || level.Frozen || level.Transitioning || level.InCutscene ||
            level.Session.Area.GetLevelSet() != "Celeste") return null;
        Player? player = level.Tracker.GetEntity<Player>();
        if (player == null || !player.Active || player.Dead || player.StateMachine.Locked) return null;
        int state = player.StateMachine.State;
        return state is Player.StNormal or Player.StClimb or Player.StDash ? player : null;
    }

    private static bool StillInRoom(Level level, Player player) {
        return !level.Paused && !level.Transitioning && !level.InCutscene && !player.Dead &&
            level.Tracker.GetEntity<Player>() == player;
    }

    private static bool MovementRoom(Level level) =>
        level.Session.Area.Mode == AreaMode.Normal && level.Session.Area.ID is 1 or 2 or 3 or 4 or 5;

    private void Update(On.Monocle.Engine.orig_Update original, Engine engine, GameTime gameTime) {
        original(engine, gameTime);
        if (directory == null) return;
        Level? level = Engine.Scene as Level;
        Player? player = ActivePlayer(level);
        string nextRoom = level?.Session.Level ?? "";
        long now = Environment.TickCount64;
        bool sameContext = level == lastLevel && nextRoom == room && player == lastPlayer;
        if (!sameContext || epoch.Length == 0) {
            ClearEffects();
            epoch = session + "_" + ++generation;
            lastLevel = level;
            lastPlayer = player;
            room = nextRoom;
        }
        bool key = engine.IsActive && Keyboard.GetState().IsKeyDown(Keys.F9);
        if (key && !pausePressed) {
            localPaused = !localPaused;
            pauseAwaitingSync = localPaused;
            if (localPaused) ClearEffects();
        }
        pausePressed = key;
        ExpireEffects(now);
        foreach (var pending in pendingEntities) {
            Acknowledge(pending.Id, pending.Entity.Scene == pending.Level && player != null && level == pending.Level
                ? "ok" : "resultado incierto; entidad sin confirmar");
        }
        pendingEntities.Clear();
        if (now < pollAt) return;
        pollAt = now + 125;
        Poll(level, player, now);
    }

    private bool RefillDash(On.Celeste.Player.orig_RefillDash original, Player player) {
        if (player == lastPlayer && player.Scene is Level level && level == lastLevel &&
            level.Session.Level == room && StillInRoom(level, player) &&
            Environment.TickCount64 < dashLockUntil && player.OnGround()) return false;
        return original(player);
    }

    private void UpdatePlayer(On.Celeste.Player.orig_Update original, Player player) {
        long now = Environment.TickCount64;
        bool ownsPlayer = player == lastPlayer && player.Scene is Level level && level == lastLevel &&
            level.Session.Level == room && StillInRoom(level, player) && !level.Frozen;
        bool mirrored = ownsPlayer && now < mirrorUntil;
        bool oldDirection = Input.MoveX.Inverted;
        if (mirrored) Input.MoveX.Inverted = !oldDirection;
        try {
            if (ownsPlayer && player.StateMachine.State == Player.StDash && now < shortUntil &&
                (!shortUsed || shortenedCurrentDash) && player.Speed.LengthSquared() > 120f * 120f) {
                shortUsed = true;
                shortenedCurrentDash = true;
                player.Speed = Vector2.Normalize(player.Speed) * 120f;
            }
            original(player);
            if (!ownsPlayer || player.Dead) return;
            if (player.StateMachine.State != Player.StDash) shortenedCurrentDash = false;
            if (now < windUntil && player.Scene is Level current && current.Wind == Vector2.Zero &&
                player.StateMachine.State == Player.StNormal) {
                player.MoveH(windDirection * 28f * Engine.DeltaTime);
            }
        } finally {
            if (mirrored) Input.MoveX.Inverted = oldDirection;
        }
    }

    private void Poll(Level? level, Player? player, long now) {
        bool playing = level != null && player != null;
        bool paused = true;
        try {
            string path = Path.Combine(directory!, "inbox.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 65536) {
                using var doc = JsonDocument.Parse(stream);
                JsonElement inbox = doc.RootElement;
                if (Text(inbox, "session") == session && Number(inbox, "heartbeatMs") is long beat &&
                    beat > 0 && beat <= now && now - beat <= 2000) {
                    paused = Flag(inbox, "paused", true);
                    if (paused) pauseAwaitingSync = false;
                    else if (localPaused && !pauseAwaitingSync) localPaused = false;
                    if (Property(inbox, "effects", out var commands) &&
                        commands.ValueKind == JsonValueKind.Array && commands.GetArrayLength() <= 20) {
                        foreach (var command in commands.EnumerateArray()) {
                            string id = Text(command, "id");
                            if (id.Length is < 1 or > 80 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
                                seen.Count >= 100000 || !seen.Add(id)) continue;
                            string? result;
                            try {
                                result = playing && !paused && !localPaused
                                    ? Apply(inbox, command, level!, player!, now, id)
                                    : "fuera de una partida activa o en pausa";
                            } catch (Exception error) {
                                Logger.Log(LogLevel.Warn, "Cloud90GameLink", error.ToString());
                                result = "resultado incierto; efecto sin confirmar";
                            }
                            if (result != null) Acknowledge(id, result);
                        }
                    }
                }
            }
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
            // A missing, stale or malformed inbox cannot apply effects.
        }
        if (paused || localPaused || !playing) ClearEffects();
        string[] capabilities = playing ? Capabilities(level!).ToArray() : Array.Empty<string>();
        try {
            string path = Path.Combine(directory!, "status.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new {
                session, epoch, bridgeVersion = BridgeVersion, pid = Environment.ProcessId,
                heartbeatMs = now, inPlay = playing, paused = localPaused, capabilities, acks
            }));
            File.Move(temp, path, true);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            // Retry on the next heartbeat.
        }
    }

    private void Acknowledge(string id, string result) {
        acks.Enqueue(new { id, result });
        if (acks.Count > 100) acks.Dequeue();
    }

    private static IEnumerable<string> Capabilities(Level level) {
        foreach (string effect in Cooldowns.Keys) {
            if (effect is "dash_remove" or "wind" or "short_dash" or "mirror_controls" or "rescue_spring") {
                if (!MovementRoom(level)) continue;
            }
            if (effect == "spawn_chaser" && !CanSpawnIn(level)) continue;
            yield return effect;
        }
    }

    private string? Apply(JsonElement inbox, JsonElement command, Level level, Player player, long now, string id) {
        if (ActivePlayer(level) != player) return "Madeline no tiene control";
        if (Text(command, "epoch") != epoch) return "intento anterior";
        long expiry = Number(command, "expiresMs");
        if (expiry <= now || expiry - now > 10000) return "caducado";
        string effect = Text(command, "effect"), arg = Text(command, "arg");
        if (!Capabilities(level).Contains(effect)) return "efecto no disponible en esta sala";
        if (cooldowns.GetValueOrDefault(effect) > now) return "en enfriamiento";
        bool sabotage = effect is "dash_remove" or "wind" or "short_dash" or "mirror_controls" or "retry_room" or "spawn_chaser";
        if (sabotage && now < shieldUntil) return "escudo activo";
        if (effect is "retry_room" or "spawn_chaser") {
            if (Flag(inbox, "noDeath", true) || Flag(command, "safe", true)) return "modo sin morir activo";
            if (effect == "retry_room" && (!Flag(inbox, "allowDestructive", false) ||
                                            !Flag(command, "allowDestructive", false))) return "permiso requerido";
        }
        long Duration(int limit) => now + Math.Clamp(Number(command, "ms"), 1000, limit);
        switch (effect) {
            case "dash_refill":
                if (player.Dashes >= player.MaxDashes) return "dash lleno o no recargable";
                int oldDashes = player.Dashes;
                dashLockUntil = 0;
                if (!player.RefillDash() || player.Dashes <= oldDashes) return "dash no recargable";
                break;
            case "stamina_refill":
                float oldStamina = player.Stamina;
                player.RefillStamina();
                if (player.Stamina <= oldStamina) return "resistencia llena";
                break;
            case "shield":
                shieldUntil = Duration(12000);
                break;
            case "dash_remove":
                if (!player.OnGround() || player.StateMachine.State != Player.StNormal || player.Dashes <= 0)
                    return "requiere suelo y un dash disponible";
                player.Dashes--;
                dashLockUntil = now + 1250;
                break;
            case "wind":
                if (arg is not ("left" or "right")) return "dirección desconocida";
                if (level.Wind != Vector2.Zero) return "la sala ya tiene viento";
                windDirection = arg == "left" ? -1 : 1;
                windUntil = Duration(4000);
                break;
            case "short_dash":
                if (player.StateMachine.State == Player.StDash) return "hay un dash en curso";
                shortUsed = shortenedCurrentDash = false;
                shortUntil = Duration(8000);
                break;
            case "mirror_controls":
                mirrorUntil = Duration(4000);
                break;
            case "retry_room":
                if (player.Die(Vector2.Zero) == null) return "el juego no permitió reiniciar";
                break;
            case "spawn_chaser":
                if (chaser != null) return "ya hay un perseguidor";
                chaser = SpawnChaser(level, player, arg);
                if (chaser == null) return "enemigo o posición incompatible";
                chaserUntil = Duration(12000);
                pendingEntities.Add((id, level, chaser));
                break;
            case "rescue_spring":
                if (spring != null) return "ya hay un muelle";
                spring = SpawnSpring(level, player);
                if (spring == null) return "no hay suelo libre para el muelle";
                springUntil = Duration(10000);
                pendingEntities.Add((id, level, spring));
                break;
        }
        cooldowns[effect] = now + Cooldowns[effect] * 1000L;
        return effect is "spawn_chaser" or "rescue_spring" ? null : "ok";
    }

    private static bool CanSpawnIn(Level level) => level.Session.Area.Mode == AreaMode.Normal &&
        level.Session.Area.ID is 2 or 5;

    private static Entity? SpawnChaser(Level level, Player player, string arg) {
        int chapter = level.Session.Area.ID;
        if (arg == "badeline" && (chapter != 2 || level.Entities.FindFirst<BadelineOldsite>() != null)) return null;
        if (arg == "seeker" && chapter != 5 && chapter != 2) return null;
        if (arg is not ("badeline" or "seeker")) return null;
        foreach (int offset in new[] { -48, 48, -32, 32 }) {
            Vector2 position = player.Center + new Vector2(offset, -12);
            var area = new Rectangle((int)position.X - 10, (int)position.Y - 10, 20, 20);
            if (!level.Bounds.Contains(area) || level.CollideCheck<Solid>(area)) continue;
            Entity enemy = arg == "badeline" ? new BadelineOldsite(position, 0) : new Seeker(position, Array.Empty<Vector2>());
            level.Add(enemy);
            return enemy;
        }
        return null;
    }

    private static Entity? SpawnSpring(Level level, Player player) {
        if (!player.OnGround()) return null;
        foreach (int offset in new[] { 20, -20 }) {
            Vector2 position = player.BottomCenter + new Vector2(offset, 0);
            var clear = new Rectangle((int)position.X - 8, (int)position.Y - 10, 16, 10);
            var support = new Rectangle(clear.X, clear.Bottom, clear.Width, 2);
            if (!level.Bounds.Contains(clear) || level.CollideCheck<Solid>(clear) ||
                !level.CollideCheck<Solid>(support)) continue;
            var entity = new Spring(position, Spring.Orientations.Floor, true);
            level.Add(entity);
            return entity;
        }
        return null;
    }

    private void ExpireEffects(long now) {
        if (now >= chaserUntil) Remove(ref chaser);
        if (now >= springUntil) Remove(ref spring);
    }

    private void ClearEffects() {
        shieldUntil = windUntil = mirrorUntil = shortUntil = dashLockUntil = 0;
        shortUsed = shortenedCurrentDash = false;
        Remove(ref chaser);
        Remove(ref spring);
    }

    private void Remove(ref Entity? entity) {
        if (entity != null) {
            entity.Collidable = false;
            entity.Active = false;
            lastLevel?.Remove(entity);
        }
        entity = null;
    }

    private static bool Property(JsonElement value, string name, out JsonElement property) {
        property = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out property);
    }
    private static string Text(JsonElement value, string name) =>
        Property(value, name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static long Number(JsonElement value, string name) =>
        Property(value, name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long number) ? number : 0;
    private static bool Flag(JsonElement value, string name, bool fallback) =>
        Property(value, name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
}
