using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LokrModAPI.Serialization;
using SimpleJSON;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Reads/writes character.json, the sidecar holding a character's own identity (CharacterProfile).</summary>
	/// <remarks>Unlike RigEditorScene's optional sidecars (rig.pivots.json/rig.animsource.json, which have a well-defined fallback for their absence), Save here always writes, never deletes-when-trivial -- a character's own identity has no fallback, so this file is scaffolded immediately at creation and never allowed to not exist.</remarks>
	internal static class CharacterProfileSidecar
	{
		private const string FileName = "character.json";

		/// <summary>Loads a profile from character.json, or a folder-name-defaulted profile if the file is missing or corrupt. Never throws, never returns null.</summary>
		/// <remarks>Falls back to parsing the character's own definition/rlheroes.txt (RLHeroesParser) for every field this sidecar didn't find in character.json -- critical for a character.json written before 2026-08-11 (stats/skills/appearance/etc. weren't persisted here at all yet), which would otherwise silently resolve to CharacterProfile's blank field defaults instead of that character's real data. Found and fixed 2026-08-11 after it corrupted a real character's own rlheroes.txt this exact way: Load() produced a defaulted profile, and the next PersistAndSync wrote that straight back over the character's real file.</remarks>
		internal static CharacterProfile Load(string folder)
		{
			CharacterProfile profile = new CharacterProfile { Id = Path.GetFileName(folder) };
			string path = Path.Combine(folder, FileName);
			bool foundLevelsInSidecar = false;
			if (File.Exists(path))
			{
				try
				{
					JSONNode root = JSON.Parse(File.ReadAllText(path));
					profile.Name = root["name"].Value ?? string.Empty;
					profile.Description = root["description"].Value ?? string.Empty;
					profile.Locked = root["locked"].AsBool;
					profile.UnlockAchievement = root["unlockAchievement"].Value ?? string.Empty;
					profile.ImportedFromLegacyMod = root["importedFromLegacyMod"].AsBool;
					profile.VanillaSourceUniqueId = root["vanillaSourceUniqueId"].Value ?? string.Empty;
					profile.VanillaNameStem = root["vanillaNameStem"].Value ?? string.Empty;
					profile.VanillaMetaExo = root["vanillaMetaExo"].Value ?? string.Empty;
					JSONNode blockKeysNode = root["vanillaBlockKeys"];
					if (blockKeysNode.Count > 0)
					{
						profile.VanillaBlockKeys.Clear();
						foreach (JSONNode keyNode in blockKeysNode.Children)
						{
							string blockKey = keyNode.Value ?? string.Empty;
							if (blockKey.Length > 0)
							{
								profile.VanillaBlockKeys.Add(blockKey);
							}
						}
					}
					if (!Enum.TryParse(root["tier"].Value, out CharacterTier tier))
					{
						tier = CharacterTier.Companion;
					}
					profile.Tier = tier;
					if (!Enum.TryParse(root["entityType"].Value, out CharacterEntityType entityType))
					{
						entityType = CharacterEntityType.Hero;
					}
					profile.EntityType = entityType;

					profile.Model = GetOr(root, "model", profile.Model);
					profile.AttackType = GetOr(root, "attackType", profile.AttackType);
					profile.Icon = GetOr(root, "icon", profile.Icon);
					profile.Background = GetOr(root, "background", profile.Background);
					profile.UnitOnMap = GetOr(root, "unitOnMap", profile.UnitOnMap);
					profile.PortraitBackgroundColor = GetOr(root, "portraitBackgroundColor", profile.PortraitBackgroundColor);
					LoadCinematicTags(root, profile);
					profile.DefaultSkill = GetOr(root, "defaultSkill", profile.DefaultSkill);
					profile.SoundAssetId = GetOr(root, "soundAssetId", profile.SoundAssetId);

					JSONNode levelsNode = root["levels"];
					if (levelsNode.Count > 0)
					{
						foundLevelsInSidecar = true;
						profile.Levels.Clear();
						foreach (JSONNode levelNode in levelsNode.Children)
						{
							CharacterLevel level = new CharacterLevel { Level = levelNode["level"].AsInt };
							foreach (JSONNode statNode in levelNode["stats"].Children)
							{
								level.Stats.Add(new StatEntry { Name = statNode["name"].Value ?? string.Empty, Value = statNode["value"].AsFloat });
							}
							profile.Levels.Add(level);
						}
					}

					JSONNode statesNode = root["states"];
					if (statesNode.Count > 0)
					{
						profile.States.Clear();
						foreach (JSONNode stateNode in statesNode.Children)
						{
							profile.States[stateNode["name"].Value ?? string.Empty] = stateNode["on"].AsBool;
						}
					}

					JSONNode skillsNode = root["skills"];
					if (skillsNode.Count > 0)
					{
						profile.Skills.Clear();
						foreach (JSONNode skillNode in skillsNode.Children)
						{
							profile.Skills.Add(skillNode.Value ?? string.Empty);
						}
					}

					JSONNode progressionNode = root["skillProgression"];
					if (progressionNode.Count > 0)
					{
						profile.SkillProgression.Clear();
						foreach (JSONNode entryNode in progressionNode.Children)
						{
							LevelSkillEntry entry = new LevelSkillEntry { Level = entryNode["level"].AsInt };
							foreach (JSONNode idNode in entryNode["skillIds"].Children)
							{
								entry.SkillIds.Add(idNode.Value ?? string.Empty);
							}
							profile.SkillProgression.Add(entry);
						}
					}

					JSONNode soundClipsNode = root["soundClips"];
					if (soundClipsNode.Count > 0)
					{
						profile.SoundClips.Clear();
						foreach (JSONNode clipNode in soundClipsNode.Children)
						{
							profile.SoundClips[clipNode["event"].Value ?? string.Empty] = clipNode["clip"].Value ?? string.Empty;
						}
					}

					JSONNode localizationsNode = root["localizations"];
					if (localizationsNode.Count > 0)
					{
						profile.Localizations.Clear();
						foreach (JSONNode localeNode in localizationsNode.Children)
						{
							string locale = localeNode["locale"].Value ?? string.Empty;
							if (locale.Length == 0)
							{
								continue;
							}
							profile.Localizations[locale] = new CharacterLocalizedText
							{
								Name = localeNode["name"].Value ?? string.Empty,
								Description = localeNode["description"].Value ?? string.Empty,
							};
						}
					}
				}
				catch (Exception ex)
				{
					LokrCharacterLabPlugin.Log.LogWarning("CharacterProfileSidecar: failed to parse " + path + ": " + ex.Message);
				}
			}

			if (!foundLevelsInSidecar)
			{
				string rlHeroesPath = Path.Combine(folder, "definition", "rlheroes.txt");
				if (File.Exists(rlHeroesPath))
				{
					try
					{
						RLHeroesParser.ParseInto(File.ReadAllText(rlHeroesPath), profile);
					}
					catch (Exception ex)
					{
						LokrCharacterLabPlugin.Log.LogWarning("CharacterProfileSidecar: failed to migrate " + rlHeroesPath + ": " + ex.Message);
					}
				}
			}
			return profile;
		}

		/// <summary>Returns root[key]'s string value, or fallback if the key is missing or blank -- every field this is used for is always a non-empty identifier in practice, so an empty read is treated the same as a missing key.</summary>
		private static string GetOr(JSONNode root, string key, string fallback)
		{
			string value = root[key].Value;
			return string.IsNullOrEmpty(value) ? fallback : value;
		}

		/// <summary>Loads cinematicTags from a JSON array (current format) or a legacy pipe-delimited string.</summary>
		private static void LoadCinematicTags(JSONNode root, CharacterProfile profile)
		{
			JSONNode cinematicTagsNode = root["cinematicTags"];
			if (cinematicTagsNode == null)
			{
				return;
			}
			if (cinematicTagsNode.Count > 0)
			{
				profile.CinematicTags.Clear();
				foreach (JSONNode tagNode in cinematicTagsNode.Children)
				{
					string tag = tagNode.Value ?? string.Empty;
					if (tag.Length > 0)
					{
						profile.CinematicTags.Add(tag);
					}
				}
				return;
			}
			string pipeDelimited = cinematicTagsNode.Value;
			if (string.IsNullOrEmpty(pipeDelimited))
			{
				return;
			}
			profile.CinematicTags.Clear();
			foreach (string part in pipeDelimited.Split('|'))
			{
				string trimmed = part.Trim();
				if (trimmed.Length > 0)
				{
					profile.CinematicTags.Add(trimmed);
				}
			}
		}

		/// <summary>Writes a profile to character.json.</summary>
		internal static void Save(string folder, CharacterProfile profile)
		{
			StringBuilder json = new StringBuilder();
			json.Append("{\"id\":\"").Append(profile.Id)
				.Append("\",\"name\":\"").Append(TextEscaping.JsonEscape(profile.Name))
				.Append("\",\"description\":\"").Append(TextEscaping.JsonEscape(profile.Description))
				.Append("\",\"locked\":").Append(profile.Locked ? "true" : "false")
				.Append(",\"unlockAchievement\":\"").Append(TextEscaping.JsonEscape(profile.UnlockAchievement))
				.Append("\",\"tier\":\"").Append(profile.Tier)
				.Append("\",\"entityType\":\"").Append(profile.EntityType)
				.Append("\",\"importedFromLegacyMod\":").Append(profile.ImportedFromLegacyMod ? "true" : "false")
				.Append(",\"vanillaSourceUniqueId\":\"").Append(TextEscaping.JsonEscape(profile.VanillaSourceUniqueId ?? string.Empty))
				.Append("\",\"vanillaNameStem\":\"").Append(TextEscaping.JsonEscape(profile.VanillaNameStem ?? string.Empty))
				.Append("\",\"vanillaMetaExo\":\"").Append(TextEscaping.JsonEscape(profile.VanillaMetaExo ?? string.Empty))
				.Append("\",\"vanillaBlockKeys\":").Append(StringListToJson(profile.VanillaBlockKeys))
				.Append(",\"model\":\"").Append(TextEscaping.JsonEscape(profile.Model))
				.Append("\",\"attackType\":\"").Append(TextEscaping.JsonEscape(profile.AttackType))
				.Append("\",\"icon\":\"").Append(TextEscaping.JsonEscape(profile.Icon))
				.Append("\",\"background\":\"").Append(TextEscaping.JsonEscape(profile.Background))
				.Append("\",\"unitOnMap\":\"").Append(TextEscaping.JsonEscape(profile.UnitOnMap))
				.Append("\",\"portraitBackgroundColor\":\"").Append(TextEscaping.JsonEscape(profile.PortraitBackgroundColor))
				.Append("\",\"defaultSkill\":\"").Append(TextEscaping.JsonEscape(profile.DefaultSkill))
				.Append("\",\"soundAssetId\":\"").Append(TextEscaping.JsonEscape(profile.SoundAssetId))
				.Append("\",\"levels\":").Append(LevelsToJson(profile.Levels))
				.Append(",\"states\":").Append(StatesToJson(profile.States))
				.Append(",\"cinematicTags\":").Append(StringListToJson(profile.CinematicTags))
				.Append(",\"skills\":").Append(StringListToJson(profile.Skills))
				.Append(",\"skillProgression\":").Append(SkillProgressionToJson(profile.SkillProgression))
				.Append(",\"soundClips\":").Append(SoundClipsToJson(profile.SoundClips))
				.Append(",\"localizations\":").Append(LocalizationsToJson(profile.Localizations))
				.Append("}");
			File.WriteAllText(Path.Combine(folder, FileName), json.ToString());
		}

		private static string LevelsToJson(List<CharacterLevel> levels)
		{
			StringBuilder json = new StringBuilder("[");
			for (int i = 0; i < levels.Count; i++)
			{
				if (i > 0)
				{
					json.Append(",");
				}
				json.Append("{\"level\":").Append(levels[i].Level).Append(",\"stats\":").Append(StatsToJson(levels[i].Stats)).Append("}");
			}
			json.Append("]");
			return json.ToString();
		}

		/// <summary>Renders a level's stats list as a JSON array of {"name":..,"value":..} objects.</summary>
		private static string StatsToJson(List<StatEntry> stats)
		{
			StringBuilder json = new StringBuilder("[");
			for (int i = 0; i < stats.Count; i++)
			{
				if (i > 0)
				{
					json.Append(",");
				}
				json.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(stats[i].Name))
					.Append("\",\"value\":").Append(stats[i].Value.ToString(CultureInfo.InvariantCulture))
					.Append("}");
			}
			json.Append("]");
			return json.ToString();
		}

		private static string StatesToJson(Dictionary<string, bool> states)
		{
			StringBuilder json = new StringBuilder("[");
			bool first = true;
			foreach (KeyValuePair<string, bool> state in states)
			{
				if (!first)
				{
					json.Append(",");
				}
				first = false;
				json.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(state.Key)).Append("\",\"on\":").Append(state.Value ? "true" : "false").Append("}");
			}
			json.Append("]");
			return json.ToString();
		}

		private static string StringListToJson(List<string> values)
		{
			StringBuilder json = new StringBuilder("[");
			for (int i = 0; i < values.Count; i++)
			{
				if (i > 0)
				{
					json.Append(",");
				}
				json.Append("\"").Append(TextEscaping.JsonEscape(values[i])).Append("\"");
			}
			json.Append("]");
			return json.ToString();
		}

		private static string SkillProgressionToJson(List<LevelSkillEntry> entries)
		{
			StringBuilder json = new StringBuilder("[");
			for (int i = 0; i < entries.Count; i++)
			{
				if (i > 0)
				{
					json.Append(",");
				}
				json.Append("{\"level\":").Append(entries[i].Level).Append(",\"skillIds\":").Append(StringListToJson(entries[i].SkillIds)).Append("}");
			}
			json.Append("]");
			return json.ToString();
		}

		private static string SoundClipsToJson(Dictionary<string, string> clips)
		{
			StringBuilder json = new StringBuilder("[");
			bool first = true;
			foreach (KeyValuePair<string, string> clip in clips)
			{
				if (!first)
				{
					json.Append(",");
				}
				first = false;
				json.Append("{\"event\":\"").Append(TextEscaping.JsonEscape(clip.Key)).Append("\",\"clip\":\"").Append(TextEscaping.JsonEscape(clip.Value)).Append("\"}");
			}
			json.Append("]");
			return json.ToString();
		}

		private static string LocalizationsToJson(Dictionary<string, CharacterLocalizedText> localizations)
		{
			StringBuilder json = new StringBuilder("[");
			bool first = true;
			foreach (KeyValuePair<string, CharacterLocalizedText> entry in localizations)
			{
				if (!first)
				{
					json.Append(",");
				}
				first = false;
				json.Append("{\"locale\":\"").Append(TextEscaping.JsonEscape(entry.Key))
					.Append("\",\"name\":\"").Append(TextEscaping.JsonEscape(entry.Value.Name))
					.Append("\",\"description\":\"").Append(TextEscaping.JsonEscape(entry.Value.Description))
					.Append("\"}");
			}
			json.Append("]");
			return json.ToString();
		}
	}
}
