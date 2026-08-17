using System;
using System.Collections.Generic;
using Ironhide.Legends.Model.Game.Units;
using LokrModAPI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Read-only catalog of every shipped hero/enemy unit definition, parsed straight from the game's own Resources bundle.</summary>
	/// <remarks>
	/// The "extend the scan to RLHeroes_new.txt and EnemiesDefinitions" half of Phase 4
	/// (docs/roadmaps/started/vanilla-ability-edit.md) -- same shape as VanillaAbilityCatalog, reads
	/// Balance/UnitDefinitions (the folder UnityDefinitionsParser.LoadData hardcodes as a string
	/// literal; unlike AbilitiesDefinitions.abilitiesFolder there's no private field to Traverse)
	/// via ResourcesWrapper.LoadAll&lt;TextAsset&gt;, so it's always vanilla-only.
	///
	/// Parses each asset with UnityDefinitionsParser.instance.ParseText (already Harmony-patched by
	/// UnityDefinitionsParserPatches to drop the duplicate-key crash), not
	/// CharacterAPI.KnownUnitDefinitions -- that dictionary also holds every mod/Lab fragment, and
	/// more importantly for a blast-radius scan, pre-SolveInheritance objects (a Lvl2/Lvl3 row that
	/// inherits skills from its parent shows skills == null there, so a scan over it can miss real
	/// references). A per-block scan here only sees explicitly written skill references for the same
	/// reason -- the parent block itself still matches.
	///
	/// Retains each definition's source asset name (e.g. "EnemiesDefinitions_Tutorial") since
	/// UnitDefinition itself doesn't carry that provenance -- it's the only concrete signal this
	/// track found for a tutorial/progression blocklist (see IsFromTutorialAsset).
	/// </remarks>
	internal static class VanillaUnitCatalog
	{
		private const string UnitDefinitionsFolder = "Balance/UnitDefinitions";

		private static Dictionary<string, UnitDefinition> cache;
		private static Dictionary<string, string> sourceAssetById;

		/// <summary>Every shipped unit definition, keyed by its KV block key. Built once and cached; call Refresh() to force a re-read.</summary>
		internal static IReadOnlyDictionary<string, UnitDefinition> All()
		{
			if (cache == null)
			{
				Refresh();
			}

			return cache;
		}

		/// <summary>Re-reads the catalog from the live Resources bundle.</summary>
		internal static void Refresh()
		{
			Dictionary<string, UnitDefinition> definitions = new Dictionary<string, UnitDefinition>();
			Dictionary<string, string> sourceAsset = new Dictionary<string, string>();
			UnityDefinitionsParser parser = null;
			try
			{
				parser = UnityDefinitionsParser.instance;
			}
			catch (Exception)
			{
			}

			if (parser != null)
			{
				foreach (TextAsset asset in ResourcesWrapper.LoadAll<TextAsset>(UnitDefinitionsFolder))
				{
					foreach (KeyValuePair<string, UnitDefinition> entry in parser.ParseText(asset.text))
					{
						definitions[entry.Key] = entry.Value;
						sourceAsset[entry.Key] = asset.name;
					}

					Resources.UnloadAsset(asset);
				}
			}

			cache = definitions;
			sourceAssetById = sourceAsset;
		}

		/// <summary>True when this unit's own source asset name looks tutorial-only (e.g. EnemiesDefinitions_Tutorial).</summary>
		/// <remarks>A soft signal for Phase 4's "consider a blocklist," not a hard block -- surfaced as a warning in the copy-confirm modal, nothing more.</remarks>
		internal static bool IsFromTutorialAsset(string unitBlockKey)
		{
			All();
			return sourceAssetById != null
				&& !string.IsNullOrEmpty(unitBlockKey)
				&& sourceAssetById.TryGetValue(unitBlockKey, out string asset)
				&& asset != null
				&& asset.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
