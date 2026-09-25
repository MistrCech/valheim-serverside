## [1.11.1] - 2026-09-25

An audit of every patch against Valheim 1.0.15 and 1.0.12, prompted by MistrCech/valheim-serverside#2.
That pull request's premise does not hold -- the only public build, 1.0.15 (25390671, 18 September
2026), still calls `IsAnyPlayerInEventArea` in `RandEventSystem.FixedUpdate`, and raids go active on
it with this mod -- but the patch was fragile in the way it describes.

### Fixed

- Random events: the `RandEventSystem.FixedUpdate` patch finds the three sites it needs by the field
  and method each uses and changes the method completely or not at all, with a warning naming what
  is missing. Before, if a game update moved the call it reuses, it did nothing without a word -- or
  reversed the local player check without replacing the local player's position, which would throw
  on every fixed step of an event. Checked by blanking each site in the real IL: 1.11.0 reversed the
  check alone in two of three cases, 1.11.1 left the method unchanged and warned in all three. The
  same for `SpawnSystem.UpdateSpawning`, whose patch threw instead and so switched off the whole mod.
- Players still logging in (version check, password) no longer count as players standing at the
  world origin. Every per-player loop counted them, so while someone sat at the password prompt the
  server loaded the zones around the origin and simulated what is there. Measured with a player who
  never finishes logging in: 1.11.0 loaded the origin zone and 9 objects around it, 1.11.1 nothing.
- `Ship.UpdateOwner` checks that the ship still has its network object, as vanilla does: the timer
  can fire in the frame the ship is removed.
- Event spawners check the player against the active event, the one whose spawners they get,
  instead of the random event.
- ValheimPlus compatibility is patched outside the per-feature safety net, so a ValheimPlus version
  its patches do not fit threw out of startup and left the whole mod unpatched. It is now logged and
  the rest applies.
- `[Fixes] DungeonLoadGuard` runs after ValheimCommunityPatch's own fix for failed dungeon rooms, as
  its description always said; before, that depended on the plugins' load order.

### Changed

- `[MaxObjectsPerFrame] MaxObjects` accepts 1 to 10000 (0 or less throttled creation to about one
  object per frame), and its description says what a vanilla dedicated server does: 100.
- The vanilla drift check also watches `ZNetScene.OutsideActiveArea` and is reviewed for 1.0.15.

### Removed

- The `WearNTear.UpdateSupport` prefix. It called `SetupColliders` when `m_colliders` was set and
  `m_bounds` was not, but the game only ever sets both together, so it never did anything -- on every
  building piece's support update.

## [1.11.0] - 2026-09-23

### Added

- `[Fixes] DungeonLoadGuard` (on): a dungeon whose room bundle Unity refuses to load no longer
  wedges its zone. In the game's asset loader a refused bundle ("another AssetBundle with the same
  files is already loaded") is stored as null and still reported as loaded; the next step throws,
  the dungeon never hears back, never spawns, and its zone stays flagged as loading -- on a server
  seen as an endless loop over the same dungeons (ddormer/valheim-serverside#120). Only a server
  running this mod gets there, since a vanilla dedicated server never loads dungeons. The guard
  takes over the bundle Unity already holds, reports a load that really failed as failed instead
  of throwing, and lets a dungeon with a failed room go so its zone keeps working and it is tried
  again the next time it is created. Checked by opening the forest crypt room bundles behind the
  loader's back: 1.10.2 logged 13 refusals, threw, and spawned none of three crypts; 1.11.0 took
  over all 13 bundles and spawned all three.
- Compatibility with ValheimCommunityPatch, which speeds up the object pass around one point, the
  server's reference position -- the world origin on a dedicated server -- while this mod simulates
  around every player:
  - its zone-diff unload (`ZNetScene.RemoveObjects`) drops everything outside the simulation
    distance of that point whenever the object lists look as the game filled them, which they do
    when no players' areas overlap. Objects around the players were then destroyed and created
    again on every pass: doors, beds and items that could not be used, dungeons reloading dozens of
    times a second until Unity refused their room bundles as already loaded, once a crash of the
    server itself (ddormer/valheim-serverside#119, #120). Measured with two players walking
    between two dungeons: 178 to 1903 dungeon loads at up to 90 a second and hundreds of refused
    bundles with it, 52 loads and none without it;
  - its spawn queue (`ZNetScene.CreateObjectsSorted`) orders new objects by distance from that
    point, so this mod's ordering by the nearest player never ran.

  Neither has a switch of its own; both are removed when the world starts unless
  `[Compat] ValheimCommunityPatchUnload` / `ValheimCommunityPatchSpawnQueue` keeps them, the rest
  of ValheimCommunityPatch is left alone. With the unload kept, the object lists are marked as
  edited on every pass so it takes the game's own unload check, its own path for mods like this
  one (measured: 52 loads, no errors).

### Changed

- The frame rate patch also sets the refresh rate the game assumes (1.0.14 and later), so 30 and
  60 FPS no longer depend on what display modes a headless process reports. Idea from @Merl in
  MistrCech/valheim-serverside#1.

## [1.10.2] - 2026-09-21

### Fixed

- Valheim 1.0.14 gave `PresentManager.RequestTargetFrameRate` a second parameter and renamed the
  first (the patch that let the frame rate limiter work with v-sync), so the patch that sets the
  server's frame rate failed and took the whole Performance feature down with it (logged as
  "Feature Performance failed to apply and is disabled"). From 1.0.14 on, the server therefore ran
  at the game's 30 FPS, without the even world-update interval, the physics catch-up limit, the
  zone budget and the performance report. The patch now takes every overload of that method and
  its first argument by position, not by name. Reported by @Merl in MistrCech/valheim-serverside#1,
  who found it first and also traced it to 1.0.14.

  Checked on 1.0.15 against 1.0.12 with a server simulating a base: a client's `UseDoor` toggles
  the door, a bed takes its owner, an item a client claims stays claimed, and `army_eikthyr`
  spawns its waves -- the same on both versions.

## [1.10.1] - 2026-09-13

### Fixed

- Accented letters typed into the server console arrived as `?`: AMP on Windows writes its own code page to the process while Unity's Mono reads standard input as UTF-8. Lines are now read as bytes; valid UTF-8 is taken as such, anything else is decoded with `[Server] ConsoleInputCodePage` (1250 by default; 852, 1252, 65001), and the first such line is logged with its bytes so the right page can be set.


## [1.10.0] - 2026-09-13

### Added

- Console commands `broadcast <text>` (a message in the middle of every player's screen, the way the game announces a raid) and `event <name> <player>` (a raid at that player; `events` lists the names). The game's own `event` needs a local player and cannot work on a dedicated server.
- Anything typed on the server console that is not one of ours is passed to the game's own console, which a dedicated server has but never reads, and what it prints goes to standard output for the panel. So the vanilla server commands work too: `kick`, `ban`, `unban`, `banned`, `stopevent`, `randomevent`, and after `devcommands` the cheat commands that need no player: `skiptime`, `setkey`, `removekey`, `resetkeys`, `listkeys`, `env`, `tod`, `wind`. Verified on a local 1.0.12 server.

### Fixed

- `[Fixes] TeleportGhosts`: a player who portalled or respawned stayed visible to the players near the old spot, frozen, until they next crossed a zone line. Valheim 1.0 files an object under its new sector before it stores the new position, and the check that tells each player to drop objects that left their area reads the position, so it saw the old spot and queued nothing. The check is re-run once the position is stored; a player already told has no entry left, so nothing is sent twice. Reproduced and verified with a fake peer on a local 1.0.12 server. Reported for 1.0 by ValheimCommunityPatch ("Fix Teleport Ghost Players").

### Removed

- `[AdminChat]`: a Valheim client runs anything typed into the chat with a leading `/` as a local console command and never sends it, so `/give` could not reach the server from a real client. `MaxGiveAmount` moved to `[Server]`.


## [1.9.0] - 2026-09-12

### Fixed

- `[Fixes] SaveClientChanges`: Valheim 1.0 saves only the world chunks it marked as changed, and a change that arrives from a player for an object the player owns marks nothing, so the new state lived only in memory until something else in that chunk changed. The chunk is now marked when such a change arrives. With this mod the server owns nearly everything near players, so the window was small: what a player just built, the ship they steer, their drops. Reported for 1.0 by ValheimCommunityPatch.


## [1.8.0] - 2026-09-11

### Added

- Console replies are also written to standard output, so a panel such as AMP shows them even with BepInEx's console off.
- Console commands `give <item> <amount> <player>` (drops the items in front of that player, stacked as the item allows; the name may be a unique beginning) and `players`, next to `save` and `stop`, for the panel the server runs in. Valheim 1.0 only lets the host use cheat commands, so `spawn` from the game console says "not valid in the current context" on a dedicated server even for admins.
- `[AdminChat]` (off by default): admins listed in adminlist.txt can shout `/give <item> [amount]`, `/save` and `/help`; replies go to their console.
- The performance log now says what the slowest frame of each period was doing: players' messages, world updates, object creation, zone generation, creature logic, other game updates, saving, and how much was Unity's own work (physics, garbage collection), with the garbage collections that ran in that frame. On the live server a ~300 ms frame showed up almost every 5 minutes without a known cause.


## [1.7.0] - 2026-09-11

### Added

- `[Performance] ServerTargetFps` (60): the game sets a dedicated server to 30 FPS, so a frame finished in 12 ms still lasts 33 ms and every reaction to a player waits for it. Measured on the live server with one player: 30 FPS at a median frame of 35 ms while the game logic took a fraction of that. 60 halves the wait whenever the server has the headroom and changes nothing under load. 0 keeps the game's 30.


## [1.6.0] - 2026-09-11

### Added

- `[Performance]` settings that smooth the server's frame time, and a periodic performance log (`StatsIntervalMinutes`): FPS, frame time (average, median, 95%, 99%, worst), fixed steps per frame and the share of time their game logic takes, the cost of sending world updates and of generating zones. With this mod the server simulates everything, so its frame time is what players feel: a felled tree turns into wood only after the server has caught up.
- `SendIntervalMs` (100): every player gets world updates at a fixed interval, the sends spread over the frames in between. Valheim serves one player per frame, so with N players each waited N+1 frames: at 15 FPS with 4 players about 330 ms, and longer with every player who joins. Measured under load with 4 players: about 3x as many sends for 0.4 percentage points more of the main thread.
- `MaxCatchUpMs` (100): after a slow frame Unity reruns the fixed step (physics, every character, creature AI) once per 20 ms missed; Valheim allowed 200 ms of catch-up, i.e. 10 steps after one bad frame, which made the next frame bad too. Now at most 5. Game time runs slightly slow during such frames.
- `MaxZonesPerTick` (1): new zones are generated one per zone tick, players taking turns, instead of one per exploring player in the same frame. Measured under load with 4 players exploring: the worst zone tick fell from 70-177 ms to 22-47 ms, with the same number of zones generated per minute. It caps how many zones a tick generates, not what one costs: a zone holding a large location can still take a few hundred ms.

None of these raise the frame rate; they cut the spikes and the waiting between server and player. Measured on a local copy of a live world with four simulated players walking outward and an artificial 50 ms of load per frame plus a 300 ms stall every 10 s.


## [1.5.0] - 2026-09-10

### Added

- Console commands on standard input: `save` saves the world, `stop` saves and shuts down cleanly. Lets server panels such as AMP stop the server without losing progress since the last autosave (AMP: App.ExitMethod=String, BepInEx console disabled).
- Cap Unity job worker threads at 8 (`[Server] UnityJobWorkers`). Unity starts one per CPU core and idle ones still use CPU; an idle server on a 24-thread machine went from 108% to 38% of a core.
- The server creates the objects nearest to a player first. It sorted them by its own reference position, which on a dedicated server lies outside the world, so after a portal or entering a new area the surroundings of a player could be the last to become active. Idea from upstream PR #100 by jsza.


### Changed

- Default per-player send queue raised from 32 KB to 48 KB. On a live server with four players it was full in about 0.1% of send ticks, only in bursts (portals, new areas), peaking at the 32 KB limit. 48 KB at 20 ticks/s is about 960 KB/s, just under the Steam send rate cap. Existing config files keep their value.


### Fixed

- Generate ghost zones around players again, as vanilla does. The ZoneSystem.Update replacement only created local zones, which reach the near simulation distance, so unexplored land in the far ring was not generated ahead of players and distant objects there (large trees, cliffs, the Mistlands mist) appeared only much closer.


## [1.3.0] - 2026-09-10

### Added

- Server-side networking limits from BetterNetworking (by CW-Jesse, MIT): per-player send queue 32 KB instead of 10 KB and Steam send rate 256-1024 KB/s instead of 150 KB/s, configurable under [Networking], with a periodic log of how often each player's send queue was full. Clients stay vanilla.


## [1.2.0] - 2026-09-10

### Changed

- Renamed to Sarkastic.eu Dedicated Simulation, a fork of Serverside Simulations by ddormer. The plugin GUID is unchanged; delete `Serverside_Simulations.dll` when upgrading.
- Game members are accessed directly instead of through Traverse, so game updates that rename them fail the build instead of silently doing nothing. If a Core patch fails to apply the mod now removes all its patches and the server runs vanilla. A startup check logs a warning when a vanilla method replaced by the mod has changed since it was last reviewed.


### Fixed

- Update for Valheim 1.0 (Vector2s zones and SimulationDistance). Based on ddormer/valheim-serverside#118 by @mreastman, which also fixed item pickup and the Pickable.RPC_Pick null reference.
- Fix the ZoneSystem.Update replacement dropping vanilla behaviour: location prefabs loaded by the server were never released (a memory leak), zones were created while locations were still being generated, and ZoneSystem.TimeSinceStart stayed at zero.
- Fix the server never creating objects when started with a non-classic -simulationdistance (any value other than 0 or 2).
- Fix Valheim 1.0 null references on the server: ship sails changing (repeated every physics frame while the sail moved), taking items from a Frost Foundry (the item was duplicated and stayed in the station) and leviathans diving.
- Update ValheimPlus compatibility (GetNearbyChests)


## [1.1.9] - 2025-05-14

### Fixed

- Fix missing effects (Revert EffectList.Create patch)


## [1.1.8] - 2025-04-20

### Added

- Remove `AudioMan.Update`, reducing future error spam


### Fixed

- Fix null references to `WearNTear.m_bounds` and `Humanoid.m_currentAttack.m_character`
- Fix shield generators and audio log spam


## [1.1.7] - 2024-11-12

### Added

- Environment.props and bepinex publicizer


### Fixed

- Update method references to static, fixing Bog Witch launch issue. Thanks to @bpage-dev


## [1.1.6] - 2023-11-22

### Fixed

- Fix exception when updating ship owner in 0.217.28
- Fix MaxObjectsPerFrame transpiler for Valheim 0.217.28


## [1.1.5] - 2023-10-25

### Fixed

- Fix private method access errors in Release build


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


### Misc

- Fix AssemblyPublicizer output path.
- Update BepInEx, Harmony and MonoMod libs


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


## [1.1.3] - 2021-09-22

### Fixed

- Fix Valheim Plus autofuel compatibility.


## [1.1.2] - 2021-09-16

### Changed

- Remove old fishing fixes (appears to have been fixed in latest Valheim patch)


## [1.1.1] - 2021-05-14

### Changed

- Update to BepInEx 5.4.10


### Fixed

- Fix fishing


## [1.1.0] - 2021-05-07

### Added

- Added "max objects per frame" configuration. Allowing for faster or slower area loading on the server.


## [1.0.3] - 2021-04-21

### Fixed

- Fix Ship container access and improve Ship ownership transfer
- Fix Ship taking 10 damage when owner changes to server.


## [1.0.2] - 2021-04-19

### Fixed

- Fix objects not being created on the server in 0.150.3.


## [1.0.1] - 2021-04-15

### Fixed

- Prevent event monsters from spawning outside of the random event area, during a random event.
