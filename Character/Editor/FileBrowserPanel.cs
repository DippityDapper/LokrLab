using System;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Character Lab wrapper around <see cref="UiFileBrowser"/> for Save / Import / Atlas / portraits.</summary>
	internal static class FileBrowserPanel
	{
		/// <summary>Builds the shared SimpleUI file-browser modal on canvas if needed.</summary>
		internal static void EnsureBuilt(Transform canvas)
		{
			UiFileBrowser.EnsureModal(canvas);
		}

		/// <summary>Opens the browser in folder-select mode.</summary>
		internal static void OpenForFolder(string startingFolder, Action<string> onFolderSelected)
		{
			UiFileBrowser.PickFolder(Lab.Canvas, "Select a folder", startingFolder, onFolderSelected, CharacterPlaces());
		}

		/// <summary>Opens the browser in file-select mode. extensions are lowercase with a leading '.'.</summary>
		internal static void OpenForFile(string title, string startingPath, string[] extensions, Action<string> onFileSelected)
		{
			UiFileBrowser.PickFile(Lab.Canvas, title, startingPath, extensions, onFileSelected, CharacterPlaces());
		}

		/// <summary>Drops the shared modal when the lab scene is about to be destroyed.</summary>
		internal static void ResetSession()
		{
			UiFileBrowser.ReleaseModal();
		}

		private static UiFileBrowserPlace[] CharacterPlaces()
		{
			return new[]
			{
				new UiFileBrowserPlace("Mods", CharacterLabPaths.GameModsRoot),
				new UiFileBrowserPlace("LokrCharacterLab", CharacterLabPaths.CharactersRoot),
				new UiFileBrowserPlace("LokrLab", CharacterLabPaths.ModRoot)
			};
		}
	}
}
