using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Appearance category: the active character's remaining rlheroes.txt identity/appearance fields that don't have a more specific home elsewhere (Model, AttackType, Icon, Background, UnitOnMap, PortraitBackgroundColor).</summary>
	/// <remarks>Combat prefab (Model) is the units-bundle spawn template whose ExoSkeletonUnitAnimationController sequence names the custom rig must provide -- see CombatSequenceNames. The mesh is MetaExo / the Lab rig. Icon/Background are unread by the base game as far as this project's investigation found (docs/roadmaps/completed/full-port/gaps.md) -- still editable here for round-trip fidelity with an imported character's original file, not because they're known to do anything.</remarks>
	internal static class CharacterAppearancePanel
	{
		private static UiComboBox modelField;
		private static UiComboBox attackTypeField;
		private static UiTextField iconField;
		private static UiTextField backgroundField;
		private static UiTextField unitOnMapField;
		private static UiTextField portraitBackgroundColorField;
		private static bool suppressFieldEvents;

		/// <summary>Builds the appearance fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			section.Add(UiLabel.Create(section.ContentTransform,
				"Combat prefab is the vanilla units-bundle spawn template (animation controllers), not the mesh. MetaExo / the Lab rig is the art.",
				UiTheme.Default, 11, TextAnchor.UpperLeft).FixedHeight(32f));
			modelField = AddComboField(section, "Combat prefab (Model):", CharacterLabOptionsAPI.PropertyOptionList.ModelValues, HomeWorkstationScene.SetModel, "character.appearance.Model");
			attackTypeField = AddComboField(section, "Attack Type:", CharacterLabOptionsAPI.PropertyOptionList.AttackTypes, HomeWorkstationScene.SetAttackType, "character.appearance.AttackType");
			iconField = AddTextField(section, "Icon:", HomeWorkstationScene.SetIcon, "character.appearance.Icon");
			backgroundField = AddTextField(section, "Background:", HomeWorkstationScene.SetBackground, "character.appearance.Background");
			unitOnMapField = AddTextField(section, "Unit On Map:", HomeWorkstationScene.SetUnitOnMap, "character.appearance.UnitOnMap");
			portraitBackgroundColorField = AddTextField(section, "Portrait Background Color:", HomeWorkstationScene.SetPortraitBackgroundColor, "character.appearance.PortraitBackgroundColor");

			return section;
		}

		private static UiComboBox AddComboField(UiStack section, string label, CharacterLabOptionsAPI.PropertyOptionList optionList, System.Action<string> onEndEdit, string hoverKey)
		{
			section.Add(UiLabel.Create(section.ContentTransform, label).FixedHeight(18f));
			UiComboBox field = UiComboBox.Create(section.ContentTransform, CharacterLabOptionsAPI.GetOptions(optionList));
			field.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					onEndEdit(value);
				}
			});
			section.Add(field.FixedHeight(28f));
			LabHoverInfo.Bind(field.GameObject, hoverKey, () => field.InputField.text);
			return field;
		}

		/// <summary>Adds a labeled single-line text field wired to onEndEdit, guarded by suppressFieldEvents during Refresh.</summary>
		private static UiTextField AddTextField(UiStack section, string label, System.Action<string> onEndEdit, string hoverKey)
		{
			section.Add(UiLabel.Create(section.ContentTransform, label).FixedHeight(18f));
			UiTextField field = UiTextField.Create(section.ContentTransform);
			field.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					onEndEdit(value);
				}
			});
			section.Add(field.FixedHeight(28f));
			LabHoverInfo.Bind(field.GameObject, hoverKey);
			return field;
		}

		/// <summary>Populates fields from a profile. A no-op if profile is null.</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null)
			{
				return;
			}
			suppressFieldEvents = true;
			modelField.SetOptions(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.ModelValues));
			attackTypeField.SetOptions(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.AttackTypes));
			modelField.SetText(profile.Model);
			attackTypeField.SetText(profile.AttackType);
			iconField.SetText(profile.Icon);
			backgroundField.SetText(profile.Background);
			unitOnMapField.SetText(profile.UnitOnMap);
			portraitBackgroundColorField.SetText(profile.PortraitBackgroundColor);
			suppressFieldEvents = false;
		}
	}
}
