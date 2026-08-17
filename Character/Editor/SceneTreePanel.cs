using System.Collections.Generic;
using System.Globalization;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Godot-Scene-Tree-style left dock panel: a read+select surface for parts and scale-reference overlays.</summary>
	/// <remarks>
	/// RigEditorScene owns all the actual state (loadedParts, loadedReferences, selection); this just renders it and
	/// calls back into RigEditorScene.SelectPart / SelectReference on interaction. Per-property editing lives in
	/// InspectorPanel.
	///
	/// Built on UiList&lt;DraggablePart&gt;, keyed by part name -- rows are reused (not destroyed/rebuilt) across
	/// Refresh() calls, since Refresh runs on every playback tick and a row's Button needs to stay the same
	/// GameObject for the part's whole lifetime, or a click mid-playback can be interrupted by an unrelated
	/// refresh landing between press and release. UiList&lt;T&gt;'s key-diffing handles create/destroy/reorder;
	/// UpdateRow still runs on every item afterward since a row's content can change without its key changing.
	/// Scale-reference overlays sit in a separate list above the parts, hidden entirely when none are present.
	/// </remarks>
	internal static class SceneTreePanel
	{
		private static UiLabel referencesHeader;
		private static UiList<ReferenceCharacter> referencesList;
		private static UiList<DraggablePart> list;

		/// <summary>Builds the panel into the dock row's SceneTree slot.</summary>
		internal static void Build(Transform parent, Font labelFont)
		{
			UiPanel panel = UiPanel.Create(parent, UiTheme.Default, "Scene Tree (top=back, bottom=front)");
			UiStack outer = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 4f, padding: 0f);
			outer.Grow();
			panel.Add(outer);

			referencesHeader = UiLabel.Create(outer.ContentTransform, "References", UiTheme.Default, 12);
			outer.Add(referencesHeader.FixedHeight(20f));

			referencesList = UiList<ReferenceCharacter>.Create(outer.ContentTransform, UiOrientation.Vertical, UiTheme.Default, spacing: 0f, padding: 0f, scrollable: false);
			outer.Add(referencesList);

			list = UiList<DraggablePart>.Create(outer.ContentTransform, UiOrientation.Vertical, UiTheme.Default, spacing: 0f, padding: 4f);
			list.Grow();
			outer.Add(list);
		}

		/// <summary>Rebuilds the row set (via key-diffing) and refreshes every row's color/label.</summary>
		/// <remarks>No-ops until <see cref="Build"/> runs. The shell uses <c>NodeTreePanel</c> instead of this legacy dock.</remarks>
		internal static void Refresh(List<DraggablePart> parts, DraggablePart selected)
		{
			if (list == null)
			{
				return;
			}

			IReadOnlyList<ReferenceCharacter> references = RigEditorScene.LoadedReferences;
			bool hasReferences = references.Count > 0;
			referencesHeader.Visible(hasReferences);
			referencesList.Visible(hasReferences);
			if (hasReferences)
			{
				List<ReferenceCharacter> referenceItems = new List<ReferenceCharacter>();
				foreach (ReferenceCharacter reference in references)
				{
					if (reference != null)
					{
						referenceItems.Add(reference);
					}
				}
				referencesList.SetItems(referenceItems, reference => reference.InstanceId.ToString(CultureInfo.InvariantCulture), BuildReferenceRow);
				foreach (ReferenceCharacter reference in referenceItems)
				{
					if (referencesList.TryGetRow(reference.InstanceId.ToString(CultureInfo.InvariantCulture), out GameObject row))
					{
						UpdateReferenceRow(row, reference, reference == RigEditorScene.SelectedReference);
					}
				}
			}

			List<DraggablePart> sorted = new List<DraggablePart>(parts);
			sorted.RemoveAll(p => p == null);
			sorted.Sort((a, b) => a.StaticLayer.CompareTo(b.StaticLayer));

			list.SetItems(sorted, part => part.PartName, BuildRow);

			foreach (DraggablePart part in sorted)
			{
				if (list.TryGetRow(part.PartName, out GameObject row))
				{
					UpdateRow(row, part, part == selected);
				}
			}
		}

		/// <summary>Builds one row. part is captured once, safe since rows are keyed (and reused) by part name. Ctrl+click adds/removes this row from the multi-selection instead of replacing it, matching the viewport's own click convention.</summary>
		private static UiElement BuildRow(Transform parent, DraggablePart part)
		{
			UiButton row = UiButton.Create(parent, string.Empty, () =>
			{
				if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
				{
					RigEditorScene.ToggleMultiSelect(part);
				}
				else
				{
					RigEditorScene.SelectPart(part);
				}
			}, primary: false).FixedHeight(24f);
			row.Label.alignment = TextAnchor.MiddleLeft;
			return row;
		}

		/// <summary>Refreshes one row's color/label. isActive means this is RigEditorScene.SelectedPart specifically (brightest highlight); every other multi-selected part gets a dimmer version of the same color.</summary>
		private static void UpdateRow(GameObject row, DraggablePart part, bool isActive)
		{
			bool includedInFrame = RigEditorScene.IsPartIncludedInActiveFrame(part.PartName);
			Color color;
			if (isActive)
			{
				color = new Color(0.9f, 0.75f, 0.2f, 0.9f);
			}
			else if (RigEditorScene.IsPartMultiSelected(part))
			{
				color = new Color(0.75f, 0.6f, 0.15f, 0.7f);
			}
			else if (includedInFrame)
			{
				color = new Color(1f, 1f, 1f, 0.08f);
			}
			else
			{
				color = new Color(1f, 0.2f, 0.2f, 0.12f);
			}
			row.GetComponent<Image>().color = color;

			string label = part.PartName;
			if (!part.Visible)
			{
				label += " (hidden)";
			}
			if (!includedInFrame)
			{
				label += " (not in this frame)";
			}
			else if (RigEditorScene.IsPartApproximateInActiveFrame(part.PartName))
			{
				label += " (read-only pose)";
			}
			row.transform.Find("Label").GetComponent<Text>().text = label;
		}

		/// <summary>Builds one reference-overlay row. Clicking selects the overlay as a whole (no Ctrl+click multi-select -- overlays are not mixed with part group-drags).</summary>
		private static UiElement BuildReferenceRow(Transform parent, ReferenceCharacter reference)
		{
			ReferenceCharacter captured = reference;
			UiButton row = UiButton.Create(parent, string.Empty, () => RigEditorScene.SelectReference(captured), primary: false).FixedHeight(24f);
			row.Label.alignment = TextAnchor.MiddleLeft;
			return row;
		}

		/// <summary>Refreshes one reference row's color/label. Active uses the same yellow as a selected part; idle uses a cyan tint so overlays read as distinct from parts.</summary>
		private static void UpdateReferenceRow(GameObject row, ReferenceCharacter reference, bool isActive)
		{
			Color color = isActive
				? new Color(0.9f, 0.75f, 0.2f, 0.9f)
				: new Color(0.3f, 0.75f, 0.9f, 0.25f);
			row.GetComponent<Image>().color = color;

			string label = "Ref: " + reference.DisplayName;
			if (!reference.Visible)
			{
				label += " (hidden)";
			}
			row.transform.Find("Label").GetComponent<Text>().text = label;
		}
	}
}
