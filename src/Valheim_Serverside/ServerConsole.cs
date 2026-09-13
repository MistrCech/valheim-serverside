using System;
using System.Collections.Concurrent;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Valheim_Serverside
{
	/*
		Commands read from the server's standard input.

		Own commands: `save`, `stop`, `players`, `give <item> <amount> <player>`, `broadcast <text>`,
		`event <name> <player>`, `events`, `help`. Anything else is passed to the game's own console
		(the Terminal a dedicated server has but never reads), so the vanilla server commands work
		too: `kick`, `ban`, `unban`, `banned`, `stopevent`, `randomevent`, and after `devcommands`
		the cheat commands that need no player: `skiptime`, `setkey`, `removekey`, `resetkeys`,
		`listkeys`, `env`, `tod`, `wind`. The commands that act on "the player" (`spawn`, `god`,
		`tp`, `event` without a target) cannot work on a dedicated server; `give` and
		`event <name> <player>` are the server's versions.

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
		private const string Commands = "save | stop | players | give <item> <amount> <player> | broadcast <text> | event <name> <player> | events | help"
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
							Reply(AdminCommands.Broadcast(command.Substring(words[0].Length).Trim(), "console"));
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
					case "events":
						Reply(worldLoaded ? "events: " + AdminCommands.Events() : "no world loaded");
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
