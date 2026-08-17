using System;
using System.Collections.Generic;
using System.IO;
using Ironhide.AssetBundles;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Lists deco prefabs in the scenario bundle and loads one by name.</summary>
	/// <remarks>
	/// Vanilla rooms store props as PPtrs on the templates prefab. Lab places
	/// by name the same way cinematics do:
	/// <c>AssetBundleManager.LoadAsset&lt;GameObject&gt;("scenario", name)</c>
	/// (the manager lowercases the name). Does not call <c>LoadAssetBundle</c>
	/// when the bundle is already loaded. FXMega / Skill / Comic stay out —
	/// only asset names that contain <c>deco</c> are listed.
	/// </remarks>
	internal static class EncounterPropCatalog
	{
		private static List<string> cachedNames;

		/// <summary>Sorted lowercase deco prefab names. Cached after the first scan.</summary>
		internal static IReadOnlyList<string> ListNames()
		{
			if (cachedNames != null)
			{
				return cachedNames;
			}

			cachedNames = new List<string>();
			AssetBundle bundle = EnsureScenarioBundle();
			if (bundle == null)
			{
				return cachedNames;
			}

			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] assets = bundle.GetAllAssetNames();
			if (assets == null)
			{
				return cachedNames;
			}

			for (int i = 0; i < assets.Length; i++)
			{
				string name = PrefabName(assets[i]);
				if (!LooksLikeDeco(name) || !seen.Add(name))
				{
					continue;
				}

				cachedNames.Add(name);
			}

			cachedNames.Sort(StringComparer.OrdinalIgnoreCase);
			return cachedNames;
		}

		/// <summary>Loads the scenario prefab by name, falling back to the templates bundle. Null when neither has it.</summary>
		/// <remarks>
		/// Most Add Prop catalogue picks are curated <c>scenario</c> deco names (see the class
		/// remarks). Props read off a vanilla room's own <c>encounterObjs</c> by
		/// <c>VanillaEncounterImporter</c> are not guaranteed to be scenario-bundle deco --
		/// some room-specific dressing is embedded directly in the <c>templates</c> bundle the
		/// room prefab itself came from, so a scenario miss falls back there before giving up.
		/// </remarks>
		internal static GameObject Load(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName))
			{
				return null;
			}

			string name = prefabName.ToLowerInvariant();
			AssetBundle scenario = EnsureScenarioBundle();
			GameObject prefab = scenario != null ? scenario.LoadAsset<GameObject>(name) : null;
			if (prefab != null)
			{
				return prefab;
			}

			AssetBundle templates = EncounterTerrainCatalog.EnsureTemplatesBundle();
			return templates != null ? templates.LoadAsset<GameObject>(name) : null;
		}

		/// <summary>Short label for the Add Prop picker.</summary>
		internal static string Label(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName))
			{
				return "(none)";
			}

			return prefabName;
		}

		/// <summary>The boot-loaded scenario bundle, or null when it cannot be opened.</summary>
		internal static AssetBundle GetBundle()
		{
			return EnsureScenarioBundle();
		}

		/// <summary>The boot-loaded scenario bundle, or a first-time load if it is missing.</summary>
		/// <remarks>
		/// Same guard as the templates catalog: do not call
		/// <c>LoadAssetBundle</c> when Unity already has the bundle.
		/// </remarks>
		private static AssetBundle EnsureScenarioBundle()
		{
			AssetBundle bundle = AssetBundleManager.GetBundle("scenario");
			if (bundle != null)
			{
				return bundle;
			}

			foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (loaded != null && string.Equals(loaded.name, "scenario", StringComparison.OrdinalIgnoreCase))
				{
					return loaded;
				}
			}

			try
			{
				return AssetBundleManager.LoadAssetBundle("scenario");
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string PrefabName(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
			{
				return string.Empty;
			}

			return Path.GetFileNameWithoutExtension(assetPath.Replace('\\', '/')).ToLowerInvariant();
		}

		private static bool LooksLikeDeco(string name)
		{
			return !string.IsNullOrEmpty(name) && name.IndexOf("deco", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
