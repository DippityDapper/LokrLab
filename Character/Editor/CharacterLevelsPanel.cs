using System.Globalization;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Level Properties category: the active character's rlheroes.txt archetype chain (rank-up growth) -- a tab per rank plus that rank's own stat overrides as addable/removable name/value rows.</summary>
	/// <remarks>Level 1 always exists and typically carries every stat a character needs; level 2+ tabs are optional rank-up diffs (only the stats that change at that rank), exactly how real shipped characters like Onagro grow on level-up -- see CharacterLevel's own remarks for how this was verified against Onagro's actual file before trusting it.</remarks>
	internal static class CharacterLevelsPanel
	{
		private static UiList<CharacterLevel> levelTabs;
		private static UiButton removeLevelButton;
		private static UiList<StatEntry> statList;
		private static UiComboBox newStatNameField;

		/// <summary>Builds the level-tab row and stat list into the shared inspector content.</summary>
		/// <remarks>statList is deliberately non-scrollable, the same reasoning as HomeNavPanel's own workstation-button list -- a scrollable UiList has no self-sizing of its own, and nesting one inside this section (itself nested inside the shared inspector's own self-fitting scrollable content) leaves no well-defined "leftover space" for Grow() to claim, collapsing to zero. Non-scrollable gets a real ContentSizeFitter that sizes to the rows' own combined height, letting the whole self-fitting chain (statList -&gt; section -&gt; the shared inspector) work correctly, with the inspector's own outer ScrollRect providing page-level scrolling if the visible category's content overflows.</remarks>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			UiStack tabRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(tabRow.FixedHeight(30f));
			levelTabs = UiList<CharacterLevel>.Create(tabRow.ContentTransform, UiOrientation.Horizontal, UiTheme.Default, spacing: 4f, padding: 0f, scrollable: false);
			levelTabs.Grow();
			tabRow.Add(levelTabs);
			LabHoverInfo.Bind(levelTabs.GameObject, "character.levels.Tab");
			UiButton addLevel = UiButton.Create(tabRow.ContentTransform, "+ Add Level", HomeWorkstationScene.AddLevel, primary: false);
			tabRow.Add(addLevel.FixedWidth(100f));
			LabHoverInfo.Bind(addLevel.GameObject, "character.levels.AddLevel");
			removeLevelButton = UiButton.Create(tabRow.ContentTransform, "Remove Level", OnRemoveLevelClicked, primary: false).FixedWidth(100f);
			tabRow.Add(removeLevelButton);

			statList = UiList<StatEntry>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(statList);

			UiStack addRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addRow.FixedHeight(30f));
			newStatNameField = UiComboBox.Create(addRow.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.StatNames), "new stat name");
			addRow.Add(newStatNameField.Grow());
			addRow.Add(UiButton.Create(addRow.ContentTransform, "Add", OnAddClicked, primary: false).FixedWidth(60f));

			return section;
		}

		/// <summary>Rebuilds the level tabs and the current rank's stat list. A no-op if profile is null.</summary>
		/// <remarks>Both UiList keys fold in state that must force a rebuild (not just an add/remove) when it changes -- the active level for tab highlighting, and the editing level for stat rows -- since UiList&lt;T&gt; only rebuilds a row when its key is genuinely new, reusing the existing GameObject (and whatever text/closures were baked into it at build time) whenever the key repeats. Found 2026-08-11: keying stat rows by stat.Name alone meant switching levels never rebuilt anything for a stat name shared across ranks (the common case -- health_max, armor_max, etc. all repeat every rank), so the displayed values, and the RenameStat/SetStatValue/RemoveStat closures bound to them, silently stayed pointed at whichever level was on screen first.</remarks>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null || levelTabs == null || levelTabs.GameObject == null
				|| statList == null || statList.GameObject == null)
			{
				return;
			}
			newStatNameField.SetOptions(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.StatNames));
			levelTabs.SetItems(profile.Levels,
				level => level.Level.ToString(CultureInfo.InvariantCulture) + "|" + (level.Level == HomeWorkstationScene.CurrentEditingLevel),
				BuildLevelTab);
			removeLevelButton.Interactable(profile.Levels.Count > 1);

			CharacterLevel current = profile.Levels.Find(level => level.Level == HomeWorkstationScene.CurrentEditingLevel);
			if (current == null && profile.Levels.Count > 0)
			{
				current = profile.Levels[0];
			}
			int editingLevel = current?.Level ?? 1;
			statList.SetItems(current?.Stats ?? new System.Collections.Generic.List<StatEntry>(),
				stat => editingLevel + "|" + stat.Name, (parent, stat) => BuildStatRow(parent, editingLevel, stat));
		}

		private static UiElement BuildLevelTab(Transform parent, CharacterLevel level)
		{
			bool isActive = level.Level == HomeWorkstationScene.CurrentEditingLevel;
			UiButton tab = UiButton.Create(parent, "Level " + level.Level.ToString(CultureInfo.InvariantCulture),
				() => HomeWorkstationScene.SelectLevel(level.Level), primary: isActive);
			return tab.FixedWidth(90f);
		}

		/// <summary>Drops cached list widgets after Close Project destroys the inspector host.</summary>
		internal static void ResetSession()
		{
			levelTabs = null;
			removeLevelButton = null;
			statList = null;
			newStatNameField = null;
		}

		/// <summary>Builds one stat row: an editable name field, an editable value field, and a remove button.</summary>
		private static UiElement BuildStatRow(Transform parent, int level, StatEntry stat)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiComboBox nameField = UiComboBox.Create(row.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.StatNames), stat.Name);
			nameField.OnEndEdit(value => HomeWorkstationScene.RenameStat(level, stat.Name, value.Trim()));
			row.Add(nameField.Grow());
			LabHoverInfo.Bind(nameField.GameObject, "character.levels.StatName");

			UiTextField valueField = UiTextField.Create(row.ContentTransform, FormatValue(stat.Value));
			valueField.OnEndEdit(value => OnValueEndEdit(level, stat.Name, value));
			row.Add(valueField.FixedWidth(70f));
			LabHoverInfo.Bind(valueField.GameObject, "character.levels.StatValue");

			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveStat(level, stat.Name), primary: false).FixedWidth(28f));

			return row.FixedHeight(30f);
		}

		private static void OnValueEndEdit(int level, string statName, string typedValue)
		{
			if (float.TryParse(typedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			{
				HomeWorkstationScene.SetStatValue(level, statName, parsed);
			}
		}

		private static void OnAddClicked()
		{
			HomeWorkstationScene.AddStat(HomeWorkstationScene.CurrentEditingLevel, newStatNameField.InputField.text.Trim());
			newStatNameField.SetText(string.Empty);
		}

		private static void OnRemoveLevelClicked()
		{
			HomeWorkstationScene.RemoveLevel(HomeWorkstationScene.CurrentEditingLevel);
		}

		private static string FormatValue(float value)
		{
			return value == System.Math.Floor(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture);
		}
	}
}
