using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Valheim_Serverside
{
	/*
		Commands read from the server's standard input.

		Own commands: `save`, `stop`, `players`, `give <item> <amount> <player>`, `broadcast <text>` (`\n` is a line break and
		`\u010d` the letter with that code, for a panel that passes nothing but ASCII and an old code page),
		`event <name> <player>`, `events`, `tp <player|all> <x> <z> | <x> <y> <z> | start | to <player>` (also `teleport`,
		`goto`), `pos [player|all]`, `heal <player|all> [amount]`, `spawn <prefab> [amount] [level] <player>`,
		`characters`, `allow <character>`, `decay ...` (base decay), `anticheat ...`, `help`. Anything else is passed to the game's own console
		(the Terminal a dedicated server has but never reads), so the vanilla server commands work
		too: `kick`, `ban`, `unban`, `banned`, `stopevent`, `randomevent`, `setkey`, `removekey`, and
		after `devcommands` the cheat commands that need no player: `skiptime`, `resetkeys`,
		`listkeys`, `env`, `tod`, `wind`. The game's own commands that act on "the player" (`spawn`, `god`,
		`goto`, `heal`, `event` without a target) cannot work on a dedicated server; `give`, `tp`, `heal`, `spawn`
		and `event <name> <player>` are the server's versions. `god`, `fly` and `ghost` have none: they live
		wholly in the player's game.

		A vanilla dedicated server does not read its standard input, so a panel such as AMP can
		only stop it by closing or killing the process. On Windows that skips the world save on
		shutdown, and everything since the last autosave is lost. With this, the panel can send
		`stop` instead (in AMP: App.ExitMethod=String, App.ExitString=stop), and `save` can be
		typed into its console or scheduled.

		Standard input is read on a background thread; commands run on the main thread from
		ServersidePlugin.Update.
	*/
	public static class ServerConsole
	{
		private const string Commands = "save | stop | players | give <item> <amount> <player> | broadcast <text> | event <name> <player> | events"
			+ " | tp <player|all> <x> <z> | tp <player|all> <x> <y> <z> | tp <player|all> start | tp <player|all> to <player> | pos [player|all] | heal <player|all> [amount] | spawn <prefab> [amount] [level] <player>"
			+ " | characters | allow <character> | decay [away <player|all> <days> | exempt <player> | unexempt <player> | scan] | anticheat [status | scan | revert [all]] | help"
			+ " -- anything else goes to the game console: kick, ban, unban, banned, stopevent, devcommands, skiptime ...";

		private static readonly ConcurrentQueue<string> s_commands = new ConcurrentQueue<string>();

		public static void Start()
		{
			Thread reader = new Thread(ReadLoop) { IsBackground = true, Name = "Dedicated Simulation console" };
			reader.Start();
			ServersidePlugin.logger.LogInfo("Console commands enabled on standard input: " + Commands);
		}

		private static void ReadLoop()
		{
			try
			{
				ConsoleInput.ReadLines(() => PluginConfiguration.Configuration.consoleInputCodePage.Value, (line, notice) =>
				{
					if (notice != null)
					{
						s_commands.Enqueue("" + notice);
					}
					line = line.Trim();
					if (line.Length > 0)
					{
						s_commands.Enqueue(line);
					}
				});
			}
			catch (Exception e)
			{
				s_commands.Enqueue("\0" + e.Message);
			}
		}

		/*
			Replies go to the log and to standard output: with BepInEx's console off (needed on
			Windows for standard input to reach the game) the log is not on standard output, and
			standard output is what a panel such as AMP shows.
		*/
		private static void Reply(string text)
		{
			ServersidePlugin.logger.LogInfo("Console: " + text);
			WriteOut(text);
		}

		private static void WriteOut(string text)
		{
			try
			{
				System.Console.Out.WriteLine("Console: " + text);
				System.Console.Out.Flush();
			}
			catch (Exception)
			{
				// No standard output; the log has it.
			}
		}

		public static void ProcessPending()
		{
			while (s_commands.TryDequeue(out string command))
			{
				Execute(command);
			}
			try
			{
				AdminCommands.Update(Reply);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Console: watching teleports failed: {e}");
			}
		}

		private static void Execute(string command)
		{
			if (command[0] == '\0')
			{
				ServersidePlugin.logger.LogWarning($"Console commands unavailable, cannot read standard input: {command.Substring(1)}");
				return;
			}
			if (command[0] == '')
			{
				ServersidePlugin.logger.LogWarning(command.Substring(1));
				WriteOut(command.Substring(1));
				return;
			}
			string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			bool worldLoaded = ZNet.instance && ZNet.instance.IsServer();
			try
			{
				switch (words[0].ToLowerInvariant())
				{
					case "save":
						Reply(AdminCommands.Save("console"));
						break;
					case "stop":
					case "quit":
					case "shutdown":
						// Quitting runs Game.OnApplicationQuit, which saves the world before shutting down.
						Reply("saving world and shutting down");
						Application.Quit();
						break;
					case "players":
						Reply(worldLoaded ? AdminCommands.Players() : "no world loaded");
						break;
					case "give":
						// give <item> <amount> <player name, may contain spaces>
						if (!worldLoaded)
						{
							Reply("no world loaded");
						}
						else if (words.Length < 4 || !AdminCommands.TryParseAmount(words[2], out int amount))
						{
							Reply("usage: give <item> <amount> <player>, e.g. give Copper 120 Ulf");
						}
						else
						{
							string name = string.Join(" ", words, 3, words.Length - 3);
							ZNetPeer target = AdminCommands.FindPlayer(name, out string problem);
							Reply(target == null ? problem : AdminCommands.Give(target, words[1], amount, "console"));
						}
						break;
					case "broadcast":
					case "say":
						if (!worldLoaded)
						{
							Reply("no world loaded");
						}
						else if (words.Length < 2)
						{
							Reply("usage: broadcast <text>");
						}
						else
						{
							Reply(AdminCommands.Broadcast(PlayerMessages.Unescape(command.Substring(words[0].Length).Trim()), "console"));
						}
						break;
					case "event":
						// event <name> <player name, may contain spaces>: starts the event at that player
						if (!worldLoaded)
						{
							Reply("no world loaded");
						}
						else if (words.Length < 3)
						{
							Reply("usage: event <name> <player>, e.g. event army_eikthyr Ulf; `events` lists the names");
						}
						else
						{
							string name = string.Join(" ", words, 2, words.Length - 2);
							ZNetPeer target = AdminCommands.FindPlayer(name, out string problem);
							Reply(target == null ? problem : AdminCommands.StartEvent(words[1], target, "console"));
						}
						break;
					case "tp":
					case "teleport":
					case "goto":
						Reply(worldLoaded ? Tp(words) : "no world loaded");
						break;
					case "pos":
					case "where":
						if (!worldLoaded)
						{
							Reply("no world loaded");
						}
						else
						{
							string problem = null;
							List<ZNetPeer> who = words.Length < 2 ? Online(null) : Players(string.Join(" ", words, 1, words.Length - 1), null, out problem);
							Reply(who == null ? problem : AdminCommands.Positions(who));
						}
						break;
					case "heal":
						Reply(worldLoaded ? Heal(words) : "no world loaded");
						break;
					case "spawn":
						Reply(worldLoaded ? Spawn(words) : "no world loaded");
						break;
					case "events":
						Reply(worldLoaded ? "events: " + AdminCommands.Events() : "no world loaded");
						break;
					case "characters":
						Reply(worldLoaded ? Features.CharacterGuard.Describe() : "no world loaded");
						break;
					case "allow":
						// allow <character name, may contain spaces>: lets in a character the guard turned away
						Reply(!worldLoaded ? "no world loaded" : words.Length < 2 ? "usage: allow <character>" : Features.CharacterGuard.Allow(string.Join(" ", words, 1, words.Length - 1)));
						break;
					case "decay":
						// decay: who is away and every base's stage; decay away|exempt|unexempt <player> ...; decay scan: a pass now
						Reply(worldLoaded ? Features.Decay.Console(words) : "no world loaded");
						break;
					case "anticheat":
						// anticheat: what it has found since the start; anticheat scan|revert: look through the stored world, take away what is set to On
						Reply(worldLoaded ? Features.AntiCheat.Console(words) : "no world loaded");
						break;
					case "help":
						Reply("commands: " + Commands);
						break;
					default:
						RunGameCommand(command, words[0]);
						break;
				}
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Console: '{command}' failed: {e}");
				Reply($"'{command}' failed: {e.Message}");
			}
		}

		private const string TpUsage = "usage: tp <player|all> <x> <z> | <x> <y> <z> | start | to <player>, e.g. tp Ulf -1170 -260, tp Ulf -1170,-260, tp all to Ulf";
		private const float WorldEdge = 10500f;

		private static List<ZNetPeer> Online(ZNetPeer except)
		{
			return ZNet.instance.GetPeers().Where(p => p.IsReady() && !string.IsNullOrEmpty(p.m_playerName) && p != except).ToList();
		}

		// `all` (everyone online but `except`) or one player by name
		private static List<ZNetPeer> Players(string name, ZNetPeer except, out string problem)
		{
			problem = null;
			if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
			{
				List<ZNetPeer> all = Online(except);
				if (all.Count == 0)
				{
					problem = except != null ? $"Nobody online but {except.m_playerName}" : "No players online";
					return null;
				}
				return all;
			}
			ZNetPeer one = AdminCommands.FindPlayer(name, out problem);
			if (one != null && one == except)
			{
				problem = $"{one.m_playerName} is already there";
				return null;
			}
			return one != null ? new List<ZNetPeer> { one } : null;
		}

		private static string Join(string[] words, int from, int count)
		{
			return count > 0 ? string.Join(" ", words, from, count) : "";
		}

		// tp <player|all> <x> <z> | <x> <y> <z> | <x,z> | <x,y,z> | start | to <player>; names may contain spaces.
		private static string Tp(string[] words)
		{
			if (words.Length < 3)
			{
				return TpUsage;
			}
			string problem = null;
			int n = words.Length;
			for (int i = 2; i < n - 1; i++)
			{
				if (!string.Equals(words[i], "to", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				ZNetPeer dest = AdminCommands.FindPlayer(Join(words, i + 1, n - i - 1), out string destProblem);
				if (dest == null)
				{
					problem = problem ?? destProblem;
					continue;
				}
				List<ZNetPeer> movers = Players(Join(words, 1, i - 1), dest, out string moverProblem);
				if (movers == null)
				{
					problem = moverProblem;
					continue;
				}
				ZDO character = ZDOMan.instance.GetZDO(dest.m_characterID);
				Quaternion? facing = character != null ? character.GetRotation() : (Quaternion?)null;
				return AdminCommands.Teleport(movers, AdminCommands.PositionOf(dest) + Vector3.up * 0.3f, facing, dest.m_playerName, "console");
			}
			if (problem != null)
			{
				return problem;
			}
			if (string.Equals(words[n - 1], "start", StringComparison.OrdinalIgnoreCase))
			{
				List<ZNetPeer> movers = Players(Join(words, 1, n - 2), null, out problem);
				if (movers == null)
				{
					return problem;
				}
				return AdminCommands.TryStart(out Vector3 start) ? AdminCommands.Teleport(movers, start, null, "the start", "console") : "This world has no start (StartTemple)";
			}
			// The forms with coordinates, the first whose name part is a player wins: x,y,z or x,z as one word, then x y z, then x z.
			List<KeyValuePair<int, float[]>> forms = new List<KeyValuePair<int, float[]>>();
			string[] parts = words[n - 1].Split(',');
			if (parts.Length >= 2 && parts.Length <= 3)
			{
				float[] v = parts.Select(t => AdminCommands.TryParseNumber(t, out float f) ? f : float.NaN).ToArray();
				if (!v.Any(float.IsNaN))
				{
					forms.Add(new KeyValuePair<int, float[]>(n - 2, v));
				}
			}
			for (int count = 3; count >= 2; count--)
			{
				if (n - 1 - count < 1)
				{
					continue;
				}
				float[] v = new float[count];
				bool numbers = true;
				for (int k = 0; k < count && numbers; k++)
				{
					numbers = AdminCommands.TryParseNumber(words[n - count + k], out v[k]);
				}
				if (numbers)
				{
					forms.Add(new KeyValuePair<int, float[]>(n - 1 - count, v));
				}
			}
			if (forms.Count == 0)
			{
				return TpUsage;
			}
			foreach (KeyValuePair<int, float[]> form in forms)
			{
				List<ZNetPeer> movers = Players(Join(words, 1, form.Key), null, out string formProblem);
				if (movers == null)
				{
					problem = problem ?? formProblem;
					continue;
				}
				float[] v = form.Value;
				Vector3 to = v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : new Vector3(v[0], AdminCommands.GotoHeight, v[1]);
				if (Mathf.Abs(to.x) > WorldEdge || Mathf.Abs(to.z) > WorldEdge)
				{
					return $"{to.x:0},{to.z:0} is outside the world (it reaches {WorldEdge:0} m from the middle)";
				}
				return AdminCommands.Teleport(movers, to, null, "coordinates", "console");
			}
			return problem;
		}

		// heal <player|all> [amount]
		private static string Heal(string[] words)
		{
			if (words.Length < 2)
			{
				return "usage: heal <player|all> [amount], e.g. heal Ulf; without an amount, to full health";
			}
			int n = words.Length;
			float amount = 0f;
			List<ZNetPeer> who = null;
			string problem = null;
			if (n >= 3 && AdminCommands.TryParseNumber(words[n - 1], out float given) && given > 0f)
			{
				who = Players(Join(words, 1, n - 2), null, out problem);
				amount = who != null ? given : 0f;
			}
			if (who == null)
			{
				who = Players(Join(words, 1, n - 1), null, out string whole);
				problem = who == null ? problem ?? whole : null;
			}
			return who == null ? problem : string.Join("; ", who.Select(p => AdminCommands.Heal(p, amount, "console")));
		}

		// spawn <prefab> [amount] [level] <player>
		private static string Spawn(string[] words)
		{
			const string usage = "usage: spawn <prefab> [amount] [level] <player>, e.g. spawn Boar 2 Ulf, spawn Troll 1 3 Ulf (level 3: two stars)";
			if (words.Length < 3)
			{
				return usage;
			}
			int next = 2;
			int amount = 1;
			int level = 1;
			if (next < words.Length - 1 && AdminCommands.TryParseAmount(words[next], out int a))
			{
				amount = a;
				next++;
				if (next < words.Length - 1 && AdminCommands.TryParseAmount(words[next], out int l))
				{
					level = l;
					next++;
				}
			}
			ZNetPeer target = AdminCommands.FindPlayer(Join(words, next, words.Length - next), out string problem);
			return target == null ? problem : AdminCommands.Spawn(target, words[1], amount, level, "console");
		}

		/*
			Anything that is not ours goes to the game's console. What that prints (Terminal.AddString)
			is copied to standard output while the command runs; the game itself already logs it.
		*/
		private static bool s_capturing;
		private static int s_captured;
		private static Harmony s_capture;

		private static void RunGameCommand(string command, string word)
		{
			if (!Console.instance)
			{
				Reply($"'{word}' is not one of ours and the game console is not up yet. Commands: {Commands}");
				return;
			}
			if (s_capture == null)
			{
				s_capture = new Harmony(ServersidePlugin.PluginGUID + ".ServerConsole");
				s_capture.Patch(AccessTools.Method(typeof(Terminal), "AddString", new[] { typeof(string) }),
					postfix: new HarmonyMethod(typeof(ServerConsole), nameof(AddStringPostfix)));
			}
			s_capturing = true;
			s_captured = 0;
			try
			{
				Console.instance.TryRunCommand(command, silentFail: false, skipAllowedCheck: true);
			}
			finally
			{
				s_capturing = false;
			}
			if (s_captured == 0)
			{
				Reply($"game console ran '{word}' without output");
			}
		}

		public static void AddStringPostfix(Terminal __instance, string text)
		{
			if (s_capturing && __instance == Console.instance)
			{
				s_captured++;
				WriteOut(text);
			}
		}
	}
}
