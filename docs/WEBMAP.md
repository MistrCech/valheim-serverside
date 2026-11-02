# Web map

`[WebMap] Enabled = true` makes the server write what a web map of the world needs, as files. It sends nothing anywhere: a
program of yours (a web server, a script) reads the files and serves them. Players keep the vanilla game.

## What is written

| File | In | Public | When |
|---|---|---|---|
| `live.json` | `PublicDirectory` | the one file a web server may hand to anyone | every `Interval` seconds (30) |
| `places.json` | `Directory` | no | at the start, then every 5 minutes |
| `explored/<account>.bin` | `Directory` | no | maps that changed, at most once a minute, and when the world is saved or the server stops |
| `explored/index.tsv` | `Directory` | no | with the maps |

`Directory` empty: `<world>.webmap` next to the world. `PublicDirectory` empty: the same folder. Put `PublicDirectory` in the
folder your web server serves and `Directory` outside it. The world's name is in no file.

### live.json

```
{"v":1,"t":<unix s>,"seed":<int>,"time":<game seconds>,"online":3,
 "players":[{"n":"name","x":123.4,"y":31.2,"z":-567.8}],
 "portals":[{"t":"*tag","x":123,"z":-567}],
 "bases":{"cell":512,"min":15,"cells":[{"i":-3,"j":2,"n":142}]}}
```

- `t` is when the file was written. A file older than a few `Interval`s means the server is down; when it stops, the last file
  lists nobody.
- `players`: everyone online with a character, by the position the game itself uses for the player (`GetRefPos`). With
  `OnlyPublicPositions = true` only those who have switched on "show my position" on the game's map; `online` counts everyone.
- `portals`: only those whose tag starts with the portal lock's `PublicPrefix` (`*`), which the owners made open to everyone.
- `bases`: the player-built pieces that have a builder, counted per square of `BaseCellMetres` (`i` = floor(x / cell), `j` =
  floor(z / cell)); squares with fewer than `BaseMinPieces` are left out. No exact place, no builder's name. `null` until the
  first count is done (a few seconds after the start); recounted every 5 minutes, a slice of the world index per frame.

### places.json (private)

Every location the world has, placed or not: `{"n":"Crypt2","x":..,"z":..,"p":placed,"u":unique,"i":iconPlaced,"a":iconAlways}`,
and `defeated`, the world keys of the bosses beaten. It is the world's secrets: it is for a program that shows a player only
what they have found.

### explored/&lt;account&gt;.bin (private)

What the game shows as a character's explored map lives in the player's game only and is never sent to the server. The server
makes it again from where the character is: every 2 seconds it marks a disc of `ceil(ExploreRadius / 12)` cells (9 for the
game's 100 m) of a 2048 x 2048 grid of 12 m cells, exactly the disc `Minimap.Explore` marks. The file is the gzip of the
524,288 bytes of the grid, bit `y * 2048 + x` in byte `i >> 3`, bit `i & 7`, `x = round(X / 12 + 1024)`, `y = round(Z / 12 +
1024)` (south at y = 0):

```python
np.unpackbits(np.frombuffer(gzip.decompress(data), np.uint8), bitorder='little').reshape(2048, 2048)
```

The account is the one the player connects with (the host name of the socket: on Steam the SteamID64), not the character.
Therefore:

- a player's map starts when the feature is switched on; where they went before, the server does not know until they go again;
- what a cartography table or shared knowledge gives a player is not counted, only their own steps.

`index.tsv` lists the accounts: `account`, `name`, `cells explored`, `last seen online (unix s)`, tab separated, a header
line first. A map is held in memory only while its player is online; a file that cannot be read is kept as `.bad` and that
account starts again.

## Settings

| `[WebMap]` | Default | |
|---|---|---|
| `Enabled` | false | Needs a restart. |
| `Directory` / `PublicDirectory` | empty | See above. |
| `Interval` | 30 | Seconds between two writes of `live.json` (5-600). |
| `OnlyPublicPositions` | false | List only players who show their position. |
| `ExploreRadius` | 100 | Metres explored round a character (20-300). |
| `BaseCellMetres` | 512 | Side of the squares of `bases` (128-2048). |
| `BaseMinPieces` | 15 | Squares with fewer pieces are not listed (1-1000). |
| `Places` | true | Write `places.json`. |

## Cost and safety

The game's thread never waits for a disk: files are written by one background thread from a copy, each through a temporary
file that replaces the old one (the retry is for a web server on Windows that has the old file open). The exception is the
last write, before the world is saved or the server stops, which waits for at most 10 seconds. The count of the pieces
takes a slice of the world index per frame; on the live world (85,788 entries, 7,018 pieces with a builder) it took 8 ms in
all, the worst frame 3 ms. A failing disk gives a few warnings in the log, never an error in the game's loop; a position that
is not a number marks nothing.
