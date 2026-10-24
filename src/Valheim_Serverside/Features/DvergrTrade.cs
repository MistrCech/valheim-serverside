using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		The dvergr of the Mistlands are friends, and killing them for what their camps hold feels bad. This lets a player
		buy from them what they need, and have their ward switched off, for the trophies of the seekers and the other
		monsters of the same land, not coins. A vanilla game cannot show a trade window (Trader.m_items live in the
		client), so the trade is a chest: whatever the player puts into it and closes is weighed by the server, which
		takes the price and leaves the goods in its place. Nothing is installed on the players' side.

		The camps: each of eight locations (the three guard towers, the lighthouse, the two excavations and the harbour of the
		Mistlands, the charred tower of the Ashlands) holds exactly one dverger_guardstone, a ward of the dvergr's own
		(PrivateArea, radius 32, faction Dverger). When a player comes within StandRange of one, the server puts up a stand
		next to it: one dvergr chest (TreasureChest_dvergrtown) and one sign with the prices. Both are made as the game
		makes a new object (a ZDO, then the game instantiates it) and carry a marker of this mod in their data (sgg_trade:
		1 chest, 2 sign), by which they are found again after a restart (ZDOIDs are renumbered on load and are never
		stored). A stand is put up only when the camp is really loaded on the server -- the ward and every persistent object
		within 12 m of it have an instance; MaxObjectsPerFrame loads a camp over several frames and a wall that is not there
		yet must not be built through. The spot is found with physics (the server has the colliders of what is loaded): the
		first of 16 directions at 2.5 to 5 m where the ground is level and near the ward's height, and the chest and the sign
		beside it fit. A chest or a sign someone has destroyed is put up again after ten minutes. Switching the feature
		off puts up nothing new and takes nothing down.

		The trade: a chest that a player has opened belongs to that player's game until the server takes it back (the game
		gives the owner of an opened chest to the player and nothing ever returns it while he stays in the area; Core only
		releases what its owner left). So every second, for the stands near a player, a chest that is not in use (the game
		keeps InUse at 1 while it is open) is taken back by the server, and when its data revision is not the one last
		processed, what it holds is read: the offers are paid in the order of the list, each as many times as it is paid in
		full, the price taken out, the goods put in, and the result written back (a client reads it again when the revision
		changes). The chest is taken back only after it has stayed with the player's game for a moment: the game's
		"stack all" and "take all" hand the chest over without InUse, and the player's game writes into it a moment
		later, which a server that took it straight back would cut off. It is weighed a tick after it was taken back: a
		player's game that is still the owner grants itself an open without asking anyone, and if that crosses the server
		taking it back, the game's newer data wins on the server with its older owner revision while the game itself takes
		the server's; the server sees that revision go down, weighs nothing and gives the chest back to that game. After the
		server has written a chest, nobody may open it for a moment ("in use"): a game loads a chest's contents once a
		second and never while it is open, so one opened before it has loaded the server's write would show the old
		contents and save them back. Goods that do not fit undo that trade and the item offers after it (an offer of the
		ward is still weighed). The item ledger books what a player takes out of the chest in the usual way (it ignores
		the server's own writes). The chest carries s_addedDefaultItems (so the game does not fill it with the camp's
		loot) and s_cheated = 1 (Resource Regrowth's treasure chests never restock a chest whose s_cheated is not 0; the
		game marks what an object drops as cheated only at 2, so the wood of a broken chest is clean).

		The ward: "Ward:<hours>" as the goods. The ward gets a time in its data (sgg_ward_off_until, ZNet time), is switched
		off, and every dvergr near it that was angry is calmed (nothing in the game calms them again). The game switches a
		dvergr ward back on whenever its area is made (PrivateArea.Awake: enabledByDefault, on the owner), so it is kept
		off by the tick every two seconds and by a patch on that Awake, and put back on when the time is up. While it is off,
		the one thing that would anger the camp for taking what lies in it -- picking a lantern or a stein (the "thief"
		reason of BaseAI.SetAggravated) -- is not passed on to the dvergr. Hitting a dvergr still angers them (and
		paying again calms them). Damaging a thing inside a ward that is switched off angers nobody: the game's own rule.
		The ward itself takes no damage while it is off, or it could be broken without a fight (and the camp would lose its
		stand). Paid hours are added to what is left, and an offer of the ward is taken only while all its hours fit
		within seven days from now; otherwise its trophies stay in the chest and the player is told (MessageWardMax). The
		time is the server's (ZNet), which stands still while nobody is online and jumps when the players sleep, as the
		game's own timers do.

		Not here: coins, a trade window, prices per camp, taking the stands down when the feature is switched off.
	*/
	public class DvergrTrade : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.dvergrTradeEnabled.Value;
		}

		private const string WardPrefab = "dverger_guardstone";
		private const string ChestPrefab = "TreasureChest_dvergrtown";
		private const string SignPrefab = "sign";
		// A chest that stayed with a player's game this long (seconds) while not in use goes back to the server.
		private const float SettleSeconds = 1.5f;
		// After a chest has been opened, how long the player's game has to set InUse before the server may take it back.
		private const float OpenGraceSeconds = 5f;
		private const float RetrySeconds = 600f;
		private const float WardCapDays = 7f;
		// After the server has written a chest: a player's game loads it at most a second after the data arrives.
		private const float WriteLockSeconds = 2.5f;
		private const float SignOffset = 1.3f;
		// The sign's pivot is the middle of its board (its colliders reach 0.28 m below and above it): lifted by a little less
		// than half its height it stands on the ground, where the game's support check (0.15 m around each collider) finds
		// the terrain. The chest's pivot is its bottom.
		private const float SignLift = 0.26f;

		private static readonly int s_keyTrade = "sgg_trade".GetStableHashCode();
		private static readonly int s_keyOff = "sgg_ward_off_until".GetStableHashCode();

		private class Offer
		{
			public string[] priceNames;
			public int[] priceCounts;
			public string rewardName;
			public int rewardCount;
			// More than 0: the reward is the ward switched off for this many hours.
			public double hours;
		}

		private class Stand
		{
			public ZDOID ward;
			public ZDOID chest = ZDOID.None;
			public ZDOID sign = ZDOID.None;
			public string location;
			public bool madeBefore;
			public bool noSpotLogged;
			public bool waitLogged;
			public float missingSince;
			public float retryAt;
			public float nextSearch;
			public float waitingSince;
		}

		private class ChestState
		{
			public bool hasDone;
			public uint done;
			public long lastUser;
			public float ownerSince;
			public float openUntil;
			// Nobody may open it until then (the server has just written it).
			public float lockedUntil;
			// Taken back from a player's game at this time (0: not), with this owner revision; weighed a tick later.
			public float takenAt;
			public ushort takenRevision;
		}

		private static bool s_ready;
		private static bool s_broken;
		private static int s_wardHash;
		private static int s_chestHash;
		private static int s_signHash;
		private static int s_chestWidth = 3;
		private static int s_chestHeight = 2;
		private static float s_wardRadius = 32f;
		private static float s_campRadius = 64f;
		private static readonly HashSet<int> s_dvergr = new HashSet<int>();

		private static List<Offer> s_offers = new List<Offer>();
		private static string s_offersFrom;
		private static int s_offersVersion;

		private static readonly List<ZDOID> s_wardIds = new List<ZDOID>();
		private static readonly List<ZDOID> s_scanIds = new List<ZDOID>();
		private static readonly List<ZDO> s_scanList = new List<ZDO>();
		private static readonly HashSet<ZDOID> s_scanSeen = new HashSet<ZDOID>();
		private static int s_scanIndex;
		private static bool s_scanning;
		private static float s_nextScan;

		private static readonly Dictionary<ZDOID, Stand> s_stands = new Dictionary<ZDOID, Stand>();
		private static readonly Dictionary<ZDOID, ChestState> s_chests = new Dictionary<ZDOID, ChestState>();
		// The wards switched off by the key in their data: ward -> position. Read by the aggravation patch on every call.
		private static readonly Dictionary<ZDOID, Vector3> s_off = new Dictionary<ZDOID, Vector3>();
		private static readonly List<ZDOID> s_offIds = new List<ZDOID>();

		private static readonly List<Vector3> s_peerPositions = new List<Vector3>();
		private static readonly List<ZDO> s_near = new List<ZDO>();
		private static readonly List<ItemDrop.ItemData> s_items = new List<ItemDrop.ItemData>();
		private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
		private static readonly Dictionary<string, string> s_names = new Dictionary<string, string>();
		private static string s_namesLanguage = "";
		private static Dictionary<string, string> s_signWords;
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		private static string s_signKey;
		private static string s_signText;
		private static float s_nextWardTick;
		private static float s_nextStandTick;
		private static int s_errors;
		private static float s_errorsSince;
		private static int s_rayMask = -1;
		private static int s_boxMask = -1;
		private static readonly Vector3 s_chestHalf = new Vector3(0.9f, 0.5f, 0.7f);
		private static readonly Vector3 s_signHalf = new Vector3(0.45f, 0.6f, 0.15f);
		private static readonly float[] s_distances = { 2.5f, 3f, 3.5f, 4f, 5f };

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
			{
				return;
			}
			bool enabled = Configuration.dvergrTradeEnabled.Value;
			// Switched off in the configuration, the wards that are off still come back on at their time.
			if (!enabled && s_off.Count == 0)
			{
				return;
			}
			if (!Ready())
			{
				return;
			}
			if (enabled)
			{
				ScanStep();
			}
			float now = Time.time;
			if (now >= s_nextWardTick)
			{
				s_nextWardTick = now + 2f;
				WardTick();
			}
			if (enabled && now >= s_nextStandTick)
			{
				s_nextStandTick = now + 1f;
				StandTick();
			}
		}

		// What the prefabs say: the ward's radius, the chest's size, which creatures are the dvergr's.
		private static bool Ready()
		{
			if (s_broken)
			{
				return false;
			}
			if (s_ready)
			{
				return true;
			}
			if (!ZNetScene.instance || ZNetScene.instance.m_prefabs == null || ZNetScene.instance.m_prefabs.Count == 0
				|| ObjectDB.instance == null || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0)
			{
				return false;
			}
			GameObject ward = ZNetScene.instance.GetPrefab(WardPrefab);
			GameObject chest = ZNetScene.instance.GetPrefab(ChestPrefab);
			GameObject sign = ZNetScene.instance.GetPrefab(SignPrefab);
			Container container = chest ? chest.GetComponentInChildren<Container>(true) : null;
			PrivateArea area = ward ? ward.GetComponent<PrivateArea>() : null;
			if (!ward || !chest || !sign || !container || !area || !ward.GetComponent<ZNetView>() || !chest.GetComponent<ZNetView>() || !sign.GetComponent<ZNetView>())
			{
				s_broken = true;
				ServersidePlugin.logger.LogError($"DvergrTrade: the game has no {WardPrefab}, {ChestPrefab} or {SignPrefab} as expected (ward {(bool)ward}, chest {(bool)chest}, its container {(bool)container}, sign {(bool)sign}); the feature is off");
				return false;
			}
			s_wardHash = WardPrefab.GetStableHashCode();
			s_chestHash = ChestPrefab.GetStableHashCode();
			s_signHash = SignPrefab.GetStableHashCode();
			s_chestWidth = container.m_width;
			s_chestHeight = container.m_height;
			s_wardRadius = area.m_radius;
			s_campRadius = 2f * area.m_radius;
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (!prefab)
				{
					continue;
				}
				Character character = prefab.GetComponent<Character>();
				if (character && character.m_faction == Character.Faction.Dverger)
				{
					s_dvergr.Add(prefab.name.GetStableHashCode());
				}
			}
			s_ready = true;
			ServersidePlugin.logger.LogInfo($"DvergrTrade: ward {WardPrefab} {s_wardRadius:0.#} m, chest {ChestPrefab} {s_chestWidth}x{s_chestHeight}, {s_dvergr.Count} kind(s) of dvergr creature are calmed with the ward");
			return true;
		}

		// ---- Offers -------------------------------------------------------------------------------------------------

		// "TrophySeeker:3 > BlackMarble:20, TrophySeekerBrute:1 > Ward:24" -> the offers; read again when the configured text changes.
		private static void RefreshOffers()
		{
			string text = Configuration.dvergrTradeOffers.Value ?? "";
			if (s_offersFrom == text)
			{
				return;
			}
			s_offersFrom = text;
			s_offersVersion++;
			List<Offer> offers = new List<Offer>();
			HashSet<string> priced = new HashSet<string>();
			foreach (string raw in text.Split(','))
			{
				string entry = raw.Trim();
				if (entry.Length == 0)
				{
					continue;
				}
				int arrow = entry.IndexOf('>');
				if (arrow < 0)
				{
					WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}' has no '>' between its price and its reward; skipped");
					continue;
				}
				Offer offer = new Offer();
				List<string> names = new List<string>();
				List<int> counts = new List<int>();
				bool valid = true;
				foreach (string part in entry.Substring(0, arrow).Split('+'))
				{
					if (!SplitCount(part, out string name, out int count) || !IsItem(name))
					{
						WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}': '{part.Trim()}' is not an item with a count; skipped");
						valid = false;
						break;
					}
					int at = names.IndexOf(name);
					if (at >= 0)
					{
						counts[at] += count;
					}
					else
					{
						names.Add(name);
						counts.Add(count);
					}
				}
				if (!valid)
				{
					continue;
				}
				string reward = entry.Substring(arrow + 1).Trim();
				if (!SplitCount(reward, out string rewardName, out int rewardCount, true))
				{
					WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}': the reward '{reward}' is not an item with a count, nor Ward:<hours>; skipped");
					continue;
				}
				if (string.Equals(rewardName, "Ward", StringComparison.OrdinalIgnoreCase))
				{
					int colon = reward.LastIndexOf(':');
					if (colon < 0 || !double.TryParse(reward.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) || hours <= 0d || double.IsInfinity(hours))
					{
						WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}': Ward needs hours above 0 (Ward:24, Ward:1.5); skipped");
						continue;
					}
					offer.hours = hours;
				}
				else if (!IsItem(rewardName))
				{
					WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}': the game has no item '{rewardName}'; skipped");
					continue;
				}
				offer.priceNames = names.ToArray();
				offer.priceCounts = counts.ToArray();
				offer.rewardName = rewardName;
				offer.rewardCount = rewardCount;
				// One price item belongs to one offer: with two that take the same item, which of them is paid would depend on the count.
				string shared = null;
				foreach (string name in offer.priceNames)
				{
					if (priced.Contains(name))
					{
						shared = name;
						break;
					}
				}
				if (shared != null)
				{
					WarnOnce("offer/" + entry, $"DvergrTrade: the offer '{entry}' takes {shared}, which an offer before it already takes; the first in the list wins, this one is skipped");
					continue;
				}
				foreach (string name in offer.priceNames)
				{
					priced.Add(name);
				}
				offers.Add(offer);
			}
			s_offers = offers;
			ServersidePlugin.logger.LogInfo($"DvergrTrade: {offers.Count} offer(s): {string.Join("; ", offers.ConvertAll(o => DescribePlain(o)))}");
		}

		// "Name:count" or just "Name" (count 1). With allowWard the name may be Ward and the count is not read here.
		private static bool SplitCount(string part, out string name, out int count, bool allowWard = false)
		{
			name = null;
			count = 1;
			string trimmed = part.Trim();
			int colon = trimmed.LastIndexOf(':');
			name = colon < 0 ? trimmed : trimmed.Substring(0, colon).Trim();
			if (name.Length == 0)
			{
				return false;
			}
			if (allowWard && string.Equals(name, "Ward", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (colon >= 0 && (!int.TryParse(trimmed.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1 || count > 10000))
			{
				return false;
			}
			return true;
		}

		private static bool IsItem(string prefab)
		{
			GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
			return item && item.GetComponent<ItemDrop>();
		}

		private static string Token(string prefab)
		{
			GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
			return item ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : prefab;
		}

		// The item's name for the text of the sign: in SignLanguage, else as the server's language has it (English on a
		// dedicated server).
		private static string ServerName(string prefab)
		{
			string language = (Configuration.dvergrTradeSignLanguage.Value ?? "").Trim();
			if (language != s_namesLanguage)
			{
				s_names.Clear();
				s_signWords = language.Length > 0 ? Words(language) : null;
				s_namesLanguage = language;
			}
			if (s_names.TryGetValue(prefab, out string cached))
			{
				return cached;
			}
			string token = Token(prefab);
			string name = null;
			if (s_signWords != null && token.StartsWith("$", StringComparison.Ordinal))
			{
				s_signWords.TryGetValue(token.Substring(1), out name);
			}
			try
			{
				name = string.IsNullOrEmpty(name) && Localization.instance != null ? Localization.instance.Localize(token) : name;
			}
			catch (Exception)
			{
				// no localization on this server: the fallback below
			}
			if (string.IsNullOrEmpty(name) || name == token || name[0] == '[' || name[0] == '$')
			{
				name = token.StartsWith("$item_", StringComparison.Ordinal) ? token.Substring(6).Replace('_', ' ') : prefab;
			}
			s_names[prefab] = name;
			return name;
		}

		// The game's words in one language (key without the $ -> text), read from its own localization files. LoadCSV
		// only adds to the m_translations of the instance it is called on: one made without the constructor reads the
		// files and leaves Localization.instance, and so the language of everything else on the server, as it is.
		private static Dictionary<string, string> Words(string language)
		{
			Dictionary<string, string> words = new Dictionary<string, string>();
			try
			{
				Localization reader = (Localization)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Localization));
				AccessTools.Field(typeof(Localization), "m_translations").SetValue(reader, words);
				foreach (TextAsset file in Resources.Load<LocalizationSettings>("LocalizationSettings").Localizations)
				{
					reader.LoadCSV(file, language);
				}
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"DvergrTrade: the game's words in SignLanguage {language} could not be read ({e.Message}); the sign keeps the server's names");
				return null;
			}
			if (words.Count == 0)
			{
				ServersidePlugin.logger.LogWarning($"DvergrTrade: the game has no language {language} (SignLanguage); the sign keeps the server's names");
				return null;
			}
			return words;
		}

		private static string Hours(double hours)
		{
			return hours.ToString("0.##", CultureInfo.InvariantCulture);
		}

		// a no-break space after the count, so neither the messages' wrap (it breaks at plain spaces) nor the sign's puts the
		// count on one line and the item on the next
		private static string Times(int count, string what)
		{
			return count.ToString(CultureInfo.InvariantCulture) + "\u00d7\u00a0" + what;
		}

		private static string WardText(double hours)
		{
			return (Configuration.dvergrTradeWardOffText.Value ?? "").Replace("{hours}", Hours(hours));
		}

		// One offer for a player's screen: item names are tokens, the player's own game puts them in their language.
		private static string DescribeToken(Offer offer)
		{
			StringBuilder text = new StringBuilder();
			for (int i = 0; i < offer.priceNames.Length; i++)
			{
				text.Append(i > 0 ? " + " : "").Append(Times(offer.priceCounts[i], Token(offer.priceNames[i])));
			}
			text.Append(" \u2192 ");
			text.Append(offer.hours > 0d ? WardText(offer.hours) : Times(offer.rewardCount, Token(offer.rewardName)));
			return text.ToString();
		}

		// One offer for the sign, which is not localized: the server's names.
		private static string DescribeSign(Offer offer)
		{
			StringBuilder text = new StringBuilder();
			for (int i = 0; i < offer.priceNames.Length; i++)
			{
				text.Append(i > 0 ? " + " : "").Append(Times(offer.priceCounts[i], ServerName(offer.priceNames[i])));
			}
			text.Append(": ");
			text.Append(offer.hours > 0d ? WardText(offer.hours) : Times(offer.rewardCount, ServerName(offer.rewardName)));
			return text.ToString();
		}

		// One offer for the log: prefab names.
		private static string DescribePlain(Offer offer)
		{
			StringBuilder text = new StringBuilder();
			for (int i = 0; i < offer.priceNames.Length; i++)
			{
				text.Append(i > 0 ? "+" : "").Append(offer.priceCounts[i]).Append("x ").Append(offer.priceNames[i]);
			}
			text.Append(" > ");
			text.Append(offer.hours > 0d ? "ward off " + Hours(offer.hours) + " h" : offer.rewardCount + "x " + offer.rewardName);
			return text.ToString();
		}

		private static string SignText()
		{
			string key = s_offersVersion + "|" + Configuration.dvergrTradeSignTitle.Value + "|" + Configuration.dvergrTradeSignLanguage.Value + "|" + Configuration.dvergrTradeSignTextSize.Value + "|" + Configuration.dvergrTradeWardOffText.Value;
			if (key == s_signKey)
			{
				return s_signText;
			}
			StringBuilder text = new StringBuilder();
			text.Append("<size=").Append(Configuration.dvergrTradeSignTextSize.Value).Append("%>").Append(Configuration.dvergrTradeSignTitle.Value);
			foreach (Offer offer in s_offers)
			{
				text.Append('\n').Append(DescribeSign(offer));
			}
			text.Append("</size>");
			s_signKey = key;
			s_signText = text.ToString();
			ServersidePlugin.logger.LogInfo($"DvergrTrade: the sign reads: {s_signText.RemoveRichTextTags().Replace("\n", " / ")}");
			return s_signText;
		}

		// ---- The wards ------------------------------------------------------------------------------------------------

		// Every 30 s the wards are collected again, a few hundred sectors a frame (the world has some hundred thousand objects).
		private static void ScanStep()
		{
			if (!s_scanning)
			{
				if (Time.time < s_nextScan)
				{
					return;
				}
				s_scanning = true;
				s_scanIndex = 0;
				s_scanList.Clear();
			}
			if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(WardPrefab, s_scanList, ref s_scanIndex))
			{
				return;
			}
			s_scanning = false;
			s_nextScan = Time.time + 30f;
			s_scanSeen.Clear();
			s_scanIds.Clear();
			Dictionary<ZDOID, Vector3> off = new Dictionary<ZDOID, Vector3>();
			foreach (ZDO zdo in s_scanList)
			{
				if (!zdo.IsValid() || zdo.GetPrefab() != s_wardHash || !s_scanSeen.Add(zdo.m_uid))
				{
					continue;
				}
				s_scanIds.Add(zdo.m_uid);
				if (zdo.GetLong(s_keyOff, 0L) != 0L)
				{
					off[zdo.m_uid] = zdo.GetPosition();
				}
			}
			s_scanList.Clear();
			s_wardIds.Clear();
			s_wardIds.AddRange(s_scanIds);
			// The wards that are off are the ones whose data says so: rebuilt from it (this is also how they are found after a restart).
			// One bought off while the scan ran may have been read before it got its key: it stays while its data says so.
			foreach (KeyValuePair<ZDOID, Vector3> pair in s_off)
			{
				ZDO known = ZDOMan.instance.GetZDO(pair.Key);
				if (known != null && known.GetLong(s_keyOff, 0L) != 0L)
				{
					off[pair.Key] = pair.Value;
				}
			}
			if (off.Count != s_off.Count)
			{
				ServersidePlugin.logger.LogInfo($"DvergrTrade: {s_wardIds.Count} ward(s) in the world, {off.Count} switched off");
			}
			s_off.Clear();
			foreach (KeyValuePair<ZDOID, Vector3> pair in off)
			{
				s_off[pair.Key] = pair.Value;
			}
		}

		// Every two seconds: the wards that are off stay off, and those whose time is up are switched on.
		private static void WardTick()
		{
			if (s_off.Count == 0)
			{
				return;
			}
			s_offIds.Clear();
			foreach (ZDOID id in s_off.Keys)
			{
				s_offIds.Add(id);
			}
			long now = ZNet.instance.GetTime().Ticks;
			foreach (ZDOID id in s_offIds)
			{
				try
				{
					ZDO ward = ZDOMan.instance.GetZDO(id);
					long until = ward != null && ward.GetPrefab() == s_wardHash ? ward.GetLong(s_keyOff, 0L) : 0L;
					if (until == 0L)
					{
						s_off.Remove(id);
						continue;
					}
					if (until > now)
					{
						if (ward.GetBool(ZDOVars.s_enabled))
						{
							SetWardEnabled(ward, false);
						}
						continue;
					}
					SetWardEnabled(ward, true);
					ward.RemoveLong(s_keyOff);
					ZDOMan.instance.SetDirtySector(ward);
					s_off.Remove(id);
					ServersidePlugin.logger.LogInfo($"DvergrTrade: the ward at {Where(ward.GetPosition())} is switched on again");
					Announce(ward.GetPosition(), Configuration.dvergrTradeMessageWardOn.Value, 0L);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static void SetWardEnabled(ZDO ward, bool on)
		{
			long server = ZDOMan.GetSessionID();
			if (ward.GetOwner() != server)
			{
				ward.SetOwner(server);
			}
			ward.Set(ZDOVars.s_enabled, on);
		}

		// Pays for hours of a ward switched off. Returns the hours it is off for from now.
		private static double SwitchOff(ZDO ward, double hours)
		{
			long now = ZNet.instance.GetTime().Ticks;
			long until = Math.Max(now, ward.GetLong(s_keyOff, 0L)) + (long)(hours * TimeSpan.TicksPerHour);
			long cap = now + (long)(WardCapDays * TimeSpan.TicksPerDay);
			if (until > cap)
			{
				until = cap;
			}
			long server = ZDOMan.GetSessionID();
			if (ward.GetOwner() != server)
			{
				ward.SetOwner(server);
			}
			ward.Set(s_keyOff, until);
			ward.Set(ZDOVars.s_enabled, false);
			s_off[ward.m_uid] = ward.GetPosition();
			Calm(ward);
			return (until - now) / (double)TimeSpan.TicksPerHour;
		}

		// A dvergr that is angry stays angry: nothing in the game calms one. The camp's are calmed when the ward is bought off.
		private static void Calm(ZDO ward)
		{
			Vector3 at = ward.GetPosition();
			long server = ZDOMan.GetSessionID();
			int calmed = 0;
			Nearby(at, s_campRadius, s_near);
			foreach (ZDO zdo in s_near)
			{
				if (!s_dvergr.Contains(zdo.GetPrefab()) || FlatSqr(zdo.GetPosition(), at) > s_campRadius * s_campRadius || !zdo.GetBool(ZDOVars.s_aggravated))
				{
					continue;
				}
				if (zdo.GetOwner() != server)
				{
					zdo.SetOwner(server);
				}
				zdo.Set(ZDOVars.s_aggravated, false);
				calmed++;
			}
			s_near.Clear();
			if (calmed > 0)
			{
				ServersidePlugin.logger.LogInfo($"DvergrTrade: {calmed} dvergr at {Where(at)} calmed");
			}
		}

		// The hours a ward is still off for (0: it is on).
		private static double RemainingHours(ZDO ward)
		{
			long until = ward != null ? ward.GetLong(s_keyOff, 0L) : 0L;
			long now = ZNet.instance.GetTime().Ticks;
			return until > now ? (until - now) / (double)TimeSpan.TicksPerHour : 0d;
		}

		// The state of a ward for the message to whoever opens the chest.
		private static string WardState(ZDO ward)
		{
			double hours = RemainingHours(ward);
			return hours > 0d ? (Configuration.dvergrTradeWardStateOff.Value ?? "").Replace("{hours}", Hours(hours)) : Configuration.dvergrTradeWardStateOn.Value ?? "";
		}

		// ---- Stands ---------------------------------------------------------------------------------------------------

		private static void StandTick()
		{
			RefreshOffers();
			s_peerPositions.Clear();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer.IsReady())
				{
					s_peerPositions.Add(peer.GetRefPos());
				}
			}
			if (s_peerPositions.Count == 0)
			{
				return;
			}
			float range = Configuration.dvergrTradeStandRange.Value;
			foreach (ZDOID id in s_wardIds)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				if (ward == null || ward.GetPrefab() != s_wardHash)
				{
					continue;
				}
				Vector3 at = ward.GetPosition();
				bool near = false;
				foreach (Vector3 peer in s_peerPositions)
				{
					if (FlatSqr(peer, at) <= range * range)
					{
						near = true;
						break;
					}
				}
				if (!near)
				{
					continue;
				}
				try
				{
					if (!s_stands.TryGetValue(id, out Stand stand))
					{
						stand = new Stand { ward = id };
						s_stands[id] = stand;
					}
					Maintain(ward, stand);
					Trade(ward, stand);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static bool Exists(ZDOID id, int prefab)
		{
			if (id.IsNone())
			{
				return false;
			}
			ZDO zdo = ZDOMan.instance.GetZDO(id);
			return zdo != null && zdo.GetPrefab() == prefab;
		}

		// A stand that is there is left alone (but for the text of its sign); one that is not is found, put up or put up again.
		private static void Maintain(ZDO ward, Stand stand)
		{
			float now = Time.time;
			bool haveChest = Exists(stand.chest, s_chestHash);
			bool haveSign = Exists(stand.sign, s_signHash);
			if (!haveChest || !haveSign)
			{
				if (now >= stand.nextSearch)
				{
					stand.nextSearch = now + 5f;
					FindMarkers(ward, stand);
					haveChest = Exists(stand.chest, s_chestHash);
					haveSign = Exists(stand.sign, s_signHash);
				}
			}
			if (haveChest && haveSign)
			{
				stand.madeBefore = true;
				stand.missingSince = 0f;
				ZDO sign = ZDOMan.instance.GetZDO(stand.sign);
				string text = SignText();
				if (sign.GetString(ZDOVars.s_text, "") != text)
				{
					// Someone has written on it (possible while the ward is off), or the offers changed.
					long server = ZDOMan.GetSessionID();
					if (sign.GetOwner() != server)
					{
						sign.SetOwner(server);
					}
					sign.Set(ZDOVars.s_text, text);
				}
				return;
			}
			if (haveChest || haveSign)
			{
				stand.madeBefore = true;
			}
			if (stand.madeBefore)
			{
				// Made before, and a part of it is gone (someone took it down): put up again after a while.
				if (stand.missingSince == 0f)
				{
					stand.missingSince = now;
				}
				if (now - stand.missingSince < RetrySeconds)
				{
					return;
				}
			}
			if (now < stand.retryAt)
			{
				return;
			}
			if (!haveChest && !haveSign)
			{
				PutUp(ward, stand);
			}
			else if (CampLoaded(ward, stand))
			{
				PutUpMissing(ward, stand, haveChest);
			}
		}

		// The stand's chest and sign by their markers, within 8 m of the ward.
		private static void FindMarkers(ZDO ward, Stand stand)
		{
			Vector3 at = ward.GetPosition();
			Nearby(at, 8f, s_near);
			foreach (ZDO zdo in s_near)
			{
				int prefab = zdo.GetPrefab();
				if ((prefab != s_chestHash && prefab != s_signHash) || (zdo.GetPosition() - at).sqrMagnitude > 64f)
				{
					continue;
				}
				int kind = zdo.GetInt(s_keyTrade, 0);
				if (prefab == s_chestHash && kind == 1 && !Exists(stand.chest, s_chestHash))
				{
					stand.chest = zdo.m_uid;
				}
				else if (prefab == s_signHash && kind == 2 && !Exists(stand.sign, s_signHash))
				{
					stand.sign = zdo.m_uid;
				}
			}
			s_near.Clear();
		}

		// True when the ward has an instance and so has every persistent object within 12 m of it: the camp is made, nothing is built into a wall that is not there yet.
		private static bool CampLoaded(ZDO ward, Stand stand)
		{
			int missing = 0;
			string first = null;
			if (ZNetScene.instance.FindInstance(ward) == null)
			{
				missing = 1;
				first = WardPrefab;
			}
			else
			{
				Vector3 at = ward.GetPosition();
				Nearby(at, 12f, s_near);
				foreach (ZDO zdo in s_near)
				{
					if (!zdo.Persistent || (zdo.GetPosition() - at).sqrMagnitude > 144f || ZNetScene.instance.GetPrefab(zdo.GetPrefab()) == null)
					{
						continue;
					}
					if (!ZNetScene.instance.HaveInstance(zdo))
					{
						if (missing++ == 0)
						{
							first = ZNetScene.instance.GetPrefab(zdo.GetPrefab()).name;
						}
					}
				}
				s_near.Clear();
			}
			if (missing == 0)
			{
				stand.waitingSince = 0f;
				return true;
			}
			if (stand.waitingSince == 0f)
			{
				stand.waitingSince = Time.time;
			}
			else if (!stand.waitLogged && Time.time - stand.waitingSince > 120f)
			{
				stand.waitLogged = true;
				ServersidePlugin.logger.LogWarning($"DvergrTrade: the ward at {Where(ward.GetPosition())} ({LocationOf(ward.GetPosition(), stand)}) has waited two minutes for {missing} object(s) of its camp to be made (first: {first}); the stand is put up when they are");
			}
			return false;
		}

		private static void PutUp(ZDO ward, Stand stand)
		{
			if (!CampLoaded(ward, stand))
			{
				return;
			}
			Vector3 at = ward.GetPosition();
			Vector3 forward = ward.GetRotation() * Vector3.forward;
			forward.y = 0f;
			forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
			if (s_rayMask < 0)
			{
				s_rayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
				s_boxMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
			}
			foreach (float distance in s_distances)
			{
				for (int i = 0; i < 16; i++)
				{
					Vector3 direction = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * forward;
					if (!TrySpot(at, direction, distance, out Vector3 chestAt, out Quaternion rotation, out Vector3 signAt))
					{
						continue;
					}
					ZDO chest = CreateObject(s_chestHash, chestAt, rotation);
					chest.Set(ZDOVars.s_addedDefaultItems, true);
					chest.Set(ZDOVars.s_cheated, 1);
					chest.Set(s_keyTrade, 1);
					ZDO sign = CreateObject(s_signHash, signAt, rotation);
					sign.Set(ZDOVars.s_text, SignText());
					sign.Set(s_keyTrade, 2);
					stand.chest = chest.m_uid;
					stand.sign = sign.m_uid;
					stand.madeBefore = true;
					stand.missingSince = 0f;
					ServersidePlugin.logger.LogInfo($"DvergrTrade: stand put up at {LocationOf(at, stand)}, ward {Where(at)}: chest {Offset(chestAt - at)}, sign {Offset(signAt - at)} from the ward");
					return;
				}
			}
			stand.retryAt = Time.time + RetrySeconds;
			if (!stand.noSpotLogged)
			{
				stand.noSpotLogged = true;
				ServersidePlugin.logger.LogWarning($"DvergrTrade: no free spot for a stand within 5 m of the ward at {LocationOf(at, stand)} {Where(at)}; trying again in ten minutes");
			}
		}

		// One part of a stand is gone: the other shows where it stood (same rotation, the sign 1.3 m to the chest's right).
		private static void PutUpMissing(ZDO ward, Stand stand, bool haveChest)
		{
			ZDO other = ZDOMan.instance.GetZDO(haveChest ? stand.chest : stand.sign);
			Quaternion rotation = other.GetRotation();
			Vector3 side = rotation * Vector3.right * SignOffset;
			if (haveChest)
			{
				ZDO sign = CreateObject(s_signHash, other.GetPosition() + side + Vector3.up * SignLift, rotation);
				sign.Set(ZDOVars.s_text, SignText());
				sign.Set(s_keyTrade, 2);
				stand.sign = sign.m_uid;
			}
			else
			{
				ZDO chest = CreateObject(s_chestHash, other.GetPosition() - side - Vector3.up * SignLift, rotation);
				chest.Set(ZDOVars.s_addedDefaultItems, true);
				chest.Set(ZDOVars.s_cheated, 1);
				chest.Set(s_keyTrade, 1);
				stand.chest = chest.m_uid;
			}
			stand.missingSince = 0f;
			ServersidePlugin.logger.LogInfo($"DvergrTrade: the {(haveChest ? "sign" : "chest")} of the stand at {LocationOf(ward.GetPosition(), stand)}, ward {Where(ward.GetPosition())}, is put up again");
		}

		// A spot for the chest (front away from the ward) with the sign beside it: level ground near the ward's height, free of anything.
		private static bool TrySpot(Vector3 ward, Vector3 direction, float distance, out Vector3 chestAt, out Quaternion rotation, out Vector3 signAt)
		{
			chestAt = default;
			signAt = default;
			rotation = Quaternion.LookRotation(direction, Vector3.up);
			if (!Ground(ward + direction * distance, ward.y, out chestAt))
			{
				return false;
			}
			if (Physics.CheckBox(chestAt + Vector3.up * 0.6f, s_chestHalf, rotation, s_boxMask, QueryTriggerInteraction.Ignore))
			{
				return false;
			}
			if (!Ground(chestAt + rotation * Vector3.right * SignOffset, ward.y, out signAt) || Mathf.Abs(signAt.y - chestAt.y) > 0.4f)
			{
				return false;
			}
			if (Physics.CheckBox(signAt + Vector3.up * 0.7f, s_signHalf, rotation, s_boxMask, QueryTriggerInteraction.Ignore))
			{
				return false;
			}
			signAt += Vector3.up * SignLift;
			return true;
		}

		// The ground below a point: 9 m down from 4 m above the ward's height, within 1.5 m of it, and level.
		private static bool Ground(Vector3 at, float wardY, out Vector3 ground)
		{
			ground = default;
			if (!Physics.Raycast(new Vector3(at.x, wardY + 4f, at.z), Vector3.down, out RaycastHit hit, 9f, s_rayMask, QueryTriggerInteraction.Ignore))
			{
				return false;
			}
			if (Mathf.Abs(hit.point.y - wardY) > 1.5f || hit.normal.y < 0.85f)
			{
				return false;
			}
			ground = hit.point;
			return true;
		}

		// What ZNetView.Awake does for a new object, without instantiating it: the game creates the object from this when it next loads the area.
		private static ZDO CreateObject(int prefabHash, Vector3 position, Quaternion rotation)
		{
			ZNetView view = ZNetScene.instance.GetPrefab(prefabHash).GetComponent<ZNetView>();
			ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefabHash);
			zdo.Persistent = view.m_persistent;
			zdo.Type = view.m_type;
			zdo.Distant = view.m_distant;
			zdo.SetPrefab(prefabHash);
			zdo.SetRotation(rotation);
			// Saved worlds only rewrite chunks marked as changed; SetRotation marks it only when the rotation differs from the default.
			ZDOMan.instance.SetDirtySector(zdo);
			return zdo;
		}

		// ---- Trading --------------------------------------------------------------------------------------------------

		private static ChestState StateOf(ZDOID chest)
		{
			if (!s_chests.TryGetValue(chest, out ChestState state))
			{
				state = new ChestState();
				s_chests[chest] = state;
			}
			return state;
		}

		// Every second, for a stand's chest near a player: take it back from the player's game once closed, and weigh what is in it.
		private static void Trade(ZDO ward, Stand stand)
		{
			if (!Exists(stand.chest, s_chestHash) || s_offers.Count == 0)
			{
				return;
			}
			ZDO chest = ZDOMan.instance.GetZDO(stand.chest);
			ChestState state = StateOf(stand.chest);
			long server = ZDOMan.GetSessionID();
			float now = Time.time;
			long owner = chest.GetOwner();
			ZNetPeer peer = owner != 0L && owner != server ? ZNet.instance.GetPeer(owner) : null;
			bool taken = state.takenAt > 0f && now - state.takenAt < 5f;
			state.takenAt = 0f;
			if (peer != null && taken && chest.OwnerRevision < state.takenRevision)
			{
				// Taken back last tick, and a player's game has it again with an owner revision older than the one the server
				// gave it: the game granted itself an open as the server took the chest back, its newer data won here and it
				// took the server's owner itself. Nothing is weighed; the chest goes back to that game, with a revision above both.
				chest.SetOwner(server);
				chest.SetOwner(owner);
				state.ownerSince = 0f;
				ServersidePlugin.logger.LogInfo($"DvergrTrade: {peer.m_playerName} opened the chest at {LocationOf(ward.GetPosition(), stand)} just as the server took it back; it is theirs again");
				return;
			}
			if (chest.GetInt(ZDOVars.s_inUse, 0) != 0)
			{
				if (peer != null)
				{
					// Open in a player's game.
					state.lastUser = owner;
					state.ownerSince = 0f;
					state.openUntil = 0f;
					return;
				}
				// Left open by a game that has gone: the server's own copy of the chest resets the flag only when it sees a change.
				chest.SetOwner(server);
				chest.Set(ZDOVars.s_inUse, 0);
			}
			if (peer != null)
			{
				state.lastUser = owner;
				if (state.ownerSince == 0f)
				{
					state.ownerSince = now;
				}
				if (now < state.openUntil || now - state.ownerSince < SettleSeconds)
				{
					return;
				}
				state.ownerSince = 0f;
				// Taken back now and weighed on the next tick, by when an open that the player's game granted itself meanwhile
				// has arrived (see the top of the file).
				chest.SetOwner(server);
				state.takenAt = now;
				state.takenRevision = chest.OwnerRevision;
				return;
			}
			if (chest.GetOwner() != server)
			{
				chest.SetOwner(server);
			}
			if (state.hasDone && state.done == chest.DataRevision)
			{
				return;
			}
			Process(ward, stand, chest, state);
		}

		private static void Process(ZDO ward, Stand stand, ZDO chest, ChestState state)
		{
			state.hasDone = true;
			state.done = chest.DataRevision;
			try
			{
				Weigh(ward, stand, chest, state);
			}
			finally
			{
				// Whoever used it is told once; the next use finds out again who it is.
				state.lastUser = 0L;
			}
		}

		private static void Weigh(ZDO ward, Stand stand, ZDO chest, ChestState state)
		{
			byte[] bytes = chest.GetByteArray(ZDOVars.s_items);
			if (bytes == null || bytes.Length == 0)
			{
				return;
			}
			Inventory inventory = new Inventory("DvergrTrade", null, s_chestWidth, s_chestHeight);
			inventory.Load(new ZPackage(bytes));
			Recount(inventory);
			bool anyPrice = false;
			foreach (Offer offer in s_offers)
			{
				foreach (string name in offer.priceNames)
				{
					if (s_counts.ContainsKey(name))
					{
						anyPrice = true;
					}
				}
			}
			if (!anyPrice)
			{
				return;
			}

			Dictionary<string, int> paid = new Dictionary<string, int>();
			Dictionary<string, int> got = new Dictionary<string, int>();
			double hours = 0d;
			int trades = 0;
			Offer unfit = null;
			bool capped = false;
			double wardHave = RemainingHours(ward);
			double capHours = WardCapDays * 24d;
			foreach (Offer offer in s_offers)
			{
				// After goods that did not fit, only an offer of the ward (it needs no room) is still weighed.
				while ((unfit == null || offer.hours > 0d) && trades < 500 && PaidInFull(offer))
				{
					// The ward is off for at most seven days from now: an offer whose hours do not all fit is not taken, and
					// its trophies stay in the chest.
					if (offer.hours > 0d && wardHave + hours + offer.hours > capHours + 0.001d)
					{
						capped = true;
						break;
					}
					byte[] before = null;
					if (offer.hours <= 0d)
					{
						ZPackage snapshot = new ZPackage();
						inventory.Save(snapshot);
						before = snapshot.GetArray();
					}
					for (int i = 0; i < offer.priceNames.Length; i++)
					{
						Remove(inventory, offer.priceNames[i], offer.priceCounts[i]);
					}
					if (offer.hours <= 0d && !Add(inventory, offer.rewardName, offer.rewardCount))
					{
						// No room for the goods: this trade is undone and nothing more is traded.
						inventory.Load(new ZPackage(before));
						Recount(inventory);
						unfit = offer;
						break;
					}
					for (int i = 0; i < offer.priceNames.Length; i++)
					{
						Count(paid, offer.priceNames[i], offer.priceCounts[i]);
					}
					if (offer.hours > 0d)
					{
						hours += offer.hours;
					}
					else
					{
						Count(got, offer.rewardName, offer.rewardCount);
					}
					trades++;
				}
			}

			ZNetPeer user = state.lastUser != 0L ? ZNet.instance.GetPeer(state.lastUser) : null;
			List<string> lines = new List<string>();
			if (trades > 0)
			{
				ZPackage package = new ZPackage();
				inventory.Save(package);
				chest.Set(ZDOVars.s_items, package.GetArray());
				state.done = chest.DataRevision;
				state.lockedUntil = Time.time + WriteLockSeconds;
				if (got.Count > 0)
				{
					lines.Add(Format(Configuration.dvergrTradeMessageTrade.Value, "{paid}", Listing(paid), "{got}", Listing(got)));
				}
				StringBuilder log = new StringBuilder();
				log.Append("DvergrTrade: ").Append(user != null ? user.m_playerName : "somebody").Append(" at ").Append(LocationOf(ward.GetPosition(), stand)).Append(": paid ").Append(ListingPlain(paid));
				if (got.Count > 0)
				{
					log.Append(", got ").Append(ListingPlain(got));
				}
				if (hours > 0d)
				{
					log.Append(", ward off for ").Append(Hours(hours)).Append(" h");
				}
				ServersidePlugin.logger.LogInfo(log.ToString());
			}
			if (unfit != null)
			{
				lines.Add(Format(Configuration.dvergrTradeMessageFull.Value, "{got}", Times(unfit.rewardCount, Token(unfit.rewardName))));
			}
			if (capped)
			{
				lines.Add(Format(Configuration.dvergrTradeMessageWardMax.Value, "{hours}", Hours(wardHave + hours), "{max}", Hours(capHours)));
			}
			if (unfit == null && !capped && trades == 0)
			{
				string missing = Missing();
				if (missing.Length > 0)
				{
					lines.Add(Format(Configuration.dvergrTradeMessageMissing.Value, "{missing}", missing));
				}
			}
			string wardLine = null;
			if (hours > 0d)
			{
				double remaining = SwitchOff(ward, hours);
				wardLine = Format(Configuration.dvergrTradeMessageWardOff.Value, "{hours}", Hours(remaining));
				ServersidePlugin.logger.LogInfo($"DvergrTrade: the ward at {LocationOf(ward.GetPosition(), stand)} {Where(ward.GetPosition())} is off for {Hours(remaining)} h");
				Announce(ward.GetPosition(), wardLine, state.lastUser);
				if (user != null && user.IsReady() && FlatSqr(user.GetRefPos(), ward.GetPosition()) <= Configuration.dvergrTradeStandRange.Value * Configuration.dvergrTradeStandRange.Value)
				{
					lines.Add(wardLine);
				}
			}
			if (user != null && user.IsReady() && lines.Count > 0)
			{
				string message = string.Join("\n", lines.FindAll(l => !string.IsNullOrEmpty(l)));
				if (message.Length > 0)
				{
					PlayerMessages.Show(user.m_uid, MessageHud.MessageType.Center, message);
				}
			}
		}

		private static void Count(Dictionary<string, int> into, string name, int count)
		{
			into.TryGetValue(name, out int have);
			into[name] = have + count;
		}

		private static void Recount(Inventory inventory)
		{
			s_counts.Clear();
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				if (item.m_dropPrefab)
				{
					Count(s_counts, item.m_dropPrefab.name, item.m_stack);
				}
			}
		}

		private static bool PaidInFull(Offer offer)
		{
			for (int i = 0; i < offer.priceNames.Length; i++)
			{
				if (!s_counts.TryGetValue(offer.priceNames[i], out int have) || have < offer.priceCounts[i])
				{
					return false;
				}
			}
			return true;
		}

		private static void Remove(Inventory inventory, string name, int count)
		{
			s_items.Clear();
			s_items.AddRange(inventory.GetAllItems());
			foreach (ItemDrop.ItemData item in s_items)
			{
				if (count <= 0)
				{
					break;
				}
				if (item.m_dropPrefab && item.m_dropPrefab.name == name)
				{
					int take = Math.Min(item.m_stack, count);
					inventory.RemoveItem(item, take);
					count -= take;
				}
			}
			s_items.Clear();
			Recount(inventory);
		}

		// The goods into the chest, a stack at a time (the game's own AddItem, world level and quality 1). False when they do not all fit.
		private static bool Add(Inventory inventory, string name, int count)
		{
			GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
			int stack = Math.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
			while (count > 0)
			{
				int amount = Math.Min(count, stack);
				if (!inventory.AddItem(prefab, amount))
				{
					return false;
				}
				count -= amount;
			}
			Recount(inventory);
			return true;
		}

		private static string Listing(Dictionary<string, int> items)
		{
			List<string> parts = new List<string>();
			foreach (KeyValuePair<string, int> item in items)
			{
				parts.Add(Times(item.Value, Token(item.Key)));
			}
			return string.Join(", ", parts);
		}

		private static string ListingPlain(Dictionary<string, int> items)
		{
			List<string> parts = new List<string>();
			foreach (KeyValuePair<string, int> item in items)
			{
				parts.Add(item.Value + "x " + item.Key);
			}
			return string.Join(", ", parts);
		}

		// What the first offer that has any of its price in the chest still lacks.
		private static string Missing()
		{
			foreach (Offer offer in s_offers)
			{
				bool any = false;
				foreach (string name in offer.priceNames)
				{
					if (s_counts.ContainsKey(name))
					{
						any = true;
					}
				}
				if (!any)
				{
					continue;
				}
				List<string> parts = new List<string>();
				for (int i = 0; i < offer.priceNames.Length; i++)
				{
					s_counts.TryGetValue(offer.priceNames[i], out int have);
					if (have < offer.priceCounts[i])
					{
						parts.Add(Times(offer.priceCounts[i] - have, Token(offer.priceNames[i])));
					}
				}
				return string.Join(", ", parts);
			}
			return "";
		}

		private static string Format(string text, string key, string value, string key2 = null, string value2 = null)
		{
			text = (text ?? "").Replace(key, value);
			return key2 != null ? text.Replace(key2, value2) : text;
		}

		// ---- Messages -------------------------------------------------------------------------------------------------

		// The players within StandRange of a place are told, in the middle of the screen; `except` is told in another way.
		private static void Announce(Vector3 at, string text, long except)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			float range = Configuration.dvergrTradeStandRange.Value;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer.IsReady() && peer.m_uid != except && FlatSqr(peer.GetRefPos(), at) <= range * range)
				{
					PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, text);
				}
			}
		}

		private static void OnOpened(ZDO chest, long uid)
		{
			ChestState state = StateOf(chest.m_uid);
			state.lastUser = uid;
			state.ownerSince = 0f;
			state.openUntil = Time.time + OpenGraceSeconds;
			ZDO ward = null;
			foreach (Stand stand in s_stands.Values)
			{
				if (stand.chest == chest.m_uid)
				{
					ward = ZDOMan.instance.GetZDO(stand.ward);
					break;
				}
			}
			StringBuilder offers = new StringBuilder();
			foreach (Offer offer in s_offers)
			{
				offers.Append(offers.Length > 0 ? "\n" : "").Append(DescribeToken(offer));
			}
			string text = Format(Configuration.dvergrTradeMessageOpen.Value, "{offers}", offers.ToString(), "{ward}", WardState(ward));
			if (!string.IsNullOrEmpty(text))
			{
				PlayerMessages.Show(uid, MessageHud.MessageType.TopLeft, text);
			}
		}

		// ---- Helpers --------------------------------------------------------------------------------------------------

		// Every object of the sectors that a circle touches; the caller measures the distance.
		private static void Nearby(Vector3 centre, float radius, List<ZDO> into)
		{
			into.Clear();
			Vector2s low = ZoneSystem.GetZone(new Vector3(centre.x - radius, 0f, centre.z - radius));
			Vector2s high = ZoneSystem.GetZone(new Vector3(centre.x + radius, 0f, centre.z + radius));
			SimulationDistance none = new SimulationDistance(0, 0);
			for (int x = low.x; x <= high.x; x++)
			{
				for (int y = low.y; y <= high.y; y++)
				{
					ZDOMan.instance.FindSectorObjects(new Vector2s(x, y), none, into);
				}
			}
		}

		private static float FlatSqr(Vector3 a, Vector3 b)
		{
			float dx = a.x - b.x;
			float dz = a.z - b.z;
			return dx * dx + dz * dz;
		}

		private static string Where(Vector3 at)
		{
			return $"{at.x:0},{at.z:0}";
		}

		private static string Offset(Vector3 d)
		{
			return $"{d.x:0.0},{d.y:0.0},{d.z:0.0}";
		}

		// The location a camp belongs to, by the nearest one the world has (for the log); kept with the stand.
		private static string LocationOf(Vector3 at, Stand stand)
		{
			if (stand != null && stand.location != null)
			{
				return stand.location;
			}
			string name = "?";
			float best = 100f * 100f;
			if (ZoneSystem.instance != null)
			{
				foreach (ZoneSystem.LocationInstance location in ZoneSystem.instance.GetLocationList())
				{
					float d = FlatSqr(location.m_position, at);
					if (location.m_location != null && d < best)
					{
						best = d;
						name = location.m_location.m_prefabName;
					}
				}
			}
			if (stand != null)
			{
				stand.location = name;
			}
			return name;
		}

		private static void WarnOnce(string key, string message)
		{
			if (s_logged.Add(key))
			{
				ServersidePlugin.logger.LogWarning(message);
			}
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
				ServersidePlugin.logger.LogWarning($"DvergrTrade: {e}");
			}
		}

		// ---- Patches --------------------------------------------------------------------------------------------------

		// The chest was opened by a player (the server gave it to his game): he is told the prices and the state of the ward.
		[HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
		public static class Container_RPC_RequestOpen_Patch
		{
			// Just after the server has written a trade chest, an open is answered "in use" (see the top of the file).
			static bool Prefix(Container __instance, long uid)
			{
				if (!Configuration.dvergrTradeEnabled.Value || !s_ready || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					ZNetView view = __instance.GetComponent<ZNetView>();
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					if (zdo != null && view.IsOwner() && zdo.GetInt(s_keyTrade, 0) == 1 && s_chests.TryGetValue(zdo.m_uid, out ChestState state) && Time.time < state.lockedUntil)
					{
						view.InvokeRPC(uid, "RPC_OpenResponse", false);
						return false;
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
				return true;
			}

			static void Postfix(Container __instance, long uid)
			{
				if (!Configuration.dvergrTradeEnabled.Value || !s_ready || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return;
				}
				try
				{
					ZNetView view = __instance.GetComponent<ZNetView>();
					ZDO zdo = view ? view.GetZDO() : null;
					if (zdo != null && zdo.GetInt(s_keyTrade, 0) == 1 && zdo.GetOwner() == uid)
					{
						OnOpened(zdo, uid);
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		// The game switches a dvergr ward on whenever its area is made (enabledByDefault, on the owner): one that the key says is off is switched straight off again.
		[HarmonyPatch(typeof(PrivateArea), "Awake")]
		public static class PrivateArea_Awake_Patch
		{
			static void Postfix(PrivateArea __instance)
			{
				if (!ZNet.instance || !ZNet.instance.IsServer())
				{
					return;
				}
				try
				{
					ZNetView view = __instance.GetComponent<ZNetView>();
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					if (zdo == null || zdo.GetLong(s_keyOff, 0L) == 0L)
					{
						return;
					}
					s_off[zdo.m_uid] = zdo.GetPosition();
					if (view.IsOwner() && zdo.GetLong(s_keyOff, 0L) > ZNet.instance.GetTime().Ticks)
					{
						zdo.Set(ZDOVars.s_enabled, false);
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		// A dvergr ward that is switched off takes no damage: off, it passes no damage on to the camp's anger, so it could be
		// broken without a fight, and the camp would lose its stand.
		[HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
		public static class WearNTear_RPC_Damage_Patch
		{
			static bool Prefix(WearNTear __instance)
			{
				if (s_off.Count == 0 || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					ZNetView view = __instance.GetComponent<ZNetView>();
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					return zdo == null || !s_off.ContainsKey(zdo.m_uid) || zdo.GetLong(s_keyOff, 0L) <= ZNet.instance.GetTime().Ticks;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// While the ward is off, taking what lies in the camp (a lantern, a stein) does not anger the dvergr; hitting one still does.
		[HarmonyPatch(typeof(BaseAI), "RPC_SetAggravated")]
		public static class BaseAI_RPC_SetAggravated_Patch
		{
			static bool Prefix(BaseAI __instance, bool aggro, int reason)
			{
				if (!aggro || reason != (int)BaseAI.AggravatedReason.Theif || s_off.Count == 0 || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					Vector3 at = __instance.transform.position;
					foreach (Vector3 ward in s_off.Values)
					{
						if (FlatSqr(at, ward) <= s_campRadius * s_campRadius)
						{
							return false;
						}
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
				return true;
			}
		}
	}
}
