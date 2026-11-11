#define WIN32_LEAN_AND_MEAN
#include <main.h>
#include <shlobj.h>

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

template <typename... Args>
int ReadNative(UINT64 hash, Args... args) {
    nativeInit(hash);
    (nativePush64(static_cast<UINT64>(args)), ...);
    // These natives return BOOL or a 32-bit integer in the low word.
    return static_cast<int>(*nativeCall());
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

    return {ReadNative(GetMissionFlag) ? "story_mission" : "story_free_roam_candidate",
            ped, health, maxHealth, ReadNative(IsPedOnMount, ped) != 0};
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
    Sample previous{""};
    while (true) {
        const Sample sample = ReadContext();
        if (canLog && sample != previous && _wfopen_s(&log, logPath.c_str(), L"a") == 0) {
            std::fprintf(log, "%llu ms context=%s ped=%d health=%d max_health=%d mounted=%d\n",
                         GetTickCount64() - start, sample.context.data(), sample.ped, sample.health,
                         sample.maxHealth, static_cast<int>(sample.mounted));
            std::fclose(log);
        }
        previous = sample;
        scriptWait(500);
    }
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) scriptRegister(module, ScriptMain);
    if (reason == DLL_PROCESS_DETACH) scriptUnregister(module);
    return TRUE;
}
