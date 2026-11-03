# Web map

`[WebMap] Enabled = true` makes the server write what a map of the world needs, as files. It is a feed, not a map: no map,
no web page and no login come with it, and it sends nothing anywhere. A program of yours (a web server and a page, a script)
reads the files and shows them as you like, and anyone can build their own map on them. Players keep the vanilla game.

By default very little is public: only the players who switched on "show my position" in the game, no portals, no bases.
Everything else you have to ask for (`PortalPrefix`, `Bases`, `OnlyPublicPositions = false`).

## What is written

| File | In | Public | When |
|---|---|---|---|
| `live.json` | `PublicDirectory` | the one file a web server may hand to anyone | every `Interval` seconds (30) |
| `places.json` | `Directory` | no | at the start, then every 5 minutes |
| `explored/<account>.bin` | `Directory` | no | maps that changed, at most once a minute, and when the world is saved or the server stops |
| `explored/index.tsv` | `Directory` | no | with the maps |

`Directory` empty: `<world>.webmap` next to the world. `PublicDirectory` empty: the folder `public` inside `Directory`. So by
default `<world>.webmap/public/live.json` is the file for a web server and `<world>.webmap/places.json` and
`<world>.webmap/explored/` are not: **serve the folder `public` and nothing above it**. The world's name is in no file.

### live.json

```
{"v":1,"t":<unix s>,"world":"<id>","time":<game seconds>,"online":3,
 "players":[{"n":"name","x":123.4,"y":31.2,"z":-567.8}],
 "portals":[{"t":"*tag","x":123,"z":-567}],
 "bases":{"cell":512,"min":15,"cells":[{"i":-3,"j":2,"n":142}]}}
```

- `t` is when the file was written. A file older than a few `Interval`s means the server is down; when it stops, the last file
  lists nobody.
- `world` is an id of the world (a string, a number would not survive JavaScript): when it changes, the world is another one
  and what you kept of the old one (a picture of its map) is not its. It is not the seed: whoever has the seed can make the
  whole world, the places of its secrets included, on their own computer. The seed is in `places.json`.
- `time` is the game's clock in seconds, from which the time of day follows.
- `players`: everyone online with a character, by the position the game itself uses for the player (`GetRefPos`), or with
  `OnlyPublicPositions = true` (the default) only those who have switched on "show my position" on the game's map; `online`
  counts everyone.
- `portals`: only those whose tag starts with `PortalPrefix` (empty by default: none). The portal lock's `PublicPrefix` is `*`
  by default; use the same prefix to list the portals their owners have opened to everyone.
- `bases`: with `Bases = true`, the player-built pieces that have a builder, counted per square of `BaseCellMetres` (`i` =
  floor(x / cell), `j` = floor(z / cell)); squares with fewer than `BaseMinPieces` are left out. No exact place, no builder's
  name. `null` when `Bases` is off, and until the first count is done (a few seconds after the start); recounted every 5
  minutes, a slice of the world index per frame.

### places.json (private)

`{"v":1,"t":..,"world":"<id>","seed":<int>,"defeated":[..],"places":[..]}`: the seed, the world keys of the bosses beaten, and
every location the world has, placed or not: `{"n":"Crypt2","x":..,"z":..,"p":placed,"u":unique,"i":iconPlaced,"a":iconAlways}`.
It is the world's secrets: it is for a program that shows a player only what they have found. The seed is what a tool that draws
the terrain of the world needs.

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
line first. It and the file names hold the players' ids: private. A map is held in memory only while its player is online; a
file that cannot be read is kept as `.bad` and that account starts again.

## Making a map of it

The coordinates are the game's: metres, `x` to the east, `z` to the north, `y` up. The game's own map is a grid of 2048 x 2048
cells of 12 m, the middle of the world at cell (1024, 1024): `column = round(x / 12 + 1024)`, `row = round(z / 12 + 1024)`
(rounded half to even). A picture of the world drawn on that grid takes the players, the explored cells and the squares of
`bases` without any other conversion (a base square `i`, `j` of `cell` metres starts at `x = i * cell`, `z = j * cell`).

```python
import gzip
import numpy as np
from PIL import Image

raw = gzip.decompress(open('explored/<account>.bin', 'rb').read())
grid = np.unpackbits(np.frombuffer(raw, np.uint8), bitorder='little').reshape(2048, 2048).astype(bool)   # grid[row, column]
Image.fromarray(grid[::-1].astype(np.uint8) * 255).save('explored.png')                                  # flipped: north at the top
```

What the server does not do is draw the terrain: the picture under the players is yours to make from the seed in `places.json`
with whatever tool you like, and so is the page that shows it. A page that shows `live.json` only has to fetch it every
`Interval` seconds, from the same address as the page, or from another one if the web server sends the
`Access-Control-Allow-Origin` header.

## Settings

| `[WebMap]` | Default | |
|---|---|---|
| `Enabled` | false | Needs a restart. |
| `Directory` / `PublicDirectory` | empty | See above. |
| `Interval` | 30 | Seconds between two writes of `live.json` (5-600). |
| `OnlyPublicPositions` | true | List only the players who show their position. `false`: everyone online, whatever they chose in the game. |
| `PortalPrefix` | empty | Portals whose tag begins with this are listed. Empty: none. |
| `Bases` | false | List the squares in which players have built. `false`: the pieces are not even counted. |
| `BaseCellMetres` | 512 | Side of the squares of `bases` (128-2048). |
| `BaseMinPieces` | 15 | Squares with fewer pieces are not listed (1-1000). |
| `ExploreRadius` | 100 | Metres explored round a character (20-300). |
| `Places` | true | Write `places.json`. |

## Cost and safety

The game's thread never waits for a disk: files are written by one background thread from a copy, each through a temporary
file that replaces the old one (the retry is for a web server on Windows that has the old file open). The exception is the
last write, before the world is saved or the server stops, which waits for at most 10 seconds. The count of the pieces
takes a slice of the world index per frame; on a world of 85,788 index entries (7,018 pieces with a builder) it took 8 ms in
all, the worst frame 3 ms. A failing disk gives a few warnings in the log, never an error in the game's loop; a position that
is not a number marks nothing.
