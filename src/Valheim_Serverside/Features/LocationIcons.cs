using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Map icons of locations -- the traders (Haldor, Hildir, the Bog Witch), Hildir's dungeons, the Deep
		North boss room, the ancient upgrade station: vanilla sends every player the icon of each of them
		as soon as its zone is generated (ZoneSystem.SendLocationIcons to everyone, for a location whose
		m_iconPlaced is set and that is placed), which happens when anyone passes within a few zones,
		often without anyone having seen it; and a player's map shows such an icon wherever they have
		been or not (Minimap.UpdateLocationPins adds it, UpdatePins looks at no fog).

		Here each player is sent only the icons of the locations they have been near themselves
		(DiscoverRadius, horizontally, also from inside a dungeon above or below it), remembered per
		character (the character's player id, the same as the other features use) in
		<world>.icons.txt next to the world. Icons the game shows always (m_iconAlways: the start temple)
		stay for everyone. Nothing changes for the players' own pins.
	*/
	public class LocationIcons : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.locationIconsEnabled.Value;
		}

		// Per character: the zones of the icon locations it has been near.
		private static readonly Dictionary<long, HashSet<Vector2s>> s_found = new Dictionary<long, HashSet<Vector2s>>();
		// Per connected peer: for which character and how many icons it was last sent.
		private static readonly Dictionary<long, KeyValuePair<long, int>> s_sent = new Dictionary<long, KeyValuePair<long, int>>();
		private static string s_path;
		private static bool s_loaded, s_dirty, s_announced;
		private static float s_nextTick, s_nextSave;
		private static int s_errors;

		private static long PlayerOf(ZNetPeer peer)
		{
			if (peer == null || peer.m_characterID.IsNone())
			{
				return 0L;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
		}

		// The game's own choice of icons (ZoneSystem.GetLocationIcons on the server).
		private static List<ZoneSystem.LocationInstance> IconLocations()
		{
			return ZoneSystem.instance.m_locationInstances.Values
				.Where(li => li.m_location != null && (li.m_location.m_iconAlways || (li.m_location.m_iconPlaced && li.m_placed))).ToList();
		}

		// What ZoneSystem.SendLocationIcons sends, for this peer's character only.
		private static void SendTo(ZNetPeer peer)
		{
			long player = PlayerOf(peer);
			HashSet<Vector2s> found = player != 0L && s_found.TryGetValue(player, out HashSet<Vector2s> set) ? set : null;
			List<ZoneSystem.LocationInstance> icons = IconLocations()
				.Where(li => li.m_location.m_iconAlways || (found != null && found.Contains(ZoneSystem.GetZone(li.m_position)))).ToList();
			ZPackage pkg = new ZPackage();
			pkg.Write(icons.Count);
			foreach (ZoneSystem.LocationInstance icon in icons)
			{
				pkg.Write(icon.m_position);
				pkg.Write(icon.m_location.m_prefab.Name);
			}
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "LocationIcons", pkg);
			s_sent[peer.m_uid] = new KeyValuePair<long, int>(player, icons.Count);
		}

		// The game sends icons when a location with one is placed (to everyone) and to every player who joins.
		[HarmonyPatch(typeof(ZoneSystem), "SendLocationIcons")]
		public static class ZoneSystem_SendLocationIcons_Patch
		{
			static bool Prefix(long peer)
			{
				if (!Configuration.locationIconsEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return true;
				}
				try
				{
					Load();
					foreach (ZNetPeer p in ZNet.instance.GetPeers())
					{
						if (peer == 0L || p.m_uid == peer)
						{
							SendTo(p);
						}
					}
					return false;
				}
				catch (Exception e)
				{
					if (s_errors++ < 5)
					{
						ServersidePlugin.logger.LogWarning($"Location icons: {e}");
					}
					return true;
				}
			}
		}

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.locationIconsEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || !ZoneSystem.instance || ZNet.World == null || Time.time < s_nextTick)
			{
				return;
			}
			s_nextTick = Time.time + 2f;
			Load();
			if (!s_announced)
			{
				s_announced = true;
				List<ZoneSystem.LocationInstance> all = IconLocations();
				ServersidePlugin.logger.LogInfo($"Location icons: a player's map shows a location's icon once they have been within {Configuration.locationIconsDiscoverRadius.Value:0} m of it; "
					+ $"{all.Count(li => !li.m_location.m_iconAlways)} placed so far ({string.Join(", ", all.Where(li => !li.m_location.m_iconAlways).Select(li => li.m_location.m_prefabName).Distinct())}), "
					+ $"{all.Count(li => li.m_location.m_iconAlways)} shown to everyone; found by {s_found.Count} character(s) so far.");
			}
			float radius = Mathf.Max(0f, Configuration.locationIconsDiscoverRadius.Value);
			List<ZoneSystem.LocationInstance> placed = IconLocations().Where(li => !li.m_location.m_iconAlways).ToList();
			HashSet<long> connected = new HashSet<long>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				connected.Add(peer.m_uid);
				if (!peer.IsReady())
				{
					continue;
				}
				long player = PlayerOf(peer);
				// A peer whose character has just appeared gets its own icons, not the ones sent before it had one.
				bool resend = !s_sent.TryGetValue(peer.m_uid, out KeyValuePair<long, int> sent) || sent.Key != player;
				if (player != 0L)
				{
					Vector3 at = peer.GetRefPos();
					foreach (ZoneSystem.LocationInstance location in placed)
					{
						float x = location.m_position.x - at.x, z = location.m_position.z - at.z;
						if (x * x + z * z > radius * radius)
						{
							continue;
						}
						if (!s_found.TryGetValue(player, out HashSet<Vector2s> set))
						{
							s_found[player] = set = new HashSet<Vector2s>();
						}
						if (set.Add(ZoneSystem.GetZone(location.m_position)))
						{
							resend = true;
							s_dirty = true;
							ServersidePlugin.logger.LogInfo($"Location icons: {peer.m_playerName} found {location.m_location.m_prefabName} at {location.m_position.x:0},{location.m_position.z:0}");
						}
					}
				}
				if (resend)
				{
					SendTo(peer);
				}
			}
			foreach (long gone in s_sent.Keys.Where(uid => !connected.Contains(uid)).ToList())
			{
				s_sent.Remove(gone);
			}
			if (s_dirty && Time.time >= s_nextSave)
			{
				Save();
			}
		}

		private static void Load()
		{
			if (s_loaded || ZNet.World == null)
			{
				return;
			}
			s_loaded = true;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".icons.txt");
			string file = File.Exists(s_path) ? s_path : File.Exists(s_path + ".tmp") ? s_path + ".tmp" : null;
			if (file == null)
			{
				return;
			}
			foreach (string line in File.ReadAllLines(file))
			{
				string[] f = line.Split(' ');
				if (f.Length < 3 || f[0].StartsWith("#"))
				{
					continue;
				}
				try
				{
					long player = long.Parse(f[0], CultureInfo.InvariantCulture);
					if (!s_found.TryGetValue(player, out HashSet<Vector2s> set))
					{
						s_found[player] = set = new HashSet<Vector2s>();
					}
					set.Add(new Vector2s(int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture)));
				}
				catch (Exception)
				{
					// A damaged line costs that one icon, found again when its player is near.
				}
			}
		}

		private static void Save()
		{
			s_nextSave = Time.time + 30f;
			try
			{
				List<string> lines = new List<string> { "# Location icons each character has found: player id, zone x, zone y. Edit only while the server is stopped." };
				foreach (KeyValuePair<long, HashSet<Vector2s>> player in s_found)
				{
					foreach (Vector2s zone in player.Value)
					{
						lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", player.Key, zone.x, zone.y));
					}
				}
				File.WriteAllLines(s_path + ".tmp", lines);
				if (File.Exists(s_path))
				{
					File.Delete(s_path);
				}
				File.Move(s_path + ".tmp", s_path);
				s_dirty = false;
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Location icons: could not save {s_path}: {e.Message}");
			}
		}
	}
}
