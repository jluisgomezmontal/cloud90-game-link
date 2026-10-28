// GTA SA 1.0 US only. Addresses/offsets verified against DK22Pac/plugin-sdk.
// All game access runs after CGame::Process on the game's main thread.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <wincrypt.h>
#include <algorithm>
#include <fstream>
#include <string>
#include <vector>
#include <unordered_set>
#include "vendor/json.hpp"
#include "vendor/minhook/include/MinHook.h"
using json = nlohmann::json;
static void (__cdecl *originalProcess)();
static std::wstring directory;
static std::string session, epoch;
static unsigned generation = 0;
static bool wasPlaying = false, localPaused = false, f9Down = false, panicAwaitingAck = false;
static ULONGLONG lastTick = 0;
static std::unordered_set<std::string> seen;
static json acks = json::array();
static const std::vector<std::string> capabilities = {"heal", "armour", "money_add", "wanted_clear", "wanted_set", "repair_vehicle", "drunk_controls", "invert_controls", "no_shoot", "remove_weapon", "remove_ammo", "launch", "invert_steering", "stuck_accelerator", "burst_tyres", "ice_floor", "spin_vehicle", "engine_off", "no_jump", "freeze_feet", "dense_fog", "camera_shake", "spawn_enemies", "spawn_pursuer", "spawn_obstacles"};
template<class T> static T& field(void* p, size_t offset) { return *reinterpret_cast<T*>(static_cast<char*>(p) + offset); }
static std::wstring env(const wchar_t* key) { wchar_t buffer[32768]; DWORD n = GetEnvironmentVariableW(key, buffer, 32768); return n && n < 32768 ? std::wstring(buffer, n) : L""; }
static bool validId(const std::string& id) {
    if (id.empty() || id.size() > 80) return false;
    for (unsigned char c : id) if (!isalnum(c) && c != '_' && c != '-') return false;
    return true;
}
static json readInbox() {
    HANDLE file = CreateFileW((directory + L"\\inbox.json").c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    DWORD size = GetFileSize(file, nullptr), count = 0;
    std::string bytes;
    if (size <= 65536) { bytes.resize(size); if (!ReadFile(file, bytes.data(), size, &count, nullptr) || count != size) bytes.clear(); }
    CloseHandle(file);
    return json::parse(bytes, nullptr, false);
}
static void writeStatus(const json& status) {
    const std::string bytes = status.dump();
    std::wstring temp = directory + L"\\status.tmp", path = directory + L"\\status.json";
    HANDLE file = CreateFileW(temp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD count = 0; bool ok = WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr) && count == bytes.size();
    CloseHandle(file);
    if (ok) MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}
static bool supportedExe() {
    wchar_t path[MAX_PATH]; if (!GetModuleFileNameW(nullptr, path, MAX_PATH)) return false;
    HCRYPTPROV provider = 0; HCRYPTHASH hash = 0;
    if (!CryptAcquireContextW(&provider, nullptr, nullptr, PROV_RSA_AES, CRYPT_VERIFYCONTEXT)) return false;
    bool ok = CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash) != 0;
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) ok = false;
    BYTE buffer[16384]; DWORD count;
    while (ok) { if (!ReadFile(file, buffer, sizeof(buffer), &count, nullptr)) { ok = false; break; } if (!count) break; ok = CryptHashData(hash, buffer, count, 0) != 0; }
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    BYTE digest[32]; DWORD length = sizeof(digest);
    if (ok) ok = CryptGetHashParam(hash, HP_HASHVAL, digest, &length, 0) != 0;
    std::string hex; const char* digits = "0123456789abcdef";
    if (ok) for (BYTE b : digest) { hex += digits[b >> 4]; hex += digits[b & 15]; }
    if (hash) CryptDestroyHash(hash); CryptReleaseContext(provider, 0);
    return ok && hex == "a559aa772fd136379155efa71f00c47aad34bbfeae6196b0fe1047d0645cbd26" && reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)) == 0x400000;
}
#include "Effects.h"
#include "Spawns.h"
static int lastPed = -1;
static void tick() {
    ULONGLONG now = GetTickCount64(); if (now - lastTick < 100) return; lastTick = now;
    // Player 1 only; refuse two-player mode, menus, cutscenes, death/arrest.
    void* info = reinterpret_cast<void*>(0xB7CD98);
    void* ped = field<void*>(info, 0);
    bool playing = playingNow();
    int currentPed = playing ? pedRef(ped) : -1;
    if (playing != wasPlaying || currentPed != lastPed) { epoch = session + "_" + std::to_string(++generation); wasPlaying = playing; lastPed = currentPed; activeEffects.clear(); }
    DWORD foregroundPid = 0; GetWindowThreadProcessId(GetForegroundWindow(), &foregroundPid);
    bool down = foregroundPid == GetCurrentProcessId() && (GetAsyncKeyState(VK_F9) & 0x8000);
    if (down && !f9Down) { localPaused = !localPaused; panicAwaitingAck = localPaused; } f9Down = down;
    json inbox = readInbox();
    bool fresh = inbox.is_object() && inbox.value("session", "") == session && inbox.contains("heartbeatMs") && inbox["heartbeatMs"].is_number_unsigned();
    if (fresh) { ULONGLONG beat = inbox["heartbeatMs"].get<ULONGLONG>(); fresh = now >= beat && now - beat <= 2000; }
    // F9 pauses immediately. Once Game Link acknowledges it, its Resume button
    // must also be able to release the local pause.
    if (fresh && localPaused) {
        if (inbox.value("paused", true)) panicAwaitingAck = false;
        else if (!panicAwaitingAck) localPaused = false;
    }
    remotePaused = !fresh || inbox.value("paused", true);
    inboxBeat = fresh ? inbox["heartbeatMs"].get<ULONGLONG>() : 0;
    clearExpiredEffects();
    updateSpawns();
    if (fresh && inbox.contains("effects") && inbox["effects"].is_array() && inbox["effects"].size() <= 20) {
        for (const auto& command : inbox["effects"]) {
            if (!command.is_object() || !command.contains("id") || !command["id"].is_string()) continue;
            std::string id = command["id"].get<std::string>(); if (!validId(id) || seen.count(id)) continue;
            // Bound memory without forgetting command IDs (until the next launch).
            if (seen.size() >= 100000) continue;
            seen.insert(id);
            std::string result = "ok", effect = command.value("effect", ""), arg = command.value("arg", "");
            if (!playing) result = "no disponible ahora";
            else if (localPaused || inbox.value("paused", true)) result = "pausado";
            else if (command.value("epoch", "") != epoch) result = "intento anterior";
            else if (!command.contains("expiresMs") || !command["expiresMs"].is_number_unsigned() || command["expiresMs"].get<ULONGLONG>() <= now || command["expiresMs"].get<ULONGLONG>() > now + 10000) result = "caducado";
            else if (effect == "heal") field<float>(ped, 0x540) = std::clamp(field<float>(ped, 0x544), 1.0f, 1000.0f);
            else if (effect == "armour") field<float>(ped, 0x548) = std::clamp<int>(field<unsigned char>(info, 0x150), 100, 150);
            else if (effect == "money_add") { int& money = field<int>(info, 0xB8); money = static_cast<int>(std::clamp<long long>(static_cast<long long>(money) + 1000, 0, 999999999)); }
            else if (effect == "wanted_clear" || effect == "wanted_set") {
                int level = 0;
                if (effect == "wanted_set") { if (arg.empty()) level = 3; else if (arg.size() != 1 || arg[0] < '1' || arg[0] > '6') result = "nivel invalido"; else level = arg[0] - '0'; }
                void* wanted = reinterpret_cast<void* (__cdecl *)(int)>(0x56E230)(0);
                if (!wanted) result = "no disponible ahora";
                else if (result == "ok") reinterpret_cast<void (__thiscall *)(void*, int)>(0x562470)(wanted, level);
            } else if (effect == "repair_vehicle") {
                void* vehicle = reinterpret_cast<void* (__cdecl *)(int, bool)>(0x56E0D0)(0, false);
                if (!vehicle || field<float>(vehicle, 0x4C0) <= 0 || field<int>(vehicle, 0x590) != 0) result = "necesitas un automovil";
                else { auto vtable = *reinterpret_cast<void***>(vehicle); reinterpret_cast<void (__thiscall *)(void*)>(vtable[50])(vehicle); field<float>(vehicle, 0x4C0) = 1000.0f; }
            } else if (spawnEffect(effect)) result = queueSpawn(effect, command);
            else if (trollEffect(effect)) result = applyTroll(effect, command.value("ms", 0), command, inbox);
            else result = "efecto desconocido";
            if (result != "pending") { acks.push_back({{"id", id}, {"result", result}}); if (acks.size() > 100) acks.erase(acks.begin()); }
        }
    }
    json status = {{"session", session}, {"bridgeVersion", "cloud90-gtasa-3"}, {"pid", GetCurrentProcessId()}, {"heartbeatMs", now}, {"epoch", epoch}, {"inPlay", playing}, {"paused", localPaused}, {"capabilities", capabilities}, {"acks", acks}, {"reason", playing ? "CJ listo" : "menu, pausa o partida no disponible"}};
    status["activeEffects"] = json::array(); for (const auto& effect : activeEffects) status["activeEffects"].push_back(effect.first);
    status["spawnedEntities"] = ownedSpawns.size(); status["preparingSpawn"] = pendingSpawn.active;
    if (playing) status["telemetry"] = {{"health", field<float>(ped, 0x540)}, {"armour", field<float>(ped, 0x548)}, {"money", field<int>(info, 0xB8)}};
    writeStatus(status);
}
static void __cdecl process() {
    // Remove our actors before AI/physics gets another step after a panic.
    try { if ((!effectsAllowed() || sceneCleanupRequested) && (pendingSpawn.active || !ownedSpawns.empty())) updateSpawns(); }
    catch (...) { remotePaused = true; }
    originalProcess();
    try { tick(); clearExpiredEffects(); } catch (...) { remotePaused = true; activeEffects.clear(); restoreFog(); }
}
extern "C" __declspec(dllexport) void InitializeASI() {
    static bool initialized = false; if (initialized) return; initialized = true;
    std::wstring value = env(L"CLOUD90_GTASA_SESSION");
    if (value.size() != 32 || value.find_first_not_of(L"0123456789abcdef") != std::wstring::npos) return;
    for (wchar_t c : value) session += static_cast<char>(c); directory = env(L"CLOUD90_GTASA_DIRECTORY");
    if (directory.empty() || !supportedExe() || GetModuleHandleW(L"samp.dll") || GetModuleHandleW(L"moonloader.asi")) return;
    epoch = session + "_0";
    if (MH_Initialize() != MH_OK) return;
    if (MH_CreateHook(reinterpret_cast<void*>(0x53BEE0), &process, reinterpret_cast<void**>(&originalProcess)) != MH_OK) return;
    if (!installEffectHooks() || !installSpawnHooks() || MH_EnableHook(MH_ALL_HOOKS) != MH_OK) return;
    writeStatus({{"session", session}, {"bridgeVersion", "cloud90-gtasa-3"}, {"pid", GetCurrentProcessId()}, {"heartbeatMs", GetTickCount64()}, {"epoch", epoch}, {"inPlay", false}, {"paused", false}, {"capabilities", capabilities}, {"acks", acks}, {"reason", "modulo cargado; esperando partida"}});
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance); return TRUE; }
