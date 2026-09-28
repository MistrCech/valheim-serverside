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
		An account, per character, of the valuable items it got on this server and the ones it put back
		into the world. What a character carries is invisible to the server, but nearly every way to get
		an item passes through an object the server sees:
		- in: picking an item up off the ground (the client takes the item over and deletes it: a
		  DestroyZDO from that player), taking it out of a chest, a cart, a ship or a tombstone (the
		  container's contents, sent by the player who has it open, lose it);
		- out: dropping it (a new item on the ground from that player), putting it into a container,
		  feeding it to a smelter, kiln or refinery (RPC_AddOre, which runs on the server that owns it),
		  building with it (a new piece with that player as its creator), and dying (the tombstone holds
		  the whole bag).
		Crafting is not seen; a tracked item made from tracked items (bronze from copper and tin) is
		counted as made from what the character had. What traders sell is not tracked.

		When a character puts more of an item into the world than it ever got here -- and could have
		made -- the rest came from outside this world. That is written to the guard log and, with
		Mode = On, taken away: a dropped stack is cut down, a container loses it once nobody has it open,
		a smelter refuses it (the client has already taken it out of the bag). Built pieces are only
		logged.

		The account starts at zero only for a character that is new and fresh here (see CharacterGuard);
		then it is exact. For a character that existed before, what it carried when counting began is
		unknown; its account becomes exact the first time it dies, when the tombstone shows the whole
		bag. Until then its findings are logged as unverified and never acted on.
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
		}

		private class Recipe
		{
			public int amount;
			public List<KeyValuePair<string, int>> ingredients;
		}

		private struct Confiscation
		{
			public ZDOID container;
			public string item;
			public int amount;
			public long peer;
			public float since;
		}

		private static bool s_ready;
		private static string s_path;
		private static bool s_dirty;
		private static float s_saveAt;
		private static readonly Dictionary<long, Account> s_accounts = new Dictionary<long, Account>();
		private static readonly Dictionary<int, string> s_tracked = new Dictionary<int, string>();
		private static readonly Dictionary<string, string> s_sharedNames = new Dictionary<string, string>(StringComparer.Ordinal);
		private static readonly HashSet<int> s_containers = new HashSet<int>();
		private static readonly Dictionary<int, Vector2i> s_containerSizes = new Dictionary<int, Vector2i>();
		private static readonly Dictionary<int, List<KeyValuePair<string, int>>> s_pieceCosts = new Dictionary<int, List<KeyValuePair<string, int>>>();
		// Smallest batch first: bronze has a recipe for one bar and one for five.
		private static readonly Dictionary<string, List<Recipe>> s_recipes = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
		private static readonly List<Confiscation> s_pending = new List<Confiscation>();
		private static int s_tombstone;

		private static bool Enforcing => Configuration.itemLedgerMode.Value == Mode.On;

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (Configuration.itemLedgerMode.Value == Mode.Off || !ZNet.instance || !ZNet.instance.IsServer() || ZNet.World == null
				|| ZDOMan.instance == null || !ObjectDB.instance || !ZNetScene.instance)
			{
				return;
			}
			if (!s_ready)
			{
				Setup();
			}
			CharacterGuard.EnsureFootprint();
			for (int i = s_pending.Count - 1; i >= 0; i--)
			{
				if (TryConfiscateFromContainer(s_pending[i]) || Time.time - s_pending[i].since > 600f)
				{
					s_pending.RemoveAt(i);
				}
			}
			if (s_dirty && Time.time >= s_saveAt)
			{
				Save();
			}
		}

		private static void Setup()
		{
			s_ready = true;
			List<string> unknown = new List<string>();
			foreach (string name in Configuration.itemLedgerItems.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
			{
				GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
				ItemDrop drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
				if (!drop)
				{
					unknown.Add(name);
					continue;
				}
				s_tracked[name.GetStableHashCode()] = name;
				s_sharedNames[name] = drop.m_itemData.m_shared.m_name;
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
						if (s_tracked.Remove(item.m_prefab.gameObject.name.GetStableHashCode()))
						{
							sold.Add(item.m_prefab.gameObject.name);
						}
					}
				}
			}
			s_tombstone = "Player_tombstone".GetStableHashCode();
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (!prefab)
				{
					continue;
				}
				int hash = prefab.name.GetStableHashCode();
				Container container = prefab.GetComponentInChildren<Container>(true);
				if (container)
				{
					s_containers.Add(hash);
					s_containerSizes[hash] = new Vector2i(Mathf.Max(8, container.m_width), Mathf.Max(8, container.m_height));
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
			// Tracked items a character can make from tracked items only, e.g. bronze.
			foreach (global::Recipe recipe in ObjectDB.instance.m_recipes)
			{
				if (!recipe || !recipe.m_enabled || !recipe.m_item || !s_tracked.ContainsKey(recipe.m_item.gameObject.name.GetStableHashCode()))
				{
					continue;
				}
				List<KeyValuePair<string, int>> ingredients = recipe.m_resources
					.Where(r => r.m_resItem && r.m_amount > 0)
					.Select(r => new KeyValuePair<string, int>(r.m_resItem.gameObject.name, r.m_amount)).ToList();
				if (ingredients.Count > 0 && ingredients.All(r => s_tracked.ContainsKey(r.Key.GetStableHashCode())))
				{
					string made = recipe.m_item.gameObject.name;
					if (!s_recipes.TryGetValue(made, out List<Recipe> list))
					{
						s_recipes[made] = list = new List<Recipe>();
					}
					list.Add(new Recipe { amount = Mathf.Max(1, recipe.m_amount), ingredients = ingredients });
					list.Sort((a, b) => a.amount.CompareTo(b.amount));
				}
			}
			Load();
			string summary = $"Item ledger: {Configuration.itemLedgerMode.Value}. Tracking {s_tracked.Count} item(s): {string.Join(", ", s_tracked.Values.OrderBy(n => n))}; "
				+ $"{s_accounts.Count} account(s) in {Path.GetFileName(s_path)}; {s_pieceCosts.Count} kind(s) of piece built with them; made from tracked items: {(s_recipes.Count > 0 ? string.Join(", ", s_recipes.Keys) : "none")}."
				+ (unknown.Count > 0 ? $" Unknown item names ignored: {string.Join(", ", unknown)}." : "")
				+ (sold.Count > 0 ? $" Sold by traders, not tracked: {string.Join(", ", sold)}." : "");
			ServersidePlugin.logger.LogInfo(summary);
			GuardLog.Write(summary);
		}

		// The account of the character this peer plays; created on the first thing it does.
		private static Account AccountOf(long peerUid)
		{
			ZNetPeer peer = ZNet.instance.GetPeer(peerUid);
			if (peer == null || peer.m_characterID.IsNone())
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
				bool fresh = CharacterGuard.IsBrandNew(peer, character, id);
				s_accounts[id] = account = new Account { id = id, name = peer.m_playerName, exact = fresh, since = DateTime.UtcNow };
				GuardLog.Write($"{peer.m_playerName} ({id}): account opened, "
					+ (fresh ? "exact from zero (a new, fresh character)" : "what it carries now is unknown; exact from its first death here"));
				MarkDirty();
			}
			account.name = peer.m_playerName;
			return account;
		}

		private static Account AccountOfCharacter(long id)
		{
			return s_accounts.TryGetValue(id, out Account account) ? account : null;
		}

		private static void In(Account account, string item, int amount, string how)
		{
			if (account == null || amount <= 0)
			{
				return;
			}
			account.balance[item] = Have(account, item) + amount;
			MarkDirty();
			if (Configuration.itemLedgerLogAll.Value)
			{
				GuardLog.Write($"{account.name}: +{amount} {item} ({how}), has {account.balance[item]}");
			}
		}

		/*
			Takes an amount out of the account; returns how much of it the character never got here
			(nor could make from what it got). The account never goes below zero.
		*/
		private static int Out(Account account, string item, int amount, string how)
		{
			int have = Have(account, item);
			int taken = Mathf.Min(have, amount);
			account.balance[item] = have - taken;
			int missing = amount - taken;
			if (missing > 0 && s_recipes.TryGetValue(item, out List<Recipe> recipes))
			{
				foreach (Recipe recipe in recipes)
				{
					if (missing <= 0)
					{
						break;
					}
					int crafts = recipe.ingredients.Min(r => Have(account, r.Key) / r.Value);
					int needed = Mathf.Min(crafts, (missing + recipe.amount - 1) / recipe.amount);
					foreach (KeyValuePair<string, int> r in recipe.ingredients)
					{
						account.balance[r.Key] = Have(account, r.Key) - needed * r.Value;
					}
					int made = needed * recipe.amount;
					account.balance[item] = Have(account, item) + Mathf.Max(0, made - missing);
					missing = Mathf.Max(0, missing - made);
				}
			}
			MarkDirty();
			if (Configuration.itemLedgerLogAll.Value)
			{
				GuardLog.Write($"{account.name}: -{amount} {item} ({how}), has {Have(account, item)}{(missing > 0 ? $", {missing} unexplained" : "")}");
			}
			return missing;
		}

		private static int Have(Account account, string item)
		{
			return account.balance.TryGetValue(item, out int n) ? n : 0;
		}

		// Returns whether it is to be taken away. What was built with cannot be; that is only logged.
		private static bool Report(Account account, long peerUid, int missing, string item, string how, bool canTakeAway = true)
		{
			if (missing <= 0)
			{
				return false;
			}
			bool act = Enforcing && account.exact && canTakeAway;
			string what = $"{account.name} ({account.id}) {how} {missing} {item} it never got on this server"
				+ (account.exact ? "" : " (unverified: what it carried before counting began is unknown)")
				+ (act ? ": taken away" : "");
			GuardLog.Write(what);
			ServersidePlugin.logger.LogWarning("Item ledger: " + what);
			if (act && peerUid != 0L)
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, "ShowMessage", (int)MessageHud.MessageType.Center,
					string.Format(Configuration.itemLedgerMessage.Value, missing, item));
			}
			return act;
		}

		// Incoming data from a player: new objects it made and containers it changed.
		public static void BeforeDeserialize(ZDO zdo, out object state)
		{
			state = null;
			if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off)
			{
				return;
			}
			int prefab = zdo.GetPrefab();
			if (prefab == 0)
			{
				state = true;  // a new object
			}
			else if (s_containers.Contains(prefab))
			{
				state = zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0];
			}
		}

		public static void AfterDeserialize(ZDO zdo, object state)
		{
			if (state == null)
			{
				return;
			}
			try
			{
				long owner = zdo.GetOwner();
				if (owner == 0L || owner == ZDOMan.GetSessionID() || ZNet.instance.GetPeer(owner) == null)
				{
					return;
				}
				int prefab = zdo.GetPrefab();
				if (state is bool)
				{
					NewObject(zdo, prefab, owner);
				}
				else if (state is byte[] before)
				{
					byte[] after = zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0];
					if (!ReferenceEquals(before, after) && !before.SequenceEqual(after))
					{
						ContainerChanged(zdo, prefab, owner, before, after);
					}
				}
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Item ledger: could not read an incoming object: {e.Message}");
			}
		}

		private static void NewObject(ZDO zdo, int prefab, long owner)
		{
			if (s_tracked.TryGetValue(prefab, out string item))
			{
				Account account = AccountOf(owner);
				if (account == null)
				{
					return;
				}
				int stack = zdo.GetInt(ZDOVars.s_stack, 1);
				int missing = Out(account, item, stack, "dropped");
				if (Report(account, owner, missing, item, "dropped"))
				{
					zdo.SetOwner(ZDOMan.GetSessionID());
					if (missing >= stack)
					{
						ZDOMan.instance.DestroyZDO(zdo);
					}
					else
					{
						zdo.Set(ZDOVars.s_stack, stack - missing);
					}
				}
			}
			else if (prefab == s_tombstone)
			{
				Died(zdo, owner);
			}
			else if (s_pieceCosts.TryGetValue(prefab, out List<KeyValuePair<string, int>> cost))
			{
				Account account = AccountOf(owner);
				if (account == null || zdo.GetLong(ZDOVars.s_creator, 0L) != account.id)
				{
					return;
				}
				foreach (KeyValuePair<string, int> c in cost)
				{
					Report(account, 0L, Out(account, c.Key, c.Value, "built"), c.Key, "built with", canTakeAway: false);
				}
			}
		}

		/*
			A tombstone is the whole bag: every tracked item in it must be accounted for, and after the
			death the bag is empty. Taking the items back out of the tombstone counts them in again.
		*/
		private static void Died(ZDO tombstone, long owner)
		{
			Account account = AccountOf(owner);
			if (account == null || tombstone.GetLong(ZDOVars.s_owner, 0L) != account.id)
			{
				return;
			}
			Dictionary<string, int> bag = Contents(tombstone.GetByteArray(ZDOVars.s_items), tombstone.GetPrefab());
			bool whole = !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory);
			// A character whose account is not exact yet is only being counted from now; its bag is not held against it.
			if (account.exact)
			{
				foreach (KeyValuePair<string, int> item in bag)
				{
					int missing = Out(account, item.Key, item.Value, "died carrying");
					if (Report(account, owner, missing, item.Key, "died carrying"))
					{
						s_pending.Add(new Confiscation { container = tombstone.m_uid, item = item.Key, amount = missing, peer = owner, since = Time.time });
					}
				}
			}
			if (whole)
			{
				foreach (string item in account.balance.Keys.ToList())
				{
					account.balance[item] = 0;
				}
				if (!account.exact)
				{
					account.exact = true;
					GuardLog.Write($"{account.name} ({account.id}) died: the tombstone showed the whole bag, the account is exact from now on");
				}
				MarkDirty();
			}
		}

		private static void ContainerChanged(ZDO zdo, int prefab, long owner, byte[] before, byte[] after)
		{
			Dictionary<string, int> was = Contents(before, prefab);
			Dictionary<string, int> now = Contents(after, prefab);
			Account account = null;
			foreach (string item in was.Keys.Union(now.Keys))
			{
				int delta = (now.TryGetValue(item, out int n) ? n : 0) - (was.TryGetValue(item, out int w) ? w : 0);
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
					In(account, item, -delta, "took from a container");
				}
				else
				{
					int missing = Out(account, item, delta, "put into a container");
					if (Report(account, owner, missing, item, "put into a container"))
					{
						s_pending.Add(new Confiscation { container = zdo.m_uid, item = item, amount = missing, peer = owner, since = Time.time });
					}
				}
			}
		}

		// Tracked items in a saved inventory (Inventory.Save), by item prefab name.
		private static Dictionary<string, int> Contents(byte[] data, int prefab)
		{
			Dictionary<string, int> items = new Dictionary<string, int>(StringComparer.Ordinal);
			if (data == null || data.Length == 0)
			{
				return items;
			}
			Vector2i size = s_containerSizes.TryGetValue(prefab, out Vector2i s) ? s : new Vector2i(8, 8);
			Inventory inventory = new Inventory("ledger", null, size.x, size.y);
			inventory.Load(new ZPackage(data));
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				string name = item.m_dropPrefab ? item.m_dropPrefab.name : null;
				if (name != null && s_tracked.ContainsKey(name.GetStableHashCode()))
				{
					items[name] = (items.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
				}
			}
			return items;
		}

		// Removes the items once nobody has the container open; the server owns it for that.
		private static bool TryConfiscateFromContainer(Confiscation c)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(c.container);
			if (zdo == null)
			{
				return true;
			}
			if (zdo.GetInt(ZDOVars.s_inUse, 0) != 0)
			{
				return false;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			Vector2i size = s_containerSizes.TryGetValue(zdo.GetPrefab(), out Vector2i s) ? s : new Vector2i(8, 8);
			Inventory inventory = new Inventory("ledger", null, size.x, size.y);
			inventory.Load(new ZPackage(zdo.GetByteArray(ZDOVars.s_items) ?? new byte[0]));
			int had = inventory.CountItems(s_sharedNames[c.item]);
			inventory.RemoveItem(s_sharedNames[c.item], Mathf.Min(had, c.amount));
			ZPackage package = new ZPackage();
			inventory.Save(package);
			zdo.Set(ZDOVars.s_items, package.GetArray());
			GuardLog.Write($"took {Mathf.Min(had, c.amount)} {c.item} out of the container at {zdo.GetPosition():F0}");
			return true;
		}

		public static void Smelting(long sender, string item, ref bool runOriginal)
		{
			if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off || item == null || !s_tracked.ContainsKey(item.GetStableHashCode())
				|| sender == ZDOMan.GetSessionID())
			{
				return;
			}
			Account account = AccountOf(sender);
			if (account == null)
			{
				return;
			}
			int missing = Out(account, item, 1, "put into a smelter");
			if (Report(account, sender, missing, item, "put into a smelter"))
			{
				// The client already took it out of the bag; not adding it takes it away.
				runOriginal = false;
			}
		}

		// Picked up: the player took the item over and deleted it.
		public static void Destroyed(long sender, ZDO zdo)
		{
			if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off || zdo == null || sender == ZDOMan.GetSessionID()
				|| !s_tracked.TryGetValue(zdo.GetPrefab(), out string item))
			{
				return;
			}
			In(AccountOf(sender), item, zdo.GetInt(ZDOVars.s_stack, 1), "picked up");
		}

		private static void MarkDirty()
		{
			if (!s_dirty)
			{
				s_dirty = true;
				s_saveAt = Time.time + 30f;
			}
		}

		/*
			<world>.items.txt next to the world save, one line per character, tab separated:
			id, exact or unknown, counting since, name, item=amount,...
		*/
		private static void Load()
		{
			World world = ZNet.World;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(world.m_fileSource), world.m_name + ".items.txt");
			if (!File.Exists(s_path))
			{
				return;
			}
			foreach (string line in File.ReadAllLines(s_path))
			{
				string[] f = line.Split('\t');
				if (line.StartsWith("#", StringComparison.Ordinal) || f.Length < 5 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
				{
					continue;
				}
				Account account = new Account
				{
					id = id,
					exact = f[1] == "exact",
					since = DateTime.TryParse(f[2], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t) ? t : DateTime.UtcNow,
					name = f[3],
				};
				foreach (string pair in f[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string[] kv = pair.Split('=');
					if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
					{
						account.balance[kv[0]] = n;
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
				"# Sarkastic.eu Dedicated Simulation: item ledger, what each character got on this world and still has by the count. Tab separated:",
				"# id, exact/unknown (unknown = what it carried when counting began is not known), counting since (UTC), name, item=amount,...",
			};
			foreach (Account a in s_accounts.Values.OrderBy(a => a.since))
			{
				lines.Add(string.Join("\t", new[]
				{
					a.id.ToString(CultureInfo.InvariantCulture), a.exact ? "exact" : "unknown", a.since.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
					(a.name ?? "").Replace('\t', ' '),
					string.Join(",", a.balance.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")),
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

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static void Prefix(ZDO __instance, out object __state)
			{
				BeforeDeserialize(__instance, out __state);
			}

			static void Postfix(ZDO __instance, object __state)
			{
				AfterDeserialize(__instance, __state);
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "RPC_DestroyZDO")]
		public static class ZDOMan_RPC_DestroyZDO_Patch
		{
			// Read the ids before the game deletes the objects; the package is read again by the game.
			static void Prefix(long sender, ZPackage pkg)
			{
				if (!s_ready || Configuration.itemLedgerMode.Value == Mode.Off)
				{
					return;
				}
				ZPackage copy = new ZPackage(pkg.GetArray());
				copy.SetPos(pkg.GetPos());
				int count = copy.ReadInt();
				for (int i = 0; i < count; i++)
				{
					Destroyed(sender, ZDOMan.instance.GetZDO(copy.ReadZDOID()));
				}
			}
		}

		[HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
		public static class Smelter_RPC_AddOre_Patch
		{
			static bool Prefix(Smelter __instance, long sender, string name)
			{
				bool run = true;
				if (__instance.m_nview && __instance.m_nview.IsOwner())
				{
					Smelting(sender, name, ref run);
				}
				return run;
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
	}

	// The character guard's own log, next to the world save: <world>.guard.log.
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
