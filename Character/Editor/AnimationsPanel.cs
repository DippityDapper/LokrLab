using System.Collections.Generic;
using LokrLab.Editor.Animation;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Top strip of the timeline area: "+ Add Animation", then Rest Pose, then one button per clip -- clicking any of them switches to Inspector's Animation target without touching which part is selected in the viewport.</summary>
	/// <remarks>Add Animation presets are CombatSequenceNames.PresetsForModel for the open character's Model, rebuilt each time the modal opens -- not the union of every dumped prefab. See animation-data-model.md.</remarks>
	internal static class AnimationsPanel
	{
		private const float ButtonWidth = 110f;
		private const float ButtonHeight = 26f;

		private static UiStack buttonRow;
		private static UiModal addModal;
		private static UiStack presetRow;
		private static UiTextField addNameField;

		/// <summary>Builds the Add Animation button and the scrollable Rest Pose/clip button row (scrollable since a rig can accumulate many clips).</summary>
		internal static void Build(Transform canvas, Font labelFont)
		{
			UiPanel panel = UiPanel.Create(canvas, UiTheme.Default, region: EditorLayout.AnimationsRowRegion);
			panel.Add(BuildRow(panel.ContentParent));
			BuildAddAnimationPopup(canvas);
		}

		/// <summary>Builds the clip-button strip into a layout parent (shell Timeline bottom panel).</summary>
		internal static UiStack BuildInto(Transform parent, Transform canvas)
		{
			UiStack row = BuildRow(parent);
			if (addModal == null)
			{
				BuildAddAnimationPopup(canvas);
			}

			return row;
		}

		private static UiStack BuildRow(Transform parent)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 5f, padding: 4f);
			UiButton add = UiButton.Create(row.ContentTransform, "+ Add Animation", OnAddAnimationClicked, primary: false);
			row.Add(add.FixedWidth(140f).FixedHeight(ButtonHeight));
			LabHoverInfo.Bind(add.GameObject, "animator.animations.Add");

			buttonRow = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 5f, padding: 0f, scrollable: true);
			buttonRow.Grow();
			row.Add(buttonRow.FixedHeight(ButtonHeight + 6f));
			return row;
		}

		/// <summary>Drops the clip-button row ref after the Timeline dock is destroyed.</summary>
		internal static void Unbind()
		{
			buttonRow = null;
		}

		/// <summary>Rebuilds the Rest Pose/clip button row, highlighting the active one.</summary>
		/// <remarks>No-ops until <see cref="Build"/> or <see cref="BuildInto"/> has created the row.</remarks>
		internal static void Refresh(List<AnimationClip> clips, AnimationClip activeClip)
		{
			if (buttonRow == null)
			{
				return;
			}

			buttonRow.Clear();

			UiButton restPoseButton = UiButton.Create(buttonRow.ContentTransform, "Rest Pose", RigEditorScene.SelectRestPose, primary: false)
				.FixedWidth(ButtonWidth).FixedHeight(ButtonHeight);
			restPoseButton.SetColor(activeClip == null ? UiTheme.Default.AccentColor : UiTheme.Default.RowButtonColor);
			buttonRow.Add(restPoseButton);
			LabHoverInfo.Bind(restPoseButton.GameObject, "animator.frame.Title");

			foreach (AnimationClip clip in clips)
			{
				AnimationClip captured = clip;
				UiButton button = UiButton.Create(buttonRow.ContentTransform, captured.Name, () => RigEditorScene.SelectClip(captured), primary: false)
					.FixedWidth(ButtonWidth).FixedHeight(ButtonHeight);
				button.SetColor(captured == activeClip ? UiTheme.Default.AccentColor : UiTheme.Default.RowButtonColor);
				buttonRow.Add(button);
			}
		}

		private static void BuildAddAnimationPopup(Transform canvas)
		{
			addModal = UiModal.Create(canvas, UiTheme.Default, "Add Animation", 650f, 320f);
			UiStack content = UiStack.Vertical(addModal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			addModal.Add(content);

			content.Add(UiLabel.Create(content.ContentTransform, "Quick add — names this character's Model prefab looks up:").FixedHeight(20f));

			presetRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 5f, padding: 0f, scrollable: true);
			content.Add(presetRow.FixedHeight(ButtonHeight + 6f));

			content.Add(UiLabel.Create(content.ContentTransform, "Or a custom name:").FixedHeight(20f));

			addNameField = UiTextField.Create(content.ContentTransform, "NewClip");
			content.Add(addNameField.FixedHeight(34f));
			LabHoverInfo.Bind(addNameField.GameObject, "animator.animations.CustomName");

			UiStack buttonsRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(buttonsRow.FixedHeight(36f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Add", OnAddAnimationConfirmClicked, primary: false).FixedWidth(150f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Cancel", addModal.Hide, primary: false).FixedWidth(110f));
		}

		/// <summary>Fills the Add Animation preset row from PresetsForModel of the open character's Model.</summary>
		private static void RebuildPresetButtons()
		{
			presetRow.Clear();
			string model = "HumanArcher";
			CharacterProfile profile = CharacterProfileSidecar.Load(RigEditorScene.CurrentFolder);
			if (profile != null && !string.IsNullOrEmpty(profile.Model))
			{
				model = profile.Model;
			}
			foreach (string presetName in CombatSequenceNames.PresetsForModel(model))
			{
				string captured = presetName;
				UiButton preset = UiButton.Create(presetRow.ContentTransform, captured, () => OnPresetAnimationClicked(captured), primary: false);
				presetRow.Add(preset.FixedWidth(ButtonWidth).FixedHeight(ButtonHeight));
				LabHoverInfo.Bind(preset.GameObject, "animator.animations.Preset");
			}
		}

		private static void OnAddAnimationClicked()
		{
			addNameField.SetText("NewClip");
			RebuildPresetButtons();
			addModal.Show();
		}

		private static void OnPresetAnimationClicked(string name)
		{
			addModal.Hide();
			RigEditorScene.CreateNewClip(name);
		}

		private static void OnAddAnimationConfirmClicked()
		{
			string name = addNameField.InputField.text;
			addModal.Hide();
			RigEditorScene.CreateNewClip(name);
		}
	}
}
