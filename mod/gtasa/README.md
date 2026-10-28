# GTA San Andreas / Cloud90

Classic Windows 32-bit GTA SA 1.0 US, single player. The installer and native
bridge both require the exact executable SHA-256:
`a559aa772fd136379155efa71f00c47aad34bbfeae6196b0fe1047d0645cbd26`.
Other executables, SA-MP and MoonLoader installations are refused. No game
executables, game assets or saved games are patched by this module.

## Use

1. Close GTA. Select its folder (the folder containing `gta_sa.exe`) in Game Link.
2. Use **Cómo conectar y jugar** for the guide included in the application.
3. Generate the GTA token at https://cloud90.dev/juegos/gtasa after GTA is enabled
   on Cloud90, then use **Pegar token** in Game Link.
4. Choose **Abrir juego**, complete any startup dialogs and load a single-player
   game. Wait for **CJ listo: partida activa para recibir efectos**.
5. Configure and enable the desired rules in Cloud90. Keep Game Link open.

GTA's backend catalog and download instructions are prepared in the companion
backend/frontend branches. They must be published alongside Game Link 1.4.0;
this local build alone does not enable GTA on the production website.

## Effects

| ID | Behavior |
|---|---|
| `heal` | Set CJ's health to his current maximum |
| `armour` | Refill armour |
| `money_add` | Add $1,000, capped at $999,999,999 |
| `wanted_clear` | Clear the wanted level |
| `wanted_set` | Set 1–6 stars; default is 3 |
| `repair_vehicle` | Repair CJ's current automobile; refuse on foot, bikes and destroyed vehicles |
| `drunk_controls` | Sideways input drift on foot, up to 15 s |
| `invert_controls` | Reverse walking axes, up to 10 s; replaces drunken input |
| `no_shoot` | Block firing on foot in all four controller modes, up to 8 s |
| `remove_weapon` | Remove the equipped weapon, on foot; instant, not restored |
| `remove_ammo` | Empty the equipped firearm, on foot; instant, not restored |
| `launch` | Vertical impulse from solid ground; opt-in, 60 s native minimum cooldown |
| `invert_steering` | Reverse the driver's steering, up to 10 s |
| `stuck_accelerator` | Force acceleration with brakes available, up to 3 s |
| `burst_tyres` | Burst the current automobile's four tyres; repair reverses this |
| `ice_floor` | Reduce traction for this driver's automobile, up to 8 s |
| `spin_vehicle` | Angular impulse while driving; opt-in, 60 s native minimum cooldown |
| `roulette_troll` | Server-side virtual effect: only eligible troll effects in the configured pool |
| `engine_off` | Disable engine torque for the driver's car, up to 5 s, brakes retained; replaces stuck accelerator |
| `no_jump` | Block jumping on foot, up to 10 s |
| `freeze_feet` | Block walking and jumping on foot, up to 3 s, shooting/aiming retained |
| `dense_fog` | Temporary rendering fog, up to 15 s; no forced-weather/save changes |
| `camera_shake` | Temporary camera shake, up to 6 s |
| `spawn_enemies` | 1–3 armed Ballas (default 2), up to 45 s; native minimum wait 45 s |
| `spawn_pursuer` | One Sultan with a hostile driver on a nearby road, up to 60 s; native minimum wait 45 s |
| `spawn_obstacles` | Three traffic cones or roadwork barriers, up to 30 s; native minimum wait 30 s |

## Bridge 3 appearances

Appearances require outdoor free roam (no mission, cutscene, interior, pause,
death or two-player mode), on foot or as the driver of a regular automobile.
The native module requests only fixed game models, asynchronously with default
streaming flags. It never blocks the main thread waiting for streaming and does
not add global mission-required model flags. The command is acknowledged only
after creating the entities, or refused/cancelled; loading has a five-second
deadline bounded by the original command expiry.

Placement checks ground height, water, line of sight to buildings/objects and
empty collision spheres. Pursuers additionally require a nearby car path node;
obstacles are placed ahead of the player, farther ahead at higher driving speeds.
No spawn runs clear-area commands. The creation opcodes' own entity placement is
preceded by those checks. Maximum simultaneous ownership is eight entities:
up to four NPCs including a pursuer's driver, one pursuer car and three objects.

Every owned entity has a generation-checked pool handle, model ID and deadline.
Cleanup removes NPCs before cars, only touches matching owned entities, and runs
on expiry, loss of gameplay/connection, pause, entering a mission/interior or
moving over 120 m away. When the game's scene processing is paused, cleanup can
only complete when scene processing resumes. A pursuer vehicle occupied by CJ
is released to normal game cleanup instead of being deleted around the player.
The world-reset hook drops old-world handles before pool generations are reused.

Spawns use a private 0xE0-byte script context and fixed SA opcodes with local
outputs. The global script parameter scratch array is saved/restored per call;
no incoming message can provide an opcode, model ID or script payload. Parameter
contracts were checked against plugin-sdk and Sanny Builder's SA command library.
Enemies get finite pistol ammunition and low accuracy; pursuit uses the game's
driver mission, rather than directly changing arbitrary AI vehicles.

Bridge 3 also hooks weather and camera updates. Fog restores the previous
rendering value before normal weather updates; camera shake preserves stronger
native shakes and leaves no persistent camera shake settings. Engine-off is
scoped to the current car's physics call and does not disable the brakes.

Bridge version 2 reports its actual capabilities. A bridge-1 module retains its
baseline six effects. Unknown capabilities are not forwarded to Cloud90.
Temporary effects use bounded wall-clock deadlines and a ped pool handle; vehicle
effects also retain a generation-checked vehicle handle. Changing to a vehicle
while using foot effects, or leaving/replacing a vehicle, cancels those effects.
Pause, F9, stale heartbeat, malformed inbox and losing playable state clear them.

`CPad::UpdatePads` is hooked after input refresh. It does not change controls
settings or persistent disable flags. Ice uses a stack-local handling copy only
during the current automobile's `ProcessControl`; shared handling data and other
cars are unchanged. Weapon removal, ammunition removal, tyre damage and physical
impulses are instant gameplay actions and are not undone by pause.

`launch` and `spin_vehicle` require both the current inbox policy and the command
to allow destructive effects with no-death disabled. The backend and native
module independently enforce their 60-second minimum cooldowns. Other native
troll cooldown minima are 10 s for temporary effects and 15 s for inventory/tyres.
Mode without death blocks those two impulses; it does not make normal gameplay
or the other control/handling modifiers harmless.

Only live single-player gameplay accepts effects. Menus, pause, cutscenes,
death/arrest and local two-player mode are excluded. F9 toggles local pause;
Game Link's pause/resume controls also apply. Each launch has a random session;
each gameplay transition changes its epoch. Commands require fresh heartbeats,
the current epoch and an expiry no more than 10 seconds away. Duplicate command
IDs are never applied twice during a launch.

The native module hooks `CGame::Process` using MinHook and applies effects on
the game's main thread. Address/offset references are from
DK22Pac/plugin-sdk commit `15f15b60bbf74c106e1b496ff92c98764abf4605`.
The SDK's code is not linked into the module.

## Files and removal

The installer adds `Cloud90.GTASA.asi`, `dinput8.dll`, third-party notices and
`cloud90-gtasa-install.json`. It refuses conflicting existing files and records
hashes for the files it owns. Removal preserves unrelated files, modified files
and a shared ASI loader. No network requests or tokens originate in the native
module; tokens remain in Game Link's DPAPI-protected settings.

## Build

Run `mod/gtasa/build.ps1` with the x86 Visual C++ toolchain (located using
Visual Studio Installer's vswhere). `-VcVars` selects
another Visual Studio `vcvars32.bat` installation. Output is
`mod/gtasa/dist/Cloud90.GTASA.asi`.

The pinned Ultimate ASI Loader Win32 `loader/dinput8.dll` is verified and copied
to the module's `dist` folder when building. Its SHA-256 is
`63b465dbc1bf9c5ab4e33c3ccad099d9edb702f2c51587ac8d4f196497396437`.
Source: https://github.com/ThirteenAG/Ultimate-ASI-Loader.
MinHook and nlohmann/json sources and licenses are in `vendor`.

The native module and desktop client compile locally. Real-game verification
remains with the owner; compilation is not confirmation that effects work.
