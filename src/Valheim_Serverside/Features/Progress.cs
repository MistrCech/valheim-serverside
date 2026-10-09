using System.Collections.Generic;
using System.Linq;

namespace Valheim_Serverside.Features
{
	/*
		How many bosses a player has beaten, in the game's order, read from the one thing a vanilla
		client tells the server about its own progress: the raids it is still "ready for", which the
		client works out from the items it knows and its own keys and sends as
		ZNet.m_serverSyncedPlayerData["possibleEvents"] (RandEventSystem.PlayerIsReadyForEvent).

		The main raids form a chain -- each needs the previous boss's drop known and stops once the
		next boss's drop is -- so the first chain raid still listed tells how far the player is. The
		forest troll and surtling raids, which need the Elder's and Bonemass's drop actually known,
		confirm those two steps: the chain raids also stop for a player who merely holds that boss's
		power, and a power can be taken at a stone where someone else hung the trophy. Checked
		against a port of PlayerIsReadyForEvent over the game's raid data (1.0.16): exact for every
		progress level; taking other people's powers cannot lift a player at or below the Elder;
		above that, only taking the Queen's power and every power before it in the chain (from
		Bonemass's level Moder's too, from Moder's Yagluth's) makes a player look past the Queen.

		Which drop counts (the game's drop tables, 1.0.16): for Eikthyr his trophy or a Hard Antler,
		the Elder's trophy or the Swamp Key, Bonemass's trophy or the Wishbone (everyone at those two
		kills gets one), Moder's trophy or a Dragon Tear; for Yagluth only a Torn Spirit (or a Wisp or
		Demister from the Mistlands), for the Queen only a Majestic Carapace, not their trophies. Fader
		leaves nothing of his own in the list. A player whose list has not arrived yet counts as
		having beaten nothing.

		Used by the night spawn guard and the guardian stones.
	*/
	public static class Progress
	{
		public static readonly string[] BossNames = { "nothing", "Eikthyr", "the Elder", "Bonemass", "Moder", "Yagluth", "the Queen" };

		// The raids the progress is read from; every one must exist in the game with a per-player condition.
		private static readonly string[] s_evidence = { "army_eikthyr", "army_theelder", "foresttrolls", "army_bonemass", "surtlings", "army_moder", "army_goblin", "army_gjall", "army_seekers", "gemgoblin" };

		// Raids with no per-player condition at all: every vanilla client lists them, so a list without any is not one.
		private static HashSet<string> s_alwaysReady;
		private static bool s_checked, s_readable;

		/*
			The game's raid data must still be what the rule was worked out against; if it is not,
			whatever uses the rule stays out of the way rather than guess.
		*/
		public static bool Readable()
		{
			if (s_checked)
			{
				return s_readable;
			}
			if (!RandEventSystem.instance)
			{
				return false;
			}
			s_checked = true;
			List<RandomEvent> events = RandEventSystem.instance.m_events;
			string[] missing = s_evidence.Where(name => !events.Any(ev => ev.m_name == name && HasPlayerCondition(ev))).ToArray();
			s_alwaysReady = new HashSet<string>(events.Where(ev => !HasPlayerCondition(ev)).Select(ev => ev.m_name));
			s_readable = missing.Length == 0 && s_alwaysReady.Count > 0;
			if (!s_readable)
			{
				ServersidePlugin.logger.LogWarning($"Boss progress: the game's raid data is not what the progress rule was made for "
					+ $"(missing or changed: {(missing.Length > 0 ? string.Join(", ", missing) : "no raid without a per-player condition")}); "
					+ "the night spawn guard and the guardian stones do nothing and the game stays vanilla.");
			}
			return s_readable;
		}

		private static bool HasPlayerCondition(RandomEvent ev)
		{
			return ev.m_altRequiredKnownItems.Count > 0 || ev.m_altRequiredNotKnownItems.Count > 0 || ev.m_altNotRequiredPlayerKeys.Count > 0
				|| ev.m_altRequiredPlayerKeysAny.Count > 0 || ev.m_altRequiredPlayerKeysAll.Count > 0;
		}

		// Only after Readable() returned true.
		internal static int Beaten(string possibleEvents)
		{
			if (string.IsNullOrEmpty(possibleEvents))
			{
				return 0;
			}
			HashSet<string> ready = new HashSet<string>(possibleEvents.Split(','));
			if (!ready.Overlaps(s_alwaysReady))
			{
				return 0;
			}
			if (ready.Contains("army_eikthyr")) return 0;
			if (ready.Contains("army_theelder")) return 1;
			if (!ready.Contains("foresttrolls")) return 0; // past the Elder's raid without the Elder's drop: a power taken, not a kill
			if (ready.Contains("army_bonemass")) return 2;
			if (!ready.Contains("surtlings")) return 2; // the same for Bonemass
			if (ready.Contains("army_moder")) return 3;
			if (ready.Contains("army_goblin")) return 4;
			if (ready.Contains("army_gjall") || ready.Contains("army_seekers")) return 5;
			if (ready.Contains("gemgoblin")) return 3; // past Moder's and Yagluth's raids with neither shown by a drop: cannot tell which
			return 6;
		}

		// Whether the player's own list has reached the server yet.
		internal static bool Known(ZNetPeer peer)
		{
			return peer != null && peer.m_serverSyncedPlayerData.ContainsKey("possibleEvents");
		}

		// Only after Readable() returned true.
		internal static int Beaten(ZNetPeer peer)
		{
			return peer != null && peer.m_serverSyncedPlayerData.TryGetValue("possibleEvents", out string events) ? Beaten(events) : 0;
		}
	}
}
