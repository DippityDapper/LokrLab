using System;
using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends.Model.Game.Units.Abilities;
using LokrModAPI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Read-only catalog of every shipped ability, parsed straight from the game's own Resources bundle.</summary>
	/// <remarks>
	/// Built for the Vanilla Ability Edit track's Phase 1 browser
	/// (docs/roadmaps/started/vanilla-ability-edit.md) -- reads the same Resources folder
	/// AbilitiesDefinitionsPatches.ExecuteLoad does
	/// (LokrCharacterLoader/Patches/AbilitiesDefinitionsPatches.cs:50-52), not the dev-only
	/// docs/character-reference/_extracted/ dump, so this works in a shipped build and always
	/// reflects vanilla only (Lab/mod overrides are merged into the parser's own dictionary, never
	/// written back into this Resources folder). A handful of bundle TextAssets hold more than one
	/// top-level ability block (e.g. _basicAbilities.txt), so each asset is split with
	/// AbilityKvIO.LoadAllFromText rather than the single-root TryLoad Lab-authored folders use.
	/// </remarks>
	internal static class VanillaAbilityCatalog
	{
		private static List<AbilityFileModel> cache;
		private static Dictionary<string, string> sourceTextById;

		/// <summary>Every shipped ability, sorted by id. Built once and cached; call Refresh() to force a re-read.</summary>
		internal static IReadOnlyList<AbilityFileModel> All()
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
			List<AbilityFileModel> models = new List<AbilityFileModel>();
			Dictionary<string, string> rawTextById = new Dictionary<string, string>();
			AbilitiesDefinitions instance = AbilitiesDefinitions.instance;
			string abilitiesFolder = instance != null
				? Traverse.Create(instance).Field<string>("abilitiesFolder").Value
				: null;

			if (!string.IsNullOrEmpty(abilitiesFolder))
			{
				foreach (TextAsset asset in ResourcesWrapper.LoadAll<TextAsset>(abilitiesFolder))
				{
					List<AbilityFileModel> parsed = AbilityKvIO.LoadAllFromText(asset.text, asset.name, out Dictionary<string, string> assetRawText, out _);
					models.AddRange(parsed);
					foreach (KeyValuePair<string, string> pair in assetRawText)
					{
						rawTextById[pair.Key] = pair.Value;
					}

					Resources.UnloadAsset(asset);
				}
			}

			models.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
			cache = models;
			sourceTextById = rawTextById;
		}

		/// <summary>Finds one shipped ability by id, or null.</summary>
		internal static AbilityFileModel Find(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			foreach (AbilityFileModel model in All())
			{
				if (string.Equals(model.Id, id, StringComparison.Ordinal))
				{
					return model;
				}
			}

			return null;
		}

		/// <summary>The shipped ability's own KV block source text (KeyValue.ToString(0)), or null. Used to copy it into a Lab library folder close to verbatim -- see VanillaAbilityImporter.</summary>
		internal static string FindSourceText(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			All();
			return sourceTextById != null && sourceTextById.TryGetValue(id, out string text) ? text : null;
		}
	}
}
