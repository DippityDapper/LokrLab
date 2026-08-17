using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LokrLab.Editor.General;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Load workstation panel that lists every subfolder under CharactersRoot so the user can open a character without browsing the filesystem.</summary>
	internal static class CharacterFolderScanPanel
	{
		private static readonly Rect Region = EditorLayout.LoadBrowseRegion;

		private static UiList<string> folderList;
		private static UiLabel emptyLabel;
		private static UiLabel countLabel;

		/// <summary>Builds the scrollable folder list.</summary>
		internal static void Build(Transform screenRoot, Font labelFont)
		{
			UiPanel panel = UiPanel.Create(screenRoot, CharacterLabUi.NestedPanelTheme, "All Characters", region: Region);
			UiStack content = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			panel.Add(content);

			countLabel = UiLabel.Create(content.ContentTransform, "Scanning...");
			content.Add(countLabel.FixedHeight(20f));

			emptyLabel = UiLabel.Create(content.ContentTransform, "No characters yet — create one from the panel on the left.");
			content.Add(emptyLabel);

			folderList = UiList<string>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			folderList.Grow();
			content.Add(folderList);
		}

		/// <summary>Rescans CharactersRoot and rebuilds the list rows.</summary>
		internal static void Refresh()
		{
			if (folderList == null)
			{
				return;
			}

			List<string> folders = ScanCharacterFolders();
			countLabel.SetText(folders.Count == 1 ? "1 character on disk" : folders.Count + " characters on disk");
			emptyLabel.Visible(folders.Count == 0);
			folderList.SetItems(folders, folder => folder, BuildFolderRow);
		}

		/// <summary>Returns every immediate subfolder of CharactersRoot, sorted by display name.</summary>
		internal static List<string> ScanCharacterFolders()
		{
			CharacterLabPaths.EnsureFoldersExist();
			if (!Directory.Exists(CharacterLabPaths.CharactersRoot))
			{
				return new List<string>();
			}

			return Directory.GetDirectories(CharacterLabPaths.CharactersRoot)
				.OrderBy(FormatFolderLabel, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static UiElement BuildFolderRow(Transform parent, string folder)
		{
			return UiButton.Create(parent, FormatFolderLabel(folder),
				() => OnLoadClicked(folder), primary: false).FixedHeight(26f);
		}

		private static void OnLoadClicked(string folder)
		{
			HomeWorkstationScene.OnLoadCharacterSelected(folder);
			Lab.SwitchToHome();
		}

		/// <summary>Display name from character.json plus the folder id, e.g. "Onagro (7602525401223468973)".</summary>
		internal static string FormatFolderLabel(string folder)
		{
			CharacterProfile profile = CharacterProfileSidecar.Load(folder);
			string name = string.IsNullOrEmpty(profile.Name) ? profile.Id : profile.Name;
			string id = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(id))
			{
				id = folder;
			}
			return name + " (" + id + ")";
		}
	}
}
