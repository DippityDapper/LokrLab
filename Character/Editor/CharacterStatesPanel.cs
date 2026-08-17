using System.Collections.Generic;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's States category: the active character's rlheroes.txt states block (LEGEND, combat-immunity flags like CANT_BE_POISONED, behavior flags like NOT_IN_INITIATIVE_BAR, etc.) as addable/removable name/toggle rows.</summary>
	/// <remarks>An open-ended list rather than a fixed checklist of known flags — built-in combobox suggestions come from base-game data only; mods extend via CharacterLabOptionsAPI.</remarks>
	internal static class CharacterStatesPanel
	{
		private static UiList<string> stateList;
		private static UiComboBox newStateNameField;

		/// <summary>Builds the state list and Add-state row into the shared inspector content.</summary>
		/// <remarks>stateList is deliberately non-scrollable -- see CharacterLevelsPanel's own remarks for why a scrollable UiList nested this deep inside a self-fitting chain collapses to zero instead of sizing to its rows.</remarks>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			stateList = UiList<string>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(stateList);

			UiStack addRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addRow.FixedHeight(30f));
			newStateNameField = UiComboBox.Create(addRow.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.StateFlags), "new state flag (e.g. LEGEND)");
			addRow.Add(newStateNameField.Grow());
			addRow.Add(UiButton.Create(addRow.ContentTransform, "Add", OnAddClicked, primary: false).FixedWidth(60f));

			return section;
		}

		/// <summary>Rebuilds the state list. A no-op if profile is null.</summary>
		/// <remarks>Keys fold in the toggle's own current value, not just the flag name -- UiList&lt;T&gt; only rebuilds a row when its key is genuinely new, so a name-only key would let a row built for one character's "LEGEND": true silently keep showing that value after switching to a different character whose own "LEGEND" is false (or absent). See CharacterLevelsPanel's own remarks for the same class of bug found there first.</remarks>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null)
			{
				return;
			}
			newStateNameField.SetOptions(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.StateFlags));
			List<string> names = new List<string>(profile.States.Keys);
			names.Sort();
			stateList.SetItems(names, name => name + "|" + profile.States[name], (parent, name) => BuildStateRow(parent, profile, name));
		}

		private static UiElement BuildStateRow(Transform parent, CharacterProfile profile, string name)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiToggle toggle = UiToggle.Create(row.ContentTransform, name, profile.States.TryGetValue(name, out bool on) && on);
			toggle.OnValueChanged(value => HomeWorkstationScene.SetState(name, value));
			toggle.Grow();
			row.Add(toggle);
			LabHoverInfo.Bind(toggle.GameObject, "character.states.Flag");

			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveState(name), primary: false).FixedWidth(28f));

			return row.FixedHeight(28f);
		}

		private static void OnAddClicked()
		{
			HomeWorkstationScene.AddState(newStateNameField.InputField.text.Trim());
			newStateNameField.SetText(string.Empty);
		}
	}
}
