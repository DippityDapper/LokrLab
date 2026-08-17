using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LokrModAPI.Serialization;
using LokrCharacterLoader;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Keeps a character's game-consumable content files (definition/rlheroes.txt, roster.json, localization_en_US.txt, read at boot by LokrCharacterLoader's CharacterLabContentLoader) in sync with its CharacterProfile.</summary>
	/// <remarks>Sync() is the single entry point General calls after every identity edit, creation, or import. rlheroes.txt is always fully regenerated from the profile's own fields (resolved 2026-08-11) -- earlier this skipped regeneration entirely for a LegacyModImporter-imported character, preserving its old mod's file byte-for-byte but leaving nothing but the identity fields editable through the Lab; RLHeroesParser now parses that file's full content into the profile once at import time instead, so every character (imported or freshly created) is Lab-owned data end to end, and "editable in the Lab, not by hand-editing a file" holds for all of it. Every profile-sourced string appended below goes through TextEscaping.KvEscape (rlheroes.txt) or TextEscaping.JsonEscape (roster.json) -- pre-redesign audit C-01: an unescaped quote in e.g. a skill id or sound key can corrupt the shared TextAsset this fragment gets spliced into, breaking every other hero/enemy sharing that file, not just this one.</remarks>
	internal static class RLHeroesGenerator
	{
		/// <summary>Regenerates rlheroes.txt, roster.json, and the localization name/lore lines from the given profile.</summary>
		internal static void Sync(string characterFolder, CharacterProfile profile)
		{
			WriteRLHeroes(characterFolder, profile);
			if (profile.EntityType == CharacterEntityType.Hero)
			{
				string rosterId = VanillaOverrideRules.EngineUniqueId(profile.VanillaSourceUniqueId, profile.Id);
				WriteHeroRoster(characterFolder, rosterId, profile.UnlockAchievement, profile.Locked, profile.Tier, profile.IsVanillaOverride);
			}
			else
			{
				DeleteHeroRosterIfPresent(characterFolder);
			}
			string locStem = profile.IsVanillaOverride
				? VanillaOverrideRules.LocStem(profile.VanillaNameStem, profile.VanillaSourceUniqueId)
				: profile.Id;
			SyncLocalizationFile(characterFolder, "localization_en_US.txt", locStem, profile.Name, profile.Description);
			foreach (KeyValuePair<string, CharacterLocalizedText> localization in profile.Localizations)
			{
				SyncLocalizationFile(
					characterFolder,
					"localization_" + localization.Key + ".txt",
					locStem,
					localization.Value.Name,
					localization.Value.Description);
			}
		}

		/// <summary>Writes the full rlheroes.txt archetype chain from profile's own fields -- one block per profile.Levels entry, chained by InheritsFrom/nextLevelArchetype. Only the level-1 block carries UniqueId/Model/MetaExo/Name/cinematicTags/AttackType/Icon/Background/UnitOnMap/PortraitBackgroundColor/skills/skillProgression/defaultSkill/states/soundConfig -- level 2+ blocks are pure stat-override diffs, matching how real shipped rank-up characters (e.g. Onagro) are actually structured. MetaExo/Name are always the character's own Id (never user-typed), matching CustomRigLoader's own folder-name-match requirement and how localization is keyed -- kept even for an EnemySummon entity so an Animator-authored custom rig still resolves for it, unlike real shipped EnemiesDefinitions/*.txt files (which only ever reskin an existing hero's rig and so never needed MetaExo at all). UniqueId and the roster/portrait-facing identity fields (Background/Icon/UnitOnMap/PortraitBackgroundColor) are Hero-only -- an EnemySummon has no roster entry or roster/map art to point them at, matching real shipped EnemiesDefinitions/*.txt files, which never carry them either (see docs/roadmaps/completed/full-port/gaps.md). InheritsFrom is "Hero" for a Hero, "Base" for an EnemySummon, matching the same real-file convention.</summary>
		private static void WriteRLHeroes(string characterFolder, CharacterProfile profile)
		{
			bool isHero = profile.EntityType == CharacterEntityType.Hero;
			string definitionFolder = Path.Combine(characterFolder, "definition");
			Directory.CreateDirectory(definitionFolder);

			string authoredId = profile.IsVanillaOverride
				? profile.VanillaSourceUniqueId
				: LabAliases.ToAuthoredRef(characterFolder, profile.Id);
			string escapedId = TextEscaping.KvEscape(authoredId);
			bool labRig = File.Exists(Path.Combine(characterFolder, "rig", "rig.json"));
			string metaExo = TextEscaping.KvEscape(VanillaOverrideRules.EngineMetaExo(
				profile.VanillaSourceUniqueId,
				profile.VanillaMetaExo,
				profile.Id,
				labRig));
			string nameStem = TextEscaping.KvEscape(profile.IsVanillaOverride
				? VanillaOverrideRules.LocStem(profile.VanillaNameStem, profile.VanillaSourceUniqueId)
				: authoredId);

			StringBuilder content = new StringBuilder();
			for (int i = 0; i < profile.Levels.Count; i++)
			{
				CharacterLevel level = profile.Levels[i];
				bool isBaseLevel = i == 0;
				string blockKey = TextEscaping.KvEscape(EngineBlockKey(profile, authoredId, i, level.Level));

				content.Append("\"").Append(blockKey).Append("\"\n{\n");
				if (isBaseLevel)
				{
					if (isHero)
					{
						content.Append("\t\"UniqueId\" \"").Append(escapedId).Append("\"\n");
					}
					content.Append("\t\"Model\" \"").Append(TextEscaping.KvEscape(profile.Model)).Append("\"\n");
					content.Append("\t\"MetaExo\" \"").Append(metaExo).Append("\"\n");
					content.Append("\t\"Name\" \"").Append(nameStem).Append("\"\n");
					content.Append("\t\"InheritsFrom\" \"").Append(isHero ? "Hero" : "Base").Append("\"\n");
					content.Append("\t\"cinematicTags\" \"").Append(TextEscaping.KvEscape(string.Join("|", profile.CinematicTags))).Append("\"\n");
					content.Append("\t\"AttackType\" \"").Append(TextEscaping.KvEscape(profile.AttackType)).Append("\"\n");
					if (isHero)
					{
						content.Append("\t\"Icon\" \"").Append(TextEscaping.KvEscape(profile.Icon)).Append("\"\n");
						content.Append("\t\"Background\" \"").Append(TextEscaping.KvEscape(profile.Background)).Append("\"\n");
						content.Append("\t\"UnitOnMap\" \"").Append(TextEscaping.KvEscape(profile.UnitOnMap)).Append("\"\n");
						content.Append("\t\"PortraitBackgroundColor\" \"").Append(TextEscaping.KvEscape(profile.PortraitBackgroundColor)).Append("\"\n");
					}
				}
				else
				{
					content.Append("\t\"InheritsFrom\" \"").Append(
						TextEscaping.KvEscape(EngineBlockKey(profile, authoredId, i - 1, profile.Levels[i - 1].Level)))
						.Append("\"\n");
				}
				if (i + 1 < profile.Levels.Count)
				{
					content.Append("\t\"nextLevelArchetype\" \"").Append(
						TextEscaping.KvEscape(EngineBlockKey(profile, authoredId, i + 1, profile.Levels[i + 1].Level)))
						.Append("\"\n");
				}

				content.Append("\n\t\"stats\"\n\t{\n");
				foreach (StatEntry stat in level.Stats)
				{
					content.Append("\t\t\"").Append(TextEscaping.KvEscape(stat.Name)).Append("\" \"").Append(FormatStatValue(stat.Value)).Append("\"\n");
				}
				content.Append("\t}\n");

				if (isBaseLevel)
				{
					content.Append("\n\t\"skills\"\n\t{\n");
					for (int s = 0; s < profile.Skills.Count; s++)
					{
						content.Append("\t\t\"").Append(s + 1).Append("\" \"").Append(TextEscaping.KvEscape(profile.Skills[s])).Append("\"\n");
					}
					content.Append("\t}\n");

					content.Append("\n\t\"skillProgression\"\n\t{\n");
					foreach (LevelSkillEntry entry in profile.SkillProgression)
					{
						content.Append("\t\t\"").Append(entry.Level).Append("\" {");
						for (int s = 0; s < entry.SkillIds.Count; s++)
						{
							content.Append(" \"").Append(s + 1).Append("\" \"").Append(TextEscaping.KvEscape(entry.SkillIds[s])).Append("\"");
						}
						content.Append(" }\n");
					}
					content.Append("\t}\n");

					content.Append("\n\t\"defaultSkill\" \"").Append(TextEscaping.KvEscape(profile.DefaultSkill)).Append("\"\n");

					if (profile.States.Count > 0)
					{
						content.Append("\n\t\"states\"\n\t{\n");
						foreach (KeyValuePair<string, bool> state in profile.States)
						{
							if (state.Value)
							{
								content.Append("\t\t\"").Append(TextEscaping.KvEscape(state.Key)).Append("\" \"1\"\n");
							}
						}
						content.Append("\t}\n");
					}

					content.Append("\n\t\"soundConfig\"\n\t{\n");
					content.Append("\t\t\"assetId\" \"").Append(TextEscaping.KvEscape(profile.SoundAssetId)).Append("\"\n");
					content.Append("\t\t\"sounds\"\n\t\t{\n");
					foreach (KeyValuePair<string, string> sound in profile.SoundClips)
					{
						content.Append("\t\t\t\"").Append(TextEscaping.KvEscape(sound.Key)).Append("\" \"").Append(TextEscaping.KvEscape(sound.Value)).Append("\"\n");
					}
					content.Append("\t\t}\n\t}\n");
				}

				content.Append("}\n\n");
			}

			File.WriteAllText(Path.Combine(definitionFolder, "rlheroes.txt"), content.ToString());
		}

		/// <summary>The block key for one rank of the archetype chain -- the character's folder id for level 1, "&lt;Id&gt;_Lvl&lt;N&gt;" for level 2+.</summary>
		/// <remarks>
		/// Sandbox and roster look up Definitions by this key (the folder id), not UniqueId alone.
		/// SpawnUnit #word literals that cannot start with a digit use a separate c-prefix via
		/// CharacterLabPaths.ToExpressionSafeBlockKey; do not put that prefix on the block key.
		/// </remarks>
		private static string BlockKey(string id, int level)
		{
			return level == 1 ? id : id + "_Lvl" + level.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>Formats a stat value the way the real shipped files do -- a bare integer when whole (e.g. "4", not "4.0"), otherwise as few decimal digits as round-trip.</summary>
		private static string FormatStatValue(float value)
		{
			return value == Math.Floor(value) && !float.IsInfinity(value)
				? ((long)value).ToString(CultureInfo.InvariantCulture)
				: value.ToString("R", CultureInfo.InvariantCulture);
		}

		/// <summary>Shipped block key for an override rank, or the Lab folder id chain for a fork.</summary>
		private static string EngineBlockKey(CharacterProfile profile, string authoredId, int levelIndex, int levelNumber)
		{
			if (profile.IsVanillaOverride)
			{
				return VanillaOverrideRules.BlockKeyAt(profile.VanillaBlockKeys, levelIndex, profile.VanillaSourceUniqueId);
			}

			return BlockKey(authoredId, levelNumber);
		}

		/// <summary>Writes roster.json's id/unlockAchievement/locked fields.</summary>
		/// <remarks>unlockAchievement is omitted entirely when empty, matching the old system's own convention -- real shipped roster fragments either have a real achievement id or no "unlockAchievement" key at all, never an empty-string one. tier isn't written here despite being a parameter: it decides which HeroRosterManager list (legends vs. companions) this fragment gets spliced into -- CharacterLabContentLoader reads that straight from character.json's own "tier" field instead, since it's the fragment's *destination*, not part of the fragment's own content. Override projects write the vanilla UniqueId literally so last-wins replaces Gerald, not a $alias.</remarks>
		private static void WriteHeroRoster(
			string characterFolder,
			string id,
			string unlockAchievement,
			bool locked,
			CharacterTier tier,
			bool vanillaOverride)
		{
			StringBuilder json = new StringBuilder();
			string authoredId = vanillaOverride ? id : LabAliases.ToAuthoredRef(characterFolder, id);
			json.Append("{\"id\":\"").Append(TextEscaping.JsonEscape(authoredId)).Append("\"");
			if (!string.IsNullOrEmpty(unlockAchievement))
			{
				json.Append(",\"unlockAchievement\":\"").Append(TextEscaping.JsonEscape(unlockAchievement)).Append("\"");
			}
			json.Append(",\"locked\":").Append(locked ? "true" : "false").Append("}");
			File.WriteAllText(Path.Combine(characterFolder, "roster.json"), json.ToString());
		}

		/// <summary>Deletes a stale roster.json left behind by switching a character from Hero to EnemySummon -- otherwise CharacterLabContentLoader.OnBuildingHeroRoster would keep contributing a ghost roster entry for an entity that no longer means to have one.</summary>
		private static void DeleteHeroRosterIfPresent(string characterFolder)
		{
			string path = Path.Combine(characterFolder, "roster.json");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		/// <summary>Read-modify-writes just the UNIT_*_NAME_0001/UNIT_*_LORE lines in the given locale file (e.g. localization_en_US.txt, localization_es.txt), leaving every other line (including an imported character's copied-wholesale SKILL_*/COMBAT_MODIFIER_* lines) untouched. Called once for English (profile.Name/Description) and once per CharacterProfile.Localizations entry.</summary>
		/// <remarks>
		/// The UNIT_* stem is the unique id (engine key), not $alias. Roster lookup is
		/// UNIT_&lt;definition.name&gt;_NAME_0001 after $alias expand on rlheroes.txt, so a
		/// UNIT_$assassin_NAME_0001 line never matches.
		/// </remarks>
		private static void SyncLocalizationFile(string characterFolder, string fileName, string id, string name, string description)
		{
			string path = Path.Combine(characterFolder, fileName);
			string nameKey = LabCatalogRules.UnitNameLocKey(id);
			string loreKey = LabCatalogRules.UnitLoreLocKey(id);

			List<string> lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
			bool nameWritten = UpsertLine(lines, nameKey, name);
			bool loreWritten = UpsertLine(lines, loreKey, description);
			if (!nameWritten)
			{
				lines.Add(FormatLine(nameKey, name));
			}
			if (!loreWritten)
			{
				lines.Add(FormatLine(loreKey, description));
			}

			string aliasKey = LabAliases.FindKeyForId(LabAliases.Load(characterFolder), id);
			if (!string.IsNullOrEmpty(aliasKey))
			{
				RemoveKeyedLine(lines, "UNIT_$" + aliasKey + "_NAME_0001");
				RemoveKeyedLine(lines, "UNIT_$" + aliasKey + "_LORE");
			}

			File.WriteAllLines(path, lines);
		}

		/// <summary>Replaces an existing "KEY" = "VALUE" line in place, matching the base game's LocalizationManager.lineKVRegexp format exactly. Returns true if found and replaced; false if the caller still needs to append a new line.</summary>
		private static bool UpsertLine(List<string> lines, string key, string value)
		{
			Regex keyPattern = new Regex("^\\s*\"" + Regex.Escape(key) + "\"\\s*=");
			for (int i = 0; i < lines.Count; i++)
			{
				if (keyPattern.IsMatch(lines[i]))
				{
					lines[i] = FormatLine(key, value);
					return true;
				}
			}
			return false;
		}

		/// <summary>Removes a localization line whose key matches, so stale UNIT_$alias_* stems do not sit beside the unique-id keys.</summary>
		private static void RemoveKeyedLine(List<string> lines, string key)
		{
			Regex keyPattern = new Regex("^\\s*\"" + Regex.Escape(key) + "\"\\s*=");
			for (int i = lines.Count - 1; i >= 0; i--)
			{
				if (keyPattern.IsMatch(lines[i]))
				{
					lines.RemoveAt(i);
				}
			}
		}

		/// <summary>Formats a "KEY" = "VALUE" localization line, escaping quotes in the value.</summary>
		private static string FormatLine(string key, string value)
		{
			return "\"" + key + "\" = \"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
		}
	}
}
