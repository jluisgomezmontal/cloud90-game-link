using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using Framework.FrameworkCore;
using Framework.FrameworkCore.Attributes.Logic;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using HarmonyLib;
using Gameplay.UI.Others.UIGameLogic;
using Newtonsoft.Json.Linq;
using Tools.Level.Layout;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Cloud90.Blasphemous {
    [BepInPlugin("cloud90.blasphemous", "Cloud90 Game Link", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin {
        private static Plugin instance;
        private static readonly Dictionary<string,int> Cooldowns = new Dictionary<string,int> {
            {"heal",15},{"health_max",45},{"damage",30},{"fervour_add",20},{"fervour_max",45},
            {"fervour_empty",45},{"flask_add",45},{"flask_remove",60},{"shield",30},{"kill",120},
            {"sword_boost",45},{"sword_weak",45},{"slow",45},{"no_dash",45},{"invulnerable",60},
            {"tears_add",60},{"tears_remove",60},{"ambush",0}
        };
        private static readonly Dictionary<string,string> Enemies = new Dictionary<string,string> {
            {"flagellant","EN01"},{"stoner","EN05"},{"bell_ghost","EN04"}
        };
        private static readonly HashSet<string> Troll = new HashSet<string> {
            "damage","fervour_empty","flask_remove","kill","sword_weak","slow","no_dash","tears_remove","ambush"
        };
        private static readonly HashSet<string> Persistent = new HashSet<string> {"kill","tears_add","tears_remove"};
        private readonly Dictionary<string,ulong> cooldowns = new Dictionary<string,ulong>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly Queue<object> acks = new Queue<object>();
        private string session, folder, epoch, room;
        private Penitent owner;
        private LevelInitializer level;
        private int generation, pid;
        private ulong nextPoll, shield, sword, slow, noDash, invulnerable;
        private float swordFactor, oldSpeed, appliedSpeed;
        private sealed class AmbushEnemy {
            public GameObject Object;
            public Enemy Enemy;
            public string Viewer, PendingId;
            public ulong Until;
        }
        private readonly List<AmbushEnemy> ambushes = new List<AmbushEnemy>();
        private bool paused, pauseSync, stopped = true;
        private bool canAmbush;
        private Harmony hooks;
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

        private void Awake() {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cloud90GameLink\blasphemous");
            try {
                string pointer = Path.Combine(root, "active-session.txt");
                if (!File.Exists(pointer) || new FileInfo(pointer).Length != 32) return;
                session = File.ReadAllText(pointer);
                if (session.Length != 32 || session.Any(c => !"0123456789abcdef".Contains(c.ToString()))) return;
                folder = Path.Combine(root, session);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveJson;
                using (var process = Process.GetCurrentProcess()) pid = process.Id;
                hooks = new Harmony("cloud90.blasphemous");
                Patch(typeof(PenitentSword), "Attack", new[] {typeof(Hit)}, "Sword");
                Patch(typeof(Penitent), "Damage", new[] {typeof(Hit)}, "Incoming");
                Patch(typeof(Ability), "Cast", Type.EmptyTypes, "Cast");
                instance = this;
            } catch (Exception error) {
                Logger.LogError(error);
                folder = null;
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveJson;
                if (hooks != null) hooks.UnpatchSelf();
            }
        }

        private static Assembly ResolveJson(object sender, ResolveEventArgs args) {
            var name = new AssemblyName(args.Name);
            if (name.Name != "Newtonsoft.Json" || name.Version != new Version(13, 0, 0, 0)) return null;
            using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Cloud90.Newtonsoft.Json.dll")) {
                var bytes = new byte[checked((int)stream.Length)];
                using (var reader = new BinaryReader(stream)) bytes = reader.ReadBytes(bytes.Length);
                return Assembly.Load(bytes);
            }
        }

        private void Patch(Type type, string name, Type[] args, string prefix) {
            var method = AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            hooks.Patch(method, prefix: new HarmonyMethod(typeof(Plugin), prefix));
        }

        private void OnDestroy() {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveJson;
            Clear();
            if (hooks != null) hooks.UnpatchSelf();
            if (instance == this) instance = null;
        }

        private static void Sword(PenitentSword __instance, ref Hit __0) {
            var mod = instance;
            if (mod != null && mod.Running(mod.sword) &&
                __instance.GetComponentInParent<Penitent>() == mod.owner) __0.DamageAmount *= mod.swordFactor;
        }

        private static bool Incoming(Penitent __instance, Hit __0) {
            var mod = instance;
            return mod == null || __instance != mod.owner || !mod.Running(mod.invulnerable) ||
                __0.Unnavoidable || !__0.AttackingEntity || !__0.AttackingEntity.GetComponentInParent<Enemy>();
        }

        private static bool Cast(Ability __instance) {
            var mod = instance;
            return mod == null || !mod.Running(mod.noDash) || __instance != mod.owner.Dash;
        }

        private bool Running(ulong end) {
            return !stopped && !paused && GetTickCount64() < end && owner && Playable() == owner;
        }

        private static Penitent Playable() {
            var logic = Core.Logic;
            if (logic == null || logic.IsPaused || logic.CurrentState != LogicStates.Playing ||
                Core.LevelManager == null || Core.LevelManager.InCinematicsChangeLevel != LevelManager.CinematicsChangeLevel.No ||
                !logic.CurrentLevelConfig || Time.timeScale <= 0f) return null;
            var player = logic.Penitent;
            return player && player.isActiveAndEnabled && !player.Status.Dead && !player.Status.AbilitiesDisabled &&
                !player.IsOnExecution && !player.IsPickingCollectibleItem && player.PlatformCharacterInput &&
                !player.PlatformCharacterInput.Blocked ? player : null;
        }

        private void Update() {
            if (folder == null) return;
            ulong now = GetTickCount64();
            var player = Playable();
            var activeLevel = Core.Logic == null ? null : Core.Logic.CurrentLevelConfig;
            string scene = SceneManager.GetActiveScene().name;
            if (!ReferenceEquals(owner, player) || !ReferenceEquals(level, activeLevel) || room != scene || epoch == null) {
                Clear();
                owner = player;
                level = activeLevel;
                room = scene;
                epoch = session + "_" + ++generation;
            }
            if (Application.isFocused && Input.GetKeyDown(KeyCode.F9)) {
                paused = !paused;
                pauseSync = paused;
                if (paused) Clear();
            }
            if (slow != 0 && (now >= slow || !Grounded(player))) RestoreSpeed();
            for (int i = ambushes.Count - 1; i >= 0; i--) {
                var ambush = ambushes[i];
                if (ambush.PendingId != null && ambush.Enemy && ambush.Enemy.isActiveAndEnabled) {
                    Ack(ambush.PendingId, "ok");
                    ambush.PendingId = null;
                }
                if (now >= ambush.Until || !ambush.Enemy || ambush.Enemy.Status.Dead) RemoveSpawn(i);
            }
            if (now < nextPoll) return;
            nextPoll = now + 125;
            Poll(now);
        }

        private void Poll(ulong now) {
            stopped = true;
            canAmbush = owner && !FindObjectOfType<BossHealth>();
            try {
                using (var stream = new FileStream(Path.Combine(folder, "inbox.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) {
                    if (stream.Length <= 65536) {
                        var data = JObject.Parse(reader.ReadToEnd());
                        ulong beat = data.Value<ulong>("heartbeatMs");
                        if (data.Value<string>("session") == session && beat > 0 && beat <= now && now - beat <= 2000) {
                            stopped = data.Value<bool?>("paused") != false;
                            if (stopped) pauseSync = false;
                            else if (paused && !pauseSync) paused = false;
                            var commands = data["effects"] as JArray;
                            if (commands != null && commands.Count <= 20) foreach (var token in commands) {
                                var command = token as JObject;
                                if (command == null) continue;
                                string id = command.Value<string>("id");
                                if (String.IsNullOrEmpty(id) || id.Length > 80 ||
                                    id.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) ||
                                    seen.Count >= 100000 || !seen.Add(id)) continue;
                                string result;
                                try { result = Apply(data, command, now, id); }
                                catch (Exception error) { Logger.LogWarning(error); result = "resultado incierto; efecto sin confirmar"; }
                                if (result != null) Ack(id, result);
                            }
                        }
                    }
                }
            } catch (Exception error) {
                if (!(error is IOException)) Logger.LogWarning(error);
            }
            if (stopped || paused || !owner) Clear();
            try {
                string path = Path.Combine(folder, "status.json");
                string temp = path + ".tmp";
                File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    session, epoch, pid, heartbeatMs = now, bridgeVersion = "cloud90-blasphemous-1",
                    inPlay = owner != null, paused,
                    capabilities = owner ? Cooldowns.Keys.Where(effect => effect != "ambush" || canAmbush).ToArray() : new string[0], acks
                }));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            } catch (IOException) { }
        }

        private void Ack(string id, string result) {
            acks.Enqueue(new {id, result});
            if (acks.Count > 100) acks.Dequeue();
        }

        private static bool Grounded(Penitent player) {
            return player && player.PlatformCharacterController && player.PlatformCharacterController.IsGrounded && !player.IsDashing;
        }

        private static bool Set(VariableAttribute stat, float value) {
            float before = stat.Current;
            stat.Current = Mathf.Clamp(value, 0f, stat.CurrentMax);
            return !Mathf.Approximately(stat.Current, before);
        }

        private string Apply(JObject data, JObject cmd, ulong now, string id) {
            if (stopped || paused || !owner || Playable() != owner) return "fuera de partida o en pausa";
            if (cmd.Value<string>("epoch") != epoch) return "intento anterior";
            ulong expires = cmd.Value<ulong>("expiresMs");
            if (expires <= now || expires - now > 10000) return "caducado";
            string effect = cmd.Value<string>("effect");
            int cooldown;
            if (effect == null || !Cooldowns.TryGetValue(effect, out cooldown)) return "efecto desconocido";
            if (Troll.Contains(effect) && now < shield) return "escudo activo";
            ulong allowedAt;
            if (cooldowns.TryGetValue(effect, out allowedAt) && now < allowedAt) return "en enfriamiento";
            if ((effect == "kill" || effect == "ambush") &&
                (data.Value<bool?>("noDeath") != false || cmd.Value<bool?>("safe") != false)) return "modo sin morir activo";
            if (Persistent.Contains(effect) && (data.Value<bool?>("allowDestructive") != true ||
                cmd.Value<bool?>("allowDestructive") != true)) return "permiso requerido";
            var stats = owner.Stats;
            bool changed = true;
            int ms = Math.Max(1000, cmd.Value<int>("ms"));
            if ((effect == "heal" || effect == "health_max" || effect == "damage") &&
                Core.PenitenceManager.UseStocksOfHealth) return "penitencia de vida incompatible";
            switch (effect) {
                case "heal": changed = Set(stats.Life, stats.Life.Current + stats.Life.CurrentMax * .25f); break;
                case "health_max": changed = Set(stats.Life, stats.Life.CurrentMax); break;
                case "damage":
                    if (stats.Life.Current <= 1) return "vida al límite";
                    changed = Set(stats.Life, Math.Max(1, stats.Life.Current - stats.Life.CurrentMax * .2f));
                    break;
                case "fervour_add": changed = Set(stats.Fervour, stats.Fervour.Current + stats.Fervour.CurrentMax * .3f); break;
                case "fervour_max": changed = Set(stats.Fervour, stats.Fervour.CurrentMax); break;
                case "fervour_empty":
                    if (owner.PrayerCast.Casting) return "plegaria en curso";
                    changed = Set(stats.Fervour, 0); break;
                case "flask_add":
                case "flask_remove":
                    if (owner.Healing.IsHealing) return "curación en curso";
                    changed = Set(stats.Flask, stats.Flask.Current + (effect == "flask_add" ? 1 : -1)); break;
                case "shield": shield = now + (ulong)Math.Min(ms, 15000); break;
                case "kill": owner.KillInstanteneously(); changed = owner.Status.Dead; break;
                case "sword_boost":
                case "sword_weak":
                    swordFactor = effect == "sword_boost" ? 1.25f : .75f;
                    sword = now + (ulong)Math.Min(ms, 10000); break;
                case "slow":
                    if (!Grounded(owner)) return "requiere estar en suelo";
                    RestoreSpeed();
                    oldSpeed = owner.PlatformCharacterController.MaxWalkingSpeed;
                    appliedSpeed = oldSpeed * .7f;
                    owner.PlatformCharacterController.MaxWalkingSpeed = appliedSpeed;
                    slow = now + (ulong)Math.Min(ms, 4000); break;
                case "no_dash":
                    if (!Grounded(owner)) return "requiere estar en suelo";
                    noDash = now + (ulong)Math.Min(ms, 3000); break;
                case "invulnerable": invulnerable = now + (ulong)Math.Min(ms, 5000); break;
                case "tears_add":
                case "tears_remove":
                    changed = Set(stats.Purge, stats.Purge.Current + (effect == "tears_add" ? 500 : -500)); break;
                case "ambush":
                    string result = Spawn(cmd.Value<string>("arg"), cmd.Value<string>("by"), now, id);
                    if (result != null) return result;
                    break;
            }
            if (!changed) return "medidor al límite";
            if (cooldown > 0) cooldowns[effect] = now + (ulong)cooldown * 1000;
            return effect == "ambush" ? null : "ok";
        }

        private string Spawn(string choice, string sender, ulong now, string id) {
            if (!canAmbush) return "emboscada no disponible durante un jefe";
            if (!Grounded(owner)) return "requiere estar en suelo";
            string model;
            if (!Enemies.TryGetValue(choice ?? "", out model)) return "enemigo desconocido";
            var points = level.GetComponentsInChildren<EnemySpawnPoint>();
            var point = points.FirstOrDefault(p => p && p.isActiveAndEnabled && !p.SpawnOnArena &&
                !p.EnemySpawnDisabled && p.SelectedEnemy && p.SelectedEnemy.GetComponent<Enemy>() &&
                p.SelectedEnemy.GetComponent<Enemy>().Id == model &&
                Mathf.Abs(p.Position.y - owner.transform.position.y) <= (choice == "bell_ghost" ? 5f : 2f) &&
                Vector2.Distance(p.Position, owner.transform.position) >= 4f &&
                Vector2.Distance(p.Position, owner.transform.position) <= 10f &&
                (!p.SpawnedEnemy || p.SpawnedEnemy.Status.Dead));
            if (!point) return "sin punto compatible para este enemigo en la sala";
            var spawned = Instantiate(point.SelectedEnemy, point.Position, Quaternion.identity);
            spawned.transform.SetParent(level.transform, true);
            var enemy = spawned.GetComponent<Enemy>();
            enemy.SpawningId = -1;
            enemy.purgePointsWhenDead = 0;
            var behavior = spawned.GetComponentInChildren<EnemyBehaviour>();
            if (behavior) behavior.EnableBehaviourOnLoad = true;
            ambushes.Add(new AmbushEnemy {
                Object = spawned, Enemy = enemy, PendingId = id, Until = now + 30000,
                Viewer = String.Concat((sender ?? "").Where(c => !Char.IsControl(c) && c != '<' && c != '>').Take(28).ToArray())
            });
            return null;
        }

        private void OnGUI() {
            var camera = Camera.main;
            if (Event.current.type != EventType.Repaint || !camera || ambushes.Count == 0) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = false };
            style.normal.textColor = Color.white;
            foreach (var ambush in ambushes) {
                if (!Running(ambush.Until) || !ambush.Enemy || ambush.Enemy.Status.Dead ||
                    String.IsNullOrEmpty(ambush.Viewer)) continue;
                var renderer = ambush.Object.GetComponentInChildren<SpriteRenderer>();
                if (!renderer || !renderer.isVisible) continue;
                var anchor = renderer.bounds.center;
                anchor.y = renderer.bounds.max.y + .25f;
                var pos = camera.WorldToScreenPoint(anchor);
                if (pos.z <= 0 || pos.x < 0 || pos.x > Screen.width || pos.y < 0 || pos.y > Screen.height) continue;
                GUI.Label(new Rect(pos.x - 125, Screen.height - pos.y - 20, 250, 22), ambush.Viewer, style);
            }
        }

        private void RestoreSpeed() {
            if (slow != 0 && owner && owner.PlatformCharacterController &&
                Mathf.Approximately(owner.PlatformCharacterController.MaxWalkingSpeed, appliedSpeed))
                owner.PlatformCharacterController.MaxWalkingSpeed = oldSpeed;
            slow = 0;
        }

        private void RemoveSpawn(int index) {
            var ambush = ambushes[index];
            if (ambush.PendingId != null) Ack(ambush.PendingId, "resultado incierto; enemigo sin confirmar");
            if (ambush.Object) { ambush.Object.SetActive(false); Destroy(ambush.Object); }
            ambushes.RemoveAt(index);
        }

        private void Clear() {
            shield = sword = noDash = invulnerable = 0;
            RestoreSpeed();
            for (int i = ambushes.Count - 1; i >= 0; i--) RemoveSpawn(i);
        }
    }
}
