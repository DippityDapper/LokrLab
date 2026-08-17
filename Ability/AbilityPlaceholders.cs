using System;
using System.IO;
using LokrCharacterLoader;
using LokrLab;

namespace LokrAbilityLab
{
	/// <summary>Installs this plugin's Placeholders/ ability templates into Mods and uses them for new abilities.</summary>
	/// <remarks>
	/// Templates live in BepInEx/plugins/LokrLab/Placeholders/. First install writes
	/// Mods/LokrLab/LokrAbilityLab/placeholders/. After a library-folder Rename,
	/// project.json placeholdersLibrary keeps that folder as the install target so
	/// new-character skill ids still resolve. new-ability.txt is the KV body written
	/// for a newly created ability id. Existing Mods files are never overwritten,
	/// including abilities already rekeyed onto slug_token.
	/// </remarks>
	internal static class AbilityPlaceholders
	{
		/// <summary>Token in new-ability.txt replaced with the new ability id.</summary>
		internal const string IdToken = "__ABILITY_ID__";

		/// <summary>First-install Mods library folder name, and the leftover id Rename ports away from.</summary>
		internal const string LibraryFolderId = "placeholders";

		/// <summary>project.json flag that marks the shipped placeholders library after a folder rename.</summary>
		internal const string ProjectRoleKey = "placeholdersLibrary";

		private static readonly string[] ShippedAbilityFolderIds =
		{
			"placeholder_attack",
			"placeholder_skill",
			"placeholder_passive",
			"placeholder_passive_2",
			"placeholder_passive_3"
		};

		/// <summary>This plugin's Placeholders/ folder next to LokrLab.dll.</summary>
		internal static string PluginPlaceholdersFolder =>
			Path.Combine(Path.GetDirectoryName(typeof(LokrAbilityLabPlugin).Assembly.Location) ?? string.Empty, "Placeholders");

		/// <summary>Default first-install path before the library folder is renamed.</summary>
		internal static string DefaultLibraryFolder =>
			Path.Combine(AbilityLabPaths.LibrariesRoot, LibraryFolderId);

		/// <summary>Live placeholders library folder, including after a slug_token Rename.</summary>
		internal static string InstalledLibraryFolder => FindInstalledLibraryFolder();

		/// <summary>Copies missing placeholder abilities from the plugin folder into Mods. Does not overwrite existing ability.txt.</summary>
		internal static void InstallIntoMods()
		{
			string sourceRoot = PluginPlaceholdersFolder;
			if (!Directory.Exists(sourceRoot))
			{
				LokrAbilityLabPlugin.Log.LogWarning("Ability Placeholders folder missing: " + sourceRoot);
				return;
			}

			string destRoot = FindInstalledLibraryFolder();
			Directory.CreateDirectory(destRoot);

			string sourceMarker = Path.Combine(sourceRoot, "project.json");
			string destMarker = Path.Combine(destRoot, "project.json");
			if (File.Exists(sourceMarker) && !File.Exists(destMarker))
			{
				File.Copy(sourceMarker, destMarker);
			}

			MarkAsPlaceholdersLibrary(destRoot);

			foreach (string sourceAbility in Directory.GetDirectories(sourceRoot))
			{
				string definition = Path.Combine(sourceAbility, AbilityLabPaths.DefinitionFileName);
				if (!File.Exists(definition))
				{
					continue;
				}

				string id = Path.GetFileName(sourceAbility);
				if (HasAbilityForOriginalId(destRoot, id))
				{
					continue;
				}

				CopyAbilityFolderIfMissing(sourceAbility, Path.Combine(destRoot, id));
			}
		}

		/// <summary>Writes a new ability folder from the selected create-sheet template (melee fallback is new-ability.txt).</summary>
		internal static bool TryWriteNewAbility(string libraryFolder, string id, out string error)
		{
			return Editor.AbilityTemplates.TryWrite(libraryFolder, id, Editor.AbilityTemplates.SelectedId, out error);
		}

		/// <summary>Stamps Placeholders/new-ability.txt when a coded template cannot be saved.</summary>
		internal static bool TryWriteNewAbilityFromFile(string libraryFolder, string id, out string error)
		{
			string template = Path.Combine(PluginPlaceholdersFolder, "new-ability.txt");
			if (!File.Exists(template))
			{
				error = "Ability placeholder template missing: " + template;
				return false;
			}

			string destFolder = AbilityLabPaths.AbilityFolder(libraryFolder, id);
			Directory.CreateDirectory(AbilityLabPaths.AbilityIconsFolder(libraryFolder, id));
			string body = File.ReadAllText(template).Replace(IdToken, id);
			File.WriteAllText(AbilityLabPaths.AbilityDefinitionPath(libraryFolder, id), body);
			File.WriteAllText(Path.Combine(destFolder, "localization_en_US.txt"),
				"\"SKILL_" + id + "_NAME\" = \"" + id + "\"\n" +
				"\"SKILL_" + id + "_DESCRIPTION\" = \"A new ability.\"\n");
			error = null;
			return true;
		}

		/// <summary>True when this folder is the shipped placeholders library (legacy name or role marker).</summary>
		internal static bool IsPlaceholdersLibrary(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return false;
			}

			string id = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.Equals(id, LibraryFolderId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return HasPlaceholdersRoleMarker(folder);
		}

		/// <summary>Writes project.json placeholdersLibrary so install and new characters still find this folder after Rename.</summary>
		internal static void MarkAsPlaceholdersLibrary(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return;
			}

			Directory.CreateDirectory(folder);
			string display = Projects.AbilityLibrarySession.ReadDisplayName(folder) ?? "Placeholders";
			string escaped = display.Replace("\\", "\\\\").Replace("\"", "\\\"");
			File.WriteAllText(Path.Combine(folder, "project.json"),
				"{\"projectType\":\"ability-library\",\"schemaVersion\":1,\"displayName\":\"" + escaped +
				"\",\"" + ProjectRoleKey + "\":true}");
		}

		/// <summary>Current on-disk ability id for a shipped placeholder stem, or the stem when that folder is still present.</summary>
		internal static string ResolveAbilityId(string originalId)
		{
			if (string.IsNullOrEmpty(originalId))
			{
				return originalId;
			}

			string library = FindInstalledLibraryFolder();
			if (string.IsNullOrEmpty(library) || !Directory.Exists(library))
			{
				return originalId;
			}

			return ResolveAbilityIdIn(library, originalId);
		}

		/// <summary>Live placeholders library folder: leftover name, role marker, or a library that already holds the shipped set.</summary>
		private static string FindInstalledLibraryFolder()
		{
			string defaultPath = DefaultLibraryFolder;
			if (Directory.Exists(defaultPath))
			{
				return defaultPath;
			}

			string inferred = null;
			foreach (string folder in AbilityLabPaths.EnumerateLibraryFolders())
			{
				if (HasPlaceholdersRoleMarker(folder))
				{
					return folder;
				}

				if (inferred == null && CountShippedPlaceholderMatches(folder) >= 2)
				{
					inferred = folder;
				}
			}

			return inferred ?? defaultPath;
		}

		private static string ResolveAbilityIdIn(string libraryFolder, string originalId)
		{
			if (File.Exists(AbilityLabPaths.AbilityDefinitionPath(libraryFolder, originalId)))
			{
				return originalId;
			}

			foreach ((string abilityFolder, string abilityId) in AbilityLabPaths.EnumerateAbilitiesIn(libraryFolder))
			{
				if (LabAliases.Load(abilityFolder).TryGetValue(originalId, out string mapped)
					&& !string.IsNullOrEmpty(mapped)
					&& File.Exists(AbilityLabPaths.AbilityDefinitionPath(libraryFolder, mapped)))
				{
					return mapped;
				}

				if (LabSlugIds.LooksLikeSlugTokenId(abilityId)
					&& string.Equals(LabSlugIds.SlugFromId(abilityId, string.Empty), originalId, StringComparison.OrdinalIgnoreCase))
				{
					return abilityId;
				}
			}

			return originalId;
		}

		private static bool HasAbilityForOriginalId(string libraryFolder, string originalId)
		{
			string resolved = ResolveAbilityIdIn(libraryFolder, originalId);
			return File.Exists(AbilityLabPaths.AbilityDefinitionPath(libraryFolder, resolved));
		}

		private static bool HasPlaceholdersRoleMarker(string folder)
		{
			string path = Path.Combine(folder, "project.json");
			if (!File.Exists(path))
			{
				return false;
			}

			try
			{
				string text = File.ReadAllText(path);
				int key = text.IndexOf("\"" + ProjectRoleKey + "\"", StringComparison.Ordinal);
				if (key < 0)
				{
					return false;
				}

				int colon = text.IndexOf(':', key);
				if (colon < 0)
				{
					return false;
				}

				string rest = text.Substring(colon + 1).TrimStart();
				return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private static int CountShippedPlaceholderMatches(string folder)
		{
			int count = 0;
			for (int i = 0; i < ShippedAbilityFolderIds.Length; i++)
			{
				if (HasAbilityForOriginalId(folder, ShippedAbilityFolderIds[i]))
				{
					count++;
				}
			}

			return count;
		}

		private static void CopyAbilityFolderIfMissing(string sourceFolder, string destFolder)
		{
			string destDefinition = Path.Combine(destFolder, AbilityLabPaths.DefinitionFileName);
			if (File.Exists(destDefinition))
			{
				return;
			}

			Directory.CreateDirectory(destFolder);
			foreach (string sourceFile in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
			{
				string relative = sourceFile.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string destFile = Path.Combine(destFolder, relative);
				if (File.Exists(destFile))
				{
					continue;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(destFile));
				File.Copy(sourceFile, destFile);
			}
		}
	}
}
