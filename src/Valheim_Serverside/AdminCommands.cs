using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside
{
	/*
		What the server console's own commands do (ServerConsole). Everything here runs on the
		server's main thread.
	*/
	public static class AdminCommands
	{
		private static Dictionary<string, GameObject> s_items;

		// Online players by name: exact match first, then a unique start or part of the name.
		public static ZNetPeer FindPlayer(string name, out string problem)
		{
			problem = null;
			List<ZNetPeer> online = ZNet.instance.GetPeers().Where(p => p.IsReady() && !string.IsNullOrEmpty(p.m_playerName)).ToList();
			ZNetPeer exact = online.FirstOrDefault(p => string.Equals(p.m_playerName, name, StringComparison.OrdinalIgnoreCase));
			if (exact != null)
			{
				return exact;
			}
			List<ZNetPeer> starts = online.Where(p => p.m_playerName.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
			List<ZNetPeer> found = starts.Count > 0 ? starts : online.Where(p => p.m_playerName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			if (found.Count == 1)
			{
				return found[0];
			}
			problem = found.Count == 0
				? $"No player '{name}' online. Online: {Names(online)}"
				: $"'{name}' matches several players: {Names(found)}";
			return null;
		}

		public static string Players()
		{
			List<ZNetPeer> online = ZNet.instance.GetPeers().Where(p => p.IsReady()).ToList();
			if (online.Count == 0)
			{
				return "No players online";
			}
			return string.Join("; ", online.Select(p =>
			{
				Vector3 at = p.GetRefPos();
				return $"{p.m_playerName} ({p.m_socket.GetHostName()}) at {at.x:0},{at.z:0}";
			}));
		}

		// Drops the items in front of the player, stacked as the item allows. Returns what happened, for the log and the reply.
		public static string Give(ZNetPeer target, string itemName, int amount, string byWhom)
		{
			GameObject prefab = FindItem(itemName);
			if (!prefab)
			{
				return $"No item '{itemName}'.{Suggest(itemName)}";
			}
			amount = Mathf.Clamp(amount, 1, Mathf.Max(1, PluginConfiguration.Configuration.maxGiveAmount.Value));
			ZDO character = ZDOMan.instance.GetZDO(target.m_characterID);
			Vector3 origin = (character != null ? character.GetPosition() : target.GetRefPos()) + Vector3.up * 1.5f;
			int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
			int stacks = 0;
			for (int left = amount; left > 0; left -= maxStack)
			{
				Vector2 spread = UnityEngine.Random.insideUnitCircle * 0.75f;
				Vector3 position = origin + new Vector3(spread.x, 0.25f * stacks, spread.y);
				GameObject go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
				go.GetComponent<ItemDrop>().SetStack(Mathf.Min(left, maxStack));
				stacks++;
			}
			string result = $"{byWhom} gave {amount} x {prefab.name} to {target.m_playerName} in {stacks} stack(s) at {origin.x:0},{origin.z:0}";
			ServersidePlugin.logger.LogInfo("Admin: " + result);
			return result;
		}

		public static string Save(string byWhom)
		{
			if (!ZNet.instance || !ZNet.instance.IsServer())
			{
				return "No world loaded, nothing to save";
			}
			if (!ZNet.instance.EnoughDiskSpaceAvailable(out bool _))
			{
				return "Not enough disk space, world not saved";
			}
			// The same call as the server's own autosave (Game.UpdateSaving). The vanilla `save`
			// command goes through RPC_Save, which throws on a dedicated server when not sent by a player.
			ServersidePlugin.logger.LogInfo($"Admin: {byWhom} requested a world save");
			ZNet.instance.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true);
			return "Saving world";
		}

		// A message in the middle of every player's screen, the way the game announces a raid. Chat
		// from the server is not shown by Valheim 1.0 clients (it needs a real player's user id).
		public static string Broadcast(string text, string byWhom)
		{
			float held = PlayerMessages.Show(ZRoutedRpc.Everybody, MessageHud.MessageType.Center, text);
			string shown = text.Replace("\n", "\\n");
			ServersidePlugin.logger.LogInfo($"Admin: {byWhom} broadcast: {shown}");
			return $"shown to {ZNet.instance.GetNrOfPlayers()} player(s){(held > 0f ? $" for {held:0} s" : "")}: {shown}";
		}

		// Starts a random event (raid) at the player, like the game's `event` command at the host.
		public static string StartEvent(string name, ZNetPeer target, string byWhom)
		{
			if (!RandEventSystem.instance || !RandEventSystem.instance.HaveEvent(name))
			{
				return $"No event '{name}'. Events: {Events()}";
			}
			ZDO character = ZDOMan.instance.GetZDO(target.m_characterID);
			Vector3 at = character != null ? character.GetPosition() : target.GetRefPos();
			RandEventSystem.instance.SetRandomEventByName(name, at);
			string result = $"{byWhom} started event {name} at {target.m_playerName} ({at.x:0},{at.z:0})";
			ServersidePlugin.logger.LogInfo("Admin: " + result);
			return result;
		}

		public static string Events()
		{
			return RandEventSystem.instance ? string.Join(", ", RandEventSystem.instance.m_events.Select(e => e.m_name)) : "none (no world loaded)";
		}

		/*
			Teleports are the game's own. The server asks the player's game to move its player (RPC_TeleportPlayer, which
			vanilla's `recall` sends too) and the game does it as it does a portal: after 2 s the player is at the spot,
			and once the area has loaded (8 s at the earliest) on the floor below it. At a height of 30, which is what
			the game's own `goto` uses, the spot is under the ground nearly everywhere. The game then waits 15 s and puts
			the player on top of whatever stands there: the ground, a roof or a rock. At sea, the player ends up in the water.
			Each teleport is watched until the character is there, and the console is told.
		*/
		public const float GotoHeight = 30f;
		private const float ArrivedMetres = 20f;
		private const float GiveUpSeconds = 40f;

		private class Move
		{
			public long user;
			public string name;
			public Vector3 to;
			public float sent;
			public float settle;
		}

		private static readonly List<Move> s_moves = new List<Move>();
		private static float s_nextLook;

		// The start: where a new character first appears, 2 m above the middle of the sacrificial stones (Game.FindSpawnPoint).
		public static bool TryStart(out Vector3 start)
		{
			foreach (var location in ZoneSystem.instance.m_locationInstances.Values)
			{
				if (location.m_location != null && location.m_location.m_prefabName == "StartTemple")
				{
					start = location.m_position + Vector3.up * 2f;
					return true;
				}
			}
			start = Vector3.zero;
			return false;
		}

		// Where the player's character is, as the server last heard from their game.
		public static Vector3 PositionOf(ZNetPeer peer)
		{
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetPosition() : peer.GetRefPos();
		}

		// `to` is the spot; `facing` null keeps the way each player faces; `settle` is how long after sending the
		// game takes to put them down (15 s at the goto height, 8 s above a floor).
		public static string Teleport(List<ZNetPeer> players, Vector3 to, Quaternion? facing, string what, string byWhom)
		{
			if (players.Count == 0)
			{
				return "Nobody to teleport";
			}
			bool underGround = Mathf.Approximately(to.y, GotoHeight);
			List<string> sent = new List<string>();
			foreach (ZNetPeer peer in players)
			{
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				Vector3 from = character != null ? character.GetPosition() : peer.GetRefPos();
				Quaternion rotation = facing ?? (character != null ? character.GetRotation() : Quaternion.identity);
				Features.AntiCheat.ServerMoved(peer.m_uid, to);
				ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "RPC_TeleportPlayer", to, rotation, true);
				s_moves.RemoveAll(m => m.user == peer.m_uid);
				s_moves.Add(new Move { user = peer.m_uid, name = peer.m_playerName, to = to, sent = Time.time, settle = underGround ? 17f : 10f });
				sent.Add($"{peer.m_playerName} from {At(from)} ({Vector3.Distance(from, to):0} m)");
			}
			string height = underGround ? "onto whatever stands there in about 15 s" : $"to height {to.y:0.#}";
			string result = $"{byWhom} sent {string.Join(", ", sent)} to {what} at {to.x:0},{to.z:0}, {height}";
			ServersidePlugin.logger.LogInfo("Admin: " + result);
			return result;
		}

		// Once a second: which of the teleports sent are done, given up or left. `reply` writes to the console.
		public static void Update(Action<string> reply)
		{
			if (s_moves.Count == 0 || Time.time < s_nextLook || !ZNet.instance || ZDOMan.instance == null)
			{
				return;
			}
			s_nextLook = Time.time + 1f;
			foreach (Move move in s_moves.ToList())
			{
				float since = Time.time - move.sent;
				ZNetPeer peer = ZNet.instance.GetPeer(move.user);
				if (peer == null)
				{
					reply($"tp: {move.name} left before they were there");
					s_moves.Remove(move);
					continue;
				}
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				Vector3 at = character != null ? character.GetPosition() : peer.GetRefPos();
				bool there = new Vector2(at.x - move.to.x, at.z - move.to.z).magnitude < ArrivedMetres;
				if (there && since >= move.settle)
				{
					string health = character != null ? $", health {character.GetFloat(ZDOVars.s_health):0} of {character.GetFloat(ZDOVars.s_maxHealth):0}" : "";
					string line = $"tp: {move.name} is there, {since:0} s after it was sent: at {At(at)}{health}";
					ServersidePlugin.logger.LogInfo("Admin: " + line);
					reply(line);
					s_moves.Remove(move);
				}
				else if (!there && since > GiveUpSeconds)
				{
					string line = $"tp: {move.name} is not there after {GiveUpSeconds:0} s, their game did not move them (dead, in a menu or already teleporting?); still at {At(at)}";
					ServersidePlugin.logger.LogInfo("Admin: " + line);
					reply(line);
					s_moves.Remove(move);
				}
			}
		}

		public static string Positions(List<ZNetPeer> players)
		{
			if (players.Count == 0)
			{
				return "No players online";
			}
			return string.Join("; ", players.Select(peer =>
			{
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				Vector3 at = character != null ? character.GetPosition() : peer.GetRefPos();
				string place = Character.InInterior(at) ? "in a dungeon" : WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(at).ToString() : "";
				string health = character != null ? $", health {character.GetFloat(ZDOVars.s_health):0} of {character.GetFloat(ZDOVars.s_maxHealth):0}" : ", no character yet";
				return $"{peer.m_playerName} at {At(at)} {place}{health}";
			}));
		}

		// Health only: the game has no message for another's stamina. Their game adds it, up to their maximum, unless they are dead.
		public static string Heal(ZNetPeer target, float amount, string byWhom)
		{
			ZDO character = ZDOMan.instance.GetZDO(target.m_characterID);
			if (character == null)
			{
				return $"{target.m_playerName} has no character yet";
			}
			float health = character.GetFloat(ZDOVars.s_health);
			float max = character.GetFloat(ZDOVars.s_maxHealth);
			if (health <= 0f)
			{
				return $"{target.m_playerName} is dead";
			}
			float hp = amount > 0f ? amount : Mathf.Max(max - health, 0f) + 1000f;
			ZRoutedRpc.instance.InvokeRoutedRPC(character.GetOwner(), target.m_characterID, "RPC_Heal", hp, true);
			string result = $"{byWhom} healed {target.m_playerName}: health was {health:0} of {max:0}, their game adds {(amount > 0f ? $"{amount:0}" : "the rest")}";
			ServersidePlugin.logger.LogInfo("Admin: " + result);
			return result;
		}

		// What the game's own `spawn` does in front of its player, in front of the target: at most MaxSpawnAmount, level 1 to 9,
		// marked as cheated as the game marks its own (what such a creature drops is cheated too).
		public static string Spawn(ZNetPeer target, string prefabName, int amount, int level, string byWhom)
		{
			GameObject prefab = FindPrefab(prefabName);
			if (!prefab)
			{
				return $"No prefab '{prefabName}'.{SuggestPrefab(prefabName)}";
			}
			if (prefab.GetComponent<ItemDrop>())
			{
				return $"{prefab.name} is an item: give {prefab.name} <amount> {target.m_playerName}";
			}
			if (prefab.GetComponent<Player>())
			{
				return "Players cannot be spawned";
			}
			amount = Mathf.Clamp(amount, 1, Mathf.Max(1, PluginConfiguration.Configuration.maxSpawnAmount.Value));
			level = Mathf.Clamp(level, 1, 9);
			ZDO character = ZDOMan.instance.GetZDO(target.m_characterID);
			Vector3 at = character != null ? character.GetPosition() : target.GetRefPos();
			Quaternion facing = character != null ? character.GetRotation() : Quaternion.identity;
			Vector3 origin = at + facing * Vector3.forward * 3f + Vector3.up;
			int levelled = 0;
			for (int i = 0; i < amount; i++)
			{
				Vector2 spread = amount == 1 ? Vector2.zero : UnityEngine.Random.insideUnitCircle * 1.5f;
				GameObject go = UnityEngine.Object.Instantiate(prefab, origin + new Vector3(spread.x, 0f, spread.y), Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
				Character creature = go.GetComponent<Character>();
				if (creature && level > 1)
				{
					creature.SetLevel(level);
					levelled++;
				}
				ZNetView view = go.GetComponent<ZNetView>();
				if (view && view.IsValid())
				{
					view.GetZDO().Set(ZDOVars.s_cheated, 2);
				}
			}
			string stars = levelled > 0 ? $" level {level}" : "";
			string result = $"{byWhom} spawned {amount} x {prefab.name}{stars} in front of {target.m_playerName} at {At(origin)}";
			ServersidePlugin.logger.LogInfo("Admin: " + result);
			return result;
		}

		public static string At(Vector3 p)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0}", p.x, p.y, p.z);
		}

		private static Dictionary<string, GameObject> s_prefabs;

		private static GameObject FindPrefab(string name)
		{
			if (s_prefabs == null)
			{
				s_prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					if (prefab && prefab.GetComponent<ZNetView>() && !s_prefabs.ContainsKey(prefab.name))
					{
						s_prefabs[prefab.name] = prefab;
					}
				}
			}
			return s_prefabs.TryGetValue(name, out GameObject found) ? found : null;
		}

		private static string SuggestPrefab(string name)
		{
			List<string> similar = s_prefabs.Keys.Where(k => k.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(k => k.Length).Take(8).ToList();
			return similar.Count > 0 ? " Similar: " + string.Join(", ", similar) : "";
		}

		public static bool TryParseNumber(string text, out float value)
		{
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		public static bool TryParseAmount(string text, out int amount)
		{
			return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount);
		}

		private static string Names(List<ZNetPeer> peers)
		{
			return peers.Count == 0 ? "nobody" : string.Join(", ", peers.Select(p => p.m_playerName));
		}

		private static GameObject FindItem(string name)
		{
			if (s_items == null)
			{
				s_items = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					if (prefab && prefab.GetComponent<ItemDrop>() && !s_items.ContainsKey(prefab.name))
					{
						s_items[prefab.name] = prefab;
					}
				}
			}
			return s_items.TryGetValue(name, out GameObject found) ? found : null;
		}

		private static string Suggest(string name)
		{
			List<string> similar = new List<string>();
			foreach (string key in s_items.Keys)
			{
				if (key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					similar.Add(key);
					if (similar.Count == 8)
					{
						break;
					}
				}
			}
			return similar.Count > 0 ? " Similar: " + string.Join(", ", similar) : "";
		}
	}
}
