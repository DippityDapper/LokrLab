using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Skills category: the active character's cinematicTags/skills/defaultSkill/skillProgression fields.</summary>
	internal static class CharacterSkillsPanel
	{
		private static UiList<string> cinematicTagList;
		private static UiComboBox newCinematicTagField;
		private static UiList<int> skillList;
		private static UiComboBox newSkillField;
		private static UiDropdown defaultSkillField;
		private static UiList<LevelSkillEntry> skillProgressionList;
		private static UiTextField newProgressionLevelField;
		private static bool suppressFieldEvents;

		/// <summary>Builds the skills fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			section.Add(UiLabel.Create(section.ContentTransform, "Cinematic Tags:").FixedHeight(18f));
			cinematicTagList = UiList<string>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(cinematicTagList);
			LabHoverInfo.Bind(cinematicTagList.GameObject, "character.skills.CinematicTag");

			UiStack addTagRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addTagRow.FixedHeight(30f));
			newCinematicTagField = UiComboBox.Create(addTagRow.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.CinematicTags), "add tag");
			addTagRow.Add(newCinematicTagField.Grow());
			addTagRow.Add(UiButton.Create(addTagRow.ContentTransform, "Add", OnAddCinematicTagClicked, primary: false).FixedWidth(60f));

			section.Add(UiLabel.Create(section.ContentTransform, "Skills:").FixedHeight(18f));
			skillList = UiList<int>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(skillList);
			LabHoverInfo.Bind(skillList.GameObject, "character.skills.SkillId");

			UiStack addSkillRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addSkillRow.FixedHeight(30f));
			newSkillField = UiComboBox.Create(addSkillRow.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SkillIds), "skill id");
			addSkillRow.Add(newSkillField.Grow());
			addSkillRow.Add(UiButton.Create(addSkillRow.ContentTransform, "Add", OnAddSkillClicked, primary: false).FixedWidth(60f));

			section.Add(UiLabel.Create(section.ContentTransform, "Default Skill (basic attack):").FixedHeight(18f));
			defaultSkillField = UiDropdown.Create(section.ContentTransform, System.Array.Empty<string>());
			defaultSkillField.OnValueChanged(OnDefaultSkillChanged);
			section.Add(defaultSkillField.FixedHeight(28f));
			LabHoverInfo.Bind(defaultSkillField.GameObject, "character.skills.DefaultSkill");

			section.Add(UiLabel.Create(section.ContentTransform, "Skill Progression:").FixedHeight(18f));
			skillProgressionList = UiList<LevelSkillEntry>.Create(section.ContentTransform, spacing: 8f, padding: 0f, scrollable: false);
			section.Add(skillProgressionList);

			UiStack addProgressionRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addProgressionRow.FixedHeight(30f));
			addProgressionRow.Add(UiLabel.Create(addProgressionRow.ContentTransform, "Level:").FixedWidth(40f));
			newProgressionLevelField = UiTextField.Create(addProgressionRow.ContentTransform, "1");
			addProgressionRow.Add(newProgressionLevelField.FixedWidth(50f));
			addProgressionRow.Add(UiButton.Create(addProgressionRow.ContentTransform, "Add Level", OnAddProgressionLevelClicked, primary: false).FixedWidth(90f));

			return section;
		}

		/// <summary>Populates fields from a profile. A no-op if profile is null.</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null)
			{
				return;
			}
			suppressFieldEvents = true;
			newCinematicTagField.SetOptions(CinematicTagOptionsFor(profile));
			IReadOnlyList<string> skillOptions = SkillOptionsFor(profile);
			newSkillField.SetOptions(skillOptions);
			cinematicTagList.SetItems(profile.CinematicTags, tag => tag, BuildCinematicTagRow);

			List<int> skillIndices = Enumerable.Range(0, profile.Skills.Count).ToList();
			skillList.SetItems(skillIndices,
				index => index + "|" + profile.Skills[index],
				(parent, index) => BuildSkillRow(parent, profile, index));

			RefreshDefaultSkillField(profile);

			List<LevelSkillEntry> progressionEntries = profile.SkillProgression.OrderBy(entry => entry.Level).ToList();
			skillProgressionList.SetItems(progressionEntries,
				entry => entry.Level + "|" + string.Join(",", entry.SkillIds),
				(parent, entry) => BuildProgressionEntry(parent, profile, entry));

			suppressFieldEvents = false;
		}

		private static UiElement BuildCinematicTagRow(Transform parent, string tag)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(UiLabel.Create(row.ContentTransform, tag).Grow());
			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveCinematicTag(tag), primary: false).FixedWidth(28f));
			return row.FixedHeight(28f);
		}

		private static UiElement BuildSkillRow(Transform parent, CharacterProfile profile, int index)
		{
			string skillId = profile.Skills[index];
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiComboBox skillField = UiComboBox.Create(row.ContentTransform, SkillOptionsFor(profile), skillId);
			skillField.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					HomeWorkstationScene.SetSkillAt(index, value.Trim());
				}
			});
			row.Add(skillField.Grow());
			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveSkillAt(index), primary: false).FixedWidth(28f));

			return row.FixedHeight(30f);
		}

		private static UiElement BuildProgressionEntry(Transform parent, CharacterProfile profile, LevelSkillEntry entry)
		{
			int level = entry.Level;
			UiStack block = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiStack headerRow = UiStack.Horizontal(block.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			block.Add(headerRow.FixedHeight(30f));
			headerRow.Add(UiLabel.Create(headerRow.ContentTransform, "Level").FixedWidth(40f));
			UiTextField levelField = UiTextField.Create(headerRow.ContentTransform, level.ToString(CultureInfo.InvariantCulture));
			levelField.OnEndEdit(value =>
			{
				if (suppressFieldEvents || !int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int newLevel))
				{
					return;
				}
				HomeWorkstationScene.SetSkillProgressionEntryLevel(level, newLevel);
			});
			headerRow.Add(levelField.FixedWidth(50f));
			LabHoverInfo.Bind(levelField.GameObject, "character.skills.ProgressionLevel");
			headerRow.Add(UiButton.Create(headerRow.ContentTransform, "Remove Level", () => HomeWorkstationScene.RemoveSkillProgressionEntry(level), primary: false).FixedWidth(100f));

			UiList<int> skillsAtLevel = UiList<int>.Create(block.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			block.Add(skillsAtLevel);
			List<int> skillIndices = Enumerable.Range(0, entry.SkillIds.Count).ToList();
			skillsAtLevel.SetItems(skillIndices,
				index => level + "|" + index + "|" + entry.SkillIds[index],
				(skillParent, index) => BuildProgressionSkillRow(skillParent, profile, level, index, entry.SkillIds[index]));

			UiStack addSkillRow = UiStack.Horizontal(block.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			block.Add(addSkillRow.FixedHeight(30f));
			UiComboBox newProgressionSkillField = UiComboBox.Create(addSkillRow.ContentTransform, SkillOptionsFor(profile), "skill id");
			addSkillRow.Add(newProgressionSkillField.Grow());
			addSkillRow.Add(UiButton.Create(addSkillRow.ContentTransform, "Add", () =>
			{
				HomeWorkstationScene.AddSkillProgressionSkill(level, newProgressionSkillField.InputField.text);
				newProgressionSkillField.SetText(string.Empty);
			}, primary: false).FixedWidth(60f));

			return block;
		}

		private static UiElement BuildProgressionSkillRow(Transform parent, CharacterProfile profile, int level, int skillIndex, string skillId)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiComboBox skillField = UiComboBox.Create(row.ContentTransform, SkillOptionsFor(profile), skillId);
			skillField.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					HomeWorkstationScene.SetSkillProgressionSkillAt(level, skillIndex, value.Trim());
				}
			});
			row.Add(skillField.Grow());
			LabHoverInfo.Bind(skillField.GameObject, "character.skills.ProgressionSkill");
			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveSkillProgressionSkillAt(level, skillIndex), primary: false).FixedWidth(28f));

			return row.FixedHeight(30f);
		}

		private static void OnDefaultSkillChanged(int index)
		{
			if (suppressFieldEvents)
			{
				return;
			}
			CharacterProfile profile = HomeWorkstationScene.CurrentProfile;
			List<string> options = HomeWorkstationScene.GetDefaultSkillCandidates();
			if (profile == null || index < 0 || index >= options.Count)
			{
				return;
			}
			HomeWorkstationScene.SetDefaultSkill(options[index]);
		}

		private static void RefreshDefaultSkillField(CharacterProfile profile)
		{
			HomeWorkstationScene.EnsureDefaultSkillValid();
			List<string> options = HomeWorkstationScene.GetDefaultSkillCandidates();
			defaultSkillField.SetOptions(options);
			if (options.Count == 0)
			{
				return;
			}
			int selectedIndex = options.IndexOf(profile.DefaultSkill);
			if (selectedIndex < 0)
			{
				selectedIndex = 0;
			}
			defaultSkillField.SetValueSilently(selectedIndex);
		}

		private static void OnAddCinematicTagClicked()
		{
			HomeWorkstationScene.AddCinematicTag(newCinematicTagField.InputField.text);
			newCinematicTagField.SetText(string.Empty);
		}

		private static void OnAddSkillClicked()
		{
			HomeWorkstationScene.AddSkill(newSkillField.InputField.text);
			newSkillField.SetText(string.Empty);
		}

		private static void OnAddProgressionLevelClicked()
		{
			if (!int.TryParse(newProgressionLevelField.InputField.text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
			{
				return;
			}
			HomeWorkstationScene.AddSkillProgressionEntry(level);
		}

		private static IEnumerable<string> CinematicTagOptionsFor(CharacterProfile profile)
		{
			HashSet<string> tags = new HashSet<string>(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.CinematicTags));
			foreach (string tag in profile.CinematicTags)
			{
				if (!string.IsNullOrEmpty(tag))
				{
					tags.Add(tag);
				}
			}
			return tags.OrderBy(tag => tag);
		}

		private static IReadOnlyList<string> SkillOptionsFor(CharacterProfile profile)
		{
			HashSet<string> ids = new HashSet<string>(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SkillIds));
			foreach (string id in profile.Skills)
			{
				if (!string.IsNullOrEmpty(id))
				{
					ids.Add(id);
				}
			}
			foreach (LevelSkillEntry entry in profile.SkillProgression)
			{
				foreach (string id in entry.SkillIds)
				{
					if (!string.IsNullOrEmpty(id))
					{
						ids.Add(id);
					}
				}
			}
			return ids.OrderBy(id => id).ToList();
		}
	}
}
