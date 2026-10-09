using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Valheim_Serverside.Features
{
	/*
		Vanilla's ambient spawns that come with boss progress (Greydwarfs at night once Eikthyr is
		dead, Draugr/Goblin/Greydwarf Elite and Shaman once the Elder is, Skeletons once Bonemass
		is, Goblins once Yagluth is, Seekers/Seeker Brood/Ticks once the Queen is, Charred once Fader
		is) are each gated by a single world-wide key (SpawnSystem.SpawnData.m_requiredGlobalKey,
		set the moment ANY player beats that boss anywhere), with no per-player alternative. A new
		player on a world a veteran has pushed far ahead meets monsters they could not have prepared
		for.

		What this adds: those spawns, around a group of players, only once every player in the
		spawning zone (vanilla's own 64x64 m candidate set: a spawn is placed 40-80 m from one of
		them) has personally got that far. Otherwise the entry behaves for that zone exactly as in a
		world where the boss still lives -- its timer runs and it spawns nothing. Raids are left
		alone: who they target is already decided per player (`-setkey playerevents`).

		"Personally got that far" is read from the one thing a vanilla client tells the server about
		its own progress: the list of raids it is still "ready for", which the client works out
		from its own known items and keys and sends as ZNet.m_serverSyncedPlayerData["possibleEvents"]
		(RandEventSystem.PlayerIsReadyForEvent, the same list CharacterGuard reads). The main raids
		form a chain -- each needs the previous boss's drop known and stops once the next boss's drop
		is -- so the first chain raid still on the list tells how far the player is. Two raids that
		need a boss's drop actually known (foresttrolls: the Elder's, surtlings: Bonemass's) confirm
		the step, because the chain raids also stop for a player who merely holds that boss's power,
		and a power can be taken from a trophy someone else hung. Everything was checked against a
		port of PlayerIsReadyForEvent over the game's raid data (1.0.16): exact for every progress
		level; taking other people's powers cannot lift a player at or below the Elder; above that,
		only taking the Queen's power plus every power before it in the chain (from Bonemass on:
		Moder's, from Moder on: Yagluth's) makes a player look past the Queen. Fader has nothing
		of his own in the list, so his Charred count as the Queen's.

		A player whose list has not arrived yet counts as having beaten nothing.
	*/
	public class NightSpawnGuard : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.nightSpawnGuardEnabled.Value;
		}

		// How many bosses, in the game's order, a player must have beaten before the spawns behind each key appear around them.
		private static readonly Dictionary<string, int> s_needed = new Dictionary<string, int>
		{
			{ "defeated_eikthyr", 1 },
			{ "defeated_gdking", 2 },
			{ "defeated_bonemass", 3 },
			{ "defeated_dragon", 4 },
			{ "defeated_goblinking", 5 },
			{ "defeated_queen", 6 },
			{ "defeated_fader", 6 }, // nothing of Fader's own in the list; the Queen is the closest
		};

		private static readonly string[] s_bossNames = { "nothing", "Eikthyr", "the Elder", "Bonemass", "Moder", "Yagluth", "the Queen" };

		// The raids the progress is read from; every one must exist in the game with a per-player condition.
		private static readonly string[] s_evidence = { "army_eikthyr", "army_theelder", "foresttrolls", "army_bonemass", "surtlings", "army_moder", "army_goblin", "army_gjall", "army_seekers", "gemgoblin" };

		// A key no world has: a spawn that needs it behaves as in a world where the boss still lives.
		private const string HeldBackKey = "nightspawnguard-held-back";

		// Raids with no per-player condition at all: every vanilla client lists them, so a list without any is not one.
		private static HashSet<string> s_alwaysReady;
		private static bool s_checked, s_usable;

		private static readonly List<KeyValuePair<SpawnSystem.SpawnData, string>> s_heldBack = new List<KeyValuePair<SpawnSystem.SpawnData, string>>();
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		private static int s_errors;

		/*
			The game's raid data must still be what the rule was worked out against; if it is not,
			the guard stays out of the way (vanilla spawns) rather than guess.
		*/
		private static bool Usable()
		{
			if (s_checked)
			{
				return s_usable;
			}
			if (!RandEventSystem.instance)
			{
				return false;
			}
			s_checked = true;
			List<RandomEvent> events = RandEventSystem.instance.m_events;
			string[] missing = s_evidence.Where(name => !events.Any(ev => ev.m_name == name && HasPlayerCondition(ev))).ToArray();
			s_alwaysReady = new HashSet<string>(events.Where(ev => !HasPlayerCondition(ev)).Select(ev => ev.m_name));
			s_usable = missing.Length == 0 && s_alwaysReady.Count > 0;
			if (!s_usable)
			{
				ServersidePlugin.logger.LogWarning($"Night spawn guard: the game's raid data is not what the progress rule was made for "
					+ $"(missing or changed: {(missing.Length > 0 ? string.Join(", ", missing) : "no raid without a per-player condition")}); the guard does nothing and spawns stay vanilla.");
			}
			return s_usable;
		}

		private static bool HasPlayerCondition(RandomEvent ev)
		{
			return ev.m_altRequiredKnownItems.Count > 0 || ev.m_altRequiredNotKnownItems.Count > 0 || ev.m_altNotRequiredPlayerKeys.Count > 0
				|| ev.m_altRequiredPlayerKeysAny.Count > 0 || ev.m_altRequiredPlayerKeysAll.Count > 0;
		}

		// How many bosses this player has beaten, in the game's order, as far as their own raid list shows.
		internal static int Beaten(string possibleEvents)
		{
			if (string.IsNullOrEmpty(possibleEvents))
			{
				return 0;
			}
			HashSet<string> ready = new HashSet<string>(possibleEvents.Split(','));
			if (!ready.Overlaps(s_alwaysReady))
			{
				return 0;
			}
			if (ready.Contains("army_eikthyr")) return 0;
			if (ready.Contains("army_theelder")) return 1;
			if (!ready.Contains("foresttrolls")) return 0; // past the Elder's raid without the Elder's drop: a power taken, not a kill
			if (ready.Contains("army_bonemass")) return 2;
			if (!ready.Contains("surtlings")) return 2; // the same for Bonemass
			if (ready.Contains("army_moder")) return 3;
			if (ready.Contains("army_goblin")) return 4;
			if (ready.Contains("army_gjall") || ready.Contains("army_seekers")) return 5;
			if (ready.Contains("gemgoblin")) return 3; // past Moder's and Yagluth's raids with neither shown by a drop: cannot tell which
			return 6;
		}

		// The least progressed player in the zone decides; one the server cannot match to a peer counts as having beaten nothing.
		private static int LeastBeaten(List<Player> players)
		{
			Dictionary<ZDOID, ZNetPeer> byCharacter = new Dictionary<ZDOID, ZNetPeer>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				byCharacter[peer.m_characterID] = peer;
			}
			int least = int.MaxValue;
			foreach (Player player in players)
			{
				int beaten = 0;
				if (player && byCharacter.TryGetValue(player.GetZDOID(), out ZNetPeer peer) && peer.m_serverSyncedPlayerData.TryGetValue("possibleEvents", out string events))
				{
					beaten = Beaten(events);
				}
				least = Math.Min(least, beaten);
			}
			return least;
		}

		[HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
		public static class SpawnSystem_UpdateSpawnList_Patch
		{
			static void Prefix(List<SpawnSystem.SpawnData> spawners, bool eventSpawners)
			{
				if (eventSpawners || !Configuration.nightSpawnGuardEnabled.Value || !ZNet.instance)
				{
					return;
				}
				try
				{
					int least = -1;
					foreach (SpawnSystem.SpawnData spawner in spawners)
					{
						if (spawner.m_requiredGlobalKey == null || !s_needed.TryGetValue(spawner.m_requiredGlobalKey, out int needed))
						{
							continue;
						}
						if (least < 0)
						{
							// UpdateSpawning has just filled this with the players in the zone (the same list it spawns around).
							if (!Usable() || SpawnSystem.m_tempNearPlayers.Count == 0)
							{
								return;
							}
							least = LeastBeaten(SpawnSystem.m_tempNearPlayers);
						}
						if (least >= needed)
						{
							continue;
						}
						s_heldBack.Add(new KeyValuePair<SpawnSystem.SpawnData, string>(spawner, spawner.m_requiredGlobalKey));
						spawner.m_requiredGlobalKey = HeldBackKey;
						if (s_logged.Add(spawner.m_name + "/" + least))
						{
							ServersidePlugin.logger.LogInfo($"Night spawn guard: holding back {spawner.m_name} near {SpawnSystem.m_tempNearPlayers.Count} player(s) "
								+ $"-- it needs {s_bossNames[needed]} beaten, the least progressed of them has beaten {s_bossNames[least]}");
						}
					}
				}
				catch (Exception e)
				{
					Restore();
					if (s_errors++ < 5)
					{
						ServersidePlugin.logger.LogWarning($"Night spawn guard: {e}");
					}
				}
			}

			// Always runs, also when the spawn code throws: the shared spawn table must never keep the stand-in key.
			static void Finalizer()
			{
				Restore();
			}
		}

		private static void Restore()
		{
			foreach (KeyValuePair<SpawnSystem.SpawnData, string> held in s_heldBack)
			{
				held.Key.m_requiredGlobalKey = held.Value;
			}
			s_heldBack.Clear();
		}
	}
}
