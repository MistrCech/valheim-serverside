# Sarkastic.gg Dedicated Simulation

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

The dedicated server simulates the world — monsters, physics, ships without a driver — instead of handing each area to whichever player got there first. **Server-side only: players keep vanilla clients.**

Updated for Valheim **1.0.15**; also runs on 1.0.12.

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
| `[MaxObjectsPerFrame] MaxObjects` | 100 | Objects the server creates per frame, 1 to 10000. A vanilla dedicated server creates 100; higher loads areas faster at more CPU, lower fills them in more slowly. |
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
| `[CharacterGuard] NewCharacterMessage` / `ChangedAwayMessage` | (English text) | Shown in the middle of the player's screen before the kick. |
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
| `[FireControl] Enabled` | false | Limit how far fire spreads (see Fire control below). With the three settings below at their defaults it changes nothing. |
| `[FireControl] MaxRadius` | -1 | A fire starts only within this many metres (straight line) of the source its chain began at. -1: no limit. |
| `[FireControl] MaxSpread` | -1 | Caps how many further generations of sparks any fire may set off. 0: nothing throws sparks except an arrow or fireball where it lands. -1: as in the game. |
| `[FireControl] FireplaceIgnition` | true | Lit fireplaces set alight whatever burnable touches their flame. Off: a hearth, brazier or torch never starts a fire; a bonfire or campfire still throws sparks. |
| `[FireControl] OnlyProjectiles` | false | Only projectiles start fires -- a fire arrow, a Staff of Embers fireball, a meteor, a lava rock -- and what they set alight burns and spreads as before (within `MaxRadius` and `MaxSpread`). Nothing a base has (fireplaces, the fires they lit, the troll a player summons) throws a spark or sets anything alight. Surtlings' and fuling shamans' fireballs never set anything alight in the game. |
| `[Performance] StatsIntervalMinutes` | 5 | How often to log FPS, frame times, physics steps per frame, the cost of world updates and zone generation, and what the slowest frame was doing, while players are online. 0 disables. |

## Character guard

Valheim keeps a character on the player's own computer and a vanilla client never sends its inventory to the server, so no server-side mod can read or replace what a character carries. The server does see a character's id (the same on every world), what it wears and holds (both hands, back slots, armour, utility, trinket, with the upgrade level of weapons), and the raids it is ready for, which the game works out from the items the character knows and the bosses it has beaten. The guard uses that:

- **New characters** -- not in this world's list and without anything built, a bed or a tombstone here -- must be fresh: still ready for Eikthyr's raid (the game stops that once a character knows the antler, bronze or iron pickaxe, hard antler or Eikthyr's trophy) and wearing nothing beyond what a level 1 workbench makes. A character brought in with progress from another world gets a message and is kicked 8 seconds later (`NewCharacterAction`).
- **Known characters** that come back wearing something they did not have when last seen here, or ready for other raids than then, were played somewhere else in between; that is logged (`ChangedAway`, or kicked). What an online character wears and knows is noted every minute as well as when it leaves, since a server shutdown does not disconnect the players one by one.
- Characters that built something or own a bed or a tombstone in the world count as known, so switching this on does not lock out existing players. Admins are exempt.
- `characters` on the console shows how the guard sees who is online; `allow <name>` lets a character in (and one that is waiting for its kick stay). The list is `<world>.characters.txt` next to the world save.

What a character carries without wearing it stays invisible to the server: this stops characters being imported with their progress; the item ledger below catches materials brought in by known characters once they reach the world.

## Item ledger

The server never sees a bag, but nearly every way into and out of one passes through an object it does see:

- **In:** picking an item up (the player takes it over and deletes it), taking it out of a chest, cart, ship or tombstone, or off an item stand or armour stand.
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
