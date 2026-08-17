using System;
using System.IO;
using UnityEngine;
using LokrCharacterLab;
using LokrLab;
using LokrModAPI;

namespace LokrLab.Editor
{
	/// <summary>Single place every disk path this plugin touches gets built from.</summary>
	/// <remarks>
	/// Characters live under Mods/LokrLab/LokrCharacterLab/&lt;characterId&gt;/. The category
	/// folder name stays LokrCharacterLab so Character Loader scans do not collide with a
	/// generic Characters folder. Ability libraries live under Mods/LokrLab/LokrAbilityLab/.
	/// </remarks>
	internal static class CharacterLabPaths
	{
		/// <summary>Suite mod package root (Mods/LokrLab).</summary>
		internal static string SuiteRoot => Path.Combine(Application.dataPath, "Mods", "LokrLab");

		/// <summary>Game Mods folder. Legacy pack import starts here so Official Pack / Musketeer sit next to LokrLab.</summary>
		internal static string GameModsRoot => Path.Combine(Application.dataPath, "Mods");

		/// <summary>Alias for SuiteRoot (Lab-authored content, not the import start folder).</summary>
		internal static string ModRoot => SuiteRoot;

		/// <summary>ModAPI category name and on-disk folder for Lab-authored characters.</summary>
		internal const string CharactersCategory = "LokrCharacterLab";

		/// <summary>Folder holding every character this plugin has created, one subfolder per character id.</summary>
		internal static string CharactersRoot => Path.Combine(SuiteRoot, CharactersCategory);

		/// <summary>Pre-suite package category (Mods/LokrCharacterLab/LokrCharacterLab).</summary>
		internal static string LegacyPackageCategoryRoot =>
			Path.Combine(Application.dataPath, "Mods", "LokrCharacterLab", CharactersCategory);

		/// <summary>Pre-rename character root inside the old package (Mods/LokrCharacterLab/Characters).</summary>
		internal static string LegacyPackageCharactersRoot =>
			Path.Combine(Application.dataPath, "Mods", "LokrCharacterLab", "Characters");

		/// <summary>Characters written under Mods/LokrLab/Characters before the category name settled.</summary>
		internal static string LegacyShellCharactersRoot => Path.Combine(SuiteRoot, "Characters");

		/// <summary>Editor-only data that is not part of any single character (recent.json).</summary>
		internal static string EditorDataRoot => Path.Combine(SuiteRoot, "EditorData");

		/// <summary>Pre-suite Character Lab editor data.</summary>
		internal static string LegacyPackageEditorDataRoot =>
			Path.Combine(Application.dataPath, "Mods", "LokrCharacterLab", "EditorData");

		/// <summary>Ability Lab libraries root (Mods/LokrLab/LokrAbilityLab).</summary>
		internal static string AbilityLabLibrariesRoot => Path.Combine(SuiteRoot, "LokrAbilityLab");

		/// <summary>Legacy Ability Lab singleton root, still scanned when rewriting SpawnUnit refs.</summary>
		internal static string AbilityLabAbilitiesRoot => AbilityLabLibrariesRoot;

		/// <summary>A character's own sounds subfolder.</summary>
		internal static string CharacterSoundsFolder(string characterId) => Path.Combine(CharactersRoot, characterId, "sounds");

		/// <summary>A character's own portraits subfolder.</summary>
		internal static string CharacterPortraitsFolder(string characterId) => Path.Combine(CharactersRoot, characterId, "portraits");

		/// <summary>Creates CharactersRoot/EditorDataRoot if missing.</summary>
		internal static void EnsureFoldersExist()
		{
			Directory.CreateDirectory(CharactersRoot);
			Directory.CreateDirectory(EditorDataRoot);
		}

		/// <summary>Moves leftover package and Characters/ trees into Mods/LokrLab/LokrCharacterLab.</summary>
		internal static void MigrateLegacyCharactersRoot()
		{
			MergeCharacterFolders(LegacyPackageCategoryRoot);
			MergeCharacterFolders(LegacyPackageCharactersRoot);
			MergeCharacterFolders(LegacyShellCharactersRoot);
			MergeEditorData(LegacyPackageEditorDataRoot);
		}

		/// <summary>Rewrites a stored folder path that still points at a pre-suite character root.</summary>
		internal static string RewriteMigratedFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return folder;
			}

			string rewritten = RewritePrefix(folder, LegacyPackageCategoryRoot, CharactersRoot);
			rewritten = RewritePrefix(rewritten, LegacyPackageCharactersRoot, CharactersRoot);
			rewritten = RewritePrefix(rewritten, LegacyShellCharactersRoot, CharactersRoot);
			return rewritten;
		}

		private static string RewritePrefix(string folder, string oldRoot, string newRoot)
		{
			if (string.IsNullOrEmpty(oldRoot) || string.IsNullOrEmpty(newRoot))
			{
				return folder;
			}

			string full = Path.GetFullPath(folder);
			string oldFull = Path.GetFullPath(oldRoot);
			if (!full.StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
			{
				return folder;
			}

			string rest = full.Substring(oldFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return string.IsNullOrEmpty(rest) ? newRoot : Path.Combine(newRoot, rest);
		}

		/// <summary>Moves each child of source into CharactersRoot, then removes the empty source folder.</summary>
		private static void MergeCharacterFolders(string sourceRoot)
		{
			if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot)
				|| string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(CharactersRoot), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			try
			{
				Directory.CreateDirectory(CharactersRoot);
				foreach (string child in Directory.GetDirectories(sourceRoot))
				{
					string dest = Path.Combine(CharactersRoot, Path.GetFileName(child));
					if (Directory.Exists(dest))
					{
						LokrCharacterLabPlugin.Log.LogWarning(
							"Skipped migrating '" + child + "' — '" + dest + "' already exists.");
						continue;
					}

					Directory.Move(child, dest);
				}

				if (Directory.GetFileSystemEntries(sourceRoot).Length == 0)
				{
					Directory.Delete(sourceRoot);
				}

				LokrCharacterLabPlugin.Log.LogInfo("Migrated " + sourceRoot + " into " + CharactersRoot + ".");
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Could not migrate " + sourceRoot + ": " + ex.Message);
			}
		}

		private static void MergeEditorData(string sourceRoot)
		{
			if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot)
				|| string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(EditorDataRoot), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			try
			{
				Directory.CreateDirectory(EditorDataRoot);
				foreach (string file in Directory.GetFiles(sourceRoot))
				{
					string dest = Path.Combine(EditorDataRoot, Path.GetFileName(file));
					if (File.Exists(dest))
					{
						continue;
					}

					File.Move(file, dest);
				}

				if (Directory.GetFileSystemEntries(sourceRoot).Length == 0)
				{
					Directory.Delete(sourceRoot);
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Could not migrate editor data " + sourceRoot + ": " + ex.Message);
			}
		}

		/// <summary>Library folder that contains abilityId/ability.txt, or null.</summary>
		internal static string FindAbilityLibraryFolder(string abilityId)
		{
			if (string.IsNullOrEmpty(abilityId))
			{
				return null;
			}

			if (Directory.Exists(AbilityLabLibrariesRoot))
			{
				foreach (string library in Directory.GetDirectories(AbilityLabLibrariesRoot))
				{
					if (File.Exists(Path.Combine(library, abilityId, "ability.txt")))
					{
						return library;
					}
				}
			}

			foreach ((string _, string library) in ModAPI.Files.EnumerateCategorySubfolders("LokrAbilityLab"))
			{
				if (File.Exists(Path.Combine(library, abilityId, "ability.txt")))
				{
					return library;
				}
			}

			return null;
		}

		/// <summary>First Ability Lab library, creating Mods/LokrLab/LokrAbilityLab/imported if none exist.</summary>
		internal static string ResolveAbilityImportLibrary()
		{
			if (Directory.Exists(AbilityLabLibrariesRoot))
			{
				string[] existing = Directory.GetDirectories(AbilityLabLibrariesRoot);
				if (existing.Length > 0)
				{
					return existing[0];
				}
			}

			string imported = Path.Combine(AbilityLabLibrariesRoot, "imported");
			Directory.CreateDirectory(imported);
			string marker = Path.Combine(imported, "project.json");
			if (!File.Exists(marker))
			{
				File.WriteAllText(marker,
					"{\"projectType\":\"ability-library\",\"schemaVersion\":1,\"displayName\":\"Imported\"}");
			}

			return imported;
		}

		/// <summary>Mints a <c>slug_token</c> folder id and retries while that folder already exists.</summary>
		/// <remarks>Existing 18-digit folders stay valid. New creates use the editable slug plus a 6-character Crockford token. SpawnUnit #UnitName for leftover numeric ids still uses <see cref="ToExpressionSafeBlockKey"/>.</remarks>
		internal static string GenerateNewCharacterId(string slug = null)
		{
			string stem = string.IsNullOrEmpty(slug) ? "character" : slug;
			return LabSlugIds.MintUniqueId(stem, id => Directory.Exists(Path.Combine(CharactersRoot, id)));
		}

		/// <summary>SpawnUnit #word for a folder id: prefixes <c>c</c> when the id starts with a digit.</summary>
		/// <remarks>ExpressionsParser word tokens must start with a letter. The KV block key and UniqueId stay the folder id so sandbox/roster ContainsKey(folderId) still hits. See docs/issues/resolved/sandbox-summon-missing-unit-view.md.</remarks>
		internal static string ToExpressionSafeBlockKey(string id)
		{
			if (string.IsNullOrEmpty(id) || !char.IsDigit(id[0]))
			{
				return id;
			}
			return "c" + id;
		}
	}
}
