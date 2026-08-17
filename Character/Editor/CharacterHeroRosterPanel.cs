using System.Collections.Generic;
using System.Linq;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Achievements;
using LokrLab.Editor.General;
using SimpleUI;
using UnityEngine;
using LokrCharacterLab;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Hero Roster category: the fields that decide roster.json's own content and which HeroRosterManager list this character is spliced into (Tier, Locked, Unlock Achievement).</summary>
	/// <remarks>Split out of the old combined identity panel 2026-08-11 -- these are roster.json's own concern, not the character's own name/description identity, and now sit alongside Levels/States/Portraits/etc. as their own nav category rather than crowding one fixed panel.</remarks>
	internal static class CharacterHeroRosterPanel
	{
		/// <summary>The Unlock Achievement dropdown's index-0 option, mapping to CharacterProfile.UnlockAchievement = "".</summary>
		private const string NoAchievementOption = "(None -- always unlocked once Locked is off)";

		private static UiToggle legendToggle;
		private static UiToggle lockedToggle;
		private static UiDropdown unlockAchievementDropdown;
		private static List<string> unlockAchievementIds;
		private static bool suppressFieldEvents;

		/// <summary>Builds the Tier/Locked/Unlock Achievement fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			legendToggle = UiToggle.Create(section.ContentTransform, "Legend (off = Companion)", false);
			legendToggle.OnValueChanged(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetTier(value ? CharacterTier.Legend : CharacterTier.Companion); });
			section.Add(legendToggle.FixedHeight(24f));
			LabHoverInfo.Bind(legendToggle.GameObject, "character.roster.Tier");

			lockedToggle = UiToggle.Create(section.ContentTransform, "Locked", true);
			lockedToggle.OnValueChanged(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetLocked(value); });
			section.Add(lockedToggle.FixedHeight(24f));
			LabHoverInfo.Bind(lockedToggle.GameObject, "character.roster.Locked");

			section.Add(UiLabel.Create(section.ContentTransform, "Unlock Achievement (optional, only matters while Locked):").FixedHeight(20f));
			unlockAchievementIds = GetAllAchievementIds();
			List<string> options = new List<string> { NoAchievementOption };
			options.AddRange(unlockAchievementIds);
			unlockAchievementDropdown = UiDropdown.Create(section.ContentTransform, options);
			unlockAchievementDropdown.OnValueChanged(index => { if (!suppressFieldEvents) HomeWorkstationScene.SetUnlockAchievement(index <= 0 ? string.Empty : unlockAchievementIds[index - 1]); });
			section.Add(unlockAchievementDropdown.FixedHeight(30f));
			LabHoverInfo.Bind(unlockAchievementDropdown.GameObject, "character.roster.UnlockAchievement");

			return section;
		}

		/// <summary>Every real base-game achievement id, sorted, straight from the same source the base game's own AchievementDebugPanel reads (MetagameManager.instance.Player.AchievementManager.AchievementDefinitionConfig.achievements) -- not a hand-maintained copy that could drift from the real list. Defensive: MetagameManager not being fully initialized yet would be a genuine base-game bug if it happened here (the Lab is only reachable from the main menu, well after metagame boot), but crashing the whole Properties panel over an achievement dropdown would be a worse failure mode than an empty list, so this never throws.</summary>
		private static List<string> GetAllAchievementIds()
		{
			try
			{
				AchievementDefinitionConfig config = MetagameManager.instance?.Player?.AchievementManager?.AchievementDefinitionConfig;
				if (config?.achievements == null)
				{
					return new List<string>();
				}
				return config.achievements.Select(definition => definition.id).OrderBy(id => id).ToList();
			}
			catch (System.Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("CharacterHeroRosterPanel: failed to read achievement definitions: " + ex.Message);
				return new List<string>();
			}
		}

		/// <summary>Populates fields from a profile, or leaves them at their last state if null (nothing meaningful to blank -- unlike text fields, toggles/dropdowns always show some value).</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null)
			{
				return;
			}
			suppressFieldEvents = true;
			legendToggle.SetValueSilently(profile.Tier == CharacterTier.Legend);
			lockedToggle.SetValueSilently(profile.Locked);
			int achievementIndex = string.IsNullOrEmpty(profile.UnlockAchievement) ? -1 : unlockAchievementIds.IndexOf(profile.UnlockAchievement);
			unlockAchievementDropdown.SetValueSilently(achievementIndex + 1);
			suppressFieldEvents = false;
		}
	}
}
