using System.Collections.Generic;
using KVLib;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Parses an existing rlheroes.txt file into a CharacterProfile's stats/skills/sound/appearance/level-chain fields.</summary>
	/// <remarks>Uses the base game's own KVLib.KVParser (Ironhide.Legends.dll, already referenced solution-wide) rather than a hand-rolled regex, unlike LegacyModImporter's earlier narrow UniqueId/Name-only extraction -- a real parser is worth it now that every field in the file needs to round-trip losslessly through CharacterProfile, not just two of them. Only touches the fields this file actually owns; Id/Name/Description/Locked/UnlockAchievement/Tier/ImportedFromLegacyMod come from roster.json/localization/import bookkeeping instead, see LegacyModImporter. Real files can hold more than one top-level block chained by InheritsFrom/nextLevelArchetype (a rank-up archetype chain -- see Onagro's own real file, the case this was built and verified against 2026-08-11) -- ParseInto walks that whole chain into profile.Levels, not just the first block.</remarks>
	internal static class RLHeroesParser
	{
		/// <summary>Overwrites profile's stats/skills/sound/appearance/level-chain/states fields from rlHeroesText's full block chain. A no-op for any single-value field whose KV key isn't present on the base block, leaving profile's existing value (its own default, if this is a freshly scaffolded profile).</summary>
		internal static void ParseInto(string rlHeroesText, CharacterProfile profile)
		{
			KeyValue[] roots = KVParser.KV1.ParseAll(rlHeroesText);
			if (roots.Length == 0)
			{
				return;
			}

			Dictionary<string, KeyValue> blocksByKey = new Dictionary<string, KeyValue>();
			foreach (KeyValue root in roots)
			{
				blocksByKey[root.Key] = root;
			}

			KeyValue baseBlock = FindBaseBlock(roots);

			profile.Model = GetString(baseBlock, "Model", profile.Model);
			profile.AttackType = GetString(baseBlock, "AttackType", profile.AttackType);
			profile.Icon = GetString(baseBlock, "Icon", profile.Icon);
			profile.Background = GetString(baseBlock, "Background", profile.Background);
			profile.UnitOnMap = GetString(baseBlock, "UnitOnMap", profile.UnitOnMap);
			profile.PortraitBackgroundColor = GetString(baseBlock, "PortraitBackgroundColor", profile.PortraitBackgroundColor);
			string cinematicTagsRaw = GetString(baseBlock, "cinematicTags", null);
			if (cinematicTagsRaw != null)
			{
				profile.CinematicTags.Clear();
				foreach (string part in cinematicTagsRaw.Split('|'))
				{
					string trimmed = part.Trim();
					if (trimmed.Length > 0)
					{
						profile.CinematicTags.Add(trimmed);
					}
				}
			}
			profile.DefaultSkill = GetString(baseBlock, "defaultSkill", profile.DefaultSkill);

			KeyValue skillsBlock = baseBlock["skills"];
			if (skillsBlock != null)
			{
				profile.Skills.Clear();
				foreach (KeyValue skill in skillsBlock.Children)
				{
					profile.Skills.Add(skill.GetString());
				}
			}

			KeyValue progressionBlock = baseBlock["skillProgression"];
			if (progressionBlock != null)
			{
				profile.SkillProgression.Clear();
				foreach (KeyValue levelBlock in progressionBlock.Children)
				{
					if (!int.TryParse(levelBlock.Key, out int progressionLevel))
					{
						continue;
					}
					LevelSkillEntry entry = new LevelSkillEntry { Level = progressionLevel };
					foreach (KeyValue skill in levelBlock.Children)
					{
						entry.SkillIds.Add(skill.GetString());
					}
					profile.SkillProgression.Add(entry);
				}
			}

			KeyValue soundConfigBlock = baseBlock["soundConfig"];
			if (soundConfigBlock != null)
			{
				profile.SoundAssetId = GetString(soundConfigBlock, "assetId", profile.SoundAssetId);
				KeyValue soundsBlock = soundConfigBlock["sounds"];
				if (soundsBlock != null)
				{
					profile.SoundClips.Clear();
					foreach (KeyValue sound in soundsBlock.Children)
					{
						profile.SoundClips[sound.Key] = sound.GetString();
					}
				}
			}

			profile.States.Clear();
			KeyValue statesBlock = baseBlock["states"];
			if (statesBlock != null)
			{
				foreach (KeyValue state in statesBlock.Children)
				{
					profile.States[state.Key] = state.GetInt() != 0;
				}
			}

			ParseLevelChain(baseBlock, blocksByKey, profile);
		}

		/// <summary>The block with a "UniqueId" field is always the chain's base (level 1) -- every other block in a real multi-rank file (Onagro's Lvl2/Lvl3) only carries InheritsFrom/nextLevelArchetype/stats, no identity fields. Falls back to the first root if none has one (shouldn't happen for a well-formed file).</summary>
		private static KeyValue FindBaseBlock(KeyValue[] roots)
		{
			foreach (KeyValue root in roots)
			{
				if (root["UniqueId"] != null)
				{
					return root;
				}
			}
			return roots[0];
		}

		/// <summary>Walks nextLevelArchetype from baseBlock, one CharacterLevel per block visited, reading only that block's own "stats" (level 1 gets every field, level 2+ only whatever it explicitly overrides -- exactly what's already in the file, not re-derived).</summary>
		private static void ParseLevelChain(KeyValue baseBlock, Dictionary<string, KeyValue> blocksByKey, CharacterProfile profile)
		{
			profile.Levels.Clear();
			KeyValue currentBlock = baseBlock;
			int levelNumber = 1;
			HashSet<string> visitedKeys = new HashSet<string>();
			while (currentBlock != null && visitedKeys.Add(currentBlock.Key))
			{
				CharacterLevel level = new CharacterLevel { Level = levelNumber };
				KeyValue statsBlock = currentBlock["stats"];
				if (statsBlock != null)
				{
					foreach (KeyValue stat in statsBlock.Children)
					{
						level.Stats.Add(new StatEntry { Name = stat.Key, Value = stat.GetFloat() });
					}
				}
				profile.Levels.Add(level);

				KeyValue nextField = currentBlock["nextLevelArchetype"];
				string nextKey = nextField != null ? nextField.GetString() : null;
				currentBlock = nextKey != null && blocksByKey.TryGetValue(nextKey, out KeyValue nextBlock) ? nextBlock : null;
				levelNumber++;
			}
			if (profile.Levels.Count == 0)
			{
				profile.Levels.Add(new CharacterLevel { Level = 1 });
			}
		}

		/// <summary>Reads a top-level string field from block, or fallback if the key isn't present.</summary>
		private static string GetString(KeyValue block, string key, string fallback)
		{
			KeyValue field = block[key];
			return field != null ? field.GetString() : fallback;
		}
	}
}
