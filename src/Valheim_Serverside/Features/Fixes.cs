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
	*/
	public class Fixes : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fixSaveClientChanges.Value || Configuration.fixTeleportGhosts.Value || Configuration.fixServerSmoke.Value;
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
	}
}
