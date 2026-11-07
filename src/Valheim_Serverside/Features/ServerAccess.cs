using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Two limits of the game on who may join, for an owner who wants them otherwise. Only the server decides
		either, so clients stay vanilla. They are two features so that one the game has changed under cannot take
		the other with it: a public server with no password that lost PublicWithoutPassword would not start.

		PlayerLimit ([Server] MaxPlayers): the game lets at most 10 players in -- ZNet.RPC_PeerInfo turns away
		whoever comes when 10 are in, "server is full" -- and tells Steam's server list 10. Both become the
		configured number. Crossplay (PlayFab) lobbies keep the game's own limit of 10.

		PublicWithoutPassword ([Server] PublicWithoutPassword): the game will not start a public server
		(-public 1) without a password of at least 5 characters that is not part of the world's name or seed;
		a server that is not public may have none. With this a public server starts with an empty password
		(-password ""): it is listed with no lock and anyone may join. A password that is given still has to
		meet the game's rules. On Windows the game's player leaves an empty argument out of the list it hands the
		game, so `-password ""` took the next argument for the password (`-public`, the way AMP writes the line)
		and that argument was not read for itself; with this the server reads its command line as Windows split it.
	*/
	public class PlayerLimit : IFeature
	{
		public const int VanillaMaxPlayers = 10;

		public bool FeatureEnabled()
		{
			return Configuration.maxPlayers.Value != VanillaMaxPlayers;
		}

		public static int MaxPlayers()
		{
			return Mathf.Clamp(Configuration.maxPlayers.Value, 1, 64);
		}

		[HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
		public static class ZNet_RPC_PeerInfo_Patch
		/*
			The check is `if (GetNrOfPlayers() >= 10)`: the constant right after the call becomes a call for the
			configured number. Anything other than exactly one such place means the game changed it, and the
			feature is switched off rather than half applied.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				MethodInfo count = AccessTools.Method(typeof(ZNet), nameof(ZNet.GetNrOfPlayers));
				MethodInfo limit = AccessTools.Method(typeof(PlayerLimit), nameof(MaxPlayers));
				List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
				int replaced = 0;
				for (int i = 0; i + 1 < codes.Count; i++)
				{
					if (codes[i].Calls(count) && codes[i + 1].LoadsConstant(VanillaMaxPlayers))
					{
						CodeInstruction call = new CodeInstruction(OpCodes.Call, limit);
						call.labels.AddRange(codes[i + 1].labels);
						call.blocks.AddRange(codes[i + 1].blocks);
						codes[i + 1] = call;
						replaced++;
					}
				}
				if (replaced != 1)
				{
					throw new InvalidOperationException($"ZNet.RPC_PeerInfo: found {replaced} player limit check(s), expected 1. The game changed this method; the patch needs reviewing.");
				}
				ServersidePlugin.logger.LogInfo($"Player limit: {MaxPlayers()} players may be in the game at once (the game: {VanillaMaxPlayers})");
				return codes;
			}
		}

		[HarmonyPatch(typeof(ZSteamMatchmaking), "RegisterServer")]
		public static class ZSteamMatchmaking_RegisterServer_Patch
		/*
			RegisterServer tells Steam the server takes 10 players; after it, the configured number, so the server
			list shows how many may come.
		*/
		{
			static void Postfix()
			{
				SteamGameServer.SetMaxPlayerCount(MaxPlayers());
			}
		}
	}

	public class PublicWithoutPassword : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.publicWithoutPassword.Value;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetCommandLineW();

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CommandLineToArgvW(IntPtr commandLine, out int count);

		[DllImport("kernel32.dll")]
		private static extern IntPtr LocalFree(IntPtr memory);

		private static string[] SplitAsWindows()
		{
			IntPtr list = CommandLineToArgvW(GetCommandLineW(), out int count);
			if (list == IntPtr.Zero)
			{
				return null;
			}
			try
			{
				string[] args = new string[count];
				for (int i = 0; i < count; i++)
				{
					args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(list, i * IntPtr.Size));
				}
				return args;
			}
			finally
			{
				LocalFree(list);
			}
		}

		/*
			What ParseServerArguments reads instead of Environment.GetCommandLineArgs(). Elsewhere than on Windows,
			and whenever Windows' split differs from the game's list in anything but empty arguments, the game's own
			list. The log says which flags an empty argument was kept after, never a value.
		*/
		public static string[] CommandLineArgs()
		{
			string[] game = Environment.GetCommandLineArgs();
			if (Environment.OSVersion.Platform != PlatformID.Win32NT || game.Length == 0)
			{
				return game;
			}
			string[] windows;
			try
			{
				windows = SplitAsWindows();
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Command line: Windows' own split failed ({e.GetType().Name}), so the game reads its own list, which leaves out an empty argument such as -password \"\"");
				return game;
			}
			if (windows == null || windows.Length == 0)
			{
				return game;
			}
			if (!windows.Skip(1).Where(a => a.Length > 0).SequenceEqual(game.Skip(1).Where(a => a.Length > 0)))
			{
				ServersidePlugin.logger.LogWarning("Command line: Windows splits it differently from the game in more than empty arguments, so the game reads its own list, which leaves out an empty argument such as -password \"\"");
				return game;
			}
			List<string> after = new List<string>();
			for (int i = 1; i < windows.Length; i++)
			{
				if (windows[i].Length == 0)
				{
					after.Add(windows[i - 1].StartsWith("-") ? windows[i - 1] : "an argument");
				}
			}
			if (after.Count == game.Skip(1).Count(a => a.Length == 0))
			{
				return game;
			}
			ServersidePlugin.logger.LogInfo($"Command line: read as Windows split it, with the empty argument(s) the game's player leaves out, after {string.Join(", ", after)}");
			windows[0] = game[0];
			return windows;
		}

		[HarmonyPatch(typeof(FejdStartup), "ParseServerArguments")]
		public static class FejdStartup_ParseServerArguments_Patch
		/*
			The one call of Environment.GetCommandLineArgs() becomes CommandLineArgs(). Not exactly one: the game
			changed the method, and the feature is switched off rather than half applied.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				MethodInfo gameArgs = AccessTools.Method(typeof(Environment), nameof(Environment.GetCommandLineArgs));
				MethodInfo ours = AccessTools.Method(typeof(PublicWithoutPassword), nameof(CommandLineArgs));
				List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
				int replaced = 0;
				for (int i = 0; i < codes.Count; i++)
				{
					if (codes[i].Calls(gameArgs))
					{
						codes[i].operand = ours;
						replaced++;
					}
				}
				if (replaced != 1)
				{
					throw new InvalidOperationException($"FejdStartup.ParseServerArguments: found {replaced} call(s) of Environment.GetCommandLineArgs, expected 1. The game changed this method; the patch needs reviewing.");
				}
				return codes;
			}
		}

		[HarmonyPatch(typeof(FejdStartup), "IsPublicPasswordValid")]
		public static class FejdStartup_IsPublicPasswordValid_Patch
		{
			static bool Prefix(string password, ref bool __result)
			{
				if (!string.IsNullOrEmpty(password))
				{
					return true;
				}
				__result = true;
				ServersidePlugin.logger.LogInfo("Public server without a password: it is listed with no lock and anyone may join");
				return false;
			}
		}
	}
}
