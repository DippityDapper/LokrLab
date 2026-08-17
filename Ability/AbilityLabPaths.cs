using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LokrAbilityLab
{
	/// <summary>Single place every disk path this plugin touches gets built from.</summary>
	/// <remarks>
	/// Libraries live under Mods/LokrLab/LokrAbilityLab/&lt;libraryId&gt;/. The category
	/// folder name stays LokrAbilityLab so Character Loader scans do not collide with a generic
	/// Abilities folder. Each library folder holds ability subfolders
	/// (ability.txt, icons/, localization_*.txt). Ability ids stay the folder names characters
	/// reference; library folders use a <c>slug_token</c> id and a display name in project.json.
	/// </remarks>
	internal static class AbilityLabPaths
	{
		/// <summary>The KV definition filename inside an ability folder.</summary>
		internal const string DefinitionFileName = "ability.txt";

		/// <summary>ModAPI category name and on-disk folder for ability libraries.</summary>
		internal const string LibrariesCategory = "LokrAbilityLab";

		/// <summary>Suite mod package root (Mods/LokrLab). File browsers use this as ModRoot.</summary>
		internal static string SuiteRoot => Path.Combine(Application.dataPath, "Mods", "LokrLab");

		/// <summary>Alias for SuiteRoot (file-browser start folder).</summary>
		internal static string ModRoot => SuiteRoot;

		/// <summary>Folder holding every ability library this plugin has authored.</summary>
		internal static string LibrariesRoot => Path.Combine(SuiteRoot, LibrariesCategory);

		/// <summary>Pre-suite package category (Mods/LokrAbilityLab/LokrAbilityLab).</summary>
		internal static string LegacyPackageLibrariesRoot =>
			Path.Combine(Application.dataPath, "Mods", "LokrAbilityLab", LibrariesCategory);

		/// <summary>Pre-library singleton root (Mods/LokrAbilityLab/Abilities). Migrated on boot.</summary>
		internal static string LegacyAbilitiesRoot =>
			Path.Combine(Application.dataPath, "Mods", "LokrAbilityLab", "Abilities");

		/// <summary>One library's folder, LibrariesRoot/&lt;libraryId&gt;/.</summary>
		internal static string LibraryFolder(string libraryId) => Path.Combine(LibrariesRoot, libraryId);

		/// <summary>One ability's folder inside a library.</summary>
		internal static string AbilityFolder(string libraryFolder, string abilityId) => Path.Combine(libraryFolder, abilityId);

		/// <summary>The KV definition file inside an ability folder.</summary>
		internal static string AbilityDefinitionPath(string libraryFolder, string abilityId) =>
			Path.Combine(AbilityFolder(libraryFolder, abilityId), DefinitionFileName);

		/// <summary>One ability's icons subfolder.</summary>
		internal static string AbilityIconsFolder(string libraryFolder, string abilityId) =>
			Path.Combine(AbilityFolder(libraryFolder, abilityId), "icons");

		/// <summary>Creates LibrariesRoot if missing.</summary>
		internal static void EnsureFoldersExist()
		{
			Directory.CreateDirectory(LibrariesRoot);
		}

		/// <summary>Mints a <c>slug_token</c> library folder id and retries while that folder already exists.</summary>
		/// <remarks>Leftover numeric library folders stay valid until Rename. Ability folders inside a library use <see cref="GenerateNewAbilityId"/>.</remarks>
		internal static string GenerateNewLibraryId(string slug = null)
		{
			string stem = string.IsNullOrEmpty(slug) ? "library" : slug;
			return LokrLab.LabSlugIds.MintUniqueId(stem, id => Directory.Exists(LibraryFolder(id)));
		}

		/// <summary>Mints a <c>slug_token</c> ability folder id that is unique across every library.</summary>
		internal static string GenerateNewAbilityId(string slug)
		{
			return LokrLab.LabSlugIds.MintUniqueId(slug, AbilityIdExists);
		}

		/// <summary>Every library folder under LibrariesRoot.</summary>
		internal static IEnumerable<string> EnumerateLibraryFolders()
		{
			EnsureFoldersExist();
			if (!Directory.Exists(LibrariesRoot))
			{
				yield break;
			}

			foreach (string folder in Directory.GetDirectories(LibrariesRoot))
			{
				yield return folder;
			}
		}

		/// <summary>Every ability (library folder + id) that has ability.txt.</summary>
		internal static IEnumerable<(string LibraryFolder, string AbilityId)> EnumerateAbilities()
		{
			foreach (string libraryFolder in EnumerateLibraryFolders())
			{
				foreach ((string _, string abilityId) in EnumerateAbilitiesIn(libraryFolder))
				{
					yield return (libraryFolder, abilityId);
				}
			}
		}

		/// <summary>Abilities inside one library folder.</summary>
		internal static IEnumerable<(string AbilityFolder, string AbilityId)> EnumerateAbilitiesIn(string libraryFolder)
		{
			if (string.IsNullOrEmpty(libraryFolder) || !Directory.Exists(libraryFolder))
			{
				yield break;
			}

			foreach (string folder in Directory.GetDirectories(libraryFolder))
			{
				if (File.Exists(Path.Combine(folder, DefinitionFileName)))
				{
					yield return (folder, Path.GetFileName(folder));
				}
			}
		}

		/// <summary>Library folder that contains the given ability id, or null.</summary>
		internal static string FindLibraryFolderForAbility(string abilityId)
		{
			if (string.IsNullOrEmpty(abilityId))
			{
				return null;
			}

			foreach ((string libraryFolder, string id) in EnumerateAbilities())
			{
				if (string.Equals(id, abilityId, System.StringComparison.OrdinalIgnoreCase))
				{
					return libraryFolder;
				}
			}

			return null;
		}

		/// <summary>First library folder, or null if none exist yet.</summary>
		internal static string FirstLibraryFolder()
		{
			foreach (string folder in EnumerateLibraryFolders())
			{
				return folder;
			}

			return null;
		}

		/// <summary>True if any library already has this ability id.</summary>
		internal static bool AbilityIdExists(string abilityId)
		{
			return !string.IsNullOrEmpty(FindLibraryFolderForAbility(abilityId));
		}

		/// <summary>Moves leftover package libraries, then wraps the old singleton Abilities/ tree.</summary>
		internal static void MigrateLegacySingleton()
		{
			MergeLibraryFolders(LegacyPackageLibrariesRoot);
			MigrateLegacyAbilitiesFolder();
		}

		/// <summary>Rewrites a stored folder path that still points at a pre-suite library root.</summary>
		internal static string RewriteMigratedFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return folder;
			}

			string full = Path.GetFullPath(folder);
			string oldFull = Path.GetFullPath(LegacyPackageLibrariesRoot);
			if (!full.StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
			{
				return folder;
			}

			string rest = full.Substring(oldFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return string.IsNullOrEmpty(rest) ? LibrariesRoot : Path.Combine(LibrariesRoot, rest);
		}

		private static void MergeLibraryFolders(string sourceRoot)
		{
			if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot)
				|| string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(LibrariesRoot), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			try
			{
				EnsureFoldersExist();
				foreach (string child in Directory.GetDirectories(sourceRoot))
				{
					string dest = Path.Combine(LibrariesRoot, Path.GetFileName(child));
					if (Directory.Exists(dest))
					{
						LokrAbilityLabPlugin.Log.LogWarning(
							"Skipped migrating '" + child + "' — '" + dest + "' already exists.");
						continue;
					}

					Directory.Move(child, dest);
				}

				if (Directory.GetFileSystemEntries(sourceRoot).Length == 0)
				{
					Directory.Delete(sourceRoot);
				}

				LokrAbilityLabPlugin.Log.LogInfo("Migrated " + sourceRoot + " into " + LibrariesRoot + ".");
			}
			catch (IOException ex)
			{
				LokrAbilityLabPlugin.Log.LogWarning("Could not migrate " + sourceRoot + ": " + ex.Message);
			}
		}

		/// <summary>Wraps the old singleton Abilities/ folder into one generated library.</summary>
		private static void MigrateLegacyAbilitiesFolder()
		{
			if (!Directory.Exists(LegacyAbilitiesRoot))
			{
				return;
			}

			List<string> abilityFolders = new List<string>();
			foreach (string folder in Directory.GetDirectories(LegacyAbilitiesRoot))
			{
				if (File.Exists(Path.Combine(folder, DefinitionFileName)))
				{
					abilityFolders.Add(folder);
				}
			}

			string marker = Path.Combine(LegacyAbilitiesRoot, "project.json");
			if (abilityFolders.Count == 0 && !File.Exists(marker))
			{
				return;
			}

			EnsureFoldersExist();
			string libraryId = GenerateNewLibraryId();
			string dest = LibraryFolder(libraryId);
			Directory.CreateDirectory(dest);
			File.WriteAllText(Path.Combine(dest, "project.json"),
				"{\"projectType\":\"ability-library\",\"schemaVersion\":1,\"displayName\":\"Ability Library\"}");

			foreach (string source in abilityFolders)
			{
				string name = Path.GetFileName(source);
				string target = Path.Combine(dest, name);
				if (Directory.Exists(target))
				{
					continue;
				}

				Directory.Move(source, target);
			}

			if (File.Exists(marker))
			{
				File.Delete(marker);
			}

			try
			{
				if (Directory.GetDirectories(LegacyAbilitiesRoot).Length == 0
					&& Directory.GetFiles(LegacyAbilitiesRoot).Length == 0)
				{
					Directory.Delete(LegacyAbilitiesRoot);
				}
			}
			catch (IOException)
			{
			}

			LokrAbilityLabPlugin.Log.LogInfo("Migrated legacy Abilities/ folder into library " + libraryId + ".");
		}
	}
}
