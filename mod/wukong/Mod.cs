using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using b1;
using BtlShare;
using CSharpModBase;
using HarmonyLib;
using UnrealEngine.Engine;
using UnrealEngine.Runtime;

namespace Cloud90.Wukong {
    public sealed class Mod : ICSharpMod {
        public string Name => "Cloud90.Wukong";
        public string Version => "1.0.0";
        private static Mod current;
        private readonly Harmony harmony = new Harmony("cloud90.wukong");
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly Queue<object> acks = new Queue<object>();
        private readonly Dictionary<string, ulong> availableAt = new Dictionary<string, ulong>();
        private readonly Dictionary<EBGUSimpleState, ulong> ownedStates = new Dictionary<EBGUSimpleState, ulong>();
        private string folder, session, epoch;
        private BGUPlayerCharacterCS owner;
        private BGUPlayerCharacterCS attached;
        private ulong nextPoll, shield, attackUntil;
        private float attackBefore, attackApplied;
        private int generation, pid;

        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
        private static readonly Dictionary<string,int> Cooldown = new Dictionary<string,int> {
            {"heal",15},{"health_max",45},{"damage",30},{"mana_add",30},{"mana_empty",45},
            {"stamina_add",20},{"focus_add",30},{"focus_remove",30},{"shield",30},
            {"spirit_qi_add",60},{"vessel_qi_add",60},{"staff_boost",45},{"staff_weak",45},
            {"slow",45},{"no_dodge",45},{"invulnerable",60},{"kill",120}
        };
        private static readonly HashSet<string> Troll = new HashSet<string> {
            "damage","mana_empty","focus_remove","staff_weak","slow","no_dodge","kill"
        };

        public void Init() {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cloud90GameLink\wukong");
            string pointer = Path.Combine(root, "active-session.txt");
            if (!File.Exists(pointer) || new FileInfo(pointer).Length != 32) return;
            session = File.ReadAllText(pointer);
            if (session.Length != 32 || session.Any(c => !"0123456789abcdef".Contains(c))) return;
            folder = Path.Combine(root, session);
            using (var process = Process.GetCurrentProcess()) pid = process.Id;
            current = this;
            harmony.Patch(AccessTools.Method(typeof(BGUPlayerCharacterCS), nameof(BGUPlayerCharacterCS.AfterInitAllComp)),
                postfix: new HarmonyMethod(typeof(Mod), nameof(Attach)));
        }

        public void DeInit() {
            Clear();
            harmony.UnpatchAll("cloud90.wukong");
            if (current == this) current = null;
        }

        private static void Attach(BGUPlayerCharacterCS __instance) {
            var mod = current;
            if (mod == null || __instance.ActorCompContainerCS == null || ReferenceEquals(mod.attached, __instance)) return;
            mod.attached = __instance;
            __instance.ActorCompContainerCS.AddComp(new PollComponent(mod, __instance));
            __instance.ActorCompContainerCS.RecalculateCanTick();
        }

        private sealed class PollComponent : UActorCompBaseCS {
            private readonly Mod mod;
            private readonly BGUPlayerCharacterCS player;
            public PollComponent(Mod mod, BGUPlayerCharacterCS player) { this.mod = mod; this.player = player; }
            public override void OnAttach() { SetCanTick(true); }
            public override int GetTickGroupMask() { return 1; }
            public override void OnTickWithGroup(float delta, int group) {
                if (current != mod) { SetCanTick(false); return; }
                try { mod.Tick(player); }
                catch (Exception error) { mod.Clear(); Console.WriteLine("[Cloud90 Wukong] " + error.Message); }
            }
        }

        private static bool Playable(BGUPlayerCharacterCS player) {
            var controller = UGameplayStatics.GetPlayerController(player, 0);
            return controller != null && controller.GetControlledPawn() == player &&
                BGU_DataUtil.GetPlayerControlReadonlyData<IBPC_TransData, BPC_TransData>(controller)?.GetIsTrans() == false &&
                !UGameplayStatics.IsGamePaused(player) &&
                !BGUFunctionLibraryCS.BGUIsUnitDead(player) &&
                !BGUFunctionLibraryCS.BGUHasUnitState(player, EBGUUnitState.Teleporting) &&
                !BGUFunctionLibraryCS.BGUHasUnitSimpleState(player, EBGUSimpleState.PlayerInSeq) &&
                !BGUFunctionLibraryCS.BGUHasUnitSimpleState(player, EBGUSimpleState.InPhaseTransition) &&
                !BGUFunctionLibraryCS.BGUHasUnitSimpleState(player, EBGUSimpleState.IgnoreAllInput) &&
                Read(player, EBGUAttrFloat.Hp) > 0;
        }

        private void Tick(BGUPlayerCharacterCS player) {
            if (!ReferenceEquals(attached, player)) return;
            ulong now = GetTickCount64();
            if (now < nextPoll) return;
            nextPoll = now + 125;
            var active = Playable(player) ? player : null;
            if (!ReferenceEquals(owner, active) || epoch == null) {
                Clear();
                owner = active;
                epoch = session + "_" + ++generation;
            }
            if (attackUntil != 0 && now >= attackUntil) RestoreAttack();
            foreach (var state in ownedStates.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToArray())
                RemoveState(state);
            bool fresh = false;
            try {
                using (var stream = new FileStream(Path.Combine(folder, "inbox.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) {
                    if (stream.Length <= 65536) {
                        var inbox = json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
                        ulong beat = ULong(inbox,"heartbeatMs");
                        fresh = Text(inbox,"session") == session && beat > 0 && now >= beat &&
                            now - beat <= 2000 && !Flag(inbox,"paused",true);
                        object raw;
                        if (fresh && inbox.TryGetValue("effects",out raw) && raw is object[] commands && commands.Length <= 20) {
                            foreach (var entry in commands) {
                                if (!(entry is Dictionary<string,object> command)) continue;
                                string id = Text(command,"id");
                                if (id.Length < 1 || id.Length > 80 ||
                                    id.Any(c => !"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".Contains(c)) ||
                                    seen.Count >= 100000 || !seen.Add(id)) continue;
                                string result;
                                try { result = Apply(inbox, command, now); }
                                catch (Exception) { result = "resultado incierto; efecto sin confirmar"; }
                                acks.Enqueue(new { id, result });
                                if (acks.Count > 100) acks.Dequeue();
                            }
                        }
                    }
                }
            } catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (!fresh || owner == null) Clear();
            WriteStatus(now);
        }

        private void WriteStatus(ulong now) {
            string path = Path.Combine(folder,"status.json"), temp = path + ".tmp";
            try {
                File.WriteAllText(temp, json.Serialize(new {
                    session, epoch, pid, heartbeatMs = now, bridgeVersion = "cloud90-wukong-1",
                    inPlay = owner != null, capabilities = owner == null ? new string[0] : Cooldown.Keys.ToArray(), acks
                }));
                if (File.Exists(path)) File.Replace(temp,path,null);
                else File.Move(temp,path);
            } catch (IOException) { }
        }

        private string Apply(Dictionary<string,object> inbox, Dictionary<string,object> cmd, ulong now) {
            if (owner == null || !Playable(owner)) return "fuera de partida";
            if (Text(cmd,"epoch") != epoch) return "intento anterior";
            ulong expiry = ULong(cmd,"expiresMs");
            if (expiry <= now || expiry - now > 10000) return "caducado";
            string effect = Text(cmd,"effect");
            if (!Cooldown.TryGetValue(effect,out int seconds)) return "efecto desconocido";
            if (availableAt.TryGetValue(effect,out ulong allowed) && allowed > now) return "en enfriamiento";
            if (Troll.Contains(effect) && now < shield) return "escudo activo";
            if (effect == "kill" && (Flag(inbox,"noDeath",true) || Flag(cmd,"safe",true) ||
                !Flag(inbox,"allowDestructive",false) || !Flag(cmd,"allowDestructive",false))) return "permiso requerido";
            ulong End(int limit) => now + Math.Min((ulong)limit, Math.Max(1000UL, ULong(cmd,"ms")));
            string result;
            switch (effect) {
                case "heal": result = Change(EBGUAttrFloat.Hp,EBGUAttrFloat.HpMax,.25f); break;
                case "health_max": result = Change(EBGUAttrFloat.Hp,EBGUAttrFloat.HpMax,1); break;
                case "damage": result = Change(EBGUAttrFloat.Hp,EBGUAttrFloat.HpMax,-.2f,1); break;
                case "mana_add": result = Change(EBGUAttrFloat.Mp,EBGUAttrFloat.MpMax,.3f); break;
                case "mana_empty":
                    var skills = BGU_DataUtil.GetUnPersistentReadOnlyData<BUC_SkillInstsData>(owner);
                    if (skills == null || skills.CurrentCastingSkillID > 0 ||
                        BGUFunctionLibraryCS.BGUHasUnitState(owner, EBGUUnitState.Attacking)) return "acción en curso";
                    result = Change(EBGUAttrFloat.Mp,EBGUAttrFloat.MpMax,-1); break;
                case "stamina_add": result = Change(EBGUAttrFloat.Stamina,EBGUAttrFloat.StaminaMax,.35f); break;
                case "focus_add":
                case "focus_remove":
                    float levels = Read(owner,EBGUAttrFloat.PelevelMax);
                    if (levels < 1 || levels > 10) return "concentración no disponible";
                    result = Change(EBGUAttrFloat.Pevalue,EBGUAttrFloat.PevalueMax,(effect == "focus_add" ? 1 : -1)/levels);
                    break;
                case "spirit_qi_add": result = Change(EBGUAttrFloat.VigorEnergy,EBGUAttrFloat.VigorEnergyMax,.25f); break;
                case "vessel_qi_add": result = Change(EBGUAttrFloat.FabaoEnergy,EBGUAttrFloat.FabaoEnergyMax,.25f); break;
                case "shield": shield = End(15000); result = "ok"; break;
                case "staff_boost":
                case "staff_weak":
                    RestoreAttack();
                    attackBefore = Read(owner,EBGUAttrFloat.AtkBase);
                    if (attackBefore <= 0 || float.IsNaN(attackBefore) || float.IsInfinity(attackBefore)) return "ataque no disponible";
                    attackApplied = attackBefore * (effect == "staff_boost" ? 1.25f : .75f);
                    attackUntil = End(10000);
                    BGUFunctionLibraryCS.BGUSetAttrValue(owner,EBGUAttrFloat.AtkBase,attackApplied);
                    result = Verified(EBGUAttrFloat.AtkBase,attackApplied);
                    break;
                case "slow": result = AddState(EBGUSimpleState.MoveSlowly, End(4000)); break;
                case "no_dodge": result = AddState(EBGUSimpleState.IgnoreRollSkill, End(3000)); break;
                case "invulnerable": result = AddState(EBGUSimpleState.ImmueDamage, End(5000)); break;
                case "kill":
                    BGUFunctionLibraryCS.BGUGMDead(owner);
                    result = BGUFunctionLibraryCS.BGUIsUnitDead(owner) ? "ok" : "resultado incierto; muerte sin confirmar";
                    break;
                default: return "efecto desconocido";
            }
            if (result == "ok" || result.StartsWith("resultado incierto")) availableAt[effect] = now + (ulong)seconds * 1000;
            return result;
        }

        private static float Read(AActor actor, EBGUAttrFloat attr) => BGUFunctionLibraryCS.GetAttrValue(actor,attr);

        private string Change(EBGUAttrFloat attr, EBGUAttrFloat max, float fraction, float floor = 0) {
            float before = Read(owner,attr), limit = Read(owner,max);
            if (float.IsNaN(before) || float.IsInfinity(before) || float.IsNaN(limit) || float.IsInfinity(limit) ||
                limit <= 0 || limit < floor || before < floor || before > limit) return "medidor no disponible";
            float target = Math.Min(limit, Math.Max(floor, before + limit * fraction));
            if (Math.Abs(target - before) < .01f) return "medidor al límite";
            BGUFunctionLibraryCS.BGUSetAttrValue(owner,attr,target);
            return Verified(attr,target);
        }

        private string Verified(EBGUAttrFloat attr, float target) =>
            Math.Abs(Read(owner,attr) - target) <= .05f ? "ok" : "resultado incierto; atributo sin confirmar";

        private string AddState(EBGUSimpleState state, ulong until) {
            if (BGUFunctionLibraryCS.BGUHasUnitSimpleState(owner,state)) return "estado ya activo";
            ownedStates[state] = until;
            BGUFunctionLibraryCS.BGUSetUnitSimpleState(owner,state,true);
            return BGUFunctionLibraryCS.BGUHasUnitSimpleState(owner,state) ? "ok" : "resultado incierto; estado sin confirmar";
        }

        private void RemoveState(EBGUSimpleState state) {
            if (owner != null && BGUFunctionLibraryCS.BGUHasUnitSimpleState(owner,state))
                BGUFunctionLibraryCS.BGUSetUnitSimpleState(owner,state,false);
            ownedStates.Remove(state);
        }

        private void RestoreAttack() {
            if (owner != null && attackUntil != 0 && Math.Abs(Read(owner,EBGUAttrFloat.AtkBase) - attackApplied) < .05f)
                BGUFunctionLibraryCS.BGUSetAttrValue(owner,EBGUAttrFloat.AtkBase,attackBefore);
            attackUntil = 0;
        }

        private void Clear() {
            shield = 0;
            RestoreAttack();
            foreach (var state in ownedStates.Keys.ToArray()) RemoveState(state);
        }

        private static string Text(Dictionary<string,object> source, string key) =>
            source.TryGetValue(key,out object value) ? value as string ?? "" : "";
        private static ulong ULong(Dictionary<string,object> source, string key) =>
            source.TryGetValue(key,out object value) && ulong.TryParse(Convert.ToString(value),out ulong number) ? number : 0;
        private static bool Flag(Dictionary<string,object> source, string key, bool fallback) =>
            source.TryGetValue(key,out object value) && value is bool flag ? flag : fallback;
    }
}
