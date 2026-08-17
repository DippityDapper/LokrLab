using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace LokrLab.Shell
{
	/// <summary>Persists recently opened project folders for the Project Browser Recent section.</summary>
	/// <remarks>
	/// The file keeps the full most-recent-first list. The Project Browser Recent section shows
	/// only the newest five; '-' on a Recent row calls Remove without deleting the project.
	/// </remarks>
	internal static class RecentProjectsStore
	{
		private static string FilePath => Path.Combine(Application.dataPath, "Mods", "LokrLab", "EditorData", "recent-projects.json");

		private static string RewriteMigratedFolder(string folder)
		{
			string mods = Path.Combine(Application.dataPath, "Mods");
			string rewritten = RewritePrefix(folder, Path.Combine(mods, "LokrCharacterLab", "LokrCharacterLab"), Path.Combine(mods, "LokrLab", "LokrCharacterLab"));
			rewritten = RewritePrefix(rewritten, Path.Combine(mods, "LokrCharacterLab", "Characters"), Path.Combine(mods, "LokrLab", "LokrCharacterLab"));
			rewritten = RewritePrefix(rewritten, Path.Combine(mods, "LokrLab", "Characters"), Path.Combine(mods, "LokrLab", "LokrCharacterLab"));
			rewritten = RewritePrefix(rewritten, Path.Combine(mods, "LokrAbilityLab", "LokrAbilityLab"), Path.Combine(mods, "LokrLab", "LokrAbilityLab"));
			return rewritten;
		}

		private static string RewritePrefix(string folder, string oldRoot, string newRoot)
		{
			string full = Path.GetFullPath(folder);
			string oldFull = Path.GetFullPath(oldRoot);
			if (!full.StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
			{
				return folder;
			}

			string rest = full.Substring(oldFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return string.IsNullOrEmpty(rest) ? newRoot : Path.Combine(newRoot, rest);
		}

		/// <summary>Loads recent folders, most-recent first. Missing or corrupt files yield an empty list.</summary>
		internal static IReadOnlyList<string> Load()
		{
			List<string> result = new List<string>();
			string path = FilePath;
			if (!File.Exists(path))
			{
				return result;
			}

			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(path));
				foreach (JSONNode node in root["recent"].Children)
				{
					string folder = node.Value;
					if (!string.IsNullOrEmpty(folder))
					{
						result.Add(RewriteMigratedFolder(folder));
					}
				}
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogWarning("RecentProjectsStore: failed to parse " + path + ": " + ex.Message);
			}

			return result;
		}

		/// <summary>Records a project folder as the most recently opened.</summary>
		/// <remarks>Does not trim the file. Display capping lives in the Project Browser menu.</remarks>
		internal static void Record(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return;
			}

			List<string> paths = new List<string>(Load());
			paths.RemoveAll(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase));
			paths.Insert(0, folder);
			Save(paths);
		}

		/// <summary>Drops a folder from the recent list (unpin from the menu, or after delete).</summary>
		internal static void Remove(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return;
			}

			List<string> paths = new List<string>(Load());
			int before = paths.Count;
			paths.RemoveAll(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase));
			if (paths.Count != before)
			{
				Save(paths);
			}
		}

		private static void Save(List<string> paths)
		{
			StringBuilder json = new StringBuilder();
			json.Append("{\"recent\":[");
			for (int i = 0; i < paths.Count; i++)
			{
				if (i > 0)
				{
					json.Append(",");
				}

				json.Append("\"").Append(paths[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
			}

			json.Append("]}");

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				File.WriteAllText(FilePath, json.ToString());
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogWarning("RecentProjectsStore: failed to write " + FilePath + ": " + ex.Message);
			}
		}
	}
}
