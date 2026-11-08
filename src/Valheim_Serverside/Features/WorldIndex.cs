using FeaturesLib;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		The objects of the few kinds the guards look up again and again: the wards players own (PrivateArea of the players'
		faction), the portals (TeleportWorld) and the guardian stones (BossStone). Each was found by looking through the whole
		world -- the ward guard every 5 s, the portal lock every 10 s, the guardian stones every 60 s -- and the live world has
		891 000 objects: 35-45 ms each time, in one frame. And the objects that can have a builder, for the decay: what has a
		Piece (the game sets a builder in Piece.SetCreator alone), and what the game does not know or that has no ZNetView
		(an object of a mod that is gone), which the decay reads as it always did.

		Here they are found once, after the world has loaded, and kept up to date as the game makes and takes away objects:
		- made: ZDOMan.AddIfPortal, which every new object goes through with its prefab, both what the server makes
		  (CreateNewZDO, also for what a plugin makes) and what a player's game sends (RPC_ZDOData, after Deserialize, so the
		  prefab is known; it is called again with every later update of the object, which changes nothing here);
		- taken away: ZDOMan.m_onZDODestroyed, which the game calls for every object it removes (HandleDestroyedZDO, the only
		  place an object leaves the world while it runs);
		- loaded: the world is read in all at once (ZDOMan.LoadChunks or Load), past both of those; the index is made again
		  after it.
		A new ward protects at once, not up to 5 s later.

		To be sure nothing goes past the hooks, every 10 min the whole world is looked through again and the index compared
		with what was seen, both in slices (at most 20 000 objects or entries a frame, under 1 ms on the test machine), and
		what the index had wrong is put right and written to the log. Should the hooks not be in place (a game update renamed
		a method), the index is made again by looking through the world whenever it is asked for and is more than 5 s old:
		what the guards did before, once for all of them.

		The lists handed out are never changed afterwards: a change makes a new list, so a guard can go through one while
		objects are made or taken away. They hold ZDOIDs (the game reuses ZDO objects), and a ZDO that has gone since is null.
	*/
	public class WorldIndex : IFeature
	{
		public bool FeatureEnabled()
		{
			return true;
		}

		public enum Kind { Ward = 0, Portal = 1, BossStone = 2, Built = 3 }
		private const int Kinds = 4;
		private static readonly string[] s_kindNames = { "ward(s)", "portal(s)", "guardian stone(s)", "object(s) that can have a builder" };

		private const float AuditSeconds = 600f;
		private const int AuditPerFrame = 20000;
		private const float FallbackSeconds = 5f;

		// Per prefab, the kinds its objects are of: bit k for Kind k (a ward or a portal is also a piece).
		private static readonly Dictionary<int, int> s_kindsOf = new Dictionary<int, int>();
		private static readonly HashSet<ZDOID>[] s_sets = { new HashSet<ZDOID>(), new HashSet<ZDOID>(), new HashSet<ZDOID>(), new HashSet<ZDOID>() };
		private static readonly List<ZDOID>[] s_lists = { new List<ZDOID>(), new List<ZDOID>(), new List<ZDOID>(), new List<ZDOID>() };
		private static readonly bool[] s_dirty = new bool[Kinds];
		private static readonly List<ZDOID> s_empty = new List<ZDOID>();
		private static ZDOMan s_man;
		private static bool s_built, s_hooked, s_classified, s_logged;
		private static float s_builtAt, s_nextAudit;

		// The audit: what it is doing (0 waiting, 1 looking through the world, 2 comparing the index with what it saw, 3 done),
		// where it is, what it has seen and found, and what it cost.
		// s_auditIds holds the index's entries of the kind being compared (s_auditCount of them, -1 before they are copied);
		// it and s_seen are made big enough when the index is made, so the audit itself allocates nothing big.
		private static int s_auditPhase, s_auditSector, s_auditKind, s_auditPos, s_auditCount, s_auditFrames, s_auditObjects, s_audits;
		private static ZDOID[] s_auditIds = new ZDOID[0];
		private static double s_auditMs, s_auditWorstMs;
		private static readonly HashSet<ZDOID>[] s_seen = { new HashSet<ZDOID>(), new HashSet<ZDOID>(), new HashSet<ZDOID>(), new HashSet<ZDOID>() };
		private static readonly List<ZDOID>[] s_missing = { new List<ZDOID>(), new List<ZDOID>(), new List<ZDOID>(), new List<ZDOID>() };
		private static readonly int[] s_missingCount = new int[Kinds], s_staleCount = new int[Kinds];

		public static List<ZDOID> Wards => Get(Kind.Ward);
		public static List<ZDOID> Portals => Get(Kind.Portal);
		public static List<ZDOID> BossStones => Get(Kind.BossStone);
		// In the order the world had them when the index was made; what is made later goes anywhere in it.
		public static List<ZDOID> Built => Get(Kind.Built);

		// True once the index can answer: the world and the prefabs are there. Makes it on the first call.
		public static bool Ready()
		{
			if (ZDOMan.instance == null || !ZNetScene.instance || ZNetScene.instance.m_prefabs == null || ZNetScene.instance.m_prefabs.Count == 0)
			{
				return false;
			}
			if (!s_built || s_man != ZDOMan.instance || (!s_hooked && Time.time - s_builtAt >= FallbackSeconds))
			{
				Build();
			}
			return true;
		}

		private static List<ZDOID> Get(Kind kind)
		{
			return Ready() ? Snapshot((int)kind) : s_empty;
		}

		private static List<ZDOID> Snapshot(int k)
		{
			if (s_dirty[k])
			{
				s_lists[k] = new List<ZDOID>(s_sets[k]);
				s_dirty[k] = false;
			}
			return s_lists[k];
		}

		private static int KindsOf(int prefab)
		{
			if (s_kindsOf.TryGetValue(prefab, out int kinds))
			{
				return kinds;
			}
			// A prefab registered after the first look (or one the game does not have): looked at once.
			kinds = Classify(ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null);
			s_kindsOf[prefab] = kinds;
			return kinds;
		}

		private static int Bit(Kind kind)
		{
			return 1 << (int)kind;
		}

		private static int Classify(GameObject prefab)
		{
			if (!prefab || !prefab.GetComponent<ZNetView>())
			{
				return Bit(Kind.Built);
			}
			int kinds = 0;
			if (prefab.GetComponentInChildren<global::Piece>(true))
			{
				kinds |= Bit(Kind.Built);
			}
			PrivateArea area = prefab.GetComponent<PrivateArea>();
			if (area && area.m_ownerFaction == Character.Faction.Players)
			{
				kinds |= Bit(Kind.Ward);
			}
			if (prefab.GetComponent<TeleportWorld>())
			{
				kinds |= Bit(Kind.Portal);
			}
			if (prefab.GetComponent<BossStone>())
			{
				kinds |= Bit(Kind.BossStone);
			}
			return kinds;
		}

		private static void Add(HashSet<ZDOID>[] sets, int kinds, ZDOID id, bool dirty)
		{
			for (int k = 0; kinds != 0; k++, kinds >>= 1)
			{
				if ((kinds & 1) != 0 && sets[k].Add(id) && dirty)
				{
					s_dirty[k] = true;
				}
			}
		}

		private static void Build()
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			if (!s_classified)
			{
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					if (prefab)
					{
						s_kindsOf[prefab.name.GetStableHashCode()] = Classify(prefab);
					}
				}
				s_classified = true;
			}
			if (s_man != ZDOMan.instance)
			{
				s_man = ZDOMan.instance;
				s_man.m_onZDODestroyed = (Action<ZDO>)Delegate.Combine(s_man.m_onZDODestroyed, new Action<ZDO>(OnDestroyed));
				s_hooked = HooksInPlace();
			}
			for (int k = 0; k < Kinds; k++)
			{
				s_sets[k].Clear();
				s_dirty[k] = true;
			}
			foreach (ZDO zdo in s_man.m_objectsByID.Values)
			{
				Add(s_sets, KindsOf(zdo.GetPrefab()), zdo.m_uid, false);
			}
			if (s_hooked)
			{
				// Room for what the audit keeps, with a quarter to spare for what is built later.
				int most = 0;
				for (int k = 0; k < Kinds; k++)
				{
					int room = s_sets[k].Count + s_sets[k].Count / 4 + 64;
					s_seen[k] = new HashSet<ZDOID>(room);
					most = Math.Max(most, room);
				}
				s_auditIds = new ZDOID[most];
			}
			s_built = true;
			s_builtAt = Time.time;
			s_nextAudit = Time.time + AuditSeconds;
			s_auditPhase = 0;
			s_auditCount = -1;
			if (!s_logged)
			{
				s_logged = true;
				ServersidePlugin.logger.LogInfo($"World index: {Counts()} among {s_man.m_objectsByID.Count} objects, found in {watch.Elapsed.TotalMilliseconds:0.0} ms; "
					+ (s_hooked
						? "kept up to date as objects are made and taken away, and checked against the whole world every 10 min"
						: $"the game's methods it hooks are not patched, so it is made again from the whole world when it is more than {FallbackSeconds:0} s old"));
			}
		}

		private static string Counts()
		{
			return string.Join(", ", Enumerable.Range(0, Kinds).Select(k => $"{s_sets[k].Count} {s_kindNames[k]}"));
		}

		private static bool HooksInPlace()
		{
			try
			{
				Patches info = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZDOMan), "AddIfPortal"));
				return info != null && info.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(TrackMade));
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void Made(ZDO zdo, int prefab)
		{
			if (!s_built || s_man != ZDOMan.instance || zdo == null || prefab == 0)
			{
				return;
			}
			Add(s_sets, KindsOf(prefab), zdo.m_uid, true);
			OnMade?.Invoke(zdo, prefab);
		}

		// For other features that follow objects as they are made and taken away (the ward guard: who planted a crop). Made is called
		// from the game's AddIfPortal (also with every later update of an object a player's game sends), Gone inside HandleDestroyedZDO:
		// a handler must not throw.
		internal static event Action<ZDO, int> OnMade;
		internal static event Action<ZDO> OnGone;

		// Called by the game inside HandleDestroyedZDO: an exception here would stop the game removing the object.
		private static void OnDestroyed(ZDO zdo)
		{
			try
			{
				if (!s_built || zdo == null)
				{
					return;
				}
				int kinds = KindsOf(zdo.GetPrefab());
				for (int k = 0; kinds != 0; k++, kinds >>= 1)
				{
					if ((kinds & 1) != 0 && s_sets[k].Remove(zdo.m_uid))
					{
						s_dirty[k] = true;
					}
				}
				OnGone?.Invoke(zdo);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"World index: {e.Message}");
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "AddIfPortal")]
		public static class TrackMade
		{
			private static void Postfix(ZDO zdo, int prefabHash)
			{
				Made(zdo, prefabHash);
			}
		}

		// The world read in (also again, after a reset): what is in the index is of another world.
		[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.LoadChunks))]
		public static class LoadedChunks
		{
			private static void Postfix()
			{
				s_built = false;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Load))]
		public static class Loaded
		{
			private static void Postfix()
			{
				s_built = false;
			}
		}

		// Called from ServersidePlugin.Update: the check against the whole world, a slice a frame.
		public static void Tick()
		{
			if (!s_built || !s_hooked || s_man == null || s_man != ZDOMan.instance)
			{
				return;
			}
			if (s_auditPhase == 0 && Time.time < s_nextAudit)
			{
				return;
			}
			// What a frame of the audit costs: all of it but writing to the log.
			long start = System.Diagnostics.Stopwatch.GetTimestamp();
			if (s_auditPhase == 0)
			{
				s_auditPhase = 1;
				s_auditSector = s_auditKind = s_auditPos = s_auditFrames = s_auditObjects = 0;
				s_auditCount = -1;
				s_auditMs = s_auditWorstMs = 0;
				for (int k = 0; k < Kinds; k++)
				{
					s_seen[k].Clear();
					s_missing[k].Clear();
					s_missingCount[k] = s_staleCount[k] = 0;
				}
			}
			if (s_auditPhase == 1)
			{
				LookThrough();
			}
			else
			{
				Compare();
			}
			if (s_auditPhase == 3)
			{
				for (int k = 0; k < Kinds; k++)
				{
					s_seen[k].Clear();
					s_missing[k].Clear();
				}
			}
			double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			s_auditMs += ms;
			s_auditWorstMs = Math.Max(s_auditWorstMs, ms);
			s_auditFrames++;
			if (s_auditPhase == 3)
			{
				EndAudit();
			}
		}

		// 1: a slice of the sectors, at most AuditPerFrame objects, then the portals, which the game keeps apart from them. What it
		// sees and the index lacks is noted.
		private static void LookThrough()
		{
			List<ZDO>[] sectors = s_man.m_objectsBySector;
			int visited = 0;
			while (s_auditSector < sectors.Length && visited < AuditPerFrame)
			{
				List<ZDO> list = sectors[s_auditSector++];
				if (list == null)
				{
					continue;
				}
				foreach (ZDO zdo in list)
				{
					Saw(zdo);
				}
				visited += list.Count;
			}
			if (s_auditSector >= sectors.Length)
			{
				foreach (List<ZDO> list in s_man.m_portalObjects.Values)
				{
					foreach (ZDO zdo in list)
					{
						Saw(zdo);
					}
					visited += list.Count;
				}
				s_auditPhase = 2;
			}
			s_auditObjects += visited;
		}

		private static void Saw(ZDO zdo)
		{
			int kinds = KindsOf(zdo.GetPrefab());
			for (int k = 0; kinds != 0; k++, kinds >>= 1)
			{
				if ((kinds & 1) != 0 && s_seen[k].Add(zdo.m_uid) && !s_sets[k].Contains(zdo.m_uid))
				{
					s_missing[k].Add(zdo.m_uid);
				}
			}
		}

		// 2: kind by kind, what was seen and the index lacks goes in (unless it has gone again, or the game has put it in since),
		// then the index's entries, at most AuditPerFrame a frame: one that was not seen goes out if no object of that kind has
		// that id any more. (An object made while the look went on, in a sector it had passed, was not seen either, and stays.)
		private static void Compare()
		{
			int done = 0;
			while (s_auditKind < Kinds && done < AuditPerFrame)
			{
				int k = s_auditKind, bit = 1 << k;
				if (s_auditCount < 0)
				{
					foreach (ZDOID id in s_missing[k])
					{
						ZDO zdo = s_man.GetZDO(id);
						if (zdo != null && (KindsOf(zdo.GetPrefab()) & bit) != 0 && s_sets[k].Add(id))
						{
							s_missingCount[k]++;
							s_dirty[k] = true;
						}
					}
					done += s_missing[k].Count;
					// A copy, so it can be gone through over several frames while the set changes.
					HashSet<ZDOID> set = s_sets[k];
					if (s_auditIds.Length < set.Count)
					{
						s_auditIds = new ZDOID[set.Count + set.Count / 4 + 64];
					}
					set.CopyTo(s_auditIds);
					s_auditCount = set.Count;
					s_auditPos = 0;
				}
				while (s_auditPos < s_auditCount && done < AuditPerFrame)
				{
					ZDOID id = s_auditIds[s_auditPos++];
					done++;
					if (s_seen[k].Contains(id))
					{
						continue;
					}
					ZDO zdo = s_man.GetZDO(id);
					if ((zdo == null || (KindsOf(zdo.GetPrefab()) & bit) == 0) && s_sets[k].Remove(id))
					{
						s_staleCount[k]++;
						s_dirty[k] = true;
					}
				}
				if (s_auditPos >= s_auditCount)
				{
					s_auditKind++;
					s_auditCount = -1;
				}
			}
			if (s_auditKind >= Kinds)
			{
				s_auditPhase = 3;
			}
		}

		private static void EndAudit()
		{
			List<string> wrong = new List<string>();
			for (int k = 0; k < Kinds; k++)
			{
				if (s_missingCount[k] > 0 || s_staleCount[k] > 0)
				{
					wrong.Add($"{s_kindNames[k]}: {s_missingCount[k]} not in the index, {s_staleCount[k]} in it of no such object");
				}
			}
			s_audits++;
			s_auditPhase = 0;
			s_auditCount = -1;
			s_nextAudit = Time.time + AuditSeconds;
			string cost = $"{s_auditObjects} objects in {s_auditFrames} frames, {s_auditMs:0.0} ms in all, at most {s_auditWorstMs:0.00} ms in a frame";
			if (wrong.Count > 0)
			{
				ServersidePlugin.logger.LogWarning($"World index: the check against the whole world ({cost}) found the index wrong ({string.Join("; ", wrong)}), put right: "
					+ "something makes or takes away objects past the game's ZDOMan.AddIfPortal and m_onZDODestroyed");
			}
			else if (s_audits == 1)
			{
				ServersidePlugin.logger.LogInfo($"World index: checked against the whole world ({cost}): right ({Counts()})");
			}
		}
	}
}
