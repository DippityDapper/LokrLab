using LokrLab.Editor.Animation;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Character Timeline bottom panel: clip buttons plus the frame-chip strip.</summary>
	internal static class TimelineBottomPanel
	{
		/// <summary>Builds the Timeline dock body into parent and returns the root GameObject.</summary>
		internal static GameObject Build(Transform parent)
		{
			UiStack column = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 4f);
			column.Add(AnimationsPanel.BuildInto(column.ContentTransform, Lab.Canvas).FixedHeight(36f));
			column.Add(AnimationTimelinePanel.BuildInto(column.ContentTransform).Grow());
			return column.GameObject;
		}

		/// <summary>Drops widget refs after the Timeline dock is destroyed so later refreshes no-op.</summary>
		internal static void Unbind()
		{
			AnimationsPanel.Unbind();
			AnimationTimelinePanel.Unbind();
		}
	}
}
