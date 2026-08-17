using System;
using System.Collections.Generic;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>"Replace..." popup for InspectorPanel's Part section, listing every other currently-loaded part. Choosing one calls RigEditorScene.MassReplacePart(oldPart, chosen) directly; this panel is pure UI, no part-swap logic of its own.</summary>
	internal static class ReplacePartPickerPanel
	{
		private static UiModal modal;
		private static UiLabel titleLabel;
		private static UiStack list;
		private static UiLabel emptyLabel;
		private static DraggablePart oldPart;

		/// <summary>Builds the picker modal.</summary>
		internal static void Build(Transform canvas, Font labelFont)
		{
			modal = UiModal.Create(canvas, UiTheme.Default, null, 1152f, 756f);
			UiStack content = UiStack.Vertical(modal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			modal.Add(content);

			titleLabel = UiLabel.Create(content.ContentTransform, "Replace with which part?", UiTheme.Default, UiTheme.Default.TitleFontSize);
			content.Add(titleLabel.FixedHeight(26f));

			emptyLabel = UiLabel.Create(content.ContentTransform, "No other loaded parts to replace with — load a rig with more than one part first.");
			content.Add(emptyLabel);

			list = UiStack.Vertical(content.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: true);
			list.Grow();
			content.Add(list);

			content.Add(UiButton.Create(content.ContentTransform, "Cancel", Close, primary: false).FixedHeight(32f));
		}

		/// <summary>Opens the picker for the given part.</summary>
		internal static void Open(DraggablePart partToReplace)
		{
			if (partToReplace == null)
			{
				return;
			}
			oldPart = partToReplace;
			titleLabel.SetText("Replace '" + partToReplace.PartName + "' with which part?");
			RefreshList();
			modal.Show();
		}

		private static void Close()
		{
			modal.Hide();
			oldPart = null;
		}

		private static void RefreshList()
		{
			list.Clear();

			List<DraggablePart> options = new List<DraggablePart>();
			foreach (DraggablePart part in RigEditorScene.LoadedParts)
			{
				if (part != null && part != oldPart)
				{
					options.Add(part);
				}
			}
			options.Sort((a, b) => string.Compare(a.PartName, b.PartName, StringComparison.OrdinalIgnoreCase));

			emptyLabel.Visible(options.Count == 0);
			foreach (DraggablePart part in options)
			{
				DraggablePart captured = part;
				UiButton row = UiButton.Create(list.ContentTransform, part.PartName, () => Choose(captured), primary: false).FixedHeight(24f);
				list.Add(row);
			}
		}

		private static void Choose(DraggablePart newPart)
		{
			DraggablePart captured = oldPart;
			Close();
			RigEditorScene.MassReplacePart(captured, newPart);
		}
	}
}
