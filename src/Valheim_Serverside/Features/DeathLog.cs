using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		One line per player death in <world>.deaths.log, with the cause the server can tell (docs/DEATHLOG.md has the rules).

		The player's game does not say why it died: Player.OnDeath sets "dead" in the character's ZDO and sends the routed RPC "OnDeath" to everybody;
		the cause (m_lastHit.m_hitType) goes only into the player's own stats. So the server reads it from what it does see:

		- the hits of its own things on the character (Character.Damage on the player's instance: creatures' attacks, projectiles, falling trees, ballistas),
		- the hits another player's game sends (the routed RPC_Damage from its connection to the victim's: PvP, a creature or a cart that game simulates)
		  and the death itself (the routed RPC "OnDeath"),
		- the character's ZDO, polled four times a second: where it is, its stamina and health, whether it is dead.

		A reader only: nothing is dropped, nothing is changed. Off, nothing of this runs (no patch is applied, no tick does anything).
	*/
	public class DeathLog : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.deathLogEnabled.Value;
		}

		private sealed class Sample
		{
			public float t;
			public Vector3 pos;
			public float health, stamina;
			public bool dead;
		}

		private enum Kind { Object, Creature, Player }

		private sealed class Hit
		{
			public float t;
			public Kind kind;
			public bool server;             // seen as Character.Damage on the server (not a player's RPC_Damage)
			public string by, byId, token;  // prefab or player name, account of a player, the creature's m_name
			public int level;
			public bool boss;
			public string hitType;
			public float fire, poison;
		}

		private sealed class State
		{
			public readonly List<Sample> samples = new List<Sample>();
			public readonly List<Hit> hits = new List<Hit>();
			public bool seen, wasDead, logged;
		}

		private const float PollSeconds = 0.25f;
		private const float KeepSamples = 6f, KeepHits = 30f;
		private const float DirectSeconds = 3f, FireSeconds = 8f, PoisonSeconds = 30f, RecentSeconds = 10f, FallWindow = 4f;
		private const float FallMetres = 6f, EdgeMetres = 10420f;

		private static readonly int s_rpcDamage = "RPC_Damage".GetStableHashCode();
		private static readonly int s_rpcOnDeath = "OnDeath".GetStableHashCode();
		private static readonly Dictionary<ZDOID, State> s_states = new Dictionary<ZDOID, State>();
		private static readonly HashSet<ZDOID> s_live = new HashSet<ZDOID>();
		private static readonly List<ZDOID> s_gone = new List<ZDOID>();
		private static float s_next;
		private static string s_path;
		private static int s_errors;

		private static bool On()
		{
			return Configuration.deathLogEnabled.Value && ZNet.instance != null && ZNet.instance.IsServer();
		}

		private static void Fault(Exception e)
		{
			if (s_errors++ < 20)
			{
				ServersidePlugin.logger.LogWarning($"Death log: {e}");
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

		private static State StateOf(ZDOID id)
		{
			if (!s_states.TryGetValue(id, out State state))
			{
				s_states[id] = state = new State();
			}
			return state;
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

		// ---- what the server sees -----------------------------------------------------------------------------------------

		private static Sample Take(ZDO zdo)
		{
			return new Sample
			{
				t = Time.time,
				pos = zdo.GetPosition(),
				health = zdo.GetFloat(ZDOVars.s_health, -1f),
				stamina = zdo.GetFloat(ZDOVars.s_stamina, -1f),
				dead = zdo.GetBool(ZDOVars.s_dead, false)
			};
		}

		private static void Note(State state, Hit hit)
		{
			state.hits.RemoveAll(h => hit.t - h.t > KeepHits);
			state.hits.Add(hit);
		}

		// Character.Damage on the server for a player's character: the server's creatures, projectiles and objects call it (the hit is then sent on to the player's game).
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
			if (peer == null || !Own(peer) || peer.m_characterID != zdo.m_uid)
			{
				return;
			}
			Hit note = new Hit
			{
				t = Time.time,
				server = true,
				hitType = hit.m_hitType.ToString(),
				fire = hit.m_damage.m_fire,
				poison = hit.m_damage.m_poison
			};
			Character attacker = hit.HaveAttacker() ? hit.GetAttacker() : null;
			if (attacker != null)
			{
				if (hit.m_attacker == zdo.m_uid)
				{
					return;
				}
				if (attacker.IsPlayer())
				{
					note.kind = Kind.Player;
					ZNetPeer who = PeerOfCharacter(hit.m_attacker);
					note.by = who != null ? who.m_playerName : (attacker as Player)?.GetPlayerName();
					note.byId = who != null ? AccountOf(who) : null;
				}
				else
				{
					note.kind = Kind.Creature;
					note.by = global::Utils.GetPrefabName(attacker.gameObject);
					note.token = attacker.m_name;
					note.level = attacker.GetLevel();
					note.boss = attacker.IsBoss();
				}
			}
			Note(StateOf(zdo.m_uid), note);
		}

		// A reader of the routed RPCs, like AntiCheat's: compares the method by hash before it reads anything more, reads a copy of the package and puts the
		// position back, and never drops anything.
		[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
		public static class ZRoutedRpc_RPC_RoutedRPC_Patch
		{
			[HarmonyPriority(Priority.Last)]
			static void Prefix(ZRpc rpc, ZPackage pkg, bool __runOriginal)
			{
				// a hit another prefix dropped (the anti-cheat's Damage rule) never reaches the victim
				if (pkg == null || !__runOriginal || !On())
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
					if (method != s_rpcDamage && method != s_rpcOnDeath)
					{
						return;
					}
					ZNetPeer sender = ZNet.instance.GetPeer(rpc);
					if (sender == null || !Own(sender) || target.IsNone())
					{
						return;
					}
					if (method == s_rpcOnDeath)
					{
						if (sender.m_characterID == target)
						{
							Death(sender, "OnDeath");
						}
						return;
					}
					ZNetPeer victim = PeerOfCharacter(target);
					if (victim == null || victim == sender)
					{
						return;
					}
					ZPackage parameters = pkg.ReadPackage();
					HitData hit = new HitData();
					hit.Deserialize(ref parameters);
					Hit note = new Hit
					{
						t = Time.time,
						hitType = hit.m_hitType.ToString(),
						fire = hit.m_damage.m_fire,
						poison = hit.m_damage.m_poison
					};
					if (hit.m_attacker == sender.m_characterID)
					{
						note.kind = Kind.Player;
						note.by = sender.m_playerName;
						note.byId = AccountOf(sender);
					}
					else if (hit.m_attacker.IsNone())
					{
						// a thing the sender's game simulates (its cart, its boat, ...)
						note.kind = Kind.Object;
					}
					else if (!CreatureOf(hit.m_attacker, note))
					{
						// a player's hit sent in the name of another is not about the sender's game
						return;
					}
					Note(StateOf(target), note);
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

		// A creature the sender's game simulates (it owns the creature's ZDO): its hit on another player comes as that game's RPC_Damage.
		private static bool CreatureOf(ZDOID attacker, Hit note)
		{
			ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(attacker) : null;
			GameObject prefab = zdo != null && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
			Character character = prefab != null ? prefab.GetComponent<Character>() : null;
			if (character == null || character is Player)
			{
				return false;
			}
			note.kind = Kind.Creature;
			note.by = prefab.name;
			note.token = character.m_name;
			note.level = zdo.GetInt(ZDOVars.s_level, 1);
			note.boss = character.IsBoss();
			return true;
		}

		// Called from ServersidePlugin.Update.
		public static void Tick()
		{
			if (!On() || Time.time < s_next || ZDOMan.instance == null)
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
		}

		private static void Poll()
		{
			s_live.Clear();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!Own(peer))
				{
					continue;
				}
				ZDOID id = peer.m_characterID;
				s_live.Add(id);
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo == null)
				{
					continue;
				}
				State state = StateOf(id);
				Sample sample = Take(zdo);
				if (sample.dead && state.seen && !state.wasDead && !state.logged)
				{
					Death(peer, "dead flag");
				}
				state.wasDead = sample.dead;
				state.seen = true;
				state.samples.RemoveAll(s => sample.t - s.t > KeepSamples);
				state.samples.Add(sample);
				state.hits.RemoveAll(h => sample.t - h.t > KeepHits);
			}
			s_gone.Clear();
			foreach (ZDOID id in s_states.Keys)
			{
				if (!s_live.Contains(id))
				{
					s_gone.Add(id);
				}
			}
			foreach (ZDOID id in s_gone)
			{
				s_states.Remove(id);
			}
		}

		// ---- the death ----------------------------------------------------------------------------------------------------

		private static void Death(ZNetPeer peer, string source)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
			if (zdo == null)
			{
				return;
			}
			State state = StateOf(peer.m_characterID);
			if (state.logged)
			{
				return;
			}
			state.logged = true;
			string[] fields = Verdict(peer, zdo, state, out string cause, out string detail);
			Append(string.Join(",", fields));
			ServersidePlugin.logger.LogInfo($"Death log: {Clean(peer.m_playerName)} died: {cause}{(detail.Length > 0 ? " " + detail : "")} ({source})");
		}

		private static Hit Latest(State state, float now, float within, Func<Hit, bool> fits)
		{
			Hit best = null;
			foreach (Hit h in state.hits)
			{
				if (now - h.t <= within && fits(h) && (best == null || h.t >= best.t))
				{
					best = h;
				}
			}
			return best;
		}

		private static float GroundAt(Vector3 p)
		{
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float height))
			{
				return height;
			}
			return WorldGenerator.instance.GetHeight(p.x, p.z);
		}

		private static bool InWater(Sample s)
		{
			return s.pos.y <= ZoneSystem.instance.m_waterLevel + 0.3f && GroundAt(s.pos) <= s.pos.y - 1f;
		}

		private static string[] Verdict(ZNetPeer peer, ZDO zdo, State state, out string cause, out string detail)
		{
			float now = Time.time;
			Sample at = Take(zdo);
			List<Sample> all = new List<Sample>(state.samples);
			Sample before = all.Count > 0 ? all[all.Count - 1] : at;
			// what it was like before it died: the last sample the character was not dead in
			for (int i = all.Count - 1; i >= 0; i--)
			{
				if (!all[i].dead)
				{
					before = all[i];
					break;
				}
			}
			all.Add(at);
			Vector3 pos = at.pos;
			Heightmap.Biome biome = WorldGenerator.instance.GetBiome(pos);

			bool water = all.Count >= 2 ? InWater(all[all.Count - 1]) && InWater(all[all.Count - 2]) : InWater(at);
			float high = at.pos.y;
			foreach (Sample s in all)
			{
				if (now - s.t <= FallWindow && s.pos.y > high)
				{
					high = s.pos.y;
				}
			}
			float drop = Mathf.Max(0f, high - at.pos.y);

			string by = null, byId = null, token = null, hitName = null, effect = null;
			int? level = null;
			bool? boss = null;
			void FromHit(Hit h)
			{
				by = h.by;
				byId = h.kind == Kind.Player ? h.byId : null;
				if (h.kind == Kind.Creature)
				{
					token = h.token;
					level = h.level;
					boss = h.boss;
				}
				hitName = h.hitType;
			}

			Hit pvp = Latest(state, now, DirectSeconds, h => h.kind == Kind.Player);
			Hit creature = Latest(state, now, DirectSeconds, h => h.kind == Kind.Creature);
			Hit thing = Latest(state, now, DirectSeconds, h => h.kind == Kind.Object);
			Hit burn = Latest(state, now, FireSeconds, h => h.fire > 0f);
			Hit poison = Latest(state, now, PoisonSeconds, h => h.poison > 0f);
			Hit dot = burn == null ? poison : (poison == null || burn.t >= poison.t ? burn : poison);

			if (pvp != null)
			{
				cause = "player";
				FromHit(pvp);
			}
			else if (creature != null)
			{
				cause = "creature";
				FromHit(creature);
			}
			else if (thing != null)
			{
				cause = "object";
				hitName = thing.hitType;
			}
			else if (Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z) >= EdgeMetres)
			{
				cause = "edge";
			}
			else if (water && biome == Heightmap.Biome.AshLands)
			{
				cause = "boiled";
			}
			else if (water)
			{
				cause = "drowned";
			}
			else if (drop >= FallMetres)
			{
				cause = "fall";
			}
			else if (dot != null)
			{
				cause = "dot";
				effect = dot == burn ? "fire" : "poison";
				FromHit(dot);
			}
			else if (biome == Heightmap.Biome.AshLands && ZoneSystem.instance.IsLava(pos))
			{
				cause = "lava";
			}
			else if (biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth)
			{
				cause = "frozen";
			}
			else
			{
				cause = "unknown";
			}

			List<string> recent = new List<string>();
			List<Hit> byTime = new List<Hit>(state.hits);
			byTime.Sort((a, b) => b.t.CompareTo(a.t));
			foreach (Hit h in byTime)
			{
				if (now - h.t > RecentSeconds || recent.Count >= 5)
				{
					continue;
				}
				string label = h.by ?? h.hitType;
				if (!recent.Contains(label))
				{
					recent.Add(label);
				}
			}

			detail = cause == "dot" ? effect + (by != null ? " " + by : (hitName != null ? " " + hitName : "")) : (by ?? hitName ?? "");

			CultureInfo c = CultureInfo.InvariantCulture;
			StringBuilder list = new StringBuilder("[");
			for (int i = 0; i < recent.Count; i++)
			{
				list.Append(i > 0 ? "," : "").Append(Json(recent[i]));
			}
			list.Append(']');
			return new[]
			{
				"{\"time\":" + Json(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", c)),
				"\"player\":" + Json(peer.m_playerName),
				"\"id\":" + Json(AccountOf(peer)),
				"\"cause\":" + Json(cause),
				"\"by\":" + Json(by),
				"\"byId\":" + Json(byId),
				"\"name\":" + Json(token),
				"\"level\":" + (level.HasValue ? level.Value.ToString(c) : "null"),
				"\"boss\":" + (boss.HasValue ? (boss.Value ? "true" : "false") : "null"),
				"\"hit\":" + Json(hitName),
				"\"effect\":" + Json(effect),
				"\"fell\":" + drop.ToString("0.0", c),
				"\"water\":" + (water ? "true" : "false"),
				"\"biome\":" + Json(biome.ToString()),
				"\"health\":" + Number(before.health),
				"\"stamina\":" + Number(before.stamina),
				"\"recent\":" + list,
				"\"pos\":[" + Mathf.RoundToInt(pos.x).ToString(c) + "," + Mathf.RoundToInt(pos.y).ToString(c) + "," + Mathf.RoundToInt(pos.z).ToString(c) + "]}"
			};
		}

		private static string Number(float value)
		{
			return value < 0f ? "null" : value.ToString("0.0", CultureInfo.InvariantCulture);
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

		// <world>.deaths.log next to the world save, as GuardLog finds its file; UTF-8, LF.
		private static void Append(string line)
		{
			try
			{
				if (s_path == null)
				{
					if (ZNet.World == null)
					{
						return;
					}
					s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(ZNet.World.m_fileSource), ZNet.World.m_name + ".deaths.log");
				}
				File.AppendAllText(s_path, line + "\n", new UTF8Encoding(false));
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Death log: {e.Message}");
			}
		}
	}
}
