# Cuphead bridge (Steam / Windows x64)

Supports the Steam managed assembly SHA-256
`d0eafb33fedb0eedcd5eda6d2423cc7b9fcb2e60c40e92814a0ff785e2d9cbed`
(build 20241530). Other builds fail installation rather than guessing offsets.
Uses BepInEx 5.4.23.6 x64 and Newtonsoft.Json 13.0.3 net20, with hashes
checked by the build. No game assemblies are distributed.

Close Cuphead before installation, update or uninstall. Select the Steam game
folder, paste the Cuphead token, then choose **Abrir Cuphead** in Game Link.
Steam launches your own copy. The DLC is optional and must be owned/enabled
normally. One token and tab cover both the base game and DLC.

Solo gameplay only. Effects are suspended in menus, pause, death, transitions,
co-op, the King's challenges, graveyard and boss hubs. Ground-only actions
refuse airplane levels. **F9** clears active effects and toggles local panic;
Game Link's pause and dashboard pause also clear effects. Lost heartbeat clears
modifiers within three seconds; reconnect never replays expired commands.
Movement speed changes are scoped to player movement, not global game time.
The plugin enables background updates so the local bridge can report state
while Game Link is focused; it restores the prior setting when unloaded.

Health is clamped to the equipped character's current maximum.
Healing from 8 to 9 HP is refused because the native setter
would unlock a Steam achievement; native gameplay can still reach 9 HP normally.
Damage respects
native protection and the no-death flag. There are no inventory, unlock, save,
shop or progress commands. The shield blocks Cloud90 troll commands, not enemies.
A command is acknowledged only after application or with a specific refusal.

## Additional troll effects

The catalog now includes 23 real effects plus roulette. The twelve additions are:

| Effect | Scope / behavior |
|---|---|
| Ice floor | Ground, inertia and gradual braking, up to 8 s |
| Wind push | Ground, left/right movement push, up to 5 s |
| Explosion | Procedural visual burst and one normal 1-HP damage attempt |
| Burn | Procedural flames, one damage attempt immediately and another at 3 s; up to 6 s |
| Drain super | Clears current cards; does not alter loadout |
| Petrify | Native airplane stone state, up to 3 s; inputs can break it earlier |
| Auto jump | Grounded single jumps spaced at least 0.9 s apart, up to 8 s |
| Weak shots | Player-one basic damage at 50%; EX/supers excluded, up to 10 s |
| Camera zoom | Visual 15% closer orthographic view; restores native size, up to 8 s |
| Enemy reinforcements | Only compatible loaded ground spawners in Platforming_Level_1_1 / 1_2; 1–2 enemies, at most 3 active, removed after 15 gameplay seconds or F9 |
| Restart level | Native retry after 3-second warning; default cooldown 180 s, hard minimum 120 s |
| Kill | Native player death after 3-second warning; default cooldown 600 s, hard minimum 300 s |

All appear in **Probar un efecto** and the rule selector. The bridge reports
contextual capabilities; unsupported effects stay visible with a reason and are
refused by both backend and plugin. Roulette filters the current capabilities.
Kill/restart require **Permitir muerte y reinicio**, **Modo sin morir** off, and
saved settings. Both permissions are checked again immediately before execution.
F9, lost heartbeat, shield, changed permissions or attempt cancel pending actions.
The 3-second warning runs only during active gameplay, and command expiry still
runs while the game is paused. Kill/retry use normal game behavior, including its
normal death statistics. They are excluded from the default roulette pool.
No-death protects direct Cloud90 damage; enemies and ordinary level hazards keep
their normal behavior. The extra enemies use only loaded prefabs, never bosses.

These twelve additions compile and have automated integration/policy coverage;
**they have not yet been visually verified in the real game**. The owner chose to
perform their gameplay validation after deployment. See VALIDATION.md before release.

## Build the complete client

Use your legally installed game as compile references and extract the pinned
BepInEx archive to a tools directory. Obtain Newtonsoft.Json 13.0.3 from NuGet;
use `lib/net20/Newtonsoft.Json.dll` (the net45 binary cannot run on this game).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build.ps1 `
  -CupheadGameDir 'D:\SteamLibrary\steamapps\common\Cuphead' `
  -BepInExDir 'C:\tools\bepinex' `
  -BepInExArchive 'C:\tools\BepInEx_win_x64_5.4.23.6.zip' `
  -JsonDll 'C:\tools\json\lib\net20\Newtonsoft.Json.dll'
```

Without the Cuphead build parameters, the app still compiles for the other
games but intentionally refuses to install Cuphead's missing package. Such a
build must not be published as the complete Cuphead release.

The plugin uses streaming JSON because this Unity Mono runtime lacks System.Data
and its JsonUtility omits these nested DTO arrays. Commands have bounded input,
IDs, epoch, session, heartbeat and expiration, and execute on Unity's main thread.
Cuphead state and server commands carry the attempt epoch end to end, so delayed packets cannot be relabeled for a newer attempt. The backend clears queued effects and old roulette results when the attempt changes. The host verifies the reporting process path before trusting it as the game.
The local IPC files are not a security boundary against other local processes.

Installer ownership is hash-based: conflicting existing files are not replaced;
uninstall validates every owned file before removal and preserves unrelated files.
If other BepInEx plugins or patchers exist, uninstall keeps the shared loader.

## Redistribution notices

The complete client includes the pinned upstream BepInEx release without altering
its assemblies, plus the Cloud90 plugin and Newtonsoft.Json. Notices from
`licenses/` are embedded and installed alongside the plugin. Upstream sources:

- https://github.com/BepInEx/BepInEx/tree/v5.4.23.6
- https://github.com/BepInEx/HarmonyX
- https://github.com/pardeike/Harmony
- https://github.com/jbevain/cecil
- https://github.com/MonoMod/MonoMod
- https://github.com/NeighTools/UnityDoorstop/tree/v4.6.0
- https://github.com/JamesNK/Newtonsoft.Json/tree/13.0.3

## Validation status

See [the validation matrix](VALIDATION.md) for real-game confirmations, automated
checks and remaining release gates. A positive protocol ACK is recorded separately
from a visual confirmation by the tester.
