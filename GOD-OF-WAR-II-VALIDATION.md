# God of War II — experimental profile

Branch/PR set: feat/god-of-war-cloud90, alongside GoW I. Both client and backend require CLOUD90_GOW_EXPERIMENTAL=1. This is a local test build, not a stable compatibility claim.

## Implemented

- Shared PCSX2 host/transport with an independent II profile and exact serial/CRC checks.
- II token/tab, ISO/CHD selection, PINE slot 28011, F9 pause, per-context capabilities, command expiry, duplicate result cache and explicit destructive permission.
- Fourteen candidate operations: heal, damage, full health, kill, add/remove/full/empty magic, full/empty rage, Cloud90 shield, add/remove red orbs and amulet refill. See EXTRA-MODIFIERS.md for the new data locations and manual checks. Backend roulette is virtual and selects only available operations.
- Magic maximum is read from II's own current parameter table, including the signed packed-relative pointer and level-specific 16-bit entry; no hardcoded use of I's maximum or addresses.
- Catalog, default rules, no-death/destructive defaults, capability gating, setup text and destructive-permission controls in the companion backend/frontend branches.
- The web's existing gow2.webp was converted without cropping to assets/gow2.png (960 × 540); the preview embeds Art.gow2.png for both selector and banner.

## Native read-only evidence

The user's image D:\PS2\Games\God of War II (USA).iso contains SCUS_974.81, VER 1.01, NTSC. The original ELF's XOR CRC is 2F123FD8, matching PCSX2's PINE identity.

PCSX2 2.8.2 reported God of War II / SCUS-97481 / 2f123fd8. Observed during intro (mode 3, deliberately rejected for effects): player 00958660, character 00958160, vtable 002F1440, health/max 200/200, magic 200, rage 100. Parameter pointer 0068DC30, magic level 5 and decoded maximum table 0068E0CA yielded maximum 200. No writes/effects were applied to the game.

## Profile map and evidence

| Value | II location / check |
| --- | --- |
| Player | Global 002D8734; original ELF callback 001BC0D0 reads this pointer then the character |
| Context | 002D8EC0, normal-play candidate 11; multiple original ELF branches compare it with 11; intro observed as 3 |
| Character type | Vtable 002F1440 observed natively; aligned EE pointers and finite bounded meters required |
| Health / maximum | Character +178 / +17C; ELF health routine 00213760 reads +178 |
| Magic | 00335840 |
| Magic level | Byte 00335867 |
| Magic maximum | Parameters pointer 002D870C, field +4: signed word >>12 plus field address, then signed int16[level]; ELF 001D9248..001D9278 |
| Rage | 00335848; 0..100, changes meter only and does not grant the ability |

Before accepting a context, three original instructions are checked (001BC0DC, 00213760, 001D9250). Unsupported identity/signatures/context/pointers fail closed. Every write is read back; this verifies stored data, not in-game behavior or HUD response.

Primary external research: https://github.com/xs1l3n7x/pcsx2_cheats_collection/blob/main/cheats/SCUS-97481_2F123FD8.pnach corroborates magic/rage candidate data locations. No external cheat file, game ELF, ISO or BIOS is embedded in Game Link.

## Explicit remaining work

- The user performs native combat tests. No applied effect, HUD change, kill/death transition or live gift has been observed yet.
- Mode 11 alone is not proven to exclude every QTE, cinematic or scene change. Test those transitions before stable use. State loads are unsupported: pause and restart the connection; no claim of safe automatic state-load handling.
- II weapon/power upgrades are not implemented or exposed: public codes refer to persistent save/reload changes, not a confirmed live resource refresh. I's addresses are never used for II.
- Freeze, real petrification, extra enemies and enemy difficulty scaling are not implemented or exposed. They require game-thread lifecycle/state work beyond PINE data writes. No fake successful ACKs are returned for them.
- Rage availability/unlock behavior must be tested. A valid meter does not establish that the ability is unlocked in that chapter.
- No stable download, signed release or production deployment. Full local preview preserves existing compiled game modules; source-owned scripts and artwork are rebuilt from this branch.

## Manual test sequence

Start the backend branch with CLOUD90_GOW_EXPERIMENTAL=1, connect the frontend to that backend, generate the II-specific token, then run Start-GodOfWarPreview.ps1. Choose God of War II, select PCSX2, paste the token, launch the ISO and enable PINE slot 28011. Enter normal gameplay.

Test heal/full health first, then magic, then rage only where usable. Test shield against damage and empty meters. For kill, turn off Modo sin morir and enable destructive effects. Check menus/pauses/cinematics/QTE/checkpoints/death/restarts and offline recovery. Do not overwrite your main save during testing. Report the effect ID, scene, expected/actual meter, ACK and any rejection/crash. Advanced status/spawn effects are unavailable, not testable features.
