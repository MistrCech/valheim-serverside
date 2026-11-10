# Death log: one line per player death

`[DeathLog] Enabled = true` (off by default; needs a restart) makes the server write every player's death to `<world>.deaths.log`, next to the world save (the same folder as `<world>.guard.log`): UTF-8, LF, appended, one JSON object per line. It also writes one line to the BepInEx log per death: `Death log: <name> died: <cause> <killer, hit type or effect>`. Nothing is dropped or changed; with the feature off no patch is applied and nothing polls.

## Why the cause is inferred

A player's game decides the damage and the death. When it dies, `Player.OnDeath` sets `dead` in the character's object (ZDO) and sends the routed RPC `OnDeath` to everybody; the cause (`m_lastHit.m_hitType`) goes only into the player's own profile stats. The server therefore reads the cause from three things it does see:

1. **Hits of the server's own things on the character.** The server simulates creatures and world objects; their attacks, projectiles and area damage call `Character.Damage` on the server for the player's instance. Noted: the time, the attacker (prefab, `m_name`, level, boss, or a player), the hit type, the fire and poison parts.
2. **Hits sent by another player's game**, which go through the server as the routed RPC `RPC_Damage` from the sender's connection to the victim's: a PvP hit (it names the sender's own character as its attacker), a hit of a creature that game simulates (it names the creature; prefab, `m_name`, level and boss are read from the creature's ZDO), or a hit without an attacker (a cart or a boat that game simulates). A hit that names another player's character is ignored. And **the death**, the routed RPC `OnDeath` from the dying player's connection with its own character as the target.
3. **The character's ZDO**, sent by the player's game: position, stamina, health, `dead`. Polled every 0.25 s for every online player; the last 6 s are kept. A character whose `dead` turns true without the RPC is a death too.

Per character only small rings are kept (hits of the last 30 s, samples of the last 6 s); the state of a player who left is dropped. One line per character object, whichever of the RPC and the flag comes first (a respawn makes a new character object). Hits another prefix dropped (the anti-cheat's Damage rule) are not counted.

## The rules

The first rule that fits, at the time T of the death:

| # | cause | rule |
|---|---|---|
| 1 | `player` | a hit of another player (PvP) on this character within 3 s before T |
| 2 | `creature` | a creature's hit within 3 s (the server's own creature, or one another player's game simulates); the latest one |
| 3 | `object` | a hit without an attacker within 3 s (the server's object, or one another player's game simulates); `hit` is its type (`Tree`, `Turret`, `Cart`, `Structural`, `Stalagtite`, `Catapult`, `Incinerator`, `DrawBridge`, `CinderFire`, `Boat`, `Impact`, `Undefined`, ...) |
| 4 | `edge` | distance of (x, z) from (0, 0) is 10420 m or more |
| 5 | `boiled` | in water, in the Ashlands (its sea boils) |
| 6 | `drowned` | in water, anywhere else |
| 7 | `fall` | the highest point of the last 4 s minus the height at T is 6 m or more |
| 8 | `dot` | a hit with fire within 8 s, or with poison within 30 s (of a creature, an object or another player): `effect` is `fire` or `poison`, with that hit's attacker (or its hit type) |
| 9 | `lava` | Ashlands and lava under the feet |
| 10 | `frozen` | Mountain or Deep North |
| 11 | `unknown` | none of the above |

"In water": the last two samples are at most 0.3 m above the water level and the ground there is at least 1 m below them.

## The line

```json
{"time":"2026-11-10T18:25:30Z","player":"Blbecek","id":"<account id>","cause":"creature","by":"SeekerQueen","byId":null,"name":"$enemy_seekerqueen","level":1,"boss":true,"hit":"EnemyHit","effect":null,"fell":0.0,"water":false,"biome":"Mistlands","health":12.5,"stamina":40.0,"recent":["SeekerQueen","SeekerBrute"],"pos":[-6784,31,640]}
```

The keys come in this order; a value that does not apply is `null`.

| key | meaning |
|---|---|
| `time` | UTC |
| `player` | the character's name, escaped as JSON (quotes, backslashes, control characters) |
| `id` | the account id, as the guard log writes it (`<name> (<id>, character <n>)`) |
| `cause` | the table above |
| `by`, `byId` | the killer: a creature's prefab, or a player's name and account id |
| `name`, `level`, `boss` | a creature's `m_name` token (`$enemy_troll`), level and boss flag |
| `hit` | the hit type of the deciding hit (`EnemyHit`, `PlayerHit`, `Tree`, ...) |
| `effect` | `fire` or `poison` for `dot` |
| `fell` | the drop of the last 4 s in metres, one decimal, whatever the cause (0.0 if none) |
| `water` | in water by the rule above |
| `biome` | the biome's name at T |
| `health`, `stamina` | the last sample taken before the death was seen |
| `recent` | who hit the player in the last 10 s (a prefab, a player's name or a hit type), the latest first, at most 5 |
| `pos` | `[x, y, z]` in whole metres. It is the place of the death: keep it from the public |

## Limits

The cause is the server's reading, not the game's word.

- What the server cannot see: a player's **own fire, poison or smoke** with no creature behind it (a hearth, a fire arrow shot by oneself; the damage over time is dealt by the player's game); **a cart or a boat** that the player's game simulates (the hit of its collision is made there); **the edge of a cliff** the player stepped or was pushed off by its own movement; any hit a game deals to itself. Such deaths are `fall` when they ended with a drop of 6 m or more, otherwise `unknown`.
- A fall of 6 m or more in the last 4 s outranks `dot`, `lava` and `frozen`: a player who ran down a steep slope and then burned is `fall`.
- A poisoned or burning player is `dot` only when the fire or poison came with a hit the server saw (a creature's, an object's, another player's); the player's own fire is not seen.
- A hit that arrives later than 3 s before the death (a long fall of health from a bleed, a late poison tick) is attributed by the `dot` rule or not at all.
- The position a player's game sends is not checked; a modified game can write what it likes.
- A death of a character that is not the online player's current character is ignored; so is one the server never saw alive (a player who joins with a dead character).
