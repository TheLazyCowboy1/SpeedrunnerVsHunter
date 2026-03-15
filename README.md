# SpeedrunnerVsHunter

Current planned scope: A VERY simple custom Rain Meadow gamemode.

Planned features:
* The host is the "Speedrunner" who plays Story mode fairly normally. The intended rule of the game is that the Speedrunner loses if he dies on karma 1.
* The "Hunters" spawn in random shelters other than the Speedrunner's shelter in the region.
* If the Speedrunner tries to go through a gate, Hunters are kicked to lobby or sleep screen.
* After passing through a gate, Hunters are allowed to re-join the game to be spawned in a random shelter in the region.

Most of these features can be done client-side, so this may actually not be a full gamemode, but rather a handful of hooks for Story mode.
* Clients receive a checkbox in the lobby menu to "Spawn in a Random Shelter" other than the host's shelter.
* The host changes the gamemode's "changedRegions", "readyForTransition", "region", and "myLastDenPos" fields upon changing regions to allow clients to rejoin.
*   However, since neither myLastDenPos nor defaultDenPos are synced, we cannot allow players to rejoin unless they have this mod (so that we can ensure they get the right shelter). As a result, we change nothing but defaultDenPos which IS in the lobby state, even though it's not automatically synced.
* Host is supposed to use Better Host Controls to kick clients if he wants to pass through a gate.