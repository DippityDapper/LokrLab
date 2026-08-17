using System;
using LokrLab;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Slug / Auto sheet that ports a leftover Ability Library folder onto <c>slug_token</c>.</summary>
	internal static class AbilityLibraryRenameModal
	{
		private static UiModal modal;
		private static UiStack sheetHost;
		private static UiLabel errorLabel;
		private static UiTextField slugField;
		private static string libraryFolder;
		private static string oldId;
		private static string displayName;

		/// <summary>Shows the rename sheet for the open library folder.</summary>
		internal static void Show(string folder, string id, string name)
		{
			libraryFolder = folder;
			oldId = id;
			displayName = name;
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(id)
				|| LabSlugIds.LooksLikeSlugTokenId(id))
			{
				return;
			}

			if (!EnsureModal())
			{
				return;
			}

			sheetHost.Clear();
			string initialSlug = LabSlugIds.LegalizeSlug(
				!string.IsNullOrEmpty(displayName) ? displayName : oldId,
				"library");
			LabSlugCreateFields.BuildRename(
				sheetHost.ContentTransform,
				"library",
				displayName,
				initialSlug,
				initialSlug,
				out slugField,
				out _,
				out _,
				out _,
				out _);
			errorLabel.SetText(string.Empty);
			modal.Show();
		}

		private static bool EnsureModal()
		{
			if (modal != null && modal.GameObject != null)
			{
				return true;
			}

			Transform canvas = Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			modal = UiModal.Create(canvas, theme, "Rename Library", 560f, 360f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);
			sheetHost = UiStack.Vertical(content.ContentTransform, theme, spacing: 0f, padding: 0f);
			content.Add(sheetHost.Grow());
			errorLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12);
			content.Add(errorLabel.FixedHeight(22f));
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Rename", OnConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", modal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void OnConfirmed()
		{
			string error = LabSlugCreateFields.ValidateRename(
				slugField, null, "library", displayName, out string slug, out _);
			if (error != null)
			{
				errorLabel.SetText(error);
				return;
			}

			if (!AbilityLibraryIdentityRekey.TryApplyToSlugToken(libraryFolder, slug, out string newFolder, out error))
			{
				errorLabel.SetText(error);
				return;
			}

			modal.Hide();
			AbilityLibraryViewport.Invalidate();
			CharacterLabScene.ReloadOpenProject(
				LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
				newFolder,
				"ability-library");
		}
	}
}
