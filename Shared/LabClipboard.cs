using System;
using SimpleUI;
using UnityEngine;

namespace LokrLab
{
	/// <summary>Copies unique ids to the system clipboard from Lab inspectors.</summary>
	internal static class LabClipboard
	{
		/// <summary>Writes text to the system clipboard (same path as the file browser Copy Path).</summary>
		internal static void Copy(string text)
		{
			GUIUtility.systemCopyBuffer = text ?? string.Empty;
		}

		/// <summary>Id line plus a Copy button for a unique id.</summary>
		internal static void AddIdRow(UiStack section, string id)
		{
			if (section == null)
			{
				return;
			}

			string value = id ?? string.Empty;
			UiTheme theme = UiTheme.Default;
			UiStack row = UiStack.Horizontal(section.ContentTransform, theme, spacing: 6f, padding: 0f);
			section.Add(row.FixedHeight(28f));
			row.Add(UiLabel.Create(row.ContentTransform, "Id: " + (string.IsNullOrEmpty(value) ? "-" : value), theme, 13)
				.Grow());
			if (!string.IsNullOrEmpty(value))
			{
				row.Add(CreateCopyButton(row.ContentTransform, () => value, theme));
			}
		}

		/// <summary>Compact Copy button that writes the current text to the clipboard.</summary>
		internal static UiButton CreateCopyButton(Transform parent, Func<string> getText, UiTheme theme = null)
		{
			theme = theme ?? UiTheme.Default;
			UiButton button = null;
			button = UiButton.Create(parent, "Copy", () =>
			{
				string text = getText != null ? getText() : null;
				if (string.IsNullOrEmpty(text))
				{
					return;
				}

				Copy(text);
				if (button != null && button.Label != null)
				{
					button.Label.text = "Copied";
				}
			}, theme, primary: false);
			return button.FixedWidth(56f);
		}
	}
}
