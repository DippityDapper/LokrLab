using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ironhide.Legends.Model.Game.Units.Abilities;
using Ironhide.Localization;
using LokrCharacterLoader;
using LokrLab;

namespace LokrAbilityLab.Editor
{
	/// <summary>Override keeps the vanilla KV block key so last-wins replaces it everywhere; Fork mints a new id.</summary>
	internal enum VanillaAbilityImportMode
	{
		Override,
		Fork
	}

	/// <summary>Copies a shipped ability into a real Lab library folder -- Phase 2 of the Vanilla Ability Edit track.</summary>
	/// <remarks>
	/// Writes the vanilla KV block text back out close to verbatim
	/// (VanillaAbilityCatalog.FindSourceText) instead of round-tripping through
	/// AbilityKvIO.TryLoad -&gt; AbilityFileModel -&gt; TryBuildText, which is lossy (TryBuildText
	/// omits several passive-ability fields and can drop opaque modifier content -- see its own
	/// remarks, and AbilityIdentityRekey's "Text rewrite only" note on RewriteOnDisk) and, before
	/// AbilityEventNames.AllModifierEvents gained "OnSpawn" alongside this class, rejected ~13
	/// vanilla files outright. Matches LegacyModImporter.WriteAbility's approach for the identical
	/// reason.
	///
	/// Override's folder is still a minted slug_token (so two overrides of the same vanilla id, or
	/// an override sitting next to an unrelated ability, never collide on disk) -- only the KV block
	/// key inside ability.txt stays vanilla, which is what the loader's last-wins merge keys off.
	/// The Ability Library browser's own node tree is built from folder names
	/// (AbilityLabPaths.EnumerateAbilitiesIn), not each folder's parsed block key, so an override
	/// folder is not guaranteed to be reachable via the existing "Open" navigation yet -- a known
	/// gap for a later phase, not attempted here.
	///
	/// Deliberately does not open the result in the card editor or add a File menu "Edit Vanilla
	/// Ability..." entry -- docs/roadmaps/started/vanilla-ability-edit.md Phase 3 (round-trip
	/// fidelity audit) gates that until it's measured. This is the copy-into-library mechanism only,
	/// invoked from the Phase 1 browser's detail view.
	/// </remarks>
	internal static class VanillaAbilityImporter
	{
		/// <summary>Copies vanillaId into libraryFolder under a newly minted slug_token folder.</summary>
		internal static bool TryImport(string vanillaId, string libraryFolder, VanillaAbilityImportMode mode, out string newFolder, out string error)
		{
			newFolder = null;
			error = null;

			AbilityFileModel model = VanillaAbilityCatalog.Find(vanillaId);
			string sourceText = VanillaAbilityCatalog.FindSourceText(vanillaId);
			if (model == null || string.IsNullOrEmpty(sourceText))
			{
				error = "'" + vanillaId + "' is not a shipped ability.";
				return false;
			}

			if (string.IsNullOrEmpty(libraryFolder) || !Directory.Exists(libraryFolder))
			{
				error = "Library folder does not exist.";
				return false;
			}

			string slug = LabSlugIds.LegalizeSlug(vanillaId, "ability");
			string mintedId = AbilityLabPaths.GenerateNewAbilityId(slug);
			string destFolder = AbilityLabPaths.AbilityFolder(libraryFolder, mintedId);
			string destPath = AbilityLabPaths.AbilityDefinitionPath(libraryFolder, mintedId);

			string text = sourceText;
			string locStem = string.IsNullOrEmpty(model.LocalizationId) ? model.Id : model.LocalizationId;
			string engineId = vanillaId;

			if (mode == VanillaAbilityImportMode.Fork)
			{
				text = RewriteBlockKey(text, vanillaId, mintedId);
				text = RewriteLocalizationIdField(text, mintedId);
				locStem = mintedId;
				engineId = mintedId;
			}

			try
			{
				Directory.CreateDirectory(Path.Combine(destFolder, "icons"));
				File.WriteAllText(destPath, text);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}

			WriteLocalization(model, destFolder, locStem);
			LabAliases.SeedSelf(destFolder, slug, engineId);

			newFolder = destFolder;
			return true;
		}

		private static string RewriteBlockKey(string text, string oldId, string newId)
		{
			string oldKey = "\"" + oldId + "\"";
			int idx = text.IndexOf(oldKey, StringComparison.Ordinal);
			return idx < 0
				? text
				: text.Substring(0, idx) + "\"" + newId + "\"" + text.Substring(idx + oldKey.Length);
		}

		/// <summary>Rewrites an explicit "LocalizationId" field's value to newId, if present.</summary>
		/// <remarks>A vanilla ability with no explicit LocalizationId already defaults to its own block key at parse time, which RewriteBlockKey already retargeted -- nothing else to do there. This is why AbilityIdentityRekey's own "SKILL_" + oldId comparison (built for renaming Lab-authored abilities, where LocalizationId is always exactly the old id) is not reused here: several vanilla abilities set LocalizationId to a bare, unrelated stem (e.g. berserk_npc_trait.txt -&gt; "berserk_trait"), which that comparison would silently miss.</remarks>
		private static string RewriteLocalizationIdField(string text, string newId)
		{
			const string key = "\"LocalizationId\"";
			int idx = text.IndexOf(key, StringComparison.Ordinal);
			if (idx < 0)
			{
				return text;
			}

			int valueStart = text.IndexOf('"', idx + key.Length);
			if (valueStart < 0)
			{
				return text;
			}

			int valueEnd = text.IndexOf('"', valueStart + 1);
			return valueEnd < 0
				? text
				: text.Substring(0, valueStart + 1) + newId + text.Substring(valueEnd);
		}

		/// <summary>Writes localization_en_US.txt with SKILL_&lt;locStem&gt;_* strings for the ability itself, and COMBAT_MODIFIER_&lt;id&gt;_* for every modifier the body references.</summary>
		/// <remarks>
		/// Reads the merged runtime table (LocalizationManager.instance.DatabaseClone), not a
		/// pure-vanilla Resources re-parse -- if another mod or an existing override already
		/// redefines one of these keys, this copies what's currently shown rather than the original
		/// vanilla string. Acceptable for v1: for Override that's arguably correct (start from
		/// what's live); for Fork it only matters if two mods touch the same vanilla ability's loc,
		/// an edge case.
		/// </remarks>
		private static void WriteLocalization(AbilityFileModel model, string destFolder, string locStem)
		{
			if (LocalizationManager.instance == null)
			{
				return;
			}

			Dictionary<string, string> table = LocalizationManager.instance.DatabaseClone;
			if (table == null || table.Count == 0)
			{
				return;
			}

			StringBuilder body = new StringBuilder();
			string nameKey = "SKILL_" + locStem + "_NAME";
			string descKey = "SKILL_" + locStem + "_DESCRIPTION";
			AppendIfPresent(body, table, nameKey);
			AppendIfPresent(body, table, descKey);

			string skillPrefix = "SKILL_" + locStem + "_";
			foreach (KeyValuePair<string, string> pair in table)
			{
				if (pair.Key.StartsWith(skillPrefix, StringComparison.Ordinal)
					&& pair.Key != nameKey && pair.Key != descKey)
				{
					AppendLine(body, pair.Key, pair.Value);
				}
			}

			foreach (string stem in CollectModifierStems(model))
			{
				string modifierPrefix = "COMBAT_MODIFIER_" + stem + "_";
				foreach (KeyValuePair<string, string> pair in table)
				{
					if (pair.Key.StartsWith(modifierPrefix, StringComparison.Ordinal))
					{
						AppendLine(body, pair.Key, pair.Value);
					}
				}
			}

			if (body.Length == 0)
			{
				return;
			}

			File.WriteAllText(Path.Combine(destFolder, "localization_en_US.txt"), body.ToString());
		}

		/// <summary>Modifier id, and its current owning ability id if known, for every modifier this ability's body defines or references that already exists in the engine's global ability_modifiers table.</summary>
		/// <remarks>
		/// Phase 4's "warn on global modifier id collisions" (docs/roadmaps/started/vanilla-ability-edit.md)
		/// -- ability_modifiers is a flat Dictionary&lt;string, Modifier&gt; on AbilitiesDefinitions,
		/// engine-wide, not scoped per ability. For Override this collision is expected and intended
		/// (last-wins is the point). For Fork it's a sharper warning: this importer never rekeys
		/// modifier ids (see CollectModifierStems), so a Fork copy that keeps a vanilla modifier id
		/// still globally replaces that same ability_modifiers entry at load
		/// (AbilitiesDefinitionsPatches.ExecuteLoad) -- any other vanilla ability that ApplyModifiers
		/// that id gets the fork's edited version too, even though the fork's own top-level id is new
		/// and "vanilla is untouched" for everything else.
		/// </remarks>
		internal static List<(string ModifierId, string OwnerAbilityId)> FindModifierCollisions(AbilityFileModel model)
		{
			List<(string, string)> collisions = new List<(string, string)>();
			Dictionary<string, Modifier> known = AbilitiesDefinitions.instance != null
				? AbilitiesDefinitions.instance.ability_modifiers
				: null;
			if (known == null)
			{
				return collisions;
			}

			foreach (string stem in CollectModifierStems(model))
			{
				if (known.TryGetValue(stem, out Modifier modifier))
				{
					string owner = modifier != null && modifier.Ability != null ? modifier.Ability.abilityId : null;
					collisions.Add((stem, owner));
				}
			}

			return collisions;
		}

		/// <summary>Every modifier id this ability's body defines or references (ApplyModifier/RemoveModifier's ModifierName field), for pulling COMBAT_MODIFIER_&lt;id&gt;_* loc. Modifier ids are never rekeyed by this importer (only the ability's own top-level identity changes for Fork), so this is the same for Override and Fork.</summary>
		private static HashSet<string> CollectModifierStems(AbilityFileModel model)
		{
			HashSet<string> stems = new HashSet<string>(StringComparer.Ordinal);
			if (model.Body == null)
			{
				return stems;
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				if (!string.IsNullOrEmpty(mod.Id))
				{
					stems.Add(mod.Id);
				}
			}

			AbilityUsage.WalkCards(model, (eventName, card) =>
			{
				if (card.Fields != null && card.Fields.TryGetValue("ModifierName", out string modifierName)
					&& !string.IsNullOrEmpty(modifierName))
				{
					stems.Add(modifierName);
				}
			});

			return stems;
		}

		private static void AppendIfPresent(StringBuilder body, Dictionary<string, string> table, string key)
		{
			if (table.TryGetValue(key, out string value))
			{
				AppendLine(body, key, value);
			}
		}

		private static void AppendLine(StringBuilder body, string key, string value)
		{
			body.Append('"').Append(key).Append("\" = \"").Append(EscapeLoc(value)).Append("\"\n");
		}

		private static string EscapeLoc(string value)
		{
			return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}
}
