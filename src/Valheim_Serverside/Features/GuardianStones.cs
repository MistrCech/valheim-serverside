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
		A boss's power is taken at its guardian stone, wholly on the player's own computer: the stand
		(ItemStand.Interact) offers it when the stand holds a trophy (HaveAttachment, read from the
		stone's ZDO, `s_item`), and the chosen power is kept in the character, never sent to the
		server. So the server can neither see nor take away a power, but it decides what each player's
		game knows about the stone. Here a player who has not beaten that boss themselves (Progress,
		the same reading as the night spawn guard's) is sent the stone's ZDO with no trophy on it: to
		their game the stone is empty and offers nothing. Everyone else gets it as it is.

		How, and what keeps the trophy safe:
		- Each ZDO is serialized separately for each player (ZDOMan.SendZDOs); for a stone that player
		  may not use, `s_item` is set to 0 in the ZDO's own data for that one call and put back right
		  after -- no revision change, nothing saved, the world keeps the trophy.
		- A stone they may not use would invite them to hang their own trophy (the stand attaches one
		  from the inventory when it looks empty), which first asks the stone's owner for the stone
		  (RPC_RequestOwn). That request is refused where it arrives on the server: handled there when the
		  server owns the stone, and not passed on when a player's game does -- that game would grant it.
		  The same goes for a player whose game may still hold an empty copy of a stone they may by now
		  use. Without ownership their game never writes the stone.
		- A stone a player's game took over (hanging a trophy, turning it) goes back to the server after
		  10 s, so it is the server's own copy that everyone is sent.
		- Should data for such a stone ever arrive from a player who was sent it empty, the server does
		  not take it (ZDO.Deserialize skipped), takes the stone back and says so in the log.
		- A client only takes data newer than what it has: until a player's game has been sent a newer
		  revision of a stone it holds empty, that copy counts as theirs. When the player may use the
		  stone, its data revision is raised once (while the server owns it), and the real one goes out.
		- A player's game gets no guardian stone at all until their raid list has arrived, so a
		  veteran logging in at the temple is never first sent the stones empty.
		The guardian stones themselves set no world key and their trophies cannot be taken down
		(BossStone.m_setsWorldKey empty, ItemStand.m_canBeRemoved false, both read from the game's
		data, 1.0.16), so a stone shown empty changes nothing else in the world.

		A player who comes within 4 m of a stone that holds a trophy they may not use, or is refused
		one, is told why, once a quarter of an hour per stone. Fader leaves nothing of his own in the raid list; his
		stone counts as the Queen's. Admins see every stone as it is (ExemptAdmins).
	*/
	public class GuardianStones : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.guardianStonesEnabled.Value;
		}

		private class Stone
		{
			public int Needed;
			public string Boss;
		}

		// Guardian power -> bosses that must be beaten, in the game's order, and whose stone it is.
		private static readonly Dictionary<string, KeyValuePair<int, string>> s_powers = new Dictionary<string, KeyValuePair<int, string>>
		{
			{ "GP_Eikthyr", new KeyValuePair<int, string>(1, "Eikthyr") },
			{ "GP_TheElder", new KeyValuePair<int, string>(2, "the Elder") },
			{ "GP_Bonemass", new KeyValuePair<int, string>(3, "Bonemass") },
			{ "GP_Moder", new KeyValuePair<int, string>(4, "Moder") },
			{ "GP_Yagluth", new KeyValuePair<int, string>(5, "Yagluth") },
			{ "GP_Queen", new KeyValuePair<int, string>(6, "the Queen") },
			{ "GP_Fader", new KeyValuePair<int, string>(6, "Fader") }, // nothing of Fader's own in the list; the Queen is the closest
		};

		// Stone prefab hash -> what it takes, from the game's own prefabs once they are loaded.
		private static Dictionary<int, Stone> s_stones;

		// Which player was sent which stone without its trophy, and at which data revision: their game holds that copy.
		private static readonly Dictionary<KeyValuePair<long, ZDOID>, uint> s_sentEmpty = new Dictionary<KeyValuePair<long, ZDOID>, uint>();

		private static readonly Dictionary<KeyValuePair<long, ZDOID>, float> s_told = new Dictionary<KeyValuePair<long, ZDOID>, float>();
		private static readonly HashSet<KeyValuePair<long, string>> s_logged = new HashSet<KeyValuePair<long, string>>();
		private static readonly Dictionary<ZDOID, float> s_clientOwnedSince = new Dictionary<ZDOID, float>();
		private static readonly int s_requestOwn = "RPC_RequestOwn".GetStableHashCode();
		private static ZNetPeer s_receivingFrom;
		private static float s_nextTick;
		private static int s_errors;

		private static bool Ready()
		{
			if (s_stones == null && ZNetScene.instance)
			{
				s_stones = new Dictionary<int, Stone>();
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					BossStone stone = prefab ? prefab.GetComponent<BossStone>() : null;
					StatusEffect power = stone && stone.m_itemStand ? stone.m_itemStand.m_guardianPower : null;
					if (power && prefab.GetComponent<ZNetView>() && s_powers.TryGetValue(power.name, out KeyValuePair<int, string> need))
					{
						s_stones[prefab.name.GetStableHashCode()] = new Stone { Needed = need.Key, Boss = need.Value };
					}
				}
				ServersidePlugin.logger.LogInfo($"Guardian stones: {s_stones.Count} stone(s) whose trophy only players who beat the boss see");
			}
			return s_stones != null && s_stones.Count > 0 && Progress.Readable();
		}

		// Whether this player is shown the stone without its trophy.
		internal static bool ShownEmpty(ZDO zdo, ZNetPeer peer)
		{
			if (zdo == null || peer == null || !Configuration.guardianStonesEnabled.Value || !Ready() || !s_stones.TryGetValue(zdo.GetPrefab(), out Stone stone))
			{
				return false;
			}
			// The owner's game holds the stone's data; it is never handed a different copy.
			if (zdo.GetOwner() == peer.m_uid)
			{
				return false;
			}
			if (Configuration.guardianStonesExemptAdmins.Value && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName()))
			{
				return false;
			}
			return Progress.Beaten(peer) < stone.Needed;
		}

		// Whether this player may not have the stone: shown it empty, or their game may still hold an empty copy.
		private static bool MayNotHave(ZDO zdo, ZNetPeer peer)
		{
			return ShownEmpty(zdo, peer) || (zdo != null && peer != null && s_sentEmpty.ContainsKey(new KeyValuePair<long, ZDOID>(peer.m_uid, zdo.m_uid)));
		}

		private static void Refuse(ZDO zdo, ZNetPeer peer, string how)
		{
			Stone stone = s_stones[zdo.GetPrefab()];
			if (s_logged.Add(new KeyValuePair<long, string>(peer.m_uid, $"own/{zdo.m_uid}")))
			{
				ServersidePlugin.logger.LogInfo($"Guardian stones: {peer.m_playerName} may not take over {stone.Boss}'s stone (has beaten {Progress.BossNames[Progress.Beaten(peer)]}); {how}");
			}
			Message(peer, zdo, stone);
		}

		/*
			ZDOMan.SendZDOs: `zdo.Serialize(pkg)` -> `GuardianStones.Serialize(zdo, pkg, peer)`, the one
			place each object's data is written for each player.
		*/
		public static void Serialize(ZDO zdo, ZPackage pkg, ZDOMan.ZDOPeer peer)
		{
			// Every object for every player passes here; anything that is not a guardian stone goes straight through.
			if (s_stones == null || peer == null || !s_stones.ContainsKey(zdo.GetPrefab()))
			{
				zdo.Serialize(pkg);
				return;
			}
			bool empty = false;
			try
			{
				empty = ShownEmpty(zdo, peer.m_peer);
			}
			catch (Exception e)
			{
				Fault(e);
			}
			KeyValuePair<long, ZDOID> key = new KeyValuePair<long, ZDOID>(peer.m_peer.m_uid, zdo.m_uid);
			int trophy = empty ? ZDOExtraData.GetInt(zdo.m_uid, ZDOVars.s_item) : 0;
			if (trophy == 0)
			{
				zdo.Serialize(pkg);
				// The client takes only data newer than its copy: an empty copy is replaced only by a newer revision.
				if (!empty && s_sentEmpty.TryGetValue(key, out uint emptyAt) && zdo.DataRevision > emptyAt)
				{
					s_sentEmpty.Remove(key);
				}
				return;
			}
			ZDOExtraData.Set(zdo.m_uid, ZDOVars.s_item, 0);
			try
			{
				zdo.Serialize(pkg);
			}
			finally
			{
				ZDOExtraData.Set(zdo.m_uid, ZDOVars.s_item, trophy);
			}
			s_sentEmpty[key] = zdo.DataRevision;
		}

		[HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
		public static class ZDOMan_SendZDOs_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				MethodInfo serialize = AccessTools.Method(typeof(ZDO), nameof(ZDO.Serialize), new[] { typeof(ZPackage) });
				MethodInfo ours = AccessTools.Method(typeof(GuardianStones), nameof(Serialize));
				List<CodeInstruction> original = instructions.ToList();
				List<CodeInstruction> codes = new List<CodeInstruction>(original);
				int replaced = 0;
				for (int i = 0; i < codes.Count; i++)
				{
					if (!codes[i].Calls(serialize))
					{
						continue;
					}
					// The peer is SendZDOs' first argument.
					CodeInstruction peer = new CodeInstruction(OpCodes.Ldarg_1);
					peer.labels.AddRange(codes[i].labels);
					peer.blocks.AddRange(codes[i].blocks);
					codes[i] = new CodeInstruction(OpCodes.Call, ours);
					codes.Insert(i, peer);
					i++;
					replaced++;
				}
				if (replaced != 1)
				{
					ServersidePlugin.logger.LogWarning($"{__originalMethod.DeclaringType.Name}.{__originalMethod.Name}: found {replaced} ZDO.Serialize call(s), expected 1. The game changed this method; every guardian stone is sent as it is.");
					return original;
				}
				return codes;
			}
		}

		[HarmonyPatch(typeof(ItemStand), "RPC_RequestOwn")]
		public static class ItemStand_RPC_RequestOwn_Patch
		{
			static bool Prefix(ItemStand __instance, long sender)
			{
				try
				{
					ZNetView view = __instance.m_nview;
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(sender) : null;
					if (!MayNotHave(zdo, peer))
					{
						return true;
					}
					Refuse(zdo, peer, "refused");
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// A stone a player's game owns: the request would go to that game, which grants it; it is not passed on.
		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		public static class ZRoutedRpc_RouteRPC_Patch
		{
			static bool Prefix(ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (rpcData.m_methodHash != s_requestOwn || rpcData.m_targetZDO.IsNone() || ZDOMan.instance == null || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					ZDO zdo = ZDOMan.instance.GetZDO(rpcData.m_targetZDO);
					ZNetPeer peer = ZNet.instance.GetPeer(rpcData.m_senderPeerID);
					if (!MayNotHave(zdo, peer))
					{
						return true;
					}
					Refuse(zdo, peer, "not passed on to the player whose game owns it");
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// A player's game gets no guardian stone until its raid list has arrived.
		[HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
		public static class ZDOMan_CreateSyncList_Patch
		{
			static void Postfix(ZDOMan.ZDOPeer peer, List<ZDO> toSync)
			{
				if (s_stones == null || peer == null || !Configuration.guardianStonesEnabled.Value || Progress.Known(peer.m_peer))
				{
					return;
				}
				toSync.RemoveAll(zdo => s_stones.ContainsKey(zdo.GetPrefab()));
			}
		}

		// Only while data from a player is being read: who it came from.
		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix(ZDOMan __instance, ZRpc rpc)
			{
				s_receivingFrom = ZNet.instance && ZNet.instance.IsServer() ? __instance.FindPeer(rpc)?.m_peer : null;
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
				if (from == null || !s_sentEmpty.ContainsKey(new KeyValuePair<long, ZDOID>(from.m_uid, __instance.m_uid)))
				{
					return true;
				}
				// RPC_ZDOData has already taken the sender's owner and revisions by now: the server takes the stone
				// back and raises the revision, so every game gets the server's copy again (the sender's still empty).
				__instance.SetOwner(ZDOMan.GetSessionID());
				__instance.IncreaseDataRevision();
				if (s_errors++ < 20)
				{
					ServersidePlugin.logger.LogWarning($"Guardian stones: {from.m_playerName} sent data for a stone they were shown empty; not taken, the stone keeps its trophy and stays the server's");
				}
				return false;
			}
		}

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.guardianStonesEnabled.Value || Time.time < s_nextTick || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
			{
				return;
			}
			s_nextTick = Time.time + 2f;
			if (!Ready())
			{
				return;
			}
			List<ZNetPeer> peers = ZNet.instance.GetPeers();
			HashSet<long> online = new HashSet<long>(peers.Select(p => p.m_uid));
			foreach (KeyValuePair<long, ZDOID> gone in s_sentEmpty.Keys.Where(k => !online.Contains(k.Key)).ToList())
			{
				s_sentEmpty.Remove(gone);
			}
			foreach (KeyValuePair<long, ZDOID> gone in s_told.Keys.Where(k => !online.Contains(k.Key)).ToList())
			{
				s_told.Remove(gone);
			}
			s_logged.RemoveWhere(k => !online.Contains(k.Key));
			Reclaim();
			foreach (ZNetPeer peer in peers)
			{
				if (!peer.IsReady() || !Progress.Known(peer))
				{
					continue;
				}
				int beaten = Progress.Beaten(peer);
				List<string> again = new List<string>();
				foreach (KeyValuePair<KeyValuePair<long, ZDOID>, uint> sent in s_sentEmpty.Where(k => k.Key.Key == peer.m_uid).ToList())
				{
					ZDO zdo = ZDOMan.instance.GetZDO(sent.Key.Value);
					if (zdo == null)
					{
						s_sentEmpty.Remove(sent.Key);
						continue;
					}
					// A client only takes data newer than its own copy: once the player may see the stone, it goes out
					// again with the next revision -- raised here once, and only while no other player's game owns it.
					if (zdo.DataRevision > sent.Value || ShownEmpty(zdo, peer) || (zdo.HasOwner() && zdo.GetOwner() != ZDOMan.GetSessionID()))
					{
						continue;
					}
					zdo.IncreaseDataRevision();
					again.Add(s_stones[zdo.GetPrefab()].Boss);
				}
				if (again.Count > 0)
				{
					ServersidePlugin.logger.LogInfo($"Guardian stones: {peer.m_playerName} (has beaten {Progress.BossNames[beaten]}) gets the stone(s) of {string.Join(", ", again)} again, now with the trophy");
				}
				Tell(peer, beaten);
			}
		}

		// A stone a player's game took over (to hang or turn a trophy) is the server's again after 10 s.
		private static readonly List<ZDOID> s_stoneIDs = new List<ZDOID>();
		private static float s_nextStoneScan;

		private static void Reclaim()
		{
			if (Time.time >= s_nextStoneScan)
			{
				s_nextStoneScan = Time.time + 60f;
				s_stoneIDs.Clear();
				s_stoneIDs.AddRange(ZDOMan.instance.m_objectsByID.Values.Where(zdo => s_stones.ContainsKey(zdo.GetPrefab())).Select(zdo => zdo.m_uid));
			}
			long server = ZDOMan.GetSessionID();
			foreach (ZDOID id in s_stoneIDs)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo == null || !zdo.HasOwner() || zdo.GetOwner() == server)
				{
					s_clientOwnedSince.Remove(id);
					continue;
				}
				if (!s_clientOwnedSince.TryGetValue(id, out float since))
				{
					s_clientOwnedSince[id] = Time.time;
				}
				else if (Time.time - since >= 10f)
				{
					s_clientOwnedSince.Remove(id);
					zdo.SetOwner(server);
					ServersidePlugin.logger.LogInfo($"Guardian stones: {s_stones[zdo.GetPrefab()].Boss}'s stone is the server's again");
				}
			}
		}

		// A player standing at a stone that holds a trophy they may not use is told why.
		private static void Tell(ZNetPeer peer, int beaten)
		{
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			if (string.IsNullOrEmpty(Configuration.guardianStonesMessage.Value) || character == null)
			{
				return;
			}
			Vector3 at = character.GetPosition();
			foreach (KeyValuePair<long, ZDOID> sent in s_sentEmpty.Keys.Where(k => k.Key == peer.m_uid).ToList())
			{
				ZDO zdo = ZDOMan.instance.GetZDO(sent.Value);
				if (zdo == null || Vector3.Distance(zdo.GetPosition(), at) > 4f || !ShownEmpty(zdo, peer))
				{
					continue;
				}
				Stone stone = s_stones[zdo.GetPrefab()];
				if (Message(peer, zdo, stone) && s_logged.Add(new KeyValuePair<long, string>(peer.m_uid, $"told/{zdo.m_uid}")))
				{
					ServersidePlugin.logger.LogInfo($"Guardian stones: {peer.m_playerName} is at {stone.Boss}'s stone and sees it empty (has beaten {Progress.BossNames[beaten]})");
				}
			}
		}

		// At most once a quarter of an hour per player and stone.
		private static bool Message(ZNetPeer peer, ZDO zdo, Stone stone)
		{
			string message = Configuration.guardianStonesMessage.Value;
			KeyValuePair<long, ZDOID> key = new KeyValuePair<long, ZDOID>(peer.m_uid, zdo.m_uid);
			if (string.IsNullOrEmpty(message) || (s_told.TryGetValue(key, out float last) && Time.time - last < 900f))
			{
				return false;
			}
			s_told[key] = Time.time;
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.Center, string.Format(message, stone.Boss));
			return true;
		}

		private static void Fault(Exception e)
		{
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Guardian stones: {e}");
			}
		}
	}
}
