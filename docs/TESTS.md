# Tests

What the larger features of the plugin were tested with, in full (the README keeps a summary).

## Ward guard

Tested on a copy of the live world with three simulated players -- a ward's owner, a stranger and a friend put on the ward's list in the middle -- sending each request as a client does, in four runs: guard off (the same requests all went through: the stranger damaged and took down the wall, opened the chest, used the door, switched the fire pit off, claimed the bed and switched the ward off as its owner), On, LogOnly (everything went through, each refusal logged as "LogOnly: let through") and with `DestroyRequests` On. With it on: the stranger's and the friend's hits left the warded wall untouched, a wall outside every ward was hit as before, and the owner's hit, the friend's after he was put on the list, a creature's hit (a creature of the sender's game), a hit with no attacker and an admin's went through; a creature of another player's game named as the attacker, `RPC_Remove`, the chest (it answered "no"), the door, the fire pit's switch and the bed were refused, ore put into the smelter was taken; a hit sent as another player and a ward switched off in the owner's name were dropped; with `DestroyRequests` On a request for a wall the stranger does not own was taken out, and of a request for two objects only the foreign one. Finding the wards among 349 000 objects takes 6.5 ms every 5 seconds. Then with a real, unmodified Valheim 1.0.20 client (headless, its own profile) standing 1.5 m from a warded wall and hitting it with its bare hands: with the guard off the wall was down to 91 health after the first round and gone after the second; with it on, in the first round (not on the ward's list) the wall stayed untouched and the server logged "tried to hit woodwall ... refused", and in the second (written on the list) the wall was hit as in the game. The client played on for a minute with Spoofing, Structures and Interactions on and was never refused anything else.

### Outside wards (1.31.0)

`[WardGuard] OutsideWards`, tested on the copy of the live world of 1 November (game 1.0.20) with the same three simulated players and three more walls 66--74 m from the ward (the owner's, the stranger's own, one with no creator), in three runs: OutsideWards On, the guard as it runs live (OutsideWards Off) and the guard off. With it On a stranger's hit on the owner's wall outside the ward and his hammer on it were refused (the wall untouched, still standing; guard log: "fake1 (222) tried to hit woodwall ...; built by fake0 (111), no ward there -- refused"), and so were his hits on a wall inside the ward once it was switched off and on the switched-off ward itself; the builder, a friend on the list of the builder's ward (also of the switched-off one), a repair by the stranger, a hit with no attacker, the stranger's creature, his own wall and a wall nobody built all went through. With it Off and with the guard off every one of them went through, and the fifteen earlier steps of the ward guard test gave exactly what they gave on 1.26.0 on 1 November (the same world, both modes). No exceptions in any run.

Then the rest of 1.31.0 in the same three runs (the first with `OutsideWards` On, `OwnPieces` and `Tombstones` On, the other two with them Off): a chest, a door, a fire pit and a carrot sapling of the owner's outside the ward, a carrot nobody planted, a wall and a chest the stranger built inside the owner's ward, and a tombstone of the owner's. The sapling was grown on the server, and the crop carried its planter (the owner's id; with the guard off, none). With the rules On, the stranger was refused opening the owner's chest outside the ward (it answered no), his door, switching his fire pit off and picking his crop (it stayed, nothing dropped), opening the tombstone and taking everything from it, and -- after he switched on a ward of his own beside the owner's outside wall -- hitting that wall and opening the chest under it; he was shown the `OutsideWardsMessage` and `TombstonesMessage`. The friend on the list of the owner's ward opened the chest and the tombstone, the owner took everything from his tombstone and hit his own wall under the stranger's ward, the stranger hit and opened what he had built inside the owner's ward (`OwnPieces`), and the carrot nobody planted was picked (it went, a carrot dropped). With the rules Off and with the guard off all of it went through -- except that the guard as it runs live refused, as the game's ward does, the stranger's hit and chest inside the owner's ward, his own included, and the owner's hit on his own wall under the stranger's switched-on ward: the hole that `OwnPieces` and `OutsideWards` close. Steps 1--15 and O1--O11 gave exactly what they gave before. The only exceptions in the logs are three of the test kit's simulated players (`Player.UpdateGyro`), the same in every run.

### Ward guard: the six rules of 1.33.0

`DestroyLog`, `Claims`, `IndirectHits`, `TamedAnimals`, `Vehicles` and `Terrain` (with `TerrainMargin` 2 m), tested on the copy of the live world of 1 November (game 1.0.20) with the same three simulated players (owner fake0, stranger fake1, friend fake2 on the owner's list), in five runs: `v133` (the 1.31.0 rules On, `DestroyRequests` On, `DestroyLog` and the six rules On), `v133log` (the six rules LogOnly, `DestroyRequests` LogOnly, `DestroyLog` on), and the three earlier runs with the six rules at their defaults (`out`, `on`, `off`). The steps run after the earlier ones, in a corner 35 m east and 40 m south of the scene, each request sent as a client sends it. The observed values are what the server holds after the request (a guard-log line is quoted only where one was written: a refusal is written once a minute per player, kind and builder or ward, so a second refusal of the same kind within a minute leaves no line).

| Step | `v133` (On) | `v133log` (LogOnly) | `out`, `on` | `off` (no guard) |
|---|---|---|---|---|
| C1 stranger takes fake0's piece (owner only, outside the wards), then asks for it to be destroyed | owner stays the server, piece still there; line "tried to take over woodwall ... refused" | owner fake1, piece gone; lines "take over ... LogOnly: let through" and "destroyed: fake0's woodwall: fake1 (whose game owns it) asked for it to be destroyed" | owner fake1, gone, no line | owner fake1, gone |
| C2 stranger takes a sign over and writes its text | text "mine", owner the server; line "take over sign" | "hacked", owner fake1; LogOnly line | "hacked", owner fake1 | "hacked", owner fake1 |
| C3 fake0 does the same on his own sign | "own", owner fake0, no line | the same | the same | the same |
| C4 stranger claims a fire pit nobody owns | owner fake1, no line | the same | the same | the same |
| I1 stranger's hit with no attacker (Catapult) on the warded wall | health unchanged | hit applied (health 370) | hit applied | hit applied |
| I2 a boar fake1's game owns hits a wall outside the wards | unchanged | applied; line "tried to hit (EnemyHit, Boar run by the sender's own game) ... LogOnly: let through" | applied | applied |
| I3 a boar the server owns destroys fake0's weak piece | piece destroyed; line "destroyed: fake0's woodwall: hit by a Boar" | destroyed; the same line | destroyed, no line | destroyed, no line |
| T1 stranger hits the tamed boar in fake0's ward (hit of 3.1 after resistance) | health 20 -> 20; line "tried to hit Boar ... refused" | 20 -> 16.9; LogOnly line | 16.9 | 16.9 |
| T2 the friend on the list hits it | 20 -> 16.9 | 16.9 -> 13.75 | 16.9 -> 13.75 | 16.9 -> 13.75 |
| T3 stranger renames and commands it | name "Pet" | "Hax" | "Hax" | "Hax" |
| V4 stranger fires fake0's catapult that the server owns (loaded by fake0) | `RPC_Shoot` dropped (nobody hears it), still loaded; line "tried to fire Catapult ... refused" | passed (fake0 and fake2 hear it), fired by the server (no longer loaded); line "tried to fire (the server owns it, so the shot would be the server's own) ... LogOnly: let through" | passed, fired, no line (`Structures` and `OutsideWards` alone do not drop it) | passed, fired |
| V5 fake0 fires his own catapult that his game owns | fake1 and fake2 hear `RPC_Shoot` | the same | the same | the same |
| V1 stranger asks for the helm of fake0's Karve | fake1 is answered `RequestRespons` False | no answer (the fake player is not in the boat) | no answer | no answer |
| V2 the friend asks for it | no refusal | no refusal | no refusal | no refusal |
| V3 stranger asks to own fake0's cart | owner stays the server, fake1 hears `RPC_RequestDenied`; line | owner fake1; LogOnly line | owner fake1 | owner fake1 |
| V6 stranger opens the Karve's chest (on the ship's own object) | granted=False | granted=True | granted=True | granted=True |
| V7 stranger opens a cart's chest | granted=False; line | granted=True; LogOnly line | granted=True | granted=True |
| G1 stranger digs (operation `digg_blobLavaExplosion`, 3 m) 1 m from fake0's piece outside the wards | not applied (0 operations on the terrain compiler); line "tried to change the ground ... within 5 m of woodwall ... refused" | applied; LogOnly line | applied | applied |
| G2 stranger digs 20 m from it | applied | applied | applied | applied |
| G3 stranger digs inside fake0's ward | not applied; line "within reach of the ward ... whose list does not have them" | applied; LogOnly line | applied | applied |
| G4 fake0 digs inside his ward | applied | applied | applied | applied |
| D1 the friend on fake0's list takes fake0's piece outside the wards down | gone (seen on the next look: the game removes it a moment later); line "destroyed: fake0's woodwall: fake2 (333) took it down" | the same | gone, no line | gone, no line |
| G5 stranger's game creates a new terrain compiler in a zone with none, its operation 1 m from fake0's piece outside the wards | destroyed (gone on the late look: the destroy is queued); line "made a new terrain compiler ... within 5 m of woodwall ... refused" | kept, owner the server; LogOnly line | kept, owner fake1, no line | kept, owner fake1 |
| G6 the same in a free zone, 45 m from any build or ward | kept, the server owns it; no line | kept, the server owns it; no line | kept, owner fake1 | kept, owner fake1 |
| G7 stranger's game creates a second compiler in the zone of G6 (99999 operations) | destroyed; line "made a second terrain compiler in a zone that has one ... refused" | kept; LogOnly line | kept, owner fake1 (the game then drops the one with fewer operations: G6 is gone on the late look) | the same |
| G8 stranger's game takes over the server's compiler of the ward, raising its data by 1000 operations | owner stays the server, data not read (operations unchanged); line "tried to take over the terrain compiler ... refused" | owner fake1, 1002 operations; LogOnly line | owner fake1, 1002, no line | owner fake1, 1002 |
| G9 stranger's `RPC_ApplyOperation` next to fake0's piece on the G6 compiler | refused by `Terrain`, line "tried to change the ground ... refused" | line "LogOnly: let through" | no line | no line |
| G10 stranger's game creates a new compiler in a fresh zone, its last operation 30 m from fake0's piece, but its heights lowered by 2 m at two vertices 1 m from it | destroyed (gone on the late look); line "made a new terrain compiler (1 operation(s), the last at ...) whose ground is changed at ... within 3.5 m of woodwall ... refused" | kept, owner the server; LogOnly line | kept, owner fake1, no line | the same |
| G11 stranger's game sends the data of the server's compiler of the ward naming the server as the owner, a newer revision, 99999 operations | owner the server, data not read (1 operation); line "sent the ground data of the terrain compiler ..., which it does not own ... refused" | read (99999 operations), owner the server; LogOnly line | read, no line | the same |
| G12 as G5 (last operation 1 m from a piece of fake0's, a fresh zone), but the packet names the server as the owner | destroyed; line "made a new terrain compiler ... within 5 m of woodwall ... refused" | kept, owner the server; LogOnly line | kept, owner the server (as the packet says), no line | the same |

`out`, `on` and `off` against the runs of 1.31.0 (`wardguard-t31c-*`): every line of the earlier steps and every guard-log line is the same; only the start-up line differs (it names the new settings) and the scene's object ids. With the six rules at their defaults, V4 and the other new steps go as with no guard at all. The terrain compilers of 1.33.0 (G5--G9, runs `wardguard-t34-{v133,v133log,out,on,off}`) were added after the first five runs of this section: `out`, `on` and `off` equal `wardguard-t33-*` line for line in every earlier step (apart from the expected-value text of two V4 lines in the kit) and have no guard-log line for the new steps; in `v133` and `v133log` the earlier steps are unchanged. No exception and no "Vanilla drift" difference in any of the five. G9's "heard" lists are empty in every mode (the packets are read in the frame they are sent), so the guard-log line is what tells the modes apart there. A server-owned compiler far from every player is released to owner 0 by the game itself (`ZDOMan.ReleaseNearbyZDOS`); G6 therefore reads "owner 0" on the late look.

G10--G12 (runs `wardguard-t35-*`, with the final build) were added last: every earlier step and guard-log line of all five modes equals `wardguard-t34-*` but for the run-to-run noise (the position of the wandering tamed boar, whether two objects were found at the first look), no exception.

Not tested: the paint inside the data of a new terrain compiler, a player's game that really sits in a boat (the fake player is never inside the Karve, so the game's own answer to the helm is not seen where the guard lets it through); the server handing a ship to a game that may not use it (`ZDO.SetOwner` guard; no ship was put at sea); a saddled mount (`Sadle` calls); `Terrain` in `v133log` with the tool's `rotation` flag set.

## Item ledger: KickMinutes (1.32.0)

Tested on a copy of the live world (game 1.0.20) with four simulated players, the ledger in `LogOnly` with `GraceHours = 0` (so a new
character counts as fresh), the anti-cheat on (its `Identity` rule off: the simulated players have no character object of their own):

| run | `KickMinutes` | fresh, wears an iron axe at 8 s | not fresh (unverified), wears it | fresh, drops 5 iron at 9 s | fresh, puts 5 iron into a chest |
|---|---|---|---|---|---|
| k60 | 60 | logged, kicked | nothing logged, stays | logged, kicked | logged, stays |
| k0 | 0 | logged, stays | stays | logged, stays | logged, stays |

And a third run with `KickMinutes = 0.1` (6 s): a fresh character wearing the axe at 30 s and one dropping 5 iron at 30 s are logged
and both stay. The kicked ones are gone from the server's peers 45 s in (the anti-cheat's message, then the kick).

## Portal lock: Protect

1.26.0, tested on a copy of the live world taken the same morning (game 1.0.20, Ward guard off as on the live server) with three simulated players -- O, who owns a ward; S, a stranger who owns a ward of his own; F, on O's ward's list -- sending each request as a client does (`ZRoutedRpc.RPC_RoutedRPC` from the player's connection), in two runs with the same 19 steps: `Protect` on, and off as the positive control. The scene: O's pairs A (in O's ward) - B (40 m out in the open) and A2 - B2 alike, O's pair C - D with no ward, H (O's, in O's ward) - K (S's, in S's ward) as a pair from before the lock, a wall beside B and a boar run by S's game. Off, every hit and removal went through: S's hits on B, A and H, O's on K, a hit on B that names no attacker and S's hammer on B; with O's game owning B, S's hit and removal were passed on to it. On: S's hits on B (the far end) and A (the warded end), his hit with no attacker (what a catapult or a battering ram sends), O's hit on K in S's ward and S's on H in O's were refused, and so was S's hammer on B -- each portal stayed untouched, the player was told ("This portal belongs to a warded base ...") and `<world>.guard.log` got a line for each ("fake1 (222) tried to hit portal_wood at ..., which the ward over its other end at ... protects [1x since the last line] -- refused", the rest counted and written when the player left); with O's game owning B nothing of S's was passed on to it, F's hit was. Went through either way: S's hit on the wall beside B (not a portal), the hits of F and O on B, the boar's (a creature of S's game), S's on C (no ward at either end) and on K (in his own ward), an admin's, S's hammer on C, O's on A2, and S's hit on B2 once A2 was gone (no ward holds either end then); F took B down. A hit sent in O's name went through: the sender id is the client's claim (Ward guard's `Spoofing` drops that).

## Console: tp, pos, heal, spawn

1.27.0 was tested on a copy of the live world (game 1.0.20, `[AntiCheat]` on with Teleport and Speed LogOnly,
`MaxSpawnAmount` 3) with three simulated players near the start. The commands were run the way the console runs them
(`ServerConsole.Execute`). The simulated games behaved like this:

- fake1's game obeys a teleport as `Player.TeleportTo` does. It is at the spot 2.5 s later and done 8 s after the
  teleport arrived (15 s at the goto height of 30, where it lands on the ground at 14.5 s), and it refuses a teleport that
  comes while it is still teleporting.
- fake2's game never moves.
- fake0 stays where it is, except for one 1500 m jump with no teleport sent.

All 22 checks passed. In order:

1. `tp fake1 1000 -2000` reaches fake1 alone, as one `RPC_TeleportPlayer` to 1000,30,-2000 with distant teleport on.
   17 s later the console reports "fake1 is there … at 1000,52,-2000, health 40 of 100".
2. The other forms send what they say:
   - `<x> <y> <z>`, `x,z` and `x,y,z` send the coordinates given.
   - `start` sends 2 m above the middle of the start.
   - `to fake0` sends fake0's spot, 0.3 m up, facing as fake0 faces (yaw 90).
3. Nothing is sent for any of these, and each gets its own answer:
   - a name that fits several players;
   - an unknown name;
   - a spot outside the world (10500 m);
   - no target;
   - fake1 sent to itself;
   - an unknown destination;
   - a word that is no target.
4. Four teleports were sent to fake1 at once. Its game took the first and refused the other three. The console, watching
   the last one (the start), reports after 40 s that fake1 is not there: "dead, in a menu or already teleporting?".
   fake2 got the same report.
5. `tp all to fake0` reaches fake1 and fake2 but not fake0. fake1 is reported there 10 s later.
6. `pos fake1` and `pos` print position, biome and health.
7. `heal fake1` sends `RPC_Heal` to fake1's character with enough for full health. `heal fake1 25` sends 25.
8. `heal fake2` for a dead fake2 sends nothing and says so. `heal all` heals the living.
9. `spawn Boar 2 3 fake0` makes two boars at level 3, marked cheated (`cheated` 2), within 2 m of the spot 3 m in front
   of fake0.
10. `spawn Boar 9 fake0` makes 3 (the cap).
11. These are refused: `spawn Wood` (it points to `give`), an unknown prefab, `spawn Player` and `spawn` with no target.
12. `help` lists the new commands.
13. Anti-cheat logged fake0's jump (1 new Teleport finding). It logged none of fake1's moves, each made after the
    console sent fake1 there; two of them were jumps of over 2000 m (2266 m and 2063 m).

## World index

1.27.1 was tested on a copy of the live world taken the same day (891,000 objects, game 1.0.20), on the test machine.

The index itself was tested with one simulated player. All 17 checks passed:

1. A ward, a portal and a workbench made by the server are in the index at once, each under its kinds. So is a ward
   that a player's game sends (`RPC_ZDOData`). The workbench is neither a ward nor a portal, and the guardian stones
   are as many as before.
2. The workbench, taken away, is out of the index at once.
3. The index was then spoilt the way a mod going past the game's hooks would spoil it: a ward taken out, the id of no
   object put in as a portal and as built, a tree put in as a portal. The 10-minute check, started at once, put back
   exactly what was taken out (1 ward, 1 built) and took out exactly what was put in (2 portals, 1 built). A workbench
   made while it compared stayed in. A second check found the index right. Neither check ran 4 ms or more on a CPU in
   any frame.
4. With the hooks gone, an index 10 s old is made again from the world.

The check went through 891,160 objects in 50 frames, 26 ms in all, and no frame of it ran more than 1.1 ms on a CPU.
That was measured from outside the plugin with the server thread's own CPU time, because the clock time of a frame also
holds the time the thread waited for a CPU while the machine did other work (up to 0.8 ms in a frame here). In an earlier
build the check still grew its sets and copied the list of built objects while it ran: its first check had a frame of
2.4 ms on a CPU, and one run had two frames of 6.9 and 6.4 ms of clock time, which four later runs did not repeat. Making
the index takes 33 ms, once, after the world has loaded.

The other features were run on the same copy against 1.27.0:

- Decay with the live configuration, five passes 0 to 24 days on: the same report, line for line (532 lines). The
  decay test: 110 of 110, on the copy and on the copy 15 days on.
- Console 22 of 22, ward limit 17 of 17 (on) and 13 of 13 (off), anti-cheat 41 of 41, with the same log as 1.27.0
  apart from timing and the order of kicks.
- Ward guard on and off, guardian stones, 20 portals: the same log as 1.27.0.
- Portal lock on and off: the same log, except for 6 fewer resends of a portal's connection in the seconds after a ward
  was built over it. The old look, up to 10 s late, let those through.

## Anti-cheat

Tested on a copy of the live world with 30 simulated players sending each object through the server's own entry point for object data, as a client does: a fair builder (floor, three crops 0.5 s apart, a pair 0.02 s apart, wood in stacks of 20 and 50, a floor in their own ward), an admin who does all of it, and one cheater of each kind (thistle, five crops in one moment, floors 80 and 90 m away, another player's creator, 500 wood and wood of quality 9, an object the game does not have, a character that flies, floors in a stranger's ward and one outside it, a boar with the cheating mark, field overrides, floors in the start temple, floors in a dungeon, a floor made under another game's session, a game that names another's character, a game with no character, a flood of 4,100 objects, a boar of level 50 (and one of level 3, which stays), a game that writes to another's character and to another's new piece), a guest on a ward's list, a game that is warned once and not kicked, the rules that only log (they logged and let it through; an item whose data carries the cheating mark is traced), the teleport (a jump with and without a portal at the departure) and the run, and the scan and revert of the stored world -- plus the real planting times of the three players above -- and a real Valheim client joining, walking and jumping against the same server (its character object carries the stamina: a jump takes 12 of it at once and the regeneration starts a second later) (46 to 65 objects of its game judged in three runs, nothing found by any rule): every object that broke a rule was taken away and the player kicked after six seconds, nothing else was touched, nobody who played fair was kicked. `PaidPlanting` was tested the same way with 29 simulated players planting groups of crops (paid up front or in the frame of the press, partly paid, a quarter paid, only the first plant paid, a hammer in the hand, no stamina in the character, the stamina a moment late and too late, a hole in the planting times, two groups sent alternately or 0.45, 0.8 and 1.3 s apart, a game written to by another, an admin, planting times at the edge of a long, a character that ran out of stamina with a grid of nine, the world taking no stamina, the setting switched off): 29 of 29 checks. What was not tested: a real client placing pieces and planting (only the simulated ones) and the mods themselves.

## Anti-cheat: where the numbers come from

The plants of three real players on the server this was written for, replayed through the rule: 181 and 131 plants put down by hand (pairs of plants 0.02 to 0.16 s apart are in the real data -- the time written into a plant is the game's network time, which now and then steps by a few tenths of a second -- but never three in a quarter of a second) and 110 plants put down in groups by a mod (98 of 109 gaps under 0.05 s, up to 36 plants within a quarter of a second). `MinInterval` and `RatePlants` are set from that: the two players who plant by hand (312 plants) were not caught, the mod user was, plant by plant. Two hands are a small sample; a mod that shortens the pause between presses (ToolTweaks) can put honest plants closer than that.

## Anti-cheat: combat rules (1.33.0)

`Unhurt`, `Stamina`, `Damage`, `Skills` and the weapon part of `Items`, on a copy of the live world (game 1.0.20) with twelve simulated players (A to L)
whose games' writes are ZDO data sent from the player's connection and whose hits are routed `RPC_Damage` calls with a HitData, as a client sends them;
the server's hits on them are `Character.Damage` on their Player object. `[AntiCheat]` Enabled, Action Kick, Identity Off, Items On, MaxQuality 6, the four
rules in the mode of the run; `[CharacterGuard]` Enabled (NewCharacterAction Log) and `[ItemLedger] GraceHours = 0` so that a character with nothing on counts
as new and fresh. Three runs (`cheat133run.sh <label> on|log|off`, labels `gon`, `glog`, `goff`), 18 checks each, 0 failed in all three, no exception, and the plugin's "Damage bound" line in the log of `on` and `log`:

| step | what | on | log | off |
|---|---|---|---|---|
| U1 | A takes two lethal hits (500 on 60 health) and its game writes health exactly 1.0 after each (healed between) | 1 Unhurt finding | 1 | none |
| U2 | B takes 30, then a lethal hit and writes 30, then 0 and dead | none | none | none |
| U3 | C takes 8 hits of 3000 and its game keeps health at 60 and writes fresh data | 1 | 1 | none |
| U4 | D takes the same while its game says it blocks | none | none | none |
| U5 | E takes the same and sends no data | none (dropped after 6 s) | none | none |
| S1 | F (iron sword, costs 10) swings 12 times in 13 s, stamina constant 100 | 1 Stamina finding | 1 | none |
| S2 | G does the same with stamina dropping and recovering | none | none | none |
| S3 | peer 6 swings 12 times 1.2 s apart, stamina back at 100 before each swing (cost 10) | none (stamina that is back at 100 before each swing is not a cheat; the tick samples between swings show it moving) | none | none |
| D1 | H sends a hit of 5000 on a boar | 1 Damage finding, hit dropped, boar health 10 -> 10 | 1, boar killed | none, boar killed |
| D2 | B sends a hit of 1500 on a wild boar (below the automatic bound, MaxDamage 0) | none, boar killed | none | none |
| D3 | D holds a butcher knife (tamed-only) and hits a tamed boar with 2480 (the kit takes it from the plugin's bounds: above the normal bound, below the knife's) | none (the knife's bound 5625 applies), boar killed | none | none |
| D4 | E holds the same knife and sends the same 2480 on a wild boar | 1 Damage finding, hit dropped | 1, boar killed | none, boar killed |
| K1 | I (new, fresh: exact account) sends Swords level 100 in its first hit | 1 Skills finding | 1 | none |
| K2 | J (fresh) sends 200 hits at levels rising from 0 to 12 | none | none | none |
| K3 | K (not verifiable: unverified account) first at 60, 61 after 300 hits; then 95 | none; then 1 | none; then 1 | none; none |
| K4 | the ledger `<world>.skills.txt` after a save | I, J exact from 0 (Swords=0:1.5 and 0:300), K unknown from 60 | the same | no file |
| Q1 | L holds an iron sword of quality 2 and sends a hit with `m_itemLevel` 9, then 2 | 1 Items finding for the first, none for the second | the same | the same (Items is On in every run) |

Each finding of the combat rules is followed by an `evidence:` line in the guard log (7 in `on` and in `log`, none in `off`, where nothing of the combat rules runs and the
bounds below are not even computed). What the server's game says is the most that anything gives (logged at the first use, `Anti-cheat: combat bounds from the game`):
the largest protection shield absorbs 700, status effects add up to 40 armour, the skill level bonus of status effects is Swords 0, Knives 0, Clubs 20, Polearms 0, Spears 20,
Axes 0, Bows 50, Crossbows 0, Unarmed 15, Pickaxes 0, WoodCutting 0, ElementalMagic 35; the strongest hit of a weapon a player can make at world level 0 is THSwordGold_FrostFire at quality 4,
334 x 3 for its special attack = 1002, 1102 with the percentages status effects give its damage types (slash and frost +10 %), 1378 with the multiplier x1.25; the automatic `MaxDamage` bound is 2067 (1378 x 1.5); a tamed-only weapon (KnifeButcher, 3000) gets 5625 against a tamed target. The runs `gon`/`glog`/`goff` were made with a build that put the largest percentage of any type (+60 % chop, GP_TheElder) on the whole hit (bound 3006, knife hit 3607); the final runs `hon`/`hlog`/`hoff` take it type by type: 18 checks, 0 failed in each, no exception. The world level adds 120 per level to each weapon's damage (the game's own `IncreaseEqually`).

Final build of 1.33.0 (`new134`, with the Stamina and Damage changes and the terrain compilers): 41 checks, 0 failed; against `new133` only the tallies ("and 4 more" / "and 5 more" etc.), the order of two lines in the guard log and the position that moves (994 / 1008) differ.

Regression: `anticheatrun.sh` with the 30 simulated players (`KITX='ackickwait 22\n' CFGX='[AntiCheat]\nMaxQuality = 6\n'`, 150 s) on the unchanged 1.32.0 DLL (`base133`) and on the
new build (`new133`): 41 checks, 0 failed in both; the two summaries and guard logs are the same line for line except the numbers of the tallies ("and 4 more in the last 10 s" /
"and 5 more"), a position that moves (991 / 994) and the four new rules in the `anticheat` console listing (Off, 0 findings). `wardguardrun.sh ... out` on the live copy against the
earlier 1.33.0 run (`t33`): the same except the session numbers of the simulated players and one boar of the scene (`n_i3b`) that was not there yet at the start of the step in the
new run (every later line is the same).

Not tested: a real client (the rules read what a real game writes to its character and what it sends in a hit; the simulated games write what the code of the game does, read
off its source), god mode or a damage cheat of a real mod, players above skill 60 playing on the live server (the table of skill bonuses is the game's own, the experience
counted is not checked against a real character), and a protection shield or armour that the player really wears (the simulated characters wear nothing, so only the bounds are used).

## Death log (1.34.0)

`[DeathLog] Enabled` on a world with twelve simulated players (A to L) whose games' writes are ZDO data sent from the player's connection and whose
hits and deaths are routed `RPC_Damage` and `OnDeath` calls, as a client sends them; the server's hits on them are `Character.Damage` on their Player
object. Two runs (`deathrun.sh <label> on|off`): on 13 checks, 0 failed; off 13 checks, 0 failed (no file, no line).

| step | what | line |
|---|---|---|
| D1 | a server hit of a Troll on A, A dies | `creature`, `Troll`, `$enemy_troll`, level 1 |
| D2 | B's RPC_Damage on C naming B's character, C dies | `player`, B's name (quotes, backslash, tab, U+2028, emoji escaped), B's account |
| D3 | D 25 m above the ground for 1 s, then on it, then dead | `fall`, fell 25.0 |
| D4 | E in deep water, stamina 0 | `drowned` |
| D5 | a Troll's hit with fire 10 on F, F dead 4 s later | `dot`, fire, `Troll` |
| D6 | a server hit of type Tree without an attacker on G | `object`, `Tree` |
| D7 | H on a Mountain spot | `frozen` |
| D8 | I at x = 10450 | `edge` |
| D9 | J in the Meadows, nothing before | `unknown` |
| D10 | K writes the dead flag without the RPC; L sends OnDeath twice and writes the flag | one line each |
| D12 | J owns a Troll and sends its hit on B as RPC_Damage naming the Troll, B dies | `creature`, `Troll` |
| D11 | all | 12 lines, a second OnDeath adds none, the 18 keys in order |

## RPC census

1.28.0, tested on a copy of the live world taken the same evening (game 1.0.20, the census on, the rest of the
configuration at its defaults) with two simulated players. Player 0 sent, the way a client's game does: the game's own
`RPC_DamageText` to player 1; a call a server plugin had registered (the test kit's); `VPlusMapSync` (Valheim Plus) to
everybody; an unknown call five times to the server; an unknown call on a workbench; the game's `RPC_Damage` on an object
that was gone; and on the connection itself an unknown call, the ping and `ServerSyncedPlayerData` (the reference
position). Then player 0 left, player 1 sent the unknown call twice and left, and the file was read back and written again.
All 14 checks passed:
- Noted were exactly the four unknown ones, each once with its count, kind, object and words (`VPlusMapSync` by name and
  mod, the others by hash); nothing of the game's own, nothing the plugin had registered, nothing from player 1 before it
  sent anything.
- What had somewhere to go got there: the plugin's handler its words, player 1 the `VPlusMapSync`, the server player 0's
  new reference position.
- Player 0's visit left the census when they left, and the file held their four lines (`VPlusMapSync` x1, the unknown one
  x5); player 1's line of its own (x2) came next to them, player 0's stayed as they were; read back and written again,
  the file was the same.

The table of names: the game's own were read out of its decompiled code (183) and checked against a scan of the game's
assembly by the same scanner that read the mods (it found all 183, and 4 strings that are no calls). Of the 7 mods' names
seen on a live server before, the scan of the 300 mods found 5 on its own (the other two are made while the game runs, and
are in the table by hand).

The regression with this build: the tests of 1.27.1 again with the census off -- ward guard on and off, portal lock on and
off, guardian stones (new and old), 20 portals (new and old), ward limit on and off, the console (22 checks), decay on the
live copy (the DryRun report) and its test on the live copy and on pristine15 (110 checks each) -- gave the same lines as
with 1.27.1, but for session ids, times and which pass a piece with a random due time fell in. The anti-cheat test, 30
players, with the census on: 41 checks passed, and the census noted nothing (every call of the game's own). The census test
on the same build: 14 of 14.

## Decay

1.29.1 (8b9fbb7b), two changes: the base is grouped by claim (key and the time it lapses), and the tick runs the pass over
the world in slices. The decay test of the test kit (`decayrun.sh`, `[Decay] Mode = On`, `ActiveMinutes = 0`, 400 s) has
118 checks now (110 before); 118 of 118 on a copy of the live world (7,018 pieces of players) and on the pristine copy
with the game's own world. New in the test:

- Two claims under one ward, the ward made first and made last (the builder is exempt, so his own pieces never lapse, the
  stranger's pieces lapse): the strangers' walls are marked, the builder's ward and walls are not, one episode for each
  ward, and two more passes change nothing. The same test on 1.29.0 (e9e7d178) fails these three checks: with one ward
  the base began and also marked the builder's pieces (and unmarked them in the next pass), with the other nothing began.
  (The test first used a site that an earlier step of the test had built on: the check that the site is free does not
  count the test's own pieces. Both sites are now far from every other.)
- The pass in slices: the pass the tick runs (slice of 1 ms, to get more than one) took 19 to 20 slices; its report is,
  line for line, the report of the whole pass at once; the longest slice was 1.8 ms on the copy of the live world and
  4.6 ms on the pristine one, against 50 ms and 20 ms for the whole pass at once (the work is the same, 22 and 26 ms).
  (The test kit runs it with a slice of 1 ms; the default of 4 ms was not timed on its own.)

The live configuration (`Mode = DryRun`, the defaults) on the copy of the live world, 150 s: 86 bases, 521 lines of
dry-run report, identical to 1.29.0 line for line. (Run with a builder who is exempt, the two builds differ: that is
the fix.) The first pass after a start is still one go (some 85 ms), as before.

## Web map

1.29.0, tested on a copy of the live world (891,000 objects, 85,788 of them in the index, game 1.0.20) on server copies,
with simulated players from the test kit (`webmaptest`). What the plugin wrote is checked by `webmapcheck.py`, which works
every expectation out on its own from the schedule of the run and the kit's log (numpy, float32 like the game; no code of
the plugin is used). Runs A to E use `Interval = 5` (the default is 30) and ask for everything to be public:
`OnlyPublicPositions = false`, `PortalPrefix = *`, `Bases = true`, with `Directory` and `PublicDirectory` set; run G leaves
every setting at its default. Three players: fake0 stays at the start, fake1 and fake2 go to far places and back; fake2's
name holds a tab, a backslash, a quote, U+2028, an accented letter and an emoji; each has a host name of its own, as a Steam
ID is.

Run A, nothing kept before, 63 of 63 checks:
- The three bins hold exactly the cells the independent reference marks (506, 506 and 609 after the visits; the first
  scenes 253 = the game's disc of radius 9, a far place 506, back again still 506). The kit also called `Explore` with 22
  positions on a scratch map; the plugin's cells, their box and their checksum equal the reference every time: the middle
  of the map (253 cells), the corners (73), the edges where the disc is cut (117, 100), -12400 (1 cell) and beyond it (0),
  `NaN`, both infinities, 1E+30 and `float.MaxValue` (0 cells, no error), and the points where the rounding ties
  (`Mathf.RoundToInt` rounds half to even).
- `index.tsv`: the accounts, their cells, four fields a row, the odd name with the tab and the line separator turned to
  spaces.
- `live.json`: `v`, `world` (a string of digits), `time`, `online` 3, the three players with their positions, the public
  portals (3, those tagged `*`) and the base squares (14 listed of the 36 that hold a builder's piece) equal what the kit
  counted on its own; no raw line separator or control character in it; the world's name is in no file. **The seed is not in
  it**: the number of the seed is nowhere in the file, and the `world` id equals the one in `places.json`.
- `places.json` (private folder only): the seed and the world id, 12,260 places as the kit counted them, 8 unique, the 8
  `defeated_` keys. The placed count of the first file may be up to 100 fewer than the kit's (it is written before the
  zones round the players are loaded); after the 5-minute rewrite it is equal (2,391 of 2,391 in run B).
- No temporary file, no `.bad`, no exception; the periodic save wrote the bins (74 s before the stop); a visit that began
  4 s before the stop is in the bin, written by the last write ("written before the world is saved or the server stops").

Run B, a second run on the files of A, `OnlyPublicPositions = true`, two players, 38 of 38: the maps of A were loaded and
grew by this run's visits (1,012, 506 and 609 cells); the account that stayed away kept its row in `index.tsv` as it was,
with its old "last seen"; `live.json` lists only the player who shows its position while `online` says 2.

Run C, a third run after two bins were damaged (one cut at 100 bytes, one not a gzip at all), 39 of 39: both were kept as
`.bad` with one warning each and those accounts started again; the intact map was loaded as it was (1,012 cells).

Run D, the feature off, 3 of 3: no folder made, no line in the log. Run E, a `Directory` that cannot be made, 3 of 3: one
warning and nothing else, nothing in the public folder either.

Run G, only the defaults (`Enabled = true` and `Interval = 5`; no folder, nothing else in `[WebMap]`), 63 of 63: the plugin
made `<world>.webmap` next to the world and the folder `public` inside it; `live.json` is in `public` and `public` holds
nothing else (`places.json` and `explored/` are one folder up, out of what a web server pointed at `public` can reach);
`live.json` lists only the one player who shows their position while `online` says 3; the 3 portals tagged `*` that the
world has are not listed (`"portals":[]`); `bases` is `null` while the world has 14 squares with a base, and the log has no
count of the pieces at all (nothing was counted); the log says what is public ("no portals, no base squares"). The file is
147 bytes. The maps, `index.tsv` and `places.json` are the same as in run A.

Cost: the count of the pieces on the live copy (85,788 entries, 7,018 pieces with a builder, 36 squares) took 7 ms in all,
the worst frame 2.3 to 2.4 ms (20,000 entries a frame), and it is not run at all with `Bases = false`.

The regression with the final build (the DLL whose SHA-256 begins `e9e7d178`): the index test, 17 checks passed (the baseline of
1.28.0 had 16; the kit has gained a check on the second pass since, the index code is the same), among them the check of the
whole index (891,161 objects in 50 frames, 26.8 ms in all, 1.01 ms in the worst frame); the console test, 22 checks, 0 failed,
its whole summary (135 lines, the messages of the console too) the same as with 1.28.0 once the numbers and the hash of the DLL
are left out. Runs A to G, the index test, the console test and the greeting test below were all made with this one build.
The other tests (ward guard, portal lock, decay, anti-cheat, census) were not repeated for this build: with `[WebMap]` off
nothing of the feature runs (run D).

Not tested: real players' clients, the live server, a web server that holds `live.json` open on Windows (the retry in the
write is for that), and the program that will read these files (it is not part of the plugin).

## Character guard: the greeting

1.29.0, the default text of `[CharacterGuard] Welcome` (off by default) names no server any more: the title is `Welcome` and
there are no lines (it said "Welcome to Sarkastic.gg" and explained one server's zones and fire). Tested with `welcomerun.sh`
on a copy of the live world, the guard and `Welcome` on, with two simulated new characters that the guard lets in (they are
ready for `FreshEvent`, so they are "new and fresh"); the kit logs what each of them was shown.

- W1, a config without the text keys: both are greeted 6 s after they are let in ("welcomed fake0", "welcomed fake1"). Each was
  shown the title `Welcome` in the middle of the screen and nothing in the top left corner (the middle text is sent again every
  second while it is held, three times in all). The config the plugin wrote holds `WelcomeTitle = Welcome` and an empty
  `WelcomeLines`.
- W2, a config that holds the old text, as the live server's does (the title and four lines): each was shown that text as it
  is, the title in the middle and the four lines in the top left corner. A config that already has the keys is not touched by
  the new defaults.

## Server access

1.30.0, `[Server] MaxPlayers` and `PublicWithoutPassword`, game 1.0.20, on a copy of a test world.

Startup: the server was started four times on a public port, and once the world was up the test asked it, as Steam's
server list does (A2S_INFO), how many players it takes and whether it has a password:
- P1, both off, `-public 1` and an empty password: the game refused to start ("Error bad password: The password is too
  short"), as without the plugin.
- P2, `PublicWithoutPassword = true` and `MaxPlayers = 20`, `-public 1` and an empty password: the server started, logged
  both features ("Player limit: 20 players may be in the game at once (the game: 10)", "Public server without a password"),
  and told the server list 20 players and no password.
- P3, both off, `-public 1` with a valid password: 10 players and a password, which is what the question shows for the
  game's own server (the control for P2).
- P4, `PublicWithoutPassword = true` with the password `abc`: refused as too short, so a password that is given still has
  to meet the game's rules.

The same on the Windows build of the game, on a copy of the live server's install (its game files and BepInEx, none of
its saves, configs or plugins), started the way AMP starts it, one argument string with `-password "" -public 1`:
- W0, 1.30.0 before the fix, `PublicWithoutPassword = true`: the server came up, but the "Public server without a
  password" line never came and the server list showed a password. The game's player on Windows had left out the empty
  argument, so the game took `-public` for the password.
- W1, with the fix, `PublicWithoutPassword = true` and `MaxPlayers = 20`: "Command line: read as Windows split it ...
  after -password", "Public server without a password", and the server list showed 20 players and no password.
- W2, only `MaxPlayers = 20`: 20 players and a password, as the game itself does on Windows (the control for W1).
- W3, `PublicWithoutPassword = true` with the password `selftest123`: a password, and no "Command line" line, so with no
  empty argument the game's own list is read.
- P2 again on Linux with the fixed build: 20 players and no password, and no "Command line" line (elsewhere than on Windows
  the game's own list is read).

Joining: the test kit put N simulated players in the game, ran the game's own count of players, and then sent one more
connection through the game's own `ZNet.RPC_PeerInfo` with the information a client sends (its id, version, name, the
hash of the right password). Refused means the game sent error 9 and logged "server is full", and the connection got no
name; let in means no error and the connection holding its id and name, one more player counted. The connection was
removed afterwards. All 26 checks of the four runs passed:

| | MaxPlayers | players in | the next one |
|---|---|---|---|
| A | (not set: the game's 10) | 10 | refused |
| B | (not set) | 9 | let in |
| C | 20 | 10 | let in |
| D | 20 | 20 | refused |

A and B are the game's own limit with the plugin in place, and C, with the same 10 players as A, lets in the one that A
turns away. The client's side of the server list was read in the game's code: it shows the count and the limit the server
reports and does not keep anyone from joining.

With the defaults neither feature patches anything (P1, A and B: no "Player limit" line), so the other tests were not run
again for this build. The joining runs were made before the Windows fix, which changes only `PublicWithoutPassword`.
