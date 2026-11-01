using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Valheim_Serverside.Features
{
	/*
		A census of the RPCs players' games send that the game itself does not have. A client says nothing about its mods when it
		joins; what a mod leaves behind on the wire is its own RPCs: a config sync (ServerSync's "<guid> ConfigSync"), a map share
		(Valheim Plus' "VPlusMapSync"), a version check on the connection. The server passes a routed one on to whoever it is for
		(ZRoutedRpc.RouteRPC) and drops the rest without a word (ZRoutedRpc.HandleRoutedRPC, ZRpc.HandlePackage).

		What is noted: every routed RPC (ZRoutedRpc.RPC_RoutedRPC) and every RPC on a player's connection (ZRpc.HandlePackage) whose
		method the game does not have: not among the names the game registers or invokes anywhere (RpcNames.Vanilla, read out of
		the decompiled game, so a vanilla client's own RPCs that only clients register count as the game's), not registered on the
		server (by the game or a server plugin: ZRoutedRpc's functions, the connection's, the target object's view). A method is
		only a hash of its name; the name comes from RpcNames.Mods, read out of mods' code, when it is there.

		Told: one line in the log the first time a player sends each in a run; when they leave, what they sent this visit;
		everything kept in <world>.rpcs.txt next to the world (who, which, how often, how many bytes, first and last time, and
		a few readable words out of the last one, which often name the mod). Nothing is blocked or changed, and the packet is
		handed on as it came: the census reads it and puts its position back.
	*/
	public class RpcCensus : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.rpcCensusEnabled.Value;
		}

		private const string KindConnection = "connection";
		private const int MaxPerVisit = 64;
		private const int MaxLinesPerRun = 300;

		private class Entry
		{
			public int hash;
			public string kind;
			public string target;
			public long count;
			public long bytes;
			public DateTime first;
			public DateTime last;
			public string sample = "";
		}

		// One player's visit: what they sent, folded into the records when they leave or the world is saved.
		private class Visit
		{
			public readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
			public readonly Dictionary<string, Entry> all = new Dictionary<string, Entry>();
			public int dropped;
		}

		private static HashSet<int> s_vanilla;
		private static Dictionary<int, string> s_names;
		private static readonly Dictionary<ZNetPeer, Visit> s_visits = new Dictionary<ZNetPeer, Visit>();
		private static readonly Dictionary<string, Entry> s_records = new Dictionary<string, Entry>();
		private static readonly Dictionary<string, string[]> s_who = new Dictionary<string, string[]>();
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		private static string s_path;
		private static bool s_loaded;
		private static bool s_dirty;
		private static int s_lines;
		private static int s_errors;

		private static void Tables()
		{
			if (s_vanilla != null)
			{
				return;
			}
			s_vanilla = new HashSet<int>(RpcNames.Vanilla.Select(n => n.GetStableHashCode()));
			s_names = new Dictionary<int, string>();
			for (int i = 0; i + 1 < RpcNames.Mods.Length; i += 2)
			{
				int h = RpcNames.Mods[i].GetStableHashCode();
				if (!s_vanilla.Contains(h) && !s_names.ContainsKey(h))
				{
					s_names[h] = $"{RpcNames.Mods[i]} ({RpcNames.Mods[i + 1]})";
				}
			}
		}

		[HarmonyPatch(typeof(ZNet), "LoadWorld")]
		public static class ZNet_LoadWorld_Patch
		{
			static void Postfix()
			{
				try
				{
					if (!Configuration.rpcCensusEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer())
					{
						return;
					}
					Tables();
					Load();
					ServersidePlugin.logger.LogInfo($"RPC census: on; {s_vanilla.Count} RPC names of the game, {s_names.Count} of mods; "
						+ $"{s_records.Count} line(s) so far in {(s_path == null ? "<world>.rpcs.txt" : Path.GetFileName(s_path))}.");
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		public static string Describe(int hash)
		{
			Tables();
			return s_names.TryGetValue(hash, out string name) ? name : $"hash {hash}";
		}

		private static bool Known(int hash, ZDOID zdo)
		{
			if (s_vanilla.Contains(hash))
			{
				return true;
			}
			if (zdo.IsNone())
			{
				return ZRoutedRpc.instance != null && ZRoutedRpc.instance.m_functions.ContainsKey(hash);
			}
			ZDO target = ZDOMan.instance?.GetZDO(zdo);
			ZNetView view = target != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(target) : null;
			return view != null && view.m_functions.ContainsKey(hash);
		}

		[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
		public static class ZRoutedRpc_RPC_RoutedRPC_Patch
		{
			// First, so a guard that drops the RPC after it does not hide it from the census.
			[HarmonyPriority(Priority.First)]
			static void Prefix(ZRpc rpc, ZPackage pkg)
			{
				if (!Configuration.rpcCensusEnabled.Value || pkg == null || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return;
				}
				int start = pkg.GetPos();
				try
				{
					Tables();
					pkg.ReadLong();
					pkg.ReadLong();
					long target = pkg.ReadLong();
					ZDOID zdo = pkg.ReadZDOID();
					int hash = pkg.ReadInt();
					if (Known(hash, zdo))
					{
						return;
					}
					ZNetPeer peer = ZNet.instance.GetPeer(rpc);
					if (peer == null)
					{
						return;
					}
					byte[] parameters = pkg.ReadPackage().GetArray();
					string kind = target == 0L ? "everybody" : target == ZNet.GetUID() ? "server" : "player";
					Note(peer, hash, kind, ObjectName(zdo), parameters);
				}
				catch (Exception e)
				{
					Fault(e);
				}
				finally
				{
					pkg.SetPos(start);
				}
			}
		}

		[HarmonyPatch(typeof(ZRpc), "HandlePackage")]
		public static class ZRpc_HandlePackage_Patch
		{
			static void Prefix(ZRpc __instance, ZPackage package)
			{
				if (!Configuration.rpcCensusEnabled.Value || package == null || !ZNet.instance || !ZNet.instance.IsServer())
				{
					return;
				}
				int start = package.GetPos();
				try
				{
					int hash = package.ReadInt();
					if (hash == 0 || __instance.m_functions.ContainsKey(hash))
					{
						return;
					}
					Tables();
					if (s_vanilla.Contains(hash))
					{
						return;
					}
					ZNetPeer peer = ZNet.instance.GetPeer(__instance);
					if (peer == null)
					{
						return;
					}
					byte[] rest = new byte[Math.Max(0, package.Size() - package.GetPos())];
					Array.Copy(package.GetArray(), package.GetPos(), rest, 0, rest.Length);
					Note(peer, hash, KindConnection, "-", rest);
				}
				catch (Exception e)
				{
					Fault(e);
				}
				finally
				{
					package.SetPos(start);
				}
			}
		}

		private static string ObjectName(ZDOID zdo)
		{
			if (zdo.IsNone())
			{
				return "-";
			}
			ZDO target = ZDOMan.instance?.GetZDO(zdo);
			if (target == null)
			{
				return "(gone)";
			}
			UnityEngine.GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(target.GetPrefab()) : null;
			return prefab ? prefab.name : $"(prefab {target.GetPrefab()})";
		}

		private static void Note(ZNetPeer peer, int hash, string kind, string target, byte[] data)
		{
			if (!s_visits.TryGetValue(peer, out Visit visit))
			{
				s_visits[peer] = visit = new Visit();
			}
			string key = hash.ToString(CultureInfo.InvariantCulture) + "\t" + kind;
			if (!visit.entries.TryGetValue(key, out Entry e))
			{
				if (visit.entries.Count >= MaxPerVisit)
				{
					visit.dropped++;
					return;
				}
				DateTime now = DateTime.UtcNow;
				visit.entries[key] = e = new Entry { hash = hash, kind = kind, first = now };
				if (!visit.all.ContainsKey(key))
				{
					visit.all[key] = new Entry { hash = hash, kind = kind, first = now };
				}
			}
			e.count++;
			e.bytes += data.Length;
			e.last = DateTime.UtcNow;
			e.target = target;
			e.sample = Words(data);
			Entry total = visit.all[key];
			total.count++;
			total.bytes += data.Length;
			total.last = e.last;
			total.target = target;
			total.sample = e.sample;
			string host = Host(peer);
			if (s_logged.Add(host + "\t" + key) && s_lines < MaxLinesPerRun)
			{
				s_lines++;
				ServersidePlugin.logger.LogInfo($"RPC census: {Who(peer)} sent {Describe(hash)}, an RPC the game does not have: {Where(kind, target)}, {data.Length} bytes"
					+ (e.sample.Length > 0 ? $", words in it: {e.sample}" : "") + ".");
			}
		}

		private static string Where(string kind, string target)
		{
			string to = kind == KindConnection ? "on the connection" : kind == "everybody" ? "to everybody" : kind == "server" ? "to the server" : "to another player";
			return target == "-" || string.IsNullOrEmpty(target) ? to : $"{to}, on {target}";
		}

		private static string Host(ZNetPeer peer)
		{
			try
			{
				return peer.m_socket?.GetHostName() ?? "?";
			}
			catch
			{
				return "?";
			}
		}

		private static string Who(ZNetPeer peer)
		{
			string name = string.IsNullOrEmpty(peer.m_playerName) ? "(no name yet)" : Clean(peer.m_playerName);
			return $"{name} ({Host(peer)})";
		}

		// Up to three runs of 5 or more readable characters, each at most 48: what a mod puts in its packets often names it.
		private static string Words(byte[] data)
		{
			List<string> words = new List<string>();
			StringBuilder run = new StringBuilder();
			for (int i = 0; i <= data.Length && words.Count < 3; i++)
			{
				char c = i < data.Length ? (char)data[i] : '\0';
				if (c >= 0x21 && c <= 0x7e)
				{
					if (run.Length < 48)
					{
						run.Append(c);
					}
					continue;
				}
				if (run.Length >= 5)
				{
					words.Add(run.ToString());
				}
				run.Clear();
			}
			return string.Join(" | ", words);
		}

		private static string Clean(string text)
		{
			StringBuilder sb = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				sb.Append(char.IsControl(c) ? ' ' : c);
			}
			return sb.ToString();
		}

		private static string Stamp(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

		// What a visit sent since the last fold, into the records (by player, name, method and kind).
		private static void Fold(ZNetPeer peer, Visit visit)
		{
			if (visit.entries.Count == 0)
			{
				return;
			}
			string host = Host(peer);
			string name = string.IsNullOrEmpty(peer.m_playerName) ? "-" : Clean(peer.m_playerName).Replace('\t', ' ');
			foreach (Entry e in visit.entries.Values)
			{
				string key = $"{host}\t{name}\t{e.hash.ToString(CultureInfo.InvariantCulture)}\t{e.kind}";
				if (!s_records.TryGetValue(key, out Entry r))
				{
					s_records[key] = r = new Entry { hash = e.hash, kind = e.kind, first = e.first };
					s_who[key] = new[] { host, name };
				}
				r.count += e.count;
				r.bytes += e.bytes;
				r.last = e.last > r.last ? e.last : r.last;
				r.target = e.target;
				r.sample = e.sample;
			}
			visit.entries.Clear();
			s_dirty = true;
		}

		private static void Load()
		{
			if (s_loaded || ZNet.World == null)
			{
				return;
			}
			s_loaded = true;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".rpcs.txt");
			string file = File.Exists(s_path) ? s_path : File.Exists(s_path + ".tmp") ? s_path + ".tmp" : null;
			if (file == null)
			{
				return;
			}
			foreach (string line in File.ReadAllLines(file))
			{
				string[] f = line.Split('\t');
				if (line.StartsWith("#", StringComparison.Ordinal) || f.Length < 11 || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hash))
				{
					continue;
				}
				string key = $"{f[0]}\t{f[1]}\t{f[2]}\t{f[4]}";
				s_records[key] = new Entry
				{
					hash = hash,
					kind = f[4],
					target = f[5],
					count = long.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long c) ? c : 0,
					bytes = long.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b) ? b : 0,
					first = DateTime.TryParse(f[8], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t1) ? t1 : DateTime.UtcNow,
					last = DateTime.TryParse(f[9], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t2) ? t2 : DateTime.UtcNow,
					sample = f[10],
				};
				s_who[key] = new[] { f[0], f[1] };
			}
		}

		private static void Save()
		{
			if (!s_dirty)
			{
				return;
			}
			try
			{
				Load();
				if (s_path == null)
				{
					return;
				}
				List<string> lines = new List<string>
				{
					"# RPCs players' games sent that the game does not have (Dedicated Simulation, [RpcCensus]). Tab separated:",
					"# player (host), character, method hash, method (mod) if known, kind (everybody/server/player = routed to, connection), object it was on, count, bytes, first, last (UTC), words in the last one",
				};
				foreach (KeyValuePair<string, Entry> pair in s_records.OrderBy(p => p.Key, StringComparer.Ordinal))
				{
					Entry e = pair.Value;
					string[] who = s_who[pair.Key];
					string named = s_names.TryGetValue(e.hash, out string n) ? n : "-";
					lines.Add(string.Join("\t", who[0], who[1], e.hash.ToString(CultureInfo.InvariantCulture), named, e.kind, (e.target ?? "-").Replace('\t', ' '),
						e.count.ToString(CultureInfo.InvariantCulture), e.bytes.ToString(CultureInfo.InvariantCulture), Stamp(e.first), Stamp(e.last), e.sample.Replace('\t', ' ')));
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
				Fault(e);
			}
		}

		private static void FoldAllAndSave()
		{
			if (!Configuration.rpcCensusEnabled.Value || s_vanilla == null)
			{
				return;
			}
			Load();
			foreach (KeyValuePair<ZNetPeer, Visit> pair in s_visits)
			{
				Fold(pair.Key, pair.Value);
			}
			Save();
		}

		[HarmonyPatch(typeof(ZNet), "Disconnect")]
		public static class ZNet_Disconnect_Patch
		{
			static void Prefix(ZNetPeer peer)
			{
				try
				{
					if (!Configuration.rpcCensusEnabled.Value || peer == null || !ZNet.instance || !ZNet.instance.IsServer() || !s_visits.TryGetValue(peer, out Visit visit))
					{
						return;
					}
					s_visits.Remove(peer);
					if (visit.all.Count > 0)
					{
						string list = string.Join("; ", visit.all.Values.OrderByDescending(e => e.count).Take(12)
							.Select(e => $"{Describe(e.hash)} x{e.count} {Where(e.kind, "-")}"));
						ServersidePlugin.logger.LogInfo($"RPC census: {Who(peer)} left; this visit {visit.all.Count} RPC(s) the game does not have: {list}"
							+ (visit.all.Count > 12 ? ", ..." : "") + (visit.dropped > 0 ? $"; {visit.dropped} more of more kinds not noted" : "") + ".");
					}
					Load();
					Fold(peer, visit);
					Save();
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		[HarmonyPatch(typeof(ZNet), "SaveWorld")]
		public static class ZNet_SaveWorld_Patch
		{
			static void Postfix()
			{
				try
				{
					FoldAllAndSave();
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		[HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
		public static class ZNet_Shutdown_Patch
		{
			static void Prefix()
			{
				try
				{
					FoldAllAndSave();
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static void Fault(Exception e)
		{
			if (s_errors++ < 5)
			{
				ServersidePlugin.logger.LogWarning($"RPC census: {e.GetType().Name}: {e.Message}");
			}
		}
	}
}
