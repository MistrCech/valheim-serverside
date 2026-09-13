using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;

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
	*/
	public class Fixes : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fixSaveClientChanges.Value || Configuration.fixTeleportGhosts.Value;
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
	}
}
