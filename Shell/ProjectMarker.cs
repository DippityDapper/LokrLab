using System;
using System.IO;
using SimpleJSON;

namespace LokrLab.Shell
{
	/// <summary>Reads and writes the universal project.json marker every project folder carries.</summary>
	/// <remarks>
	/// Lives in LokrLab, not LokrLabApi: the contracts plugin has no IO. The marker is the one
	/// format every project type shares so the Project Browser can route without understanding
	/// type-specific content. Project types write their own markers on create/load.
	/// </remarks>
	internal static class ProjectMarker
	{
		/// <summary>File name written at the root of every project folder.</summary>
		internal const string FileName = "project.json";

		/// <summary>Current marker schema version.</summary>
		internal const int SchemaVersion = 1;

		/// <summary>Writes or overwrites project.json in the given folder.</summary>
		internal static void Write(string folder, string projectTypeId, string displayName = null)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(projectTypeId))
			{
				return;
			}

			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileName);
			string json = "{\"projectType\":\"" + Escape(projectTypeId) + "\",\"schemaVersion\":" + SchemaVersion;
			if (!string.IsNullOrEmpty(displayName))
			{
				json += ",\"displayName\":\"" + Escape(displayName) + "\"";
			}

			json += "}";
			try
			{
				File.WriteAllText(path, json);
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogWarning("ProjectMarker: failed to write " + path + ": " + ex.Message);
			}
		}

		/// <summary>Reads an optional display name from project.json, or null if missing.</summary>
		internal static string TryReadDisplayName(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return null;
			}

			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(path));
				string displayName = root["displayName"].Value;
				return string.IsNullOrEmpty(displayName) ? null : displayName;
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogWarning("ProjectMarker: failed to parse " + path + ": " + ex.Message);
				return null;
			}
		}

		/// <summary>Reads the project type id from a folder's project.json, or null if missing/unreadable.</summary>
		internal static string TryReadProjectType(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return null;
			}

			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(path));
				string projectType = root["projectType"].Value;
				return string.IsNullOrEmpty(projectType) ? null : projectType;
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogWarning("ProjectMarker: failed to parse " + path + ": " + ex.Message);
				return null;
			}
		}

		private static string Escape(string value)
		{
			return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}
}
