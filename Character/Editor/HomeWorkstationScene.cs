using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LokrLab.Editor.General;
using LokrLab.Projects;
using LokrCharacterLoader;
using SimpleUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Home workstation's own orchestrator -- the hub every other workstation (Properties, the Rig Editor, and Load) is reached from. Also the single owner of "which character is currently active" state, shared by Load/Properties/the Animator hand-off.</summary>
	/// <remarks>Field-level CharacterProfile mutation (SetName/AddSkill/AddStat/... — the ~40 methods a Properties panel calls per field/list operation) and the PersistAndSync write-through they all share moved to CharacterProfileService 2026-08-12 (pre-redesign audit P2) — this class's own remaining job is building the Home screen, owning which character is active (via CharacterSession) and the recent-characters list, and driving reload. Every method Properties panels called before still exists here under the same name, now as a thin forward — see CharacterProfileService's own doc comment for why.</remarks>
	internal static class HomeWorkstationScene
	{
		/// <summary>The folder of the currently active character, or null if none is loaded.</summary>
		/// <remarks>Thin forward onto CharacterSession.Folder (extracted 2026-08-12, pre-redesign audit P2) -- kept here, under the same name, so no other file's read of this property needed to change; the state itself now lives in a class that isn't tied to the Home workstation's own UI orchestration.</remarks>
		internal static string CurrentCharacterFolder
		{
			get => CharacterSession.Folder;
			private set => CharacterSession.SetFolder(value);
		}
		/// <summary>The currently active character's profile, or null if none is loaded.</summary>
		/// <remarks>Thin forward onto CharacterSession.Profile -- see CurrentCharacterFolder's own remarks.</remarks>
		internal static CharacterProfile CurrentProfile
		{
			get => CharacterSession.Profile;
			private set => CharacterSession.SetProfile(value);
		}

		private static readonly List<string> recentCharacters = new List<string>();
		/// <summary>The folders of recently created or loaded characters, most recent first.</summary>
		internal static IReadOnlyList<string> RecentCharacters => recentCharacters;

		private static UiLabel statusLabel;

		/// <summary>Builds the Home screen's status label and panels, and loads recent characters.</summary>
		internal static void Build(Scene scene, Transform screenRoot)
		{
			CharacterLabPaths.EnsureFoldersExist();
			CharacterReadinessRegistry.RegisterDefaults();

			recentCharacters.Clear();
			recentCharacters.AddRange(RecentFilesStore.Load());

			statusLabel = UiLabel.Create(screenRoot, "", fontSize: 14, alignment: TextAnchor.MiddleCenter).Name("HomeStatus");
			statusLabel.RectTransform.anchorMin = new Vector2(0.5f, 0.12f);
			statusLabel.RectTransform.anchorMax = new Vector2(0.5f, 0.12f);
			statusLabel.RectTransform.sizeDelta = new Vector2(1600f, 30f);
			statusLabel.RectTransform.anchoredPosition = Vector2.zero;

			Transform contentRoot = Lab.GetWorkstationContentRoot(screenRoot);
			HomeNavPanel.Build(contentRoot, Lab.DefaultFont);
			ReadinessChecklistPanel.Build(contentRoot, Lab.DefaultFont);

			RefreshCurrentCharacterPanels();
		}

		/// <summary>Drops Home chrome refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			statusLabel = null;
		}

		/// <summary>Updates the Home status label and mirrors the message to the log.</summary>
		internal static void SetStatus(string message)
		{
			if (statusLabel != null && statusLabel.GameObject != null)
			{
				statusLabel.SetText(message);
			}
			LokrCharacterLabPlugin.Log.LogInfo("HomeWorkstationScene: " + message);
		}

		/// <summary>Scaffolds every file a brand-new character needs upfront (never lazily). Also the exact path LegacyModImporter reuses, so an imported character is created through the same code as a manually-created one.</summary>
		/// <remarks>The Project Browser / Load create sheet stores a <see cref="CharacterCreateRequest"/> on <see cref="CharacterCreateSheet.Pending"/> first (name, slug, alias, description, role). Legacy import calls this with no pending request and gets a blank unnamed slate, then overwrites from the old mod.</remarks>
		internal static void OnCreateCharacterConfirmed()
		{
			CharacterCreateRequest request = CharacterCreateSheet.Pending;
			CharacterCreateSheet.ClearPending();
			CreateCharacter(request);
		}

		/// <summary>Writes the character folder, placeholder rig/portraits/skills, character.json, and generated game files, then selects the new character.</summary>
		internal static void CreateCharacter(CharacterCreateRequest request)
		{
			CharacterLabPaths.EnsureFoldersExist();
			string slug = request != null && !string.IsNullOrEmpty(request.Slug)
				? request.Slug
				: LabSlugIds.LegalizeSlug(request != null ? request.Name : null, "character");
			string id = CharacterLabPaths.GenerateNewCharacterId(slug);
			string folder = ScaffoldCharacterFolder(id);
			CharacterPlaceholders.WritePlaceholderVisuals(folder, id);

			CharacterProfile profile = new CharacterProfile { Id = id };
			if (request != null)
			{
				profile.Name = request.Name ?? string.Empty;
				profile.Description = request.Description ?? string.Empty;
				profile.EntityType = request.EntityType;
				profile.Tier = request.Tier;
			}

			CharacterPlaceholders.ApplyToNewProfile(profile);
			CharacterProfileSidecar.Save(folder, profile);
			string alias = request != null && !string.IsNullOrEmpty(request.Alias) ? request.Alias : slug;
			LokrCharacterLoader.LabAliases.SeedSelf(folder, alias, id);
			RLHeroesGenerator.Sync(folder, profile);

			AddRecentCharacter(folder);
			CurrentCharacterFolder = folder;
			CurrentProfile = profile;
			CurrentEditingLevel = 1;
			RefreshCurrentCharacterPanels();
			string shown = string.IsNullOrEmpty(profile.Name) ? id : profile.Name;
			SetStatus("Created new character '" + shown + "'.");
		}

		/// <summary>Creates CharactersRoot/&lt;id&gt;/ with the subfolders every character uses (rig, sprites, definition, sounds, portraits). Does not write files, select the character, or add it to recents.</summary>
		internal static string ScaffoldCharacterFolder(string id)
		{
			string folder = Path.Combine(CharacterLabPaths.CharactersRoot, id);
			Directory.CreateDirectory(Path.Combine(folder, "rig"));
			Directory.CreateDirectory(Path.Combine(folder, "sprites"));
			Directory.CreateDirectory(Path.Combine(folder, "definition"));
			Directory.CreateDirectory(CharacterLabPaths.CharacterSoundsFolder(id));
			Directory.CreateDirectory(CharacterLabPaths.CharacterPortraitsFolder(id));
			return folder;
		}

		/// <summary>Loads an existing character from the given folder and makes it the current character.</summary>
		internal static void OnLoadCharacterSelected(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				SetStatus("Folder does not exist: " + folder);
				return;
			}
			string previousFolder = folder;
			folder = CharacterIdentityRekey.ApplyIfLegacyNamedFolder(folder);
			if (!string.Equals(previousFolder, folder, System.StringComparison.OrdinalIgnoreCase))
			{
				recentCharacters.RemoveAll(f => string.Equals(f, previousFolder, System.StringComparison.OrdinalIgnoreCase));
			}
			CurrentProfile = CharacterProfileSidecar.Load(folder);
			string healed = VanillaCharacterExtract.HealLoadedOverride(folder, CurrentProfile);
			if (!string.Equals(folder, healed, System.StringComparison.Ordinal))
			{
				recentCharacters.RemoveAll(f => string.Equals(f, folder, System.StringComparison.OrdinalIgnoreCase));
				folder = healed;
			}
			CurrentCharacterFolder = folder;
			CurrentEditingLevel = 1;
			AddRecentCharacter(folder);
			RefreshCurrentCharacterPanels();
			SetStatus("Loaded character '" + CurrentProfile.Id + "'.");
		}

		/// <summary>Sets the current character's name and persists the change.</summary>
		internal static void SetName(string name) => CharacterProfileService.SetName(name);

		/// <summary>Sets the current character's description and persists the change.</summary>
		internal static void SetDescription(string description) => CharacterProfileService.SetDescription(description);

		/// <summary>Sets whether this character is a playable Hero or a non-playable Enemy/Summon unit, and persists the change.</summary>
		internal static void SetEntityType(CharacterEntityType entityType) => CharacterProfileService.SetEntityType(entityType);

		/// <summary>Sets which HeroRosterManager list this character belongs in and persists the change.</summary>
		internal static void SetTier(CharacterTier tier) => CharacterProfileService.SetTier(tier);

		/// <summary>Sets the current character's locked state and persists the change.</summary>
		internal static void SetLocked(bool locked) => CharacterProfileService.SetLocked(locked);

		/// <summary>Sets the current character's unlock-achievement id and persists the change.</summary>
		internal static void SetUnlockAchievement(string unlockAchievement) => CharacterProfileService.SetUnlockAchievement(unlockAchievement);

		/// <summary>Sets the current character's "Model" KV field.</summary>
		internal static void SetModel(string model) => CharacterProfileService.SetModel(model);

		/// <summary>Sets the current character's "AttackType" KV field.</summary>
		internal static void SetAttackType(string attackType) => CharacterProfileService.SetAttackType(attackType);

		/// <summary>Sets the current character's "Icon" KV field.</summary>
		internal static void SetIcon(string icon) => CharacterProfileService.SetIcon(icon);

		/// <summary>Sets the current character's "Background" KV field.</summary>
		internal static void SetBackground(string background) => CharacterProfileService.SetBackground(background);

		/// <summary>Sets the current character's "UnitOnMap" KV field.</summary>
		internal static void SetUnitOnMap(string unitOnMap) => CharacterProfileService.SetUnitOnMap(unitOnMap);

		/// <summary>Sets the current character's "PortraitBackgroundColor" KV field.</summary>
		internal static void SetPortraitBackgroundColor(string hexColor) => CharacterProfileService.SetPortraitBackgroundColor(hexColor);

		/// <summary>Adds a cinematic tag unless the name is blank or already in the list.</summary>
		internal static void AddCinematicTag(string tag) => CharacterProfileService.AddCinematicTag(tag);

		/// <summary>Removes a cinematic tag from the list.</summary>
		internal static void RemoveCinematicTag(string tag) => CharacterProfileService.RemoveCinematicTag(tag);

		/// <summary>Adds a skill id to the end of the current character's base-level "skills" block.</summary>
		internal static void AddSkill(string skillId) => CharacterProfileService.AddSkill(skillId);

		/// <summary>Removes one skill id from the base-level "skills" block by list index.</summary>
		internal static void RemoveSkillAt(int index) => CharacterProfileService.RemoveSkillAt(index);

		/// <summary>Replaces one skill id in the base-level "skills" block by list index.</summary>
		internal static void SetSkillAt(int index, string skillId) => CharacterProfileService.SetSkillAt(index, skillId);

		/// <summary>Skill ids valid for defaultSkill (basic attack). Excludes passive traits and skillProgression unlocks.</summary>
		internal static List<string> GetDefaultSkillCandidates() => CharacterProfileService.GetDefaultSkillCandidates();

		/// <summary>Sets the current character's "defaultSkill" KV field (basic attack, separate from skillProgression).</summary>
		internal static void SetDefaultSkill(string defaultSkill) => CharacterProfileService.SetDefaultSkill(defaultSkill);

		/// <summary>Fixes defaultSkill when it is empty, unknown, or duplicates a skillProgression pick.</summary>
		internal static void EnsureDefaultSkillValid() => CharacterProfileService.EnsureDefaultSkillValid();

		/// <summary>Adds an empty skillProgression entry for the given level. A no-op if that level already exists.</summary>
		internal static void AddSkillProgressionEntry(int level) => CharacterProfileService.AddSkillProgressionEntry(level);

		/// <summary>Removes a skillProgression entry and all skill ids at that level.</summary>
		internal static void RemoveSkillProgressionEntry(int level) => CharacterProfileService.RemoveSkillProgressionEntry(level);

		/// <summary>Changes the level key on one skillProgression entry.</summary>
		internal static void SetSkillProgressionEntryLevel(int oldLevel, int newLevel) => CharacterProfileService.SetSkillProgressionEntryLevel(oldLevel, newLevel);

		/// <summary>Adds a skill id to one skillProgression level's grant list.</summary>
		internal static void AddSkillProgressionSkill(int level, string skillId) => CharacterProfileService.AddSkillProgressionSkill(level, skillId);

		/// <summary>Removes one skill id from a skillProgression level by list index.</summary>
		internal static void RemoveSkillProgressionSkillAt(int level, int skillIndex) => CharacterProfileService.RemoveSkillProgressionSkillAt(level, skillIndex);

		/// <summary>Replaces one skill id on a skillProgression level by list index.</summary>
		internal static void SetSkillProgressionSkillAt(int level, int skillIndex, string skillId) => CharacterProfileService.SetSkillProgressionSkillAt(level, skillIndex, skillId);

		/// <summary>Adds an empty locale entry, unless it's already tracked or not a recognized non-English locale.</summary>
		internal static void AddLocalization(string locale) => CharacterProfileService.AddLocalization(locale);

		/// <summary>Removes a locale entry, deleting its localization_&lt;locale&gt;.txt file so a stale translation doesn't linger on disk.</summary>
		internal static void RemoveLocalization(string locale) => CharacterProfileService.RemoveLocalization(locale);

		/// <summary>Sets a locale entry's Name.</summary>
		internal static void SetLocalizationName(string locale, string name) => CharacterProfileService.SetLocalizationName(locale, name);

		/// <summary>Sets a locale entry's Description.</summary>
		internal static void SetLocalizationDescription(string locale, string description) => CharacterProfileService.SetLocalizationDescription(locale, description);

		/// <summary>Sets the current character's "soundConfig" block's "assetId" field.</summary>
		internal static void SetSoundAssetId(string assetId) => CharacterProfileService.SetSoundAssetId(assetId);

		/// <summary>Adds or updates one entry in the current character's <c>soundConfig.sounds</c> block.</summary>
		internal static void AddSoundClip(string eventName, string clipId) => CharacterProfileService.AddSoundClip(eventName, clipId);

		/// <summary>Removes one entry from the current character's <c>soundConfig.sounds</c> block.</summary>
		internal static void RemoveSoundClip(string eventName) => CharacterProfileService.RemoveSoundClip(eventName);

		/// <summary>Renames a sound event key while preserving its clip id.</summary>
		internal static void RenameSoundClip(string oldEvent, string newEvent) => CharacterProfileService.RenameSoundClip(oldEvent, newEvent);

		/// <summary>Sets the clip id for an existing sound event key.</summary>
		internal static void SetSoundClipValue(string eventName, string clipId) => CharacterProfileService.SetSoundClipValue(eventName, clipId);

		/// <summary>Which rank Level Properties is currently showing/editing. Reset to 1 whenever a different character loads.</summary>
		/// <remarks>Thin forward onto CharacterSession.EditingLevel (default 1, so no separate initializer is needed here) -- see CurrentCharacterFolder's own remarks.</remarks>
		internal static int CurrentEditingLevel
		{
			get => CharacterSession.EditingLevel;
			private set => CharacterSession.SetEditingLevel(value);
		}

		/// <summary>Switches which rank Level Properties shows, if it exists.</summary>
		internal static void SelectLevel(int level)
		{
			if (CharacterProfileService.FindLevel(level) == null)
			{
				return;
			}
			CurrentEditingLevel = level;
			CharacterLevelsPanel.Refresh(CurrentProfile);
		}

		/// <summary>Adds a new top rank to the archetype chain (one past the current highest), seeded with just a "level" stat, and selects it.</summary>
		internal static void AddLevel() => CharacterProfileService.AddLevel();

		/// <summary>Removes a rank from the archetype chain (never the last remaining one), then renumbers the rest sequentially so the chain stays contiguous.</summary>
		internal static void RemoveLevel(int level) => CharacterProfileService.RemoveLevel(level);

		/// <summary>Adds a new stat to the given rank with a value of 0, unless the name is blank or already used on that rank.</summary>
		internal static void AddStat(int level, string name) => CharacterProfileService.AddStat(level, name);

		/// <summary>Renames a stat on the given rank, unless the new name is blank, unchanged, or already used by another stat on that same rank.</summary>
		internal static void RenameStat(int level, string oldName, string newName) => CharacterProfileService.RenameStat(level, oldName, newName);

		/// <summary>Sets a stat's value on the given rank.</summary>
		internal static void SetStatValue(int level, string name, float value) => CharacterProfileService.SetStatValue(level, name, value);

		/// <summary>Removes a stat entirely from the given rank.</summary>
		internal static void RemoveStat(int level, string name) => CharacterProfileService.RemoveStat(level, name);

		/// <summary>Adds a new state flag, defaulted on, unless the name is blank or already tracked.</summary>
		internal static void AddState(string name) => CharacterProfileService.AddState(name);

		/// <summary>Toggles a tracked state flag on/off.</summary>
		internal static void SetState(string name, bool on) => CharacterProfileService.SetState(name, on);

		/// <summary>Stops tracking a state flag entirely.</summary>
		internal static void RemoveState(string name) => CharacterProfileService.RemoveState(name);

		/// <summary>Copies sourcePath into this character's Portraits/&lt;id&gt;/&lt;id&gt;_&lt;slot&gt;.png slot, overwriting any existing file there. A no-op if sourcePath doesn't exist (e.g. the file browser was cancelled).</summary>
		internal static void SetPortrait(string slot, string sourcePath) => CharacterProfileService.SetPortrait(slot, sourcePath);

		/// <summary>Deletes this character's Portraits/&lt;id&gt;/&lt;id&gt;_&lt;slot&gt;.png slot, if set.</summary>
		internal static void RemovePortrait(string slot) => CharacterProfileService.RemovePortrait(slot);

		/// <summary>Re-runs the readiness checklist for the current character and refreshes its panel.</summary>
		internal static void RefreshReadinessChecklist()
		{
			if (CurrentCharacterFolder == null || CurrentProfile == null)
			{
				ReadinessChecklistPanel.Refresh(new List<ReadinessItem>(), false);
				return;
			}
			List<ReadinessItem> items = CharacterReadinessRegistry.RunAll(CurrentCharacterFolder, CurrentProfile);
			ReadinessChecklistPanel.Refresh(items, true);
		}

		/// <summary>Switches to the Load workstation so the player can pick a different character.</summary>
		internal static void OnSwitchCharacterClicked()
		{
			Lab.SwitchToLoad();
		}

		/// <summary>Writes the current profile and generated game files to disk without reloading runtime caches.</summary>
		internal static void PersistCurrentCharacter()
		{
			if (CurrentProfile == null || string.IsNullOrEmpty(CurrentCharacterFolder))
			{
				return;
			}

			CharacterProfileService.PersistAndSync();
		}

		/// <summary>Re-reads the current character's on-disk files into the running game's content caches.</summary>
		internal static void OnReloadInGameClicked()
		{
			CharacterAPI.ReloadResult result = LabContentReloader.ReloadCurrentCharacter(persistFirst: true);
			if (!result.Success)
			{
				SetStatus(result.ErrorMessage ?? "Reload failed.");
				return;
			}

			SetStatus(string.Format(CultureInfo.InvariantCulture,
				"Game content reloaded ({0:F0} ms). Re-open hero room to verify.",
				result.ElapsedMs));
		}

		/// <summary>Called by Lab.SwitchToHome. The Animator may well have just Saved, so the checklist needs to reflect that; harmless as a no-op re-read on every other entry to Home too.</summary>
		internal static void RefreshForReturnFromAnimator()
		{
			if (CurrentCharacterFolder != null)
			{
				CurrentProfile = CharacterProfileSidecar.Load(CurrentCharacterFolder);
			}
			RefreshCurrentCharacterPanels();
		}

		/// <summary>Adds a folder to the front of the recent-characters list, trimming to the 10 most recent.</summary>
		internal static void AddRecentCharacter(string folder)
		{
			recentCharacters.RemoveAll(f => string.Equals(f, folder, System.StringComparison.OrdinalIgnoreCase));
			recentCharacters.Insert(0, folder);
			if (recentCharacters.Count > 10)
			{
				recentCharacters.RemoveRange(10, recentCharacters.Count - 10);
			}
			RecentFilesStore.Save(recentCharacters);
			CharacterListPanel.Refresh();
		}

		/// <summary>Drops a folder from the recent-characters list without loading or deleting the character.</summary>
		internal static void RemoveRecentCharacter(string folder)
		{
			recentCharacters.RemoveAll(f => string.Equals(f, folder, System.StringComparison.OrdinalIgnoreCase));
			RecentFilesStore.Save(recentCharacters);
			CharacterListPanel.Refresh();
		}

		private static void RefreshCurrentCharacterPanels()
		{
			HomeNavPanel.Refresh(CurrentProfile);
			PropertiesWorkstationScene.RefreshAll(CurrentProfile);
			RefreshReadinessChecklist();
		}
	}
}
