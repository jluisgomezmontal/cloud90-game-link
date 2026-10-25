# Cloud90 Game Link

Lets a live stream on [Cloud90](https://cloud90.live) change PC games: viewers'
gifts heal you, spawn enemies, invert your controls, fill your power gauge…
One small Windows app for every PC game Cloud90 supports, a tab per game:

| Game | How | Needs |
|---|---|---|
| **CTR Nitro-Fueled** | Side-by-side native offline bridge; Wumpa, weapons, turbo and timed controls. JSON inbox and native acknowledgements. | OCTR v0.6.4 portable, the separately installed [CTR Cloud90 module](https://github.com/jluisgomezmontal/ctr-cloud90/blob/feat/cloud90-game-link/CLOUD90.md), and the exact supported CUSA13795 1.21 image |
| **Hades** | `mod/Cloud90.lua`, a mod for the game's own Lua (one `Import` line at the end of `RoomManager.lua`, with a backup). It reads `Content/Scripts/Cloud90Inbox.lua` with `loadfile` and answers on the game's console. | Hades on Steam (Windows) |
| **The King of Fighters 2002** | `mame/plugins/cloud90`, a MAME plugin. It reads `inbox.lua` and answers in `acks.txt` (in `%LOCALAPPDATA%\Cloud90GameLink\mame\kof2002`). Only during a round of a game you started, vs the CPU. | [MAME](https://www.mamedev.org) and your own `kof2002.zip` + `neogeo.zip` |
| **Metal Slug 3** | The same MAME plugin (`...\mame\mslug3`): lives, grenades, weapons, timer, invincibility, and enemies copied from the ones on screen (taken from the game's own free list of objects). Only while playing a game you started. | MAME and your own `mslug3.zip` + `neogeo.zip` |

The app talks to Cloud90 with each game's token (DPAPI-sealed), in the same
protocol as the Nebula-based Super Mario Galaxy link, installs/uninstalls the
game side, and opens the game itself so it can read the game's answers.
`Pausar efectos` is the panic button. Log: `%LOCALAPPDATA%\Cloud90GameLink\link.log`.

Nothing of any game is included or distributed: no game files, no ROMs, no BIOS.

CTR requires Game Link 1.2.0. Install its module with CTR closed, select the
portable folder in its tab and launch from Game Link. Play a local Arcade
normal race with one human against CPU. F9 and the app's pause button stop
effects. CTR commands have a per-launch session and heartbeat; disconnects
clear pending commands. Installation preserves the original runtime and saves;
uninstall removes only the module's three validated files. See the module's
documentation for the supported image hash and current validation limits.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 [-SigningKey key.xml]
```

Uses the C# compiler that comes with Windows (.NET Framework 4.x). Output:
`dist\Cloud90GameLink.exe` (+ `.sig` with a signing key).

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
