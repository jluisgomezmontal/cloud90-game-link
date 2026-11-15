using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using b1;
using HarmonyLib;
using UnrealEngine.Engine;
using UnrealEngine.NavigationSystem;
using UnrealEngine.Runtime;

namespace Cloud90.Wukong {
    public sealed partial class Mod {
        private static readonly HashSet<string> NormalEnemies = new HashSet<string> {
            "wolf_scout", "wolf_archer", "crow_diviner"
        };
        private static readonly HashSet<string> Bosses = new HashSet<string> {
            "wandering_wight", "guangzhi", "tiger_vanguard"
        };

        public sealed class AmbushProfile {
            public string ExecutableSha256 { get; set; }
            public AmbushZone[] Zones { get; set; }
        }

        public sealed class AmbushZone {
            public string Level { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Radius { get; set; }
            public Dictionary<string, string> Enemies { get; set; }

            internal bool Contains(FVector point) {
                double dx = point.X - X, dy = point.Y - Y, dz = point.Z - Z;
                return dx * dx + dy * dy + dz * dz <= Radius * Radius;
            }
        }

        private sealed class AmbushEnemy {
            internal BUTamerActor Tamer;
            internal BGUCharacterCS Monster;
            internal AmbushZone Zone;
            internal string Id, Viewer;
            internal bool Boss, Confirmed;
            internal ulong Expires, RemoveAt;
            internal FVector Position;
        }

        private AmbushProfile ambushProfile;
        private readonly List<AmbushEnemy> ambushes = new List<AmbushEnemy>();
        private ulong bossReadyAt;
        private bool captureAmbushDiagnostics;

        private void CaptureAmbushDiagnostics() {
            if (!captureAmbushDiagnostics) return;
            captureAmbushDiagnostics = false;
            try {
                var origin = owner.GetActorLocation();
                var candidates = UGameplayStatics.GetAllActorsOfClassList<BUTamerActor>(owner)
                    .Where(tamer => !tamer.IsNullOrDestroyed() && !ambushes.Any(entry => entry.Tamer == tamer))
                    .Select(tamer => new { Tamer = tamer, Distance = DistanceSquared(tamer.GetActorLocation(), origin) })
                    .Where(item => item.Distance <= 3000 * 3000)
                    .OrderBy(item => item.Distance).Take(30).Select(item => {
                        var monster = item.Tamer.GetMonster();
                        return new {
                            Class = item.Tamer.GetClass()?.GetPathName(),
                            ResourceId = monster.IsNullOrDestroyed() ? 0 : BGUFunctionLibraryCS.BGUGetResID(monster),
                            Distance = Math.Sqrt(item.Distance)
                        };
                    }).ToArray();
                string executableHash;
                using (var process = Process.GetCurrentProcess())
                using (var file = File.OpenRead(process.MainModule.FileName))
                using (var sha = SHA256.Create())
                    executableHash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                File.WriteAllText(Path.Combine(folder, "..", "ambush-diagnostics.json"), json.Serialize(new {
                    ExecutableSha256 = executableHash, Level = UGameplayStatics.GetCurrentLevelName(owner),
                    origin.X, origin.Y, origin.Z, Candidates = candidates
                }));
            } catch (Exception error) {
                Console.WriteLine("[Cloud90 Wukong] No se pudo registrar el diagnóstico: " + error.Message);
            }
        }

        private void LoadAmbushProfile(string root) {
            captureAmbushDiagnostics = File.Exists(Path.Combine(root, "capture-ambush.txt"));
            var path = Path.Combine(root, "ambush-profile.json");
            if (!File.Exists(path)) return;
            try {
                if (new FileInfo(path).Length > 65536) throw new InvalidDataException("perfil demasiado grande");
                var profile = json.Deserialize<AmbushProfile>(File.ReadAllText(path));
                using (var process = Process.GetCurrentProcess())
                using (var file = File.OpenRead(process.MainModule.FileName))
                using (var sha = SHA256.Create()) {
                    var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                    if (!hash.Equals(profile?.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("el ejecutable no coincide con el perfil");
                }
                if (profile.Zones == null || profile.Zones.Length == 0 ||
                    profile.Zones.Any(zone => zone == null || String.IsNullOrWhiteSpace(zone.Level) ||
                        !(zone.Radius >= 600 && zone.Radius <= 3000) || zone.Enemies == null ||
                        zone.Enemies.Any(pair => (!NormalEnemies.Contains(pair.Key) && !Bosses.Contains(pair.Key)) ||
                            String.IsNullOrEmpty(pair.Value) || !pair.Value.StartsWith("/Game/", StringComparison.Ordinal) ||
                            !pair.Value.EndsWith("_C", StringComparison.Ordinal))))
                    throw new InvalidDataException("zonas o clases inválidas");
                harmony.Patch(AccessTools.Method(typeof(AHUD), nameof(AHUD.ReceiveDrawHUD)),
                    postfix: new HarmonyMethod(typeof(Mod), nameof(DrawAmbushHud)));
                ambushProfile = profile;
            } catch (Exception error) {
                Console.WriteLine("[Cloud90 Wukong] Emboscada desactivada: " + error.Message);
            }
        }

        private AmbushZone ActiveZone() {
            if (owner == null || ambushProfile == null ||
                BGWGameInstanceCS.TickingGameInstNetMode(owner) != EGameInstNetMode.StandAlone)
                return null;
            var level = UGameplayStatics.GetCurrentLevelName(owner);
            var location = owner.GetActorLocation();
            return ambushProfile.Zones.FirstOrDefault(zone => zone.Level == level && zone.Contains(location));
        }

        private IEnumerable<string> AmbushCapabilities() {
            var zone = ActiveZone();
            if (zone == null) yield break;
            if (zone.Enemies.Keys.Any(NormalEnemies.Contains)) yield return "ambush";
            if (zone.Enemies.Keys.Any(Bosses.Contains)) yield return "ambush_boss";
        }

        private string StartAmbush(Dictionary<string, object> inbox, Dictionary<string, object> command,
            ulong now, ulong expires, bool boss) {
            if (now < shield) return "escudo activo";
            if (Flag(inbox, "noDeath", true) || Flag(command, "safe", true)) return "modo sin morir activo";
            if (boss && (!Flag(inbox, "allowDestructive", false) ||
                !Flag(command, "allowDestructive", false))) return "permiso requerido";
            if (boss && (now < bossReadyAt || ambushes.Any(enemy => enemy.Boss)))
                return "jefe activo o en enfriamiento";

            var zone = ActiveZone();
            if (zone == null) return "emboscada fuera de una zona habilitada";
            var choice = Text(command, "arg");
            if (!(boss ? Bosses : NormalEnemies).Contains(choice) ||
                !zone.Enemies.TryGetValue(choice, out string path)) return "enemigo no habilitado en esta zona";
            UClass enemyClass = UObject.LoadClass<BUTamerActor>(default, path);
            if (enemyClass == null) return "clase de enemigo no disponible";

            var origin = owner.GetActorLocation();
            FVector point = default;
            bool found = false;
            for (int i = 0; i < 12; i++) {
                if (!UNavigationSystemV1.GetRandomReachablePointInRadius(owner, origin, out point, 1500, null, default) ||
                    !zone.Contains(point) || Math.Abs(point.Z - origin.Z) > 175 ||
                    DistanceSquared(point, origin) < 600 * 600 ||
                    ambushes.Any(enemy => DistanceSquared(point, enemy.Position) < 350 * 350)) continue;
                found = true;
                break;
            }
            if (!found) return "sin posición transitable para la emboscada";

            var transform = new FTransform(point);
            var enemyEntry = new AmbushEnemy {
                Id = Text(command, "id"),
                Viewer = new string(Text(command, "by").Where(c => !Char.IsControl(c)).Take(40).ToArray()),
                Boss = boss, Position = point, Expires = expires, Zone = zone
            };
            enemyEntry.Tamer = BGU_UnrealWorldUtil.BeginSpawnUnitDeferred(owner.World,
                new TSubclassOf<BUTamerActor>(enemyClass), in transform,
                new FUnitSpawnInfo { bFacePlayer = true, bCatchPlayer = true });
            if (enemyEntry.Tamer == null) return "no se pudo crear el enemigo";
            ambushes.Add(enemyEntry);
            try {
                if (String.IsNullOrEmpty(BGU_UnrealWorldUtil.FinishSpawnUnitDeferred(enemyEntry.Tamer, transform))) {
                    RemoveAmbush(ambushes.Count - 1);
                    return "no se pudo iniciar el enemigo";
                }
            } catch {
                RemoveAmbush(ambushes.Count - 1);
                throw;
            }
            if (boss) bossReadyAt = now + 180000;
            return null;
        }

        private static double DistanceSquared(FVector first, FVector second) {
            double x = first.X - second.X, y = first.Y - second.Y, z = first.Z - second.Z;
            return x * x + y * y + z * z;
        }

        private void TickAmbushes(ulong now) {
            if (ambushes.Count == 0) return;
            var zone = ActiveZone();
            for (int i = ambushes.Count - 1; i >= 0; i--) {
                var enemy = ambushes[i];
                if (enemy.Tamer.IsNullOrDestroyed() || enemy.Zone != zone) { CancelAmbush(i); continue; }
                enemy.Monster = enemy.Tamer.GetMonster();
                if (!enemy.Monster.IsNullOrDestroyed()) {
                    enemy.Position = enemy.Monster.GetActorLocation();
                    if (BGUFunctionLibraryCS.BGUIsUnitDead(enemy.Monster)) {
                        if (enemy.RemoveAt == 0) enemy.RemoveAt = now + 5000;
                    } else if (!enemy.Confirmed && BGUFunctionLibraryCS.BGUGetTarget(enemy.Monster) == owner) {
                        enemy.Confirmed = true;
                        Ack(enemy.Id, "ok");
                    }
                }
                if ((enemy.Confirmed && enemy.Monster.IsNullOrDestroyed()) ||
                    (!enemy.Confirmed && now >= enemy.Expires) ||
                    (enemy.RemoveAt != 0 && now >= enemy.RemoveAt)) CancelAmbush(i);
            }
        }

        private void CancelAmbush(int index) {
            var enemy = ambushes[index];
            if (!enemy.Confirmed) {
                Ack(enemy.Id, "resultado incierto; combate sin confirmar");
                enemy.Confirmed = true;
            }
            RemoveAmbush(index);
        }

        private void RemoveAmbush(int index) {
            var enemy = ambushes[index];
            var monster = enemy.Monster;
            if (monster.IsNullOrDestroyed() && !enemy.Tamer.IsNullOrDestroyed())
                monster = enemy.Tamer.GetMonster();
            if (!monster.IsNullOrDestroyed()) BGUFunctionLibraryCS.DestroyAllProjectile(monster);
            if (!enemy.Tamer.IsNullOrDestroyed()) BGU_UnrealWorldUtil.DestroyActor(enemy.Tamer);
            if (!monster.IsNullOrDestroyed()) BGU_UnrealWorldUtil.DestroyActor(monster);
            ambushes.RemoveAt(index);
        }

        private void ClearAmbushes() {
            for (int i = ambushes.Count - 1; i >= 0; i--) CancelAmbush(i);
        }

        private static void DrawAmbushHud(AHUD __instance, int SizeX, int SizeY) {
            var mod = current;
            var controller = __instance.PlayerOwner;
            if (mod?.owner == null || controller == null || controller.GetControlledPawn() != mod.owner) return;
            foreach (var enemy in mod.ambushes) {
                var monster = enemy.Monster;
                if (!enemy.Confirmed || enemy.RemoveAt != 0 || String.IsNullOrEmpty(enemy.Viewer) ||
                    monster.IsNullOrDestroyed() || !controller.LineOfSightTo(monster, default)) continue;
                var head = monster.GetActorLocation();
                head.Z += enemy.Boss ? 300 : 200;
                if (controller.ProjectWorldLocationToScreen(head, out FVector2D screen) &&
                    screen.X > 0 && screen.Y > 0 && screen.X < SizeX && screen.Y < SizeY)
                    __instance.DrawText(enemy.Viewer, new FLinearColor(1, 1, 1, 1),
                        (float)screen.X, (float)screen.Y, null, 1, false);
            }
        }
    }
}
