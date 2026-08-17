using System.Collections.Generic;
using LokrLab.Editor.General;
using LokrLab.Projects;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Editor
{
	/// <summary>File → Edit Vanilla Hero list over live UniqueIds.</summary>
	internal static class VanillaHeroPickerModal
	{
		private static UiModal modal;
		private static UiStack list;
		private static UiLabel emptyLabel;
		private static UiLabel statusLabel;

		/// <summary>Shows shipped heroes. Picking one extracts or opens the Lab override.</summary>
		internal static void Show()
		{
			if (!EnsureModal())
			{
				return;
			}

			Rebuild();
			modal.Show();
		}

		private static bool EnsureModal()
		{
			if (modal != null && modal.GameObject != null)
			{
				return true;
			}

			Transform canvas = LokrLab.Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			modal = UiModal.Create(canvas, theme, "Edit Vanilla Hero", 560f, 520f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);
			content.Add(UiLabel.Create(
				content.ContentTransform,
				"Opens a shipped hero as a Lab override. Campaign and saves still use the same id.",
				theme,
				12,
				TextAnchor.UpperLeft).FixedHeight(40f));
			emptyLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			content.Add(emptyLabel.FixedHeight(28f));
			list = UiStack.Vertical(content.ContentTransform, theme, spacing: 2f, padding: 0f, scrollable: true);
			content.Add(list.Grow());
			statusLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			content.Add(statusLabel.FixedHeight(28f));
			content.Add(UiButton.Create(content.ContentTransform, "Cancel", Hide, theme, primary: false)
				.FixedHeight(32f));
			LabHoverInfo.Bind(modal.GameObject, "character.file.EditVanillaHero");
			return true;
		}

		private static void Rebuild()
		{
			list.Clear();
			statusLabel.SetText(string.Empty);
			List<VanillaHeroRow> rows = VanillaCharacterExtract.ListVanillaHeroes();
			emptyLabel.SetText(rows.Count == 0
				? "No shipped heroes are loaded yet."
				: string.Empty);
			emptyLabel.Visible(rows.Count == 0);
			for (int i = 0; i < rows.Count; i++)
			{
				VanillaHeroRow row = rows[i];
				string label = row.DisplayName;
				if (!string.Equals(row.DisplayName, row.UniqueId, System.StringComparison.Ordinal))
				{
					label = row.DisplayName + "  (" + row.UniqueId + ")";
				}

				if (!string.IsNullOrEmpty(row.ExistingFolder))
				{
					label += "  — Lab override";
				}

				VanillaHeroRow captured = row;
				list.Add(UiButton.Create(list.ContentTransform, label, () => Choose(captured), primary: false)
					.FixedHeight(26f));
			}
		}

		private static void Choose(VanillaHeroRow row)
		{
			string status = null;
			if (row == null || !VanillaCharacterExtract.TryOpenOrCreate(row.UniqueId, out string folder, out status))
			{
				if (statusLabel != null)
				{
					statusLabel.SetText(status ?? "Could not open that hero.");
				}

				return;
			}

			Hide();
			CharacterLabScene.OpenProjectFromApi(CharacterProjectType.Id, folder, null);
		}

		private static void Hide()
		{
			if (modal != null)
			{
				modal.Hide();
			}
		}
	}
}
