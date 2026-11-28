# Fell & Sell Plus

MelonLoader mods for [Fell & Sell](https://store.steampowered.com/app/4627110/) by MGI (Mad God Inc). Fell & Sell Plus is the base: new prayers and skills, a level cap of 120, permanent upgrades at Grandma's grave, a shop window, a loot filter, crafting filters, enemy health bars and difficulty presets. Four add-ons come with it and can be switched on or off in the settings app: Bows, Horde (an endless mode), Character (third person and a look editor) and Co-op (two players over Steam). A one-click setup installs everything.

Players: download the setup from the mod's Nexus Mods page. This repository holds the source code.

## What is here

| Path | What it is |
|---|---|
| `src/FellSellPlus` | The base mod (C#, .NET 6, MelonLoader 0.7 for IL2CPP, Harmony) |
| `src/FellSellBows` | Bows add-on: three craftable bows with a hold-to-draw shot |
| `src/FellSellHorde` | Horde add-on: endless waves in the first boss's hall |
| `src/FellSellCharacter` | Character add-on: third-person view and a look editor from the game's own character parts |
| `src/FellSellCoop` | Co-op add-on: two-player runs over Steam P2P or LAN, host-run enemies (needs Plus; uses Character for partner bodies) |
| `src/Shared` | Code shared by the mods (save backup on load) |
| `tools/Configurator` | The settings app (C#, WPF, .NET 8) |
| `tools/Installer` | The setup (C#, WPF, .NET Framework 4.8) |
| `tools/build-release.ps1` | Builds the mods, the settings app and the setup, and packs the release into `dist/` |
| `tools/*.py`, `tools/assets/*.py` | Build-time helpers: bow meshes and icons for the Bows add-on, the Character part catalogue, Nexus page art |
| `docs` | Manual install notes shipped in the archive and the add-ons' READMEs (`docs/release`) |

## Building

Requirements: Windows, the .NET 8 SDK, Fell & Sell on Steam.

1. **MelonLoader package.** Download MelonLoader 0.7.3 x64 (`MelonLoader.x64.zip`) from <https://github.com/LavaGang/MelonLoader/releases> and unpack it into `vendor/melonloader` (so that `vendor/melonloader/version.dll` and `vendor/melonloader/MelonLoader/` exist). The release build ships these files unchanged.
2. **Reference assemblies.** Install that MelonLoader into the game folder and start the game once, so MelonLoader generates the game's IL2CPP interop assemblies. Then copy:
   - `<game>/MelonLoader/net6/*` into `lib/ml/`
   - `<game>/MelonLoader/Il2CppAssemblies/*` into `lib/ml/il2cpp/`
   These are the game's and MelonLoader's own assemblies, so they are not in the repository.
3. **Build the release:**
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/build-release.ps1
   ```
   The script builds the mod, publishes the settings app, packs both together with MelonLoader into the setup and writes `dist/FellSellPlus-<version>-Setup.exe` and `dist/FellSellPlus-<version>-Manual.zip`.

One mod only: `dotnet build src/<Mod> -c Release` gives `src/<Mod>/bin/Release/net6.0/<Mod>.dll` (copy it to `<game>/Mods`).

## What the setup does

- Finds the game through the Steam registry keys and `libraryfolders.vdf`, or lets the player pick the folder.
- Copies the player's saves to `Documents\Fell & Sell Plus\Save backups`.
- If the Windows Desktop Runtime 8 or newer is missing, downloads Microsoft's official installer from `aka.ms` and runs it.
- Unpacks MelonLoader, the mod and the settings app into the game folder and creates a "Fell & Sell Plus Settings" shortcut.
- Copies the add-ons the player keeps switched on into `Mods` (Co-op is off by default and brings Character along). The settings app switches add-ons later by moving their dll between `Mods` and `UserData/FellSellPlus/DisabledMods`.
- Uninstall removes all of that again; saves are never touched.

## How the mod works

- Hooks are Harmony patches on the game's IL2CPP methods; each patch class is applied on its own, so a game update that breaks one hook only disables that feature.
- Three constants that the game compiled inline (cards per statue, the Dungeon+ prayer cap) are patched in memory by searching an exact byte pattern inside their own method; if the pattern is not found exactly once, that feature is skipped.
- .NET tiered compilation is turned off in MelonLoader's runtime config: on this game it crashes the runtime when hot mod methods are recompiled.
- Co-op sends small state packets (position, look, held items, hits) over Steam P2P (the game's own `steam_api64.dll`, flat API) or UDP; the host's game runs enemies, bosses and room events, the guest shows copies and forwards his hits.
- Every mod copies the save slots into `UserData/SaveBackups` when it loads.
- Settings are MelonPreferences entries (`UserData/MelonPreferences.cfg`, sections `FellSellPlus_*`); the mod also writes their schema for the settings app.
- The settings app shows pictures from the game. They are not in this repository: the mod exports them from the running game into `UserData/FellSellPlus/Art`.

## Credits

- MelonLoader by LavaGang (Apache License 2.0).
- Fonts in the settings app and setup: Unbounded, Manrope and JetBrains Mono (SIL Open Font License 1.1), see `tools/Configurator/Fonts`.
- Some ideas were inspired by kellewic's Fell & Sell mods.

© MGI (Mad God Inc). The source is public so the mod can be reviewed and built; reuse of the code or assets needs permission.
