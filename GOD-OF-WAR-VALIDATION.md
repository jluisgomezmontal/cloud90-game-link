# God of War — experimental branch, NOT a production release

2026-11-10. Branch: feat/god-of-war-cloud90. Client protocol version: 1.8.0-dev.

## Implemented

- Local-only PINE transport, bounded responses, fragmented reads, timeouts and strict address limits.
- PCSX2 folder selection, ISO/CHD selection, separate token, contextual capabilities and F9 pause.
- Experimental SCUS-97399 / D6385328 profile: +25% health, -15% health (minimum 1), +25% magic, -25% magic and Cloud90 sabotage shield. No code injection or PNACH installation.
- Backend contextual policy, MAX access, presets and roulette integration. Frontend experimental status and capability filtering.
- Both client and backend require `CLOUD90_GOW_EXPERIMENTAL=1` to expose/use this work. Disabled by default. This is a developer test switch, not a declaration of compatibility.

## Evidence

PCSX2 2.8.2 was queried through PINE with the user's image. It reported God of War, SCUS-97399, UUID d6385328 and running state. Read-only observations: player 00D534C0, character 00795800, vtable 002BA528, health/max 100/100, magic 0, scene state 3. No effects were applied in this state: the profile requires scene state 11.

Memory candidates were researched against https://github.com/dphades1572/god-of-war-coop (MIT), notably its `mod/pipeline/spawn_p2.py`. This project is a reference, not a dependency. Its second-player spawning routine must not be treated as an enemy spawning routine.

## NOT validated or implemented

- No native write test during player-controlled combat. No claim that the five candidate effects work in gameplay yet.
- No verified detection of all menus, QTE, cinematics, checkpoints or save-state loads. Do not load save states during a connected session; automatic safe handling remains required before stable release.
- No enemy spawn API/object allocator/resource ownership map. No added enemies, enemy damage/health scaling, fury, input effects, invulnerability or difficulty modifiers.
- God of War II image, serial, CRC and profile are missing. The frontend continues to show it as upcoming; no GoW I addresses may be used for II.
- No full release build, signature, published download or production/live-gift test. A compiler-only executable in the research directory omits embedded game modules and must not be distributed.

## Continue in this order

1. Confirm visible local gameplay and inspect scene transitions without writes. Prove that the context predicate excludes unsafe scenes.
2. Validate each instant write, maximum, unlock state and HUD update. Read back changes and distinguish PINE delivery from in-game application before success ACK.
3. Add verified state-load/transition invalidation. Test races between command execution and pause/disconnect/context changes.
4. Investigate enemy object lifecycle, in-game spawn functions and loaded resource types on the game thread. Bound count, placement, lifetime and completion conditions; no copying raw live objects.
5. Verify a single compatible reinforcement in multiple arenas, then add candidate difficulty effects and expose only verified capabilities.
6. Get II's image and repeat independent profiling and tests.
7. Complete end-to-end local validation, existing-game regressions, full signed build, UI QA and review before a production test.

PCSX2 configuration was backed up at workspace `gow-research/PCSX2.before.ini` before enabling PINE. BIOS, ISO, memory cards and save states were not edited. PINE remains enabled for development.
