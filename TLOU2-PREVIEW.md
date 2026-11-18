# TLOU2 preview integration

The Game Link host and the Cloud90 Live preview catalog are wired to an adapter contract. A native ASI probe can report its process, executable hash and heartbeat to the host. The probe reports an inactive session with no supported effects. Native spawn and cleanup functions still need verification in the game. The normal client omits the TLOU2 tab. The web catalog uses the normal Game Link access rules.

## Manual spawner check

On 2026-11-17 the user reported that CalcioElite's Spawner worked in campaign mode on the supplied installation. Its local log shows the game-loop hook active and records queued requests for an infected runner and a clicker. A queued request does not confirm entity creation or hostile AI. No Return was not part of this manual test.

The installed ASI exposes no PE exports. The core DLL exports only `NpcSpawnerBuildMarker`. The public mod page describes menu controls and does not document a spawn API. A Game Link adapter still needs a supported way to request a spawn and receive the result, plus a way to detect encounter transitions and remove the NPC it created.

## Build the native probe

Run `mod/tlou2/build.ps1` from the repository root. The result is `dist/tlou2-native/Cloud90.Tlou2.asi`. The currently installed game uses a `winmm.dll` ASI loader. Use the existing loader and retain its files. The probe checks for `tlou-ii.exe`, publishes a heartbeat only for an active local session and advertises zero effects. The hash passed to the preview builder must match the executable, including when testing diagnostics.

## Check loading in the actual campaign

On 2026-11-17 the campaign launch produced `probe-38144.json` from inside `tlou-ii.exe`. The reported SHA-256 matched the installed executable (`AD2E89FEA79AD41668039D0D8CC940311E41C5AFDD5CA8F614181DE8AFE0CC82`). The campaign probe observed two live heartbeats 500 ms apart. An offline replay through the packaged Game Link client's status reader accepted the captured payload with a fresh timestamp, advertised zero effects, and rejected the payload with a timestamp aged three seconds. The desktop client and Cloud90 Live still need a live connection test.

Close the game, copy the built ASI next to the two game executables and start the campaign normally. Leave the existing loader and mods in place. The probe records its load in `%LOCALAPPDATA%\Cloud90GameLink\tlou2\probe-<pid>.json`, including the exact executable name and its SHA-256. Match the report to the process that remains active in campaign. Both executable names are recognized for this check. Only `tlou-ii.exe` publishes the existing Game Link session heartbeat. This standalone check works without starting Game Link or setting a token. Remove the diagnostic ASI with the game closed if retiring the test.

With the campaign open and the TLOU2 Game Link session closed, run `tests/tlou2-live-probe.ps1` in PowerShell under the same Windows account as the game. The script creates a temporary local session using the client's file protocol, waits for two increasing heartbeats, prints the executable and hash, and clears its session pointer. It has passed with the native ASI loaded in a fixture executable. A successful run in the campaign verifies the adapter's local status transport. The desktop client and Cloud90 Live connection are separate checks.

On 2026-11-17 `NdGameSdk.log` reported successful initialization, and `T2R.ModClient.log` reported `GAME initialized!` and identified the build as `t2r-pc-1.6.0`. Cloud90's probe loaded in the same launch and reported the same executable hash. This validates SDK startup for the observed build. Neither log showed script catalog support, the installed binaries lacked the script manager identifiers found in source, and no `ScriptManager.json` was found in the game directory.

NdGameSdk source revision `6082fbb` includes T2R level-state access and a generic engine script-call implementation. Its script manager can enumerate registered native functions and cache the results in `ScriptManager.json`. Building that revision requires the missing SDK dependencies. The shell could not resolve GitHub during dependency discovery.

## Offline script inspection

ndarc build 220 extracted `build/pc/main/bin.psarc` into the build output. `t2r-dc-disasm` disassembled ten candidate scripts. A local FNV-1a lookup against names in the verified `tlou-ii.exe` resolved the following calls without a separate SID database:

| Evidence | Observed calls |
| --- | --- |
| NPC spawn test | `spawn-entity` with a runtime argument followed by two zero values. |
| Patrol supermarket mixed encounter | `wait-spawn-entity` followed by NPC state checks in the same script. |
| Patrol supermarket runner encounter | `entity-exists?`, `npc-alive?` and `npc-group-alive-count`. |
| NPC respawner and schema test scripts | `kill-entity` with an entity argument. |

The `spawn-entity-at` calls observed in the rope script do not establish that this function can create a runner. The disassembler prints zero for some dynamic arguments. Inspect the register assignments before inferring a call signature. `kill-entity` is evidence of a death operation. Safe removal without affecting encounter progression still needs verification.

The first runtime milestone is to resolve a valid runner spawner reference, execute a single spawn on the game thread, and confirm the resulting entity. Keep effect capabilities empty until the runner's lifecycle is verified in campaign.

## Native catalog and client attach

`tests/tlou2-native-catalog.ps1` reads the game process with query and read access. On 2026-11-17 it verified the initialized descriptors for `spawn-entity`, `entity-exists?`, `npc-alive?` and `kill-entity` in campaign, PID 44832. The descriptors matched the callbacks and dispatch table recovered from the pinned executable. The test never invokes a game function.

`npc-infected-runner` appears in the campaign level sets. Those references identify the archetype; the NPC spawn test passes a runtime argument to `spawn-entity`. The campaign spawner lifecycle, valid dispatch context, and entity ownership are still unverified. The T2R SDK source signature for the script registry did not match the installed binary. The dispatch descriptors alone do not establish a valid call context.

The pinned executable's `spawn-entity-type` traverses the loaded-level spawner registries, matches the type at entry offset `0x70`, and passes the field at `0x58` to the shared spawn routine. It iterates all matching entries, so it cannot guarantee one runner per gift. `get-spawner-type` independently reads the same type field. `tests/tlou2-native-catalog.ps1 -InspectRunnerSpawner` mirrors only the table reads. The first campaign snapshot found 782 loaded levels and 1,986 spawner entries, with zero entries typed `npc-infected-runner`. The updated probe also checks the distinct `infected-runner` ID found in the game's AI and spawn assets and lists distinct nonzero spawn arguments for matches. A Windows memory fixture verified both identifiers, deduplication and bounds. The updated live snapshot is pending.

A second campaign snapshot showed zero matches for both type IDs and no spawn arguments. The horde script calls `get-spawners-by-tag` with a tag derived from the encounter's `self` context and then calls `wait-spawn-entity`. Static inspection of the engine lookup located the spawner tag table and its key/value layout. `tests/tlou2-native-catalog.ps1 -InspectSpawnerTags` now captures distinct tag keys, their counts and the associated spawn arguments in `%LOCALAPPDATA%\Cloud90GameLink\tlou2\spawner-tags-<pid>.json`. A Windows fixture verified the snapshot, tag deduplication and argument association. No tag has been validated as a runner spawner in the live campaign yet.

The live tag snapshot for PID 44832 contained 444 distinct tags and 1,985 distinct nonzero spawn arguments across 1,986 tagged spawners. Matching the tags to asset names resolved 94 tags, with no runner-specific tag confirmed. Comparing loaded arguments against 5,304 campaign scripts found 121 scripts with at least one match, concentrated in Watchtower and Tunnels scripts. Disassembly found four direct `spawn-entity` calls using loaded arguments and three `wait-spawn-entity` calls. Two direct calls in `ss-wat-watchtower-escape-street` use arguments `92EDB85D4EB05C8D` and `233BB22AF59E7512`, followed by two zero values. Neither argument has been verified as a runner or as a Cloud90-owned entity. The lone match in `ss-snd-wat-explore-runner-ambush` goes to an unidentified function and does not establish a spawn candidate. `world-watchtower.psarc` contains the relevant scene packages for classifying the loaded arguments.

Extracting the Watchtower archive resolved the four direct `spawn-entity` arguments to a Humvee, a truck, shotgun ammo and a shotgun. The corresponding `*-ingame.pak` files resolved 592 loaded arguments, including 31 names beginning with `npc-`. No runner archetype was confirmed for those NPC entries. Do not use the four object calls as the runner smoke test. The remaining target is a runner whose spawner and cleanup can be verified independently of the campaign's scripted NPCs.

The Patrol supermarket packages identify `npc-runner-stores-3`, `npc-pat-supermarket-runner-1` and `npc-pat-market-runner-eating-1` as runner entries with the type field `Infected`. The supermarket runner script uses the first one for scripted patrol, sleep and encounter checks. The earlier checks for `infected-runner` and `npc-infected-runner` in that field do not identify this record class. The Watchtower packages contained 80 analogous `Infected` records, of which none matched the previous loaded argument snapshot.

The `spawn-entity` name lookup also searches the level manager's entity-spawner level list. The updated `-InspectSpawnerTags` probe reads both level lists, deduplicates shared levels and spawners, and records the live `Infected` arguments for comparison with package names. A Windows fixture verified a named infected entry present in the second list only and a duplicate spawner shared between lists. In the live campaign the second list contained 18 levels, all already present in the first list. The combined snapshot still had 782 distinct levels, 1,986 spawner entries and zero arguments with type `Infected`.

The executable registers `c-spawn-npc`, `c-spawn-npc-with-look`, `c-spawn-npc-debug` and `xspawn-npc-at`. The normal `c-spawn-npc` callback explicitly checks for a spawner and rejects an already-spawned object on a single-use spawner. `c-spawn-npc-debug` takes a transform plus string and symbol inputs, builds a separate request, and returns the result through the script value. The game's script wrapper uses `get-process-user-id` on that result. This is a candidate for a standalone runner. Its required archetype, resource loading, calling context and ownership/cleanup remain unverified, so no effect is enabled.

In the packaged TLOU2 preview, select the folder containing `tlou-ii.exe`, open campaign as usual, and click **Abrir o conectar juego**. Game Link checks the running process path, game SHA-256 and installed adapter before attaching. The attach path reads the already-installed ASI and does not install a replacement while the game is open. A Windows fixture test observed the diagnostic heartbeat and session cleanup on exit with the data directory redirected to the test workspace. A live desktop-client test remains pending.

The installed game's Spawn Character menu contains an `Infected Runner` entry whose descriptor begins with the IDs for `Infected` and `infected-runner`. The menu and `c-spawn-npc-debug` call the same worker in this build. The optional `-InspectDebugSpawner` catalog check verifies the registered script callback. It does not execute the callback. A live adapter still needs to supply the transform and resource inputs on the game thread, obtain the new NPC's identity, and confirm its creation and removal.

The stock schema respawner passes a locator, two schema strings, three schema-derived symbols and two zero arguments to wrapper `4601FB8D1ED3E38E`. After the spawn callback returns, this wrapper obtains the process user ID and passes that ID to `wait-for-entity-spawned`. The optional debug catalog also verifies the registration of those two functions. A returned process by itself is insufficient to acknowledge an effect.

The installed NdGameSdk.dll (SHA-256 `1303F4752DB1BDB996574666E027E8BC394A2D33BD1999750ED27469B17C33D0`) has 169 named exports. Its export table lacks both `ScriptManager::LookupCFunc` and `ScriptCFunc::CallScriptCFunc` found in the locally inspected SDK source at `6082fbb`. That source and the installed DLL cannot be used as a matching header/binary pair for these calls.

## Gift Spawner campaign test

On November 18, 2026, TLOU2 Gift Spawner 1.2.0 ran in the verified campaign build `t2r-pc-1.6.0`. Its bundled bridge simulated one gift ID `5655` using a test gift table set to one `Runner` per Rose. The bridge reported one NPC spawned out of one reserved. The game log independently recorded `spawn: id=1 preset=Runner user=test-viewer` with `result=ok`. The player saw the enemy in campaign.

The mod's local protocol distinguishes `ack` from a successful `spawned` result. The player ran the test from normal PowerShell after the restricted development session received `EPERM` opening the named pipe. This proves the third-party mod's spawn path. The Cloud90 Live **Probar** path and removal of the individual NPC remain unverified. The documented `cleanup` action clears queued requests and spawned NPCs as a group. Integrating the mod into a distributable product requires the author's permission.

The desktop preview can attach to the independently installed Gift Spawner when the game directory contains the mod. It verifies that the pipe belongs to the selected game process and accepts only protocol 1, mod version 1.2.0 and the verified game build. It advertises only `spawn_runner` when the mod reports a ready game. The preview sends one Runner, applies a 30-second cooldown and ignores positive queue acknowledgments. It reports success only on a matching successful Runner spawn notification. The mode does not bundle third-party files or use the shared cleanup command.

The local preview completed the path from the test page through the desktop client to campaign. Gift Spawner reported one Runner from the request and the page displayed a confirmed spawn. While the game ran in the background at 9.6 FPS, Gift Spawner temporarily blocked spawns. Returning the game to the foreground restored frame rate and let the protection period expire.

An administrator can generate a TLOU2 token in Cloud90 Live. Other accounts need MAX with approval for TLOU2. Open the packaged desktop client, paste the TLOU2 token, and connect an active campaign. Disable **Sin morir** for **Refuerzo infectado**. The result appears in history after the spawn acknowledgment. Game Link distinguishes manual tests from audience effects and passes the audience name when supplied by Cloud90 Live. The hosted page still needs an end-to-end test.

## Testing enemy ambushes

Game Link protocol `cloud90-game-link-tlou2-3` reports the exact option IDs in `ambushOptions` and signals when Gift Spawner can accept a new NPC. Cloud90 queues rapid ambushes and sends one at a time when the mod is ready. The legacy Runner effect retains its 30-second interval. The bridge sends a random distance from 5 through 10 meters. Gift Spawner chooses placement using its forward spawn and navigation logic. The protocol provides no free angle or world-coordinate field.

The backend defaults to the 23 presets whose appearances were confirmed by Gift Spawner after testing from the Cloud90 preview. `GAME_LINK_TLOU2_VERIFIED_AMBUSH_OPTIONS` overrides the complete comma-separated list. Game Link and the backend must both report support for an option before it can fire. Sorpresa chooses an available common enemy. Bosses are chosen directly. The dashboard history and OBS use the concrete enemy from the dispatched command.

Open a repeatable campaign checkpoint and run `tests/tlou2-preview-local.ps1 -Executable <path-to-preview-exe>` in normal PowerShell. Its local page lists the options reported by Game Link independently of the hosted backend's verified list. Test the original Runner and then each new preset. Record the chapter, appearance, hostility, defeat and whether the objective advances. Test both an empty area and an active encounter, plus pause and transition behavior. The response from the mod confirms appearance. Combat and progression require observation in the game.

QA4 lists Rat King, its Stalker and Bloater profiles, the two hostile WLF variants and both Seraphite bosses when the installed mod reports those presets. The QA4 campaign log confirmed successful appearances for these profiles except the Seraphite brute boss, which remains available in the local preview for testing. The main Game Link build embeds the verified executable hash and enables the TLOU2 tab. Install Gift Spawner 1.2.0 separately.

On November 18, 2026, a manual spawn from Gift Spawner's in-game menu in campaign changed the NPC counter from 0 to 1 and reported `preset=Clicker`, `wait_ms=432` and `result=ok` on build `t2r-pc-1.6.0`. The player died roughly six seconds later. The log does not identify the cause of death. This establishes appearance for this checkpoint. Game Link dispatch, Clicker defeat and campaign progression still need verification.

Later that morning, the local Game Link preview confirmed appearances for Runner, Clicker, armored Clicker, Stalker, Shambler, Bloater, Crawler, armored Rattler with rifle, Seraphite with hammer and Seraphite brute with sledgehammer. The game log recorded `user=Cloud90` and `result=ok` for all ten. These results establish appearance. Defeat and campaign progression still need verification.

The preview keeps the discovered options visible when the game pauses or Game Link reconnects. A scheduled test waits for the currently reported option, a stable campaign epoch and the interval between spawns. Switch back to the game after scheduling and resume the campaign within 45 seconds.

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
