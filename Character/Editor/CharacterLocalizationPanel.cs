using System.Collections.Generic;
using System.Linq;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Localization category: per-locale Name/Description overrides beyond the character's own English fields (General category).</summary>
	/// <remarks>Keyed by CharacterProfile.Localizations' own locale-suffix keys (see LocaleCodes.AllNonEnglish) -- English itself lives on CharacterProfile.Name/Description directly (General category), not as an entry here.</remarks>
	internal static class CharacterLocalizationPanel
	{
		private const string NoLocaleOption = "(all locales added)";

		private static UiList<string> localizationList;
		private static UiDropdown addLocaleDropdown;
		private static List<string> availableLocales;
		private static bool suppressFieldEvents;

		/// <summary>Builds the localization fields into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			section.Add(UiLabel.Create(section.ContentTransform, "Per-locale Name/Description overrides (English is the General category's own Name/Description):").FixedHeight(32f));

			localizationList = UiList<string>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			section.Add(localizationList);

			UiStack addRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(addRow.FixedHeight(30f));
			addLocaleDropdown = UiDropdown.Create(addRow.ContentTransform, new List<string> { NoLocaleOption });
			addRow.Add(addLocaleDropdown.Grow());
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

			List<string> locales = profile.Localizations.Keys.OrderBy(key => key).ToList();
			localizationList.SetItems(locales,
				locale => locale + "|" + profile.Localizations[locale].Name + "|" + profile.Localizations[locale].Description,
				(parent, locale) => BuildLocaleRow(parent, profile, locale));

			availableLocales = LocaleCodes.AllNonEnglish.Where(code => !profile.Localizations.ContainsKey(code)).ToList();
			addLocaleDropdown.SetOptions(availableLocales.Count > 0 ? availableLocales : new List<string> { NoLocaleOption });

			suppressFieldEvents = false;
		}

		private static UiElement BuildLocaleRow(Transform parent, CharacterProfile profile, string locale)
		{
			CharacterLocalizedText entry = profile.Localizations[locale];
			UiStack row = UiStack.Vertical(parent, UiTheme.Default, spacing: 2f, padding: 4f);

			UiStack header = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			header.Add(UiLabel.Create(header.ContentTransform, locale).Grow());
			header.Add(UiButton.Create(header.ContentTransform, "x", () => HomeWorkstationScene.RemoveLocalization(locale), primary: false).FixedWidth(28f));
			row.Add(header.FixedHeight(24f));

			UiTextField nameField = UiTextField.Create(row.ContentTransform, entry.Name);
			nameField.OnEndEdit(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetLocalizationName(locale, value); });
			row.Add(nameField.FixedHeight(28f));
			LabHoverInfo.Bind(nameField.GameObject, "character.localization.Name");

			UiTextField descriptionField = UiTextField.Create(row.ContentTransform, entry.Description, multiline: true);
			descriptionField.OnEndEdit(value => { if (!suppressFieldEvents) HomeWorkstationScene.SetLocalizationDescription(locale, value); });
			row.Add(descriptionField.FixedHeight(48f));
			LabHoverInfo.Bind(descriptionField.GameObject, "character.localization.Description");

			return row.FixedHeight(112f);
		}

		private static void OnAddClicked()
		{
			if (availableLocales == null || availableLocales.Count == 0)
			{
				return;
			}
			int index = addLocaleDropdown.Dropdown.value;
			if (index < 0 || index >= availableLocales.Count)
			{
				return;
			}
			HomeWorkstationScene.AddLocalization(availableLocales[index]);
		}
	}
}
