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

		How far each player has got is read from their own client's raid list (Progress, shared with
		the guardian stones): exact for every progress level, and other people's powers taken at a stone
		cannot lift a player at or below the Elder. Fader leaves nothing of his own in that list, so his
		Charred count as the Queen's. A player whose list has not arrived yet counts as having beaten
		nothing.
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

		// A key no world has: a spawn that needs it behaves as in a world where the boss still lives.
		private const string HeldBackKey = "nightspawnguard-held-back";

		private static readonly List<KeyValuePair<SpawnSystem.SpawnData, string>> s_heldBack = new List<KeyValuePair<SpawnSystem.SpawnData, string>>();
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		private static int s_errors;

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
				int beaten = player && byCharacter.TryGetValue(player.GetZDOID(), out ZNetPeer peer) ? Progress.Beaten(peer) : 0;
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
							if (!Progress.Readable() || SpawnSystem.m_tempNearPlayers.Count == 0)
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
								+ $"-- it needs {Progress.BossNames[needed]} beaten, the least progressed of them has beaten {Progress.BossNames[least]}");
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
