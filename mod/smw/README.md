# Super Mario World · Cloud90 bridge 1

Game Link 1.5.0 adds `smw` to the existing library and WebSocket protocol.
The module is loaded into memory through the runtime's opt-in Lua TCP interface.
No runtime, ROM, Python dependency, executable patch or game-side installed file
is distributed. The token stays in Game Link's existing DPAPI settings.

## Compatible installation

- Windows, `SuperMarioWorldSNESRecomp.exe` build 0.15.6.
- Executable SHA-256: `fb1540efa35cb92f899a34fc7422fe1d4f1d6dea116ceed1f409c0f7c2cb7586`.
- Stock, unheadered USA ROM SHA-256: `0838e531fe22c077528febe14cb3ff7c492f1f5fa8de354192bdff7137c27f5b`.
- Select the ROM in the ordinary runtime once to populate `rom.cfg`.
- Disable Native Co-op and Captain Falcon in the runtime's Mods screen.
- One-player offline games only. Game Link bypasses the launcher using the user's
  selected ROM, preserves configuration/saves and removes inherited netplay settings.
- The child alone receives `SNESRECOMP_LUA_PORT`, on a free loopback port. Game Link
  checks the listening PID before uploading its embedded script.

Open Game Link, choose Super Mario World, select the folder, paste its own token,
then open the game. Choose 1 Player and enter a level. There is no Python setup.

## Effects

16 game effects plus the existing backend roulette:

| ID | Behavior / limit |
| --- | --- |
| `mushroom`, `fire_flower`, `cape`, `small` | Instant power changes; small is a lethal troll effect |
| `invincible` | Damage protection, 30 s maximum; does not stop lava, pits or timeout |
| `fire_stream` | Native extra projectile pool, 30/s, 15 s maximum; leaves fire power |
| `spawn_enemy`, `spawn_wave` | Copy 1 or 3 supported on-screen sprites with enough empty ordinary slots |
| `life_add` | +1 life, maximum 99 displayed |
| `time_add`, `time_subtract` | ±30 clock units, only with active timer; subtract retains at least 10 |
| `invert_controls` | Horizontal directions swapped, maximum 10 s |
| `auto_jump` | Periodic jump input, maximum 10 s |
| `super_speed` | Ground movement acceleration, capped at ±48 native velocity units, maximum 10 s |
| `high_jump` | Initial upward velocity boost, maximum 10 s |
| `shield` | Rejects/cancels troll effects, maximum 60 s |

Jump, speed, fire and enemy effects require supported land conditions, no Yoshi,
balloon, cape flight or special boss mode. Enemies are limited to already visible
Koopas and Goombas with initialized collision/graphics data, at least 64 pixels
from Mario. The wave shares their proven spawn location; sprites may overlap.
Spawned enemies and instant powers/lives/time are not rolled back by panic.

Timed effects use active simulation frames (60 frames per configured second),
extend only up to their individual maximum, and clear on pause, transitions,
death, connection loss and the 120-frame heartbeat watchdog. Re-enabling no-death
cancels active troll effects. Existing native damage protection is preserved.

F9 is intercepted only in the launched game's foreground window and toggles
Game Link pause, consuming the original load-slot-9 shortcut. Shift+F9 remains
save-slot-9. Other savestate loads while connected are unsupported: pause effects
and restart the game to reconnect. Guest frame discontinuities invalidate most
loads but cannot identify every possible same-frame saved-state restore.

## Protocol

Backend: `/api/game-link/ws`, version `cloud90-game-link-1.5.0`, game `smw`.
Session, current attempt epoch, current capabilities, no-death policy and original
per-command safe flag are checked locally. Remote text never becomes executable
Lua: the client sends only its embedded script and escaped fixed-function data.
Each command ID is applied once, with a 512-result deduplication window and a
10-second delivery deadline. Only one command is sent per poll to stay within
the runtime's 1024-byte per-value result cap. ACKs mean applied/rejected by Lua,
not merely delivered to TCP. Initial rules and troll mode are disabled.

The sandbox intentionally removes `pcall`/`xpcall` and file/network APIs. The
module uses return values for expected rejections. Unexpected Lua/transport
errors pause effects and require the owner to resume after reconnection.

## Validation

`npm test` includes the Lua module with fake WRAM and the actual sandbox's missing
protected-call APIs. `npm run test:protocol` checks SMW WebSocket ACKs, epochs and
policy revocation alongside existing games.

Native validation against an isolated local runtime copy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/smw-live.ps1 `
  -GameDir C:\path\to\isolated-runtime `
  -EnterLevelLua C:\path\to\SuperMarioWorldRecomp\tools\lua\enter_level.lua
```

Verified on 2026-11-06: real PID ownership, ordinary-input navigation into a
level, embedded script in the restricted VM, status/capabilities, C# inbox ACK,
fire power, exactly-once life increment, native fire stream and panic cleanup,
no-death rejection, stale attempt rejection, stable context across normal frames
and watchdog cleanup. Tests used separate saves and did not modify the owner's
runtime installation. Full effect/level coverage, physical F9 interaction,
long-running streams and real production gifts remain owner validation.

Runtime reference: https://github.com/mstan/SuperMarioWorldRecomp.
Our bridge is independently implemented; no runtime/helper source is packaged.
The card screenshot is cropped from the upstream snesrecomp documentation image
`docs/assets/games/super-mario-world-wide.png`, also used by the desktop library.
