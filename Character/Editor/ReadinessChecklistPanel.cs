using System.Collections.Generic;
using LokrLab;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Editor
{
	/// <summary>The live Error/Warning list from CharacterReadinessRegistry for whatever character is currently loaded.</summary>
	internal static class ReadinessChecklistPanel
	{
		private static readonly Rect Region = EditorLayout.HomeChecklistRegion;

		private static UiLabel emptyLabel;
		private static UiList<ReadinessItem> list;

		/// <summary>Builds the checklist panel.</summary>
		internal static void Build(Transform screenRoot, Font labelFont)
		{
			UiPanel panel = UiPanel.Create(screenRoot, CharacterLabUi.NestedPanelTheme, "Readiness Checklist", region: Region);
			panel.Add(BuildBody(panel.ContentParent));
		}

		/// <summary>Builds the checklist into a layout parent (shell Checklist bottom panel).</summary>
		internal static GameObject BuildInto(Transform parent)
		{
			return BuildBody(parent).GameObject;
		}

		private static UiStack BuildBody(Transform parent)
		{
			UiStack content = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 12f);

			emptyLabel = UiLabel.Create(content.ContentTransform, "Create or load a character to see its checklist.");
			content.Add(emptyLabel);

			list = UiList<ReadinessItem>.Create(content.ContentTransform, spacing: 2f, padding: 0f);
			list.Grow();
			content.Add(list);
			LabHoverInfo.Bind(content.GameObject, "character.readiness.Panel");
			return content;
		}

		/// <summary>Drops widget refs after the Checklist dock or lab scene is destroyed so later refreshes no-op.</summary>
		internal static void Unbind()
		{
			emptyLabel = null;
			list = null;
		}

		/// <summary>True when the checklist widgets still exist in the current lab scene.</summary>
		private static bool IsLive()
		{
			return emptyLabel != null && emptyLabel.GameObject != null
				&& list != null && list.GameObject != null;
		}

		/// <summary>Rebuilds the checklist rows for a character, or shows an empty-state message.</summary>
		internal static void Refresh(List<ReadinessItem> items, bool characterLoaded)
		{
			if (!IsLive())
			{
				Unbind();
				return;
			}

			if (!characterLoaded)
			{
				emptyLabel.SetText("Create or load a character to see its checklist.").Visible(true);
				list.Clear();
				return;
			}
			if (items.Count == 0)
			{
				emptyLabel.SetText("Nothing outstanding.").Visible(true);
				list.Clear();
				return;
			}

			emptyLabel.Visible(false);
			list.SetItems(items, item => item.Severity + "|" + item.Message, (parent, item) =>
			{
				string prefix = item.Severity == ReadinessSeverity.Error ? "[ERROR] " : "[WARNING] ";
				UiLabel row = UiLabel.Create(parent, prefix + item.Message, alignment: TextAnchor.UpperLeft).FixedHeight(30f);
				row.SetColor(item.Severity == ReadinessSeverity.Error ? UiTheme.Default.ErrorColor : UiTheme.Default.WarningColor);
				return row;
			});
		}
	}
}
