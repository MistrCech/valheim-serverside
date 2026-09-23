using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using SoftReferenceableAssets;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		A dungeon whose room bundle Unity refuses to load never spawns, and its zone stays flagged as
		loading for good. In the game's asset loader (SoftReferenceableAssets, Valheim 1.0.15):

		- Unity refuses a bundle with "another AssetBundle with the same files is already loaded"
		  and hands back no bundle; BundleLoader.OnLoadAsyncCompleted stores that null and still
		  reports the load as succeeded;
		- AssetLoader.OnBundleLoadCompleted then calls LoadAssetFromBundleAsync, which dereferences
		  the null bundle and throws;
		- so the asset's callbacks never run, DungeonGenerator.OnRoomLoaded never hears back, the
		  dungeon never spawns and ZoneSystem keeps its zone in m_loadingObjectsInZones.

		A vanilla dedicated server never instantiates dungeons near players, so it never gets there.
		This mod does, for every player, all day. Seen on a 1.0.15 Linux server as an endless loop
		over the same two dungeons (ddormer/valheim-serverside#120).

		Three guards, each of which does nothing unless that failure happens:
		1. OnLoadAsyncCompleted without a bundle: take the bundle Unity says is already loaded, the
		   same one under the same name, or report the load as failed if there is none.
		2. LoadAssetFromBundleAsync without a bundle: report the asset as failed instead of throwing.
		3. OnRoomLoaded with a failed room: let the dungeon go -- no spawn, its rooms and its zone
		   released -- so the zone keeps working and the dungeon is tried again the next time it is
		   created. Skipped when an earlier prefix already handled it (ValheimCommunityPatch has its
		   own fix for this callback, which then spawns the dungeon without the missing room).
	*/
	public class DungeonLoadGuard : IFeature
	{
		private static readonly HashSet<string> s_logged = new HashSet<string>();

		public bool FeatureEnabled()
		{
			return Configuration.fixDungeonLoadGuard.Value;
		}

		private static void LogOnce(string key, string text)
		{
			if (s_logged.Add(key))
			{
				ServersidePlugin.logger.LogWarning(text);
			}
		}

		// The bundle Unity already holds for this loader: loaded under the loader's name, or its file's.
		private static AssetBundle FindLoaded(string bundleName, string bundlePath)
		{
			string fileName = string.IsNullOrEmpty(bundlePath) ? null : Path.GetFileName(bundlePath);
			foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (bundle != null && (bundle.name == bundleName || (fileName != null && bundle.name == fileName)))
				{
					return bundle;
				}
			}
			return null;
		}

		[HarmonyPatch(typeof(BundleLoader), "OnLoadAsyncCompleted", new Type[0])]
		public static class BundleLoader_OnLoadAsyncCompleted_Patch
		{
			static bool Prefix(ref BundleLoader __instance)
			{
				AssetBundleCreateRequestWrapper request = __instance.m_asyncLoadRequest;
				if (request == null || request.AssetBundle != null)
				{
					return true;
				}
				AssetBundle held = FindLoaded(__instance.m_bundleName, __instance.m_bundlePath);
				__instance.m_bundle = held;
				__instance.m_asyncLoadRequest = null;
				LogOnce("bundle " + __instance.m_bundleName, held != null
					? $"Unity refused to load bundle {__instance.m_bundleName} again because it is already loaded; using the one it holds, so what needs it still loads."
					: $"Unity returned no bundle for {__instance.m_bundleName}; reporting its assets as failed rather than letting the game throw on it.");
				if (__instance.m_shouldBeLoaded)
				{
					__instance.InvokeCallbacks(held != null ? LoadResult.Succeeded : LoadResult.Failed);
				}
				else if (BundleLoader.s_tickOnAsyncOperationComplete)
				{
					__instance.TickAsync();
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(AssetLoader), "LoadAssetFromBundleAsync")]
		public static class AssetLoader_LoadAssetFromBundleAsync_Patch
		{
			static bool Prefix(ref AssetLoader __instance)
			{
				AssetBundleLoader loader = AssetBundleLoader.Instance;
				if (loader == null || loader.m_bundleLoaders[__instance.m_bundleLoaderIndex].Bundle != null)
				{
					return true;
				}
				LogOnce("asset " + __instance.m_assetID, $"Asset {__instance.m_assetID} ({__instance.m_assetPathInBundle}) has no bundle to load from; reporting it as failed.");
				__instance.InvokeCallbacks(LoadResult.Failed);
				return false;
			}
		}

		[HarmonyPatch(typeof(DungeonGenerator), "OnRoomLoaded")]
		public static class DungeonGenerator_OnRoomLoaded_Patch
		{
			static bool Prefix(DungeonGenerator __instance, LoadResult result, bool __runOriginal)
			{
				if (!__runOriginal || result == LoadResult.Succeeded || __instance == null || !__instance)
				{
					return true;
				}
				Vector3 position = __instance.transform.position;
				LogOnce("dungeon " + ZoneSystem.GetZone(position), $"Dungeon {__instance.name} at {position:F0}: a room failed to load ({result}). Letting it go so its zone keeps working; it is tried again the next time it is created.");
				// Never reaches zero now, so rooms that still arrive do not spawn half a dungeon.
				__instance.m_roomsToLoad = int.MaxValue;
				__instance.ReleaseHeldReferences();
				return false;
			}
		}
	}
}
