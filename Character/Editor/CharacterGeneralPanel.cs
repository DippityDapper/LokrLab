using System;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's General category: the active character's own identity fields (Character ID, Name, Description).</summary>
	/// <remarks>Character ID is shown read-only (an internal identifier, never player-visible); Name/Description commit on end-edit. Locked/Unlock Achievement moved to their own Hero Roster category (2026-08-11) -- they're roster.json's own concern, not this character's own identity.</remarks>
	internal static class CharacterGeneralPanel
	{
		private static UiLabel idValueLabel;
		private static UiToggle entityTypeToggle;
		private static UiTextField nameField;
		private static UiTextField descriptionField;
		private static bool suppressFieldEvents;

		/// <summary>Builds the identity fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			section.Add(UiLabel.Create(section.ContentTransform, "Character ID (internal, never shown to the player):").FixedHeight(20f));
			idValueLabel = UiLabel.Create(section.ContentTransform, "-");
			section.Add(idValueLabel.FixedHeight(24f));
			LabHoverInfo.Bind(idValueLabel.GameObject, "character.general.Id");

			entityTypeToggle = UiToggle.Create(section.ContentTransform, "Enemy/Summon (off = playable Hero -- no roster entry, no Hero Roster category fields)", false);
			entityTypeToggle.OnValueChanged(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetEntityType(value ? CharacterEntityType.EnemySummon : CharacterEntityType.Hero); });
			section.Add(entityTypeToggle.FixedHeight(24f));
			LabHoverInfo.Bind(entityTypeToggle.GameObject, "character.general.EntityType");

			section.Add(UiLabel.Create(section.ContentTransform, "Name:").FixedHeight(20f));
			nameField = UiTextField.Create(section.ContentTransform);
			nameField.OnEndEdit(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetName(value); });
			section.Add(nameField.FixedHeight(30f));
			LabHoverInfo.Bind(nameField.GameObject, "character.general.Name");

			section.Add(UiLabel.Create(section.ContentTransform, "Description:").FixedHeight(20f));
			descriptionField = UiTextField.Create(section.ContentTransform, multiline: true);
			descriptionField.OnEndEdit(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetDescription(value); });
			section.Add(descriptionField.FixedHeight(60f));
			LabHoverInfo.Bind(descriptionField.GameObject, "character.general.Description");

			return section;
		}

		/// <summary>Populates fields from a profile, or blanks them if null.</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			suppressFieldEvents = true;
			idValueLabel.SetText(profile != null ? profile.Id : "-");
			entityTypeToggle.SetValueSilently(profile != null && profile.EntityType == CharacterEntityType.EnemySummon);
			nameField.SetText(profile != null ? profile.Name : string.Empty);
			descriptionField.SetText(profile != null ? profile.Description : string.Empty);
			suppressFieldEvents = false;
		}

		/// <summary>Writes focused Name/Description text into the profile before a persist or reload.</summary>
		/// <remarks>
		/// Description is multiline, so Enter inserts a newline instead of ending edit. Close Lab
		/// and Reload in Game used to persist the previous value, then the destroyed field flushed
		/// the new text to disk too late for the in-memory loc table — hero-room lore needed a
		/// process restart. Deactivate fires OnEndEdit; a second apply covers a field that was
		/// already unfocused but never committed.
		/// </remarks>
		internal static void CommitPending()
		{
			if (suppressFieldEvents || CharacterSession.Profile == null)
			{
				return;
			}

			CommitIfChanged(nameField, CharacterSession.Profile.Name, HomeWorkstationScene.SetName);
			CommitIfChanged(descriptionField, CharacterSession.Profile.Description, HomeWorkstationScene.SetDescription);
		}

		/// <summary>Drops widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			idValueLabel = null;
			entityTypeToggle = null;
			nameField = null;
			descriptionField = null;
			suppressFieldEvents = false;
		}

		private static void CommitIfChanged(UiTextField field, string current, Action<string> apply)
		{
			if (field == null || field.GameObject == null || field.InputField == null)
			{
				return;
			}

			if (field.InputField.isFocused)
			{
				field.InputField.DeactivateInputField();
			}

			string text = field.InputField.text ?? string.Empty;
			if (!string.Equals(text, current ?? string.Empty, StringComparison.Ordinal))
			{
				apply(text);
			}
		}
	}
}
