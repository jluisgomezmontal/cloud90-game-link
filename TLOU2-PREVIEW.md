# TLOU2 preview integration

The Game Link host and the Cloud90 Live preview catalog are wired to an adapter contract. No game-side adapter, native offsets, spawn functions, or supported game hash has been verified. The normal client omits the TLOU2 tab. The backend omits the catalog unless `CLOUD90_TLOU2_EXPERIMENTAL=1`.

## Manual spawner check

On 2026-11-17 the user reported that CalcioElite's Spawner worked on the supplied installation. Its local log shows the game-loop hook active and records queued requests for an infected runner and a clicker. A queued request does not confirm entity creation, hostile AI, or encounter completion in No Return.

The installed ASI exposes no PE exports. The core DLL exports only `NpcSpawnerBuildMarker`. The public mod page describes menu controls and does not document a spawn API. A Game Link adapter still needs a supported way to request a spawn and receive the result, plus a way to detect encounter transitions and remove the NPC it created.

## Verifying an adapter

1. Confirm the executable path, SHA-256 and which process owns the Steam No Return session. This host targets `tlou-ii.exe`. Verify loader compatibility separately and document where the loader expects the adapter DLL. Game Link writes only `Cloud90.Tlou2.dll` next to the executable and its ownership manifest.
2. Build the adapter for the verified executable and package it with `build-gow-preview.ps1 -BaseExecutable <complete-client> -Tlou2Adapter <adapter.dll> -Tlou2GameSha256 <sha256>`. The output is `dist/Cloud90GameLink-Tlou2Preview.exe`.
3. Use the SteamModHost file protocol. In `%LOCALAPPDATA%\Cloud90GameLink\tlou2`, read the active session from `active-session.txt` and poll that session directory's `inbox.json`. Publish `status.json` atomically. Follow `src/Hades.cs` for the inbox schema and `src/SteamModHost.cs` for status and ACK fields.
4. In each status include the game PID, `bridgeVersion: "cloud90-tlou2-1"`, actual `gameSha256`, `mode: "no_return"` during No Return, a GetTickCount64-based heartbeat, the session and a session-prefixed encounter epoch. Only advertise capabilities proven in the live scene. Publish `inPlay: false` across pauses, loads, cutscenes, deaths and encounter transitions.
5. Implement `spawn_runner`, `heal` and `shield` on the game thread. Validate pause, connectivity, epoch, expiry, duplicate IDs and protection policy at execution. Limit the preview to one spawn per action, two owned live NPCs and 30 seconds between spawns. Track only owned NPCs, clean them up on transition or disconnect, and distinguish entity creation from confirmation of combat in diagnostics. ACK success only after the effect is confirmed. Read back health after applying bounded healing. Allow shield to block only new Cloud90 sabotage.
6. Before enabling the catalog, validate a runner in two arenas and verify encounter completion with an owned NPC alive. Then test the web test button, a real gift, replayed commands, pause, reconnection, cleanup and overlay confirmation. Keep the catalog flag off while steps 1–5 are pending.

## Local executable inventory

The supplied directory contains two different executables. The hashes identify files observed on disk and do not establish support.

| File | SHA-256 |
|---|---|
| tlou-ii-l.exe | 0C75EA7A39A59EE3ABA58CCA322C8759BB3A1E79CB3C1DD5CCFA418263A23255 |
| tlou-ii.exe | AD2E89FEA79AD41668039D0D8CC940311E41C5AFDD5CA8F614181DE8AFE0CC82 |
