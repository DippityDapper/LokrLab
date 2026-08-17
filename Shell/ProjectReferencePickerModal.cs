using System;
using System.Collections.Generic;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Shell
{
	/// <summary>Shared cross-project picker. Encounter Add Combatant is the first caller.</summary>
	/// <remarks>
	/// <see cref="LokrLabApi.LokrLabApi.PickProjectReference"/> is synchronous and cannot wait on a
	/// click. This modal is the async picker §2.5 described; callers pass a callback.
	/// </remarks>
	internal static class ProjectReferencePickerModal
	{
		private static UiModal modal;
		private static UiLabel headerLabel;
		private static UiStack list;
		private static UiLabel emptyLabel;
		private static Action<ProjectReference> onPicked;

		/// <summary>Lists projects of <paramref name="projectTypeId"/>. Cancel or pick closes the modal.</summary>
		internal static void Show(string projectTypeId, Action<ProjectReference> picked)
		{
			onPicked = picked;
			if (!EnsureModal())
			{
				return;
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(projectTypeId);
			if (headerLabel != null)
			{
				headerLabel.SetText(type != null ? "Choose " + type.DisplayName : "Choose project");
			}

			Rebuild(projectTypeId);
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
			modal = UiModal.Create(canvas, theme, "Choose project", 560f, 480f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);
			headerLabel = UiLabel.Create(content.ContentTransform, "Choose project", theme, 13);
			content.Add(headerLabel.FixedHeight(22f));
			emptyLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			content.Add(emptyLabel.FixedHeight(36f));
			list = UiStack.Vertical(content.ContentTransform, theme, spacing: 2f, padding: 0f, scrollable: true);
			content.Add(list.Grow());
			content.Add(UiButton.Create(content.ContentTransform, "Cancel", Hide, theme, primary: false)
				.FixedHeight(32f));
			return true;
		}

		private static void Rebuild(string projectTypeId)
		{
			list.Clear();
			List<ProjectReference> rows = ProjectBrowser.ListProjectReferences(projectTypeId);
			emptyLabel.SetText(rows.Count == 0
				? "No projects of this type are on disk."
				: string.Empty);
			emptyLabel.Visible(rows.Count == 0);
			for (int i = 0; i < rows.Count; i++)
			{
				ProjectReference row = rows[i];
				string label = row.DisplayName;
				if (!string.Equals(row.DisplayName, row.ProjectId, StringComparison.Ordinal))
				{
					label = row.DisplayName + "  (" + row.ProjectId + ")";
				}

				list.Add(UiButton.Create(list.ContentTransform, label, () => Choose(row), primary: false)
					.FixedHeight(26f));
			}
		}

		private static void Choose(ProjectReference reference)
		{
			Action<ProjectReference> callback = onPicked;
			Hide();
			callback?.Invoke(reference);
		}

		private static void Hide()
		{
			onPicked = null;
			if (modal != null)
			{
				modal.Hide();
			}
		}
	}
}
