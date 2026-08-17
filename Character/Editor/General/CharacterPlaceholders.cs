using System.IO;
using System.Reflection;
using LokrAbilityLab;
using LokrCharacterLab;
using UnityEngine;

namespace LokrLab.Editor.General
{
	/// <summary>Copies this plugin's Placeholders/ templates onto a new character and assigns the Ability Lab placeholder ids.</summary>
	/// <remarks>
	/// Visuals (rig.json, body.png, portrait.png) live in BepInEx/plugins/LokrLab/Placeholders/.
	/// Ability KV lives in the same Placeholders/ folder — this class writes the
	/// current on-disk ids (leftover stems or slug_token after Rename) that characters reference.
	/// UIHeroRoomHeroData.LoadSkillsAndTraits indexes skillProgression[1], [2], and [3], so all three
	/// ranks are required (2 / 3 / 3 slots).
	/// </remarks>
	internal static class CharacterPlaceholders
	{
		/// <summary>Ability id assigned as defaultSkill (basic attack). Must not appear in skillProgression.</summary>
		internal const string AttackId = "placeholder_attack";

		/// <summary>Ability id granted in every skillProgression rank until the author replaces it.</summary>
		internal const string SkillId = "placeholder_skill";

		/// <summary>First passive trait id written into the skills block.</summary>
		internal const string PassiveId = "placeholder_passive";

		/// <summary>Second placeholder trait — the hero room has five trait circles.</summary>
		internal const string PassiveId2 = "placeholder_passive_2";

		/// <summary>Third placeholder trait.</summary>
		internal const string PassiveId3 = "placeholder_passive_3";

		/// <summary>Vanilla Model prefab combat instantiates; the custom MetaExo rig is swapped onto it.</summary>
		internal const string DefaultModel = "HumanArcher";

		/// <summary>AttackType written on a new hero so melee placeholder abilities match the Model.</summary>
		internal const string DefaultAttackType = "MELEE";

		/// <summary>Vanilla sound-group name used when a character has no authored clips yet.</summary>
		internal const string DefaultSoundAssetId = "DynamicSoundGroupGenericSkillSounds";

		private const string BodyPartName = "body";
		private const int FallbackSpriteSize = 64;

		/// <summary>This plugin's Placeholders/ folder next to LokrLab.dll.</summary>
		internal static string PluginPlaceholdersFolder =>
			Path.Combine(Path.GetDirectoryName(typeof(LokrCharacterLabPlugin).Assembly.Location) ?? string.Empty, "Placeholders");

		/// <summary>Fills skill, passive, attack, Model, AttackType, and sound fields on a brand-new profile.</summary>
		internal static void ApplyToNewProfile(CharacterProfile profile)
		{
			if (profile == null)
			{
				return;
			}

			if (string.IsNullOrEmpty(profile.DefaultSkill))
			{
				profile.DefaultSkill = AbilityPlaceholders.ResolveAbilityId(AttackId);
			}

			if (profile.Skills.Count == 0)
			{
				profile.Skills.Add(AbilityPlaceholders.ResolveAbilityId(PassiveId));
				profile.Skills.Add(AbilityPlaceholders.ResolveAbilityId(PassiveId2));
				profile.Skills.Add(AbilityPlaceholders.ResolveAbilityId(PassiveId3));
			}

			EnsureProgressionLevel(profile, 1, 2);
			EnsureProgressionLevel(profile, 2, 3);
			EnsureProgressionLevel(profile, 3, 3);

			if (string.IsNullOrEmpty(profile.Model))
			{
				profile.Model = DefaultModel;
			}

			if (string.IsNullOrEmpty(profile.AttackType))
			{
				profile.AttackType = DefaultAttackType;
			}

			if (string.IsNullOrEmpty(profile.SoundAssetId))
			{
				profile.SoundAssetId = DefaultSoundAssetId;
			}

			if (profile.EntityType == CharacterEntityType.Hero && profile.CinematicTags.Count == 0)
			{
				profile.CinematicTags.Add("Heroe");
			}
		}

		/// <summary>Copies Placeholders/rig.json, body.png, and portrait.png onto the character folder when those files are missing.</summary>
		internal static void WritePlaceholderVisuals(string folder, string characterId)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(characterId))
			{
				return;
			}

			string templates = PluginPlaceholdersFolder;
			Directory.CreateDirectory(Path.Combine(folder, "rig"));
			Directory.CreateDirectory(Path.Combine(folder, "sprites"));

			string rigPath = Path.Combine(folder, "rig", "rig.json");
			if (!File.Exists(rigPath) || IsEmptyRig(File.ReadAllText(rigPath)))
			{
				CopyOrWarn(Path.Combine(templates, "rig.json"), rigPath);
			}

			string spritePath = Path.Combine(folder, "sprites", BodyPartName + ".png");
			if (!File.Exists(spritePath))
			{
				if (!CopyOrWarn(Path.Combine(templates, "body.png"), spritePath))
				{
					WriteSolidPng(spritePath, new Color(0.55f, 0.55f, 0.62f, 1f));
				}
			}

			string[] slots = { "MINI", "BIG", "BANNER", "MAPMINI" };
			string portraitTemplate = Path.Combine(templates, "portrait.png");
			for (int i = 0; i < slots.Length; i++)
			{
				string portraitPath = CharacterPortraitsPanel.SlotPath(characterId, slots[i]);
				if (File.Exists(portraitPath))
				{
					continue;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(portraitPath));
				if (!CopyOrWarn(portraitTemplate, portraitPath))
				{
					WriteSolidPng(portraitPath, new Color(0.45f, 0.48f, 0.55f, 1f));
				}
			}
		}

		/// <summary>Adds a skillProgression rank with the given number of placeholder_skill slots if that rank is missing.</summary>
		private static void EnsureProgressionLevel(CharacterProfile profile, int level, int slotCount)
		{
			for (int i = 0; i < profile.SkillProgression.Count; i++)
			{
				if (profile.SkillProgression[i].Level == level)
				{
					return;
				}
			}

			string skillId = AbilityPlaceholders.ResolveAbilityId(SkillId);
			LevelSkillEntry entry = new LevelSkillEntry { Level = level };
			for (int s = 0; s < slotCount; s++)
			{
				entry.SkillIds.Add(skillId);
			}

			profile.SkillProgression.Add(entry);
		}

		private static bool IsEmptyRig(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return true;
			}

			return json.IndexOf("\"parts\":[]", System.StringComparison.Ordinal) >= 0
				&& json.IndexOf("\"animations\":[]", System.StringComparison.Ordinal) >= 0;
		}

		private static bool CopyOrWarn(string source, string dest)
		{
			if (!File.Exists(source))
			{
				LokrCharacterLabPlugin.Log.LogWarning("Character placeholder missing: " + source);
				return false;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(dest));
			File.Copy(source, dest, overwrite: true);
			return true;
		}

		private static void WriteSolidPng(string path, Color color)
		{
			Texture2D texture = new Texture2D(FallbackSpriteSize, FallbackSpriteSize, TextureFormat.RGBA32, false);
			Color[] pixels = new Color[FallbackSpriteSize * FallbackSpriteSize];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = color;
			}

			texture.SetPixels(pixels);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.Destroy(texture);
		}
	}
}
