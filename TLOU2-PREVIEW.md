# TLOU2 preview integration

The Game Link host and the Cloud90 Live preview catalog are wired to an adapter contract. A native ASI probe can report its process, executable hash and heartbeat to the host. The probe reports an inactive session with no supported effects. Native spawn and cleanup functions still need verification in the game. The normal client omits the TLOU2 tab. The backend omits the catalog unless `CLOUD90_TLOU2_EXPERIMENTAL=1`.

## Manual spawner check

On 2026-11-17 the user reported that CalcioElite's Spawner worked in campaign mode on the supplied installation. Its local log shows the game-loop hook active and records queued requests for an infected runner and a clicker. A queued request does not confirm entity creation or hostile AI. No Return was not part of this manual test.

The installed ASI exposes no PE exports. The core DLL exports only `NpcSpawnerBuildMarker`. The public mod page describes menu controls and does not document a spawn API. A Game Link adapter still needs a supported way to request a spawn and receive the result, plus a way to detect encounter transitions and remove the NPC it created.

## Build the native probe

Run `mod/tlou2/build.ps1` from the repository root. The result is `dist/tlou2-native/Cloud90.Tlou2.asi`. The currently installed game uses a `winmm.dll` ASI loader. Use the existing loader and retain its files. The probe checks for `tlou-ii.exe`, publishes a heartbeat only for an active local session and advertises zero effects. The hash passed to the preview builder must match the executable, including when testing diagnostics.

## Check loading in the actual campaign

Close the game, copy the built ASI next to the two game executables and start the campaign normally. Leave the existing loader and mods in place. The probe records its load in `%LOCALAPPDATA%\Cloud90GameLink\tlou2\probe-<pid>.json`, including the exact executable name and its SHA-256. Match the report to the process that remains active in campaign. Both executable names are recognized for this check. Only `tlou-ii.exe` publishes the existing Game Link session heartbeat. This standalone check works without starting Game Link or setting a token. Remove the diagnostic ASI with the game closed if retiring the test.

NdGameSdk revision `6082fbb` includes T2R level-state access and a generic engine script-call implementation. Source inspection did not establish a working API for creating, querying and removing an NPC. The compatibility of its runtime with the campaign executable remains untested.

## Verifying an adapter

1. Confirm the executable path, SHA-256 and which process owns the Steam game session. This host targets `tlou-ii.exe`. Verify loader compatibility separately and document where the loader expects the adapter ASI. Game Link writes only `Cloud90.Tlou2.asi` next to the executable and its ownership manifest.
2. Build the adapter for the verified executable and package it with `build-gow-preview.ps1 -BaseExecutable <complete-client> -Tlou2Adapter <adapter.asi> -Tlou2GameSha256 <sha256>`. The output is `dist/Cloud90GameLink-Tlou2Preview.exe`.
3. Use the SteamModHost file protocol. In `%LOCALAPPDATA%\Cloud90GameLink\tlou2`, read the active session from `active-session.txt` and poll that session directory's `inbox.json`. Publish `status.json` atomically. Follow `src/Hades.cs` for the inbox schema and `src/SteamModHost.cs` for status and ACK fields.
4. In each status include the game PID, `bridgeVersion: "cloud90-tlou2-1"`, actual `gameSha256`, `mode: "campaign"` during campaign or `mode: "no_return"` during No Return, a GetTickCount64-based heartbeat, the session and a session-prefixed encounter epoch. Only advertise capabilities proven in the live scene. Publish `inPlay: false` across pauses, loads, cutscenes, deaths and encounter transitions.
5. Implement `spawn_runner`, `heal` and `shield` on the game thread. Validate pause, connectivity, epoch, expiry, duplicate IDs and protection policy at execution. Limit the preview to one spawn per action, two owned live NPCs and 30 seconds between spawns. Track only owned NPCs, clean them up on transition or disconnect, and distinguish entity creation from confirmation of combat in diagnostics. ACK success only after the effect is confirmed. Read back health after applying bounded healing. Allow shield to block only new Cloud90 sabotage.
6. Validate campaign first in a repeatable encounter. Verify that a spawned runner engages the player, can be removed by the adapter, and does not block progression when alive. Validate No Return independently before advertising capabilities for that mode. Then test the web test button, a real gift, replayed commands, pause, reconnection, cleanup and overlay confirmation. Keep the catalog flag off while steps 1–5 are pending.

## Local executable inventory

The supplied directory contains two different executables. The hashes identify files observed on disk and do not establish support.

| File | SHA-256 |
|---|---|
| tlou-ii-l.exe | 0C75EA7A39A59EE3ABA58CCA322C8759BB3A1E79CB3C1DD5CCFA418263A23255 |
| tlou-ii.exe | AD2E89FEA79AD41668039D0D8CC940311E41C5AFDD5CA8F614181DE8AFE0CC82 |
