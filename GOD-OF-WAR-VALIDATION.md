# God of War — experimental branch, NOT a production release

2026-11-10. Branch: feat/god-of-war-cloud90. Client protocol version: 1.8.0-dev.

## Implemented

- Local-only PINE transport, bounded responses, fragmented reads, timeouts and strict address limits.
- PCSX2 folder selection, ISO/CHD selection, separate token, contextual capabilities and F9 pause.
- Experimental SCUS-97399 / D6385328 profile: +25% health, -15% health (minimum 1), full health, kill (health zero), +25% magic, -25% magic, full/empty magic, full/empty rage, min/max Blades of Chaos, min/max power upgrades and Cloud90 sabotage shield. No code injection or PNACH installation. Integer and float writes are read back; this verifies the stored value, not that the game has processed the intended behavior.
- Backend contextual policy, MAX access, presets and roulette integration. Frontend experimental status and capability filtering.
- Both client and backend require `CLOUD90_GOW_EXPERIMENTAL=1` to expose/use this work. Disabled by default. This is a developer test switch, not a declaration of compatibility.

## Evidence

PCSX2 2.8.2 was queried through PINE with the user's image. It reported God of War, SCUS-97399, UUID d6385328 and running state. Read-only observations: player 00D534C0, character 00795800, vtable 002BA528, health/max 100/100, magic 0, scene state 3. No effects were applied in this state: the profile requires scene state 11.

Memory candidates were researched against https://github.com/dphades1572/god-of-war-coop (MIT), notably its `mod/pipeline/spawn_p2.py`. This project is a reference, not a dependency. Its second-player spawning routine must not be treated as an enemy spawning routine.

## NOT validated or implemented

- No native write test during player-controlled combat. The user will perform gameplay tests. No claim that the candidate effects work in gameplay yet.
- No verified detection of all menus, QTE, cinematics, checkpoints or save-state loads. Do not load save states during a connected session; automatic safe handling remains required before stable release.
- No enemy spawn API/object allocator/resource ownership map. No added enemies, enemy damage/health scaling, input effects, invulnerability or difficulty modifiers. Freeze and petrify have no supported state mapping and are intentionally absent from capabilities/catalog. Pausing the emulator is not a substitute for either effect.
- God of War II now has an independent opt-in profile, eleven candidate operations, catalog and artwork. See GOD-OF-WAR-II-VALIDATION.md for its map and read-only native evidence. Its upgrade/status/spawn effects remain unavailable; I's addresses are never used for II.
- A full local preview can now be built with `build-gow-preview.ps1 -BaseExecutable <existing complete Game Link exe>`. It preserves the existing compiled Cuphead/GTA modules and licenses, refreshes source-owned scripts and embeds all art including GoW I. `dist/Cloud90GameLink-GowPreview.exe` is unsigned and unpublished. The earlier compiler-only executable in the research directory still omits embedded modules and must not be distributed.

## Added candidates and semantics

- `heal_max` uses the current maximum (no health-cap upgrade); `kill` writes zero and requires noDeath=false and allowDestructive=true in both policy and command.
- `magic_max` uses the maximum for the unlocked magic level; `magic_empty` empties once, not a timed lock.
- `rage_max` / `rage_empty`: float at 00302D20, 100 / 0. Does not unlock the rage ability.
- `blades_min` / `blades_max`: raw level field 0076D808, 1 / 5.
- `levels_min` / `levels_max`: power level fields 0076D7F4..0076D804, 0 / 2, plus blades 1 / 5. “Level” means upgrade levels, not a global character level, campaign chapter or difficulty. Native refresh, unlock behavior and weapon-resource changes still require gameplay tests. Reject unexpected existing field ranges.
- Upgrade changes require destructive permission and may persist in a subsequent save. Pause does not undo instant writes. Default roulette excludes them and kill.
- Candidate rage and upgrade addresses come from the original raw-code database: https://github.com/root670/CheatDevicePS2/blob/master/CheatDatabase.txt (God Of War section, Max Power-Up Level Codes and Refill Rage Of The Gods Meter). The database is research evidence, not a native test. No database content or game binaries are included in the client.

### Further status-effect research (not implemented)

Read-only inspection of the user's original ELF identified script names `SCR_TurnToRock` and `SCR_UnFreeze`. Their registration sites point at callbacks 0019F0A0 and 0019F040. Static inspection suggests event 0405 for turning a target to stone and event 040C for unfreezing the hero through a game event dispatcher. This does not establish a safe external operation: argument ownership, eligibility, effect exit and the required game-thread execution path still need validation. The string `Freeze` found nearby belongs to camera modes, not proof of a player freeze flag. Do not expose these as working effects, call game functions from the PINE thread, or distribute the extracted ELF.

## Manual test handoff

1. Run the backend branch with `CLOUD90_GOW_EXPERIMENTAL=1`. Keep the current production deployment unchanged until reviewed.
2. Run the frontend branch against that backend. Use its GoW I token.
3. Start Game Link with `Start-GodOfWarPreview.ps1`, select the PCSX2 folder and ISO, enable PINE slot 28011 in PCSX2 and enter gameplay.
4. Test heal/full health and magic first. Record expected/actual medidor values and any rejected command. Test fury only after it is unlocked.
5. For kill, disable no-death and enable destructive effects. Verify shield blocks kill, then let shield expire and test zero health.
6. Back up the memory card before testing upgrade changes. Test each weapon/power, HUD and next checkpoint; do not overwrite your main save until behavior is confirmed.
7. Do not test freeze/petrify/spawn: these are not implemented. Do not load save states during a connection.

## Continue in this order

1. Confirm visible local gameplay and inspect scene transitions without writes. Prove that the context predicate excludes unsafe scenes.
2. Validate each instant write, maximum, unlock state and HUD update. Read back changes and distinguish PINE delivery from in-game application before success ACK.
3. Add verified state-load/transition invalidation. Test races between command execution and pause/disconnect/context changes.
4. Investigate enemy object lifecycle, in-game spawn functions and loaded resource types on the game thread. Bound count, placement, lifetime and completion conditions; no copying raw live objects.
5. Verify a single compatible reinforcement in multiple arenas, then add candidate difficulty effects and expose only verified capabilities.
6. Get II's image and repeat independent profiling and tests.
7. Complete end-to-end local validation, existing-game regressions, full signed build, UI QA and review before a production test.

PCSX2 configuration was backed up at workspace `gow-research/PCSX2.before.ini` before enabling PINE. BIOS, ISO, memory cards and save states were not edited. PINE remains enabled for development.
