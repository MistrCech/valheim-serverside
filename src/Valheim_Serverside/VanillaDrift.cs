using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Valheim_Serverside
{
	/*
		Several Core patches replace a vanilla method outright (a prefix returning false). When a
		game update adds something to the original, the replacement silently drops it -- this is
		how ZoneSystem.Update ended up never releasing location prefabs, never updating
		m_lastFixedTime and ignoring the location-generation gate.

		At startup this compares what each replaced original calls into the game's own assemblies
		with what it called when the replacement was last reviewed, and logs the difference. It
		cannot tell whether a change matters; it makes sure somebody looks.
	*/
	public static class VanillaDrift
	{
		public const string ReviewedForGameVersion = "1.0.15";

		private static readonly Dictionary<MethodBase, string[]> Expected = new Dictionary<MethodBase, string[]>
		{
			{ AccessTools.Method(typeof(ZoneSystem), "Update"), new[] {
				"CinematicsManager::IsPlaying", "TextViewer::IsShowingIntro", "Utils::GetTimeBudgetForTargetFrameRate", "ZNet::GetConnectionStatus",
				"ZNet::GetPeers", "ZNet::GetReferencePosition", "ZNet::IsServer", "ZNet::get_instance",
				"ZNetPeer::GetRefPos", "ZoneSystem::CreateGhostZones", "ZoneSystem::CreateLocalZones", "ZoneSystem::TimeSinceStart",
				"ZoneSystem::UpdatePrefabLifetimes", "ZoneSystem::UpdateTTL", "ZoneSystem::get_LocationsGenerated",
			} },
			{ AccessTools.Method(typeof(ZoneSystem), "IsActiveAreaLoaded"), new[] {
				"SimulationDistance::get_IsClassic", "SimulationDistance::get_NearSimulationDistance", "Vector2s::.ctor", "ZNet::GetReferencePosition",
				"ZNet::get_instance", "ZoneSystem::GetZone", "ZoneSystem::ZonesWithinRadius",
			} },
			{ AccessTools.Method(typeof(ZNetScene), "CreateDestroyObjects"), new[] {
				"ZDOMan::FindSectorObjects", "ZDOMan::get_instance", "ZNet::GetReferencePosition", "ZNet::GetSyncedSimulationDistance",
				"ZNet::get_instance", "ZNetScene::CreateObjects", "ZNetScene::RemoveObjects", "ZoneSystem::GetZone",
			} },
			{ AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS"), new[] {
				"SimulationDistance::.ctor", "SimulationDistance::get_IsClassic", "SimulationDistance::get_NearSimulationDistance", "ZDO::GetOwner",
				"ZDO::GetPosition", "ZDO::HasOwner", "ZDO::SetOwner", "ZDO::get_Persistent",
				"ZDOMan::FindSectorObjects", "ZDOMan::IsInPeerActiveArea", "ZNet::GetSyncedSimulationDistance", "ZNet::get_instance",
				"ZNetScene::InActiveArea", "ZoneSystem::GetZone",
			} },
			{ AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2"), new[] {
				"ZDOMan::SendZDOs",
			} },
			{ AccessTools.Method(typeof(ZNetScene), "OutsideActiveArea", new[] { typeof(UnityEngine.Vector3) }), new[] {
				"ZNet::GetReferencePosition", "ZNet::get_instance", "ZNetScene::OutsideActiveArea", "ZoneSystem::GetZone",
			} },
			{ AccessTools.Method(typeof(Ship), "UpdateOwner"), new[] {
				"Ship::GetNewOwnerID", "Ship::IsPlayerInBoat", "Ship::RefreshPlayerList", "ZDO::SetOwner",
				"ZLog::Log", "ZNetView::GetZDO", "ZNetView::IsOwner", "ZNetView::IsValid",
			} },
		};

		public static void Check(ManualLogSource logger)
		{
			string gameVersion = Version.CurrentVersion.ToString();
			int drifted = 0;
			foreach (KeyValuePair<MethodBase, string[]> entry in Expected)
			{
				if (entry.Key == null)
				{
					logger.LogError("Vanilla drift check: a replaced method no longer exists in this game version.");
					drifted++;
					continue;
				}
				HashSet<string> actual = GameCalls(entry.Key);
				List<string> added = actual.Except(entry.Value).OrderBy(s => s).ToList();
				List<string> removed = entry.Value.Except(actual).OrderBy(s => s).ToList();
				if (added.Count == 0 && removed.Count == 0)
				{
					continue;
				}
				drifted++;
				logger.LogWarning(
					$"Vanilla {entry.Key.DeclaringType.Name}.{entry.Key.Name} changed since this mod's replacement was reviewed "
					+ $"(game {ReviewedForGameVersion}, running {gameVersion}). "
					+ $"Now calls: [{string.Join(", ", added)}]. No longer calls: [{string.Join(", ", removed)}]. "
					+ "The replacement may be dropping new vanilla behaviour.");
			}
			if (drifted == 0)
			{
				logger.LogInfo($"Vanilla drift check passed ({Expected.Count} replaced methods, reviewed for {ReviewedForGameVersion}, running {gameVersion}).");
			}
		}

		private static HashSet<string> GameCalls(MethodBase method)
		{
			HashSet<string> calls = new HashSet<string>();
			foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
			{
				if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Newobj)
					&& instruction.operand is MethodBase callee
					&& callee.DeclaringType != null)
				{
					string assembly = callee.DeclaringType.Assembly.GetName().Name;
					if (assembly == "assembly_valheim" || assembly == "assembly_utils")
					{
						calls.Add($"{callee.DeclaringType.Name}::{callee.Name}");
					}
				}
			}
			return calls;
		}
	}
}
