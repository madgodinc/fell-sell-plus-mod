# Fell & Sell Coop: changelog

## 0.1.0

First public version, tested with two players over a Steam friends lobby: dungeon, forest and horde runs with enemies, bosses and room events run by the host, per-player loot, revive, and a Co-op entry in the main menu to host or join. Requires Fell & Sell Plus 1.1.0.

- Two-step co-op. The pause menu has "Invite a friend": the first "Invite" turns the running game into a host on the spot (friends lobby, no trip to the main menu, no reload) and sends the Steam invitation as soon as the lobby exists.
- While a friend hosts, the menu entry reads "<friend> is playing: join" in the main menu and in the pause menu. One press joins: from the main menu the newest save loads by itself, from the village the link comes up without loading. "Join" in the Co-op window does the same.
- A Steam invitation accepted while the game is closed now also loads the newest save by itself.
- Status lines at the top of the screen: "Waiting for a friend", "Friend connected: name", "Friend disconnected: name".
- Inviting and joining start in the village or in the main menu, not in the middle of a run.
- The Horde mod's statue window has "Invite a friend": it opens the co-op window in the village, makes you the host and lists your Steam friends with "Invite".
- Doors: a door one player opened stands open for everybody on the floor (`ShareDoors`).
- Traps: a pressure-plate trap goes off in every player's game and spinning blades turn in step with the host's; each player is hurt only by his own game's traps (`ShareTraps`).
- Forest: a guest's forest takes the host's event; the host's storm lightning no longer hurts a guest who cannot see it (`ShareForestEvent`).
