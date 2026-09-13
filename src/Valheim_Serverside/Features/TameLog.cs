using System;
using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		A line in the server log whenever a tamed creature dies, saying what killed it -- for the
		"where did the pigs go" question. A creature's death runs through Character.CheckDeath on
		its owner (with this mod, the server) once its health has reached zero, and the last hit
		applied to it (Character.m_lastHit, set in ApplyDamage) says how: the hit's type (a
		creature's attack, a player's, burning, smoke, a fall, drowning, freezing, poison, ...) and
		the attacker's object id, which names the creature -- tamed or not, with its level -- or the
		player. A tamed creature taken out of the world while alive (ZNetScene.Destroy on it, as a
		console command would) is logged as well.
	*/
	public class TameLog : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.logTameDeaths.Value;
		}

		[HarmonyPatch(typeof(Character), "CheckDeath")]
		public static class Character_CheckDeath_Patch
		{
			static void Prefix(Character __instance)
			{
				if (!Configuration.logTameDeaths.Value || __instance.IsDead() || __instance.GetHealth() > 0f || !__instance.IsTamed())
				{
					return;
				}
				try
				{
					ServersidePlugin.logger.LogInfo($"Tame died: {Describe(__instance)}: {Cause(__instance.m_lastHit)}");
				}
				catch (Exception e)
				{
					ServersidePlugin.logger.LogWarning($"Tame died, but describing it failed: {e.Message}");
				}
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "Destroy", typeof(GameObject))]
		public static class ZNetScene_Destroy_Patch
		{
			static void Prefix(GameObject go)
			{
				if (!Configuration.logTameDeaths.Value || !go)
				{
					return;
				}
				Character character = go.GetComponent<Character>();
				if (character && !character.IsDead() && character.GetHealth() > 0f && character.IsTamed())
				{
					ServersidePlugin.logger.LogInfo($"Tame removed alive (not a death; a command or another mod): {Describe(character)}");
				}
			}
		}

		// "Boar 'Pepa' (level 2, 60 of 60 health) at (-2451, 33, 2522)"
		private static string Describe(Character character)
		{
			ZDO zdo = character.m_nview ? character.m_nview.GetZDO() : null;
			string name = zdo != null ? zdo.GetString(ZDOVars.s_tamedName) : "";
			Vector3 at = character.transform.position;
			return $"{PrefabName(zdo, character.gameObject.name)}{(name.Length > 0 ? $" '{name}'" : "")} (level {character.GetLevel()}, {character.GetMaxHealth():0} health) at ({at.x:0}, {at.y:0}, {at.z:0})";
		}

		private static string Cause(HitData hit)
		{
			if (hit == null)
			{
				return "no hit recorded (health set to zero directly)";
			}
			string how = $"{hit.m_hitType}, {hit.GetTotalDamage():0} damage";
			if (hit.m_attacker.IsNone())
			{
				return how;
			}
			ZDO attacker = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(hit.m_attacker) : null;
			if (attacker == null)
			{
				return $"{how}, by something no longer in the world ({hit.m_attacker})";
			}
			string prefab = PrefabName(attacker, null);
			if (prefab == "Player")
			{
				return $"{how}, by player {PlayerName(hit.m_attacker, attacker)}";
			}
			string tamed = attacker.GetBool(ZDOVars.s_tamed) ? "tamed " : "";
			string tamedName = attacker.GetString(ZDOVars.s_tamedName);
			return $"{how}, by {tamed}{prefab}{(tamedName.Length > 0 ? $" '{tamedName}'" : "")} (level {attacker.GetInt(ZDOVars.s_level, 1)})";
		}

		private static string PlayerName(ZDOID characterId, ZDO zdo)
		{
			if (ZNet.instance)
			{
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					if (peer.m_characterID == characterId)
					{
						return peer.m_playerName;
					}
				}
			}
			string name = zdo.GetString(ZDOVars.s_playerName);
			return name.Length > 0 ? name : characterId.ToString();
		}

		private static string PrefabName(ZDO zdo, string fallback)
		{
			GameObject prefab = zdo != null && ZNetScene.instance ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
			if (prefab)
			{
				return prefab.name;
			}
			return fallback != null ? fallback.Replace("(Clone)", "") : "?";
		}
	}
}
