using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		An account, per character, of the items that lock progress: what it got on this server and what it
		put back into the world. A vanilla client never shows the server its bag, but nearly every way into
		and out of a bag passes through an object the server sees.

		In:
		- picking an item up: the player takes the item over and deletes it (DestroyZDO from that player);
		- taking it out of a chest, cart, ship or tombstone: the player who has the container open sends its
		  new contents.
		Out:
		- dropping it: a new item from that player that has been in a bag before. Player.OnInventoryChanged
		  marks everything in a bag as picked up, and an item that falls out of a destroyed piece, a creature
		  or a rock does not carry the mark;
		- putting it into a container, on an item stand or an armor stand;
		- feeding it to a smelter, kiln, refinery, cooking station or fermenter, or offering it at a boss
		  altar -- those calls reach the server, which owns these objects near players;
		- building with it (a new piece the player created) and dying with it (a tombstone is the whole bag).
		Seen as it is used, not as it moves:
		- what a character wears and holds, checked every few seconds; an upgrade shows as a higher quality.
		Not seen: crafting, upgrading, eating, buying from a trader, what a caught fish brings along. A
		tracked item a character has not got here counts as crafted by it if its recipe can be paid from the
		tracked items it did get (the untracked ingredients are assumed at hand), and a craft counts as
		yielding the most it can: the crafting bonus at its luckiest, the best ingredient of a one-of recipe.
		An upgrade may have been made at Valheim 1.0's upgrade station, which takes only upgrader items, and
		an item held may have broken there and handed part of its materials back. A caught fish counts in
		the most its extra drops can bring. What traders sell is not tracked. An item the game removes
		after an hour with nobody near is no pickup.

		An item that falls out of a container, item stand or armor stand its owner destroyed or emptied has
		been in a bag, but not in this player's: drops from the same player shortly after such an event are
		matched against it first. New drops wait a moment for that.

		When a character puts more of an item into the world than it ever got here, nor could make, the rest
		came from another world. That is written to the guard log; with Mode = On it is also taken away --
		a dropped stack is cut down, a container or stand loses it once nobody is using it, a station ignores
		it, a boss altar refuses to summon with it -- and the player is told. What was built with it or is
		worn is only logged. What is unexplained is never counted as got.

		The account is exact only when what the character carried at the start is known: a new, fresh
		character that starts at zero (see CharacterGuard), or any character after its first death here,
		when the tombstone shows the whole bag. Until then its findings are logged as unverified and never
		acted on. For the first GraceHours after the ledger is switched on, no character counts as new: the
		players who are already around come back with what they had.
	*/
	public class ItemLedger : IFeature
	{
		public enum Mode { Off, LogOnly, On }

		public bool FeatureEnabled()
		{
			return Configuration.itemLedgerMode.Value != Mode.Off;
		}

		private class Account
		{
			public long id;
			public string name = "";
			public bool exact;
			public DateTime since;
			public readonly Dictionary<string, int> balance = new Dictionary<string, int>(StringComparer.Ordinal);
			// Highest quality the character was seen with, per item: anything above it was upgraded unseen.
			public readonly Dictionary<string, int> quality = new Dictionary<string, int>(StringComparer.Ordinal);
			// What it wears and holds now (not saved): that cannot have broken at an upgrade station.
			public HashSet<string> wornNow = new HashSet<string>(StringComparer.Ordinal);
		}

		private class Ingredient
		{
			public string item;
			public bool tracked;
			// Taken by Valheim 1.0's upgrade station, and only there (Piece.Requirement.m_upgraderResource).
			public bool upgrader;
			public Piece.Requirement requirement;
		}

		private class RecipeInfo
		{
			public int amount;
			// The most one craft can yield: the crafting bonus adds to a stack made at a station (see Setup).
			public int most;
			public bool anyOne;
			public int maxQuality;
			// Upgraded at the upgrade station as well, which takes only the upgrader items and goes past the top
			// quality; when it fails it breaks the item and hands this share of its materials back.
			public bool upgradeStation;
			public float breakReturns;
			public List<Ingredient> ingredients;
		}

		// What a caught fish can bring along straight into the bag (FishingFloat.Catch: one roll of its m_extraDrops).
		private struct FishExtra
		{
			public string item;
			public int most;
			public int rolls;
			public bool scales;
			public int maxStack;
		}

		private class PendingDrop
		{
			public ZDOID uid;
			public long peer;
			public string item;
			public int stack;
			public int quality;
			public float since;
		}

		private class Expected
		{
			public string item;
			public int count;
			public float until;
		}

		private enum HolderKind { Container, ItemStand, ArmorStand }

		private class Confiscation
		{
			public ZDOID holder;
			public HolderKind kind;
			public string item;
			public int amount;
			public int slot;
			public long peer;
			public float since;
		}

		// What a holder (container or stand) looked like before a player's update replaced it.
		private class Snapshot
		{
			public HolderKind kind;
			public byte[] items;
			public int[] slots;
		}

		private struct Held
		{
			public string item;
			public int quality;
			public int count;
			public bool pickedUp;
		}

		private static readonly object NewObject = new object();
		private const float DropWait = 2.5f;
		private const float ExpectSeconds = 15f;
		private const float WornInterval = 10f;
		private const int MakeDepth = 3;

		private static bool s_ready;
		private static string s_path;
		private static bool s_dirty;
		private static float s_saveAt;
		private static DateTime s_started;
		private static long s_sender;
		private static float s_nextWorn;
		private static readonly Dictionary<long, Account> s_accounts = new Dictionary<long, Account>();
		private static readonly Dictionary<int, string> s_tracked = new Dictionary<int, string>();
		private static readonly Dictionary<string, string> s_why = new Dictionary<string, string>(StringComparer.Ordinal);
		private static readonly Dictionary<string, string> s_sharedNames = new Dictionary<string, string>(StringComparer.Ordinal);
		private static readonly Dictionary<int, Vector2i> s_containers = new Dictionary<int, Vector2i>();
		private static readonly HashSet<int> s_dumpingContainers = new HashSet<int>();
		private static readonly HashSet<int> s_itemStands = new HashSet<int>();
		private static readonly Dictionary<int, int> s_armorStands = new Dictionary<int, int>();
		private static readonly Dictionary<int, List<KeyValuePair<string, int>>> s_pieceCosts = new Dictionary<int, List<KeyValuePair<string, int>>>();
		// Smallest batch first: bronze has a recipe for one bar and one for five.
		private static readonly Dictionary<string, List<RecipeInfo>> s_recipes = new Dictionary<string, List<RecipeInfo>>(StringComparer.Ordinal);
		private static readonly Dictionary<ZDOID, PendingDrop> s_pendingDrops = new Dictionary<ZDOID, PendingDrop>();
		private static readonly Dictionary<long, List<Expected>> s_expected = new Dictionary<long, List<Expected>>();
		private static readonly List<Confiscation> s_confiscations = new List<Confiscation>();
		private static readonly HashSet<string> s_wornSeen = new HashSet<string>(StringComparer.Ordinal);
		// Fish a player takes straight into the bag (Fish.m_pickupItem, no item of its own): prefab -> item, stack.
		private static readonly Dictionary<int, KeyValuePair<string, int>> s_fishPickups = new Dictionary<int, KeyValuePair<string, int>>();
		private static readonly Dictionary<int, List<FishExtra>> s_fishExtras = new Dictionary<int, List<FishExtra>>();
		// Material -> the items an upgrade station hands part of it back from when it breaks them.
		private static readonly Dictionary<string, List<string>> s_salvage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		private static int s_tombstone;
		private static int s_warnings;
		private static bool s_quiet;

		// The item stand keeps its item in "item"/"itemData", the armor stand slot i in "<i>_item"/"<i>_itemData".
		private static readonly int[] SlotItem = Enumerable.Range(0, 16).Select(i => (i + "_item").GetStableHashCode()).ToArray();
		private static readonly int[] SlotData = Enumerable.Range(0, 16).Select(i => (i + "_itemData").GetStableHashCode()).ToArray();

		private static readonly int[] Worn =
		{
			ZDOVars.s_rightItem, ZDOVars.s_leftItem, ZDOVars.s_rightBackItem, ZDOVars.s_leftBackItem,
			ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_helmetItem, ZDOVars.s_shoulderItem,
			ZDOVars.s_utilityItem, ZDOVars.s_trinketItem,
		};
		private static readonly Dictionary<int, int> WornQuality = new Dictionary<int, int>
		{
			{ ZDOVars.s_rightItem, ZDOVars.s_rightItemQuality }, { ZDOVars.s_leftItem, ZDOVars.s_leftItemQuality },
			{ ZDOVars.s_rightBackItem, ZDOVars.s_rightBackItemQuality }, { ZDOVars.s_leftBackItem, ZDOVars.s_leftBackItemQuality },
		};

		/*
			What summons a boss, from the altars (OfferingBowl) in the game's locations and dungeon rooms,
			listed from Valheim 1.0.16: Eikthyr, the Elder, Bonemass, Yagluth, the Queen, Fader, the Deep North's
			king and its memorial. Moder takes her eggs on item stands, which are counted as such. The altars live
			in location prefabs, which are not loaded at startup, so the list is kept here; names the game does
			not know are left out.
		*/
		private static readonly string[] BossSummonItems =
		{
			"TrophyDeer", "AncientSeed", "WitheredBone", "GoblinTotem", "TrophySeekerBrute", "Bell", "HatefulBlood", "MemorialCoal",
		};

		private static bool Enforcing => Configuration.itemLedgerMode.Value == Mode.On;

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (Configuration.itemLedgerMode.Value == Mode.Off || !ZNet.instance || !ZNet.instance.IsServer() || ZNet.World == null
				|| ZDOMan.instance == null || !ObjectDB.instance || !ZNetScene.instance || !ZoneSystem.instance)
			{
				return;
			}
			if (!s_ready)
			{
				Setup();
			}
			CharacterGuard.EnsureFootprint();
			float now = Time.time;
			foreach (PendingDrop drop in s_pendingDrops.Values.Where(d => now - d.since >= DropWait).ToList())
			{
				Guard(() => ProcessDrop(drop, ZDOMan.instance.GetZDO(drop.uid), beingPickedUp: false));
			}
			for (int i = s_confiscations.Count - 1; i >= 0; i--)
			{
				Confiscation c = s_confiscations[i];
				bool done = false;
				Guard(() => done = TryConfiscate(c));
				if (done || now - c.since > 600f)
				{
					s_confiscations.RemoveAt(i);
				}
			}
			if (now >= s_nextWorn)
			{
				s_nextWorn = now + WornInterval;
				Guard(CheckWorn);
			}
			if (s_dirty && now >= s_saveAt)
			{
				Save();
			}
		}

		private static void Guard(System.Action action)
		{
			try
			{
				action();
			}
			catch (Exception e)
			{
				if (s_warnings++ < 20)
				{
					ServersidePlugin.logger.LogWarning($"Item ledger: {e}");
				}
			}
		}

		private static void Setup()
		{
			s_ready = true;
			List<string> unknown = new List<string>();
			string spec = Configuration.itemLedgerItems.Value.Trim();
			Dictionary<string, string> why = new Dictionary<string, string>(StringComparer.Ordinal);
			if (spec.Equals("auto", StringComparison.OrdinalIgnoreCase))
			{
				ProgressItems(why);
			}
			else
			{
				foreach (string name in SplitNames(spec))
				{
					why[name] = "listed";
				}
			}
			foreach (string name in SplitNames(Configuration.itemLedgerExtra.Value))
			{
				why[name] = "ExtraItems";
			}
			foreach (string name in SplitNames(Configuration.itemLedgerExclude.Value))
			{
				why.Remove(name);
			}
			foreach (KeyValuePair<string, string> entry in why)
			{
				GameObject prefab = ObjectDB.instance.GetItemPrefab(entry.Key);
				ItemDrop drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
				if (!drop)
				{
					unknown.Add(entry.Key);
					continue;
				}
				s_tracked[entry.Key.GetStableHashCode()] = entry.Key;
				s_why[entry.Key] = entry.Value;
				s_sharedNames[entry.Key] = drop.m_itemData.m_shared.m_name;
			}
			// What traders sell can be bought unseen.
			List<string> sold = new List<string>();
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				Trader trader = prefab ? prefab.GetComponent<Trader>() : null;
				if (trader)
				{
					foreach (Trader.TradeItem item in trader.m_items.Where(t => t.m_prefab))
					{
						string name = item.m_prefab.gameObject.name;
						if (s_tracked.Remove(name.GetStableHashCode()))
						{
							s_why.Remove(name);
							sold.Add(name);
						}
					}
				}
			}
			s_tombstone = "Player_tombstone".GetStableHashCode();
			List<string> fishing = new List<string>();
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (!prefab)
				{
					continue;
				}
				int hash = prefab.name.GetStableHashCode();
				// A caught fish's extra drops go straight into the bag, unseen; the most one catch can bring is counted in.
				Fish fishWithExtras = prefab.GetComponent<Fish>();
				DropTable extraDrops = fishWithExtras ? fishWithExtras.m_extraDrops : null;
				if (extraDrops != null && !extraDrops.IsEmpty() && extraDrops.m_dropChance > 0f)
				{
					List<FishExtra> extras = new List<FishExtra>();
					foreach (DropTable.DropData d in extraDrops.m_drops.Where(d => d.m_item && d.m_item.GetComponent<ItemDrop>() && s_tracked.ContainsKey(d.m_item.name.GetStableHashCode())))
					{
						ItemDrop.ItemData.SharedData shared = d.m_item.GetComponent<ItemDrop>().m_itemData.m_shared;
						extras.Add(new FishExtra
						{
							item = d.m_item.name,
							most = Mathf.Max(Mathf.Max(1, d.m_stackMin), Mathf.Min(shared.m_maxStackSize, d.m_stackMax)),
							rolls = extraDrops.m_oneOfEach ? 1 : Mathf.Max(1, extraDrops.m_dropMax),
							scales = !d.m_dontScale,
							maxStack = shared.m_maxStackSize,
						});
					}
					if (extras.Count > 0)
					{
						s_fishExtras[hash] = extras;
						fishing.Add($"{prefab.name} {string.Join(" ", extras.Select(e => $"{CaughtWith(e)} {e.item}"))}");
					}
				}
				Container container = prefab.GetComponentInChildren<Container>(true);
				if (container)
				{
					s_containers[hash] = new Vector2i(Mathf.Max(8, container.m_width), Mathf.Max(8, container.m_height));
					// Destroyed, a ship or cart moves its cargo into a crate; a chest spills it.
					if (!container.m_destroyedLootPrefab)
					{
						s_dumpingContainers.Add(hash);
					}
				}
				if (prefab.GetComponent<ItemStand>())
				{
					s_itemStands.Add(hash);
				}
				Fish fishPrefab = prefab.GetComponent<Fish>();
				if (fishPrefab && !prefab.GetComponent<ItemDrop>() && fishPrefab.m_pickupItem
					&& s_tracked.ContainsKey(fishPrefab.m_pickupItem.name.GetStableHashCode()))
				{
					s_fishPickups[hash] = new KeyValuePair<string, int>(fishPrefab.m_pickupItem.name, Mathf.Max(1, fishPrefab.m_pickupItemStackSize));
				}
				ArmorStand armorStand = prefab.GetComponent<ArmorStand>();
				if (armorStand && armorStand.m_slots.Count > 0)
				{
					s_armorStands[hash] = Mathf.Min(armorStand.m_slots.Count, SlotItem.Length);
				}
				Piece piece = prefab.GetComponent<Piece>();
				if (piece && piece.m_resources != null)
				{
					List<KeyValuePair<string, int>> cost = piece.m_resources
						.Where(r => r.m_resItem && s_tracked.ContainsKey(r.m_resItem.gameObject.name.GetStableHashCode()) && r.m_amount > 0)
						.Select(r => new KeyValuePair<string, int>(r.m_resItem.gameObject.name, r.m_amount)).ToList();
					if (cost.Count > 0)
					{
						s_pieceCosts[hash] = cost;
					}
				}
			}
			/*
				The crafting bonus (InventoryGui.DoCrafting): at a station with a crafting skill, each craft of a
				stack may add m_craftBonusAmount, cumulatively over a multi-craft -- the n-th lucky craft of a batch
				adds n of them. A whole batch of five in luck yields fifteen more, three per craft: that much is
				counted as possible, so no amount of luck reads as another world.
			*/
			InventoryGui gui = Resources.FindObjectsOfTypeAll<InventoryGui>().FirstOrDefault();
			int batch = gui ? Mathf.Max(1, gui.m_multiCraftAmount) : 5;
			int luck = gui && gui.m_craftBonusChance <= 0f ? 0 : Mathf.CeilToInt((gui ? gui.m_craftBonusAmount : 1) * (batch + 1) / 2f);
			foreach (Recipe recipe in ObjectDB.instance.m_recipes)
			{
				if (!recipe || !recipe.m_enabled || recipe.m_noCraftOnlyUpgrade || !recipe.m_item || !s_tracked.ContainsKey(recipe.m_item.gameObject.name.GetStableHashCode()))
				{
					continue;
				}
				string made = recipe.m_item.gameObject.name;
				if (!s_recipes.TryGetValue(made, out List<RecipeInfo> list))
				{
					s_recipes[made] = list = new List<RecipeInfo>();
				}
				List<Ingredient> ingredients = recipe.m_resources.Where(r => r != null && r.m_resItem && (r.m_amount > 0 || r.m_amountPerLevel > 0)).Select(r => new Ingredient
				{
					item = r.m_resItem.gameObject.name,
					tracked = s_tracked.ContainsKey(r.m_resItem.gameObject.name.GetStableHashCode()),
					upgrader = r.m_upgraderResource,
					requirement = r,
				}).ToList();
				ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
				// One of several ingredients (Recipe.GetAmount): a better one, or one that gives extra, yields more.
				int better = !recipe.m_requireOnlyOneIngredient ? 0 : ingredients.Where(i => !i.upgrader)
					.Select(i => Mathf.CeilToInt((Mathf.Max(1, i.requirement.m_resItem.m_itemData.m_shared.m_maxQuality) - 1) * recipe.m_amount * recipe.m_qualityResultAmountMultiplier)
						+ Mathf.Max(0, i.requirement.m_extraAmountOnlyOneIngredient))
					.DefaultIfEmpty(0).Max();
				bool lucky = recipe.m_craftingStation && recipe.m_craftingStation.m_craftingSkill != Skills.SkillType.None && shared.m_maxStackSize > 1;
				float returns = ingredients.Where(i => i.upgrader).Select(i => i.requirement.m_resItem.m_itemData.m_shared)
					.Where(s => s.m_breakChance > 0f).Select(s => s.m_breakReturnIngreientsAmount).DefaultIfEmpty(0f).Max();
				RecipeInfo info = new RecipeInfo
				{
					amount = Mathf.Max(1, recipe.m_amount),
					anyOne = recipe.m_requireOnlyOneIngredient,
					maxQuality = Mathf.Max(1, shared.m_maxQuality),
					upgradeStation = ingredients.Any(i => i.upgrader),
					breakReturns = returns,
					ingredients = ingredients,
				};
				info.most = info.amount + better + (lucky ? luck : 0);
				list.Add(info);
				list.Sort((a, b) => a.amount.CompareTo(b.amount));
				if (returns > 0f)
				{
					foreach (Ingredient ingredient in ingredients.Where(i => i.tracked && !i.upgrader && i.requirement.m_recover))
					{
						if (!s_salvage.TryGetValue(ingredient.item, out List<string> from))
						{
							s_salvage[ingredient.item] = from = new List<string>();
						}
						if (!from.Contains(made))
						{
							from.Add(made);
						}
					}
				}
			}
			Load();
			string summary = $"Item ledger: {Configuration.itemLedgerMode.Value}. Tracking {s_tracked.Count} item(s); {s_accounts.Count} account(s) in {Path.GetFileName(s_path)}, "
				+ $"counting since {s_started:yyyy-MM-dd HH:mm} UTC{(InGrace() ? $" (grace until {s_started.AddHours(Configuration.itemLedgerGraceHours.Value):yyyy-MM-dd HH:mm}: no character counts as new)" : "")}; "
				+ $"{s_recipes.Count} of them have recipes, {s_pieceCosts.Count} kinds of piece are built with them; a craft at a station may yield {luck} more of a stack by luck."
				+ (unknown.Count > 0 ? $" Unknown item names ignored: {string.Join(", ", unknown)}." : "")
				+ (sold.Count > 0 ? $" Sold by traders, not tracked: {string.Join(", ", sold.OrderBy(n => n))}." : "")
				+ (fishing.Count > 0 ? $" A caught fish counts in at most: {string.Join(", ", fishing.OrderBy(n => n))}." : "");
			ServersidePlugin.logger.LogInfo(summary);
			GuardLog.Write(summary);
			foreach (IGrouping<string, string> group in s_why.GroupBy(kv => kv.Value.StartsWith("dropped by ", StringComparison.Ordinal) ? "dropped by a boss" : kv.Value, kv => kv.Key).OrderBy(g => g.Key))
			{
				GuardLog.Write($"Item ledger: tracked, {group.Key} ({group.Count()}): {string.Join(", ", group.OrderBy(n => n))}");
			}
		}

		private static IEnumerable<string> SplitNames(string list)
		{
			return (list ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0);
		}

		private static bool IsWorkbench(CraftingStation station)
		{
			return station && global::Utils.GetPrefabName(station.gameObject) == "piece_workbench";
		}

		/*
			The items that lock progress, from the game's own data:
			- what the game does not let through a portal (ores, metals, dragon eggs);
			- what bosses drop, and what summons them;
			- materials that every recipe and piece using them needs more than a level 1 workbench for, and
			  items that every recipe making them needs more for: what a forge, cauldron, black forge,
			  galdr table ... or an upgraded workbench makes, and what goes into it.
		*/
		private static void ProgressItems(Dictionary<string, string> why)
		{
			void Add(string name, string reason)
			{
				if (!why.ContainsKey(name))
				{
					why[name] = reason;
				}
			}
			foreach (GameObject item in ObjectDB.instance.m_items)
			{
				ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
				if (drop && !drop.m_itemData.m_shared.m_teleportable)
				{
					Add(item.name, "cannot go through a portal");
				}
			}
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				Character character = prefab ? prefab.GetComponent<Character>() : null;
				CharacterDrop drops = character && character.m_boss ? prefab.GetComponent<CharacterDrop>() : null;
				if (drops)
				{
					foreach (CharacterDrop.Drop d in drops.m_drops.Where(d => d.m_prefab && d.m_prefab.GetComponent<ItemDrop>()))
					{
						Add(d.m_prefab.name, "dropped by " + prefab.name);
					}
				}
			}
			foreach (string name in BossSummonItems)
			{
				Add(name, "summons a boss");
			}
			// Materials: true while a basic use (no station, or a level 1 workbench) has been seen.
			Dictionary<string, bool> basicUse = new Dictionary<string, bool>(StringComparer.Ordinal);
			Dictionary<string, bool> basicMake = new Dictionary<string, bool>(StringComparer.Ordinal);
			void Use(Piece.Requirement[] requirements, bool advanced)
			{
				foreach (Piece.Requirement r in requirements ?? new Piece.Requirement[0])
				{
					if (r != null && r.m_resItem)
					{
						string name = r.m_resItem.gameObject.name;
						basicUse[name] = (basicUse.TryGetValue(name, out bool basic) && basic) || !advanced;
					}
				}
			}
			foreach (Recipe recipe in ObjectDB.instance.m_recipes)
			{
				if (recipe && recipe.m_enabled && recipe.m_item)
				{
					bool advanced = recipe.m_craftingStation && (!IsWorkbench(recipe.m_craftingStation) || recipe.m_minStationLevel > 1);
					Use(recipe.m_resources, advanced);
					string made = recipe.m_item.gameObject.name;
					basicMake[made] = (basicMake.TryGetValue(made, out bool basic) && basic) || !advanced;
				}
			}
			// Pieces the player can build: those in the build tools' piece tables.
			foreach (GameObject item in ObjectDB.instance.m_items)
			{
				PieceTable table = item ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
				if (!table)
				{
					continue;
				}
				foreach (GameObject pieceObject in table.m_pieces)
				{
					Piece piece = pieceObject ? pieceObject.GetComponent<Piece>() : null;
					if (piece)
					{
						Use(piece.m_resources, piece.m_craftingStation && !IsWorkbench(piece.m_craftingStation));
					}
				}
			}
			foreach (KeyValuePair<string, bool> use in basicUse.Where(u => !u.Value))
			{
				Add(use.Key, "only used past a level 1 workbench");
			}
			foreach (KeyValuePair<string, bool> make in basicMake.Where(m => !m.Value))
			{
				Add(make.Key, "only made past a level 1 workbench");
			}
		}

		private static bool InGrace()
		{
			return (DateTime.UtcNow - s_started).TotalHours < Configuration.itemLedgerGraceHours.Value;
		}

		// The account of the character this peer plays; opened on the first thing it does.
		private static Account AccountOf(long peerUid)
		{
			if (peerUid == 0L || peerUid == ZDOMan.GetSessionID())
			{
				return null;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(peerUid);
			if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone())
			{
				return null;
			}
			if (Configuration.itemLedgerExemptAdmins.Value && ZNet.instance.IsAdmin(peer.m_socket.GetHostName()))
			{
				return null;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			long id = character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
			if (id == 0L)
			{
				return null;
			}
			if (!s_accounts.TryGetValue(id, out Account account))
			{
				bool fresh = !InGrace() && CharacterGuard.IsBrandNew(peer, character, id);
				s_accounts[id] = account = new Account { id = id, name = peer.m_playerName, exact = fresh, since = DateTime.UtcNow };
				GuardLog.Write($"{peer.m_playerName} ({id}): account opened, "
					+ (fresh ? "exact from zero (a new, fresh character)" : "what it carries now is unknown; exact from its first death here"));
				MarkDirty();
			}
			account.name = peer.m_playerName;
			return account;
		}

		private static int Have(Account account, string item)
		{
			return account.balance.TryGetValue(item, out int n) ? n : 0;
		}

		private static void Add(Account account, string item, int amount)
		{
			account.balance[item] = Have(account, item) + amount;
			if (account.balance[item] <= 0)
			{
				account.balance.Remove(item);
			}
			MarkDirty();
		}

		private static void SeenQuality(Account account, string item, int quality)
		{
			if (quality > 1 && (!account.quality.TryGetValue(item, out int q) || q < quality))
			{
				account.quality[item] = quality;
				MarkDirty();
			}
		}

		private static void In(Account account, string item, int amount, int quality, string how)
		{
			if (account == null || amount <= 0)
			{
				return;
			}
			Add(account, item, amount);
			SeenQuality(account, item, quality);
			if (Configuration.itemLedgerLogAll.Value && !s_quiet)
			{
				GuardLog.Write($"{account.name}: +{amount} {Label(item, quality)} ({how}), has {Have(account, item)}");
			}
		}

		/*
			Takes an amount out of the account and returns how much of it the character never got here nor could
			make. What can be made is made first (its tracked ingredients paid). The account never goes below zero.
		*/
		private static int Out(Account account, string item, int amount, int quality, string how)
		{
			int have = Have(account, item);
			if (have < amount)
			{
				Make(account, item, amount - have, quality, 0);
				have = Have(account, item);
			}
			if (have < amount)
			{
				Salvage(account, item, amount - have);
				have = Have(account, item);
			}
			int taken = Mathf.Min(have, amount);
			Add(account, item, -taken);
			SeenQuality(account, item, quality);
			int missing = amount - taken;
			if (Configuration.itemLedgerLogAll.Value && !s_quiet)
			{
				GuardLog.Write($"{account.name}: -{amount} {Label(item, quality)} ({how}), has {Have(account, item)}{(missing > 0 ? $", {missing} unexplained" : "")}");
			}
			return missing;
		}

		// How much of an item the account cannot explain, without changing the account.
		private static int Missing(Account account, string item, int amount, int quality)
		{
			Dictionary<string, int> balance = new Dictionary<string, int>(account.balance, StringComparer.Ordinal);
			Dictionary<string, int> qualities = new Dictionary<string, int>(account.quality, StringComparer.Ordinal);
			bool dirty = s_dirty;
			try
			{
				s_quiet = true;
				return Out(account, item, amount, quality, "check");
			}
			finally
			{
				s_quiet = false;
				account.balance.Clear();
				foreach (KeyValuePair<string, int> kv in balance)
				{
					account.balance[kv.Key] = kv.Value;
				}
				account.quality.Clear();
				foreach (KeyValuePair<string, int> kv in qualities)
				{
					account.quality[kv.Key] = kv.Value;
				}
				s_dirty = dirty;
			}
		}

		/*
			Makes up to count of an item (at a quality: an upgraded item costs its upgrades too) from what the
			account holds, the way a crafting station would, and adds what was made. Returns how many. A craft
			counts as yielding the most it can (Setup), and a recipe is paid in full or not at all.
		*/
		private static int Make(Account account, string item, int count, int quality, int depth)
		{
			if (count <= 0 || !s_recipes.TryGetValue(item, out List<RecipeInfo> recipes))
			{
				return 0;
			}
			if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost))
			{
				Add(account, item, count);
				return count;
			}
			int made = 0;
			int crafts = 0;
			Dictionary<string, int> paid = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (RecipeInfo recipe in recipes)
			{
				while (made < count && TryCraft(account, recipe, quality, depth, paid))
				{
					made += quality > 1 ? 1 : recipe.most;
					crafts++;
				}
				if (made >= count)
				{
					break;
				}
			}
			if (made > 0)
			{
				Add(account, item, made);
				if (Configuration.itemLedgerLogAll.Value && !s_quiet)
				{
					GuardLog.Write($"{account.name}: crafted {Label(item, quality)} {crafts}x, at most {made}"
						+ (paid.Count > 0 ? $", from {string.Join(", ", paid.Select(p => $"{p.Value} {p.Key}"))}" : "") + $", has {Have(account, item)}");
				}
			}
			return made;
		}

		private static int Cost(Ingredient ingredient, int quality)
		{
			int total = 0;
			for (int q = 1; q <= Mathf.Max(1, quality); q++)
			{
				total += ingredient.requirement.GetAmount(q);
			}
			return total;
		}

		/*
			What levels from..to of a recipe take from the tracked items. Level 1 is the craft itself: every
			ingredient but the upgrader items. Each further level takes its share of the same ingredients at a
			crafting station, or only the upgrader items at the upgrade station -- the only way past the top
			quality. Upgrader items that are not tracked make an upgrade there free as far as the account goes.
		*/
		private static Dictionary<string, int> Needs(RecipeInfo recipe, int from, int to, bool atUpgradeStation)
		{
			Dictionary<string, int> needs = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int level = Mathf.Max(1, from); level <= to; level++)
			{
				bool upgrader = level > 1 && (level > recipe.maxQuality || (atUpgradeStation && recipe.upgradeStation));
				foreach (Ingredient ingredient in recipe.ingredients.Where(i => i.tracked && i.upgrader == upgrader))
				{
					int amount = ingredient.requirement.GetAmount(level);
					if (amount > 0)
					{
						needs[ingredient.item] = (needs.TryGetValue(ingredient.item, out int n) ? n : 0) + amount;
					}
				}
			}
			return needs;
		}

		// The cheaper way first: free upgrades at the upgrade station, else the station's materials, else the upgrader items.
		private static bool TryCraft(Account account, RecipeInfo recipe, int quality, int depth, Dictionary<string, int> paid)
		{
			if (recipe.anyOne)
			{
				List<Ingredient> options = recipe.ingredients.Where(i => !i.upgrader).ToList();
				if (options.Any(i => !i.tracked))
				{
					return true;
				}
				foreach (Ingredient ingredient in options)
				{
					if (Pay(account, new Dictionary<string, int>(StringComparer.Ordinal) { { ingredient.item, Cost(ingredient, quality) } }, depth, paid))
					{
						return true;
					}
				}
				return false;
			}
			Dictionary<string, int> craft = Needs(recipe, 1, 1, false);
			Dictionary<string, int> station = Needs(recipe, 2, quality, false);
			Dictionary<string, int> upgrade = Needs(recipe, 2, quality, true);
			if (quality <= 1 || !recipe.upgradeStation)
			{
				return Pay(account, Merge(craft, station), depth, paid);
			}
			if (upgrade.Count == 0)
			{
				return Pay(account, craft, depth, paid);
			}
			return Pay(account, Merge(craft, station), depth, paid) || Pay(account, Merge(craft, upgrade), depth, paid);
		}

		private static Dictionary<string, int> Merge(Dictionary<string, int> a, Dictionary<string, int> b)
		{
			Dictionary<string, int> sum = new Dictionary<string, int>(a, StringComparer.Ordinal);
			foreach (KeyValuePair<string, int> n in b)
			{
				sum[n.Key] = (sum.TryGetValue(n.Key, out int m) ? m : 0) + n.Value;
			}
			return sum;
		}

		/*
			Pays all of it, making what is short, or leaves the account as it was: a part paid would turn
			materials the character still has into things it never made. Tried quietly first, so the log only
			shows crafts that happened.
		*/
		private static bool Pay(Account account, Dictionary<string, int> needs, int depth, Dictionary<string, int> paid)
		{
			Dictionary<string, int> before = new Dictionary<string, int>(account.balance, StringComparer.Ordinal);
			bool quiet = s_quiet;
			s_quiet = true;
			bool can = needs.All(need => Ensure(account, need.Key, need.Value, depth));
			s_quiet = quiet;
			account.balance.Clear();
			foreach (KeyValuePair<string, int> kv in before)
			{
				account.balance[kv.Key] = kv.Value;
			}
			if (!can)
			{
				return false;
			}
			foreach (KeyValuePair<string, int> need in needs)
			{
				Ensure(account, need.Key, need.Value, depth);
			}
			foreach (KeyValuePair<string, int> need in needs)
			{
				Add(account, need.Key, -need.Value);
				paid[need.Key] = (paid.TryGetValue(need.Key, out int n) ? n : 0) + need.Value;
			}
			return true;
		}

		private static bool Ensure(Account account, string item, int need, int depth)
		{
			int have = Have(account, item);
			if (have < need && depth + 1 < MakeDepth)
			{
				Make(account, item, need - have, 1, depth + 1);
			}
			return Have(account, item) >= need;
		}

		/*
			Valheim 1.0's upgrade station breaks what it fails to upgrade and hands part of its materials back
			(InventoryGui.DoCrafting): ceil((first + current level's amount) * m_breakReturnIngreientsAmount) of
			every recoverable ingredient. An item the account holds may have gone that way.
		*/
		private static void Salvage(Account account, string item, int need)
		{
			if (!s_salvage.TryGetValue(item, out List<string> sources) || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost))
			{
				return;
			}
			int got = 0;
			foreach (string source in sources)
			{
				RecipeInfo recipe = s_recipes[source].FirstOrDefault(r => r.breakReturns > 0f);
				int quality = account.quality.TryGetValue(source, out int seen) ? seen : 1;
				while (recipe != null && got < need && Have(account, source) > (account.wornNow.Contains(source) ? 1 : 0))
				{
					Add(account, source, -1);
					List<string> back = new List<string>();
					foreach (Ingredient ingredient in recipe.ingredients.Where(i => i.tracked && !i.upgrader && i.requirement.m_recover))
					{
						int amount = Mathf.CeilToInt((ingredient.requirement.GetAmount(1) + ingredient.requirement.GetAmount(quality)) * recipe.breakReturns);
						if (amount > 0)
						{
							Add(account, ingredient.item, amount);
							back.Add($"{amount} {ingredient.item}");
							got += ingredient.item == item ? amount : 0;
						}
					}
					if (Configuration.itemLedgerLogAll.Value && !s_quiet)
					{
						GuardLog.Write($"{account.name}: -1 {Label(source, quality)} (may have broken at an upgrade station), got back {string.Join(", ", back)}");
					}
				}
			}
		}

		// The most of an extra drop one catch can bring (DropTable.GetDropListItems, with Game.ScaleDrops).
		private static int CaughtWith(FishExtra extra)
		{
			int most = extra.most;
			if (extra.scales && Game.m_resourceRate != 1f)
			{
				most = Mathf.Max(most, Mathf.CeilToInt((extra.most + 1) * Game.m_resourceRate));
				most = extra.maxStack > 1 ? Mathf.Min(most, extra.maxStack) : most;
			}
			return most * extra.rolls;
		}

		private static string Label(string item, int quality)
		{
			return quality > 1 ? $"{item}@{quality}" : item;
		}

		// Logs what cannot be explained; returns whether it is to be taken away.
		private static bool Report(Account account, long peerUid, int missing, string item, int quality, string how, bool canTakeAway = true)
		{
			if (account == null || missing <= 0)
			{
				return false;
			}
			bool act = Enforcing && account.exact && canTakeAway;
			string what = $"{account.name} ({account.id}) {how} {missing} {Label(item, quality)} it never got on this server"
				+ (account.exact ? "" : " (unverified: what it carried before counting began is unknown)")
				+ (act ? ": taken away" : "");
			GuardLog.Write(what);
			ServersidePlugin.logger.LogWarning("Item ledger: " + what);
			if (act && peerUid != 0L)
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, "ShowMessage", (int)MessageHud.MessageType.Center,
					string.Format(Configuration.itemLedgerMessage.Value, missing, LocalizedName(item)));
			}
			return act;
		}

		// The item's name token ($item_copper): the client's message display translates it.
		private static string LocalizedName(string item)
		{
			return s_sharedNames.TryGetValue(item, out string name) ? name : item;
		}

		// ---- What the items in a ZDO are -------------------------------------------------------------------

		// An item drop's own data (ItemDrop.SaveToZDO): stack, quality, picked up. Falls back to the prefab.
		private static Held ReadDrop(ZDO zdo, string item, int dataKey = 0)
		{
			Held stack = new Held { item = item, quality = 1, count = 1, pickedUp = false };
			byte[] data = zdo.GetByteArray(dataKey != 0 ? dataKey : ZDOVars.s_itemData);
			if (data != null && data.Length > 2)
			{
				ZPackage package = new ZPackage(data);
				Version.Item version = (Version.Item)package.ReadByte();
				ItemDrop.ItemData itemData = new ItemDrop.ItemData();
				ItemDrop.ItemData.Load(package, itemData, version);
				stack.count = Mathf.Max(1, itemData.m_stack);
				stack.quality = Mathf.Max(1, itemData.m_quality);
				stack.pickedUp = itemData.m_pickedUp;
			}
			return stack;
		}

		// Tracked items in a saved inventory (Inventory.Save).
		private static List<Held> Contents(byte[] data, int prefab)
		{
			List<Held> items = new List<Held>();
			if (data == null || data.Length == 0)
			{
				return items;
			}
			Vector2i size = s_containers.TryGetValue(prefab, out Vector2i s) ? s : new Vector2i(8, 8);
			Inventory inventory = new Inventory("ledger", null, size.x, size.y);
			inventory.Load(new ZPackage(data));
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				string name = item.m_dropPrefab ? item.m_dropPrefab.name : null;
				if (name != null && s_tracked.ContainsKey(name.GetStableHashCode()))
				{
					items.Add(new Held { item = name, quality = Mathf.Max(1, item.m_quality), count = item.m_stack, pickedUp = item.m_pickedUp });
				}
			}
			return items;
		}

		private static Dictionary<(string, int), int> Totals(IEnumerable<Held> stacks)
		{
			Dictionary<(string, int), int> totals = new Dictionary<(string, int), int>();
			foreach (Held s in stacks)
			{
				totals[(s.item, s.quality)] = (totals.TryGetValue((s.item, s.quality), out int n) ? n : 0) + s.count;
			}
			return totals;
		}

		private static int[] ReadSlots(ZDO zdo, int slots)
		{
			int[] items = new int[slots];
			for (int i = 0; i < slots; i++)
			{
				items[i] = zdo.GetInt(SlotItem[i], 0);
			}
			return items;
		}

		// ---- Incoming object data from a player --------------------------------------------------------------

		public static object BeforeDeserialize(ZDO zdo)
		{
			if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off || s_sender == 0L || s_sender == ZDOMan.GetSessionID()
				|| ZDOMan.instance.m_deadZDOs.ContainsKey(zdo.m_uid))
			{
				return null;
			}
			int prefab = zdo.GetPrefab();
			if (prefab == 0)
			{
				return NewObject;
			}
			if (s_containers.ContainsKey(prefab))
			{
				return new Snapshot { kind = HolderKind.Container, items = zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0] };
			}
			if (s_itemStands.Contains(prefab))
			{
				return new Snapshot { kind = HolderKind.ItemStand, slots = new[] { zdo.GetInt(ZDOVars.s_item, 0) } };
			}
			if (s_armorStands.TryGetValue(prefab, out int slots))
			{
				return new Snapshot { kind = HolderKind.ArmorStand, slots = ReadSlots(zdo, slots) };
			}
			return null;
		}

		public static void AfterDeserialize(ZDO zdo, object state)
		{
			if (state == null)
			{
				return;
			}
			Guard(() =>
			{
				// Only what the sending player owns: its own new objects and the holders it has open.
				long owner = zdo.GetOwner();
				if (owner != s_sender)
				{
					return;
				}
				int prefab = zdo.GetPrefab();
				if (state == NewObject)
				{
					NewFromPlayer(zdo, prefab, owner);
					return;
				}
				Snapshot before = (Snapshot)state;
				switch (before.kind)
				{
					case HolderKind.Container:
						ContainerChanged(zdo, prefab, owner, before.items);
						break;
					case HolderKind.ItemStand:
						StandChanged(zdo, owner, HolderKind.ItemStand, 0, before.slots[0], zdo.GetInt(ZDOVars.s_item, 0), ZDOVars.s_itemData);
						break;
					case HolderKind.ArmorStand:
						int[] now = ReadSlots(zdo, before.slots.Length);
						for (int i = 0; i < now.Length; i++)
						{
							StandChanged(zdo, owner, HolderKind.ArmorStand, i, before.slots[i], now[i], SlotData[i]);
						}
						break;
				}
			});
		}

		private static void NewFromPlayer(ZDO zdo, int prefab, long owner)
		{
			if (s_tracked.TryGetValue(prefab, out string item))
			{
				Held stack = ReadDrop(zdo, item);
				// Only an item that has been in a bag: what falls out of a piece, creature or rock is new.
				if (stack.pickedUp)
				{
					s_pendingDrops[zdo.m_uid] = new PendingDrop { uid = zdo.m_uid, peer = owner, item = item, stack = stack.count, quality = stack.quality, since = Time.time };
				}
			}
			else if (prefab == s_tombstone)
			{
				Died(zdo, owner);
			}
			else if (s_pieceCosts.TryGetValue(prefab, out List<KeyValuePair<string, int>> cost) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost))
			{
				Account account = AccountOf(owner);
				if (account == null || zdo.GetLong(ZDOVars.s_creator, 0L) != account.id)
				{
					return;
				}
				foreach (KeyValuePair<string, int> c in cost)
				{
					Report(account, owner, Out(account, c.Key, c.Value, 1, "built"), c.Key, 1, "built with", canTakeAway: false);
				}
			}
		}

		private static int ProcessDrop(PendingDrop drop, ZDO zdo, bool beingPickedUp)
		{
			s_pendingDrops.Remove(drop.uid);
			int fromWorld = TakeExpected(drop.peer, drop.item, drop.stack);
			int fromBag = drop.stack - fromWorld;
			Account account = fromBag > 0 ? AccountOf(drop.peer) : null;
			if (account == null)
			{
				return 0;
			}
			int missing = Out(account, drop.item, fromBag, drop.quality, "dropped");
			// Picked up again before it was looked at, it is back in a bag: nothing to cut, it is only not counted in.
			if (Report(account, drop.peer, missing, drop.item, drop.quality, "dropped", canTakeAway: !beingPickedUp && zdo != null))
			{
				CutDrop(zdo, missing);
			}
			return missing;
		}

		private static void CutDrop(ZDO zdo, int amount)
		{
			zdo.SetOwner(ZDOMan.GetSessionID());
			GameObject instance = ZNetScene.instance.FindInstance(zdo.m_uid);
			ItemDrop drop = instance ? instance.GetComponent<ItemDrop>() : null;
			int stack = drop ? drop.m_itemData.m_stack : ReadDrop(zdo, "").count;
			if (amount >= stack)
			{
				if (instance)
				{
					ZNetScene.instance.Destroy(instance);
				}
				else
				{
					ZDOMan.instance.DestroyZDO(zdo);
				}
				return;
			}
			if (drop)
			{
				drop.m_itemData.m_stack = stack - amount;
				ItemDrop.SaveToZDO(drop.m_itemData, zdo);
				return;
			}
			ItemDrop.ItemData itemData = new ItemDrop.ItemData();
			ItemDrop.LoadFromZDO(itemData, zdo);
			itemData.m_dropPrefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			itemData.m_stack = stack - amount;
			ItemDrop.SaveToZDO(itemData, zdo);
		}

		private static void Expect(long peer, string item, int count)
		{
			if (count <= 0)
			{
				return;
			}
			if (!s_expected.TryGetValue(peer, out List<Expected> list))
			{
				s_expected[peer] = list = new List<Expected>();
			}
			list.Add(new Expected { item = item, count = count, until = Time.time + ExpectSeconds });
		}

		// How much of a drop the player's recent emptying of a holder explains.
		private static int TakeExpected(long peer, string item, int count)
		{
			if (!s_expected.TryGetValue(peer, out List<Expected> list))
			{
				return 0;
			}
			list.RemoveAll(e => e.until < Time.time - DropWait);
			int taken = 0;
			foreach (Expected e in list.Where(e => e.item == item))
			{
				int n = Mathf.Min(e.count, count - taken);
				e.count -= n;
				taken += n;
				if (taken >= count)
				{
					break;
				}
			}
			list.RemoveAll(e => e.count <= 0);
			return taken;
		}

		/*
			A tombstone is the whole bag. For an exact account every tracked item in it must be explained;
			afterwards the bag is empty, and taking the items back out of the tombstone counts them in again.
		*/
		private static void Died(ZDO tombstone, long owner)
		{
			Account account = AccountOf(owner);
			if (account == null || tombstone.GetLong(ZDOVars.s_owner, 0L) != account.id)
			{
				return;
			}
			List<Held> bag = Contents(tombstone.GetByteArray(ZDOVars.s_items), tombstone.GetPrefab());
			if (account.exact)
			{
				foreach (KeyValuePair<(string, int), int> stack in Totals(bag))
				{
					int missing = Out(account, stack.Key.Item1, stack.Value, stack.Key.Item2, "died carrying");
					if (Report(account, owner, missing, stack.Key.Item1, stack.Key.Item2, "died carrying"))
					{
						s_confiscations.Add(new Confiscation { holder = tombstone.m_uid, kind = HolderKind.Container, item = stack.Key.Item1, amount = missing, peer = owner, since = Time.time });
					}
				}
			}
			bool whole = !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory);
			if (whole)
			{
				account.balance.Clear();
				if (!account.exact)
				{
					account.exact = true;
					GuardLog.Write($"{account.name} ({account.id}) died: the tombstone showed the whole bag ({bag.Sum(s => s.count)} tracked item(s)), the account is exact from now on");
				}
				MarkDirty();
			}
		}

		private static void ContainerChanged(ZDO zdo, int prefab, long owner, byte[] before)
		{
			byte[] after = zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0];
			if (before.SequenceEqual(after))
			{
				return;
			}
			Dictionary<(string, int), int> was = Totals(Contents(before, prefab));
			Dictionary<(string, int), int> now = Totals(Contents(after, prefab));
			Account account = null;
			foreach ((string, int) key in was.Keys.Union(now.Keys).ToList())
			{
				int delta = (now.TryGetValue(key, out int n) ? n : 0) - (was.TryGetValue(key, out int w) ? w : 0);
				if (delta == 0)
				{
					continue;
				}
				account = account ?? AccountOf(owner);
				if (account == null)
				{
					return;
				}
				if (delta < 0)
				{
					In(account, key.Item1, -delta, key.Item2, "took from a container");
					continue;
				}
				int missing = Out(account, key.Item1, delta, key.Item2, "put into a container");
				if (Report(account, owner, missing, key.Item1, key.Item2, "put into a container"))
				{
					s_confiscations.Add(new Confiscation { holder = zdo.m_uid, kind = HolderKind.Container, item = key.Item1, amount = missing, peer = owner, since = Time.time });
				}
			}
		}

		private static void StandChanged(ZDO zdo, long owner, HolderKind kind, int slot, int was, int now, int dataKey)
		{
			if (was == now)
			{
				return;
			}
			if (was != 0 && s_tracked.TryGetValue(was, out string removed))
			{
				// Taken off: the item falls out of the stand, so a drop of it from this player follows.
				Expect(owner, removed, 1);
			}
			if (now != 0 && s_tracked.TryGetValue(now, out string put))
			{
				Account account = AccountOf(owner);
				if (account == null)
				{
					return;
				}
				int quality = ReadDrop(zdo, put, dataKey).quality;
				int missing = Out(account, put, 1, quality, kind == HolderKind.ItemStand ? "put on an item stand" : "put on an armor stand");
				if (Report(account, owner, missing, put, quality, kind == HolderKind.ItemStand ? "put on an item stand" : "put on an armor stand"))
				{
					s_confiscations.Add(new Confiscation { holder = zdo.m_uid, kind = kind, item = put, amount = 1, slot = slot, peer = owner, since = Time.time });
				}
			}
		}

		// Takes the items away once nobody is using the holder; the server owns it for that.
		private static bool TryConfiscate(Confiscation c)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(c.holder);
			if (zdo == null)
			{
				return true;
			}
			if (c.kind == HolderKind.Container)
			{
				if (zdo.GetInt(ZDOVars.s_inUse, 0) != 0)
				{
					return false;
				}
				zdo.SetOwner(ZDOMan.GetSessionID());
				Vector2i size = s_containers.TryGetValue(zdo.GetPrefab(), out Vector2i s) ? s : new Vector2i(8, 8);
				Inventory inventory = new Inventory("ledger", null, size.x, size.y);
				inventory.Load(new ZPackage(zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0]));
				int had = inventory.CountItems(s_sharedNames[c.item], -1, matchWorldLevel: false);
				int taken = Mathf.Min(had, c.amount);
				inventory.RemoveItem(s_sharedNames[c.item], taken, -1, worldLevelBased: false);
				ZPackage package = new ZPackage();
				inventory.Save(package);
				zdo.Set(ZDOVars.s_items, package.GetArray());
				GuardLog.Write($"took {taken} {c.item} out of the {ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name} at {zdo.GetPosition():F0}");
				return true;
			}
			int key = c.kind == HolderKind.ItemStand ? ZDOVars.s_item : SlotItem[c.slot];
			if (zdo.GetInt(key, 0) != c.item.GetStableHashCode())
			{
				return true;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(key, 0);
			if (c.kind == HolderKind.ItemStand)
			{
				zdo.Set(ZDOVars.s_type, 0);
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, zdo.m_uid, "SetVisualItem", 0, 0, 0, 0);
			}
			else
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, zdo.m_uid, "RPC_SetVisualItem", c.slot, 0, 0);
			}
			GuardLog.Write($"took {c.item} off the stand at {zdo.GetPosition():F0}");
			return true;
		}

		/*
			ItemDrop.TimedDestruction: an item that has lain an hour with no player within 25 m is removed by
			the game that owns it, which then sends the DestroyZDO a pickup would. A pickup happens beside the player.
		*/
		private static bool Despawned(long sender, ZDO zdo)
		{
			long spawned = zdo.GetLong(ZDOVars.s_spawnTime, 0L);
			if (spawned == 0L || (ZNet.instance.GetTime() - new DateTime(spawned)).TotalSeconds < 3600.0)
			{
				return false;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(sender);
			ZDO character = peer != null && !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
			Vector3 player = character != null ? character.GetPosition() : peer != null ? peer.m_refPos : zdo.GetPosition();
			return Vector3.Distance(player, zdo.GetPosition()) > 25f;
		}

		// Picked up, or a holder emptied by being destroyed: a DestroyZDO from that player.
		public static void Destroyed(long sender, ZDO zdo)
		{
			if (zdo == null || sender == 0L || sender == ZDOMan.GetSessionID())
			{
				return;
			}
			int prefab = zdo.GetPrefab();
			// A fresh fish out of the water is a catch; what it brought along went straight into the bag.
			if (s_fishExtras.TryGetValue(prefab, out List<FishExtra> extras) && !ReadDrop(zdo, "").pickedUp && !Despawned(sender, zdo))
			{
				Account angler = AccountOf(sender);
				foreach (FishExtra extra in extras)
				{
					In(angler, extra.item, CaughtWith(extra), 1, "may have come with a caught " + ZNetScene.instance.GetPrefab(prefab)?.name);
				}
			}
			if (s_tracked.TryGetValue(prefab, out string item))
			{
				int missing = s_pendingDrops.TryGetValue(zdo.m_uid, out PendingDrop pending) ? ProcessDrop(pending, zdo, beingPickedUp: true) : 0;
				Held stack = ReadDrop(zdo, item);
				if (Despawned(sender, zdo))
				{
					if (Configuration.itemLedgerLogAll.Value)
					{
						GuardLog.Write($"{stack.count} {Label(item, stack.quality)} lying at {zdo.GetPosition():F0} for an hour, nobody near, was removed by the game of {AccountOf(sender)?.name ?? sender.ToString()}");
					}
					return;
				}
				// What could not be explained when it was dropped is not counted in again.
				In(AccountOf(sender), item, stack.count - missing, stack.quality, "picked up");
				return;
			}
			if (s_fishPickups.TryGetValue(prefab, out KeyValuePair<string, int> fished))
			{
				In(AccountOf(sender), fished.Key, fished.Value, 1, "caught");
				return;
			}
			if (s_dumpingContainers.Contains(prefab))
			{
				foreach (Held stack in Contents(zdo.GetByteArray(ZDOVars.s_items), prefab).Where(s => s.pickedUp))
				{
					Expect(sender, stack.item, stack.count);
				}
			}
			if (s_itemStands.Contains(prefab) && s_tracked.TryGetValue(zdo.GetInt(ZDOVars.s_item, 0), out string onStand))
			{
				Expect(sender, onStand, 1);
			}
			if (s_armorStands.TryGetValue(prefab, out int slots))
			{
				foreach (int hash in ReadSlots(zdo, slots))
				{
					if (s_tracked.TryGetValue(hash, out string worn))
					{
						Expect(sender, worn, 1);
					}
				}
			}
		}

		// ---- Stations and altars (their calls run on the server, which owns them near players) ---------------

		private static void Fed(long sender, string item, string how, ref bool undo)
		{
			if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off || item == null || !s_tracked.ContainsKey(item.GetStableHashCode()))
			{
				return;
			}
			Account account = AccountOf(sender);
			if (account == null)
			{
				return;
			}
			int missing = Out(account, item, 1, 1, how);
			undo = Report(account, sender, missing, item, 1, how);
		}

		// The wearing check: what each player wears and holds must be explained, and so must its upgrades.
		private static void CheckWorn()
		{
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!peer.IsReady() || peer.m_characterID.IsNone())
				{
					continue;
				}
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				Account account = character != null ? AccountOf(peer.m_uid) : null;
				if (account == null)
				{
					continue;
				}
				account.wornNow = new HashSet<string>(Worn.Select(slot => s_tracked.TryGetValue(character.GetInt(slot, 0), out string item) ? item : null).Where(item => item != null), StringComparer.Ordinal);
				foreach (int slot in Worn)
				{
					if (!s_tracked.TryGetValue(character.GetInt(slot, 0), out string item))
					{
						continue;
					}
					int quality = WornQuality.TryGetValue(slot, out int qualitySlot) ? Mathf.Max(1, character.GetInt(qualitySlot, 1)) : 1;
					if (s_wornSeen.Add($"{account.id}|{item}|{quality}"))
					{
						Wears(account, peer.m_uid, item, quality);
					}
				}
			}
		}

		private static void Wears(Account account, long peer, string item, int quality)
		{
			int known = account.quality.TryGetValue(item, out int q) ? q : 1;
			if (Have(account, item) >= 1)
			{
				/*
					Upgraded since it was last seen, at a crafting station with its materials or at the upgrade
					station with upgrader items (free here while those are not tracked). Only reported when
					neither way can be explained; the account is left as it is.
				*/
				if (quality > known && s_recipes.TryGetValue(item, out List<RecipeInfo> recipes))
				{
					List<KeyValuePair<string, int>> MissingFor(Dictionary<string, int> needs)
					{
						return needs.Select(n => new KeyValuePair<string, int>(n.Key, Missing(account, n.Key, n.Value, 1))).Where(m => m.Value > 0).ToList();
					}
					List<KeyValuePair<string, int>> missing = MissingFor(Needs(recipes[0], known + 1, quality, false));
					if (missing.Count > 0 && recipes[0].upgradeStation)
					{
						List<KeyValuePair<string, int>> other = MissingFor(Needs(recipes[0], known + 1, quality, true));
						missing = other.Sum(m => m.Value) < missing.Sum(m => m.Value) ? other : missing;
					}
					foreach (KeyValuePair<string, int> m in missing)
					{
						Report(account, peer, m.Value, m.Key, 1, $"upgraded {item} to quality {quality} with", canTakeAway: false);
					}
				}
				SeenQuality(account, item, quality);
				return;
			}
			if (Make(account, item, 1, quality, 0) >= 1)
			{
				SeenQuality(account, item, quality);
				return;
			}
			Report(account, peer, 1, item, quality, "wears", canTakeAway: false);
			SeenQuality(account, item, quality);
		}

		// ---- Keeping the accounts ------------------------------------------------------------------------------

		private static void MarkDirty()
		{
			if (!s_dirty)
			{
				s_dirty = true;
				s_saveAt = Time.time + 30f;
			}
		}

		/*
			<world>.items.txt next to the world save. A "started" line, then one line per character, tab
			separated: id, exact or unknown, counting since, name, item=amount,..., item@quality,...
		*/
		private static void Load()
		{
			World world = ZNet.World;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(world.m_fileSource), world.m_name + ".items.txt");
			s_started = DateTime.UtcNow;
			if (!File.Exists(s_path))
			{
				MarkDirty();
				return;
			}
			foreach (string line in File.ReadAllLines(s_path))
			{
				string[] f = line.Split('\t');
				if (f[0] == "started" && f.Length > 1)
				{
					s_started = ParseTime(f[1]);
					continue;
				}
				if (line.StartsWith("#", StringComparison.Ordinal) || f.Length < 5 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
				{
					continue;
				}
				Account account = new Account { id = id, exact = f[1] == "exact", since = ParseTime(f[2]), name = f[3] };
				foreach (string pair in f[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string[] kv = pair.Split('=');
					if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
					{
						account.balance[kv[0]] = n;
					}
				}
				if (f.Length > 5)
				{
					foreach (string pair in f[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
					{
						string[] kv = pair.Split('@');
						if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
						{
							account.quality[kv[0]] = n;
						}
					}
				}
				s_accounts[id] = account;
			}
		}

		public static void Save()
		{
			if (s_path == null)
			{
				return;
			}
			s_dirty = false;
			List<string> lines = new List<string>
			{
				"# Sarkastic.eu Dedicated Simulation: item ledger -- what each character got on this world and still has by the count. Tab separated:",
				"# id, exact/unknown (unknown = what it carried when counting began is not known), counting since (UTC), name, item=amount,..., item@highest quality seen,...",
				"started\t" + FormatTime(s_started),
			};
			foreach (Account a in s_accounts.Values.OrderBy(a => a.since))
			{
				lines.Add(string.Join("\t", new[]
				{
					a.id.ToString(CultureInfo.InvariantCulture), a.exact ? "exact" : "unknown", FormatTime(a.since), (a.name ?? "").Replace('\t', ' '),
					string.Join(",", a.balance.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")),
					string.Join(",", a.quality.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}@{kv.Value.ToString(CultureInfo.InvariantCulture)}")),
				}));
			}
			try
			{
				string temp = s_path + ".tmp";
				File.WriteAllLines(temp, lines);
				if (File.Exists(s_path))
				{
					File.Delete(s_path);
				}
				File.Move(temp, s_path);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Item ledger: could not save {s_path}: {e.Message}");
			}
		}

		private static string FormatTime(DateTime t)
		{
			return t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		}

		private static DateTime ParseTime(string s)
		{
			return DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime t) ? t : DateTime.UtcNow;
		}

		// ---- Patches ----------------------------------------------------------------------------------------------

		// Who sent the object data being applied: changes count for that player only.
		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix(ZDOMan __instance, ZRpc rpc)
			{
				s_sender = 0L;
				if (s_ready)
				{
					ZDOMan.ZDOPeer peer = __instance.FindPeer(rpc);
					s_sender = peer?.m_peer?.m_uid ?? 0L;
				}
			}

			static void Finalizer()
			{
				s_sender = 0L;
			}
		}

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static void Prefix(ZDO __instance, out object __state)
			{
				__state = BeforeDeserialize(__instance);
			}

			static void Postfix(ZDO __instance, object __state)
			{
				AfterDeserialize(__instance, __state);
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "RPC_DestroyZDO")]
		public static class ZDOMan_RPC_DestroyZDO_Patch
		{
			// Read the objects before the game deletes them; the package is read again by the game.
			static void Prefix(long sender, ZPackage pkg)
			{
				if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off)
				{
					return;
				}
				Guard(() =>
				{
					ZPackage copy = new ZPackage(pkg.GetArray());
					copy.SetPos(pkg.GetPos());
					int count = copy.ReadInt();
					for (int i = 0; i < count; i++)
					{
						Destroyed(sender, ZDOMan.instance.GetZDO(copy.ReadZDOID()));
					}
				});
			}
		}

		// Accepted ore and fuel are taken back out again rather than refused, so nothing hands them back.
		[HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
		public static class Smelter_RPC_AddOre_Patch
		{
			static void Prefix(Smelter __instance, out int __state)
			{
				__state = __instance.m_nview && __instance.m_nview.IsOwner() ? __instance.GetQueueSize() : -1;
			}

			static void Postfix(Smelter __instance, long sender, string name, int __state)
			{
				if (__state < 0 || __instance.GetQueueSize() != __state + 1)
				{
					return;
				}
				bool undo = false;
				Guard(() => Fed(sender, name, "put into a " + global::Utils.GetPrefabName(__instance.gameObject), ref undo));
				if (undo)
				{
					__instance.m_nview.GetZDO().Set(ZDOVars.s_queued, __state);
				}
			}
		}

		[HarmonyPatch(typeof(Smelter), "RPC_AddFuel")]
		public static class Smelter_RPC_AddFuel_Patch
		{
			static void Prefix(Smelter __instance, out float __state)
			{
				__state = __instance.m_nview && __instance.m_nview.IsOwner() && __instance.m_fuelItem ? __instance.GetFuel() : -1f;
			}

			static void Postfix(Smelter __instance, long sender, float __state)
			{
				if (__state < 0f || __instance.GetFuel() <= __state)
				{
					return;
				}
				bool undo = false;
				Guard(() => Fed(sender, __instance.m_fuelItem.gameObject.name, "fueled a " + global::Utils.GetPrefabName(__instance.gameObject) + " with", ref undo));
				if (undo)
				{
					__instance.SetFuel(__state);
				}
			}
		}

		// The client has already taken the item out of the bag; these are only counted.
		[HarmonyPatch(typeof(CookingStation), "RPC_AddItem")]
		public static class CookingStation_RPC_AddItem_Patch
		{
			static void Postfix(CookingStation __instance, long sender, string itemName)
			{
				if (__instance.m_nview && __instance.m_nview.IsOwner())
				{
					bool ignored = false;
					Guard(() => Fed(sender, itemName, "put on a cooking station", ref ignored));
				}
			}
		}

		[HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
		public static class Fermenter_RPC_AddItem_Patch
		{
			static void Postfix(Fermenter __instance, long sender, int nameHash)
			{
				if (__instance.m_nview && __instance.m_nview.IsOwner() && s_tracked.TryGetValue(nameHash, out string item))
				{
					bool ignored = false;
					Guard(() => Fed(sender, item, "put into a fermenter", ref ignored));
				}
			}
		}

		// Summoning a boss with items from another world: with Mode = On the altar refuses.
		[HarmonyPatch(typeof(OfferingBowl), "RPC_SpawnBoss")]
		public static class OfferingBowl_RPC_SpawnBoss_Patch
		{
			static bool Prefix(OfferingBowl __instance, long senderId, bool removeItemsFromInventory, out bool __state)
			{
				__state = false;
				if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off || !removeItemsFromInventory || !__instance.m_bossItem
					|| !__instance.m_nview || !__instance.m_nview.IsOwner() || __instance.IsBossSpawnQueued())
				{
					return true;
				}
				string item = __instance.m_bossItem.gameObject.name;
				if (!s_tracked.ContainsKey(item.GetStableHashCode()))
				{
					return true;
				}
				Account account = AccountOf(senderId);
				if (account == null)
				{
					return true;
				}
				__state = true;
				if (!Enforcing || !account.exact)
				{
					return true;
				}
				int missing = Missing(account, item, __instance.m_bossItems, 1);
				if (missing <= 0)
				{
					return true;
				}
				__state = false;
				GuardLog.Write($"{account.name} ({account.id}) offered {__instance.m_bossItems} {item} at a boss altar; {missing} never got on this server: the altar refuses");
				ServersidePlugin.logger.LogWarning($"Item ledger: {account.name} tried to summon a boss with {missing} {item} from another world");
				ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "ShowMessage", (int)MessageHud.MessageType.Center,
					string.Format(Configuration.itemLedgerAltarMessage.Value, missing, LocalizedName(item)));
				return false;
			}

			static void Postfix(OfferingBowl __instance, long senderId, bool __state)
			{
				if (!__state || !__instance.IsBossSpawnQueued())
				{
					return;
				}
				Account account = AccountOf(senderId);
				string item = __instance.m_bossItem.gameObject.name;
				Guard(() => Report(account, senderId, Out(account, item, __instance.m_bossItems, 1, "summoned a boss"), item, 1, "summoned a boss with", canTakeAway: false));
			}
		}

		[HarmonyPatch(typeof(ZNet), "Disconnect")]
		public static class ZNet_Disconnect_Patch
		{
			static void Prefix()
			{
				if (s_dirty)
				{
					Save();
				}
			}
		}

		// The accounts are saved with the world, so after a crash both come back from the same moment.
		[HarmonyPatch(typeof(ZNet), "SaveWorld")]
		public static class ZNet_SaveWorld_Patch
		{
			static void Postfix()
			{
				if (s_ready && Configuration.itemLedgerMode.Value != Mode.Off)
				{
					Save();
				}
			}
		}
	}

	// The character guard's and the item ledger's own log, next to the world save: <world>.guard.log.
	public static class GuardLog
	{
		private static string s_path;

		public static void Write(string line)
		{
			try
			{
				if (s_path == null)
				{
					if (ZNet.World == null)
					{
						return;
					}
					s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".guard.log");
				}
				File.AppendAllText(s_path, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z' ", CultureInfo.InvariantCulture) + line + Environment.NewLine);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Guard log: {e.Message}");
			}
		}
	}
}
