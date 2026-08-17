using System.Collections.Generic;
using System.Linq;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Sound category: the active character's soundConfig assetId/sounds fields.</summary>
	internal static class CharacterSoundPanel
	{
		private static UiComboBox assetIdField;
		private static UiList<string> soundClipList;
		private static UiComboBox newEventField;
		private static UiTextField newClipField;
		private static bool suppressFieldEvents;

		/// <summary>Builds the sound fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			section.Add(UiLabel.Create(section.ContentTransform, "Sound Asset Id:").FixedHeight(18f));
			assetIdField = UiComboBox.Create(section.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SoundAssetIds));
			assetIdField.OnEndEdit(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetSoundAssetId(value); });
			section.Add(assetIdField.FixedHeight(28f));
			LabHoverInfo.Bind(assetIdField.GameObject, "character.sound.AssetId");

			section.Add(UiLabel.Create(section.ContentTransform, "Sound Clips:").FixedHeight(18f));
			soundClipList = UiList<string>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(soundClipList);

			UiStack addRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addRow.FixedHeight(30f));
			newEventField = UiComboBox.Create(addRow.ContentTransform, CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SoundEvents), "event");
			addRow.Add(newEventField.Grow());
			newClipField = UiTextField.Create(addRow.ContentTransform);
			addRow.Add(newClipField.Grow());
			addRow.Add(UiButton.Create(addRow.ContentTransform, "Add", OnAddClicked, primary: false).FixedWidth(60f));

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
			assetIdField.SetOptions(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SoundAssetIds));
			assetIdField.SetText(profile.SoundAssetId);
			newEventField.SetOptions(SoundEventOptionsFor(profile));
			List<string> events = profile.SoundClips.Keys.OrderBy(key => key).ToList();
			soundClipList.SetItems(events,
				eventName => eventName + "|" + profile.SoundClips[eventName],
				(parent, eventName) => BuildSoundClipRow(parent, profile, eventName));
			suppressFieldEvents = false;
		}

		private static UiElement BuildSoundClipRow(Transform parent, CharacterProfile profile, string eventName)
		{
			string clip = profile.SoundClips[eventName];
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);

			UiComboBox eventField = UiComboBox.Create(row.ContentTransform, SoundEventOptionsFor(profile), eventName);
			eventField.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					HomeWorkstationScene.RenameSoundClip(eventName, value.Trim());
				}
			});
			row.Add(eventField.Grow());
			LabHoverInfo.Bind(eventField.GameObject, "character.sound.Event");

			UiTextField clipField = UiTextField.Create(row.ContentTransform, clip);
			clipField.OnEndEdit(value =>
			{
				if (!suppressFieldEvents)
				{
					HomeWorkstationScene.SetSoundClipValue(eventName, value);
				}
			});
			row.Add(clipField.Grow());
			LabHoverInfo.Bind(clipField.GameObject, "character.sound.Clip");

			row.Add(UiButton.Create(row.ContentTransform, "x", () => HomeWorkstationScene.RemoveSoundClip(eventName), primary: false).FixedWidth(28f));

			return row.FixedHeight(30f);
		}

		private static void OnAddClicked()
		{
			string eventName = newEventField.InputField.text.Trim();
			if (eventName.Length == 0)
			{
				return;
			}
			HomeWorkstationScene.AddSoundClip(eventName, newClipField.InputField.text.Trim());
			newEventField.SetText(string.Empty);
			newClipField.SetText(string.Empty);
		}

		private static IReadOnlyList<string> SoundEventOptionsFor(CharacterProfile profile)
		{
			HashSet<string> merged = new HashSet<string>(CharacterLabOptionsAPI.GetOptions(CharacterLabOptionsAPI.PropertyOptionList.SoundEvents));
			foreach (string key in profile.SoundClips.Keys)
			{
				merged.Add(key);
			}
			List<string> sorted = merged.ToList();
			sorted.Sort();
			return sorted;
		}
	}
}
