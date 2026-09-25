using BepInEx.Configuration;

namespace PluginConfiguration
{
	public class Configuration
	{
		public static ConfigEntry<bool> modEnabled;

		public static ConfigEntry<bool> maxObjectsPerFrameEnabled;
		public static ConfigEntry<int> maxObjectsPerFrame;

		public static ConfigEntry<bool> networkingEnabled;
		public static ConfigEntry<int> networkQueueSizeKB;
		public static ConfigEntry<int> networkSendRateMinKB;
		public static ConfigEntry<int> networkSendRateMaxKB;
		public static ConfigEntry<int> networkStatsMinutes;

		public static ConfigEntry<bool> consoleCommandsEnabled;
		public static ConfigEntry<int> unityJobWorkers;

		public static ConfigEntry<int> sendIntervalMs;
		public static ConfigEntry<int> maxCatchUpMs;
		public static ConfigEntry<int> maxZonesPerTick;
		public static ConfigEntry<int> performanceStatsMinutes;
		public static ConfigEntry<int> serverTargetFps;

		public static ConfigEntry<int> maxGiveAmount;
		public static ConfigEntry<int> consoleInputCodePage;

		public static ConfigEntry<bool> fixSaveClientChanges;
		public static ConfigEntry<bool> fixDungeonLoadGuard;
		public static ConfigEntry<bool> compatVcpSpawnQueue;
		public static ConfigEntry<bool> compatVcpUnload;
		public static ConfigEntry<bool> fixTeleportGhosts;

		public static void Load(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			maxObjectsPerFrameEnabled = config.Bind<bool>("MaxObjectsPerFrame", "Enabled", true, "Enable or disable the feature");
			maxObjectsPerFrame = config.Bind<int>("MaxObjectsPerFrame", "MaxObjects", 100,
				new ConfigDescription("Maximum number of objects the server can create per frame. A vanilla dedicated server creates 100 (the game's loading-screen rate: a server has no player of its own); lower values make areas fill in more slowly.",
					new AcceptableValueRange<int>(1, 10000)));

			networkingEnabled = config.Bind<bool>("Networking", "Enabled", true,
				"Raise the limits on how fast the server sends world data to each player (server-side part of BetterNetworking). Needs a restart.");
			networkQueueSizeKB = config.Bind<int>("Networking", "QueueSizeKB", 48,
				new ConfigDescription("Data queued per player before the server stops sending world updates for that tick. Valheim: 10. At 20 ticks/s, 48 KB is ~960 KB/s, just under SteamSendRateMaxKB; more needs a higher send rate too. A fuller queue delays new updates behind it. Above 80 Steam starts failing.",
					new AcceptableValueRange<int>(10, 80)));
			networkSendRateMinKB = config.Bind<int>("Networking", "SteamSendRateMinKB", 256,
				new ConfigDescription("Minimum rate Steam attempts to send to each player, KB/s. Valheim: 150. Keep it below the server's upload speed divided by the number of players.",
					new AcceptableValueRange<int>(64, 4096)));
			networkSendRateMaxKB = config.Bind<int>("Networking", "SteamSendRateMaxKB", 1024,
				new ConfigDescription("Maximum rate Steam sends to each player, KB/s. Valheim: 150.",
					new AcceptableValueRange<int>(64, 4096)));
			networkStatsMinutes = config.Bind<int>("Networking", "StatsIntervalMinutes", 5,
				"Every this many minutes, log per player how often their send queue was full. 0 disables.");

			consoleCommandsEnabled = config.Bind<bool>("Server", "ConsoleCommands", true,
				"Read commands from standard input: save, stop, players, give <item> <amount> <player>, broadcast <text>, event <name> <player>, events, help; anything else goes to the game's own console (kick, ban, unban, banned, stopevent, devcommands, skiptime ...). In AMP set App.HasWriteableConsole=True to type them into its console, and App.ExitMethod=String with App.ExitString=stop to shut down cleanly. On Windows also set [Logging.Console] Enabled = false in BepInEx.cfg, or BepInEx's own console takes over standard input.");
			maxGiveAmount = config.Bind<int>("Server", "MaxGiveAmount", 1000,
				"Most items one give command may drop.");
			consoleInputCodePage = config.Bind<int>("Server", "ConsoleInputCodePage", 1250,
				new ConfigDescription("How to read a console line that is not valid UTF-8 (UTF-8 is always tried first). Panels on Windows may write their code page instead: 1250 Central European (Czech), 852 Central European DOS, 1252 Western, 65001 UTF-8 only. The log says which bytes arrived the first time this is needed.",
					new AcceptableValueList<int>(1250, 852, 1252, 65001)));
			unityJobWorkers = config.Bind<int>("Server", "UnityJobWorkers", 8,
				"Upper limit on Unity job worker threads. Unity starts one per CPU core, and on many-core hosts the idle ones still use CPU. Only ever lowers the count. 0 leaves Unity's default.");

			sendIntervalMs = config.Bind<int>("Performance", "SendIntervalMs", 100,
				new ConfigDescription("How often each player is sent world updates, in real milliseconds. Valheim sends to one player per frame, so each player waits players+1 frames: at 15 FPS with 4 players ~330 ms. Every send builds that player's list of nearby objects, so shorter intervals cost server CPU. 0 keeps Valheim's behaviour.",
					new AcceptableValueRange<int>(0, 1000)));
			maxCatchUpMs = config.Bind<int>("Performance", "MaxCatchUpMs", 100,
				new ConfigDescription("Longest frame the server counts in full (Unity's maximum allowed timestep). After a slow frame Unity runs physics and every creature's fixed update again for each 20 ms it fell behind; Valheim allows 200 ms (10 steps), 100 caps it at 5, so one slow frame does not make the next one slow too. Game time runs slightly slower during such frames. Needs a restart. 0 keeps the game's setting.",
					new AcceptableValueRange<int>(0, 1000)));
			maxZonesPerTick = config.Bind<int>("Performance", "MaxZonesPerTick", 1,
				new ConfigDescription("Most new zones the server generates per zone tick (10 ticks a second), shared by all players in turn. A new zone is generated in full in one frame, so several players exploring at once used to cost one zone each in the same frame. 0 = one per player per tick, as before.",
					new AcceptableValueRange<int>(0, 100)));
			serverTargetFps = config.Bind<int>("Performance", "ServerTargetFps", 60,
				new ConfigDescription("Frame rate the server aims for. The game sets a dedicated server to 30, so even with time to spare a frame waits 33 ms; every reaction to a player (a hit, a felled tree) waits for a server frame. 60 halves that wait whenever the server has the headroom, and costs nothing when it has not. Needs a restart. 0 keeps the game's 30.",
					new AcceptableValueRange<int>(0, 240)));
			performanceStatsMinutes = config.Bind<int>("Performance", "StatsIntervalMinutes", 5,
				"Every this many minutes, log frame times, physics steps per frame, the cost of sending world updates and of generating zones. 0 disables.");

			fixSaveClientChanges = config.Bind<bool>("Fixes", "SaveClientChanges", true,
				"Mark a world chunk as changed when a player's own change to an object arrives, so the next save writes it. Valheim 1.0 only rewrites changed chunks and does not count changes received from players, so what a player just built or moved could be missing after a restart.");
			fixDungeonLoadGuard = config.Bind<bool>("Fixes", "DungeonLoadGuard", true,
				"Keep a dungeon whose room bundle fails to load from wedging its zone for good. When Unity refuses a bundle, the game still reports the load as done and then throws, the dungeon never hears back and its zone stays flagged as loading. A vanilla dedicated server never loads dungeons, this mod does. The guard takes over a bundle Unity says is already loaded, reports a load that really failed as failed, and lets such a dungeon go so its zone keeps working and it is tried again next time.");
			compatVcpSpawnQueue = config.Bind<bool>("Compat", "ValheimCommunityPatchSpawnQueue", false,
				"Only matters with ValheimCommunityPatch installed. Its spawn queue replaces ZNetScene.CreateObjectsSorted and orders new objects by the server's reference position, which on a dedicated server is the world origin, so this mod's ordering by the nearest player never runs. Off: that patch of ValheimCommunityPatch is removed when the world starts. On: it is kept.");
			compatVcpUnload = config.Bind<bool>("Compat", "ValheimCommunityPatchUnload", false,
				"Only matters with ValheimCommunityPatch installed. Its zone-diff unload replaces ZNetScene.RemoveObjects and, whenever the object lists look untouched, drops everything outside the simulation distance of the server's reference position -- the world origin on a dedicated server. With players whose areas do not overlap, objects around them are then destroyed and created again on every pass (dungeons reloading dozens of times a second, doors, beds and items that cannot be used). Off: that patch of ValheimCommunityPatch is removed when the world starts. On: it is kept, and this mod marks its object lists as edited on every pass so that patch takes the game's own unload check.");
			fixTeleportGhosts = config.Bind<bool>("Fixes", "TeleportGhosts", true,
				"Tell the players near the old spot to drop a player who teleported away. Valheim 1.0 checks whether an object left their area before it stores the new position, so the teleported player stayed there for them, frozen, until they next crossed a zone line.");
		}
	}
}
