# Metal Slug X · Cloud90 Game Link

Game Link 1.7.0 connects Cloud90 game/token **`mslugx`** to **MAME 0.289 for Windows**, ROM set `mslugx`. Supply your own `mslugx.zip`, `neogeo.zip` and dependencies reported by `mame mslugx -verifyroms`, in `roms`. No ROM or BIOS is distributed.

## Setup

1. Close MAME and older Game Link instances; use the complete signed 1.7.0-rc.1 client.
2. In Cloud90 use a MAX account, request this game's approval and generate its own token. Metal Slug 3's token is independent.
3. Select Metal Slug X in Game Link, select the folder with `mame.exe`, install the plugin and paste the token. The client validates the emulator version and audits the ROM before launching.
4. Insert credit, press P1 Start and enter a single-player mission. Effects require a live soldier on foot in normal form; tutorial/demo, selection, death, continue, P2, vehicles and transformations are unsupported.
5. Configure `/juegos/mslugx`, test an effect, then enable only the intended event rules. Sample rules, processing and troll mode start disabled.

F9 pauses effects while the launched MAME window has focus; Shift+F9 keeps MAME's function. The app also has a pause button. Offline only, without Fightcade/netplay.

## 23 effects and roulette

| Effect | Behavior / default and maximum duration |
|---|---|
| Add/remove reserve life | ±1 reserve, range 0–9; does not kill/resurrect the active soldier |
| Add/empty grenades | +10 capped at 99 / set to zero |
| Give weapon / restore pistol | Nine verified weapons; subtype, ammo and form request come from audited ROM pickup tables |
| Add ammo | +100 capped at 999; finite ammunition only |
| Hit invulnerability | 8 s / 15 s; not a promise of protection from falls, clock or special hazards |
| Add/remove clock | ±20 game clock units, cap 99; removal rejects at 10 or below and never raises a low clock |
| Freeze clock | 10 s / 20 s |
| Invert left/right | 5 s / 10 s |
| No jump / no shooting / no grenade button | 4/8 s, 3/6 s, 5/10 s respectively |
| Paralyze | 1 s / 2 s; gameplay controls only |
| Auto jump / auto fire | 5 s / 10 s; normal ammo consumption |
| Tunnel vision / blackout | 5/10 s, 1/2 s respectively |
| Slow motion / turbo | Whole emulator at 0.5× or 1.25×; 5/10 s or 4/8 s |
| Shield | 15 s / 60 s; blocks and cancels trolls |

Weapons: Heavy Machine Gun, Shotgun, Rocket Launcher, Flame Shot, Laser, Enemy Chaser, Iron Lizard, Drop Shot and Super Grenade. Weapon IDs differ from Metal Slug 3 (HMG is 5).

Roulette is server-side and uses the available, permitted pool. Remove-life and paralyze are excluded from the default pool; explicit selection can include them. Trolls require opt-in and a three-second grace period follows a new active context. Control sabotage can cause indirect deaths.

Temporary effects clear on pause, heartbeat loss, disconnect, death, zone/mission change, state load, reset and exit. Owned input overrides are released; speed restores its previous value. Hit protection stops renewing the native respawn counter and removes the owned remainder, preserving pre-existing protection while applicable. Applied lives, inventory and weapon changes are not rolled back.

Enemy cloning and vehicle effects are deferred: MS3's allocator/task table is not valid evidence for MSX and is not reused here.

## Transport and memory facts

`session.lua` shares only MAME lifecycle and bounded JSON/ACK transport with MVSC. Each adapter owns its validator, memory and effects. The new host uses session, combat/attempt epoch, UTC expiry, four-second heartbeat, bounded deduplication and dynamic capabilities. Legacy KOF/MS3 retain their transport. The shared plugin installer requires MAME closed.

| Address | Meaning verified in MAME 0.289 / `mslugx` |
|---|---|
| `100088` | Mission game state (2), also seen during tutorial: insufficient alone |
| `10FDB6` / `10FDB7` | BIOS P1/P2 active flags; require 1 / 0 |
| `101274` | P1 status object's soldier link, require `1014C4` |
| `10128C` | P1 status, 1 active, 2 continue |
| `10151C` | Soldier state, 1 active; other states include death/respawn |
| `101288` | Reserve lives, 0 is valid last active life |
| `1013DE` | **Binary** clock byte, not MS3's BCD clock |
| `10C718` | Soldier/action/vehicle mode; 0/1 on foot, 1 observed during normal grenade throw; 2 vehicle |
| `101557` | Form, allow only 0/1 normal |
| `101548` | u16 native respawn protection counter, observed countdown at 30 ticks/s |
| `10C71A`, `10C71C`, `10C71E` | Shot counter, subtype and weapon ID |
| `10C720`, `10C724` | Grenade byte and u16 ammo; `FFFF` infinite |
| `101558`, `101559` | Requested weapon form and associated transition byte |
| `10C2F4`, `10C2F6`, `10C2FA` | Mission, area and section, invalidate context on change |
| ROM `C08A8`, `C08B8`, `C0918` | Pickup subtype bytes, ammo words and form request bytes |

Implementation is independently written. Address facts were cross-checked against [Metal Slug X recomp's documented profile](https://github.com/AlmazProud/metal-slug-x-recomp/blob/main/docs/PHASE7B_DEBUG.md) and native MAME observations; no project source or generated ROM code is incorporated. The adapter uses [MAME Lua APIs](https://docs.mamedev.org/luascript/index.html).

## Validation

```powershell
npm test
npm run test:protocol -- -JsonDll <pinned-Newtonsoft.Json.dll>
powershell -ExecutionPolicy Bypass -File tests/mame/run-mslugx-e2e.ps1
powershell -ExecutionPolicy Bypass -File tests/mame/run-mslugx-app-e2e.ps1
```

Native MAME tests use isolated cfg/NVRAM/save directories and the owner's separately installed ROMs. Verified: real mission entry; all 23 modifiers; nine weapons firing and consuming ammo, screenshots; normal grenade consumption; JSON ACK; duplicate and stale-command rejection; speed/shield/panic cleanup; state-load and MAME pause/resume; actual enemy death and normal respawn invalidating context. Full WebSocket → C# host → MAME → ACK chain passed. Native regressions passed for MVSC, KOF and MS3; MVSC's complete host chain also passed after shared transport extraction.

Unit/protocol tests cover inventory/clock limits, invalid commands, capabilities, dedupe, epoch changes, expiration, menus, P2, forms/vehicle exclusion, controls, owned protection and speed restoration. Frontend card/configuration are reviewed at desktop and 390 px mobile.

Owner production validation remains: real gifts/live events and overlays, physical F9/controller behavior, and a broader mission/character/vehicle-transition matrix. Those are not represented as already tested.

Artwork is the owner's supplied 620×300 PNG, used unchanged in the web card and embedded Game Link library/banner.
