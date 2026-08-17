using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LokrCharacterLab;
using LokrCharacterLoader;
using LokrLab;
using LokrModAPI;
using UnityEngine;

namespace LokrLab.Editor.General
{
	/// <summary>Rewrites a leftover named-folder or numeric-id character onto a <c>slug_token</c> id.</summary>
	/// <remarks>Onagro (and any other pre-random-id import) kept the old UniqueId as the folder name. Folder name is UniqueId, MetaExo, roster id, UNIT_* stem, and the portraits/&lt;id&gt;_SLOT.png prefix -- leaving it as a display name breaks the "id is opaque, Name is what the player sees" split CharacterProfile.Id documents. EnemySummon props follow the same rule: the folder becomes a generated id, the KV block key / SpawnUnit #word uses the expression-safe form (leading c), and Ability Lab UnitName lines are rewritten. Named leftover ImportedFromLegacyMod folders still rekey on Load. Leftover 18-digit folders stay until the Character inspector Rename button, so a load does not silently move them.</remarks>
	internal static class CharacterIdentityRekey
	{
		/// <summary>If this folder is a leftover named-id legacy import, rekeys it and returns the new folder; otherwise returns the input unchanged.</summary>
		internal static string ApplyIfLegacyNamedFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return folder;
			}
			CharacterProfile profile = CharacterProfileSidecar.Load(folder);
			if (!profile.ImportedFromLegacyMod)
			{
				return folder;
			}
			string oldId = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (LooksLikeGeneratedId(oldId))
			{
				return folder;
			}

			string slug = LabSlugIds.LegalizeSlug(
				!string.IsNullOrEmpty(profile.Name) ? profile.Name : oldId,
				"character");
			return Apply(folder, profile, oldId, slug, slug);
		}

		/// <summary>Renames a leftover non-<c>slug_token</c> folder onto a minted id. Does not run on load.</summary>
		internal static bool TryApplyToSlugToken(string folder, string slug, string alias, out string newFolder, out string error)
		{
			newFolder = folder;
			error = null;
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				error = "Folder does not exist.";
				return false;
			}

			string oldId = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (LabSlugIds.LooksLikeSlugTokenId(oldId))
			{
				error = "This character already uses a slug_token id.";
				return false;
			}

			if (!LabSlugIds.IsLegalSlug(slug))
			{
				error = "Slug must start with a letter and use only lowercase letters, digits, and underscores.";
				return false;
			}

			if (string.IsNullOrEmpty(alias))
			{
				alias = slug;
			}

			if (!LabSlugIds.IsLegalSlug(alias))
			{
				error = "Alias must start with a letter and use only lowercase letters, digits, and underscores.";
				return false;
			}

			CharacterProfile profile = CharacterProfileSidecar.Load(folder);
			try
			{
				newFolder = Apply(folder, profile, oldId, slug, alias);
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		/// <summary>True when the folder name is an 18-digit Lab id or a <c>slug_token</c> id.</summary>
		private static bool LooksLikeGeneratedId(string id)
		{
			return LabSlugIds.LooksLikeGeneratedId(id);
		}

		/// <summary>Rewrites identity fields, UNIT_* keys, &lt;oldId&gt;_-prefixed portraits/sounds, and Ability Lab SpawnUnit UnitName references, then moves the folder to CharactersRoot/&lt;newId&gt;/.</summary>
		private static string Apply(string folder, CharacterProfile profile, string oldId, string slug, string alias)
		{
			if (string.IsNullOrEmpty(alias))
			{
				alias = slug;
			}

			string newId = CharacterLabPaths.GenerateNewCharacterId(slug);
			string newFolder = Path.Combine(CharacterLabPaths.CharactersRoot, newId);
			RewriteLocalizationStems(folder, oldId, newId);
			RenamePrefixedFiles(Path.Combine(folder, "portraits"), oldId, newId);
			RenamePrefixedFiles(Path.Combine(folder, "sounds"), oldId, newId);
			profile.Id = newId;
			CharacterProfileSidecar.Save(folder, profile);
			RetargetAliases(folder, oldId, newId, alias);
			RLHeroesGenerator.Sync(folder, profile);
			RewriteAbilityUnitNames(oldId, newId, alias, folder);
			Directory.Move(folder, newFolder);
			LokrCharacterLabPlugin.Log.LogInfo("CharacterIdentityRekey: rekeyed '" + oldId + "' to '" + newId + "' at " + newFolder);
			return newFolder;
		}

		/// <summary>Rewrites SpawnUnit UnitName values that pointed at oldId so they spawn the new block key. Leaves Icon/Nick and other fields that merely share the old string alone.</summary>
		internal static void RewriteAbilityUnitNames(string oldId, string newId)
		{
			RewriteAbilityUnitNames(oldId, newId, null, null);
		}

		/// <summary>Rewrites SpawnUnit UnitName values that pointed at oldId. Writes <c>$alias</c> when an alias is supplied.</summary>
		internal static void RewriteAbilityUnitNames(string oldId, string newId, string alias, string characterFolder)
		{
			string aliasAlts = AliasAlternatives(characterFolder, oldId, newId);
			string pattern = "(\"UnitName\"\\s+\")(?:"
				+ "(?:#?c?)" + Regex.Escape(oldId)
				+ aliasAlts
				+ ")\"";
			Regex unitName = new Regex(pattern);
			string authored = !string.IsNullOrEmpty(alias)
				? "$" + alias
				: "#" + CharacterLabPaths.ToExpressionSafeBlockKey(newId);
			string replacement = "$1" + authored + "\"";
			foreach ((string _, string library) in ModAPI.Files.EnumerateCategorySubfolders("LokrAbilityLab"))
			{
				RewriteAbilityUnitNamesInLibrary(library, unitName, replacement, oldId, newId, alias, characterFolder);
			}

			string legacy = Path.Combine(Application.dataPath, "Mods", "LokrAbilityLab", "Abilities");
			RewriteAbilityUnitNamesInLibrary(legacy, unitName, replacement, oldId, newId, alias, characterFolder);
		}

		/// <summary>Builds <c>|$oldAlias|$other</c> alternatives for UnitName values that already used $alias.</summary>
		private static string AliasAlternatives(string characterFolder, string oldId, string newId)
		{
			if (string.IsNullOrEmpty(characterFolder))
			{
				return string.Empty;
			}

			StringBuilder alts = new StringBuilder();
			Dictionary<string, string> map = LabAliases.Load(characterFolder);
			foreach (KeyValuePair<string, string> pair in map)
			{
				if (string.Equals(pair.Value, oldId, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(pair.Value, newId, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(pair.Value, "c" + oldId, StringComparison.OrdinalIgnoreCase))
				{
					alts.Append('|').Append(Regex.Escape("$" + pair.Key));
				}
			}

			return alts.ToString();
		}

		private static void RewriteAbilityUnitNamesInLibrary(
			string libraryFolder,
			Regex unitName,
			string replacement,
			string oldId,
			string newId,
			string alias,
			string characterFolder)
		{
			if (!Directory.Exists(libraryFolder))
			{
				return;
			}

			foreach (string abilityFolder in Directory.GetDirectories(libraryFolder))
			{
				string path = Path.Combine(abilityFolder, "ability.txt");
				if (!File.Exists(path))
				{
					continue;
				}

				string text = File.ReadAllText(path);
				string updated = unitName.Replace(text, replacement);
				if (updated != text)
				{
					File.WriteAllText(path, updated);
					if (!string.IsNullOrEmpty(characterFolder) && !string.IsNullOrEmpty(alias))
					{
						LabAliases.CopyAlias(characterFolder, abilityFolder, newId, alias);
					}
				}

				RetargetAliases(abilityFolder, oldId, newId, updated != text ? alias : null);
			}
		}

		/// <summary>Updates alias values that pointed at oldId and seeds the chosen self-alias.</summary>
		private static void RetargetAliases(string folder, string oldId, string newId, string alias)
		{
			Dictionary<string, string> map = LabAliases.Load(folder);
			List<string> keys = new List<string>(map.Keys);
			bool changed = false;
			for (int i = 0; i < keys.Count; i++)
			{
				string key = keys[i];
				if (string.Equals(map[key], oldId, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(map[key], "c" + oldId, StringComparison.OrdinalIgnoreCase))
				{
					map[key] = newId;
					changed = true;
				}
			}

			if (!string.IsNullOrEmpty(alias))
			{
				map[alias] = newId;
				changed = true;
			}

			if (changed)
			{
				LabAliases.Save(folder, map);
			}
		}

		/// <summary>Renames UNIT_&lt;oldId&gt;_* localization keys to UNIT_&lt;newId&gt;_* in every localization_*.txt, leaving SKILL_*/COMBAT_MODIFIER_* lines untouched.</summary>
		private static void RewriteLocalizationStems(string folder, string oldId, string newId)
		{
			string oldPrefix = "\"UNIT_" + oldId + "_";
			string newPrefix = "\"UNIT_" + newId + "_";
			foreach (string path in Directory.GetFiles(folder, "localization_*.txt"))
			{
				string text = File.ReadAllText(path);
				if (text.IndexOf(oldPrefix, StringComparison.Ordinal) < 0)
				{
					continue;
				}
				File.WriteAllText(path, text.Replace(oldPrefix, newPrefix));
			}
		}

		/// <summary>Renames files whose name starts with &lt;oldId&gt;_ so PortraitPatches/SoundPatches resolve them under the new id.</summary>
		private static void RenamePrefixedFiles(string directory, string oldId, string newId)
		{
			if (!Directory.Exists(directory))
			{
				return;
			}
			string prefix = oldId + "_";
			foreach (string path in Directory.GetFiles(directory))
			{
				string name = Path.GetFileName(path);
				if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					string renamed = newId + name.Substring(oldId.Length);
					File.Move(path, Path.Combine(directory, renamed));
				}
			}
		}
	}
}
