using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Projects
{
	/// <summary>Values collected by the Character New Project sheet before scaffolding.</summary>
	internal sealed class CharacterCreateRequest
	{
		/// <summary>Display name written to CharacterProfile.Name.</summary>
		internal string Name = string.Empty;
		/// <summary>Human stem of the minted folder id. Auto-filled from Name unless the user turns Auto off.</summary>
		internal string Slug = string.Empty;
		/// <summary>Self-alias key written to aliases.json. Auto-filled from Slug unless the user turns Alias Auto off.</summary>
		internal string Alias = string.Empty;
		/// <summary>Flavor text written to CharacterProfile.Description.</summary>
		internal string Description = string.Empty;
		/// <summary>Playable hero vs enemy/summon.</summary>
		internal CharacterEntityType EntityType = CharacterEntityType.Hero;
		/// <summary>Companion vs Legend roster list. Ignored for EnemySummon.</summary>
		internal CharacterTier Tier = CharacterTier.Companion;
	}

	/// <summary>Character project type's New Project fill-out sheet: name, slug, alias, Auto, description, and role.</summary>
	internal static class CharacterCreateSheet
	{
		/// <summary>Last committed create form. Consumed by HomeWorkstationScene, then cleared.</summary>
		internal static CharacterCreateRequest Pending;

		private static readonly string[] RoleLabels =
		{
			"Companion (playable)",
			"Legend (playable)",
			"Enemy / Summon"
		};

		private static UiTextField nameField;
		private static UiTextField slugField;
		private static UiTextField aliasField;
		private static UiTextField descriptionField;
		private static UiDropdown roleDropdown;

		/// <summary>Builds the Character init fields under the shell's New Project wizard (or the Load create modal).</summary>
		internal static void Build(Transform parent)
		{
			Pending = null;
			nameField = null;
			slugField = null;
			aliasField = null;
			descriptionField = null;
			roleDropdown = null;
			if (parent == null)
			{
				return;
			}

			UiTheme theme = UiTheme.Default;
			LabSlugCreateFields.Build(parent, "Name", "character", out nameField, out slugField, out aliasField, out _, out _, out _, "character.create");

			UiStack column = UiStack.Vertical(parent, theme, spacing: 6f, padding: 0f);
			column.Add(UiLabel.Create(column.ContentTransform, "Description", theme, 13).FixedHeight(20f));
			descriptionField = UiTextField.Create(column.ContentTransform, string.Empty, theme, multiline: true);
			column.Add(descriptionField.FixedHeight(64f));

			column.Add(UiLabel.Create(column.ContentTransform, "Role", theme, 13).FixedHeight(20f));
			roleDropdown = UiDropdown.Create(column.ContentTransform, RoleLabels, theme);
			column.Add(roleDropdown.FixedHeight(28f));
			LabHoverInfo.Bind(roleDropdown.GameObject, "character.create.Role");
			column.Add(UiLabel.Create(column.ContentTransform,
				"Companion and Legend are playable roster heroes. Enemy / Summon is a non-playable unit.",
				theme, 11).FixedHeight(36f));
		}

		/// <summary>Validates the form and stores <see cref="Pending"/>. Returns an error, or null to create.</summary>
		internal static string Commit()
		{
			string error = LabSlugCreateFields.Validate(nameField, slugField, aliasField, "character", out string name, out string slug, out string alias);
			if (error != null)
			{
				return error == "Enter a name." ? "Enter a character name." : error;
			}

			string description = descriptionField != null
				? (descriptionField.InputField.text ?? string.Empty).Trim()
				: string.Empty;
			int role = roleDropdown != null ? roleDropdown.Dropdown.value : 0;
			Pending = new CharacterCreateRequest
			{
				Name = name,
				Slug = slug,
				Alias = alias,
				Description = description,
				EntityType = role == 2 ? CharacterEntityType.EnemySummon : CharacterEntityType.Hero,
				Tier = role == 1 ? CharacterTier.Legend : CharacterTier.Companion
			};
			return null;
		}

		/// <summary>Drops a committed request after scaffolding consumes it.</summary>
		internal static void ClearPending()
		{
			Pending = null;
		}
	}
}
