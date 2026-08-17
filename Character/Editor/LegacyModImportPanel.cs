using System.Collections.Generic;
using System.IO;
using LokrAbilityLab;
using LokrAbilityLab.Projects;
using LokrLab.Editor.General;
using LokrLabApi;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Scan → selection sheet → Confirm writes only checked rows, then a result modal.</summary>
	/// <remarks>
	/// Replaces the old one-shot Import. Pack roots and multi-block files show as separate rows.
	/// Built lazily on Lab.Canvas so Project Browser Import works without the Load screen.
	/// Closing a successful result opens the first imported hero as a Character project
	/// (SwitchToHome used to show the empty shell with no CurrentSession). The ability
	/// library field creates a new library when the typed name matches none.
	/// </remarks>
	internal static class LegacyModImportPanel
	{
		private static UiModal pickerModal;
		private static UiModal resultModal;
		private static UiLabel scanLabel;
		private static UiLabel skippedLabel;
		private static UiLabel warningLabel;
		private static UiLabel resultLabel;
		private static UiComboBox libraryField;
		private static UiList<LegacyPackItem> heroList;
		private static UiList<LegacyPackItem> abilityList;
		private static UiList<LegacyPackItem> summonList;
		private static LegacyPackScanResult currentScan;
		private static readonly List<string> libraryFolders = new List<string>();
		private static bool lastImportSucceeded;
		private static string lastHeroFolder;

		/// <summary>No-op kept so LoadWorkstationScene.Build still compiles; modals are created on first RunImport.</summary>
		internal static void Build(Transform screenRoot, Font font)
		{
		}

		/// <summary>Scans the folder and opens the selection sheet, or the result modal on a failed scan.</summary>
		internal static void RunImport(string legacyModFolder)
		{
			LegacyPackScanResult scan = LegacyPackScan.Scan(legacyModFolder);
			if (!scan.Success)
			{
				ShowResult(false, scan.Message, null);
				return;
			}

			currentScan = scan;
			if (!EnsurePicker())
			{
				LegacyModImporter.ImportResult fallback = LegacyModImporter.Import(scan);
				ShowResult(fallback.Success, FormatResult(fallback), fallback.CharacterFolder);
				return;
			}

			RebuildPicker();
			pickerModal.Show();
		}

		/// <summary>Drops modal refs after the lab scene is torn down.</summary>
		internal static void ResetSession()
		{
			pickerModal = null;
			resultModal = null;
			scanLabel = null;
			skippedLabel = null;
			warningLabel = null;
			resultLabel = null;
			libraryField = null;
			heroList = null;
			abilityList = null;
			summonList = null;
			currentScan = null;
			libraryFolders.Clear();
			lastImportSucceeded = false;
			lastHeroFolder = null;
		}

		private static bool EnsurePicker()
		{
			if (pickerModal != null && pickerModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			pickerModal = UiModal.Create(canvas, theme, "Import Legacy Pack", 1152f, 720f);
			UiStack content = UiStack.Vertical(pickerModal.ContentParent, theme, spacing: 6f, padding: 12f);
			pickerModal.Add(content);

			scanLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 13, TextAnchor.UpperLeft);
			content.Add(scanLabel.FixedHeight(36f));
			skippedLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			content.Add(skippedLabel.FixedHeight(28f));

			UiStack selectRow = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			selectRow.Add(UiButton.Create(selectRow.ContentTransform, "Select all", () => SetAllSelected(true), theme, primary: false).Grow());
			selectRow.Add(UiButton.Create(selectRow.ContentTransform, "Select none", () => SetAllSelected(false), theme, primary: false).Grow());
			content.Add(selectRow.FixedHeight(28f));

			content.Add(UiLabel.Create(content.ContentTransform, "Heroes", theme, 13).FixedHeight(18f));
			heroList = UiList<LegacyPackItem>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			content.Add(heroList.Grow());
			content.Add(UiLabel.Create(content.ContentTransform, "Abilities", theme, 13).FixedHeight(18f));
			abilityList = UiList<LegacyPackItem>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			content.Add(abilityList.Grow());
			content.Add(UiLabel.Create(content.ContentTransform, "Summons / props", theme, 13).FixedHeight(18f));
			summonList = UiList<LegacyPackItem>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			content.Add(summonList.Grow());

			content.Add(UiLabel.Create(content.ContentTransform, "Ability library (pick one, or type a new name)", theme, 12).FixedHeight(18f));
			libraryField = UiComboBox.Create(content.ContentTransform, new string[0], string.Empty, theme);
			libraryField.OnEndEdit(OnLibraryPicked);
			content.Add(libraryField.FixedHeight(28f));
			LabHoverInfo.Bind(libraryField.GameObject, "character.import.AbilityLibrary");
			LabHoverInfo.Bind(content.GameObject, "character.import.Sheet");

			warningLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			content.Add(warningLabel.FixedHeight(40f));

			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Import selected", OnConfirm, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", pickerModal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void RebuildPicker()
		{
			scanLabel.SetText(currentScan.Message + "\n" + currentScan.RootFolder);
			skippedLabel.SetText(currentScan.SkippedNotes.Count == 0
				? string.Empty
				: string.Join(" ", currentScan.SkippedNotes.ToArray()));
			heroList.SetItems(currentScan.Heroes, item => item.Key, (parent, item) => BuildRow(parent, item, "character.import.Hero"));
			abilityList.SetItems(currentScan.Abilities, item => item.Key, (parent, item) => BuildRow(parent, item, "character.import.Ability"));
			summonList.SetItems(currentScan.Summons, item => item.Key, (parent, item) => BuildRow(parent, item, "character.import.Summon"));
			FillLibraryCombo();
			RefreshWarning();
		}

		private static UiElement BuildRow(Transform parent, LegacyPackItem item, string hoverKey)
		{
			string label = RowLabel(item);
			UiToggle toggle = UiToggle.Create(parent, label, item.Selected && string.IsNullOrEmpty(item.ParseError));
			if (!string.IsNullOrEmpty(item.ParseError))
			{
				toggle.Toggle.interactable = false;
			}

			toggle.OnValueChanged(value =>
			{
				item.Selected = value;
				RefreshWarning();
			});
			LabHoverInfo.Bind(toggle.GameObject, hoverKey);
			return toggle.FixedHeight(26f);
		}

		private static string RowLabel(LegacyPackItem item)
		{
			string file = Path.GetFileName(item.SourceFile);
			string source = Path.GetFileName(item.SourceFolder);
			string label = item.DisplayName + "  (" + item.BlockKey + ")  —  " + source + "/" + file;
			if (!string.IsNullOrEmpty(item.ParseError))
			{
				return label + "  [parse error: " + item.ParseError + "]";
			}

			if (!string.IsNullOrEmpty(item.CollisionNote))
			{
				return label + "  [" + item.CollisionNote + "]";
			}

			return label;
		}

		private static void SetAllSelected(bool selected)
		{
			if (currentScan == null)
			{
				return;
			}

			SetList(currentScan.Heroes, selected);
			SetList(currentScan.Abilities, selected);
			SetList(currentScan.Summons, selected);
			RebuildPicker();
		}

		private static void SetList(List<LegacyPackItem> items, bool selected)
		{
			for (int i = 0; i < items.Count; i++)
			{
				if (string.IsNullOrEmpty(items[i].ParseError))
				{
					items[i].Selected = selected;
				}
			}
		}

		private static void FillLibraryCombo()
		{
			libraryFolders.Clear();
			List<string> names = new List<string>();
			foreach (string folder in AbilityLabPaths.EnumerateLibraryFolders())
			{
				libraryFolders.Add(folder);
				names.Add(AbilityLibrarySession.ReadDisplayName(folder) ?? Path.GetFileName(folder));
			}

			libraryField.SetOptions(names.ToArray());
			string current = currentScan.AbilityLibraryFolder;
			for (int i = 0; i < libraryFolders.Count; i++)
			{
				string name = names[i];
				if (string.Equals(libraryFolders[i], current, System.StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, current, System.StringComparison.OrdinalIgnoreCase))
				{
					currentScan.AbilityLibraryFolder = libraryFolders[i];
					libraryField.SetText(name);
					return;
				}
			}

			if (string.IsNullOrEmpty(current))
			{
				current = LegacyModImporter.SuggestAbilityLibraryName(currentScan.RootFolder);
			}

			currentScan.AbilityLibraryFolder = current;
			libraryField.SetText(current);
		}

		private static void OnLibraryPicked(string displayName)
		{
			if (currentScan == null)
			{
				return;
			}

			string trimmed = (displayName ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(trimmed))
			{
				currentScan.AbilityLibraryFolder = LegacyModImporter.SuggestAbilityLibraryName(currentScan.RootFolder);
				return;
			}

			for (int i = 0; i < libraryFolders.Count; i++)
			{
				string name = AbilityLibrarySession.ReadDisplayName(libraryFolders[i]) ?? Path.GetFileName(libraryFolders[i]);
				if (string.Equals(name, trimmed, System.StringComparison.OrdinalIgnoreCase)
					|| string.Equals(libraryFolders[i], trimmed, System.StringComparison.OrdinalIgnoreCase)
					|| string.Equals(Path.GetFileName(libraryFolders[i]), trimmed, System.StringComparison.OrdinalIgnoreCase))
				{
					currentScan.AbilityLibraryFolder = libraryFolders[i];
					return;
				}
			}

			currentScan.AbilityLibraryFolder = trimmed;
		}

		private static void RefreshWarning()
		{
			if (warningLabel == null)
			{
				return;
			}

			string text = LegacyPackScan.UncheckedSummonWarning(currentScan) ?? string.Empty;
			warningLabel.SetText(text);
			warningLabel.SetColor(string.IsNullOrEmpty(text) ? Color.white : new Color(1f, 0.85f, 0.4f));
		}

		private static void OnConfirm()
		{
			if (currentScan == null)
			{
				return;
			}

			OnLibraryPicked(libraryField != null ? libraryField.InputField.text : null);
			LegacyModImporter.ImportResult result = LegacyModImporter.Import(currentScan);
			if (pickerModal != null)
			{
				pickerModal.Hide();
			}

			ShowResult(result.Success, FormatResult(result), result.CharacterFolder);
		}

		private static string FormatResult(LegacyModImporter.ImportResult result)
		{
			string text = result.Message;
			if (result.Warnings.Count > 0)
			{
				text += "\n\nNotes:";
				for (int i = 0; i < result.Warnings.Count; i++)
				{
					text += "\n- " + result.Warnings[i];
				}
			}

			return text;
		}

		private static void ShowResult(bool success, string text, string heroFolder)
		{
			if (!EnsureResult())
			{
				return;
			}

			resultLabel.SetText(text);
			resultLabel.SetColor(success ? Color.white : new Color(1f, 0.5f, 0.5f));
			lastImportSucceeded = success;
			lastHeroFolder = heroFolder;
			if (success)
			{
				LoadWorkstationScene.Refresh();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}

			resultModal.Show();
		}

		private static bool EnsureResult()
		{
			if (resultModal != null && resultModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			resultModal = UiModal.Create(canvas, theme, "Import Legacy Pack", 1152f, 500f);
			UiStack content = UiStack.Vertical(resultModal.ContentParent, theme, spacing: 8f, padding: 12f);
			resultModal.Add(content);
			resultLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 13, TextAnchor.UpperLeft);
			content.Add(resultLabel.Grow());
			content.Add(UiButton.Create(content.ContentTransform, "Close", CloseResult, theme, primary: false).FixedHeight(32f));
			return true;
		}

		private static void CloseResult()
		{
			if (resultModal != null)
			{
				resultModal.Hide();
			}

			if (lastImportSucceeded && !string.IsNullOrEmpty(lastHeroFolder))
			{
				string folder = lastHeroFolder;
				lastImportSucceeded = false;
				lastHeroFolder = null;
				LokrLabApi.LokrLabApi.JumpToProject(LokrLabApi.LokrLabApi.CharacterTypeId, folder, null);
				return;
			}

			lastImportSucceeded = false;
			lastHeroFolder = null;
		}
	}
}
