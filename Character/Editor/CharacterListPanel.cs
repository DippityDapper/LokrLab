using System.Collections.Generic;
using LokrLab;
using LokrLab.Projects;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Editor
{
	/// <summary>The Load workstation's one job: "Create New Character" / "Load Existing..." / "Import Legacy Mod..." buttons plus a scrollable recent-characters list.</summary>
	/// <remarks>Create opens the same Character fill-out sheet the Project Browser wizard uses, then hands off to HomeWorkstationScene; Import opens the legacy pack selection sheet, then the result modal. Recent rows show the display name plus the opaque folder id (e.g. "Onagro (1842...)"), not the raw folder name, with an x to drop an entry from RecentFilesStore without loading it.</remarks>
	internal static class CharacterListPanel
	{
		private static readonly Rect Region = EditorLayout.LoadActionsRegion;

		private static Transform screenRoot;
		private static UiList<string> recentList;
		private static UiLabel emptyLabel;
		private static UiModal createModal;
		private static UiStack createSheetHost;
		private static UiLabel createErrorLabel;

		/// <summary>Builds the Create/Load/Import buttons and recent-characters list.</summary>
		internal static void Build(Transform screenRoot, Font labelFont)
		{
			CharacterListPanel.screenRoot = screenRoot;
			createModal = null;
			createSheetHost = null;
			createErrorLabel = null;
			UiPanel panel = UiPanel.Create(screenRoot, CharacterLabUi.NestedPanelTheme, "Characters", region: Region);
			UiStack content = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			panel.Add(content);

			content.Add(UiButton.Create(content.ContentTransform, "+ Create New Character", OnCreateClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Load Existing...",
				() => FileBrowserPanel.OpenForFolder(CharacterLabPaths.CharactersRoot, OnLoadSelected), primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Import Legacy Mod...",
				() => FileBrowserPanel.OpenForFolder(CharacterLabPaths.GameModsRoot, LegacyModImportPanel.RunImport), primary: false).FixedHeight(32f));

			content.Add(UiLabel.Create(content.ContentTransform, "Recent:").FixedHeight(20f));

			emptyLabel = UiLabel.Create(content.ContentTransform, "No characters yet — create one above.");
			content.Add(emptyLabel);

			recentList = UiList<string>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			recentList.Grow();
			content.Add(recentList);
		}

		private static void OnCreateClicked()
		{
			if (!EnsureCreateModal())
			{
				HomeWorkstationScene.OnCreateCharacterConfirmed();
				Lab.SwitchToHome();
				return;
			}

			createSheetHost.Clear();
			CharacterCreateSheet.Build(createSheetHost.ContentTransform);
			createErrorLabel.SetText(string.Empty);
			createModal.Show();
		}

		private static bool EnsureCreateModal()
		{
			if (createModal != null && createModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = null;
			if (screenRoot != null)
			{
				Canvas found = screenRoot.GetComponentInParent<Canvas>();
				if (found != null)
				{
					canvas = found.transform;
				}
			}

			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			createModal = UiModal.Create(canvas, theme, "New Character", 640f, 660f);
			UiStack content = UiStack.Vertical(createModal.ContentParent, theme, spacing: 8f, padding: 12f);
			createModal.Add(content);
			createSheetHost = UiStack.Vertical(content.ContentTransform, theme, spacing: 0f, padding: 0f);
			content.Add(createSheetHost.Grow());
			createErrorLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12);
			content.Add(createErrorLabel.FixedHeight(22f));
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Create", OnCreateConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", createModal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void OnCreateConfirmed()
		{
			string error = CharacterCreateSheet.Commit();
			if (!string.IsNullOrEmpty(error))
			{
				if (createErrorLabel != null)
				{
					createErrorLabel.SetText(error);
				}

				return;
			}

			if (createModal != null)
			{
				createModal.Hide();
			}

			HomeWorkstationScene.OnCreateCharacterConfirmed();
			Lab.SwitchToHome();
		}

		private static void OnLoadSelected(string folder)
		{
			HomeWorkstationScene.OnLoadCharacterSelected(folder);
			Lab.SwitchToHome();
		}

		/// <summary>Rebuilds the recent-characters list as Name (id) rows with a remove button.</summary>
		/// <remarks>No-ops when the Load screen has not been built. Project Browser load/create still writes recents to disk first.</remarks>
		internal static void Refresh()
		{
			if (emptyLabel == null || recentList == null
				|| emptyLabel.GameObject == null || recentList.GameObject == null)
			{
				return;
			}

			IReadOnlyList<string> recent = HomeWorkstationScene.RecentCharacters;
			emptyLabel.Visible(recent.Count == 0);
			recentList.SetItems(recent, folder => folder, BuildRecentRow);
		}

		/// <summary>Drops widget refs after the lab scene is torn down.</summary>
		internal static void ResetSession()
		{
			emptyLabel = null;
			recentList = null;
			createModal = null;
			createSheetHost = null;
			createErrorLabel = null;
			screenRoot = null;
		}

		/// <summary>One recent character: load button labeled "Name (id)", plus an x that only removes it from the list.</summary>
		/// <remarks>Keyed by folder path so Refresh after a remove does not rebuild a different row mid-click. The x is a sibling of the load button, not a child, so it cannot also fire OnLoadSelected.</remarks>
		private static UiElement BuildRecentRow(Transform parent, string folder)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(UiButton.Create(row.ContentTransform, CharacterFolderScanPanel.FormatFolderLabel(folder),
				() => OnLoadSelected(folder), primary: false).Grow());
			row.Add(UiButton.Create(row.ContentTransform, "x",
				() => HomeWorkstationScene.RemoveRecentCharacter(folder), primary: false).FixedWidth(28f));
			return row.FixedHeight(26f);
		}
	}
}
