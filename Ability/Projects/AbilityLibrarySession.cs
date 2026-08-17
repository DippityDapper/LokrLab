using System.IO;
using LokrLabApi;

namespace LokrAbilityLab.Projects
{
	/// <summary>One Ability Library session — a folder of abilities, not a singleton.</summary>
	public sealed class AbilityLibrarySession : ProjectSession
	{
		/// <summary>Builds a session for the given library folder.</summary>
		public static AbilityLibrarySession Create(string libraryFolder)
		{
			if (string.IsNullOrEmpty(libraryFolder))
			{
				return null;
			}

			Directory.CreateDirectory(libraryFolder);
			string id = Path.GetFileName(libraryFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string display = ReadDisplayName(libraryFolder) ?? id;
			return new AbilityLibrarySession
			{
				ProjectTypeId = LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
				Id = id,
				FolderPath = libraryFolder,
				DisplayName = display,
				IsDirty = false
			};
		}

		/// <summary>Reads displayName from project.json, or null.</summary>
		internal static string ReadDisplayName(string libraryFolder)
		{
			string path = Path.Combine(libraryFolder, "project.json");
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				string text = File.ReadAllText(path);
				int key = text.IndexOf("\"displayName\"");
				if (key < 0)
				{
					return null;
				}

				int colon = text.IndexOf(':', key);
				int quote = text.IndexOf('"', colon + 1);
				int end = quote >= 0 ? text.IndexOf('"', quote + 1) : -1;
				if (quote < 0 || end < 0)
				{
					return null;
				}

				return text.Substring(quote + 1, end - quote - 1);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>Writes project.json for a new or renamed library.</summary>
		internal static void WriteMarker(string libraryFolder, string displayName)
		{
			Directory.CreateDirectory(libraryFolder);
			string escaped = (displayName ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
			File.WriteAllText(Path.Combine(libraryFolder, "project.json"),
				"{\"projectType\":\"ability-library\",\"schemaVersion\":1,\"displayName\":\"" + escaped + "\"}");
		}
	}
}
