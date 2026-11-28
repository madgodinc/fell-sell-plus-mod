# Fell & Sell Coop (separate mod, experimental)

Co-op is its own mod that works together with Fell & Sell Plus (it uses that mod's config layer,
log and player data through InternalsVisibleTo) and, optionally, Fell & Sell Character for player
bodies and held items. Entry point: CoopPlugin.

Files in Coop/:

- CoopModule.cs: protocol, state packets, partner bodies.
- Transport.cs, SteamLobby.cs: UDP, Steam P2P, finding each other through a Steam lobby, invitations.
- CoopMenu.cs: the main menu's Co-op entry and its host / join / invite window.
- CharacterBridge.cs: the only file that names the Character mod's types.
- DungeonSync.cs: the client plays the host's floor and follows the host from the village into a run.
- HostEnemies.cs: dungeon and forest enemies run by the host (cfg HostEnemies, on by default).
- HostEffects.cs, EnemySounds.cs, EnemyStatus.cs: the host's projectiles and effects, enemy voices,
  elites, freeze and curse on a client.
- RoomSync.cs, Gather.cs, CoopPause.cs, CoopLoot.cs, World.cs: boss rooms and room events, the
  waiting screen, menus that do not stop the game, the loot cut, which shared place a player is in.
- Revive.cs: down instead of dead, revive at the portal circle or next to the body in a locked boss
  room, leaving with the host.
- CoopFocus.cs: no pause on focus loss.
- CoopHud.cs, CoopDev.cs, CoopSettings.cs: status line, dev commands, settings.

Build: dotnet build src/FellSellCoop -c Release, then copy FellSellCoop.dll next to
FellSellPlus.dll in <game>/Mods. What works, settings and what is missing: docs/COOP.md.
