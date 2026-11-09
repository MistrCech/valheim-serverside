using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Anti-cheat for a server whose players run the unmodified game. The server does not see a player's mods (the game sends the
		server nothing about them), and almost every rule of the game is kept by the player's own game: what may be built where, how
		fast, how many seeds it costs, how big a stack may be, whether the character may fly. A modified game just does not keep it.
		What the server does see are the results: the objects a game creates are sent to it, and it can ask of each one whether the
		game could have made it. That is all this does; it is not a scanner of players' computers and it cannot see a cheat that
		leaves nothing in the world and nothing on the wire (a faster run, a free build, a bigger carry weight, a longer reach for a
		pickaxe); a cheat that changes how hard a player hits, how it is hurt, what it pays in stamina or how well it fights is seen
		only by the traces it leaves in what the player's game sends (the combat rules below).

		Every object that a player's game creates (the server sees them arrive in ZDOMan.RPC_ZDOData; whose it is, is the connection it came
		in on, not the id it carries, which is the sender's claim) is looked at a moment later, when its data has come.
		The rules a game that is not modified cannot break:
		- UnknownPrefabs: an object of a kind the server's game has never heard of (a content mod, a build-planner's placeholder).
		- NotBuildable: an object with a creator (only Player.PlacePiece sets it) that no tool of the game places: a thistle, a berry
		  bush, an ore, a mushroom planted with the cultivator (PlantEverything, CropUtils with its compatibility mode), a piece of
		  another mod. The tools of the game are taken from the game's own item database: every piece in the build table of any item.
		- Forged: an object whose id carries the session of another game than the one that sent it (a game that puts its work on someone else).
		- Creator: the creator written on a piece is not the character that sent it (someone else's name on one's own work).
		- Distance: a piece placed far from the player who placed it (the game reaches 5 m and a little more for large things).
		- Rate: plants that were planted less than MinPlaceInterval apart, or at the same moment, by one player. The game plants
		  at most once every 0.4 s, and a plant carries the time at which it was planted, so a slow connection cannot fake this.
		  Several seeds with one press (a row, a grid) are what this catches.
		- Items: an item the player's game dropped, with a stack, a quality or a durability above what the item can have (bigger
		  stacks, EasySpawner).
		- CheatMarks: a creature that a player's game created with the game's own mark for cheating on it (the console's `spawn`).
		  Other objects with the mark (a piece built from items that an admin handed out is marked too) are only logged.
		- Fly: the character's flight switch (`fly` in the console, flight mods that use it) is on. The game writes it to the
		  character so that others see it.
		- Fields: an object with HasFields (per-object overrides of what a component does: invulnerable, invisible, huge). The game writes them for
		  one thing only, the console's spawn command, which is what the admin tools of the build-anything kind use too.
		- Wards, NoBuild, Biome, Dungeon, Cultivated: a piece placed where the game's build mode refuses: in a ward that is switched
		  on and that the player is not on the list of (the server uses the same rule as the ward guard), in a place where nothing may
		  be built, in a biome the piece does not belong to, in a dungeon, and (a plant) on ground that is not cultivated.
		The combat rules (all four Off by default, they only log or kick: there is no object to take away, and admins are exempt). The player's
		game keeps its own health, stamina, skills and damage; the server sees what that game writes to the character (health, stamina, what it
		holds and wears, the switches for blocking and dodging), the hits the game sends to what it hit (a routed RPC with the HitData: the
		damage, the skill and its level, the quality of the weapon) and the hits that creatures the server runs land on the character:
		- Unhurt: a character that hits do not hurt (god mode, damage immunity). A lethal hit that leaves the health at exactly 1.0 (the
		  game's god mode clamps to it), twice in ten minutes; or UnhurtHits hits in a row, from creatures, that could not have been
		  blocked or dodged, worth more than any armour and any protection shield could take (the server reads the largest shield and armour
		  the game's own status effects give), without the health going down, judged on data the player's game sent after the hit. A player who
		  is never hit is never seen. Warns first.
		- Stamina: StaminaAttacks melee attacks in 20 s with a weapon that costs 4 or more while the stamina stays within 1 and the game goes on
		  sending data -- at the hits AND in the half-second samples taken in between (the game waits a second after an attack before the
		  stamina regenerates, so an honest character shows "top minus cost" there even if it is back at the top at every attack). A bow, a
		  spell and fists are not counted. Warns first.
		- Damage: a hit whose damage in all is above MaxDamage, which is 0 (automatic) by default: the strongest hit a weapon a player can make
		  sends, every multiplier at its largest, times 1.5, worked out from the game's items and status effects (logged at start as "Damage
		  bound"). The butcher knife (it works only on tamed animals) has a bound of its own, used when the target is tamed and the knife is in
		  hand. With On the hit is dropped on its way to the target (the one thing here that changes what the server passes on).
		- Skills: a weapon skill level (Swords .. ElementalMagic, not the others) that the character cannot have earned here. Per character the
		  server keeps (<world>.skills.txt) the first level it saw of each skill and the experience of the hits it has seen since; the level a
		  hit carries, less the most that any status effect adds, must be reachable from there with SkillGainFactor times that experience
		  plus SkillAllowance. A new, fresh character (the item ledger's rule, with its GraceHours) starts at 0 and is judged exactly;
		  any other character is judged only on its GROWTH after the first level seen, never on that level (it may have come from another
		  world). A character that brings its skills from another world looks like a cheat on a fresh account.
		  After every finding of these rules the guard log gets a line "evidence:" with the last eight hits taken or dealt.
		Items also looks at a melee hit: its weapon quality (HitData.m_itemLevel) above the quality of the weapon in hand.
		Only logged by default (LogOnly; set to On they act like the others): Burst (many pieces at once), Health (a piece with more
		health than its prefab can have; a mod that makes pieces tougher does that too), Rotation (a piece turned in a way the build
		mode does not offer; the free-rotation mods), Teleport (a jump of hundreds of metres in a moment with no portal at either end;
		the map-click teleport mods), Speed (faster than anything in the game for a few seconds).

		What happens: Action Log writes every finding to <world>.guard.log and the log, nothing else. Revert also takes the object
		away (a player's game that is told so by the server removes it; a game that holds the object is told to drop it). Kick also
		sends the player a message and removes them from the server a few seconds later; they can come back, and every object that
		breaks a rule is taken away again. The rules set to LogOnly never revert or kick. Admins (adminlist.txt) are not held to
		any of it (ExemptAdmins): they have the console, and a hammer that bends the rules is theirs to use.

		The same rules look through the stored world: the console command `anticheat scan` says what is in the world now that no tool of
		the game can make, who made it, and plants that were put down faster than the game allows; `anticheat revert` takes the objects
		that are set to On away. Objects of players the server has no record of, and admins' objects, are left alone.

		Not covered, and said so in the README: anything the game does inside the player's own game that leaves no trace in what it sends
		(the speed of the character, what a tool costs, what a recipe asks for, how much can be carried; a damage cheat below the Damage bound, a
		god mode of a player who is never hit by a creature of the server, stamina cheats on a bow or a spell, skills that grow by what the
		server cannot count), a mod that changes nothing in what
		the game sends, and a cheat that is careful: a player who places things one at a time, within reach, on cultivated ground,
		with a mod that only rotates them, cannot be told from one who does not use the mod.
	*/
	public class AntiCheat : IFeature
	{
		public enum Mode { Off, LogOnly, On }

		public enum Action { Log, Revert, Kick }

		public bool FeatureEnabled()
		{
			return Configuration.antiCheatEnabled.Value;
		}

		private enum Rule
		{
			UnknownPrefab, NotBuildable, Creator, Distance, Rate, Items, CheatCreature, CheatObject, Fly,
			Wards, NoBuild, Biome, Dungeon, Cultivated, Burst, Health, Rotation, Fields, Teleport, Speed, Forged, Identity, Flood, Level,
			Unhurt, Stamina, Damage, Skills
		}

		private static readonly string[] s_labels =
		{
			"an object the game does not have", "an object no tool of the game places", "a creator that is not the player who sent it",
			"placed far from the player", "planted faster than the game allows", "an item above the game's maximum",
			"a creature marked as spawned by cheating", "an object marked as made by cheating", "fly mode is on",
			"built in a ward that is not theirs", "built where nothing may be built", "built in the wrong biome",
			"built in a dungeon", "planted on ground that is not cultivated", "many pieces at once",
			"a piece with more health than it can have", "a piece turned in a way the build mode does not offer",
			"an object with field overrides of an admin tool", "moved far in a moment with no portal", "moved faster than anything in the game",
			"an object made under the name of another game", "a game that has no character of its own, or names another's", "a flood of objects", "a creature of a level the game never makes",
			"a character that a lethal hit leaves at exactly 1 health, or that hits do not hurt", "attacks that use no stamina", "a hit stronger than anything in the game deals", "a skill level the character cannot have earned here"
		};

		// The names the settings and the documentation use.
		private static readonly string[] s_names =
		{
			"UnknownPrefabs", "NotBuildable", "Creator", "Distance", "Rate", "Items", "CheatMarks", "CheatMarksOther", "Fly",
			"Wards", "NoBuild", "Biome", "Dungeon", "Cultivated", "Burst", "Health", "Rotation", "Fields", "Teleport", "Speed", "Forged", "Identity", "Flood", "Level",
			"Unhurt", "Stamina", "Damage", "Skills"
		};

		private static Mode ModeOf(Rule rule)
		{
			switch (rule)
			{
				case Rule.UnknownPrefab: return Configuration.antiCheatUnknownPrefabs.Value;
				case Rule.NotBuildable: return Configuration.antiCheatNotBuildable.Value;
				case Rule.Creator: return Configuration.antiCheatCreator.Value;
				case Rule.Distance: return Configuration.antiCheatDistance.Value;
				case Rule.Rate: return Configuration.antiCheatRate.Value;
				case Rule.Items: return Configuration.antiCheatItems.Value;
				case Rule.CheatCreature: return Configuration.antiCheatCheatMarks.Value;
				case Rule.CheatObject: return Configuration.antiCheatCheatMarksOther.Value;
				case Rule.Fly: return Configuration.antiCheatFly.Value;
				case Rule.Wards: return Configuration.antiCheatWards.Value;
				case Rule.NoBuild: return Configuration.antiCheatNoBuild.Value;
				case Rule.Biome: return Configuration.antiCheatBiome.Value;
				case Rule.Dungeon: return Configuration.antiCheatDungeon.Value;
				case Rule.Cultivated: return Configuration.antiCheatCultivated.Value;
				case Rule.Burst: return Configuration.antiCheatBurst.Value;
				case Rule.Health: return Configuration.antiCheatHealth.Value;
				case Rule.Fields: return Configuration.antiCheatFields.Value;
				case Rule.Teleport: return Configuration.antiCheatTeleport.Value;
				case Rule.Speed: return Configuration.antiCheatSpeed.Value;
				case Rule.Forged: return Configuration.antiCheatForged.Value;
				case Rule.Identity: return Configuration.antiCheatIdentity.Value;
				case Rule.Flood: return Configuration.antiCheatFlood.Value;
				case Rule.Level: return Configuration.antiCheatLevel.Value;
				case Rule.Unhurt: return Configuration.antiCheatUnhurt.Value;
				case Rule.Stamina: return Configuration.antiCheatStamina.Value;
				case Rule.Damage: return Configuration.antiCheatDamage.Value;
				case Rule.Skills: return Configuration.antiCheatSkills.Value;
				default: return Configuration.antiCheatRotation.Value;
			}
		}

		// What can be taken away: an object. A flying character, a burst of pieces, a tough piece, a fast run, a flood, and what the combat rules see are not taken away.
		private static bool Revertible(Rule rule)
		{
			return rule != Rule.Fly && rule != Rule.Burst && rule != Rule.Health && rule != Rule.Rotation && rule != Rule.Teleport && rule != Rule.Speed && rule != Rule.Flood
				&& rule != Rule.Unhurt && rule != Rule.Stamina && rule != Rule.Damage && rule != Rule.Skills;
		}

		// The rules whose answer depends on the state of the server a moment after the player's game decided (a ward switched on, the ground loaded,
		// the position of the player, a character that has just died): the first time one is broken the object is taken away and the player told, the
		// second time they are kicked.
		private static bool Soft(Rule rule)
		{
			return rule == Rule.Wards || rule == Rule.NoBuild || rule == Rule.Biome || rule == Rule.Dungeon || rule == Rule.Distance || rule == Rule.Identity || rule == Rule.Cultivated
				|| rule == Rule.Unhurt || rule == Rule.Stamina;
		}

		// A moment for the rest of an object's data to come before it is judged: the game sends an object and, a frame later, what it
		// sets when it starts (the time a plant was planted, the creator of a piece).
		private const float LookAfterSeconds = 1.2f;
		private const float KickDelaySeconds = 6f;
		private const int MaxQueued = 20000;
		private const int MaxQueuedPerPlayer = 4000;
		private const float KickMemorySeconds = 120f;
		// Two breaks of a warn-first rule count as one incident when they are less than this far apart: one honest mistake can be several objects.
		private const float IncidentSeconds = 15f;
		private const int PlantMemory = 64;
		private static readonly int s_hasFields = "HasFields".GetStableHashCode();

		private struct Born
		{
			public ZDOID id;
			public long user;
			public float at;
			public Vector3 from;
			public bool hasFrom;
			// 0: the game has no character, 1: its own, 2: another game's (a character is made by the game that plays it, so its id carries that session)
			public int character;
			public bool forged;
			public int looks;
			// The serial of the newest stamina sample of the sender when the object arrived (see Sample).
			public long sample;
		}

		// What the server's game knows about a prefab that matters for the rules.
		private class Info
		{
			public string name;
			public bool buildable;
			public bool piece;
			public bool plant;
			public bool character;
			public bool item;
			public bool container;
			public bool tilting;
			public int maxStack = 1;
			public int maxQuality = 1;
			public ItemDrop.ItemData data;
			public bool allowedInDungeons = true;
			public Heightmap.Biome onlyInBiome;
			public bool cultivatedOnly;
		}

		private struct Planting
		{
			public long ticks;
			public ZDOID id;
			public bool flagged;
		}

		// What the server saw of a character's stamina and of the item in its right hand after each package of object data from its game, kept
		// as the value that holds until the next one (a sample is added only when the stamina or the item changed). Serials count the samples of
		// one player: an object that arrived knows the serial of the newest sample before its package, and what its package and the ones after
		// it brought have higher serials, so the order is exact whatever the clock says.
		private struct Stamina
		{
			public long serial;
			public float at;
			public float value;
			public int right;
		}

		// The plants of one player that belong together: each within MinInterval of the one before in planting time, or, once the group is a burst,
		// arriving within ContinueSeconds of the one before (what one press of a mod that plants a row or a grid makes, and the presses that follow it
		// closely). A group is a burst when RatePlants of its plants lie inside MinInterval.
		private class Chain
		{
			public long firstTicks;
			public long lastTicks;
			public int count;
			public long sample;
			public float lastAt;
			public bool bursty;
			public bool logged;
			// the planting times so far, sorted (the newest 64): the burst test
			public readonly List<long> times = new List<long>();
			public readonly List<ZDOID> ids = new List<ZDOID>();
			// the plants before this index are dealt with: paid for, or taken
			public int dealt;
		}

		private class Tally
		{
			public float since;
			public int count;
			public string who;
			public string label;
			public string outcome;
		}

		private static readonly Queue<Born> s_born = new Queue<Born>();
		private static readonly Dictionary<int, Info> s_info = new Dictionary<int, Info>();
		private static HashSet<int> s_buildable;
		private static readonly Dictionary<long, List<Planting>> s_plants = new Dictionary<long, List<Planting>>();
		private static readonly Dictionary<long, List<Stamina>> s_stamina = new Dictionary<long, List<Stamina>>();
		private static readonly Dictionary<long, long> s_serial = new Dictionary<long, long>();
		private static readonly Dictionary<long, List<Chain>> s_chains = new Dictionary<long, List<Chain>>();
		private static readonly int s_cultivator = "Cultivator".GetStableHashCode();
		// How long after the last plant of a group the stamina still counts as paid for it (the game takes the price in the frame of the press;
		// this is for the way it reaches the server), how soon after the last plant of a burst the next one still belongs to the group (it must be
		// longer than the first, so that what the next group pays is never counted for this one), how long samples are kept (the newest older one is
		// always kept), the most stamina a character can have in the server's eyes, and how many plants and groups are remembered.
		private const float PaidSeconds = 0.5f;
		private const float ContinueSeconds = 1f;
		private const float StaleSeconds = 5f;
		private const float StaminaMemorySeconds = 20f;
		// The most stamina a character has in the game is 75 and what three foods add (about 210); more is not a stamina. What can fall inside
		// one window is what the character had, plus what regeneration or a potion brought in that time.
		private const float StaminaCap = 400f;
		private const float RegenAllowance = 15f;
		private const float ExhaustedStamina = 0.05f;
		// The game does not start a press of the cultivator with 5 stamina or less (Player.HaveStamina of the tool's attack stamina).
		private const float PressStamina = 5f;
		private const int ChainIds = 512;
		private const int MaxChains = 4;
		private static int s_groups;
		private static int s_paidPlants;
		private static readonly Dictionary<long, Queue<float>> s_bursts = new Dictionary<long, Queue<float>>();
		private static readonly Dictionary<long, string> s_who = new Dictionary<long, string>();
		private static readonly Dictionary<long, float> s_kickAt = new Dictionary<long, float>();
		private static readonly Dictionary<long, float> s_kicked = new Dictionary<long, float>();
		private static readonly Dictionary<long, int> s_waiting = new Dictionary<long, int>();
		private static readonly Dictionary<long, List<float>> s_strikes = new Dictionary<long, List<float>>();
		// Objects waiting to be judged and who made them; the ones another game has written to meanwhile; the characters another game has written to.
		private static readonly Dictionary<ZDOID, long> s_pending = new Dictionary<ZDOID, long>();
		private static readonly Dictionary<long, KeyValuePair<float, bool>> s_adminAt = new Dictionary<long, KeyValuePair<float, bool>>();
		private static readonly HashSet<ZDOID> s_tampered = new HashSet<ZDOID>();
		private static readonly Dictionary<ZDOID, float> s_characterTampered = new Dictionary<ZDOID, float>();
		private static readonly Queue<KeyValuePair<long, ZDOID>> s_characterWrites = new Queue<KeyValuePair<long, ZDOID>>();
		private static readonly int s_playerPrefab = "Player".GetStableHashCode();
		private static float s_nextPrune;
		private static float s_writeWindow;
		private static int s_writeCount;
		private static int s_writeSuppressed;
		private static int s_blank;
		private struct Track
		{
			public Vector3 pos;
			public float at;
			public ZDOID id;
			public int fast;
		}

		private static readonly Dictionary<long, Track> s_tracks = new Dictionary<long, Track>();
		// Teleports the server itself sent (the console's tp), by player: where to and when.
		private static readonly Dictionary<long, List<KeyValuePair<Vector3, float>>> s_serverMoves = new Dictionary<long, List<KeyValuePair<Vector3, float>>>();
		private const float ServerMoveSeconds = 60f;
		private const float ServerMoveMetres = 50f;
		private static readonly Dictionary<string, Tally> s_tallies = new Dictionary<string, Tally>();
		private static readonly Dictionary<string, float> s_rate = new Dictionary<string, float>();
		private static readonly int[] s_hits = new int[Enum.GetValues(typeof(Rule)).Length];
		private static int s_judged;
		private static int s_reverted;
		private static int s_kicks;
		private static int s_dropped;
		private static int s_unattributed;
		private static float s_nextPlayers;
		private static float s_nextFlush;
		private static float s_errorsSince;
		private static int s_errors;

		// ---- the feed ---------------------------------------------------------------------------------------------------

		// Every object that arrives in a package of object data from a player's game is put in a queue, with where the player was at that
		// moment; it is judged by Tick a moment later. Whose it is, is the connection it came in on: the id of the object is the sender's
		// claim, and a game that wants to put its work on someone else can write that someone's session into the id. While the package is
		// handled, s_sender is the connection; anything else (the server's own objects) has none.
		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix(ZRpc rpc)
			{
				try
				{
					s_sender = Configuration.antiCheatEnabled.Value && ZNet.instance != null && ZNet.instance.IsServer() ? ZNet.instance.GetPeer(rpc) : null;
					s_fed |= s_sender != null;
				}
				catch (Exception e)
				{
					s_sender = null;
					Fault(e);
				}
			}

			static void Finalizer()
			{
				ZNetPeer sender = s_sender;
				s_sender = null;
				if (sender != null && PaidPlanting())
				{
					try
					{
						Sample(sender);
					}
					catch (Exception e)
					{
						Fault(e);
					}
				}
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
		public static class ZDOMan_CreateNewZDO_Patch
		{
			static void Postfix(ZDOID uid)
			{
				if (s_sender == null)
				{
					return;
				}
				try
				{
					Arrived(uid);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		// The server accepts a write to any object from any game that sends a newer revision of it, whoever owns it. What matters here: an
		// object that waits to be judged and is written by another game than the one that made it (it is then no longer that game's work), and
		// a character object written by a game that is not its own (nothing in the game does that).
		[HarmonyPatch(typeof(ZDO), "Deserialize", typeof(ZPackage))]
		public static class ZDO_Deserialize_Patch
		{
			static void Postfix(ZDO __instance)
			{
				ZNetPeer sender = s_sender;
				if (sender == null)
				{
					return;
				}
				try
				{
					// A character object written by a game that is not the one that made it. (This comes first: a character is waiting to be judged
					// like any other new object for its first 1.2 s.)
					if (__instance.GetPrefab() == s_playerPrefab && __instance.m_uid.UserID != sender.m_uid)
					{
						s_characterTampered[__instance.m_uid] = Time.time;
						if (s_characterWrites.Count < 200)
						{
							s_characterWrites.Enqueue(new KeyValuePair<long, ZDOID>(sender.m_uid, __instance.m_uid));
						}
					}
					if (s_pending.Count > 0 && s_pending.TryGetValue(__instance.m_uid, out long maker) && maker != sender.m_uid)
					{
						s_tampered.Add(__instance.m_uid);
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static ZNetPeer s_sender;
		// True once the feed has run, which it can only if the patches were applied (they are, at start, only if [AntiCheat] Enabled was set): the rules
		// that read the character are not run without it, because they could not tell a character another game has written to.
		private static bool s_fed;
		// Set while an object is judged that another game has written to: what is wrong with it is taken away, but nobody is warned or kicked for it.
		private static bool s_noBlame;

		private static void Arrived(ZDOID uid)
		{
			ZNetPeer peer = s_sender;
			long user = peer.m_uid;
			if (AdminOf(peer))
			{
				// Admins are not held to any rule, so nothing of theirs is queued (a pasted base of thousands of pieces is theirs to paste).
				return;
			}
			s_waiting.TryGetValue(user, out int waiting);
			if (waiting >= MaxQueuedPerPlayer || s_born.Count >= MaxQueued)
			{
				s_dropped++;
				if (waiting >= MaxQueuedPerPlayer)
				{
					// This player's own objects fill their share: they are acted on. When only the whole queue is full, whoever sent one more is not blamed.
					if (Rate("queue" + user, 60f))
					{
						Hit(Rule.Flood, user, peer, null, "?", $"{waiting} of its objects are waiting to be looked at ({s_born.Count} in all; the limit is {MaxQueuedPerPlayer} for one player); the rest are not looked at", peer.m_refPos);
					}
				}
				else if (Rate("queue", 60f))
				{
					Write($"{s_born.Count} objects are waiting to be looked at; those that come now are not looked at");
				}
				return;
			}
			s_waiting[user] = waiting + 1;
			Born born = new Born { id = uid, user = user, at = Time.time, forged = uid.UserID != user };
			if (PaidPlanting())
			{
				s_serial.TryGetValue(user, out born.sample);
			}
			if (!s_who.ContainsKey(user))
			{
				s_who[user] = Who(peer);
			}
			if (!peer.m_characterID.IsNone())
			{
				born.character = peer.m_characterID.UserID == user ? 1 : 2;
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				if (character != null && born.character == 1)
				{
					born.from = character.GetPosition();
					born.hasFrom = true;
				}
			}
			s_pending[uid] = user;
			s_born.Enqueue(born);
		}

		// ---- what the game knows -------------------------------------------------------------------------------------------

		// Every piece the game itself lets a player place: those in the build table of any item (the hammer, the hoe, the cultivator ...), and those
		// that an item's attack puts down (Attack.m_spawnOnTrigger with a Piece goes through Player.PlacePiece too, and so writes a creator).
		private static HashSet<int> Buildable()
		{
			if (s_buildable == null)
			{
				HashSet<int> set = new HashSet<int>();
				List<string> byAttack = new List<string>();
				int tools = 0;
				foreach (GameObject go in ObjectDB.instance.m_items)
				{
					ItemDrop.ItemData.SharedData shared = go ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
					if (shared == null)
					{
						continue;
					}
					PieceTable table = shared.m_buildPieces;
					if (table != null && table.m_pieces != null)
					{
						tools++;
						foreach (GameObject piece in table.m_pieces)
						{
							if (piece)
							{
								set.Add(piece.name.GetStableHashCode());
							}
						}
					}
					foreach (Attack attack in new[] { shared.m_attack, shared.m_secondaryAttack })
					{
						GameObject spawned = attack != null ? attack.m_spawnOnTrigger : null;
						if (spawned && spawned.GetComponent<Piece>() && set.Add(spawned.name.GetStableHashCode()))
						{
							byAttack.Add(spawned.name + " (" + go.name + ")");
						}
					}
				}
				s_buildable = set;
				ServersidePlugin.logger.LogInfo($"Anti-cheat: {set.Count} pieces in the build tables of {tools} tool(s) of the game and put down by attacks [{string.Join(", ", byAttack)}]; action {Configuration.antiCheatAction.Value}, admins {(Configuration.antiCheatExemptAdmins.Value ? "exempt" : "held to it as well")}.");
			}
			return s_buildable;
		}

		private static Info InfoOf(int hash)
		{
			if (s_info.TryGetValue(hash, out Info info))
			{
				return info;
			}
			GameObject prefab = ZNetScene.instance.GetPrefab(hash);
			if (prefab == null)
			{
				return null;
			}
			Piece piece = prefab.GetComponent<Piece>();
			ItemDrop drop = prefab.GetComponent<ItemDrop>();
			Plant plant = prefab.GetComponent<Plant>();
			info = new Info
			{
				name = prefab.name,
				buildable = Buildable().Contains(hash),
				piece = piece != null,
				plant = plant != null,
				character = prefab.GetComponent<Character>() != null,
				container = prefab.GetComponent<Container>() != null,
				item = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null,
				// Vines are put against a wall and turned to it, and so are a few other pieces; they are not judged by their turn.
				tilting = (plant != null && plant.m_attachDistance > 0f)
			};
			if (info.item)
			{
				info.data = drop.m_itemData;
				info.maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
				info.maxQuality = Mathf.Max(1, drop.m_itemData.m_shared.m_maxQuality);
			}
			if (piece != null)
			{
				info.allowedInDungeons = piece.m_allowedInDungeons;
				info.onlyInBiome = piece.m_onlyInBiome;
				info.cultivatedOnly = piece.m_cultivatedGroundOnly;
			}
			s_info[hash] = info;
			return info;
		}

		// ---- people -------------------------------------------------------------------------------------------------------

		// The id of the player whose connection this is, from its own character; a character that another game made says nothing about this one.
		private static long PlayerId(ZNetPeer peer)
		{
			if (peer == null || peer.m_characterID.IsNone() || peer.m_characterID.UserID != peer.m_uid)
			{
				return 0L;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
		}

		private static bool IsAdmin(ZNetPeer peer)
		{
			return Configuration.antiCheatExemptAdmins.Value && peer != null && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
		}

		// The same, remembered for a few seconds per session: a game makes objects by the hundred and the account does not change.
		private static bool AdminOf(ZNetPeer peer)
		{
			if (s_adminAt.TryGetValue(peer.m_uid, out KeyValuePair<float, bool> known) && Time.time - known.Key < 10f)
			{
				return known.Value;
			}
			bool admin = IsAdmin(peer);
			s_adminAt[peer.m_uid] = new KeyValuePair<float, bool>(Time.time, admin);
			return admin;
		}

		private static bool IsAdminAccount(string account)
		{
			return Configuration.antiCheatExemptAdmins.Value && !string.IsNullOrEmpty(account) && ZNet.instance.IsAdmin(account);
		}

		private static string Who(ZNetPeer peer)
		{
			return $"{Clean(peer.m_playerName)} ({(peer.m_socket != null ? peer.m_socket.GetHostName() : "?")}, character {PlayerId(peer)})";
		}

		private static string WhoIs(long user, ZNetPeer peer)
		{
			if (peer != null)
			{
				return s_who[user] = Who(peer);
			}
			return s_who.TryGetValue(user, out string known) ? known + " (left)" : $"a game with session {user} (left)";
		}

		// A player's name goes into a log line: no line breaks or other control characters, or a name could forge a line.
		private static string Clean(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "?";
			}
			char[] c = name.ToCharArray();
			for (int i = 0; i < c.Length; i++)
			{
				if (char.IsControl(c[i]))
				{
					c[i] = ' ';
				}
			}
			return new string(c);
		}

		private static string At(Vector3 p)
		{
			return $"{p.x:0},{p.z:0}";
		}

		private static bool Rate(string key, float seconds)
		{
			if (s_rate.Count > 4000)
			{
				s_rate.Clear();
			}
			if (s_rate.TryGetValue(key, out float last) && Time.time - last < seconds)
			{
				return false;
			}
			s_rate[key] = Time.time;
			return true;
		}

		private static void Fault(Exception e)
		{
			if (Time.realtimeSinceStartup - s_errorsSince > 900f)
			{
				s_errorsSince = Time.realtimeSinceStartup;
				s_errors = 0;
			}
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Anti-cheat: {e}");
			}
		}

		// ---- the tick -----------------------------------------------------------------------------------------------------

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.antiCheatEnabled.Value || ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.World == null
				|| ZDOMan.instance == null || ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null || ZNetScene.instance.m_prefabs.Count == 0
				|| !ObjectDB.instance || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0)
			{
				return;
			}
			if (!s_fed)
			{
				return;
			}
			RunKicks();
			try
			{
				Characters();
			}
			catch (Exception e)
			{
				Fault(e);
			}
			Look();
			if (Time.time >= s_nextCombat && CombatOn())
			{
				s_nextCombat = Time.time + 0.5f;
				try
				{
					CombatTick();
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
			if (Time.time >= s_nextPlayers)
			{
				s_nextPlayers = Time.time + 2f;
				try
				{
					Players();
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
			if (Time.time >= s_nextFlush)
			{
				s_nextFlush = Time.time + 5f;
				Flush(false);
			}
			if (Time.time >= s_nextPrune)
			{
				s_nextPrune = Time.time + 300f;
				Prune();
			}
		}

		private static void Look()
		{
			int budget = 400;
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			while (s_born.Count > 0 && budget-- > 0 && Time.time - s_born.Peek().at >= LookAfterSeconds)
			{
				Born born = s_born.Dequeue();
				try
				{
					Judge(born);
				}
				catch (Exception e)
				{
					Fault(e);
				}
				if (watch.ElapsedMilliseconds >= 3)
				{
					break;
				}
			}
		}

		// A game that wrote to the character of another game: nothing in the game does that.
		private static void Characters()
		{
			int budget = 20;
			while (s_characterWrites.Count > 0 && budget-- > 0)
			{
				KeyValuePair<long, ZDOID> write = s_characterWrites.Dequeue();
				ZNetPeer writer = ZNet.instance.GetPeer(write.Key);
				if (writer == null || AdminOf(writer))
				{
					continue;
				}
				ZDO target = ZDOMan.instance.GetZDO(write.Value);
				Hit(Rule.Forged, write.Key, writer, null, "Player", "it wrote to the character of another game, which nothing in the game does", target != null ? target.GetPosition() : writer.m_refPos);
			}
		}

		// A character that another game has written to stays out of the rules for as long as it exists: what the other game put in it (the fly
		// switch, a position, a player id) stays in it too, because the server keeps the newest revision of an object and the one who wrote may
		// have made theirs the newest for good. The one who wrote is acted on (Forged); the one written to must not be.
		private static bool CharacterTampered(ZNetPeer peer)
		{
			return peer != null && s_characterTampered.ContainsKey(peer.m_characterID);
		}

		private static void Players()
		{
			bool fly = ModeOf(Rule.Fly) != Mode.Off;
			bool teleport = ModeOf(Rule.Teleport) != Mode.Off;
			bool speed = ModeOf(Rule.Speed) != Mode.Off;
			bool identity = ModeOf(Rule.Identity) != Mode.Off;
			if (!fly && !teleport && !speed && !identity)
			{
				return;
			}
			foreach (ZNetPeer peer in ZNet.instance.GetPeers().ToList())
			{
				if (!peer.IsReady() || peer.m_characterID.IsNone() || AdminOf(peer))
				{
					continue;
				}
				if (peer.m_characterID.UserID != peer.m_uid)
				{
					// The character of a player is made by the game that plays it, so its id carries that game's session.
					if (identity)
					{
						Hit(Rule.Identity, peer.m_uid, peer, null, "Player", $"it names the character of the game with session {peer.m_characterID.UserID} as its own", peer.m_refPos);
					}
					continue;
				}
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				if (character == null || CharacterTampered(peer))
				{
					continue;
				}
				if (fly && character.GetBool(ZDOVars.s_debugFly))
				{
					Hit(Rule.Fly, peer.m_uid, peer, null, "Player", "the debug fly of the game is switched on", character.GetPosition());
				}
				if (teleport || speed)
				{
					Moved(peer, character);
				}
			}
			if (s_tracks.Count > 64)
			{
				foreach (long user in s_tracks.Keys.Where(u => ZNet.instance.GetPeer(u) == null).ToList())
				{
					s_tracks.Remove(user);
				}
			}
		}

		// What is kept for players who have left: after five minutes, when nothing of theirs is waiting to be judged.
		private static void Prune()
		{
			foreach (long user in s_who.Keys.Where(u => ZNet.instance.GetPeer(u) == null && !s_waiting.ContainsKey(u)).ToList())
			{
				s_who.Remove(user);
				s_plants.Remove(user);
				s_bursts.Remove(user);
				s_tracks.Remove(user);
				s_strikes.Remove(user);
				s_adminAt.Remove(user);
			}
			foreach (long user in s_adminAt.Keys.Where(u => ZNet.instance.GetPeer(u) == null).ToList())
			{
				s_adminAt.Remove(user);
			}
			// the stamina samples and plant groups are kept for every connection that sent object data, admins' too
			foreach (long user in s_serial.Keys.Concat(s_stamina.Keys).Concat(s_chains.Keys).Distinct().Where(u => ZNet.instance.GetPeer(u) == null && !s_waiting.ContainsKey(u)).ToList())
			{
				s_stamina.Remove(user);
				s_serial.Remove(user);
				s_chains.Remove(user);
			}
			foreach (long user in s_fighters.Keys.Where(u => ZNet.instance.GetPeer(u) == null).ToList())
			{
				s_fighters.Remove(user);
			}
			foreach (long user in s_kicked.Where(kv => Time.time - kv.Value > KickMemorySeconds).Select(kv => kv.Key).ToList())
			{
				s_kicked.Remove(user);
			}
			foreach (ZDOID id in s_characterTampered.Keys.Where(id => ZDOMan.instance.GetZDO(id) == null).ToList())
			{
				s_characterTampered.Remove(id);
			}
			if (s_rate.Count > 2000)
			{
				s_rate.Clear();
			}
		}

		// The console sent a player to `to` (AdminCommands.Teleport): for a minute, their character's moves while it is within 50 m of
		// there are not judged (Teleport, Speed). The game's own teleport puts the character there after 2 s and onto the ground after 8 to 15.
		public static void ServerMoved(long user, Vector3 to)
		{
			foreach (long gone in s_serverMoves.Keys.Where(u => s_serverMoves[u].All(m => Time.time - m.Value > ServerMoveSeconds)).ToList())
			{
				s_serverMoves.Remove(gone);
			}
			if (!s_serverMoves.TryGetValue(user, out List<KeyValuePair<Vector3, float>> sent))
			{
				s_serverMoves[user] = sent = new List<KeyValuePair<Vector3, float>>();
			}
			sent.Add(new KeyValuePair<Vector3, float>(to, Time.time));
		}

		// Where the character was two seconds ago and where it is. A jump of hundreds of metres in a moment is a teleport, which in the game
		// is a portal (in the middle of the base or at a dungeon's door), a dungeon, or a respawn (a new character object). A run that
		// is faster than anything the game offers (the fastest ship under a good wind is about 15 m/s) is only counted if it lasts.
		private static void Moved(ZNetPeer peer, ZDO character)
		{
			Vector3 now = character.GetPosition();
			long user = peer.m_uid;
			s_tracks.TryGetValue(user, out Track before);
			Track track = new Track { pos = now, at = Time.time, id = character.m_uid, fast = 0 };
			if (s_serverMoves.TryGetValue(user, out List<KeyValuePair<Vector3, float>> sent))
			{
				// The server sent this player there: the jump, and the drop onto the ground some seconds later, are not theirs.
				// Every spot sent in the last minute counts, as the game takes the first of several sent at once and refuses the rest.
				sent.RemoveAll(m => Time.time - m.Value > ServerMoveSeconds);
				if (sent.Count == 0)
				{
					s_serverMoves.Remove(user);
				}
				else if (sent.Any(m => new Vector2(now.x - m.Key.x, now.z - m.Key.z).magnitude < ServerMoveMetres))
				{
					s_tracks[user] = track;
					return;
				}
			}
			if (before.id == character.m_uid && before.at > 0f)
			{
				float seconds = Time.time - before.at;
				if (seconds >= 1f && seconds <= 8f)
				{
					float metres = Vector3.Distance(before.pos, now);
					float flat = new Vector2(now.x - before.pos.x, now.z - before.pos.z).magnitude;
					if (Character.InInterior(before.pos) || Character.InInterior(now))
					{
						// into or out of a dungeon, or inside one
					}
					else if (metres > Configuration.antiCheatTeleportMetres.Value)
					{
						if (!PortalNear(before.pos) && !PortalNear(now))
						{
							Hit(Rule.Teleport, user, peer, null, "Player", $"{metres:0} m in {seconds:0.#} s, from {At(before.pos)}, with no portal at either end", now);
						}
					}
					else if (flat / seconds > Configuration.antiCheatSpeedLimit.Value)
					{
						track.fast = before.fast + 1;
						if (track.fast >= 2)
						{
							Hit(Rule.Speed, user, peer, null, "Player", $"{flat / seconds:0} m/s for {track.fast * seconds:0} s (the limit is {Configuration.antiCheatSpeedLimit.Value:0})", now);
						}
					}
				}
			}
			s_tracks[user] = track;
		}

		private static bool PortalNear(Vector3 at)
		{
			foreach (List<ZDO> list in ZDOMan.instance.m_portalObjects.Values)
			{
				foreach (ZDO portal in list)
				{
					Vector3 p = portal.GetPosition();
					if ((p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z) < 20f * 20f)
					{
						return true;
					}
				}
			}
			return false;
		}

		// ---- the rules ----------------------------------------------------------------------------------------------------

		// The object is done with: not waiting any more.
		private static void Done(Born born)
		{
			s_pending.Remove(born.id);
			s_tampered.Remove(born.id);
			if (s_waiting.TryGetValue(born.user, out int waiting))
			{
				if (waiting <= 1)
				{
					s_waiting.Remove(born.user);
				}
				else
				{
					s_waiting[born.user] = waiting - 1;
				}
			}
		}

		private static void Judge(Born born)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(born.id);
			long user = born.user;
			if (zdo == null)
			{
				Done(born);
				return;
			}
			int hash = zdo.GetPrefab();
			if (hash == 0)
			{
				// Its data has not come yet.
				if (born.looks < 2)
				{
					born.looks++;
					born.at = Time.time;
					s_born.Enqueue(born);
					return;
				}
				s_blank++;
				Done(born);
				return;
			}
			bool tampered = s_tampered.Contains(born.id);
			Done(born);
			ZNetPeer peer = ZNet.instance.GetPeer(user);
			if (peer == null)
			{
				s_unattributed++;
			}
			s_judged++;
			Vector3 pos = zdo.GetPosition();
			if (tampered)
			{
				// Another game has written to the object since (a player who opened a chest a moment after it was placed does: the game hands the
				// object over for that). It may no longer be what the game that made it sent. What is wrong with it is still taken away, but nobody
				// is blamed for it. A game that made it under a name that is not its own is acted on, and the object is left (it may be the real work
				// of the game whose name was used).
				if (born.forged && ModeOf(Rule.Forged) != Mode.Off)
				{
					Hit(Rule.Forged, user, peer, null, ZNetScene.instance.GetPrefab(hash)?.name ?? "#" + hash, $"its id says it was made by the game with session {born.id.UserID}", pos);
					return;
				}
				s_noBlame = true;
			}
			try
			{
				JudgeObject(born, zdo, peer, user, hash, pos);
			}
			finally
			{
				s_noBlame = false;
			}
		}

		private static void JudgeObject(Born born, ZDO zdo, ZNetPeer peer, long user, int hash, Vector3 pos)
		{
			if (born.forged && ModeOf(Rule.Forged) != Mode.Off)
			{
				Hit(Rule.Forged, user, peer, zdo, ZNetScene.instance.GetPrefab(hash)?.name ?? "#" + hash, $"its id says it was made by the game with session {born.id.UserID}", pos);
				return;
			}
			Info info = InfoOf(hash);
			if (info == null)
			{
				Hit(Rule.UnknownPrefab, user, peer, zdo, "#" + hash, "the server's game has no such object", pos);
				return;
			}
			if (zdo.GetBool(s_hasFields) && ModeOf(Rule.Fields) != Mode.Off)
			{
				Hit(Rule.Fields, user, peer, zdo, info.name, "the game writes field overrides (HasFields) only for the console's spawn command given component fields", pos);
				return;
			}
			if (zdo.GetInt(ZDOVars.s_cheated, 0) == 2)
			{
				// A creature carries the mark only if a cheating game spawned it. Anything else may carry it for a harmless reason (a piece built
				// from items that an admin handed out): that is only a trace.
				Hit(info.character ? Rule.CheatCreature : Rule.CheatObject, user, peer, zdo, info.name, "it carries the game's mark for cheating", pos);
				if (info.character && ModeOf(Rule.CheatCreature) == Mode.On)
				{
					return;
				}
			}
			if (info.character && hash != s_playerPrefab && ModeOf(Rule.Level) != Mode.Off)
			{
				// The game levels a creature up from its spawn data: three stars at the most, a few more with the world's modifiers.
				int level = zdo.GetInt(ZDOVars.s_level, 1);
				if (level > Configuration.antiCheatMaxLevel.Value)
				{
					Hit(Rule.Level, user, peer, zdo, info.name, $"level {level}; the game makes none above {Configuration.antiCheatMaxLevel.Value}", pos);
					return;
				}
			}
			long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
			if (creator != 0L)
			{
				if (!info.buildable)
				{
					Hit(Rule.NotBuildable, user, peer, zdo, info.name, info.piece ? "a piece that is in no build table of the game" : "it is not a piece at all, yet it has a creator: the game's build mode never makes this", pos);
					return;
				}
				JudgePiece(born, zdo, peer, user, info, creator, pos);
				return;
			}
			if (info.item)
			{
				JudgeItem(zdo, peer, user, info, pos);
			}
		}

		private static void JudgePiece(Born born, ZDO zdo, ZNetPeer peer, long user, Info info, long creator, Vector3 pos)
		{
			// A piece is placed by a character, and the character is the game's own.
			if (born.character != 1 && ModeOf(Rule.Identity) != Mode.Off)
			{
				Hit(Rule.Identity, user, peer, zdo, info.name, born.character == 2 ? "its game names a character that another game made" : "its game has no character, and only a character places a piece", pos);
				return;
			}
			// What another game has written to the character cannot be held against the player.
			bool trusted = born.character == 1 && !CharacterTampered(peer);
			long sender = trusted ? PlayerId(peer) : 0L;
			if (sender != 0L && creator != sender)
			{
				Hit(Rule.Creator, user, peer, zdo, info.name, $"the creator written on it is {creator}", pos);
				return;
			}
			float max = Configuration.antiCheatMaxDistance.Value;
			if (trusted && born.hasFrom && max > 0f && Vector3.Distance(born.from, pos) > max && FarNow(peer, pos, max))
			{
				Hit(Rule.Distance, user, peer, zdo, info.name, $"{Vector3.Distance(born.from, pos):0} m from the player (the game reaches 5 m, and a little more with large pieces)", pos);
				return;
			}
			if (info.plant)
			{
				long planted = zdo.GetLong(ZDOVars.s_plantTime, 0L);
				if (planted != 0L && (PaidPlanting() ? Group(born, user, peer, zdo, info, planted, pos) : Faster(user, peer, zdo, info, planted, pos)))
				{
					return;
				}
			}
			else
			{
				Burst(user, peer, zdo, info, born.at, pos);
			}
			// Where the build mode refuses.
			long player = sender != 0L ? sender : creator;
			if (ModeOf(Rule.Wards) != Mode.Off && !WardGuard.MayBuildAt(pos, player, out ZDO ward))
			{
				long owner = ward != null ? ward.GetLong(ZDOVars.s_creator, 0L) : 0L;
				Hit(Rule.Wards, user, peer, zdo, info.name, $"inside the ward of {NameOfCharacter(owner)} at {(ward != null ? At(ward.GetPosition()) : "?")}", pos);
				return;
			}
			if (ModeOf(Rule.NoBuild) != Mode.Off && Location.IsInsideNoBuildLocation(pos))
			{
				Hit(Rule.NoBuild, user, peer, zdo, info.name, "inside a place where the game does not let anyone build", pos);
				return;
			}
			if (ModeOf(Rule.Biome) != Mode.Off && info.onlyInBiome != Heightmap.Biome.None)
			{
				// Only where the server has the ground loaded: the game's own test reads the ground, and without it there is no answer.
				// A ground that exists may not be built yet and then answers Meadows, and near a border its answer is an interpolation: the world
				// generator must say the same before it is held against the player.
				Heightmap ground = Heightmap.FindHeightmap(pos);
				if (ground != null && (ground.GetBiome(pos) & info.onlyInBiome) == 0 && (WorldGenerator.instance.GetBiome(pos.x, pos.z) & info.onlyInBiome) == 0)
				{
					Hit(Rule.Biome, user, peer, zdo, info.name, $"it belongs to {info.onlyInBiome}, this is {ground.GetBiome(pos)}", pos);
					return;
				}
			}
			if (ModeOf(Rule.Dungeon) != Mode.Off && !info.allowedInDungeons && Character.InInterior(pos) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DungeonBuild))
			{
				Hit(Rule.Dungeon, user, peer, zdo, info.name, "it may not be built in a dungeon", pos);
				return;
			}
			if (ModeOf(Rule.Cultivated) != Mode.Off && info.cultivatedOnly)
			{
				Heightmap map = Heightmap.FindHeightmap(pos);
				if (map != null && !map.IsCultivated(pos))
				{
					Hit(Rule.Cultivated, user, peer, zdo, info.name, "it can only be put on cultivated ground", pos);
					return;
				}
			}
			if (ModeOf(Rule.Rotation) != Mode.Off && !info.tilting && Turned(zdo.GetRotation()))
			{
				Hit(Rule.Rotation, user, peer, zdo, info.name, "rotation " + zdo.GetRotation().eulerAngles.ToString("0.#"), pos);
				return;
			}
			if (ModeOf(Rule.Health) != Mode.Off && info.piece)
			{
				float limit = Decay.MaxHealthOf(zdo.GetPrefab());
				if (limit > 0f && zdo.GetFloat(ZDOVars.s_health, limit) > limit * 1.05f + 1f)
				{
					Hit(Rule.Health, user, peer, zdo, info.name, $"health {zdo.GetFloat(ZDOVars.s_health, limit):0}, the most it can have is {limit:0}", pos);
				}
			}
		}

		// Far from where the player is now as well: a player who teleported, or whose position the server had not caught up with, is near their piece
		// in one of the two.
		private static bool FarNow(ZNetPeer peer, Vector3 pos, float max)
		{
			if (peer == null || peer.m_characterID.IsNone())
			{
				return true;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character == null || Vector3.Distance(character.GetPosition(), pos) > max;
		}

		// The game turns a piece about the vertical axis in steps of 22.5 degrees and in no other way.
		private static bool Turned(Quaternion rotation)
		{
			Vector3 e = rotation.eulerAngles;
			float pitch = Mathf.DeltaAngle(0f, e.x);
			float roll = Mathf.DeltaAngle(0f, e.z);
			float yaw = Mathf.Repeat(e.y, 22.5f);
			return Mathf.Abs(pitch) > 1f || Mathf.Abs(roll) > 1f || (yaw > 1f && yaw < 21.5f);
		}

		// The game plants once in 0.4 s at the fastest, and writes the time into the plant (the network time of the game, which now and then
		// steps by a few tenths of a second, so two plants of an honest player can come out a few hundredths apart: the real world has such
		// pairs). Three or more plants of one player inside MinInterval cannot be: a row or a grid of seeds with one press puts them all in
		// the same moment. Every member of such a group is more than the game allows; they are all taken away.
		private static bool Faster(long user, ZNetPeer peer, ZDO zdo, Info info, long planted, Vector3 pos)
		{
			if (ModeOf(Rule.Rate) == Mode.Off)
			{
				return false;
			}
			if (!s_plants.TryGetValue(user, out List<Planting> recent))
			{
				s_plants[user] = recent = new List<Planting>();
			}
			long window = (long)(Mathf.Max(0.01f, Configuration.antiCheatMinInterval.Value) * TimeSpan.TicksPerSecond);
			int need = Mathf.Max(2, Configuration.antiCheatRatePlants.Value);
			// the plants close enough in time to matter, and this one (index -1), in time order
			List<KeyValuePair<long, int>> near = new List<KeyValuePair<long, int>>();
			for (int i = 0; i < recent.Count; i++)
			{
				if (Math.Abs(recent[i].ticks - planted) <= window)
				{
					near.Add(new KeyValuePair<long, int>(recent[i].ticks, i));
				}
			}
			near.Add(new KeyValuePair<long, int>(planted, -1));
			near.Sort((a, b) => a.Key.CompareTo(b.Key));
			bool[] member = new bool[near.Count];
			bool faster = false;
			for (int i = 0; i + need <= near.Count; i++)
			{
				if (near[i + need - 1].Key - near[i].Key <= window)
				{
					for (int k = i; k < i + need; k++)
					{
						member[k] = true;
					}
				}
			}
			for (int k = 0; k < near.Count; k++)
			{
				if (near[k].Value == -1 && member[k])
				{
					faster = true;
				}
			}
			if (faster)
			{
				for (int k = 0; k < near.Count; k++)
				{
					int index = near[k].Value;
					if (!member[k] || index < 0 || recent[index].flagged)
					{
						continue;
					}
					Planting other = recent[index];
					other.flagged = true;
					recent[index] = other;
					ZDO first = ZDOMan.instance.GetZDO(other.id);
					if (first != null)
					{
						Hit(Rule.Rate, user, peer, first, info.name, $"{need} or more plants of the same player within {Configuration.antiCheatMinInterval.Value:0.##} s (the game plants one in 0.4 s)", first.GetPosition());
					}
				}
			}
			recent.Add(new Planting { ticks = planted, id = zdo.m_uid, flagged = faster });
			if (recent.Count > PlantMemory)
			{
				recent.RemoveAt(0);
			}
			if (faster)
			{
				Hit(Rule.Rate, user, peer, zdo, info.name, $"{need} or more plants of the same player within {Configuration.antiCheatMinInterval.Value:0.##} s (the game plants one in 0.4 s)", pos);
			}
			return faster;
		}

		// ---- paid planting ------------------------------------------------------------------------------------------------

		private static bool PaidPlanting()
		{
			return Configuration.antiCheatPaidPlanting.Value && ModeOf(Rule.Rate) != Mode.Off;
		}

		// After each package of object data from a player's game: the stamina of the character and the item in its right hand, when either has
		// changed. The game writes the stamina into the character in every physics step in which it changed and sends it with everything else that
		// changed, new objects first, so the package that carries a press of the cultivator (or the next one) carries what the press cost. The
		// mods that plant a grid pay their price in the frame of the press, before the plants that come after the first one (PlantEasily plants
		// two a frame), or do not pay it.
		private static void Sample(ZNetPeer peer)
		{
			if (peer.m_characterID.IsNone() || peer.m_characterID.UserID != peer.m_uid || CharacterTampered(peer))
			{
				return;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			if (character == null)
			{
				return;
			}
			float value = character.GetFloat(ZDOVars.s_stamina, -1f);
			if (!(value >= 0f) || value > StaminaCap)
			{
				// absent, not a number, or more than any character has
				return;
			}
			int right = character.GetInt(ZDOVars.s_rightItem, 0);
			long user = peer.m_uid;
			if (!s_stamina.TryGetValue(user, out List<Stamina> list))
			{
				s_stamina[user] = list = new List<Stamina>();
			}
			if (list.Count > 0 && list[list.Count - 1].right == right && Mathf.Abs(list[list.Count - 1].value - value) < 0.0005f)
			{
				return;
			}
			s_serial.TryGetValue(user, out long serial);
			s_serial[user] = ++serial;
			list.Add(new Stamina { serial = serial, at = Time.time, value = value, right = right });
			// the newest sample older than the memory stays: its value still holds
			while (list.Count > 2 && Time.time - list[1].at > StaminaMemorySeconds)
			{
				list.RemoveAt(0);
			}
		}

		private struct Payment
		{
			public int paid;
			public float before;
			public float credit;
			public float cost;
			public string why;
		}

		// How many plants of a group the player's own game paid for: the stamina of the character went down, from what it was when the first plant
		// arrived (the sum of its falls up to PaidSeconds after the last plant that came), by at least PlantStamina for each (times the world's
		// stamina rate), and the cultivator was in the hand. The game takes the stamina of a press and the seed and the wear of the tool in the same
		// frame, and the mods that plant a grid do the same for each plant (or leave it out, each with a switch); the server sees the stamina only,
		// and cannot tell why it fell.
		private static Payment PaidFor(long user, Chain chain)
		{
			Payment p = new Payment { cost = Mathf.Max(0.1f, Configuration.antiCheatPlantStamina.Value) * Mathf.Max(0f, Game.m_staminaRate) };
			if (!s_stamina.TryGetValue(user, out List<Stamina> list) || list.Count == 0)
			{
				p.why = "the server has seen no stamina of the character";
				return p;
			}
			// the newest sample the server had before the first plant (one that has been forgotten since, in a very long group, is replaced by the oldest left)
			int b = -1;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].serial <= chain.sample)
				{
					b = i;
				}
			}
			if (b < 0)
			{
				if (chain.sample == 0L)
				{
					p.why = "the server had seen no stamina of the character before the plants";
					return p;
				}
				b = 0;
			}
			if (list[b].right != s_cultivator)
			{
				p.why = "the cultivator is not in the player's hand";
				return p;
			}
			p.before = list[b].value;
			float falls = 0f;
			float low = list[b].value;
			// what the character had when the press began: the stamina may have regenerated a little between the sample and the plants
			float peak = list[b].value;
			bool fell = false;
			for (int i = b + 1; i < list.Count && list[i].at <= chain.lastAt + PaidSeconds; i++)
			{
				falls += Mathf.Max(0f, list[i - 1].value - list[i].value);
				low = Mathf.Min(low, list[i].value);
				fell |= list[i].value < list[i - 1].value;
				if (!fell)
				{
					peak = Mathf.Max(peak, list[i].value);
				}
			}
			falls = Mathf.Min(falls, list[b].value + RegenAllowance);
			p.credit = falls;
			p.paid = p.cost <= 0.0005f || falls / p.cost + 0.001f >= chain.count ? chain.count : Mathf.FloorToInt(falls / p.cost + 0.001f);
			if (p.paid < chain.count && p.cost > 0.0005f && low <= ExhaustedStamina && falls >= p.cost && peak > PressStamina)
			{
				// The stamina ran out inside the group. The game takes what there is and no more, and a mod that does not look at the stamina for the
				// extras (PlantEasily) plants the rest of the grid all the same: the group is as paid as the character's stamina could make it.
				p.paid = chain.count;
				p.why = null;
			}
			return p;
		}

		// The plants of one player that follow each other within MinInterval are a group: what one press of a mod that plants a row or a grid
		// makes. The game plants one in 0.4 s, so RatePlants of them or more inside MinInterval are not the game's (the rule that was set from the
		// real world); they are let through when the player's own game paid for them (PaidFor), the first ones that were paid for, in the order
		// they came. Each plant is judged as it comes, against the group so far; the plants that came before the group was a burst are taken now if
		// they are not paid for. A group that is not a burst is left alone, as it always was.
		private static bool Group(Born born, long user, ZNetPeer peer, ZDO zdo, Info info, long planted, Vector3 pos)
		{
			if (ModeOf(Rule.Rate) == Mode.Off)
			{
				return false;
			}
			long window = (long)(Mathf.Max(0.01f, Configuration.antiCheatMinInterval.Value) * TimeSpan.TicksPerSecond);
			int need = Mathf.Max(2, Configuration.antiCheatRatePlants.Value);
			if (!s_chains.TryGetValue(user, out List<Chain> chains))
			{
				s_chains[user] = chains = new List<Chain>();
			}
			Chain chain = null;
			for (int i = chains.Count - 1; i >= 0 && chain == null; i--)
			{
				// (differences in double: a forged planting time may be anything a long can hold)
				if (born.at - chains[i].lastAt <= StaleSeconds && (Math.Abs((double)planted - (double)chains[i].lastTicks) <= window || (chains[i].bursty && born.at - chains[i].lastAt <= ContinueSeconds)))
				{
					chain = chains[i];
				}
			}
			if (chain == null)
			{
				chain = new Chain { sample = born.sample, firstTicks = planted, lastTicks = planted };
				chains.Add(chain);
				if (chains.Count > MaxChains)
				{
					chains.RemoveAt(0);
				}
			}
			chain.count++;
			chain.lastTicks = Math.Max(chain.lastTicks, planted);
			chain.lastAt = Mathf.Max(chain.lastAt, born.at);
			if (chain.ids.Count < ChainIds)
			{
				chain.ids.Add(zdo.m_uid);
			}
			if (!chain.bursty)
			{
				int at = chain.times.BinarySearch(planted);
				chain.times.Insert(at < 0 ? ~at : at, planted);
				if (chain.times.Count > 64)
				{
					chain.times.RemoveAt(0);
				}
				for (int i = 0; i + need <= chain.times.Count && !chain.bursty; i++)
				{
					chain.bursty = (double)chain.times[i + need - 1] - (double)chain.times[i] <= window;
				}
				if (chain.bursty)
				{
					s_groups++;
				}
			}
			// What another game has written to a character says nothing about its player.
			if (!chain.bursty || CharacterTampered(peer))
			{
				return false;
			}
			Payment pay = PaidFor(user, chain);
			string seen = pay.why ?? $"the stamina of the character went down by {pay.credit:0.#} (from {pay.before:0.#}) where the game takes {pay.cost:0.##} for each plant";
			if (!chain.logged && Rate("paid" + user, 5f))
			{
				chain.logged = true;
				Write($"{WhoIs(user, peer)}: a group of {chain.count} or more plants, each within {Configuration.antiCheatMinInterval.Value:0.##} s of the one before ({info.name} at {At(pos)}): {seen}; {Mathf.Min(pay.paid, chain.count)} of {chain.count} paid for so far{(pay.paid >= chain.count ? ": let through" : "")}");
			}
			string detail = $"a group of {chain.count} plants, each within {Configuration.antiCheatMinInterval.Value:0.##} s of the one before (the game plants one in 0.4 s), and not paid for as the game does: {seen}";
			bool taken = false;
			for (int i = chain.dealt; i < chain.ids.Count; i++)
			{
				if (i < pay.paid)
				{
					s_paidPlants++;
					continue;
				}
				ZDO member = ZDOMan.instance.GetZDO(chain.ids[i]);
				if (member != null)
				{
					Hit(Rule.Rate, user, peer, member, info.name, detail, member.GetPosition());
				}
				if (chain.ids[i] == zdo.m_uid)
				{
					taken = true;
				}
			}
			chain.dealt = chain.ids.Count;
			if (chain.count > chain.ids.Count)
			{
				// beyond the plants the group remembers
				if (chain.count - 1 >= pay.paid)
				{
					Hit(Rule.Rate, user, peer, zdo, info.name, detail, pos);
					taken = true;
				}
				else
				{
					s_paidPlants++;
				}
			}
			return taken;
		}

		private static void Burst(long user, ZNetPeer peer, ZDO zdo, Info info, float at, Vector3 pos)
		{
			if (ModeOf(Rule.Burst) == Mode.Off)
			{
				return;
			}
			if (!s_bursts.TryGetValue(user, out Queue<float> times))
			{
				s_bursts[user] = times = new Queue<float>();
			}
			times.Enqueue(at);
			while (times.Count > 0 && at - times.Peek() > 3f)
			{
				times.Dequeue();
			}
			int limit = Mathf.Max(2, Configuration.antiCheatBurstPieces.Value);
			if (times.Count >= limit && Rate("burst" + user, 10f))
			{
				Hit(Rule.Burst, user, peer, zdo, info.name, $"{times.Count} pieces in 3 s (the game places one in 0.4 s)", pos);
			}
		}

		// A dropped item's data (ItemDrop.SaveToZDO): the 1.0 game keeps stack, quality and durability inside one blob; older saves had keys of their own.
		private static void ReadDrop(ZDO zdo, out int stack, out int quality, out float durability, out bool cheated)
		{
			cheated = false;
			stack = zdo.GetInt(ZDOVars.s_stack, 1);
			quality = zdo.GetInt(ZDOVars.s_quality, 1);
			durability = zdo.GetFloat(ZDOVars.s_durability, 0f);
			byte[] data = zdo.GetByteArray(ZDOVars.s_itemData);
			if (data != null && data.Length > 2)
			{
				try
				{
					ZPackage package = new ZPackage(data);
					global::Version.Item version = (global::Version.Item)package.ReadByte();
					ItemDrop.ItemData itemData = new ItemDrop.ItemData();
					ItemDrop.ItemData.Load(package, itemData, version);
					stack = itemData.m_stack;
					quality = itemData.m_quality;
					durability = itemData.m_durability;
					cheated = itemData.m_cheated;
				}
				catch (Exception)
				{
					// not readable: the keys above stand
				}
			}
		}

		// What is wrong with an item that has this stack, quality and durability, or null.
		private static string TooMuch(Info info, int stack, int quality, float durability)
		{
			if (stack > info.maxStack)
			{
				return $"stack of {stack}, the most the item can have is {info.maxStack}";
			}
			// Quality has no ceiling in the game since 1.0: the upgrade station takes it one step at a time past the recipe's top, and an egg or a
			// fish carries the level of the creature. So it is checked only against MaxQuality when the owner set one (0 = not checked).
			int maxQuality = Configuration.antiCheatMaxQuality.Value;
			if (maxQuality > 0 && quality > Mathf.Max(info.maxQuality, maxQuality))
			{
				return $"quality {quality}, more than MaxQuality {maxQuality}";
			}
			if (info.data.m_shared.m_useDurability && durability > info.data.GetMaxDurability(Mathf.Max(1, quality)) * 1.01f + 1f)
			{
				return $"durability {durability:0}, the most it can have is {info.data.GetMaxDurability(Mathf.Max(1, quality)):0}";
			}
			return null;
		}

		private static void JudgeItem(ZDO zdo, ZNetPeer peer, long user, Info info, Vector3 pos)
		{
			if (ModeOf(Rule.Items) == Mode.Off)
			{
				return;
			}
			ReadDrop(zdo, out int stack, out int quality, out float durability, out bool cheated);
			string wrong = TooMuch(info, stack, quality, durability);
			if (wrong != null)
			{
				Hit(Rule.Items, user, peer, zdo, info.name, wrong, pos);
			}
			else if (cheated)
			{
				// Items that a cheating game made carry the game's mark in their data (NoCheatedTag and CheaterBypass keep it from being written);
				// so do items an admin handed out, which is why it is only a trace.
				Hit(Rule.CheatObject, user, peer, zdo, info.name, "the item carries the game's mark for cheating", pos);
			}
		}

		private static string NameOfCharacter(long id)
		{
			return CharacterGuard.NameOfCharacter(id) ?? "character " + id;
		}

		// ---- what is done -------------------------------------------------------------------------------------------------

		// True when the line was written (the first finding of its kind for the player in ten seconds); the ones that follow are counted.
		private static bool Hit(Rule rule, long user, ZNetPeer peer, ZDO zdo, string prefab, string detail, Vector3 pos)
		{
			Mode mode = ModeOf(rule);
			if (mode == Mode.Off)
			{
				return false;
			}
			s_hits[(int)rule]++;
			Action action = mode == Mode.On ? Configuration.antiCheatAction.Value : Action.Log;
			List<string> did = new List<string>();
			if (action >= Action.Revert && zdo != null && Revertible(rule))
			{
				Revert(zdo);
				s_reverted++;
				did.Add("taken away");
			}
			if (action == Action.Kick && peer != null && !s_noBlame)
			{
				if (Soft(rule) && Strike(user) < Configuration.antiCheatSoftStrikes.Value)
				{
					Warn(peer, user, rule);
					did.Add("warned, kicked the next time");
				}
				else if (!BeingKicked(user))
				{
					Schedule(peer, user, s_labels[(int)rule]);
					did.Add("kicked");
				}
				else
				{
					did.Add("already being kicked");
				}
			}
			string outcome = mode == Mode.LogOnly ? "LogOnly: let through" : action == Action.Log ? "only logged (Action Log)" : did.Count > 0 ? string.Join(", ", did) : "nothing to take away";
			string who = WhoIs(user, peer);
			string line = $"{who}: {s_labels[(int)rule]}: {prefab} at {At(pos)}: {detail}";
			string key = user + "|" + (int)rule;
			if (s_tallies.TryGetValue(key, out Tally tally))
			{
				tally.count++;
				tally.outcome = outcome;
				return false;
			}
			s_tallies[key] = new Tally { since = Time.time, count = 0, who = who, label = s_labels[(int)rule], outcome = outcome };
			Write($"{line}; {outcome}");
			return true;
		}

		// How many incidents in the last ten minutes a rule of the warn-first kind was broken in, this one included. Breaks less than IncidentSeconds
		// after the last one are the same incident: one mistake of an honest player can be several objects, and a warning needs time to be read.
		private static int Strike(long user)
		{
			if (!s_strikes.TryGetValue(user, out List<float> times))
			{
				s_strikes[user] = times = new List<float>();
			}
			times.RemoveAll(t => Time.time - t > 600f);
			if (times.Count == 0 || Time.time - times[times.Count - 1] >= IncidentSeconds)
			{
				times.Add(Time.time);
			}
			return times.Count;
		}

		private static void Warn(ZNetPeer peer, long user, Rule rule)
		{
			string message = (Configuration.antiCheatWarnMessage.Value ?? "").Replace("{0}", s_labels[(int)rule]);
			if (message.Length > 0 && Rate("warn" + user, 3f))
			{
				PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, message);
			}
		}

		// A kick another guard asks for (the item ledger): the same message, wait and memory as the rules' own. False when the anti-cheat
		// is off (nobody would run the kick), the player is gone, exempt as an admin or already being kicked.
		public static bool KickFor(long user, string why)
		{
			if (!Configuration.antiCheatEnabled.Value || ZNet.instance == null || !ZNet.instance.IsServer())
			{
				return false;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(user);
			if (peer == null || IsAdmin(peer) || BeingKicked(user))
			{
				return false;
			}
			Schedule(peer, user, why);
			return true;
		}

		private static bool BeingKicked(long user)
		{
			return s_kickAt.ContainsKey(user) || (s_kicked.TryGetValue(user, out float at) && Time.time - at < KickMemorySeconds);
		}

		// At most a hundred and twenty lines a minute into the log, the rest counted: a game that makes a new session every few seconds cannot
		// fill the disk with lines about itself.
		private static void Write(string text)
		{
			if (Time.time - s_writeWindow > 60f)
			{
				if (s_writeSuppressed > 0)
				{
					string note = $"{s_writeSuppressed} more line(s) in the last minute were not written";
					ServersidePlugin.logger.LogWarning("Anti-cheat: " + note);
					GuardLog.Write("AntiCheat: " + note);
				}
				s_writeWindow = Time.time;
				s_writeCount = 0;
				s_writeSuppressed = 0;
			}
			if (++s_writeCount > 120)
			{
				s_writeSuppressed++;
				return;
			}
			ServersidePlugin.logger.LogWarning("Anti-cheat: " + text);
			GuardLog.Write("AntiCheat: " + text);
		}

		// The first finding of a kind for a player is written at once; the ones that follow are counted and written together.
		private static void Flush(bool all)
		{
			if (s_tallies.Count == 0)
			{
				return;
			}
			foreach (string key in s_tallies.Keys.ToList())
			{
				Tally tally = s_tallies[key];
				if (!all && Time.time - tally.since < 10f)
				{
					continue;
				}
				s_tallies.Remove(key);
				if (tally.count > 0)
				{
					Write($"{tally.who}: {tally.label}: and {tally.count} more in the last {Time.time - tally.since:0} s; {tally.outcome}");
				}
			}
		}

		// What a player's game has not got right, taken away the way the game takes down a piece that is not a WearNTear.
		private static void Revert(ZDO zdo)
		{
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZNetView view = ZNetScene.instance.FindInstance(zdo);
			if (view)
			{
				ZNetScene.instance.Destroy(view.gameObject);
			}
			else
			{
				ZDOMan.instance.DestroyZDO(zdo);
			}
		}

		private static void Schedule(ZNetPeer peer, long user, string why)
		{
			string message = (Configuration.antiCheatMessage.Value ?? "").Replace("{0}", why);
			float held = message.Length > 0 ? PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, message) : 0f;
			// The kick waits until the message has been held, so that it can be read to its end.
			s_kickAt[user] = Time.time + Mathf.Max(KickDelaySeconds, held);
		}

		private static void RunKicks()
		{
			if (s_kickAt.Count == 0)
			{
				return;
			}
			foreach (long user in s_kickAt.Where(kv => Time.time >= kv.Value).Select(kv => kv.Key).ToList())
			{
				s_kickAt.Remove(user);
				s_kicked[user] = Time.time;
				s_kicks++;
				ZNetPeer peer = ZNet.instance.GetPeer(user);
				if (peer != null)
				{
					Write($"{Who(peer)} is kicked");
					ZNet.instance.InternalKick(peer);
				}
			}
		}

		// ---- the combat rules -----------------------------------------------------------------------------------------------
		// Unhurt, Stamina, Damage and Skills (all Off by default), and the Items rule for the weapon in the hand. The game keeps none of it
		// where the server can see: health, stamina and skills are kept by the player's own game. What the server sees is what the game
		// sends: the character's data (health, stamina, what it holds and wears), the hits it sends to what it hits (a routed RPC with the
		// HitData: the damage, the skill and its level, the weapon's quality) and the hits the server's creatures land on it (Character.Damage
		// runs on the server for a player's character; the player's game applies them). Nothing here runs while all four rules and Items are Off.

		private static bool CombatOn()
		{
			return ModeOf(Rule.Unhurt) != Mode.Off || ModeOf(Rule.Stamina) != Mode.Off || ModeOf(Rule.Damage) != Mode.Off || ModeOf(Rule.Skills) != Mode.Off;
		}

		private static readonly int s_rpcDamage = "RPC_Damage".GetStableHashCode();
		private static readonly int s_rpcHit = "Hit".GetStableHashCode();
		private static readonly int s_rpcApplyOperation = "RPC_ApplyOperation".GetStableHashCode();

		// A hit the server's creature landed on a player's character, kept for the evidence line (the health after it is filled in when it is judged).
		private class Taken
		{
			public float at;
			public string attacker;
			public float raw;
			public float expected;
			public float before;
			public float after = -1f;
			public bool blocked;
			public bool dodging;
		}

		// A hit the player's game sent to what it hit.
		private class Dealt
		{
			public float at;
			public string target;
			public Skills.SkillType skill;
			public float level;
			public int itemLevel;
			public float damage;
			public float stamina;
		}

		// A hit on a character waiting for the character's own data to come: judged when the data has come (Unhurt).
		private class Pending
		{
			public float at;
			public uint revision;
			public float before;
			public float max;
			public float raw;
			public float expected;
			public bool blocked;
			public bool dodging;
			public Taken entry;
		}

		private class Beat
		{
			public float at;
			public float stamina;
			public uint revision;
		}

		private class Fighter
		{
			public long user;
			public float seen;
			public readonly List<Pending> pending = new List<Pending>();
			public int streak;
			public float streakTotal;
			public readonly List<float> clamps = new List<float>();
			public readonly List<Beat> attacks = new List<Beat>();
			public readonly List<Beat> ticks = new List<Beat>();
			public float lastAttack = -99f;
			public readonly Queue<Taken> taken = new Queue<Taken>();
			public readonly Queue<Dealt> dealt = new Queue<Dealt>();
			public readonly Dictionary<int, float> skillFound = new Dictionary<int, float>();
		}

		private static readonly Dictionary<long, Fighter> s_fighters = new Dictionary<long, Fighter>();
		private const int EvidenceKept = 24;
		private const int EvidenceShown = 8;
		private const int MaxPending = 32;
		private const float JudgeAfterSeconds = 1.5f;
		private const float JudgeUntilSeconds = 6f;
		private const float StaminaWindowSeconds = 20f;
		private const int MinTickSamples = 3;
		private const float SameAttackSeconds = 0.25f;
		private static float s_nextCombat;
		private static float s_nextCombatPlayers;

		private static Fighter FighterOf(ZNetPeer peer)
		{
			if (!s_fighters.TryGetValue(peer.m_uid, out Fighter f))
			{
				s_fighters[peer.m_uid] = f = new Fighter { user = peer.m_uid, seen = Time.time };
			}
			return f;
		}

		// A player the combat rules look at: ready, with a character of its own (made by its own game), not an admin, and a character that no other
		// game has written to.
		private static ZDO Fighting(ZNetPeer peer)
		{
			if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone() || peer.m_characterID.UserID != peer.m_uid || AdminOf(peer) || CharacterTampered(peer))
			{
				return null;
			}
			return ZDOMan.instance.GetZDO(peer.m_characterID);
		}

		// ---- what the game says is possible -------------------------------------------------------------------------------

		private static bool s_boundsReady;
		private static float s_maxHit;
		private static float s_maxHitTamedOnly;
		private static float s_shieldMax;
		private static float s_armorAdd;
		private static float s_armorMultiplier = 1f;
		private static readonly Dictionary<int, float> s_skillBonus = new Dictionary<int, float>();
		private static readonly Dictionary<int, float> s_skillStep = new Dictionary<int, float>();
		private static readonly float[] s_levelCost = new float[102];
		private static readonly Dictionary<int, ItemDrop.ItemData.SharedData> s_shared = new Dictionary<int, ItemDrop.ItemData.SharedData>();

		private static readonly Skills.SkillType[] TrackedSkills =
		{
			Skills.SkillType.Swords, Skills.SkillType.Knives, Skills.SkillType.Clubs, Skills.SkillType.Polearms, Skills.SkillType.Spears, Skills.SkillType.Axes,
			Skills.SkillType.Bows, Skills.SkillType.Crossbows, Skills.SkillType.Unarmed, Skills.SkillType.Pickaxes, Skills.SkillType.WoodCutting, Skills.SkillType.ElementalMagic,
		};

		private static bool IsMelee(Skills.SkillType skill)
		{
			switch (skill)
			{
				case Skills.SkillType.Swords:
				case Skills.SkillType.Knives:
				case Skills.SkillType.Clubs:
				case Skills.SkillType.Polearms:
				case Skills.SkillType.Spears:
				case Skills.SkillType.Axes:
				case Skills.SkillType.Unarmed:
				case Skills.SkillType.Pickaxes:
				case Skills.SkillType.WoodCutting:
					return true;
				default:
					return false;
			}
		}

		private static bool IsTracked(Skills.SkillType skill)
		{
			return Array.IndexOf(TrackedSkills, skill) >= 0;
		}

		// What the server's game says is the most that anything gives: computed once, from the status effects and items of ObjectDB, and logged.
		// - the largest absorb a protection shield can have (SE_Shield, at skill 100, in the world's level);
		// - the armour a status effect adds to a character (SE_Stats), for the armour the Unhurt rule allows;
		// - for each tracked skill, the sum of all that any status effect adds to its level (the server cannot know which are active);
		// - the cost of each level (the experience from level 0 to L) and the experience step of each skill (from the Player prefab);
		// - the strongest hit a weapon can send (the best weapon at the best quality it has, every multiplier at its largest).
		private static void CombatBounds()
		{
			if (s_boundsReady)
			{
				return;
			}
			s_boundsReady = true;
			try
			{
				List<StatusEffect> effects = new List<StatusEffect>();
				HashSet<StatusEffect> known = new HashSet<StatusEffect>();
				void add(StatusEffect e)
				{
					if (e != null && known.Add(e))
					{
						effects.Add(e);
					}
				}
				foreach (StatusEffect e in ObjectDB.instance.m_StatusEffects)
				{
					add(e);
				}
				float strongestMelee = 0f, strongestAmmo = 0f, strongestBow = 0f, strongestTamedOnly = 0f, damageMultiplier = 1f;
				float worldLevel = Mathf.Max(0f, Game.m_worldLevel);
				float worldDamage = worldLevel * (Game.instance != null ? Game.instance.m_worldLevelGearBaseDamage : 120);
				List<(float hit, string name, float total, float multiplier)> melee = new List<(float, string, float, float)>();
				string meleeName = "?", ammoName = "?", bowName = "?", tamedOnlyName = "?";
				Dictionary<string, Recipe> recipeOf = new Dictionary<string, Recipe>();
				foreach (Recipe r in ObjectDB.instance.m_recipes)
				{
					if (r != null && r.m_enabled && r.m_item != null)
					{
						recipeOf[r.m_item.name] = r;
					}
				}
				HashSet<string> craftable = new HashSet<string>(recipeOf.Keys);
				// the status effects of the items first: what a status effect does to an attack (SE_Stats.ModifyAttack) is a multiplier on the whole
				// hit and a percentage on each damage type of its own, so the percentages are taken type by type (the Elder's +60% is chop only)
				foreach (GameObject go in ObjectDB.instance.m_items)
				{
					ItemDrop drop = go ? go.GetComponent<ItemDrop>() : null;
					if (drop != null)
					{
						ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
						add(s.m_equipStatusEffect);
						add(s.m_setStatusEffect);
						add(s.m_consumeStatusEffect);
						add(s.m_attackStatusEffect);
					}
				}
				HitData.DamageTypes percent = default;
				foreach (SE_Stats st in effects.OfType<SE_Stats>())
				{
					damageMultiplier = Mathf.Max(damageMultiplier, st.m_damageModifier);
					HitData.DamageTypes p = st.m_percentigeDamageModifiers;
					percent.m_damage = Mathf.Max(percent.m_damage, p.m_damage);
					percent.m_blunt = Mathf.Max(percent.m_blunt, p.m_blunt);
					percent.m_slash = Mathf.Max(percent.m_slash, p.m_slash);
					percent.m_pierce = Mathf.Max(percent.m_pierce, p.m_pierce);
					percent.m_chop = Mathf.Max(percent.m_chop, p.m_chop);
					percent.m_pickaxe = Mathf.Max(percent.m_pickaxe, p.m_pickaxe);
					percent.m_fire = Mathf.Max(percent.m_fire, p.m_fire);
					percent.m_frost = Mathf.Max(percent.m_frost, p.m_frost);
					percent.m_lightning = Mathf.Max(percent.m_lightning, p.m_lightning);
					percent.m_poison = Mathf.Max(percent.m_poison, p.m_poison);
					percent.m_spirit = Mathf.Max(percent.m_spirit, p.m_spirit);
					percent.m_nonPlayer = Mathf.Max(percent.m_nonPlayer, p.m_nonPlayer);
				}
				string percentText = string.Join(" ", new[] { ("damage", percent.m_damage), ("blunt", percent.m_blunt), ("slash", percent.m_slash), ("pierce", percent.m_pierce),
					("chop", percent.m_chop), ("pickaxe", percent.m_pickaxe), ("fire", percent.m_fire), ("frost", percent.m_frost), ("lightning", percent.m_lightning),
					("poison", percent.m_poison), ("spirit", percent.m_spirit), ("nonPlayer", percent.m_nonPlayer) }.Where(t => t.Item2 > 0f).Select(t => $"{t.Item1} +{t.Item2 * 100f:0}%"));
				foreach (GameObject go in ObjectDB.instance.m_items)
				{
					ItemDrop drop = go ? go.GetComponent<ItemDrop>() : null;
					if (drop == null)
					{
						continue;
					}
					ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
					add(shared.m_equipStatusEffect);
					add(shared.m_setStatusEffect);
					add(shared.m_consumeStatusEffect);
					add(shared.m_attackStatusEffect);
					bool weapon = shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon
						|| shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || shared.m_itemType == ItemDrop.ItemData.ItemType.Bow || shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo;
					// only what a player can make: the items of creatures (their attacks are items too) and the admin-only ones (SledgeCheat) have no recipe
					if (!weapon || !craftable.Contains(drop.name))
					{
						continue;
					}
					HitData.DamageTypes damage = shared.m_damages;
					damage.Add(shared.m_damagesPerLevel, Mathf.Max(0, shared.m_maxQuality - 1));
					if (worldDamage > 0f)
					{
						// ItemData.GetDamage(quality, worldLevel): IncreaseEqually(..., separate utility damage). The chop part grows on top of the increase,
						// so the total grows by more than worldDamage; the game's own method says what it comes to.
						damage.IncreaseEqually(worldDamage, true);
					}
					damage.Modify(percent);
					float total = damage.GetTotalDamage();
					float multiplier = 1f;
					foreach (Attack attack in new[] { shared.m_attack, shared.m_secondaryAttack })
					{
						if (attack == null)
						{
							continue;
						}
						float m = Mathf.Max(1f, attack.m_damageMultiplier);
						if (attack.m_attackChainLevels > 1)
						{
							m *= Mathf.Max(1f, attack.m_lastChainDamageMultiplier);
						}
						m *= 1f + Mathf.Max(0f, attack.m_damageMultiplierByTotalHealthMissing);
						multiplier = Mathf.Max(multiplier, m);
					}
					float hit = total * multiplier;
					if (shared.m_tamedOnly)
					{
						// a weapon that only works on tamed animals (the butcher knife) has a bound of its own, used only against a tamed target
						if (hit > strongestTamedOnly)
						{
							strongestTamedOnly = hit;
							tamedOnlyName = drop.name;
						}
						continue;
					}
					if (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo)
					{
						if (hit > strongestAmmo)
						{
							strongestAmmo = hit;
							ammoName = drop.name;
						}
					}
					else if (shared.m_itemType == ItemDrop.ItemData.ItemType.Bow)
					{
						if (hit > strongestBow)
						{
							strongestBow = hit;
							bowName = drop.name;
						}
					}
					else
					{
						melee.Add((hit, drop.name + "[q" + shared.m_maxQuality + " a" + (shared.m_attack != null ? shared.m_attack.m_damageMultiplier.ToString("0.##") + "/" + shared.m_attack.m_attackChainLevels + "/" + shared.m_attack.m_lastChainDamageMultiplier.ToString("0.##") + "/" + shared.m_attack.m_damageMultiplierByTotalHealthMissing.ToString("0.##") : "-") + " b" + (shared.m_secondaryAttack != null ? shared.m_secondaryAttack.m_damageMultiplier.ToString("0.##") + "/" + shared.m_secondaryAttack.m_damageMultiplierByTotalHealthMissing.ToString("0.##") : "-") + " " + (recipeOf.TryGetValue(drop.name, out Recipe rec) ? (rec.m_craftingStation ? rec.m_craftingStation.name : "no station") + " " + rec.m_minStationLevel : "-") + "]", total, multiplier));
						if (hit > strongestMelee)
						{
							strongestMelee = hit;
							meleeName = drop.name;
						}
					}
				}
				foreach (StatusEffect e in effects)
				{
					if (e is SE_Shield shield)
					{
						s_shieldMax = Mathf.Max(s_shieldMax, shield.m_absorbDamage + shield.m_absorbDamagePerSkillLevel * 100f + shield.m_absorbDamageWorldLevel * worldLevel);
					}
				}
				// armour: the largest additions of two effects at once, and the largest multiplier
				List<float> adds = effects.OfType<SE_Stats>().Select(s => s.m_addArmor).Where(a => a > 0f).OrderByDescending(a => a).ToList();
				s_armorAdd = adds.Take(2).Sum();
				s_armorMultiplier = 1f + effects.OfType<SE_Stats>().Select(s => s.m_armorMultiplier).DefaultIfEmpty(0f).Max();
				foreach (Skills.SkillType skill in TrackedSkills)
				{
					float bonus = 0f;
					foreach (SE_Stats stats in effects.OfType<SE_Stats>())
					{
						if (stats.m_skillLevel != Skills.SkillType.None && (stats.m_skillLevel == Skills.SkillType.All || stats.m_skillLevel == skill) && stats.m_skillLevelModifier > 0f)
						{
							bonus += stats.m_skillLevelModifier;
						}
						if (stats.m_skillLevel2 != Skills.SkillType.None && (stats.m_skillLevel2 == Skills.SkillType.All || stats.m_skillLevel2 == skill) && stats.m_skillLevelModifier2 > 0f)
						{
							bonus += stats.m_skillLevelModifier2;
						}
					}
					s_skillBonus[(int)skill] = bonus;
				}
				Skills skills = ZNetScene.instance.GetPrefab("Player")?.GetComponent<Skills>();
				if (skills != null)
				{
					foreach (Skills.SkillDef def in skills.m_skills)
					{
						s_skillStep[(int)def.m_skill] = def.m_increseStep;
					}
				}
				for (int level = 1; level <= 100; level++)
				{
					s_levelCost[level] = s_levelCost[level - 1] + Mathf.Pow(level, 1.5f) * 0.5f + 0.5f;
				}
				s_levelCost[101] = s_levelCost[100];
				// the percentages of status effects are in each weapon's total already (type by type); the multiplier is on the whole hit
				float strongest = Mathf.Max(strongestMelee, strongestBow + strongestAmmo) * damageMultiplier;
				s_maxHit = strongest * 1.5f;
				s_maxHitTamedOnly = strongestTamedOnly * damageMultiplier * 1.5f;
				ServersidePlugin.logger.LogInfo($"Anti-cheat: Damage bound {(Configuration.antiCheatMaxDamage.Value > 0f ? Configuration.antiCheatMaxDamage.Value.ToString("0") + " (MaxDamage)" : s_maxHit.ToString("0") + " (automatic)")}; automatic {s_maxHit:0} = the strongest hit {strongest:0} x 1.5, "
					+ $"tamed targets with a tamed-only weapon {s_maxHitTamedOnly:0} ({tamedOnlyName} {strongestTamedOnly:0}); status effects x{damageMultiplier:0.##} and by type {(percentText.Length > 0 ? percentText : "nothing")} at most, world level {worldLevel:0} adds {worldDamage:0} per weapon");
				ServersidePlugin.logger.LogInfo($"Anti-cheat: combat bounds from the game: the largest protection shield absorbs {s_shieldMax:0}; status effects add up to {s_armorAdd:0} armour (x{s_armorMultiplier:0.##}); "
					+ $"the strongest hit a weapon sends, all multipliers at their largest (x{damageMultiplier:0.##} and the percentages by type for status effects): {strongest:0} (melee {strongestMelee:0} {meleeName}; bow {strongestBow:0} {bowName} + arrow {strongestAmmo:0} {ammoName})");
				ServersidePlugin.logger.LogInfo("Anti-cheat: the strongest melee weapons (hit = damage at the top quality x the largest attack multiplier): "
					+ string.Join(", ", melee.OrderByDescending(m => m.hit).Take(12).Select(m => $"{m.name} {m.hit:0} ({m.total:0} x {m.multiplier:0.##})")));
				ServersidePlugin.logger.LogInfo("Anti-cheat: skill level bonus of status effects, most of any: " + string.Join(", ", TrackedSkills.Select(s => $"{s} +{s_skillBonus[(int)s]:0.#} (step {(s_skillStep.TryGetValue((int)s, out float step) ? step : 0f):0.##})")));
			}
			catch (Exception e)
			{
				s_boundsReady = false;
				s_shieldMax = 0f;
				s_maxHit = 0f;
				s_maxHitTamedOnly = 0f;
				Fault(e);
			}
		}

		// The item whose name hash a character's data holds in a slot (the game writes the hash of the prefab's name).
		private static ItemDrop.ItemData.SharedData SharedOf(int hash)
		{
			if (hash == 0)
			{
				return null;
			}
			if (!s_shared.TryGetValue(hash, out ItemDrop.ItemData.SharedData shared))
			{
				GameObject go = ObjectDB.instance.GetItemPrefab(hash);
				ItemDrop drop = go ? go.GetComponent<ItemDrop>() : null;
				s_shared[hash] = shared = drop != null ? drop.m_itemData.m_shared : null;
			}
			return shared;
		}

		private static bool IsWeaponType(ItemDrop.ItemData.ItemType type)
		{
			return type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
		}

		// The weapon a character holds: the right hand's item, or the left hand's when that is a two-handed weapon that goes in the left. Its quality is
		// 0 when the character does not say.
		private static ItemDrop.ItemData.SharedData WeaponIn(ZDO character, out int quality, out string name)
		{
			quality = 0;
			name = "";
			int right = character.GetInt(ZDOVars.s_rightItem, 0);
			ItemDrop.ItemData.SharedData shared = SharedOf(right);
			if (shared != null && IsWeaponType(shared.m_itemType))
			{
				name = ObjectDB.instance.GetItemPrefab(right).name;
				quality = character.GetInt(ZDOVars.s_rightItemQuality, 0);
				return shared;
			}
			int left = character.GetInt(ZDOVars.s_leftItem, 0);
			shared = SharedOf(left);
			if (shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
			{
				name = ObjectDB.instance.GetItemPrefab(left).name;
				quality = character.GetInt(ZDOVars.s_leftItemQuality, 0);
				return shared;
			}
			return null;
		}

		// The most armour the character could be wearing: each worn piece at its largest quality, and what the effects can add, with a quarter over.
		private static float ArmorUpper(ZDO character)
		{
			float armor = 0f;
			float world = Mathf.Max(0f, Game.m_worldLevel) * (Game.instance != null ? Game.instance.m_worldLevelGearBaseAC : 38);
			foreach (int slot in new[] { ZDOVars.s_helmetItem, ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_shoulderItem })
			{
				ItemDrop.ItemData.SharedData shared = SharedOf(character.GetInt(slot, 0));
				if (shared != null)
				{
					armor += shared.m_armor + shared.m_armorPerLevel * Mathf.Max(0, shared.m_maxQuality - 1) + world;
				}
			}
			return (armor + s_armorAdd) * s_armorMultiplier * 1.25f;
		}

		// ---- evidence ---------------------------------------------------------------------------------------------------------

		private static void Remember<T>(Queue<T> ring, T entry)
		{
			ring.Enqueue(entry);
			while (ring.Count > EvidenceKept)
			{
				ring.Dequeue();
			}
		}

		// After a finding of the combat rules: the last hits taken or dealt, for the owner to read next to the finding.
		private static void Evidence(Rule rule, long user)
		{
			if (!s_fighters.TryGetValue(user, out Fighter f))
			{
				return;
			}
			float now = Time.time;
			List<string> lines;
			if (rule == Rule.Unhurt)
			{
				lines = f.taken.Reverse().Take(EvidenceShown).Reverse().Select(t => $"-{now - t.at:0.0}s {t.attacker} raw {t.raw:0} expected {t.expected:0} health {t.before:0.###}>{(t.after < 0f ? "?" : t.after.ToString("0.###"))}{(t.blocked ? " blocking" : "")}{(t.dodging ? " dodging" : "")}").ToList();
			}
			else
			{
				lines = f.dealt.Reverse().Take(EvidenceShown).Reverse().Select(d => $"-{now - d.at:0.0}s {d.target} {d.skill} lvl {d.level:0} item {d.itemLevel} dmg {d.damage:0} stamina {d.stamina:0.#}").ToList();
			}
			if (lines.Count > 0)
			{
				Write("  evidence: " + string.Join("; ", lines));
			}
		}

		// A finding of the combat rules (and of Items, for the weapon): the line, and the evidence under it when the line was written.
		private static void Finding(Rule rule, ZNetPeer peer, string prefab, string detail, Vector3 pos)
		{
			if (Hit(rule, peer.m_uid, peer, null, prefab, detail, pos))
			{
				Evidence(rule, peer.m_uid);
			}
		}

		// ---- Unhurt: hits taken ------------------------------------------------------------------------------------------------

		// Character.Damage on the server for a player's character: the server's creatures (their Attack, Projectile and Aoe) call it, and it sends the hit on to
		// the player's game, which armours, resists, blocks, dodges and applies it. Here the hit is noted, with what the character looked like.
		[HarmonyPatch(typeof(Character), "Damage")]
		public static class Character_Damage_Patch
		{
			static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null || !(__instance is Player) || !Configuration.antiCheatEnabled.Value || ModeOf(Rule.Unhurt) == Mode.Off)
				{
					return;
				}
				try
				{
					Struck((Player)__instance, hit);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static void Struck(Player player, HitData hit)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
			{
				return;
			}
			ZNetView view = player.GetComponent<ZNetView>();
			ZDO zdo = view != null ? view.GetZDO() : null;
			if (zdo == null || zdo.GetOwner() == ZDOMan.GetSessionID())
			{
				return;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(zdo.GetOwner());
			if (peer == null || peer.m_characterID != zdo.m_uid || Fighting(peer) == null)
			{
				return;
			}
			// A hit of another player (PvP) is another matter; a hit whose attacker the game cannot find is not applied by it.
			Character attacker = null;
			if (hit.HaveAttacker())
			{
				attacker = hit.GetAttacker();
				if (attacker == null || attacker.IsPlayer())
				{
					return;
				}
			}
			float max = zdo.GetFloat(ZDOVars.s_maxHealth, 0f);
			float before = zdo.GetFloat(ZDOVars.s_health, max);
			if (max <= 0f || before <= 0f || zdo.GetBool(ZDOVars.s_dead) || zdo.GetBool(ZDOVars.s_debugFly))
			{
				return;
			}
			// What the player's game does to a creature's hit before it armours it (Character.RPC_Damage).
			float scale = 1f;
			if (attacker != null)
			{
				try
				{
					scale = Game.instance.GetDifficultyDamageScalePlayer(player.transform.position) * Game.m_enemyDamageRate;
				}
				catch (Exception)
				{
					scale = Game.m_enemyDamageRate;
				}
			}
			float armor = ArmorUpper(zdo);
			HitData.DamageTypes d = hit.m_damage;
			float expected = 0f;
			foreach (float part in new[] { d.m_blunt, d.m_slash, d.m_pierce })
			{
				if (part > 0f)
				{
					expected += HitData.DamageTypes.ApplyArmor(part * scale, armor);
				}
			}
			expected *= 0.25f;
			Fighter f = FighterOf(peer);
			Taken taken = new Taken
			{
				at = Time.time, attacker = attacker != null ? global::Utils.GetPrefabName(attacker.gameObject) : hit.m_hitType.ToString(), raw = hit.GetTotalDamage() * scale, expected = expected, before = before,
				blocked = zdo.GetBool(ZDOVars.s_isBlockingHash), dodging = zdo.GetBool(ZDOVars.s_dodgeinv),
			};
			Remember(f.taken, taken);
			f.pending.Add(new Pending
			{
				at = taken.at, revision = zdo.DataRevision, before = before, max = max, raw = taken.raw, expected = expected, blocked = taken.blocked, dodging = taken.dodging, entry = taken,
			});
			if (f.pending.Count > MaxPending)
			{
				f.pending.RemoveAt(0);
			}
		}

		// The checks that have had the character's own data since the hit (at least three newer revisions: the player's game writes health every
		// frame it changes), 1.5 s or more after it; the ones that get none within 6 s are dropped without a verdict.
		private static void JudgeTaken()
		{
			foreach (Fighter f in s_fighters.Values.ToList())
			{
				if (f.pending.Count == 0)
				{
					continue;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(f.user);
				ZDO character = peer != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
				if (peer == null || character == null || ModeOf(Rule.Unhurt) == Mode.Off)
				{
					f.pending.Clear();
					continue;
				}
				float now = Time.time;
				foreach (Pending p in f.pending.ToList())
				{
					float age = now - p.at;
					if (age < JudgeAfterSeconds)
					{
						continue;
					}
					if (character.DataRevision < p.revision + 3)
					{
						if (age > JudgeUntilSeconds)
						{
							f.pending.Remove(p);
						}
						continue;
					}
					f.pending.Remove(p);
					try
					{
						JudgeOne(f, peer, character, p, now);
					}
					catch (Exception e)
					{
						Fault(e);
					}
				}
			}
		}

		private static void JudgeOne(Fighter f, ZNetPeer peer, ZDO character, Pending p, float now)
		{
			float health = character.GetFloat(ZDOVars.s_health, p.max);
			bool dead = character.GetBool(ZDOVars.s_dead);
			p.entry.after = health;
			// A lethal hit that left exactly 1.0: the game's god mode clamps to it, an honest health never lands on it.
			if (!dead && Mathf.Abs(health - 1f) < 0.0005f && p.before > 1.5f && p.raw >= p.before - 1f)
			{
				f.clamps.RemoveAll(t => now - t > 600f);
				f.clamps.Add(now);
				f.streak = 0;
				f.streakTotal = 0f;
				if (f.clamps.Count >= 2)
				{
					Finding(Rule.Unhurt, peer, "Player", $"a lethal hit left it at exactly 1 health, {f.clamps.Count} times in {(now - f.clamps[0]) / 60f:0.#} min", character.GetPosition());
				}
				return;
			}
			if (dead || health < p.before - 0.05f)
			{
				f.streak = 0;
				f.streakTotal = 0f;
				return;
			}
			bool telling = !p.blocked && !p.dodging && !character.GetBool(ZDOVars.s_isBlockingHash) && !character.GetBool(ZDOVars.s_dodgeinv) && p.expected >= Mathf.Max(0.1f * p.max, 5f);
			if (!telling)
			{
				return;
			}
			f.streak++;
			f.streakTotal += p.expected;
			if (f.streak >= Configuration.antiCheatUnhurtHits.Value && f.streakTotal >= s_shieldMax)
			{
				Finding(Rule.Unhurt, peer, "Player", $"{f.streak} hits in a row did not lower its health (they were worth {f.streakTotal:0} at the least; a protection shield absorbs {s_shieldMax:0} at the most)", character.GetPosition());
				f.streak = 0;
				f.streakTotal = 0f;
			}
		}

		// ---- hits dealt: Damage, Stamina, Skills, Items ------------------------------------------------------------------------

		// First of the routed RPCs, in AntiCheat: it looks only at the three methods it needs (compared by hash before anything is read), reads a copy
		// of the package and puts the position back; false only drops a hit of the Damage rule.
		[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
		public static class ZRoutedRpc_RPC_RoutedRPC_Patch
		{
			static bool Prefix(ZRpc rpc, ZPackage pkg)
			{
				if (pkg == null || !Configuration.antiCheatEnabled.Value || !(CombatOn() || ModeOf(Rule.Items) != Mode.Off) || ZNet.instance == null || !ZNet.instance.IsServer())
				{
					return true;
				}
				int start = pkg.GetPos();
				try
				{
					pkg.ReadLong();
					pkg.ReadLong();
					pkg.ReadLong();
					ZDOID target = pkg.ReadZDOID();
					int method = pkg.ReadInt();
					if (method != s_rpcDamage && method != s_rpcHit && method != s_rpcApplyOperation)
					{
						return true;
					}
					ZNetPeer peer = ZNet.instance.GetPeer(rpc);
					if (peer == null)
					{
						return true;
					}
					ZPackage parameters = pkg.ReadPackage();
					return Dealing(peer, method, target, parameters);
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
				finally
				{
					pkg.SetPos(start);
				}
			}
		}

		// False = drop the RPC.
		private static bool Dealing(ZNetPeer peer, int method, ZDOID target, ZPackage parameters)
		{
			ZDO character = Fighting(peer);
			if (character == null)
			{
				return true;
			}
			if (method == s_rpcApplyOperation)
			{
				if (ModeOf(Rule.Skills) != Mode.Off)
				{
					Dug(peer, character);
				}
				return true;
			}
			HitData hit = new HitData();
			hit.Deserialize(ref parameters);
			// Only a player's own hit: what is sent in the name of a creature, or of another player, is not about this player's game.
			if (hit.m_attacker.IsNone() || hit.m_attacker != peer.m_characterID)
			{
				return true;
			}
			float now = Time.time;
			bool combat = CombatOn();
			Fighter f = combat ? FighterOf(peer) : null;
			ZDO targetZdo = target.IsNone() ? null : ZDOMan.instance.GetZDO(target);
			string targetName = "?";
			if (targetZdo != null)
			{
				GameObject prefab = ZNetScene.instance.GetPrefab(targetZdo.GetPrefab());
				targetName = prefab ? prefab.name : targetZdo.GetPrefab().ToString();
			}
			Vector3 pos = targetZdo != null ? targetZdo.GetPosition() : character.GetPosition();
			float total = hit.GetTotalDamage();
			float stamina = character.GetFloat(ZDOVars.s_stamina, -1f);
			if (combat)
			{
				Remember(f.dealt, new Dealt { at = now, target = targetName, skill = hit.m_skill, level = hit.m_skillLevel, itemLevel = hit.m_itemLevel, damage = total, stamina = stamina });
			}
			bool pass = true;
			Mode damage = ModeOf(Rule.Damage);
			if (damage != Mode.Off)
			{
				CombatBounds();
				float maxDamage = Configuration.antiCheatMaxDamage.Value > 0f ? Configuration.antiCheatMaxDamage.Value : s_maxHit;
				string basis = Configuration.antiCheatMaxDamage.Value > 0f ? "MaxDamage" : "automatic";
				// A tamed animal hit with a weapon that only works on tamed animals (the butcher knife): that weapon's own bound, always the automatic one.
				if (s_maxHitTamedOnly > 0f && targetZdo != null && targetZdo.GetBool(ZDOVars.s_tamed, false))
				{
					ItemDrop.ItemData.SharedData held = WeaponIn(character, out int heldQuality, out string heldName);
					if (held != null && held.m_tamedOnly)
					{
						maxDamage = s_maxHitTamedOnly;
						basis = "automatic, " + heldName + " on a tamed animal";
					}
				}
				if (maxDamage > 0f && (float.IsNaN(total) || total > maxDamage))
				{
					Finding(Rule.Damage, peer, targetName, $"a hit of {total:0} damage (the most allowed is {maxDamage:0}, {basis}), {hit.m_skill}, on {targetName}", pos);
					pass = damage != Mode.On;
				}
			}
			if (IsMelee(hit.m_skill) && !hit.m_ranged)
			{
				if (combat && ModeOf(Rule.Stamina) != Mode.Off)
				{
					Swung(peer, character, f, now, stamina);
				}
				if (ModeOf(Rule.Items) != Mode.Off)
				{
					ItemDrop.ItemData.SharedData weapon = WeaponIn(character, out int held, out string weaponName);
					if (weapon != null && held > 0 && hit.m_itemLevel > held)
					{
						Finding(Rule.Items, peer, weaponName, $"a hit from a weapon of quality {hit.m_itemLevel}, the weapon in hand is quality {held}", pos);
					}
				}
			}
			if (ModeOf(Rule.Skills) != Mode.Off && IsTracked(hit.m_skill))
			{
				Learned(peer, character, f, hit, pos);
			}
			return pass;
		}

		// ---- Stamina ------------------------------------------------------------------------------------------------------------

		private static void Swung(ZNetPeer peer, ZDO character, Fighter f, float now, float stamina)
		{
			if (stamina < 0f || now - f.lastAttack < SameAttackSeconds)
			{
				return;
			}
			ItemDrop.ItemData.SharedData weapon = WeaponIn(character, out int quality, out string name);
			if (weapon == null || weapon.m_attack == null)
			{
				return;
			}
			float cost = weapon.m_attack.m_attackStamina;
			if (weapon.m_secondaryAttack != null && !string.IsNullOrEmpty(weapon.m_secondaryAttack.m_attackAnimation))
			{
				cost = Mathf.Min(cost, weapon.m_secondaryAttack.m_attackStamina);
			}
			if (cost < 4f)
			{
				return;
			}
			f.lastAttack = now;
			f.attacks.Add(new Beat { at = now, stamina = stamina, revision = character.DataRevision });
			f.attacks.RemoveAll(a => now - a.at > StaminaWindowSeconds);
			int needed = Configuration.antiCheatStaminaAttacks.Value;
			if (f.attacks.Count < needed)
			{
				return;
			}
			float min = f.attacks.Min(a => a.stamina), maxStamina = f.attacks.Max(a => a.stamina);
			uint first = f.attacks.Min(a => a.revision), last = f.attacks.Max(a => a.revision);
			if (maxStamina - min < 1f && min > 0f && last - first >= 10)
			{
				// The hit samples alone are not enough: a player whose stamina is back at the top at every attack gives constant hit samples too. The game waits
				// a second after an attack before it regenerates, so an honest character shows (top - cost) in the half-second samples taken in between.
				float since = f.attacks[0].at;
				List<Beat> between = f.ticks.Where(t => t.at >= since).ToList();
				if (between.Count < MinTickSamples)
				{
					return;
				}
				float tickMin = between.Min(t => t.stamina), tickMax = between.Max(t => t.stamina);
				if (tickMax - tickMin >= 1f || tickMin <= 0f)
				{
					return;
				}
				Finding(Rule.Stamina, peer, name, $"{f.attacks.Count} attacks in {now - f.attacks[0].at:0.#} s with a weapon that costs {cost:0.#}, stamina stayed at {maxStamina:0.#} at the hits and {tickMin:0.#}..{tickMax:0.#} in {between.Count} samples between them", character.GetPosition());
				f.attacks.Clear();
				f.ticks.Clear();
			}
		}

		// Every half second, for each fighter who has attacked within the window: the stamina its character shows now (what Swung compares with the hit samples).
		private static void SampleStamina()
		{
			float now = Time.time;
			foreach (Fighter f in s_fighters.Values)
			{
				f.ticks.RemoveAll(t => now - t.at > StaminaWindowSeconds);
				if (f.attacks.Count == 0 || now - f.attacks[f.attacks.Count - 1].at > StaminaWindowSeconds)
				{
					continue;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(f.user);
				ZDO character = peer != null ? Fighting(peer) : null;
				float stamina = character != null ? character.GetFloat(ZDOVars.s_stamina, -1f) : -1f;
				if (stamina >= 0f)
				{
					f.ticks.Add(new Beat { at = now, stamina = stamina, revision = character.DataRevision });
				}
			}
		}

		// ---- Skills ----------------------------------------------------------------------------------------------------------------

		private class SkillAccount
		{
			public long id;
			public string name = "";
			public bool exact;
			public DateTime since;
			public readonly Dictionary<int, float> first = new Dictionary<int, float>();
			public readonly Dictionary<int, double> xp = new Dictionary<int, double>();
		}

		private static readonly Dictionary<long, SkillAccount> s_skillAccounts = new Dictionary<long, SkillAccount>();
		private static string s_skillsPath;
		private static DateTime s_skillsStarted;
		private static bool s_skillsLoaded;
		private static bool s_skillsFailed;
		private static bool s_skillsDirty;
		private static float s_skillsNextSave;
		private const float OpenWaitSeconds = 15f;
		private const float SkillsSaveSeconds = 300f;

		// The account of the character this peer plays, opened when it is first seen: exact (every skill starts at 0) when the character guard says it is new
		// and fresh (and the ledger's grace is over), else unverified. The raid list that decides it comes from the player's game a moment after it joins, so
		// an account that is not exact yet is tried again until OpenWaitSeconds after the player was first seen.
		private static SkillAccount AccountOf(ZNetPeer peer, ZDO character, Fighter f)
		{
			long id = character.GetLong(ZDOVars.s_playerID, 0L);
			if (id == 0L || s_skillsFailed)
			{
				return null;
			}
			if (!s_skillsLoaded)
			{
				LoadSkills();
				if (s_skillsFailed)
				{
					return null;
				}
			}
			if (s_skillAccounts.TryGetValue(id, out SkillAccount account))
			{
				account.name = peer.m_playerName;
				return account;
			}
			CharacterGuard.EnsureFootprint();
			bool fresh = (DateTime.UtcNow - s_skillsStarted).TotalHours >= Configuration.itemLedgerGraceHours.Value && CharacterGuard.IsBrandNew(peer, character, id);
			if (!fresh && Time.time - f.seen < OpenWaitSeconds)
			{
				return null;
			}
			CombatBounds();
			account = new SkillAccount { id = id, name = peer.m_playerName, exact = fresh, since = DateTime.UtcNow };
			if (fresh)
			{
				foreach (Skills.SkillType skill in TrackedSkills)
				{
					account.first[(int)skill] = 0f;
					account.xp[(int)skill] = 0.0;
				}
			}
			s_skillAccounts[id] = account;
			s_skillsDirty = true;
			GuardLog.Write($"{peer.m_playerName} ({id}): skills account opened, " + (fresh ? "exact from zero (a new, fresh character)" : "what it could do when counting began is unknown; judged on its growth from its first levels seen"));
			return account;
		}

		private static void Learned(ZNetPeer peer, ZDO character, Fighter f, HitData hit, Vector3 pos)
		{
			SkillAccount account = AccountOf(peer, character, f);
			if (account == null)
			{
				return;
			}
			int key = (int)hit.m_skill;
			float level = float.IsNaN(hit.m_skillLevel) ? 0f : Mathf.Clamp(hit.m_skillLevel, 0f, 1000f);
			if (!account.first.ContainsKey(key))
			{
				// First seen, not from zero: the most it could have had then is what the hit says.
				account.first[key] = account.exact ? 0f : level;
				account.xp[key] = 0.0;
			}
			CombatBounds();
			float step = s_skillStep.TryGetValue(key, out float s) ? s : 1f;
			account.xp[key] += Mathf.Clamp(hit.m_skillRaiseAmount, 0f, 100f) * 1.5f * step * Skills_GainRate();
			s_skillsDirty = true;
			float least = Mathf.Max(0f, level - (s_skillBonus.TryGetValue(key, out float bonus) ? bonus : 0f));
			int claimed = Mathf.Clamp(Mathf.FloorToInt(least), 0, 100);
			int from = Mathf.Clamp(Mathf.FloorToInt(account.first[key]), 0, 100);
			float needed = s_levelCost[claimed] - s_levelCost[from];
			float allowed = Configuration.antiCheatSkillGainFactor.Value * (float)account.xp[key] + Configuration.antiCheatSkillAllowance.Value;
			if (needed <= allowed)
			{
				return;
			}
			if (f.skillFound.TryGetValue(key, out float last) && Time.time - last < 600f)
			{
				return;
			}
			f.skillFound[key] = Time.time;
			Finding(Rule.Skills, peer, "Player", $"{hit.m_skill} level >= {claimed} (sent {hit.m_skillLevel:0}), it has earned at most {account.xp[key]:0} experience of the {needed:0} needed since {account.since:yyyy-MM-dd}"
				+ (account.exact ? " (exact from zero)" : $" (first seen at {account.first[key]:0})"), pos);
		}

		private static float Skills_GainRate()
		{
			return Mathf.Max(0f, Game.m_skillGainRate);
		}

		// A terrain operation from the player's game (digging, levelling, paving) raises the pickaxe skill.
		private static void Dug(ZNetPeer peer, ZDO character)
		{
			Fighter f = FighterOf(peer);
			SkillAccount account = AccountOf(peer, character, f);
			if (account == null)
			{
				return;
			}
			int key = (int)Skills.SkillType.Pickaxes;
			if (!account.first.ContainsKey(key))
			{
				return;
			}
			CombatBounds();
			account.xp[key] += 1.5 * (s_skillStep.TryGetValue(key, out float step) ? step : 1f) * Skills_GainRate();
			s_skillsDirty = true;
		}

		/*
			<world>.skills.txt next to the world save. A "started" line, then one line per character, tab separated: id, exact or unknown, counting since,
			name, skill=first:xp,... (the first level seen and the experience counted since).
		*/
		private static void LoadSkills()
		{
			s_skillsLoaded = true;
			s_skillsStarted = DateTime.UtcNow;
			try
			{
				World world = ZNet.World;
				s_skillsPath = Path.Combine(SaveSystem.GetWorldsSaveRootPath(world.m_fileSource), world.m_name + ".skills.txt");
				string read = File.Exists(s_skillsPath) ? s_skillsPath : File.Exists(s_skillsPath + ".tmp") ? s_skillsPath + ".tmp" : null;
				if (read == null)
				{
					s_skillsDirty = true;
					return;
				}
				foreach (string line in File.ReadAllLines(read))
				{
					string[] fields = line.Split('\t');
					if (fields[0] == "started" && fields.Length > 1)
					{
						s_skillsStarted = ParseTime(fields[1]);
						continue;
					}
					if (line.StartsWith("#", StringComparison.Ordinal) || fields.Length < 5 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
					{
						continue;
					}
					SkillAccount account = new SkillAccount { id = id, exact = fields[1] == "exact", since = ParseTime(fields[2]), name = fields[3] };
					foreach (string pair in fields[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
					{
						string[] kv = pair.Split('=');
						string[] values = kv.Length == 2 ? kv[1].Split(':') : new string[0];
						if (values.Length == 2 && Enum.IsDefined(typeof(Skills.SkillType), kv[0]) && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float first)
							&& double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double xp))
						{
							int key = (int)Enum.Parse(typeof(Skills.SkillType), kv[0]);
							account.first[key] = first;
							account.xp[key] = xp;
						}
					}
					s_skillAccounts[id] = account;
				}
			}
			catch (Exception e)
			{
				s_skillsFailed = true;
				ServersidePlugin.logger.LogError($"Anti-cheat: could not read the skills ledger, so the Skills rule stays off until the server restarts: {e}");
				GuardLog.Write($"AntiCheat: could not read the skills ledger ({e.GetType().Name}: {e.Message}), so the Skills rule stays off until the server restarts");
			}
		}

		private static string FormatTime(DateTime t)
		{
			return t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		}

		private static DateTime ParseTime(string s)
		{
			return DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime t) ? t : DateTime.UtcNow;
		}

		// Written to a temp file and moved over the old one, as the item ledger does.
		public static void SaveSkills()
		{
			if (!s_skillsLoaded || s_skillsFailed || s_skillsPath == null || !s_skillsDirty)
			{
				return;
			}
			s_skillsDirty = false;
			s_skillsNextSave = Time.time + SkillsSaveSeconds;
			try
			{
				List<string> lines = new List<string>
				{
					"# Sarkastic.gg Dedicated Simulation: skills ledger -- what each character's weapon skills could have grown to on this world. Tab separated:",
					"# id, exact/unknown (unknown = its levels when counting began are not known), counting since (UTC), name, skill=first level seen:experience counted since,...",
					"started\t" + FormatTime(s_skillsStarted),
				};
				foreach (SkillAccount a in s_skillAccounts.Values.OrderBy(a => a.since))
				{
					lines.Add(string.Join("\t", new[]
					{
						a.id.ToString(CultureInfo.InvariantCulture), a.exact ? "exact" : "unknown", FormatTime(a.since), (a.name ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '),
						string.Join(",", a.first.OrderBy(kv => kv.Key).Select(kv => $"{(Skills.SkillType)kv.Key}={kv.Value.ToString("0.##", CultureInfo.InvariantCulture)}:{(a.xp.TryGetValue(kv.Key, out double xp) ? xp : 0.0).ToString("0.###", CultureInfo.InvariantCulture)}")),
					}));
				}
				string temp = s_skillsPath + ".tmp";
				File.WriteAllLines(temp, lines);
				if (File.Exists(s_skillsPath))
				{
					File.Delete(s_skillsPath);
				}
				File.Move(temp, s_skillsPath);
			}
			catch (Exception e)
			{
				s_skillsDirty = true;
				ServersidePlugin.logger.LogWarning($"Anti-cheat: could not save {s_skillsPath}: {e.Message}");
			}
		}

		// The skills ledger is saved with the world (so that after a crash both come back from the same moment) and when a player leaves.
		[HarmonyPatch(typeof(ZNet), "SaveWorld")]
		public static class ZNet_SaveWorld_Patch
		{
			static void Postfix()
			{
				if (ModeOf(Rule.Skills) != Mode.Off && Configuration.antiCheatEnabled.Value)
				{
					SaveSkills();
				}
			}
		}

		[HarmonyPatch(typeof(ZNet), "Disconnect")]
		public static class ZNet_Disconnect_Patch
		{
			static void Prefix()
			{
				if (s_skillsLoaded && s_skillsDirty && Configuration.antiCheatEnabled.Value)
				{
					SaveSkills();
				}
			}
		}

		// ---- the combat tick: every half second the checks that wait for fresh data; every two seconds the accounts ------------------------

		private static void CombatTick()
		{
			if (ModeOf(Rule.Unhurt) != Mode.Off)
			{
				CombatBounds();
				JudgeTaken();
			}
			if (ModeOf(Rule.Stamina) != Mode.Off)
			{
				SampleStamina();
			}
			if (Time.time < s_nextCombatPlayers)
			{
				return;
			}
			s_nextCombatPlayers = Time.time + 2f;
			if (ModeOf(Rule.Skills) != Mode.Off)
			{
				foreach (ZNetPeer peer in ZNet.instance.GetPeers().ToList())
				{
					ZDO character = Fighting(peer);
					if (character != null)
					{
						AccountOf(peer, character, FighterOf(peer));
					}
				}
				if (s_skillsDirty && Time.time >= s_skillsNextSave)
				{
					SaveSkills();
				}
			}
			foreach (long user in s_fighters.Keys.Where(u => ZNet.instance.GetPeer(u) == null).ToList())
			{
				s_fighters.Remove(user);
			}
		}

		// ---- the stored world ---------------------------------------------------------------------------------------------

		private class Found
		{
			public ZDOID id;
			public Rule rule;
			public string prefab;
			public Vector3 pos;
			public long creator;
			public string detail;
			public bool reportOnly;
		}

		// What is in the world that the game's tools cannot make. The rules that need a player (distance, creator, ward, cultivated ground,
		// no-build places) cannot be judged any more; the others can. Only what no tool of the game makes (NotBuildable) and the dropped items
		// above their maximum are ever taken away by `revert`; the rest (biome, dungeon, the contents of chests) is only reported: the world may
		// have been built under a rule that was different then (a world modifier, an admin).
		private static List<Found> Sweep(Dictionary<long, int> plantRates, Dictionary<long, int> plants)
		{
			List<Found> found = new List<Found>();
			Dictionary<long, List<long>> times = new Dictionary<long, List<long>>();
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
			{
				int hash = zdo.GetPrefab();
				if (hash == 0)
				{
					continue;
				}
				long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
				if (creator != 0L)
				{
					Info info = InfoOf(hash);
					Vector3 pos = zdo.GetPosition();
					if (info == null)
					{
						found.Add(new Found { id = zdo.m_uid, rule = Rule.UnknownPrefab, prefab = "#" + hash, pos = pos, creator = creator, detail = "the game has no such object" });
						continue;
					}
					if (!info.buildable)
					{
						found.Add(new Found { id = zdo.m_uid, rule = Rule.NotBuildable, prefab = info.name, pos = pos, creator = creator, detail = info.piece ? "in no build table" : "not a piece, yet it has a creator" });
						continue;
					}
					if (info.container)
					{
						ChestInfo(zdo, info, creator, found);
					}
					if (info.onlyInBiome != Heightmap.Biome.None)
					{
						// Where the ground is not loaded (most of the world) the world generator answers; near its borders it can differ from the ground.
						Heightmap.Biome here = Heightmap.FindBiome(pos);
						if (here == Heightmap.Biome.None)
						{
							here = WorldGenerator.instance.GetBiome(pos.x, pos.z);
						}
						if ((here & info.onlyInBiome) == 0)
						{
							found.Add(new Found { id = zdo.m_uid, rule = Rule.Biome, prefab = info.name, pos = pos, creator = creator, reportOnly = true, detail = $"belongs to {info.onlyInBiome}, this is {here}" });
						}
					}
					if (!info.allowedInDungeons && Character.InInterior(pos) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DungeonBuild))
					{
						found.Add(new Found { id = zdo.m_uid, rule = Rule.Dungeon, prefab = info.name, pos = pos, creator = creator, reportOnly = true, detail = "may not be built in a dungeon" });
					}
					if (info.plant)
					{
						long planted = zdo.GetLong(ZDOVars.s_plantTime, 0L);
						if (planted != 0L)
						{
							if (!times.TryGetValue(creator, out List<long> list))
							{
								times[creator] = list = new List<long>();
							}
							list.Add(planted);
						}
					}
					continue;
				}
				ItemInfo(zdo, hash, found);
			}
			long window = (long)(Mathf.Max(0.01f, Configuration.antiCheatMinInterval.Value) * TimeSpan.TicksPerSecond);
			int need = Mathf.Max(2, Configuration.antiCheatRatePlants.Value);
			foreach (KeyValuePair<long, List<long>> kv in times)
			{
				kv.Value.Sort();
				bool[] member = new bool[kv.Value.Count];
				for (int i = 0; i + need <= kv.Value.Count; i++)
				{
					if (kv.Value[i + need - 1] - kv.Value[i] <= window)
					{
						for (int k = i; k < i + need; k++)
						{
							member[k] = true;
						}
					}
				}
				plants[kv.Key] = kv.Value.Count;
				plantRates[kv.Key] = member.Count(m => m);
			}
			return found;
		}

		private static void ItemInfo(ZDO zdo, int hash, List<Found> found)
		{
			Info info = InfoOf(hash);
			if (info == null || !info.item)
			{
				return;
			}
			ReadDrop(zdo, out int stack, out int quality, out float durability, out bool cheated);
			string wrong = TooMuch(info, stack, quality, durability);
			if (wrong != null)
			{
				found.Add(new Found { id = zdo.m_uid, rule = Rule.Items, prefab = info.name, pos = zdo.GetPosition(), creator = 0L, detail = wrong });
			}
		}

		// What a chest holds that no item can be: read the way ItemLedger reads it (the 1.0 format, item by item). Only reported, never taken away:
		// the chest is someone's, and who put the stack in is not on the chest.
		private static void ChestInfo(ZDO zdo, Info info, long creator, List<Found> found)
		{
			byte[] data = zdo.GetByteArray(ZDOVars.s_items);
			if (data == null || data.Length < 8)
			{
				return;
			}
			try
			{
				ZPackage package = new ZPackage(data);
				global::Version.Item version = (global::Version.Item)package.ReadInt();
				if (version < global::Version.Item.Smaller)
				{
					return;
				}
				int count = package.ReadUShort();
				for (int i = 0; i < count; i++)
				{
					(int hash, ItemDrop.ItemData item) = ItemDrop.ItemData.Load(package, version);
					Info content = InfoOf(hash);
					if (content == null || !content.item)
					{
						continue;
					}
					string wrong = TooMuch(content, item.m_stack, item.m_quality, item.m_durability);
					if (wrong != null)
					{
						found.Add(new Found { id = zdo.m_uid, rule = Rule.Items, prefab = info.name, pos = zdo.GetPosition(), creator = creator, reportOnly = true, detail = $"holds {content.name}: {wrong}" });
						return;
					}
				}
			}
			catch (Exception)
			{
				// not readable
			}
		}

		// Whether `anticheat revert` takes a finding away, and if not why. Only what no tool of the game makes (NotBuildable) and dropped items above
		// their maximum, when their rule is On; never an admin's, and never one whose maker the character guard does not know unless `all` is said (the
		// creator written on a piece is what the player's game wrote, and without a record there is no telling an admin's from a stranger's).
		private static bool Takes(Found f, bool all, out string why)
		{
			why = null;
			if (f.reportOnly || ModeOf(f.rule) != Mode.On || !Revertible(f.rule) || f.rule == Rule.UnknownPrefab)
			{
				why = "only reported";
				return false;
			}
			if (f.creator != 0L)
			{
				string account = CharacterGuard.AccountOf(f.creator);
				if (IsAdminAccount(account))
				{
					why = "an admin's";
					return false;
				}
				if (account == null && !all)
				{
					why = "creator not known to the character guard, `anticheat revert all` takes it";
					return false;
				}
			}
			return true;
		}

		public static string Console(string[] words)
		{
			if (!Configuration.antiCheatEnabled.Value)
			{
				return "the anti-cheat is off ([AntiCheat] Enabled)";
			}
			string sub = words.Length > 1 ? words[1].ToLowerInvariant() : "status";
			if (sub == "status")
			{
				StringBuilder text = new StringBuilder();
				text.Append($"anti-cheat: action {Configuration.antiCheatAction.Value}, admins {(Configuration.antiCheatExemptAdmins.Value ? "exempt" : "held to it as well")}{(s_fed ? "" : "; NOT RUNNING: no player's game has sent object data since the start (or [AntiCheat] Enabled was off at the start and the patches were not applied)")}; ");
				text.Append($"{s_judged} objects of players' games judged, {s_born.Count} waiting, {s_reverted} taken away, {s_kicks} kick(s), {s_unattributed} from a game that had left, {s_blank} that never got their data, {s_dropped} not looked at (queue full)\n");
				if (PaidPlanting())
				{
					text.Append($"paid planting: {s_groups} group(s) of plants since the start, {s_paidPlants} plant(s) in them let through as paid for\n");
				}
				foreach (Rule rule in Enum.GetValues(typeof(Rule)))
				{
					text.Append($"  {s_names[(int)rule],-16} {ModeOf(rule),-8} {s_hits[(int)rule],5} finding(s)  {s_labels[(int)rule]}\n");
				}
				text.Append("`anticheat scan` looks through the stored world, `anticheat revert` takes away what it finds that no tool of the game makes");
				return text.ToString();
			}
			if (sub != "scan" && sub != "revert")
			{
				return "usage: anticheat [status | scan | revert [all]]";
			}
			bool all = words.Length > 2 && words[2].ToLowerInvariant() == "all";
			Buildable();
			Dictionary<long, int> quick = new Dictionary<long, int>();
			Dictionary<long, int> plants = new Dictionary<long, int>();
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			List<Found> found = Sweep(quick, plants);
			StringBuilder report = new StringBuilder();
			report.Append($"anti-cheat scan of {ZDOMan.instance.m_objectsByID.Count} objects in {watch.ElapsedMilliseconds} ms: {found.Count} finding(s)\n");
			int reverted = 0;
			int left = 0;
			foreach (IGrouping<Rule, Found> group in found.GroupBy(f => f.rule))
			{
				report.Append($"  {s_names[(int)group.Key]}: {group.Count()} ({s_labels[(int)group.Key]}), mode {ModeOf(group.Key)}\n");
				foreach (Found f in group.Take(30))
				{
					bool takes = Takes(f, all, out string why);
					report.Append($"    {f.prefab} at {At(f.pos)} by {(f.creator != 0L ? NameOfCharacter(f.creator) : "nobody")}: {f.detail}{(takes ? (sub == "revert" ? " -> taken away" : " (revert takes it away)") : " [" + why + "]")}\n");
				}
				if (group.Count() > 30)
				{
					report.Append($"    ... and {group.Count() - 30} more\n");
				}
			}
			if (sub == "revert")
			{
				foreach (Found f in found)
				{
					if (!Takes(f, all, out string why))
					{
						left++;
						continue;
					}
					ZDO zdo = ZDOMan.instance.GetZDO(f.id);
					if (zdo != null)
					{
						try
						{
							Revert(zdo);
							reverted++;
							GuardLog.Write($"AntiCheat: scan: {f.prefab} at {At(f.pos)} by {NameOfCharacter(f.creator)}: {s_labels[(int)f.rule]}: {f.detail}; taken away");
						}
						catch (Exception e)
						{
							Fault(e);
							left++;
						}
					}
				}
				report.Append($"taken away: {reverted}; left as they are: {left} (only reported, an admin's, a maker the character guard does not know, a chest's contents, an object the game does not have)\n");
			}
			int quickPlayers = 0;
			foreach (KeyValuePair<long, int> kv in quick.OrderByDescending(k => k.Value))
			{
				if (kv.Value == 0)
				{
					continue;
				}
				if (quickPlayers++ == 0)
				{
					report.Append($"plants put down {Mathf.Max(2, Configuration.antiCheatRatePlants.Value)} or more within {Configuration.antiCheatMinInterval.Value:0.##} s by the same player (the game plants one in 0.4 s; they are crops of the game, not taken away):\n");
				}
				report.Append($"    {NameOfCharacter(kv.Key)}: {kv.Value} of {plants[kv.Key]} plants\n");
			}
			return report.ToString().TrimEnd('\n');
		}
	}
}
