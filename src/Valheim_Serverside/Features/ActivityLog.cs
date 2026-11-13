using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		What the players do, one JSON line at a time, in <world>.activity/<yyyy-MM-dd>.jsonl (UTC days, kept for Days days; docs/ACTIVITYLOG.md has every line).

		A reader only: nothing is dropped, nothing is changed. It sees what the server sees, which is a good deal, but not everything: a player's game
		simulates the creatures around it, so its hits on them never pass the server. What the server does get is the write of the creature's health into
		its ZDO (in ZDOMan.RPC_ZDOData, from the game that owns the creature), and that is the "mobhp" line: every drop of a creature's health, and whose
		game wrote it.

		Off, nothing of this runs: no patch is applied (they are the nested classes, applied only for an enabled feature), no tick does anything, no folder is made.
	*/
	public class ActivityLog : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.activityLogEnabled.Value;
		}

		private const float PollSeconds = 1f;
		private const float JumpMetres = 40f, AfkMetres = 0.5f, AfkSeconds = 60f, AggregateSeconds = 60f;
		private const float ForgetSeconds = 300f, NearMetres = 40f, RagdollMetres = 6f, RagdollGrace = 2f;
		private const int MaxTracked = 5000, MaxBuffered = 200000;

		private static readonly int s_rpcDamage = "RPC_Damage".GetStableHashCode();
		private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
		private static readonly Regex s_fileName = new Regex(@"^\d{4}-\d{2}-\d{2}\.jsonl$", RegexOptions.Compiled);
		private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

		private static int s_errors;
		private static bool s_started, s_quitHooked;
		private static float s_next;
		private static string s_dir, s_today;

		// the line being built, and the lines waiting for the next flush (each with the UTC day of its own t)
		private static readonly StringBuilder s_b = new StringBuilder(512);
		private static string s_ev;
		private static readonly List<string> s_buf = new List<string>();
		private static readonly List<string> s_bufDay = new List<string>();
		private static int s_dropped;
		private static string s_capDay;
		private static long s_capBytes;
		private static bool s_capHit;
		private static float s_nextWarn;

		private static bool On()
		{
			return Configuration.activityLogEnabled.Value && ZNet.instance != null && ZNet.instance.IsServer();
		}

		private static void Fault(Exception e)
		{
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Activity log: {e}");
			}
		}

		// ---- people -------------------------------------------------------------------------------------------------------

		// The account id as the guard log writes it (AntiCheat.Who: <name> (<id>, character <n>)).
		private static string AccountOf(ZNetPeer peer)
		{
			return peer != null && peer.m_socket != null ? peer.m_socket.GetHostName() : "?";
		}

		// The peer's own character: the id carries the session of the game that plays it (what another game made says nothing about this one).
		private static bool Own(ZNetPeer peer)
		{
			return peer != null && !peer.m_characterID.IsNone() && peer.m_characterID.UserID == peer.m_uid;
		}

		// The character number the guard log writes after "character" (AntiCheat.PlayerId); 0 when there is none yet.
		private static long PlayerId(ZNetPeer peer)
		{
			if (!Own(peer))
			{
				return 0L;
			}
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
		}

		private static ZNetPeer PeerOfCharacter(ZDOID id)
		{
			if (id.IsNone())
			{
				return null;
			}
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer != null && Own(peer) && peer.m_characterID == id)
				{
					return peer;
				}
			}
			return null;
		}

		private static string Clean(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "?";
			}
			char[] c = name.ToCharArray();
			for (int i = 0; i < c.Length; i++)
			{
				if (char.IsControl(c[i]) || c[i] == '\u2028' || c[i] == '\u2029')
				{
					c[i] = ' ';
				}
			}
			return new string(c);
		}

		// ---- what is written ----------------------------------------------------------------------------------------------

		private static StringBuilder Begin(string ev)
		{
			s_ev = ev;
			s_b.Clear();
			return s_b.Append("{\"t\":\"").Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv)).Append("\",\"ev\":\"").Append(ev).Append('"');
		}

		private static string Num(float value)
		{
			return float.IsNaN(value) || float.IsInfinity(value) ? "null" : value.ToString("0.0", Inv);
		}

		private static string Vec(Vector3 v)
		{
			return "[" + Num(v.x) + "," + Num(v.y) + "," + Num(v.z) + "]";
		}

		private static StringBuilder Str(StringBuilder b, string key, string value)
		{
			return b.Append(",\"").Append(key).Append("\":").Append(Json(value));
		}

		private static StringBuilder Raw(StringBuilder b, string key, string json)
		{
			return b.Append(",\"").Append(key).Append("\":").Append(json);
		}

		private static StringBuilder Flt(StringBuilder b, string key, float value)
		{
			return b.Append(",\"").Append(key).Append("\":").Append(Num(value));
		}

		private static StringBuilder Int(StringBuilder b, string key, long value)
		{
			return b.Append(",\"").Append(key).Append("\":").Append(value.ToString(Inv));
		}

		private static StringBuilder Bool(StringBuilder b, string key, bool value)
		{
			return b.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");
		}

		// The player fields of a line: p, acc, pid.
		private static StringBuilder Who(StringBuilder b, ZNetPeer peer, long pid)
		{
			Str(b, "p", Clean(peer.m_playerName));
			Str(b, "acc", AccountOf(peer));
			return pid != 0L ? Int(b, "pid", pid) : Raw(b, "pid", "null");
		}

		// The line is complete: queued for the next flush, unless the day's file is over its size.
		private static void End()
		{
			string ev = s_ev;
			string line = s_b.Append('}').ToString();
			Queue(ev, line);
		}

		private static bool AllowedAfterCap(string ev)
		{
			return ev == "start" || ev == "join" || ev == "leave" || ev == "spawn" || ev == "died" || ev == "mobend" || ev == "guard" || ev == "cap";
		}

		private static void Queue(string ev, string line)
		{
			string day = line.Substring(6, 10);
			if (day != s_capDay)
			{
				// a new day's file: its size so far is what is on disk
				s_capDay = day;
				s_capBytes = 0L;
				s_capHit = false;
				try
				{
					FileInfo info = new FileInfo(DayFile(day));
					if (info.Exists)
					{
						s_capBytes = info.Length;
					}
				}
				catch (Exception)
				{
				}
			}
			if (s_capHit && !AllowedAfterCap(ev))
			{
				return;
			}
			if (s_buf.Count >= MaxBuffered)
			{
				s_dropped++;
				return;
			}
			s_buf.Add(line);
			s_bufDay.Add(day);
			s_capBytes += s_utf8.GetByteCount(line) + 1;
			double max = Configuration.activityLogMaxMegabytes.Value * 1048576.0;
			if (!s_capHit && max > 0.0 && s_capBytes >= max)
			{
				s_capHit = true;
				Begin("cap");
				Raw(s_b, "mb", Configuration.activityLogMaxMegabytes.Value.ToString("0.###", Inv));
				Int(s_b, "bytes", s_capBytes);
				Str(s_b, "day", day);
				End();
			}
		}

		private static string DayFile(string day)
		{
			return Path.Combine(s_dir, day + ".jsonl");
		}

		// Appended once a second, at a save and at shutdown: open, append, close, so nothing keeps a file open.
		private static void Flush()
		{
			if (s_buf.Count == 0 || s_dir == null)
			{
				return;
			}
			try
			{
				Directory.CreateDirectory(s_dir);
				StringBuilder text = new StringBuilder();
				string day = s_bufDay[0];
				for (int i = 0; i < s_buf.Count; i++)
				{
					if (s_bufDay[i] != day)
					{
						File.AppendAllText(DayFile(day), text.ToString(), s_utf8);
						text.Clear();
						day = s_bufDay[i];
					}
					text.Append(s_buf[i]).Append('\n');
				}
				File.AppendAllText(DayFile(day), text.ToString(), s_utf8);
				s_buf.Clear();
				s_bufDay.Clear();
			}
			catch (Exception e)
			{
				// the lines stay for the next try; a folder that cannot be written does not fill the memory
				if (Time.realtimeSinceStartup >= s_nextWarn)
				{
					s_nextWarn = Time.realtimeSinceStartup + 300f;
					ServersidePlugin.logger.LogWarning($"Activity log: could not write {s_dir}: {e.Message}");
				}
			}
		}

		// A JSON string (or null): quotes, backslashes and control characters escaped, so a name cannot break the line; a lone surrogate is written as an escape.
		private static string Json(string text)
		{
			if (text == null)
			{
				return "null";
			}
			StringBuilder b = new StringBuilder(text.Length + 2);
			b.Append('"');
			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				switch (ch)
				{
					case '"': b.Append("\\\""); break;
					case '\\': b.Append("\\\\"); break;
					case '\n': b.Append("\\n"); break;
					case '\r': b.Append("\\r"); break;
					case '\t': b.Append("\\t"); break;
					default:
						bool pair = char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
						if (pair)
						{
							b.Append(ch).Append(text[++i]);
						}
						else if (ch < 0x20 || ch == 0x7f || ch == '\u2028' || ch == '\u2029' || char.IsSurrogate(ch) || char.IsControl(ch))
						{
							b.Append("\\u").Append(((int)ch).ToString("x4"));
						}
						else
						{
							b.Append(ch);
						}
						break;
				}
			}
			return b.Append('"').ToString();
		}

		// ---- names --------------------------------------------------------------------------------------------------------

		private sealed class CreatureInfo
		{
			public string name;
			public float health;
			public bool boss;
		}

		// Built once from the game's prefabs, when the scene is there: creatures (a Character that is not a Player), pieces (a Piece that is no ItemDrop
		// and no Character), item drops, ragdolls. Until then the hooks below do nothing.
		private static Dictionary<int, CreatureInfo> s_creatures;
		private static HashSet<int> s_pieces, s_drops, s_ragdolls;
		private static readonly Dictionary<int, string> s_prefabJson = new Dictionary<int, string>();
		private static readonly Dictionary<int, string> s_itemJson = new Dictionary<int, string>();

		private static void BuildSets()
		{
			Dictionary<int, CreatureInfo> creatures = new Dictionary<int, CreatureInfo>();
			HashSet<int> pieces = new HashSet<int>(), drops = new HashSet<int>(), ragdolls = new HashSet<int>();
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (prefab == null)
				{
					continue;
				}
				int hash = prefab.name.GetStableHashCode();
				Character character = prefab.GetComponent<Character>();
				if (character != null && !(character is Player))
				{
					creatures[hash] = new CreatureInfo { name = prefab.name, health = character.m_health, boss = character.m_boss };
				}
				else if (character == null && prefab.GetComponent<ItemDrop>() != null)
				{
					drops.Add(hash);
				}
				else if (character == null && prefab.GetComponent<Piece>() != null)
				{
					pieces.Add(hash);
				}
				if (prefab.name.EndsWith("_ragdoll", StringComparison.Ordinal))
				{
					ragdolls.Add(hash);
				}
			}
			s_pieces = pieces;
			s_drops = drops;
			s_ragdolls = ragdolls;
			s_creatures = creatures;
		}

		// The prefab's name as a JSON value (the hash as a number when the game has no such prefab).
		private static string PrefabJson(int hash)
		{
			if (!s_prefabJson.TryGetValue(hash, out string json))
			{
				GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
				json = prefab != null ? Json(prefab.name) : hash.ToString(Inv);
				if (prefab != null || ZNetScene.instance != null)
				{
					s_prefabJson[hash] = json;
				}
			}
			return json;
		}

		// An item's name from the object database as a JSON value: null for none, the hash as a number for an unknown one.
		private static string ItemJson(int hash)
		{
			if (hash == 0)
			{
				return "null";
			}
			if (!s_itemJson.TryGetValue(hash, out string json))
			{
				if (ObjectDB.instance == null)
				{
					return hash.ToString(Inv);
				}
				GameObject item = ObjectDB.instance.GetItemPrefab(hash);
				s_itemJson[hash] = json = item != null ? Json(item.name) : hash.ToString(Inv);
			}
			return json;
		}

		// ---- the sessions -------------------------------------------------------------------------------------------------

		private sealed class Agg
		{
			public int n, own, items;
			public Vector3 pos;
			public float since;
		}

		private sealed class Session
		{
			public ZNetPeer peer;
			public float joined;
			public long pid;
			public ZDOID charId = ZDOID.None;
			public bool sampled, dead, equipKnown;
			public Vector3 lastPos;
			public float lastT, hp = float.NaN;
			public float winT0, winDist, winMax;
			public readonly int[] equip = new int[7];
			// the last written pos line, for the AFK rule
			public bool wrote;
			public Vector3 wPos;
			public float wHp, wSt, wAt;
			public int wR, wL;
			public readonly Dictionary<int, Agg> build = new Dictionary<int, Agg>(), unbuild = new Dictionary<int, Agg>(), pickup = new Dictionary<int, Agg>();
		}

		private static readonly Dictionary<ZNetPeer, Session> s_sessions = new Dictionary<ZNetPeer, Session>();
		private static readonly List<ZNetPeer> s_gone = new List<ZNetPeer>();
		private static readonly HashSet<ZNetPeer> s_present = new HashSet<ZNetPeer>();

		private static readonly int[] s_equipVars =
		{
			ZDOVars.s_rightItem, ZDOVars.s_leftItem, ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_helmetItem, ZDOVars.s_shoulderItem, ZDOVars.s_utilityItem
		};
		private static readonly string[] s_equipKeys = { "r", "l", "chest", "legs", "helmet", "shoulder", "utility" };

		private static Session SessionOf(ZNetPeer peer)
		{
			return peer != null && s_sessions.TryGetValue(peer, out Session s) ? s : null;
		}

		private static void Join(ZNetPeer peer)
		{
			Session s = new Session { peer = peer, joined = Time.realtimeSinceStartup };
			s_sessions[peer] = s;
			Who(Begin("join"), peer, 0L);
			End();
		}

		private static void Leave(Session s)
		{
			if (!s_sessions.Remove(s.peer))
			{
				return;
			}
			FlushAggregates(s, true);
			Who(Begin("leave"), s.peer, s.pid);
			Flt(s_b, "secs", Time.realtimeSinceStartup - s.joined);
			End();
		}

		private static void Aggregate(Dictionary<int, Agg> into, int prefab, Vector3 pos, int items, bool own)
		{
			if (!into.TryGetValue(prefab, out Agg a))
			{
				into[prefab] = a = new Agg { pos = pos, since = Time.time };
			}
			a.n++;
			a.items += items;
			if (own)
			{
				a.own++;
			}
		}

		private static void FlushAggregates(Session s, bool all)
		{
			float now = Time.time;
			WriteAggregates(s, s.build, "build", all, now);
			WriteAggregates(s, s.unbuild, "unbuild", all, now);
			WriteAggregates(s, s.pickup, "pickup", all, now);
		}

		private static readonly List<int> s_done = new List<int>();

		private static void WriteAggregates(Session s, Dictionary<int, Agg> from, string ev, bool all, float now)
		{
			if (from.Count == 0)
			{
				return;
			}
			s_done.Clear();
			foreach (KeyValuePair<int, Agg> kv in from)
			{
				if (all || now - kv.Value.since >= AggregateSeconds)
				{
					s_done.Add(kv.Key);
				}
			}
			foreach (int prefab in s_done)
			{
				Agg a = from[prefab];
				from.Remove(prefab);
				Who(Begin(ev), s.peer, s.pid);
				Raw(s_b, "prefab", PrefabJson(prefab));
				Int(s_b, "n", a.n);
				if (ev == "unbuild")
				{
					Int(s_b, "own", a.own);
				}
				else if (ev == "pickup")
				{
					Int(s_b, "items", a.items);
				}
				if (ev != "unbuild")
				{
					Raw(s_b, "pos", Vec(a.pos));
				}
				End();
			}
		}

		// ---- the tick -----------------------------------------------------------------------------------------------------

		// Called from ServersidePlugin.Update.
		public static void Tick()
		{
			if (!Configuration.activityLogEnabled.Value || Time.time < s_next)
			{
				return;
			}
			if (!Ready())
			{
				return;
			}
			s_next = Time.time + PollSeconds;
			try
			{
				Poll();
			}
			catch (Exception e)
			{
				Fault(e);
			}
			Flush();
		}

		// The feature starts when there is a server with a world: the folder is named, old days are deleted, "start" is the first line.
		private static bool Ready()
		{
			if (s_started)
			{
				return true;
			}
			if (!On() || ZNet.World == null || ZDOMan.instance == null)
			{
				return false;
			}
			s_started = true;
			s_dir = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".activity");
			s_today = DateTime.UtcNow.ToString("yyyy-MM-dd", Inv);
			if (!s_quitHooked)
			{
				s_quitHooked = true;
				Application.quitting += Flush;
			}
			try
			{
				Retention();
			}
			catch (Exception e)
			{
				Fault(e);
			}
			Begin("start");
			Str(s_b, "version", ServersidePlugin.PluginVersion);
			Int(s_b, "days", Configuration.activityLogDays.Value);
			Raw(s_b, "posSecs", Configuration.activityLogPositionSeconds.Value.ToString("0.###", Inv));
			Raw(s_b, "capMB", Configuration.activityLogMaxMegabytes.Value.ToString("0.###", Inv));
			End();
			return true;
		}

		// Files named exactly yyyy-MM-dd.jsonl whose day is older than today - (Days - 1) are deleted; nothing else in the folder is touched.
		private static void Retention()
		{
			int days = Configuration.activityLogDays.Value;
			if (days <= 0 || !Directory.Exists(s_dir))
			{
				return;
			}
			DateTime oldest = DateTime.UtcNow.Date.AddDays(-(days - 1));
			foreach (string file in Directory.GetFiles(s_dir))
			{
				string name = Path.GetFileName(file);
				if (!s_fileName.IsMatch(name))
				{
					continue;
				}
				if (DateTime.TryParseExact(name.Substring(0, 10), "yyyy-MM-dd", Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime day) && day.Date < oldest)
				{
					try
					{
						File.Delete(file);
					}
					catch (Exception e)
					{
						ServersidePlugin.logger.LogWarning($"Activity log: could not delete {file}: {e.Message}");
					}
				}
			}
		}

		private static void Poll()
		{
			float now = Time.time;
			string today = DateTime.UtcNow.ToString("yyyy-MM-dd", Inv);
			if (today != s_today)
			{
				s_today = today;
				Retention();
			}
			if (s_creatures == null && ZNetScene.instance != null && ZNetScene.instance.m_prefabs != null && ZNetScene.instance.m_prefabs.Count > 0)
			{
				BuildSets();
			}

			// who is there: a peer with a name appears, or is gone (ZNet.Disconnect has usually written the leave already)
			s_present.Clear();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || string.IsNullOrEmpty(peer.m_playerName))
				{
					continue;
				}
				s_present.Add(peer);
				if (!s_sessions.ContainsKey(peer))
				{
					Join(peer);
				}
			}
			s_gone.Clear();
			foreach (ZNetPeer peer in s_sessions.Keys)
			{
				if (!s_present.Contains(peer))
				{
					s_gone.Add(peer);
				}
			}
			foreach (ZNetPeer peer in s_gone)
			{
				Leave(s_sessions[peer]);
			}

			foreach (Session s in s_sessions.Values)
			{
				try
				{
					Sample(s, now);
					FlushAggregates(s, false);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
			Mobs(now);
		}

		// ---- the characters -----------------------------------------------------------------------------------------------

		private static float Stamina(ZDO z)
		{
			return z.GetFloat(ZDOVars.s_stamina, float.NaN);
		}

		// The character's health: stored, or (the game stores none at full health) its maximum, or unknown.
		private static float HealthOf(ZDO z)
		{
			float h = z.GetFloat(ZDOVars.s_health, -1f);
			return h >= 0f ? h : z.GetFloat(ZDOVars.s_maxHealth, float.NaN);
		}

		private static bool Same(float a, float b)
		{
			return (float.IsNaN(a) && float.IsNaN(b)) || Mathf.Abs(a - b) < 0.05f;
		}

		private static void Sample(Session s, float now)
		{
			ZNetPeer peer = s.peer;
			if (!Own(peer))
			{
				s.charId = ZDOID.None;
				return;
			}
			ZDO z = ZDOMan.instance.GetZDO(peer.m_characterID);
			if (z == null)
			{
				return;
			}
			Vector3 pos = z.GetPosition();
			s.pid = z.GetLong(ZDOVars.s_playerID, s.pid);
			bool dead = z.GetBool(ZDOVars.s_dead, false);
			float hp = HealthOf(z);
			if (peer.m_characterID != s.charId)
			{
				// the first spawn and every respawn
				s.charId = peer.m_characterID;
				s.sampled = false;
				s.equipKnown = false;
				s.dead = dead;
				s.wrote = false;
				s.winT0 = now;
				s.winDist = 0f;
				s.winMax = 0f;
				Who(Begin("spawn"), peer, s.pid);
				Raw(s_b, "pos", Vec(pos));
				End();
			}

			// equipment: all seven when any changes (the first sample after a spawn is only remembered)
			bool changed = false;
			for (int i = 0; i < 7; i++)
			{
				int item = z.GetInt(s_equipVars[i], 0);
				if (item != s.equip[i])
				{
					changed = true;
					s.equip[i] = item;
				}
			}
			if (changed && s.equipKnown)
			{
				Who(Begin("equip"), peer, s.pid);
				for (int i = 0; i < 7; i++)
				{
					Raw(s_b, s_equipKeys[i], ItemJson(s.equip[i]));
				}
				End();
			}
			s.equipKnown = true;

			if (dead && !s.dead)
			{
				Who(Begin("died"), peer, s.pid);
				Raw(s_b, "pos", Vec(pos));
				Flt(s_b, "hp", s.hp);
				End();
			}

			if (s.sampled)
			{
				float dt = Mathf.Max(0.001f, now - s.lastT);
				float dist = Vector3.Distance(pos, s.lastPos);
				if (dist > JumpMetres && !dead && !s.dead)
				{
					// a portal, a dungeon door, a teleport: not walking
					Who(Begin("jump"), peer, s.pid);
					Raw(s_b, "from", Vec(s.lastPos));
					Raw(s_b, "to", Vec(pos));
					Flt(s_b, "dist", dist);
					Flt(s_b, "dy", pos.y - s.lastPos.y);
					Flt(s_b, "secs", dt);
					End();
				}
				else
				{
					float flat = new Vector2(pos.x - s.lastPos.x, pos.z - s.lastPos.z).magnitude;
					s.winDist += flat;
					s.winMax = Mathf.Max(s.winMax, flat / dt);
				}
			}
			s.sampled = true;
			s.lastPos = pos;
			s.lastT = now;
			s.dead = dead;
			if (!float.IsNaN(hp))
			{
				s.hp = hp;
			}

			float window = Configuration.activityLogPositionSeconds.Value;
			if (now - s.winT0 >= window - 0.25f)
			{
				float secs = Mathf.Max(0.001f, now - s.winT0);
				float st = Stamina(z);
				bool still = s.wrote && Vector3.Distance(pos, s.wPos) < AfkMetres && Same(hp, s.wHp) && Same(st, s.wSt) && s.equip[0] == s.wR && s.equip[1] == s.wL;
				if (!still || now - s.wAt >= AfkSeconds)
				{
					Who(Begin("pos"), peer, s.pid);
					Raw(s_b, "pos", Vec(pos));
					Flt(s_b, "v", s.winDist / secs);
					Flt(s_b, "vmax", s.winMax);
					Flt(s_b, "hp", hp);
					Flt(s_b, "hpmax", z.GetFloat(ZDOVars.s_maxHealth, float.NaN));
					Flt(s_b, "st", st);
					Flt(s_b, "eitr", z.GetFloat(ZDOVars.s_eitr, float.NaN));
					Bool(s_b, "dead", dead);
					Raw(s_b, "r", ItemJson(s.equip[0]));
					Raw(s_b, "l", ItemJson(s.equip[1]));
					End();
					s.wrote = true;
					s.wPos = pos;
					s.wHp = hp;
					s.wSt = st;
					s.wR = s.equip[0];
					s.wL = s.equip[1];
					s.wAt = now;
				}
				s.winT0 = now;
				s.winDist = 0f;
				s.winMax = 0f;
			}
		}

		// ---- hits ---------------------------------------------------------------------------------------------------------

		private static float Total(HitData hit)
		{
			try
			{
				return hit.GetTotalDamage();
			}
			catch (Exception)
			{
				return hit.m_damage.GetTotalDamage();
			}
		}

		private static void AddType(StringBuilder b, string key, float value)
		{
			if (value != 0f)
			{
				b.Append(b.Length > 1 ? "," : "").Append('"').Append(key).Append("\":").Append(Num(value));
			}
		}

		private static string Types(HitData.DamageTypes d)
		{
			StringBuilder b = new StringBuilder("{");
			AddType(b, "blunt", d.m_blunt);
			AddType(b, "slash", d.m_slash);
			AddType(b, "pierce", d.m_pierce);
			AddType(b, "chop", d.m_chop);
			AddType(b, "pickaxe", d.m_pickaxe);
			AddType(b, "fire", d.m_fire);
			AddType(b, "frost", d.m_frost);
			AddType(b, "lightning", d.m_lightning);
			AddType(b, "poison", d.m_poison);
			AddType(b, "spirit", d.m_spirit);
			return b.Append('}').ToString();
		}

		// What the owner of a ZDO is called: the server, a player, or "?".
		private static string OwnerName(ZDO zdo)
		{
			if (zdo == null)
			{
				return "?";
			}
			long owner = zdo.GetOwner();
			if (owner == ZDOMan.GetSessionID())
			{
				return "server";
			}
			ZNetPeer peer = ZNet.instance.GetPeer(owner);
			return peer != null ? Clean(peer.m_playerName) : "?";
		}

		// A routed RPC_Damage from a player's game whose attacker is its own character: the hit the server sees.
		private static void Hit(ZNetPeer sender, ZDOID target, HitData hit)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(target);
			ZDO me = ZDOMan.instance.GetZDO(sender.m_characterID);
			ZNetPeer victim = PeerOfCharacter(target);
			Who(Begin("hit"), sender, PlayerId(sender));
			Raw(s_b, "target", zdo != null ? PrefabJson(zdo.GetPrefab()) : "null");
			int level = zdo != null ? zdo.GetInt(ZDOVars.s_level, -1) : -1;
			Raw(s_b, "tlevel", level >= 0 ? level.ToString(Inv) : "null");
			if (victim != null)
			{
				Str(s_b, "tplayer", Clean(victim.m_playerName));
			}
			Str(s_b, "towner", OwnerName(zdo));
			Str(s_b, "skill", hit.m_skill.ToString());
			Flt(s_b, "slvl", hit.m_skillLevel);
			Int(s_b, "ilvl", hit.m_itemLevel);
			Flt(s_b, "dmg", Total(hit));
			Raw(s_b, "types", Types(hit.m_damage));
			Bool(s_b, "ranged", hit.m_ranged);
			Raw(s_b, "dist", me != null ? Num(Vector3.Distance(me.GetPosition(), hit.m_point)) : "null");
			Raw(s_b, "st", me != null ? Num(Stamina(me)) : "null");
			Raw(s_b, "r", me != null ? ItemJson(me.GetInt(ZDOVars.s_rightItem, 0)) : "null");
			if (!hit.m_backstabBonus.Equals(1f))
			{
				Flt(s_b, "backstab", hit.m_backstabBonus);
			}
			End();
		}

		// A hit on a player's character from something that is not a player: by = the creature's prefab, or the hit's type.
		private static void Hurt(ZNetPeer victim, HitData hit, string by, int level, string via)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(victim.m_characterID);
			Who(Begin("hurt"), victim, PlayerId(victim));
			Str(s_b, "by", by);
			Raw(s_b, "blvl", level >= 0 ? level.ToString(Inv) : "null");
			Flt(s_b, "dmg", Total(hit));
			Raw(s_b, "types", Types(hit.m_damage));
			Flt(s_b, "hp", zdo != null ? HealthOf(zdo) : float.NaN);
			if (via != null)
			{
				Str(s_b, "via", via);
			}
			End();
		}

		// The server's own Character.Damage on a player's character instance (creatures' attacks, projectiles, falling trees, ballistas), before it is applied.
		[HarmonyPatch(typeof(Character), "Damage")]
		public static class Character_Damage_Patch
		{
			static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null || !(__instance is Player) || !On())
				{
					return;
				}
				try
				{
					Struck((Player)__instance, hit);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		private static void Struck(Player player, HitData hit)
		{
			ZNetView view = player.GetComponent<ZNetView>();
			ZDO zdo = view != null ? view.GetZDO() : null;
			if (zdo == null || zdo.GetOwner() == ZDOMan.GetSessionID())
			{
				return;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(zdo.GetOwner());
			if (peer == null || !Own(peer) || peer.m_characterID != zdo.m_uid || SessionOf(peer) == null)
			{
				return;
			}
			Character attacker = hit.HaveAttacker() ? hit.GetAttacker() : null;
			if (attacker != null)
			{
				if (hit.m_attacker == zdo.m_uid || attacker.IsPlayer())
				{
					return;
				}
				Hurt(peer, hit, global::Utils.GetPrefabName(attacker.gameObject), attacker.GetLevel(), null);
			}
			else
			{
				Hurt(peer, hit, hit.m_hitType.ToString(), -1, null);
			}
		}

		// A reader of the routed RPCs, like AntiCheat's and DeathLog's: compares the method by hash before it reads anything more, reads the package and puts
		// the position back, and never drops anything.
		[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
		public static class ZRoutedRpc_RPC_RoutedRPC_Patch
		{
			[HarmonyPriority(Priority.Last)]
			static void Prefix(ZRpc rpc, ZPackage pkg, bool __runOriginal)
			{
				// a hit another prefix dropped (the anti-cheat's Damage rule) never reaches its target
				if (pkg == null || !__runOriginal || !Configuration.activityLogEnabled.Value || !s_started)
				{
					return;
				}
				int start = pkg.GetPos();
				try
				{
					pkg.ReadLong();
					pkg.ReadLong();
					pkg.ReadLong();
					ZDOID target = pkg.ReadZDOID();
					int method = pkg.ReadInt();
					if (method != s_rpcDamage || !On())
					{
						return;
					}
					ZNetPeer sender = ZNet.instance.GetPeer(rpc);
					if (sender == null || !Own(sender) || target.IsNone() || SessionOf(sender) == null)
					{
						return;
					}
					ZPackage parameters = pkg.ReadPackage();
					HitData hit = new HitData();
					hit.Deserialize(ref parameters);
					if (hit.m_attacker == sender.m_characterID)
					{
						ActivityLog.Hit(sender, target, hit);
						return;
					}
					// a hit on a player's character from a game that is not the player's: a creature it simulates, or a thing of its own
					ZNetPeer victim = PeerOfCharacter(target);
					if (victim == null || victim == sender || SessionOf(victim) == null)
					{
						return;
					}
					if (hit.m_attacker.IsNone())
					{
						Hurt(victim, hit, hit.m_hitType.ToString(), -1, Clean(sender.m_playerName));
						return;
					}
					ZDO attacker = ZDOMan.instance.GetZDO(hit.m_attacker);
					if (attacker != null && s_creatures != null && s_creatures.TryGetValue(attacker.GetPrefab(), out CreatureInfo creature))
					{
						Hurt(victim, hit, creature.name, attacker.GetInt(ZDOVars.s_level, 1), Clean(sender.m_playerName));
					}
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

		// ---- creatures: the health a player's game takes off what it owns ------------------------------------------------------

		private sealed class Track
		{
			public int prefab, level, n;
			public float first, lastDrop, total, big, lastHp, hpmax;
			public bool boss;
			public Vector3 pos;
			public float goneAt = -1f;
			public readonly Dictionary<string, float> by = new Dictionary<string, float>();
		}

		private static readonly Dictionary<ZDOID, Track> s_tracked = new Dictionary<ZDOID, Track>();
		private static readonly List<ZDOID> s_ended = new List<ZDOID>();
		private static readonly List<ZDO> s_found = new List<ZDO>();

		// While RPC_ZDOData of a player's game is handled: who sent it, and the ZDO whose health was read before it was written / the one it created.
		private static ZNetPeer s_from;
		private static ZDO s_hpZdo, s_created;
		private static float s_hp0;

		private static float CreatureHealth(ZDO z, CreatureInfo c)
		{
			float h = z.GetFloat(ZDOVars.s_health, -1f);
			return h >= 0f ? h : z.GetFloat(ZDOVars.s_maxHealth, c.health);
		}

		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix(ZRpc rpc)
			{
				s_from = null;
				s_hpZdo = null;
				s_created = null;
				if (!Configuration.activityLogEnabled.Value)
				{
					return;
				}
				try
				{
					s_from = On() && s_started ? ZNet.instance.GetPeer(rpc) : null;
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}

			static void Finalizer()
			{
				s_from = null;
				s_hpZdo = null;
				s_created = null;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
		public static class ZDOMan_CreateNewZDO_Patch
		{
			static void Postfix(ZDO __result)
			{
				if (s_from != null)
				{
					s_created = __result;
				}
			}
		}

		// The hot path: it returns at once unless a player's game is being handled and the ZDO is a creature's.
		[HarmonyPatch(typeof(ZDO), "Deserialize", typeof(ZPackage))]
		public static class ZDO_Deserialize_Patch
		{
			static void Prefix(ZDO __instance)
			{
				s_hpZdo = null;
				if (s_from == null || s_creatures == null)
				{
					return;
				}
				try
				{
					if (s_creatures.TryGetValue(__instance.GetPrefab(), out CreatureInfo c))
					{
						s_hp0 = CreatureHealth(__instance, c);
						s_hpZdo = __instance;
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}

			static void Postfix(ZDO __instance)
			{
				if (s_hpZdo == null && s_created == null)
				{
					return;
				}
				ZNetPeer from = s_from;
				bool measured = s_hpZdo == __instance;
				bool created = s_created == __instance;
				s_hpZdo = null;
				if (created)
				{
					s_created = null;
				}
				if (from == null)
				{
					return;
				}
				try
				{
					if (measured && s_creatures.TryGetValue(__instance.GetPrefab(), out CreatureInfo c))
					{
						float hp1 = CreatureHealth(__instance, c);
						if (s_hp0 > 0f && hp1 < s_hp0 - 0.01f)
						{
							Dropped(__instance, c, s_hp0, hp1, from, null);
						}
					}
					if (created && s_pieces != null && s_pieces.Contains(__instance.GetPrefab()))
					{
						Session s = SessionOf(from);
						if (s != null)
						{
							Aggregate(s.build, __instance.GetPrefab(), __instance.GetPosition(), 0, false);
						}
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		// The server's own damage to a creature it owns: what it did to it is written like what a game wrote.
		[HarmonyPatch(typeof(Character), "ApplyDamage")]
		public static class Character_ApplyDamage_Patch
		{
			static void Prefix(Character __instance, out float __state)
			{
				__state = -1f;
				if (!Configuration.activityLogEnabled.Value || s_creatures == null || __instance is Player)
				{
					return;
				}
				try
				{
					ZNetView view = __instance.m_nview;
					if (view != null && view.IsValid() && view.IsOwner())
					{
						__state = __instance.GetHealth();
					}
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}

			static void Postfix(Character __instance, HitData hit, float __state)
			{
				if (__state <= 0f)
				{
					return;
				}
				try
				{
					float after = __instance.GetHealth();
					if (after >= __state - 0.01f)
					{
						return;
					}
					ZDO zdo = __instance.m_nview.GetZDO();
					if (zdo == null || !s_creatures.TryGetValue(zdo.GetPrefab(), out CreatureInfo c))
					{
						return;
					}
					string hitBy = null;
					if (hit != null && hit.HaveAttacker())
					{
						Character attacker = hit.GetAttacker();
						if (attacker != null)
						{
							hitBy = attacker is Player player ? Clean(player.GetPlayerName()) : global::Utils.GetPrefabName(attacker.gameObject);
						}
					}
					Dropped(zdo, c, __state, after, null, hitBy);
				}
				catch (Exception e)
				{
					Fault(e);
				}
			}
		}

		// A creature lost health: from == the game that wrote it (null: the server did).
		private static void Dropped(ZDO zdo, CreatureInfo c, float hp0, float hp1, ZNetPeer from, string hitBy)
		{
			float drop = hp0 - hp1;
			float max = zdo.GetFloat(ZDOVars.s_maxHealth, c.health);
			int level = zdo.GetInt(ZDOVars.s_level, -1);
			string by = from != null ? Clean(from.m_playerName) : "server";
			Vector3 pos = zdo.GetPosition();
			Raw(Begin("mobhp"), "by", Json(by));
			Raw(s_b, "acc", from != null ? Json(AccountOf(from)) : "null");
			Raw(s_b, "target", Json(c.name));
			Raw(s_b, "tlevel", level >= 0 ? level.ToString(Inv) : "null");
			Flt(s_b, "hp0", hp0);
			Flt(s_b, "hp1", hp1);
			Flt(s_b, "drop", drop);
			Flt(s_b, "hpmax", max);
			Raw(s_b, "pos", Vec(pos));
			ZDO me = from != null && Own(from) ? ZDOMan.instance.GetZDO(from.m_characterID) : null;
			Raw(s_b, "dist", me != null ? Num(Vector3.Distance(me.GetPosition(), pos)) : "null");
			Bool(s_b, "boss", c.boss);
			Bool(s_b, "tamed", zdo.GetBool(ZDOVars.s_tamed, false));
			if (hitBy != null)
			{
				Str(s_b, "hitby", hitBy);
			}
			End();

			float now = Time.time;
			if (!s_tracked.TryGetValue(zdo.m_uid, out Track t))
			{
				if (s_tracked.Count >= MaxTracked)
				{
					ZDOID oldest = ZDOID.None;
					float first = float.MaxValue;
					foreach (KeyValuePair<ZDOID, Track> kv in s_tracked)
					{
						if (kv.Value.first < first)
						{
							first = kv.Value.first;
							oldest = kv.Key;
						}
					}
					s_tracked.Remove(oldest);
				}
				s_tracked[zdo.m_uid] = t = new Track { prefab = zdo.GetPrefab(), first = now };
			}
			t.lastDrop = now;
			t.total += drop;
			t.n++;
			t.big = Mathf.Max(t.big, drop);
			t.lastHp = hp1;
			t.hpmax = max;
			t.level = level;
			t.boss = c.boss;
			t.pos = pos;
			t.by.TryGetValue(by, out float sum);
			t.by[by] = sum + drop;
		}

		private static bool HasRagdoll(Vector3 pos)
		{
			if (s_ragdolls == null || s_ragdolls.Count == 0)
			{
				return false;
			}
			s_found.Clear();
			ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(1, 0, true), s_found);
			foreach (ZDO z in s_found)
			{
				if (s_ragdolls.Contains(z.GetPrefab()) && Vector3.Distance(z.GetPosition(), pos) <= RagdollMetres)
				{
					s_found.Clear();
					return true;
				}
			}
			s_found.Clear();
			return false;
		}

		// Each second: a tracked creature that is gone from the world has ended (a ragdoll near its last spot says it died); one nobody has hurt for a while is forgotten.
		private static void Mobs(float now)
		{
			if (s_tracked.Count == 0)
			{
				return;
			}
			s_ended.Clear();
			foreach (KeyValuePair<ZDOID, Track> kv in s_tracked)
			{
				Track t = kv.Value;
				if (now - t.lastDrop > ForgetSeconds)
				{
					s_ended.Add(kv.Key);
					continue;
				}
				ZDO zdo = ZDOMan.instance.GetZDO(kv.Key);
				if (zdo != null)
				{
					t.pos = zdo.GetPosition();
					if (s_creatures != null && s_creatures.TryGetValue(t.prefab, out CreatureInfo c))
					{
						float hp = CreatureHealth(zdo, c);
						t.lastHp = hp;
					}
					continue;
				}
				if (t.goneAt < 0f)
				{
					t.goneAt = now;
				}
				bool died = HasRagdoll(t.pos);
				if (died || now - t.goneAt >= RagdollGrace)
				{
					MobEnd(t, died, now);
					s_ended.Add(kv.Key);
				}
			}
			foreach (ZDOID id in s_ended)
			{
				s_tracked.Remove(id);
			}
		}

		private static void MobEnd(Track t, bool died, float now)
		{
			StringBuilder by = new StringBuilder("{");
			foreach (KeyValuePair<string, float> kv in t.by)
			{
				by.Append(by.Length > 1 ? "," : "").Append(Json(kv.Key)).Append(':').Append(Num(kv.Value));
			}
			by.Append('}');
			StringBuilder near = new StringBuilder("[");
			foreach (Session s in s_sessions.Values)
			{
				ZDO z = Own(s.peer) ? ZDOMan.instance.GetZDO(s.peer.m_characterID) : null;
				if (z != null && Vector3.Distance(z.GetPosition(), t.pos) <= NearMetres)
				{
					near.Append(near.Length > 1 ? "," : "").Append(Json(Clean(s.peer.m_playerName)));
				}
			}
			near.Append(']');
			Raw(Begin("mobend"), "target", PrefabJson(t.prefab));
			Raw(s_b, "tlevel", t.level >= 0 ? t.level.ToString(Inv) : "null");
			Flt(s_b, "hpmax", t.hpmax);
			Flt(s_b, "total", t.total);
			Int(s_b, "n", t.n);
			Flt(s_b, "big", t.big);
			Flt(s_b, "secs", now - t.first);
			Flt(s_b, "last_hp", t.lastHp);
			Bool(s_b, "died", died);
			Raw(s_b, "by", by.ToString());
			Raw(s_b, "near", near.ToString());
			Bool(s_b, "boss", t.boss);
			Raw(s_b, "pos", Vec(t.pos));
			End();
		}

		// ---- building and picking up ------------------------------------------------------------------------------------

		// Objects a player's game destroys (a routed "DestroyZDO"): read before the game deletes them; the package is read again by the game.
		[HarmonyPatch(typeof(ZDOMan), "RPC_DestroyZDO")]
		public static class ZDOMan_RPC_DestroyZDO_Patch
		{
			static void Prefix(long sender, ZPackage pkg)
			{
				if (sender == 0L || s_pieces == null || !Configuration.activityLogEnabled.Value || sender == ZDOMan.GetSessionID())
				{
					return;
				}
				int start = pkg.GetPos();
				try
				{
					Session s = On() ? SessionOf(ZNet.instance.GetPeer(sender)) : null;
					if (s == null)
					{
						return;
					}
					int count = pkg.ReadInt();
					for (int i = 0; i < count && i < 20000; i++)
					{
						ZDO zdo = ZDOMan.instance.GetZDO(pkg.ReadZDOID());
						if (zdo == null)
						{
							continue;
						}
						int prefab = zdo.GetPrefab();
						if (s_pieces.Contains(prefab))
						{
							bool own = s.pid != 0L && zdo.GetLong(ZDOVars.s_creator, 0L) == s.pid;
							Aggregate(s.unbuild, prefab, zdo.GetPosition(), 0, own);
						}
						else if (s_drops.Contains(prefab))
						{
							// The stack lives in the item blob of the ZDO (s_itemData), not in ZDOVars.s_stack.
							ItemDrop.ItemData itemData = new ItemDrop.ItemData();
							ItemDrop.LoadFromZDO(itemData, zdo);
							Aggregate(s.pickup, prefab, zdo.GetPosition(), Mathf.Max(1, itemData.m_stack), false);
						}
					}
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

		// ---- the guard log ----------------------------------------------------------------------------------------------

		// Called from GuardLog.Write with each line (without its timestamp).
		public static void Guard(string line)
		{
			if (!Configuration.activityLogEnabled.Value)
			{
				return;
			}
			try
			{
				if (Ready())
				{
					Str(Begin("guard"), "line", line);
					End();
				}
			}
			catch (Exception e)
			{
				Fault(e);
			}
		}

		// ---- leaving, saving, stopping ---------------------------------------------------------------------------------

		// A peer that disconnects: its session ends here, with its aggregates, before anything of it is gone.
		[HarmonyPatch(typeof(ZNet), "Disconnect")]
		public static class ZNet_Disconnect_Patch
		{
			static void Prefix(ZNetPeer peer)
			{
				if (!s_started || !Configuration.activityLogEnabled.Value)
				{
					return;
				}
				try
				{
					Session s = SessionOf(peer);
					if (s != null)
					{
						Leave(s);
					}
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
				if (s_started && Configuration.activityLogEnabled.Value)
				{
					Flush();
				}
			}
		}

		private static void Stop()
		{
			if (!s_started || !Configuration.activityLogEnabled.Value)
			{
				return;
			}
			try
			{
				s_gone.Clear();
				s_gone.AddRange(s_sessions.Keys);
				foreach (ZNetPeer peer in s_gone)
				{
					Leave(s_sessions[peer]);
				}
			}
			catch (Exception e)
			{
				Fault(e);
			}
			Flush();
		}

		[HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
		public static class ZNet_Shutdown_Patch
		{
			static void Prefix()
			{
				Stop();
			}
		}

		[HarmonyPatch(typeof(ZNet), "OnDestroy")]
		public static class ZNet_OnDestroy_Patch
		{
			static void Prefix()
			{
				Stop();
			}
		}
	}
}
