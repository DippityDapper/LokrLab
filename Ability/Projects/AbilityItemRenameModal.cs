using System;
using LokrAbilityLab.Editor;
using LokrCharacterLoader;
using LokrLab;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Slug / alias / Auto sheet that ports a leftover ability folder onto <c>slug_token</c>.</summary>
	internal static class AbilityItemRenameModal
	{
		private static UiModal modal;
		private static UiStack sheetHost;
		private static UiLabel errorLabel;
		private static UiTextField slugField;
		private static UiTextField aliasField;
		private static string libraryFolder;
		private static string oldId;
		private static Action<string> onRenamed;

		/// <summary>Shows the rename sheet. <paramref name="renamed"/> receives the new ability id.</summary>
		internal static void Show(string library, string abilityId, Action<string> renamed)
		{
			libraryFolder = library;
			oldId = abilityId;
			onRenamed = renamed;
			if (string.IsNullOrEmpty(library) || string.IsNullOrEmpty(abilityId))
			{
				return;
			}

			if (LabSlugIds.LooksLikeSlugTokenId(abilityId))
			{
				return;
			}

			if (!EnsureModal())
			{
				return;
			}

			sheetHost.Clear();
			string abilityFolder = AbilityLabPaths.AbilityFolder(library, abilityId);
			string initialSlug = LabSlugIds.LegalizeSlug(abilityId, "ability");
			string initialAlias = LabAliases.FindKeyForId(LabAliases.Load(abilityFolder), abilityId) ?? initialSlug;
			LabSlugCreateFields.BuildRename(
				sheetHost.ContentTransform,
				"ability",
				abilityId,
				initialSlug,
				initialAlias,
				out slugField,
				out aliasField,
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
			modal = UiModal.Create(canvas, theme, "Rename Ability", 560f, 360f);
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
				slugField, aliasField, "ability", oldId, out string slug, out string alias);
			if (error != null)
			{
				errorLabel.SetText(error);
				return;
			}

			if (!AbilityIdentityRekey.TryApplyToSlugToken(libraryFolder, oldId, slug, alias, out string newId, out error))
			{
				errorLabel.SetText(error);
				return;
			}

			modal.Hide();
			Action<string> callback = onRenamed;
			onRenamed = null;
			callback?.Invoke(newId);
			AbilityLibraryViewport.Invalidate();
			LokrLabApi.LokrLabApi.JumpToProject(
				LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
				libraryFolder,
				"ability:" + newId);
		}
	}
}
