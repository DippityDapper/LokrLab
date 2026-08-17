using System;
using LokrAbilityLab.Editor;
using LokrLab;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>New Ability fill-out: name, slug, alias, Auto, and id preview. Token is minted on confirm.</summary>
	internal static class AbilityItemCreateModal
	{
		private static UiModal modal;
		private static UiStack sheetHost;
		private static UiLabel errorLabel;
		private static string libraryFolder;
		private static Action<string> onCreated;

		/// <summary>Shows the sheet for one library. <paramref name="created"/> receives the new ability id.</summary>
		internal static void Show(string library, Action<string> created)
		{
			libraryFolder = library;
			onCreated = created;
			if (!EnsureModal())
			{
				return;
			}

			sheetHost.Clear();
			AbilityItemCreateSheet.Build(sheetHost.ContentTransform);
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
			modal = UiModal.Create(canvas, theme, "New Ability", 560f, 400f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);
			sheetHost = UiStack.Vertical(content.ContentTransform, theme, spacing: 0f, padding: 0f);
			content.Add(sheetHost.Grow());
			errorLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12);
			content.Add(errorLabel.FixedHeight(22f));
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Create", OnConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", modal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void OnConfirmed()
		{
			string error = AbilityItemCreateSheet.Commit();
			if (!string.IsNullOrEmpty(error))
			{
				errorLabel.SetText(error);
				return;
			}

			AbilityItemCreateRequest request = AbilityItemCreateSheet.TakePending();
			if (request == null || string.IsNullOrEmpty(libraryFolder))
			{
				errorLabel.SetText("No ability library is open.");
				return;
			}

			string id = AbilityLabPaths.GenerateNewAbilityId(request.Slug);
			if (!AbilityListPanel.TryCreateAbility(libraryFolder, id, out error, request.Name))
			{
				errorLabel.SetText(error);
				return;
			}

			string alias = !string.IsNullOrEmpty(request.Alias) ? request.Alias : request.Slug;
			LokrCharacterLoader.LabAliases.SeedSelf(
				AbilityLabPaths.AbilityFolder(libraryFolder, id),
				alias,
				id);
			modal.Hide();
			Action<string> callback = onCreated;
			onCreated = null;
			callback?.Invoke(id);
			LokrLabApi.LokrLabApi.RequestRefresh();
		}
	}
}
