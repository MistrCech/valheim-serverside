using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Fixes for vanilla server behaviour that loses data or leaves players with a stale view.

		SaveClientChanges: Valheim 1.0 saves the world in chunks and only rewrites the chunks it
		marked as changed. A chunk is marked when the server itself changes an object
		(ZDO.IncreaseDataRevision) or an object enters or leaves it. A change that arrives from a
		player for an object the player owns is applied through ZDO.Deserialize, which marks
		nothing, so until something else in that chunk changes the new state exists only in memory
		and is gone after a restart. With this mod the server owns nearly everything near players,
		so the window is small, but a player still owns what they just built, the ship they steer
		and their own drops. Reported for 1.0 by ValheimCommunityPatch ("Fix Unsaved Client Changes").

		TeleportGhosts: a player who teleports (portal, respawn) stays visible to the players near
		the old spot, frozen, until they next cross a zone line. ZDO.InternalSetPosition files the
		object under its new sector before it stores the new position, and the check that tells
		each player to drop objects that left their area (ZDOPeer.ZDOSectorInvalidated) reads the
		position: it still sees the old spot, inside the other player's area, and queues nothing.
		Re-running the check once the position is stored tells them. A player already told has no
		entry left to match, so nothing is sent twice. Reported for 1.0 by ValheimCommunityPatch
		("Fix Teleport Ghost Players").

		ServerSmoke: smoke is what smothers a fireplace in vanilla -- every smoke source counts
		itself blocked with smoke within 0.75 m of it, and a fireplace whose source has been blocked for
		4 s goes out until the air clears (SmokeSpawner.IsBlocked) -- and what chokes a fire under a
		roof (Fire.UpdateFire: more than 7 puffs near it). A smoke source only puffs while the local
		player is within 64 m (SmokeSpawner.Spawn). A dedicated server has none, and with this mod the
		server runs the fires and fireplaces near players, so it never saw smoke: a fireplace every
		player sees smothered kept burning on the server, using fuel, throwing sparks and setting what
		touches it alight. Here a smoke source puffs on the server while any player is within the same
		64 m, and the game's cap of 100 puffs at a time -- per player's computer in vanilla, each seeing
		only its own surroundings -- is counted once per connected player.

		AntiSting: the Anti-Sting Concoction makes Deathsquitos flee the player who drank it. At the
		moment one would sting, the game that runs its AI looks through that player's status effects
		(MonsterAI.PheromoneFleeCheck), and status effects exist only in the drinking player's own
		game: the network carries none of them. With this mod the server runs the Deathsquitos, so
		they stung everyone who drank it (MistrCech/valheim-serverside#6). What the server does see
		is the drink's visual effect: the status effect starts its effects on the player
		(StatusEffect.TriggerStartEffects), the Concoction's glow is a networked object that lasts
		as long as the drink does and is destroyed when it ends or the player dies
		(RemoveStartEffects), or when the player logs out (ZDOMan.RemoveOrphanNonPersistentZDOS), and
		the game writes into it which player it belongs to (EffectList.Create, ownerZDOUserId and
		ownerZDOId). While such an object of the player's game exists for a player, the server
		answers that check as the player's own game would. Which drinks do this, which creature they
		are for and which of their effects shows it is read from the game's items, not written here.
		(The bottle the game means to show in the player's hand while drinking never reaches the
		network: Humanoid.UseItem looks the item up by its shared data, which an item in an
		inventory has a copy of, so the lookup fails.)
	*/
	public class Fixes : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fixSaveClientChanges.Value || Configuration.fixTeleportGhosts.Value || Configuration.fixServerSmoke.Value || Configuration.fixAntiSting.Value;
		}

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static void Postfix(ZDO __instance)
			{
				if (Configuration.fixSaveClientChanges.Value && __instance.Persistent && ZNet.instance && ZNet.instance.IsServer() && ZDOMan.instance != null)
				{
					ZDOMan.instance.SetDirtySector(__instance);
				}
				if (Configuration.fixAntiSting.Value && ZNet.instance && ZNet.instance.IsServer())
				{
					AntiSting.Seen(__instance);
				}
			}
		}

		[HarmonyPatch(typeof(ZDO), "InternalSetPosition")]
		public static class ZDO_InternalSetPosition_Patch
		{
			static void Prefix(ZDO __instance, out ZoneSystem.SectorIndex __state)
			{
				__state = FiledSector(__instance);
			}

			static void Postfix(ZDO __instance, ZoneSystem.SectorIndex __state)
			{
				if (!Configuration.fixTeleportGhosts.Value || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
				{
					return;
				}
				// Portals are not filed by sector at all (ZDO.SetSector returns early for them).
				if (FiledSector(__instance) == __state || (Game.instance && Game.instance.PortalPrefabHash.Contains(__instance.GetPrefab())))
				{
					return;
				}
				ZDOMan.instance.ZDOSectorInvalidated(__instance);
			}

			// The sector the object is filed under, computed the way ZDO.SetSector does.
			private static ZoneSystem.SectorIndex FiledSector(ZDO zdo)
			{
				return zdo.OutsideZones ? ZoneSystem.SectorZero : zdo.GetSectorIndex();
			}
		}

		// SmokeSpawner.Spawn reads Player.m_localPlayer once, to see whether anyone is close enough to see the smoke.
		[HarmonyPatch(typeof(SmokeSpawner), "Spawn")]
		public static class SmokeSpawner_Spawn_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
			{
				FieldInfo localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				MethodInfo watcher = AccessTools.Method(typeof(SmokeSpawner_Spawn_Patch), nameof(Watcher));
				List<CodeInstruction> originalCodes = instructions.ToList();
				List<CodeInstruction> codes = new List<CodeInstruction>(originalCodes);
				int replaced = 0;
				for (int i = 0; i < codes.Count; i++)
				{
					if (!codes[i].LoadsField(localPlayer))
					{
						continue;
					}
					CodeInstruction self = new CodeInstruction(OpCodes.Ldarg_0);
					self.labels.AddRange(codes[i].labels);
					self.blocks.AddRange(codes[i].blocks);
					codes[i] = new CodeInstruction(OpCodes.Call, watcher);
					codes.Insert(i, self);
					i++;
					replaced++;
				}
				if (replaced != 1)
				{
					// Not thrown: that would switch off the other fixes with it.
					ServersidePlugin.logger.LogWarning($"{original.DeclaringType.Name}.{original.Name}: found {replaced} Player.m_localPlayer read(s), expected 1. The game changed this method; server smoke is off.");
					return originalCodes;
				}
				return codes;
			}

			private static bool s_noRenderer;

			// The local player where there is one; on the server, the nearest player within the game's own 64 m, if any.
			public static Player Watcher(SmokeSpawner spawner)
			{
				if (Player.m_localPlayer || !Configuration.fixServerSmoke.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return Player.m_localPlayer;
				}
				// Every puff registers with the scene's smoke renderer as it is created; without one there can be no smoke.
				if (!SmokeRenderer.Instance)
				{
					if (!s_noRenderer)
					{
						s_noRenderer = true;
						ServersidePlugin.logger.LogWarning("Server smoke: this server has no smoke renderer, so it cannot make smoke; fires burn as before.");
					}
					return null;
				}
				Vector3 at = spawner.transform.position;
				Player nearest = null;
				float best = 64f;
				foreach (Player player in Player.GetAllPlayers())
				{
					float distance = player ? Vector3.Distance(player.transform.position, at) : float.MaxValue;
					if (distance <= best)
					{
						best = distance;
						nearest = player;
					}
				}
				return nearest;
			}
		}

		[HarmonyPatch(typeof(Smoke), nameof(Smoke.FadeOldest))]
		public static class Smoke_FadeOldest_Patch
		{
			static bool Prefix()
			{
				if (Player.m_localPlayer || !Configuration.fixServerSmoke.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				return Smoke.GetTotalSmoke() > 100 * Math.Max(1, ZNet.instance.GetPeers().Count);
			}
		}

		// The drinks that make a creature flee, and the players under one (see AntiSting above).
		public static class AntiSting
		{
			private struct Glow
			{
				public ZDOID player;
				public long owner;
				public HashSet<string> creatures;
			}

			// By the hash of a networked effect that a drink's status effect starts on the player and keeps while it lasts: the creatures it makes flee.
			// Null until the items are loaded.
			private static Dictionary<int, HashSet<string>> s_signs;
			// Such effects that players' games created, by the effect's object.
			private static readonly Dictionary<ZDOID, Glow> s_glows = new Dictionary<ZDOID, Glow>();
			private static readonly List<ZDOID> s_gone = new List<ZDOID>();
			private static bool s_failed;

			private static Dictionary<int, HashSet<string>> Signs()
			{
				if (s_signs != null || !ObjectDB.instance || ObjectDB.instance.m_items.Count == 0)
				{
					return s_signs;
				}
				s_signs = new Dictionary<int, HashSet<string>>();
				List<string> found = new List<string>();
				foreach (GameObject prefab in ObjectDB.instance.m_items)
				{
					ItemDrop drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
					// Consumables only: a mead base names the mead's effect too, but cannot be drunk.
					SE_Stats effect = drop && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable ? drop.m_itemData.m_shared.m_consumeStatusEffect as SE_Stats : null;
					Character creature = effect && effect.m_pheromoneFlee && effect.m_pheromoneTarget ? effect.m_pheromoneTarget.GetComponent<Character>() : null;
					if (!creature)
					{
						continue;
					}
					List<string> shownBy = new List<string>();
					foreach (EffectList.EffectData data in effect.m_startEffects?.m_effectPrefabs ?? new EffectList.EffectData[0])
					{
						// Networked, and not one that removes itself after a while (a sound): it lasts as long as the drink.
						if (data.m_enabled && data.m_prefab && data.m_prefab.GetComponent<ZNetView>() && !data.m_prefab.GetComponent<TimedDestruction>())
						{
							int hash = data.m_prefab.name.GetStableHashCode();
							if (!s_signs.TryGetValue(hash, out HashSet<string> creatures))
							{
								s_signs[hash] = creatures = new HashSet<string>();
							}
							creatures.Add(creature.m_name);
							shownBy.Add(data.m_prefab.name);
						}
					}
					found.Add(shownBy.Count > 0
						? $"{prefab.name} makes {creature.m_name} flee, seen by {string.Join(", ", shownBy)}"
						: $"{prefab.name} makes {creature.m_name} flee, but none of its effects reaches the server: not fixed");
				}
				ServersidePlugin.logger.LogInfo(found.Count == 0
					? "Anti-Sting: no drink in the game makes a creature flee; nothing to do."
					: "Anti-Sting: " + string.Join("; ", found) + ".");
				return s_signs;
			}

			// An object as it arrives from a player's game: one of those effects is a drink being taken.
			public static void Seen(ZDO zdo)
			{
				if (s_failed)
				{
					return;
				}
				try
				{
					Dictionary<int, HashSet<string>> signs = Signs();
					if (signs == null || !signs.TryGetValue(zdo.GetPrefab(), out HashSet<string> creatures) || s_glows.ContainsKey(zdo.m_uid))
					{
						return;
					}
					ZDOID player = new ZDOID(zdo.GetLong(ZDOVars.s_ownerZDOUserId, 0L), (uint)zdo.GetLong(ZDOVars.s_ownerZDOId, 0L));
					if (player.IsNone())
					{
						return;
					}
					foreach (KeyValuePair<ZDOID, Glow> kv in s_glows)
					{
						if (ZDOMan.instance.GetZDO(kv.Key) == null)
						{
							s_gone.Add(kv.Key);
						}
					}
					foreach (ZDOID id in s_gone)
					{
						s_glows.Remove(id);
					}
					s_gone.Clear();
					s_glows[zdo.m_uid] = new Glow { player = player, owner = zdo.GetOwner(), creatures = creatures };
					ZDO playerZdo = ZDOMan.instance.GetZDO(player);
					ServersidePlugin.logger.LogInfo($"Anti-Sting: {(playerZdo != null ? playerZdo.GetString(ZDOVars.s_playerName) : player.ToString())} drank something that makes {string.Join(", ", creatures)} flee them, for as long as it lasts.");
				}
				catch (Exception e)
				{
					// Not thrown: this runs inside the game's reading of network data.
					s_failed = true;
					ServersidePlugin.logger.LogError($"Anti-Sting: off until the server restarts: {e}");
				}
			}

			// Whether the player's game has such an effect on them now. It must be the player's own game's (a game could claim another player).
			public static bool Under(Character player, string creature)
			{
				if (s_glows.Count == 0)
				{
					return false;
				}
				ZDOID id = player.GetZDOID();
				long owner = player.m_nview.GetZDO().GetOwner();
				foreach (KeyValuePair<ZDOID, Glow> kv in s_glows)
				{
					if (kv.Value.player == id && kv.Value.owner == owner && kv.Value.creatures.Contains(creature))
					{
						ZDO glow = ZDOMan.instance.GetZDO(kv.Key);
						if (glow != null && glow.GetOwner() == owner)
						{
							return true;
						}
					}
				}
				return false;
			}
		}

		// The vanilla check finds nothing for a player whose game is not this one; answer it from the drink's effect the server sees.
		[HarmonyPatch(typeof(MonsterAI), "PheromoneFleeCheck")]
		public static class MonsterAI_PheromoneFleeCheck_Patch
		{
			static void Postfix(MonsterAI __instance, Character target, ref bool __result)
			{
				if (__result || !Configuration.fixAntiSting.Value || !(target is Player) || !target.m_nview || !target.m_nview.IsValid() || target.m_nview.IsOwner()
					|| !ZNet.instance || !ZNet.instance.IsServer())
				{
					return;
				}
				__result = AntiSting.Under(target, __instance.m_character.m_name);
			}
		}
	}
}
