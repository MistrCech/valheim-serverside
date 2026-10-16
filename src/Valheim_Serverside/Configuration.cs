﻿﻿﻿using BepInEx.Configuration;

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
		public static ConfigEntry<string> cpuAffinity;
		public static ConfigEntry<string> processPriority;

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

		public static ConfigEntry<bool> portalLockEnabled;
		public static ConfigEntry<bool> portalLockExemptAdmins;
		public static ConfigEntry<string> portalLockPublicPrefix;
		public static ConfigEntry<float> portalLockExtraReach;
		public static ConfigEntry<string> portalLockMessage;
		public static ConfigEntry<string> portalLockPairingMessage;
		public static ConfigEntry<string> portalLockKeepClear;
		public static ConfigEntry<float> portalLockKeepClearRadius;
		public static ConfigEntry<string> portalLockKeepClearMessage;

		public static ConfigEntry<bool> wardGuardEnabled;
		public static ConfigEntry<Valheim_Serverside.Features.WardGuard.Mode> wardGuardStructures;
		public static ConfigEntry<Valheim_Serverside.Features.WardGuard.Mode> wardGuardInteractions;
		public static ConfigEntry<Valheim_Serverside.Features.WardGuard.Mode> wardGuardSpoofing;
		public static ConfigEntry<Valheim_Serverside.Features.WardGuard.Mode> wardGuardDestroyRequests;
		public static ConfigEntry<bool> wardGuardExemptAdmins;
		public static ConfigEntry<bool> wardGuardFlash;
		public static ConfigEntry<string> wardGuardMessage;
		public static ConfigEntry<int> wardGuardMaxActiveWards;
		public static ConfigEntry<string> wardGuardMaxActiveWardsMessage;

		public static ConfigEntry<Valheim_Serverside.Features.Decay.Mode> decayMode;
		public static ConfigEntry<float> decayWardGraceDays;
		public static ConfigEntry<float> decayWardFadeDays;
		public static ConfigEntry<float> decayWorkbenchGraceDays;
		public static ConfigEntry<float> decayWorkbenchFadeDays;
		public static ConfigEntry<float> decayWorkbenchRadius;
		public static ConfigEntry<float> decayNoneGraceDays;
		public static ConfigEntry<float> decayNoneFadeDays;
		public static ConfigEntry<float> decayActiveMinutes;
		public static ConfigEntry<float> decayScanMinutes;
		public static ConfigEntry<int> decayMaxRemovals;
		public static ConfigEntry<bool> decayExemptAdmins;
		public static ConfigEntry<bool> decayLoginMessage;

		public static ConfigEntry<bool> antiCheatEnabled;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Action> antiCheatAction;
		public static ConfigEntry<bool> antiCheatExemptAdmins;
		public static ConfigEntry<string> antiCheatMessage;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatUnknownPrefabs;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatNotBuildable;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatCreator;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatDistance;
		public static ConfigEntry<float> antiCheatMaxDistance;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatRate;
		public static ConfigEntry<float> antiCheatMinInterval;
		public static ConfigEntry<int> antiCheatRatePlants;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatItems;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatCheatMarks;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatCheatMarksOther;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatFly;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatWards;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatNoBuild;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatBiome;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatDungeon;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatCultivated;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatBurst;
		public static ConfigEntry<int> antiCheatBurstPieces;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatHealth;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatRotation;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatFields;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatForged;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatIdentity;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatFlood;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatLevel;
		public static ConfigEntry<int> antiCheatMaxLevel;
		public static ConfigEntry<int> antiCheatSoftStrikes;
		public static ConfigEntry<string> antiCheatWarnMessage;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatTeleport;
		public static ConfigEntry<float> antiCheatTeleportMetres;
		public static ConfigEntry<Valheim_Serverside.Features.AntiCheat.Mode> antiCheatSpeed;
		public static ConfigEntry<float> antiCheatSpeedLimit;

		public static ConfigEntry<bool> guardianStonesEnabled;
		public static ConfigEntry<bool> welcomeEnabled;
		public static ConfigEntry<string> welcomeTitle;
		public static ConfigEntry<string> welcomeLines;
		public static ConfigEntry<bool> locationIconsEnabled;
		public static ConfigEntry<float> locationIconsDiscoverRadius;
		public static ConfigEntry<bool> guardianStonesExemptAdmins;
		public static ConfigEntry<string> guardianStonesMessage;

		public static ConfigEntry<bool> fireControlEnabled;
		public static ConfigEntry<float> fireControlMaxRadius;
		public static ConfigEntry<int> fireControlMaxSpread;
		public static ConfigEntry<bool> fireControlFireplaceIgnition;
		public static ConfigEntry<bool> fireControlOnlyProjectiles;

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
			cpuAffinity = config.Bind<string>("Server", "CpuAffinity", "",
				"Processors the server runs on. Empty: wherever the system puts it (as before). auto: one chiplet -- on AMD EPYC and Ryzen a die with its own L3 cache (and on the first EPYC generation its own memory) -- the one least busy over one second at startup, read from the system's own CPU topology, so the server's threads never move between dies. Or a list of processor numbers, e.g. 16-31 or 0-7,32-39. Processors above 63 are not used. Needs a restart.");
			processPriority = config.Bind<string>("Server", "ProcessPriority", "Normal",
				new ConfigDescription("Priority of the server process against everything else on the host: Normal (as before), AboveNormal or High. When the host is busy, a higher priority lets the server's frames go first. Needs a restart.",
					new AcceptableValueList<string>("Normal", "AboveNormal", "High")));

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

			welcomeEnabled = config.Bind<bool>("CharacterGuard", "Welcome", false,
				"Greet a character the first time it joins this world: WelcomeTitle in the middle of the screen, then each of WelcomeLines in the top left corner. A vanilla client shows it; nothing to install.");
			welcomeTitle = config.Bind<string>("CharacterGuard", "WelcomeTitle", "Welcome to Sarkastic.gg",
				"Shown in the middle of the screen a few seconds after a new character is let in. Empty = none.");
			welcomeLines = config.Bind<string>("CharacterGuard", "WelcomeLines",
				"Build a workbench to keep your area as it is: without one, a zone grows back as new after 72 h with nobody near.|A zone growing back never removes your buildings, chests or portals.|Fire spreads only from fire arrows, staffs, meteors and lava.|More at sarkastic.gg",
				"Shown one after another in the top left corner after WelcomeTitle. Separated by |.");
			locationIconsEnabled = config.Bind<bool>("LocationIcons", "Enabled", false,
				"Show a location's map icon (the traders Haldor, Hildir and the Bog Witch, Hildir's dungeons, the Deep North boss room, the ancient upgrade station) only to players who have been near it themselves. Vanilla sends every player the icon as soon as anyone's presence has generated the location's zone, and the map shows it whether the player has explored there or not. Found icons are remembered per character in <world>.icons.txt. The start temple's icon stays for everyone.");
			locationIconsDiscoverRadius = config.Bind<float>("LocationIcons", "DiscoverRadius", 50f,
				"How near (metres, horizontally) a player must have been to a location for its icon to appear on their map.");

			portalLockEnabled = config.Bind<bool>("PortalLock", "Enabled", false,
				"Let a portal be used only by those it belongs to. The server pairs portals by name alone, so anyone who builds a portal with the name of an unpaired one walks into that base; and nothing stops a player from stepping into any paired portal. Here a pair of portals is one thing: it may be used by the builder of either end, and by the owner and the players written on a ward that covers either end (one ward at one end decides for the pair; wards at both ends: a player has to be on both lists). A name that starts with PublicPrefix is open to everyone. Two portals are paired only if they have a person in common. A player who may not use a pair sees the portal unconnected. The ward's own list is used: its owner presses E on it as before. Existing pairs keep working for the builders of both ends. See the README.");
			portalLockExemptAdmins = config.Bind<bool>("PortalLock", "ExemptAdmins", true,
				"Admins (adminlist.txt) may use and rename every portal.");
			portalLockPublicPrefix = config.Bind<string>("PortalLock", "PublicPrefix", "*",
				"A portal whose name starts with this is open to everyone, as in the game: at a spawn crossroads, say. Inside someone else's ward it counts only if the ward's owner built it. Its name can still only be changed by those who manage it. Empty: no public portals.");
			portalLockExtraReach = config.Bind<float>("PortalLock", "ExtraReach", 0f,
				"Metres added to a ward's own radius (32 m for the game's ward, the circle seen around it) for deciding whether it covers a portal.");
			portalLockMessage = config.Bind<string>("PortalLock", "Message", "This portal is private. Ask its owner to add you to the ward next to it.",
				"Shown in the middle of the screen of a player who stands at a portal they may not use, or tries to rename one, at most once a quarter of an hour per portal. Empty: no message.");
			portalLockPairingMessage = config.Bind<string>("PortalLock", "PairingMessage", "No portal of this name can be linked to yours: its builder has to be on a ward next to your portal, or you on one next to theirs.",
				"Shown to the builder standing at an unconnected portal that has a partner of the same name it may not be paired with, at most once a quarter of an hour per portal. Empty: no message.");

			portalLockKeepClear = config.Bind<string>("PortalLock", "KeepClear", "StartTemple,Eikthyrnir,GDKing,Bonemass,Dragonqueen,GoblinKing,Mistlands_DvergrBossEntrance1,FaderLocation,DN_Bossroom,Vendor_BlackForest,Hildir_camp,BogWitch_Camp",
				"Locations (prefab names, comma separated) near which a player may not have a public portal: the spawn (the start temple), the boss altars, the traders. Every place the world has for each counts, the traders' other possible places included. Empty: none.");
			portalLockKeepClearRadius = config.Bind<float>("PortalLock", "KeepClearRadius", 100f,
				"How far, in metres sideways, from the place of any location in KeepClear a public portal may not stand. A player-built portal there does not count as public (its builder and the ward's list use it) and cannot be given a public name; a portal nobody built (admin-spawned) is not held to it. 0: the rule is off.");
			portalLockKeepClearMessage = config.Bind<string>("PortalLock", "KeepClearMessage", "A public portal cannot stand within {0} m of the spawn, a boss altar or a trader. Name it without the public prefix, or move it.",
				"Shown in the middle of the screen of a player whose portal is refused a public name there. {0} is the radius. Empty: no message.");

			wardGuardEnabled = config.Bind<bool>("WardGuard", "Enabled", false,
				"Make the server enforce the ward (guard stone). In the game every check of a ward is made in the acting player's own game, nothing stops a weapon from damaging a warded build (the ward only flashes), and smelters, cooking stations, fireplaces, crops, beds and stands have no ward check at all. Here the server judges the RPCs players' games send it, by the player the connection belongs to and the game's own rule: where an enabled ward covers the place, the owner of the ward and the players on its list may act, nobody else. Admins, creatures of a raid and anything the game does by itself are not touched. Every refusal is written to <world>.guard.log. Needs a restart to switch on (the patches are applied at startup); switching it off takes effect at once. See the README.");
			wardGuardStructures = config.Bind("WardGuard", "Structures", Valheim_Serverside.Features.WardGuard.Mode.On,
				"Hitting, taking down with the hammer and repairing a player's build piece (anything with a creator, and the ward itself) inside an enabled ward by a player who is not on its list: On drops it (the ward flashes and the player is told), LogOnly only writes it to the guard log, Off does nothing. A hit with no attacker (fire, a falling object) or by a creature that the sender's game owns is not a player's and is not touched.");
			wardGuardInteractions = config.Bind("WardGuard", "Interactions", Valheim_Serverside.Features.WardGuard.Mode.On,
				"The same for what a ward is meant to keep out: opening chests, doors, using item and armour stands, fermenters, beehives, sap collectors, incinerators, writing on the map table -- and what the game does not guard at all: taking the product out of a smelter, kiln or cooking station, switching a fireplace off, claiming a bed, picking planted crops. Putting something in (ore, fuel, food, ammo) is not guarded -- the player's game has already used the item up when it asks -- and neither are traps (the same request is sent when something steps on one). A chest that is refused answers the player no, as it does when it is in use.");
			wardGuardSpoofing = config.Bind("WardGuard", "Spoofing", Valheim_Serverside.Features.WardGuard.Mode.On,
				"An RPC whose sender id is not the connection it arrived on is dropped (the game's own client always sends its own id), and so are a ward's ToggleEnabled and TogglePermitted for a player id other than the sender's own: with them a modified game switches off someone's ward or puts itself on its list. LogOnly writes them to the guard log and lets them through. Admins are not exempt from this.");
			wardGuardDestroyRequests = config.Bind("WardGuard", "DestroyRequests", Valheim_Serverside.Features.WardGuard.Mode.LogOnly,
				"The game asks the server to destroy only what it owns itself, so a request for an object that another game or the server owns is what a modified game sends to take a build away. LogOnly counts them and writes a summary to the log (left in); On takes them out of the request. Leave it in LogOnly until the log shows what the honest clients send: the game claims an object and destroys it in the same frame (the hammer on a piece that is not a WearNTear, picking up, harvesting), and the claim can reach the server after the request. Admins are not exempt from this.");
			wardGuardExemptAdmins = config.Bind<bool>("WardGuard", "ExemptAdmins", true,
				"Admins (adminlist.txt) are not held to any ward.");
			wardGuardFlash = config.Bind<bool>("WardGuard", "Flash", true,
				"The ward flashes (the game's own shield effect, every player near sees it) when it refuses something, at most every 3 seconds.");
			wardGuardMessage = config.Bind<string>("WardGuard", "Message", "This place is under a ward. Ask its owner to put you on the ward's list.",
				"Shown in the middle of the screen of a player the ward refused, at most every 15 seconds. Empty: no message.");
			wardGuardMaxActiveWards = config.Bind("WardGuard", "MaxActiveWards", 2,
				new ConfigDescription("How many wards one player may have switched on at once (a ward is switched on by its creator pressing E on it; building one costs a surtling core, so it takes a few). A player who has that many and tries to switch on another is refused and told; switching one off first, or taking it down, makes room. Two ways: the request is dropped when it passes the server, and, because the game of the player who has just placed a ward handles the key press itself, the server also looks at the wards that are on every few seconds and switches off the newest one of a player who has too many. Wards that are on when the server starts, or when this setting is changed, stay on. A player is held to it while they are in the game (that is when a ward is switched on, and they can be told), and every ward above the limit goes off, the newest first. Admins are not held to it (ExemptAdmins). 0: no limit; there is no LogOnly for this. Without a limit the world fills with wards, and every ward is a base the decay (see [Decay]) keeps alive.",
					new AcceptableValueRange<int>(0, 50)));
			wardGuardMaxActiveWardsMessage = config.Bind<string>("WardGuard", "MaxActiveWardsMessage", "You can keep only {0} wards switched on. Switch one of yours off first (E on it), or take it down. A ward that is off no longer holds its base against decay.",
				"Shown in the middle of the screen of a player refused a further ward, at most every 3 seconds. {0} is the limit. Empty: no message.");

			decayMode = config.Bind("Decay", "Mode", Valheim_Serverside.Features.Decay.Mode.Off,
				"Take down what players built once the players it belongs to have stayed away long enough, so abandoned bases do not fill the world for ever. Off: nothing is tracked, and the decay clock stands still. DryRun: tracks the players and writes to the log (and <world>.guard.log) what would be taken down and when, changes nothing in the world -- run it for two weeks first (the first ward base can only begin after WardGraceDays) and read the log; it lists portals, ships and chests in every base. On: does it; `decay clear` on the server console is the way back. Pieces are everything a player built: walls and floors, chests with what is in them, beds, portals, ships and carts, crops, signs. A piece is protected as long as any claim on it is within its grace: a ward that is switched on (the owner and the players on its list hold it), a workbench within WorkbenchRadius (its builder holds it), or its builder alone; when the last has lapsed the piece goes with the base of that claim, over that claim's fade (see the days below). Nothing starts or is taken down while one of the players who hold the base is in the game. The decay clock counts only the time the server runs under decay. A player the server has not met counts as active from the first time it sees their builds, so switching decay on for the first time takes down nothing at once; after a dry run, the bases that have lapsed in the meantime (the log lists them) begin as soon as On is set. Players are told when they join. See the README.");
			decayWardGraceDays = config.Bind("Decay", "WardGraceDays", 14f,
				new ConfigDescription("A base covered by a ward that is switched on starts to decay this many days after the last of the ward's owner and the players on its list was active. The ward goes first. Then WardFadeDays go by before the last piece is gone.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayWardFadeDays = config.Bind("Decay", "WardFadeDays", 10f,
				new ConfigDescription("Over how many days the pieces of a ward base go once it has started to decay: the top pieces first (roofs, walls, floors), then chests, beds and benches, the workbench last. Total life of an abandoned ward base: WardGraceDays + WardFadeDays. 0: every piece is marked to go at once and the passes that follow take them down, MaxRemovals at a time.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayWorkbenchGraceDays = config.Bind("Decay", "WorkbenchGraceDays", 7f,
				new ConfigDescription("The same for what has no ward over it but stands within WorkbenchRadius of a workbench: it starts to decay this many days after the workbench's builder was last active.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayWorkbenchFadeDays = config.Bind("Decay", "WorkbenchFadeDays", 10f,
				new ConfigDescription("Over how many days a workbench base then goes.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayWorkbenchRadius = config.Bind("Decay", "WorkbenchRadius", 20f,
				new ConfigDescription("How far, in metres sideways, a workbench protects the pieces around it (the game lets you build within 20 m of a workbench).",
					new AcceptableValueRange<float>(1f, 100f)));
			decayNoneGraceDays = config.Bind("Decay", "NoneGraceDays", 3f,
				new ConfigDescription("The same for a piece with neither ward nor workbench: it starts to decay this many days after its builder was last active. The pieces of one builder that have no better protection are one camp.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayNoneFadeDays = config.Bind("Decay", "NoneFadeDays", 4f,
				new ConfigDescription("Over how many days a camp then goes.",
					new AcceptableValueRange<float>(0f, 365f)));
			decayActiveMinutes = config.Bind("Decay", "ActiveMinutes", 10f,
				new ConfigDescription("A player counts as active when they have been in the game this many minutes in one go, so that a quick login to see whether the base is still there does not keep it alive for ever. While they are in the game their base is not started or taken down, whatever their time so far. A player who holds a decaying base and is active again stops the decay of what is left: what is gone is gone, and a ward has to be built again.",
					new AcceptableValueRange<float>(0f, 600f)));
			decayScanMinutes = config.Bind("Decay", "ScanMinutes", 10f,
				new ConfigDescription("How often the world is looked through (one pass over all objects, some tens of milliseconds) to start decay, take down what is due and update what players are told.",
					new AcceptableValueRange<float>(1f, 120f)));
			decayMaxRemovals = config.Bind("Decay", "MaxRemovals", 200,
				new ConfigDescription("At most this many pieces are taken down in one pass; the rest wait for the next. Spreads the load of a big base over several passes.",
					new AcceptableValueRange<int>(1, 5000)));
			decayExemptAdmins = config.Bind<bool>("Decay", "ExemptAdmins", false,
				"The characters of admins (adminlist.txt) count as always active, as do those put on the list with the console command `decay exempt <player>`, so what they built -- a spawn hub, say -- never decays: their pieces, and their ward and workbench over their own pieces. Over the pieces of others their ward or workbench holds only as long as its keepers really play. The server learns that a character is an admin when it is online, or from the character guard's record of its account; `decay exempt <id>` is the sure way. False (the default): nobody is exempt, admins decay like everyone else; the console command `decay exempt` still works for single characters.");
			decayLoginMessage = config.Bind<bool>("Decay", "LoginMessage", true,
				"Tell a player half a minute after they join about their bases: those that are decaying (first), and those that start to soon (within half their grace, at most three days). At most four lines are shown.");

			antiCheatEnabled = config.Bind<bool>("AntiCheat", "Enabled", false,
				"Look at what the players' games create and take away, and perhaps remove from the server, what the unmodified game cannot make: a plant the cultivator never plants, a piece placed far from its builder, forty seeds in one moment, a stack larger than the item allows, a character that flies, a piece in someone else's ward. The server does not see a player's mods; it sees what they leave in the world, and a game that is not modified cannot leave these. Changes take effect at the next start. See the README for what is covered, what is not, and which of the popular cheat mods leave which trace.");
			antiCheatAction = config.Bind("AntiCheat", "Action", Valheim_Serverside.Features.AntiCheat.Action.Kick,
				"What happens when a rule that is set to On is broken: Log (write it to the log and <world>.guard.log, nothing else), Revert (also take the object away), Kick (also tell the player what was wrong, and remove them from the server six seconds later; they may come back, and what breaks a rule is taken away again). Every rule that is set to LogOnly only writes.");
			antiCheatExemptAdmins = config.Bind<bool>("AntiCheat", "ExemptAdmins", true,
				"Admins (adminlist.txt) are not held to the rules: they have the console and the tools that bend them.");
			antiCheatMessage = config.Bind<string>("AntiCheat", "Message", "Your game did something that the game itself cannot do ({0}), and you are removed from the server. Whatever it made that the game cannot make has been taken away. Turn off the mod that did it before you come back.",
				"Shown in the middle of the screen of a player who is about to be kicked. {0} is what they broke. Empty: no message.");
			antiCheatSoftStrikes = config.Bind("AntiCheat", "SoftStrikes", 2,
				new ConfigDescription("Distance, Wards, NoBuild, Biome, Dungeon, Cultivated and Identity depend on the state of the server a moment after the player's game decided (a ward that was switched on just then, ground that was not loaded yet, a position the server had not caught up with, a character that had just died): the first time one of them is broken in ten minutes, the object is taken away and the player gets WarnMessage; the SoftStrikes-th time they are kicked. Breaks less than 15 seconds after the last one count as the same time (one mistake can be several objects). 1: kicked at once like for the others.",
					new AcceptableValueRange<int>(1, 10)));
			antiCheatWarnMessage = config.Bind<string>("AntiCheat", "WarnMessage", "That is not allowed ({0}). If your game made something it should not, it has been taken away. If it happens again you are removed from the server.",
				"Shown in the middle of the screen of a player who broke one of the rules that warn first (see SoftStrikes). {0} is what they broke. Empty: no message.");
			antiCheatUnknownPrefabs = config.Bind("AntiCheat", "UnknownPrefabs", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A player's game created an object that the server's game does not have at all (a mod that adds things to the game, a building planner's placeholders). Off, LogOnly or On.");
			antiCheatNotBuildable = config.Bind("AntiCheat", "NotBuildable", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"An object with a creator (the game writes it when a player places a piece with the hammer, the hoe or the cultivator) that no tool of the game places: a thistle, a berry bush or an ore planted with the cultivator, a mushroom, a piece of a content mod. The tools are read from the game's own item database. Off, LogOnly or On.");
			antiCheatCreator = config.Bind("AntiCheat", "Creator", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"The creator written on a piece is not the character that sent it. Off, LogOnly or On.");
			antiCheatDistance = config.Bind("AntiCheat", "Distance", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A piece placed more than MaxDistance metres from the player who placed it, both when it arrived and a moment later. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatMaxDistance = config.Bind("AntiCheat", "MaxDistance", 30f,
				new ConfigDescription("The game places a piece within 5 m of the player's eye, a few metres more for large pieces and a few more where a piece snaps to its neighbour; the server sees the player's position a moment late. 0: no limit.",
					new AcceptableValueRange<float>(0f, 500f)));
			antiCheatRate = config.Bind("AntiCheat", "Rate", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"RatePlants or more plants of one player put down within MinInterval seconds of each other (a row or a grid of seeds with one press, a mod that plants as fast as it likes). Every plant carries the time at which the player's game planted it, so a slow connection does not bunch plants up. Off, LogOnly or On.");
			antiCheatMinInterval = config.Bind("AntiCheat", "MinInterval", 0.25f,
				new ConfigDescription("The game plants once in 0.4 s at the fastest. The time written into a plant is the game's network time, which now and then steps by a few tenths of a second, so two plants of an honest player can come out a few hundredths of a second apart (the world of this server has such pairs; no honest player has three in a quarter of a second).",
					new AcceptableValueRange<float>(0.05f, 0.4f)));
			antiCheatRatePlants = config.Bind("AntiCheat", "RatePlants", 3,
				new ConfigDescription("How many plants inside MinInterval make a group the game cannot make. 2 would also catch an honest player whose network time stepped.",
					new AcceptableValueRange<int>(2, 20)));
			antiCheatItems = config.Bind("AntiCheat", "Items", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"An item that a player's game dropped on the ground with a stack, a quality or a durability above what the item can have (stack-size mods, spawn menus). Items in chests are not looked at; the console command `anticheat scan` lists dropped ones in the stored world. Off, LogOnly or On.");
			antiCheatCheatMarks = config.Bind("AntiCheat", "CheatMarks", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A creature that a player's game created with the game's own mark for cheating (the console's spawn command, which a player can only use with a mod that unlocks the console). Mods that keep the item mark from being written (NoCheatedTag, CheaterBypass) do not touch this one; a flying player's hits on creatures write it later than the object is judged, so it is not seen then. Off, LogOnly or On.");
			antiCheatCheatMarksOther = config.Bind("AntiCheat", "CheatMarksOther", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"Anything else that carries the mark: a piece built from items an admin handed out is marked too, so this is only a trace by default. Off, LogOnly or On.");
			antiCheatFly = config.Bind("AntiCheat", "Fly", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"The character's fly switch is on (the console's fly command, and the mods that fly with the hammer in hand). The game writes the switch to the character so that others see it. Off, LogOnly or On.");
			antiCheatWards = config.Bind("AntiCheat", "Wards", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A piece placed inside a ward that is switched on, by a player who is not the ward's owner or on its list: the game's build mode refuses it, and the check is in the player's own game. Uses the ward guard's reading of the wards; a ward that is switched off guards nothing, as in the game. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatNoBuild = config.Bind("AntiCheat", "NoBuild", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A piece placed in a place where the game lets no one build (the sites of the bosses' altars and the like). Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatBiome = config.Bind("AntiCheat", "Biome", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A piece that belongs to one biome (a few do) placed in another; not judged where the server has not got the ground loaded, and only if the world generator agrees that it is the wrong biome. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatDungeon = config.Bind("AntiCheat", "Dungeon", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A piece that may not be built in a dungeon (most may not), built in one. The world modifier that allows building in dungeons is honoured; the game's other exception, an environment in which building is allowed, had no entries in the 1.0.20 server's data when this was written. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatCultivated = config.Bind("AntiCheat", "Cultivated", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"A crop put on ground that is not cultivated. LogOnly by default: the server has the ground only near players and a moment behind, so an honest planter right after hoeing can be caught out. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatBurst = config.Bind("AntiCheat", "Burst", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"BurstPieces or more pieces (not plants) from one player in three seconds: more than the game places in that time (one in 0.4 s). LogOnly by default: a connection that stalls and then delivers everything at once looks the same. Off, LogOnly or On.");
			antiCheatBurstPieces = config.Bind("AntiCheat", "BurstPieces", 15,
				new ConfigDescription("How many pieces in three seconds make a burst.",
					new AcceptableValueRange<int>(2, 200)));
			antiCheatHealth = config.Bind("AntiCheat", "Health", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"A piece with more health than its prefab can have (indestructible pieces of the build-anything hammers). LogOnly by default: skill and building mods make pieces tougher on purpose. Off, LogOnly or On.");
			antiCheatRotation = config.Bind("AntiCheat", "Rotation", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"A piece turned in a way the build mode does not offer: tilted, or not in a multiple of 22.5 degrees about the vertical (the free-rotation mods). LogOnly by default: it breaks nothing but the look. Off, LogOnly or On.");

			antiCheatFields = config.Bind("AntiCheat", "Fields", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"An object that a player's game created with per-object field overrides (the key HasFields: invulnerable, invisible, enormous). The game writes it for one thing only, the console's spawn command given component fields, which the build-anything admin hammers use as well. A plain spawn does not write it. Off, LogOnly or On.");
			antiCheatForged = config.Bind("AntiCheat", "Forged", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"An object that arrived from one player's game with an id that carries the session of another game. The game always makes its objects under its own session; a modified game can write someone else's, to have its work put on them. Judged by the connection it arrived on, so the one who is acted on is the sender. Off, LogOnly or On.");
			antiCheatIdentity = config.Bind("AntiCheat", "Identity", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A game that names as its own a character that another game made, or that sends a piece (an object with a creator) while it has no character at all. The game's character is made by the game that plays it, and a piece can only be placed by a character. Warns first, see SoftStrikes. Off, LogOnly or On.");
			antiCheatFlood = config.Bind("AntiCheat", "Flood", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A game that has thousands of objects waiting to be looked at: more than the game makes in minutes. The objects past the limit are not looked at (a game cannot be allowed to hide its work among junk), and the player is acted on. Nothing is taken away. Off, LogOnly or On.");
			antiCheatLevel = config.Bind("AntiCheat", "Level", Valheim_Serverside.Features.AntiCheat.Mode.On,
				"A creature that a player's game created with a level above MaxLevel (spawn menus let a player type any level). A player's game does create creatures now and then (the skeletons of a staff, the offspring of tamed animals), but none of those is above the levels the game gives its spawns. Off, LogOnly or On.");
			antiCheatMaxLevel = config.Bind("AntiCheat", "MaxLevel", 10,
				new ConfigDescription("The highest level of a creature that is let through. The game makes up to three stars (level 4) from its spawn data; a spawn menu lets a player type any number.",
					new AcceptableValueRange<int>(2, 1000)));
			antiCheatTeleport = config.Bind("AntiCheat", "Teleport", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"A character that moved TeleportMetres or more in a few seconds with no portal within 20 m of either end, and neither end in a dungeon (the map-click teleport mods). LogOnly by default: the check is new and the game has more ways to move a player than a portal. Off, LogOnly or On.");
			antiCheatTeleportMetres = config.Bind("AntiCheat", "TeleportMetres", 250f,
				new ConfigDescription("How far a character must have moved between two looks (the server looks every two seconds and counts looks 1 to 8 s apart) to be a teleport. The fastest ship under the best wind makes about 15 m/s.",
					new AcceptableValueRange<float>(50f, 5000f)));
			antiCheatSpeed = config.Bind("AntiCheat", "Speed", Valheim_Serverside.Features.AntiCheat.Mode.LogOnly,
				"A character that moved faster than SpeedLimit metres a second, sideways, in two looks in a row. LogOnly by default. Off, LogOnly or On.");
			antiCheatSpeedLimit = config.Bind("AntiCheat", "SpeedLimit", 30f,
				new ConfigDescription("Metres per second. A run is 7, a ship under a good wind 15; the mods that speed a player up on paths (SpeedyPaths: 1.4 times) stay well under this and are not caught by it.",
					new AcceptableValueRange<float>(10f, 200f)));

			guardianStonesEnabled = config.Bind<bool>("GuardianStones", "Enabled", false,
				"Show a boss's trophy on its guardian stone, and so offer its power, only to players who have beaten that boss themselves, as their own client's raid list shows it (the same reading as the night spawn guard). To everyone else's game the stone is empty: a vanilla client cannot take a power it cannot see, and the server could not take one back. Players who may not use a stone cannot hang a trophy on it either.");
			guardianStonesExemptAdmins = config.Bind<bool>("GuardianStones", "ExemptAdmins", true,
				"Admins see every stone as it is.");
			guardianStonesMessage = config.Bind<string>("GuardianStones", "Message", "This power is yours once you have beaten {0} yourself.",
				"Shown in the middle of the screen of a player who stands at a stone holding a trophy they may not use, at most once a quarter of an hour per stone. {0} is the boss. Empty: no message.");

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
			fireControlOnlyProjectiles = config.Bind<bool>("FireControl", "OnlyProjectiles", false,
				"Only projectiles start fires: a fire arrow, a Staff of Embers fireball, a meteor or a lava rock, and what they set alight burns and spreads as before (within MaxRadius and MaxSpread). Nothing a base has -- no fireplace, bonfire, campfire, hearth, brazier or torch, no fire they lit -- throws a spark or sets anything alight, so a player's own build never sets it on fire. Surtlings' and fuling shamans' fireballs never set anything alight in the game.");

			fixTeleportGhosts = config.Bind<bool>("Fixes", "TeleportGhosts", true,
				"Tell the players near the old spot to drop a player who teleported away. Valheim 1.0 checks whether an object left their area before it stores the new position, so the teleported player stayed there for them, frozen, until they next crossed a zone line.");
			fixServerSmoke = config.Bind<bool>("Fixes", "ServerSmoke", true,
				"Let fires and fireplaces make smoke on the server while a player is within 64 m, as they do on that player's computer in vanilla. A smoke source only puffs while the local player is that close, and a dedicated server has none, so the server never saw smoke: a fireplace in a room full of smoke, which every player sees go out, kept burning on the server -- using fuel, throwing sparks and setting what touches it alight -- and a fire under a roof never choked.");
		}
	}
}
