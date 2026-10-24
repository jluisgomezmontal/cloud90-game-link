# Cloud90 Game Link

Lets a live stream on [Cloud90](https://cloud90.live) change PC games: viewers'
gifts heal you, spawn enemies, invert your controls, fill your power gauge…
One small Windows app for every PC game Cloud90 supports, a tab per game:

| Game | How | Needs |
|---|---|---|
| **Hades** | `mod/Cloud90.lua`, a mod for the game's own Lua (one `Import` line at the end of `RoomManager.lua`, with a backup). It reads `Content/Scripts/Cloud90Inbox.lua` with `loadfile` and answers on the game's console. | Hades on Steam (Windows) |
| **The King of Fighters 2002** | `mame/plugins/cloud90`, a MAME plugin. It reads `inbox.lua` and answers in `acks.txt` (in `%LOCALAPPDATA%\Cloud90GameLink\mame\kof2002`). Only during a round of a game you started, vs the CPU. | [MAME](https://www.mamedev.org) and your own `kof2002.zip` + `neogeo.zip` |
| **Metal Slug 3** | The same MAME plugin (`...\mame\mslug3`): lives, grenades, weapons, timer, invincibility, and enemies copied from the ones on screen (taken from the game's own free list of objects). Only while playing a game you started. | MAME and your own `mslug3.zip` + `neogeo.zip` |

The app talks to Cloud90 with each game's token (DPAPI-sealed), in the same
protocol as the Nebula-based Super Mario Galaxy link, installs/uninstalls the
game side, and opens the game itself so it can read the game's answers.
`Pausar efectos` is the panic button. Log: `%LOCALAPPDATA%\Cloud90GameLink\link.log`.

Nothing of any game is included or distributed: no game files, no ROMs, no BIOS.

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
