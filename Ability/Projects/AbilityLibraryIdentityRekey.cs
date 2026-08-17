using System;
using System.IO;
using LokrLab;
using LokrLab.Shell;

namespace LokrAbilityLab.Projects
{
	/// <summary>Rewrites a leftover Ability Library folder onto a <c>slug_token</c> id.</summary>
	/// <remarks>Characters reference ability ids, not the library folder, so this only moves the library directory and recents. The shipped placeholders library is marked in project.json so install still finds it.</remarks>
	internal static class AbilityLibraryIdentityRekey
	{
		/// <summary>Renames a leftover library folder. Does not run on load.</summary>
		internal static bool TryApplyToSlugToken(string folder, string slug, out string newFolder, out string error)
		{
			newFolder = folder;
			error = null;
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				error = "Library folder does not exist.";
				return false;
			}

			string oldId = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (LabSlugIds.LooksLikeSlugTokenId(oldId))
			{
				error = "This library already uses a slug_token id.";
				return false;
			}

			if (!LabSlugIds.IsLegalSlug(slug))
			{
				error = "Slug must start with a letter and use only lowercase letters, digits, and underscores.";
				return false;
			}

			bool placeholders = AbilityPlaceholders.IsPlaceholdersLibrary(folder);
			try
			{
				string newId = AbilityLabPaths.GenerateNewLibraryId(slug);
				newFolder = AbilityLabPaths.LibraryFolder(newId);
				Directory.Move(folder, newFolder);
				RecentProjectsStore.Remove(folder);
				if (placeholders)
				{
					AbilityPlaceholders.MarkAsPlaceholdersLibrary(newFolder);
				}

				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}
	}
}
