using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		How fire spreads, from the game's own code and data (Valheim 1.0.16). Outside the Ashlands
		all of it needs the Fire world modifier (`-setkey fire`); in the Ashlands it always runs.

		Two ways a fire starts, both only from something lit and only on the ZDO owner:

		- Sparks (Cinder): a CinderSpawner throws one every few seconds, in a random upward-biased
		  direction, and it flies under gravity alone. On a burnable piece (not wet), a tree or a log
		  it always starts a fire; on uncleared grass, outside the Mountains and Deep North and not in
		  rain, with a 32 % chance. Throwers: the bonfire (4 m/s, lands up to 2.9 m away on flat
		  ground), the campfire (1.25 m/s, 1.0 m), a fire on the ground (5 m/s, 3.9 m; from a burning
		  roof farther, 6.2 m from 4 m up, 9.1 m from 12 m), a fire arrow on impact (3 sparks, 2.9 m),
		  a Staff of Embers fireball on impact (6 sparks, 5.0 m), the meteor projectile and the
		  summoned troll. Worked out by simulating the game's own spark code; README has the table.
		- Direct ignition (Fireplace.UpdateIgnite): a lit fireplace sets alight whatever burnable is
		  inside a small capsule around its flame, every 5-10 s. Hearth: about 1.4 m along the
		  world's east-west axis and 0.8 m across, up to 1.7 m high. Bonfire: 1.7 m around, up to
		  6.2 m above it. Iron fire pit 0.7 m, campfire 0.45 m, braziers 0.3 m (a floor brazier:
		  the floor under it), standing torches 0.1 m around the flame. Sconces, lanterns, the
		  jack-o-turnip and NPC fire pits never do.

		A fire burns 30 s at most (its own TimedDestruction), a spark that has not landed is gone after
		3 s. Every fire carries a generation count (CinderSpawner "spread", kept in its ZDO): it throws
		sparks only while the count is above 0, and what its sparks light gets one less. Direct
		ignition hands its own count to what it lights: 4 for the hearth, bonfire and iron fire pit,
		2 for the campfire, 1 for braziers, torches and candles. A bonfire's own sparks start at 3.

		What vanilla does not have at all is a limit in metres. Here every spark and every fire
		remembers the source its chain began at (a lit fireplace, or where an arrow, fireball or
		meteor came down: a position in its ZDO), and a fire that would start farther than MaxRadius
		from that source does not start. MaxSpread caps every generation count, and
		FireplaceIgnition switches direct ignition off entirely -- a hearth, brazier or torch then
		cannot start a fire at all, while a bonfire or campfire still throws sparks.
	*/
	public class FireControl : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fireControlEnabled.Value;
		}

		// Where the chain a spark or fire belongs to began; kept in its ZDO so it survives reloads.
		private static readonly int s_rootKey = "sgg_fire_root".GetStableHashCode();

		// Set while a spark is being thrown, and while a spark lands or a fireplace sets something alight.
		private static Vector3? s_throwRoot, s_igniteRoot;

		private static int s_stopped;

		private static Vector3 RootOf(ZNetView view, Vector3 own)
		{
			ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
			return zdo != null ? zdo.GetVec3(s_rootKey, own) : own;
		}

		// Every generation count the game reads, from a fireplace, a projectile or a fire, is capped here.
		[HarmonyPatch(typeof(CinderSpawner), "GetSpread")]
		public static class CinderSpawner_GetSpread_Patch
		{
			static void Postfix(ref int __result)
			{
				int max = Configuration.fireControlMaxSpread.Value;
				if (max >= 0 && __result > max)
				{
					__result = max;
				}
			}
		}

		[HarmonyPatch(typeof(CinderSpawner), "SpawnCinder")]
		public static class CinderSpawner_SpawnCinder_Patch
		{
			static void Prefix(CinderSpawner __instance)
			{
				s_throwRoot = RootOf(__instance.m_nview, __instance.transform.position);
			}

			static void Finalizer()
			{
				s_throwRoot = null;
			}
		}

		[HarmonyPatch(typeof(Cinder), "Setup")]
		public static class Cinder_Setup_Patch
		{
			static void Postfix(Cinder __instance)
			{
				if (s_throwRoot.HasValue && __instance.m_nview && __instance.m_nview.IsValid())
				{
					__instance.m_nview.GetZDO().Set(s_rootKey, s_throwRoot.Value);
				}
			}
		}

		[HarmonyPatch(typeof(Cinder), "OnHit")]
		public static class Cinder_OnHit_Patch
		{
			static void Prefix(Cinder __instance)
			{
				s_igniteRoot = RootOf(__instance.m_nview, __instance.transform.position);
			}

			static void Finalizer()
			{
				s_igniteRoot = null;
			}
		}

		[HarmonyPatch(typeof(Fireplace), "UpdateIgnite")]
		public static class Fireplace_UpdateIgnite_Patch
		{
			static bool Prefix(Fireplace __instance)
			{
				if (!Configuration.fireControlFireplaceIgnition.Value)
				{
					return false;
				}
				s_igniteRoot = __instance.transform.position;
				return true;
			}

			static void Finalizer()
			{
				s_igniteRoot = null;
			}
		}

		/*
			The one place a new fire learns its generation count and what it burns on, whether a spark
			landed (Cinder.OnHit) or a fireplace reached it (Fireplace.UpdateIgnite). A fire beyond
			MaxRadius from its chain's source is removed in the same frame it was created, before it
			has done anything; the caller only ever calls Setup and moves on.
		*/
		[HarmonyPatch(typeof(CinderSpawner), "Setup")]
		public static class CinderSpawner_Setup_Patch
		{
			static bool Prefix(CinderSpawner __instance, ref int spread)
			{
				int max = Configuration.fireControlMaxSpread.Value;
				if (max >= 0 && spread > max)
				{
					spread = max;
				}
				if (!s_igniteRoot.HasValue || !__instance.m_nview || !__instance.m_nview.IsValid())
				{
					return true;
				}
				Vector3 root = s_igniteRoot.Value;
				float radius = Configuration.fireControlMaxRadius.Value;
				float distance = Vector3.Distance(__instance.transform.position, root);
				if (radius >= 0f && distance > radius)
				{
					if (s_stopped++ < 10)
					{
						ServersidePlugin.logger.LogInfo($"Fire control: a fire {distance:0.0} m from its source at {root.ToString("F0")} did not start (MaxRadius {radius:0.#} m)");
					}
					__instance.m_nview.Destroy();
					return false;
				}
				__instance.m_nview.GetZDO().Set(s_rootKey, root);
				return true;
			}
		}

		private static bool s_announced;

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (s_announced || !Configuration.fireControlEnabled.Value || !ZNetScene.instance)
			{
				return;
			}
			s_announced = true;
			int spread = Configuration.fireControlMaxSpread.Value;
			float radius = Configuration.fireControlMaxRadius.Value;
			ServersidePlugin.logger.LogInfo("Fire control: "
				+ (radius >= 0f ? $"fires start at most {radius:0.#} m from their source" : "no limit in metres")
				+ (spread >= 0 ? $", at most {spread} further generation(s) of sparks" : ", generations as in the game")
				+ (Configuration.fireControlFireplaceIgnition.Value ? ", fireplaces set what touches them alight" : ", fireplaces never set anything alight directly"));
		}
	}
}
