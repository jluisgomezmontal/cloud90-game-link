// Bounded GTA-only effects. Offsets and entry points: plugin-sdk / gta-reversed.
// Input is changed only after CPad refresh; handling is scoped to one physics call.
#include <cmath>
#include <cstring>
#include <map>
struct ActiveEffect { ULONGLONG until; int pedRef; int vehicleRef; };
static std::map<std::string, ActiveEffect> activeEffects;
static std::map<std::string, ULONGLONG> lastEffect;
static bool remotePaused = true;
static bool sceneCleanupRequested = false;
static ULONGLONG inboxBeat = 0;
static void (__cdecl *originalPads)();
static void (__thiscall *originalCarControl)(void*);
static void (__cdecl *originalWeather)();
static void (__thiscall *originalCamera)(void*);
static bool fogApplied = false;
static float previousFog = 0;
static bool worldEffect(const std::string& name) { return name == "dense_fog" || name == "camera_shake"; }
static void restoreFog() {
    if (fogApplied && *reinterpret_cast<float*>(0xC81300) == 1.0f) *reinterpret_cast<float*>(0xC81300) = previousFog;
    fogApplied = false;
}
static void* player() { return *reinterpret_cast<void**>(0xB7CD98); }
static int pedRef(void* ped) { return reinterpret_cast<int (__cdecl *)(void*)>(0x54FF60)(ped); }
static int vehicleRef(void* car) { return reinterpret_cast<int (__cdecl *)(void*)>(0x54FFC0)(car); }
static void* vehicleByRef(int ref) { return reinterpret_cast<void* (__cdecl *)(int)>(0x54FFF0)(ref); }
static void* currentVehicle() { return reinterpret_cast<void* (__cdecl *)(int, bool)>(0x56E0D0)(0, false); }
static bool playingNow() {
    void* ped = player();
    return ped && !*reinterpret_cast<void**>(0xB7CD98 + 0x190) &&
        !*reinterpret_cast<bool*>(0xB7CB48) && !*reinterpret_cast<bool*>(0xB7CB49) &&
        !*reinterpret_cast<bool*>(0xB5F851) && !*reinterpret_cast<bool*>(0xB5F852) &&
        field<unsigned char>(reinterpret_cast<void*>(0xB7CD98), 0xDC) == 0 && field<float>(ped, 0x540) > 0 &&
        field<int>(ped, 0x530) != 54 && field<int>(ped, 0x530) != 55 && field<int>(ped, 0x530) != 56 && field<int>(ped, 0x530) != 63;
}
static bool effectsAllowed() {
    ULONGLONG now = GetTickCount64();
    return !localPaused && !remotePaused && now >= inboxBeat && now - inboxBeat <= 2000 && playingNow();
}
static bool driverOf(void* ped, void* car) {
    return car && field<float>(car, 0x4C0) > 0 && field<int>(car, 0x590) == 0 && field<int>(car, 0x594) == 0 && field<void*>(car, 0x460) == ped;
}
static bool footOnly(const std::string& effect) { return effect == "drunk_controls" || effect == "invert_controls" || effect == "no_shoot" || effect == "no_jump" || effect == "freeze_feet"; }
static bool active(const char* name, void* car = nullptr) {
    if (!effectsAllowed()) return false;
    auto found = activeEffects.find(name);
    if (found == activeEffects.end() || GetTickCount64() >= found->second.until) return false;
    void* ped = player();
    if (pedRef(ped) != found->second.pedRef) return false;
    if (found->second.vehicleRef == -2) return true;
    if (found->second.vehicleRef >= 0) return car && vehicleByRef(found->second.vehicleRef) == car && currentVehicle() == car && driverOf(ped, car);
    return !currentVehicle();
}
static void clearExpiredEffects() {
    if (!effectsAllowed()) { activeEffects.clear(); restoreFog(); return; }
    void* ped = player(); void* car = currentVehicle();
    for (auto it = activeEffects.begin(); it != activeEffects.end();) {
        const auto& value = it->second;
        bool remove = GetTickCount64() >= value.until || pedRef(ped) != value.pedRef;
        if (value.vehicleRef >= 0) remove |= !car || vehicleByRef(value.vehicleRef) != car || !driverOf(ped, car);
        else if (value.vehicleRef == -1) remove |= car != nullptr;
        if (remove) it = activeEffects.erase(it); else ++it;
    }
    if (!active("dense_fog")) restoreFog();
}
static void __cdecl updatePads() {
    originalPads();
    DWORD foreground = 0; GetWindowThreadProcessId(GetForegroundWindow(), &foreground);
    bool down = foreground == GetCurrentProcessId() && (GetAsyncKeyState(VK_F9) & 0x8000);
    if (down && !f9Down) { localPaused = !localPaused; panicAwaitingAck = localPaused; }
    f9Down = down;
    if (!effectsAllowed()) { sceneCleanupRequested = true; activeEffects.clear(); restoreFog(); return; }
    void* pad = reinterpret_cast<void* (__cdecl *)(int)>(0x53FB70)(0);
    if (!pad || field<unsigned short>(pad, 0x10E)) return;
    void* car = currentVehicle();
    if (!car) {
        if (active("freeze_feet")) {
            field<short>(pad,0) = 0; field<short>(pad,2) = 0; field<short>(pad,0x20) = 0;
            field<float>(player(),0x44) = 0; field<float>(player(),0x48) = 0;
        }
        if (active("no_jump") || active("freeze_feet")) field<short>(pad,0x1C) = 0;
        if (!active("freeze_feet") && active("invert_controls")) {
            field<short>(pad, 0x0) = static_cast<short>(-std::clamp<int>(field<short>(pad, 0x0), -128, 128));
            field<short>(pad, 0x2) = static_cast<short>(-std::clamp<int>(field<short>(pad, 0x2), -128, 128));
        } else if (!active("freeze_feet") && active("drunk_controls")) {
            const int drift = static_cast<int>(std::sin(GetTickCount64() / 330.0) * 80.0);
            field<short>(pad, 0x0) = static_cast<short>(std::clamp<int>(field<short>(pad, 0x0) + drift, -128, 128));
        }
        if (active("no_shoot")) {
            short mode = field<short>(pad, 0x10A);
            if (mode == 2) field<short>(pad, 0x20) = 0;
            else if (mode == 3) field<short>(pad, 0xC) = 0;
            else { field<short>(pad, 0x22) = 0; field<short>(pad, 0x8) = 0; }
        }
    } else if (driverOf(player(), car)) {
        if (active("invert_steering", car)) {
            field<short>(pad, 0x0) = static_cast<short>(-std::clamp<int>(field<short>(pad, 0x0), -128, 128));
        }
        if (active("stuck_accelerator", car)) {
            if (field<short>(pad, 0x10A) == 3) field<short>(pad, 0x6) = -128;
            else field<short>(pad, 0x20) = 255;
        }
        if (active("engine_off", car)) {
            field<short>(pad,0x20) = 0;
            if (field<short>(pad,0x10A) == 3) field<short>(pad,6) = std::max<short>(0,field<short>(pad,6));
        }
    }
}
static void __fastcall carControl(void* car, void*) {
    bool ice = active("ice_floor",car), stopped = active("engine_off",car);
    if (!ice && !stopped) { originalCarControl(car); return; }
    void* originalHandling = field<void*>(car, 0x384);
    alignas(16) unsigned char copy[0xE0];
    if (ice && originalHandling) {
        std::memcpy(copy, originalHandling, sizeof(copy)); field<float>(copy, 0x28) *= 0.20f; field<float>(copy, 0xA4) *= 0.35f;
        field<void*>(car, 0x384) = copy;
    }
    bool wasOn = (field<unsigned char>(car,0x428) & 0x10) != 0;
    if (stopped) field<unsigned char>(car,0x428) &= 0xEF;
    originalCarControl(car);
    if (field<void*>(car, 0x384) == copy) field<void*>(car, 0x384) = originalHandling;
    if (stopped && wasOn && field<float>(car,0x4C0) > 0) field<unsigned char>(car,0x428) |= 0x10;
}
static void __cdecl weatherUpdate() {
    restoreFog(); originalWeather();
    if (active("dense_fog")) {
        previousFog = *reinterpret_cast<float*>(0xC81300); *reinterpret_cast<float*>(0xC81300) = 1.0f; fogApplied = true;
    }
}
static void __fastcall cameraProcess(void* camera, void*) {
    if (!active("camera_shake") || camera != reinterpret_cast<void*>(0xB6F028)) { originalCamera(camera); return; }
    unsigned beforeTime = field<unsigned>(camera,0x5C); float beforeForce = field<float>(camera,0x12C);
    void* ped = player(); void* matrix = field<void*>(ped,0x14);
    void* coords = matrix ? static_cast<char*>(matrix)+0x30 : static_cast<char*>(ped)+4;
    reinterpret_cast<void (__thiscall *)(void*,float,float,float,float)>(0x50A9F0)(camera,0.20f,field<float>(coords,0),field<float>(coords,4),field<float>(coords,8));
    originalCamera(camera);
    // Preserve stronger shakes from normal gameplay; our shake has no lasting state.
    if (field<float>(camera,0x12C) <= std::max(beforeForce,0.20f)) { field<unsigned>(camera,0x5C) = beforeTime; field<float>(camera,0x12C) = beforeForce; }
}
static bool installEffectHooks() {
    return MH_CreateHook(reinterpret_cast<void*>(0x541DD0), &updatePads, reinterpret_cast<void**>(&originalPads)) == MH_OK &&
        MH_CreateHook(reinterpret_cast<void*>(0x6B1880), &carControl, reinterpret_cast<void**>(&originalCarControl)) == MH_OK &&
        MH_CreateHook(reinterpret_cast<void*>(0x72B850), &weatherUpdate, reinterpret_cast<void**>(&originalWeather)) == MH_OK &&
        MH_CreateHook(reinterpret_cast<void*>(0x52B730), &cameraProcess, reinterpret_cast<void**>(&originalCamera)) == MH_OK;
}
static bool trollEffect(const std::string& effect) {
    return effect == "drunk_controls" || effect == "invert_controls" || effect == "no_shoot" || effect == "remove_weapon" ||
        effect == "remove_ammo" || effect == "launch" || effect == "invert_steering" || effect == "stuck_accelerator" ||
        effect == "burst_tyres" || effect == "ice_floor" || effect == "spin_vehicle" || effect == "engine_off" || effect == "no_jump" || effect == "freeze_feet" || worldEffect(effect);
}
static std::string applyTroll(const std::string& effect, int milliseconds, const json& command, const json& inbox) {
    void* ped = player(); void* car = currentVehicle();
    ULONGLONG now = GetTickCount64();
    bool dangerous = effect == "launch" || effect == "spin_vehicle";
    if (dangerous && (inbox.value("noDeath", true) || command.value("safe", true) || !inbox.value("allowDestructive", false) || !command.value("allowDestructive", false))) return "requiere permitir impulsos peligrosos y desactivar modo sin morir";
    ULONGLONG cooldown = dangerous ? 60000 : (effect == "remove_weapon" || effect == "remove_ammo" || effect == "burst_tyres" ? 15000 : 10000);
    auto previous = lastEffect.find(effect);
    if (previous != lastEffect.end() && now - previous->second < cooldown) return "en enfriamiento";
    bool foot = footOnly(effect) || effect == "remove_weapon" || effect == "remove_ammo" || effect == "launch";
    if (foot && car) return "requiere estar a pie";
    if (!foot && !worldEffect(effect) && !driverOf(ped, car)) return "requiere conducir un automovil";
    if (effect == "remove_weapon" || effect == "remove_ammo") {
        unsigned slot = field<unsigned char>(ped, 0x718);
        if (slot == 0 || slot >= 13) return "no hay arma equipada";
        void* weapon = static_cast<char*>(ped) + 0x5A0 + slot * 0x1C;
        int type = field<int>(weapon, 0);
        if (effect == "remove_weapon") {
            if (type < 1 || type > 43) return "arma no compatible";
            reinterpret_cast<void (__thiscall *)(void*, int)>(0x5E62B0)(ped, type);
        } else {
            if (type < 22 || type > 38 || field<unsigned>(weapon, 0xC) == 0) return "necesitas un arma con balas";
            field<unsigned>(weapon, 0x8) = 0; field<unsigned>(weapon, 0xC) = 0; field<int>(weapon, 0x4) = 3;
        }
    } else if (effect == "launch") {
        if (!(field<unsigned char>(ped, 0x46C) & 1) || field<void*>(ped, 0xFC)) return "requiere estar de pie sobre el suelo";
        field<float>(ped, 0x4C) = 0.30f;
        field<unsigned char>(ped, 0x46C) &= 0xFE;
    } else if (effect == "spin_vehicle") {
        float x = field<float>(car, 0x44), y = field<float>(car, 0x48);
        if (x * x + y * y < 0.0025f) return "necesitas estar conduciendo en movimiento";
        field<float>(car, 0x58) = 0.15f;
    } else if (effect == "burst_tyres") {
        auto vtable = *reinterpret_cast<void***>(car); bool changed = false;
        for (unsigned char tyre = 0; tyre < 4; ++tyre) changed |= reinterpret_cast<bool (__thiscall *)(void*, unsigned char, bool)>(vtable[44])(car, tyre, false);
        if (!changed) return "las llantas no se pueden pinchar o ya estan pinchadas";
    } else {
        int maximum = effect == "drunk_controls" || effect == "dense_fog" ? 15000 : (effect == "no_shoot" || effect == "ice_floor" ? 8000 : (effect == "stuck_accelerator" || effect == "freeze_feet" ? 3000 : (effect == "engine_off" ? 5000 : effect == "camera_shake" ? 6000 : 10000)));
        if (milliseconds < 1 || milliseconds > maximum) return "duracion invalida";
        if (effect == "invert_controls") activeEffects.erase("drunk_controls");
        if (effect == "drunk_controls") activeEffects.erase("invert_controls");
        if (effect == "engine_off") activeEffects.erase("stuck_accelerator");
        if (effect == "stuck_accelerator") activeEffects.erase("engine_off");
        activeEffects[effect] = {now + static_cast<ULONGLONG>(milliseconds), pedRef(ped), worldEffect(effect) ? -2 : car ? vehicleRef(car) : -1};
    }
    lastEffect[effect] = now;
    return "ok";
}
