using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		A portal is used wholly on the player's own computer: stepping into it (TeleportWorldTrigger)
		teleports the player to the portal its ZDO is connected to (TeleportWorld.Teleport, which first
		needs TargetFound(): the connection kept in the ZDO). The server pairs portals by their name alone
		(Game.ConnectPortals: any two unconnected portals with the same tag), so a player who builds a
		portal with the name of someone's unpaired portal walks into that base, and nobody can stop a
		teleport once the game knows its target. The server does decide what each player's game knows
		about a portal, and that is used here.

		Who may use a portal -- the pair is one thing, so the answer is the same at both ends (a player
		let through one end must be able to come back through the other):
		- an admin (ExemptAdmins), and everyone at a pair whose name starts with PublicPrefix ("*"),
		  where that is the builder's own doing: a public portal inside someone else's ward counts only if
		  the owner of the ward built it;
		- where a ward holds an end -- covers it, in the ward's own radius, and has the portal's builder on
		  its list (PrivateArea, the ward's own list, "E" on it) -- the owner and the players on that list.
		  One ward at one end decides for the pair, the far end included; with a ward at both ends a player
		  has to be on both lists. The list counts whether the ward is switched off or not, so adding a
		  friend (which needs the ward off for a moment) never opens the portals. A ward put up over
		  someone's portal later, which does not have its builder on its list, holds nothing;
		- where no ward holds either end, the builders of the two ends.
		A portal nobody has built (admin-spawned, no ward) is open, as in the game.

		How it is kept:
		- A player who may not use a pair is sent the portal's ZDO with the connection set to "none" in the
		  data of that one call (ZDOMan.SendZDOs -> ZDO.Serialize) and put back right after: to their game
		  the portal is unconnected and it does not teleport. Nothing is saved or changed on the server.
		- A client only takes data newer than its copy: when who may use a pair changes (a name added to
		  a ward, a portal built, a ward moved), the portal's data revision is raised once, with the server
		  owning it, and the right copy goes out to every game.
		- Pairing: two unconnected portals with the same name are connected only if both builders may use
		  the pair: with a ward holding an end, each builder is on its list; with none, the same player
		  built both. Otherwise they stay unconnected, and the builder is told why. Pairs that exist stay
		  as they are, usable by the builders of both ends where no ward holds either.
		- A portal's name may be changed only by those who may manage it: its builder and the ward lists
		  (not "everyone" just because it is public). The request is not passed on to the game that
		  owns the portal, and data from anyone else for it is not taken.
		- A player who may not manage a portal is never given it (ZDO.SetOwner on the server), and one
		  whose game still owns it loses it within seconds.
	*/
	public class PortalLock : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.portalLockEnabled.Value;
		}

		// Prefab hash -> true for the portals; prefab hash -> radius for the wards players own.
		private static HashSet<int> s_portalPrefabs;
		private static Dictionary<int, float> s_wardRadius;
		private static List<ZDOID> s_portalIds;
		private static List<ZDOID> s_wardIds;
		private static float s_nextScan;
		private static float s_nextTick;
		private static int s_errors;

		private struct Sent
		{
			public bool hidden;
			public uint revision;
		}

		// What each player's game was last sent of each portal, and at which data revision.
		private static readonly Dictionary<KeyValuePair<long, ZDOID>, Sent> s_sent = new Dictionary<KeyValuePair<long, ZDOID>, Sent>();
		private static readonly Dictionary<string, float> s_told = new Dictionary<string, float>();
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		// Unconnected portals that had a partner of the same name they may not be paired with, and when.
		private static readonly Dictionary<ZDOID, float> s_unlinked = new Dictionary<ZDOID, float>();
		private static readonly List<int> s_permittedKeys = new List<int>();
		private static readonly int s_setTag = "RPC_SetTag".GetStableHashCode();
		private static readonly MethodInfo s_isConnecting = AccessTools.Method(typeof(Game), "IsCurrentlyConnectingPortal");
		private static ZNetPeer s_sendingTo;
		private static ZNetPeer s_receivingFrom;

		private static bool Ready()
		{
			if (s_portalPrefabs == null && ZNetScene.instance && ZNetScene.instance.m_prefabs != null && ZNetScene.instance.m_prefabs.Count > 0)
			{
				HashSet<int> portals = new HashSet<int>();
				Dictionary<int, float> wards = new Dictionary<int, float>();
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					if (!prefab || !prefab.GetComponent<ZNetView>())
					{
						continue;
					}
					int hash = prefab.name.GetStableHashCode();
					if (prefab.GetComponent<TeleportWorld>())
					{
						portals.Add(hash);
					}
					PrivateArea area = prefab.GetComponent<PrivateArea>();
					if (area && area.m_ownerFaction == Character.Faction.Players)
					{
						wards[hash] = area.m_radius;
					}
				}
				s_wardRadius = wards;
				s_portalPrefabs = portals;
				ServersidePlugin.logger.LogInfo($"Portal lock: {portals.Count} kind(s) of portal; the lists of these wards decide who may use them: "
					+ string.Join(", ", wards.Select(kv => ZNetScene.instance.GetPrefab(kv.Key).name + " " + kv.Value.ToString("0.#") + " m")));
			}
			if (s_portalPrefabs == null || s_portalPrefabs.Count == 0 || ZDOMan.instance == null)
			{
				return false;
			}
			if (s_wardIds == null)
			{
				Scan();
			}
			return true;
		}

		private static void Scan()
		{
			s_nextScan = Time.time + 10f;
			List<ZDOID> portals = new List<ZDOID>();
			List<ZDOID> wards = new List<ZDOID>();
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
			{
				int prefab = zdo.GetPrefab();
				if (s_portalPrefabs.Contains(prefab))
				{
					portals.Add(zdo.m_uid);
				}
				else if (s_wardRadius.ContainsKey(prefab))
				{
					wards.Add(zdo.m_uid);
				}
			}
			s_portalIds = portals;
			s_wardIds = wards;
		}

		private static bool IsPortal(ZDO zdo)
		{
			return zdo != null && s_portalPrefabs != null && s_portalPrefabs.Contains(zdo.GetPrefab());
		}

		private static long PlayerId(ZNetPeer peer)
		{
			if (peer == null || peer.m_characterID.IsNone())
			{
				return 0L;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
		}

		private static bool IsAdmin(ZNetPeer peer)
		{
			return Configuration.portalLockExemptAdmins.Value && peer != null && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
		}

		private static int PermittedKey(int i)
		{
			while (s_permittedKeys.Count <= i)
			{
				s_permittedKeys.Add(("pu_id" + s_permittedKeys.Count).GetStableHashCode());
			}
			return s_permittedKeys[i];
		}

		// The ward's owner and everyone written on it (PrivateArea.GetPermittedPlayers, read from its ZDO).
		private static HashSet<long> ListOf(ZDO ward)
		{
			HashSet<long> list = new HashSet<long>();
			long owner = ward.GetLong(ZDOVars.s_creator, 0L);
			if (owner != 0L)
			{
				list.Add(owner);
			}
			int count = Mathf.Min(ward.GetInt(ZDOVars.s_permitted, 0), 512);
			for (int i = 0; i < count; i++)
			{
				long id = ward.GetLong(PermittedKey(i), 0L);
				if (id != 0L)
				{
					list.Add(id);
				}
			}
			return list;
		}

		// One end of a pair: who built it, which ward lists cover it (null: none), whether it is public.
		private struct End
		{
			public long creator;
			public HashSet<long> list;
			public bool publicTag;
			public bool ownerless;
		}

		private static End Describe(ZDO portal)
		{
			End end = new End { creator = portal.GetLong(ZDOVars.s_creator, 0L) };
			string prefix = Configuration.portalLockPublicPrefix.Value;
			bool star = !string.IsNullOrEmpty(prefix) && portal.GetString(ZDOVars.s_tag, "").StartsWith(prefix, StringComparison.Ordinal);
			float extra = Mathf.Max(0f, Configuration.portalLockExtraReach.Value);
			Vector3 at = portal.GetPosition();
			bool builtByWardOwners = true;
			foreach (ZDOID id in s_wardIds)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				if (ward == null || !s_wardRadius.TryGetValue(ward.GetPrefab(), out float radius))
				{
					continue;
				}
				Vector3 p = ward.GetPosition();
				float reach = radius + extra;
				float dx = p.x - at.x;
				float dz = p.z - at.z;
				if (dx * dx + dz * dz >= reach * reach)
				{
					continue;
				}
				// A ward holds a portal only with its builder on the list: one put up over someone's portal later is not theirs.
				HashSet<long> list = ListOf(ward);
				if (end.creator == 0L || !list.Contains(end.creator))
				{
					continue;
				}
				// Every ward over the place has to agree, as the game asks for building there.
				if (end.list == null)
				{
					end.list = list;
				}
				else
				{
					end.list.IntersectWith(list);
				}
				if (ward.GetLong(ZDOVars.s_creator, 0L) != end.creator)
				{
					builtByWardOwners = false;
				}
			}
			end.publicTag = star && (end.list == null || builtByWardOwners);
			end.ownerless = end.creator == 0L && end.list == null;
			return end;
		}

		private static ZDO Partner(ZDO portal)
		{
			ZDOID id = portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
			return id.IsNone() ? null : ZDOMan.instance.GetZDO(id);
		}

		// On the list of the ward that holds each end that has one; with none, the builder of either end.
		private static bool Manages(ZDO portal, long who)
		{
			if (who == 0L)
			{
				return false;
			}
			End a = Describe(portal);
			ZDO partner = Partner(portal);
			End b = partner != null ? Describe(partner) : default(End);
			bool wardedA = a.list != null;
			bool wardedB = partner != null && b.list != null;
			if (!wardedA && !wardedB)
			{
				return a.creator == who || (partner != null && b.creator == who);
			}
			return (!wardedA || a.list.Contains(who)) && (!wardedB || b.list.Contains(who));
		}

		// May this player's game be sent the portal connected / may it be given the portal.
		private static bool MayManage(ZDO portal, ZNetPeer peer)
		{
			if (IsAdmin(peer))
			{
				return true;
			}
			End a = Describe(portal);
			ZDO partner = Partner(portal);
			if (a.ownerless && (partner == null || Describe(partner).ownerless))
			{
				return true;
			}
			return Manages(portal, PlayerId(peer));
		}

		private static bool MayUse(ZDO portal, ZNetPeer peer)
		{
			if (IsAdmin(peer))
			{
				return true;
			}
			End a = Describe(portal);
			ZDO partner = Partner(portal);
			if (partner == null)
			{
				if (a.ownerless || a.publicTag)
				{
					return true;
				}
			}
			else
			{
				End b = Describe(partner);
				if ((a.ownerless && b.ownerless) || (a.publicTag && b.publicTag))
				{
					return true;
				}
			}
			return Manages(portal, PlayerId(peer));
		}

		// Two unconnected portals of one name: would both builders be let use the pair? Without a ward holding
		// either end, only if one player built both: two names alike are no consent.
		private static bool Compatible(ZDO a, ZDO b)
		{
			End ea = Describe(a);
			End eb = Describe(b);
			if ((ea.publicTag && eb.publicTag) || ea.creator == 0L || eb.creator == 0L)
			{
				return true;
			}
			if (ea.list == null && eb.list == null)
			{
				return ea.creator == eb.creator;
			}
			return (ea.list == null || ea.list.Contains(eb.creator)) && (eb.list == null || eb.list.Contains(ea.creator));
		}

		/*
			ZDOMan.SendZDOs sets who the next ZDO.Serialize calls are for. A portal a player may not use is
			serialized with its connection set to none, and put back in the finalizer.
		*/
		[HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
		public static class ZDOMan_SendZDOs_Patch
		{
			static void Prefix(ZDOMan.ZDOPeer peer)
			{
				s_sendingTo = peer != null ? peer.m_peer : null;
			}

			static void Finalizer()
			{
				s_sendingTo = null;
			}
		}

		public struct Swapped
		{
			public bool swapped;
			public ZDOID target;
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.Serialize), new[] { typeof(ZPackage) })]
		public static class ZDO_Serialize_Patch
		{
			static void Prefix(ZDO __instance, out Swapped __state)
			{
				__state = default(Swapped);
				ZNetPeer peer = s_sendingTo;
				if (peer == null || !Configuration.portalLockEnabled.Value)
				{
					return;
				}
				try
				{
					if (!Ready() || !s_portalPrefabs.Contains(__instance.GetPrefab()))
					{
						return;
					}
					ZDOID target = __instance.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
					bool hide = !target.IsNone() && !MayUse(__instance, peer);
					s_sent[new KeyValuePair<long, ZDOID>(peer.m_uid, __instance.m_uid)] = new Sent { hidden = hide, revision = __instance.DataRevision };
					if (hide)
					{
						ZDOExtraData.SetConnection(__instance.m_uid, ZDOExtraData.ConnectionType.Portal, ZDOID.None);
						__state = new Swapped { swapped = true, target = target };
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}

			static void Finalizer(ZDO __instance, Swapped __state)
			{
				if (__state.swapped)
				{
					ZDOExtraData.SetConnection(__instance.m_uid, ZDOExtraData.ConnectionType.Portal, __state.target);
				}
			}
		}

		// Pairing: only portals with a person in common are connected.
		[HarmonyPatch(typeof(Game), "FindRandomUnconnectedPortal")]
		public static class Game_FindRandomUnconnectedPortal_Patch
		{
			static bool Prefix(Game __instance, List<ZDO> portals, ZDO skip, string tag, ref ZDO __result)
			{
				if (!Configuration.portalLockEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					if (!Ready())
					{
						return true;
					}
					List<ZDO> candidates = new List<ZDO>();
					bool refused = false;
					foreach (ZDO portal in portals)
					{
						if (portal == skip || portal.GetString(ZDOVars.s_tag) != tag
							|| portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None
							|| (s_isConnecting != null && (bool)s_isConnecting.Invoke(__instance, new object[] { portal })))
						{
							continue;
						}
						if (!Compatible(skip, portal))
						{
							refused = true;
							if (s_logged.Add($"pair/{skip.m_uid}/{portal.m_uid}"))
							{
								ServersidePlugin.logger.LogInfo($"Portal lock: the portals named '{tag}' at {Where(skip)} and {Where(portal)} have no person in common (builder or ward list); not connected");
							}
							continue;
						}
						candidates.Add(portal);
					}
					if (refused && candidates.Count == 0)
					{
						s_unlinked[skip.m_uid] = Time.time;
					}
					__result = candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// Changing a portal's name when the server owns the portal: the request is handled here, not routed.
		[HarmonyPatch(typeof(TeleportWorld), "RPC_SetTag")]
		public static class TeleportWorld_RPC_SetTag_Patch
		{
			static bool Prefix(TeleportWorld __instance, long sender)
			{
				if (!Configuration.portalLockEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					ZNetView view = __instance.m_nview;
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					ZNetPeer peer = ZNet.instance.GetPeer(sender);
					if (!Ready() || zdo == null || peer == null || MayManage(zdo, peer))
					{
						return true;
					}
					Refuse(peer, zdo, "change its name");
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// Changing a portal's name when a player's game owns the portal: not passed on to that game, unless the sender may manage it.
		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		public static class ZRoutedRpc_RouteRPC_Patch
		{
			static bool Prefix(ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (rpcData.m_methodHash != s_setTag || rpcData.m_targetZDO.IsNone() || ZDOMan.instance == null || !ZNet.instance
					|| !ZNet.instance.IsServer() || !Configuration.portalLockEnabled.Value)
				{
					return true;
				}
				try
				{
					ZDO zdo = ZDOMan.instance.GetZDO(rpcData.m_targetZDO);
					ZNetPeer peer = ZNet.instance.GetPeer(rpcData.m_senderPeerID);
					if (!Ready() || !IsPortal(zdo) || peer == null || MayManage(zdo, peer))
					{
						return true;
					}
					Refuse(peer, zdo, "change its name");
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// Who the data being read came from.
		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix(ZDOMan __instance, ZRpc rpc)
			{
				s_receivingFrom = ZNet.instance && ZNet.instance.IsServer() && Configuration.portalLockEnabled.Value ? __instance.FindPeer(rpc)?.m_peer : null;
			}

			static void Finalizer()
			{
				s_receivingFrom = null;
			}
		}

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static bool Prefix(ZDO __instance)
			{
				ZNetPeer from = s_receivingFrom;
				if (from == null || !IsPortal(__instance))
				{
					return true;
				}
				try
				{
					if (MayManage(__instance, from))
					{
						return true;
					}
					// RPC_ZDOData has already taken the sender's owner and revisions: the server takes the portal
					// back and raises the revision, so every game gets the server's copy again.
					__instance.SetOwner(ZDOMan.GetSessionID());
					__instance.IncreaseDataRevision();
					if (s_logged.Add($"data/{from.m_uid}/{__instance.m_uid}"))
					{
						ServersidePlugin.logger.LogWarning($"Portal lock: {from.m_playerName} sent data for the portal at {Where(__instance)}, which they may not manage; not taken");
					}
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// Nobody who may not manage a portal is given it.
		[HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
		public static class ZDO_SetOwner_Patch
		{
			static bool Prefix(ZDO __instance, long uid)
			{
				if (uid == 0L || s_portalPrefabs == null || !Configuration.portalLockEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer()
					|| uid == ZDOMan.GetSessionID() || !s_portalPrefabs.Contains(__instance.GetPrefab()))
				{
					return true;
				}
				try
				{
					ZNetPeer peer = ZNet.instance.GetPeer(uid);
					return peer == null || !Ready() || MayManage(__instance, peer);
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.portalLockEnabled.Value || Time.time < s_nextTick || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
			{
				return;
			}
			s_nextTick = Time.time + 2f;
			if (!Ready())
			{
				return;
			}
			if (Time.time >= s_nextScan)
			{
				Scan();
			}
			Dictionary<long, ZNetPeer> peers = new Dictionary<long, ZNetPeer>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				peers[peer.m_uid] = peer;
			}
			foreach (KeyValuePair<long, ZDOID> gone in s_sent.Keys.Where(k => !peers.ContainsKey(k.Key)).ToList())
			{
				s_sent.Remove(gone);
			}
			foreach (string key in s_told.Keys.Where(k => !peers.ContainsKey(long.Parse(k.Substring(0, k.IndexOf('/'))))).ToList())
			{
				s_told.Remove(key);
			}
			s_logged.RemoveWhere(k => !k.StartsWith("pair/") && !peers.ContainsKey(long.Parse(k.Split('/')[1])));
			long server = ZDOMan.GetSessionID();

			// Who may use a portal has changed since its copy went out to a player: send it again, once.
			foreach (KeyValuePair<KeyValuePair<long, ZDOID>, Sent> sent in s_sent.ToList())
			{
				ZDO zdo = ZDOMan.instance.GetZDO(sent.Key.Value);
				if (zdo == null || !peers.TryGetValue(sent.Key.Key, out ZNetPeer peer))
				{
					s_sent.Remove(sent.Key);
					continue;
				}
				if (!peer.IsReady() || zdo.DataRevision > sent.Value.revision)
				{
					continue;
				}
				bool hide = !zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal).IsNone() && !MayUse(zdo, peer);
				if (hide == sent.Value.hidden)
				{
					continue;
				}
				if (zdo.HasOwner() && zdo.GetOwner() != server)
				{
					zdo.SetOwner(server);
				}
				zdo.IncreaseDataRevision();
				if (s_logged.Add($"resend/{sent.Key.Key}/{zdo.m_uid}/{hide}"))
				{
					ServersidePlugin.logger.LogInfo($"Portal lock: the portal at {Where(zdo)} is sent again to {peer.m_playerName}: now {(hide ? "unconnected" : "connected")}");
				}
			}

			// A portal a player's game owns and that player may not manage goes back to the server.
			foreach (ZDOID id in s_portalIds)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo == null || !zdo.HasOwner() || zdo.GetOwner() == server)
				{
					continue;
				}
				if (peers.TryGetValue(zdo.GetOwner(), out ZNetPeer owner) && owner.IsReady() && !MayManage(zdo, owner))
				{
					zdo.SetOwner(server);
					zdo.IncreaseDataRevision();
					ServersidePlugin.logger.LogInfo($"Portal lock: the portal at {Where(zdo)} is the server's again ({owner.m_playerName} may not manage it)");
				}
			}

			// A player standing at a portal that they see unconnected for want of a place on a ward is told why.
			foreach (ZNetPeer peer in peers.Values)
			{
				ZDO character = peer.IsReady() && !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
				if (character == null)
				{
					continue;
				}
				Vector3 at = character.GetPosition();
				foreach (KeyValuePair<KeyValuePair<long, ZDOID>, Sent> sent in s_sent.Where(s => s.Key.Key == peer.m_uid && s.Value.hidden).ToList())
				{
					ZDO zdo = ZDOMan.instance.GetZDO(sent.Key.Value);
					if (zdo != null && Near(zdo, at))
					{
						Say(peer, zdo, "private", Configuration.portalLockMessage.Value);
					}
				}
				foreach (KeyValuePair<ZDOID, float> unlinked in s_unlinked.Where(u => Time.time - u.Value < 20f).ToList())
				{
					ZDO zdo = ZDOMan.instance.GetZDO(unlinked.Key);
					if (zdo != null && Near(zdo, at) && Manages(zdo, PlayerId(peer)))
					{
						Say(peer, zdo, "unlinked", Configuration.portalLockPairingMessage.Value);
					}
				}
			}
			foreach (ZDOID gone in s_unlinked.Where(u => Time.time - u.Value > 60f).Select(u => u.Key).ToList())
			{
				s_unlinked.Remove(gone);
			}
		}

		private static bool Near(ZDO portal, Vector3 at)
		{
			Vector3 p = portal.GetPosition();
			float dx = p.x - at.x;
			float dz = p.z - at.z;
			return dx * dx + dz * dz <= 16f && Mathf.Abs(p.y - at.y) < 8f;
		}

		// At most once a quarter of an hour per player and portal.
		private static void Say(ZNetPeer peer, ZDO portal, string kind, string message)
		{
			string key = $"{peer.m_uid}/{portal.m_uid}/{kind}";
			if (string.IsNullOrEmpty(message) || (s_told.TryGetValue(key, out float last) && Time.time - last < 900f))
			{
				return;
			}
			s_told[key] = Time.time;
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.Center, message);
			if (s_logged.Add($"told/{key}"))
			{
				ServersidePlugin.logger.LogInfo($"Portal lock: {peer.m_playerName} is told at the portal at {Where(portal)} ({kind})");
			}
		}

		private static void Refuse(ZNetPeer peer, ZDO portal, string what)
		{
			if (s_logged.Add($"rpc/{peer.m_uid}/{portal.m_uid}"))
			{
				ServersidePlugin.logger.LogInfo($"Portal lock: {peer.m_playerName} may not {what} of the portal at {Where(portal)}; not passed on");
			}
			Say(peer, portal, "private", Configuration.portalLockMessage.Value);
		}

		private static string Where(ZDO zdo)
		{
			Vector3 p = zdo.GetPosition();
			return $"{p.x:0},{p.z:0}";
		}

		private static void Fault(Exception e)
		{
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Portal lock: {e}");
			}
		}
	}
}
