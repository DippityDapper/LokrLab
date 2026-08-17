using System;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab
{
	/// <summary>Name / slug / alias / Auto / id-preview rows used by Character, Ability, and Encounter create sheets.</summary>
	internal static class LabSlugCreateFields
	{
		/// <summary>Builds the shared identity fields. Slug Auto fills from name; Alias Auto copies the slug.</summary>
		/// <param name="hoverPrefix">Hover-info key prefix (e.g. character.create). Null skips binds.</param>
		internal static void Build(
			Transform parent,
			string nameLabel,
			string emptyFallback,
			out UiTextField nameField,
			out UiTextField slugField,
			out UiTextField aliasField,
			out UiToggle autoSlug,
			out UiToggle autoAlias,
			out UiLabel previewLabel,
			string hoverPrefix = null)
		{
			UiTheme theme = UiTheme.Default;
			UiStack column = UiStack.Vertical(parent, theme, spacing: 6f, padding: 0f);
			column.Add(UiLabel.Create(column.ContentTransform, nameLabel, theme, 13).FixedHeight(20f));
			nameField = UiTextField.Create(column.ContentTransform, string.Empty, theme);
			column.Add(nameField.FixedHeight(28f));
			if (!string.IsNullOrEmpty(hoverPrefix))
			{
				LabHoverInfo.Bind(nameField.GameObject, hoverPrefix + ".Name");
			}

			BuildSlugAndAliasRows(column, theme, emptyFallback, string.Empty, string.Empty,
				out slugField, out aliasField, out autoSlug, out autoAlias, out previewLabel, hoverPrefix);
			WireCreate(nameField, slugField, aliasField, autoSlug, autoAlias, previewLabel, emptyFallback);
		}

		/// <summary>Slug / alias / Auto / preview for leftover-id rename. Slug Auto fills from the display name.</summary>
		internal static void BuildRename(
			Transform parent,
			string emptyFallback,
			string displayName,
			string initialSlug,
			string initialAlias,
			out UiTextField slugField,
			out UiTextField aliasField,
			out UiToggle autoSlug,
			out UiToggle autoAlias,
			out UiLabel previewLabel)
		{
			UiTheme theme = UiTheme.Default;
			UiStack column = UiStack.Vertical(parent, theme, spacing: 6f, padding: 0f);
			string slug = LabSlugIds.IsLegalSlug(initialSlug)
				? initialSlug
				: LabSlugIds.LegalizeSlug(displayName, emptyFallback);
			string alias = LabSlugIds.IsLegalSlug(initialAlias) ? initialAlias : slug;
			BuildSlugAndAliasRows(column, theme, emptyFallback, slug, alias,
				out slugField, out aliasField, out autoSlug, out autoAlias, out previewLabel, null);
			autoSlug.SetValueSilently(true);
			autoAlias.SetValueSilently(string.Equals(alias, slug, StringComparison.Ordinal));
			column.Add(UiLabel.Create(column.ContentTransform,
				"Rename moves the folder and rewrites engine keys. The token is minted when you confirm.",
				theme, 11).FixedHeight(32f));
			WireRename(displayName, slugField, aliasField, autoSlug, autoAlias, previewLabel, emptyFallback);
		}

		/// <summary>Validates name, slug, and alias. Empty alias falls back to slug.</summary>
		internal static string Validate(
			UiTextField nameField,
			UiTextField slugField,
			UiTextField aliasField,
			string emptyFallback,
			out string name,
			out string slug,
			out string alias)
		{
			name = nameField != null ? (nameField.InputField.text ?? string.Empty).Trim() : string.Empty;
			slug = slugField != null ? (slugField.InputField.text ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
			alias = aliasField != null ? (aliasField.InputField.text ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
			if (string.IsNullOrEmpty(name))
			{
				return "Enter a name.";
			}

			if (string.IsNullOrEmpty(slug))
			{
				slug = LabSlugIds.LegalizeSlug(name, emptyFallback);
			}

			if (!LabSlugIds.IsLegalSlug(slug))
			{
				return "Slug must start with a letter and use only lowercase letters, digits, and underscores.";
			}

			if (string.IsNullOrEmpty(alias))
			{
				alias = slug;
			}

			if (!LabSlugIds.IsLegalSlug(alias))
			{
				return "Alias must start with a letter and use only lowercase letters, digits, and underscores.";
			}

			return null;
		}

		/// <summary>Validates slug and alias on the rename strip. Empty alias falls back to slug.</summary>
		internal static string ValidateRename(
			UiTextField slugField,
			UiTextField aliasField,
			string emptyFallback,
			string displayName,
			out string slug,
			out string alias)
		{
			slug = slugField != null ? (slugField.InputField.text ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
			alias = aliasField != null ? (aliasField.InputField.text ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
			if (string.IsNullOrEmpty(slug))
			{
				slug = LabSlugIds.LegalizeSlug(displayName, emptyFallback);
			}

			if (!LabSlugIds.IsLegalSlug(slug))
			{
				return "Slug must start with a letter and use only lowercase letters, digits, and underscores.";
			}

			if (string.IsNullOrEmpty(alias))
			{
				alias = slug;
			}

			if (!LabSlugIds.IsLegalSlug(alias))
			{
				return "Alias must start with a letter and use only lowercase letters, digits, and underscores.";
			}

			return null;
		}

		private static void BuildSlugAndAliasRows(
			UiStack column,
			UiTheme theme,
			string emptyFallback,
			string initialSlug,
			string initialAlias,
			out UiTextField slugField,
			out UiTextField aliasField,
			out UiToggle autoSlug,
			out UiToggle autoAlias,
			out UiLabel previewLabel,
			string hoverPrefix)
		{
			UiStack slugRow = UiStack.Horizontal(column.ContentTransform, theme, spacing: 6f, padding: 0f);
			column.Add(slugRow.FixedHeight(28f));
			slugRow.Add(UiLabel.Create(slugRow.ContentTransform, "Slug", theme, 13).FixedWidth(44f));
			slugField = UiTextField.Create(slugRow.ContentTransform, initialSlug, theme);
			slugRow.Add(slugField.Grow());
			autoSlug = UiToggle.Create(slugRow.ContentTransform, "Auto", true, theme);
			slugRow.Add(autoSlug.FixedWidth(72f));
			if (!string.IsNullOrEmpty(hoverPrefix))
			{
				LabHoverInfo.Bind(slugRow.GameObject, hoverPrefix + ".Slug");
				LabHoverInfo.Bind(autoSlug.GameObject, hoverPrefix + ".SlugAuto");
			}

			UiStack aliasRow = UiStack.Horizontal(column.ContentTransform, theme, spacing: 6f, padding: 0f);
			column.Add(aliasRow.FixedHeight(28f));
			aliasRow.Add(UiLabel.Create(aliasRow.ContentTransform, "Alias", theme, 13).FixedWidth(44f));
			aliasField = UiTextField.Create(aliasRow.ContentTransform, initialAlias, theme);
			aliasRow.Add(aliasField.Grow());
			autoAlias = UiToggle.Create(aliasRow.ContentTransform, "Auto", true, theme);
			aliasRow.Add(autoAlias.FixedWidth(72f));
			if (!string.IsNullOrEmpty(hoverPrefix))
			{
				LabHoverInfo.Bind(aliasRow.GameObject, hoverPrefix + ".Alias");
				LabHoverInfo.Bind(autoAlias.GameObject, hoverPrefix + ".AliasAuto");
			}

			previewLabel = UiLabel.Create(column.ContentTransform, "Id: " + LabSlugIds.PreviewId(
				!string.IsNullOrEmpty(initialSlug) ? initialSlug : emptyFallback), theme, 11);
			column.Add(previewLabel.FixedHeight(20f));
			if (!string.IsNullOrEmpty(hoverPrefix))
			{
				LabHoverInfo.Bind(previewLabel.GameObject, hoverPrefix + ".IdPreview");
			}
			column.Add(UiLabel.Create(column.ContentTransform,
				"The folder and engine keys use this id. Files write $alias for the unique id.",
				theme, 11).FixedHeight(32f));
		}

		private static void WireCreate(
			UiTextField nameField,
			UiTextField slugField,
			UiTextField aliasField,
			UiToggle autoSlug,
			UiToggle autoAlias,
			UiLabel previewLabel,
			string emptyFallback)
		{
			nameField.InputField.onValueChanged.AddListener(value =>
			{
				if (autoSlug.Toggle.isOn)
				{
					slugField.SetText(LabSlugIds.LegalizeSlug(value, emptyFallback));
				}

				RefreshPreview(slugField, previewLabel, emptyFallback);
			});
			WireSlugAlias(slugField, aliasField, autoSlug, autoAlias, previewLabel, emptyFallback, () =>
				LabSlugIds.LegalizeSlug(nameField.InputField.text, emptyFallback));
		}

		private static void WireRename(
			string displayName,
			UiTextField slugField,
			UiTextField aliasField,
			UiToggle autoSlug,
			UiToggle autoAlias,
			UiLabel previewLabel,
			string emptyFallback)
		{
			WireSlugAlias(slugField, aliasField, autoSlug, autoAlias, previewLabel, emptyFallback, () =>
				LabSlugIds.LegalizeSlug(displayName, emptyFallback));
		}

		private static void WireSlugAlias(
			UiTextField slugField,
			UiTextField aliasField,
			UiToggle autoSlug,
			UiToggle autoAlias,
			UiLabel previewLabel,
			string emptyFallback,
			Func<string> slugFromName)
		{
			slugField.InputField.onValueChanged.AddListener(value =>
			{
				if (autoAlias.Toggle.isOn)
				{
					aliasField.SetText(LabSlugIds.IsLegalSlug(value) ? value.Trim().ToLowerInvariant() : LabSlugIds.LegalizeSlug(value, emptyFallback));
				}

				RefreshPreview(slugField, previewLabel, emptyFallback);
			});
			autoSlug.OnValueChanged(on =>
			{
				if (on)
				{
					slugField.SetText(slugFromName());
				}

				RefreshPreview(slugField, previewLabel, emptyFallback);
			});
			autoAlias.OnValueChanged(on =>
			{
				if (on)
				{
					string slug = slugField.InputField.text;
					aliasField.SetText(LabSlugIds.IsLegalSlug(slug)
						? slug.Trim().ToLowerInvariant()
						: LabSlugIds.LegalizeSlug(slug, emptyFallback));
				}
			});
		}

		private static void RefreshPreview(UiTextField slugField, UiLabel previewLabel, string emptyFallback)
		{
			if (previewLabel == null)
			{
				return;
			}

			string slug = slugField != null ? slugField.InputField.text : emptyFallback;
			previewLabel.SetText("Id: " + LabSlugIds.PreviewId(slug));
		}
	}
}
