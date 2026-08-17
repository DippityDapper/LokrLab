using System.Collections.Generic;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Undo history as a shell bottom panel, plus the legacy "History..." modal.</summary>
	/// <remarks>Pure UI, no history logic of its own. Rebuilds its row list on Refresh (and on Open for the modal). A plain Clear-then-rebuild is simpler than UiList key-diffing here.</remarks>
	internal static class EditHistoryPanel
	{
		private static UiModal modal;
		private static UiStack modalList;
		private static UiLabel modalEmpty;
		private static UiStack dockList;
		private static UiLabel dockEmpty;

		/// <summary>Builds the history modal.</summary>
		internal static void Build(Transform canvas, Font labelFont)
		{
			modal = UiModal.Create(canvas, UiTheme.Default, "Edit History — click an entry to jump to it", 1152f, 756f);
			UiStack content = UiStack.Vertical(modal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			modal.Add(content);

			modalEmpty = UiLabel.Create(content.ContentTransform, "No edit history yet — make an edit first.");
			content.Add(modalEmpty);

			modalList = UiStack.Vertical(content.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: true);
			modalList.Grow();
			content.Add(modalList);

			content.Add(UiButton.Create(content.ContentTransform, "Close", Close, primary: false).FixedHeight(32f));
		}

		/// <summary>Builds the history list into a layout parent (shell History bottom panel).</summary>
		internal static GameObject BuildInto(Transform parent)
		{
			UiStack content = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 8f);
			dockEmpty = UiLabel.Create(content.ContentTransform, "No edit history yet — make an edit first.");
			content.Add(dockEmpty);
			dockList = UiStack.Vertical(content.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: true);
			dockList.Grow();
			content.Add(dockList);
			Refresh();
			return content.GameObject;
		}

		/// <summary>Opens the history popup, rebuilding its row list. Prefer the shell History tab when the dock is up.</summary>
		internal static void Open()
		{
			if (Lab.FocusBottomPanel("History"))
			{
				return;
			}

			Refresh();
			if (modal != null)
			{
				modal.Show();
			}
		}

		/// <summary>Drops modal and dock refs after the lab scene is destroyed.</summary>
		/// <remarks>Unity fake-null: C# refs stay non-null after Close Lab. Refresh then Visible() NREs on the destroyed empty label. LabClosing must call this before the next Project Browser open rebuilds History.</remarks>
		internal static void ResetSession()
		{
			modal = null;
			modalList = null;
			modalEmpty = null;
			UnbindDock();
		}

		/// <summary>Drops dock widget refs after the History panel is destroyed. The History modal is unchanged.</summary>
		internal static void UnbindDock()
		{
			dockList = null;
			dockEmpty = null;
		}

		/// <summary>Rebuilds dock and modal row lists from AnimatorHistory.</summary>
		internal static void Refresh()
		{
			Fill(dockList, dockEmpty, closeAfterPick: false);
			Fill(modalList, modalEmpty, closeAfterPick: true);
		}

		private static void Close()
		{
			if (modal != null)
			{
				modal.Hide();
			}
		}

		private static void Fill(UiStack list, UiLabel emptyLabel, bool closeAfterPick)
		{
			if (!IsLive(list))
			{
				return;
			}

			list.Clear();
			IReadOnlyList<(string description, bool isCurrent)> entries = AnimatorHistory.GetHistoryView();
			if (IsLive(emptyLabel))
			{
				emptyLabel.Visible(entries.Count <= 1);
			}

			for (int i = 0; i < entries.Count; i++)
			{
				int capturedIndex = i;
				(string description, bool isCurrent) entry = entries[i];
				string label = entry.isCurrent ? "-> " + entry.description : entry.description;
				UiButton row = UiButton.Create(list.ContentTransform, label, () => Choose(capturedIndex, closeAfterPick), primary: false).FixedHeight(24f);
				row.SetColor(entry.isCurrent ? UiTheme.Default.AccentColor : UiTheme.Default.RowButtonColor);
				list.Add(row);
			}
		}

		private static void Choose(int flatIndex, bool closeAfterPick)
		{
			if (closeAfterPick)
			{
				Close();
			}

			AnimatorHistory.JumpTo(flatIndex);
		}

		/// <summary>True when the widget still exists in the current lab scene.</summary>
		private static bool IsLive(UiElement element)
		{
			return element != null && element.GameObject != null;
		}
	}
}
