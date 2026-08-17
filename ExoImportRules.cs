using System;

namespace LokrLab
{
	/// <summary>Unity-free rules for which shipped exo to reconstruct and when a Lab rig is still map-only.</summary>
	/// <remarks>
	/// Hero MetaDataAssets (ExoSkeletonHumanRanger_MetaDataAsset) hold Stand / Portrait / Victory.
	/// Combat clips (Walk, Attack, Death, …) live on the Model prefab's ExoSkeletonData.asset.
	/// See docs/reference/ExoSkeletonHumanRanger_MetaDataAsset.dump.txt (animations size = 5).
	/// </remarks>
	internal static class ExoImportRules
	{
		/// <summary>Model KV inferred from a MetaExo asset name, or empty when the name is not that pattern.</summary>
		internal static string InferModelFromMetaExo(string metaExoName)
		{
			if (string.IsNullOrEmpty(metaExoName))
			{
				return string.Empty;
			}

			const string prefix = "ExoSkeleton";
			const string suffix = "_MetaDataAsset";
			if (!metaExoName.StartsWith(prefix, StringComparison.Ordinal)
				|| !metaExoName.EndsWith(suffix, StringComparison.Ordinal)
				|| metaExoName.Length <= prefix.Length + suffix.Length)
			{
				return string.Empty;
			}

			return metaExoName.Substring(
				prefix.Length,
				metaExoName.Length - prefix.Length - suffix.Length);
		}

		/// <summary>True when the combat prefab exo has at least as many clips as the MetaExo and is not empty.</summary>
		internal static bool PreferPrefabExo(int prefabAnimationCount, int metaAnimationCount)
		{
			return prefabAnimationCount > 0 && prefabAnimationCount >= metaAnimationCount;
		}

		/// <summary>True when rig.json already contains a combat clip name (Walk / Attack / Death), not only map poses.</summary>
		internal static bool JsonHasCombatClip(string rigJson)
		{
			if (string.IsNullOrEmpty(rigJson))
			{
				return false;
			}

			return HasClipName(rigJson, "Walk")
				|| HasClipName(rigJson, "Run")
				|| HasClipName(rigJson, "Attack")
				|| HasClipName(rigJson, "Attack0")
				|| HasClipName(rigJson, "SpecialAttack")
				|| HasClipName(rigJson, "SpecialAttackA")
				|| HasClipName(rigJson, "Death");
		}

		private static bool HasClipName(string rigJson, string clipName)
		{
			return rigJson.IndexOf("\"name\":\"" + clipName + "\"", StringComparison.Ordinal) >= 0;
		}
	}
}
