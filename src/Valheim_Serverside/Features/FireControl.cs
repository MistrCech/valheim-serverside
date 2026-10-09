using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
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
		cannot start a fire at all, while a bonfire or campfire still throws sparks. OnlyProjectiles
		goes further: only a projectile starts a fire -- a fire arrow, a Staff of Embers fireball, a
		meteor, a lava rock -- and what it sets alight burns and spreads as before; nothing a base has
		(no fireplace, no fire a fireplace lit, the summoned troll) throws a spark or lights anything.
		Every spark and fire remembers whether its chain began at a projectile (also in its ZDO).
		Surtlings' and fuling shamans' fireballs never set anything alight in the game.

		All of this runs where a fire is simulated, on its owner, and the server only takes over what
		nobody owns or what its owner has left: a hearth a player has just built, or the fire their
		fire arrow started, is created and owned by that player's own game, which would burn it by the
		game's rules until they leave or the server restarts. So every fire source a player's game
		reports is taken over by the server the moment it arrives (see TakeOver). What stays on the
		player's game is their own arrow or fireball and the sparks it throws where it lands.
	*/
	public class FireControl : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fireControlEnabled.Value;
		}

		// Where the chain a spark or fire belongs to began, and whether that was a projectile; kept in its ZDO so it survives reloads.
		private static readonly int s_rootKey = "sgg_fire_root".GetStableHashCode();
		private static readonly int s_kindKey = "sgg_fire_kind".GetStableHashCode();
		private const int Unknown = 0, FromProjectile = 1, FromElsewhere = 2;

		// Set while a spark is being thrown, and while a spark lands or a fireplace sets something alight.
		private static Vector3? s_throwRoot, s_igniteRoot;
		private static int s_throwKind, s_igniteKind;

		private static int s_stopped;

		private static Vector3 RootOf(ZNetView view, Vector3 own)
		{
			ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
			return zdo != null ? zdo.GetVec3(s_rootKey, own) : own;
		}

		/*
			A spark thrown by a projectile (fire arrow, fireball, meteor) starts a chain from a projectile;
			a fire keeps what its chain began from. Everything else that throws sparks -- a bonfire or
			campfire, a fire a fireplace lit, the summoned troll -- is not a projectile.
		*/
		private static int KindOf(CinderSpawner spawner)
		{
			if (spawner.GetComponentInParent<Projectile>())
			{
				return FromProjectile;
			}
			ZDO zdo = spawner.m_nview && spawner.m_nview.IsValid() ? spawner.m_nview.GetZDO() : null;
			int kind = zdo != null ? zdo.GetInt(s_kindKey, Unknown) : Unknown;
			return kind == Unknown ? FromElsewhere : kind;
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
			static bool Prefix(CinderSpawner __instance)
			{
				int kind = KindOf(__instance);
				// Only projectiles: nothing else even throws its sparks.
				if (Configuration.fireControlOnlyProjectiles.Value && kind != FromProjectile)
				{
					return false;
				}
				s_throwRoot = RootOf(__instance.m_nview, __instance.transform.position);
				s_throwKind = kind;
				return true;
			}

			static void Finalizer()
			{
				s_throwRoot = null;
				s_throwKind = Unknown;
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
					__instance.m_nview.GetZDO().Set(s_kindKey, s_throwKind);
				}
			}
		}

		[HarmonyPatch(typeof(Cinder), "OnHit")]
		public static class Cinder_OnHit_Patch
		{
			static void Prefix(Cinder __instance)
			{
				s_igniteRoot = RootOf(__instance.m_nview, __instance.transform.position);
				ZDO zdo = __instance.m_nview && __instance.m_nview.IsValid() ? __instance.m_nview.GetZDO() : null;
				int kind = zdo != null ? zdo.GetInt(s_kindKey, Unknown) : Unknown;
				// A spark no spawner threw comes from a projectile's own hit (lava rocks) or the Ashlands sky.
				s_igniteKind = kind == Unknown ? FromProjectile : kind;
			}

			static void Finalizer()
			{
				s_igniteRoot = null;
				s_igniteKind = Unknown;
			}
		}

		[HarmonyPatch(typeof(Fireplace), "UpdateIgnite")]
		public static class Fireplace_UpdateIgnite_Patch
		{
			static bool Prefix(Fireplace __instance)
			{
				if (!Configuration.fireControlFireplaceIgnition.Value || Configuration.fireControlOnlyProjectiles.Value)
				{
					return false;
				}
				s_igniteRoot = __instance.transform.position;
				s_igniteKind = FromElsewhere;
				return true;
			}

			static void Finalizer()
			{
				s_igniteRoot = null;
				s_igniteKind = Unknown;
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
				if (Configuration.fireControlOnlyProjectiles.Value && s_igniteKind != FromProjectile)
				{
					__instance.m_nview.Destroy();
					return false;
				}
				__instance.m_nview.GetZDO().Set(s_rootKey, root);
				__instance.m_nview.GetZDO().Set(s_kindKey, s_igniteKind);
				return true;
			}
		}

		/*
			Fire sources: every fire, and every fireplace that sets what touches it alight or throws
			sparks. Projectiles and creatures are not: their owner has to fly or move them.
		*/
		private static HashSet<int> s_sources, s_fires;

		private static bool IsSource(int prefab, out bool fire)
		{
			fire = false;
			if (s_sources == null)
			{
				if (!ZNetScene.instance || ZNetScene.instance.m_prefabs.Count == 0)
				{
					return false;
				}
				s_sources = new HashSet<int>();
				s_fires = new HashSet<int>();
				foreach (GameObject candidate in ZNetScene.instance.m_prefabs)
				{
					if (!candidate || candidate.GetComponent<Projectile>() || candidate.GetComponent<Character>())
					{
						continue;
					}
					int hash = candidate.name.GetStableHashCode();
					Fireplace fireplace = candidate.GetComponent<Fireplace>();
					if (candidate.GetComponent<Fire>())
					{
						s_fires.Add(hash);
						s_sources.Add(hash);
					}
					else if (candidate.GetComponentInChildren<CinderSpawner>(true)
						|| (fireplace && fireplace.m_firePrefab && fireplace.m_igniteInterval > 0f && fireplace.m_igniteCapsuleRadius > 0f))
					{
						s_sources.Add(hash);
					}
				}
			}
			fire = s_fires.Contains(prefab);
			return s_sources.Contains(prefab);
		}

		private static int s_takenOver, s_takeOverErrors;
		private static bool s_receiving;
		private static readonly List<ZDOID> s_claimed = new List<ZDOID>();
		private static readonly List<ZDOID> s_putOut = new List<ZDOID>();

		/*
			A fire source a player's game names itself the owner of -- one it has just created, an update
			from a game that has not heard yet that it is no longer the owner, or a fireplace it claimed
			with nothing changed (Fireplace.Interact claims an ownerless one; that update carries no data
			and never reaches ZDO.Deserialize) -- becomes the server's on the spot. Every object the
			message names an owner for is noted (ZDO.SetOwnerInternal), and once the server has read
			the whole message it takes the fire sources among them; earlier, RPC_ZDOData would still
			overwrite the owner revision it raises. The owner change alone is sent back to that game,
			which then stops simulating it -- long before its first spark (2-5 s after it appears) or
			direct ignition (5 s).

			A fire that arrives without a record of its chain was lit on a player's game by a spark from
			a projectile -- a fire arrow, a Staff of Embers fireball, a meteor -- since every fireplace
			there is the server's: it counts as one, with its own position as the source (where the
			projectile came down is 2.9 m away at most for an arrow, 5.0 m for a fireball). The
			exception is a creature that throws sparks itself, the troll a player summons: a fire that
			arrives within 8 m of one is not from a projectile, and with OnlyProjectiles it is put out.
		*/
		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public static class ZDOMan_RPC_ZDOData_Patch
		{
			static void Prefix()
			{
				s_claimed.Clear();
				s_receiving = true;
			}

			static void Finalizer()
			{
				s_receiving = false;
				try
				{
					TakeOver();
				}
				catch (Exception e)
				{
					if (s_takeOverErrors++ < 5)
					{
						ServersidePlugin.logger.LogWarning($"Fire control: {e}");
					}
				}
				s_claimed.Clear();
			}
		}

		[HarmonyPatch(typeof(ZDO), "SetOwnerInternal")]
		public static class ZDO_SetOwnerInternal_Patch
		{
			static void Postfix(ZDO __instance, long uid)
			{
				if (s_receiving && uid != 0L && uid != ZDOMan.GetSessionID())
				{
					s_claimed.Add(__instance.m_uid);
				}
			}
		}

		private static void TakeOver()
		{
			long server = ZDOMan.GetSessionID();
			foreach (ZDOID id in s_claimed)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				long owner = zdo != null ? zdo.GetOwner() : 0L;
				if (owner == 0L || owner == server || !IsSource(zdo.GetPrefab(), out bool fire))
				{
					continue;
				}
				zdo.SetOwner(server);
				string why = "";
				if (fire && zdo.GetInt(s_kindKey, Unknown) == Unknown)
				{
					bool creature = SparkingCreatureNear(zdo.GetPosition());
					zdo.Set(s_kindKey, creature ? FromElsewhere : FromProjectile);
					zdo.Set(s_rootKey, zdo.GetPosition());
					why = creature ? ", lit by a summoned creature" : ", lit by a projectile";
					if (creature && Configuration.fireControlOnlyProjectiles.Value)
					{
						s_putOut.Add(id);
						why += " -- put out (only projectiles start fires)";
					}
				}
				if (s_takenOver++ < 10)
				{
					GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
					ServersidePlugin.logger.LogInfo($"Fire control: took over {(prefab ? prefab.name : "a fire source")} at {zdo.GetPosition().ToString("F0")} from a player's game{why}");
				}
			}
		}

		private static bool SparkingCreatureNear(Vector3 position)
		{
			foreach (Character character in Character.GetAllCharacters())
			{
				if (character && (character.transform.position - position).sqrMagnitude < 8f * 8f && character.GetComponentInChildren<CinderSpawner>())
				{
					return true;
				}
			}
			return false;
		}

		/*
			What a fire burns on is only known to the game that lit it (CinderSpawner.Setup keeps it in a
			plain field), and vanilla puts a fire out the moment that object is gone. A fire the server
			simulates without having lit it -- taken over from a player's game -- looks for it itself:
			the burnable piece, tree or log its flame touches.
		*/
		private static readonly int s_burnableMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle");
		private static readonly Collider[] s_touching = new Collider[16];

		[HarmonyPatch(typeof(CinderSpawner), "Awake")]
		public static class CinderSpawner_Awake_Patch
		{
			static void Postfix(CinderSpawner __instance)
			{
				if (__instance.m_hasAttachObj || !__instance.m_nview || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner() || !__instance.GetComponent<Fire>())
				{
					return;
				}
				Vector3 position = __instance.transform.position;
				int count = Physics.OverlapSphereNonAlloc(position, 0.5f, s_touching, s_burnableMask);
				GameObject best = null;
				float bestDistance = float.MaxValue;
				for (int i = 0; i < count; i++)
				{
					Collider collider = s_touching[i];
					if (collider.isTrigger || collider.transform.IsChildOf(__instance.transform)
						|| !(collider.GetComponentInParent<WearNTear>() || collider.GetComponentInParent<TreeBase>() || collider.GetComponentInParent<TreeLog>()))
					{
						continue;
					}
					float distance = (collider.ClosestPoint(position) - position).sqrMagnitude;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = collider.gameObject;
					}
				}
				if (best)
				{
					__instance.m_attachObj = best;
					__instance.m_hasAttachObj = true;
				}
			}
		}

		private static bool s_announced;

		// Called from ServersidePlugin.Update while the mod is installed.
		public static void Tick()
		{
			if (!Configuration.fireControlEnabled.Value || !ZNetScene.instance)
			{
				return;
			}
			foreach (ZDOID id in s_putOut)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsOwner())
				{
					ZNetView view = ZNetScene.instance.FindInstance(zdo);
					if (view)
					{
						ZNetScene.instance.Destroy(view.gameObject);
					}
					else
					{
						ZDOMan.instance.DestroyZDO(zdo);
					}
				}
			}
			s_putOut.Clear();
			if (s_announced)
			{
				return;
			}
			s_announced = true;
			IsSource(0, out _);
			if (s_sources != null)
			{
				List<string> names = new List<string>();
				foreach (GameObject candidate in ZNetScene.instance.m_prefabs)
				{
					if (candidate && s_sources.Contains(candidate.name.GetStableHashCode()))
					{
						names.Add(candidate.name);
					}
				}
				names.Sort(StringComparer.Ordinal);
				ServersidePlugin.logger.LogInfo($"Fire control: {names.Count} fire sources the server takes over from players' games: {string.Join(", ", names)}");
			}
			int spread = Configuration.fireControlMaxSpread.Value;
			float radius = Configuration.fireControlMaxRadius.Value;
			ServersidePlugin.logger.LogInfo("Fire control: "
				+ (radius >= 0f ? $"fires start at most {radius:0.#} m from their source" : "no limit in metres")
				+ (spread >= 0 ? $", at most {spread} further generation(s) of sparks" : ", generations as in the game")
				+ (Configuration.fireControlOnlyProjectiles.Value ? ", only projectiles start fires (fire arrows, fireballs, meteors, lava rocks) and what they set alight"
					: Configuration.fireControlFireplaceIgnition.Value ? ", fireplaces set what touches them alight" : ", fireplaces never set anything alight directly"));
		}
	}
}
