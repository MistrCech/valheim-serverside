using FeaturesLib;
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
		Base decay: what players built is taken down by the server once the players it belongs to have stayed away
		long enough, so that abandoned bases do not fill the world for ever.

		A piece (anything with a creator) can be claimed three ways, and each claim has a grace (days since the last
		of the players who hold it was active) and a fade (days over which the piece then goes):
		- a ward that is switched on covers it (the game's own radius): the ward's owner and the players on its list
		  hold it. Defaults 14 + 10 days;
		- a workbench within WorkbenchRadius: its builder holds it. Defaults 7 + 10 days;
		- the piece's builder alone (all such pieces of one builder are one camp). Defaults 3 + 4 days.
		A piece is protected as long as any claim on it is still within its grace. When the last of them has lapsed it
		belongs to the base of that claim, and goes with it. (The claim that lapses last decides, not the one whose fade
		is longest: a builder who plays every day keeps their piece in a ward whose keepers are gone, and an active
		player's workbench keeps what stands within its reach.) The two halves of a portal pair are one thing: the pair
		lives while either end is held. A builder who is exempt (admins, and whoever `decay exempt` names) is always active
		for their own pieces and for the ward and workbench they built over their own pieces; over the pieces of others
		their ward or workbench holds only as long as its keepers really play, and a guest who is exempt on someone else's
		ward list counts as the guest they are.

		A player is active when they have been in the game ActiveMinutes in one go (a quick login is no activity). Nothing
		starts or is taken down while one of the players who hold the base is in the game, or while a player stands by a
		ship or cart that is due, or while a piece that is protected rests against it (the game would break that piece
		too), or while a player is connecting (the pass waits for the next one). A player the server has not met has their
		clock started when the server first sees their builds, so switching decay on takes down nothing at once. The clock
		counts only the time the server runs under decay: a week of maintenance takes nothing from anyone's grace.

		When the grace of a base is over an episode starts: every piece of the base gets the time at which it goes, spread
		over the fade: first the ward (within a day, so that a mistake -- a setting, an old file -- can be put right before
		it is gone for good; the ward is what keeps others from touching a base), then the top pieces (roofs, then walls,
		then floors), then what holds things (chests, beds, tables), the workbench last. At most MaxBeginsPerPass bases start
		in a pass. A piece's time is written to the piece itself (sgg_decay_at; sgg_decay_ep names the episode, which is its
		base's key and the time it started), so a base does not lose its protection level when its ward goes. The base's key
		is the kind, the builder and the place of its ward or workbench, because the game numbers its objects anew every time
		the world is loaded and a number would not outlive a restart. Pieces go without a trace (no debris; what is in a chest
		goes with it). The ones that go within a day are worn: their health is cut to a fifth, which the game shows as broken
		wood and stone, and a hammer can still repair them. Every pass the marked pieces are judged again, one by one: a piece
		whose builder (or any other claim on it) is active again is unmarked (a worn piece is whole again), and an episode
		whose pieces are all unmarked is over. What is gone is gone; a ward has to be built again.

		Mode DryRun does all of this except changing the world: it writes what it would do to the log. Mode On changes it.
		The players, when they were last active, who is exempt, the episodes in progress and the world they belong to are kept
		in <world>.decay.txt.

		Players are told shortly after they join: a base that is decaying, and one that starts to soon. The server console
		has `decay` (who is away and for how long, every base and its stage), `decay away <player|all> <days>`,
		`decay exempt <player>`, `decay unexempt <player>`, `decay clear` (unmark everything, mend what is worn, forget the
		episodes) and `decay scan`.
	*/
	public class Decay : IFeature
	{
		public enum Mode { Off, DryRun, On }

		public bool FeatureEnabled()
		{
			return Configuration.decayMode.Value != Mode.Off;
		}

		private enum Tier { None = 0, Bench = 1, Ward = 2 }

		private class Person
		{
			public long id;
			public string name = "";
			public DateTime lastActive;
			public bool admin;
		}

		private class Episode
		{
			public string key;
			public Tier tier;
			public DateTime started;
			public double fadeSeconds;
			public long creator;
			public HashSet<long> keepers = new HashSet<long>();
			public string owner = "";
			public int pieces;

			// What the pieces carry: the base's key and the time the episode started, so a mark left over from an earlier
			// episode of the same base (a game that held on to it) does not name this one.
			public int Id
			{
				get
				{
					int h = (key + "@" + Unix(started).ToString(CultureInfo.InvariantCulture)).GetStableHashCode();
					return h == 0 ? 1 : h;
				}
			}
		}

		private class Session
		{
			public float since;
			public long player;
			public bool told;
		}

		private struct Piece
		{
			public ZDOID id;
			public Vector3 at;
			public long creator;
			public int prefab;
			public long stampAt;
			public int stampEpisode;
			public ZDOID partner;
		}

		private struct Protector
		{
			public ZDOID id;
			public Vector3 at;
			public float radius;
			public Tier tier;
			public long creator;
			public HashSet<long> keepers;
			// For the builder's own pieces (an exempt builder is always active), and for everybody else's (what the keepers really did).
			public DateTime activity, realActivity;
			public string name;
		}

		// One claim on a piece, and when it lapses (start) and the piece is gone (end).
		private struct Candidate
		{
			public string key;
			public Tier tier;
			public long creator;
			public HashSet<long> keepers;
			public DateTime activity, start, end;
			public ZDOID ward;
			public string owner;
		}

		private class Base
		{
			public Candidate candidate;
			public List<Piece> pieces = new List<Piece>();
		}

		private static readonly int s_keyAt = "sgg_decay_at".GetStableHashCode();
		private static readonly int s_keyEpisode = "sgg_decay_ep".GetStableHashCode();

		private static readonly Dictionary<long, Person> s_people = new Dictionary<long, Person>();
		private static readonly Dictionary<string, Episode> s_episodes = new Dictionary<string, Episode>();
		private static readonly HashSet<long> s_exempt = new HashSet<long>();
		private static readonly Dictionary<ZNetPeer, Session> s_sessions = new Dictionary<ZNetPeer, Session>();
		private static readonly Dictionary<long, List<string>> s_status = new Dictionary<long, List<string>>();
		private static readonly Dictionary<int, float> s_maxHealth = new Dictionary<int, float>();
		private static readonly List<int> s_permittedKeys = new List<int>();
		private static readonly HashSet<string> s_logged = new HashSet<string>();
		// The players who are in the game right now (by character), and where they stand, as of the start of the pass.
		private static readonly HashSet<long> s_online = new HashSet<long>();
		private static readonly List<Vector3> s_peerPositions = new List<Vector3>();
		private static readonly Dictionary<ZDOID, Piece> s_pieceById = new Dictionary<ZDOID, Piece>();
		private static List<string> s_report = new List<string>();

		private static Dictionary<int, float> s_wardRadius;
		private static HashSet<int> s_bench, s_holders, s_portals, s_ships, s_containers;

		// The decay clock counts only the time the server runs, and not while decay is off: the real time of the first run plus
		// the uptime saved in the file and since. A week of maintenance, or a host whose clock jumps, takes nothing from anyone's
		// grace. The kit replaces s_clock with its own.
		private static DateTime s_origin;
		private static double s_upBase;
		private static readonly System.Diagnostics.Stopwatch s_watch = new System.Diagnostics.Stopwatch();
		private static Func<DateTime> s_clock = Uptime;

		private static string s_path;
		private static bool s_loaded, s_dirty, s_failed, s_saveFailing;
		private static float s_nextTrack, s_nextScan, s_nextSave, s_lastScan = -1000f;
		private static string s_lastSummary = "";
		private static int s_dryDue = -1;

		private const float TrackSeconds = 20f;
		private const float SaveSeconds = 120f;
		private const float TellAfterSeconds = 25f;
		private const float FreshStatusSeconds = 120f;
		private const double WarnDays = 3.0;
		private const double WornHours = 24.0;
		private const float WornFraction = 0.2f;
		private const int ShownToAPlayer = 4;
		private const int MaxBeginsPerPass = 5;
		private const float TouchMetres = 3f;
		private const float ShipWatchMetres = 30f;
		private const long WardGraceSeconds = 86400L;

		private static DateTime Uptime()
		{
			return s_origin.AddSeconds(s_upBase + s_watch.Elapsed.TotalSeconds);
		}

		private static DateTime Now()
		{
			return s_clock();
		}

		private static long Unix(DateTime time)
		{
			return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeSeconds();
		}

		private static DateTime FromUnix(long seconds)
		{
			return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
		}

		private static string Inv(long number)
		{
			return number.ToString(CultureInfo.InvariantCulture);
		}

		private static double DaysOf(float days)
		{
			return Math.Max(0f, days);
		}

		private static float TierGrace(Tier tier)
		{
			return tier == Tier.Ward ? Configuration.decayWardGraceDays.Value : tier == Tier.Bench ? Configuration.decayWorkbenchGraceDays.Value : Configuration.decayNoneGraceDays.Value;
		}

		private static float TierFade(Tier tier)
		{
			return tier == Tier.Ward ? Configuration.decayWardFadeDays.Value : tier == Tier.Bench ? Configuration.decayWorkbenchFadeDays.Value : Configuration.decayNoneFadeDays.Value;
		}

		private static string TierName(Tier tier)
		{
			return tier == Tier.Ward ? "ward base" : tier == Tier.Bench ? "workbench base" : "camp";
		}

		private static string Name(long id)
		{
			return s_people.TryGetValue(id, out Person person) && !string.IsNullOrEmpty(person.name) ? person.name : "#" + Inv(id);
		}

		// A name goes into a log line and into a file: no line breaks, tabs or other control characters.
		private static string Clean(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "?";
			}
			char[] c = text.ToCharArray();
			for (int i = 0; i < c.Length; i++)
			{
				if (char.IsControl(c[i]))
				{
					c[i] = ' ';
				}
			}
			return new string(c);
		}

		// The base of a ward or a workbench is named by its kind, its builder and its place, which outlive a restart (the game's object numbers do not).
		private static string KeyOf(Tier tier, long creator, Vector3 at)
		{
			return (tier == Tier.Ward ? "W:" : "B:") + Inv(creator) + ":" + Inv((long)Math.Round(at.x)) + "," + Inv((long)Math.Round(at.z));
		}

		// ---- what the world's prefabs say -------------------------------------------------------------------------------

		private static bool Ready()
		{
			if (s_wardRadius == null && ZNetScene.instance && ZNetScene.instance.m_prefabs != null && ZNetScene.instance.m_prefabs.Count > 0)
			{
				Dictionary<int, float> wards = new Dictionary<int, float>();
				HashSet<int> bench = new HashSet<int>(), holders = new HashSet<int>(), portals = new HashSet<int>(), ships = new HashSet<int>(), containers = new HashSet<int>();
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
					if (prefab.name == "piece_workbench")
					{
						bench.Add(hash);
					}
					if (prefab.GetComponent<Container>())
					{
						containers.Add(hash);
					}
					if (prefab.GetComponent<Container>() || prefab.GetComponent<Bed>() || prefab.GetComponent<CraftingStation>())
					{
						holders.Add(hash);
					}
					if (prefab.GetComponent<TeleportWorld>())
					{
						portals.Add(hash);
					}
					if (prefab.GetComponent<Ship>() || prefab.GetComponent<Vagon>())
					{
						ships.Add(hash);
					}
				}
				s_holders = holders;
				s_bench = bench;
				s_portals = portals;
				s_ships = ships;
				s_containers = containers;
				s_wardRadius = wards;
			}
			return s_wardRadius != null && ZDOMan.instance != null && ZNet.World != null;
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

		private static int PermittedKey(int i)
		{
			while (s_permittedKeys.Count <= i)
			{
				s_permittedKeys.Add(("pu_id" + s_permittedKeys.Count).GetStableHashCode());
			}
			return s_permittedKeys[i];
		}

		// A piece's full health as its game reckons it: the prefab's, raised by the world level.
		internal static float MaxHealthOf(int prefab)
		{
			return MaxHealth(prefab);
		}

		private static float MaxHealth(int prefab)
		{
			if (!s_maxHealth.TryGetValue(prefab, out float health))
			{
				WearNTear wnt = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<WearNTear>();
				health = wnt ? wnt.m_health : 0f;
				if (health > 0f && Game.m_worldLevel > 0 && Game.instance != null)
				{
					health += Game.m_worldLevel * Game.instance.m_worldLevelPieceHPMultiplier * health;
				}
				s_maxHealth[prefab] = health;
			}
			return health;
		}

		// ---- activity ---------------------------------------------------------------------------------------------------

		// A player the server has not seen: the clock starts now, so nothing is taken down because decay was switched on.
		private static Person Known(long id)
		{
			if (!s_people.TryGetValue(id, out Person person))
			{
				s_people[id] = person = new Person { id = id, lastActive = Now() };
				s_dirty = true;
			}
			return person;
		}

		private static bool IsExempt(long id)
		{
			return s_exempt.Contains(id) || (Configuration.decayExemptAdmins.Value && s_people.TryGetValue(id, out Person person) && person.admin);
		}

		// When the player was last active, as far as their own things go. For one who is exempt: a time so far ahead that
		// nothing they built ever starts to decay.
		private static DateTime Activity(long id)
		{
			return IsExempt(id) ? Now().AddYears(100) : Known(id).lastActive;
		}

		// When the player was last really active, whoever they are (an exempt guest on someone else's ward counts as the guest they are).
		private static DateTime RealActivity(long id)
		{
			return Known(id).lastActive;
		}

		// The builder's own exemption holds for their ward, workbench and pieces; a guest on a ward's list counts for what they do.
		private static DateTime KeeperActivity(long creator, long keeper)
		{
			return keeper == creator ? Activity(keeper) : RealActivity(keeper);
		}

		private static void Track()
		{
			s_nextTrack = Time.time + TrackSeconds;
			double needed = Math.Max(0f, Configuration.decayActiveMinutes.Value) * 60.0;
			DateTime now = Now();
			HashSet<ZNetPeer> present = new HashSet<ZNetPeer>();
			List<KeyValuePair<ZNetPeer, long>> toTell = new List<KeyValuePair<ZNetPeer, long>>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!peer.IsReady())
				{
					continue;
				}
				// A player between two characters (dead, waiting to respawn) keeps the time they have been in the game.
				present.Add(peer);
				long id = PlayerId(peer);
				if (id == 0L)
				{
					continue;
				}
				if (!s_sessions.TryGetValue(peer, out Session session) || session.player != id)
				{
					s_sessions[peer] = session = new Session { since = Time.time, player = id };
				}
				Person person = Known(id);
				string name = Clean(peer.m_playerName);
				if (!string.IsNullOrEmpty(peer.m_playerName) && person.name != name)
				{
					person.name = name;
					s_dirty = true;
				}
				// An admin stays one once seen: another player holding the same character id (a copied character file) must not take the exemption away.
				if (!person.admin && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName()))
				{
					person.admin = true;
					s_dirty = true;
				}
				// Active: in the game long enough in one go. An away date in the future (`decay away`) is not pulled back by a login.
				if (Time.time - session.since >= needed && person.lastActive < now.AddSeconds(-60))
				{
					person.lastActive = now;
					s_dirty = true;
				}
				if (!session.told && Time.time - session.since >= TellAfterSeconds && Configuration.decayLoginMessage.Value)
				{
					session.told = true;
					toTell.Add(new KeyValuePair<ZNetPeer, long>(peer, id));
				}
			}
			foreach (ZNetPeer gone in s_sessions.Keys.Where(p => !present.Contains(p)).ToList())
			{
				s_sessions.Remove(gone);
			}
			if (toTell.Count > 0)
			{
				// What they are told is as of the last pass: make it a fresh one if that was a while ago.
				if (Configuration.decayMode.Value != Mode.Off && Time.time - s_lastScan > FreshStatusSeconds)
				{
					Scan();
				}
				foreach (KeyValuePair<ZNetPeer, long> who in toTell)
				{
					Tell(who.Key, who.Value);
				}
			}
		}

		private static void Tell(ZNetPeer peer, long id)
		{
			if (!s_status.TryGetValue(id, out List<string> lines) || lines.Count == 0)
			{
				return;
			}
			IEnumerable<string> shown = lines.Count <= ShownToAPlayer ? lines : lines.Take(ShownToAPlayer - 1).Concat(new[] { $"... and {lines.Count - (ShownToAPlayer - 1)} more of your bases are affected." });
			foreach (string line in shown)
			{
				PlayerMessages.Show(peer.m_uid, MessageHud.MessageType.TopLeft, line);
			}
		}

		// ---- the world's bases -------------------------------------------------------------------------------------------

		private static bool Covers(Vector3 at, Vector3 centre, float radius)
		{
			float dx = centre.x - at.x;
			float dz = centre.z - at.z;
			return dx * dx + dz * dz < radius * radius;
		}

		// A cell of the grid the neighbours are looked up in.
		private static long CellOf(Vector3 p)
		{
			return (long)Math.Floor(p.x / TouchMetres) * 1000003L + (long)Math.Floor(p.z / TouchMetres);
		}

		/*
			One pass: the bases and where each stands, marks cleared, episodes begun and carried out. Returns what it did, for the
			console and the test.
		*/
		private static string Scan()
		{
			if (s_failed || !s_loaded || s_wardRadius == null)
			{
				return "";
			}
			s_lastScan = Time.time;
			s_nextScan = Time.time + Math.Max(1f, Configuration.decayScanMinutes.Value) * 60f;
			Mode mode = Configuration.decayMode.Value;
			DateTime now = Now();
			long nowUnix = Unix(now);
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

			// Who is in the game: by the character, or by the last character a connected player had (between two characters).
			s_online.Clear();
			s_peerPositions.Clear();
			bool someoneLoading = false;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (!peer.IsReady())
				{
					continue;
				}
				long online = PlayerId(peer);
				if (online == 0L && s_sessions.TryGetValue(peer, out Session session))
				{
					online = session.player;
				}
				if (online != 0L)
				{
					s_online.Add(online);
				}
				else
				{
					someoneLoading = true;
				}
				s_peerPositions.Add(peer.GetRefPos());
			}
			// What is undone is always done; what destroys waits while a player is connecting (we do not know who they are yet) and while the file cannot be written.
			bool write = mode == Mode.On;
			bool strike = write && !someoneLoading && !s_saveFailing;
			if (write && !strike && s_logged.Add("defer/" + Inv((long)(Time.time / 600f))))
			{
				ServersidePlugin.logger.LogInfo(s_saveFailing
					? $"Decay: {s_path} cannot be written, so nothing is taken down until it can"
					: "Decay: a player is connecting; nothing is taken down in this pass");
			}
			if (s_logged.Count > 5000)
			{
				s_logged.Clear();
			}

			// 1. wards, workbenches, pieces
			List<Protector> protectors = new List<Protector>();
			List<Piece> pieces = new List<Piece>();
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
			{
				long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
				if (creator == 0L)
				{
					continue;
				}
				int prefab = zdo.GetPrefab();
				Vector3 at = zdo.GetPosition();
				pieces.Add(new Piece
				{
					id = zdo.m_uid, at = at, creator = creator, prefab = prefab,
					stampAt = zdo.GetLong(s_keyAt, 0L), stampEpisode = zdo.GetInt(s_keyEpisode, 0),
					partner = s_portals.Contains(prefab) ? zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) : ZDOID.None,
				});
				if (s_wardRadius.TryGetValue(prefab, out float radius) && zdo.GetBool(ZDOVars.s_enabled))
				{
					HashSet<long> keepers = new HashSet<long> { creator };
					int count = Mathf.Min(zdo.GetInt(ZDOVars.s_permitted, 0), 512);
					for (int i = 0; i < count; i++)
					{
						long id = zdo.GetLong(PermittedKey(i), 0L);
						if (id != 0L)
						{
							keepers.Add(id);
						}
					}
					// The ward knows its owner's name: use it for a player the server has not met.
					Person owner = Known(creator);
					string wardOwner = zdo.GetString(ZDOVars.s_creatorName, "");
					if (string.IsNullOrEmpty(owner.name) && !string.IsNullOrEmpty(wardOwner))
					{
						owner.name = Clean(wardOwner);
						s_dirty = true;
					}
					protectors.Add(new Protector
					{
						id = zdo.m_uid, at = at, radius = radius, tier = Tier.Ward, creator = creator, keepers = keepers, name = Name(creator),
						activity = keepers.Select(k => KeeperActivity(creator, k)).Max(), realActivity = keepers.Select(RealActivity).Max(),
					});
				}
				else if (s_bench.Contains(prefab))
				{
					protectors.Add(new Protector
					{
						id = zdo.m_uid, at = at, radius = Math.Max(1f, Configuration.decayWorkbenchRadius.Value), tier = Tier.Bench, creator = creator,
						keepers = new HashSet<long> { creator }, name = Name(creator), activity = Activity(creator), realActivity = RealActivity(creator),
					});
				}
			}
			s_pieceById.Clear();
			foreach (Piece piece in pieces)
			{
				s_pieceById[piece.id] = piece;
			}
			LearnAdmins();

			List<string> did = new List<string>();
			Dictionary<int, Episode> byId = new Dictionary<int, Episode>();
			foreach (Episode e in s_episodes.Values)
			{
				byId[e.Id] = e;
			}

			// 2. the marked pieces: each judged again (a piece someone who holds it is active for is unmarked), then the rest told apart
			List<Piece> unmarked = new List<Piece>();
			List<Piece> marked = new List<Piece>();
			Dictionary<int, int> remaining = new Dictionary<int, int>(), heldAgain = new Dictionary<int, int>();
			Dictionary<ZDOID, Candidate> bestOf = new Dictionary<ZDOID, Candidate>();
			foreach (Piece piece in pieces)
			{
				if (piece.stampEpisode == 0 || piece.stampAt == 0L)
				{
					// A mark that has lost its time (a game handed back half of it) is no mark: the piece is judged as any other.
					if (piece.stampEpisode != 0 && write)
					{
						Unstamp(ZDOMan.instance.GetZDO(piece.id), piece.prefab);
					}
					unmarked.Add(piece);
					continue;
				}
				if (!byId.TryGetValue(piece.stampEpisode, out Episode episode) || !s_episodes.ContainsKey(episode.key))
				{
					// A mark of no episode we know (the file was lost, or a game gave back an old mark): the piece is an ordinary one again.
					if (write)
					{
						Unstamp(ZDOMan.instance.GetZDO(piece.id), piece.prefab);
					}
					unmarked.Add(piece);
					continue;
				}
				if (Best(piece, protectors).start > now)
				{
					// Someone who holds this piece is active (its builder, say, in the ward of players who are gone): it stays.
					if (write)
					{
						Unstamp(ZDOMan.instance.GetZDO(piece.id), piece.prefab);
					}
					heldAgain[piece.stampEpisode] = (heldAgain.TryGetValue(piece.stampEpisode, out int again) ? again : 0) + 1;
					unmarked.Add(piece);
					continue;
				}
				remaining[piece.stampEpisode] = (remaining.TryGetValue(piece.stampEpisode, out int n) ? n : 0) + 1;
				marked.Add(piece);
			}
			foreach (KeyValuePair<int, int> again in heldAgain)
			{
				if (byId.TryGetValue(again.Key, out Episode e))
				{
					string text = $"{again.Value} piece(s) of the {TierName(e.tier)} of {Clean(e.owner)} ({e.key}) are no longer marked: their builder, or another claim on them, is active again";
					if (write)
					{
						Say(did, text);
					}
					else if (s_logged.Add($"dryheld/{e.key}/{nowUnix / 86400}"))
					{
						Say(did, "(dry run) " + text.Replace("are no longer", "would no longer be"));
					}
				}
			}

			// what is protected and stands against a marked piece holds it (the game would break the one that rests on it)
			Dictionary<long, List<Vector3>> shelter = new Dictionary<long, List<Vector3>>();
			foreach (Piece piece in unmarked)
			{
				Candidate best = Best(piece, protectors);
				bestOf[piece.id] = best;
				if (best.start > now)
				{
					long cell = CellOf(piece.at);
					if (!shelter.TryGetValue(cell, out List<Vector3> list))
					{
						shelter[cell] = list = new List<Vector3>();
					}
					list.Add(piece.at);
				}
			}
			Func<Piece, bool> held = piece =>
			{
				Episode e = byId[piece.stampEpisode];
				if (e.keepers.Any(s_online.Contains) || s_online.Contains(piece.creator))
				{
					return true;
				}
				if (s_ships.Contains(piece.prefab) && s_peerPositions.Any(p => (p.x - piece.at.x) * (p.x - piece.at.x) + (p.z - piece.at.z) * (p.z - piece.at.z) < ShipWatchMetres * ShipWatchMetres))
				{
					return true;
				}
				long cx = (long)Math.Floor(piece.at.x / TouchMetres), cz = (long)Math.Floor(piece.at.z / TouchMetres);
				for (long dx = -1; dx <= 1; dx++)
				{
					for (long dz = -1; dz <= 1; dz++)
					{
						if (shelter.TryGetValue((cx + dx) * 1000003L + (cz + dz), out List<Vector3> near) && near.Any(q => (q - piece.at).sqrMagnitude < TouchMetres * TouchMetres))
						{
							return true;
						}
					}
				}
				return false;
			};
			List<Piece> due = new List<Piece>(), worn = new List<Piece>();
			foreach (Piece piece in marked)
			{
				if (held(piece))
				{
					continue;
				}
				if (piece.stampAt <= nowUnix)
				{
					due.Add(piece);
				}
				else if (piece.stampAt - nowUnix <= WornHours * 3600.0)
				{
					worn.Add(piece);
				}
			}

			// 3. carried out
			int removed = 0;
			Dictionary<string, int> takenFrom = new Dictionary<string, int>();
			if (strike)
			{
				foreach (Piece piece in due.OrderBy(p => p.stampAt).Take(Math.Max(1, Configuration.decayMaxRemovals.Value)))
				{
					if (Remove(piece.id))
					{
						removed++;
						remaining[piece.stampEpisode] = remaining[piece.stampEpisode] - 1;
						string label = byId.TryGetValue(piece.stampEpisode, out Episode of) ? $"{TierName(of.tier)} of {Clean(of.owner)}" : "?";
						takenFrom[label] = takenFrom.TryGetValue(label, out int taken) ? taken + 1 : 1;
					}
				}
				foreach (Piece piece in worn)
				{
					Wear(piece);
				}
				if (removed > 0)
				{
					Say(did, $"{removed} piece(s) taken down ({string.Join(", ", takenFrom.Select(kv => kv.Key + " x" + kv.Value))}), {due.Count - removed} more due and waiting for the next pass");
				}
			}
			else if (mode == Mode.DryRun && due.Count != s_dryDue)
			{
				s_dryDue = due.Count;
				if (due.Count > 0)
				{
					Say(did, $"(dry run) {due.Count} piece(s) marked by an earlier run would be taken down now");
				}
			}
			if (write)
			{
				// an episode with nothing marked left is over
				foreach (Episode episode in s_episodes.Values.ToList())
				{
					episode.pieces = remaining.TryGetValue(episode.Id, out int left) ? left : 0;
					if (episode.pieces <= 0 && now - episode.started > TimeSpan.FromSeconds(Math.Min(60.0, episode.fadeSeconds)))
					{
						s_episodes.Remove(episode.key);
						s_dirty = true;
						Say(did, $"the decay of the {TierName(episode.tier)} of {Clean(episode.owner)} ({episode.key}) is over: " + (heldAgain.ContainsKey(episode.Id) ? "what was left of it is held again" : "nothing of it is left"));
					}
				}
			}

			// 4. the unmarked pieces: the claim on each that lapses last, grouped by the base it belongs to
			Dictionary<string, Base> bases = new Dictionary<string, Base>();
			foreach (Piece piece in unmarked)
			{
				Candidate best = bestOf[piece.id];
				if (!bases.TryGetValue(best.key, out Base b))
				{
					bases[best.key] = b = new Base { candidate = best };
				}
				b.pieces.Add(piece);
			}

			Dictionary<long, List<string>> urgent = new Dictionary<long, List<string>>();
			Dictionary<long, List<string>> soon = new Dictionary<long, List<string>>();
			int begun = 0, postponed = 0;
			foreach (Base b in bases.Values.OrderBy(x => x.candidate.start).ThenBy(x => x.candidate.key, StringComparer.Ordinal))
			{
				Candidate c = b.candidate;
				double daysTo = (c.start - now).TotalDays;
				if (now >= c.start)
				{
					// Someone who holds the base is in the game right now (back before the ten minutes are up): it does not start under their feet.
					if (c.keepers.Any(s_online.Contains))
					{
						if (write)
						{
							foreach (long keeper in c.keepers)
							{
								AddStatus(urgent, keeper, $"Your {TierName(c.tier)} is about to start to decay: stay in the game {Configuration.decayActiveMinutes.Value:0} minutes and it will not.");
							}
						}
						continue;
					}
					if (strike)
					{
						if (begun >= MaxBeginsPerPass)
						{
							postponed++;
						}
						else
						{
							Begin(b, now, did);
							begun++;
						}
					}
					else if (mode == Mode.DryRun && s_logged.Add($"dry/{c.key}/{nowUnix / 86400}"))
					{
						Say(did, $"(dry run) the {TierName(c.tier)} of {Clean(c.owner)} would start to decay now: {b.pieces.Count} piece(s){Kinds(b.pieces)}: {Spread(b)}, gone in {TierFade(c.tier):0.#} days");
					}
				}
				else if (daysTo <= Math.Min(WarnDays, TierGrace(c.tier) / 2.0) && write)
				{
					foreach (long keeper in c.keepers)
					{
						AddStatus(soon, keeper, $"Your {TierName(c.tier)} ({Place(b)}) starts to decay in {Math.Max(0.1, daysTo):0.#} days: log in and stay {Configuration.decayActiveMinutes.Value:0} minutes.");
					}
				}
			}
			if (postponed > 0)
			{
				Say(did, $"{postponed} more base(s) are due to start; at most {MaxBeginsPerPass} start in a pass, the rest in the next ones");
			}
			if (write)
			{
				foreach (Episode episode in s_episodes.Values)
				{
					double left = Math.Max(0.0, (episode.started + TimeSpan.FromSeconds(episode.fadeSeconds) - now).TotalDays);
					foreach (long keeper in episode.keepers)
					{
						// a keeper who has come back is not told their base is decaying
						if (KeeperActivity(episode.creator, keeper) > episode.started)
						{
							continue;
						}
						AddStatus(urgent, keeper, $"Your {TierName(episode.tier)} is decaying: it is gone in {left:0.#} days. Stay in the game {Configuration.decayActiveMinutes.Value:0} minutes to stop it.");
					}
				}
			}
			s_status.Clear();
			foreach (long keeper in urgent.Keys.Union(soon.Keys))
			{
				List<string> lines = new List<string>();
				if (urgent.TryGetValue(keeper, out List<string> first))
				{
					lines.AddRange(first);
				}
				if (soon.TryGetValue(keeper, out List<string> second))
				{
					lines.AddRange(second);
				}
				s_status[keeper] = lines;
			}

			// 5. players nobody builds for any more are forgotten (a player who comes back starts as a new one), the report for the console
			HashSet<long> referenced = new HashSet<long>(pieces.Select(p => p.creator));
			foreach (Protector p in protectors)
			{
				referenced.UnionWith(p.keepers);
			}
			foreach (Episode e in s_episodes.Values)
			{
				referenced.UnionWith(e.keepers);
			}
			List<long> forgotten = s_people.Values.Where(p => !referenced.Contains(p.id) && !p.admin && !s_exempt.Contains(p.id) && !s_online.Contains(p.id) && p.lastActive < now.AddDays(-30)).Select(p => p.id).ToList();
			foreach (long id in forgotten)
			{
				s_people.Remove(id);
				s_dirty = true;
			}
			s_report = BuildReport(bases.Values.ToList(), now);
			string summary = $"{bases.Count} base(s) not decaying, {s_episodes.Count} decaying, {pieces.Count} piece(s) of players in the world";
			if (summary != s_lastSummary)
			{
				s_lastSummary = summary;
				ServersidePlugin.logger.LogInfo($"Decay ({mode}): {summary}; scan took {watch.Elapsed.TotalMilliseconds:0.0} ms");
			}
			if (s_dirty)
			{
				Save();
			}
			return string.Join("\n", did);
		}

		// An admin the server has not seen online yet, but the character guard has (it keeps the account of every character).
		private static void LearnAdmins()
		{
			foreach (Person person in s_people.Values)
			{
				if (!person.admin)
				{
					string account = CharacterGuard.AccountOf(person.id);
					if (account != null && ZNet.instance.IsAdmin(account))
					{
						person.admin = true;
						s_dirty = true;
					}
				}
			}
		}

		// What a base is, for a player: loose pieces have no place; the others are named by where their middle is.
		private static string Place(Base b)
		{
			if (b.candidate.tier == Tier.None)
			{
				return $"{b.pieces.Count} loose piece(s)";
			}
			float x = 0f, z = 0f;
			foreach (Piece piece in b.pieces)
			{
				x += piece.at.x;
				z += piece.at.z;
			}
			return $"{b.pieces.Count} piece(s) around {x / b.pieces.Count:0},{z / b.pieces.Count:0}";
		}

		// Where the pieces of a base are, for the console and the log: the three biggest 100 m squares.
		private static string Spread(Base b)
		{
			List<IGrouping<long, Piece>> squares = b.pieces.GroupBy(p => (long)Math.Floor(p.at.x / 100f) * 100000L + (long)Math.Floor(p.at.z / 100f)).OrderByDescending(g => g.Count()).ToList();
			string text = string.Join("; ", squares.Take(3).Select(g => $"{g.Count()} around {g.Average(p => p.at.x):0},{g.Average(p => p.at.z):0}"));
			return squares.Count > 3 ? text + $"; and {squares.Count - 3} more place(s)" : text;
		}

		// What is in a base that an operator would not want to lose unseen.
		private static string Kinds(IEnumerable<Piece> pieces)
		{
			int portals = 0, ships = 0, chests = 0;
			foreach (Piece p in pieces)
			{
				if (s_portals.Contains(p.prefab))
				{
					portals++;
				}
				else if (s_ships.Contains(p.prefab))
				{
					ships++;
				}
				else if (s_containers.Contains(p.prefab))
				{
					chests++;
				}
			}
			List<string> parts = new List<string>();
			if (portals > 0)
			{
				parts.Add(portals + " portal(s)");
			}
			if (ships > 0)
			{
				parts.Add(ships + " ship(s) or cart(s)");
			}
			if (chests > 0)
			{
				parts.Add(chests + " chest(s)");
			}
			return parts.Count == 0 ? "" : " [" + string.Join(", ", parts) + "]";
		}

		private static void AddStatus(Dictionary<long, List<string>> status, long keeper, string line)
		{
			if (!status.TryGetValue(keeper, out List<string> lines))
			{
				status[keeper] = lines = new List<string>();
			}
			if (!lines.Contains(line))
			{
				lines.Add(line);
			}
		}

		// c replaces best when it lapses later; then the one that lasts longer, the higher kind, and at last the key, so ties are not left to the order of a dictionary.
		private static bool Better(Candidate c, Candidate best)
		{
			int by = c.start.CompareTo(best.start);
			if (by != 0)
			{
				return by > 0;
			}
			by = c.end.CompareTo(best.end);
			if (by != 0)
			{
				return by > 0;
			}
			if (c.tier != best.tier)
			{
				return c.tier > best.tier;
			}
			return string.CompareOrdinal(c.key, best.key) < 0;
		}

		/*
			The claim on a piece that lapses last: a piece is protected until then (start is in the future), and when it has lapsed
			the piece belongs to that claim's base. The claims: the wards that are on and the workbenches that cover it, and its builder alone.
			A portal and the portal it is connected to are one thing: the pair lives while either end is held.
		*/
		private static Candidate Best(Piece piece, List<Protector> protectors)
		{
			Candidate own = BestOne(piece, protectors);
			if (!piece.partner.IsNone() && s_pieceById.TryGetValue(piece.partner, out Piece other))
			{
				Candidate theirs = BestOne(other, protectors);
				if (Better(theirs, own))
				{
					return theirs;
				}
			}
			return own;
		}

		private static Candidate BestOne(Piece piece, List<Protector> protectors)
		{
			Candidate best = default(Candidate);
			bool any = false;
			foreach (Protector p in protectors)
			{
				if (!Covers(piece.at, p.at, p.radius))
				{
					continue;
				}
				// the builder's own pieces are held by the builder's exemption; everybody else's only by what the keepers really did
				DateTime activity = piece.creator == p.creator ? p.activity : p.realActivity;
				DateTime start = activity.AddDays(DaysOf(TierGrace(p.tier)));
				Candidate c = new Candidate
				{
					key = KeyOf(p.tier, p.creator, p.at),
					tier = p.tier,
					creator = p.creator,
					keepers = p.keepers,
					activity = activity,
					start = start,
					end = start.AddDays(DaysOf(TierFade(p.tier))),
					ward = p.tier == Tier.Ward ? p.id : ZDOID.None,
					owner = p.name,
				};
				if (!any || Better(c, best))
				{
					best = c;
					any = true;
				}
			}
			DateTime own = Activity(piece.creator);
			DateTime ownStart = own.AddDays(DaysOf(TierGrace(Tier.None)));
			Candidate alone = new Candidate
			{
				key = "N:" + Inv(piece.creator),
				tier = Tier.None,
				creator = piece.creator,
				keepers = new HashSet<long> { piece.creator },
				activity = own,
				start = ownStart,
				end = ownStart.AddDays(DaysOf(TierFade(Tier.None))),
				ward = ZDOID.None,
				owner = Name(piece.creator),
			};
			return !any || Better(alone, best) ? alone : best;
		}

		private static void Stamp(ZDOID id, long at, int episodeId)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(id);
			if (zdo == null)
			{
				return;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set("sgg_decay_at", at);
			zdo.Set(s_keyEpisode, episodeId);
		}

		// The base begins to decay: every piece gets its time, the ward first. A base whose episode is still on (pieces that were
		// unmarked in the meantime and have lapsed again) takes the new pieces into it.
		private static void Begin(Base b, DateTime now, List<string> did)
		{
			Candidate c = b.candidate;
			double fade = DaysOf(TierFade(c.tier)) * 86400.0;
			bool joined = s_episodes.TryGetValue(c.key, out Episode episode);
			if (!joined)
			{
				episode = new Episode { key = c.key, tier = c.tier, started = now, fadeSeconds = fade, creator = c.creator, keepers = new HashSet<long>(c.keepers), owner = Clean(c.owner) };
				s_episodes[c.key] = episode;
			}
			s_dirty = true;
			int id = episode.Id;
			long from = Unix(now);
			long end = Math.Max(from, Unix(episode.started) + (long)episode.fadeSeconds);
			// The ward goes first, but not at once: within a day (a tenth of the fade), so that a mistake can be put right before it is gone for good.
			long wardAt = from + Math.Min(WardGraceSeconds, (end - from) / 10);
			bool hasWard = false;
			List<Piece> rest = new List<Piece>();
			foreach (Piece piece in b.pieces)
			{
				if (c.tier == Tier.Ward && piece.id == c.ward)
				{
					hasWard = true;
					Stamp(piece.id, wardAt, id);
					continue;
				}
				rest.Add(piece);
			}
			// the top first, what holds things after, the workbench last
			Func<Piece, int> rank = p => s_bench.Contains(p.prefab) ? 2 : s_holders.Contains(p.prefab) ? 1 : 0;
			List<Piece> order = rest.OrderBy(rank).ThenByDescending(p => p.at.y - WorldGenerator.instance.GetHeight(p.at.x, p.at.z)).ToList();
			long first = hasWard ? wardAt : from;
			for (int i = 0; i < order.Count; i++)
			{
				Stamp(order[i].id, first + (long)(Math.Max(0L, end - first) * (i + 1.0) / order.Count), id);
			}
			episode.pieces += order.Count + (hasWard ? 1 : 0);
			Say(did, $"the {TierName(c.tier)} of {episode.owner} ({c.key}) {(joined ? "goes on decaying" : "starts to decay")}: {string.Join(", ", c.keepers.Select(Name))} last active {c.activity:yyyy-MM-dd}; {order.Count + (hasWard ? 1 : 0)} piece(s){Kinds(b.pieces)} ({Spread(b)}) go {(joined ? "before the end of its fade" : $"over the next {fade / 86400.0:0.#} days")}{(hasWard ? "; its ward is the first to go" : "")}");
		}

		// The mark off a piece; one that was worn out is whole again. The mark is overwritten with zeros, not taken out: a game that holds
		// the old mark is sent the zeros (a key that is missing from what it is sent is not removed from its copy).
		private static void Unstamp(ZDO zdo, int prefab)
		{
			if (zdo == null)
			{
				return;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(s_keyAt, 0L);
			zdo.Set(s_keyEpisode, 0);
			float max = MaxHealth(prefab);
			if (max > 0f && Math.Abs(zdo.GetFloat(ZDOVars.s_health, max) - max * WornFraction) < 0.5f)
			{
				zdo.Set(ZDOVars.s_health, max);
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, zdo.m_uid, "RPC_HealthChanged", max);
			}
		}

		// Taken down without a trace, the way the hammer takes down a piece that is not a WearNTear (it does not ask Piece.CanBeRemoved).
		private static bool Remove(ZDOID id)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(id);
			if (zdo == null)
			{
				return false;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZNetView view = ZNetScene.instance.FindInstance(zdo);
			if (view)
			{
				ZNetScene.instance.Destroy(view.gameObject);
			}
			else
			{
				ZDOMan.instance.DestroyZDO(zdo);
			}
			return true;
		}

		// A piece that goes within a day is worn out: the game shows broken wood and stone below a quarter of the health.
		private static bool Wear(Piece piece)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(piece.id);
			float max = MaxHealth(piece.prefab);
			if (zdo == null || max <= 0f)
			{
				return false;
			}
			float limit = max * WornFraction;
			if (zdo.GetFloat(ZDOVars.s_health, max) <= limit)
			{
				return false;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(ZDOVars.s_health, limit);
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, zdo.m_uid, "RPC_HealthChanged", limit);
			return true;
		}

		private static void Say(List<string> did, string line)
		{
			did.Add(line);
			Log(line);
		}

		private static void Log(string line)
		{
			ServersidePlugin.logger.LogInfo("Decay: " + line);
			GuardLog.Write("decay: " + line);
		}

		// Every mark off, what is worn mended, the episodes forgotten: the way back from a run that should not have been.
		private static string Clear()
		{
			int cleared = 0;
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToList())
			{
				if (zdo.GetLong(s_keyAt, 0L) != 0L || zdo.GetInt(s_keyEpisode, 0) != 0)
				{
					Unstamp(zdo, zdo.GetPrefab());
					cleared++;
				}
			}
			int episodes = s_episodes.Count;
			s_episodes.Clear();
			s_dirty = true;
			Save();
			Log($"cleared (console): {cleared} piece(s) unmarked, {episodes} episode(s) forgotten");
			return $"{cleared} piece(s) unmarked, {episodes} episode(s) forgotten";
		}

		// ---- the file -----------------------------------------------------------------------------------------------------

		private static void Load()
		{
			s_loaded = true;
			s_people.Clear();
			s_episodes.Clear();
			s_exempt.Clear();
			World world = ZNet.World;
			s_path = Path.Combine(SaveSystem.GetWorldsSaveRootPath(world.m_fileSource), world.m_name + ".decay.txt");
			string read = File.Exists(s_path) ? s_path : File.Exists(s_path + ".tmp") ? s_path + ".tmp" : null;
			bool hasOrigin = false;
			long fileWorld = 0L;
			if (read != null)
			{
				try
				{
					foreach (string line in File.ReadAllLines(read))
					{
						if (line.Length == 0 || line[0] == '#')
						{
							continue;
						}
						string[] f = line.Split('\t');
						switch (f[0])
						{
							case "firstrun":
								s_origin = FromUnix(long.Parse(f[1], CultureInfo.InvariantCulture));
								hasOrigin = true;
								break;
							case "uptime":
								s_upBase = double.Parse(f[1], CultureInfo.InvariantCulture);
								break;
							case "world":
								fileWorld = long.Parse(f[1], CultureInfo.InvariantCulture);
								break;
							case "person":
								long pid = long.Parse(f[1], CultureInfo.InvariantCulture);
								s_people[pid] = new Person
								{
									id = pid,
									lastActive = FromUnix(long.Parse(f[2], CultureInfo.InvariantCulture)),
									name = f.Length > 3 ? f[3] : "",
									admin = f.Length > 4 && f[4] == "1",
								};
								break;
							case "exempt":
								s_exempt.Add(long.Parse(f[1], CultureInfo.InvariantCulture));
								break;
							case "episode":
								s_episodes[f[1]] = new Episode
								{
									key = f[1],
									tier = (Tier)int.Parse(f[2], CultureInfo.InvariantCulture),
									started = FromUnix(long.Parse(f[3], CultureInfo.InvariantCulture)),
									fadeSeconds = double.Parse(f[4], CultureInfo.InvariantCulture),
									creator = long.Parse(f[5], CultureInfo.InvariantCulture),
									keepers = new HashSet<long>(f[6].Split(',').Where(x => x.Length > 0).Select(x => long.Parse(x, CultureInfo.InvariantCulture))),
									owner = f.Length > 7 ? f[7] : "",
								};
								break;
						}
					}
					if (!hasOrigin && (s_people.Count > 0 || s_episodes.Count > 0))
					{
						throw new InvalidDataException("the first line ('firstrun') is missing, so the times in it mean nothing");
					}
				}
				catch (Exception e)
				{
					// What cannot be read must not be written over: decay stays off until the server restarts.
					s_failed = true;
					ServersidePlugin.logger.LogError($"Decay: cannot read {s_path} ({e.Message}); decay stays off until it is fixed or removed and the server restarted");
					return;
				}
				if (fileWorld != 0L && fileWorld != world.m_uid)
				{
					// The file belongs to another world of the same name (wiped and made anew): its times and its players say nothing about this one.
					string other = s_path + ".other-world";
					try
					{
						File.Copy(read, other, true);
					}
					catch (Exception e)
					{
						ServersidePlugin.logger.LogWarning($"Decay: could not keep a copy of the file of another world: {e.Message}");
					}
					ServersidePlugin.logger.LogWarning($"Decay: {Path.GetFileName(read)} belongs to another world of the name {world.m_name} (a copy is kept as {Path.GetFileName(other)}); decay starts from nothing");
					s_people.Clear();
					s_episodes.Clear();
					s_exempt.Clear();
					hasOrigin = false;
					read = null;
				}
			}
			if (!hasOrigin)
			{
				// A new file: the server's own clock starts now, and nobody is behind on it.
				s_origin = DateTime.UtcNow;
				s_upBase = 0.0;
				s_dirty = true;
			}
			s_watch.Restart();
			ServersidePlugin.logger.LogInfo($"Decay: {s_people.Count} player(s) and {s_episodes.Count} base(s) decaying in {Path.GetFileName(s_path)}; the server has run {TimeSpan.FromSeconds(s_upBase).TotalDays:0.#} days under decay");
		}

		private static void Save()
		{
			if (s_path == null || s_failed || !s_loaded)
			{
				return;
			}
			s_dirty = false;
			try
			{
				List<string> lines = new List<string>
				{
					"# Sarkastic.gg Dedicated Simulation: base decay. Tab separated (firstrun, uptime, world / person id lastActive name admin / exempt id / episode key tier started fadeSeconds creator keepers owner). Times are Unix seconds on the server's own clock: the first run plus the time the server has run under decay. Edit only while the server is stopped.",
					"firstrun\t" + Inv(Unix(s_origin)),
					"uptime\t" + Inv((long)Math.Max(0.0, (Now() - s_origin).TotalSeconds)),
					"world\t" + Inv(ZNet.World.m_uid),
				};
				lines.AddRange(s_people.Values.OrderBy(p => p.id).Select(p => string.Join("\t", new[] { "person", Inv(p.id), Inv(Unix(p.lastActive)), p.name, p.admin ? "1" : "0" })));
				lines.AddRange(s_exempt.OrderBy(i => i).Select(i => "exempt\t" + Inv(i)));
				lines.AddRange(s_episodes.Values.OrderBy(e => e.key, StringComparer.Ordinal).Select(e => string.Join("\t", new[]
				{
					"episode", e.key, Inv((int)e.tier), Inv(Unix(e.started)), Inv((long)e.fadeSeconds), Inv(e.creator), string.Join(",", e.keepers.Select(Inv)), e.owner,
				})));
				string temp = s_path + ".tmp";
				File.WriteAllLines(temp, lines);
				if (File.Exists(s_path))
				{
					File.Delete(s_path);
				}
				File.Move(temp, s_path);
				if (s_saveFailing)
				{
					s_saveFailing = false;
					ServersidePlugin.logger.LogInfo($"Decay: {Path.GetFileName(s_path)} can be written again");
				}
			}
			catch (Exception e)
			{
				s_dirty = true;
				s_saveFailing = true;
				ServersidePlugin.logger.LogWarning($"Decay: could not save {s_path}: {e.Message}");
			}
		}

		// ---- the tick and the console -------------------------------------------------------------------------------------

		public static void Tick()
		{
			if (!ZNet.instance || !ZNet.instance.IsServer())
			{
				return;
			}
			if (Configuration.decayMode.Value == Mode.Off)
			{
				// The decay clock stands still while decay is off.
				if (s_watch.IsRunning)
				{
					s_watch.Stop();
				}
				return;
			}
			if (s_failed || !Ready())
			{
				return;
			}
			if (!s_loaded)
			{
				Load();
				if (s_failed)
				{
					return;
				}
				ServersidePlugin.logger.LogInfo($"Decay: {Configuration.decayMode.Value}. Wards {Configuration.decayWardGraceDays.Value:0.#} + {Configuration.decayWardFadeDays.Value:0.#} days, workbench (within {Configuration.decayWorkbenchRadius.Value:0.#} m) {Configuration.decayWorkbenchGraceDays.Value:0.#} + {Configuration.decayWorkbenchFadeDays.Value:0.#}, nothing {Configuration.decayNoneGraceDays.Value:0.#} + {Configuration.decayNoneFadeDays.Value:0.#}; "
					+ $"a player is active after {Configuration.decayActiveMinutes.Value:0.#} minutes in the game; {s_exempt.Count} player(s) exempt");
				s_nextScan = Time.time + 60f;
			}
			else if (!s_watch.IsRunning)
			{
				s_watch.Start();
			}
			if (Time.time >= s_nextTrack)
			{
				Track();
			}
			if (Time.time >= s_nextScan)
			{
				Scan();
			}
			if (Time.time >= s_nextSave)
			{
				// The file also keeps the uptime, which moves on whatever else changes.
				s_nextSave = Time.time + SaveSeconds;
				Save();
			}
		}

		private static List<string> BuildReport(List<Base> bases, DateTime now)
		{
			List<string> lines = new List<string>();
			foreach (Base b in bases.OrderBy(x => x.candidate.start))
			{
				Candidate c = b.candidate;
				double days = (c.start - now).TotalDays;
				string when = days > 36500 ? "never (a keeper is exempt)" : days <= 0 ? "starts to decay at the next pass" : $"starts to decay in {days:0.#} days, gone {days + TierFade(c.tier):0.#} days from now";
				string last = days > 36500 ? "" : $" (last active {c.activity:yyyy-MM-dd})";
				lines.Add($"  {TierName(c.tier)} of {Clean(c.owner)}, {b.pieces.Count} piece(s){Kinds(b.pieces)}: {Spread(b)}, kept by {string.Join(", ", c.keepers.Select(Name))}{last}: {when}");
			}
			foreach (Episode e in s_episodes.Values.OrderBy(x => x.started))
			{
				lines.Add($"  {TierName(e.tier)} of {Clean(e.owner)}: DECAYING since {e.started:yyyy-MM-dd HH:mm}, gone {(e.started + TimeSpan.FromSeconds(e.fadeSeconds) - now).TotalDays:0.#} days from now, {e.pieces} piece(s) left; held again if its builder, or another claim on a piece, is active");
			}
			return lines;
		}

		private static string Find(string who, out long id)
		{
			id = 0L;
			if (long.TryParse(who, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
			{
				id = numeric;
				return null;
			}
			List<Person> found = s_people.Values.Where(p => string.Equals(p.name, who, StringComparison.OrdinalIgnoreCase)).ToList();
			if (found.Count == 1)
			{
				id = found[0].id;
				return null;
			}
			return found.Count == 0 ? $"no player called '{who}' is known (a player id works too)" : $"'{who}' is more than one player: use the id";
		}

		public static string Console(string[] words)
		{
			if (Configuration.decayMode.Value == Mode.Off)
			{
				return "decay is off ([Decay] Mode)";
			}
			if (s_failed)
			{
				return "decay is stopped: its file could not be read (see the log)";
			}
			if (!s_loaded)
			{
				return "decay has not started yet";
			}
			string sub = words.Length > 1 ? words[1].ToLowerInvariant() : "";
			if (sub == "away" || sub == "exempt" || sub == "unexempt")
			{
				int need = sub == "away" ? 4 : 3;
				if (words.Length < need)
				{
					return sub == "away" ? "usage: decay away <player|all> <days>" : $"usage: decay {sub} <player>";
				}
				string who = string.Join(" ", words, 2, words.Length - (sub == "away" ? 3 : 2));
				double days = 0.0;
				if (sub == "away" && (!double.TryParse(words[words.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out days) || double.IsNaN(days) || days < 0 || days > 365))
				{
					return "days must be a number between 0 and 365";
				}
				if (sub == "away" && who.ToLowerInvariant() == "all")
				{
					// only ever later: a longer holiday somebody has is not cut short
					DateTime until = Now().AddDays(days);
					foreach (Person person in s_people.Values)
					{
						if (person.lastActive < until)
						{
							person.lastActive = until;
						}
					}
					s_dirty = true;
					Save();
					Log($"everybody known ({s_people.Count}) is away for {days:0.#} days (console)");
					return $"{s_people.Count} player(s) count as active until {until:yyyy-MM-dd HH:mm} at least; a player the server meets later is not covered";
				}
				string problem = Find(who, out long id);
				if (problem != null)
				{
					return problem;
				}
				if (sub == "away")
				{
					Person person = Known(id);
					person.lastActive = Now().AddDays(days);
					s_dirty = true;
					Save();
					Log($"{Name(id)} is away for {days:0.#} days (console)");
					return $"{Name(id)} counts as active until {person.lastActive:yyyy-MM-dd HH:mm}";
				}
				if (sub == "exempt")
				{
					s_exempt.Add(id);
				}
				else
				{
					s_exempt.Remove(id);
					if (s_people.TryGetValue(id, out Person person))
					{
						person.admin = false;
					}
				}
				s_dirty = true;
				Save();
				Log($"{Name(id)} is {(sub == "exempt" ? "left out of" : "put back under")} decay (console)");
				return $"{Name(id)} is {(sub == "exempt" ? "exempt from" : "under")} decay" + (sub == "unexempt" && Configuration.decayExemptAdmins.Value ? " (until the server sees them online as an admin again)" : "");
			}
			if (sub == "clear")
			{
				return Clear();
			}
			if (sub == "scan")
			{
				string did = Scan();
				return (did.Length > 0 ? did + "\n" : "") + Describe();
			}
			return Describe();
		}

		public static string Describe()
		{
			DateTime now = Now();
			List<string> lines = new List<string>
			{
				$"decay {Configuration.decayMode.Value}: {s_people.Count} player(s) known, {s_exempt.Count} exempt, {s_episodes.Count} base(s) decaying; the decay clock reads {now:yyyy-MM-dd HH:mm} (it counts only the time the server runs under decay, so dates here and in the log are on it, not on the calendar)",
			};
			foreach (Person p in s_people.Values.OrderBy(x => x.lastActive))
			{
				double idle = (now - p.lastActive).TotalDays;
				lines.Add($"  {Name(p.id)} ({p.id}): " + (idle < 0 ? $"away for {-idle:0.#} more days" : $"{idle:0.#} days since last active") + (IsExempt(p.id) ? (s_exempt.Contains(p.id) ? ", exempt" : ", exempt (admin)") : ""));
			}
			lines.AddRange(s_report);
			return string.Join("\n", lines);
		}
	}
}
