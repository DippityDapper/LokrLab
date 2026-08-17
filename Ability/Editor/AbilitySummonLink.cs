using System.IO;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Editor;
using LokrModAPI;

namespace LokrAbilityLab.Editor
{
	/// <summary>When a SpawnUnit UnitName is set, copies that unit's alias into this ability folder.</summary>
	internal static class AbilitySummonLink
	{
		/// <summary>Stores <c>$alias</c> when the target has a mapping; otherwise leaves the typed value.</summary>
		internal static string CommitUnitName(AbilityFileModel model, string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return raw;
			}

			string trimmed = raw.Trim();
			if (trimmed.Length > 0 && trimmed[0] == '$')
			{
				return trimmed;
			}

			string uniqueId = AbilityCatalogLookups.StripHash(trimmed);
			string abilityFolder = AbilityFolderOf(model);
			string characterFolder = FindCharacterFolder(uniqueId);
			string fallback = LabSlugIds.SlugFromId(uniqueId, "unit");
			string key = LabAliases.CopyAlias(characterFolder, abilityFolder, uniqueId, fallback);
			if (string.IsNullOrEmpty(key))
			{
				return trimmed;
			}

			return "$" + key;
		}

		private static string AbilityFolderOf(AbilityFileModel model)
		{
			if (model == null || string.IsNullOrEmpty(model.SourceFilePath))
			{
				return null;
			}

			return Path.GetDirectoryName(model.SourceFilePath);
		}

		private static string FindCharacterFolder(string uniqueId)
		{
			if (string.IsNullOrEmpty(uniqueId))
			{
				return null;
			}

			string direct = Path.Combine(CharacterLabPaths.CharactersRoot, uniqueId);
			if (Directory.Exists(direct))
			{
				return direct;
			}

			foreach ((string _, string folder) in ModAPI.Files.EnumerateCategorySubfolders(CharacterLabPaths.CharactersCategory))
			{
				if (string.Equals(Path.GetFileName(folder), uniqueId, System.StringComparison.OrdinalIgnoreCase))
				{
					return folder;
				}
			}

			return null;
		}
	}
}
