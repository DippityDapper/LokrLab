using System;
using System.IO;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Opens SimpleUI's file browser so Ability Lab can copy a PNG without a manual folder drop.</summary>
	internal static class AbilityFilePicker
	{
		private static string lastDirectory;

		internal static void PickPng(string title, string abilityFolder, Action<string> onPicked)
		{
			Transform canvas = ResolveCanvas();
			if (canvas == null)
			{
				AbilityEditorPanel.SetStatus("No canvas for the file browser.");
				return;
			}

			string start = FirstExisting(
				lastDirectory,
				UiFileBrowser.HostStartDirectory,
				abilityFolder,
				AbilityLabPaths.LibrariesRoot,
				AbilityLabPaths.ModRoot,
				Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

			UiFileBrowser.PickFile(canvas, title, start, new[] { ".png" }, path =>
			{
				if (string.IsNullOrEmpty(path))
				{
					return;
				}

				lastDirectory = Path.GetDirectoryName(path);
				onPicked(path);
			}, Places(abilityFolder));
		}

		private static UiFileBrowserPlace[] Places(string abilityFolder)
		{
			return new[]
			{
				new UiFileBrowserPlace("This ability", abilityFolder),
				new UiFileBrowserPlace("Ability Lab", AbilityLabPaths.LibrariesRoot),
				new UiFileBrowserPlace("Mods", AbilityLabPaths.ModRoot)
			};
		}

		private static Transform ResolveCanvas()
		{
			if (LokrLabApi.LokrLabApi.Host != null && LokrLabApi.LokrLabApi.Host.Canvas != null)
			{
				return LokrLabApi.LokrLabApi.Host.Canvas;
			}

			if (AbilityLabScene.Canvas != null)
			{
				return AbilityLabScene.Canvas;
			}

			return null;
		}

		private static string FirstExisting(params string[] paths)
		{
			foreach (string path in paths)
			{
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					return path;
				}
			}

			return null;
		}
	}
}
