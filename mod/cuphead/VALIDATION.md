# Cuphead validation — 2026-10-27

Tested installation: Windows x64, Steam build 20241530, DLC enabled.
No game assemblies or save files are included in this repository or package.

## Observed in the real game

| Check | Result |
|---|---|
| Plugin load, local status and ACK arrays | Passed on Unity 2017.4.9 / Mono CLR 2 |
| Cuphead base Run & Gun (`Platforming_Level_1_2`) | All eleven real effects below confirmed with the human tester |
| Damage / heal | 3→2→3 HP; one application per command ID |
| Super energy | One card added, confirmed visually |
| Slow / increased speed / inverted controls | Applied and restored, confirmed visually |
| Shoot / dash / jump blocking | Blocked and restored individually, confirmed visually |
| Invincibility | Damage attempt refused as protected; tester confirmed unchanged HP |
| Cloud90 shield | Damage command refused as shield active; HP remained 3 |
| F9 panic | Tester confirmed immediate slow cancellation and resume with second press |
| Ms. Chalice in base Run & Gun | Detected 4 HP maximum; damage 4→3 and healing 3→4 confirmed |
| Ms. Chalice in DLC Saltbaker | Active battle detected; damage applied and native immunity respected; healing 2→3 confirmed visually |
| Ms. Chalice jump / double jump / dash | Tester confirmed blocks and restoration in Saltbaker |
| Actual CupheadHost launch | Full packaged installer → Steam launch → plugin ready → local WebSocket slow command → positive ACK |
| Airplane (`FlyingBlimp`), Ms. Chalice | Slow, increased speed and both inverted axes confirmed; jump/dash rejected as ground-only. Plane shooting block initially failed, was corrected by also blocking held duration, then confirmed by the tester |
| Heartbeat disconnect / session rebind | Cleared effects after test client exited; rebound to fresh probe session without restarting game |
| Pause, death/retry, scene changes | Status changed and new attempt epochs observed during the real tests |

Streaming JSON avoids two failures found in the initial prototype: Unity omitted
nested command/ACK arrays, and the reflection serializer required System.Data,
which this game does not ship. The streaming codec was then exercised in-game.
The first probe also hit a sharing conflict; shared reads, atomic-write retries
and a three-second transport grace period were added and retested.

## Automated validation

- Game Link: Hades Lua tests; JSON/Lua inbox, HTTPS/WebSocket security and protocol
  tests; CTR and Cuphead ACK paths; five-tab startup; sealed token persistence.
- Cuphead policy: expiry, attempt isolation, duplicate command queueing, heartbeat,
  timer restoration and opposite speed replacement. Remote commands retain their attempt epoch; menu gifts and queued/roulette effects from previous attempts are discarded.
- Streaming JSON: nested commands and ACK arrays, 64-bit clocks, safety flags,
  malformed/truncated JSON, duplicate properties and incorrect primitive types.
- Installer: traversal rejection, ownership-only removal, modified-file refusal
  and preservation of a shared loader when another plugin exists. Full-package smoke tests also passed for fresh install/uninstall, compatible pre-existing loader, and conflict rejection without altering game files.
- Backend: 679 tests passed, 3 existing tests skipped; typecheck and build passed.
- Frontend: 36 tests passed; targeted lint, typecheck and production build passed
  with the exact locked dependencies (Next.js 16.4.3). Desktop/mobile mock-API QA;
  keyboard navigation into Cuphead; mobile instructions no longer overflow.
  WCAG A/AA axe found zero violations in the main configuration region and on the mobile page after fixes. A pre-existing desktop sidebar brand-icon finding remains outside this change.
  This is automated checking, not a claim of exhaustive accessibility compliance.

## Release gates / limits

- The eleven real effects have representative ground tests. Airplane movement, control inversion and shooting also have live confirmations.
- Co-op, King's Leap, graveyard and boss hubs are deliberately unavailable.
- Healing from 8 to 9 HP is deliberately refused to avoid the native HP9 achievement side effect; this boundary is covered by a policy test.
- No full boss clear/all Saltbaker phase-transition stress test, 20-entry soak test,
  DLC-disabled installation or complete controller matrix has been completed.
- Real production gifts and non-monetary events require merged backend/client/
  frontend changes and a signed release. No production event was simulated as
  though it were a real paid gift.
- The candidate executable is a local build. Publishing a signed release and
  production deployment remain separate from these implementation PRs.
