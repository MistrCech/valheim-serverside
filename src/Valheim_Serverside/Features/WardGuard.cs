using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		What the game's ward (guard stone) protects, and where that is decided. Every check of a ward is made
		in the acting player's own game (PrivateArea.CheckAccess reads Player.m_localPlayer): building, taking a
		piece down with the hammer, opening a chest or a door, changing a sign or a portal's name. The server
		believes whatever the owner of an object is told, so a game that skips a check is not stopped by
		anything -- and nothing in the game checks the ward when a piece is hit: a weapon, an arrow or an
		explosion goes through WearNTear.RPC_Damage, which only makes the ward flash. A player with a sword
		can take down a warded wall, a chest, or the ward itself (the game's ward has 1000 health), and the
		PvP switch is no help: it concerns players, not what they build. Smelters, kilns, cooking stations,
		fireplaces, crops, beds and item stands have no ward check at all.

		This feature makes the server the one that decides, for players' builds inside a ward that is switched
		on, with the game's own rule (PrivateArea.CheckAccess): a player may act where no enabled ward covers
		the place (the ward's radius, measured sideways, as the game does), or where one of the covering wards
		has them on its list -- the owner (its creator) or a player who put their name on it with E. All of it
		is done on the server, for every game that talks to it, so the clients stay vanilla.

		How: every routed RPC a client sends for an object that is not its own passes ZRoutedRpc.RPC_RoutedRPC on
		the server (the server's own handler, or forwarded to the game of the player who owns the object). It is
		looked at there, before anything is done with it, and judged by the player the connection belongs to --
		the sender id written in the packet is the client's own claim. What a game owns itself it handles itself
		(ZRoutedRpc.InvokeRoutedRPC calls its own handler and routes nothing): this mod's Core patch makes the
		server the owner of what stands near players, so the builds of others are not a thief's to own, but a
		piece a player has just placed stays with their game while they are in the area (the limit on switched-on
		wards below is therefore also kept on the state of the wards).
		- Structures (the build pieces players made -- anything with a creator -- and the ward itself):
		  RPC_Damage, RPC_Remove, RPC_Repair of a player who may not act there are dropped. A hit with no attacker
		  (fire, a falling object) or one by a creature that the sender's game owns (a raid, a monster they
		  lured) is not a player's and is not touched: monsters still do what they do in the game. The ward
		  flashes, as it does for a hit, and the player is told.
		- Interactions: what the game's own client checks refuse (chests with the guard stone flag, doors, item and armour
		  stands, fermenters, beehives, sap collectors, incinerators, the map table's writing) is refused by the server
		  too, and what has no check: taking the product out of a smelter or a cooking station, switching a fireplace
		  off, claiming a bed, picking crops of the Plant kind. Putting something in -- ore, fuel, food, ammo -- is left
		  alone: it is a gift, and the player's game has already taken the item from their inventory when it sends
		  the request, so a refusal would only destroy it. Traps are left alone too: the same request is sent when
		  something steps on one. A chest asked to open answers the player no, as it does when it is in use.
		- Spoofing: a packet whose sender id is not the connection it came in on is dropped; the ward's
		  ToggleEnabled and TogglePermitted RPCs take the player's id as a parameter, and a player's game may
		  only send its own (the game does).
		- Destroy requests (RPC DestroyZDO): the game asks the server to destroy objects its own game owns, so a request for
		  objects that another game or the server owns is what a modified game sends. But the game also claims an
		  object and destroys it in the same frame (the hammer on a piece that is not a WearNTear, picking up, harvesting)
		  and the claim can reach the server after the request, so an honest client shows up in this count too. LogOnly
		  (the default) counts them and writes a summary to the log; On takes them out of the request. Leave it in
		  LogOnly until the log shows what the honest clients do.

		Not covered here: ground items, tombstones (anyone may open), ships and carts as vehicles (hitting them is covered
		as a piece), the sign's text (written straight into its ZDO by the player's game), terrain, fire and
		falling or colliding objects (a hit with no attacker is not a player's own and is let through), an object
		whose ownership a player's game takes without asking its owner (a boss trophy used on a turret) and then
		hits itself, and anything a modified game does: it can write an object's data directly, name another
		player's character as its own, or name a creature as the attacker of a hit. Each is named in the README.

		Every refusal is written to <world>.guard.log (the character guard's and the item ledger's file): who,
		what, where, whose ward. With LogOnly the line ends "LogOnly: let through" and nothing is stopped.
	*/
	public class WardGuard : IFeature
	{
		public enum Mode { Off, LogOnly, On }

		public bool FeatureEnabled()
		{
			return Configuration.wardGuardEnabled.Value;
		}

		private static readonly int s_damage = "RPC_Damage".GetStableHashCode();
		private static readonly int s_remove = "RPC_Remove".GetStableHashCode();
		private static readonly int s_repair = "RPC_Repair".GetStableHashCode();
		private static readonly int s_toggleEnabled = "ToggleEnabled".GetStableHashCode();
		private static readonly int s_togglePermitted = "TogglePermitted".GetStableHashCode();
		private static readonly int s_destroy = "DestroyZDO".GetStableHashCode();
		private static readonly int s_player = "Player".GetStableHashCode();

		// Ward prefab hash -> radius, for the wards players own.
		private static Dictionary<int, float> s_wardRadius;
		// RPC method hash -> the prefabs (hash) a ward guards that RPC for.
		private static readonly Dictionary<int, HashSet<int>> s_gate = new Dictionary<int, HashSet<int>>();
		// A request for a chest, and what the chest answers when it says no.
		private static readonly Dictionary<int, string> s_answer = new Dictionary<int, string>();
		private static readonly Dictionary<int, string> s_names = new Dictionary<int, string>();
		private static List<ZDOID> s_wardIds;
		private static float s_nextScan;
		private static int s_errors;
		private static readonly List<int> s_permittedKeys = new List<int>();
		private static readonly Dictionary<string, float> s_rate = new Dictionary<string, float>();
		private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
		private static readonly Dictionary<long, DestroyTally> s_destroyTally = new Dictionary<long, DestroyTally>();

		private class DestroyTally
		{
			public string name;
			public float since;
			public int requests;
			public int foreign;
			public readonly Dictionary<string, int> prefabs = new Dictionary<string, int>();
		}

		private struct Header
		{
			public long msg, sender, target;
			public ZDOID zdo;
			public int method;
		}

		private static void Gate(int prefab, params string[] methods)
		{
			foreach (string method in methods)
			{
				int hash = method.GetStableHashCode();
				if (!s_gate.TryGetValue(hash, out HashSet<int> set))
				{
					s_gate[hash] = set = new HashSet<int>();
				}
				set.Add(prefab);
				s_names[hash] = method;
			}
		}

		private static bool Ready()
		{
			if (s_wardRadius == null && ZNetScene.instance && ZNetScene.instance.m_prefabs != null && ZNetScene.instance.m_prefabs.Count > 0)
			{
				Build();
			}
			if (s_wardRadius == null || ZDOMan.instance == null)
			{
				return false;
			}
			if (s_wardIds == null || Time.time >= s_nextScan)
			{
				Scan();
			}
			return true;
		}

		// What the game's own client checks, and the pieces that have none, as the prefabs say.
		private static void Build()
		{
			Dictionary<int, float> wards = new Dictionary<int, float>();
			int guarded = 0;
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (!prefab || !prefab.GetComponent<ZNetView>())
				{
					continue;
				}
				int hash = prefab.name.GetStableHashCode();
				PrivateArea area = prefab.GetComponent<PrivateArea>();
				if (area && area.m_ownerFaction == Character.Faction.Players)
				{
					wards[hash] = area.m_radius;
				}
				Container container = prefab.GetComponent<Container>();
				if (container && container.m_checkGuardStone)
				{
					Gate(hash, "RPC_RequestOpen", "RPC_RequestStack", "RPC_RequestTakeAll");
					guarded++;
				}
				Door door = prefab.GetComponent<Door>();
				if (door && door.m_checkGuardStone)
				{
					Gate(hash, "UseDoor");
					guarded++;
				}
				Plant plant = prefab.GetComponent<Plant>();
				if (plant && plant.m_grownPrefabs != null)
				{
					foreach (GameObject grown in plant.m_grownPrefabs)
					{
						if (grown && grown.GetComponent<Pickable>())
						{
							Gate(grown.name.GetStableHashCode(), "RPC_Pick");
						}
					}
				}
				if (!prefab.GetComponent<Piece>())
				{
					continue;
				}
				// Taking things out and changing things. Putting something in (ore, fuel, food, ammo) is not guarded: the player's game has
				// already taken the item from their inventory when it sends the request, so a refusal would only destroy it.
				if (prefab.GetComponent<Smelter>()) { Gate(hash, "RPC_EmptyProcessed"); guarded++; }
				if (prefab.GetComponent<CookingStation>()) { Gate(hash, "RPC_RemoveDoneItem"); guarded++; }
				if (prefab.GetComponent<Fireplace>()) { Gate(hash, "RPC_ToggleOn"); guarded++; }
				if (prefab.GetComponent<Fermenter>()) { Gate(hash, "RPC_Tap"); guarded++; }
				if (prefab.GetComponent<Beehive>() || prefab.GetComponent<SapCollector>()) { Gate(hash, "RPC_Extract"); guarded++; }
				if (prefab.GetComponent<Incinerator>()) { Gate(hash, "RPC_RequestIncinerate"); guarded++; }
				if (prefab.GetComponent<ItemStand>()) { Gate(hash, "RPC_DropItem", "RPC_DestroyAttachment", "RPC_RequestOwn"); guarded++; }
				if (prefab.GetComponent<ArmorStand>()) { Gate(hash, "RPC_DropItem", "RPC_DropItemByName", "RPC_DestroyAttachment", "RPC_SetPose", "RPC_RequestOwn"); guarded++; }
				if (prefab.GetComponent<Bed>()) { Gate(hash, "SetOwner"); guarded++; }
				if (prefab.GetComponent<MapTable>()) { Gate(hash, "MapData"); guarded++; }
			}
			s_answer["RPC_RequestOpen".GetStableHashCode()] = "RPC_OpenResponse";
			s_answer["RPC_RequestStack".GetStableHashCode()] = "RPC_StackResponse";
			s_answer["RPC_RequestTakeAll".GetStableHashCode()] = "RPC_TakeAllResponse";
			s_wardRadius = wards;
			ServersidePlugin.logger.LogInfo($"Ward guard: structures {Configuration.wardGuardStructures.Value}, interactions {Configuration.wardGuardInteractions.Value}, "
				+ $"spoofing {Configuration.wardGuardSpoofing.Value}, destroy requests {Configuration.wardGuardDestroyRequests.Value}. "
				+ $"Wards: {string.Join(", ", wards.Select(kv => ZNetScene.instance.GetPrefab(kv.Key).name + " " + kv.Value.ToString("0.#") + " m"))}; "
				+ $"{s_gate.Count} kinds of request on {s_gate.Values.SelectMany(s => s).Distinct().Count()} prefab(s) ({guarded} component(s)) are held to the ward's list.");
		}

		private static void Scan()
		{
			s_nextScan = Time.time + 5f;
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			List<ZDOID> wards = new List<ZDOID>();
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
			{
				if (s_wardRadius.ContainsKey(zdo.GetPrefab()))
				{
					wards.Add(zdo.m_uid);
				}
			}
			s_wardIds = wards;
			if (!s_scanLogged || (watch.ElapsedMilliseconds > 40 && Rate("slowscan", 600f)))
			{
				s_scanLogged = true;
				ServersidePlugin.logger.LogInfo($"Ward guard: {wards.Count} ward(s) among {ZDOMan.instance.m_objectsByID.Count} objects, found in {watch.Elapsed.TotalMilliseconds:0.0} ms (looked for every 5 s)");
			}
		}

		private static bool s_scanLogged;

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
			return Configuration.wardGuardExemptAdmins.Value && peer != null && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
		}

		private static int PermittedKey(int i)
		{
			while (s_permittedKeys.Count <= i)
			{
				s_permittedKeys.Add(("pu_id" + s_permittedKeys.Count).GetStableHashCode());
			}
			return s_permittedKeys[i];
		}

		// On the ward's list: its creator, or a player who put their name on it (PrivateArea.HaveLocalAccess).
		private static bool OnList(ZDO ward, long player)
		{
			if (ward.GetLong(ZDOVars.s_creator, 0L) == player)
			{
				return true;
			}
			int count = Mathf.Min(ward.GetInt(ZDOVars.s_permitted, 0), 512);
			for (int i = 0; i < count; i++)
			{
				if (ward.GetLong(PermittedKey(i), 0L) == player)
				{
					return true;
				}
			}
			return false;
		}

		/*
			The game's rule (PrivateArea.CheckAccess, not the ward-placing form): nothing in the way where no enabled
			ward covers the place; otherwise one covering ward with the player on its list is enough. A player whose
			game has not told the server who they are yet (no character) is nobody: let in only where no ward is.
			denying is the first covering ward that does not have the player on its list.
		*/
		private static bool Allowed(Vector3 at, long player, out ZDO denying)
		{
			denying = null;
			foreach (ZDOID id in s_wardIds)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				if (ward == null || !s_wardRadius.TryGetValue(ward.GetPrefab(), out float radius) || !ward.GetBool(ZDOVars.s_enabled))
				{
					continue;
				}
				Vector3 p = ward.GetPosition();
				float dx = p.x - at.x;
				float dz = p.z - at.z;
				if (dx * dx + dz * dz >= radius * radius)
				{
					continue;
				}
				if (player != 0L && OnList(ward, player))
				{
					denying = null;
					return true;
				}
				if (denying == null)
				{
					denying = ward;
				}
			}
			return denying == null;
		}

		// For the anti-cheat: may this player build at this place by the ward rule? True where no ward is known to say otherwise (the wards
		// cannot be read yet). denying is the first ward that covers the place and does not have the player on its list.
		internal static bool MayBuildAt(Vector3 at, long player, out ZDO denying)
		{
			denying = null;
			return !Ready() || Allowed(at, player, out denying);
		}

		private static string NameOf(ZDO zdo)
		{
			return zdo == null ? "?" : ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name ?? "#" + zdo.GetPrefab();
		}

		private static string Where(ZDO zdo)
		{
			Vector3 p = zdo.GetPosition();
			return $"{p.x:0},{p.z:0}";
		}

		// True if this key has not been answered for the last `seconds`.
		private static bool Rate(string key, float seconds)
		{
			if (s_rate.Count > 4000)
			{
				s_rate.Clear();
			}
			if (s_rate.TryGetValue(key, out float last) && Time.time - last < seconds)
			{
				return false;
			}
			s_rate[key] = Time.time;
			return true;
		}

		private static float s_errorsSince;

		private static void Fault(Exception e)
		{
			if (Time.realtimeSinceStartup - s_errorsSince > 900f)
			{
				s_errorsSince = Time.realtimeSinceStartup;
				s_errors = 0;
			}
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Ward guard: {e}");
			}
		}

		// A player's name goes into a log line: no line breaks or other control characters, or a name could forge a line.
		private static string Clean(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "?";
			}
			char[] c = name.ToCharArray();
			for (int i = 0; i < c.Length; i++)
			{
				if (char.IsControl(c[i]))
				{
					c[i] = ' ';
				}
			}
			return new string(c);
		}

		[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
		public static class ZRoutedRpc_RPC_RoutedRPC_Patch
		{
			static bool Prefix(ZRpc rpc, ref ZPackage pkg)
			{
				if (!Configuration.wardGuardEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				int start = pkg.GetPos();
				ZPackage replacement = null;
				bool pass = true;
				try
				{
					Header h = new Header();
					h.msg = pkg.ReadLong();
					h.sender = pkg.ReadLong();
					h.target = pkg.ReadLong();
					h.zdo = pkg.ReadZDOID();
					h.method = pkg.ReadInt();
					ZNetPeer peer = ZNet.instance.GetPeer(rpc);
					if (peer != null && Ready())
					{
						pass = Decide(peer, h, pkg, out replacement);
					}
				}
				catch (Exception e)
				{
					Fault(e);
					pass = true;
					replacement = null;
				}
				if (pass)
				{
					// What the game reads next is the packet from its first byte, or the one rebuilt for it.
					if (replacement != null)
					{
						pkg = replacement;
					}
					else
					{
						pkg.SetPos(start);
					}
				}
				return pass;
			}
		}

		// pkg is just past the header; false drops the RPC; a replacement is the rebuilt packet to hand on instead.
		private static bool Decide(ZNetPeer peer, Header h, ZPackage pkg, out ZPackage replacement)
		{
			replacement = null;
			Mode spoofing = Configuration.wardGuardSpoofing.Value;
			if (spoofing != Mode.Off && h.sender != peer.m_uid)
			{
				Report(peer, null, null, "spoofing", $"sent an RPC as {h.sender} on the connection of {peer.m_uid}", spoofing == Mode.On);
				if (spoofing == Mode.On)
				{
					return false;
				}
			}
			if (h.method == s_destroy)
			{
				return DestroyRequest(peer, h, pkg, out replacement);
			}
			if (h.zdo.IsNone())
			{
				return true;
			}
			ZDO target = ZDOMan.instance.GetZDO(h.zdo);
			if (target == null)
			{
				return true;
			}
			int prefab = target.GetPrefab();
			if (h.method == s_toggleEnabled || h.method == s_togglePermitted)
			{
				int limit = h.method == s_toggleEnabled ? Configuration.wardGuardMaxActiveWards.Value : 0;
				if (!s_wardRadius.ContainsKey(prefab) || (spoofing == Mode.Off && limit <= 0))
				{
					return true;
				}
				long claimed = pkg.ReadPackage().ReadLong();
				long actual = PlayerId(peer);
				if (actual == 0L || claimed != actual)
				{
					if (spoofing == Mode.Off)
					{
						return true;
					}
					Report(peer, target, null, "spoofing", $"asked a ward to {(h.method == s_toggleEnabled ? "switch itself" : "change its list")} as player {claimed}, theirs is {actual}", spoofing == Mode.On);
					return spoofing != Mode.On;
				}
				return limit <= 0 || IsAdmin(peer) || WithinWardLimit(peer, target, actual, limit);
			}
			if (h.method == s_damage || h.method == s_remove || h.method == s_repair)
			{
				Mode structures = Configuration.wardGuardStructures.Value;
				if (structures == Mode.Off || (target.GetLong(ZDOVars.s_creator, 0L) == 0L && !s_wardRadius.ContainsKey(prefab)) || IsAdmin(peer))
				{
					return true;
				}
				if (Allowed(target.GetPosition(), PlayerId(peer), out ZDO denying))
				{
					return true;
				}
				if (h.method == s_damage && NotAPlayersHit(peer, pkg))
				{
					return true;
				}
				string what = h.method == s_damage ? "hit" : h.method == s_remove ? "take down" : "repair";
				Report(peer, target, denying, "structure", what, structures == Mode.On);
				return structures != Mode.On;
			}
			Mode interactions = Configuration.wardGuardInteractions.Value;
			if (interactions != Mode.Off && s_gate.TryGetValue(h.method, out HashSet<int> prefabs) && prefabs.Contains(prefab) && !IsAdmin(peer))
			{
				if (Allowed(target.GetPosition(), PlayerId(peer), out ZDO denying))
				{
					return true;
				}
				Report(peer, target, denying, "interaction", s_names[h.method], interactions == Mode.On);
				if (interactions == Mode.On)
				{
					if (s_answer.TryGetValue(h.method, out string answer))
					{
						ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, h.zdo, answer, false);
					}
					return false;
				}
			}
			return true;
		}

		/*
			A ward is switched on by its creator pressing E on it (RPC ToggleEnabled; the game does nothing for anyone else). A creator
			who already has `limit` wards on is refused: the request is dropped and they are told. Switching a ward off is never held back.
			The scan that finds the wards runs every 5 seconds, so a ward the creator switched on a moment ago is remembered here
			for ten seconds, or three quick presses on three wards would all pass.
		*/
		private static readonly Dictionary<ZDOID, float> s_switchedOn = new Dictionary<ZDOID, float>();

		private static bool WithinWardLimit(ZNetPeer peer, ZDO ward, long player, int limit)
		{
			// A ward switched on a moment ago is on, whatever the copy of it here says yet: asking again is asking for it to go off.
			if (s_switchedOn.Remove(ward.m_uid) || ward.GetLong(ZDOVars.s_creator, 0L) != player || ward.GetBool(ZDOVars.s_enabled))
			{
				return true;
			}
			foreach (ZDOID id in s_switchedOn.Where(kv => Time.time - kv.Value > 10f).Select(kv => kv.Key).ToList())
			{
				s_switchedOn.Remove(id);
			}
			HashSet<ZDOID> on = new HashSet<ZDOID>();
			foreach (ZDOID id in s_wardIds)
			{
				ZDO other = ZDOMan.instance.GetZDO(id);
				if (other != null && id != ward.m_uid && other.GetLong(ZDOVars.s_creator, 0L) == player && other.GetBool(ZDOVars.s_enabled))
				{
					on.Add(id);
				}
			}
			foreach (ZDOID id in s_switchedOn.Keys)
			{
				ZDO other = ZDOMan.instance.GetZDO(id);
				if (id != ward.m_uid && other != null && other.GetLong(ZDOVars.s_creator, 0L) == player)
				{
					on.Add(id);
				}
			}
			if (on.Count < limit)
			{
				s_switchedOn[ward.m_uid] = Time.time;
				return true;
			}
			if (Rate($"log/limit/{peer.m_uid}", 60f))
			{
				string line = $"{Clean(peer.m_playerName)} ({player}) tried to switch on a ward at {Where(ward)} with {on.Count} of {limit} already on -- refused";
				ServersidePlugin.logger.LogInfo("Ward guard: " + line);
				GuardLog.Write("ward guard: " + line);
			}
			string message = Configuration.wardGuardMaxActiveWardsMessage.Value;
			if (!string.IsNullOrEmpty(message) && Rate($"say/limit/{peer.m_uid}", 3f))
			{
				string text;
				try
				{
					text = string.Format(message, limit);
				}
				catch (FormatException)
				{
					text = message;
				}
				PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, text);
			}
			return false;
		}

		/*
			The same limit on what is, not on what is asked. A ward a player has just placed is owned by that player's game (it stays with
			the game that placed it while that player is in the area), so pressing E on it is handled there and the request never passes
			the server. The switched-on ward shows up in the data the game sends the server within a moment, and the pass that looks for
			wards counts them: a creator with more switched on than the limit has the newest switched off by the server (it takes the ward
			over, writes `enabled` off and tells them). The wards that are on when the server starts stay on whatever the limit is.
		*/
		private static readonly Dictionary<ZDOID, float> s_firstOn = new Dictionary<ZDOID, float>();
		private static readonly HashSet<ZDOID> s_standing = new HashSet<ZDOID>();
		private static bool s_limitSeeded;
		private static int s_lastLimit = -1;
		private static float s_nextLimit;

		public static void Tick()
		{
			if (!Configuration.wardGuardEnabled.Value || Time.time < s_nextLimit || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
			{
				return;
			}
			s_nextLimit = Time.time + 5f;
			if (!Ready())
			{
				return;
			}
			EnforceWardLimit();
		}

		private static void EnforceWardLimit()
		{
			int limit = Configuration.wardGuardMaxActiveWards.Value;
			Dictionary<long, List<ZDO>> on = new Dictionary<long, List<ZDO>>();
			foreach (ZDOID id in s_wardIds)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				if (ward == null || !ward.GetBool(ZDOVars.s_enabled))
				{
					s_firstOn.Remove(id);
					continue;
				}
				if (!s_firstOn.ContainsKey(id))
				{
					s_firstOn[id] = Time.time;
				}
				long creator = ward.GetLong(ZDOVars.s_creator, 0L);
				if (!on.TryGetValue(creator, out List<ZDO> list))
				{
					on[creator] = list = new List<ZDO>();
				}
				list.Add(ward);
			}
			if (!s_limitSeeded || limit != s_lastLimit)
			{
				// The first look, and every time the limit is changed: what is on now was let on (or came before the rule) and stays.
				bool first = !s_limitSeeded;
				s_limitSeeded = true;
				s_lastLimit = limit;
				foreach (List<ZDO> list in on.Values)
				{
					foreach (ZDO ward in list)
					{
						s_standing.Add(ward.m_uid);
					}
				}
				if (first)
				{
					string above = string.Join(", ", on.Where(kv => limit > 0 && kv.Value.Count > limit).Select(kv => kv.Key + " has " + kv.Value.Count));
					ServersidePlugin.logger.LogInfo($"Ward guard: {s_standing.Count} ward(s) are switched on at the start; they stay on whatever MaxActiveWards is" + (above.Length > 0 ? $" (above the limit of {limit}: {above})" : ""));
				}
				else
				{
					ServersidePlugin.logger.LogInfo($"Ward guard: MaxActiveWards is {limit} now; the {s_standing.Count} ward(s) that are on stay on");
				}
				return;
			}
			if (limit <= 0)
			{
				return;
			}
			Dictionary<long, ZNetPeer> peers = new Dictionary<long, ZNetPeer>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				long player = peer.IsReady() ? PlayerId(peer) : 0L;
				if (player != 0L)
				{
					peers[player] = peer;
				}
			}
			foreach (KeyValuePair<long, List<ZDO>> creator in on)
			{
				// Only while the creator is in the game: that is when a ward is switched on, and they can be told.
				if (creator.Key == 0L || !peers.TryGetValue(creator.Key, out ZNetPeer owner))
				{
					continue;
				}
				if (IsAdmin(owner))
				{
					continue;
				}
				int room = limit - creator.Value.Count(w => s_standing.Contains(w.m_uid));
				foreach (ZDO ward in creator.Value.Where(w => !s_standing.Contains(w.m_uid)).OrderBy(w => s_firstOn[w.m_uid]).ThenBy(w => w.m_uid).Skip(Math.Max(0, room)))
				{
					ward.SetOwner(ZDOMan.GetSessionID());
					ward.Set(ZDOVars.s_enabled, false);
					s_firstOn.Remove(ward.m_uid);
					s_switchedOn.Remove(ward.m_uid);
					if (Rate($"log/limit/{creator.Key}/{ward.m_uid}", 60f))
					{
						string line = $"{Clean(owner.m_playerName)} ({creator.Key}) has more than {limit} ward(s) switched on: the ward at {Where(ward)} was switched off";
						ServersidePlugin.logger.LogInfo("Ward guard: " + line);
						GuardLog.Write("ward guard: " + line);
					}
					string message = Configuration.wardGuardMaxActiveWardsMessage.Value;
					if (!string.IsNullOrEmpty(message) && Rate($"say/limit/{owner.m_uid}", 3f))
					{
						string text;
						try
						{
							text = string.Format(message, limit);
						}
						catch (FormatException)
						{
							text = message;
						}
						PlayerMessages.Show(owner.m_uid, MessageHud.MessageType.Center, text);
					}
				}
			}
		}

		// A hit that is not a player's own: it has no attacker (fire, a falling object), or a creature made it in the game of
		// the player who sent it (a raid, a monster they lured). A player's own hit, or one whose attacker is not known, is.
		private static bool NotAPlayersHit(ZNetPeer peer, ZPackage pkg)
		{
			try
			{
				ZPackage parameters = pkg.ReadPackage();
				HitData hit = new HitData();
				hit.Deserialize(ref parameters);
				if (hit.m_attacker.IsNone())
				{
					return true;
				}
				ZDO attacker = ZDOMan.instance.GetZDO(hit.m_attacker);
				return attacker != null && attacker.GetPrefab() != s_player && attacker.GetOwner() == peer.m_uid;
			}
			catch (Exception e)
			{
				Fault(e);
				return false;
			}
		}

		/*
			RPC DestroyZDO: a count and that many ids. The game sends it only for what it owns (ZDOMan.DestroyZDO
			queues nothing else), so the ones that another game or the server owns are what a modified game does.
			LogOnly counts them; On takes them out of the request.
		*/
		private static bool DestroyRequest(ZNetPeer peer, Header h, ZPackage pkg, out ZPackage replacement)
		{
			replacement = null;
			Mode mode = Configuration.wardGuardDestroyRequests.Value;
			if (mode == Mode.Off)
			{
				return true;
			}
			// The request's one parameter is a package of its own: the parameters hold it length-prefixed.
			ZPackage parameters = pkg.ReadPackage().ReadPackage();
			int count = parameters.ReadInt();
			// The count is the client's own: a request that cannot hold what it says is left to the game, which stops at the first missing id.
			if (count < 0 || count > 50000 || (long)count * 12L > parameters.m_stream.Length - parameters.GetPos())
			{
				return true;
			}
			List<ZDOID> keep = new List<ZDOID>(count);
			List<ZDO> foreign = new List<ZDO>();
			for (int i = 0; i < count; i++)
			{
				ZDOID id = parameters.ReadZDOID();
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				long owner = zdo != null ? zdo.GetOwner() : 0L;
				if (zdo != null && owner != 0L && owner != peer.m_uid)
				{
					foreign.Add(zdo);
				}
				else
				{
					keep.Add(id);
				}
			}
			if (!s_destroyTally.TryGetValue(peer.m_uid, out DestroyTally tally))
			{
				s_destroyTally[peer.m_uid] = tally = new DestroyTally { since = Time.time, name = Clean(peer.m_playerName) };
			}
			tally.requests++;
			tally.foreign += foreign.Count;
			foreach (ZDO zdo in foreign)
			{
				string name = NameOf(zdo);
				tally.prefabs[name] = tally.prefabs.TryGetValue(name, out int n) ? n + 1 : 1;
			}
			FlushTallies(mode);
			if (mode != Mode.On || foreign.Count == 0)
			{
				return true;
			}
			if (keep.Count == 0)
			{
				return false;
			}
			ZPackage list = new ZPackage();
			list.Write(keep.Count);
			foreach (ZDOID id in keep)
			{
				list.Write(id);
			}
			ZPackage rebuilt = new ZPackage();
			rebuilt.Write(list);
			rebuilt.SetPos(0);
			ZRoutedRpc.RoutedRPCData data = new ZRoutedRpc.RoutedRPCData
			{
				m_msgID = h.msg,
				m_senderPeerID = h.sender,
				m_targetPeerID = h.target,
				m_targetZDO = h.zdo,
				m_methodHash = h.method,
				m_parameters = rebuilt,
			};
			ZPackage wire = new ZPackage();
			data.Serialize(wire);
			wire.SetPos(0);
			replacement = wire;
			return true;
		}

		// What the destroy requests of a player came to, written out once the first was five minutes ago or the player has left.
		private static void FlushTallies(Mode mode)
		{
			foreach (long uid in s_destroyTally.Keys.ToList())
			{
				DestroyTally tally = s_destroyTally[uid];
				bool gone = ZNet.instance.GetPeer(uid) == null;
				if (!gone && Time.time - tally.since <= 300f)
				{
					continue;
				}
				s_destroyTally.Remove(uid);
				if (tally.foreign == 0)
				{
					continue;
				}
				string line = $"{tally.name} asked in {tally.requests} request(s) for {tally.foreign} object(s) that it does not own to be destroyed: "
					+ string.Join(", ", tally.prefabs.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " x" + kv.Value)) + (mode == Mode.On ? " -- taken out" : " -- LogOnly: left in");
				ServersidePlugin.logger.LogInfo("Ward guard: " + line);
				GuardLog.Write("ward guard: " + line);
			}
		}

		/*
			One refusal: written to <world>.guard.log (once a minute per player, kind and ward, with how many there were since the last line),
			and with `enforced` the ward flashes and the player is told (the message at most every 15 s, the flash every 3 s).
		*/
		private static void Report(ZNetPeer peer, ZDO target, ZDO ward, string kind, string what, bool enforced)
		{
			string key = $"{peer.m_uid}/{kind}/{(ward != null ? ward.m_uid.ToString() : target != null ? target.m_uid.ToString() : "-")}";
			if (s_counts.Count > 4000)
			{
				s_counts.Clear();
			}
			int seen = (s_counts.TryGetValue(key, out int before) ? before : 0) + 1;
			s_counts[key] = seen;
			if (Rate("log/" + key, 60f))
			{
				s_counts[key] = 0;
				string who = $"{Clean(peer.m_playerName)} ({PlayerId(peer)})";
				string owner = ward != null ? $"; ward of {Clean(ward.GetString(ZDOVars.s_creatorName, "?"))} at {Where(ward)}" : "";
				string outcome = enforced ? (kind == "spoofing" ? "dropped" : "refused") : "LogOnly: let through";
				string line;
				if (target == null)
				{
					line = $"{who} {what} [{seen}x since the last line] -- {outcome}";
				}
				else if (kind == "spoofing")
				{
					line = $"{who} {what}: {NameOf(target)} at {Where(target)} [{seen}x since the last line] -- {outcome}";
				}
				else
				{
					line = $"{who} tried to {what} {NameOf(target)} at {Where(target)}{owner} [{seen}x since the last line] -- {outcome}";
				}
				ServersidePlugin.logger.LogInfo("Ward guard: " + line);
				GuardLog.Write("ward guard: " + line);
			}
			if (!enforced || kind == "spoofing")
			{
				return;
			}
			if (Configuration.wardGuardFlash.Value && ward != null && Rate("flash/" + ward.m_uid, 3f))
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ward.m_uid, "FlashShield");
			}
			string message = Configuration.wardGuardMessage.Value;
			if (!string.IsNullOrEmpty(message) && Rate("say/" + peer.m_uid, 15f))
			{
				PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, message);
			}
		}
	}
}
