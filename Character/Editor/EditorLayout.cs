using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Centralizes every screen-fraction region rect the editor's UI and cameras lay out against.</summary>
	/// <remarks>
	/// Y=0 bottom, Y=1 top, the same convention Camera.rect and RectTransform anchors both use. Every panel reads
	/// from here instead of hardcoding its own fractions. This exists because of a real bug: a new toolbar row was
	/// added by shrinking the viewport region and growing the toolbar region by the same amount, without noticing
	/// the viewport labels were positioned relative to the viewport region itself -- both moved together and
	/// landed on top of each other. Centralizing every region here, with dependent positions computed from the
	/// same constant, makes that class of bug structurally harder to reintroduce. Every adjacent pair below is
	/// deliberately cross-checked against its neighbor's edge at write time rather than tuned in isolation.
	/// </remarks>
	internal static class EditorLayout
	{
		/// <summary>Bottom edge of the title label (CharacterLabScene.BuildUI), centered at Y=0.968 with a 40px height in the 1080-tall reference canvas.</summary>
		internal const float TitleBottomEdge = 0.9495f;

		/// <summary>Top edge 0.933 -- 0.0165 clear of the title's bottom edge.</summary>
		internal static readonly Rect MenuBarRegion = new Rect(0.02f, 0.895f, 0.96f, 0.038f);

		/// <summary>Top edge 0.890 -- 0.005 clear of MenuBarRegion's bottom edge (0.895).</summary>
		internal static readonly Rect ToolbarRegion = new Rect(0.02f, 0.845f, 0.96f, 0.045f);

		/// <summary>The dock row (Scene Tree | Viewport | Inspector). Top edge 0.835, 0.01 clear of ToolbarRegion's bottom edge. X-spans below sum exactly to this row's 0.02-0.98 span.</summary>
		internal static readonly Rect SceneTreeRegion = new Rect(0.02f, 0.26f, 0.16f, 0.575f);
		/// <summary>The main, editable viewport where parts are dragged and posed.</summary>
		internal static readonly Rect MainViewportRegion = new Rect(0.185f, 0.26f, 0.42f, 0.575f);
		/// <summary>The read-only Preview viewport, showing the rig as it would actually render in-game.</summary>
		internal static readonly Rect PreviewViewportRegion = new Rect(0.625f, 0.26f, 0.17f, 0.575f);
		/// <summary>The Inspector panel's region.</summary>
		internal static readonly Rect InspectorRegion = new Rect(0.80f, 0.26f, 0.18f, 0.575f);

		/// <summary>Bottom edge of the dock row above is 0.26 -- 0.02 clear of this region's top edge (0.24). Unchanged from before the layout revamp; AnimationTimelinePanel's own internal row layout is untouched by it.</summary>
		internal static readonly Rect TimelineRegion = new Rect(0.02f, 0.10f, 0.74f, 0.14f);

		/// <summary>TimelineRegion split between AnimationsPanel (clip/Rest Pose buttons, a thin strip at the top) and AnimationTimelinePanel (frame-chip strip + Add Frame + Copy/Paste/Override/move + Play/Pause, the rest below). The two regions exactly fill TimelineRegion with no gap or overlap.</summary>
		internal static readonly Rect AnimationsRowRegion = new Rect(0.02f, 0.21f, 0.74f, 0.03f);
		/// <summary>AnimationTimelinePanel's frame-chip strip + Add Frame + Copy/Paste/Override/move + Play/Pause row, below AnimationsRowRegion within TimelineRegion.</summary>
		internal static readonly Rect FrameStripRegion = new Rect(0.02f, 0.10f, 0.74f, 0.11f);

		/// <summary>Inset for viewport labels positioned inside their own region's top edge (not floating in a gap above it, which is what caused the class-level bug). Self-contained within the region by construction.</summary>
		internal const float ViewportLabelInset = 0.022f;

		/// <summary>Shared region for the File/Edit/Help menu dropdowns (MenuBarPanel) -- never shown simultaneously, and as a transient overlay doesn't need collision-checking against the always-visible regions. Top edge flush with MenuBarRegion's bottom edge.</summary>
		internal static readonly Rect MenuDropdownRegion = new Rect(0.02f, 0.55f, 0.55f, 0.345f);

		/// <summary>Centered modal region for FileBrowserPanel/MetaExoPickerPanel -- bigger than the dropdown region since these need room for a scrollable list. Both panels bring themselves to front (SetAsLastSibling) on open, to render above the menu dropdowns too.</summary>
		internal static readonly Rect ModalPopupRegion = new Rect(0.20f, 0.15f, 0.60f, 0.70f);

		/// <summary>Smaller centered modal for MenuBarPanel's per-action popups (Load/Save/Import/Slice Atlas), which only need a title, a couple of fields, and Confirm/Cancel.</summary>
		internal static readonly Rect SmallModalRegion = new Rect(0.28f, 0.38f, 0.44f, 0.24f);

		/// <summary>Bigger than ModalPopupRegion -- IslandAtlasPickerPanel needs real screen space for the displayed atlas image so click-to-select-an-island isn't squeezed into a small area.</summary>
		internal static readonly Rect IslandPickerRegion = new Rect(0.05f, 0.08f, 0.90f, 0.84f);

		/// <summary>The Load workstation's regions — actions/recents on the left, scanned folder list on the right.</summary>
		internal static readonly Rect LoadActionsRegion = new Rect(0.02f, 0.03f, 0.30f, 0.94f);
		internal static readonly Rect LoadBrowseRegion = new Rect(0.34f, 0.03f, 0.64f, 0.94f);

		/// <summary>The Properties workstation's two-column row inside the content frame.</summary>
		internal static readonly Rect PropertiesNavRegion = new Rect(0.02f, 0.02f, 0.28f, 0.96f);
		internal static readonly Rect PropertiesInspectorRegion = new Rect(0.32f, 0.02f, 0.66f, 0.96f);

		/// <summary>Home's two-column row inside the content frame.</summary>
		internal static readonly Rect HomeNavRegion = new Rect(0.02f, 0.02f, 0.28f, 0.96f);
		internal static readonly Rect HomeChecklistRegion = new Rect(0.32f, 0.02f, 0.66f, 0.96f);
	}
}
