# Sarkastic.gg Dedicated Simulation

![version: 1.23.0](https://img.shields.io/badge/version-1.23.0-3b6e8f?style=flat-square)  ![players: vanilla clients](https://img.shields.io/badge/players-vanilla%20clients-4f7a4a?style=flat-square)  ![Valheim: 1.0.20](https://img.shields.io/badge/Valheim-1.0.20-7a5c2e?style=flat-square)  ![BepInEx: 5.4.2351](https://img.shields.io/badge/BepInEx-5.4.2351-5b5f66?style=flat-square)  ![license: MIT (own code)](https://img.shields.io/badge/license-MIT%20%28own%20code%29-5b5f66?style=flat-square)

[Source code](https://github.com/MistrCech/valheim-serverside)  ·  [Report a bug](https://github.com/MistrCech/valheim-serverside/issues/new)  ·  [Changelog](https://github.com/MistrCech/valheim-serverside/blob/main/CHANGELOG.md)  ·  [Resource Regrowth](https://thunderstore.io/c/valheim/p/Sarkastic_gg/Resource_Regrowth/)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.sarkastic.gg)  [![Support us on Patreon](https://img.shields.io/badge/Support_us_on-Patreon-FF424D?style=for-the-badge&logo=patreon&logoColor=white)](https://www.patreon.com/sarkasticgamingservers)

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

The dedicated server simulates the world — monsters, physics, ships without a driver — instead of handing each area to whichever player got there first. **Server-side only: players keep vanilla clients.**

Companion plugin: [Resource Regrowth](https://github.com/MistrCech/valheim-resource-regrowth) ([Thunderstore](https://thunderstore.io/c/valheim/p/Sarkastic_gg/Resource_Regrowth/)) brings back surtling cores, chest loot, creatures and whole zones in areas left alone; the two are made to run together.

Updated for Valheim **1.0.20** (the game's own code this mod depends on is unchanged from 1.0.16; the Ward guard and the keep-clear rule of the Portal lock, added in 1.19, were tested on 1.0.20, the older features on 1.0.16 and they have run on 1.0.20 since). Up to version 1.12 it was also checked on 1.0.15 and 1.0.12. A startup check warns about every vanilla method the mod uses that a game update has changed.

## Why, compared to vanilla

In vanilla, the first player to enter an area owns it: their game runs the monster AI and physics there, and everyone else nearby sees that area through them. If that player has a poor connection or a slow PC, everyone around suffers — monsters jump around, hits land late — and updates travel from each player to the server, on to the owner and back.

With this mod the server owns and simulates those areas:

- Each player depends only on their own connection to the server, not on someone else's.
- Clients no longer run AI and physics for the areas they would have owned, which helps slower PCs.
- Ships are handed to their driver, so steering has no round trip.

What it costs:

- The server needs more CPU, RAM and upload than a vanilla server.
- A player alone in an area now has their round trip to the server where vanilla would have had none. With a nearby server this is rarely noticeable.

### Observed on one server

Valheim 1.0.7, Windows dedicated server, up to four players, September 2026. One group's session, not a benchmark.

- No exceptions or mod warnings during play.
- Items picked up from the ground: 98% of 437 on the first ownership request (1.2.0), 214 of 214 (1.5.0); the rest within 2 s.
- The per-player send queue was full in at most 0.2% of send ticks, only in bursts such as portals.
- About 0.8 of a CPU core on average with one player, and 15–25% of a core while empty (with the job worker cap). RAM about 1.6 GB empty, 2–2.6 GB with players, levelling off.

Not covered yet: Frost Foundry, sailing, raids, Deep North events, a non-default `-simulationdistance`, and the console commands under a Windows server panel.

## What this fork adds

Compared to Serverside Simulations 1.1.9 (details in the [changelog](CHANGELOG.md)):

- **Valheim 1.0 support**, and a review of every patched method against the 1.0 code.
- **Fixes:** location prefabs were never released (a memory leak); zones could be generated before their locations; no objects were created with a non-classic `-simulationdistance`; 1.0 errors on the server with ship sails, the Frost Foundry (which duplicated items) and leviathans; the far ring of unexplored land was not pre-generated as in vanilla, so distant trees, cliffs and the Mistlands mist appeared late.
- **Objects nearest to a player are created first**, e.g. after a portal.
- **Server-side networking limits** from BetterNetworking, with a per-player log of how often they are reached.
- **Cap on Unity job worker threads**, which otherwise idle at CPU cost on many-core hosts.
- **Fix: player changes that the save skipped.** Valheim 1.0 rewrites only the world chunks it marked as changed, and a change received from a player marks nothing, so what a player just built or moved could be missing after a restart.
- **Fix: teleported players left behind as ghosts.** A player who portalled or respawned stayed visible to the players near the old spot, frozen, until they next crossed a zone line, because Valheim 1.0 checks whether an object left their area before it stores the new position.
- **Fix: fireplaces smothered for everyone but the server.** A smoke source only puffs while the local player is within 64 m, and a dedicated server has none, so the server never saw smoke: a fireplace in a room full of smoke, which every player sees go out, kept burning on the server -- using fuel, throwing sparks and setting what touches it alight -- and a fire under a roof never choked.
- **Admin commands on the server console:** `give <item> <amount> <player>`, `broadcast <text>`, `event <name> <player>`, `players`, `characters`, `allow <name>`, `save`, `stop`, typed into the panel the server runs in (AMP), and anything else goes to the game's own console (`kick`, `ban`, `banned`, `stopevent`, `setkey`, `removekey`, and after `devcommands` the cheats that need no player: `skiptime`, `listkeys`, `resetkeys` ...). A vanilla dedicated server never reads its console, and Valheim 1.0 does not let a player on a dedicated server use `spawn` from the game console, admin or not.
- **Smoother server frames:** world updates reach every player at a steady interval however many are online, one slow frame no longer makes the next one slow through physics catch-up, and new zones are generated one per tick instead of one per exploring player. A periodic log shows frame times and what they are spent on.
- **`save` and `stop` console commands** for server panels that write to standard input.
- **Character guard** (off by default): a check on the characters players join with, server-side only, so clients stay vanilla. A character this world has not seen must be fresh, and a known one that comes back changed was played elsewhere -- see below.
- **Item ledger** (off by default): an account per character of the items that lock progress, what it got here and what it put back into the world; items that came from another world are logged, or taken away -- see below.
- **Night spawn guard** (off by default): the spawns that come with boss progress (Greydwarfs, Draugr, Skeletons, Goblins, Seekers, Charred, ...) held back around a player who has not got that far themselves, even where the world as a whole has moved past it -- see below.
- **Guardian stones** (off by default): a boss's trophy on its guardian stone, and so its power, only for players who have beaten that boss themselves; to everyone else's game the stone is empty -- see below.
- **Fire control** (off by default): a limit in metres on how far fire spreads from its source, a cap on spark generations, fireplaces that never set what touches them alight, or fire from projectiles only, so a player's own build never sets it on fire -- with the reach of every fire source in the game worked out, and every fire and fireplace simulated by the server so the rules hold -- see below.
- **Portal lock** (off by default): a pair of portals used only by those it belongs to -- the builders, or the owner and the players on the ward over it; a name starting with `*` is open to everyone. Two portals are paired only if both builders may use the pair, so a portal built with the name of someone's unpaired one no longer walks anyone into their base; and no public portal near the spawn, a boss altar or a trader -- see below.
- **Ward guard** (off by default): the game's ward (guard stone) enforced by the server. In the game every check of a ward is made in the acting player's own game and nothing stops a weapon from damaging a warded build (the ward only flashes), nor a player from using a warded smelter, kiln, fire pit, bed or crop. The server judges what players' games send it, by the game's own rule: where an enabled ward covers the place, the ward's owner and the players on its list may act, nobody else -- hitting, taking down and repairing builds, opening chests and doors, taking the product out of smelters, switching fires off, claiming beds, picking crops, and more. A packet whose sender is not its connection is dropped, and so is a ward switched off or edited in another player's name; and no player may keep more than a few wards switched on (`MaxActiveWards`, 2) -- see below.
- **Base decay** (off by default): what players built is taken down by the server once the players it belongs to have stayed away long enough, so abandoned bases do not fill the world for ever. How long depends on the best protection a piece has: a ward that is switched on (14 days away, then 10 days over which the base goes), a workbench within reach (7 + 10), or nothing (3 + 4). The ward goes first, then roofs before walls, chests and beds, the workbench last; a player who comes back stops what is left of it; players are told at login, three days ahead and while it runs. A dry run (log only) first -- see below.
- **Anti-cheat** (off by default): the server looks at every object a player's game creates and takes away -- and by default kicks the player for -- what the unmodified game cannot make: a thistle, berry bush or ore planted with the cultivator, crops put down in groups faster than the game plants (unless the player's stamina paid for them: `PaidPlanting`), a piece placed in someone else's ward, far from the player or where nothing may be built, a stack larger than its item can be, the field overrides of the build-anything admin hammers, a character that flies. It can only see what a cheat leaves in the world; below is what 65 popular cheat and tweak mods leave -- see below.
- **Location icons** (off by default): a trader's (Haldor, Hildir, the Bog Witch) and other locations' map icons only for players who have found that place themselves; vanilla shows them to everyone the moment anyone's presence has generated the zone -- see below.
- **Processor placement** (off by default): the server pinned to one chiplet of an AMD EPYC or Ryzen (a die with its own L3 cache), the least busy one at startup, or to processors you name, and a higher process priority.
- **Safety:** a startup check warns when a vanilla method the mod replaces has changed in a game update; if the core patches cannot be applied, the mod removes itself and the server runs vanilla.

## Installation

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or newer on the dedicated server.
2. Copy `SarkasticGG_Dedicated_Simulation.dll` from the [latest release](https://github.com/MistrCech/valheim-serverside/releases/latest) into `BepInEx/plugins/`.
3. Back up the world and restart the server. `BepInEx/LogOutput.log` should show `Sarkastic.gg Dedicated Simulation installed` and `Vanilla drift check passed`.

Clients need nothing.

**Upgrading from Serverside Simulations:** delete `Serverside_Simulations.dll`. Both use the same plugin GUID, so only one can load; the config file `MVP.Valheim_Serverside_Simulations.cfg` carries over.

**Do not also run BetterNetworking on the server:** its server-side limits are built in.

## Configuration

`BepInEx/config/MVP.Valheim_Serverside_Simulations.cfg`, read at startup:

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | true | Turn the mod off without removing it. |
| `[MaxObjectsPerFrame] Enabled` | true | Turn the object creation limit off (vanilla's 100 per frame stays). |
| `[MaxObjectsPerFrame] MaxObjects` | 100 | Objects the server creates per frame, 1 to 10000. A vanilla dedicated server creates 100; higher loads areas faster at more CPU, lower fills them in more slowly. |
| `[Networking] Enabled` | true | Raise the limits on how fast the server sends world data to each player (the server side of BetterNetworking). Needs a restart. |
| `[Networking] QueueSizeKB` | 48 | Data queued per player before the server holds world updates for that tick (Valheim: 10). 48 KB at 20 ticks/s is about 960 KB/s, just under the send rate cap; above 80 Steam starts failing. |
| `[Networking] SteamSendRateMinKB` / `MaxKB` | 256 / 1024 | Steam send rate per player, KB/s (Valheim: 150). Keep min × players below the server's upload. |
| `[Networking] StatsIntervalMinutes` | 5 | How often to log, per player, how often the send queue was full. Near 0% means the limits are not what holds you back. 0 disables. |
| `[Server] CpuAffinity` | (empty) | Processors the server runs on. Empty: wherever the system puts it. `auto`: one chiplet -- on AMD EPYC and Ryzen a die with its own L3 cache (on the first EPYC generation also its own memory) -- the least busy over one second at startup, read from the system's own topology (Windows: processor dies, else NUMA nodes, else L3 groups; Linux: `/sys`), within the processors the process may already use. Or a list such as `16-31` or `0-7,32-39`. Processors above 63 are not used. Keep `UnityJobWorkers` below the chiplet's processor count. Needs a restart. |
| `[Server] ProcessPriority` | Normal | `Normal`, `AboveNormal` or `High`: the server's priority against everything else on the host (Linux: nice -5 / -10, which needs CAP_SYS_NICE). Needs a restart. |
| `[Server] UnityJobWorkers` | 8 | Upper limit on Unity job worker threads (Unity: one per CPU core). Only ever lowers the count; 0 leaves Unity's default. |
| `[Server] ConsoleCommands` | true | Read commands from standard input: `save`, `stop` (saves first), `players`, `give <item> <amount> <player>` (drops the items in front of that player; the name may be a unique beginning), `broadcast <text>` (a message in the middle of every player's screen), `event <name> <player>` (a raid at that player; `events` lists them), `help`; anything else goes to the game's own console. In AMP this is its console, see the AMP chapter. |
| `[Server] MaxGiveAmount` | 1000 | Most items one `give` drops. |
| `[Server] ConsoleInputCodePage` | 1250 | How a console line that is not valid UTF-8 is read (UTF-8 is always tried first): 1250 Central European, 852 Central European DOS, 1252 Western, 65001 UTF-8 only. The log shows the bytes the first time it matters. |
| `[Performance] SendIntervalMs` | 100 | How often each player gets world updates. Valheim serves one player per frame, so with N players each waits N+1 frames (330 ms at 15 FPS with 4 players). Each send costs server CPU; see the performance log. 0 keeps Valheim's behaviour. |
| `[Performance] MaxCatchUpMs` | 100 | Longest frame counted in full. After a slow frame Unity reruns physics and every creature's fixed update for each 20 ms missed (Valheim allows 200 ms, 10 times); 100 caps it at 5. Game time runs slightly slow during such frames. 0 keeps the game's setting. |
| `[Performance] MaxZonesPerTick` | 1 | New zones generated per zone tick (10 per second), players taking turns. 0 = one per player per tick, as before. |
| `[Performance] ServerTargetFps` | 60 | Frame rate the server aims for (the game sets 30). With time to spare a frame no longer waits 33 ms, so reactions to players halve; under load it changes nothing. 0 keeps 30. |
| `[Fixes] SaveClientChanges` | true | Count a change that arrives from a player as a change to its world chunk, so the next save writes it. Valheim 1.0 rewrites only changed chunks and skips those. |
| `[Fixes] TeleportGhosts` | true | Tell the players near the old spot to drop a player who teleported away. Valheim 1.0 checks whether an object left their area before it stores the new position, so the teleported player stayed there for them, frozen, until they next crossed a zone line. |
| `[Fixes] ServerSmoke` | true | Let fires and fireplaces make smoke on the server while a player is within 64 m, as they do on that player's computer in vanilla: a fireplace in a smoke-filled room goes out on the server too, and fires under a roof can choke (see Fire control below). |
| `[Fixes] DungeonLoadGuard` | true | Keep a dungeon whose room bundle fails to load from wedging its zone. When Unity refuses a bundle as already loaded, the game still reports the load as done and then throws, so the dungeon never spawns and its zone stays flagged as loading. The guard uses the bundle Unity already holds, reports a load that really failed as failed, and lets such a dungeon go so it is tried again next time. A vanilla dedicated server never loads dungeons; this mod does. |
| `[Compat] ValheimCommunityPatchUnload` | false | Only with ValheimCommunityPatch installed. Its zone-diff unload drops objects outside the simulation distance of the server's reference position -- the world origin -- so objects around players are destroyed and created again on every pass. Off: that patch of it is removed when the world starts. On: it is kept, and the object lists are marked as edited on every pass so it takes the game's own unload check. |
| `[Compat] ValheimCommunityPatchSpawnQueue` | false | Only with ValheimCommunityPatch installed. Its spawn queue orders new objects by distance from the world origin, so this mod's nearest-player ordering never runs. Off: that patch of it is removed when the world starts. On: it is kept. |
| `[CharacterGuard] Enabled` | false | Check the characters players join with (see Character guard below). |
| `[CharacterGuard] NewCharacters` | RequireFresh | `RequireFresh`: a character this world has not seen must still be ready for Eikthyr's raid and wear nothing beyond a level 1 workbench. `Allow`: every new character is let in and remembered. |
| `[CharacterGuard] NewCharacterAction` | Kick | For a new character that is not fresh: `Kick` (after a message on their screen), `Log` or `Ignore`. |
| `[CharacterGuard] ChangedAway` | Log | For a known character that comes back wearing something new or with other progress: `Log`, `Kick` or `Ignore`. |
| `[CharacterGuard] ExemptAdmins` | true | Admins are never checked. |
| `[CharacterGuard] FreshEvent` | `army_eikthyr` | The raid a fresh character is still ready for. The game stops that once a character knows the antler, the bronze or iron pickaxe, hard antler or Eikthyr's trophy. |
| `[CharacterGuard] NewCharacterMessage` / `ChangedAwayMessage` | (English text) | Shown in the middle of the player's screen before the kick. |
| `[CharacterGuard] Welcome` | false | Greet a character the first time it joins this world. |
| `[CharacterGuard] WelcomeTitle` / `WelcomeLines` | (English text) | The greeting in the middle of the screen, then the lines (separated by `\|`) in the top left corner. |
| `[ItemLedger] Mode` | Off | `Off`, `LogOnly` or `On`: keep item accounts per character (see Item ledger below); `On` also takes away items that came from another world. |
| `[ItemLedger] Items` | auto | `auto`: the items that lock progress, from the game's own data (see below). Or item prefab names, comma separated. Items traders sell are always left out. |
| `[ItemLedger] ExtraItems` / `ExcludeItems` | (empty) | Item prefab names to add to the list, or never to keep accounts of. |
| `[ItemLedger] LogAllMovements` | false | Also log every movement of a tracked item (and every craft the ledger counts), not only what cannot be accounted for. |
| `[ItemLedger] ExemptAdmins` | true | Admins get no account and are never checked. |
| `[ItemLedger] GraceHours` | 168 | For this long after the ledger first runs on a world, no character counts as new: the players who are already around come back with what they had. |
| `[ItemLedger] Message` / `AltarMessage` | (English text) | Shown in the middle of the player's screen when items are taken away, or a boss altar refuses them. |
| `[NightSpawnGuard] Enabled` | false | Hold back the spawns that come with boss progress until every player in the spawn zone has got that far themselves (see Night spawn guard below). |
| `[GuardianStones] Enabled` | false | Show a boss's trophy on its guardian stone, and so offer its power, only to players who have beaten that boss themselves (see Guardian stones below). |
| `[GuardianStones] ExemptAdmins` | true | Admins see every stone as it is. |
| `[GuardianStones] Message` | (English text) | Shown to a player standing at a stone that holds a trophy they may not use, at most once a quarter of an hour per stone; `{0}` is the boss. Empty: no message. |
| `[PortalLock] Enabled` | false | Let a pair of portals be used only by those it belongs to (see Portal lock below). |
| `[PortalLock] ExemptAdmins` | true | Admins may use and rename every portal. |
| `[PortalLock] PublicPrefix` | `*` | A portal whose name starts with this is open to everyone. Empty: none. |
| `[PortalLock] ExtraReach` | 0 | Metres added to a ward's own radius (32 m for the game's ward) when deciding whether it covers a portal. |
| `[PortalLock] Message` / `PairingMessage` | (English text) | Shown at a portal a player may not use, and to the builder of one that cannot be paired, at most once a quarter of an hour per portal. |
| `[PortalLock] KeepClear` | `StartTemple,Eikthyrnir,GDKing,Bonemass,Dragonqueen,GoblinKing,Mistlands_DvergrBossEntrance1,FaderLocation,DN_Bossroom,Vendor_BlackForest,Hildir_camp,BogWitch_Camp` | Locations near which a player may not have a public portal: the spawn, the boss altars, the traders (every place the world has for each, a trader's other possible places included). Empty: none. |
| `[PortalLock] KeepClearRadius` | 100 | How far, in metres sideways, from any of them a public portal may not stand. A player-built portal there does not count as public and cannot be given a public name. 0: the rule is off. |
| `[PortalLock] KeepClearMessage` | (English text) | Shown to a player whose portal is refused a public name there; `{0}` is the radius. |
| `[WardGuard] Enabled` | false | Make the server enforce the ward (see Ward guard below). Needs a restart to switch on; switching it off takes effect at once. |
| `[WardGuard] Structures` | On | Hitting, taking down with the hammer and repairing a player's build (and the ward itself) inside an enabled ward by a player who is not on its list; a hit with no attacker or by a creature the sender's game owns is not a player's. On: dropped, the ward flashes and the player is told. LogOnly: only written to the guard log. Off: nothing. |
| `[WardGuard] Interactions` | On | The same for opening chests and doors, stands, fermenters, beehives, sap collectors, incinerators, the map table, and what the game does not guard: taking the product out of a smelter, kiln or cooking station, switching a fireplace off, claiming a bed, picking planted crops. Putting something in, and traps, are left alone. |
| `[WardGuard] Spoofing` | On | Drop an RPC whose sender id is not its connection, and a ward's `ToggleEnabled` / `TogglePermitted` for a player id other than the sender's own. Admins are not exempt. |
| `[WardGuard] DestroyRequests` | LogOnly | A request to destroy an object that another game or the server owns, which the game's own client never sends: LogOnly counts and logs them, On takes them out of the request. Leave it in LogOnly until the log shows an honest client sends none. |
| `[WardGuard] ExemptAdmins` | true | Admins are not held to any ward. |
| `[WardGuard] Flash` | true | The ward flashes (its own shield effect) when it refuses something, at most every 3 seconds. |
| `[WardGuard] Message` | (English text) | Shown to a player the ward refused, at most every 15 seconds. Empty: no message. |
| `[WardGuard] MaxActiveWards` | 2 | How many wards one player may have switched on at once; a player who has that many and tries to switch on another is refused and told (switching one off or taking it down makes room). Wards already on stay on if the limit is lowered. Admins are not held to it. 0: no limit. |
| `[WardGuard] MaxActiveWardsMessage` | (English text) | Shown to the player so refused; `{0}` is the limit. |
| `[Decay] Mode` | Off | `Off`, `DryRun` (tracks players and writes to the log what would be taken down, changes nothing) or `On`: take down what players built once the players it belongs to have stayed away (see Base decay below). |
| `[Decay] WardGraceDays` / `WardFadeDays` | 14 / 10 | A base covered by a ward that is switched on starts to decay this many days after the last of its owner and the players on its list was active; its pieces then go over the fade. |
| `[Decay] WorkbenchGraceDays` / `WorkbenchFadeDays` | 7 / 10 | The same for what has no ward but stands within `WorkbenchRadius` of a workbench; kept by the workbench's builder. |
| `[Decay] WorkbenchRadius` | 20 | How far, in metres sideways, a workbench protects the pieces around it. |
| `[Decay] NoneGraceDays` / `NoneFadeDays` | 3 / 4 | The same for a piece with neither; kept by its builder. |
| `[Decay] ActiveMinutes` | 10 | A player counts as active when they have been online this long in one go. |
| `[Decay] ScanMinutes` | 10 | How often the world is looked through (some tens of milliseconds) to start decay, take down what is due and update what players are told. |
| `[Decay] MaxRemovals` | 200 | Most pieces taken down in one pass. |
| `[Decay] ExemptAdmins` | false | Admins' characters count as always active (the server learns that a character is an admin when it is online). Off by default: nobody is exempt unless named with `decay exempt`. |
| `[Decay] LoginMessage` | true | Tell a player half a minute after they join about their bases that are decaying or will start to in three days. |
| `[AntiCheat] Enabled` | false | Look at what players' games create and take away or kick for what the game cannot make (see Anti-cheat below). Needs a restart to switch on. |
| `[AntiCheat] Action` | Kick | What happens when a rule that is On is broken: `Log` (write it down), `Revert` (also take the object away), `Kick` (also tell the player in the middle of the screen and remove them six seconds later; they can come back and what breaks a rule is taken away again). |
| `[AntiCheat] ExemptAdmins` | true | Admins are not held to the rules. |
| `[AntiCheat] Message` | (English text) | Shown to a player about to be kicked; `{0}` is what they broke. |
| `[AntiCheat] SoftStrikes` / `WarnMessage` | 2 / (English text) | `Distance`, `Wards`, `NoBuild`, `Biome`, `Dungeon`, `Cultivated` and `Identity` depend on the state of the server a moment after the player's game decided: the first time one is broken in ten minutes the object is taken away and the player gets `WarnMessage`, the `SoftStrikes`-th time (15 s or more later) they are kicked. 1: kicked at once. |
| `[AntiCheat] UnknownPrefabs`, `NotBuildable`, `Forged`, `Identity`, `Creator`, `Distance`, `Rate`, `Items`, `CheatMarks`, `Fly`, `Fields`, `Flood`, `Level`, `Wards`, `NoBuild`, `Biome`, `Dungeon` | On | One rule each (see below): `Off`, `LogOnly` (only written to `<world>.guard.log`) or `On` (does what `Action` says). |
| `[AntiCheat] CheatMarksOther`, `Cultivated`, `Burst`, `Health`, `Rotation`, `Teleport`, `Speed` | LogOnly | The rules that can be wrong for an honest player or that break nothing but the look: a trace in the log, nothing more, until set to On. |
| `[AntiCheat] MaxLevel` | 10 | The highest creature level let through (the game makes up to three stars, level 4). |
| `[AntiCheat] MaxDistance` | 30 | Metres from the player at which a piece is "far" (the game places within 5 m of the eye, a few more for large pieces). 0: no limit. |
| `[AntiCheat] MinInterval` / `RatePlants` | 0.25 / 3 | `RatePlants` or more plants of one player within `MinInterval` seconds are more than the game plants (one in 0.4 s). |
| `[AntiCheat] PaidPlanting` / `PlantStamina` | false / 1.3 | `true`: a group of plants that `Rate` would take is let through when the cultivator was in the player's hand and the stamina of their character went down by `PlantStamina` for each plant of it (see "Paid planting" below); the plants it did not pay for are taken away and `Rate` acts as before. The game takes 1.625 at the least for a plant, times the world's stamina rate; a mod that charges less than `PlantStamina` does not pay. Needs a restart. |
| `[AntiCheat] BurstPieces` | 15 | Pieces (not plants) from one player in three seconds that make a burst. |
| `[AntiCheat] TeleportMetres` / `SpeedLimit` | 250 / 30 | A move of this many metres between two looks (every 2 s) with no portal at either end; metres per second sideways in two looks in a row. |
| `[LocationIcons] Enabled` | false | Show a location's map icon (Haldor, Hildir, the Bog Witch, Hildir's dungeons, the Deep North boss room, the ancient upgrade station) only to players who have been near it themselves; remembered per character in `<world>.icons.txt`. The start temple's icon stays for everyone. |
| `[LocationIcons] DiscoverRadius` | 0 | How near (metres, horizontally) a player must have been to a location for its icon to appear on their map. 0: as near as the game itself shows it -- the location's zone within the simulation distance of the player's zone (4 zones by default, about 256-320 m straight ahead). |
| `[FireControl] Enabled` | false | Limit how far fire spreads (see Fire control below). With the three settings below at their defaults it changes nothing. |
| `[FireControl] MaxRadius` | -1 | A fire starts only within this many metres (straight line) of the source its chain began at. -1: no limit. |
| `[FireControl] MaxSpread` | -1 | Caps how many further generations of sparks any fire may set off. 0: nothing throws sparks except an arrow or fireball where it lands. -1: as in the game. |
| `[FireControl] FireplaceIgnition` | true | Lit fireplaces set alight whatever burnable touches their flame. Off: a hearth, brazier or torch never starts a fire; a bonfire or campfire still throws sparks. |
| `[FireControl] OnlyProjectiles` | false | Only projectiles start fires -- a fire arrow, a Staff of Embers fireball, a meteor, a lava rock -- and what they set alight burns and spreads as before (within `MaxRadius` and `MaxSpread`). Nothing a base has (fireplaces, the fires they lit, the troll a player summons) throws a spark or sets anything alight. Surtlings' and fuling shamans' fireballs never set anything alight in the game. |
| `[Performance] StatsIntervalMinutes` | 5 | How often to log FPS, frame times, physics steps per frame, the cost of world updates and zone generation, and what the slowest frame was doing, while players are online. 0 disables. |

## Character guard

Valheim keeps a character on the player's own computer and a vanilla client never sends its inventory to the server, so no server-side mod can read or replace what a character carries. The server does see a character's id (the same on every world), what it wears and holds (both hands, back slots, armour, utility, trinket, with the upgrade level of weapons), and the raids it is ready for, which the game works out from the items the character knows and the bosses it has beaten. The guard uses that:

- **New characters** -- not in this world's list and without anything built, a bed or a tombstone here -- must be fresh: still ready for Eikthyr's raid (the game stops that once a character knows the antler, bronze or iron pickaxe, hard antler or Eikthyr's trophy) and wearing nothing beyond what a level 1 workbench makes. A character brought in with progress from another world gets a message and is kicked 8 seconds later (`NewCharacterAction`).
- **Known characters** that come back wearing something past a level 1 workbench they did not have when last seen here, or ready for other raids than then, were played somewhere else in between; that is logged (`ChangedAway`, or kicked). A club or rags are made anywhere in minutes and tell nothing. What an online character wears and knows is noted every minute, at every world save and at shutdown, as well as when it leaves, since a server shutdown does not disconnect the players one by one.
- **Welcome** (`Welcome`, off by default): a character let in for the first time gets `WelcomeTitle` in the middle of the screen and then each of `WelcomeLines` in the top left corner, a few seconds after joining. A kicked character gets its kick message instead.
- Characters that built something or own a bed or a tombstone in the world count as known, so switching this on does not lock out existing players. Admins are exempt.
- `characters` on the console shows how the guard sees who is online; `allow <name>` lets a character in (and one that is waiting for its kick stay). The list is `<world>.characters.txt` next to the world save.

What a character carries without wearing it stays invisible to the server: this stops characters being imported with their progress; the item ledger below catches materials brought in by known characters once they reach the world.

## Item ledger

The server never sees a bag, but nearly every way into and out of one passes through an object it does see:

- **In:** picking an item up (the player takes it over and deletes it; when the bag has room for only part of a pile, the part that went in is counted from the smaller stack the player writes back), taking it out of a chest, cart, ship or tombstone, or off an item stand or armour stand.
- **Out:** dropping it, putting it into a container or on a stand, feeding it to a smelter, kiln, refinery, cooking station or fermenter, offering it at a boss altar, building with it, dying with it (the tombstone is the whole bag). A drop counts only if the item has been in a bag: the game marks everything in a bag, and what falls out of a destroyed piece, a creature or a rock carries no mark.
- **Worn:** what a character wears and holds is checked every few seconds; an upgrade shows as a higher quality.

Crafting, upgrading, eating, buying from a trader and what a caught fish brings along are not seen. So a tracked item the character never got here counts as crafted if its recipe can be paid from the tracked items it did get, and the ledger reckons in the player's favour wherever the game leaves room: a craft at a station yields the most the crafting bonus can give (three more per craft for a lucky batch of five), an upgrade may have been made at Valheim 1.0's upgrade station, which takes only upgrader items, an item it held may have broken there and handed part of its materials back, and a caught fish counts in the most its extra drops can bring (a Fish10 one silver). What traders sell is not tracked.

**Which items:** `Items = auto` takes the items that lock progress, from the game's own data: what cannot go through a portal (ores, metals, dragon eggs), what bosses drop and what summons them, every material that all recipes and pieces using it need more than a level 1 workbench for, and every item that all its recipes need more for -- what a forge, cauldron, black forge or galdr table makes, and what goes into it. In Valheim 1.0.16 that is about 530 items; the guard log lists them by reason at startup.

When a character puts more of an item into the world than it ever got here, and could have made, the rest came from somewhere else. That is written to `<world>.guard.log` next to the world save; with `Mode = On` it is also taken away -- a dropped stack is cut down, a container or stand loses it once nobody uses it, a smelter drops it from its queue, a boss altar refuses to summon -- and the player sees a message. What was built with it, put on a cooking station or fermenter, or is worn is only logged.

- A new, fresh character (see Character guard) starts at zero, so its account is exact from the start.
- For any other character, what it carried when counting began is unknown. Its findings are logged as unverified and never acted on, until its first death here: the tombstone shows the whole bag, and from then on its account is exact.
- For the first `GraceHours` after the ledger first runs on a world, no character counts as new.
- The accounts are kept in `<world>.items.txt`.
- A fault in the ledger or the guard is logged (at most 20 times in a quarter of an hour) and never stops the game's own code. If the accounts or the known characters cannot be read, that feature stays off until the next start and leaves the file as it is.

Start with `LogOnly` for a while and read the guard log before switching to `On`.

## Night spawn guard

Some of Valheim's ambient spawns come with boss progress: Greydwarfs at night in the Meadows once Eikthyr is dead; Draugr, Greydwarf Elites and Shamans, and Odin's night visits, once the Elder is; Skeletons once Bonemass is; Goblins (the hostile wandering kind) once Yagluth is; Seekers, Seeker Brood and Ticks once the Queen is; Charred once Fader is. Vanilla gates each one by a single world-wide key, set the moment *any* player beats that boss *anywhere*: a new player can meet Seekers in the Meadows because someone else on the server has been to the Mistlands.

The guard holds such a spawn back in a zone -- the 64x64 m area vanilla spawns around; a spawn is placed 40-80 m from one of the players in it -- until every player in that zone has got that far themselves. Until then the spawn behaves there exactly as in a world where the boss still lives. In a group the least progressed player decides. Raids are not affected: with `-setkey playerevents` the game already picks raid targets by each player's own progress.

How far a player has got is read from the one thing a vanilla client tells the server about its progress: the raids it is still "ready for", which the client works out from the items it knows and its own keys (`possibleEvents`, the same list the character guard reads). The main raids form a chain -- each needs the previous boss's drop known and stops once the next boss's is -- so the first one still listed tells how far the player is. The forest troll and surtling raids, which need the Elder's and Bonemass's drop actually known, confirm those two steps, because the chain raids also stop for a player who merely took that boss's power at a stone, where someone else may have hung the trophy.

Checked against a port of the game's own raid check over its raid data (1.0.16): exact for every progress level, and no combination of other people's powers lifts a player at or below the Elder. Above that, a player looks further than they are only by taking the Queen's power and every power before it in the chain (from Bonemass's level Moder's too, from Moder's Yagluth's). A player counts a boss as beaten once they have picked up one of its drops: for Eikthyr the trophy or a Hard Antler, for the Elder the trophy or the Swamp Key, for Bonemass the trophy or the Wishbone (everyone at those two kills gets their own key and wishbone), for Moder the trophy or a Dragon Tear -- but for Yagluth only a Torn Spirit (or a Wisp or Demister from the Mistlands) and for the Queen only a Majestic Carapace, not their trophies. Fader leaves nothing of his own in the list, so his Charred count as the Queen's. A player whose list has not arrived yet counts as having beaten nothing. The Deep North's Jotun patrols are left as vanilla.

## Guardian stones

A boss's power is taken at its guardian stone, wholly in the player's own game: the stone offers it when its stand holds the trophy, and the power is kept in the character, which the server never sees. So no server can take a power away again; what it can do is decide what each player's game knows about the stone. With this on, a player who has not beaten that boss themselves -- read from their raid list the same way as the night spawn guard, with the same drops counting -- is sent the stone without its trophy: to them it is an empty stone that offers nothing. Everyone else sees it as it is, and once a player gets that far their game gets the trophy too, within a few seconds.

- Only players who may use a stone can hang its trophy. The others' game would offer to hang one from their inventory on what looks like an empty stand, which first asks the stone's owner for the stone. The server refuses that request, whether it owns the stone itself or a player's game does (it does not pass it on to that game, which would grant it), and a stone a player's game took over goes back to the server after 10 seconds.
- The trophies stay where they are. The server never changes the stone; each player's copy is made as it is sent. Should a game ever send back a stone it was shown empty, the server keeps its own, takes the stone back and logs it. The game's guardian stones set no world key and their trophies cannot be taken down (both read from its data), so a stone shown empty changes nothing else.
- A player's game gets the stones only once their raid list has arrived, so a veteran logging in at the temple never sees them empty first.
- When someone hangs the very first trophy on a stone, players who may not use it see it for up to 4 seconds before their copy shows the stone empty again (the stand's own broadcast of the new trophy). Trophies cannot be taken down, so this happens once per stone.
- A player standing at a stone they may not use is told why (`Message`), at most once a quarter of an hour per stone.
- Fader leaves nothing of his own in the raid list; his stone counts as the Queen's.
- Admins see every stone as it is (`ExemptAdmins`). A power a character took before this was switched on stays with it.

## Portal lock

Valheim pairs portals by their name alone: any two unconnected portals with the same name are connected (`Game.ConnectPortals`, on the server, every five seconds), and a player who steps into a connected portal is teleported by their own game, which the server cannot stop. So anyone who builds a portal with the name of someone's unpaired one walks into that base -- an untagged portal pairs with any other untagged one -- and anyone who finds a paired portal can use it. What the server does decide is what each player's game is told about a portal, and the lock uses that.

A pair of portals is one thing, and the same player may use both ends (one let through one end must be able to come back through the other):

- **Where a ward holds an end** -- the ward (`PrivateArea`) covers it in its own radius (32 m for the game's ward, the circle seen around it; `ExtraReach` widens it) and has the portal's builder on its list -- the owner and the players on that list may use the pair. One ward at one end decides for the pair, the far end included, wherever it stands; with a ward at both ends a player has to be on both lists. The ward's own list is used (its owner presses E on it, as before) and it counts whether the ward is switched on or off, so adding a friend never opens the portals. A ward put up over someone's portal later, which does not have its builder on its list, holds nothing.
- **Where no ward holds either end**, the builders of the two ends.
- **Everyone**, if the name starts with `PublicPrefix` (`*`) at both ends; inside someone else's ward only if that ward's owner built the portal. A name can still only be changed by those who may use the pair otherwise.
- **Admins** (`ExemptAdmins`). A portal nobody has built (spawned by an admin, no ward) stays open.

How it is kept:

- A player who may not use a pair is sent the portal's data with the connection set to "none" (`ZDOMan.SendZDOs`), and put back right after: to their game the portal is unconnected and does not teleport. Nothing is saved or changed on the server. A client only takes data newer than its copy, so when who may use a pair changes -- a name on a ward, a portal built, a ward moved -- the portal's data revision is raised once and the right copy goes out to everyone within a couple of seconds.
- Pairing: two unconnected portals of one name are connected only if both builders may use the pair -- with a ward holding an end, each builder is on its list; with none, the same player built both. Two names alike are no consent. Otherwise they stay unconnected and the builder, standing at the portal, is told why. Pairs that exist are not touched, and where no ward holds either end they stay usable by the builders of both; where a ward holds an end, its list decides who may use the pair from then on, so a builder who was never written on it asks the ward's owner (the player is told at the portal).
- A portal's name can be changed only by those who may use the pair (not by everyone just because it is public). The request is not passed on to the game that owns the portal, data from anyone else is not taken, and a player who may not use a portal is never given it.
- Everything the game's own wards already do stays: a portal can be taken apart by whoever may take apart pieces in that ward, and nobody else in a ward's area.

**Where a public portal may not stand.** A public portal next to the spawn, a boss altar or a trader makes a hub of the place that everyone passes. `KeepClear` names the locations (by default the start temple, the boss altars, Haldor, Hildir and the Bog Witch -- every place the world has for each, so a trader's other possible places count before he is found) and `KeepClearRadius` (100 m) how far, sideways, from any of them a public portal may not stand: a portal a player built there does not count as public (its builder and the ward's list use it, whatever its name starts with) and its name cannot be changed to a public one (the request is refused, the builder told why). A portal nobody built (spawned by an admin) is not held to it, so a hub at the spawn is the admin's to make. Portals that were public there when this was switched on stop being public; the log names each. Tested with two simulated players on a copy of the live world: pairs within 30, 99, 40 and 60 m of the spawn, a boss altar and Haldor were not usable by a stranger, a pair 150 m from the spawn and an admin-spawned pair 80 m from it were; a private name next to the spawn was untouched; a stranger's request to name a portal 30 m from the spawn '*...' was refused with the message and '...' went through.

What it does not do: it cannot stop a game that has been modified, and it does not hide where a portal stands. A player's character is what counts, not the account: a second character is another player.

## Ward guard

What the game's ward (guard stone, `PrivateArea`) protects, and where that is decided (Valheim 1.0.20, from the game's own code and prefab data). Every check is made in the acting player's own game -- `PrivateArea.CheckAccess` reads the local player -- so the server believes whatever a game tells it: building, taking a piece down with the hammer, opening a chest or a door, changing a sign or a portal's name. Nothing checks the ward when a piece is **hit**: a weapon, an arrow or an explosion goes through `WearNTear.RPC_Damage`, which only makes the ward flash. A player with a sword can take down a warded wall, a chest or the ward itself (1000 health; an axe and a pickaxe do nothing to it, a sword, a mace or a club do), and the PvP switch is no help, it concerns players and not what they build. Smelters, kilns, cooking stations, fireplaces, beds and planted crops have no ward check at all. A ward also protects nothing until its owner has switched it on (E), and a player whose name is on the list of one covering ward may act in all of them.

With `[WardGuard] Enabled` the server decides, for every game that talks to it, by the game's own rule (`PrivateArea.CheckAccess`): a player may act where no **enabled** ward covers the place (the ward's radius, 32 m, measured sideways), or where one of the covering wards has them on its list -- its owner, or a player who put their name on it with E. Every routed RPC a client sends for an object that is not its own passes `ZRoutedRpc.RPC_RoutedRPC` on the server; it is looked at there, before anything is done with it, and judged by the player the connection belongs to. This mod's core patch makes the server the owner of what stands near players (vanilla gives it to a player's game), so that holds for the builds of others; only an object a player's own game owns -- one it has just placed, while it stays in the area -- handles its requests in that game without the server, and such an object is the player's own.

- **Structures** (`Structures`): `RPC_Damage`, `RPC_Remove` and `RPC_Repair` for a build piece (anything with a creator, and the ward itself) in a ward, from a player who may not act there, are dropped; the ward flashes for everyone near, as it does for a hit, and the player is told. A hit with no attacker (fire, a falling object) or by a creature that the sender's game owns (a raid, a lured monster) is not a player's and is not touched: monsters do what they do in the game. Admins are not held to a ward (`ExemptAdmins`; for structures and interactions only, not for spoofing and destroy requests). Ships and carts are pieces: hitting one inside a ward is judged, steering it is not.
- **Interactions** (`Interactions`): what the game's client already refuses (chests and doors with the guard stone flag -- the iron, wood, black metal and grausten chests, barrels and wardrobes, 20 kinds of door --, item and armour stands, fermenters, beehives, sap collectors, incinerators, writing on the map table) is refused by the server too, and so is what has no check: taking the product out of a smelter, kiln or cooking station, switching a fireplace off, claiming a bed, picking planted crops. A chest asked to open answers the player no, as it does when it is in use. Personal chests (`piece_chest_private`) are the creator's and need no ward. Putting something in -- ore, fuel, food, ammo -- is left alone: it is a gift, and the player's game has already taken the item out of their inventory when it asks, so a refusal would only destroy it. Traps are left alone too: the same request is sent when something steps on one.
- **Spoofing** (`Spoofing`): the sender id in a routed packet is the client's own claim. A packet whose id is not the connection's is dropped; the game's own client always sends its own. A ward's `ToggleEnabled` and `TogglePermitted` take the player's id as a parameter: a modified game could switch off someone's ward by sending its owner's id, or put itself on the list -- both are dropped when the id is not the sender's own.
- **Destroy requests** (`DestroyRequests`): the game asks the server to destroy objects its own game owns, so a request for objects that another game or the server owns is what a modified game sends. But the game also claims an object and destroys it in the same frame (the hammer on a piece that is not a `WearNTear`, picking up, harvesting), and the claim can reach the server after the request, so an honest client shows up in the count too. LogOnly (the default) counts them and writes a summary (five minutes after the first, or when the player leaves); On takes them out of the request. Leave it in LogOnly until the log shows what the honest clients do.

- **Limit on switched-on wards** (`MaxActiveWards`, 2; 0: none): a ward is switched on by its creator pressing E on it (`ToggleEnabled`; the game does nothing for anyone else). A creator who already has that many wards on is refused -- the request is dropped and they are told in the middle of the screen -- until they switch one off or take it down. A ward that a player has just placed is owned by that player's game, which handles the key press itself without asking the server, so the limit is also kept on what is: every five seconds the server counts the wards that are on and switches off the newest of a player who has too many (taking the ward over and writing `enabled` off), and tells them. Building more is not held back. The wards that are on when the server starts, or when the limit is changed, stay on. A player is held to it while they are in the game, and every ward above the limit goes off, the newest first. Admins are not held to it. Without a limit the world fills with wards, and under Base decay every ward is a base that is kept alive; the limit asks no second kind of ward and no mod on the client.

Every refusal is written to `<world>.guard.log` (the character guard's and the item ledger's file): who, what, where, whose ward, at most once a minute per player, kind and ward, with how many there were since the last line. With LogOnly the line ends "LogOnly: let through" and nothing is stopped; set `Structures`, `Interactions` and `Spoofing` to LogOnly first to see what the rules would do (the limit on switched-on wards has no LogOnly: it is on at 2 as soon as the guard is). `Enabled` needs a restart to switch on; switching it off takes effect at once.

What it does not do: ground items, tombstones (anyone may open one, as in the game), steering a ship or pulling a cart, the text of a sign (written straight into its data by the player's game), terrain, and fire and falling or colliding objects (they have no attacker; Fire control limits fire). It cannot stop a game that takes ownership of an object without asking its owner (a boss trophy used on a turret does) and then hits it itself, nor a piece that is out of every ward's radius from carrying warded ones (taking down the pillar takes down what stands on it). And it cannot tell a modified game from an honest one, only refuse what an honest one never sends: a modified game can write an object's data directly, name another player's character as its own (the server takes the identity from the client's character data) or name a creature as the attacker of a hit.

Tested on a copy of the live world with three simulated players (a ward's owner, a stranger and a friend put on the ward's list) sending each request as a client does, with the guard off, On, LogOnly and with `DestroyRequests` On, and with a real, unmodified Valheim 1.0.20 client hitting a warded wall: off the list its hits were refused ("tried to hit woodwall ... refused") and the wall stayed untouched, on the list the wall was hit as in the game, and with the guard off it came down; the owner, a listed friend, creatures of the sender's game and admins were never refused. Finding the wards among 349 000 objects takes 6.5 ms every 5 seconds. The full run is in [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md).

## Base decay

A world where every visitor leaves a camp behind fills up: the objects stay in the save, in memory and in every player's game for ever, and an abandoned base is a ward nobody can use and chests nobody opens. With `[Decay] Mode` the server takes down what players built once the players it belongs to have stayed away long enough. It is server-side only, so clients stay vanilla, and it is meant to be run as a dry run for two weeks first (`DryRun`: it tracks the players and writes what it would do to the log -- including how many portals, ships and chests are in each base -- and changes nothing in the world; the first ward base can only begin after the ward grace of 14 days).

**Three claims on a piece.** A piece (anything a player built: walls and floors, chests with what is in them, beds, portals, ships and carts, crops, signs) can be claimed by a ward that is switched on over it, by a workbench near it, and by its own builder. Each claim has a *grace* (days since the last of the players who hold it was active, before anything happens) and a *fade* (days over which the piece then goes), so the total life of an abandoned base is grace + fade:

| claim | held by | grace + fade (defaults) |
|---|---|---|
| a ward that is **switched on** covers the piece (its own radius, 32 m) | the ward's owner and the players on its list | 14 + 10 days |
| a **workbench** within `WorkbenchRadius` (20 m) | the workbench's builder | 7 + 10 days |
| the piece's **builder** alone (all such pieces of one builder are one *camp*) | the builder | 3 + 4 days |

A piece is protected as long as *any* claim on it is within its grace; when the last has lapsed it goes with the base of that claim, over that claim's fade. A workbench holds everything within its reach whoever built it, so an active player's workbench beside an abandoned camp keeps the camp (and a ward the same); the two halves of a portal pair are one thing and live while either end is held. So a builder who plays every day keeps their piece standing inside a ward whose owner is long gone, and an abandoned base lives 24 days under a ward, 17 under a workbench, 7 as a camp. A ward that is switched off protects nothing, as in the game; a ward or a workbench that is taken down no longer holds its pieces. A base is told apart by the builder and the place of its ward or workbench (the game's own object numbers change every time the world is loaded, so they cannot be kept).

A player counts as **active** when they have been in the game `ActiveMinutes` (10) in one go, so a quick login to see whether the base is still there does not keep it for ever; and **nothing starts or is taken down while one of the players who hold the base is in the game**, even before their ten minutes are up (they are told it is about to start); nor while a player is connecting (the pass waits for the next one), nor while a player stands within 30 m of a ship or cart that is due, nor while a piece that is protected rests against it (the game would break that piece too). At most five bases begin in a pass. A player the server has not met counts as active from the first time it sees their builds, so switching decay on takes down nothing at once. The character is what counts, not the account: a second character is another player.

**The clock counts only the time the server runs under decay** (the real time of the first run plus the uptime kept in the file; it stands still while `Mode` is `Off`), so a week of maintenance, a restore or a host whose clock jumps takes nothing from anyone's grace.

**What happens.** When the last claim on a base has lapsed an *episode* begins: every piece of the base gets the time at which it goes, spread over the fade. The ward is first, within a day (a tenth of the fade), because it is what keeps others from touching a base, and a day is time to put right a mistake -- a setting, an old file -- before it is gone for good. Then the highest pieces (roofs, then walls, then floors, by height above the ground), then what holds things (chests, beds, tables), the workbench last. The time is written to the piece itself (`sgg_decay_at`, and `sgg_decay_ep` naming the episode), so a base does not lose its protection level when its ward goes. The pieces that go within a day are worn: their health is cut to a fifth, so the game shows them broken, and a hammer can still repair them. A piece goes without a trace -- no debris, and what is in a chest goes with it. At most `MaxRemovals` (200) pieces go in one pass; the world is looked through every `ScanMinutes` (10), and when a player has just joined.

**Coming back.** Every pass the marked pieces are judged again, one by one: a piece whose builder (or a workbench or ward that holds it) is active again is unmarked (a worn piece is whole again), and an episode whose pieces are all unmarked is over. The owner coming back therefore saves their own pieces; the pieces of a guest whose own claim has lapsed go on with the old schedule until a ward is built over them again. What is gone is gone, and a ward has to be built again; a piece that is marked goes unless someone who held it comes back -- a ward a stranger builds over it later does not save it. A base left without its ward is judged again by what holds it now (a workbench, or its builders alone), so a ward that is switched off or destroyed hands its pieces to the shorter claims.

**Players are told** half a minute after they join (`LoginMessage`), at most four lines: the bases that are decaying (how long is left, and that staying in the game `ActiveMinutes` stops it), then those that start soon (within half the grace, at most three days). A dry run says nothing to players.

**Who is left out.** Nobody, by default. Anyone put on the list by hand (`decay exempt`) counts as always active for what they built, and so do admins if `ExemptAdmins` is set (the server learns that a character is an admin when it is online): their pieces, their ward, their workbench, so a spawn hub never decays. An exempt guest on someone else's ward list does not make that ward immortal: on a list only a guest's real activity counts.

**Console** (the server's standard input): `decay` lists who is away and for how long, and every base with its stage and where it stands; `decay away <player> <days>` counts a player as active until so many days from now -- the grace then starts to run, so a holiday of 7 days under a 14-day ward is 21 days in all --, `decay away all <days>` everybody the server knows, never shortening a longer holiday (a pause for the whole world); `decay clear` unmarks every piece, mends what is worn and forgets the episodes (the way back from a run that should not have been); `decay exempt <player>` and `decay unexempt <player>`; `decay scan` makes a pass now. A player is a name the server has seen, or the id from the list. Everything it does is written to the log and to `<world>.guard.log` (`decay: ...`). The players, their last activity, exemptions, the episodes in progress and the world they belong to are kept in `<world>.decay.txt` next to the world, every two minutes (the dates in it, in `decay` and in the log are on the decay clock, which counts only the time the server runs, not on the calendar); a file that cannot be read is left alone and decay stays off until it is fixed.

What it does not do: it does not know who owns an object that has no creator (the world's own, and what an admin spawned), and a modified game can write a creator into an object it places; it cannot bring back what it took down (take a backup, `DryRun` first); the clock of a character is only what this mod has seen since it was switched on; and a ward that is destroyed some other way than by decay (a raid that gets past the ward guard, the owner taking it down or switching it off, the limit on switched-on wards) makes its base a workbench base or a camp at once -- days away that were within a ward's grace may be past a camp's. When the decay file cannot be read the server must be restarted once it is fixed or removed (a removed file starts everyone's clock again); with the file unwritable nothing is taken down until it can be written; a file made for another world of the same name is set aside as `<world>.decay.txt.other-world` and decay starts again. Decay and the Resource Regrowth plugin work together: when the last workbench of a place goes, Regrowth treats the place as free.

## Anti-cheat

The server does not see a player's mods -- the game tells it nothing about them -- and almost every rule of the game is kept by the player's own game: what may be built where, how fast, what a seed costs, how big a stack may be, whether a character may fly. A modified game just does not keep them, and the server believes what it is told. What the server does see are the results: every object a game creates is sent to it, and it can ask of each one whether an unmodified game could have made it. With `[AntiCheat] Enabled` it does. It is server-side only, so clients stay vanilla, and it is not a scanner of anyone's computer: a cheat that leaves nothing in the world (a faster run, a free build, a stronger sword, a longer reach) it cannot see.

**How.** An object that a player's game creates reaches the server in `ZDOMan.RPC_ZDOData`. Whose it is, is the connection it came in on: the id the object carries is only the sender's claim, and a game that wants to put its work on someone else can write that someone's session into it (which is a rule of its own, `Forged`). At least 1.2 s later, when the rest of its data has come (at most 400 objects or 3 ms of work per frame), it is judged by rules taken from the game's own code (Valheim 1.0.20): `Player.PlacePiece` is the only thing that writes a creator on a piece, a piece is aimed at within 5 m of the player's eye, `Player.m_placeDelay` is 0.4 s, a character is made by the game that plays it, and the tools' build tables are in the item database.

| rule | an object (or character) of a player's game that ... | default |
|---|---|---|
| `UnknownPrefabs` | is of a kind the server's game does not have (a content mod, a build planner's placeholders) | On |
| `NotBuildable` | has a creator but is in no build table of the game and is not put down by an item's attack: a thistle, berry bush, mushroom or ore planted with the cultivator, a piece of another mod | On |
| `Forged` | carries the id of another game's session, or writes to the character object of another game | On |
| `Identity` | is a character that names another game's character as its own, or a piece from a game that has no character at all | On, warns first |
| `Creator` | has a creator that is not the character that sent it | On |
| `Distance` | is a piece placed more than `MaxDistance` (30) m from the player, both when it arrived and a moment later | On, warns first |
| `Rate` | is one of three or more plants of one player within `MinInterval` (0.25 s): rows and grids of seeds with one press (with `PaidPlanting`: unless the player's stamina paid for them) | On |
| `Items` | is a dropped item whose stack or durability is more than the item can have, or whose quality is above 6 (the game gives a dropped egg or fish the level of the creature) | On |
| `CheatMarks` | is a creature that carries the game's own mark for cheating (the console's `spawn`) | On |
| `Fields` | carries `HasFields`, per-object field overrides (invulnerable, invisible, enormous), which the game writes only for the console's `spawn` given component fields | On |
| `Level` | is a creature of a level above `MaxLevel` (10): a spawn menu lets a player type any level | On |
| `Fly` | is a character whose fly switch is on | On |
| `Flood` | is one of thousands waiting to be looked at from one player (more than the game makes in minutes: past 4,000 the rest are not looked at, and that player is acted on; when it is the whole queue that is full nobody is blamed) | On |
| `Wards` | is a piece inside a ward that is switched on, built by a player who is not its owner or on its list (the ward guard's rule) | On, warns first |
| `NoBuild` | is a piece where the game lets no one build | On, warns first |
| `Biome` | is a piece that belongs to one biome, in another (judged only where the server has the ground loaded, and only if the world generator says the same, so near a border a piece is let through) | On, warns first |
| `Dungeon` | is a piece that may not be built in a dungeon, built in one | On, warns first |
| `CheatMarksOther` | is anything else with the cheating mark (a piece built from items an admin handed out has it too) | LogOnly |
| `Cultivated` | is a crop on ground that is not cultivated (the server has the ground only near players, a moment behind) | LogOnly, warns first |
| `Burst` | is the 15th piece from a player in three seconds (the game places one in 0.4 s; a stalled connection that delivers everything at once looks the same) | LogOnly |
| `Health` | is a piece with more health than its prefab can have (skill mods make pieces tougher on purpose) | LogOnly |
| `Rotation` | is a piece tilted, or not turned in a multiple of 22.5 degrees (the free-rotation mods; a piece the physics moved, too) | LogOnly |
| `Teleport` | is a character that moved 250 m between two looks (every 2 s; looks 1 to 8 s apart count) with no portal within 20 m of either end, neither in a dungeon | LogOnly |
| `Speed` | is a character that moved faster than 30 m/s sideways in two looks in a row | LogOnly |

**What happens.** A rule that is `On` does what `Action` says: `Log` writes the finding down, `Revert` also takes the object away (the server destroys it; whatever a chest or a stand held goes with it, nothing is refunded), `Kick` also tells the player what was wrong in the middle of their screen and removes them from the server six seconds later. The rules marked "warns first" depend on the state of the server a moment after the player's game decided (a ward switched on just then, ground that was not loaded yet, a character that had just died): the first time one is broken in ten minutes the object is taken away and the player gets `WarnMessage`; breaks less than 15 seconds later are the same time (one mistake can be several objects), and the second time (`SoftStrikes`), 15 seconds or more after, they are kicked. All the others kick at the first finding. `Fly`, `Flood`, `Teleport`, `Speed`, `Burst`, `Health` and `Rotation` (and `Identity`, when it is the character that is wrong) have no object to take away: with `Action = Revert` they only write. A rule that is `LogOnly` only writes. Admins (`adminlist.txt`) are not held to any of it (`ExemptAdmins`; nothing of theirs is even queued): they have the console and the tools that bend the rules. Kick is a removal, not a ban: the player can come back, and everything that breaks a rule is taken away again; the log line names the account, so `ban` on the console keeps it out. Every finding goes to the log and `<world>.guard.log` (`AntiCheat: ...`): who, which rule, which object, where, why; the first of a kind for a player at once, the rest counted together every ten seconds, at most 120 lines a minute. An object that another game has written to since it arrived (a player who opened a chest a moment after it was placed does, as the game hands the object over) is judged as well, but nobody is warned or kicked for what is wrong with it.

**Before you switch it on.** Set `Action = Log` first: every rule only writes, and after a few days the `AntiCheat:` lines in `<world>.guard.log` show what your players' games do -- there should be none from honest ones, and what there is tells you which mods they run. Then `Revert`, then `Kick`. A mod that adds pieces or items of its own (OdinArchitect, SeedBed, WackysDatabase, MyDirtyHoe, a content pack ...) makes objects the server's game does not have: a player who runs one is kicked at the first object it makes (`UnknownPrefabs`). If the server itself has the mod, its objects are known and the rule does not fire; for a mod that only the player has, the rule would have to be `Off`. Mods that every player is meant to have, or that change the game for everyone, trip rules for everyone: set the rule to `Off` or `LogOnly` -- PlantEverything (`NotBuildable`, `UnknownPrefabs`, `Cultivated`), a shortened tool delay such as ToolTweaks' (`Rate`, `Burst`), stack-size mods (`Items`), build-restriction mods (`Wards`, `NoBuild`, `Biome`, `Dungeon`, `Distance`). Take a backup of the world before `anticheat revert`.

**Where the numbers come from.** `MinInterval` and `RatePlants` are set from the real planting times of three players of the live world: 181 and 131 plants put down by hand (never three within a quarter of a second) and 110 put down in groups by a mod (up to 36 within a quarter of a second); the two who plant by hand were not caught, the mod user was, plant by plant. The data is in [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md); a mod that shortens the pause between presses (ToolTweaks) can put honest plants closer than that.

**Paid planting.** PlantEasily, MassFarming, ComfyMassFarming and CropUtils plant a row or a grid of crops with one press. With `PaidPlanting = true` (off by default; `Rate` must not be `Off`) a group of plants that `Rate` would take is let through when the player's own game paid for it as the game does. A plant costs a seed, a little wear on the cultivator, and stamina (5 for a press, down to half with the Farming skill and to 65 % of that in a harvester's dress and hat: 1.625 at the least, times the world's stamina rate). The server sees one of the three: the stamina and the item in the hand are in the player's character object, which the game updates in every physics step in which it changed and sends with everything else. For a group it checks that the cultivator was in the hand and that the stamina went down -- the sum of its falls from the moment the first plant arrived to half a second after the last -- by `PlantStamina` (1.3) for every plant. The plants the stamina paid for stay, in the order they came; the rest are taken away and the player is kicked, as under `Rate`. Once a group is a burst it goes on while its plants arrive less than a second after each other (a hitch of the player's game does not split a grid, and what the next group pays is never counted for this one). A character whose stamina ran out inside the group is as paid as it could be (PlantEasily plants the rest of the grid without looking at the stamina); a character that another game has written to, and an admin, are not held to it. Each group is written once to `<world>.guard.log` with what the server saw, and `anticheat` counts the groups and the plants let through.

By the code of the mods as of October 2026: PlantEasily 2.3.0 charges the game's own price for each plant in the frame of the press (unless its `UseStamina` is off); MassFarming 1.13.0 charges 5 for each extra and stops when the stamina is out (unless `IgnoreStamina` is on); ComfyMassFarming 1.6.0 charges (5 plus the dress's modifier) x 1.77 and has no switch; CropUtils 2.1.1 charges a twentieth (`StaminaDiscountConfig` 20, which divides the wear too) and so pays for a few plants only until its discount is 1. What the server cannot see: why the stamina fell (sprinting or dodging at that moment is credited to the plants, at most what the character had and 15 more), the seeds and the wear of the tool (a mod with only the wear switched off passes), and what a game that forges the values writes. The first plant of a press is paid by the game itself, which pays for one to three plants at the full price, so a group of up to three with free extras cannot be told from a paid one, and over a stretch of presses a mod with free extras gets no more than two to four plants for each press the game charged.

**The stored world.** The console command `anticheat scan` looks through everything in the world with the rules that do not need a player: what no tool of the game makes (`NotBuildable`) and objects of a kind the game does not have, dropped items and chests with more in a stack than an item can have, pieces in the wrong biome or in a dungeon, plants put down in groups (and says who made it). It does not look for field overrides, the cheating mark, forged ids, pieces in wards or far from their player: those need the player. `anticheat revert` takes away only what no tool of the game makes (if `NotBuildable` is `On`) and dropped items above their maximum (if `Items` is `On`; a dropped item has no owner on it, so they go whoever dropped them), and never the contents of a chest, a piece in the wrong biome or in a dungeon (the world may have been built under another rule then), an object the game does not have, or a piece of an admin; a piece whose maker the character guard does not know is left alone unless `anticheat revert all` is said (the creator is what the player's game wrote, and without a record there is no telling an admin's from a stranger's). It ignores `Action`, it cannot be undone, and `scan` first shows exactly what `revert` would take. `anticheat` alone says what has been found since the start. On a copy of the live world this was written for, a scan of 600,000 objects takes some tens of milliseconds and found exactly one thing no tool of the game makes -- a thistle -- and no unknown object, nothing in the wrong biome, no stack above its maximum.

**The popular cheat mods.** Which mods leave what in the world, and which rule sees it, is tabulated in [docs/MODS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/MODS.md): from the READMEs of 65 popular cheat and tweak mods on Thunderstore and the decompiled code of 29 of them (October 2026) -- planting (PlantEverything, PlantEasily, CropUtils, MassFarming), build restrictions (BuildRestrictionTweaks, NoBuildRestriction), flight and teleport, spawn menus (EasySpawner, Infinity Hammer), stack sizes and item spawners, and the mods that change nothing the server can see (speed, tool wear, free pieces). The short of it: what a mod leaves as an object in the world is seen, what it changes inside the player's own game is not.

**What it cannot see, and how it can be got round.** Anything the game does inside the player's own game -- how fast a character runs, how hard a weapon hits, what a tool or a recipe costs, how much can be carried, how long a pickaxe reaches (the stamina of a character is in the character object, and `PaidPlanting` reads it for planting, nothing else). A cheat that is careful -- single pieces, within reach, on cultivated ground, with a mod that only turns them -- cannot be told from a player who does not use it. A player's inventory is not on the server, so nothing here can say that a player planted seeds they did not have (`PaidPlanting` checks the stamina a planting takes, not the seeds). An object is judged once, 1.2 s after it arrived: a game that sends an innocent object and rewrites it later is not seen again, and an object whose data never comes is counted and dropped. A game decides what character it calls its own; the rules that read the character (`Fly`, `Teleport`, `Speed`, `Creator`, `Distance`, `Identity`) trust it as far as it is checked (it must be made by the connection's own game), and a game that has none is held to that for pieces. The server accepts a write to any object from any game that sends a newer revision -- the vanilla server does -- so a modified game can still change another player's objects; the anti-cheat acts on a game that writes to a character, and leaves a character that has been written to out of its rules. Thousands of objects at once are not all looked at (`Flood`). And a kick is not a ban.

Tested on a copy of the live world with 30 simulated players sending each object through the server's own entry point for object data, as a client does -- a fair builder, an admin and one cheater of each kind, the rules that only log, the teleport and the run, the scan and revert of the stored world, plus the real planting times of the three players above -- and a real Valheim client joining, walking and jumping against the same server (46 to 65 objects of its game judged in three runs, nothing found; its character object carries the stamina: a jump takes 12 of it at once and the regeneration starts a second later): every object that broke a rule was taken away and the player kicked after six seconds, nothing else was touched, nobody who played fair was kicked. `PaidPlanting` was tested with 29 simulated players planting groups of crops (paid up front or in the frame of the press, partly paid, a quarter paid, a hammer in the hand, no stamina in the character, the stamina late, a hole in the planting times, alternating groups, a character written to by another game, an admin, the world taking no stamina, the setting switched off): 29 of 29 checks. What was not tested: a real client placing pieces and planting, and the mods themselves. Everything that was tried is listed in [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md).

## Location icons

Some locations carry an icon for the map: the traders (Haldor, Hildir, the Bog Witch), Hildir's three
dungeons, the Deep North boss room and the ancient upgrade station; the start temple's is always shown.
Vanilla sends every player the icon of each of them as soon as the location is placed -- when its zone
is generated, which happens whenever anyone passes within a few zones, usually without anyone seeing it
-- and the map draws it whether the player has explored there or not (`ZoneSystem.SendLocationIcons`
goes to everyone; `Minimap` checks no fog for it). So the first player to pass near Haldor's camp gives
everyone his position.

With `[LocationIcons] Enabled`, the server sends each player only the icons of the locations they have
been near themselves, also from inside a dungeon above or below, and remembers them per character
(`<world>.icons.txt`, one line per character and location). How near is `DiscoverRadius`: 0 (the
default) is as near as the game itself shows an icon -- the location's zone is one the game generates
around that player (`ZoneSystem.CreateGhostZones`: the simulation distance, near + far zones of the
player's zone, a square unless the distance is not "classic"; 4 zones by default, about 256-320 m
straight ahead, more on a diagonal). That is the moment vanilla shows the icon the first time, to
everyone; here the player who comes that near gets it, and every later player when they come as near.
A number above 0 is metres. A player who finds one gets its icon within two seconds. Icons the game always shows stay for everyone, and players'
own pins are not touched. Tested with two simulated players on a copy of the live world: far away both
were sent only the start temple; the one taken to the ancient upgrade station got its icon too, the
other did not; after a restart the first one got both icons at once.

## Fire control

How fire spreads, from the game's own code and data (Valheim 1.0.16). Outside the Ashlands all of it needs the Fire world modifier (`-setkey fire`); in the Ashlands fire always works. Two ways a fire starts, both only from something lit:

- **Sparks.** A bonfire or campfire throws one every 5 s (50 % chance each time), a fire burning on a piece every 2 s and one on the ground every 5 s (30 %), in a random upward-biased direction, under gravity alone. On a burnable piece that is not wet, a tree or a log a spark always starts a fire; on uncleared grass, outside the Mountains and Deep North and not in rain, with a 32 % chance.
- **Direct ignition.** A lit fireplace sets alight whatever burnable is inside a small capsule around its flame, every 5 to 10 seconds.

Every fire carries a generation count: it throws sparks only while the count is above 0, and what its sparks light gets one less. What a fireplace lights directly gets the fireplace's own count. How far each source reaches -- spark landing distances simulated with the game's own spark code, 400 000 sparks each, on flat ground:

| Source | Sets alight directly | Its own sparks land at most | Spark flights the chain can make | Farthest on flat ground |
|---|---|---|---|---|
| Bonfire | 1.7 m around, up to 6.2 m above | 2.9 m (half within 2.3 m) | 4 | about 17 m |
| Campfire | 0.45 m, up to 1.6 m high | 1.0 m | 2 | about 8 m |
| Hearth | 1.4 m along the world's east-west axis, 0.8 m across, up to 1.7 m high (the capsule does not turn with the hearth) | -- | 4 | about 17 m |
| Iron fire pit | 0.7 m | -- | 4 | about 16 m |
| Floor and ceiling braziers | 0.3 m; a floor brazier reaches the floor under it | -- | 1 | about 4 m |
| Standing torches (iron, wood, blue, green) | 0.1 m around the flame | -- | 1 | about 4 m |
| Resin candle | 0.1 m, 0.25 % every 25 s | -- | 1 | about 4 m |
| Sconce, lanterns, jack-o-turnip, snow lantern, NPC fire pits, forges, kilns, smelters | never | -- | -- | -- |
| Fire arrow | -- | 3 sparks on impact, 2.9 m | 4 | about 15 m |
| Staff of Embers fireball | -- | 6 sparks on impact, 5.0 m | 4 | about 17 m |

A fire's sparks fly at 5 m/s and land up to 3.9 m away on flat ground, farther from a height: 5.1 m from a fire 2 m up, 6.2 m from 4 m, 7.8 m from 8 m, 9.1 m from 12 m. "Farthest" adds up the longest flight of every generation, so it is a ceiling: each one has to land on something burnable, in the right direction, before its fire goes out. Ashlands meteors and the summoned troll throw sparks too.

A fire burns for at most 30 seconds (the game's own timer on every fire), and a spark that has not landed within 3 s is gone; in its 30 seconds a fire on a piece throws about four or five sparks, one on the ground about two. It goes out sooner when rain falls on it with no roof above, when what it burns on is destroyed, when a smoke bomb goes off within 3 m, or when it chokes on smoke: under a roof, more than 7 puffs within 2.9 m of a point 3.2 m above it. In a house that is the smoke of a hearth or fire pit; a fire's own smoke source stops puffing as soon as its smoke has nowhere to go -- every smoke source counts itself blocked with smoke within 0.75 m of it (0.4 m for braziers), and a fireplace whose smoke source has been blocked for 4 s goes out until the air clears (see `[Fixes] ServerSmoke`).

On a dedicated server the weather that puts fires out, and makes pieces too wet to catch, is the Meadows' wherever the fire is: the server has no camera to follow, so the game falls back to the Meadows for it, in the Ashlands and the Swamp as much as anywhere.

Vanilla has no limit in metres; `[FireControl]` adds one. Every spark and fire remembers the source its chain began at (a lit fireplace, or where an arrow, fireball or meteor came down; kept in its ZDO, so it survives a restart), and with `MaxRadius` set, a fire that would start farther than that from its source does not start. `MaxSpread` caps every generation count. `FireplaceIgnition = false` stops direct ignition entirely: a hearth, brazier or torch then cannot start a fire at all, while a bonfire or campfire still throws sparks.

`OnlyProjectiles = true` goes further: only a projectile starts a fire. Every spark and fire also remembers whether its chain began at a projectile (in its ZDO), and only those throw sparks; no fireplace sets anything alight or throws a spark of its own. Which attacks carry fire was read from the game's data: a fire arrow (3 sparks where it lands), a Staff of Embers fireball (6), Ashlands meteors and lava rocks, and the sparks of the Ashlands sky. The fireballs of surtlings, fuling shamans and the Charred mages, and Fader's fire breath and wall of fire, deal fire damage but never set anything alight in the game.

**Who simulates a fire.** Sparks fly, and fireplaces ignite, only on the machine that owns the object, and the server only takes over what nobody owns or what its owner has left. What a player builds or sets alight is created and owned by their own game: without more, a hearth a player has just built, or the fire their arrow started, would burn by the game's rules on that player's computer until they left the area or the server restarted -- in 1.13 and 1.14 none of the settings above reached them. Now the server takes every fire source (every fire, and every fireplace that ignites or throws sparks: 15 kinds) the moment a player's game reports it, whether new, an update from a game that has not heard the news yet, or a fireplace a player merely claimed (pressing E on one nobody owns). That game stops simulating it long before its first spark (2-5 s) or direct ignition (5 s). A fire taken over this way that has no record of its chain was lit by a projectile on that player's computer and counts as one, its own position as the source -- up to 2.9 m from where an arrow, 5.0 m from where a fireball came down, which `MaxRadius` therefore allows on top. A fire that appears within 8 m of a creature that throws sparks (the summoned troll) is not from a projectile, and with `OnlyProjectiles` it is put out. The server's copy of a fire finds the piece, tree or log it burns on, so it goes out when that is gone, as on the game that lit it. What stays on the player's computer is their own arrow or fireball and the sparks it throws where it lands.

## Hosting notes

- **After a game update**, look for `Vanilla ... changed` warnings in the log, and keep world backups.
- **On a shared host with a chiplet CPU** (AMD EPYC, Ryzen), `CpuAffinity = auto` keeps the server on one die, the one least busy when it starts; other servers on the host are not pinned and may still use it. Each server measures for itself, so two started in the same second can pick the same die.
- Running under AMP (CubeCoders)? Settings and pitfalls (Sleep mode, stop without save, console commands) are in [MistrCech/amp-valheim-bepinex](https://github.com/MistrCech/amp-valheim-bepinex).

## Caveats

- Only runs on dedicated servers.
- Uses considerably more server resources than vanilla; a weak CPU or little RAM may make play worse, not better.
- Disable the mod when using the `optterrain` command.
- It does not prevent cheating or any kind of client manipulation.
- Game updates can break it in unexpected ways; back up characters and worlds before updating.

## How it works

Ordinarily, to keep server resource usage low, the Valheim server hands off simulation of an area to the first client that enters it. This mod makes terrain, monsters and other objects that are normally created and owned by clients be created on — and thus owned and simulated by — the server, around every connected player.

#### For mod developers - compatibility

This mod keeps the plugin GUID of Serverside Simulations, `MVP.Valheim_Serverside_Simulations`, so existing checks for it keep working.

If your mod changes the simulation or behaviour of the world, it has to be able to run on the dedicated server:
- `Player.m_localPlayer` is always `null` on a dedicated server; check for it.
- On a dedicated server, `ZNet.instance.GetReferencePosition()` returns a position outside of the world, unrelated to any player.
- Graphics or HUD code should be behind a `ZNet.instance.IsDedicated()` check if it can run on the server.

## Credits

- **Serverside Simulations** by ddormer and its contributors: this is a fork, and the simulation itself -- the zones around players loaded and simulated on the server, the configuration and the feature framework -- is their work. The original is at [ddormer/valheim-serverside](https://github.com/ddormer/valheim-serverside) and on Thunderstore as `mvp/Serverside_Simulations`.
- **@mreastman**: the port to Valheim 1.0 ([ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118)) this fork builds on.
- **@Merl**: found why the frame rate patch broke on Valheim 1.0.14 ([#1](https://github.com/MistrCech/valheim-serverside/pull/1)); the refresh rate idea is in 1.11.0.
- **BetterNetworking**: the server-side network limits are its idea.

## License

Our own work is MIT, see [LICENSE](https://github.com/MistrCech/valheim-serverside/blob/main/LICENSE). A few files carry code from Serverside Simulations by ddormer, which has no licence; that code is not covered, see [NOTICE.md](https://github.com/MistrCech/valheim-serverside/blob/main/NOTICE.md).

## Building

Create `src/Environment.props` pointing at a Valheim dedicated server install that has BepInEx:

```
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Needs to be your path to the base Valheim dedicated server folder -->
    <VALHEIM_DEDI_INSTALL>E:\SteamLibrary\steamapps\common\Valheim dedicated server</VALHEIM_DEDI_INSTALL>
  </PropertyGroup>
</Project>
```

Then, from the repository root (Windows or Linux, tested with .NET SDK 10):

```
dotnet build src/Valheim_Serverside/Serverside_Simulations.csproj -c Release -p:SolutionDir=<repository root>/
```

The DLL ends up in `bin/Release/`. `SolutionDir` is needed when building the project on its own; building `Valheim_Serverside.sln` sets it.
