#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <main.h>
#include <shlobj.h>

#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <string_view>

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
    const bool preview = canLog && HasMarker(logPath.parent_path(), L"enable-preview.txt");
    const bool extendedPreview = preview && HasMarker(logPath.parent_path(), L"enable-extended-preview.txt");
    std::array<bool, PreviewEffects.size()> held{};
    std::array<ULONGLONG, PreviewEffects.size()> availableAt{};
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
        }
        scriptWait(preview ? 0 : 500);
    }
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) scriptRegister(module, ScriptMain);
    if (reason == DLL_PROCESS_DETACH) scriptUnregister(module);
    return TRUE;
}
