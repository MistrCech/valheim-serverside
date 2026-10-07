using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		A server-side check on the characters players join with.

		Valheim keeps a character on the player's own computer, and a vanilla client never sends its
		inventory to the server, so the server can neither read nor replace what a character carries.
		What it does see:
		- the character's id (the player ZDO's "playerID"), the same for that character on every world;
		- what the character wears and holds (VisEquipment: both hands, both back slots, chest, legs,
		  helmet, shoulders, utility, trinket), with the upgrade level of weapons;
		- the raids the character is ready for ("possibleEvents", which every client sends): the game
		  decides that from the items the character knows and the bosses it has beaten
		  (RandEventSystem.PlayerIsReadyForEvent), so it shows how far the character has come.

		Two checks follow from that:
		- a character this world has not seen before must be fresh: still ready for Eikthyr's raid
		  (it knows none of the Eikthyr-tier items) and wearing nothing beyond what a level 1 workbench
		  makes. A character brought in with progress from another world is kicked, or only logged.
		- a known character that comes back wearing something new, or with other progress than it had
		  when it left, played somewhere else in between; that is logged, or kicked.

		"Known" is every character in this world's list, plus -- so that switching this on does not lock
		anyone out -- every character that has built something or owns a bed or a tombstone here. An
		admin can let a character in with the console command `allow <name>`.

		What a character carries in its inventory without wearing it stays invisible. This stops
		characters being imported with their progress; it cannot stop a known character from bringing
		materials in its bags.
	*/
	public class CharacterGuard : IFeature
	{
		public enum NewCharacterPolicy { Allow, RequireFresh }
		public enum Action { Ignore, Log, Kick }

		public bool FeatureEnabled()
		{
			return Configuration.characterGuardEnabled.Value;
		}

		private class Record
		{
			public long id;
			public string name = "";
			public string account = "";
			public string reason = "";
			public DateTime firstSeen, lastSeen;
			// What the character wore and which raids it was ready for when it last left; null if never seen leaving.
			public string[] wearing;
			public string[] progress;
		}

		private class Session
		{
			public float since;
			public float zdoSince = -1f;
			public bool decided;
			public long id;
			public float kickAt = -1f;
			public bool kicked;
		}

		private static readonly Dictionary<long, Record> s_records = new Dictionary<long, Record>();
		private static readonly Dictionary<ZNetPeer, Session> s_sessions = new Dictionary<ZNetPeer, Session>();
		// Characters turned away this session, for `allow`: name -> record (not saved until allowed).
		private static readonly Dictionary<string, Record> s_rejected = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
		private static HashSet<long> s_footprint;
		private static bool s_footprintStarted;
		private static string s_path;
		private static Dictionary<int, bool> s_advanced;
		private static HashSet<string> s_progressEvents;
		private static float s_nextTick;
		private static float s_nextSnapshot;
		// The known list could not be read: the guard stays off until the server restarts, and the file is left alone.
		private static bool s_failed;
		private static int s_warnings;
		private static float s_warningsSince;

		private const float SettleSeconds = 3f;
		private const float EventsWaitSeconds = 20f;
		private const float KickDelaySeconds = 8f;
		// How often what an online character wears and knows is taken as the reference for its return: a server
		// shutdown never calls ZNet.Disconnect, so the leave itself is not always seen.
		private const float SnapshotSeconds = 60f;

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

		// The item ledger needs the world's footprint and the known list even with the guard itself off.
		public static void EnsureFootprint()
		{
			if (s_failed)
			{
				return;
			}
			if (s_path == null)
			{
				try
				{
					Load();
				}
				catch (Exception e)
				{
					s_failed = true;
					ServersidePlugin.logger.LogError($"Character guard: could not read the known characters, so it stays off until the server restarts: {e}");
					GuardLog.Write($"Character guard: could not read the known characters ({e.GetType().Name}: {e.Message}), so it stays off until the server restarts");
					return;
				}
			}
			if (!s_footprintStarted)
			{
				s_footprintStarted = true;
				ServersidePlugin.instance.StartCoroutine(CollectFootprint());
			}
		}

		/*
			A character the world has not seen before and that is fresh: no builds, bed or tombstone here,
			not on the known list, still ready for Eikthyr's raid and wearing nothing advanced. What it
			carries then counts as nothing worth tracking. Anything unknown (the footprint still being
			collected, no raid list from the client yet) answers no.
		*/
		public static bool IsBrandNew(ZNetPeer peer, ZDO character, long id)
		{
			if (s_failed || s_footprint == null || s_footprint.Contains(id) || (s_records.TryGetValue(id, out Record known) && known.reason != "new and fresh")
				|| !peer.m_serverSyncedPlayerData.TryGetValue(RandEventSystem.PossibleEventsKey, out string events))
			{
				return false;
			}
			if (known != null && (DateTime.UtcNow - known.firstSeen).TotalMinutes > 10)
			{
				// Fresh when it was first seen, but it has been played here since: its bag is not empty any more.
				return false;
			}
			return FreshnessProblems(Wearing(character), events, true).Count == 0;
		}

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.characterGuardEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || ZNet.World == null
				|| ZDOMan.instance == null || !ObjectDB.instance || !RandEventSystem.instance || Time.time < s_nextTick)
			{
				return;
			}
			s_nextTick = Time.time + 1f;
			EnsureFootprint();
			if (s_failed)
			{
				return;
			}
			foreach (ZNetPeer peer in ZNet.instance.GetPeers().ToList())
			{
				try
				{
					if (!peer.IsReady())
					{
						continue;
					}
					if (!s_sessions.TryGetValue(peer, out Session session))
					{
						s_sessions[peer] = session = new Session { since = Time.time };
					}
					if (session.kickAt >= 0f && Time.time >= session.kickAt)
					{
						session.kickAt = -1f;
						session.kicked = true;
						ServersidePlugin.logger.LogInfo($"Character guard: kicking {peer.m_playerName}");
						ZNet.instance.InternalKick(peer);
					}
					if (!session.decided && s_footprint != null)
					{
						TryDecide(peer, session);
					}
				}
				catch (Exception e)
				{
					Warn($"could not check {peer.m_playerName}", e);
				}
			}
			foreach (ZNetPeer gone in s_sessions.Keys.Where(p => !ZNet.instance.GetPeers().Contains(p)).ToList())
			{
				s_sessions.Remove(gone);
			}
			if (Time.time >= s_nextSnapshot)
			{
				s_nextSnapshot = Time.time + SnapshotSeconds;
				bool changed = false;
				foreach (KeyValuePair<ZNetPeer, Session> kv in s_sessions)
				{
					try
					{
						changed |= Snapshot(kv.Key, kv.Value);
					}
					catch (Exception e)
					{
						Warn($"could not note what {kv.Key.m_playerName} wears", e);
					}
				}
				if (changed)
				{
					Save();
				}
			}
		}

		// What the character wears and knows now, as the reference for its return. True if that changed.
		private static bool Snapshot(ZNetPeer peer, Session session)
		{
			if (!session.decided || session.kicked || session.kickAt >= 0f || !s_records.TryGetValue(session.id, out Record record))
			{
				return false;
			}
			ZDO character = peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
			if (character == null || character.GetLong(ZDOVars.s_playerID, 0L) != session.id)
			{
				return false;
			}
			string[] wearing = Wearing(character);
			string[] progress = peer.m_serverSyncedPlayerData.TryGetValue(RandEventSystem.PossibleEventsKey, out string events) ? Progress(events) : record.progress;
			bool changed = record.wearing == null || !record.wearing.SequenceEqual(wearing)
				|| (progress != null && (record.progress == null || !record.progress.SequenceEqual(progress)));
			record.wearing = wearing;
			record.progress = progress;
			record.lastSeen = DateTime.UtcNow;
			return changed;
		}

		// At most 20 in a quarter of an hour, so a fault that repeats stays visible without flooding the log.
		private static void Warn(string what, Exception e)
		{
			float now = Time.realtimeSinceStartup;
			if (now - s_warningsSince > 900f)
			{
				s_warningsSince = now;
				s_warnings = 0;
			}
			if (s_warnings++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Character guard: {what}: {e}");
			}
		}

		private static void TryDecide(ZNetPeer peer, Session session)
		{
			ZDO character = peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
			long id = character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
			if (id == 0L)
			{
				return;
			}
			// The client fills in what the character wears right after it spawns; give it a moment.
			if (session.zdoSince < 0f)
			{
				session.zdoSince = Time.time;
			}
			bool haveEvents = peer.m_serverSyncedPlayerData.TryGetValue(RandEventSystem.PossibleEventsKey, out string events);
			if (Time.time - session.zdoSince < SettleSeconds || (!haveEvents && Time.time - session.zdoSince < EventsWaitSeconds))
			{
				return;
			}
			session.decided = true;
			session.id = id;

			string account = peer.m_socket.GetHostName();
			string[] wearing = Wearing(character);
			string[] progress = haveEvents ? Progress(events) : null;
			string name = peer.m_playerName;
			DateTime now = DateTime.UtcNow;

			if (Configuration.characterGuardExemptAdmins.Value && ZNet.instance.IsAdmin(account))
			{
				Remember(id, name, account, "admin", now);
				return;
			}

			if (s_records.TryGetValue(id, out Record known))
			{
				List<string> changes = Changes(known, wearing, progress);
				known.name = name;
				known.account = account;
				known.lastSeen = now;
				Save();
				if (changes.Count > 0)
				{
					if (Configuration.characterGuardChangedAway.Value == Action.Kick)
					{
						s_rejected[name] = known;
					}
					Act(peer, session, Configuration.characterGuardChangedAway.Value,
						$"{name} ({account}) came back changed since leaving this world: {string.Join("; ", changes)}",
						Configuration.characterGuardChangedMessage.Value);
				}
				return;
			}
			if (s_footprint.Contains(id))
			{
				Remember(id, name, account, "built or slept here before", now);
				return;
			}

			List<string> reasons = FreshnessProblems(wearing, events, haveEvents);
			if (reasons.Count == 0 || Configuration.characterGuardNewCharacters.Value == NewCharacterPolicy.Allow)
			{
				Remember(id, name, account, reasons.Count == 0 ? "new and fresh" : "new, allowed with progress", now);
				if (reasons.Count > 0)
				{
					ServersidePlugin.logger.LogInfo($"Character guard: new character {name} ({account}) let in with progress: {string.Join("; ", reasons)}");
				}
				return;
			}
			string what = $"new character {name} ({account}) has progress from elsewhere: {string.Join("; ", reasons)}";
			Action action = Configuration.characterGuardNewCharacterAction.Value;
			if (action == Action.Kick)
			{
				s_rejected[name] = new Record { id = id, name = name, account = account, firstSeen = now, lastSeen = now };
			}
			else
			{
				// Logged once; from now on it is a known character.
				Remember(id, name, account, "new with progress, only logged", now);
			}
			Act(peer, session, action, what, Configuration.characterGuardNewMessage.Value);
		}

		private static void Act(ZNetPeer peer, Session session, Action action, string what, string message)
		{
			if (action == Action.Ignore)
			{
				return;
			}
			GuardLog.Write(what + (action == Action.Kick ? ": kicked" : ""));
			if (action == Action.Log)
			{
				ServersidePlugin.logger.LogWarning("Character guard: " + what);
				return;
			}
			ServersidePlugin.logger.LogWarning($"Character guard: {what}. Kicking in {KickDelaySeconds:0} s; `allow {peer.m_playerName}` in the console lets this character in.");
			// A vanilla client shows this in the middle of the screen; the kick follows so it can be read.
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.Center, message);
			session.kickAt = Time.time + KickDelaySeconds;
		}

		private static void Remember(long id, string name, string account, string reason, DateTime now)
		{
			if (!s_records.TryGetValue(id, out Record record))
			{
				s_records[id] = record = new Record { id = id, firstSeen = now, reason = reason };
				ServersidePlugin.logger.LogInfo($"Character guard: {name} ({account}) is now known: {reason}");
				GuardLog.Write($"{name} ({account}, character {id}) is now known: {reason}");
			}
			record.name = name;
			record.account = account;
			record.lastSeen = now;
			Save();
		}

		// What a fresh character could not have: progress past Eikthyr, gear past a level 1 workbench.
		private static List<string> FreshnessProblems(string[] wearing, string events, bool haveEvents)
		{
			List<string> problems = new List<string>();
			string freshEvent = Configuration.characterGuardFreshEvent.Value;
			if (haveEvents && !string.IsNullOrEmpty(freshEvent) && !Split(events).Contains(freshEvent))
			{
				problems.Add($"no longer ready for {freshEvent} (knows items past the first boss)");
			}
			List<string> gear = wearing.Where(w => IsAdvanced(w)).ToList();
			if (gear.Count > 0)
			{
				problems.Add("wears " + string.Join(", ", gear));
			}
			return problems;
		}

		private static List<string> Changes(Record known, string[] wearing, string[] progress)
		{
			List<string> changes = new List<string>();
			if (known.wearing != null)
			{
				List<string> added = wearing.Except(known.wearing).ToList();
				if (added.Count > 0)
				{
					changes.Add("now wears " + string.Join(", ", added));
				}
			}
			if (known.progress != null && progress != null)
			{
				List<string> gained = progress.Except(known.progress).ToList();
				List<string> lost = known.progress.Except(progress).ToList();
				if (gained.Count > 0 || lost.Count > 0)
				{
					changes.Add($"raid readiness changed (+{string.Join(",", gained)} -{string.Join(",", lost)})");
				}
			}
			return changes;
		}

		// Item prefab name, with the upgrade level where the game syncs one ("SwordIron@3").
		private static string[] Wearing(ZDO character)
		{
			List<string> worn = new List<string>();
			foreach (int slot in Worn)
			{
				int hash = character.GetInt(slot, 0);
				if (hash == 0)
				{
					continue;
				}
				GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
				string name = prefab ? prefab.name : hash.ToString(CultureInfo.InvariantCulture);
				if (WornQuality.TryGetValue(slot, out int qualitySlot))
				{
					int quality = character.GetInt(qualitySlot, 1);
					if (quality > 1)
					{
						name += "@" + quality.ToString(CultureInfo.InvariantCulture);
					}
				}
				worn.Add(name);
			}
			worn.Sort(StringComparer.Ordinal);
			return worn.ToArray();
		}

		// Raids whose readiness depends on the character's own progress; the rest are ready for everyone.
		private static string[] Progress(string events)
		{
			if (s_progressEvents == null)
			{
				s_progressEvents = new HashSet<string>(RandEventSystem.instance.m_events
					.Where(e => e.m_altRequiredKnownItems.Count > 0 || e.m_altRequiredNotKnownItems.Count > 0 || e.m_altRequiredPlayerKeysAny.Count > 0
						|| e.m_altRequiredPlayerKeysAll.Count > 0 || e.m_altNotRequiredPlayerKeys.Count > 0)
					.Select(e => e.m_name));
			}
			string[] progress = Split(events).Where(s_progressEvents.Contains).ToArray();
			Array.Sort(progress, StringComparer.Ordinal);
			return progress;
		}

		private static string[] Split(string list)
		{
			return string.IsNullOrEmpty(list) ? new string[0] : list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/*
			Gear a new character could not have made: anything crafted at a station other than the
			workbench, or at a workbench above level 1, and anything with no recipe at all (loot,
			boss drops). Taken from the item's easiest recipe.
		*/
		private static bool IsAdvanced(string worn)
		{
			if (s_advanced == null)
			{
				Dictionary<int, bool> table = new Dictionary<int, bool>();
				foreach (Recipe recipe in ObjectDB.instance.m_recipes)
				{
					if (recipe == null || !recipe.m_item || !recipe.m_enabled)
					{
						continue;
					}
					int hash = recipe.m_item.gameObject.name.GetStableHashCode();
					bool advanced = recipe.m_craftingStation
						&& (global::Utils.GetPrefabName(recipe.m_craftingStation.gameObject) != "piece_workbench" || recipe.m_minStationLevel > 1);
					table[hash] = table.TryGetValue(hash, out bool before) ? before && advanced : advanced;
				}
				s_advanced = table;
			}
			string name = worn.Split('@')[0];
			if (worn.Contains("@"))
			{
				// An upgraded weapon needs a workbench above level 1, or the station it came from.
				return true;
			}
			return !s_advanced.TryGetValue(name.GetStableHashCode(), out bool isAdvanced) || isAdvanced;
		}

		// Every character that has left a mark on this world: built pieces, beds, tombstones.
		private static IEnumerator CollectFootprint()
		{
			HashSet<long> ids = new HashSet<long>();
			List<ZDO> zdos = new List<ZDO>(ZDOMan.instance.m_objectsByID.Values);
			int tombstone = "Player_tombstone".GetStableHashCode();
			for (int i = 0; i < zdos.Count; i++)
			{
				ZDO zdo = zdos[i];
				long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
				if (creator != 0L)
				{
					ids.Add(creator);
				}
				// Beds and tombstones keep their owner's player id.
				long owner = zdo.GetLong(ZDOVars.s_owner, 0L);
				if (owner != 0L && (zdo.GetPrefab() == tombstone || !string.IsNullOrEmpty(zdo.GetString(ZDOVars.s_ownerName, ""))))
				{
					ids.Add(owner);
				}
				if (i % 50000 == 49999)
				{
					yield return null;
				}
			}
			s_footprint = ids;
			ServersidePlugin.logger.LogInfo($"Character guard: on. {s_records.Count} character(s) in {Path.GetFileName(s_path)}, {ids.Count} with builds, beds or tombstones in this world. "
				+ $"New characters: {Configuration.characterGuardNewCharacters.Value}, action {Configuration.characterGuardNewCharacterAction.Value}; changed while away: {Configuration.characterGuardChangedAway.Value}.");
		}

		/*
			The list lives next to the world save, one line per character, tab separated:
			id, first seen, last seen, reason, account, name, what it wore when it left, raids it was ready for then.
		*/
		private static void Load()
		{
			World world = ZNet.World;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(world.m_fileSource), world.m_name + ".characters.txt");
			s_records.Clear();
			string read = File.Exists(s_path) ? s_path : File.Exists(s_path + ".tmp") ? s_path + ".tmp" : null;
			if (read == null)
			{
				return;
			}
			if (read != s_path)
			{
				ServersidePlugin.logger.LogWarning($"Character guard: {Path.GetFileName(s_path)} is missing; reading {Path.GetFileName(read)}, left by a save that was cut short");
			}
			foreach (string line in File.ReadAllLines(read))
			{
				if (line.StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}
				string[] f = line.Split('\t');
				if (f.Length < 8 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
				{
					continue;
				}
				s_records[id] = new Record
				{
					id = id,
					firstSeen = ParseTime(f[1]),
					lastSeen = ParseTime(f[2]),
					reason = f[3],
					account = f[4],
					name = f[5],
					wearing = f[6] == "-" ? null : Split(f[6].Replace(' ', ',')),
					progress = f[7] == "-" ? null : Split(f[7]),
				};
			}
		}

		private static void Save()
		{
			if (s_path == null || s_failed)
			{
				return;
			}
			try
			{
				Write();
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Character guard: could not save {s_path}: {e.Message}");
			}
		}

		private static void Write()
		{
			List<string> lines = new List<string>
			{
				"# Sarkastic.gg Dedicated Simulation: characters known on this world (character guard). Tab separated:",
				"# id, first seen, last seen (UTC), why known, account, name, worn when last leaving, raids ready for then ('-' = not seen leaving yet).",
			};
			foreach (Record r in s_records.Values.OrderBy(r => r.firstSeen))
			{
				lines.Add(string.Join("\t", new[]
				{
					r.id.ToString(CultureInfo.InvariantCulture), FormatTime(r.firstSeen), FormatTime(r.lastSeen), Clean(r.reason), Clean(r.account), Clean(r.name),
					r.wearing == null ? "-" : string.Join(" ", r.wearing), r.progress == null ? "-" : string.Join(",", r.progress),
				}));
			}
			string temp = s_path + ".tmp";
			File.WriteAllLines(temp, lines);
			if (File.Exists(s_path))
			{
				File.Delete(s_path);
			}
			File.Move(temp, s_path);
		}

		private static string Clean(string s)
		{
			return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
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

		// The console's `allow <name>`: lets in a character that was turned away (or is online and unknown).
		public static string Allow(string name)
		{
			if (!Configuration.characterGuardEnabled.Value || s_path == null)
			{
				return "the character guard is off";
			}
			// Still online (e.g. waiting for the kick): let it stay.
			ZNetPeer peer = AdminCommands.FindPlayer(name, out string problem);
			if (peer != null && s_sessions.TryGetValue(peer, out Session session) && session.id != 0L && !session.kicked)
			{
				session.kickAt = -1f;
				s_rejected.Remove(peer.m_playerName);
				Remember(session.id, peer.m_playerName, peer.m_socket.GetHostName(), "allowed by an admin", DateTime.UtcNow);
				AllowAsItIsNow(s_records[session.id]);
				return $"{peer.m_playerName} is allowed";
			}
			if (s_rejected.TryGetValue(name, out Record rejected))
			{
				s_rejected.Remove(name);
				s_records[rejected.id] = rejected;
				AllowAsItIsNow(rejected);
				ServersidePlugin.logger.LogInfo($"Character guard: {rejected.name} ({rejected.account}) allowed by an admin");
				return $"{rejected.name} is allowed; they can join again";
			}
			if (peer == null)
			{
				return problem + (s_rejected.Count > 0 ? $". Turned away this session: {string.Join(", ", s_rejected.Keys)}" : "");
			}
			return $"{peer.m_playerName} has not been checked yet; try again in a few seconds";
		}

		// A character an admin let in counts as it is now: what it wears and knows when it next leaves is the new reference.
		private static void AllowAsItIsNow(Record record)
		{
			record.reason = "allowed by an admin";
			record.wearing = null;
			record.progress = null;
			Save();
		}

		// The console's `characters`: who is online and how the guard sees them.
		public static string Describe()
		{
			if (!Configuration.characterGuardEnabled.Value || s_path == null)
			{
				return "the character guard is off";
			}
			List<string> parts = new List<string>();
			foreach (KeyValuePair<ZNetPeer, Session> kv in s_sessions)
			{
				string state = !kv.Value.decided ? "not checked yet"
					: kv.Value.kickAt >= 0f || kv.Value.kicked ? "being kicked"
					: s_records.TryGetValue(kv.Value.id, out Record r) ? "known: " + r.reason : "unknown";
				parts.Add($"{kv.Key.m_playerName}: {state}");
			}
			return $"{s_records.Count} known character(s) in {Path.GetFileName(s_path)}. Online: {(parts.Count > 0 ? string.Join("; ", parts) : "nobody")}"
				+ (s_rejected.Count > 0 ? $". Turned away: {string.Join(", ", s_rejected.Keys)}" : "");
		}

		[HarmonyPatch(typeof(ZNet), "Disconnect")]
		public static class ZNet_Disconnect_Patch
		{
			// What the character wears and knows as it leaves; a different state on its return means it was played elsewhere.
			static void Prefix(ZNetPeer peer)
			{
				try
				{
					if (!ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null || !ObjectDB.instance || !RandEventSystem.instance
						|| peer == null || !s_sessions.TryGetValue(peer, out Session session))
					{
						return;
					}
					if (session.decided && !session.kicked && s_records.ContainsKey(session.id))
					{
						Snapshot(peer, session);
						Save();
					}
				}
				catch (Exception e)
				{
					Warn($"could not note what {peer?.m_playerName} wore when leaving", e);
				}
			}
		}
	}
}
