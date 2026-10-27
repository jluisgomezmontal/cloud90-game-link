# Cloud90 Game Link

Lets a live stream on [Cloud90](https://cloud90.live) change PC games: viewers'
gifts heal you, spawn enemies, invert your controls, fill your power gauge…
One small Windows app for every PC game Cloud90 supports, a tab per game:

| Game | How | Needs |
|---|---|---|
| **Cuphead + The Delicious Last Course** | BepInEx plugin with a local session, heartbeat and per-attempt commands. Health, movement and control effects in solo levels. | Cuphead on Steam, Windows x64 build 20241530; DLC optional and separately owned. See [build and validation notes](mod/cuphead/README.md). |
| **CTR Nitro-Fueled** | Side-by-side native offline bridge; Wumpa, weapons, turbo and timed controls. JSON inbox and native acknowledgements. | OCTR v0.6.4 portable, the separately installed [CTR Cloud90 module](https://github.com/jluisgomezmontal/ctr-cloud90/blob/main/CLOUD90.md), and the exact supported CUSA13795 1.21 image |
| **Hades** | `mod/Cloud90.lua`, a mod for the game's own Lua (one `Import` line at the end of `RoomManager.lua`, with a backup). It reads `Content/Scripts/Cloud90Inbox.lua` with `loadfile` and answers on the game's console. | Hades on Steam (Windows) |
| **The King of Fighters 2002** | `mame/plugins/cloud90`, a MAME plugin. It reads `inbox.lua` and answers in `acks.txt` (in `%LOCALAPPDATA%\Cloud90GameLink\mame\kof2002`). Only during a round of a game you started, vs the CPU. | [MAME](https://www.mamedev.org) and your own `kof2002.zip` + `neogeo.zip` |
| **Metal Slug 3** | The same MAME plugin (`...\mame\mslug3`): lives, grenades, weapons, timer, invincibility, and enemies copied from the ones on screen (taken from the game's own free list of objects). Only while playing a game you started. | MAME and your own `mslug3.zip` + `neogeo.zip` |

The app talks to Cloud90 with each game's token (DPAPI-sealed), in the same
protocol as the Nebula-based Super Mario Galaxy link, installs/uninstalls the
game side, and opens the game itself so it can read the game's answers.
`Pausar efectos` is the panic button. Log: `%LOCALAPPDATA%\Cloud90GameLink\link.log`.

Nothing of any game is included or distributed: no game files, no ROMs, no BIOS.

CTR basic effects support the older module. The new gravity, squash, fall and chaos effects require Game Link 1.3.1 and CTR bridge 2. CTR requires Game Link 1.2.2 or later and the updated module with `launcherBridgeVersion=1`.
Install its module with CTR and its client closed, select the portable folder
in its tab and launch from Game Link. This opens the original
`Crash™ Team Racing Nitro-Fueled.exe` client; use its Play button. Its language,
display, audio and controller settings remain managed by that client. The
native bridge relays state and acknowledgements through a per-session file,
independent of the client's console capture. Stopping the game clears the
playing state within three seconds, even if the client stays open. Play a local Arcade
normal race with one human against CPU. F9 and the app's pause button stop
effects. CTR commands have a per-launch session and heartbeat; disconnects
clear pending commands. Installation preserves the original runtime and saves;
uninstall removes only the module's three validated files. See the module's
documentation for the supported image hash and current validation limits.

## CTR bridge 2

CTR module capabilities and race epochs are relayed to the backend. New effects
require module bridge 2 and this 1.3.1 connector; bridge 1 retains baseline
support. Welcome/policy and per-command authorization reach the native inbox.
Fall requires explicit permission and no-death off. Install both downloads from
the new release with CTR and Game Link closed. The module owns only its existing
three files; the original launcher, runtime, game data and saves are preserved.
All four actions appear in the platform test panel after deploying the matching
backend/frontend PRs. Their real-game validation remains with the owner.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 [-SigningKey key.xml]
```

Uses the C# compiler that comes with Windows (.NET Framework 4.x). Output:
`dist\Cloud90GameLink.exe` (+ `.sig` with a signing key).

A complete Cuphead release additionally requires the game references and pinned
BepInEx/Newtonsoft packages; use the [Cuphead build command](mod/cuphead/README.md#build-the-complete-client).
The default build cannot install Cuphead's plugin and is for CI/other games.

## Tests

```powershell
npm ci
npm test                                                          # Hades mod vs a fake engine
npm run test:protocol                                             # local WebSocket security/protocol tests
powershell -ExecutionPolicy Bypass -File tests\link-test.ps1 -WsModule <ws>      # inbox, installers, protocol
powershell -ExecutionPolicy Bypass -File tests\mame\run-kof-e2e.ps1              # MAME plugin on real MAME (KOF)
powershell -ExecutionPolicy Bypass -File tests\mame\run-mslug3-e2e.ps1           # MAME plugin on real MAME (Metal Slug 3)
powershell -ExecutionPolicy Bypass -File tests\mame\run-app-e2e.ps1 -WsModule <ws>  # app + real MAME
```

The MAME tests play KOF 2002 / Metal Slug 3 headless by themselves and need your own ROMs in `C:\MAME\roms`.

The local protocol tests do not launch games or contact production servers.
They cover HTTPS/WSS endpoints, rejection of remote plaintext connections,
UTF-8 characters split across WebSocket fragments, and the 64 KiB message
limit. Each connection owns its ping timer and sends have a 10-second timeout.
Plain HTTP/WS is supported only for loopback test servers. Stop the app before
replacing the executable; its connections are cancelled when the window closes.

## License

MIT. Hades is a trademark of Supergiant Games; The King of Fighters and Metal Slug are
trademarks of SNK. This project is not affiliated with or endorsed by them.
