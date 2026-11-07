# Marvel vs. Capcom · Cloud90

Game Link 1.6.0 connects Cloud90 game ID `mvsc` to **MAME 0.289 for Windows**, ROM set **`mvscu` (USA 980123)**. This is a CPS-2 game. Use your own `mvscu.zip`, parent `mvsc.zip` and any dependencies reported by `mame mvscu -verifyroms`, in MAME's `roms` folder. No ROM, BIOS or game files are distributed.

## Setup

1. Close MAME, install the signed Game Link prerelease and select Marvel vs. Capcom.
2. Select the MAME folder. Game Link checks the exact emulator version and audits the USA ROM before launch.
3. Request approval for this game in Cloud90, use a MAX account and generate its own `mvsc` token. A KOF token does not work here.
4. Install the plugin and launch from Game Link. Insert credits and start P1 against CPU. USA defaults require two coins per credit.
5. Configure rules in `/juegos/mvsc`; sample rules, event processing and troll mode start disabled. Test an effect during an actual fight.

F9 pauses effects while this MAME window is focused; the app also has a pause button. Shift+F9 keeps MAME's original behavior. Effects are unavailable in demos, selection, menus, results, two-player games and netplay. Fightcade is unsupported.

## Effects

| Effect | Behavior / maximum duration |
|---|---|
| Heal / damage active fighter | +36 / −22 HP out of 144; damage leaves at least 1 HP |
| Heal CPU active fighter | +36 HP |
| Add / empty player super | Team meter, +1 bar up to 3 / clear bars and fraction |
| Add CPU super | +1 team bar up to 3 |
| Add / remove clock | +20 capped at 99 / −20 with minimum 10 game clock units |
| Freeze clock | 10 seconds default, 20 maximum |
| Invert controls | Left/right only, 5 seconds default, 10 maximum |
| No punches / no kicks / no jump | 4 seconds default, 8 maximum |
| Auto jump / tunnel vision | 5 seconds default, 10 maximum |
| Blackout | 2 seconds default, 3 maximum |
| Slow motion | Both teams at 0.5 speed; 5 seconds default, 10 maximum |
| Shield | Blocks and cancels trolls; 15 seconds default, 60 maximum |

These are 18 native capabilities. Roulette is resolved by the server using the available, permitted effects. Trolls require opt-in and have a three-second grace period after entering a fight. Durations use simulated game time. Damage cannot directly KO; normal CPU attacks still can. Health targets the currently active fighter even after a real tag. Forced tags, assists, spawning fighters and instant KO are deferred.

## Transport and cleanup

The shared installer adds `mvsc.lua` alongside the existing plugin. Legacy KOF/Metal Slug use their original path; the MVSC environment selects the separate adapter. Commands use a per-launch session, combat epoch, UTC deadline, JSON inbox, bounded deduplication and a four-second heartbeat. ACKs return through the existing Game Link connection. Loss of connection, pause, end of fight, reset or loading a state clears temporary input/visual/speed effects and invalidates old commands. Instant health/meter changes are not rolled back. Slow motion restores the prior throttle rate.

## Memory facts (USA only)

| Address | Meaning |
|---|---|
| `FF4100`, `FF4101` | Started game / P1 against CPU flags, both must equal 1 |
| `FF4104`, `FF4106`, `FF4000` | Match, fighting phase and fight state; require 8 / 4 / 8 |
| `FF4008` | BCD round clock |
| `FF40C8`, `FF40D0` | Active player / CPU record pointers |
| `FF3000`, `FF3800` | Player team records |
| `FF3400`, `FF3C00` | CPU team records |
| Record + `270`, `27A` | HP and recoverable HP ceiling (u16) |
| Record + `274`, `272` | Super bars (u8) and fraction (u16) |
| Record + `281` | Active flag (0 active, 1 reserve) |
| `FF4113` | Stage byte, included in context invalidation |

The implementation was independently written using observed MAME behavior and address facts cross-checked against [FBNeo training mode](https://github.com/peon2/fbneo-training-mode/blob/master/games/mvsc/mvsc.lua) and [cps2recomp](https://github.com/tmcleroy/cps2recomp). No source from those projects is incorporated. Native adapter APIs follow [MAME Lua documentation](https://docs.mamedev.org/luascript/index.html).

## Validation

Run `npm test` and `npm run test:protocol`. With the separately owned ROMs and MAME installed:

```powershell
powershell -ExecutionPolicy Bypass -File tests/mame/run-mvsc-e2e.ps1
powershell -ExecutionPolicy Bypass -File tests/mame/run-mvsc-app-e2e.ps1
powershell -ExecutionPolicy Bypass -File tests/mame/run-kof-e2e.ps1
powershell -ExecutionPolicy Bypass -File tests/mame/run-mslug3-e2e.ps1
```

Validated on native MAME 0.289: all 18 effects, ACKs, real character tag and active HP target, state-load invalidation, pause/resume, isolated JSON inbox and the complete WebSocket → C# host → actual MAME → ACK chain. KOF and Metal Slug native regression tests pass. Unit/protocol tests cover expiration, deduplication, policies and stale attempts. Native tests use isolated configuration/save directories.

Owner production validation remains: real live events and overlays, physical F9/controller interaction, and a broader character/stage matrix. Do not describe those as already tested.

Artwork selected by the owner: [IGN image](https://sm.ign.com/t/ign_mear/video/m/marvel-vs-/marvel-vs-capcom-fighting-collection-arcade-classics-officia_dd4k.1280.png). The same PNG is embedded in Game Link and served by the frontend.
