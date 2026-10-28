// Fixed, internal SCM commands only. No script code/opcodes come from viewers.
// The tiny local context uses SA's documented script parameter format.
struct ScriptOutput { unsigned short index; };
struct ScriptResult {
    bool condition; int values[4];
    float number(unsigned index) const { float value; std::memcpy(&value, &values[index], 4); return value; }
};
struct ScriptBytes {
    std::vector<unsigned char> bytes;
    template<class T> void raw(T value) { const auto* p = reinterpret_cast<const unsigned char*>(&value); bytes.insert(bytes.end(), p, p + sizeof(value)); }
    void add(int value) { bytes.push_back(1); raw(value); }
    void add(float value) { bytes.push_back(6); raw(value); }
    void add(ScriptOutput value) { bytes.push_back(3); raw(value.index); }
};
template<class... Args> static ScriptResult scm(unsigned short opcode, Args... args) {
    alignas(16) unsigned char script[0xE0] = {};
    int saved[32]; std::memcpy(saved, reinterpret_cast<void*>(0xA43C78), sizeof(saved));
    reinterpret_cast<void (__thiscall *)(void*)>(0x4648E0)(script);
    std::memcpy(script + 8, "c90mod", 6);
    field<bool>(script, 0xC6) = false; field<bool>(script, 0xDC) = false;
    for (int i = 0; i < 4; ++i) field<int>(script, 0x3C + i * 4) = -1;
    ScriptBytes code; code.raw(opcode); (code.add(args), ...);
    field<void*>(script, 0x10) = code.bytes.data(); field<void*>(script, 0x14) = code.bytes.data();
    reinterpret_cast<void (__thiscall *)(void*)>(0x469EB0)(script);
    ScriptResult result = {}; result.condition = field<bool>(script, 0xC5);
    std::memcpy(result.values, script + 0x3C, sizeof(result.values));
    std::memcpy(reinterpret_cast<void*>(0xA43C78), saved, sizeof(saved));
    return result;
}
struct Point { float x, y, z; };
static Point position(void* entity) {
    void* matrix = field<void*>(entity, 0x14);
    return matrix ? Point{field<float>(matrix, 0x30), field<float>(matrix, 0x34), field<float>(matrix, 0x38)} : Point{field<float>(entity, 4), field<float>(entity, 8), field<float>(entity, 12)};
}
static Point forward(void* entity) {
    void* matrix = field<void*>(entity, 0x14);
    if (matrix) return {field<float>(matrix, 0x10), field<float>(matrix, 0x14), 0};
    float angle = field<float>(entity, 0x10); return {-std::sin(angle), std::cos(angle), 0};
}
static float distance2(Point a, Point b) { float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return x*x + y*y + z*z; }
static bool exteriorFreeRoam() {
    if (!playingNow() || *reinterpret_cast<int*>(0xB72914) != 0 || field<unsigned char>(player(), 0x2F) != 0) return false;
    if (reinterpret_cast<bool (__cdecl *)()>(0x464D50)()) return false;
    void* car = currentVehicle();
    return !car || driverOf(player(), car);
}
static bool groundPoint(Point& point, float playerZ) {
    bool found = false;
    float z = reinterpret_cast<float (__cdecl *)(float,float,float,bool*,void**)>(0x5696C0)(point.x, point.y, playerZ + 4, &found, nullptr);
    if (!found || !std::isfinite(z) || std::abs(z - playerZ) > 3.0f) return false;
    float water = 0;
    if (reinterpret_cast<bool (__cdecl *)(float,float,float,float*,int,int)>(0x6E8580)(point.x,point.y,z,&water,0,0) && water > z + 0.1f) return false;
    point.z = z;
    return true;
}
static bool emptySphere(Point point, float radius) {
    return !reinterpret_cast<void* (__cdecl *)(Point,float,void*,bool,bool,bool,bool,bool,bool)>(0x569E20)(point,radius,nullptr,true,true,true,true,true,false);
}
static bool lineClear(Point a, Point b) {
    return reinterpret_cast<bool (__cdecl *)(const Point&,const Point&,bool,bool,bool,bool,bool,bool,bool)>(0x56A490)(a,b,true,false,false,true,true,false,false);
}
enum class OwnedKind { Ped, Car, Object };
struct OwnedSpawn { OwnedKind kind; int handle; int model; ULONGLONG until; bool driver = false; };
struct PendingSpawn { bool active = false; std::string id, effect, arg, epoch; int duration = 0, ped = -1; ULONGLONG deadline = 0; };
static std::vector<OwnedSpawn> ownedSpawns;
static PendingSpawn pendingSpawn;
static unsigned lastWorldTime = 0;
static void (__cdecl *originalWorldReset)();
static bool spawnEffect(const std::string& name) { return name == "spawn_enemies" || name == "spawn_pursuer" || name == "spawn_obstacles"; }
static void spawnAck(const std::string& id, const std::string& result) {
    acks.push_back({{"id",id},{"result",result}}); if (acks.size() > 100) acks.erase(acks.begin());
}
static void* ownedEntity(const OwnedSpawn& spawn) {
    uintptr_t pool = spawn.kind == OwnedKind::Ped ? 0xB74490 : spawn.kind == OwnedKind::Car ? 0xB74494 : 0xB7449C;
    if (!*reinterpret_cast<void**>(pool)) return nullptr;
    uintptr_t lookup = spawn.kind == OwnedKind::Ped ? 0x54FF90 : spawn.kind == OwnedKind::Car ? 0x54FFF0 : 0x550050;
    void* entity = reinterpret_cast<void* (__cdecl *)(int)>(lookup)(spawn.handle);
    return entity && field<short>(entity, 0x22) == spawn.model ? entity : nullptr;
}
static void removeSpawn(const OwnedSpawn& spawn) {
    void* entity = ownedEntity(spawn); if (!entity || entity == player()) return;
    if (spawn.kind == OwnedKind::Car && currentVehicle() == entity) { scm(0x01C3, spawn.handle); return; }
    scm(spawn.kind == OwnedKind::Ped ? 0x009B : spawn.kind == OwnedKind::Car ? 0x00A6 : 0x0108, spawn.handle);
}
static void cancelPending(const std::string& reason) {
    if (!pendingSpawn.active) return;
    spawnAck(pendingSpawn.id, reason); pendingSpawn.active = false;
}
static void __cdecl worldReset() {
    // Pool generations are re-used on loading/restarting: never carry ownership
    // into the replacement world. Its normal reset disposes of the old entities.
    ownedSpawns.clear(); cancelPending("cambio de partida"); lastWorldTime = 0;
    activeEffects.clear(); restoreFog(); sceneCleanupRequested = true;
    epoch = session + "_" + std::to_string(++generation); wasPlaying = false;
    originalWorldReset();
}
static bool installSpawnHooks() { return MH_CreateHook(reinterpret_cast<void*>(0x53BCF0),&worldReset,reinterpret_cast<void**>(&originalWorldReset)) == MH_OK; }
static std::vector<int> spawnModels(const std::string& effect, const std::string& arg) {
    if (effect == "spawn_enemies") return {102, 346};
    if (effect == "spawn_pursuer") return {102, 560};
    return {arg == "barriers" ? 1228 : 1238};
}
static int countOwned(OwnedKind kind) { int count = 0; for (const auto& spawn : ownedSpawns) if (spawn.kind == kind && ownedEntity(spawn)) ++count; return count; }
static int countFootEnemies() { int count = 0; for (const auto& spawn : ownedSpawns) if (spawn.kind == OwnedKind::Ped && !spawn.driver && ownedEntity(spawn)) ++count; return count; }
static std::string queueSpawn(const std::string& effect, const json& command) {
    if (!exteriorFreeRoam()) return "apariciones solo en exteriores fuera de misiones, a pie o conduciendo un automovil";
    if (pendingSpawn.active) return "ya se esta preparando una aparicion";
    std::string arg = command.value("arg", "");
    if (effect == "spawn_enemies" && !arg.empty() && arg != "1" && arg != "2" && arg != "3") return "cantidad invalida";
    if (effect == "spawn_obstacles" && !arg.empty() && arg != "cones" && arg != "barriers") return "obstaculo invalido";
    if (effect == "spawn_pursuer" && !arg.empty()) return "opcion invalida";
    int amount = effect == "spawn_enemies" ? (arg.empty() ? 2 : arg[0]-'0') : 3;
    if (effect == "spawn_enemies" && (countFootEnemies() + amount > 3 || countOwned(OwnedKind::Ped) + amount > 4)) return "limite de enemigos activos";
    if (effect == "spawn_pursuer" && (countOwned(OwnedKind::Car) >= 1 || countOwned(OwnedKind::Ped) >= 4)) return "ya hay un perseguidor o demasiados enemigos";
    if (effect == "spawn_obstacles" && countOwned(OwnedKind::Object) > 0) return "ya hay obstaculos activos";
    if (ownedSpawns.size() + (effect == "spawn_pursuer" ? 2 : amount) > 8) return "limite de apariciones activas";
    int milliseconds = command.value("ms", 0);
    int maximum = effect == "spawn_pursuer" ? 60000 : effect == "spawn_enemies" ? 45000 : 30000;
    if (milliseconds < 1000 || milliseconds > maximum) return "duracion invalida";
    ULONGLONG now = GetTickCount64();
    auto last = lastEffect.find(effect);
    ULONGLONG cooldown = effect == "spawn_obstacles" ? 30000 : 45000;
    if (last != lastEffect.end() && now - last->second < cooldown) return "en enfriamiento";
    pendingSpawn = {true,command.value("id", ""),effect,arg,epoch,milliseconds,pedRef(player()),std::min(now + 5000,command["expiresMs"].get<ULONGLONG>())};
    for (int model : spawnModels(effect, arg)) reinterpret_cast<void (__cdecl *)(int,int)>(0x4087E0)(model, 0);
    lastEffect[effect] = now;
    return "pending";
}
static bool validSpawnPoint(Point target, Point origin, float radius) {
    if (!groundPoint(target, origin.z)) return false;
    target.z += radius + 0.15f;
    Point eye = origin; eye.z += 1;
    return emptySphere(target,radius) && lineClear(eye,target);
}
static std::string createPending() {
    const auto task = pendingSpawn;
    void* ped = player(); void* car = currentVehicle();
    void* anchor = car ? car : ped; Point origin = position(anchor), direction = forward(anchor);
    if (!std::isfinite(origin.x) || !std::isfinite(origin.y) || !std::isfinite(origin.z)) return "posicion no disponible";
    float length = std::sqrt(direction.x*direction.x + direction.y*direction.y); if (length < .1f) return "direccion no disponible";
    direction.x /= length; direction.y /= length;
    std::vector<Point> points;
    float heading = 0;
    if (task.effect == "spawn_pursuer") {
        Point behind{origin.x-direction.x*24,origin.y-direction.y*24,origin.z};
        auto node = scm(0x03D3,behind.x,behind.y,behind.z,ScriptOutput{0},ScriptOutput{1},ScriptOutput{2},ScriptOutput{3});
        Point point{node.number(0),node.number(1),node.number(2)}; heading = node.number(3);
        float distance = distance2(point,origin);
        if (!std::isfinite(distance) || !std::isfinite(heading) || distance < 196 || distance > 3025 || !groundPoint(point,origin.z)) return "no hay carretera cercana para el perseguidor";
        if (!validSpawnPoint(point,origin,1.5f)) return "la carretera no tiene espacio libre";
        float angle = heading * 0.0174532925f;
        for (float offset : {-1.6f,1.6f}) {
            Point end{point.x - std::sin(angle)*offset,point.y + std::cos(angle)*offset,point.z};
            if (!validSpawnPoint(end,origin,1.5f)) return "la carretera no tiene espacio libre";
        }
        points.push_back(point);
    } else {
        int count = task.effect == "spawn_enemies" ? (task.arg.empty() ? 2 : task.arg[0]-'0') : 3;
        for (int i = 0; i < count; ++i) {
            Point target;
            if (task.effect == "spawn_enemies") {
                float angle = (i - (count-1)*0.5f)*0.8f;
                target = {origin.x + (direction.x*std::cos(angle)-direction.y*std::sin(angle))*12,origin.y + (direction.y*std::cos(angle)+direction.x*std::sin(angle))*12,origin.z};
            } else {
                float speed = car ? std::sqrt(field<float>(car,0x44)*field<float>(car,0x44)+field<float>(car,0x48)*field<float>(car,0x48)) : 0;
                float ahead = car ? std::clamp(20 + speed*55,20.0f,45.0f) : 12;
                float side = (i-1)*3.0f;
                target = {origin.x + direction.x*ahead - direction.y*side,origin.y + direction.y*ahead + direction.x*side,origin.z};
            }
            float radius = task.effect == "spawn_enemies" ? .75f : (task.arg == "barriers" ? 1.2f : .55f);
            if (!groundPoint(target,origin.z) || !validSpawnPoint(target,origin,radius)) return "no hay espacio libre en el suelo cercano";
            points.push_back(target);
        }
    }
    size_t first = ownedSpawns.size();
    ULONGLONG until = GetTickCount64() + task.duration;
    auto rollback = [&]() { for (size_t n = ownedSpawns.size(); n > first; --n) removeSpawn(ownedSpawns[n-1]); ownedSpawns.resize(first); };
    if (task.effect == "spawn_enemies") {
        for (Point point : points) {
            int handle = scm(0x009A,7,102,point.x,point.y,point.z+1.0f,ScriptOutput{0}).values[0];
            OwnedSpawn entry{OwnedKind::Ped,handle,102,until};
            if (handle < 0 || !ownedEntity(entry)) { rollback(); return "no se pudo crear el enemigo"; }
            ownedSpawns.push_back(entry);
            scm(0x01B2,handle,22,60); scm(0x02E2,handle,20); scm(0x077A,handle,4,0);
            scm(0x05E2,handle,pedRef(ped));
        }
    } else if (task.effect == "spawn_pursuer") {
        Point point = points[0];
        int handle = scm(0x00A5,560,point.x,point.y,point.z,ScriptOutput{0}).values[0];
        OwnedSpawn entry{OwnedKind::Car,handle,560,until};
        if (handle < 0 || !ownedEntity(entry)) return "no se pudo crear el perseguidor";
        ownedSpawns.push_back(entry); scm(0x0175,handle,heading);
        int driver = scm(0x0129,handle,7,102,ScriptOutput{0}).values[0];
        OwnedSpawn person{OwnedKind::Ped,driver,102,until,true};
        if (driver < 0 || !ownedEntity(person)) { rollback(); return "no se pudo crear el conductor"; }
        ownedSpawns.push_back(person);
        scm(0x077A,driver,4,0); scm(0x00AD,handle,20.0f); scm(0x00AE,handle,2); scm(0x00AF,handle,2);
    } else {
        int model = task.arg == "barriers" ? 1228 : 1238;
        for (Point point : points) {
            int handle = scm(0x0107,model,point.x,point.y,point.z,ScriptOutput{0}).values[0];
            OwnedSpawn entry{OwnedKind::Object,handle,model,until};
            if (handle < 0 || !ownedEntity(entry)) { rollback(); return "no se pudo crear el obstaculo"; }
            ownedSpawns.push_back(entry);
        }
    }
    return "ok";
}
static void updateSpawns() {
    unsigned clock = *reinterpret_cast<unsigned*>(0xB7CB84);
    if (clock < lastWorldTime) { ownedSpawns.clear(); cancelPending("cambio de partida"); }
    lastWorldTime = clock;
    bool enabled = effectsAllowed() && exteriorFreeRoam() && !sceneCleanupRequested;
    sceneCleanupRequested = false;
    Point origin = player() ? position(player()) : Point{};
    // Remove NPCs before their vehicles so no driver retains a deleted car.
    for (OwnedKind kind : {OwnedKind::Ped,OwnedKind::Object,OwnedKind::Car}) {
        for (auto it = ownedSpawns.begin(); it != ownedSpawns.end();) {
            if (it->kind != kind) { ++it; continue; }
            void* entity = ownedEntity(*it);
            if (!entity || !enabled || GetTickCount64() >= it->until || distance2(position(entity),origin) > 14400) {
                if (entity) removeSpawn(*it);
                it = ownedSpawns.erase(it);
            } else ++it;
        }
    }
    if (!pendingSpawn.active) return;
    if (!enabled || pendingSpawn.epoch != epoch || pedRef(player()) != pendingSpawn.ped) { cancelPending("aparicion cancelada por pausa o cambio de partida"); return; }
    if (GetTickCount64() >= pendingSpawn.deadline) { cancelPending("los modelos no cargaron a tiempo"); return; }
    for (int model : spawnModels(pendingSpawn.effect,pendingSpawn.arg)) {
        if (!scm(0x0248,model).condition) { reinterpret_cast<void (__cdecl *)(int,int)>(0x4087E0)(model,0); return; }
    }
    std::string result = createPending();
    spawnAck(pendingSpawn.id,result); pendingSpawn.active = false;
}
