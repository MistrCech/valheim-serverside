# Sarkastic.gg Dedicated Simulation

![version: 1.33.0](https://img.shields.io/badge/version-1.33.0-3b6e8f?style=flat-square)  ![players: vanilla clients](https://img.shields.io/badge/players-vanilla%20clients-4f7a4a?style=flat-square)  ![Valheim: 1.0.20](https://img.shields.io/badge/Valheim-1.0.20-7a5c2e?style=flat-square)  ![BepInEx: 5.4.2351](https://img.shields.io/badge/BepInEx-5.4.2351-5b5f66?style=flat-square)  ![license: MIT (own code)](https://img.shields.io/badge/license-MIT%20%28own%20code%29-5b5f66?style=flat-square)

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
- **Admin commands on the server console:** `tp <player|all> <x> <z>` / `<x> <y> <z>` / `start` / `to <player>` (the game's own teleport; the console says when they are there), `pos`, `heal <player>`, `spawn <prefab> [amount] [level] <player>`, `give <item> <amount> <player>`, `broadcast <text>` (`\n` for a line break, `\u010d` for a letter by its code), `event <name> <player>`, `players`, `characters`, `allow <name>`, `save`, `stop`, typed into the panel the server runs in (AMP), and anything else goes to the game's own console (`kick`, `ban`, `banned`, `stopevent`, `setkey`, `removekey`, and after `devcommands` the cheats that need no player: `skiptime`, `listkeys`, `resetkeys` ...). A vanilla dedicated server never reads its console, and Valheim 1.0 does not let a player on a dedicated server use `spawn` from the game console, admin or not; `god`, `fly` and `ghost` live wholly in the player's game and have no server version.
- **Smoother server frames:** world updates reach every player at a steady interval however many are online, one slow frame no longer makes the next one slow through physics catch-up, and new zones are generated one per tick instead of one per exploring player. The guards find the wards, portals, guardian stones and pieces they need in an index of the world kept up to date, instead of looking through all of its objects again and again. A periodic log shows frame times and what they are spent on.
- **`save` and `stop` console commands** for server panels that write to standard input.
- **Character guard** (off by default): a check on the characters players join with, server-side only, so clients stay vanilla. A character this world has not seen must be fresh, and a known one that comes back changed was played elsewhere -- see below.
- **Item ledger** (off by default): an account per character of the items that lock progress, what it got here and what it put back into the world; items that came from another world are logged, or taken away -- see below.
- **Night spawn guard** (off by default): the spawns that come with boss progress (Greydwarfs, Draugr, Skeletons, Goblins, Seekers, Charred, ...) held back around a player who has not got that far themselves, even where the world as a whole has moved past it -- see below.
- **Guardian stones** (off by default): a boss's trophy on its guardian stone, and so its power, only for players who have beaten that boss themselves; to everyone else's game the stone is empty -- see below.
- **Fire control** (off by default): a limit in metres on how far fire spreads from its source, a cap on spark generations, fireplaces that never set what touches them alight, or fire from projectiles only, so a player's own build never sets it on fire -- with the reach of every fire source in the game worked out, and every fire and fireplace simulated by the server so the rules hold -- see below.
- **Death log** (off by default): one JSON line per player death in `<world>.deaths.log`, with the cause the server can tell -- a player's or a creature's hit, a falling tree or ballista, a fall, drowning, the edge of the world, burning, poison, cold, lava -- for a bot to post -- see below.
- **Portal lock** (off by default): a pair of portals used only by those it belongs to -- the builders, or the owner and the players on the ward over it; a name starting with `*` is open to everyone. Two portals are paired only if both builders may use the pair, so a portal built with the name of someone's unpaired one no longer walks anyone into their base; no public portal near the spawn, a boss altar or a trader; and with `Protect`, a portal a ward holds, and the far end of its pair, can be hit or taken down only by the ward's list -- see below.
- **Ward guard** (off by default): the game's ward (guard stone) enforced by the server. In the game every check of a ward is made in the acting player's own game and nothing stops a weapon from damaging a warded build (the ward only flashes), nor a player from using a warded smelter, kiln, fire pit, bed or crop. The server judges what players' games send it, by the game's own rule: where an enabled ward covers the place, the ward's owner and the players on its list may act, nobody else -- hitting, taking down and repairing builds, opening chests and doors, taking the product out of smelters, switching fires off, claiming beds, picking crops, and more. A packet whose sender is not its connection is dropped, and so is a ward switched off or edited in another player's name; and no player may keep more than a few wards switched on (`MaxActiveWards`, 2). With `OutsideWards`, builds are protected where no ward is too: only their builder and the builder's friends may break or use them, and someone else's ward gives no rights over them; with `Tombstones`, only the dead player and their friends may open a tombstone -- see below.
- **Base decay** (off by default): what players built is taken down by the server once the players it belongs to have stayed away long enough, so abandoned bases do not fill the world for ever. How long depends on the best protection a piece has: a ward that is switched on (14 days away, then 10 days over which the base goes), a workbench within reach (7 + 10), or nothing (3 + 4). The ward goes first, then roofs before walls, chests and beds, the workbench last; a player who comes back stops what is left of it; players are told at login, three days ahead and while it runs. A dry run (log only) first -- see below.
- **Anti-cheat** (off by default): the server looks at every object a player's game creates and takes away -- and by default kicks the player for -- what the unmodified game cannot make: a thistle, berry bush or ore planted with the cultivator, crops put down in groups faster than the game plants (unless the player's stamina paid for them: `PaidPlanting`), a piece placed in someone else's ward, far from the player or where nothing may be built, a stack larger than its item can be, the field overrides of the build-anything admin hammers, a character that flies. It can only see what a cheat leaves in the world; below is what 65 popular cheat and tweak mods leave -- see below.
- **RPC census** (off by default): every call a player's game sends that the game does not have -- a mod's -- noted with the mod's name where known, in `<world>.rpcs.txt`: which mods your players run. Nothing is blocked.
- **Location icons** (off by default): a trader's (Haldor, Hildir, the Bog Witch) and other locations' map icons only for players who have found that place themselves; vanilla shows them to everyone the moment anyone's presence has generated the zone -- see below.
- **Web map** (off by default): a feed of files for a map of the world, which you serve and draw yourself -- who is online and where (by default only the players who show their position), if you ask for them the portals open to everyone and how many pieces players have built in each 512 m square, every location, and the part of the map each player has been through (made again on the server from where they walk). Nothing is sent anywhere, and no map or web page comes with it.
- **Dvergr trade** (off by default): the dvergr of the Mistlands are friends, and their camps hold what a player needs. Next to each dvergr ward the server puts up a chest and a sign; what a player puts into the chest and closes -- seeker trophies, say -- is weighed against a list of offers, the price taken and the goods (black marble, soft tissue, black cores ...) left in its place, or the camp's ward switched off for some hours and its angry dvergr calmed, so what lies in the camp can be taken without a fight. Vanilla clients; nothing to install -- see below.
- **Processor placement** (off by default): the server pinned to one chiplet of an AMD EPYC or Ryzen (a die with its own L3 cache), the least busy one at startup, or to processors you name, and a higher process priority.
- **More players, and a public server with no password** (both off by default): `MaxPlayers` lets more than the game's 10 players in at once and tells Steam's server list the same number, and `PublicWithoutPassword` lets a public server start with an empty password, listed with no lock. Only the server decides either, so clients stay vanilla. Tests in [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md#server-access).
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
| `[Server] MaxPlayers` | 10 | How many players may be in the game at once, 1 to 64. The game lets in 10 and tells the next one the server is full; this number takes the place of that 10, and of the 10 the server tells Steam's server list, which a vanilla client shows as players / limit. Each player is more for the server to simulate and send. Crossplay (PlayFab) lobbies keep the game's 10. At 10 the game's own check is left as it is. Needs a restart. |
| `[Server] PublicWithoutPassword` | false | Let a public server (`-public 1`) start with an empty password (`-password ""`): it is listed with no lock and anyone may join. The game will not start a public server without a password of at least 5 characters that is not part of the world's name or seed (a server that is not public may have none, but it is not listed either). A password that is given still has to meet the game's rules. On Windows the game's player leaves an empty argument out, so `-password ""` used to make the next argument the password (`-public`, the way AMP writes the line); with this on, the server reads its command line as Windows split it. Needs a restart. |
| `[Server] MessageLineLength` | 50 | Longest line, in characters, of a message in the middle of a player's screen (anti-cheat, wards, portals, the welcome, broadcasts); the top left corner, in letters half as big, takes twice as many. A vanilla client never wraps them, so a longer line ran off both sides of the screen. Broken at spaces; 0 = one line as before. Needs a restart. |
| `[Server] MessageSecondsPerLine` | 3 | How long a message in the middle of a player's screen is held, for each line of it (at most 30 seconds in all). The game fades such a message out over 4 seconds from the moment it arrives, so one of several lines was half gone before it was read; the server sends it again every second for this long, so it stays as bright as at first. Each repeat is one more entry in the player's message log (Compendium, Logs), which keeps the last 50. A kick that follows a message (anti-cheat, character guard) waits until it has been held. 0 = shown once, as the game does. |
| `[Server] UnityJobWorkers` | 8 | Upper limit on Unity job worker threads (Unity: one per CPU core). Only ever lowers the count; 0 leaves Unity's default. |
| `[Server] ConsoleCommands` | true | Read commands from standard input: `save`, `stop` (saves first), `players`, `give <item> <amount> <player>` (drops the items in front of that player; the name may be a unique beginning), `broadcast <text>` (a message in the middle of every player's screen; in the text `\n` is a line break, `\u010d` the letter with that code, `\\` a backslash), `event <name> <player>` (a raid at that player; `events` lists them), `tp <player|all> <x> <z>` (on top of whatever stands there, about 15 s later) / `<x> <y> <z>` / `start` / `to <player>` (the game's own teleport; the console says when they are there, or that their game did not move them), `pos [player|all]`, `heal <player|all> [amount]` (health only), `spawn <prefab> [amount] [level] <player>` (3 m in front of that player, marked cheated as the game's own spawn), `help`; anything else goes to the game's own console. In AMP this is its console, see the AMP chapter. |
| `[Server] MaxGiveAmount` | 1000 | Most items one `give` drops. |
| `[Server] MaxSpawnAmount` | 20 | Most creatures or objects one `spawn` makes. |
| `[Server] ConsoleInputCodePage` | 852 | How a console line that is not valid UTF-8 is read (UTF-8 is always tried first): 852 Central European DOS, 1250 Central European, 1252 Western, 65001 UTF-8 only. AMP on Windows writes its OEM code page, 850 on a Western one, whose á é í ó ú ý (and capitals) have the same bytes in 852; the letters 850 lacks (č ď ě ň ř š ť ů ž) are plain ones by the time the line arrives, so in `broadcast` write them as `\u010d`. The log shows the bytes the first time it matters. |
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
| `[CharacterGuard] WelcomeTitle` / `WelcomeLines` | `Welcome` / empty | The greeting in the middle of the screen, then the lines (separated by `\|`, for example the rules of the server) in the top left corner. |
| `[ItemLedger] Mode` | Off | `Off`, `LogOnly` or `On`: keep item accounts per character (see Item ledger below); `On` also takes away items that came from another world. |
| `[ItemLedger] Items` | auto | `auto`: the items that lock progress, from the game's own data (see below). Or item prefab names, comma separated. Items traders sell are always left out. |
| `[ItemLedger] ExtraItems` / `ExcludeItems` | (empty) | Item prefab names to add to the list, or never to keep accounts of. |
| `[ItemLedger] LogAllMovements` | false | Also log every movement of a tracked item (and every craft the ledger counts), not only what cannot be accounted for. |
| `[ItemLedger] ExemptAdmins` | true | Admins get no account and are never checked. |
| `[ItemLedger] GraceHours` | 168 | For this long after the ledger first runs on a world, no character counts as new: the players who are already around come back with what they had. |
| `[ItemLedger] KickMinutes` | 0 | A character with an exact account that wears or drops a tracked item it never got here within this many minutes after its account was opened is kicked (needs `[AntiCheat] Enabled`). 0: nobody is kicked. |
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
| `[PortalLock] Protect` | false | Who may hit a portal or take it down with the hammer: where a ward holds it, the ward's owner and list; where none does but one holds the other end of its pair, that ward's list; where no ward holds either end, anyone. Admins always. |
| `[PortalLock] ProtectMessage` | (English text) | Shown to a player whose hit or removal of a protected portal is refused, at most once a quarter of an hour per portal. |
| `[WardGuard] Enabled` | false | Make the server enforce the ward (see Ward guard below). Needs a restart to switch on; switching it off takes effect at once. |
| `[WardGuard] Structures` | On | Hitting, taking down with the hammer and repairing a player's build (and the ward itself) inside an enabled ward by a player who is not on its list; a hit with no attacker or by a creature the sender's game owns is not a player's. On: dropped, the ward flashes and the player is told. LogOnly: only written to the guard log. Off: nothing. |
| `[WardGuard] Interactions` | On | The same for opening chests and doors, stands, fermenters, beehives, sap collectors, incinerators, the map table, and what the game does not guard: taking the product out of a smelter, kiln or cooking station, switching a fireplace off, claiming a bed, picking planted crops. Putting something in, and traps, are left alone. |
| `[WardGuard] Spoofing` | On | Drop an RPC whose sender id is not its connection, and a ward's `ToggleEnabled` / `TogglePermitted` for a player id other than the sender's own. Admins are not exempt. |
| `[WardGuard] DestroyRequests` | LogOnly | A request to destroy an object that another game or the server owns, which the game's own client never sends: LogOnly counts and logs them, On takes them out of the request. Leave it in LogOnly until the log shows an honest client sends none. |
| `[WardGuard] ExemptAdmins` | true | Admins are not held to any ward. |
| `[WardGuard] Flash` | true | The ward flashes (its own shield effect) when it refuses something, at most every 3 seconds. |
| `[WardGuard] Message` | (English text) | Shown to a player the ward refused, at most every 15 seconds. Empty: no message. |
| `[WardGuard] OutsideWards` | Off | Where no enabled ward covers the place: hitting a player's build, taking it down with the hammer and the uses `Interactions` covers (chests, doors, stands, smelters, fires, beds, crops) by anyone but its builder, the players on the list of one of the builder's wards (on or not) and admins. A ward the builder is not on gives no rights over the build either, so its owner cannot take a neighbour's pieces down. On drops it, LogOnly only writes it to the guard log. Repairing and putting things in stay open; fire, falling objects and creatures are let through. |
| `[WardGuard] OutsideWardsMessage` | (English text) | Shown to a player refused under `OutsideWards`, at most every 15 seconds. Empty: no message. |
| `[WardGuard] OwnPieces` | false | A player may always hit, take down, repair and use what they built, also inside a ward they are not on. |
| `[WardGuard] Tombstones` | Off | Opening a tombstone, taking everything from it or stacking into it by anyone but the dead player, the players on the list of one of their wards (on or not) and admins. On answers the player no, LogOnly only writes it to the guard log. |
| `[WardGuard] TombstonesMessage` | (English text) | Shown to a player refused under `Tombstones`, at most every 15 seconds. Empty: no message. |
| `[WardGuard] DestroyLog` | false | Write a line to the guard log when a player's build is destroyed by someone other than its builder: who (the player, or the creature and the player it was chasing, or the kind of hit and the game that sent it), whose piece, which, where. At most one line per destroyer and builder a minute, with a count and up to eight prefab names. It also logs a request to destroy a piece another game or the server owns, whatever `DestroyRequests` says. Nothing is stopped. |
| `[WardGuard] Claims` | Off | A player's game taking over (becoming the owner of) a player's build it may not act on by the rules above, which a game does in order to destroy or change it. On: the ownership stays where it is and the data in the packet is dropped; LogOnly: written to the guard log. Taking over an object nobody owns, a new object and your own is left alone. |
| `[WardGuard] IndirectHits` | Off | Today a hit with no attacker (a catapult, fire, a falling object) or by a creature the sender's game owns is let through as not a player's own. On: it counts as the sender's own hit and is refused where the rules above would refuse the sender's hand; LogOnly: written to the guard log with the kind of hit, and let through. |
| `[WardGuard] TamedAnimals` | Off | Tamed animals inside an enabled ward whose list the player is not on: hitting, commanding, naming, saddling and riding them are refused (On) or logged (LogOnly). Admins are exempt. |
| `[WardGuard] Vehicles` | Off | Ships and carts a player built, anywhere: only the builder, the players on the list of one of the builder's wards and admins may take the helm, steer, own a cart or open a ship's chest. On refuses (the helm and the cart answer no; a catapult's loading, firing and legs are dropped) and the server hands such a ship to itself instead of to the player; LogOnly writes it to the guard log. A shot sent to a catapult the server owns (the player's game was refused ownership, or was slow) would be fired by the server itself, out of the guard's sight, so it is dropped too, for anyone but an admin, whenever `Vehicles` or `IndirectHits` is not Off (the stricter of the two decides). |
| `[WardGuard] Terrain` | Off | Digging, levelling, raising and paving the ground with the hoe, the pickaxe and the cultivator: refused (On, the operation is dropped) or logged (LogOnly) when it reaches into an enabled ward whose list the player is not on (needs `Structures`), or comes within `TerrainMargin` of a build of a player whose side the player is not on (needs `OutsideWards`). The server keeps the terrain compilers: one that a player's game creates (the first dig in a zone nobody dug) is judged on its last operation and on every place where its data changes the height of the ground, and destroyed if it would be refused or if the zone already has one; a kept one is taken over by the server so the player's next operation is judged; a game that takes a compiler back, or sends the data of one it does not own, is refused. The builder (with `OwnPieces`) and admins are exempt. |
| `[WardGuard] TerrainMargin` | 2 | Metres beyond the tool's own radius within which a build outside the wards keeps the ground around it safe under `Terrain`. 0 to 20. |
| `[WardGuard] MaxActiveWards` | 2 | How many wards one player may have switched on at once; a player who has that many and tries to switch on another is refused and told (switching one off or taking it down makes room). Wards already on stay on if the limit is lowered. Admins are not held to it. 0: no limit. |
| `[WardGuard] MaxActiveWardsMessage` | (English text) | Shown to the player so refused; `{0}` is the limit. |
| `[Decay] Mode` | Off | `Off`, `DryRun` (tracks players and writes to the log what would be taken down, changes nothing) or `On`: take down what players built once the players it belongs to have stayed away (see Base decay below). |
| `[Decay] WardGraceDays` / `WardFadeDays` | 14 / 10 | A base covered by a ward that is switched on starts to decay this many days after the last of its owner and the players on its list was active; its pieces then go over the fade. |
| `[Decay] WorkbenchGraceDays` / `WorkbenchFadeDays` | 7 / 10 | The same for what has no ward but stands within `WorkbenchRadius` of a workbench; kept by the workbench's builder. |
| `[Decay] WorkbenchRadius` | 20 | How far, in metres sideways, a workbench protects the pieces around it. |
| `[Decay] NoneGraceDays` / `NoneFadeDays` | 3 / 4 | The same for a piece with neither; kept by its builder. |
| `[Decay] ActiveMinutes` | 10 | A player counts as active when they have been online this long in one go. |
| `[Decay] ScanMinutes` | 10 | How often decay goes over what players built (some 20 ms of work on a world of 891,000 objects, 80 ms the first pass after a start, which reads every object; the tick spreads it over frames in slices of some 4 ms) to start decay, take down what is due and update what players are told. |
| `[Decay] MaxRemovals` | 200 | Most pieces taken down in one pass. |
| `[Decay] ExemptAdmins` | false | Admins' characters count as always active (the server learns that a character is an admin when it is online). Off by default: nobody is exempt unless named with `decay exempt`. |
| `[Decay] LoginMessage` | true | Tell a player half a minute after they join about their bases that are decaying or will start to in three days. |
| `[AntiCheat] Enabled` | false | Look at what players' games create and take away or kick for what the game cannot make (see Anti-cheat below). Needs a restart to switch on. |
| `[AntiCheat] Action` | Kick | What happens when a rule that is On is broken: `Log` (write it down), `Revert` (also take the object away), `Kick` (also tell the player in the middle of the screen and remove them six seconds later; they can come back and what breaks a rule is taken away again). |
| `[AntiCheat] ExemptAdmins` | true | Admins are not held to the rules. |
| `[AntiCheat] Message` | (English text) | Shown to a player about to be kicked; `{0}` is what they broke. |
| `[AntiCheat] SoftStrikes` / `WarnMessage` | 2 / (English text) | `Distance`, `Wards`, `NoBuild`, `Biome`, `Dungeon`, `Cultivated`, `Identity`, `Unhurt` and `Stamina` depend on the state of the server a moment after the player's game decided: the first time one is broken in ten minutes the object is taken away and the player gets `WarnMessage`, the `SoftStrikes`-th time (15 s or more later) they are kicked. 1: kicked at once. |
| `[AntiCheat] UnknownPrefabs`, `NotBuildable`, `Forged`, `Identity`, `Creator`, `Distance`, `Rate`, `Items`, `CheatMarks`, `Fly`, `Fields`, `Flood`, `Level`, `Wards`, `NoBuild`, `Biome`, `Dungeon` | On | One rule each (see below): `Off`, `LogOnly` (only written to `<world>.guard.log`) or `On` (does what `Action` says). |
| `[AntiCheat] CheatMarksOther`, `Cultivated`, `Burst`, `Health`, `Rotation`, `Teleport`, `Speed` | LogOnly | The rules that can be wrong for an honest player or that break nothing but the look: a trace in the log, nothing more, until set to On. |
| `[AntiCheat] MaxLevel` | 10 | The highest creature level let through (the game makes up to three stars, level 4). |
| `[AntiCheat] MaxQuality` | 0 | The highest quality of a dropped item let through; 0 = not checked. The upgrade station of 1.0 raises quality past the recipe's top with no ceiling of its own, so leave this at 0 unless you know a number your players cannot reach. |
| `[AntiCheat] MaxDistance` | 30 | Metres from the player at which a piece is "far" (the game places within 5 m of the eye, a few more for large pieces). 0: no limit. |
| `[AntiCheat] MinInterval` / `RatePlants` | 0.25 / 3 | `RatePlants` or more plants of one player within `MinInterval` seconds are more than the game plants (one in 0.4 s). |
| `[AntiCheat] PaidPlanting` / `PlantStamina` | false / 1.3 | `true`: a group of plants that `Rate` would take is let through when the cultivator was in the player's hand and the stamina of their character went down by `PlantStamina` for each plant of it (see "Paid planting" below); the plants it did not pay for are taken away and `Rate` acts as before. The game takes 1.625 at the least for a plant, times the world's stamina rate; a mod that charges less than `PlantStamina` does not pay. Needs a restart. |
| `[AntiCheat] BurstPieces` | 15 | Pieces (not plants) from one player in three seconds that make a burst. |
| `[AntiCheat] TeleportMetres` / `SpeedLimit` | 250 / 30 | A move of this many metres between two looks (every 2 s) with no portal at either end; metres per second sideways in two looks in a row. |
| `[AntiCheat] Unhurt`, `Stamina`, `Damage`, `Skills` | Off | The rules of a player's own game that the server sees only by what the game sends (see "The combat rules" below): hits that do not hurt, attacks that cost no stamina, a hit stronger than anything in the game, a weapon skill that cannot have been earned here. `Off`, `LogOnly` or `On`. There is no object to take away: they log or (`Action = Kick`) kick, `Damage` On also drops the hit. Admins are exempt. |
| `[AntiCheat] UnhurtHits` | 8 | Hits in a row that should have taken a real part of the health and did not (3 to 100). |
| `[AntiCheat] StaminaAttacks` | 12 | Melee attacks in 20 seconds, with a weapon that costs 4 stamina or more, in which the stamina does not move, neither at the hits nor in the samples the server takes every half second between them (an honest character whose stamina is back at the top at every swing shows "top minus cost" between two) (4 to 200). |
| `[AntiCheat] MaxDamage` | 0 | The most damage one hit may deal, all types together. 0 (automatic): the server works out from the game's own items and status effects the strongest hit a weapon a player can make sends, every multiplier at its largest (quality, world level, the special attack, the strongest damage status effect), and allows 1.5 times that (3006 on game 1.0.20 at world level 0, logged at start as "Damage bound"). The butcher knife, which works only on tamed animals, has a bound of its own (9000) that counts only against a tamed animal with the knife in hand. A positive value is used as it is (a world with higher damage settings, or a mod that makes weapons stronger); the knife keeps its own bound on a tamed animal. The cheats this is for deal many times more. |
| `[AntiCheat] SkillGainFactor` / `SkillAllowance` | 3 / 80 | A skill level is let through when the experience it costs from the first level seen is at most `SkillGainFactor` times the experience of the hits the server has counted plus `SkillAllowance` (reaching level 10 from 0 costs about 76, level 20 about 330). |
| `[DeathLog] Enabled` | false | Write every player's death to `<world>.deaths.log` (see Death log below). Needs a restart. |
| `[RpcCensus] Enabled` | false | Note every RPC a player's game sends that the game does not have (see RPC census below). Nothing is blocked or changed. Needs a restart. |
| `[LocationIcons] Enabled` | false | Show a location's map icon (Haldor, Hildir, the Bog Witch, Hildir's dungeons, the Deep North boss room, the ancient upgrade station) only to players who have been near it themselves; remembered per character in `<world>.icons.txt`. The start temple's icon stays for everyone. |
| `[LocationIcons] DiscoverRadius` | 0 | How near (metres, horizontally) a player must have been to a location for its icon to appear on their map. 0: as near as the game itself shows it -- the location's zone within the simulation distance of the player's zone (4 zones by default, about 256-320 m straight ahead). |
| `[WebMap] Enabled` | false | Write the files of a map feed (see Web map below). Needs a restart. |
| `[WebMap] Directory` / `PublicDirectory` | empty | Where the private files (`places.json`, `explored/`) and the one public file (`live.json`) go. Empty: `<world>.webmap` next to the world, and the folder `public` inside it. |
| `[WebMap] Interval` / `OnlyPublicPositions` | 30 / true | Seconds between two writes of `live.json`; `true`: list only players who show their position on the game's map, `false`: everyone online. |
| `[WebMap] PortalPrefix` / `Bases` | empty / false | List the portals whose tag begins with this (empty: none); list the squares in which players have built (`false`: the pieces are not even counted). |
| `[WebMap] ExploreRadius` / `BaseCellMetres` / `BaseMinPieces` / `Places` | 100 / 512 / 15 / true | Metres a character explores; side of the squares pieces are counted in; squares with fewer pieces are left out; write `places.json`. |
| `[FireControl] Enabled` | false | Limit how far fire spreads (see Fire control below). With the three settings below at their defaults it changes nothing. |
| `[FireControl] MaxRadius` | -1 | A fire starts only within this many metres (straight line) of the source its chain began at. -1: no limit. |
| `[FireControl] MaxSpread` | -1 | Caps how many further generations of sparks any fire may set off. 0: nothing throws sparks except an arrow or fireball where it lands. -1: as in the game. |
| `[FireControl] FireplaceIgnition` | true | Lit fireplaces set alight whatever burnable touches their flame. Off: a hearth, brazier or torch never starts a fire; a bonfire or campfire still throws sparks. |
| `[FireControl] OnlyProjectiles` | false | Only projectiles start fires -- a fire arrow, a Staff of Embers fireball, a meteor, a lava rock -- and what they set alight burns and spreads as before (within `MaxRadius` and `MaxSpread`). Nothing a base has (fireplaces, the fires they lit, the troll a player summons) throws a spark or sets anything alight. Surtlings' and fuling shamans' fireballs never set anything alight in the game. |
| `[DvergrTrade] Enabled` | false | Put up a trade chest and sign at the dvergr wards (see Dvergr trade below). Needs a restart to switch on; switching it off puts up nothing new and takes nothing down. |
| `[DvergrTrade] Offers` | `TrophySeeker:3 > BlackMarble:20, TrophyTick:2 > Softtissue:10, TrophyGjall:1 > BlackCore:2, TrophyHare:1 > Lantern:1, TrophySeekerBrute:1 > Ward:24` | Comma separated `price > reward`. The price is one or more `Prefab:count` joined by `+`; the reward is `Prefab:count` or `Ward:<hours>` (decimals allowed). Paid in this order, each as many times as it is paid in full. Two offers that take the same item: the first wins; an unknown item skips its offer; both are warned about in the log. Read again when the value changes. |
| `[DvergrTrade] OffersAshlands` | `TrophyCharredMelee:3 > Grausten:20, TrophyAsksvin:2 > FlametalOreNew:5, TrophyMorgen:1 > MoltenCore:2, TrophyVolture:1 > Lantern:1, TrophyFallenValkyrie:1 > Ward:24` | The same for the Ashlands' charred tower: the goods and trophies of its own land. `Offers` holds for the camps of the Mistlands. Empty, or not one offer in it valid: `Offers` holds for the Ashlands too. |
| `[DvergrTrade] StandRange` | 48 | Metres, sideways: a player this close to a dvergr ward makes the server put up that camp's stand, and is told when its ward goes off and on. |
| `[DvergrTrade] SignTitle` / `SignTextSize` | (English text) / 45 | The first line of the sign (one line per offer follows), and the percentage of `<size=..%>` around the text, which is also the size of the text a player reads when pointing at the sign (e.g. 70 makes that larger). The game writes a sign in dark brown, which hardly shows on the dvergr's dark board: `<color=#F2E6C8>` at the head of the title makes the whole sign light. How the game sizes the text: [docs/DVERGRTRADE.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/DVERGRTRADE.md#the-sign). |
| `[DvergrTrade] SignLanguage` | (empty) | The language of the item names on the sign, as the game's own language list names it (`Czech`, `German`, `Polish` ...): the game's own words, as the players' inventories show them. Empty: the server's language, English on a dedicated server. |
| `[DvergrTrade] WardOffText` / `WardStateOn` / `WardStateOff` | `ward off for {hours} h` / `Ward: on` / `Ward: off for {hours} h more` | The words that are not item names: how a ward offer reads on the sign and in `{offers}`, and `{ward}` in `MessageOpen` while the ward is on and off. |
| `[DvergrTrade] MessageOpen` | (English text) | Top left, to whoever opens the chest. `{offers}` is one line per offer (item names in the player's own language), `{ward}` the state of the ward. |
| `[DvergrTrade] MessageTrade` / `MessageMissing` / `MessageFull` | (English text) | In the middle of the screen, to the player who closed the chest: what was paid and left (`{paid}`, `{got}`), what an offer still lacks (`{missing}`), that the goods did not fit (`{got}`). |
| `[DvergrTrade] MessageWardOff` / `MessageWardOn` | (English text) | In the middle of the screen, to every player within `StandRange` of the camp when its ward is paid off (`{hours}`: the hours it is off for) and when it comes back on. |
| `[DvergrTrade] MessageWardMax` | (English text) | In the middle of the screen, to the player who closed the chest, when an offer of the ward was paid but its hours would take the ward past seven days from now (that offer is not taken, its trophies stay): `{hours}` the hours it is off for, `{max}` the most. |
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

`KickMinutes` kicks a cheater at once, also with `LogOnly`: a new, fresh character that wears or drops, within its first minutes, an item
it never got here (spawned gear is worn at once) is kicked as the anti-cheat kicks. Only exact accounts, only worn or dropped items and
only within that window: later, a source the ledger missed could make an honest player look like one.

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

Valheim pairs portals by their name alone, so anyone can use, connect to or rename anyone's portal. With `[PortalLock] Enabled` a pair is used, paired and renamed only by those it belongs to: where a ward holds an end, the ward's owner and the players on its list; where no ward holds either end, the builders of the two ends; everyone if the name starts with `PublicPrefix` (`*`) at both ends; admins with `ExemptAdmins`. `Protect` (off by default) keeps everyone else from hitting or taking down a portal, and `KeepClear` keeps public portals away from the start temple, the boss altars and the traders. It cannot stop a modified game.

Who may use a pair, how it is kept, protection, where a public portal may not stand and what it does not do: [docs/PORTALLOCK.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/PORTALLOCK.md).

## Ward guard

In the game every check of a ward is made in the acting player's own game, and nothing checks the ward when a piece is hit: a sword takes down a warded wall, a chest or the ward itself. With `[WardGuard] Enabled` the server decides, for every game that talks to it, by the game's own rule: inside an enabled ward (32 m, measured sideways) only its owner and the players on its list may hit, take down or repair a build (`Structures`), open its chests and doors or use its smelters, fires, beds and crops (`Interactions`). A packet sent in another player's name is dropped (`Spoofing`), requests to destroy what another game owns are counted (`DestroyRequests`), and a player may keep only `MaxActiveWards` wards switched on.

With `OutsideWards` the server protects builds where no ward is, too: only the builder, the players on the list of one of the builder's wards and admins may hit a build, take it down or use it (open a chest or a door, take from a smelter, pick a crop they planted), and someone else's ward gives no rights over it. Repairing and putting things in stay open, and fire, falling objects and creatures are not held back. With `OwnPieces` a builder may always act on their own pieces, and with `Tombstones` a tombstone opens only for the dead player, their friends and admins.

Six more rules are off by default (DS 1.33.0): `DestroyLog` records who destroyed a player's build, `Claims` stops a modified game taking over a piece it may not act on (the first step of destroying or rewriting it), `IndirectHits` counts a catapult's or a creature's hit, which names no player, as the sender's own, `TamedAnimals` keeps strangers off tamed animals in a ward, `Vehicles` keeps strangers off a player's ships and carts, and `Terrain` keeps the ground under a ward and around a player's builds from being dug by strangers (the server keeps the terrain compilers, so a game that created one cannot dig past the check). What stays open: a creature the server runs hitting a build (a raid), fire and falling objects with no player behind them (`IndirectHits` covers only what a player's game sends), the text written on a sign by its builder's own game, a ship or cart moved by physics (ramming), the heights and paint inside the data of a new terrain compiler (only its last operation is judged), and anything a modified game does by writing an object's data directly into a piece it already owns.

Every refusal is written to `<world>.guard.log` with the player's name, so an attempt is on record even when nothing broke. What the game's ward protects and where, each rule in full, what the guard does not cover and how it was tested: [docs/WARDGUARD.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/WARDGUARD.md).

## Base decay

With `[Decay] Mode` the server takes down what players built and left. A piece is held by a ward that is switched on over it (14 + 10 days of grace and fade by default), by a workbench within `WorkbenchRadius` (7 + 10) and by its builder (3 + 4); when the last claim has lapsed the base goes over that claim's fade, the ward first. A player counts as active after `ActiveMinutes` (10) in one go, nothing is taken down while one of the players who hold the base is online, the clock counts only the time the server runs under decay, and a piece is unmarked again when a holder comes back. Start with `DryRun`.

The claims, what happens in an episode, coming back, what players are told, who is left out, the console and what it does not do: [docs/DECAY.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/DECAY.md).

## Anti-cheat

The server does not see a player's mods -- the game tells it nothing about them -- and almost every rule of the game is kept by the player's own game: what may be built where, how fast, what a seed costs, how big a stack may be, whether a character may fly. A modified game just does not keep them, and the server believes what it is told. What the server does see are the results: every object a game creates is sent to it, and it can ask of each one whether an unmodified game could have made it. With `[AntiCheat] Enabled` it does. It is server-side only, so clients stay vanilla, and it is not a scanner of anyone's computer: a cheat that leaves nothing in the world and nothing on the wire (a faster run, a free build, a longer reach) it cannot see; god mode, endless stamina, a stronger sword and skills that cannot have been earned it sees only by what the game sends about them (see "The combat rules" below).

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
| `Items` | is a dropped item whose stack or durability is more than the item can have, or whose quality is above `MaxQuality` when one is set (the game's upgrade station has no ceiling; an egg or a fish carries the level of the creature); or a melee hit that carries a weapon quality higher than that of the weapon the character holds | On |
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
| `Unhurt` | is a character that a lethal hit leaves at exactly 1 health twice in ten minutes (the game's god mode clamps to 1.0), or that 8 hits in a row, from creatures, which were worth more than its armour and any protection shield could take, did not lower the health | Off, warns first |
| `Stamina` | swings a weapon that costs stamina 12 times in 20 seconds while the stamina its game reports stays where it was, at the hits and in between | Off, warns first |
| `Damage` | sends a hit whose damage in all is above the strongest hit any weapon of the game can make (`MaxDamage` 0: automatic, 2067 here); On also drops the hit | Off |
| `Skills` | sends a weapon skill level it cannot have earned here, judged on the experience of the hits the server has seen it send | Off |

**What happens.** A rule that is `On` does what `Action` says: `Log` writes the finding down, `Revert` also takes the object away (the server destroys it; whatever a chest or a stand held goes with it, nothing is refunded), `Kick` also tells the player what was wrong in the middle of their screen and removes them from the server six seconds later. The rules marked "warns first" depend on the state of the server a moment after the player's game decided (a ward switched on just then, ground that was not loaded yet, a character that had just died): the first time one is broken in ten minutes the object is taken away and the player gets `WarnMessage`; breaks less than 15 seconds later are the same time (one mistake can be several objects), and the second time (`SoftStrikes`), 15 seconds or more after, they are kicked. All the others kick at the first finding. `Fly`, `Flood`, `Teleport`, `Speed`, `Burst`, `Health` and `Rotation` (and `Identity`, when it is the character that is wrong) have no object to take away: with `Action = Revert` they only write. A rule that is `LogOnly` only writes. Admins (`adminlist.txt`) are not held to any of it (`ExemptAdmins`; nothing of theirs is even queued): they have the console and the tools that bend the rules. Kick is a removal, not a ban: the player can come back, and everything that breaks a rule is taken away again; the log line names the account, so `ban` on the console keeps it out. Every finding goes to the log and `<world>.guard.log` (`AntiCheat: ...`): who, which rule, which object, where, why; the first of a kind for a player at once, the rest counted together every ten seconds, at most 120 lines a minute. An object that another game has written to since it arrived (a player who opened a chest a moment after it was placed does, as the game hands the object over) is judged as well, but nobody is warned or kicked for what is wrong with it.

**Before you switch it on.** Set `Action = Log` first: every rule only writes, and after a few days the `AntiCheat:` lines in `<world>.guard.log` show what your players' games do -- there should be none from honest ones, and what there is tells you which mods they run. Then `Revert`, then `Kick`. A mod that adds pieces or items of its own (OdinArchitect, SeedBed, WackysDatabase, MyDirtyHoe, a content pack ...) makes objects the server's game does not have: a player who runs one is kicked at the first object it makes (`UnknownPrefabs`). If the server itself has the mod, its objects are known and the rule does not fire; for a mod that only the player has, the rule would have to be `Off`. Mods that every player is meant to have, or that change the game for everyone, trip rules for everyone: set the rule to `Off` or `LogOnly` -- PlantEverything (`NotBuildable`, `UnknownPrefabs`, `Cultivated`), a shortened tool delay such as ToolTweaks' (`Rate`, `Burst`), stack-size mods (`Items`), build-restriction mods (`Wards`, `NoBuild`, `Biome`, `Dungeon`, `Distance`). Take a backup of the world before `anticheat revert`.

**Where the numbers come from.** `MinInterval` and `RatePlants` are set from the real planting times of three players of the live world: 181 and 131 plants put down by hand (never three within a quarter of a second) and 110 put down in groups by a mod (up to 36 within a quarter of a second); the two who plant by hand were not caught, the mod user was, plant by plant. The data is in [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md); a mod that shortens the pause between presses (ToolTweaks) can put honest plants closer than that.

**Paid planting.** PlantEasily, MassFarming, ComfyMassFarming and CropUtils plant a row or a grid of crops with one press. With `PaidPlanting = true` (off by default; `Rate` must not be `Off`) a group of plants that `Rate` would take is let through when the player's own game paid for it as the game does: the cultivator was in the hand and the character's stamina went down by `PlantStamina` (1.3) for every plant. How the stamina is read, what each of those mods charges, and what the server cannot see of it: [docs/ANTICHEAT.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/ANTICHEAT.md#paid-planting).

**The combat rules** (`Unhurt`, `Stamina`, `Damage`, `Skills`; off by default). A player's game keeps its own health, stamina, skills and damage and the server is not asked, but it does see what that game writes to the character (health, stamina, the weapon in hand, whether it blocks or dodges), the hits it sends to what it hit (the damage, the skill and its level, the quality of the weapon) and the hits that the server's own creatures land on the character. These rules read the traces. `Unhurt`: god mode only clamps a lethal hit to exactly 1.0 health, which an honest character never ends at, and damage immunity leaves the health where it was after hits that no armour or shield (the largest the game's status effects give on this server: a shield of 700, 40 armour) could absorb; it needs hits from creatures, so a player who is never hit is never seen, and it waits for fresh data from the player's game after each hit. `Stamina`: an attack pays its price when it starts, so twelve swings with a weapon that costs 4 or more while the stamina stays put, at the hits and in the samples between them, and the game keeps sending data are not a game that pays (an honest player whose stamina is back at the top at every swing is not mistaken for it: between two swings the game shows the price for a second); a bow, a spell and fists are not counted. `Damage`: the damage of a hit is set by the attacker's game, and one above the bound is not one the game makes: `MaxDamage` 0 works the bound out from the game's items (the strongest weapon at its top quality and world level, every multiplier, the strongest damage status effect, times 1.5; the butcher knife has its own on a tamed animal) and logs it at start (the cheats it is for multiply by ten or a hundred; a cheat that stays under the limit it cannot see). `Skills`: per character the server keeps (`<world>.skills.txt` next to the world, saved with it) the first level of each weapon skill it saw and the experience of the hits it has seen since (every hit counts, which over-counts a swing that hits several; the skill's step and the world's skill gain rate are included), and flags a level (less the most that any status effect adds to that skill: Clubs +20, Spears +20, Bows +50, Unarmed +15, ElementalMagic +35, the others +0) that costs more experience than `SkillGainFactor` times what was counted plus `SkillAllowance`. Only the weapon skills are tracked (Swords, Knives, Clubs, Polearms, Spears, Axes, Bows, Crossbows, Unarmed, Pickaxes, WoodCutting, ElementalMagic). **A new, fresh character** (the item ledger's rule, with its `GraceHours`) starts at 0 and is judged exactly. **Any other character cannot be judged on its first level** -- it may have come from another world, or been played before the server counted -- **only on its growth after the first level seen**; and a character that brings its skills from another world looks like a cheat if it was opened as a fresh one (use `LogOnly` if you let those in). Each finding is followed by a line `evidence:` in `<world>.guard.log` with the last eight hits taken or dealt, to check a `LogOnly` finding by. What these rules do not see: a damage cheat under the Damage bound; god mode on a player no creature ever hits (and PvP hits, which are not looked at); stamina cheats on a bow or a spell, and cheats that lower the cost a little; experience the server does not count (crafting, building, running and the like raise other skills, which are not tracked); a cheat careful to look like a player. Nothing runs while all four are `Off` and `Items` is `Off`.

**The stored world.** The console command `anticheat scan` looks through everything in the world with the rules that do not need a player; `anticheat revert` takes away only what no tool of the game makes and dropped items above their maximum (it ignores `Action` and cannot be undone, and `scan` first shows exactly what it would take); `anticheat` alone says what has been found since the start. What each looks for and what it leaves alone: [docs/ANTICHEAT.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/ANTICHEAT.md#the-stored-world).

**The popular cheat mods.** Which mods leave what in the world, and which rule sees it, is tabulated in [docs/MODS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/MODS.md): from the READMEs of 65 popular cheat and tweak mods on Thunderstore and the decompiled code of 29 of them (October 2026) -- planting (PlantEverything, PlantEasily, CropUtils, MassFarming), build restrictions (BuildRestrictionTweaks, NoBuildRestriction), flight and teleport, spawn menus (EasySpawner, Infinity Hammer), stack sizes and item spawners, and the mods that change nothing the server can see (speed, tool wear, free pieces). The short of it: what a mod leaves as an object in the world is seen, what it changes inside the player's own game is not.

**What it cannot see.** Anything the game does inside the player's own game that leaves no trace in what it sends -- how fast a character runs, what a tool or a recipe costs, how much can be carried, how long a pickaxe reaches -- and a cheat careful enough to look like a player. The rest, and how the rules can be got round: [docs/ANTICHEAT.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/ANTICHEAT.md#what-it-cannot-see-and-how-it-can-be-got-round).

Tested on a copy of the live world with 30 simulated players -- a fair builder, an admin and one cheater of each kind -- and a real Valheim client joining, walking and jumping against the same server: every object that broke a rule was taken away and the player kicked after six seconds, nothing else was touched, nobody who played fair was kicked; `PaidPlanting` with 29 simulated players, 29 of 29 checks. What was not tested: a real client placing pieces and planting, and the mods themselves. Everything that was tried: [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md#anti-cheat).

## RPC census

A game talks to the server in RPCs, calls sent by the hash of their name. Many mods add calls of their own: ServerSync, which 101 of the 300 most downloaded mods bundle, sends "ServerSync VersionCheck" when the player joins and "<mod> ConfigSync" for each mod's settings; Valheim Plus shares the map with `VPlusMapSync`. A server without the mod drops them without a word. With `[RpcCensus] Enabled` it writes down each one it does not have, and so which mods each player runs (the ones that talk). Nothing is blocked or changed.

The first of each call a player sends in a run goes to the log (`RPC census: <player> (<host>) sent <name> (<mod>) ...`: to whom, on what object, a few readable words out of it), a summary when they leave, and everything to `<world>.rpcs.txt` (tab separated: player, call, kind, object, count, bytes, first and last seen; kept across restarts; `<host>` is the platform id, as in `<world>.guard.log`). Names: the game's own 183 RPCs and 468 of mods, read out of the 300 most downloaded mods on Thunderstore; a call with no name is written as `hash <number>`. A mod that sends the server nothing leaves no trace. Tests: [docs/TESTS.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/TESTS.md#rpc-census).

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

## Web map

With `[WebMap] Enabled` the server writes the files a map of the world can be made from. The plugin ships no map and no web page and sends nothing anywhere: you serve the files and draw the map, or use one somebody built on them. `live.json` is the one file for the public: the players online with their positions (by default only those who show their position on the game's map), if you ask for them the portals whose tag begins with `PortalPrefix` and the player-built pieces counted per 512 m square (`Bases`; no builder, no exact place), and an id of the world -- not its seed. `places.json` (private) holds the seed, every location of the world and the bosses beaten; per account (private), the part of the map the player has been through. That last one lives only in the player's game and never reaches the server, so the server makes it again from where the character is, every 2 seconds, as the game does: it starts when the feature does, and what a cartography table gives is not counted. Serve the folder `public` and nothing above it. Formats, folders, how to draw the files and limits: [docs/WEBMAP.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/WEBMAP.md).

## Fire control

How fire spreads in the game (sparks, generations, how far and how long, the weather) and who simulates a fire on a dedicated server: [docs/FIRECONTROL.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/FIRECONTROL.md).

Vanilla has no limit in metres; `[FireControl]` adds one. Every spark and fire remembers the source its chain began at (a lit fireplace, or where an arrow, fireball or meteor came down; kept in its ZDO, so it survives a restart), and with `MaxRadius` set, a fire that would start farther than that from its source does not start. `MaxSpread` caps every generation count. `FireplaceIgnition = false` stops direct ignition entirely: a hearth, brazier or torch then cannot start a fire at all, while a bonfire or campfire still throws sparks.

`OnlyProjectiles = true` goes further: only a projectile starts a fire. Every spark and fire also remembers whether its chain began at a projectile (in its ZDO), and only those throw sparks; no fireplace sets anything alight or throws a spark of its own. Which attacks carry fire was read from the game's data: a fire arrow (3 sparks where it lands), a Staff of Embers fireball (6), Ashlands meteors and lava rocks, and the sparks of the Ashlands sky. The fireballs of surtlings, fuling shamans and the Charred mages, and Fader's fire breath and wall of fire, deal fire damage but never set anything alight in the game.

## Death log

The player's game does not tell the server why it died (the cause goes only into the player's own stats), so with `[DeathLog] Enabled` the server reads it from what it does see and writes one JSON line per death to `<world>.deaths.log`, next to the world save, for a bot to post. It sees the hits of its own creatures, projectiles and objects on a player, another player's hit, and the character's position, health and stamina four times a second.

The cause is one of `player`, `creature`, `object` (a falling tree, a ballista, ...), `edge`, `boiled`, `drowned`, `fall`, `dot` (burning or poison after a creature's hit), `lava`, `frozen` or `unknown`; the line also has the killer, the biome, the health before, who else hit the player in the last 10 s and the place. The cause is the server's inference, not the game's word. The rules, the line, and what the server cannot see (a player's own fire or poison, a cart or boat the player's game simulates, a step off a cliff): [docs/DEATHLOG.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/DEATHLOG.md).

## Dvergr trade

The dvergr of the Mistlands are friends, and killing them to empty a camp feels wrong. A vanilla game can show a trade window only for a trader the game knows (the goods of `Trader` live in the client), so the trade here is a chest. Everything is on the dedicated server (Valheim 1.0.20); the players need nothing.

- **The stands.** Each of the eight dvergr camps (the three Mistlands guard towers, the lighthouse, the two excavations, the harbour and the Ashlands' charred tower) has exactly one `dverger_guardstone`, the dvergr's own ward. When a player comes within `StandRange` of one, the server puts up a stand beside it once the camp is loaded: a dvergr chest, which anyone may open even inside the ward, and a sign with the title and one line per offer. A destroyed chest or sign is put up again after ten minutes; the stands are found again after a restart.
- **The trade.** Put the price in, close the chest. The server takes the chest back from the player's game, weighs what is in it, takes the price and leaves the goods (new items of quality 1); offers are paid in the order of the list, each as many times as it is paid in full. Goods that do not fit undo that trade, and the player is told. Each trade is logged.
- **The ward.** `Ward:<hours>` as the reward switches the camp's ward off for that long (a second payment adds to what is left, up to 7 days from now) and calms the dvergr near it that were angry. While it is off, picking what lies in the camp does not anger the dvergr; hitting a dvergr still does, and paying again calms them. The time survives a restart.

In full -- the locations' names, how the chest is taken back and weighed, how the ward is kept off, how the game sizes the sign's text: [docs/DVERGRTRADE.md](https://github.com/MistrCech/valheim-serverside/blob/main/docs/DVERGRTRADE.md).

Not in this: coins, a trade window, prices per camp, taking the stands down when the feature is switched off (they stay where they are). Switching the feature off in the configuration does not end a ward's time: it still comes back on when it is up.

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
