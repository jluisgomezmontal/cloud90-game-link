# Cloud90 Hades Link

Lets a live stream on [Cloud90](https://cloud90.live) change a run of **Hades**
(Supergiant Games): viewers' gifts heal Zagreus, drop boons, slow him down,
spawn enemies of the zone he's in, and more.

It has two parts:

- **`mod/Cloud90.lua`** — a mod for the game's own Lua scripts. The game's Lua
  can't use the network or files, so it reads `Content/Scripts/Cloud90Inbox.lua`
  a few times a second with `loadfile` and answers on the game's console
  (`CLOUD90 ack <id> ok`).
- **`Cloud90HadesLink.exe`** — a small Windows app. It installs the mod (one
  `Import` line at the end of `RoomManager.lua`, with a backup, and an
  **Uninstall** button), opens Hades so it can read the mod's answers, and talks
  to Cloud90 with the game's token, in the same protocol as the Nebula-based
  Super Mario Galaxy link.

Nothing of the game is included or distributed: the mod is only the file in
`mod/`, installed into the player's own copy.

## Use

1. In Cloud90 open **Juegos › Hades**, ask for access and generate your token.
2. Open `Cloud90HadesLink.exe`, copy the token and press **Pegar token**.
3. Press **Abrir Hades** (open the game from the Link, not from Steam, so the Link
   can read what the game answers).

`Pausar efectos` is the panic button. Logs: `%LOCALAPPDATA%\Cloud90HadesLink\link.log`.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 [-SigningKey key.xml]
```

Uses the C# compiler that comes with Windows (.NET Framework 4.x). Output:
`dist\Cloud90HadesLink.exe` (+ `.sig` with a signing key).

## Tests (no Hades needed)

```powershell
npm install
npm test                                                # the mod, against a fake engine
powershell -ExecutionPolicy Bypass -File tests\link-test.ps1 -WsModule <path to the ws package>
```

## License

MIT. Hades is a trademark of Supergiant Games; this project is not affiliated
with or endorsed by them.
