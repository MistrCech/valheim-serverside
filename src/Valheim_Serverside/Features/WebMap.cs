using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		What a web map of the world needs, written as files for a program that serves them (nothing here talks to the network):

		live.json (public, every Interval seconds): who is online and where, the public portals (a tag that begins with the
		portal lock's PublicPrefix), and the player-built pieces counted in squares of BaseCellMetres -- no builders, no exact
		places. places.json (private, every 5 minutes): every location of the world (what the game placed, and what it has not
		placed yet) and which bosses are beaten. explored/<account>.bin (private): the part of the map each account has been
		through, kept the way the game keeps its own map.

		A player's explored map lives in their game only (Minimap.m_explored is saved in the character and never sent), so here it
		is made again from where the character is: every 2 s a disc of the same size as the game's -- ceil(ExploreRadius / 12)
		cells of a 2048 x 2048 grid of 12 m cells, centred on (x / 12 + 1024, z / 12 + 1024), rounded -- is marked, exactly as
		Minimap.Explore marks it. What a player's game knew before this was on, and what a cartography table gave it, is not
		known here; the map starts when the feature does. The account is the one the player connects with (the host name of
		their socket, the Steam id on Steam), not the character.

		The files are written by one background thread (a copy of the data is handed over; the game's thread never waits for a
		disk, except in the last write when the world is saved or the server stops), each through a temporary file that then
		replaces the old one.
	*/
	public class WebMap : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.webMapEnabled.Value;
		}

		private const int Grid = 2048;
		private const float PixelMetres = 12f;
		private const int BitsetBytes = Grid * Grid / 8;
		private const float ExploreEvery = 2f;
		private const float SaveEvery = 60f;
		private const float PlacesEvery = 300f;
		private const float BasesEvery = 300f;
		private const int BasesPerFrame = 20000;

		private sealed class Account
		{
			public string id;
			public string name = "";
			public byte[] bits;	// null while the account is not online: its map is in its file only
			public int cells;
			public long last;
			public bool dirty;
			public int seen;	// the pass of TrackPlayers that found the account online last
		}

		private static readonly Dictionary<string, Account> s_accounts = new Dictionary<string, Account>();
		private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		private static string s_dir, s_publicDir;
		private static bool s_failed, s_loaded, s_announced, s_indexDirty;
		private static float s_nextExplore, s_nextLive, s_nextPlaces, s_nextSave, s_nextBases;
		private static int s_errors, s_pass;

		// The squares of pieces: the last complete count, and the one being made.
		private static Dictionary<long, int> s_bases;
		private static List<ZDOID> s_scanList;
		private static int s_scanPos;
		private static Dictionary<long, int> s_scanCells;
		private static readonly System.Diagnostics.Stopwatch s_scanWatch = new System.Diagnostics.Stopwatch();
		private static double s_scanWorstMs, s_scanTotalMs;
		private static bool s_scanLogged;

		// The writer: one thread, jobs in order; what it could not do is reported back through these.
		private static BlockingCollection<Action> s_jobs;
		private static readonly ConcurrentQueue<KeyValuePair<string, byte[]>> s_unwritten = new ConcurrentQueue<KeyValuePair<string, byte[]>>();
		private static readonly ConcurrentQueue<string> s_messages = new ConcurrentQueue<string>();

		private static void Enqueue(Action job)
		{
			if (s_jobs == null)
			{
				s_jobs = new BlockingCollection<Action>(256);
				Thread writer = new Thread(() =>
				{
					foreach (Action next in s_jobs.GetConsumingEnumerable())
					{
						try
						{
							next();
						}
						catch (Exception e)
						{
							s_messages.Enqueue($"{e.GetType().Name}: {e.Message}");
						}
					}
				});
				writer.IsBackground = true;
				writer.Name = "Web map writer";
				writer.Start();
			}
			if (!s_jobs.TryAdd(job))
			{
				s_messages.Enqueue("the writer is behind (256 jobs waiting); this one is dropped");
			}
		}

		// Waits until what has been handed to the writer is written (at most `seconds`).
		private static void Drain(float seconds)
		{
			if (s_jobs == null)
			{
				return;
			}
			using (ManualResetEvent done = new ManualResetEvent(false))
			{
				if (s_jobs.TryAdd(() => done.Set()))
				{
					done.WaitOne(TimeSpan.FromSeconds(seconds));
				}
			}
		}

		private static void Report()
		{
			while (s_messages.TryDequeue(out string message))
			{
				if (s_errors++ < 5)
				{
					ServersidePlugin.logger.LogWarning($"Web map: {message}");
				}
			}
			// A map the writer could not write comes back, to be written again with whatever the account has done since (the account may
			// have let go of it, or read the old file again, in the meantime).
			while (s_unwritten.TryDequeue(out KeyValuePair<string, byte[]> back))
			{
				if (!s_accounts.TryGetValue(back.Key, out Account account))
				{
					continue;
				}
				if (account.bits == null)
				{
					account.bits = back.Value;
				}
				else
				{
					for (int i = 0; i < BitsetBytes; i++)
					{
						account.bits[i] |= back.Value[i];
					}
				}
				account.cells = Count(account.bits);
				account.dirty = true;
			}
		}

		private static void Replace(string temporary, string path)
		{
			for (int attempt = 0; ; attempt++)
			{
				try
				{
					if (File.Exists(path))
					{
						File.Replace(temporary, path, null);
					}
					else
					{
						File.Move(temporary, path);
					}
					return;
				}
				catch (IOException)
				{
					// A reader has the old file open (a web server on Windows): once more, then the next round.
					if (attempt >= 2)
					{
						throw;
					}
					Thread.Sleep(100);
				}
			}
		}

		private static void WriteText(string path, string text)
		{
			string temporary = path + ".tmp";
			File.WriteAllBytes(temporary, new UTF8Encoding(false).GetBytes(text));
			Replace(temporary, path);
		}

		private static void WriteBitset(string path, byte[] bits)
		{
			string temporary = path + ".tmp";
			using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			using (GZipStream zip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest))
			{
				zip.Write(bits, 0, bits.Length);
			}
			Replace(temporary, path);
		}

		private static long Unix()
		{
			return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		}

		private static bool Resolve()
		{
			if (s_dir != null)
			{
				return true;
			}
			if (s_failed || ZNet.World == null)
			{
				return false;
			}
			try
			{
				string dir = Configuration.webMapDirectory.Value;
				if (string.IsNullOrWhiteSpace(dir))
				{
					dir = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".webmap");
				}
				string pub = Configuration.webMapPublicDirectory.Value;
				if (string.IsNullOrWhiteSpace(pub))
				{
					pub = dir;
				}
				Directory.CreateDirectory(Path.Combine(dir, "explored"));
				Directory.CreateDirectory(pub);
				s_dir = dir;
				s_publicDir = pub;
				return true;
			}
			catch (Exception e)
			{
				s_failed = true;
				ServersidePlugin.logger.LogWarning($"Web map: the folders cannot be used ({e.Message}); nothing is written until the next start.");
				return false;
			}
		}

		// The file name of an account: its socket's host name, letters and digits only.
		private static string KeyOf(ZNetPeer peer)
		{
			string host = peer.m_socket != null ? peer.m_socket.GetHostName() : null;
			if (string.IsNullOrEmpty(host))
			{
				return null;
			}
			StringBuilder key = new StringBuilder();
			foreach (char c in host)
			{
				key.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-') ? c : '_');
				if (key.Length >= 64)
				{
					break;
				}
			}
			return key.ToString();
		}

		private static int Count(byte[] bits)
		{
			int n = 0;
			for (int i = 0; i < bits.Length; i++)
			{
				for (byte b = bits[i]; b != 0; b &= (byte)(b - 1))
				{
					n++;
				}
			}
			return n;
		}

		// A name for the tab separated index: no control character, no line separator of any kind.
		private static string Clean(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			StringBuilder clean = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				clean.Append(c < ' ' || c == '\u007f' || c == '\u0085' || c == '\u2028' || c == '\u2029' ? ' ' : c);
			}
			return clean.ToString();
		}

		private static bool SafeKey(string key)
		{
			if (string.IsNullOrEmpty(key) || key.Length > 64)
			{
				return false;
			}
			foreach (char c in key)
			{
				if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-')))
				{
					return false;
				}
			}
			return true;
		}

		private static byte[] ReadBitset(string file)
		{
			byte[] bits = new byte[BitsetBytes];
			using (FileStream stream = File.OpenRead(file))
			using (GZipStream zip = new GZipStream(stream, CompressionMode.Decompress))
			{
				int got = 0, n;
				while (got < BitsetBytes && (n = zip.Read(bits, got, BitsetBytes - got)) > 0)
				{
					got += n;
				}
				if (got != BitsetBytes || zip.ReadByte() != -1)
				{
					throw new InvalidDataException($"not {BitsetBytes} bytes");
				}
			}
			return bits;
		}

		// What the accounts are, from the index and the folder. A map is read when it is needed (its player is online), so the memory
		// held is that of the players online, not of everyone who ever came.
		private static void Load()
		{
			if (s_loaded)
			{
				return;
			}
			s_loaded = true;
			string folder = Path.Combine(s_dir, "explored");
			string index = Path.Combine(folder, "index.tsv");
			if (File.Exists(index))
			{
				foreach (string line in File.ReadAllLines(index))
				{
					string[] f = line.Split('\t');
					if (f.Length >= 4 && !line.StartsWith("#") && SafeKey(f[0]) && File.Exists(Path.Combine(folder, f[0] + ".bin"))
						&& int.TryParse(f[2], NumberStyles.Integer, Inv, out int cells) && long.TryParse(f[3], NumberStyles.Integer, Inv, out long last))
					{
						s_accounts[f[0]] = new Account { id = f[0], name = f[1], cells = cells, last = last };
					}
				}
			}
			foreach (string file in Directory.GetFiles(folder, "*.bin"))
			{
				// A map the index does not list (the index was lost): counted once, and let go again.
				string id = Path.GetFileNameWithoutExtension(file);
				if (s_accounts.ContainsKey(id) || !SafeKey(id))
				{
					continue;
				}
				Account account = new Account { id = id, last = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds() };
				s_accounts[id] = account;
				Ensure(account);
				account.bits = null;
				s_indexDirty = true;
			}
		}

		// Has the map of an account in memory: read from its file. A file that cannot be read is kept as .bad and the account starts again.
		private static void Ensure(Account account)
		{
			if (account.bits != null)
			{
				return;
			}
			string file = Path.Combine(s_dir, "explored", account.id + ".bin");
			byte[] bits = null;
			if (File.Exists(file))
			{
				try
				{
					bits = ReadBitset(file);
				}
				catch (Exception e)
				{
					try
					{
						File.Delete(file + ".bad");
						File.Move(file, file + ".bad");
					}
					catch (Exception)
					{
						// Left where it is; the next write replaces it.
					}
					ServersidePlugin.logger.LogWarning($"Web map: {Path.GetFileName(file)} cannot be read ({e.Message}); that account starts again, the file is kept as .bad");
				}
			}
			if (bits == null)
			{
				bits = new byte[BitsetBytes];
				account.cells = 0;
				s_indexDirty = true;
			}
			else
			{
				account.cells = Count(bits);
			}
			account.bits = bits;
		}

		// Marks the disc the game marks (Minimap.Explore); returns whether any cell was new.
		private static bool Explore(Account account, Vector3 at, int radius)
		{
			// A position that is not a number, or too far out to touch a cell, marks nothing (and the conversion below stays defined).
			float fx = at.x / PixelMetres + Grid / 2, fz = at.z / PixelMetres + Grid / 2;
			if (!(fx > -radius - 1 && fx < Grid + radius + 1 && fz > -radius - 1 && fz < Grid + radius + 1))
			{
				return false;
			}
			int px = Mathf.RoundToInt(fx), py = Mathf.RoundToInt(fz);
			bool changed = false;
			for (int y = py - radius; y <= py + radius; y++)
			{
				if (y < 0 || y >= Grid)
				{
					continue;
				}
				for (int x = px - radius; x <= px + radius; x++)
				{
					if (x < 0 || x >= Grid || (x - px) * (x - px) + (y - py) * (y - py) > radius * radius)
					{
						continue;
					}
					int bit = y * Grid + x;
					byte mask = (byte)(1 << (bit & 7));
					if ((account.bits[bit >> 3] & mask) == 0)
					{
						account.bits[bit >> 3] |= mask;
						account.cells++;
						changed = true;
					}
				}
			}
			return changed;
		}

		// Where the player's game says the player is: what the game's own map of players, and the zones sent to it, go by.
		private static Vector3 PositionOf(ZNetPeer peer)
		{
			return peer.GetRefPos();
		}

		private static void TrackPlayers()
		{
			int radius = Mathf.Max(1, (int)Mathf.Ceil(Configuration.webMapExploreRadius.Value / PixelMetres));
			s_pass++;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!peer.IsReady() || peer.m_characterID.IsNone())
				{
					continue;
				}
				string key = KeyOf(peer);
				if (key == null)
				{
					continue;
				}
				if (!s_accounts.TryGetValue(key, out Account account))
				{
					s_accounts[key] = account = new Account { id = key };
					s_indexDirty = true;
				}
				account.seen = s_pass;
				Ensure(account);
				string name = Clean(peer.m_playerName);
				if (account.name != name)
				{
					account.name = name;
					s_indexDirty = true;
				}
				account.last = Unix();
				s_indexDirty = true;
				if (Explore(account, PositionOf(peer), radius))
				{
					account.dirty = true;
				}
			}
			// A player who left lets go of the map once it is written.
			foreach (Account a in s_accounts.Values)
			{
				if (a.bits != null && a.seen != s_pass && !a.dirty)
				{
					a.bits = null;
				}
			}
		}

		private static string IndexText()
		{
			StringBuilder text = new StringBuilder("# account\tname\tcells explored\tlast seen online (unix s)\n");
			foreach (Account a in s_accounts.Values.OrderBy(a => a.id, StringComparer.Ordinal))
			{
				text.Append(a.id).Append('\t').Append(a.name).Append('\t').Append(a.cells.ToString(Inv)).Append('\t').Append(a.last.ToString(Inv)).Append('\n');
			}
			return text.ToString();
		}

		// Hands what changed to the writer. `last` is the write before the world saves or the server stops.
		private static void SaveExplored(bool last)
		{
			List<KeyValuePair<string, byte[]>> changed = null;
			foreach (Account a in s_accounts.Values)
			{
				if (a.dirty)
				{
					(changed ?? (changed = new List<KeyValuePair<string, byte[]>>())).Add(new KeyValuePair<string, byte[]>(a.id, (byte[])a.bits.Clone()));
					a.dirty = false;
				}
			}
			if (changed == null && !s_indexDirty)
			{
				return;
			}
			s_indexDirty = false;
			string folder = Path.Combine(s_dir, "explored"), index = IndexText();
			Enqueue(() =>
			{
				if (changed != null)
				{
					foreach (KeyValuePair<string, byte[]> job in changed)
					{
						try
						{
							WriteBitset(Path.Combine(folder, job.Key + ".bin"), job.Value);
						}
						catch (Exception e)
						{
							s_unwritten.Enqueue(job);
							s_messages.Enqueue($"could not write the map of an account ({e.Message}); tried again at the next write");
						}
					}
				}
				WriteText(Path.Combine(folder, "index.tsv"), index);
			});
			if (last)
			{
				Drain(10f);
				if (changed != null)
				{
					ServersidePlugin.logger.LogInfo($"Web map: written before the world is saved or the server stops: {changed.Count} explored map(s) that had changed");
				}
			}
		}

		private static void Number(StringBuilder text, float value, string format)
		{
			text.Append(value.ToString(format, Inv));
		}

		private static void Quote(StringBuilder text, string value)
		{
			text.Append('"');
			foreach (char c in value ?? "")
			{
				switch (c)
				{
					case '"': text.Append("\\\""); break;
					case '\\': text.Append("\\\\"); break;
					case '\n': text.Append("\\n"); break;
					case '\r': text.Append("\\r"); break;
					case '\t': text.Append("\\t"); break;
					default:
						if (c < 0x20 || c == '\u2028' || c == '\u2029')
						{
							text.Append("\\u").Append(((int)c).ToString("x4", Inv));
						}
						else
						{
							text.Append(c);
						}
						break;
				}
			}
			text.Append('"');
		}

		private static string LiveJson()
		{
			bool onlyPublic = Configuration.webMapOnlyPublicPositions.Value;
			StringBuilder text = new StringBuilder(4096);
			text.Append("{\"v\":1,\"t\":").Append(Unix().ToString(Inv)).Append(",\"seed\":").Append(ZNet.World.m_seed.ToString(Inv));
			text.Append(",\"time\":");
			Number(text, (float)ZNet.instance.GetTimeSeconds(), "0.0");
			int online = 0;
			StringBuilder players = new StringBuilder();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!peer.IsReady() || peer.m_characterID.IsNone())
				{
					continue;
				}
				online++;
				if (onlyPublic && !peer.m_publicRefPos)
				{
					continue;
				}
				Vector3 at = PositionOf(peer);
				players.Append(players.Length > 0 ? ",{\"n\":" : "{\"n\":");
				Quote(players, peer.m_playerName);
				players.Append(",\"x\":");
				Number(players, at.x, "0.0");
				players.Append(",\"y\":");
				Number(players, at.y, "0.0");
				players.Append(",\"z\":");
				Number(players, at.z, "0.0");
				players.Append('}');
			}
			text.Append(",\"online\":").Append(online.ToString(Inv)).Append(",\"players\":[").Append(players).Append("],\"portals\":[");
			string prefix = Configuration.portalLockPublicPrefix.Value;
			bool first = true;
			if (!string.IsNullOrEmpty(prefix))
			{
				foreach (ZDOID id in WorldIndex.Portals)
				{
					ZDO portal = ZDOMan.instance.GetZDO(id);
					string tag = portal != null ? portal.GetString(ZDOVars.s_tag, "") : "";
					if (string.IsNullOrEmpty(tag) || !tag.StartsWith(prefix, StringComparison.Ordinal))
					{
						continue;
					}
					Vector3 at = portal.GetPosition();
					text.Append(first ? "{\"t\":" : ",{\"t\":");
					first = false;
					Quote(text, tag);
					text.Append(",\"x\":").Append(Mathf.RoundToInt(at.x).ToString(Inv)).Append(",\"z\":").Append(Mathf.RoundToInt(at.z).ToString(Inv)).Append('}');
				}
			}
			text.Append("],\"bases\":");
			if (s_bases == null)
			{
				text.Append("null");
			}
			else
			{
				int min = Configuration.webMapBaseMinPieces.Value;
				text.Append("{\"cell\":").Append(Configuration.webMapBaseCell.Value.ToString(Inv)).Append(",\"min\":").Append(min.ToString(Inv)).Append(",\"cells\":[");
				first = true;
				foreach (KeyValuePair<long, int> cell in s_bases.Where(c => c.Value >= min).OrderBy(c => c.Key))
				{
					text.Append(first ? "{\"i\":" : ",{\"i\":").Append(CellI(cell.Key).ToString(Inv)).Append(",\"j\":").Append(CellJ(cell.Key).ToString(Inv)).Append(",\"n\":").Append(cell.Value.ToString(Inv)).Append('}');
					first = false;
				}
				text.Append("]}");
			}
			text.Append('}');
			return text.ToString();
		}

		private static long CellKey(int i, int j)
		{
			return unchecked(((long)i << 32) | (uint)j);
		}

		private static int CellI(long key)
		{
			return (int)(key >> 32);
		}

		private static int CellJ(long key)
		{
			return unchecked((int)(key & 0xFFFFFFFFL));
		}

		// The pieces that have a builder, counted per square, a slice of the world index per frame.
		private static void CountBases(float now)
		{
			if (s_scanList == null)
			{
				if (now < s_nextBases || !WorldIndex.Ready())
				{
					return;
				}
				s_scanList = WorldIndex.Built;
				s_scanPos = 0;
				s_scanCells = new Dictionary<long, int>();
			}
			float cell = Mathf.Max(1, Configuration.webMapBaseCell.Value);
			int end = Math.Min(s_scanList.Count, s_scanPos + BasesPerFrame);
			s_scanWatch.Restart();
			for (; s_scanPos < end; s_scanPos++)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(s_scanList[s_scanPos]);
				if (zdo == null || zdo.GetLong(ZDOVars.s_creator, 0L) == 0L)
				{
					continue;
				}
				Vector3 at = zdo.GetPosition();
				long key = CellKey(Mathf.FloorToInt(at.x / cell), Mathf.FloorToInt(at.z / cell));
				s_scanCells.TryGetValue(key, out int n);
				s_scanCells[key] = n + 1;
			}
			double ms = s_scanWatch.Elapsed.TotalMilliseconds;
			s_scanTotalMs += ms;
			s_scanWorstMs = Math.Max(s_scanWorstMs, ms);
			if (s_scanPos >= s_scanList.Count)
			{
				s_bases = s_scanCells;
				s_scanCells = null;
				s_nextBases = now + BasesEvery;
				if (!s_scanLogged)
				{
					s_scanLogged = true;
					int min = Configuration.webMapBaseMinPieces.Value;
					ServersidePlugin.logger.LogInfo($"Web map: counted {s_scanList.Count} index entries; {s_bases.Values.Sum()} pieces have a builder, in {s_bases.Count} square(s) of {cell:0} m, "
						+ $"{s_bases.Count(c => c.Value >= min)} of them with {min} or more; {s_scanTotalMs:0} ms in all, the worst frame {s_scanWorstMs:0.0} ms.");
				}
				s_scanList = null;
				s_scanTotalMs = 0;
				s_scanWorstMs = 0;
			}
		}

		private static string PlacesJson()
		{
			List<ZoneSystem.LocationInstance> all = ZoneSystem.instance.m_locationInstances.Values.Where(li => li.m_location != null).ToList();
			if (all.Count == 0)
			{
				return null;
			}
			StringBuilder text = new StringBuilder(all.Count * 70 + 512);
			text.Append("{\"v\":1,\"t\":").Append(Unix().ToString(Inv)).Append(",\"seed\":").Append(ZNet.World.m_seed.ToString(Inv)).Append(",\"defeated\":[");
			bool first = true;
			foreach (string key in ZoneSystem.instance.GetGlobalKeys().Where(k => k.StartsWith("defeated_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
			{
				text.Append(first ? "" : ",");
				first = false;
				Quote(text, key);
			}
			text.Append("],\"places\":[");
			first = true;
			foreach (ZoneSystem.LocationInstance li in all)
			{
				text.Append(first ? "{\"n\":" : ",{\"n\":");
				first = false;
				Quote(text, li.m_location.m_prefabName);
				text.Append(",\"x\":").Append(Mathf.RoundToInt(li.m_position.x).ToString(Inv)).Append(",\"z\":").Append(Mathf.RoundToInt(li.m_position.z).ToString(Inv));
				text.Append(",\"p\":").Append(li.m_placed ? '1' : '0').Append(",\"u\":").Append(li.m_location.m_unique ? '1' : '0');
				text.Append(",\"i\":").Append(li.m_location.m_iconPlaced ? '1' : '0').Append(",\"a\":").Append(li.m_location.m_iconAlways ? '1' : '0').Append('}');
			}
			text.Append("]}");
			return text.ToString();
		}

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.webMapEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null || !ZoneSystem.instance || !Resolve())
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			Load();
			Report();
			if (!s_announced)
			{
				s_announced = true;
				float radius = Configuration.webMapExploreRadius.Value;
				ServersidePlugin.logger.LogInfo($"Web map: writing {s_publicDir}{Path.DirectorySeparatorChar}live.json every {Configuration.webMapInterval.Value} s"
					+ (Configuration.webMapPlaces.Value ? ", places.json" : "") + $" and the explored maps ({s_accounts.Count} account(s) so far) in {s_dir}; "
					+ $"a player explores a disc of {Mathf.Max(1, Mathf.CeilToInt(radius / PixelMetres))} cells ({radius:0} m), "
					+ (Configuration.webMapOnlyPublicPositions.Value ? "only players who show their position are listed" : "every player online is listed") + ".");
			}
			if (now >= s_nextExplore)
			{
				s_nextExplore = now + ExploreEvery;
				TrackPlayers();
			}
			CountBases(now);
			if (now >= s_nextLive)
			{
				s_nextLive = now + Configuration.webMapInterval.Value;
				string live = LiveJson(), path = Path.Combine(s_publicDir, "live.json");
				Enqueue(() => WriteText(path, live));
			}
			if (Configuration.webMapPlaces.Value && now >= s_nextPlaces)
			{
				string places = PlacesJson();
				s_nextPlaces = now + (places == null ? 30f : PlacesEvery);
				if (places != null)
				{
					string path = Path.Combine(s_dir, "places.json");
					Enqueue(() => WriteText(path, places));
				}
			}
			if (now >= s_nextSave)
			{
				s_nextSave = now + SaveEvery;
				SaveExplored(false);
			}
		}

		private static void Final()
		{
			if (!Configuration.webMapEnabled.Value || s_dir == null || !s_loaded)
			{
				return;
			}
			try
			{
				Report();
				SaveExplored(true);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Web map: the last write failed: {e.Message}");
			}
		}

		[HarmonyPatch(typeof(ZNet), "SaveWorld")]
		public static class ZNet_SaveWorld_Patch
		{
			static void Postfix()
			{
				Final();
			}
		}

		[HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
		public static class ZNet_Shutdown_Patch
		{
			static void Prefix()
			{
				Final();
			}
		}
	}
}
