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
		- Outside wards (OutsideWards, Off by default): a player's build that no ward of the builder's own people covers --
		  no enabled ward there, or only wards whose list does not have the builder on it -- is the builder's: RPC_Damage,
		  RPC_Remove and the interactions below are dropped unless the sender built it, is on the list of one of the
		  builder's wards (switched on or not: the people the builder lets into their base) or is an admin. A ward put up
		  beside someone's build therefore gives its owner nothing over it. Repairing and putting things in stay open, and
		  the same hits as above (no attacker, a creature) are let through. Where the builder's own ward covers the build,
		  that ward's list decides. A crop has no builder in the game (Plant.Grow makes it a new object): the sapling's
		  creator is written into it as it grows (s_planter), so a crop is its planter's.
		- Own pieces (OwnPieces, off by default): the builder may always act on their piece, inside anyone's ward too.
		- Tombstones (Tombstones, Off by default): opening a tombstone or taking from it (RPC_RequestOpen, RPC_RequestStack,
		  RPC_RequestTakeAll) is for the player who died, the players on the list of one of their wards and admins, anywhere.
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

		DS 1.33.0, six more rules, all Off by default:
		- Claims: a game that takes over (SetOwnerInternal, from ZDOMan.RPC_ZDOData) a player's build, without a new object,
		  from a server or another game that owns it, and that by the rules above may not act on it, is the first step of
		  destroying (DestroyZDO has no owner check) or rewriting it. On leaves the owner where it was and drops the data
		  in the packet; LogOnly writes a line ("take over"). An object nobody owns, and a player's own, are let alone.
		- IndirectHits: the hits let through above (no attacker, a creature the sender's game owns) are what a catapult,
		  fire and a falling object send, but also what a modified game sends. On treats them as the sender's own hit;
		  LogOnly writes the line with the kind of hit.
		- TamedAnimals: a tamed creature in an enabled ward whose list the player is not on: hit (as IndirectHits says),
		  Tameable Command / SetName / AddSaddle / SetSaddle, Sadle RequestControl (answered no) / RemoveSaddle / Controls.
		- Vehicles: a ship or cart with a builder, anywhere, is for the builder, the players on the list of one of the
		  builder's wards and admins: RequestControl is answered no, steering is dropped, a cart's RPC_RequestOwn is
		  answered RPC_RequestDenied, a ship's chest RPCs are answered no, and ZDO.SetOwner does not hand such a ship
		  or cart to a game that may not use it (the server keeps it). A catapult (a cart with a Catapult on the same
		  object) has RPC_SetLoadedVisual, RPC_Shoot and RPC_OnLegUse dropped for a player who may not use it, and an
		  RPC_Shoot to a catapult that the server owns is dropped for anyone but an admin when Vehicles, IndirectHits,
		  Structures or OutsideWards is not Off (the strictest decides): the owner's game shoots, so the server would
		  shoot itself, and its hits are its own and never pass the guard. The cart's chest is answered like a ship's.
		- Terrain: RPC_ApplyOperation on a terrain compiler is dropped when the operation reaches (its radius) into an
		  enabled ward whose list the player is not on (Structures needed), or comes within TerrainMargin of a build of a
		  player whose side the player is not on (OutsideWards needed). The builder with OwnPieces and admins are exempt.
		- DestroyLog: a line when a player's build is destroyed by someone other than its builder, from the game's own
		  WearNTear.Destroy, with who (a player, a creature and the player it was after, a kind of hit and the game that
		  sent it, "took it down", "collapsed or removed by the server"), at most one line a minute per destroyer and
		  builder; a request to destroy what another game owns is logged as well, whatever DestroyRequests says. Nothing
		  is stopped.

		Not covered here: ground items, tombstones unless Tombstones is on (in the game anyone may open them), a ship or
		cart moved by physics, the sign's text written by the game of whoever owns the sign (Claims covers the takeover,
		not a game writing into its own object), fire and falling objects with no player behind them (IndirectHits covers
		only what a player's game sends), a terrain compiler whose owner is a player's game, a creature the server runs
		hitting a build (a raid), an object whose ownership a player's game takes without asking its owner and then
		hits itself (Claims On stops the takeover of a player's build, not of a trophy turret), and anything a modified
		game does: it can write an object's data directly, name another player's character as its own, or name a
		creature as the attacker of a hit. Each is named in the README.

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
		// 1.33.0: tamed animals, vehicles, terrain
		private static readonly int s_requestControl = "RequestControl".GetStableHashCode();
		private static readonly int s_requestOwn = "RPC_RequestOwn".GetStableHashCode();
		private static readonly int s_shoot = "RPC_Shoot".GetStableHashCode();
		private static readonly HashSet<int> s_catapultCalls = new HashSet<int> { s_shoot, "RPC_SetLoadedVisual".GetStableHashCode(), "RPC_OnLegUse".GetStableHashCode() };
		private static readonly int s_applyOperation = "RPC_ApplyOperation".GetStableHashCode();
		// the requests for a tamed animal that are held to the ward's list, and what to call them in the log
		private static readonly Dictionary<int, string> s_tamedNames = new Dictionary<int, string>
		{
			{ "RPC_Damage".GetStableHashCode(), "hit" },
			{ "Command".GetStableHashCode(), "give orders to" },
			{ "SetName".GetStableHashCode(), "rename" },
			{ "AddSaddle".GetStableHashCode(), "saddle" },
			{ "SetSaddle".GetStableHashCode(), "saddle" },
			{ "RequestControl".GetStableHashCode(), "ride" },
			{ "RemoveSaddle".GetStableHashCode(), "unsaddle" },
			{ "Controls".GetStableHashCode(), "ride" },
		};
		// the requests that use a ship or a cart, and what to call them in the log
		private static readonly Dictionary<int, string> s_vehicleNames = new Dictionary<int, string>
		{
			{ "RequestControl".GetStableHashCode(), "take the helm of" },
			{ "Forward".GetStableHashCode(), "set the sail of" },
			{ "Backward".GetStableHashCode(), "set the sail of" },
			{ "Stop".GetStableHashCode(), "set the sail of" },
			{ "Rudder".GetStableHashCode(), "steer" },
			{ "RPC_RequestOwn".GetStableHashCode(), "take" },
			{ "RPC_Shoot".GetStableHashCode(), "fire" },
			{ "RPC_SetLoadedVisual".GetStableHashCode(), "load" },
			{ "RPC_OnLegUse".GetStableHashCode(), "use the leg of" },
			{ "RPC_RequestOpen".GetStableHashCode(), "open the storage of" },
			{ "RPC_RequestStack".GetStableHashCode(), "put into the storage of" },
			{ "RPC_RequestTakeAll".GetStableHashCode(), "take everything from the storage of" },
		};
		private static readonly HashSet<int> s_shipControls = new HashSet<int> { "RequestControl".GetStableHashCode(), "Forward".GetStableHashCode(), "Backward".GetStableHashCode(), "Stop".GetStableHashCode(), "Rudder".GetStableHashCode() };
		// prefabs with a Tameable, with a TerrainComp, and the ships and carts (storage: its Container is part of the vehicle's own object)
		private static readonly HashSet<int> s_tameable = new HashSet<int>();
		private static readonly HashSet<int> s_terrainComps = new HashSet<int>();
		private static readonly HashSet<int> s_catapults = new HashSet<int>();
		private struct Vehicle { public bool ship, storage; }
		private static readonly Dictionary<int, Vehicle> s_vehicles = new Dictionary<int, Vehicle>();

		// Ward prefab hash -> radius, for the wards players own.
		private static Dictionary<int, float> s_wardRadius;
		// RPC method hash -> the prefabs (hash) a ward guards that RPC for.
		private static readonly Dictionary<int, HashSet<int>> s_gate = new Dictionary<int, HashSet<int>>();
		// A request for a chest, and what the chest answers when it says no.
		private static readonly Dictionary<int, string> s_answer = new Dictionary<int, string>();
		private static readonly Dictionary<int, string> s_names = new Dictionary<int, string>();
		// Tombstones (TombStone) and the container requests that open one or take from it.
		private static readonly HashSet<int> s_tombstones = new HashSet<int>();
		private static readonly HashSet<int> s_tombRequests = new HashSet<int> { "RPC_RequestOpen".GetStableHashCode(), "RPC_RequestStack".GetStableHashCode(), "RPC_RequestTakeAll".GetStableHashCode() };
		/*
			Who planted a crop. The game grows a crop as a new object where the sapling stood and destroys the sapling (Plant.Grow), and
			the crop has no builder: the planter (the sapling's creator) is written into the crop under this key. Whichever game grew it,
			the server sees the crop being made and the sapling taken away a moment apart, in either order; each waits a minute for the
			other. Crops that grew before this came in carry nobody.
		*/
		internal static readonly int s_planter = "SarkasticGG_planter".GetStableHashCode();
		// sapling prefab -> how far from it its crop may stand (a plant that grows against something: its attach distance)
		private static readonly Dictionary<int, float> s_saplings = new Dictionary<int, float>();
		private static readonly HashSet<int> s_crops = new HashSet<int>();
		private struct Pending { public ZDOID id; public Vector3 at; public long planter; public float reach; public float time; }
		private static readonly List<Pending> s_goneSaplings = new List<Pending>();
		private static readonly List<Pending> s_newCrops = new List<Pending>();
		private static int s_cropErrors;
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
			// The wards come from the world index (kept up to date as wards are made and taken away), no longer from a look through
			// the whole world every 5 s (35-45 ms on the live world).
			return WorldIndex.Ready();
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
							s_crops.Add(grown.name.GetStableHashCode());
							s_saplings[hash] = Mathf.Max(0.3f, plant.m_attachDistance + 0.5f);
						}
					}
				}
				if (prefab.GetComponent<TombStone>())
				{
					s_tombstones.Add(hash);
				}
				if (prefab.GetComponent<Tameable>())
				{
					s_tameable.Add(hash);
				}
				if (prefab.GetComponent<TerrainComp>())
				{
					s_terrainComps.Add(hash);
				}
				if (prefab.GetComponent<Catapult>())
				{
					s_catapults.Add(hash);
				}
				bool isShip = prefab.GetComponent<Ship>() != null;
				if (isShip || prefab.GetComponent<Vagon>())
				{
					ZNetView root = prefab.GetComponent<ZNetView>();
					bool storage = false;
					foreach (Container c in prefab.GetComponentsInChildren<Container>(true))
					{
						storage |= c.GetComponentInParent<ZNetView>(true) == root;
					}
					s_vehicles[hash] = new Vehicle { ship = isShip, storage = storage };
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
			WorldIndex.OnMade -= CropMade;
			WorldIndex.OnMade += CropMade;
			WorldIndex.OnGone -= SaplingGone;
			WorldIndex.OnGone += SaplingGone;
			s_wardRadius = wards;
			ServersidePlugin.logger.LogInfo($"Ward guard: structures {Configuration.wardGuardStructures.Value}, outside wards {Configuration.wardGuardOutsideWards.Value}, own pieces {Configuration.wardGuardOwnPieces.Value}, "
				+ $"tombstones {Configuration.wardGuardTombstones.Value} ({s_tombstones.Count} prefab(s)), crops of {s_saplings.Count} sapling(s) carry their planter, interactions {Configuration.wardGuardInteractions.Value}, "
				+ $"spoofing {Configuration.wardGuardSpoofing.Value}, destroy requests {Configuration.wardGuardDestroyRequests.Value}. "
				+ $"destroy log {Configuration.wardGuardDestroyLog.Value}, claims {Configuration.wardGuardClaims.Value}, indirect hits {Configuration.wardGuardIndirectHits.Value}, tamed animals {Configuration.wardGuardTamedAnimals.Value} ({s_tameable.Count} prefab(s)), "
				+ $"vehicles {Configuration.wardGuardVehicles.Value} ({string.Join(", ", s_vehicles.Select(kv => ZNetScene.instance.GetPrefab(kv.Key).name + (kv.Value.storage ? " +storage" : "")))}), terrain {Configuration.wardGuardTerrain.Value} ({s_terrainComps.Count} prefab(s)). "
				+ $"Wards: {string.Join(", ", wards.Select(kv => ZNetScene.instance.GetPrefab(kv.Key).name + " " + kv.Value.ToString("0.#") + " m"))}; "
				+ $"{s_gate.Count} kinds of request on {s_gate.Values.SelectMany(s => s).Distinct().Count()} prefab(s) ({guarded} component(s)) are held to the ward's list.");
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
			return Allowed(at, player, out denying, out _);
		}

		// covered: an enabled ward covers the place (allowed then means the player is on the list of one of them).
		private static bool Allowed(Vector3 at, long player, out ZDO denying, out bool covered)
		{
			denying = null;
			covered = false;
			foreach (ZDOID id in WorldIndex.Wards)
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
				covered = true;
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

		// On the list of one of the builder's wards, switched on or not: the people the builder lets into their base.
		private static bool FriendOf(long builder, long player)
		{
			foreach (ZDOID id in WorldIndex.Wards)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				if (ward != null && s_wardRadius.ContainsKey(ward.GetPrefab()) && ward.GetLong(ZDOVars.s_creator, 0L) == builder && OnList(ward, player))
				{
					return true;
				}
			}
			return false;
		}

		// Where OutsideWards rules: the builder and the people on the list of one of the builder's wards.
		private static bool BuildersSide(long builder, long player)
		{
			return player != 0L && (player == builder || FriendOf(builder, player));
		}

		// OutsideWards: is this build left to its builder? Yes where no enabled ward covers it, and where only wards cover it whose
		// list does not have the builder on it (someone else's ward beside it). covering: then the first such ward.
		private static bool BuildersOwn(Vector3 at, long builder, out ZDO covering)
		{
			bool mine = Allowed(at, builder, out ZDO notTheirs, out bool covered);
			covering = mine ? null : notTheirs;
			return !covered || !mine;
		}

		// The builder of a build: its creator, or for a crop the one who planted it.
		private static long Builder(ZDO zdo)
		{
			long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
			return creator != 0L ? creator : zdo.GetLong(s_planter, 0L);
		}

		// A request refused: a chest's question gets its no, the rest is dropped.
		private static bool Refuse(ZNetPeer peer, Header h)
		{
			if (s_answer.TryGetValue(h.method, out string answer))
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, h.zdo, answer, false);
			}
			return false;
		}

		private static void Prune(List<Pending> list, float now)
		{
			list.RemoveAll(p => now - p.time > 60f || now < p.time);
			if (list.Count > 2000)
			{
				list.RemoveRange(0, list.Count - 2000);
			}
		}

		// A crop made (or updated) without a planter: the sapling it grew from may be gone already, else it is waited for.
		private static void CropMade(ZDO zdo, int prefab)
		{
			try
			{
				if (!s_crops.Contains(prefab) || zdo.GetLong(s_planter, 0L) != 0L)
				{
					return;
				}
				float now = Time.time;
				Prune(s_goneSaplings, now);
				Vector3 at = zdo.GetPosition();
				int best = -1;
				float bestD = float.MaxValue;
				for (int i = 0; i < s_goneSaplings.Count; i++)
				{
					float d = (s_goneSaplings[i].at - at).sqrMagnitude;
					if (d <= s_goneSaplings[i].reach * s_goneSaplings[i].reach && d < bestD)
					{
						best = i;
						bestD = d;
					}
				}
				if (best >= 0)
				{
					zdo.Set(s_planter, s_goneSaplings[best].planter);
					s_goneSaplings.RemoveAt(best);
					return;
				}
				Prune(s_newCrops, now);
				if (!s_newCrops.Exists(p => p.id == zdo.m_uid))
				{
					s_newCrops.Add(new Pending { id = zdo.m_uid, at = at, time = now });
				}
			}
			catch (Exception e)
			{
				if (s_cropErrors++ < 5)
				{
					ServersidePlugin.logger.LogWarning($"Ward guard: crop planter: {e.Message}");
				}
			}
		}

		// A sapling taken away (inside the game's HandleDestroyedZDO: never throws): the crop it grew into, made a moment ago, gets its
		// planter; else the planter waits a minute for the crop.
		private static void SaplingGone(ZDO zdo)
		{
			try
			{
				if (!s_saplings.TryGetValue(zdo.GetPrefab(), out float reach))
				{
					return;
				}
				long planter = zdo.GetLong(ZDOVars.s_creator, 0L);
				if (planter == 0L)
				{
					return;
				}
				float now = Time.time;
				Prune(s_newCrops, now);
				Vector3 at = zdo.GetPosition();
				int best = -1;
				float bestD = float.MaxValue;
				for (int i = 0; i < s_newCrops.Count; i++)
				{
					float d = (s_newCrops[i].at - at).sqrMagnitude;
					if (d <= reach * reach && d < bestD)
					{
						best = i;
						bestD = d;
					}
				}
				if (best >= 0)
				{
					ZDO crop = ZDOMan.instance.GetZDO(s_newCrops[best].id);
					s_newCrops.RemoveAt(best);
					if (crop != null && crop.GetLong(s_planter, 0L) == 0L)
					{
						crop.Set(s_planter, planter);
						return;
					}
				}
				Prune(s_goneSaplings, now);
				s_goneSaplings.Add(new Pending { at = at, planter = planter, reach = reach, time = now });
			}
			catch (Exception e)
			{
				if (s_cropErrors++ < 5)
				{
					ServersidePlugin.logger.LogWarning($"Ward guard: crop planter: {e.Message}");
				}
			}
		}

		// A player's name for the guard log: from their connection while they are online, else from a ward they built.
		private static string PlayerName(long player)
		{
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (PlayerId(peer) == player)
				{
					return Clean(peer.m_playerName);
				}
			}
			foreach (ZDOID id in WorldIndex.Wards)
			{
				ZDO ward = ZDOMan.instance.GetZDO(id);
				string name = ward != null && ward.GetLong(ZDOVars.s_creator, 0L) == player ? ward.GetString(ZDOVars.s_creatorName, "") : "";
				if (name.Length > 0)
				{
					return Clean(name);
				}
			}
			return "?";
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
			if (s_tamedNames.ContainsKey(h.method) && Configuration.wardGuardTamedAnimals.Value != Mode.Off && s_tameable.Contains(prefab) && target.GetBool(ZDOVars.s_tamed))
			{
				return TamedDecision(peer, h, pkg, target);
			}
			if (s_vehicleNames.ContainsKey(h.method) && Configuration.wardGuardVehicles.Value != Mode.Off && s_vehicles.TryGetValue(prefab, out Vehicle vehicle)
				&& ((vehicle.ship && s_shipControls.Contains(h.method)) || (!vehicle.ship && h.method == s_requestOwn) || (vehicle.storage && s_tombRequests.Contains(h.method))))
			{
				return VehicleDecision(peer, h, target);
			}
			if (s_catapultCalls.Contains(h.method) && s_catapults.Contains(prefab) && !CatapultAllowed(peer, h, target))
			{
				return false;
			}
			if (h.method == s_applyOperation && Configuration.wardGuardTerrain.Value != Mode.Off && s_terrainComps.Contains(prefab))
			{
				return TerrainDecision(peer, pkg);
			}
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
				Mode outside = Configuration.wardGuardOutsideWards.Value;
				long builder = target.GetLong(ZDOVars.s_creator, 0L);
				if ((structures == Mode.Off && outside == Mode.Off) || (builder == 0L && !s_wardRadius.ContainsKey(prefab)) || IsAdmin(peer))
				{
					return true;
				}
				long player = PlayerId(peer);
				if (player != 0L && player == builder && Configuration.wardGuardOwnPieces.Value)
				{
					return true;
				}
				Vector3 at = target.GetPosition();
				// the hit, read once (the package is read through): is it a player's own, and what to call it
				bool hitRead = false;
				HitInfo hit = default(HitInfo);
				// OutsideWards: a build no ward of its builder's people covers is the builder's and their friends' (a ward of others beside it gives nothing).
				if (outside != Mode.Off && builder != 0L && h.method != s_repair && !BuildersSide(builder, player) && BuildersOwn(at, builder, out ZDO beside))
				{
					string verb = h.method == s_damage ? "hit" : "take down";
					if (h.method == s_damage)
					{
						hit = ReadHit(peer, pkg);
						hitRead = true;
						if (hit.indirect)
						{
							// not a player's own hit: let through as before, unless IndirectHits says it is the sender's doing
							Mode indirect = Configuration.wardGuardIndirectHits.Value;
							if (indirect == Mode.Off)
							{
								return true;
							}
							verb = "hit (" + hit.text + ")";
							if (indirect == Mode.LogOnly)
							{
								Report(peer, target, null, "indirect", verb, false, builder, beside, BuilderText(builder, beside));
								return true;
							}
						}
					}
					Report(peer, target, null, "build", verb, outside == Mode.On, builder, beside);
					if (outside == Mode.On)
					{
						return false;
					}
				}
				if (structures == Mode.Off || Allowed(at, player, out ZDO denying))
				{
					return true;
				}
				string what = h.method == s_damage ? "hit" : h.method == s_remove ? "take down" : "repair";
				if (h.method == s_damage)
				{
					if (!hitRead)
					{
						hit = ReadHit(peer, pkg);
					}
					if (hit.indirect)
					{
						Mode indirect = Configuration.wardGuardIndirectHits.Value;
						if (indirect == Mode.Off)
						{
							return true;
						}
						what = "hit (" + hit.text + ")";
						if (indirect == Mode.LogOnly)
						{
							Report(peer, target, denying, "indirect", what, false);
							return true;
						}
					}
				}
				Report(peer, target, denying, "structure", what, structures == Mode.On);
				return structures != Mode.On;
			}
			if (s_tombstones.Contains(prefab) && s_tombRequests.Contains(h.method))
			{
				// Tombstones: the player who died and their friends, anywhere; no ward plays a part.
				Mode tombs = Configuration.wardGuardTombstones.Value;
				long dead = target.GetLong(ZDOVars.s_owner, 0L);
				if (tombs == Mode.Off || dead == 0L || IsAdmin(peer) || BuildersSide(dead, PlayerId(peer)))
				{
					return true;
				}
				Report(peer, target, null, "tombstone", s_names.TryGetValue(h.method, out string verb) ? verb : "open", tombs == Mode.On, dead);
				return tombs == Mode.On ? Refuse(peer, h) : true;
			}
			if (s_gate.TryGetValue(h.method, out HashSet<int> prefabs) && prefabs.Contains(prefab))
			{
				Mode interactions = Configuration.wardGuardInteractions.Value;
				Mode outside = Configuration.wardGuardOutsideWards.Value;
				if ((interactions == Mode.Off && outside == Mode.Off) || IsAdmin(peer))
				{
					return true;
				}
				long player = PlayerId(peer);
				long builder = Builder(target);
				if (player != 0L && player == builder && Configuration.wardGuardOwnPieces.Value)
				{
					return true;
				}
				Vector3 at = target.GetPosition();
				if (outside != Mode.Off && builder != 0L && !BuildersSide(builder, player) && BuildersOwn(at, builder, out ZDO beside))
				{
					Report(peer, target, null, "use", s_names[h.method], outside == Mode.On, builder, beside);
					if (outside == Mode.On)
					{
						return Refuse(peer, h);
					}
				}
				if (interactions == Mode.Off || Allowed(at, player, out ZDO denying))
				{
					return true;
				}
				Report(peer, target, denying, "interaction", s_names[h.method], interactions == Mode.On);
				return interactions == Mode.On ? Refuse(peer, h) : true;
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
			foreach (ZDOID id in WorldIndex.Wards)
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
			FlushDestroyed();
			EnforceWardLimit();
		}

		private static void EnforceWardLimit()
		{
			int limit = Configuration.wardGuardMaxActiveWards.Value;
			Dictionary<long, List<ZDO>> on = new Dictionary<long, List<ZDO>>();
			foreach (ZDOID id in WorldIndex.Wards)
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

		// What a hit is: indirect = not a player's own: it has no attacker (fire, a falling object, a catapult, a cart), or a creature
		// made it in the game of the player who sent it (a raid, a monster they lured). A player's own hit, or one whose attacker is not
		// known, is not. text says what it is, for the log ("Catapult, no attacker").
		private struct HitInfo
		{
			public bool indirect;
			public string text;
		}

		// Reads the hit out of the packet (pkg just past the header: it moves it on, so read once).
		private static HitInfo ReadHit(ZNetPeer peer, ZPackage pkg)
		{
			HitInfo info = new HitInfo { text = "?" };
			try
			{
				ZPackage parameters = pkg.ReadPackage();
				HitData hit = new HitData();
				hit.Deserialize(ref parameters);
				if (hit.m_attacker.IsNone())
				{
					info.indirect = true;
					info.text = hit.m_hitType + ", no attacker";
					return info;
				}
				ZDO attacker = ZDOMan.instance.GetZDO(hit.m_attacker);
				info.indirect = attacker != null && attacker.GetPrefab() != s_player && attacker.GetOwner() == peer.m_uid;
				info.text = hit.m_hitType + ", " + (attacker == null ? "an attacker that is not known" : attacker.GetPrefab() == s_player ? "a player" : NameOf(attacker) + (info.indirect ? " run by the sender's own game" : " run by another game"));
				return info;
			}
			catch (Exception e)
			{
				Fault(e);
				return default(HitInfo);
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
			bool log = Configuration.wardGuardDestroyLog.Value;
			if (mode == Mode.Off && !log)
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
			long sender = log ? PlayerId(peer) : 0L;
			for (int i = 0; i < count; i++)
			{
				ZDOID id = parameters.ReadZDOID();
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				long owner = zdo != null ? zdo.GetOwner() : 0L;
				if (log && zdo != null)
				{
					// DestroyLog: somebody's build, asked for by somebody else's game
					long builder = zdo.GetLong(ZDOVars.s_creator, 0L);
					if (builder != 0L && builder != sender)
					{
						bool theirs = owner == peer.m_uid;
						float health = zdo.GetFloat(ZDOVars.s_health, -1f);
						NoteDestroyed(builder, NameOf(zdo), zdo.GetPosition(), "request/" + peer.m_uid + (theirs ? "/own" : "/other"),
							$"{Clean(peer.m_playerName)} ({sender}) ({(theirs ? "whose game owns it" : owner == ZDOMan.GetSessionID() ? "which the server owns" : "which another game owns")}) asked for {{it}} to be destroyed",
							health >= 0f ? health.ToString("0.#") : null);
					}
				}
				if (zdo != null && owner != 0L && owner != peer.m_uid)
				{
					foreign.Add(zdo);
				}
				else
				{
					keep.Add(id);
				}
			}
			if (mode == Mode.Off)
			{
				return true;
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

		// Where a build stands, for the log: whose, and the ward of others beside it that gives them nothing.
		private static string BuilderText(long builder, ZDO beside)
		{
			return $"; built by {PlayerName(builder)} ({builder}), " + (beside != null ? $"in the ward of {Clean(beside.GetString(ZDOVars.s_creatorName, "?"))} at {Where(beside)}, whose list does not have them" : "no ward there");
		}

		// An answer to a request that is refused: sent to the player who asked, as the object would send it.
		private static void Answer(ZNetPeer peer, ZDOID zdo, string method, params object[] parameters)
		{
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, zdo, method, parameters);
		}

		/*
			TamedAnimals: a tamed creature (a Tameable whose ZDO says tamed) standing in a switched-on ward is held to the ward's list. Its
			RPCs are the ones the game's client sends for hitting it, ordering it, naming it, saddling it and riding it.
		*/
		private static bool TamedDecision(ZNetPeer peer, Header h, ZPackage pkg, ZDO target)
		{
			Mode mode = Configuration.wardGuardTamedAnimals.Value;
			if (IsAdmin(peer))
			{
				return true;
			}
			if (Allowed(target.GetPosition(), PlayerId(peer), out ZDO ward))
			{
				return true;
			}
			string what = s_tamedNames[h.method];
			if (h.method == s_damage)
			{
				HitInfo hit = ReadHit(peer, pkg);
				if (hit.indirect)
				{
					// not a player's own hit: as under Structures
					Mode indirect = Configuration.wardGuardIndirectHits.Value;
					if (indirect == Mode.Off)
					{
						return true;
					}
					what = "hit (" + hit.text + ")";
					if (indirect == Mode.LogOnly)
					{
						Report(peer, target, ward, "indirect", what, false);
						return true;
					}
				}
			}
			Report(peer, target, ward, "tamed", what, mode == Mode.On);
			if (mode != Mode.On)
			{
				return true;
			}
			if (h.method == s_requestControl)
			{
				Answer(peer, h.zdo, "RequestRespons", false);
			}
			return false;
		}

		/*
			Vehicles: a ship or a cart somebody built is used by its builder, the players on the list of one of the builder's wards, and
			admins. The ship's helm and sails (ShipControlls and Ship RPCs), the cart's RPC_RequestOwn, and the storage if it is on the
			vehicle's own object (Build() looks).
		*/
		private static bool MayUseVehicle(ZNetPeer peer, ZDO vehicle, out long builder)
		{
			builder = Builder(vehicle);
			return builder == 0L || IsAdmin(peer) || BuildersSide(builder, PlayerId(peer));
		}

		/*
			A catapult is a cart (Vagon) with a Catapult on the same object. The player's game loads it (RPC_SetLoadedVisual), asks to own
			it (RPC_RequestOwn) and fires it a second later (RPC_Shoot, to everybody); only the owner's game shoots. When the request was
			refused or slow, the server owns it when RPC_Shoot arrives and shoots itself: its hits are the server's own and no routed RPC
			passes here. So a shot sent to a catapult the server owns is dropped, whatever the player is (admins excepted), as long as
			Vehicles or IndirectHits is not Off (the stricter of the two decides: LogOnly only logs). Structures and OutsideWards alone do
			not do it: a server that switched on only those keeps the game's catapult as it was.
			Under Vehicles a player who may not use the catapult has loading, shooting and the leg dropped as well.
		*/
		private static bool CatapultAllowed(ZNetPeer peer, Header h, ZDO target)
		{
			if (IsAdmin(peer))
			{
				return true;
			}
			Mode vehicles = Configuration.wardGuardVehicles.Value;
			long builder = 0L;
			bool may = vehicles == Mode.Off || MayUseVehicle(peer, target, out builder);
			if (!may && vehicles == Mode.On)
			{
				Report(peer, target, null, "vehicle", s_vehicleNames[h.method], true, builder);
				return false;
			}
			if (h.method == s_shoot && target.GetOwner() == ZDOMan.GetSessionID())
			{
				Mode strictest = (Mode)Math.Max((int)vehicles, (int)Configuration.wardGuardIndirectHits.Value);
				if (strictest != Mode.Off)
				{
					Report(peer, target, null, "vehicle", "fire (the server owns it, so the shot would be the server's own)", strictest == Mode.On, builder != 0L ? builder : Builder(target));
					return strictest != Mode.On;
				}
			}
			if (!may)
			{
				Report(peer, target, null, "vehicle", s_vehicleNames[h.method], false, builder);
			}
			return true;
		}

		private static bool VehicleDecision(ZNetPeer peer, Header h, ZDO target)
		{
			Mode mode = Configuration.wardGuardVehicles.Value;
			if (MayUseVehicle(peer, target, out long builder))
			{
				return true;
			}
			Report(peer, target, null, "vehicle", s_vehicleNames[h.method], mode == Mode.On, builder);
			if (mode != Mode.On)
			{
				return true;
			}
			if (h.method == s_requestControl)
			{
				Answer(peer, h.zdo, "RequestRespons", false);
				return false;
			}
			if (h.method == s_requestOwn)
			{
				Answer(peer, h.zdo, "RPC_RequestDenied");
				return false;
			}
			return Refuse(peer, h);
		}

		/*
			The mod gives a ship to the game of whoever is at its helm (Ship_UpdateOwner in Core), and the game itself hands objects near
			a player to that player's game (ZDOMan.ReleaseNearbyZDOS). A ship or a cart is not handed to the game of a player who may
			not use it: it stays with the server.
		*/
		[HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
		public static class ZDO_SetOwner_Vehicles_Patch
		{
			static bool Prefix(ZDO __instance, long uid)
			{
				if (uid == 0L || s_vehicles.Count == 0 || Configuration.wardGuardVehicles.Value == Mode.Off || !Configuration.wardGuardEnabled.Value
					|| !ZNet.instance || !ZNet.instance.IsServer() || uid == ZDOMan.GetSessionID() || !s_vehicles.ContainsKey(__instance.GetPrefab()))
				{
					return true;
				}
				try
				{
					ZNetPeer peer = ZNet.instance.GetPeer(uid);
					if (peer == null || !Ready() || MayUseVehicle(peer, __instance, out long builder))
					{
						return true;
					}
					Mode mode = Configuration.wardGuardVehicles.Value;
					Report(peer, __instance, null, "vehicle", "be given", mode == Mode.On, builder, null, null, false);
					if (mode != Mode.On)
					{
						return true;
					}
					// the server keeps it (nobody owning a ship would leave it unsimulated)
					if (__instance.GetOwner() != ZDOMan.GetSessionID())
					{
						__instance.SetOwner(ZDOMan.GetSessionID());
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

		/*
			Terrain: the ground is changed by RPC_ApplyOperation on the terrain's own object: a position, a rotation if the first bool is
			set, and the terrain tool (a hash that names a TerrainOp prefab). The copy is read here; the game reads the packet again.
		*/
		private static readonly List<ZDO> s_nearby = new List<ZDO>();

		// Every object of the sectors that a circle touches; the caller measures the distance.
		private static void Nearby(Vector3 centre, float radius, List<ZDO> into)
		{
			into.Clear();
			Vector2s low = ZoneSystem.GetZone(new Vector3(centre.x - radius, 0f, centre.z - radius));
			Vector2s high = ZoneSystem.GetZone(new Vector3(centre.x + radius, 0f, centre.z + radius));
			SimulationDistance none = new SimulationDistance(0, 0);
			for (int x = low.x; x <= high.x; x++)
			{
				for (int y = low.y; y <= high.y; y++)
				{
					ZDOMan.instance.FindSectorObjects(new Vector2s(x, y), none, into);
				}
			}
		}

		private static bool TerrainDecision(ZNetPeer peer, ZPackage pkg)
		{
			Mode mode = Configuration.wardGuardTerrain.Value;
			if ((Configuration.wardGuardStructures.Value == Mode.Off && Configuration.wardGuardOutsideWards.Value == Mode.Off) || IsAdmin(peer))
			{
				return true;
			}
			ZPackage op = pkg.ReadPackage().ReadPackage();
			Vector3 pos = op.ReadVector3();
			if (op.ReadBool())
			{
				op.ReadVector3();
			}
			TerrainOp.Settings settings = TerrainOp.Settings.Deserialize(op);
			if (settings == null)
			{
				return true;
			}
			if (TerrainRefused(peer, pos, settings.GetRadius(), out ZDO ward, out string why, out string place))
			{
				Report(peer, null, ward, "terrain", $"tried to change the ground at {place}, {why}", mode == Mode.On);
				return mode != Mode.On;
			}
			return true;
		}

		// The two tests of Terrain for an operation at `pos` that reaches `r` metres: true = refused; `why` is the end of the log line (what the operation
		// touches), `ward` the ward that is the reason in (a), `place` the position and radius as the lines write them.
		// (a) a switched-on ward whose list does not have the player, within its radius plus the operation's;
		// (b) a build of someone whose side the player is not on, near enough to be undermined.
		private static bool TerrainRefused(ZNetPeer peer, Vector3 pos, float r, out ZDO ward, out string why, out string place)
		{
			ward = null;
			why = null;
			Mode structures = Configuration.wardGuardStructures.Value;
			Mode outside = Configuration.wardGuardOutsideWards.Value;
			long player = PlayerId(peer);
			place = $"{pos.x:0},{pos.z:0} (radius {r:0.#} m)";
			if (structures != Mode.Off)
			{
				foreach (ZDOID id in WorldIndex.Wards)
				{
					ZDO candidate = ZDOMan.instance.GetZDO(id);
					if (candidate == null || !s_wardRadius.TryGetValue(candidate.GetPrefab(), out float radius) || !candidate.GetBool(ZDOVars.s_enabled))
					{
						continue;
					}
					Vector3 p = candidate.GetPosition();
					float reach = radius + r;
					float dx = p.x - pos.x;
					float dz = p.z - pos.z;
					if (dx * dx + dz * dz >= reach * reach || (player != 0L && OnList(candidate, player)))
					{
						continue;
					}
					ward = candidate;
					why = $"within reach of the ward of {Clean(candidate.GetString(ZDOVars.s_creatorName, "?"))} at {Where(candidate)} whose list does not have them";
					return true;
				}
			}
			if (outside != Mode.Off)
			{
				float reach = r + Configuration.wardGuardTerrainMargin.Value;
				Nearby(pos, reach, s_nearby);
				ZDO found = null;
				foreach (ZDO zdo in s_nearby)
				{
					long builder = zdo.GetLong(ZDOVars.s_creator, 0L);
					if (builder == 0L)
					{
						continue;
					}
					Vector3 p = zdo.GetPosition();
					float dx = p.x - pos.x;
					float dz = p.z - pos.z;
					if (dx * dx + dz * dz >= reach * reach || BuildersSide(builder, player) || !BuildersOwn(p, builder, out _))
					{
						continue;
					}
					found = zdo;
					break;
				}
				s_nearby.Clear();
				if (found != null)
				{
					why = $"within {reach:0.#} m of {NameOf(found)} at {Where(found)} built by {PlayerName(Builder(found))} ({Builder(found)})";
					return true;
				}
			}
			return false;
		}

		/*
			The server keeps the terrain compilers. A compiler's operations reach the server's check only when the server owns it, and a player's game
			owns one in exactly one case: it created it (Heightmap.GetAndCreateTerrainCompiler, in a zone that had none loaded). Then every operation of
			that game in that zone runs in the game itself and the server only gets the data (s_TCData) in RPC_ZDOData. So, for a packet from a player's
			game (Terrain on, not an admin):
			- a ZDO the packet creates (CreateNewZDO is noted while the packet is read) that turns out, after Deserialize, to be a terrain compiler owned by
			  the sender is judged in the finalizer: a second compiler in a zone that already has one (the game keeps the one with the most operations, so a
			  forged one with a high count replaces a base's ground in every game) and one whose last operation (point, radius, in the header of s_TCData) is
			  one that Terrain would refuse are destroyed (On); whatever is kept is taken over by the server at once, so the next operation is routed
			  to the server and goes through TerrainDecision;
			- a game that takes an existing compiler over (it says it owns it now) is refused like a Claims claim, whatever Claims is set to.
			Everything of this is in Report with the kind "terrain". LogOnly logs and lets it be (the take-over line at most once a minute per player).
		*/
		private static ZNetPeer s_terrainFrom;
		private static readonly List<ZDO> s_made = new List<ZDO>();

		[HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
		public static class ZDOMan_CreateNewZDO_Terrain_Patch
		{
			static void Postfix(ZDO __result)
			{
				if (s_terrainFrom != null && __result != null)
				{
					s_made.Add(__result);
				}
			}
		}

		// Called from the finalizer of RPC_ZDOData; `from` is the sender's peer.
		private static void TerrainMade(ZNetPeer from)
		{
			Mode mode = Configuration.wardGuardTerrain.Value;
			if (mode == Mode.Off || !Ready())
			{
				return;
			}
			long server = ZDOMan.GetSessionID();
			List<ZDO> made = new List<ZDO>(s_made);
			s_made.Clear();
			foreach (ZDO zdo in made)
			{
				// the ZDO of the pool may have been reused or the object gone: only one still in the world, a compiler. Whoever the packet names as its
				// owner: a game names itself for what it made, a forged packet that names the server or someone else is judged the same way.
				if (zdo == null || ZDOMan.instance.GetZDO(zdo.m_uid) != zdo || !s_terrainComps.Contains(zdo.GetPrefab()))
				{
					continue;
				}
				Vector3 at = zdo.GetPosition();
				string reason = null;
				ZDO ward = null;
				// (a) a second compiler in the zone
				Vector2s zone = ZoneSystem.GetZone(at);
				List<ZDO> inZone = new List<ZDO>();
				Nearby(at, 0f, inZone);
				if (inZone.Any(o => o != zdo && o.m_uid != zdo.m_uid && s_terrainComps.Contains(o.GetPrefab()) && ZoneSystem.GetZone(o.GetPosition()) == zone))
				{
					reason = $"made a second terrain compiler in a zone that has one, at {at.x:0},{at.z:0}";
				}
				else
				{
					// (b) the last operation it holds
					byte[] data = zdo.GetByteArray(ZDOVars.s_TCData);
					if (data != null && data.Length > 0)
					{
						try
						{
							ZPackage header = new ZPackage(Utils.Decompress(data));
							header.ReadInt();
							int operations = header.ReadInt();
							Vector3 point = header.ReadVector3();
							float radius = header.ReadSingle();
							if (TerrainRefused(from, point, radius, out ward, out string why, out string place))
							{
								reason = $"made a new terrain compiler ({operations} operation(s)) with an operation at {place}, {why}";
							}
							else
							{
								// The last operation is only what the header says: the heights themselves are judged too, in cells of 2 m (a forged compiler
								// can lower a base's ground and name a last operation far from it). Vertex i is (i % pitch, i / pitch) of the zone's heightmap,
								// centred on the zone (Heightmap.WorldToVertex). Paint is only colour and is not judged.
								int count = header.ReadInt();
								int pitch = Mathf.RoundToInt(Mathf.Sqrt(count));
								if (count <= 0 || pitch * pitch != count)
								{
									throw new FormatException($"{count} heights");
								}
								int width = pitch - 1;
								float scale = ZoneSystem.c_ZoneSize / width;
								Vector3 centre = ZoneSystem.GetZonePos(zone);
								HashSet<int> cells = new HashSet<int>();
								for (int i = 0; i < count; i++)
								{
									if (!header.ReadBool())
									{
										continue;
									}
									float level = header.ReadSingle();
									float smooth = header.ReadSingle();
									if (Mathf.Abs(level) + Mathf.Abs(smooth) > 0.01f)
									{
										int cx = Mathf.FloorToInt((i % pitch) * scale / 2f);
										int cz = Mathf.FloorToInt((i / pitch) * scale / 2f);
										cells.Add(cx * 1000 + cz);
									}
								}
								foreach (int cell in cells)
								{
									Vector3 c = new Vector3(centre.x - width * scale / 2f + (cell / 1000) * 2f + 1f, 0f, centre.z - width * scale / 2f + (cell % 1000) * 2f + 1f);
									if (TerrainRefused(from, c, 1.5f, out ward, out why, out place))
									{
										reason = $"made a new terrain compiler ({operations} operation(s), the last at {point.x:0},{point.z:0}) whose ground is changed at {place}, {why}";
										break;
									}
								}
							}
						}
						catch (Exception e)
						{
							reason = $"made a new terrain compiler at {at.x:0},{at.z:0} whose data cannot be read ({e.GetType().Name})";
						}
					}
				}
				if (reason != null)
				{
					Report(from, null, ward, "terrain", reason, mode == Mode.On, subject: "new/" + zdo.m_uid);
					if (mode == Mode.On)
					{
						zdo.SetOwner(server);
						ZDOMan.instance.DestroyZDO(zdo);
						continue;
					}
				}
				// kept: the server owns it from now on, the player's next operation comes to the server
				zdo.SetOwner(server);
			}
		}

		/*
			Claims. RPC_ZDOData takes an object over for the sender's game when the packet names the sender as the owner: the owner-only
			bump (same data revision) and the data path (then Deserialize). A claim is when the object is not new (it has a creator), has an
			owner who is not the sender, the sender is not handing it on, and the sender may not act on it. On: the owner stays, the data
			is not read (Deserialize skips it), and when RPC_ZDOData is done the server raises both revisions so every game, the sender's
			too, gets the server's copy again.
		*/
		private static ZNetPeer s_claimsFrom;
		private static readonly Dictionary<ZDOID, Vector3> s_claimed = new Dictionary<ZDOID, Vector3>();

		// May this player act on this build by the rules of the ward guard (the test RPC_Remove gets), and if not, which ward is the reason.
		private static bool MayActOn(ZNetPeer peer, ZDO zdo, long builder, out ZDO denying, out ZDO beside)
		{
			denying = null;
			beside = null;
			if (IsAdmin(peer))
			{
				return true;
			}
			long player = PlayerId(peer);
			if (player != 0L && player == builder && Configuration.wardGuardOwnPieces.Value)
			{
				return true;
			}
			Vector3 at = zdo.GetPosition();
			if (Configuration.wardGuardOutsideWards.Value != Mode.Off && !BuildersSide(builder, player) && BuildersOwn(at, builder, out beside))
			{
				return false;
			}
			if (Configuration.wardGuardStructures.Value != Mode.Off && !Allowed(at, player, out denying))
			{
				return false;
			}
			if (Configuration.wardGuardVehicles.Value != Mode.Off && s_vehicles.ContainsKey(zdo.GetPrefab()) && !BuildersSide(builder, player))
			{
				return false;
			}
			return true;
		}

		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Claims_Patch
		{
			static void Prefix(ZDOMan __instance, ZRpc rpc)
			{
				bool server = Configuration.wardGuardEnabled.Value && ZNet.instance && ZNet.instance.IsServer();
				ZNetPeer from = server && (Configuration.wardGuardClaims.Value != Mode.Off || Configuration.wardGuardTerrain.Value != Mode.Off) ? __instance.FindPeer(rpc)?.m_peer : null;
				s_claimsFrom = Configuration.wardGuardClaims.Value != Mode.Off ? from : null;
				s_terrainFrom = Configuration.wardGuardTerrain.Value != Mode.Off && from != null && !IsAdmin(from) ? from : null;
				s_made.Clear();
			}

			static void Finalizer()
			{
				ZNetPeer terrain = s_terrainFrom;
				s_claimsFrom = null;
				s_terrainFrom = null;
				if (terrain != null && s_made.Count > 0)
				{
					try
					{
						TerrainMade(terrain);
					}
					catch (Exception e)
					{
						Fault(e);
					}
				}
				s_made.Clear();
				if (s_claimed.Count == 0)
				{
					return;
				}
				try
				{
					foreach (KeyValuePair<ZDOID, Vector3> kv in s_claimed)
					{
						ZDO zdo = ZDOMan.instance.GetZDO(kv.Key);
						if (zdo == null)
						{
							continue;
						}
						// RPC_ZDOData has written the sender's revisions (and its position): above both, with the position back, every game gets the server's copy.
						zdo.OwnerRevision++;
						zdo.InternalSetPosition(kv.Value);
						zdo.IncreaseDataRevision();
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
				s_claimed.Clear();
			}
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwnerInternal))]
		public static class ZDO_SetOwnerInternal_Claims_Patch
		{
			static bool Prefix(ZDO __instance, long uid)
			{
				ZNetPeer terrain = s_terrainFrom;
				if (terrain != null && uid == terrain.m_uid && __instance.GetOwner() != uid && !s_made.Contains(__instance))
				{
					try
					{
						if (!TerrainTakeOver(terrain, __instance))
						{
							return false;
						}
					}
					catch (Exception e)
					{
						Fault(e);
					}
				}
				ZNetPeer from = s_claimsFrom;
				if (from == null)
				{
					return true;
				}
				try
				{
					long old = __instance.GetOwner();
					if (old == 0L || old == from.m_uid || old == uid)
					{
						return true;
					}
					long builder = __instance.GetLong(ZDOVars.s_creator, 0L);
					if (builder == 0L || !Ready() || MayActOn(from, __instance, builder, out ZDO denying, out ZDO beside))
					{
						return true;
					}
					Mode mode = Configuration.wardGuardClaims.Value;
					Report(from, __instance, denying, "claim", "take over", mode == Mode.On, builder, beside, denying == null && beside != null ? BuilderText(builder, beside) : null);
					if (mode != Mode.On)
					{
						return true;
					}
					s_claimed[__instance.m_uid] = __instance.GetPosition();
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		// A player's game says it owns a terrain compiler that exists already: false = refused (On), the way a refused claim is.
		private static bool TerrainTakeOver(ZNetPeer from, ZDO zdo)
		{
			if (!s_terrainComps.Contains(zdo.GetPrefab()) || !Ready())
			{
				return true;
			}
			Mode mode = Configuration.wardGuardTerrain.Value;
			Vector3 at = zdo.GetPosition();
			Report(from, null, null, "terrain", $"tried to take over the terrain compiler at {at.x:0},{at.z:0}", mode == Mode.On, subject: "takeover");
			if (mode != Mode.On)
			{
				return true;
			}
			s_claimed[zdo.m_uid] = at;
			return false;
		}

		// The data that came with a refused claim is not read. Terrain: a player's game that sends the data of an existing terrain compiler it does
		// not own (the packet names the server or someone else as the owner, so it is no take-over) writes ground it has no right to: no game does
		// that, a compiler's data is written by its owner only (ApplyOperation is routed to the owner). On: refused like a claim; LogOnly: logged.
		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Claims_Patch
		{
			static bool Prefix(ZDO __instance)
			{
				if (s_claimed.Count > 0 && s_claimed.ContainsKey(__instance.m_uid))
				{
					return false;
				}
				ZNetPeer terrain = s_terrainFrom;
				if (terrain == null || __instance.GetOwner() == terrain.m_uid || s_made.Contains(__instance))
				{
					return true;
				}
				try
				{
					if (!s_terrainComps.Contains(__instance.GetPrefab()) || !Ready())
					{
						return true;
					}
					Mode mode = Configuration.wardGuardTerrain.Value;
					Vector3 at = __instance.GetPosition();
					Report(terrain, null, null, "terrain", $"sent the ground data of the terrain compiler at {at.x:0},{at.z:0}, which it does not own", mode == Mode.On, subject: "write");
					if (mode != Mode.On)
					{
						return true;
					}
					s_claimed[__instance.m_uid] = at;
					return false;
				}
				catch (Exception e)
				{
					Fault(e);
					return true;
				}
			}
		}

		/*
			DestroyLog. A piece with a creator destroyed by someone but its builder: WearNTear.Destroy is where a hit that takes the last
			health, the hammer's take-down (RPC_Remove) and a collapse all end. Who did it is the hit's attacker (a player, or a creature
			and the player it is after) or, where the hit names none, the game that sent the RPC; a collapse has no one. The log has one
			line a minute per destroyer and builder.
		*/
		private static bool s_inDamage, s_inRemove;
		private static long s_damageSender, s_removeSender;

		private class Destroyed
		{
			public string how, builderName, at, health, first;
			public long builder;
			public float lastWrite = -1000f;
			public int count;
			public readonly Dictionary<string, int> prefabs = new Dictionary<string, int>();
		}

		private static readonly Dictionary<string, Destroyed> s_destroyed = new Dictionary<string, Destroyed>();

		private static void NoteDestroyed(long builder, string prefab, Vector3 at, string key, string how, string health)
		{
			string full = key + "/" + builder;
			if (s_destroyed.Count > 2000)
			{
				s_destroyed.Clear();
			}
			if (!s_destroyed.TryGetValue(full, out Destroyed d))
			{
				s_destroyed[full] = d = new Destroyed { builder = builder, how = how };
			}
			if (d.count == 0)
			{
				d.at = $"{at.x:0},{at.z:0}";
				d.health = health;
				d.first = prefab;
				d.builderName = PlayerName(builder);
			}
			d.how = how;
			d.count++;
			d.prefabs[prefab] = d.prefabs.TryGetValue(prefab, out int n) ? n + 1 : 1;
			if (Time.time - d.lastWrite >= 60f)
			{
				WriteDestroyed(d);
			}
		}

		private static void WriteDestroyed(Destroyed d)
		{
			string pieces = d.count == 1 ? d.first : $"{d.count} pieces ({string.Join(", ", d.prefabs.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " x" + kv.Value))})";
			string line = $"{d.builderName} ({d.builder})'s {pieces} at {d.at}{(d.count > 1 ? " (the first)" : "")}{(d.health != null && d.count == 1 ? $" (health {d.health})" : "")}: {d.how.Replace("{it}", d.count == 1 ? "it" : "them")}";
			ServersidePlugin.logger.LogInfo("Ward guard: destroyed: " + line);
			GuardLog.Write("ward guard: destroyed: " + line);
			d.count = 0;
			d.prefabs.Clear();
			d.lastWrite = Time.time;
		}

		// What was not written yet is written a minute after the last line.
		private static void FlushDestroyed()
		{
			if (s_destroyed.Count == 0)
			{
				return;
			}
			foreach (string key in s_destroyed.Keys.ToList())
			{
				Destroyed d = s_destroyed[key];
				if (d.count > 0 && Time.time - d.lastWrite >= 60f)
				{
					WriteDestroyed(d);
				}
				else if (d.count == 0 && Time.time - d.lastWrite > 300f)
				{
					s_destroyed.Remove(key);
				}
			}
		}

		private static ZNetPeer PeerOfCharacter(ZDOID character)
		{
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer.m_characterID == character)
				{
					return peer;
				}
			}
			return null;
		}

		private static string Label(ZNetPeer peer)
		{
			return $"{Clean(peer.m_playerName)} ({PlayerId(peer)})";
		}

		[HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
		public static class WearNTear_RPC_Damage_Patch
		{
			static void Prefix(long sender)
			{
				s_inDamage = true;
				s_damageSender = sender;
			}

			static void Finalizer()
			{
				s_inDamage = false;
				s_damageSender = 0L;
			}
		}

		[HarmonyPatch(typeof(WearNTear), "RPC_Remove")]
		public static class WearNTear_RPC_Remove_Patch
		{
			static void Prefix(long sender)
			{
				s_inRemove = true;
				s_removeSender = sender;
			}

			static void Finalizer()
			{
				s_inRemove = false;
				s_removeSender = 0L;
			}
		}

		[HarmonyPatch(typeof(WearNTear), "Destroy", new[] { typeof(HitData), typeof(bool) })]
		public static class WearNTear_Destroy_Patch
		{
			static void Prefix(WearNTear __instance, HitData hitData)
			{
				if (!Configuration.wardGuardDestroyLog.Value || !Configuration.wardGuardEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null)
				{
					return;
				}
				try
				{
					ZNetView view = __instance.m_nview;
					ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
					long builder = zdo != null ? zdo.GetLong(ZDOVars.s_creator, 0L) : 0L;
					if (builder == 0L)
					{
						return;
					}
					string key;
					string how;
					long session = ZDOMan.GetSessionID();
					if (hitData != null && !hitData.m_attacker.IsNone())
					{
						ZDO attacker = ZDOMan.instance.GetZDO(hitData.m_attacker);
						ZNetPeer by = PeerOfCharacter(hitData.m_attacker);
						if (by != null || (attacker != null && attacker.GetPrefab() == s_player))
						{
							long id = by != null ? PlayerId(by) : attacker.GetLong(ZDOVars.s_playerID, 0L);
							if (id == builder)
							{
								return;
							}
							key = "player/" + id;
							how = $"hit by {(by != null ? Label(by) : Clean(attacker.GetString(ZDOVars.s_playerName, "?")) + " (" + id + ")")}";
						}
						else if (attacker != null)
						{
							ZNetView attackerView = ZNetScene.instance.FindInstance(attacker);
							BaseAI ai = attackerView ? attackerView.GetComponent<BaseAI>() : null;
							Player chased = ai ? ai.GetTargetCreature() as Player : null;
							string name = NameOf(attacker);
							key = "creature/" + name + "/" + (chased ? chased.GetPlayerID() : 0L);
							how = $"hit by a {name}" + (chased ? $" that was after {Clean(chased.GetPlayerName())} ({chased.GetPlayerID()})" : "");
						}
						else
						{
							key = "attacker/unknown";
							how = "hit by an attacker that is not in the world any more";
						}
					}
					else if (hitData != null)
					{
						// no attacker: fire, a falling object, a catapult, a cart, a boat, a felled tree -- or a modified game
						string type = hitData.m_hitType.ToString();
						if (s_inDamage && s_damageSender != 0L && s_damageSender != session)
						{
							ZNetPeer sent = ZNet.instance.GetPeer(s_damageSender);
							if (sent != null && PlayerId(sent) == builder)
							{
								return;
							}
							key = "hit/" + type + "/" + s_damageSender;
							how = $"hit of type {type} with no attacker, sent by {(sent != null ? Label(sent) : "the game " + s_damageSender)}";
						}
						else
						{
							key = "hit/" + type;
							how = $"hit of type {type} with no attacker, made by the server";
						}
					}
					else if (s_inRemove && s_removeSender != 0L && s_removeSender != session)
					{
						ZNetPeer sent = ZNet.instance.GetPeer(s_removeSender);
						if (sent != null && PlayerId(sent) == builder)
						{
							return;
						}
						key = "removed/" + s_removeSender;
						how = $"{(sent != null ? Label(sent) : "the game " + s_removeSender)} took {{it}} down";
					}
					else
					{
						key = "server";
						how = "collapsed or removed by the server";
					}
					NoteDestroyed(builder, NameOf(zdo), zdo.GetPosition(), key, how, null);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		/*
			One refusal: written to <world>.guard.log (once a minute per player, kind and ward, with how many there were since the last line),
			and with `enforced` the ward flashes and the player is told (the message at most every 15 s, the flash every 3 s).
		*/
		private static void Report(ZNetPeer peer, ZDO target, ZDO ward, string kind, string what, bool enforced, long builder = 0L, ZDO beside = null, string detail = null, bool tell = true, string subject = null)
		{
			// one line a minute per player and ward; a builder's builds and a tombstone per player and builder, so a run along a wall is one line
			bool byBuilder = kind == "build" || kind == "use" || kind == "tombstone";
			string key = byBuilder || kind == "vehicle" || kind == "claim" ? $"{peer.m_uid}/{kind}/{builder}"
				: $"{peer.m_uid}/{kind}/{(subject ?? (ward != null ? ward.m_uid.ToString() : target != null ? target.m_uid.ToString() : "-"))}";
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
				string owner = detail != null ? detail
					: ward != null ? $"; ward of {Clean(ward.GetString(ZDOVars.s_creatorName, "?"))} at {Where(ward)}"
					: kind == "tombstone" ? $"; tombstone of {Clean(target.GetString(ZDOVars.s_ownerName, "?"))} ({builder})"
					: byBuilder ? BuilderText(builder, beside)
					: kind == "vehicle" || kind == "claim" ? $"; built by {PlayerName(builder)} ({builder})"
					: "";
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
			if (!enforced || kind == "spoofing" || !tell)
			{
				return;
			}
			if (Configuration.wardGuardFlash.Value && ward != null && Rate("flash/" + ward.m_uid, 3f))
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ward.m_uid, "FlashShield");
			}
			string message = kind == "tombstone" ? Configuration.wardGuardTombstonesMessage.Value
				: byBuilder || kind == "vehicle" || ((kind == "claim" || kind == "terrain") && ward == null) ? Configuration.wardGuardOutsideWardsMessage.Value : Configuration.wardGuardMessage.Value;
			if (!string.IsNullOrEmpty(message) && Rate("say/" + peer.m_uid, 15f))
			{
				PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.Center, message);
			}
		}
	}
}
