using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleJSON;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Persists the Load workstation's recent-characters list (HomeWorkstationScene.AddRecentCharacter/RemoveRecentCharacter) to CharacterLabPaths.EditorDataRoot/recent.json, a flat JSON array of folder path strings, most-recent-first.</summary>
	internal static class RecentFilesStore
	{
		private static string FilePath => Path.Combine(CharacterLabPaths.EditorDataRoot, "recent.json");

		/// <summary>Loads the recent-folders list, or empty if the file is missing/corrupt.</summary>
		internal static List<string> Load()
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
						result.Add(CharacterLabPaths.RewriteMigratedFolder(folder));
					}
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("RecentFilesStore: failed to parse recent.json at " + path + ": " + ex.Message);
			}
			return result;
		}

		/// <summary>Writes the recent-folders list.</summary>
		internal static void Save(List<string> paths)
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
				Directory.CreateDirectory(CharacterLabPaths.EditorDataRoot);
				File.WriteAllText(FilePath, json.ToString());
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("RecentFilesStore: failed to write recent.json: " + ex.Message);
			}
		}
	}
}
