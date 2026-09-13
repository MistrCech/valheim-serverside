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
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.Center, text);
			ServersidePlugin.logger.LogInfo($"Admin: {byWhom} broadcast: {text}");
			return $"shown to {ZNet.instance.GetNrOfPlayers()} player(s): {text}";
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
