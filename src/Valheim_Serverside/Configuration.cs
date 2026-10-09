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
		public static ConfigEntry<bool> fixServerSmoke;

		public static ConfigEntry<bool> characterGuardEnabled;
		public static ConfigEntry<Valheim_Serverside.Features.CharacterGuard.NewCharacterPolicy> characterGuardNewCharacters;
		public static ConfigEntry<Valheim_Serverside.Features.CharacterGuard.Action> characterGuardNewCharacterAction;
		public static ConfigEntry<Valheim_Serverside.Features.CharacterGuard.Action> characterGuardChangedAway;
		public static ConfigEntry<bool> characterGuardExemptAdmins;
		public static ConfigEntry<string> characterGuardFreshEvent;
		public static ConfigEntry<string> characterGuardNewMessage;
		public static ConfigEntry<string> characterGuardChangedMessage;

		public static ConfigEntry<Valheim_Serverside.Features.ItemLedger.Mode> itemLedgerMode;
		public static ConfigEntry<string> itemLedgerItems;
		public static ConfigEntry<string> itemLedgerExtra;
		public static ConfigEntry<string> itemLedgerExclude;
		public static ConfigEntry<bool> itemLedgerExemptAdmins;
		public static ConfigEntry<float> itemLedgerGraceHours;
		public static ConfigEntry<string> itemLedgerAltarMessage;
		public static ConfigEntry<bool> itemLedgerLogAll;
		public static ConfigEntry<string> itemLedgerMessage;

		public static ConfigEntry<bool> nightSpawnGuardEnabled;

		public static ConfigEntry<bool> fireControlEnabled;
		public static ConfigEntry<float> fireControlMaxRadius;
		public static ConfigEntry<int> fireControlMaxSpread;
		public static ConfigEntry<bool> fireControlFireplaceIgnition;

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
			characterGuardEnabled = config.Bind<bool>("CharacterGuard", "Enabled", false,
				"Check the characters players join with. A vanilla client keeps its character to itself, so the server cannot see or replace what a character carries; it sees what it wears and, from the raids it is ready for, how far it has come. A character this world has not seen must be fresh (NewCharacters), and a known one that comes back wearing something new or with other progress than it left with was played elsewhere (ChangedAway). Characters that built something or own a bed or tombstone here count as known. The list is kept next to the world save (<world>.characters.txt); `allow <name>` in the console lets a character in.");
			characterGuardNewCharacters = config.Bind("CharacterGuard", "NewCharacters", Valheim_Serverside.Features.CharacterGuard.NewCharacterPolicy.RequireFresh,
				"RequireFresh: a character this world has not seen must still be ready for Eikthyr's raid (knows nothing past the first boss) and wear nothing beyond a level 1 workbench. Allow: every new character is let in and remembered.");
			characterGuardNewCharacterAction = config.Bind("CharacterGuard", "NewCharacterAction", Valheim_Serverside.Features.CharacterGuard.Action.Kick,
				"What happens to a new character that is not fresh: Kick (after a message on their screen), Log, or Ignore.");
			characterGuardChangedAway = config.Bind("CharacterGuard", "ChangedAway", Valheim_Serverside.Features.CharacterGuard.Action.Log,
				"What happens to a known character that comes back wearing something new, or with other progress than it had when it left: Log, Kick, or Ignore.");
			characterGuardExemptAdmins = config.Bind<bool>("CharacterGuard", "ExemptAdmins", true,
				"Admins (adminlist.txt) are never checked.");
			characterGuardFreshEvent = config.Bind<string>("CharacterGuard", "FreshEvent", "army_eikthyr",
				"The raid a fresh character is still ready for. The game stops a character being ready for army_eikthyr once it knows the antler, bronze or iron pickaxe, hard antler or Eikthyr's trophy.");
			characterGuardNewMessage = config.Bind<string>("CharacterGuard", "NewCharacterMessage",
				"This character brings progress from another world. Please join with a new character, or ask an admin.",
				"Shown in the middle of the screen before a new character that is not fresh is kicked.");
			characterGuardChangedMessage = config.Bind<string>("CharacterGuard", "ChangedAwayMessage",
				"This character was played on another world since it left here. Please ask an admin.",
				"Shown in the middle of the screen before a changed character is kicked (ChangedAway = Kick).");

			itemLedgerMode = config.Bind("ItemLedger", "Mode", Valheim_Serverside.Features.ItemLedger.Mode.Off,
				"Off, LogOnly or On. Keep an account per character of the items that lock progress (Items): what it got on this server -- picked up, taken out of containers -- and what it put back into the world -- dropped, put into containers, on stands, into smelters, kilns, cooking stations and fermenters, offered at boss altars, built with, died carrying -- and what it wears. More than it ever got here, and could have crafted from it, came from another world: that is written to <world>.guard.log next to the world save. With On it is also taken away (a dropped stack is cut down, a container or stand loses it once nobody uses it, a smelter drops it from its queue, a boss altar refuses to summon) and the player is told; what was built with or is worn is only logged. Only characters whose account is exact are acted on: a new, fresh character from the start, any other after its first death here, when the tombstone shows its whole bag.");
			itemLedgerItems = config.Bind<string>("ItemLedger", "Items", "auto",
				"Items to keep accounts of: auto, or item prefab names, comma separated. auto takes the items that lock progress, from the game's own data: what the game does not let through a portal (ores, metals, dragon eggs), what bosses drop and what summons them, every material that all recipes and pieces using it need more than a level 1 workbench for, and every item that all its recipes need more for. The list, by reason, is written to the guard log at startup. Items traders sell are always left out.");
			itemLedgerExtra = config.Bind<string>("ItemLedger", "ExtraItems", "",
				"More item prefab names to keep accounts of, comma separated.");
			itemLedgerExclude = config.Bind<string>("ItemLedger", "ExcludeItems", "",
				"Item prefab names never to keep accounts of, comma separated.");
			itemLedgerLogAll = config.Bind<bool>("ItemLedger", "LogAllMovements", false,
				"Also write every movement of a tracked item to the guard log, and every craft the ledger counts, not only what cannot be accounted for.");
			itemLedgerExemptAdmins = config.Bind<bool>("ItemLedger", "ExemptAdmins", true,
				"Admins (adminlist.txt) get no account and are never checked.");
			itemLedgerGraceHours = config.Bind<float>("ItemLedger", "GraceHours", 168f,
				new ConfigDescription("For this many real hours after the ledger first runs on a world, no character counts as new: the players who are already around come back with what they had, and their accounts start as unknown.",
					new AcceptableValueRange<float>(0f, 8760f)));
			itemLedgerMessage = config.Bind<string>("ItemLedger", "Message", "{0} x {1} did not come from this world and was taken away.",
				"Shown in the middle of the player's screen when items are taken away (Mode = On). {0} is the amount, {1} the item.");
			itemLedgerAltarMessage = config.Bind<string>("ItemLedger", "AltarMessage", "{0} x {1} did not come from this world; the altar refuses them.",
				"Shown when a boss altar refuses items from another world (Mode = On). {0} is the amount, {1} the item.");

			nightSpawnGuardEnabled = config.Bind<bool>("NightSpawnGuard", "Enabled", false,
				"Hold back the ambient spawns that come with boss progress (Greydwarfs at night after Eikthyr, Draugr and Greydwarf Elites and Shamans after the Elder, Skeletons after Bonemass, Goblins after Yagluth, Seekers and Ticks after the Queen, Charred after Fader) around a group of players until every player in that spawn zone has got that far themselves, as their own client's raid list shows it. Until then the zone behaves as if the boss were still alive. In a group the least progressed player decides. Raids are not affected (-setkey playerevents decides those per player).");

			fireControlEnabled = config.Bind<bool>("FireControl", "Enabled", false,
				"Limit how far fire spreads (the Fire world modifier, and the Ashlands, which always burn). See the README for how far each source reaches in the game.");
			fireControlMaxRadius = config.Bind<float>("FireControl", "MaxRadius", -1f,
				new ConfigDescription("A fire starts only within this many metres (straight line) of the source its chain began at: a lit fireplace, or where a fire arrow, fireball or meteor came down. Sparks still fly, but one that lands farther away starts nothing. -1: no limit in metres (vanilla).",
					new AcceptableValueRange<float>(-1f, 100f)));
			fireControlMaxSpread = config.Bind<int>("FireControl", "MaxSpread", -1,
				new ConfigDescription("How many further generations of sparks any fire may set off. The game gives a bonfire, hearth and iron fire pit 4, a campfire 2, braziers, torches and candles 1, fire arrows and fireballs 4. 0: nothing throws sparks except an arrow or fireball where it lands, and nothing that catches fire spreads it further. -1: as in the game.",
					new AcceptableValueRange<int>(-1, 10)));
			fireControlFireplaceIgnition = config.Bind<bool>("FireControl", "FireplaceIgnition", true,
				"A lit hearth, bonfire, campfire, iron fire pit, brazier or standing torch sets alight whatever burnable touches its flame (vanilla). Off: no fireplace ever does -- a hearth, brazier or torch then cannot start a fire at all, a bonfire or campfire still throws sparks.");

			fixTeleportGhosts = config.Bind<bool>("Fixes", "TeleportGhosts", true,
				"Tell the players near the old spot to drop a player who teleported away. Valheim 1.0 checks whether an object left their area before it stores the new position, so the teleported player stayed there for them, frozen, until they next crossed a zone line.");
			fixServerSmoke = config.Bind<bool>("Fixes", "ServerSmoke", true,
				"Let fires and fireplaces make smoke on the server while a player is within 64 m, as they do on that player's computer in vanilla. A smoke source only puffs while the local player is that close, and a dedicated server has none, so the server never saw smoke: a fireplace in a room full of smoke, which every player sees go out, kept burning on the server -- using fuel, throwing sparks and setting what touches it alight -- and a fire under a roof never choked.");
		}
	}
}
