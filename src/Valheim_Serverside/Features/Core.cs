using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;


namespace Valheim_Serverside.Features
{
	public class Core : IFeature
	{
		public bool FeatureEnabled()
		{
			return true;
		}

		public static bool IsServer()
		{
			return ZNet.instance && ZNet.instance.IsServer();
		}

		public static void PrintLog(string text)
		{
			System.Diagnostics.Trace.WriteLine(text);
		}

		public static void PrintLog(object[] obj)
		{
			System.Diagnostics.Trace.WriteLine(string.Concat(obj));
		}

		/*
			Private game members are reached directly rather than through Traverse: the
			assembly is publicized at build time, so a renamed or re-typed member is a compile
			error on the next game update. Traverse returns a default value when it cannot find
			its target, which is how the 1.0 IsInPeerActiveArea signature change silently made
			the server reclaim every ground item.
		*/

		[HarmonyPatch(typeof(Pickable), "RPC_Pick")]
		public static class Pickable_RPC_Pick_Patch
		/*
			RPC_Pick throws a NullReferenceException whenever it executes on a
			headless dedicated server -- confirmed live (Exception in
			ZRpc::HandlePackage, NullReferenceException inside RPC_Pick's own
			DMD wrapper) and via IL disassembly of the real method body:
			it unconditionally does `Player.m_localPlayer.GetZDOID()` to
			attribute a pickup visual/audio effect. Player.m_localPlayer is
			always null on a dedicated server -- there's no local player
			there, ever, with or without this mod. Under vanilla peer-hosted
			play this never matters, because RPC_Pick only ever runs on
			whichever player's own client already owns the object. This mod
			moves ownership of persistent objects (including ground items)
			to the server, so a Pickable owned by the server is the one
			context where RPC_Pick actually executes headless -- and always
			crashes, silently to the player (the client just sees nothing
			happen and keeps retrying).

			Fix: a transpiler that replaces just the crashing two-instruction
			sequence (ldsfld Player::m_localPlayer; callvirt
			Character::GetZDOID()) with a call to a null-safe equivalent,
			leaving 100% of the rest of vanilla's method (item drops,
			aggravate, the RPC_SetPicked broadcast that actually removes the
			item for everyone) untouched and unduplicated.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				return ServerSafe.NullSafeLocalPlayer(instructions, __originalMethod, expected: 1);
			}
		}

		[HarmonyPatch(typeof(Ship), "UpdateSailSize")]
		public static class Ship_UpdateSailSize_Patch
		/*
			1.0 attributes the sail-change effect to the local player:
			`Player.m_localPlayer.GetPlayerID() == m_shipControlls.GetUser() ? Player.m_localPlayer.GetZDOID() : ZDOID.None`.

			Ship.CustomFixedUpdate calls UpdateSail before its IsOwner check, so this runs for every
			ship the server has instantiated. It throws before `m_sailWasInPosition` is updated,
			so it throws again on every physics frame until the sail stops moving, and when the
			server owns the ship the rest of that frame's ship physics is skipped.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				return ServerSafe.NullSafeLocalPlayer(instructions, __originalMethod, expected: 2);
			}
		}

		[HarmonyPatch(typeof(CookingStation), "SpawnItem")]
		public static class CookingStation_SpawnItem_Patch
		/*
			1.0 records the local player as the crafter of cooked items when `m_recordCrafter` is set.

			SpawnItem runs from RPC_RemoveDoneItem on the station's owner, which is often the server
			with this mod. It throws after the item has been instantiated but before
			RPC_RemoveDoneItem clears the slot, so every attempt to take the food would spawn
			another copy while the station keeps it. On the server no crafter is recorded instead.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				return ServerSafe.NullSafeLocalPlayer(instructions, __originalMethod, expected: 2);
			}
		}

		[HarmonyPatch(typeof(Leviathan), "RPC_Left")]
		public static class Leviathan_RPC_Left_Patch
		/*
			1.0 increments a stat for the local player when a leviathan dives, reading
			Player.m_localPlayer.transform unguarded. The handler is only registered on the owner,
			which can be the server. The dive itself already happened in Leave(); only the stat
			(meaningless on a server) is skipped.
		*/
		{
			static bool Prefix()
			{
				return Player.m_localPlayer != null;
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		public class CreateDestroyObjects_Patch
		/*
			The bread and butter of the mod, this patch facilitates spawning objects on the server.

			Creates and destroys ZDOs by finding all objects in each peer area.

			Some object overlap can happen if peers are close to each other, the objects are
			deduplicated by using a HashSet, see `List.Distinct`.

			This method originally works only with objects surrounding `ZNet.GetReferencePosition()` which returns some
			made-up nonsense on a dedicated server.

			DistantObjects: Are objects that have `m_distant` set to `true`, set (probably) in the prefab data;
			Distant objects are not affected by draw distance.

			CreateObjects: Makes no distinction between objects and nearby-objects except in the order
						   they are created.
		
			RemoveObjects: Marks all ZDOs for deletion by setting the current frame number on the ZDO,
						   and then checks if any of the ZDOs marked for deletion have an older/different
						   frame number.
		*/
		{
			private static readonly HashSet<ZDO> s_seen = new HashSet<ZDO>();

			private static bool Prefix(ZNetScene __instance)
			{
				// Reuses the lists vanilla uses for the same purpose instead of allocating two per call.
				List<ZDO> currentObjects = __instance.m_tempCurrentObjects;
				List<ZDO> currentDistantObjects = __instance.m_tempCurrentDistantObjects;
				currentObjects.Clear();
				currentDistantObjects.Clear();

				// 1.0: one SimulationDistance replaces m_activeArea/m_activeDistantArea. Vanilla reads
				// the synced value from ZNet here, not ZoneSystem's copy.
				SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
				foreach (ZNetPeer znetPeer in ZNet.instance.GetConnectedPeers())
				{
					Vector2s zone = ZoneSystem.GetZone(znetPeer.GetRefPos());
					ZDOMan.instance.FindSectorObjects(zone, distance, currentObjects, currentDistantObjects);
				}

				RemoveDuplicates(currentObjects);
				RemoveDuplicates(currentDistantObjects);
				if (Compat_ValheimCommunityPatch.UnloadKept)
				{
					// ValheimCommunityPatch unloads around the reference position when these lists look as the
					// game filled them, which they do when no peers' areas overlap; a no-op edit sends it down
					// the game's own unload check instead.
					currentObjects.Reverse(0, 0);
					currentDistantObjects.Reverse(0, 0);
				}
				__instance.CreateObjects(currentObjects, currentDistantObjects);
				__instance.RemoveObjects(currentObjects, currentDistantObjects);
				return false;
			}

			// Peers' areas overlap; keeps the first occurrence like Enumerable.Distinct did.
			private static void RemoveDuplicates(List<ZDO> zdos)
			{
				s_seen.Clear();
				zdos.RemoveAll(zdo => !s_seen.Add(zdo));
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]
		public static class ZNetScene_CreateObjectsSorted_Patch
		/*
			CreateObjectsSorted instantiates the objects that are not created yet, closest to
			ZNet.GetReferencePosition() first, a limited number per frame. On a dedicated server that
			position is outside the world, so the order was effectively arbitrary: after a portal or
			on entering a new area, the objects next to a player could be among the last the server
			creates and starts simulating. Sort by distance to the nearest peer instead (the idea of
			upstream PR #100 by jsza). This orders server-side creation only; clients sort their own.
		*/
		{
			private static readonly List<Vector3> s_peerPositions = new List<Vector3>();
			private static int s_peerPositionsFrame = -1;

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				MethodInfo distanceSqr = AccessTools.Method(typeof(Utils), nameof(Utils.DistanceSqr), new[] { typeof(Vector3), typeof(Vector3) });
				MethodInfo nearest = AccessTools.Method(typeof(ZNetScene_CreateObjectsSorted_Patch), nameof(NearestPeerDistanceSqr));
				List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
				int replaced = 0;
				foreach (CodeInstruction code in codes)
				{
					if (code.Calls(distanceSqr))
					{
						code.operand = nearest;
						replaced++;
					}
				}
				if (replaced != 1)
				{
					ServersidePlugin.logger.LogWarning($"{__originalMethod.DeclaringType.Name}.{__originalMethod.Name}: replaced {replaced} sort distance call(s), expected 1. The game changed this method; the patch needs reviewing.");
				}
				return codes;
			}

			// Same signature as the Utils.DistanceSqr call it replaces; the reference position is only a fallback.
			public static float NearestPeerDistanceSqr(Vector3 referencePosition, Vector3 position)
			{
				if (s_peerPositionsFrame != Time.frameCount)
				{
					s_peerPositionsFrame = Time.frameCount;
					s_peerPositions.Clear();
					foreach (ZNetPeer peer in ZNet.instance.GetPeers())
					{
						s_peerPositions.Add(peer.GetRefPos());
					}
				}
				if (s_peerPositions.Count == 0)
				{
					return Utils.DistanceSqr(referencePosition, position);
				}
				float nearest = float.MaxValue;
				foreach (Vector3 peerPosition in s_peerPositions)
				{
					nearest = Mathf.Min(nearest, Utils.DistanceSqr(peerPosition, position));
				}
				return nearest;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "IsActiveAreaLoaded")]
		public static class ZoneSystem_IsActiveAreaLoaded_Patch
		/*
			Vanilla checks the zones around the server's reference position; this checks them
			around every peer, since those are the zones the server now creates.

			1.0: unless the simulation distance is "classic" (the default, or `-simulationdistance`
			0 or 2), CreateLocalZones only creates zones within a radius, not the whole square.
			The square's corners never load in that mode, so they must be skipped here too or this
			never returns true and the server never creates any objects.
		*/
		{
			private static bool Prefix(ZoneSystem __instance, ref bool __result)
			{
				SimulationDistance distance = __instance.m_simulationDistance;
				int near = distance.NearSimulationDistance;
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					Vector2s zone = ZoneSystem.GetZone(peer.GetRefPos());
					for (int i = zone.y - near; i <= zone.y + near; i++)
					{
						for (int j = zone.x - near; j <= zone.x + near; j++)
						{
							Vector2s candidate = new Vector2s(j, i);
							if ((distance.IsClassic || __instance.ZonesWithinRadius(zone, candidate, near))
								&& !__instance.m_zones.ContainsKey(candidate))
							{
								__result = false;
								return false;
							}
						}
					}
				}
				__result = true;
				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "Update")]
		public static class ZoneSystem_Update_Patch
		/*
			Creates Local-Zones for each peer position. Enabling simulation to be handled by the server.

			Original method: tries to create a Local-Zone for the position the player is standing in,
			if this is a server then a Ghost-Zone is created for the current reference position as well
			as for each peer's position.

			Local-Zone: Created on every player's client, container for things like terrain and vegetation.
			Ghost-Zone: Created only on the server, unsimulated (associated GameObjects are destroyed), used
						only to send associated information to clients.

			Vanilla also does three things this replacement used to drop:
			- keeps m_lastFixedTime current (ZoneSystem.TimeSinceStart);
			- while the server is still generating locations, creates no zones at all -- a zone
			  generated early would permanently miss the locations meant for it;
			- releases location prefabs whose lifetime has run out (UpdatePrefabLifetimes). Without
			  it every location prefab the server ever loaded stayed in memory.
			The first two frames are left to vanilla; the rest is replicated below.

			Ghost zones: vanilla also generates ghost zones around every peer, out to the full
			simulation distance (near + far), whenever no local zone was created that tick. The
			replacement created local zones instead, which only reach the near distance, so in
			unexplored land nothing existed in the far ring and clients could not show distant
			objects there (large trees, cliffs, the Mistlands mist) until they were much closer.
			Ghost zones are generated around peers again, as in vanilla.
		*/
		{
			static bool Prefix(ZoneSystem __instance)
			{
				if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected
					|| (ZNet.instance.IsServer() && !__instance.LocationsGenerated))
				{
					return true;
				}

				__instance.m_lastFixedTime = Time.fixedTime;
				__instance.m_updateTimer += Time.deltaTime;
				if (__instance.m_updateTimer > 0.1f)
				{
					__instance.m_updateTimer = 0f;
					// Vanilla's CreateLocalZones/CreateGhostZones for ZNet.GetReferencePosition() are left out:
					// on a dedicated server that position is outside the world.
					__instance.UpdateTTL(0.1f);
					if (ZNet.instance.IsServer())
					{
						long started = System.Diagnostics.Stopwatch.GetTimestamp();
						List<ZNetPeer> peers = ZNet.instance.GetPeers();
						int local = SpawnZones(__instance, peers, ghost: false);
						int ghosts = local == 0 ? SpawnZones(__instance, peers, ghost: true) : 0;
						PerformanceStats.Zones(local, ghosts, started);
					}
					__instance.UpdatePrefabLifetimes();
				}
				return false;
			}

			private static int s_nextPeer;

			/*
				Each call generates at most one zone, for that peer. A new zone is generated in full
				in one frame (terrain, vegetation, locations), so one zone per exploring player in the
				same tick was a spike. At most MaxZonesPerTick are generated per tick, the peers taking
				turns from the one after the last served. A peer skipped for a tick loses nothing: its
				zones live 4 s without a refresh (m_zoneTTL) and it comes up at least every N ticks.
			*/
			private static int SpawnZones(ZoneSystem zoneSystem, List<ZNetPeer> peers, bool ghost)
			{
				int budget = Configuration.maxZonesPerTick.Value > 0 ? Configuration.maxZonesPerTick.Value : int.MaxValue;
				int count = peers.Count;
				int spawned = 0;
				for (int n = 0; n < count && spawned < budget; n++)
				{
					int index = (s_nextPeer + n) % count;
					Vector3 position = peers[index].GetRefPos();
					if (ghost ? zoneSystem.CreateGhostZones(position) : zoneSystem.CreateLocalZones(position))
					{
						spawned++;
						s_nextPeer = (index + 1) % count;
					}
				}
				return spawned;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
		public static class ZDOMan_ReleaseNearbyZDOS_Patch
		/*
			Releases nearby ZDOs for a player if no other peers are nearby that player.
			If instead the nearby ZDO has no owner, set owner to server so that it simulates on the server.

			Original method:
			If ZDO is no longer near the peer, release ownership. If no owner set, change ownership to said peer.
		*/
		{
			static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
			{
				Vector2s zone = ZoneSystem.GetZone(refPosition);
				List<ZDO> nearObjects = __instance.m_tempNearObjects;
				nearObjects.Clear();

				// 1.0: a far distance of 0 replaces the old separate `activeDistantArea = 0` argument,
				// i.e. near objects only, as in vanilla.
				SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
				__instance.FindSectorObjects(zone, new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic), nearObjects);

				long serverUID = ZNet.GetUID();
				foreach (ZDO zdo in nearObjects)
				{
					if (!zdo.Persistent)
					{
						continue;
					}
					// 1.0: active-area checks take a position instead of a sector.
					Vector3 position = zdo.GetPosition();
					bool anyPlayerInArea = false;
					foreach (ZNetPeer peer in ZNet.instance.GetPeers())
					{
						if (ZNetScene.InActiveArea(position, ZoneSystem.GetZone(peer.GetRefPos())))
						{
							anyPlayerInArea = true;
							break;
						}
					}
					long zdoOwner = zdo.GetOwner();
					if (zdoOwner == uid || zdoOwner == serverUID)
					{
						if (!anyPlayerInArea)
						{
							zdo.SetOwner(0L);
						}
					}
					else if ((zdoOwner == 0L || !__instance.IsInPeerActiveArea(position, zdoOwner)) && anyPlayerInArea)
					{
						zdo.SetOwner(serverUID);
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
		public static class RandEventSystem_FixedUpdate_Patch
		/*
			Replaces the check:

				if (Player.m_localPlayer && this.IsInsideRandomEventArea(this.m_randomEvent, Player.m_localPlayer.transform.position))

			with:

				if (this.IsAnyPlayerInEventArea(this.m_randomEvent))

			Player.m_localPlayer is always null on a dedicated server (there is no local player
			character), so the original check always fails and the event gets deactivated
			(SetActiveEvent(null, false)) before it can ever go active -- meaning random events
			(raids) never activate, and therefore their monsters never spawn, on a dedicated server.

			Previously this transpiler looked for an existing call to IsAnyPlayerInEventArea
			elsewhere in FixedUpdate and reused its cached boolean result instead of writing a
			fresh call. As of the game update that shipped ~2026-09-19, FixedUpdate no longer
			calls IsAnyPlayerInEventArea anywhere in its body, so that anchor never matched, this
			whole transpiler silently no-op'd, and the original m_localPlayer==null check stayed
			live -- confirmed 2026-09-24 via static IL comparison against the deployed
			assembly_valheim.dll (monodis dump of RandEventSystem::FixedUpdate showed the raw
			vanilla null-check block untouched, with no IsAnyPlayerInEventArea call anywhere in
			the method). No random event had ever gone active on this dedicated server as a
			result. Fixed by locating the null-check + IsInsideRandomEventArea block directly
			(by field/method identity, not by reusing an assumed-nearby call) and replacing it
			with a fresh IsAnyPlayerInEventArea(m_randomEvent) call, so this no longer depends on
			the vanilla method happening to call it elsewhere first.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				FieldInfo field_m_localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				MethodInfo opImplicitInfo = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit");
				MethodInfo isInsideRandomEventAreaInfo = AccessTools.Method(typeof(RandEventSystem), "IsInsideRandomEventArea");
				MethodInfo isAnyPlayerInfo = AccessTools.Method(typeof(RandEventSystem), "IsAnyPlayerInEventArea");
				FieldInfo field_m_randomEvent = AccessTools.Field(typeof(RandEventSystem), "m_randomEvent");

				var matcher = new CodeMatcher(instructions);

				// Find "if (Player.m_localPlayer) { ... }" -- the truthiness check on m_localPlayer.
				matcher.MatchForward(false,
					new CodeMatch(OpCodes.Ldsfld, field_m_localPlayer),
					new CodeMatch(OpCodes.Call, opImplicitInfo),
					new CodeMatch(OpCodes.Brfalse)
				);
				if (!matcher.IsValid)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: m_localPlayer null-check pattern not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				int startPos = matcher.Pos;

				// From there, find the IsInsideRandomEventArea(...) call and the branch right after it.
				matcher.MatchForward(false,
					new CodeMatch(OpCodes.Call, isInsideRandomEventAreaInfo)
				);
				if (!matcher.IsValid)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: IsInsideRandomEventArea call not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				matcher.Advance(1);
				if (!matcher.IsValid || !matcher.Instruction.Branches(out System.Reflection.Emit.Label? branchTarget) || branchTarget == null)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: expected branch after IsInsideRandomEventArea call not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				int endPos = matcher.Pos;

				return matcher
					.Start()
					.Advance(startPos)
					.RemoveInstructions(endPos - startPos + 1)
					.InsertAndAdvance(
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Ldfld, field_m_randomEvent),
						new CodeInstruction(OpCodes.Call, isAnyPlayerInfo),
						new CodeInstruction(OpCodes.Brfalse, branchTarget.Value)
					)
					.InstructionEnumeration();
			}
		}

		public static List<SpawnSystem.SpawnData> GetCurrentSpawners(RandEventSystem instance, SpawnSystem spawnSystem)
		/*
			Return spawners if there are nearby players in the event area.
		*/
		{
			if (instance.m_activeEvent == null)
			{
				return null;
			}

			Vector3 spawnSystemPosition = spawnSystem.m_nview.GetZDO().GetPosition();
			foreach (Player player in Player.GetAllPlayers())
			{
				if (ZNetScene.InActiveArea(spawnSystemPosition, ZoneSystem.GetZone(player.transform.position))
					&& instance.IsInsideRandomEventArea(instance.m_randomEvent, player.transform.position))
				{
					return instance.GetCurrentSpawners();
				}
			}
			return null;
		}

		[HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
		public static class SpawnSystem_UpdateSpawning_Patch
		/*
			Patches out m_localPlayer == null check in SpawnSystem.UpdateSpawning
			by reversing the boolean check.

			Fixes enemies not spawning during random events.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _instructions)
			{
				return new CodeMatcher(_instructions)
					// Reverse Player.m_localPlayer == false check to allow function to run on dedicated server
					.MatchForward(true,
						new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer))),
						new CodeMatch(OpCodes.Ldnull),
						new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")),
						new CodeMatch(OpCodes.Brfalse)
					)
					.SetOpcodeAndAdvance(OpCodes.Brtrue)

					// Replace RandEventSystem.GetCurrentSpawners call with call to our method.
					.MatchForward(false,
						new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(RandEventSystem), nameof(RandEventSystem.GetCurrentSpawners)))
					)
					.RemoveInstruction()
					.Insert(
						// Arg 0 is SpawnSystem instance; push to stack (2nd arg to Core.GetCurrentSpawners)
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Core), nameof(Core.GetCurrentSpawners)))
					)

					.InstructionEnumeration()
				;
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "OutsideActiveArea", new Type[] { typeof(Vector3) })]
		public static class ZNetScene_OutsideActiveArea_Patch
		/*
			Originally uses `ZNet.GetReferencePosition` to determine active area but with the server 
			handling all areas, it must check if the `Vector3` is within any of the peers' active areas.

			Returns `false` if the point is within *any* of the peers' active areas and `false` otherwise.

			SpawnArea (e.g BonePileSpawner) uses `OutsideActiveArea` to determine if it should be simulated.
		*/
		{
			static bool Prefix(ref bool __result, ZNetScene __instance, Vector3 point)
			{
				__result = true;
				foreach (ZNetPeer znetPeer in ZNet.instance.GetPeers())
				{
					// OutsideActiveArea(Vector3, Vector3) is gone in 1.0 -- only
					// (Vector3) and (Vector3, Vector2s) remain, so pass the peer's
					// zone instead of their raw position.
					if (!ZNetScene.OutsideActiveArea(point, ZoneSystem.GetZone(znetPeer.GetRefPos())))
					{
						__result = false;
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		public static class ZRoutedRpc_RouteRPC_Patch
		/*
			When a client requests to be the "user" (driver) of a ship this RPC method
			is sent from the current ship owner when they accept the request.
			We set the owner of the ship to the new ship driver.

			Allows players to drive ships with no roundtrip latency.
		*/
		{
			static void Prefix(ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (rpcData.m_methodHash == "RequestRespons".GetStableHashCode())
				{
					bool granted = rpcData.m_parameters.ReadBool();
					ZDO zdo = ZDOMan.instance.GetZDO(rpcData.m_targetZDO);
					if (zdo != null && granted)
					{
						ServersidePlugin.logger.LogDebug($"RequestRespons: Setting ship's owner to {rpcData.m_targetPeerID}");
						zdo.SetOwner(rpcData.m_targetPeerID);
					}
				}
			}
		}

		[HarmonyPatch(typeof(Humanoid), "UpdateAttack")]
		public static class Humanoid_UpdateAttack_Patch
		/*
			Remove the `m_currentAttack` from the Humanoid if it doesn't have a character instance.
			
			The underlying reason for an `Attack` instance not to have `m_character` set
			is currently not known, and requires further investigation.
		 */
		{
			static void Prefix(ref Humanoid __instance)
			{
				if (__instance.m_currentAttack != null && __instance.m_currentAttack.m_character == null)
				{
					__instance.m_currentAttack = null;
				}
			}
		}

		[HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
		public static class WearNTear_UpdateSupport_Patch
		/*
			Call `SetupColliders` if `WearNTear.m_bounds` is not set but `WearNTear.m_colliders` are set.
			

			The underlying reason is currently not known, and requires further investigation.
		 */
		{
			static void Prefix(ref WearNTear __instance)
			{
				if (__instance.m_colliders != null && __instance.m_bounds == null)
				{
					__instance.SetupColliders();
				}
			}
		}

		[HarmonyPatch(typeof(Ship), "UpdateOwner")]
		public static class Ship_UpdateOwner_Patch
		/*
			This method is invoked on a 4 second timer. 

			Keep the Ship owner set to the Ship's driver.

			If the Ship has no valid user, set the owner to the server
			to ensure simulations are handled by the server.

			Only change ownership when the Ship's container is not in use,
			to prevent them from being kicked out of said container.

			Prevent boat from taking impact damage from out of sync water 
			levels when taking ownership.
		*/
		{
			static bool Prefix(ref Ship __instance)
			{
				ZDO zdo = __instance.m_nview.GetZDO();
				// Don't do anything if a player is using ship's container
				if (zdo.GetInt("InUse", 0) == 0)
				{
					if (!__instance.m_shipControlls.HaveValidUser())
					{
						__instance.m_lastWaterImpactTime = Time.time;
						zdo.SetOwner(ZNet.GetUID());
						return false;
					}
					long driver = __instance.m_shipControlls.GetUser();
					// The driver's Player can be missing on the server for a moment (not instantiated
					// yet, or just left); keep the current owner until it shows up.
					Player driverPlayer = driver != 0L ? Player.GetPlayer(driver) : null;
					if (driverPlayer != null)
					{
						long driverID = driverPlayer.GetOwner();
						ServersidePlugin.logger.LogDebug($"UpdateOwner: Setting ship's owner to {driverID}");
						zdo.SetOwner(driverID);
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(AudioMan), "Update")]
		public static class AudioMan_Update_Patch
		/*
			Skip `AudioMan.Update` on the server.
		 */
		{
			static bool Prefix()
			{
				return false;
			}
		}

		[HarmonyPatch(typeof(ShieldDomeImageEffect), "GetDomeColor")]
		public static class ShieldDomeImageEffect_GetDomeColor_Patch
		/*
			The original code results in a null reference while trying to create a gradient.
			We force `GetDomeColor` to return a constant value in favor of patching the
			larger caller function to remove the reference to `GetDomeColor`.
		 */
		{
			static bool Prefix(ref Color __result)
			{
				__result = new Color(1, 1, 1);
				return false;
			}
		}

	}
}
