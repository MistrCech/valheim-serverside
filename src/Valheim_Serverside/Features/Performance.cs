using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		With this mod the server simulates everything, so its main thread sets the pace for every
		player: a hit on a tree, the tree falling and the wood appearing all wait for server frames.
		Measured on a live server (4 players, EPYC 7551P): main thread ~93% busy, ~15 FPS.

		This feature spreads world updates to players evenly in time. Two more settings belong with
		it: the physics catch-up limit (ServersidePlugin) and the zone budget (Core). The report
		shows what each costs, so the settings can be tuned on numbers.
	*/
	public class Performance : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.sendIntervalMs.Value > 0 || Configuration.performanceStatsMinutes.Value > 0 || Configuration.serverTargetFps.Value > 0;
		}

		[HarmonyPatch(typeof(ZDOMan), "SendZDOToPeers2")]
		public static class ZDOMan_SendZDOToPeers2_Patch
		/*
			Vanilla starts a round every 50 ms and then sends to one player per frame, so with N
			players each gets world updates every N+1 frames: at 15 FPS and 4 players every ~330 ms,
			and slower with every player who joins. Instead every player is sent to once per
			SendIntervalMs, the sends spread evenly over the frames in between so their cost does not
			bunch up in one frame. When frames are longer than the interval, every player is sent to
			every frame -- never more than once per frame.
		*/
		{
			private static double s_owed;
			private static int s_next;
			private static double s_last = -1;

			static bool Prefix(ZDOMan __instance)
			{
				float interval = Configuration.sendIntervalMs.Value / 1000f;
				if (interval <= 0f)
				{
					return true;
				}
				double now = Time.realtimeSinceStartupAsDouble;
				double elapsed = s_last < 0 ? 0 : now - s_last;
				s_last = now;
				List<ZDOMan.ZDOPeer> peers = __instance.m_peers;
				int count = peers.Count;
				if (count == 0)
				{
					s_owed = 0;
					return false;
				}
				s_owed = Math.Min(s_owed + count * elapsed / interval, count);
				int sends = (int)s_owed;
				s_owed -= sends;
				for (int i = 0; i < sends; i++)
				{
					if (s_next >= count)
					{
						s_next = 0;
					}
					__instance.SendZDOs(peers[s_next++], flush: false);
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
		public static class ZDOMan_SendZDOs_Timing
		{
			static void Prefix()
			{
				PerformanceStats.SendStarted();
			}

			static void Postfix()
			{
				PerformanceStats.SendFinished();
			}
		}

		/*
			The game asks for 30 FPS on a dedicated server (GraphicsSettingsManager.RequestTargetFrameRateFromPreset),
			so a frame finished in 12 ms still lasts 33 ms. Every request that goes through here on a
			dedicated server is replaced by the configured rate; below 30 the game would treat the value
			as "no limit" and spin a core at full speed, so that is the floor.

			Bound by position (__0, __1) rather than by name: Valheim 1.0.14 split the refresh rate
			out of this method, and the parameter this patch used to name, value, no longer exists.
			Harmony then throws, which took the whole feature down. The argument list in the attribute
			keeps a real signature change loud instead of silent.

			Both parameters are set. The second one decides whether the game quantises the request
			against 59.94 or 119.88 Hz (PresentManager.UpdateDisplayRefreshRate), and it arrives as the
			preset's fps limit, -1 by default. Setting it as well makes 30 and 60 land deterministically;
			they are the only requests in [30, 240] that are submultiples of 59.94 within the margin the
			game uses, so a higher value stays at the mercy of what Screen.resolutions reports headless.
		*/
		[HarmonyPatch(typeof(PresentManager), "RequestTargetFrameRate", new Type[] { typeof(int), typeof(int) })]
		public static class PresentManager_RequestTargetFrameRate_Patch
		{
			private static int s_logged;

			static void Prefix(ref int __0, ref int __1)
			{
				int fps = Configuration.serverTargetFps.Value;
				// Called before ZNet exists (GraphicsSettingsManager.Awake), so the plugin's own check is used.
				if (fps <= 0 || !ServersidePlugin.IsDedicated())
				{
					return;
				}
				int wanted = Mathf.Clamp(fps, 30, 240);
				if (s_logged++ == 0 || wanted != __0)
				{
					ServersidePlugin.logger.LogInfo($"Server target frame rate: {__0} -> {wanted}");
				}
				__0 = wanted;
				__1 = wanted;
			}
		}

		// The game logic run once per fixed step: characters, creature AI, synced objects, ships.
		[HarmonyPatch(typeof(MonoUpdaters), "FixedUpdate")]
		public static class MonoUpdaters_FixedUpdate_Timing
		{
			static void Prefix()
			{
				PerformanceStats.FixedStarted();
			}

			static void Postfix()
			{
				PerformanceStats.FixedFinished();
			}
		}

		/*
			Where a slow frame went. Each of these brackets one part of the frame; whatever is left
			over is Unity itself (physics, rendering stubs, garbage collection).
		*/
		[HarmonyPatch(typeof(ZNet), "UpdatePeers")]
		public static class ZNet_UpdatePeers_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.Peers, __state); }
		}

		[HarmonyPatch(typeof(ZDOMan), "Update")]
		public static class ZDOMan_Update_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.World, __state); }
		}

		[HarmonyPatch(typeof(ZNetScene), "Update")]
		public static class ZNetScene_Update_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.Objects, __state); }
		}

		[HarmonyPatch(typeof(MonoUpdaters), "Update")]
		public static class MonoUpdaters_Update_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.Updaters, __state); }
		}

		[HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
		public static class MonoUpdaters_LateUpdate_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.Updaters, __state); }
		}

		[HarmonyPatch(typeof(ZNet), "SaveWorld")]
		public static class ZNet_SaveWorld_Timing
		{
			static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
			static void Postfix(long __state) { PerformanceStats.Section(PerformanceStats.Save, __state); }
		}
	}

	/*
		Where the server's frame time goes, logged every StatsIntervalMinutes. Everything here runs
		on the main thread.
	*/
	public static class PerformanceStats
	{
		private const double SlowFrameSeconds = 0.1;
		private const int BucketMs = 5;

		public const int Peers = 0, World = 1, Objects = 2, ZoneGen = 3, Creatures = 4, Updaters = 5, Save = 6;
		private static readonly string[] SectionNames = { "players' messages", "world updates", "objects", "zones", "creatures", "other game updates", "saving" };
		private static readonly double[] s_sections = new double[SectionNames.Length];
		private static readonly double[] s_worstSections = new double[SectionNames.Length];
		private static int s_worstGc0, s_worstGc2, s_worstSteps, s_lastGc0, s_lastGc2;
		private static readonly int[] s_histogram = new int[200]; // frame times in 5 ms steps; the last one takes the rest

		private static double s_lastFrame = -1;
		private static double s_nextReport = -1;
		private static double s_periodStart;
		private static int s_frames, s_slowFrames;
		private static double s_frameSum, s_frameMax;
		private static int s_stepsThisFrame, s_stepSum, s_stepMax;
		private static long s_fixedStarted;
		private static double s_fixedThisFrame, s_fixedSum, s_fixedMax;
		private static int s_sends;
		private static double s_sendSum, s_sendMax;
		private static long s_sendStarted;
		private static int s_localZones, s_ghostZones;
		private static double s_zoneSum, s_zoneMax;

		private static bool Enabled => Configuration.performanceStatsMinutes.Value > 0;

		private static double Seconds(long ticks)
		{
			return (double)ticks / Stopwatch.Frequency;
		}

		public static void FixedStep()
		{
			s_stepsThisFrame++;
		}

		public static void FixedStarted()
		{
			s_fixedStarted = Stopwatch.GetTimestamp();
		}

		public static void FixedFinished()
		{
			double took = Seconds(Stopwatch.GetTimestamp() - s_fixedStarted);
			s_fixedThisFrame += took;
			s_sections[Creatures] += took;
		}

		public static void Section(int index, long started)
		{
			s_sections[index] += Seconds(Stopwatch.GetTimestamp() - started);
		}

		public static void SendStarted()
		{
			s_sendStarted = Stopwatch.GetTimestamp();
		}

		public static void SendFinished()
		{
			double took = Seconds(Stopwatch.GetTimestamp() - s_sendStarted);
			s_sends++;
			s_sendSum += took;
			s_sendMax = Math.Max(s_sendMax, took);
		}

		// Time spent in one zone tick that generated at least one zone.
		public static void Zones(int local, int ghost, long started)
		{
			if (local + ghost == 0)
			{
				return;
			}
			double took = Seconds(Stopwatch.GetTimestamp() - started);
			s_sections[ZoneGen] += took;
			s_localZones += local;
			s_ghostZones += ghost;
			s_zoneSum += took;
			s_zoneMax = Math.Max(s_zoneMax, took);
		}

		public static void Frame()
		{
			int steps = s_stepsThisFrame;
			double fixedTime = s_fixedThisFrame;
			s_stepsThisFrame = 0;
			s_fixedThisFrame = 0;
			if (!Enabled)
			{
				return;
			}
			double now = Time.realtimeSinceStartupAsDouble;
			if (s_lastFrame < 0)
			{
				s_lastFrame = now;
				s_periodStart = now;
				s_nextReport = now + Configuration.performanceStatsMinutes.Value * 60.0;
				return;
			}
			double frame = now - s_lastFrame;
			s_lastFrame = now;
			s_frames++;
			s_frameSum += frame;
			int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
			if (frame > s_frameMax)
			{
				s_frameMax = frame;
				Array.Copy(s_sections, s_worstSections, s_sections.Length);
				s_worstGc0 = gc0 - s_lastGc0;
				s_worstGc2 = gc2 - s_lastGc2;
				s_worstSteps = steps;
			}
			s_lastGc0 = gc0;
			s_lastGc2 = gc2;
			Array.Clear(s_sections, 0, s_sections.Length);
			if (frame > SlowFrameSeconds)
			{
				s_slowFrames++;
			}
			s_histogram[Math.Min((int)(frame * 1000 / BucketMs), s_histogram.Length - 1)]++;
			s_stepSum += steps;
			s_stepMax = Math.Max(s_stepMax, steps);
			s_fixedSum += fixedTime;
			s_fixedMax = Math.Max(s_fixedMax, fixedTime);
			if (now >= s_nextReport)
			{
				Report(now - s_periodStart);
				s_periodStart = now;
				s_nextReport = now + Configuration.performanceStatsMinutes.Value * 60.0;
			}
		}

		// What the slowest frame of the period was doing; "unaccounted" is Unity's own work (physics, GC).
		private static string WorstFrame()
		{
			double known = 0;
			List<string> parts = new List<string>();
			for (int i = 0; i < SectionNames.Length; i++)
			{
				known += s_worstSections[i];
				if (s_worstSections[i] >= 0.001)
				{
					parts.Add($"{SectionNames[i]} {1000 * s_worstSections[i]:0}");
				}
			}
			parts.Add($"unaccounted {1000 * Math.Max(0, s_frameMax - known):0}");
			string gc = s_worstGc2 > 0 ? $", full GC x{s_worstGc2}" : (s_worstGc0 > 0 ? $", GC x{s_worstGc0}" : "");
			return $"Worst frame ({1000 * s_frameMax:0} ms) spent, ms: {string.Join(", ", parts)}; {s_worstSteps} fixed step(s){gc}.";
		}

		// Upper edge of the 5 ms step the given share of frames falls within.
		private static int Percentile(double share)
		{
			int target = (int)Math.Ceiling(share * s_frames);
			int seen = 0;
			for (int i = 0; i < s_histogram.Length; i++)
			{
				seen += s_histogram[i];
				if (seen >= target)
				{
					return (i + 1) * BucketMs;
				}
			}
			return s_histogram.Length * BucketMs;
		}

		private static void Report(double period)
		{
			int players = ZNet.instance ? ZNet.instance.GetPeers().Count : 0;
			if (s_frames > 0 && players > 0)
			{
				ServersidePlugin.logger.LogInfo(
					$"Performance over {period / 60:0.#} min, {players} player(s): "
					+ $"{s_frames / period:0.#} FPS, frame avg {1000 * s_frameSum / s_frames:0} ms, median {Percentile(0.5)}, 95% {Percentile(0.95)}, 99% {Percentile(0.99)}, worst {1000 * s_frameMax:0} ms, {s_slowFrames} over {1000 * SlowFrameSeconds:0} ms; "
					+ $"fixed steps per frame avg {(double)s_stepSum / s_frames:0.0}, max {s_stepMax}, their game logic {100 * s_fixedSum / period:0.0}% of the time, worst frame {1000 * s_fixedMax:0} ms; "
					+ $"world sends {s_sends} ({s_sends / period:0.#}/s), avg {(s_sends > 0 ? 1000 * s_sendSum / s_sends : 0):0.0} ms, worst {1000 * s_sendMax:0.0} ms, {100 * s_sendSum / period:0.0}% of the time; "
					+ $"zones generated {s_localZones} (+{s_ghostZones} ghost), {1000 * s_zoneSum:0} ms in all, worst tick {1000 * s_zoneMax:0} ms. "
					+ WorstFrame());
			}
			Array.Clear(s_histogram, 0, s_histogram.Length);
			Array.Clear(s_worstSections, 0, s_worstSections.Length);
			s_worstGc0 = s_worstGc2 = s_worstSteps = 0;
			s_frames = s_slowFrames = s_stepSum = s_stepMax = s_sends = s_localZones = s_ghostZones = 0;
			s_frameSum = s_frameMax = s_sendSum = s_sendMax = s_zoneSum = s_zoneMax = s_fixedSum = s_fixedMax = 0;
		}
	}
}
