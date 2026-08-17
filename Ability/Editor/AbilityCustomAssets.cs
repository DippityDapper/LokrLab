using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LokrCharacterLoader;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Creates and lists per-ability sprite FX / projectile folders. The Loader builds the prefabs.</summary>
	internal static class AbilityCustomAssets
	{
		private static readonly Regex SafeName = new Regex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

		internal static string AbilityFolder(AbilityFileModel model)
		{
			if (model == null || string.IsNullOrEmpty(model.SourceFilePath))
			{
				return null;
			}

			return Path.GetDirectoryName(model.SourceFilePath);
		}

		internal static List<string> ListFx(AbilityFileModel model)
		{
			return ListChildren(AbilityFolder(model), "fx");
		}

		internal static List<string> ListProjectiles(AbilityFileModel model)
		{
			return ListChildren(AbilityFolder(model), "projectiles");
		}

		internal static bool TryCreateFxFromSprite(AbilityFileModel model, string requestedName, string sourcePng, out string createdName, out string error)
		{
			return TryCreateFromSprite(model, "fx", requestedName, sourcePng, WriteDefaultFxJson, out createdName, out error);
		}

		internal static bool TryCreateProjectileFromSprite(AbilityFileModel model, string requestedName, string sourcePng, out string createdName, out string error)
		{
			return TryCreateFromSprite(model, "projectiles", requestedName, sourcePng, WriteDefaultProjectileJson, out createdName, out error);
		}

		internal static bool TrySetSprite(string abilityFolder, string nested, string name, string sourcePng, out string error)
		{
			error = null;
			if (string.IsNullOrEmpty(abilityFolder) || string.IsNullOrEmpty(name))
			{
				error = "Save the ability once before adding custom visuals.";
				return false;
			}

			string folder = Path.Combine(abilityFolder, nested, name);
			Directory.CreateDirectory(folder);
			return TryCopySprite(sourcePng, folder, out error);
		}

		internal static string NameFromFile(string path)
		{
			string stem = Path.GetFileNameWithoutExtension(path ?? string.Empty);
			StringBuilder text = new StringBuilder();
			foreach (char c in stem)
			{
				if (char.IsLetterOrDigit(c) || c == '_')
				{
					text.Append(c);
				}
			}

			string name = text.ToString();
			if (name.Length == 0 || !char.IsLetter(name[0]))
			{
				name = "Fx" + name;
			}

			return name;
		}

		internal static SpriteFxEdit ReadFx(string abilityFolder, string name)
		{
			return ReadFxJson(Path.Combine(abilityFolder, "fx", name, "fx.json"));
		}

		internal static void WriteFx(string abilityFolder, string name, SpriteFxEdit edit)
		{
			if (edit != null)
			{
				edit.attachPoint = NormalizeAttachPoint(edit.attachPoint);
			}

			string folder = Path.Combine(abilityFolder, "fx", name);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "fx.json"), edit.ToJson());
		}

		/// <summary>FXMega sockets are Chest / Base / Head — not the expression tokens #Chest.</summary>
		internal static string NormalizeAttachPoint(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "Chest";
			}

			string text = value.Trim();
			if (text.Length > 0 && text[0] == '#')
			{
				text = text.Substring(1);
			}

			return string.IsNullOrEmpty(text) ? "Chest" : text;
		}

		internal static bool HasSprite(string abilityFolder, string nested, string name)
		{
			string folder = Path.Combine(abilityFolder, nested, name);
			return File.Exists(Path.Combine(folder, "sprite.png"))
				|| Directory.GetFiles(folder, "*.png").Length > 0;
		}

		internal static bool Owns(AbilityFileModel model, string nested, string name)
		{
			string abilityFolder = AbilityFolder(model);
			if (string.IsNullOrEmpty(abilityFolder) || string.IsNullOrEmpty(name))
			{
				return false;
			}

			return Directory.Exists(Path.Combine(abilityFolder, nested, name));
		}

		internal static bool TryDelete(AbilityFileModel model, string nested, string name, out string error)
		{
			error = null;
			string abilityFolder = AbilityFolder(model);
			if (string.IsNullOrEmpty(abilityFolder) || string.IsNullOrEmpty(name))
			{
				return true;
			}

			string folder = Path.Combine(abilityFolder, nested, name);
			if (!Directory.Exists(folder))
			{
				return true;
			}

			try
			{
				Directory.Delete(folder, recursive: true);
				string parent = Path.GetDirectoryName(folder);
				if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
				{
					Directory.Delete(parent);
				}

				RefreshRuntime();
				return true;
			}
			catch (Exception ex)
			{
				error = "Could not delete " + name + ": " + ex.Message;
				return false;
			}
		}

		internal static string ReadRestoreFxId(string abilityFolder, string name)
		{
			return ReadFx(abilityFolder, name).restoreFxId ?? string.Empty;
		}

		internal static void WriteRestoreFxId(string abilityFolder, string name, string restoreFxId)
		{
			SpriteFxEdit edit = ReadFx(abilityFolder, name);
			edit.restoreFxId = restoreFxId ?? string.Empty;
			WriteFx(abilityFolder, name, edit);
		}

		internal static string ReadRestoreModel(string abilityFolder, string name)
		{
			return ReadProjectile(abilityFolder, name).restoreModel ?? string.Empty;
		}

		internal static void WriteRestoreModel(string abilityFolder, string name, string restoreModel)
		{
			ProjectileEdit edit = ReadProjectile(abilityFolder, name);
			edit.restoreModel = restoreModel ?? string.Empty;
			WriteProjectile(abilityFolder, name, edit);
		}

		internal static ProjectileEdit ReadProjectile(string abilityFolder, string name)
		{
			return ReadProjectileJson(Path.Combine(abilityFolder, "projectiles", name, "projectile.json"));
		}

		internal static void WriteProjectile(string abilityFolder, string name, ProjectileEdit edit)
		{
			string folder = Path.Combine(abilityFolder, "projectiles", name);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "projectile.json"), edit.ToJson());
		}

		internal static void RefreshRuntime()
		{
			CharacterAPI.RefreshCustomVisuals();
		}

		private static List<string> ListChildren(string abilityFolder, string nested)
		{
			List<string> names = new List<string>();
			if (string.IsNullOrEmpty(abilityFolder))
			{
				return names;
			}

			string root = Path.Combine(abilityFolder, nested);
			if (!Directory.Exists(root))
			{
				return names;
			}

			foreach (string folder in Directory.GetDirectories(root))
			{
				names.Add(Path.GetFileName(folder));
			}

			names.Sort(StringComparer.OrdinalIgnoreCase);
			return names;
		}

		private static bool TryCreateFromSprite(AbilityFileModel model, string nested, string requestedName, string sourcePng, Action<string> writeJson, out string createdName, out string error)
		{
			createdName = null;
			error = null;
			string abilityFolder = AbilityFolder(model);
			if (string.IsNullOrEmpty(abilityFolder))
			{
				error = "Save the ability once before adding custom visuals.";
				return false;
			}

			string name = (requestedName ?? string.Empty).Trim();
			if (!SafeName.IsMatch(name))
			{
				name = NameFromFile(sourcePng);
			}

			if (!SafeName.IsMatch(name))
			{
				error = "Could not make a valid name from that file. Use a PNG whose name starts with a letter (letters, digits, and underscore only).";
				return false;
			}

			string folder = Path.Combine(abilityFolder, nested, name);
			Directory.CreateDirectory(folder);
			if (nested == "fx" && !File.Exists(Path.Combine(folder, "fx.json")))
			{
				writeJson(folder);
			}
			else if (nested == "projectiles" && !File.Exists(Path.Combine(folder, "projectile.json")))
			{
				writeJson(folder);
			}

			if (!TryCopySprite(sourcePng, folder, out error))
			{
				return false;
			}

			createdName = name;
			return true;
		}

		private static bool TryCopySprite(string sourcePng, string destFolder, out string error)
		{
			error = null;
			if (string.IsNullOrEmpty(sourcePng) || !File.Exists(sourcePng))
			{
				error = "That file is gone.";
				return false;
			}

			if (!string.Equals(Path.GetExtension(sourcePng), ".png", StringComparison.OrdinalIgnoreCase))
			{
				error = "Pick a PNG.";
				return false;
			}

			string dest = Path.Combine(destFolder, "sprite.png");
			string sourceFull = Path.GetFullPath(sourcePng);
			string destFull = Path.GetFullPath(dest);
			if (!string.Equals(sourceFull, destFull, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(sourceFull, destFull, overwrite: true);
			}

			RefreshRuntime();
			return true;
		}

		private static void WriteDefaultFxJson(string folder)
		{
			File.WriteAllText(Path.Combine(folder, "fx.json"), new SpriteFxEdit().ToJson());
		}

		private static void WriteDefaultProjectileJson(string folder)
		{
			File.WriteAllText(Path.Combine(folder, "projectile.json"), new ProjectileEdit().ToJson());
		}

		private static ProjectileEdit ReadProjectileJson(string path)
		{
			ProjectileEdit edit = new ProjectileEdit();
			if (!File.Exists(path))
			{
				return edit;
			}

			try
			{
				ProjectileEdit parsed = JsonUtility.FromJson<ProjectileEdit>(File.ReadAllText(path));
				return parsed ?? edit;
			}
			catch
			{
				return edit;
			}
		}

		private static SpriteFxEdit ReadFxJson(string path)
		{
			SpriteFxEdit edit = new SpriteFxEdit();
			if (!File.Exists(path))
			{
				return edit;
			}

			try
			{
				SpriteFxEdit parsed = JsonUtility.FromJson<SpriteFxEdit>(File.ReadAllText(path));
				edit = parsed ?? edit;
			}
			catch
			{
			}

			edit.attachPoint = NormalizeAttachPoint(edit.attachPoint);
			return edit;
		}

		[Serializable]
		internal sealed class SpriteFxEdit
		{
			public string attachPoint = "Chest";
			public string createEvent = "start";
			public string castCreateEvent = "AbilityAction";
			public string removeEvent = "AbilityEnd";
			public string finishEvent = "AbilityEnd";
			public bool detached = false;
			public bool loops = false;
			public float duration = 0.6f;
			public string soundId = string.Empty;
			public float pixelsPerUnit = 100f;
			public string restoreFxId = string.Empty;

			internal string ToJson()
			{
				StringBuilder text = new StringBuilder();
				text.Append("{\n");
				text.Append("  \"attachPoint\": \"").Append(Escape(attachPoint)).Append("\",\n");
				text.Append("  \"createEvent\": \"").Append(Escape(createEvent)).Append("\",\n");
				text.Append("  \"castCreateEvent\": \"").Append(Escape(castCreateEvent)).Append("\",\n");
				text.Append("  \"removeEvent\": \"").Append(Escape(removeEvent)).Append("\",\n");
				text.Append("  \"finishEvent\": \"").Append(Escape(finishEvent)).Append("\",\n");
				text.Append("  \"detached\": ").Append(detached ? "true" : "false").Append(",\n");
				text.Append("  \"loops\": ").Append(loops ? "true" : "false").Append(",\n");
				text.Append("  \"duration\": ").Append(duration.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"soundId\": \"").Append(Escape(soundId)).Append("\",\n");
				text.Append("  \"pixelsPerUnit\": ").Append(pixelsPerUnit.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"restoreFxId\": \"").Append(Escape(restoreFxId)).Append("\"\n");
				text.Append("}\n");
				return text.ToString();
			}

			private static string Escape(string value)
			{
				return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
			}
		}

		[Serializable]
		internal sealed class ProjectileEdit
		{
			public float maxSpeed = 8f;
			public float maxForce = 24f;
			public float slowingDistance = 0.4f;
			public float forceMultiplier = 1f;
			public bool keepTrackingTarget = true;
			public bool ignoresRotation;
			public float pixelsPerUnit = 100f;
			public string restoreModel = string.Empty;

			internal string ToJson()
			{
				StringBuilder text = new StringBuilder();
				text.Append("{\n");
				text.Append("  \"maxSpeed\": ").Append(maxSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"maxForce\": ").Append(maxForce.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"slowingDistance\": ").Append(slowingDistance.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"forceMultiplier\": ").Append(forceMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"keepTrackingTarget\": ").Append(keepTrackingTarget ? "true" : "false").Append(",\n");
				text.Append("  \"ignoresRotation\": ").Append(ignoresRotation ? "true" : "false").Append(",\n");
				text.Append("  \"pixelsPerUnit\": ").Append(pixelsPerUnit.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
				text.Append("  \"restoreModel\": \"").Append(SpriteFxEditEscape(restoreModel)).Append("\"\n");
				text.Append("}\n");
				return text.ToString();
			}

			private static string SpriteFxEditEscape(string value)
			{
				return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
			}
		}
	}
}
