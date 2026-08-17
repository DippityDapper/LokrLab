using System.Collections.Generic;
using System.IO;
using System.Linq;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Owns every field-level mutation of the active character's CharacterProfile, and the PersistAndSync write-through every mutation goes through.</summary>
	/// <remarks>
	/// Extracted from HomeWorkstationScene (pre-redesign audit P2 "SRP / god classes" target
	/// split, item 2 of 4 -- "CharacterProfileService: persistence + sync, replaces PersistAndSync
	/// sprawl") -- these ~40 methods (SetName/SetTier/AddSkill/AddStat/... one per profile field or
	/// list operation) are a single, self-contained concern distinct from HomeWorkstationScene's
	/// remaining job (building the Home screen, owning which character is active and the
	/// recent-characters list, driving reload). Every method here reads/writes CharacterSession.Profile
	/// directly rather than through HomeWorkstationScene's own CurrentProfile property, since that
	/// property's setter is private to HomeWorkstationScene -- going straight to CharacterSession
	/// avoids needing a second, wider access path opened up just for this move.
	///
	/// HomeWorkstationScene's own SetX/AddX/RemoveX method names all still exist, now as thin
	/// forwards onto this class -- every Properties panel (CharacterGeneralPanel,
	/// CharacterSkillsPanel, CharacterLevelsPanel, ...) still calls
	/// HomeWorkstationScene.SetName/AddSkill/etc. exactly as before, so none of those ~15 panel
	/// files needed to change. This mirrors the same forwarding pattern CharacterSession's own
	/// extraction already used for CurrentCharacterFolder/CurrentProfile/CurrentEditingLevel.
	/// </remarks>
	internal static class CharacterProfileService
	{
		/// <summary>Sets the current character's name and persists the change.</summary>
		internal static void SetName(string name)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Name = name ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's description and persists the change.</summary>
		internal static void SetDescription(string description)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Description = description ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets whether this character is a playable Hero or a non-playable Enemy/Summon unit, and persists the change. Switching to EnemySummon deletes any existing roster.json via RLHeroesGenerator.Sync's own EntityType branch, so no stale roster entry lingers.</summary>
		internal static void SetEntityType(CharacterEntityType entityType)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.EntityType = entityType;
			PersistAndSync();
		}

		/// <summary>Sets which HeroRosterManager list this character belongs in and persists the change.</summary>
		internal static void SetTier(CharacterTier tier)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Tier = tier;
			PersistAndSync();
		}

		/// <summary>Sets the current character's locked state and persists the change.</summary>
		internal static void SetLocked(bool locked)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Locked = locked;
			PersistAndSync();
		}

		/// <summary>Sets the current character's unlock-achievement id and persists the change.</summary>
		internal static void SetUnlockAchievement(string unlockAchievement)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.UnlockAchievement = unlockAchievement ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "Model" KV field.</summary>
		internal static void SetModel(string model)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Model = model ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "AttackType" KV field.</summary>
		internal static void SetAttackType(string attackType)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.AttackType = attackType ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "Icon" KV field.</summary>
		internal static void SetIcon(string icon)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Icon = icon ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "Background" KV field.</summary>
		internal static void SetBackground(string background)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.Background = background ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "UnitOnMap" KV field.</summary>
		internal static void SetUnitOnMap(string unitOnMap)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.UnitOnMap = unitOnMap ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "PortraitBackgroundColor" KV field.</summary>
		internal static void SetPortraitBackgroundColor(string hexColor)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.PortraitBackgroundColor = hexColor ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Adds a cinematic tag unless the name is blank or already in the list.</summary>
		internal static void AddCinematicTag(string tag)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(tag))
			{
				return;
			}
			string trimmed = tag.Trim();
			if (CharacterSession.Profile.CinematicTags.Contains(trimmed))
			{
				return;
			}
			CharacterSession.Profile.CinematicTags.Add(trimmed);
			PersistAndSync();
		}

		/// <summary>Removes a cinematic tag from the list.</summary>
		internal static void RemoveCinematicTag(string tag)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.CinematicTags.Remove(tag);
			PersistAndSync();
		}

		/// <summary>Adds a skill id to the end of the current character's base-level "skills" block.</summary>
		internal static void AddSkill(string skillId)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(skillId))
			{
				return;
			}
			CharacterSession.Profile.Skills.Add(skillId.Trim());
			PersistAndSync();
		}

		/// <summary>Removes one skill id from the base-level "skills" block by list index.</summary>
		internal static void RemoveSkillAt(int index)
		{
			if (CharacterSession.Profile == null || index < 0 || index >= CharacterSession.Profile.Skills.Count)
			{
				return;
			}
			string removed = CharacterSession.Profile.Skills[index];
			CharacterSession.Profile.Skills.RemoveAt(index);
			if (CharacterSession.Profile.DefaultSkill == removed)
			{
				SyncDefaultSkillWithProgression();
			}
			PersistAndSync();
		}

		/// <summary>Replaces one skill id in the base-level "skills" block by list index.</summary>
		internal static void SetSkillAt(int index, string skillId)
		{
			if (CharacterSession.Profile == null || index < 0 || index >= CharacterSession.Profile.Skills.Count)
			{
				return;
			}
			string trimmed = skillId?.Trim() ?? string.Empty;
			CharacterSession.Profile.Skills[index] = trimmed;
			PersistAndSync();
		}

		/// <summary>Skill ids valid for defaultSkill (basic attack). Excludes passive traits and skillProgression unlocks.</summary>
		internal static List<string> GetDefaultSkillCandidates()
		{
			List<string> candidates = new List<string>();
			if (CharacterSession.Profile == null)
			{
				return candidates;
			}

			HashSet<string> seen = new HashSet<string>();
			HashSet<string> traits = new HashSet<string>(CharacterSession.Profile.Skills.Where(skillId => !string.IsNullOrEmpty(skillId)));
			HashSet<string> progressionSkills = GetSkillProgressionSkillIds(CharacterSession.Profile);

			void TryAdd(string skillId)
			{
				if (string.IsNullOrEmpty(skillId) || traits.Contains(skillId) || progressionSkills.Contains(skillId) || !seen.Add(skillId))
				{
					return;
				}
				candidates.Add(skillId);
			}

			TryAdd(CharacterSession.Profile.DefaultSkill);
			foreach (string skillId in CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SkillIds))
			{
				TryAdd(skillId);
			}

			return candidates;
		}

		private static HashSet<string> GetSkillProgressionSkillIds(CharacterProfile profile)
		{
			HashSet<string> ids = new HashSet<string>();
			foreach (LevelSkillEntry entry in profile.SkillProgression)
			{
				foreach (string skillId in entry.SkillIds)
				{
					if (!string.IsNullOrEmpty(skillId))
					{
						ids.Add(skillId);
					}
				}
			}
			return ids;
		}

		private static bool IsDefaultSkillConflictingWithProgression(CharacterProfile profile)
		{
			return !string.IsNullOrEmpty(profile.DefaultSkill)
				&& GetSkillProgressionSkillIds(profile).Contains(profile.DefaultSkill);
		}

		/// <summary>Sets the current character's "defaultSkill" KV field (basic attack, separate from skillProgression).</summary>
		internal static void SetDefaultSkill(string defaultSkill)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			string trimmed = defaultSkill ?? string.Empty;
			if (trimmed.Length > 0 && !GetDefaultSkillCandidates().Contains(trimmed))
			{
				return;
			}
			CharacterSession.Profile.DefaultSkill = trimmed;
			PersistAndSync();
		}

		private static void SyncDefaultSkillWithProgression()
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}

			List<string> candidates = GetDefaultSkillCandidates();
			if (candidates.Count == 0)
			{
				CharacterSession.Profile.DefaultSkill = string.Empty;
				return;
			}

			if (string.IsNullOrEmpty(CharacterSession.Profile.DefaultSkill)
				|| !candidates.Contains(CharacterSession.Profile.DefaultSkill)
				|| IsDefaultSkillConflictingWithProgression(CharacterSession.Profile))
			{
				CharacterSession.Profile.DefaultSkill = candidates[0];
			}
		}

		/// <summary>Fixes defaultSkill when it is empty, unknown, or duplicates a skillProgression pick.</summary>
		internal static void EnsureDefaultSkillValid()
		{
			SyncDefaultSkillWithProgression();
		}

		/// <summary>Adds an empty skillProgression entry for the given level. A no-op if that level already exists.</summary>
		internal static void AddSkillProgressionEntry(int level)
		{
			if (CharacterSession.Profile == null || level <= 0)
			{
				return;
			}
			if (CharacterSession.Profile.SkillProgression.Exists(entry => entry.Level == level))
			{
				return;
			}
			CharacterSession.Profile.SkillProgression.Add(new LevelSkillEntry { Level = level, SkillIds = new List<string>() });
			CharacterSession.Profile.SkillProgression.Sort((a, b) => a.Level.CompareTo(b.Level));
			PersistAndSync();
		}

		/// <summary>Removes a skillProgression entry and all skill ids at that level.</summary>
		internal static void RemoveSkillProgressionEntry(int level)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			LevelSkillEntry entry = CharacterSession.Profile.SkillProgression.Find(e => e.Level == level);
			if (entry == null)
			{
				return;
			}
			CharacterSession.Profile.SkillProgression.Remove(entry);
			PersistAndSync();
		}

		/// <summary>Changes the level key on one skillProgression entry.</summary>
		internal static void SetSkillProgressionEntryLevel(int oldLevel, int newLevel)
		{
			if (CharacterSession.Profile == null || oldLevel == newLevel || newLevel <= 0)
			{
				return;
			}
			LevelSkillEntry entry = CharacterSession.Profile.SkillProgression.Find(e => e.Level == oldLevel);
			if (entry == null)
			{
				return;
			}
			if (CharacterSession.Profile.SkillProgression.Exists(e => e.Level == newLevel))
			{
				return;
			}
			entry.Level = newLevel;
			CharacterSession.Profile.SkillProgression.Sort((a, b) => a.Level.CompareTo(b.Level));
			PersistAndSync();
		}

		/// <summary>Adds a skill id to one skillProgression level's grant list.</summary>
		internal static void AddSkillProgressionSkill(int level, string skillId)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(skillId))
			{
				return;
			}
			LevelSkillEntry entry = CharacterSession.Profile.SkillProgression.Find(e => e.Level == level);
			if (entry == null)
			{
				return;
			}
			entry.SkillIds.Add(skillId.Trim());
			PersistAndSync();
		}

		/// <summary>Removes one skill id from a skillProgression level by list index.</summary>
		internal static void RemoveSkillProgressionSkillAt(int level, int skillIndex)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			LevelSkillEntry entry = CharacterSession.Profile.SkillProgression.Find(e => e.Level == level);
			if (entry == null || skillIndex < 0 || skillIndex >= entry.SkillIds.Count)
			{
				return;
			}
			entry.SkillIds.RemoveAt(skillIndex);
			PersistAndSync();
		}

		/// <summary>Replaces one skill id on a skillProgression level by list index.</summary>
		internal static void SetSkillProgressionSkillAt(int level, int skillIndex, string skillId)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			LevelSkillEntry entry = CharacterSession.Profile.SkillProgression.Find(e => e.Level == level);
			if (entry == null || skillIndex < 0 || skillIndex >= entry.SkillIds.Count)
			{
				return;
			}
			entry.SkillIds[skillIndex] = skillId?.Trim() ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Adds an empty locale entry, unless it's already tracked or not a recognized non-English locale.</summary>
		internal static void AddLocalization(string locale)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(locale) || CharacterSession.Profile.Localizations.ContainsKey(locale))
			{
				return;
			}
			CharacterSession.Profile.Localizations[locale] = new CharacterLocalizedText();
			PersistAndSync();
		}

		/// <summary>Removes a locale entry, deleting its localization_&lt;locale&gt;.txt file so a stale translation doesn't linger on disk.</summary>
		internal static void RemoveLocalization(string locale)
		{
			if (CharacterSession.Profile == null || !CharacterSession.Profile.Localizations.Remove(locale))
			{
				return;
			}
			string path = Path.Combine(CharacterSession.Folder, "localization_" + locale + ".txt");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			PersistAndSync();
		}

		/// <summary>Sets a locale entry's Name.</summary>
		internal static void SetLocalizationName(string locale, string name)
		{
			if (CharacterSession.Profile == null || !CharacterSession.Profile.Localizations.TryGetValue(locale, out CharacterLocalizedText entry))
			{
				return;
			}
			entry.Name = name ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets a locale entry's Description.</summary>
		internal static void SetLocalizationDescription(string locale, string description)
		{
			if (CharacterSession.Profile == null || !CharacterSession.Profile.Localizations.TryGetValue(locale, out CharacterLocalizedText entry))
			{
				return;
			}
			entry.Description = description ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Sets the current character's "soundConfig" block's "assetId" field.</summary>
		internal static void SetSoundAssetId(string assetId)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.SoundAssetId = assetId ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Adds or updates one entry in the current character's <c>soundConfig.sounds</c> block.</summary>
		internal static void AddSoundClip(string eventName, string clipId)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(eventName))
			{
				return;
			}
			string key = eventName.Trim();
			CharacterSession.Profile.SoundClips[key] = clipId?.Trim() ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Removes one entry from the current character's <c>soundConfig.sounds</c> block.</summary>
		internal static void RemoveSoundClip(string eventName)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.SoundClips.Remove(eventName);
			PersistAndSync();
		}

		/// <summary>Renames a sound event key while preserving its clip id.</summary>
		internal static void RenameSoundClip(string oldEvent, string newEvent)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(newEvent) || !CharacterSession.Profile.SoundClips.ContainsKey(oldEvent))
			{
				return;
			}
			string trimmed = newEvent.Trim();
			if (trimmed == oldEvent)
			{
				return;
			}
			string clip = CharacterSession.Profile.SoundClips[oldEvent];
			CharacterSession.Profile.SoundClips.Remove(oldEvent);
			CharacterSession.Profile.SoundClips[trimmed] = clip;
			PersistAndSync();
		}

		/// <summary>Sets the clip id for an existing sound event key.</summary>
		internal static void SetSoundClipValue(string eventName, string clipId)
		{
			if (CharacterSession.Profile == null || !CharacterSession.Profile.SoundClips.ContainsKey(eventName))
			{
				return;
			}
			CharacterSession.Profile.SoundClips[eventName] = clipId ?? string.Empty;
			PersistAndSync();
		}

		/// <summary>Adds a new top rank to the archetype chain (one past the current highest), seeded with just a "level" stat, and selects it.</summary>
		internal static void AddLevel()
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			int nextLevel = 1;
			foreach (CharacterLevel existing in CharacterSession.Profile.Levels)
			{
				if (existing.Level >= nextLevel)
				{
					nextLevel = existing.Level + 1;
				}
			}
			CharacterSession.Profile.Levels.Add(new CharacterLevel { Level = nextLevel, Stats = new List<StatEntry> { new StatEntry { Name = "level", Value = nextLevel } } });
			CharacterSession.SetEditingLevel(nextLevel);
			PersistAndSync();
		}

		/// <summary>Removes a rank from the archetype chain (never the last remaining one), then renumbers the rest sequentially so the chain stays contiguous.</summary>
		internal static void RemoveLevel(int level)
		{
			if (CharacterSession.Profile == null || CharacterSession.Profile.Levels.Count <= 1)
			{
				return;
			}
			CharacterSession.Profile.Levels.RemoveAll(existing => existing.Level == level);
			RenumberLevels();
			CharacterSession.SetEditingLevel(CharacterSession.Profile.Levels[0].Level);
			PersistAndSync();
		}

		/// <summary>Renumbers Levels to 1..N in list order, keeping each entry's own "level" stat (if it has one) in sync.</summary>
		private static void RenumberLevels()
		{
			for (int i = 0; i < CharacterSession.Profile.Levels.Count; i++)
			{
				int newLevel = i + 1;
				CharacterLevel entry = CharacterSession.Profile.Levels[i];
				StatEntry levelStat = entry.Stats.Find(stat => stat.Name == "level");
				if (levelStat != null)
				{
					levelStat.Value = newLevel;
				}
				entry.Level = newLevel;
			}
		}

		/// <summary>Adds a new stat to the given rank with a value of 0, unless the name is blank or already used on that rank.</summary>
		internal static void AddStat(int level, string name)
		{
			CharacterLevel entry = FindLevel(level);
			if (entry == null || string.IsNullOrWhiteSpace(name) || FindStat(entry, name) != null)
			{
				return;
			}
			entry.Stats.Add(new StatEntry { Name = name, Value = 0f });
			PersistAndSync();
		}

		/// <summary>Renames a stat on the given rank, unless the new name is blank, unchanged, or already used by another stat on that same rank.</summary>
		internal static void RenameStat(int level, string oldName, string newName)
		{
			CharacterLevel entry = FindLevel(level);
			StatEntry stat = entry != null ? FindStat(entry, oldName) : null;
			if (stat == null || string.IsNullOrWhiteSpace(newName) || newName == oldName || FindStat(entry, newName) != null)
			{
				return;
			}
			stat.Name = newName;
			PersistAndSync();
		}

		/// <summary>Sets a stat's value on the given rank.</summary>
		internal static void SetStatValue(int level, string name, float value)
		{
			CharacterLevel entry = FindLevel(level);
			StatEntry stat = entry != null ? FindStat(entry, name) : null;
			if (stat == null)
			{
				return;
			}
			stat.Value = value;
			PersistAndSync();
		}

		/// <summary>Removes a stat entirely from the given rank.</summary>
		internal static void RemoveStat(int level, string name)
		{
			CharacterLevel entry = FindLevel(level);
			if (entry == null)
			{
				return;
			}
			entry.Stats.RemoveAll(stat => stat.Name == name);
			PersistAndSync();
		}

		/// <summary>Adds a new state flag, defaulted on, unless the name is blank or already tracked.</summary>
		internal static void AddState(string name)
		{
			if (CharacterSession.Profile == null || string.IsNullOrWhiteSpace(name) || CharacterSession.Profile.States.ContainsKey(name))
			{
				return;
			}
			CharacterSession.Profile.States[name] = true;
			PersistAndSync();
		}

		/// <summary>Toggles a tracked state flag on/off.</summary>
		internal static void SetState(string name, bool on)
		{
			if (CharacterSession.Profile == null || !CharacterSession.Profile.States.ContainsKey(name))
			{
				return;
			}
			CharacterSession.Profile.States[name] = on;
			PersistAndSync();
		}

		/// <summary>Stops tracking a state flag entirely.</summary>
		internal static void RemoveState(string name)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			CharacterSession.Profile.States.Remove(name);
			PersistAndSync();
		}

		/// <summary>Copies sourcePath into this character's Portraits/&lt;id&gt;/&lt;id&gt;_&lt;slot&gt;.png slot, overwriting any existing file there. A no-op if sourcePath doesn't exist (e.g. the file browser was cancelled).</summary>
		internal static void SetPortrait(string slot, string sourcePath)
		{
			if (CharacterSession.Profile == null || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
			{
				return;
			}
			string destPath = CharacterPortraitsPanel.SlotPath(CharacterSession.Profile.Id, slot);
			Directory.CreateDirectory(Path.GetDirectoryName(destPath));
			File.Copy(sourcePath, destPath, overwrite: true);
			CharacterPortraitsPanel.Refresh(CharacterSession.Profile);
			HomeWorkstationScene.RefreshReadinessChecklist();
		}

		/// <summary>Deletes this character's Portraits/&lt;id&gt;/&lt;id&gt;_&lt;slot&gt;.png slot, if set.</summary>
		internal static void RemovePortrait(string slot)
		{
			if (CharacterSession.Profile == null)
			{
				return;
			}
			string path = CharacterPortraitsPanel.SlotPath(CharacterSession.Profile.Id, slot);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			CharacterPortraitsPanel.Refresh(CharacterSession.Profile);
			HomeWorkstationScene.RefreshReadinessChecklist();
		}

		/// <summary>Finds the archetype rank entry for the given level, or null if none exists. Also used by HomeWorkstationScene.SelectLevel to validate a level exists before switching to it.</summary>
		internal static CharacterLevel FindLevel(int level)
		{
			return CharacterSession.Profile?.Levels.Find(entry => entry.Level == level);
		}

		private static StatEntry FindStat(CharacterLevel level, string name)
		{
			return level.Stats.Find(stat => stat.Name == name);
		}

		/// <summary>Writes character.json and generated game files without refreshing Properties UI.</summary>
		/// <remarks>
		/// Close Lab resets Properties widgets before reload. PersistAndSync's RefreshAll is a
		/// no-op then, but skipping it keeps teardown from touching destroyed inspector hosts.
		/// </remarks>
		internal static void PersistToDisk()
		{
			if (CharacterSession.Profile == null || string.IsNullOrEmpty(CharacterSession.Folder))
			{
				return;
			}

			EnsureDefaultSkillValid();
			CharacterProfileSidecar.Save(CharacterSession.Folder, CharacterSession.Profile);
			RLHeroesGenerator.Sync(CharacterSession.Folder, CharacterSession.Profile);
		}

		/// <summary>Writes the current profile to character.json and regenerates rlheroes.txt/roster.json/localization, then refreshes every panel that shows profile data.</summary>
		/// <remarks>internal (not private) so HomeWorkstationScene.PersistCurrentCharacter can call it directly for a persist-without-any-field-change request.</remarks>
		internal static void PersistAndSync()
		{
			EnsureDefaultSkillValid();
			CharacterProfileSidecar.Save(CharacterSession.Folder, CharacterSession.Profile);
			RLHeroesGenerator.Sync(CharacterSession.Folder, CharacterSession.Profile);
			PropertiesWorkstationScene.RefreshAll(CharacterSession.Profile);
			HomeWorkstationScene.RefreshReadinessChecklist();
		}
	}
}
