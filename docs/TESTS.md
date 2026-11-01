# Tests

What the larger features of the plugin were tested with, in full (the README keeps a summary).

## Ward guard

Tested on a copy of the live world with three simulated players -- a ward's owner, a stranger and a friend put on the ward's list in the middle -- sending each request as a client does, in four runs: guard off (the same requests all went through: the stranger damaged and took down the wall, opened the chest, used the door, switched the fire pit off, claimed the bed and switched the ward off as its owner), On, LogOnly (everything went through, each refusal logged as "LogOnly: let through") and with `DestroyRequests` On. With it on: the stranger's and the friend's hits left the warded wall untouched, a wall outside every ward was hit as before, and the owner's hit, the friend's after he was put on the list, a creature's hit (a creature of the sender's game), a hit with no attacker and an admin's went through; a creature of another player's game named as the attacker, `RPC_Remove`, the chest (it answered "no"), the door, the fire pit's switch and the bed were refused, ore put into the smelter was taken; a hit sent as another player and a ward switched off in the owner's name were dropped; with `DestroyRequests` On a request for a wall the stranger does not own was taken out, and of a request for two objects only the foreign one. Finding the wards among 349 000 objects takes 6.5 ms every 5 seconds. Then with a real, unmodified Valheim 1.0.20 client (headless, its own profile) standing 1.5 m from a warded wall and hitting it with its bare hands: with the guard off the wall was down to 91 health after the first round and gone after the second; with it on, in the first round (not on the ward's list) the wall stayed untouched and the server logged "tried to hit woodwall ... refused", and in the second (written on the list) the wall was hit as in the game. The client played on for a minute with Spoofing, Structures and Interactions on and was never refused anything else.

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
