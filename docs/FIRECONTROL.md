# Fire control: how fire spreads

The game's side of `[FireControl]` (the README keeps what the settings do).

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

**Who simulates a fire.** Sparks fly, and fireplaces ignite, only on the machine that owns the object, and the server only takes over what nobody owns or what its owner has left. What a player builds or sets alight is created and owned by their own game: without more, a hearth a player has just built, or the fire their arrow started, would burn by the game's rules on that player's computer until they left the area or the server restarted -- in 1.13 and 1.14 none of the settings above reached them. Now the server takes every fire source (every fire, and every fireplace that ignites or throws sparks: 15 kinds) the moment a player's game reports it, whether new, an update from a game that has not heard the news yet, or a fireplace a player merely claimed (pressing E on one nobody owns). That game stops simulating it long before its first spark (2-5 s) or direct ignition (5 s). A fire taken over this way that has no record of its chain was lit by a projectile on that player's computer and counts as one, its own position as the source -- up to 2.9 m from where an arrow, 5.0 m from where a fireball came down, which `MaxRadius` therefore allows on top. A fire that appears within 8 m of a creature that throws sparks (the summoned troll) is not from a projectile, and with `OnlyProjectiles` it is put out. The server's copy of a fire finds the piece, tree or log it burns on, so it goes out when that is gone, as on the game that lit it. What stays on the player's computer is their own arrow or fireball and the sparks it throws where it lands.
