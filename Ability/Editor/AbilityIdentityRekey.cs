using System;
using System.IO;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrModAPI;

namespace LokrAbilityLab.Editor
{
	/// <summary>Rewrites a leftover non-<c>slug_token</c> ability folder onto a minted id.</summary>
	/// <remarks>
	/// <see cref="TryApplyToSlugToken"/> is the Ability inspector Rename button.
	/// <see cref="RewriteOnDisk"/> is the legacy-import path that retargets leftover pack text
	/// already written into a minted folder.
	/// </remarks>
	internal static class AbilityIdentityRekey
	{
		/// <summary>Renames a leftover ability folder, block key, loc stems, and character skill refs.</summary>
		internal static bool TryApplyToSlugToken(
			string libraryFolder,
			string oldId,
			string slug,
			string alias,
			out string newId,
			out string error)
		{
			newId = null;
			error = null;
			if (string.IsNullOrEmpty(libraryFolder) || !Directory.Exists(libraryFolder))
			{
				error = "Library folder does not exist.";
				return false;
			}

			if (string.IsNullOrEmpty(oldId) || LabSlugIds.LooksLikeSlugTokenId(oldId))
			{
				error = "This ability already uses a slug_token id.";
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

			string oldFolder = AbilityLabPaths.AbilityFolder(libraryFolder, oldId);
			if (!Directory.Exists(oldFolder))
			{
				error = "Ability folder does not exist.";
				return false;
			}

			try
			{
				newId = AbilityLabPaths.GenerateNewAbilityId(slug);
				string newFolder = AbilityLabPaths.AbilityFolder(libraryFolder, newId);
				if (!RewriteAbilityFile(AbilityLabPaths.AbilityDefinitionPath(libraryFolder, oldId), oldId, newId, out error))
				{
					return false;
				}

				RewriteLocalizationStems(oldFolder, oldId, newId);
				LabAliases.SeedSelf(oldFolder, alias, newId);
				RewriteCharacterSkillRefs(oldId, newId);
				Directory.Move(oldFolder, newFolder);
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		/// <summary>Rewrites ability.txt's block key and SKILL_ loc stems in a folder that already uses the new id as its name.</summary>
		/// <remarks>
		/// Legacy import writes leftover pack text into a minted folder, then this retargets the
		/// files without a Directory.Move. Text rewrite only — AbilityFileModel save can drop
		/// nested pack modifiers.
		/// </remarks>
		internal static bool RewriteOnDisk(string abilityFolder, string oldId, string newId, out string error)
		{
			error = null;
			if (string.IsNullOrEmpty(abilityFolder) || !Directory.Exists(abilityFolder))
			{
				error = "Ability folder does not exist.";
				return false;
			}

			string path = Path.Combine(abilityFolder, AbilityLabPaths.DefinitionFileName);
			if (!File.Exists(path))
			{
				error = "ability.txt is missing.";
				return false;
			}

			if (!RewriteAbilityFileText(path, oldId, newId, out error))
			{
				return false;
			}

			RewriteLocalizationStems(abilityFolder, oldId, newId);
			return true;
		}

		/// <summary>Updates the KV block key and LocalizationId, falling back to a text rewrite when save validation fails.</summary>
		private static bool RewriteAbilityFile(string path, string oldId, string newId, out string error)
		{
			error = null;
			if (!File.Exists(path))
			{
				error = "ability.txt is missing.";
				return false;
			}

			if (AbilityKvIO.TryLoad(path, out AbilityFileModel model, out _))
			{
				model.Id = newId;
				if (string.Equals(model.LocalizationId, "SKILL_" + oldId, StringComparison.OrdinalIgnoreCase))
				{
					model.LocalizationId = "SKILL_" + newId;
				}

				if (AbilityKvIO.TrySave(model, path, out error))
				{
					return true;
				}
			}

			return RewriteAbilityFileText(path, oldId, newId, out error);
		}

		/// <summary>Rewrites the first quoted leftover key and SKILL_ stems when the typed save path cannot run.</summary>
		private static bool RewriteAbilityFileText(string path, string oldId, string newId, out string error)
		{
			error = null;
			string text = File.ReadAllText(path);
			string oldKey = "\"" + oldId + "\"";
			int idx = text.IndexOf(oldKey, StringComparison.Ordinal);
			if (idx >= 0)
			{
				text = text.Substring(0, idx) + "\"" + newId + "\"" + text.Substring(idx + oldKey.Length);
			}

			text = text.Replace("\"SKILL_" + oldId, "\"SKILL_" + newId);
			File.WriteAllText(path, text);
			return true;
		}

		/// <summary>Renames SKILL_&lt;oldId&gt;_* localization keys to SKILL_&lt;newId&gt;_*.</summary>
		private static void RewriteLocalizationStems(string folder, string oldId, string newId)
		{
			string oldPrefix = "\"SKILL_" + oldId + "_";
			string newPrefix = "\"SKILL_" + newId + "_";
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

		/// <summary>Rewrites defaultSkill / skills / skillProgression ids on every Lab character folder.</summary>
		private static void RewriteCharacterSkillRefs(string oldId, string newId)
		{
			foreach (string category in new[] { CharacterLabPaths.CharactersCategory, "Characters" })
			{
				if (ModAPI.Files == null)
				{
					continue;
				}

				foreach ((string _, string folder) in ModAPI.Files.EnumerateCategorySubfolders(category))
				{
					CharacterProfile profile = CharacterProfileSidecar.Load(folder);
					if (!RewriteSkillIds(profile, oldId, newId))
					{
						continue;
					}

					CharacterProfileSidecar.Save(folder, profile);
					RLHeroesGenerator.Sync(folder, profile);
					if (string.Equals(CharacterSession.Folder, folder, StringComparison.OrdinalIgnoreCase)
						&& CharacterSession.Profile != null)
					{
						RewriteSkillIds(CharacterSession.Profile, oldId, newId);
					}
				}
			}
		}

		/// <summary>Replaces exact oldId matches on the profile's skill fields. Returns true when anything changed.</summary>
		private static bool RewriteSkillIds(CharacterProfile profile, string oldId, string newId)
		{
			if (profile == null)
			{
				return false;
			}

			bool changed = false;
			if (string.Equals(profile.DefaultSkill, oldId, StringComparison.OrdinalIgnoreCase))
			{
				profile.DefaultSkill = newId;
				changed = true;
			}

			if (profile.Skills != null)
			{
				for (int i = 0; i < profile.Skills.Count; i++)
				{
					if (string.Equals(profile.Skills[i], oldId, StringComparison.OrdinalIgnoreCase))
					{
						profile.Skills[i] = newId;
						changed = true;
					}
				}
			}

			if (profile.SkillProgression != null)
			{
				foreach (LevelSkillEntry entry in profile.SkillProgression)
				{
					if (entry == null || entry.SkillIds == null)
					{
						continue;
					}

					for (int i = 0; i < entry.SkillIds.Count; i++)
					{
						if (string.Equals(entry.SkillIds[i], oldId, StringComparison.OrdinalIgnoreCase))
						{
							entry.SkillIds[i] = newId;
							changed = true;
						}
					}
				}
			}

			return changed;
		}
	}
}
