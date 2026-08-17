using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LokrModAPI.Serialization;
using SimpleJSON;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Projects
{
	/// <summary>Reads part and clip names from a character's rig.json without loading the Animator.</summary>
	/// <remarks>
	/// Presentation-only reads. Writes insert one object into the named array by bracket-counting
	/// so the rest of the file is left byte-for-byte intact — imported matrix-heavy rigs are
	/// refused entirely when they already have authored frames.
	/// </remarks>
	internal static class CharacterRigOutline
	{
		/// <summary>Absolute path of folder/rig/rig.json.</summary>
		internal static string RigJsonPath(string folder)
		{
			return Path.Combine(folder ?? string.Empty, "rig", "rig.json");
		}

		/// <summary>Part names in static-list order, or empty if the file is missing/unreadable.</summary>
		internal static List<string> ReadPartNames(string folder)
		{
			return ReadNames(folder, "parts");
		}

		/// <summary>Animation clip names in file order, or empty if the file is missing/unreadable.</summary>
		internal static List<string> ReadAnimationNames(string folder)
		{
			return ReadNames(folder, "animations");
		}

		/// <summary>Reads one part's name and rest offsets from rig.json, or false if missing.</summary>
		internal static bool TryReadPart(string folder, string name, out float offsetX, out float offsetY)
		{
			offsetX = 0f;
			offsetY = 0f;
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			JSONNode root = TryParse(folder);
			if (root == null)
			{
				return false;
			}

			foreach (JSONNode child in root["parts"].Children)
			{
				if (!string.Equals(child["name"].Value, name, StringComparison.Ordinal))
				{
					continue;
				}

				offsetX = child["offsetX"].AsFloat;
				offsetY = child["offsetY"].AsFloat;
				return true;
			}

			return false;
		}

		/// <summary>Frame count for a named clip, or -1 if the clip is missing.</summary>
		internal static int ReadAnimationFrameCount(string folder, string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return -1;
			}

			JSONNode root = TryParse(folder);
			if (root == null)
			{
				return -1;
			}

			foreach (JSONNode child in root["animations"].Children)
			{
				if (!string.Equals(child["name"].Value, name, StringComparison.Ordinal))
				{
					continue;
				}

				JSONNode frames = child["frames"];
				return frames != null ? frames.Count : 0;
			}

			return -1;
		}

		/// <summary>Appends a part when the rig has no authored frames. Returns false if refused or on IO error.</summary>
		internal static bool TryAddPart(string folder, string name, out string error)
		{
			return TryInsertNamedObject(folder, "parts", "{\"name\":\"" + TextEscaping.JsonEscape(name) + "\",\"offsetX\":0,\"offsetY\":0}", name, out error);
		}

		/// <summary>Appends an empty animation clip when the rig has no authored frames. Returns false if refused or on IO error.</summary>
		internal static bool TryAddAnimation(string folder, string name, out string error)
		{
			return TryInsertNamedObject(folder, "animations", "{\"name\":\"" + TextEscaping.JsonEscape(name) + "\",\"frames\":[]}", name, out error);
		}

		private static List<string> ReadNames(string folder, string arrayKey)
		{
			List<string> names = new List<string>();
			JSONNode root = TryParse(folder);
			if (root == null)
			{
				return names;
			}

			foreach (JSONNode child in root[arrayKey].Children)
			{
				string name = child["name"].Value;
				if (!string.IsNullOrEmpty(name))
				{
					names.Add(name);
				}
			}

			return names;
		}

		private static bool TryInsertNamedObject(string folder, string arrayKey, string objectJson, string name, out string error)
		{
			error = null;
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
			{
				error = "Folder and name are required.";
				return false;
			}

			string path = RigJsonPath(folder);
			JSONNode root = TryParse(folder);
			if (root == null)
			{
				error = "Could not read " + path + ".";
				return false;
			}

			if (HasAuthoredFrames(root))
			{
				error = "This rig already has authored animation frames. Add parts and clips from File → Animator so the existing data is not rewritten.";
				return false;
			}

			foreach (JSONNode child in root[arrayKey].Children)
			{
				if (string.Equals(child["name"].Value, name, StringComparison.OrdinalIgnoreCase))
				{
					error = "'" + name + "' already exists.";
					return false;
				}
			}

			string text;
			try
			{
				text = File.ReadAllText(path);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}

			if (!TryInsertIntoJsonArray(text, arrayKey, objectJson, out string updated))
			{
				error = "Could not find the '" + arrayKey + "' array in rig.json.";
				return false;
			}

			try
			{
				File.WriteAllText(path, updated);
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		/// <summary>Inserts objectJson as the last element of the first JSON array named arrayKey.</summary>
		private static bool TryInsertIntoJsonArray(string text, string arrayKey, string objectJson, out string updated)
		{
			updated = null;
			string needle = "\"" + arrayKey + "\"";
			int keyIndex = text.IndexOf(needle, StringComparison.Ordinal);
			if (keyIndex < 0)
			{
				return false;
			}

			int colon = text.IndexOf(':', keyIndex + needle.Length);
			if (colon < 0)
			{
				return false;
			}

			int open = text.IndexOf('[', colon);
			if (open < 0)
			{
				return false;
			}

			int close = FindMatchingBracket(text, open);
			if (close < 0)
			{
				return false;
			}

			string inside = text.Substring(open + 1, close - open - 1).Trim();
			StringBuilder next = new StringBuilder(text.Length + objectJson.Length + 2);
			next.Append(text, 0, close);
			if (inside.Length > 0)
			{
				next.Append(',');
			}
			next.Append(objectJson);
			next.Append(text, close, text.Length - close);
			updated = next.ToString();
			return true;
		}

		private static int FindMatchingBracket(string text, int openIndex)
		{
			int depth = 0;
			bool inString = false;
			bool escape = false;
			for (int i = openIndex; i < text.Length; i++)
			{
				char c = text[i];
				if (inString)
				{
					if (escape)
					{
						escape = false;
					}
					else if (c == '\\')
					{
						escape = true;
					}
					else if (c == '"')
					{
						inString = false;
					}
					continue;
				}

				if (c == '"')
				{
					inString = true;
				}
				else if (c == '[')
				{
					depth++;
				}
				else if (c == ']')
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}

		private static bool HasAuthoredFrames(JSONNode root)
		{
			foreach (JSONNode animNode in root["animations"].Children)
			{
				JSONNode frames = animNode["frames"];
				if (frames != null && frames.Count > 0)
				{
					return true;
				}
			}

			return false;
		}

		private static JSONNode TryParse(string folder)
		{
			string path = RigJsonPath(folder);
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				return JSON.Parse(File.ReadAllText(path));
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("CharacterRigOutline: failed to parse " + path + ": " + ex.Message);
				return null;
			}
		}
	}
}
