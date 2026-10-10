## [1.16.0] - 2026-10-10

### Added

- `[LocationIcons]` (off by default): a location's map icon -- Haldor, Hildir, the Bog Witch, Hildir's
  dungeons, the Deep North boss room, the ancient upgrade station -- only for players who have been
  within `DiscoverRadius` (50 m) of it themselves. Vanilla sends every player every such icon as soon as
  the location is placed, which happens when anyone passes within a few zones of it, and the map shows
  it whatever the player has explored: the first player near Haldor's camp gives everyone his position.
  `ZoneSystem.SendLocationIcons` is replaced by a per-player version (also when a player joins and when
  a location is placed); found icons are remembered per character in `<world>.icons.txt` and sent within
  two seconds of finding one. The start temple's icon (always shown by the game) stays for everyone.
  Tested with two simulated players on a copy of the live world, including a restart.

## [1.15.0] - 2026-10-09

### Added

- `[Server] CpuAffinity` (empty by default: as before): `auto` pins the server to one chiplet --
  on AMD EPYC and Ryzen a die with its own L3 cache, on the first EPYC generation also with its own
  memory -- the one least busy over one second at startup (measured on a background thread, so
  startup does not wait), read from the system's own topology: Windows processor dies
  (GetLogicalProcessorInformationEx), else NUMA nodes, else L3 groups; Linux `/sys` die, node and
  cache lists. Only processors the process may already use are considered. Or a list such as
  `16-31`. Windows sets the whole process (SetProcessAffinityMask: no thread can leave it); Linux
  every thread, read again until no new one appears, and once more 30 s and 5 min after startup for
  threads that set their own processors (Steam starts three). If anything is unclear -- one chiplet,
  load unknown, a list that is not one -- nothing changes and the log says why.
- `[Server] ProcessPriority`: `Normal` (as before), `AboveNormal` or `High` (Linux: nice -5 / -10).
- `[FireControl] OnlyProjectiles` (off): only a projectile starts a fire -- a fire arrow, a Staff of
  Embers fireball, a meteor, a lava rock -- and what it sets alight burns and spreads as before
  (within `MaxRadius` and `MaxSpread`); nothing a base has (fireplaces, the fires they lit, the troll a
  player summons) throws a spark or sets anything alight. Every spark and fire records whether its
  chain began at a projectile (`sgg_fire_kind` in its ZDO). From the game's data: surtlings', fuling
  shamans' and the Charred mages' fireballs never set anything alight.

### Fixed

- Fire control reached only the fires and fireplaces the server simulated. What a player builds or
  sets alight is created and owned by their own game, and the server only takes over what nobody
  owns or what its owner has left, so a hearth a player had just built, or the fire their arrow
  started, burned by the game's own rules on that player's computer until they left or the server
  restarted -- since 1.13.0. The server now takes every fire source (every fire, and every fireplace
  that ignites or throws sparks) the moment a player's game names itself its owner: a new one, an
  update from a game that has not heard yet, or a bare claim with no data (`Fireplace.Interact` on an
  ownerless fireplace, which never reaches `ZDO.Deserialize`) -- noted in `ZDO.SetOwnerInternal` while
  `ZDOMan.RPC_ZDOData` runs, taken when it is done. A fire taken over without a record of its chain
  counts as from a projectile, rooted at its own position, unless a creature that throws sparks (the
  summoned troll) is within 8 m; with `OnlyProjectiles` such a fire is put out. The server's copy of a
  fire finds the piece, tree or log it burns on, so it goes out when that is gone.

An independent review by two readers found that a bare ownership claim was missed, that a fire taken
over lost the piece it burned on, that fires of the summoned troll counted as from a projectile, and,
in the processor placement, that an unknown load picked a chiplet blindly, that offline Linux
processors broke the die reading, that a priority failure could stop the plugin loading, and that
malformed lists were read leniently; all fixed above. A player's own arrow or fireball still throws
its first sparks on their computer, so `MaxRadius` counts from the first fire it lit (up to 2.9 m
from an arrow's impact, 5.0 m from a fireball's).

Tested on 1.0.16: with `OnlyProjectiles` a lit bonfire, hearth and floor brazier on a wood deck and
two loose fires set nothing alight in 60 s, while a Staff of Embers burst (6 sparks, recorded as
projectile) lit the grass twice and those fires threw sparks on. A simulated player's game reported a
lit hearth, a fire and a fire arrow: the server took the hearth and the fire at once (the fire as from
a projectile) and left the arrow; a newer update naming the player's game the owner, and a bare claim,
were both taken back the same moment, with the owner change sent back. A fire reported 2 m from a
summoned troll was put out; one on a wood floor found the floor and went out when the floor was
destroyed. Processor placement on a Ryzen 9 7900X3D: `auto` read 2 dies and took the idler one
(processors 0-5,12-17); the three threads Steam starts a few seconds later with all processors were
moved back after 30 s, leaving all 88 threads on the die. On the live host (Windows 11, EPYC 7551P),
a standalone probe with the same code, pinning only itself, read 4 processor dies (0-15, 16-31,
32-47, 48-63), the same 4 NUMA nodes and 8 L3 groups, measured all 64 processors and took die 0-15,
5 % busy; malformed lists were refused with the reason.

## [1.14.0] - 2026-10-09

### Added

- `[GuardianStones]` (off by default): a boss's trophy on its guardian stone, and so its power, only for
  players who have beaten that boss themselves. A power is taken and kept wholly in the player's own game,
  so a server can never take one back; instead each player's game is sent the stone as that player may
  use it: a stone whose boss they have not beaten arrives without its trophy (`s_item` set to 0 in the
  stone's own data for that one serialization in `ZDOMan.SendZDOs`, and put back -- no revision change,
  nothing saved), and their vanilla client has no power to offer. Their game would offer to hang a trophy
  of their own on what looks empty, which first asks the stone's owner for it: refused where it reaches
  the server, whether the server owns the stone (`ItemStand.RPC_RequestOwn`) or a player's game does (the
  request is not passed on, `ZRoutedRpc.RouteRPC` -- that game would grant it); also refused while their
  game may still hold an empty copy. A stone a player's game took over goes back to the server after
  10 s. Data for a stone from a player who was sent it empty is not taken (`ZDO.Deserialize` skipped, the
  server takes the stone back and raises its revision). Once a player gets that far, the stone's revision
  is raised once (while the server owns it), and their game takes the trophy; an empty copy counts as
  theirs until a newer revision has gone out. No stone is sent to a player until their raid list has
  arrived (`ZDOMan.CreateSyncList`), so a veteran logging in at the temple is never sent them empty
  first. A player standing at such a stone, or refused one, is told why (`Message`). Progress is read as
  for the night spawn guard (now shared, `Progress`). The game's guardian stones set no world key and
  their trophies cannot be taken down (read from its data). A review by three independent readers found
  that a stone owned by another player's game let the request through to that game, that dropping the
  bookkeeping when the same revision went out left a player's game with a stale empty copy, and that a
  veteran logging in got the stones empty first; all three are fixed above. Tested on a 1.0.16 copy of
  the live world, all seven stones with their trophies, reading back what each simulated player's
  connection was actually sent: a fresh player got every stone empty, a veteran all seven trophies, a
  player past the Elder only Eikthyr's and the Elder's; the fresh player was refused the Eikthyr stand,
  the veteran given it; with the veteran's game owning Yagluth's stone, the fresh player's request for it
  was not passed on (the veteran's connection never got it) and the stone was the server's again 10 s
  later; data sending Bonemass's stone back empty was not taken; the fresh player at Eikthyr's stone was
  told; a request from the fresh player in the moment their list said past the Queen, while their game
  still held the empty copy, was refused; then all seven stones went out to them with the trophies; a
  player whose list had not arrived got no stone at all until it did, then all seven with the trophies;
  every trophy in place at the end; patch, transpiler and raid checks pass; no exceptions from the
  plugin.

### Changed

- The night spawn guard's README now says which drops count for each boss, read from the game's drop
  tables: for Yagluth only a Torn Spirit (or a Wisp or Demister), for the Queen only a Majestic Carapace,
  not their trophies; "passing the trophy around" was right only for the first four bosses.

## [1.13.0] - 2026-10-09

### Added

- `[NightSpawnGuard] Enabled` (off by default): the spawns that come with boss progress -- Greydwarfs
  at night after Eikthyr; Draugr, Greydwarf Elites and Shamans and Odin after the Elder; Skeletons
  after Bonemass (the Menhir alt biome's too); Goblins after Yagluth; Seekers, Seeker Brood and Ticks
  after the Queen; Charred after Fader, read from the game's own spawn tables -- are held back in a
  spawn zone until every player in it has got that far themselves. Until then the zone behaves as in
  a world where that boss still lives (the entry's key is swapped for one no world has, for the one
  call, and always restored, also when the game's code throws). A player's progress is read from the
  raids their own client still lists for them (`possibleEvents`): the first main raid still listed,
  with the forest troll and surtling raids confirming the Elder's and Bonemass's steps, so a power
  taken at a stone someone else filled does not count as a kill. Worked out with a port of the game's
  raid check over its 1.0.16 raid data, for every progress level and every combination of other
  people's powers (README). Raids are not touched. If the game's raid data stops matching, the guard
  does nothing and says so. Tested on a 1.0.16 server at midnight with every boss's key set, players
  in open meadows: a newcomer with a veteran, and a newcomer who took Eikthyr's power from someone
  else's trophy -- all 13 entries held back; two players past Bonemass -- only Goblins, Seekers,
  Seeker Brood, Ticks and Charred held back, and a Greydwarf Elite spawned; a player past the Elder
  holding four other bosses' powers -- held back from Skeletons on; two veterans -- nothing held back;
  after a minute of it no spawn entry was left with the stand-in key (0 of 2602).
- `[FireControl]` (off by default): `MaxRadius` -- a fire starts only within this many metres of the
  source its chain began at (a lit fireplace, or where an arrow, fireball or meteor came down; every
  spark and fire keeps it in its ZDO); `MaxSpread` -- caps every spark generation count;
  `FireplaceIgnition` -- off, no fireplace sets alight what touches its flame, so a hearth, brazier or
  torch cannot start a fire at all. How far every fire source reaches in the game is in the README,
  worked out from its code and data: what each fireplace sets alight directly (the hearth about 1.4 m,
  the bonfire 1.7 m around and 6.2 m up, a floor brazier the floor under it, standing torches 0.1 m;
  sconces, lanterns, forges, kilns and smelters never), how far a spark lands (simulated with the
  game's own spark code: bonfire 2.9 m, campfire 1.0 m, a fire 3.9 m on flat ground and farther from
  a height), and how many generations follow. Tested on a fresh copy of the live world with a lit
  bonfire on a 14x14 m wooden deck, a hearth 1.3 m from a wooden wall and a floor brazier on wood:
  with the game's rules the hearth lit the wall three times in 15 s, the brazier the floor under it,
  and the deck burned outwards to 8.4 m within 25 s of the first spark landing (7 floors gone after 40 s); with
  `MaxRadius = 3` and `FireplaceIgnition = false` nothing was lit directly, no fire started beyond
  3.0 m of its source (5 stopped, 3.1-6.1 m out) and no floor burned; 0 exceptions.

### Fixed

- Fireplaces smothered for everyone but the server (`[Fixes] ServerSmoke`, on). A smoke source only
  puffs while the local player is within 64 m; a dedicated server has none, so the server never saw
  smoke: a fireplace in a room full of smoke -- every smoke source counts itself blocked with smoke
  within 0.75 m of it (0.4 m for braziers), and a fireplace blocked for 4 s goes out until the air
  clears -- went out for every player and kept burning on the server, using fuel, throwing sparks
  and setting what touches it alight; and a fire under a roof never choked. Now a smoke source
  puffs on the server while any player is within the same 64 m (`SmokeSpawner.Spawn`, one read of
  `Player.m_localPlayer` swapped for the nearest player in range), and the game's cap of 100 puffs
  at a time is counted once per connected player. Tested: the server made smoke around a player
  (up to 230 puffs with the test's fires and fireplaces), and a fire's own smoke source in a closed
  room counted itself blocked once its smoke had nowhere to go, as it does on a player's computer;
  0 exceptions. How often that smothers a fireplace or chokes a fire depends on the room; a fire
  still burns 30 s at most either way.

## [1.12.1] - 2026-10-07

### Changed

- Renamed to Sarkastic.gg Dedicated Simulation: the community moved from sarkastic.eu to sarkastic.gg.
  The DLL is now `SarkasticGG_Dedicated_Simulation.dll`; delete `SarkasticEU_Dedicated_Simulation.dll`
  from `BepInEx/plugins` when upgrading (BepInEx would load only the newer of the two, but the old file
  stays behind). The plugin GUID, and so the config file `MVP.Valheim_Serverside_Simulations.cfg`, is
  unchanged, and nothing else is.

## [1.12.0] - 2026-10-03

Valheim 1.0.16 (Steam build 25527701, 25 September 2026) changes none of the methods this mod
patches; 1.11.1 passes the patch, transpiler and raid checks on it, and so does this release.

### Added

- `[CharacterGuard]` (off by default): a server-side check on the characters players join with, for
  servers whose players keep vanilla clients. A vanilla client never sends its inventory, so the
  server cannot read or replace it; it sees the character's id, what it wears (with weapon upgrade
  levels), and the raids it is ready for, which the game derives from the items it knows and the
  bosses it has beaten. A character this world has not seen must be fresh -- still ready for
  Eikthyr's raid and wearing nothing beyond a level 1 workbench -- or it is shown a message and
  kicked (`NewCharacterAction`: Kick, Log, Ignore). A known character that comes back wearing
  something new or ready for other raids than when it left is logged (`ChangedAway`: Log, Kick,
  Ignore). Characters that built something or own a bed or tombstone in the world count as known,
  so existing players are not locked out; admins are exempt. Console: `characters`, `allow <name>`.
  The list is kept in `<world>.characters.txt`. Tested with fake players on a copy of a live world:
  a fresh character and one with a club and rags let in, one with iron gear and progress kicked,
  a builder of the world let in; on their return a new sword and new raid readiness logged (or
  kicked with `ChangedAway = Kick`), an unchanged character let in, and `allow` both cancelling a
  pending kick and letting a kicked character in.
- `[ItemLedger] Mode` (Off by default; LogOnly; On): an account per character of the items that lock
  progress, for servers whose players keep vanilla clients: what it got on the server and what it
  put back into the world. In: picked up (a DestroyZDO from that player), taken out of a container
  or off a stand. Out: dropped -- only what has been in a bag, which the game marks, so the
  resources of a removed piece do not count, and what falls out of a chest or stand its owner
  removed is matched against what it held -- put into a container or on a stand, fed to a smelter,
  cooking station or fermenter, offered at a boss altar, built with, died carrying. What a character
  wears and holds is checked every 10 seconds; an upgrade shows as a higher quality. More out than
  in, and than it could have crafted, came from another world: written to `<world>.guard.log`, and
  with On taken away -- a dropped stack cut down, a container or stand emptied of it once nobody
  uses it, a smelter's queue undone, a boss altar refusing -- and the player told (`Message`,
  `AltarMessage`). Only exact accounts are acted on: a new, fresh character from zero, any other
  after its first death here, when the tombstone shows the whole bag; for the first `GraceHours`
  (168) no character counts as new. Admins are exempt. Accounts in `<world>.items.txt`. Findings
  for an account that is not exact yet go to the guard log only, not to the BepInEx log.
- Both features keep out of the game's way: every patch catches its own faults and logs them (at
  most 20 in a quarter of an hour), so a fault never stops the original method -- a disconnect, a
  boss summon, object data from a player. If the accounts or the known characters cannot be read,
  that feature stays off until the next start and leaves the file as it is; a save cut short is
  read back from its `.tmp`. Container contents are read without creating the items, as
  `Inventory.Load` would for every stack. The guard notes what an online character wears and knows
  every minute, since a server shutdown does not disconnect the players one by one.
- `[ItemLedger] Items = auto`: the items that lock progress, from the game's own data -- what cannot
  go through a portal, what bosses drop and what summons them, every material that all recipes and
  pieces using it need more than a level 1 workbench for, every item that all its recipes need more
  for. 544 items in Valheim 1.0.16, listed by reason in the guard log at startup; `ExtraItems` and
  `ExcludeItems` adjust it, and what traders sell is always left out.
- Crafting is not seen, so a tracked item counts as crafted from the tracked items the character got,
  reckoned in its favour wherever the game leaves room: the crafting bonus at its luckiest (three
  more per craft at a station, for a lucky batch of five), an upgrade made at the 1.0 upgrade station
  with upgrader items, an item broken there handing back 35 % of its materials (never one it wears),
  a caught fish bringing the most its extra drops can (a Fish10 one silver, a Fish6 two iron ore).
  A recipe is paid in full or not at all, and an item the game removes after an hour with nobody
  near is no pickup.
- Tested on a copy of a live world on Valheim 1.0.16, with simulated vanilla clients sending through
  the server's own network entry points (`ZDOMan.RPC_ZDOData`, routed calls): black metal put into a
  chest, iron dropped, iron scrap and copper ore fed to a smelter, a boss trophy put on a stand,
  silver beyond what one caught fish brings and copper the game had removed 37 m away put into a
  chest, a boss summoned with blood the character never got -- logged, and with On taken out of the
  chest, cut, dropped from the smelter's queue, taken off the stand, refused by the altar. Bronze
  dropped beyond the four bars its copper and tin could make with the best luck: the one bar over
  logged, and only logged when the drop was picked up again at once. Nothing logged for a chest a
  player removed or a trophy taken off a stand, picked up again; a caught fish's silver; bronze from
  a sword broken at the upgrade station once it was no longer worn. Its upgrade while worn was
  reported as the cheaper of the two ways, one upgrader item. An older character's silver noted as
  unverified until its death, then 2 beyond what it took back taken away; a forge built from copper
  it never got only logged.
- The character guard and the item ledger write their own log, `<world>.guard.log`.

### Fixed

- The console help said `setkey` and `removekey` need `devcommands`; they do not (only `listkeys`,
  `resetkeys` and the like are cheats).

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
