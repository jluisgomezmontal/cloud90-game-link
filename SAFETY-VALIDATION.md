# Context, state-load and outcome improvements

## Implemented

- Each Game Link launch gives PCSX2 a unique dedicated logfile using its documented `-logfile` CLI option. Effects stay suspended until this log is readable; loss, truncation or oversized unread data fails closed.
- Logged VM pauses/resumes and ELF transitions discard pending commands and reset the context. Three consecutive matching player/character observations are required before a new epoch becomes ready.
- GoW I additionally rejects a script-held player (player +10) and an active grab/finisher handle (character +324 -> animation +84), following the previously researched single-hero/finisher layout. No assumption that II shares these offsets is made.
- The log is checked again before each command. Native save-state load markers latch effects off; F9/dashboard resume cannot bypass the latch. Restart the game from Game Link to establish a fresh run/log. This is conservative detection, not transparent resume after arbitrary state loads.
- F9/manual pause cancels local pending work with an uncertain result rather than silently losing its acknowledgement. Pauses and disconnects suspend context and clear the local shield.
- Meter/currency operations already at their target report a limit/no change rather than a successful application.
- Available effects follow current meter bounds: a full meter does not offer refill, an empty meter/counter does not offer draining, and unknown values remove the operation. Each command still revalidates the player/context and limits before writing.
- GoW announcements are held until a successful acknowledgement. Unknown outcomes keep their cooldown, are not automatically retried and are shown as uncertain rather than definitely not applied. Context changes/disconnects close pending history entries as uncertain; late or duplicate ACKs do not announce old effects.

## Automatic and real-emulator tests

Unit/profile tests cover I's script/finisher rejection, three-observation stability, new epochs, partial log lines, pause generation, load latch, truncated logs and no-write at a full meter. Backend tests cover confirmed-only announcements, duplicate/late ACK rejection, uncertain-result cooldown and closed-session history. Frontend tests distinguish uncertain outcomes from definite rejection.

Real PCSX2 2.8.2 tests were run separately against both user ISOs. Each test copied configuration to a fresh temporary data root, used separate blank memory cards and state storage, and PINE port 28012. The original memory cards/states were not used or modified. A state was created in isolated slot 1, then reloaded via PINE. Both games logged Loading SPU2 / Loading GS and the guard latched successfully. A native GoW I intro context (mode 3) was observed and rejected for effects. No health, magic, currency or upgrade values were written by these tests.

Evidence retained locally:

- II: c90-gow-native-ac6f4fcbef1d433ba9dddf249842b36e under the user's temporary folder.
- II repeat with forced unique blank card filenames: c90-gow-native-aac5cda54fb04103ad79b97d758d962c; native context 1 was also rejected.
- I: c90-gow-native-de88748da2be42639c8f2396e1fb3b4a under the user's temporary folder.

Reproduce with `tests/gow-native-safety.ps1`, passing PCSX2 executable, ISO, the source INI and gow1/gow2. The script refuses to run while PCSX2 is already open, creates a unique isolated data root, keeps the evidence, and stops only its own test process. It does not run in CI because user BIOS/ISOs are not distributed.

## Remaining limits

These native tests validate intro rejection and detection of a real state reload, not combat effects or every cinematic/QTE. II's special-scene flags still need mapping. Polling/log observation is not an atomic game-thread hook: a transition between the final check and an in-flight write remains possible. Keep both profiles experimental and do not load states during a live connection.

The desktop helper timed out launching PCSX2 and returned no targetable game window; no visible combat, HUD response or actual enemy encounter was inspected. Combat validation still needs a controllable active game window. Extra native tests do not establish stable compatibility or successful live gifts.

The user subsequently chose to perform combat testing in production themselves. No further desktop handoff is required for this implementation work; do not mark those gameplay checks as completed.

Primary implementation references: https://pcsx2.net/docs/advanced/cli/ and PCSX2 v2.8.0 SaveState.cpp (`SysState_ComponentFreezeIn`) / VMManager.cpp pause messages, corroborated by native 2.8.2 logs in the tests above.
