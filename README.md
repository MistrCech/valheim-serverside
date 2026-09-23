# Sarkastic.eu Dedicated Simulation

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

The dedicated server simulates the world — monsters, physics, ships without a driver — instead of handing each area to whichever player got there first. **Server-side only: players keep vanilla clients.**

Updated for Valheim **1.0.7**.

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
- **Admin commands on the server console:** `give <item> <amount> <player>`, `broadcast <text>`, `event <name> <player>`, `players`, `save`, `stop`, typed into the panel the server runs in (AMP), and anything else goes to the game's own console (`kick`, `ban`, `banned`, `stopevent`, and after `devcommands` the cheats that need no player: `skiptime`, `setkey`, `resetkeys` ...). A vanilla dedicated server never reads its console, and Valheim 1.0 does not let a player on a dedicated server use `spawn` from the game console, admin or not.
- **Smoother server frames:** world updates reach every player at a steady interval however many are online, one slow frame no longer makes the next one slow through physics catch-up, and new zones are generated one per tick instead of one per exploring player. A periodic log shows frame times and what they are spent on.
- **`save` and `stop` console commands** for server panels that write to standard input.
- **Safety:** a startup check warns when a vanilla method the mod replaces has changed in a game update; if the core patches cannot be applied, the mod removes itself and the server runs vanilla.

## Installation

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or newer on the dedicated server.
2. Copy `SarkasticEU_Dedicated_Simulation.dll` from the [latest release](https://github.com/MistrCech/valheim-serverside/releases/latest) into `BepInEx/plugins/`.
3. Back up the world and restart the server. `BepInEx/LogOutput.log` should show `Sarkastic.eu Dedicated Simulation installed` and `Vanilla drift check passed`.

Clients need nothing.

**Upgrading from Serverside Simulations:** delete `Serverside_Simulations.dll`. Both use the same plugin GUID, so only one can load; the config file `MVP.Valheim_Serverside_Simulations.cfg` carries over.

**Do not also run BetterNetworking on the server:** its server-side limits are built in.

## Configuration

`BepInEx/config/MVP.Valheim_Serverside_Simulations.cfg`, read at startup:

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | true | Turn the mod off without removing it. |
| `[MaxObjectsPerFrame] MaxObjects` | 100 | Objects the server creates per frame. Higher loads areas faster at more CPU. |
| `[Networking] QueueSizeKB` | 48 | Data queued per player before the server holds world updates for that tick (Valheim: 10). 48 KB at 20 ticks/s is about 960 KB/s, just under the send rate cap; above 80 Steam starts failing. |
| `[Networking] SteamSendRateMinKB` / `MaxKB` | 256 / 1024 | Steam send rate per player, KB/s (Valheim: 150). Keep min × players below the server's upload. |
| `[Networking] StatsIntervalMinutes` | 5 | How often to log, per player, how often the send queue was full. Near 0% means the limits are not what holds you back. 0 disables. |
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
| `[Fixes] DungeonLoadGuard` | true | Keep a dungeon whose room bundle fails to load from wedging its zone. When Unity refuses a bundle as already loaded, the game still reports the load as done and then throws, so the dungeon never spawns and its zone stays flagged as loading. The guard uses the bundle Unity already holds, reports a load that really failed as failed, and lets such a dungeon go so it is tried again next time. A vanilla dedicated server never loads dungeons; this mod does. |
| `[Compat] ValheimCommunityPatchUnload` | false | Only with ValheimCommunityPatch installed. Its zone-diff unload drops objects outside the simulation distance of the server's reference position -- the world origin -- so objects around players are destroyed and created again on every pass. Off: that patch of it is removed when the world starts. On: it is kept, and the object lists are marked as edited on every pass so it takes the game's own unload check. |
| `[Compat] ValheimCommunityPatchSpawnQueue` | false | Only with ValheimCommunityPatch installed. Its spawn queue orders new objects by distance from the world origin, so this mod's nearest-player ordering never runs. Off: that patch of it is removed when the world starts. On: it is kept. |
| `[Performance] StatsIntervalMinutes` | 5 | How often to log FPS, frame times, physics steps per frame, the cost of world updates and zone generation, and what the slowest frame was doing, while players are online. 0 disables. |

## Hosting notes

- **After a game update**, look for `Vanilla ... changed` warnings in the log, and keep world backups.
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
