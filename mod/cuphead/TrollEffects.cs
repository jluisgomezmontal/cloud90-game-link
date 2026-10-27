using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Cloud90.Cuphead
{
    public sealed partial class Plugin
    {
        const string Deferred = "__pending";
        static readonly MethodInfo SuperSetter = AccessTools.PropertySetter(typeof(PlayerStatsManager), "SuperMeter");
        static readonly MethodInfo SuperChanged = AccessTools.Method(typeof(PlayerStatsManager), "OnSuperChanged");
        static readonly MethodInfo StoneSetter = AccessTools.PropertySetter(typeof(PlayerStatsManager), "StoneTime");
        static readonly MethodInfo NativeDeath = AccessTools.Method(typeof(PlayerStatsManager), "OnStatsDeath");
        static readonly FieldInfo EnemyShadow = AccessTools.Field(typeof(PlatformingLevelGroundMovementEnemy), "shadow");
        readonly List<SpawnedEnemy> spawned = new List<SpawnedEnemy>();
        bool liveNoDeath = true, liveDestructive;
        bool burnSafe;
        internal bool settingStone;
        float burnDelay, iceVelocity, windDirection = 1, nextAutoJump;
        int autoJumpFrame = -1;
        Command pendingDestructive;
        float pendingDelay;
        internal PlayerStatsManager stoneOwner;
        Camera zoomCamera;
        float originalSize, appliedSize, explosionTime;
        string[] capabilityCache = new string[0];
        float nextCapabilities;

        sealed class SpawnedEnemy { public GameObject actor, shadow; public float life = 15f; }

        string[] Capabilities()
        {
            if (!IsPlayable()) return new string[0];
            if (Time.realtimeSinceStartup < nextCapabilities) return capabilityCache;
            nextCapabilities = Time.realtimeSinceStartup + 0.5f;
            var list = new List<string> { "heal", "damage", "super_add", "invincible", "no_shoot", "invert_controls", "slow", "super_speed", "shield", "burn", "explosion", "restart_level" };
            if (SuperSetter != null && SuperChanged != null) list.Add("super_drain");
            if (NativeDeath != null) list.Add("kill");
            if (player is LevelPlayerController) list.AddRange(new[] { "no_dash", "no_jump", "ice_floor", "wind_push", "auto_jump" });
            if (player is PlanePlayerController && StoneSetter != null) list.Add("petrify");
            list.Add("weak_shots");
            if (CupheadLevelCamera.Current != null && CupheadLevelCamera.Current.GetComponent<Camera>() != null) list.Add("camera_zoom");
            if (EnemyProvider() != null) list.Add("spawn_enemies");
            capabilityCache = list.ToArray();
            return capabilityCache;
        }

        void ClearEffects()
        {
            effects.Clear();
            if (pendingDestructive != null) Complete(pendingDestructive.id, "orden cancelada");
            pendingDestructive = null;
            burnDelay = iceVelocity = explosionTime = 0;
            autoJumpFrame = -1;
            ClearStone(); RestoreZoom();
            for (int i = spawned.Count - 1; i >= 0; i--) DestroySpawn(spawned[i]);
            spawned.Clear();
            nextCapabilities = 0;
        }

        void Complete(string id, string result)
        {
            Ack ack;
            if (!seen.TryGetValue(id, out ack) || ack.result != Deferred) return;
            ack.result = result ?? "ok";
            acks.Add(ack);
            while (acks.Count > 128) acks.RemoveAt(0);
            Logger.LogInfo("Pending effect " + id + ": " + ack.result);
        }

        string Hurt(bool safe)
        {
            if (effects.Active("shield")) return "escudo activo";
            if ((safe || liveNoDeath) && player.stats.Health <= 1) return "modo sin morir";
            int before = player.stats.Health;
            player.damageReceiver.TakeDamage(new DamageDealer.DamageInfo(1f, DamageDealer.Direction.Neutral, player.center, DamageDealer.DamageSource.Enemy));
            return player.stats.Health < before ? null : "jugador protegido";
        }

        string ApplyTroll(Command c)
        {
            if (Array.IndexOf(Capabilities(), c.effect) < 0) return "efecto no compatible en este nivel";
            switch (c.effect)
            {
                case "ice_floor": case "auto_jump": case "weak_shots": case "camera_zoom":
                    effects.Set(c.effect, c.ms); return null;
                case "wind_push":
                    windDirection = c.arg == "left" ? -1f : c.arg == "right" ? 1f : UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    effects.Set(c.effect, c.ms); return null;
                case "explosion":
                    string hurt = Hurt(c.safe);
                    if (hurt != null) return hurt;
                    explosionTime = 0.65f;
                    return null;
                case "burn":
                    if (effects.Active("burn")) return "quemadura ya activa";
                    string first = Hurt(c.safe);
                    if (first != null) return first;
                    burnSafe = c.safe; burnDelay = 3f;
                    effects.Set("burn", c.ms); return null;
                case "super_drain":
                    if (!player.stats.CanGainSuperMeter || player.stats.SuperMeter <= 0) return "energia no disponible";
                    SuperSetter.Invoke(player.stats, new object[] { 0f });
                    SuperChanged.Invoke(player.stats, new object[] { false });
                    return player.stats.SuperMeter == 0f ? null : "no se pudo vaciar energia";
                case "petrify":
                    if (player.stats.StoneTime > 0) return "jugador ya petrificado";
                    settingStone = true;
                    try { player.stats.GetStoned(Math.Min(3f, c.ms / 1000f)); }
                    finally { settingStone = false; }
                    if (player.stats.StoneTime <= 0) return "petrificacion no aplicada";
                    stoneOwner = player.stats;
                    effects.Set("petrify", Math.Min(3000, c.ms)); return null;
                case "spawn_enemies": return SpawnEnemies(c.arg == "2" ? 2 : 1);
                case "kill": case "restart_level":
                    string denied = EffectState.DestructiveRefusal(c, liveNoDeath, liveDestructive, effects.Active("shield"));
                    if (denied != null) return denied;
                    if (pendingDestructive != null) return "ya hay una orden pendiente";
                    pendingDestructive = c; pendingDelay = 3f;
                    return Deferred;
                default: return "efecto desconocido";
            }
        }

        void TickTrolls(float delta)
        {
            if (pendingDestructive != null)
            {
                Command c = pendingDestructive;
                string denied = EffectState.DestructiveRefusal(c, liveNoDeath, liveDestructive, effects.Active("shield"));
                if (c.epoch != epoch) denied = "intento anterior";
                if (Now >= c.expiresMs) denied = "comando caducado";
                if (denied != null) { pendingDestructive = null; Complete(c.id, denied); }
                else if (Live)
                {
                    pendingDelay -= delta;
                    if (pendingDelay <= 0)
                    {
                        pendingDestructive = null;
                        string currentPermission = VerifyDestructivePolicy(c);
                        if (currentPermission != null) { Complete(c.id, currentPermission); return; }
                        if (c.effect == "kill")
                        {
                            if (Has("invincible") || !player.CanTakeDamage) Complete(c.id, "jugador protegido");
                            else { try { player.stats.SetHealth(0); NativeDeath.Invoke(player.stats, null); Complete(c.id, player.IsDead ? null : "muerte no confirmada"); } catch { Complete(c.id, "no se pudo confirmar muerte"); } }
                        }
                        else if (SceneLoader.CurrentlyLoading) Complete(c.id, "transicion en curso");
                        else
                        {
                            PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerOne, false);
                            try { SceneLoader.ReloadLevel(); Complete(c.id, SceneLoader.CurrentlyLoading ? null : "reinicio no iniciado"); }
                            catch { PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerOne, true); Complete(c.id, "no se pudo reiniciar"); }
                        }
                    }
                }
            }
            if (!Live) { RestoreZoom(); return; }
            if (effects.Active("shield"))
            {
                foreach (string name in effects.Names()) if (EffectState.Troll(name)) effects.Remove(name);
                burnDelay = iceVelocity = 0; ClearStone(); RestoreZoom();
            }
            if (effects.Active("burn") && burnDelay > 0)
            {
                burnDelay -= delta;
                if (burnDelay <= 0) Hurt(burnSafe);
            }
            if (stoneOwner != null && (!effects.Active("petrify") || stoneOwner.StoneTime <= 0)) ClearStone();
            explosionTime = Math.Max(0, explosionTime - delta);
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                SpawnedEnemy item = spawned[i];
                item.life -= delta;
                if (item.actor == null || item.life <= 0) { DestroySpawn(item); spawned.RemoveAt(i); }
            }
        }

        string VerifyDestructivePolicy(Command c)
        {
            try
            {
                var current = BridgeJson.Read<InboxMessage>(ReadShared(System.IO.Path.Combine(folder, "inbox.json")));
                if (current == null || current.session != session || !EffectState.Fresh(current.heartbeatMs, Now) || current.paused || !Live || c.epoch != epoch || c.expiresMs <= Now) return "orden cancelada";
                return EffectState.DestructiveRefusal(c, current.noDeath, current.allowDestructive, effects.Active("shield"));
            }
            catch { return "no se pudo verificar permiso"; }
        }

        void ClearStone()
        {
            if (stoneOwner != null && stoneOwner.StoneTime > 0 && StoneSetter != null) StoneSetter.Invoke(stoneOwner, new object[] { 0f });
            stoneOwner = null;
            effects.Remove("petrify");
        }

        public void GroundMovement(LevelPlayerMotor motor, LevelPlayerMotor.VelocityManager velocity)
        {
            float desired = velocity.move * SpeedFactor;
            if (Has("ice_floor") && motor.Grounded && !motor.Dashing && !motor.Locked)
            {
                iceVelocity = EffectState.IceVelocity(iceVelocity, desired, Time.fixedDeltaTime);
                velocity.move = iceVelocity;
            }
            else { iceVelocity = desired; velocity.move = desired; }
            if (Has("wind_push") && !motor.Locked && !motor.Dashing) velocity.move += windDirection * 160f;
        }

        public bool AutoJump(Rewired.Player input, int action)
        {
            var ground = player as LevelPlayerController;
            if (action != 2 || !Has("auto_jump") || Has("no_jump") || ground == null || player.input.actions != input || !ground.motor.Grounded || ground.motor.Locked || ground.motor.Dashing) return false;
            if (autoJumpFrame == Time.frameCount) return true;
            if (Time.unscaledTime < nextAutoJump) return false;
            autoJumpFrame = Time.frameCount; nextAutoJump = Time.unscaledTime + 0.9f;
            return true;
        }

        void RestoreZoom()
        {
            if (zoomCamera != null && Mathf.Approximately(zoomCamera.orthographicSize, appliedSize)) zoomCamera.orthographicSize = originalSize;
            zoomCamera = null;
        }
        void LateUpdate()
        {
            if (!Has("camera_zoom")) { RestoreZoom(); return; }
            var current = CupheadLevelCamera.Current;
            if (current == null) return;
            var camera = current.GetComponent<Camera>();
            if (camera == null) return;
            RestoreZoom();
            zoomCamera = camera; originalSize = camera.orthographicSize; appliedSize = originalSize * 0.85f;
            camera.orthographicSize = appliedSize;
        }

        PlatformingLevelGroundMovementEnemy EnemyProvider()
        {
            if (!(player is LevelPlayerController) || level == null || (level.CurrentLevel.ToString() != "Platforming_Level_1_1" && level.CurrentLevel.ToString() != "Platforming_Level_1_2")) return null;
            foreach (var spawner in UnityEngine.Object.FindObjectsOfType<PlatformingLevelGroundMovementEnemySpawner>())
            {
                var prefab = spawner.enemyPrefab;
                if (prefab == null) continue;
                string id = prefab.ID.ToString();
                if (id == "blue_goblin" || id == "pink_goblin" || id == "flower_grunt" || id == "mushroom") return prefab;
            }
            return null;
        }
        string SpawnEnemies(int count)
        {
            for (int i = spawned.Count - 1; i >= 0; i--) if (spawned[i].actor == null) { DestroySpawn(spawned[i]); spawned.RemoveAt(i); }
            if (spawned.Count + count > 3) return "limite de refuerzos activos";
            var prefab = EnemyProvider();
            if (prefab == null || CupheadLevelCamera.Current == null) return "sin enemigo compatible en este nivel";
            Rect bounds = CupheadLevelCamera.Current.Bounds;
            float x = player.center.x < bounds.center.x ? bounds.xMax - 65f : bounds.xMin + 65f;
            if (Math.Abs(x - player.center.x) < 150f) return "sin espacio para refuerzos";
            var hit = Physics2D.Raycast(new Vector2(x, bounds.yMax), Vector2.down, bounds.height + 400f, 1 << 20);
            if (hit.collider == null || hit.normal.y < 0.6f) return "sin suelo seguro para refuerzos";
            var created = new List<SpawnedEnemy>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var actor = prefab.Spawn(new Vector3(x, hit.point.y + 70f + 50f * i, 0f), x > player.center.x ? PlatformingLevelGroundMovementEnemy.Direction.Left : PlatformingLevelGroundMovementEnemy.Direction.Right, true);
                    if (actor == null) throw new InvalidOperationException("spawn no aplicado");
                    var shadow = EnemyShadow == null ? null : EnemyShadow.GetValue(actor) as Transform;
                    created.Add(new SpawnedEnemy { actor = actor.gameObject, shadow = shadow == null ? null : shadow.gameObject });
                    actor.GoToGround();
                }
                spawned.AddRange(created); return null;
            }
            catch { foreach (var item in created) DestroySpawn(item); return "no se pudo generar refuerzo"; }
        }
        static void DestroySpawn(SpawnedEnemy item)
        {
            if (item.shadow != null) UnityEngine.Object.Destroy(item.shadow);
            if (item.actor != null) UnityEngine.Object.Destroy(item.actor);
        }
        public bool OwnsEnemy(GameObject actor) { return spawned.Exists(delegate(SpawnedEnemy item) { return item.actor == actor; }); }

        // A readable local warning, not a flash or a full-screen obstruction.
        void OnGUI()
        {
            if (!Live) return;
            if (pendingDestructive != null) GUI.Box(new Rect(Screen.width / 2f - 150, 30, 300, 55), (pendingDestructive.effect == "kill" ? "MUERTE" : "REINICIO") + " EN " + Mathf.CeilToInt(pendingDelay) + " s\nF9 cancela la orden");
            if (player == null || Camera.main == null) return;
            Vector3 point = Camera.main.WorldToScreenPoint(player.center);
            string label = effects.Active("burn") ? "QUEMADURA" : explosionTime > 0 ? "EXPLOSION" : "";
            if (label.Length == 0) return;
            Color old = GUI.color;
            GUI.color = effects.Active("burn") ? new Color(1f, 0.45f, 0.1f) : new Color(1f, 0.8f, 0.25f);
            if (effects.Active("burn"))
            {
                for (int i = 0; i < 5; i++)
                {
                    float height = 9f + 5f * Mathf.Sin(Time.unscaledTime * 4f + i);
                    GUI.DrawTexture(new Rect(point.x - 18 + i * 8, Screen.height - point.y - height, 6, height), Texture2D.whiteTexture);
                }
            }
            if (explosionTime > 0)
            {
                float radius = (1f - explosionTime / 0.65f) * 60f;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    GUI.DrawTexture(new Rect(point.x + Mathf.Cos(angle) * radius - 5, Screen.height - point.y + Mathf.Sin(angle) * radius - 5, 10, 10), Texture2D.whiteTexture);
                }
            }
            GUI.Box(new Rect(point.x - 65, Screen.height - point.y - 70, 130, 30), label);
            GUI.color = old;
        }
    }

    [HarmonyPatch(typeof(DamageDealer), "DealDamage")]
    static class WeakShotsPatch
    {
        static void Prefix(DamageDealer __instance, DamageDealer.DamageSource ___damageSource, out float __state)
        {
            __state = float.NaN;
            var p = Plugin.Instance;
            if (p == null || !p.Has("weak_shots") || __instance.PlayerId != PlayerId.PlayerOne || ___damageSource == DamageDealer.DamageSource.Ex || ___damageSource == DamageDealer.DamageSource.Super) return;
            __state = __instance.DamageMultiplier; __instance.DamageMultiplier *= 0.5f;
        }
        static void Finalizer(DamageDealer __instance, float __state) { if (!float.IsNaN(__state)) __instance.DamageMultiplier = __state; }
    }
    [HarmonyPatch(typeof(PlayerStatsManager), "GetStoned")]
    static class StoneOwnershipPatch
    {
        static void Prefix(PlayerStatsManager __instance)
        {
            var p = Plugin.Instance;
            if (p != null && p.stoneOwner == __instance && !p.settingStone && __instance.StoneTime <= 0) p.stoneOwner = null;
        }
    }
    [HarmonyPatch(typeof(AbstractPlatformingLevelEnemy), "OnDamageTaken")]
    static class SpawnScoringPatch
    {
        static void Prefix(AbstractPlatformingLevelEnemy __instance, out bool __state) { __state = Plugin.Instance != null && Plugin.Instance.OwnsEnemy(__instance.gameObject) && Level.ScoringData.pacifistRun; }
        static void Finalizer(bool __state) { if (__state && Level.Current != null) Level.ScoringData.pacifistRun = true; }
    }
}
