# Additional candidates: red orbs and amulet

Implemented in the existing experimental profiles, same PR set. The user performs native gameplay tests; no applied effect or HUD update has been observed for these additions.

| Operation | GoW I | GoW II |
| --- | --- | --- |
| orbs_add | +500, capped at 60,000 | +500, capped at 999,999 |
| orbs_remove | -500, floor zero | -500, floor zero |
| amulet_max | Not applicable / not advertised | Set Amulet of the Fates meter to 100 once |

Both orb changes require the existing destructive/permanent-change permission and may persist at the next save. Removal is troll and blocked by the Cloud90 shield. Default roulette excludes orb changes. Amulet refill does not unlock the ability or bypass zone restrictions.

I's live progress record stores red orbs as a 16-bit value at 00302D28; write opcode 5 preserves adjacent item counts. This layout is documented in the previously inspected god-of-war-coop progress record. II uses its independent 32-bit value at 00335850, read by the original ELF getter at 001D94E4; do not use the 0071C3BC display counter as the authoritative value. II's amulet float is 00335844, read by original ELF getter 001D92A0 and corroborated by the existing researched SCUS-97481_2F123FD8 PNACH data. No cheat file or game ELF is distributed.

Reject out-of-range counters/meters, wrong identity, unknown context and expired commands. Read back writes and cache uncertain results without retrying. Capability lists advertise only each game's mapped operations. Tests cover bounds, persistent permission, shield, II-only amulet and the exact narrow 16-bit request encoding.

Manual test: back up your memory card, use a secondary save, record currency, apply each orb operation, check HUD/shop, buy an upgrade and save/reload. Test shield against removal and insufficient/near-cap balances. In II, test refill only where the amulet has been obtained and can be used. Report stored value versus actual HUD/usable behavior separately.

Not implemented: input blocking/inversion, reduced Kratos damage, enemy damage/health scaling, reinforcements, random weapons and invulnerability. These require their own verified input/state/object paths and restoration lifecycle; do not substitute a HUD change or global emulator pause for them. Freeze and petrify remain unavailable as previously documented.
