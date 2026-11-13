# Activity log: what players do, one JSON line at a time

`[ActivityLog] Enabled = true` (off by default; needs a restart to switch on) makes the server write what it can see of every player's play to one file per day, in the folder `<world>.activity` next to the world save (the same place as `<world>.guard.log`). One JSON object per line. It is for studying how players play, for example to tune the anti-cheat: how fast people really move, how much damage a weapon really does, how the health of creatures falls.

With `Enabled = false` nothing of it runs: the patches return at once, nothing polls and no folder is made. Server only.

Only the server's view is in it. A player's game does a lot without telling the server (see "What the server cannot see").

## Config

| setting | default | what it does |
|---|---|---|
| `[ActivityLog] Enabled` | false | Write the log. Needs a restart to switch on. |
| `[ActivityLog] Days` | 7 | Keep the files of the last this many days (today included), 0..365. Older ones are deleted when the server starts and when a new UTC day begins. 0 keeps everything. |
| `[ActivityLog] PositionSeconds` | 5 | How often a `pos` line is written for each online player, 1..60. |
| `[ActivityLog] MaxMegabytesPerDay` | 200 | When a day's file reaches this size, only `start`, `join`, `leave`, `spawn`, `died`, `mobend`, `guard` and one `cap` line are written for the rest of that day. 0 = no limit. |

## Files

One file per UTC day, `yyyy-MM-dd.jsonl`, UTF-8 without BOM, LF line ends, in `<world>.activity`. A line belongs to the file of the UTC day of its own `t`. Lines are kept in memory and appended once a second (open, append, close: nothing holds the file open), when the world is saved and when the server shuts down.

Retention touches only files whose name is exactly `yyyy-MM-dd.jsonl` and whose date is older than today minus (`Days` - 1). Anything else in the folder stays.

Numbers: positions are `[x, y, z]` with one decimal, distances, damage, health and stamina one decimal, always with a dot. `null` is "not known". Names are cleaned like the death log's (control characters, U+2028 and U+2029 become spaces) and escaped as JSON.

## The lines

Every line starts with `"t"` (UTC, `yyyy-MM-ddTHH:mm:ss.fffZ`) and `"ev"`. Lines about a player then have `"p"` (name), `"acc"` (account, as the death log writes it, for example `steam_7656...`) and `"pid"` (the character number the guard log writes after "character"; null until the character exists).

| `ev` | when | fields after `p`, `acc`, `pid` |
|---|---|---|
| `start` | the feature starts on a server with a world (no player) | `version`, `days`, `posSecs`, `capMB` |
| `join` | a peer with a name appears | |
| `leave` | it goes away (once; its building, unbuilding and pickup totals are written first) | `secs` (session length) |
| `spawn` | the player's own character is new in this session (first spawn and every respawn) | `pos` |
| `pos` | every `PositionSeconds` for each player with a character | `pos`, `v` (horizontal m/s over the window), `vmax` (largest horizontal speed of the 1-second samples in it), `hp`, `hpmax`, `st` (stamina), `eitr`, `dead`, `r`, `l` (right and left item). A player who stays within 0.5 m with the same health, stamina and items gets one line a minute. |
| `equip` | one of the seven worn or held items changes (checked every second; not on the first sample after a spawn) | `r`, `l`, `chest`, `legs`, `helmet`, `shoulder`, `utility` (item names or null) |
| `jump` | between two 1-second samples of the same character the distance is over 40 m and it is not dead (portals, dungeon doors, teleports) | `from`, `to`, `dist`, `dy`, `secs` |
| `died` | the character's dead flag turns true (the cause is the death log's job) | `pos`, `hp` (the last health seen before) |
| `hit` | a hit sent through the server by this player's game (`RPC_Damage` naming the player's own character as attacker) | `target` (prefab), `tlevel`, `tplayer` (only when the target is a player), `towner` (`server`, the owner's name or `?`), `skill`, `slvl`, `ilvl`, `dmg`, `types` (every damage type that is not zero), `ranged`, `dist` (attacker to the hit point), `st`, `r`, `backstab` (only when the bonus is not 1) |
| `hurt` | damage taken: the server's own `Character.Damage` on the player, or an `RPC_Damage` on the player whose attacker is a creature another player's game simulates | `by` (attacker prefab, or the hit type without an attacker), `blvl`, `dmg`, `types`, `hp` (health before), and `via` (the sending player's name) in the second case |
| `mobhp` | a creature lost health (see below) | no `p`, `acc`, `pid`: `by`, `acc`, `target`, `tlevel`, `hp0`, `hp1`, `drop`, `hpmax`, `pos`, `dist`, `boss`, `tamed`, `hitby` |
| `mobend` | a creature that had a `mobhp` line is gone from the server | no player: `target`, `tlevel`, `hpmax`, `total`, `n`, `big`, `secs`, `last_hp`, `died`, `by` (name to total), `near` (players within 40 m), `boss`, `pos` |
| `build` | pieces created by this player, added up over 60 s from the first | `prefab`, `n`, `pos` (the first) |
| `unbuild` | pieces destroyed by this player's game, added up the same way: taken down with the hammer, or broken while this game owns them (by its player, by creatures, by falling apart) | `prefab`, `n`, `own` (how many were built by this player) |
| `pickup` | dropped items destroyed by this player's game, added up the same way: mostly picked up, but an item that despawns or burns while this game owns it counts too | `prefab`, `n` (objects), `items` (their stacks added), `pos` |
| `guard` | every line the guard log (`<world>.guard.log`) gets (no player) | `line` (the text without the timestamp) |
| `cap` | the day's file reached `MaxMegabytesPerDay` (no player) | `mb` (the limit), `bytes` (the size reached), `day` |

### mobhp: health a creature loses in its owner's game

In the game, a creature is simulated by one player's game, the owner's, and the hit that hurts it is dealt there; the server hears nothing of it. All the server sees is that the owner's game writes the creature's new health into the creature's object. So `mobhp` is written when a peer's data changes a creature's health from above zero to lower. It is health lost from any cause in that game: a hit of that player or of another, fire, poison, a fall, another creature. `by` is the player whose game wrote it, not necessarily who struck. `dist` is from the creature to that player's character. For the creatures the server owns itself, `by` is `server`, `acc` and `dist` are null and `hitby` names the attacker when it is known.

### mobend: how it ended

The server keeps every creature that had a `mobhp` line, and each second looks whether it is still there. When it is gone, `died` says whether a ragdoll lay within 6 m of where it was last seen (a creature that was only unloaded or despawned has none). A creature that loses no health for 5 minutes is forgotten without a line. At most 5000 are kept.

### Hits and what they are worth

`hit` is written only for hits that go through the server: a player's game sends them to the game that owns the target. The `dist` of a ranged hit is to the hit point, not to the target's centre. `dmg` is the sum of the damage as the sender's game filled it in; the target's resistances come later, in its owner's game, and show in `mobhp`.

## What the server cannot see

Written down so the log is not read for more than it holds.

- A hit a player's game deals to a creature that game owns itself (the common case) is seen only as the `mobhp` it causes. There is no `hit` for it.
- Crafting, cooking, the inventory, its moves and what the player carries: all inside the player's game. Not seen.
- Terrain changes and picking plants: only partly visible, not logged.
- Chat: not logged, on purpose.
- Anything a game does without a write or a message to the server.
