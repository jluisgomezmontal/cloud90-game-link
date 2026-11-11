#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <main.h>
#include <shlobj.h>

#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <string_view>
#include <unordered_set>

#include "../gtasa/vendor/json.hpp"

namespace {
// Native names, signatures and hashes from alloc8or's RDR3 native database.
constexpr UINT64 NetworkIsInSession = 0xCA97246103B63917;
constexpr UINT64 NetworkIsSessionStarted = 0x9DE624D2FC4B603F;
constexpr UINT64 NetworkIsGameInProgress = 0x10FAB35428CCC9D7;
constexpr UINT64 IsPauseMenuActive = 0x535384D6067BA42E;
constexpr UINT64 IsScreenFadedIn = 0x37F9A426FBCF4AF2;
constexpr UINT64 IsGameplayCamRendering = 0x8660EA714834E412;
constexpr UINT64 GetMissionFlag = 0xB15CD1CF58771DE1;
constexpr UINT64 PlayerId = 0x217E9DC48139933D;
constexpr UINT64 PlayerPedId = 0x096275889B8E0EE0;
constexpr UINT64 IsPlayerControlOn = 0x7964097FCE4C244B;
constexpr UINT64 DoesEntityExist = 0xD42BD6EB2E0F1677;
constexpr UINT64 IsEntityDead = 0x7D5B1F88E7504BBA;
constexpr UINT64 GetEntityHealth = 0x82368787EA73C0F7;
constexpr UINT64 GetPedMaxHealth = 0x4700A416E8324EF3;
constexpr UINT64 IsPedOnMount = 0x460BC76A0E10655E;
constexpr UINT64 SetEntityHealth = 0xAC2767ED8BDFAB15;
constexpr UINT64 GetMount = 0xE7E11B8DCBED1058;
constexpr UINT64 GetPedStamina = 0x775A1CA7893AA8B5;
constexpr UINT64 GetPedMaxStamina = 0xCB42AFE2B613EE55;
constexpr UINT64 ChangePedStamina = 0xC3D4B754C0E86B9E;
constexpr UINT64 IsDeadEyeEnabled = 0xDE6C85975F9D4894;
constexpr UINT64 IsDeadEyeActive = 0xB16223CB7DA965F0;
constexpr UINT64 GetDeadEyeAmount = 0x029884FB65821B07;
constexpr UINT64 RestoreDeadEyeRing = 0x2498035289B5688F;
constexpr UINT64 GetCurrentWeapon = 0x3A87E44BB9A01D54;
constexpr UINT64 GetWeaponEntity = 0x3B390A939AF0B5FC;
constexpr UINT64 GetAmmoInClip = 0x2E1202248937775C;
constexpr UINT64 GetAmmoType = 0x7FEAD38B326B9F74;
constexpr UINT64 GetAmmoByType = 0x39D22031557946C1;
constexpr UINT64 SetAmmoByType = 0x5FD1E1F011E76D7E;
constexpr UINT64 GetWantedLevel = 0xABC532F9098BFD9D;
constexpr UINT64 GetBounty = 0x54310AAB97B92816;
constexpr UINT64 ReportCrime = 0xF60386770878A98F;
constexpr UINT64 ForceLawResponse = 0x956510F8C36B5C64;
constexpr UINT64 ClearCurrentPursuit = 0x55F37F5F3F2475E1;
constexpr UINT64 ClearWantedScore = 0x062B4A4A3396351D;
constexpr UINT64 SetWantedIntensity = 0xA80FF73F772ACF6A;
constexpr UINT64 IsModelValid = 0x392C8D8E07B70EFC;
constexpr UINT64 IsModelAPed = 0xC3F09DE9D6D17DDA;
constexpr UINT64 RequestModel = 0xFA28FE3A6246FC30;
constexpr UINT64 HasModelLoaded = 0x1283B8B89DD5D1B6;
constexpr UINT64 ReleaseModel = 0x4AD96EF928BD4F9A;
constexpr UINT64 GetEntityCoords = 0xA86D5F069399F44D;
constexpr UINT64 GetOffsetCoords = 0x1899F328B0E12848;
constexpr UINT64 GetSafeCoord = 0xB61C8E878A4199CA;
constexpr UINT64 HasPositionCollision = 0x6BFBDC46139C45AB;
constexpr UINT64 IsEntityInWater = 0xDDE5C125AC446723;
constexpr UINT64 CreatePed = 0xD49F9B0955C367DE;
constexpr UINT64 DeletePed = 0xCC0EF140F99365C5;
constexpr UINT64 GetEntityModel = 0xDA76A9F39210D365;
constexpr UINT64 BelongsToScript = 0x622B1980CBE13332;
constexpr UINT64 SetMissionEntity = 0xDC19C288082E586E;
constexpr UINT64 EquipOutfitPreset = 0x77FF8D35EEC6BBC4;
constexpr UINT64 GiveWeaponToPed = 0x5E3BDDBCB83F3D84;
constexpr UINT64 TaskCombatPed = 0xF166E48407BAC484;
constexpr UINT64 IsPedInCombat = 0x4859F1FC66A6278E;
constexpr std::string_view FreeRoam = "story_free_roam_candidate";

UINT64 NativeArg(float value) {
    return std::bit_cast<UINT32>(value);
}

UINT64 NativeArg(int value) {
    return static_cast<UINT32>(value);
}

UINT64 NativeArg(UINT32 value) {
    return value;
}

template <typename T>
UINT64 NativeArg(T* value) {
    return reinterpret_cast<UINT64>(value);
}

template <typename... Args>
PUINT64 CallNative(UINT64 hash, Args... args) {
    nativeInit(hash);
    (nativePush64(NativeArg(args)), ...);
    return nativeCall();
}

template <typename... Args>
int ReadNative(UINT64 hash, Args... args) {
    return std::bit_cast<int>(static_cast<UINT32>(*CallNative(hash, args...)));
}

float ReadFloat(UINT64 hash, int handle) {
    return std::bit_cast<float>(static_cast<UINT32>(*CallNative(hash, handle)));
}

struct Sample {
    std::string_view context;
    int ped = 0;
    int health = 0;
    int maxHealth = 0;
    bool mounted = false;

    bool operator==(const Sample&) const = default;
};

Sample ReadContext() {
    if (ReadNative(NetworkIsInSession) || ReadNative(NetworkIsSessionStarted) ||
        ReadNative(NetworkIsGameInProgress)) {
        return {"online"};
    }
    if (ReadNative(IsPauseMenuActive)) return {"pause_menu"};
    if (!ReadNative(IsScreenFadedIn) || !ReadNative(IsGameplayCamRendering)) return {"transition_or_camera"};

    const int ped = ReadNative(PlayerPedId);
    if (!ped || !ReadNative(DoesEntityExist, ped)) return {"no_player"};
    if (ReadNative(IsEntityDead, ped)) return {"dead", ped};
    if (!ReadNative(IsPlayerControlOn, ReadNative(PlayerId))) return {"no_control", ped};

    const int health = ReadNative(GetEntityHealth, ped);
    const int maxHealth = ReadNative(GetPedMaxHealth, ped);
    if (health <= 0 || maxHealth <= 0) return {"invalid_health", ped};

    return {ReadNative(GetMissionFlag) ? "story_mission" : FreeRoam,
            ped, health, maxHealth, ReadNative(IsPedOnMount, ped) != 0};
}

enum class Effect { Heal, Damage, DeadEye, HorseStamina, Shield, RemoveAmmo, StartWanted, ClearWanted };

struct PreviewEffect {
    Effect effect;
    int key;
    const char* name;
    ULONGLONG cooldown;
    bool sabotage = false;
    bool extended = false;
};

constexpr std::array PreviewEffects{
    PreviewEffect{Effect::Heal, '1', "heal", 15000},
    PreviewEffect{Effect::Damage, '2', "damage", 30000, true},
    PreviewEffect{Effect::DeadEye, '3', "dead_eye_add", 15000},
    PreviewEffect{Effect::HorseStamina, '4', "horse_stamina_add", 15000},
    PreviewEffect{Effect::Shield, '5', "shield", 15000},
    PreviewEffect{Effect::RemoveAmmo, '6', "ammo_remove", 60000, true, true},
    PreviewEffect{Effect::StartWanted, '7', "wanted_start", 90000, true, true},
    PreviewEffect{Effect::ClearWanted, '8', "wanted_clear", 90000, false, true},
};

struct EffectResult {
    const char* reason;
    float before = 0;
    float after = 0;
};

EffectResult ModifyHealth(const Sample& sample, bool heal) {
    if (sample.health > sample.maxHealth) return {"invalid_health"};
    const int delta = heal ? sample.maxHealth / 4
                           : static_cast<int>(static_cast<long long>(sample.maxHealth) * 15 / 100);
    const int target = heal ? sample.health + std::min(delta, sample.maxHealth - sample.health)
                            : sample.health - std::min(delta, sample.health - 1);
    if (target == sample.health) {
        const float health = static_cast<float>(sample.health);
        return {"limit_reached", health, health};
    }

    CallNative(SetEntityHealth, sample.ped, target, 0);
    const int after = ReadNative(GetEntityHealth, sample.ped);
    return {after == target ? "applied" : "unconfirmed",
            static_cast<float>(sample.health), static_cast<float>(after)};
}

EffectResult RestoreHorse(const Sample& sample) {
    if (!sample.mounted) return {"not_mounted"};
    const int horse = ReadNative(GetMount, sample.ped);
    if (!horse || !ReadNative(DoesEntityExist, horse) || ReadNative(IsEntityDead, horse)) {
        return {"invalid_mount"};
    }
    const float before = ReadFloat(GetPedStamina, horse);
    const float maximum = ReadFloat(GetPedMaxStamina, horse);
    if (!std::isfinite(before) || !std::isfinite(maximum) || maximum <= 0 ||
        before < 0 || before > maximum) return {"invalid_stamina"};
    if (before >= maximum) return {"limit_reached", before, before};
    if (horse != ReadNative(GetMount, sample.ped)) return {"mount_changed"};

    const float amount = std::min(maximum * 0.25f, maximum - before);
    CallNative(ChangePedStamina, horse, amount);
    const float after = ReadFloat(GetPedStamina, horse);
    return {std::isfinite(after) && std::abs(after - (before + amount)) <= 0.01f
                ? "applied" : "unconfirmed", before, after};
}

EffectResult RestoreDeadEye() {
    const int player = ReadNative(PlayerId);
    if (!ReadNative(IsDeadEyeEnabled, player) || ReadNative(IsDeadEyeActive, player)) {
        return {"dead_eye_unavailable"};
    }
    const float before = ReadFloat(GetDeadEyeAmount, player);
    // The preview assumes a normalized ring. Reject readings in any other range.
    if (!std::isfinite(before) || before < 0 || before > 1) return {"unknown_dead_eye_scale"};
    if (before >= 1) return {"limit_reached", before, before};

    const float amount = std::min(0.25f, 1.0f - before);
    CallNative(RestoreDeadEyeRing, player, amount);
    const float after = ReadFloat(GetDeadEyeAmount, player);
    return {std::isfinite(after) && std::abs(after - (before + amount)) <= 0.01f
                ? "applied" : "unconfirmed", before, after};
}

EffectResult RemoveAmmo(const Sample& sample) {
    UINT32 weapon = 0;
    if (!ReadNative(GetCurrentWeapon, sample.ped, &weapon, 1, 0, 0) || !weapon ||
        !ReadNative(GetWeaponEntity, sample.ped, 0) || ReadNative(GetWeaponEntity, sample.ped, 1)) {
        return {"single_weapon_required"};
    }
    int loaded = 0;
    if (!ReadNative(GetAmmoInClip, sample.ped, &loaded, weapon) || loaded < 0) return {"unknown_clip"};
    const UINT32 ammoType = static_cast<UINT32>(ReadNative(GetAmmoType, sample.ped, weapon));
    if (!ammoType) return {"unknown_ammo_type"};
    const int total = ReadNative(GetAmmoByType, sample.ped, ammoType);
    if (total < loaded) return {"unknown_ammo_count"};
    const int reserve = total - loaded;
    const int amount = reserve / 4;
    if (amount == 0) return {"limit_reached", static_cast<float>(reserve), static_cast<float>(reserve)};

    CallNative(SetAmmoByType, sample.ped, ammoType, total - amount);
    int loadedAfter = 0;
    const bool clipUnchanged = ReadNative(GetAmmoInClip, sample.ped, &loadedAfter, weapon) && loadedAfter == loaded;
    const int totalAfter = ReadNative(GetAmmoByType, sample.ped, ammoType);
    return {clipUnchanged && totalAfter == total - amount ? "applied" : "unconfirmed",
            static_cast<float>(reserve), static_cast<float>(totalAfter - loadedAfter)};
}

constexpr UINT32 GameHash(std::string_view lowerCaseName) {
    UINT32 hash = 0;
    for (const unsigned char character : lowerCaseName) {
        hash += character;
        hash += hash << 10;
        hash ^= hash >> 6;
    }
    hash += hash << 3;
    hash ^= hash >> 11;
    hash += hash << 15;
    return hash;
}

struct NativeVector {
    alignas(8) float x;
    alignas(8) float y;
    alignas(8) float z;
};
static_assert(sizeof(NativeVector) == 24);

template <typename... Args>
NativeVector ReadVector(UINT64 hash, Args... args) {
    NativeVector value{};
    std::memcpy(&value, CallNative(hash, args...), sizeof(value));
    return value;
}

struct EnemyOption {
    const char* id;
    UINT32 model;
    UINT32 weapon;
    int key;
};

constexpr std::array EnemyOptions{
    EnemyOption{"bandit", GameHash("g_m_m_unibanditos_01"), GameHash("weapon_revolver_cattleman"), VK_F1},
    EnemyOption{"bounty_hunter", GameHash("g_m_m_bountyhunters_01"), GameHash("weapon_repeater_carbine"), VK_F2},
    EnemyOption{"wolf", GameHash("a_c_wolf"), 0, VK_F3},
    EnemyOption{"bear", GameHash("a_c_bear_01"), 0, VK_F4},
    EnemyOption{"vampire", GameHash("cs_vampire"), GameHash("weapon_melee_knife_vampire"), VK_F5},
};
static_assert(EnemyOptions[0].model == 0x19486791);
static_assert(EnemyOptions[1].model == 0x1973FE45);
static_assert(EnemyOptions[2].model == 0xBBD91DDA);
static_assert(EnemyOptions[3].model == 0xBCFD0E7F);
static_assert(EnemyOptions[4].model == 0xD95BCB7D && EnemyOptions[4].weapon == 0x14D3F94D);

bool FindEnemyPosition(int target, NativeVector& position) {
    if (ReadNative(IsEntityInWater, target)) return false;
    const auto origin = ReadVector(GetEntityCoords, target, 1, 0);
    constexpr std::array<NativeVector, 4> offsets{{{0, -18, 0}, {-18, 0, 0}, {18, 0, 0}, {0, 18, 0}}};
    for (const auto& offset : offsets) {
        const auto probe = ReadVector(GetOffsetCoords, target, offset.x, offset.y, offset.z);
        NativeVector candidate{};
        if (!ReadNative(GetSafeCoord, probe.x, probe.y, probe.z, 1, &candidate, 0)) continue;
        const float distance = std::hypot(candidate.x - origin.x, candidate.y - origin.y);
        if (distance >= 12 && distance <= 26 && std::abs(candidate.z - origin.z) <= 4 &&
            ReadNative(HasPositionCollision, candidate.x, candidate.y, candidate.z)) {
            position = candidate;
            return true;
        }
    }
    return false;
}

struct Ambush {
    const EnemyOption* option = nullptr;
    int target = 0;
    int enemy = 0;
    ULONGLONG deadline = 0;
    ULONGLONG combatCheckAt = 0;
    ULONGLONG availableAt = 0;
    bool cleanup = false;
    bool reportedCombat = false;

    const char* Start(const EnemyOption& selected, const Sample& sample, ULONGLONG now, bool shielded) {
        if (sample.context != FreeRoam) return "out_of_context";
        if (shielded) return "shielded";
        if (option) return "ambush_active";
        if (now < availableAt) return "cooldown";
        if (!ReadNative(IsModelValid, selected.model) || !ReadNative(IsModelAPed, selected.model)) {
            return "model_unavailable";
        }
        option = &selected;
        cleanup = false;
        target = sample.ped;
        deadline = now + 3000;
        availableAt = now + 60000;
        CallNative(RequestModel, option->model, 0);
        return "loading_model";
    }

    bool OwnsEnemy() const {
        return ReadNative(DoesEntityExist, enemy) &&
            static_cast<UINT32>(ReadNative(GetEntityModel, enemy)) == option->model &&
            ReadNative(BelongsToScript, enemy, 0);
    }

    const char* Tick(const Sample& sample, ULONGLONG now, bool shielded) {
        if (!option) return nullptr;
        if (sample.context == "online") {
            cleanup = true;
            return nullptr;
        }
        if (enemy) {
            if (!OwnsEnemy()) {
                option = nullptr;
                enemy = 0;
                return "entity_no_longer_owned";
            }
            if (cleanup || sample.context != FreeRoam || sample.ped != target ||
                now >= deadline || ReadNative(IsEntityDead, enemy)) {
                cleanup = true;
                int handle = enemy;
                CallNative(DeletePed, &handle);
                if (OwnsEnemy()) return nullptr;
                option = nullptr;
                enemy = 0;
                return "entity_removed";
            }
            if (!reportedCombat) {
                const bool inCombat = ReadNative(IsPedInCombat, enemy, target) != 0;
                if (inCombat || now >= combatCheckAt) {
                    reportedCombat = true;
                    return inCombat ? "combat_confirmed" : "combat_unconfirmed";
                }
            }
            return nullptr;
        }

        const char* failure = nullptr;
        if (cleanup || sample.context != FreeRoam || sample.ped != target) failure = "context_changed";
        else if (shielded) failure = "shielded";
        else if (now >= deadline) failure = "model_timeout";
        else if (!ReadNative(HasModelLoaded, option->model)) return nullptr;

        if (!failure) {
            NativeVector position{};
            if (!FindEnemyPosition(target, position)) failure = "no_safe_position";
            else {
                enemy = ReadNative(CreatePed, option->model, position.x, position.y, position.z, 0.0f, 0, 1, 0, 0);
                if (!enemy || !ReadNative(DoesEntityExist, enemy)) failure = "spawn_failed";
            }
        }
        CallNative(ReleaseModel, option->model);
        if (failure) {
            option = nullptr;
            enemy = 0;
            cleanup = false;
            return failure;
        }

        CallNative(SetMissionEntity, enemy, 1, 0);
        CallNative(EquipOutfitPreset, enemy, 0, 0);
        if (ReadNative(IsEntityInWater, enemy)) {
            cleanup = true;
            return "unsafe_spawn";
        }
        if (option->weapon) {
            CallNative(GiveWeaponToPed, enemy, option->weapon, 30, 1, 0, 0, 0, 0.0f, 0.0f,
                       GameHash("add_reason_default"), 1, 0.0f, 0);
        }
        CallNative(TaskCombatPed, enemy, target, 0, 16);
        deadline = now + 45000;
        combatCheckAt = now + 3000;
        reportedCombat = false;
        return "spawned";
    }
};

EffectResult ModifyWanted(const Sample& sample, bool start) {
    const int player = ReadNative(PlayerId);
    const int before = ReadNative(GetWantedLevel, player);
    const int bounty = ReadNative(GetBounty, player);
    if (before < 0 || bounty < 0) return {"invalid_wanted_state"};
    if (start ? before > 0 : before == 0) return {"limit_reached", static_cast<float>(before), static_cast<float>(before)};

    if (start) {
        CallNative(ReportCrime, player, GameHash("crime_wanted_level_up_debug_high"), 0, sample.ped, 1);
        CallNative(ForceLawResponse);
        CallNative(SetWantedIntensity, player, 100);
    } else {
        CallNative(ClearCurrentPursuit);
        CallNative(ClearWantedScore, player);
        CallNative(SetWantedIntensity, player, 0);
    }
    const int after = ReadNative(GetWantedLevel, player);
    return {ReadNative(GetBounty, player) == bounty && (start ? after > 0 : after == 0)
                ? "applied" : "unconfirmed", static_cast<float>(before), static_cast<float>(after)};
}

EffectResult ApplyEffect(Effect effect, const Sample& sample, ULONGLONG now, ULONGLONG& shieldUntil) {
    if (sample.context != FreeRoam) return {"out_of_context"};
    switch (effect) {
        case Effect::Heal: return ModifyHealth(sample, true);
        case Effect::Damage: return ModifyHealth(sample, false);
        case Effect::DeadEye: return RestoreDeadEye();
        case Effect::HorseStamina: return RestoreHorse(sample);
        case Effect::Shield:
            shieldUntil = now + 15000;
            return {"applied"};
        case Effect::RemoveAmmo: return RemoveAmmo(sample);
        case Effect::StartWanted: return ModifyWanted(sample, true);
        case Effect::ClearWanted: return ModifyWanted(sample, false);
    }
    return {"unknown_effect"};
}

using Json = nlohmann::json;

class LiveBridge {
    std::filesystem::path directory;
    std::string session, epoch, ambushId;
    std::unordered_set<std::string> seen;
    Json acks = Json::array();
    unsigned generation = 0;
    int lastPed = 0;
    ULONGLONG nextPoll = 0, shieldUntil = 0;
    ULONGLONG ambushExpires = 0;
    std::array<ULONGLONG, PreviewEffects.size()> availableAt{};
    Ambush ambush;

    static bool ValidId(const std::string& id) {
        return !id.empty() && id.size() <= 80 && id.find_first_not_of(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-") == std::string::npos;
    }

    static std::string ReadFile(const std::filesystem::path& path, DWORD limit) {
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE |
                                  FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (file == INVALID_HANDLE_VALUE) return {};
        const DWORD size = GetFileSize(file, nullptr);
        std::string data;
        DWORD count = 0;
        if (size <= limit) {
            data.resize(size);
            if (!::ReadFile(file, data.data(), size, &count, nullptr) || count != size) data.clear();
        }
        CloseHandle(file);
        return data;
    }

    void WriteStatus(const Json& status) const {
        const auto temp = directory / L"status.tmp";
        const auto path = directory / L"status.json";
        std::ofstream out(temp, std::ios::binary | std::ios::trunc);
        out << status.dump();
        out.close();
        if (out) MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING);
    }

    void Ack(const std::string& id, const char* result) {
        acks.push_back({{"id", id}, {"result", result}});
        if (acks.size() > 100) acks.erase(acks.begin());
    }

    const char* Command(const Json& inbox, const Json& command, const std::string& id,
                        const Sample& sample, ULONGLONG now, bool paused) {
        if (sample.context != FreeRoam) return "fuera de exploración libre";
        if (paused) return "pausado";
        if (command.value("epoch", "") != epoch) return "intento anterior";
        const auto expires = command.value("expiresMs", 0ULL);
        if (expires <= now || expires - now > 10000) return "caducado";
        const auto effect = command.value("effect", "");
        const bool safe = inbox.value("noDeath", true) || command.value("safe", true);
        if (effect == "bandit_ambush") {
            if (safe) return "modo sin morir activo";
            const auto arg = command.value("arg", "");
            const auto choice = std::find_if(EnemyOptions.begin(), EnemyOptions.end(),
                [&](const EnemyOption& option) { return arg == option.id; });
            if (choice == EnemyOptions.end()) return "enemigo desconocido";
            const char* result = ambush.Start(*choice, sample, now, now < shieldUntil);
            if (std::string_view(result) != "loading_model") return result;
            ambushId = id;
            ambushExpires = expires;
            return nullptr;
        }
        for (size_t index = 0; index < PreviewEffects.size(); ++index) {
            const auto& option = PreviewEffects[index];
            if (effect != option.name) continue;
            if (option.extended && (!inbox.value("allowDestructive", false) ||
                                    !command.value("allowDestructive", false))) return "permiso requerido";
            if (option.sabotage && now < shieldUntil) return "bloqueado por escudo";
            if (now < availableAt[index]) return "en enfriamiento";
            const auto result = ApplyEffect(option.effect, sample, now, shieldUntil);
            const std::string_view reason = result.reason;
            if (reason == "applied" || reason == "unconfirmed") availableAt[index] = now + option.cooldown;
            if (option.effect == Effect::Shield && reason == "applied") {
                shieldUntil = now + std::clamp(command.value("ms", 15000), 1000, 15000);
            }
            if (reason == "applied") return "ok";
            if (reason == "unconfirmed") return "resultado incierto; efecto sin confirmar";
            return result.reason;
        }
        return "efecto desconocido";
    }

public:
    explicit LiveBridge(const std::filesystem::path& root) {
        if (root.empty()) return;
        auto candidate = ReadFile(root / L"active-session.txt", 32);
        if (candidate.size() != 32 || candidate.find_first_not_of("0123456789abcdef") != std::string::npos) return;
        session = std::move(candidate);
        directory = root / session;
    }

    bool Active() const { return !session.empty(); }

    void Tick(const Sample& sample, ULONGLONG now) {
        const bool playing = sample.context == FreeRoam;
        const int ped = playing ? sample.ped : 0;
        if (ped != lastPed || epoch.empty()) {
            epoch = session + "_" + std::to_string(++generation);
            shieldUntil = 0;
            if (ambush.option) ambush.cleanup = true;
            lastPed = ped;
        }
        if (now < nextPoll) return;
        nextPoll = now + 100;
        auto inbox = Json::parse(ReadFile(directory / L"inbox.json", 65536), nullptr, false);
        bool fresh = false, paused = true;
        try {
            if (inbox.is_object() && inbox.value("session", "") == session) {
                const auto heartbeat = inbox.value("heartbeatMs", 0ULL);
                fresh = heartbeat && now >= heartbeat && now - heartbeat <= 2000;
                paused = !fresh || inbox.value("paused", true);
            }
        } catch (const Json::exception&) {
            fresh = false;
            paused = true;
        }
        if (paused) {
            shieldUntil = 0;
            if (ambush.option) ambush.cleanup = true;
        }
        if (ambush.option && !ambushId.empty() && now >= ambushExpires) {
            ambush.cleanup = true;
            Ack(ambushId, ambush.enemy ? "resultado incierto; combate sin confirmar" : "caducado");
            ambushId.clear();
        }
        if (ambush.option) {
            const char* result = ambush.Tick(sample, now, now < shieldUntil);
            if (result && !ambushId.empty() && std::string_view(result) != "spawned") {
                const std::string_view outcome = result;
                Ack(ambushId, outcome == "combat_confirmed" ? "ok" :
                    outcome == "combat_unconfirmed" ? "resultado incierto; combate sin confirmar" : result);
                ambushId.clear();
            }
        }
        if (fresh && inbox.contains("effects") && inbox["effects"].is_array() && inbox["effects"].size() <= 20) {
            for (const auto& command : inbox["effects"]) {
                if (!command.is_object() || !command.contains("id") || !command["id"].is_string()) continue;
                const std::string id = command["id"];
                if (!ValidId(id) || seen.size() >= 100000 || !seen.insert(id).second) continue;
                const char* result = "comando inválido";
                try {
                    result = Command(inbox, command, id, sample, now, paused);
                } catch (const Json::exception&) {}
                if (result) Ack(id, result);
            }
        }
        Json capabilities = Json::array();
        if (playing) {
            for (const auto& effect : PreviewEffects) {
                if (effect.effect == Effect::HorseStamina && !sample.mounted) continue;
                capabilities.push_back(effect.name);
            }
            capabilities.push_back("bandit_ambush");
        }
        WriteStatus({{"session", session}, {"bridgeVersion", "cloud90-rdr2-1"},
                     {"pid", GetCurrentProcessId()}, {"heartbeatMs", now}, {"epoch", epoch},
                     {"inPlay", playing}, {"capabilities", capabilities}, {"acks", acks}});
    }
};

bool IsKeyDown(int key) {
    return (GetAsyncKeyState(key) & 0x8000) != 0;
}

bool GameHasFocus() {
    DWORD foregroundPid = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &foregroundPid);
    return foregroundPid == GetCurrentProcessId();
}

bool HasMarker(const std::filesystem::path& directory, const wchar_t* name) {
    if (directory.empty()) return false;
    const DWORD attributes = GetFileAttributesW((directory / name).c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

void LogAmbush(const std::filesystem::path& path, ULONGLONG elapsed, const EnemyOption& option, const char* result) {
    FILE* log = nullptr;
    if (_wfopen_s(&log, path.c_str(), L"a") == 0) {
        std::fprintf(log, "%llu ms effect=bandit_ambush enemy=%s result=%s\n", elapsed, option.id, result);
        std::fclose(log);
    }
}

std::filesystem::path GetLogPath() {
    PWSTR localAppData = nullptr;
    const HRESULT result = SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData);
    if (FAILED(result)) {
        CoTaskMemFree(localAppData);
        return {};
    }
    const std::filesystem::path localPath(localAppData);
    CoTaskMemFree(localAppData);
    const auto directory = localPath / L"Cloud90GameLink" / L"rdr2";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    return error ? std::filesystem::path{} : directory / L"diagnostics.log";
}

void ScriptMain() {
    const auto logPath = GetLogPath();
    FILE* log = nullptr;
    const bool canLog = !logPath.empty() && _wfopen_s(&log, logPath.c_str(), L"w") == 0;
    if (canLog) {
        std::fprintf(log, "Cloud90 RDR2 diagnostic v1, pid=%lu\n", GetCurrentProcessId());
        std::fclose(log);
    } else {
        OutputDebugStringA("Cloud90 RDR2: could not open diagnostics.log\n");
    }

    const auto start = GetTickCount64();
    LiveBridge live(logPath.parent_path());
    const bool preview = !live.Active() && canLog && HasMarker(logPath.parent_path(), L"enable-preview.txt");
    const bool extendedPreview = preview && HasMarker(logPath.parent_path(), L"enable-extended-preview.txt");
    std::array<bool, PreviewEffects.size()> held{};
    std::array<ULONGLONG, PreviewEffects.size()> availableAt{};
    std::array<bool, EnemyOptions.size()> enemyKeysHeld{};
    Ambush ambush;
    ULONGLONG shieldUntil = 0;
    ULONGLONG nextLog = 0;
    int lastPed = 0;
    Sample previous{""};
    while (true) {
        const Sample sample = ReadContext();
        const auto now = GetTickCount64();
        if (now >= nextLog) {
            if (canLog && sample != previous && _wfopen_s(&log, logPath.c_str(), L"a") == 0) {
                std::fprintf(log, "%llu ms context=%s ped=%d health=%d max_health=%d mounted=%d\n",
                             now - start, sample.context.data(), sample.ped, sample.health,
                             sample.maxHealth, static_cast<int>(sample.mounted));
                std::fclose(log);
            }
            previous = sample;
            nextLog = now + 500;
        }

        if (preview) {
            if (sample.context != FreeRoam || sample.ped != lastPed) shieldUntil = 0;
            lastPed = sample.ped;
            const bool modifier = IsKeyDown(VK_CONTROL) && IsKeyDown(VK_SHIFT);
            for (size_t i = 0; i < PreviewEffects.size(); ++i) {
                const auto& effect = PreviewEffects[i];
                const bool down = modifier && IsKeyDown(effect.key);
                const bool pressed = down && !held[i];
                held[i] = down;
                if (!pressed || !GameHasFocus()) continue;
                EffectResult result{"extended_preview_disabled"};
                if (!effect.extended || extendedPreview) {
                    if (now < availableAt[i]) result = {"cooldown"};
                    else if (effect.sabotage && now < shieldUntil) result = {"shielded"};
                    else result = ApplyEffect(effect.effect, ReadContext(), now, shieldUntil);
                }
                if (std::string_view(result.reason) == "applied" || std::string_view(result.reason) == "unconfirmed") {
                    availableAt[i] = now + effect.cooldown;
                }
                if (_wfopen_s(&log, logPath.c_str(), L"a") == 0) {
                    std::fprintf(log, "%llu ms effect=%s result=%s before=%.3f after=%.3f\n",
                                 now - start, effect.name, result.reason, result.before, result.after);
                    std::fclose(log);
                }
            }
            if (extendedPreview) {
                for (size_t i = 0; i < EnemyOptions.size(); ++i) {
                    const auto& option = EnemyOptions[i];
                    const bool down = modifier && IsKeyDown(option.key);
                    const bool pressed = down && !enemyKeysHeld[i];
                    enemyKeysHeld[i] = down;
                    if (pressed && GameHasFocus()) {
                        const char* result = ambush.Start(option, ReadContext(), now, now < shieldUntil);
                        LogAmbush(logPath, now - start, option, result);
                    }
                }
                if (const auto* option = ambush.option) {
                    if (const char* result = ambush.Tick(sample, now, now < shieldUntil)) {
                        LogAmbush(logPath, now - start, *option, result);
                    }
                }
            }
        }
        if (live.Active()) live.Tick(sample, now);
        scriptWait(preview ? 0 : live.Active() ? 50 : 500);
    }
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) scriptRegister(module, ScriptMain);
    if (reason == DLL_PROCESS_DETACH) scriptUnregister(module);
    return TRUE;
}
