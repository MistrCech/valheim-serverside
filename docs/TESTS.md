# Tests

What the larger features of the plugin were tested with, in full (the README keeps a summary).

## Ward guard

Tested on a copy of the live world with three simulated players -- a ward's owner, a stranger and a friend put on the ward's list in the middle -- sending each request as a client does, in four runs: guard off (the same requests all went through: the stranger damaged and took down the wall, opened the chest, used the door, switched the fire pit off, claimed the bed and switched the ward off as its owner), On, LogOnly (everything went through, each refusal logged as "LogOnly: let through") and with `DestroyRequests` On. With it on: the stranger's and the friend's hits left the warded wall untouched, a wall outside every ward was hit as before, and the owner's hit, the friend's after he was put on the list, a creature's hit (a creature of the sender's game), a hit with no attacker and an admin's went through; a creature of another player's game named as the attacker, `RPC_Remove`, the chest (it answered "no"), the door, the fire pit's switch and the bed were refused, ore put into the smelter was taken; a hit sent as another player and a ward switched off in the owner's name were dropped; with `DestroyRequests` On a request for a wall the stranger does not own was taken out, and of a request for two objects only the foreign one. Finding the wards among 349 000 objects takes 6.5 ms every 5 seconds. Then with a real, unmodified Valheim 1.0.20 client (headless, its own profile) standing 1.5 m from a warded wall and hitting it with its bare hands: with the guard off the wall was down to 91 health after the first round and gone after the second; with it on, in the first round (not on the ward's list) the wall stayed untouched and the server logged "tried to hit woodwall ... refused", and in the second (written on the list) the wall was hit as in the game. The client played on for a minute with Spoofing, Structures and Interactions on and was never refused anything else.

### Outside wards (1.31.0)

`[WardGuard] OutsideWards`, tested on the copy of the live world of 1 November (game 1.0.20) with the same three simulated players and three more walls 66--74 m from the ward (the owner's, the stranger's own, one with no creator), in three runs: OutsideWards On, the guard as it runs live (OutsideWards Off) and the guard off. With it On a stranger's hit on the owner's wall outside the ward and his hammer on it were refused (the wall untouched, still standing; guard log: "fake1 (222) tried to hit woodwall ...; built by fake0 (111), no ward there -- refused"), and so were his hits on a wall inside the ward once it was switched off and on the switched-off ward itself; the builder, a friend on the list of the builder's ward (also of the switched-off one), a repair by the stranger, a hit with no attacker, the stranger's creature, his own wall and a wall nobody built all went through. With it Off and with the guard off every one of them went through, and the fifteen earlier steps of the ward guard test gave exactly what they gave on 1.26.0 on 1 November (the same world, both modes). No exceptions in any run.

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
