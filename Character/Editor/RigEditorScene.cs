using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ironhide.ExoSkeleton;
using LokrCharacterLoader.CustomRigs;
using LokrLab.Editor.Animation;
using LokrLab.Editor.General;
using LokrModAPI;
using LokrModAPI.Serialization;
using SimpleJSON;
using SimpleUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LokrCharacterLab;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor
{
	/// <summary>The core orchestrator of the rig editor -- Load/Save, selection, Mass Edit, easing, Preview, and matrix math.</summary>
	/// <remarks>
	/// Point it at a folder of part PNGs (optionally one that already has a rig.json --
	/// CustomRigLoader's own Mods/*/CharacterRigs/&lt;RigId&gt;/ convention), drag each part into
	/// place, keyframe animation clips, and Save writes out a rig.json in the exact schema
	/// ExoSkeletonDataAsset.ReloadData expects. Preview round-trips that file through the real
	/// loading path (LokrCharacterLoader.CustomRigs.CustomRigLoader.BuildFromFolder) so what you
	/// see is what a hero assigned to this rig would actually render, not just the flat editor view.
	///
	/// See docs/roadmaps/completed/archive/character-lab-animation-plan.md for the design this implements. Key point that
	/// shapes everything below: a part's rest position (offsetX/offsetY) is ONE value shared by
	/// every animation in the rig -- the schema has no per-frame equivalent -- so every keyframe
	/// stores its position as a delta from rest, not an absolute value. Rotation/scale have no
	/// such constraint (the format has no "rest rotation" concept at all) and are stored as plain
	/// absolutes per keyframe.
	/// </remarks>
	internal static class RigEditorScene
	{
		/// <summary>Name of the pseudo-tool that just selects a part without acting on a drag.</summary>
		/// <remarks>The rest of the toolset is whatever's registered in AnimatorToolRegistry (Move/Rotate/Scale/Scale XY/Pivot by default -- see docs/roadmaps/completed/archive/animator-near-term-plan.md §7/§5). Select isn't a registered IAnimatorTool since it doesn't act on a drag at all. EditorInputController owns the actual key bindings/drag dispatch, driven by AnimatorToolRegistry.Find(CurrentToolName).</remarks>
		internal const string SelectToolName = "Select";

		/// <summary>The main viewport's camera, used for click-to-select, dragging, and pan/zoom hit-testing.</summary>
		internal static Camera ActiveCamera;
		/// <summary>The name of the currently active editing tool (Move/Rotate/Scale/.../Select); set via SetTool.</summary>
		internal static string CurrentToolName { get; private set; } = "Move";

		/// <summary>The Preview viewport's camera, for EditorInputController's Preview pan/zoom handling.</summary>
		/// <remarks>Preview has no auto-fit/re-centering of its own (tried and removed -- bounds computed off ExoSkeletonRenderer's mesh weren't reliable enough; manual pan/zoom alone works better), so this is the only accessor Preview needs.</remarks>
		internal static Camera PreviewCamera => previewCamera;

		/// <summary>Parent transform for every loaded part in the main viewport.</summary>
		private static Transform partsRoot;
		/// <summary>Parent transform for scale-reference overlays in the Main Viewport (same world origin as partsRoot, not previewRoot).</summary>
		private static Transform referencesRoot;
		/// <summary>Parent transform for the Preview viewport's rendered rig.</summary>
		private static Transform previewRoot;
		/// <summary>Parent transform for the Preview viewport's rendered rig, for RigPreviewService's own use -- kept as a read-only property over the private field (rather than the field itself being internal) so nothing outside this class can reassign it.</summary>
		internal static Transform PreviewRoot => previewRoot;
		/// <summary>The Preview viewport's camera (backing field for PreviewCamera).</summary>
		private static Camera previewCamera;

		/// <summary>The folder MenuBarPanel's popups currently target (default: CharacterLabPaths.CharactersRoot).</summary>
		internal static string CurrentFolder { get; private set; } = CharacterLabPaths.CharactersRoot;
		/// <summary>True after OnLoadClicked finishes for CurrentFolder; cleared by ResetSession so a destroyed runtime cannot look "already loaded."</summary>
		private static bool hasLoadedCurrentFolder;
		/// <summary>The character-import id MenuBarPanel's popups currently target.</summary>
		internal static string CurrentImportId { get; private set; } = "ExoSkeletonHumanRanger_MetaDataAsset";
		/// <summary>The atlas path MenuBarPanel's popups currently target.</summary>
		internal static string CurrentAtlasPath { get; private set; } = string.Empty;
		/// <summary>The atlas row count MenuBarPanel's popups currently target.</summary>
		internal static string CurrentAtlasRows { get; private set; } = "1";
		/// <summary>The atlas column count MenuBarPanel's popups currently target.</summary>
		internal static string CurrentAtlasCols { get; private set; } = "1";

		private static UiLabel statusLabel;
		private static readonly List<DraggablePart> loadedParts = new List<DraggablePart>();
		private static DraggablePart selectedPart;
		/// <summary>The single "active" part -- what the Inspector's Part section shows and the pivot handle tracks.</summary>
		internal static DraggablePart SelectedPart => selectedPart;

		/// <summary>Parts additionally selected via Ctrl+click, on top of SelectedPart (which is always a member of this set).</summary>
		/// <remarks>A plain SelectPart click resets this to just SelectedPart; Ctrl+click (ToggleMultiSelect) is what grows it. See EditorInputController's Ctrl+click/Ctrl+A wiring and SceneTreePanel's own row click handling.</remarks>
		private static readonly HashSet<DraggablePart> multiSelection = new HashSet<DraggablePart>();
		/// <summary>Every part currently in the multi-selection.</summary>
		internal static IReadOnlyCollection<DraggablePart> MultiSelection => multiSelection;
		/// <summary>Whether a part is currently part of the multi-selection.</summary>
		internal static bool IsPartMultiSelected(DraggablePart part) => part != null && multiSelection.Contains(part);

		private static readonly List<ReferenceCharacter> loadedReferences = new List<ReferenceCharacter>();
		private static ReferenceCharacter selectedReference;

		/// <summary>Every scale-reference overlay currently in the Main Viewport.</summary>
		internal static IReadOnlyList<ReferenceCharacter> LoadedReferences => loadedReferences;
		/// <summary>The currently selected scale-reference overlay, or null when a part/animation/frame is selected instead.</summary>
		internal static ReferenceCharacter SelectedReference => selectedReference;

		/// <summary>Which of Part/Animation/Frame/Reference the Inspector currently shows.</summary>
		internal enum InspectorTarget
		{
			/// <summary>Nothing selected yet.</summary>
			None,
			/// <summary>The Inspector shows the selected part's properties.</summary>
			Part,
			/// <summary>The Inspector shows the selected animation's properties.</summary>
			Animation,
			/// <summary>The Inspector shows the selected frame's properties.</summary>
			Frame,
			/// <summary>The Inspector shows the selected scale-reference overlay's properties.</summary>
			Reference
		}
		/// <summary>Backing field for CurrentInspectorTarget, independent of selectedPart -- clicking an animation or frame switches this without touching the part selection.</summary>
		private static InspectorTarget inspectorTarget = InspectorTarget.None;
		/// <summary>Which of Part/Animation/Frame the Inspector currently shows.</summary>
		internal static InspectorTarget CurrentInspectorTarget => inspectorTarget;

		/// <summary>The currently-edited animation clip, or null when editing Rest Pose.</summary>
		internal static AnimationClip ActiveClip => activeClip;
		/// <summary>The currently-edited frame's index within ActiveClip.</summary>
		internal static int ActiveFrameIndex => activeFrameIndex;
		/// <summary>The currently-shown baked-frame index within the active frame's BakedFrames (0 unless a clip is playing).</summary>
		internal static int ActiveBakedIndex => activeBakedIndex;
		/// <summary>Every part currently loaded into the editor.</summary>
		internal static IReadOnlyList<DraggablePart> LoadedParts => loadedParts;

		/// <summary>Session-wide "Mass Edit" mode: while on, a committed edit to any multi-selected part also nudges every other frame of the active clip by the same relative amount for that part.</summary>
		/// <remarks>See CommitCurrentPoseToActiveContext/PropagateMassEdit. One session flag, not a per-part sticky setting -- it applies to every member of MultiSelection at commit time (a group move/rotate/scale of a whole character across every frame of a clip), not only to SelectedPart. Lives on ToolbarPanel because it is a global editing mode, not a property of whichever part the Inspector happens to be showing.</remarks>
		private static bool massEditEnabled;
		/// <summary>Whether session-wide Mass Edit mode is on.</summary>
		internal static bool MassEditEnabled => massEditEnabled;

		/// <summary>Session clipboard holding a deep-cloned PoseFrame from CopyActiveFrame, independent of which clip is active so a frame can be pasted into a different animation.</summary>
		/// <remarks>Cleared on Load because part names and clip identity belong to the previous character. Never persisted -- this is an editor convenience, not rig.json data. BakedFrames are omitted (RigSnapshotCloner.Clone already skips them); a paste/override rebakes via RefreshTimeline.</remarks>
		private static PoseFrame frameClipboard;
		/// <summary>True when CopyActiveFrame has stored a frame that PasteFrameAsNew / OverrideActiveFrame can consume.</summary>
		internal static bool HasFrameClipboard => frameClipboard != null;

		/// <summary>Enables or disables session-wide Mass Edit mode.</summary>
		internal static void SetMassEditEnabled(bool enabled)
		{
			massEditEnabled = enabled;
			SetStatus(enabled
				? "Mass Edit ON — edits to every selected part apply to every other frame of this clip."
				: "Mass Edit OFF.");
			ToolbarPanel.RefreshMassEditToggle();
			RefreshTimeline();
		}

		/// <summary>The part (if any) EditorInputController currently has a live drag in progress on.</summary>
		/// <remarks>
		/// Set at drag-begin, cleared at drag-end (see EditorInputController.TryBeginDrag/mouse-up
		/// handling). ApplyContextPoseToParts skips reapplying this part's stored pose while it's
		/// set, so a drag's own continuous writes to the part's live transform can never be
		/// stomped by a context reapplication mid-drag. The guard is unconditional -- a dragged
		/// part's transform should never be reset out from under it for any reason.
		/// </remarks>
		internal static DraggablePart ActivelyDraggingPart { get; set; }

		/// <summary>Same protection as ActivelyDraggingPart, but for a group drag (multi-select Move/Rotate/Scale/Scale XY).</summary>
		/// <remarks>Populated for every IAnimatorTool.SupportsGroupDrag tool, not only Move -- Mass Edit can leave playback running through a group rotate/scale, and ApplyContextPoseToParts has to skip the whole selection or it would stomp the live drag.</remarks>
		private static readonly HashSet<DraggablePart> activelyDraggingGroup = new HashSet<DraggablePart>();
		/// <summary>Replaces the set of parts considered "actively dragging" as a Move-tool group.</summary>
		internal static void SetActivelyDraggingGroup(IEnumerable<DraggablePart> parts)
		{
			activelyDraggingGroup.Clear();
			if (parts == null)
			{
				return;
			}
			foreach (DraggablePart part in parts)
			{
				if (part != null)
				{
					activelyDraggingGroup.Add(part);
				}
			}
		}
		/// <summary>Clears the actively-dragging group.</summary>
		internal static void ClearActivelyDraggingGroup()
		{
			activelyDraggingGroup.Clear();
		}

		/// <summary>Small on-screen marker for the selected part's pivot, or the multi-select temp pivot when more than one part is selected.</summary>
		private static GameObject pivotHandleObject;
		private static Sprite pivotHandleSprite;
		/// <summary>True when the current multi-selection has a session-only group pivot that does not write RestPose.PivotOffset.</summary>
		private static bool hasTemporaryGroupPivot;
		/// <summary>World position of the session temp group pivot; meaningful only when hasTemporaryGroupPivot is true.</summary>
		private static Vector2 temporaryGroupPivotWorld;

		/// <summary>Rest pose per part (keyed by part name); activeClip == null means "editing Rest Pose."</summary>
		private static readonly Dictionary<string, RestPose> restPoses = new Dictionary<string, RestPose>();
		/// <summary>Every authored animation clip.</summary>
		private static readonly List<AnimationClip> clips = new List<AnimationClip>();
		private static AnimationClip activeClip;
		private static int activeFrameIndex;

		/// <summary>Increments on every clip/frame context switch so Inspector OnEndEdit can ignore a late commit meant for the previous frame.</summary>
		/// <remarks>
		/// Clicking a timeline chip or Node Tree clip deselects a focused pose field. Unity fires
		/// <c>onEndEdit</c> after the switch, so SetPart* would write the old field into the new
		/// frame or clip (Mass Edit off). Fields record this generation on focus and no-op when it
		/// has moved. See docs/issues/unresolved/animator-pose-leaks-across-frames.md.
		/// </remarks>
		internal static int PoseContextGeneration { get; private set; }

		/// <summary>Position within the active frame's BakedFrames. Reset to 0 on every activeFrameIndex change; only TickPlayback advances it, and only while playing.</summary>
		private static int activeBakedIndex;
		private static bool isPlaying;
		private static float playTimer;

		/// <summary>Conversion factor between rig.json's pixel-space offsets and this editor's world units (1 world unit = 100px).</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) so RigLoadService's own LoadSavedRig converts offsets identically to this class's own OnSaveClicked.</remarks>
		internal const float PixelsToUnits = 100f;

		/// <summary>Parent for every top-level scene object Build() creates (both viewport cameras, partsRoot, previewRoot, input/playback controllers).</summary>
		/// <remarks>See CharacterLabScene.SwitchToAnimator/SwitchToHome -- parenting everything here lets switching screens be two plain SetActive calls instead of destroying/rebuilding the Animator's entire camera/input setup every time. worldPositionStays:true on every SetParent call means this doesn't affect transform math elsewhere.</remarks>
		private static GameObject runtimeRoot;

		/// <summary>Shows or hides the entire Animator runtime (all cameras, parts, and controllers).</summary>
		internal static void SetRuntimeActive(bool active)
		{
			if (runtimeRoot != null)
			{
				runtimeRoot.SetActive(active);
			}

			if (!active)
			{
				if (ActiveCamera != null)
				{
					ActiveCamera.enabled = false;
				}

				if (previewCamera != null)
				{
					previewCamera.enabled = false;
				}
			}
		}

		/// <summary>True while the Animator runtime exists and is active (shell workspace or legacy workstation).</summary>
		internal static bool IsRuntimeLive => runtimeRoot != null && runtimeRoot.activeInHierarchy;

		/// <summary>True after <see cref="OnLoadClicked"/> has loaded this folder into the current runtime (survives tab switches; cleared by <see cref="ResetSession"/>).</summary>
		internal static bool HasLoadedFolder(string folder)
		{
			return hasLoadedCurrentFolder && runtimeRoot != null && CurrentFolder == folder
				&& !string.IsNullOrEmpty(folder);
		}

		/// <summary>Builds cameras, parts roots, and input/playback without the legacy full-screen dock chrome.</summary>
		/// <remarks>
		/// Used by the shell Animator workspace. Camera.rect is a placeholder; ViewportCameraBinder
		/// overwrites it from the center-dock slots. Pickers and history live on the shared canvas.
		/// </remarks>
		internal static void EnsureShellRuntime(Scene scene, Transform canvas)
		{
			if (runtimeRoot != null)
			{
				return;
			}

			CharacterLabPaths.EnsureFoldersExist();
			ResetSession();

			runtimeRoot = new GameObject("AnimatorRuntimeRoot");
			SceneManager.MoveGameObjectToScene(runtimeRoot, scene);

			GameObject partsRootObject = new GameObject("EditorParts");
			partsRoot = partsRootObject.transform;
			SceneManager.MoveGameObjectToScene(partsRootObject, scene);
			partsRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(partsRootObject);

			GameObject referencesRootObject = new GameObject("EditorReferences");
			referencesRoot = referencesRootObject.transform;
			SceneManager.MoveGameObjectToScene(referencesRootObject, scene);
			referencesRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(referencesRootObject);

			GameObject previewRootObject = new GameObject("EditorPreview");
			previewRoot = previewRootObject.transform;
			previewRoot.position = new Vector3(100f, 0f, 0f);
			SceneManager.MoveGameObjectToScene(previewRootObject, scene);
			previewRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(previewRootObject);

			Camera backdropCamera = Lab.BackdropCamera;
			if (backdropCamera != null)
			{
				backdropCamera.clearFlags = CameraClearFlags.SolidColor;
				backdropCamera.cullingMask = 0;
				backdropCamera.rect = new Rect(0f, 0f, 1f, 1f);
				backdropCamera.depth = 1000f;
			}

			const float backdropDepth = 1000f;
			const float viewportOrthoSize = 1.35f;
			Rect placeholder = new Rect(0.3f, 0.3f, 0.2f, 0.2f);

			GameObject mainViewportCameraObject = new GameObject("MainViewportCamera", typeof(Camera));
			Camera mainViewportCamera = mainViewportCameraObject.GetComponent<Camera>();
			mainViewportCamera.clearFlags = CameraClearFlags.SolidColor;
			mainViewportCamera.backgroundColor = backdropCamera != null
				? backdropCamera.backgroundColor
				: new Color(0.08f, 0.09f, 0.12f, 1f);
			mainViewportCamera.cullingMask = CharacterLabLayers.ViewportMask;
			mainViewportCamera.orthographic = true;
			mainViewportCamera.orthographicSize = viewportOrthoSize;
			mainViewportCamera.rect = placeholder;
			mainViewportCamera.depth = backdropDepth + 1f;
			mainViewportCameraObject.transform.position = new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(mainViewportCameraObject, scene);
			mainViewportCameraObject.transform.SetParent(runtimeRoot.transform, true);
			ActiveCamera = mainViewportCamera;
			ViewportGrid.Build(partsRoot, 70f);
			PortraitFrameGuide.Build(partsRoot);
			CharacterLabLayers.ApplyToHierarchy(partsRoot);

			GameObject previewCameraObject = new GameObject("PreviewCamera", typeof(Camera));
			previewCamera = previewCameraObject.GetComponent<Camera>();
			previewCamera.clearFlags = CameraClearFlags.SolidColor;
			previewCamera.backgroundColor = backdropCamera != null
				? backdropCamera.backgroundColor
				: new Color(0.08f, 0.09f, 0.12f, 1f);
			previewCamera.cullingMask = CharacterLabLayers.ViewportMask;
			previewCamera.orthographic = true;
			previewCamera.orthographicSize = viewportOrthoSize;
			previewCamera.rect = placeholder;
			previewCamera.depth = backdropDepth + 2f;
			previewCameraObject.transform.position = previewRoot.position + new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(previewCameraObject, scene);
			previewCameraObject.transform.SetParent(runtimeRoot.transform, true);
			ViewportGrid.Build(previewRoot, 10f);
			CharacterLabLayers.ApplyToHierarchy(previewRoot);

			GameObject pivotHandleGameObject = new GameObject("PivotHandle", typeof(SpriteRenderer));
			pivotHandleGameObject.transform.SetParent(partsRoot, false);
			SpriteRenderer pivotHandleRenderer = pivotHandleGameObject.GetComponent<SpriteRenderer>();
			pivotHandleRenderer.sprite = GetPivotHandleSprite();
			pivotHandleRenderer.color = new Color(0.2f, 1f, 0.4f, 0.95f);
			pivotHandleRenderer.sortingOrder = 9999;
			pivotHandleGameObject.transform.localScale = Vector3.one * 0.12f;
			pivotHandleObject = pivotHandleGameObject;
			pivotHandleObject.SetActive(false);
			CharacterLabLayers.ApplyToHierarchy(pivotHandleObject);

			GameObject inputControllerObject = new GameObject("EditorInputController", typeof(EditorInputController));
			SceneManager.MoveGameObjectToScene(inputControllerObject, scene);
			inputControllerObject.transform.SetParent(runtimeRoot.transform, true);

			GameObject playbackControllerObject = new GameObject("AnimationPlaybackController", typeof(AnimationPlaybackController));
			SceneManager.MoveGameObjectToScene(playbackControllerObject, scene);
			playbackControllerObject.transform.SetParent(runtimeRoot.transform, true);

			AnimatorImportRegistry.RegisterDefaults();
			MetaExoPickerPanel.Build(canvas, Lab.DefaultFont);
			ReplacePartPickerPanel.Build(canvas, Lab.DefaultFont);
			EditHistoryPanel.Build(canvas, Lab.DefaultFont);
			IslandAtlasPickerPanel.Build(canvas, Lab.DefaultFont);
			MenuBarPanel.EnsurePopups(canvas);
		}

		/// <summary>Finds a loaded part by PartName, or null.</summary>
		internal static DraggablePart FindPartByName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			foreach (DraggablePart part in loadedParts)
			{
				if (part != null && part.PartName == name)
				{
					return part;
				}
			}

			return null;
		}

		/// <summary>Selects the loaded part with this name, or no-ops if it is not loaded.</summary>
		internal static void SelectPartByName(string name)
		{
			SelectPart(FindPartByName(name));
		}

		/// <summary>Finds an authored clip by name, or null.</summary>
		internal static AnimationClip FindClipByName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			foreach (AnimationClip clip in clips)
			{
				if (clip != null && clip.Name == name)
				{
					return clip;
				}
			}

			return null;
		}

		/// <summary>Switches the editing context to the named clip, or no-ops if it is not loaded.</summary>
		internal static void SelectClipByName(string name)
		{
			AnimationClip clip = FindClipByName(name);
			if (clip != null)
			{
				SelectClip(clip);
			}
		}

		/// <summary>Points SetStatus at the shell toolbar label when the Animator is hosted in-shell.</summary>
		internal static void SetShellStatusLabel(UiLabel label)
		{
			statusLabel = label;
		}

		/// <summary>Clears every in-memory editing-session field back to an empty state, destroying any loaded DraggablePart/ReferenceCharacter GameObjects that still exist.</summary>
		/// <remarks>
		/// Fixes a real bug (pre-redesign audit C-03): Lab.CloseTo tears down the whole
		/// lab scene via a LoadSceneMode.Single transition, which destroys every DraggablePart this
		/// class's own static fields (loadedParts, clips, restPoses, ...) still reference -- but
		/// those C# static fields themselves survive (this is a static class, not scene state), so
		/// without an explicit reset they keep pointing at now-destroyed GameObjects. The next time
		/// the Animator workstation opens, Build() calls RefreshTimeline() as its own last line --
		/// before OnLoadClicked (the thing that used to be the only place clearing loadedParts) ever
		/// runs -- so SceneTreePanel.Refresh/InspectorPanel.Refresh would read those stale references
		/// and throw MissingReferenceException on a destroyed Unity Object's own members.
		///
		/// Deliberately does NOT touch CurrentFolder or any Unity-object infrastructure field
		/// (partsRoot, previewRoot, statusLabel, previewObject, pivotHandleObject, ...) --
		/// Build() unconditionally recreates all of those itself, earlier in the same method, before
		/// anything (including this method's own callers) could read a stale one; resetting them here
		/// too would be redundant, not safer. Called from the start of Build() (so RefreshTimeline()
		/// at Build()'s own end never sees stale state) and from Lab.CloseTo (so nothing
		/// reads stale state in between, even before the scene's own destruction actually runs). Both
		/// call sites represent the whole lab scene being destroyed, which is why this also destroys
		/// scale-reference overlays -- OnLoadClicked (switching folders within one still-live session)
		/// deliberately does not call this and keeps its own narrower clear block instead, since a
		/// same-session folder switch should not discard those overlays the way a real teardown does.
		/// </remarks>
		internal static void ResetSession()
		{
			foreach (DraggablePart part in loadedParts)
			{
				if (part != null)
				{
					UnityEngine.Object.Destroy(part.gameObject);
				}
			}
			loadedParts.Clear();
			multiSelection.Clear();
			activelyDraggingGroup.Clear();
			selectedPart = null;

			foreach (ReferenceCharacter reference in loadedReferences)
			{
				if (reference != null)
				{
					UnityEngine.Object.Destroy(reference.gameObject);
				}
			}
			loadedReferences.Clear();
			selectedReference = null;

			inspectorTarget = InspectorTarget.None;
			massEditEnabled = false;
			frameClipboard = null;
			restPoses.Clear();
			clips.Clear();
			activeClip = null;
			activeFrameIndex = 0;
			activeBakedIndex = 0;
			isPlaying = false;
			playTimer = 0f;
			hasTemporaryGroupPivot = false;
			hasLoadedCurrentFolder = false;
			AnimatorHistory.Clear();
		}

		/// <summary>Builds the Animator's runtime scene objects (cameras, viewports, input/playback controllers) under runtimeRoot.</summary>
		/// <remarks>
		/// previewRoot sits 100 units from the origin: both viewport cameras render the same scene
		/// with no culling-mask separation, so the only thing keeping edit parts and preview parts
		/// out of each other's view is that neither camera's orthographic frustum reaches that far
		/// -- 100 units comfortably exceeds any realistic rig extent or zoom/pan range.
		///
		/// ActiveCamera (as CharacterLabScene set it up) is a full-screen SolidColor-clear camera
		/// that exists to paint over the still-loaded-but-hidden main menu scene every frame; its
		/// cullingMask is zeroed here so it only does that backdrop job, while each viewport gets
		/// its own new camera layered on top at a higher depth, clipped to its own rect -- their
		/// world content can't bleed past their own frustum into each other or into the panels
		/// beside them, regardless of window size.
		///
		/// The dock row (SceneTree | Main Viewport | Preview | Inspector) is one UiSplit with fixed
		/// weights instead of hand-tuned EditorLayout Rect constants, so slot fractions always sum
		/// to exactly 1 by construction (see EditorLayout's own doc comment on the bug that caused).
		/// The two viewport slots are camera-rendered, not UI, so their resolved rects are composed
		/// with the dock row's screen position (ToScreenRect) to feed Camera.rect directly rather
		/// than going through a UiStack.
		///
		/// ViewportGrid's 70-unit half-extent covers EditorInputController's own pan bounds (30)
		/// plus its max zoom-out reach (~22); Preview's grid uses 10 units since Preview never
		/// pans/zooms. Viewport labels are positioned inside each region's own top edge (see
		/// EditorLayout.ViewportLabelInset) so they can't drift outside that region later.
		///
		/// MenuBarPanel is built last, deliberately: Unity draws later siblings on top, and
		/// MenuBarPanel's dropdowns are meant to overlay the toolbar/dock row while open.
		/// FileBrowserPanel is built by CharacterLabHooks on lab open (and again by
		/// EnsureShellRuntime / Open if those widgets were never created or were destroyed).
		///
		/// Calls ResetSession() first, before anything else -- see that method's own doc comment
		/// (C-03) for why this method's own RefreshTimeline() call at the very end would otherwise
		/// read DraggablePart/clip/etc. references left over from a scene already torn down since
		/// the last Build().
		/// </remarks>
		internal static void Build(Scene scene, Transform canvas)
		{
			CharacterLabPaths.EnsureFoldersExist();
			ResetSession();

			runtimeRoot = new GameObject("AnimatorRuntimeRoot");
			SceneManager.MoveGameObjectToScene(runtimeRoot, scene);

			GameObject partsRootObject = new GameObject("EditorParts");
			partsRoot = partsRootObject.transform;
			SceneManager.MoveGameObjectToScene(partsRootObject, scene);
			partsRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(partsRootObject);

			GameObject referencesRootObject = new GameObject("EditorReferences");
			referencesRoot = referencesRootObject.transform;
			SceneManager.MoveGameObjectToScene(referencesRootObject, scene);
			referencesRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(referencesRootObject);

			GameObject previewRootObject = new GameObject("EditorPreview");
			previewRoot = previewRootObject.transform;
			previewRoot.position = new Vector3(100f, 0f, 0f);
			SceneManager.MoveGameObjectToScene(previewRootObject, scene);
			previewRootObject.transform.SetParent(runtimeRoot.transform, true);
			CharacterLabLayers.ApplyToHierarchy(previewRootObject);

			Camera backdropCamera = Lab.BackdropCamera;
			if (backdropCamera != null)
			{
				backdropCamera.clearFlags = CameraClearFlags.SolidColor;
				backdropCamera.cullingMask = 0;
				backdropCamera.rect = new Rect(0f, 0f, 1f, 1f);
				backdropCamera.depth = 1000f;
			}

			const float backdropDepth = 1000f;

			Rect dockRowScreenRect = new Rect(0.02f, 0.26f, 0.96f, 0.575f);
			GameObject dockRowObject = new GameObject("DockRow", typeof(RectTransform));
			dockRowObject.transform.SetParent(canvas, false);
			RectTransform dockRowRect = dockRowObject.GetComponent<RectTransform>();
			dockRowRect.anchorMin = new Vector2(dockRowScreenRect.x, dockRowScreenRect.y);
			dockRowRect.anchorMax = new Vector2(dockRowScreenRect.x + dockRowScreenRect.width, dockRowScreenRect.y + dockRowScreenRect.height);
			dockRowRect.offsetMin = Vector2.zero;
			dockRowRect.offsetMax = Vector2.zero;

			UiSplit dockRow = UiSplit.Columns(dockRowObject.transform, UiTheme.Default,
				ColumnSpec.Weighted(0.16f), ColumnSpec.Weighted(0.005f), ColumnSpec.Weighted(0.42f),
				ColumnSpec.Weighted(0.02f), ColumnSpec.Weighted(0.17f), ColumnSpec.Weighted(0.005f), ColumnSpec.Weighted(0.18f));
			Rect mainViewportScreenRect = ToScreenRect(dockRowScreenRect, dockRow.Slots[2].NormalizedRect);
			Rect previewViewportScreenRect = ToScreenRect(dockRowScreenRect, dockRow.Slots[4].NormalizedRect);

			const float viewportOrthoSize = 1.35f;
			GameObject mainViewportCameraObject = new GameObject("MainViewportCamera", typeof(Camera));
			Camera mainViewportCamera = mainViewportCameraObject.GetComponent<Camera>();
			mainViewportCamera.clearFlags = CameraClearFlags.SolidColor;
			mainViewportCamera.backgroundColor = backdropCamera != null
				? backdropCamera.backgroundColor
				: new Color(0.08f, 0.09f, 0.12f, 1f);
			mainViewportCamera.cullingMask = CharacterLabLayers.ViewportMask;
			mainViewportCamera.orthographic = true;
			mainViewportCamera.orthographicSize = viewportOrthoSize;
			mainViewportCamera.rect = mainViewportScreenRect;
			mainViewportCamera.depth = backdropDepth + 1f;
			mainViewportCameraObject.transform.position = new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(mainViewportCameraObject, scene);
			mainViewportCameraObject.transform.SetParent(runtimeRoot.transform, true);
			ActiveCamera = mainViewportCamera;
			ViewportGrid.Build(partsRoot, 70f);
			PortraitFrameGuide.Build(partsRoot);
			CharacterLabLayers.ApplyToHierarchy(partsRoot);

			GameObject previewCameraObject = new GameObject("PreviewCamera", typeof(Camera));
			previewCamera = previewCameraObject.GetComponent<Camera>();
			previewCamera.clearFlags = CameraClearFlags.SolidColor;
			previewCamera.backgroundColor = backdropCamera != null
				? backdropCamera.backgroundColor
				: new Color(0.08f, 0.09f, 0.12f, 1f);
			previewCamera.cullingMask = CharacterLabLayers.ViewportMask;
			previewCamera.orthographic = true;
			previewCamera.orthographicSize = viewportOrthoSize;
			previewCamera.rect = previewViewportScreenRect;
			previewCamera.depth = backdropDepth + 2f;
			previewCameraObject.transform.position = previewRoot.position + new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(previewCameraObject, scene);
			previewCameraObject.transform.SetParent(runtimeRoot.transform, true);
			ViewportGrid.Build(previewRoot, 10f);
			CharacterLabLayers.ApplyToHierarchy(previewRoot);

			CreateViewportFrame(canvas, "MainViewportFrame", mainViewportScreenRect, new Color(1f, 1f, 1f, 0.25f));
			CreateViewportFrame(canvas, "PreviewViewportFrame", previewViewportScreenRect, new Color(1f, 1f, 1f, 0.25f));
			CreateLabelInsideRegionTop(canvas, "MainViewportLabel", "Editable (drag parts here)", mainViewportScreenRect);
			CreateLabelInsideRegionTop(canvas, "PreviewViewportLabel", "Preview (real in-engine render)", previewViewportScreenRect);

			statusLabel = ToolbarPanel.Build(canvas);
			SceneTreePanel.Build(dockRow.Slots[0].GameObject.transform, Lab.DefaultFont);
			InspectorPanel.Build(dockRow.Slots[6].GameObject.transform, Lab.DefaultFont);
			AnimationsPanel.Build(canvas, Lab.DefaultFont);
			AnimationTimelinePanel.Build(canvas, Lab.DefaultFont);
			MenuBarPanel.Build(canvas, Lab.DefaultFont);
			AnimatorImportRegistry.RegisterDefaults();
			MetaExoPickerPanel.Build(canvas, Lab.DefaultFont);
			ReplacePartPickerPanel.Build(canvas, Lab.DefaultFont);
			EditHistoryPanel.Build(canvas, Lab.DefaultFont);
			IslandAtlasPickerPanel.Build(canvas, Lab.DefaultFont);

			GameObject pivotHandleGameObject = new GameObject("PivotHandle", typeof(SpriteRenderer));
			pivotHandleGameObject.transform.SetParent(partsRoot, false);
			SpriteRenderer pivotHandleRenderer = pivotHandleGameObject.GetComponent<SpriteRenderer>();
			pivotHandleRenderer.sprite = GetPivotHandleSprite();
			pivotHandleRenderer.color = new Color(0.2f, 1f, 0.4f, 0.95f);
			pivotHandleRenderer.sortingOrder = 9999;
			pivotHandleGameObject.transform.localScale = Vector3.one * 0.12f;
			pivotHandleObject = pivotHandleGameObject;
			pivotHandleObject.SetActive(false);
			CharacterLabLayers.ApplyToHierarchy(pivotHandleObject);

			GameObject inputControllerObject = new GameObject("EditorInputController", typeof(EditorInputController));
			SceneManager.MoveGameObjectToScene(inputControllerObject, scene);
			inputControllerObject.transform.SetParent(runtimeRoot.transform, true);

			GameObject playbackControllerObject = new GameObject("AnimationPlaybackController", typeof(AnimationPlaybackController));
			SceneManager.MoveGameObjectToScene(playbackControllerObject, scene);
			playbackControllerObject.transform.SetParent(runtimeRoot.transform, true);

			RefreshTimeline();
		}

		/// <summary>Draws a thin border outline (not a filled panel) around a world-camera viewport region.</summary>
		/// <remarks>A solid UI panel would draw over the camera's rendered content (Canvas ScreenSpaceOverlay always composites above every camera, regardless of sibling order), so this only occupies the border pixels.</remarks>
		private static void CreateViewportFrame(Transform canvas, string name, Rect region, Color color)
		{
			const float thickness = 2f;
			Vector2 min = new Vector2(region.x, region.y);
			Vector2 max = new Vector2(region.x + region.width, region.y + region.height);
			CreateFrameBar(canvas, name + "_Top", new Vector2(min.x, max.y), new Vector2(max.x, max.y), thickness, color, true);
			CreateFrameBar(canvas, name + "_Bottom", new Vector2(min.x, min.y), new Vector2(max.x, min.y), thickness, color, true);
			CreateFrameBar(canvas, name + "_Left", new Vector2(min.x, min.y), new Vector2(min.x, max.y), thickness, color, false);
			CreateFrameBar(canvas, name + "_Right", new Vector2(max.x, min.y), new Vector2(max.x, max.y), thickness, color, false);
		}

		private static void CreateFrameBar(Transform canvas, string name, Vector2 anchorA, Vector2 anchorB, float thickness, Color color, bool horizontal)
		{
			GameObject bar = new GameObject(name, typeof(Image));
			bar.transform.SetParent(canvas, false);
			RectTransform rect = bar.GetComponent<RectTransform>();
			rect.anchorMin = anchorA;
			rect.anchorMax = anchorB;
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = horizontal ? new Vector2(0f, thickness) : new Vector2(thickness, 0f);
			rect.anchoredPosition = Vector2.zero;
			bar.GetComponent<Image>().color = color;
		}

		/// <summary>Creates a label positioned inside the top edge of a screen region, so it can't drift outside it later.</summary>
		private static void CreateLabelInsideRegionTop(Transform canvas, string name, string text, Rect region)
		{
			UiLabel label = UiLabel.Create(canvas, text, fontSize: 12, alignment: TextAnchor.MiddleCenter).Name(name);
			Vector2 anchorCenter = new Vector2(region.x + region.width * 0.5f, region.y + region.height - EditorLayout.ViewportLabelInset);
			label.RectTransform.anchorMin = anchorCenter;
			label.RectTransform.anchorMax = anchorCenter;
			label.RectTransform.sizeDelta = new Vector2(380f, 22f);
			label.RectTransform.anchoredPosition = Vector2.zero;
		}

		/// <summary>Composes a UiSplit slot's within-container fraction with the container's own screen-fraction position, for camera-rendered viewport slots that need a real Camera.rect.</summary>
		private static Rect ToScreenRect(Rect container, Rect withinContainer)
		{
			return new Rect(
				container.x + withinContainer.x * container.width,
				container.y + withinContainer.y * container.height,
				withinContainer.width * container.width,
				withinContainer.height * container.height);
		}

		/// <summary>Lazily generates a plain white square sprite for the pivot handle marker (procedural -- not part of any rig's art).</summary>
		private static Sprite GetPivotHandleSprite()
		{
			if (pivotHandleSprite == null)
			{
				const int size = 16;
				Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
				Color[] pixels = new Color[size * size];
				for (int i = 0; i < pixels.Length; i++)
				{
					pixels[i] = Color.white;
				}
				texture.SetPixels(pixels);
				texture.Apply();
				pivotHandleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
			}
			return pivotHandleSprite;
		}

		/// <summary>Sets the active tool and notifies ToolbarPanel to refresh its mode buttons.</summary>
		internal static void SetTool(string toolName)
		{
			CurrentToolName = toolName;
			ToolbarPanel.RefreshModeButtons();
			SetStatus("Tool: " + toolName);
		}

		/// <summary>Sets the status bar text and logs the same message.</summary>
		internal static void SetStatus(string message)
		{
			if (statusLabel != null)
			{
				statusLabel.SetText(message);
			}
			else
			{
				Lab.SetStatus(message);
			}
			LokrCharacterLabPlugin.Log.LogInfo("RigEditorScene: " + message);
		}

		/// <summary>Imports a base-game character by metaExo id and points CurrentFolder at the result.</summary>
		internal static void OnImportClicked(string metaExoId)
		{
			CurrentImportId = metaExoId;
			if (CharacterImporter.Import(metaExoId, out string outputFolder, out string message))
			{
				CurrentFolder = outputFolder;
				SetStatus(message);
			}
			else
			{
				SetStatus(message);
			}
		}

		/// <summary>Called by IslandAtlasPickerPanel after a successful import; points CurrentFolder at the output so Load opens straight into it.</summary>
		internal static void OnIslandAtlasImported(string outputFolder, string message)
		{
			CurrentFolder = outputFolder;
			SetStatus(message);
		}

		/// <summary>Slices a grid atlas image into part sprites under CurrentFolder/sprites.</summary>
		internal static void OnSliceAtlasClicked(string atlasPath, string rowsText, string colsText)
		{
			CurrentAtlasPath = atlasPath;
			CurrentAtlasRows = rowsText;
			CurrentAtlasCols = colsText;
			Dictionary<string, string> parameters = new Dictionary<string, string>
			{
				{ "atlasPath", atlasPath },
				{ "rows", rowsText },
				{ "cols", colsText }
			};
			string atlasTargetFolder = Path.Combine(CurrentFolder, "sprites");
			Directory.CreateDirectory(atlasTargetFolder);
			bool success = AnimatorImportRegistry.TryImport("Grid Atlas", atlasTargetFolder, parameters, out string message);
			SetStatus(success ? message : "Atlas import failed: " + message);
		}

		/// <summary>Loads a rig from a character folder, clearing all prior editor state (selection, clips, undo history).</summary>
		/// <remarks>
		/// Character folder layout: &lt;folder&gt;/rig/ (rig.json + sidecars) and &lt;folder&gt;/sprites/
		/// (part PNGs). Saved part order becomes each part's initial StaticLayer (draw order) on
		/// reload. A part used more than once in the same frame (e.g. one arm sprite reused for both
		/// limbs) gets extra "(copy N)" DraggablePart instances spawned for it, since one
		/// DraggablePart can only show one placement. Stand/Portrait (or StandStatic) are
		/// auto-created if missing, since every base-game read of Hero.exoSkeletonDataAsset requires
		/// them (see CustomRigLoader.RequiredAnimationNames). Preview rebuilds automatically if the
		/// folder already has a rig.json.
		///
		/// Deliberately keeps its own clear block rather than calling ResetSession() -- this method's
		/// scope is "switch to a different folder within the same session," which should not also
		/// discard scale-reference overlays (ResetSession() does, since those are real GameObjects a
		/// full scene teardown destroys anyway); loadedParts/selection/animation state is cleared
		/// here for the same reason ResetSession() clears it, just without the overlay side effect.
		/// Also clears multiSelection/activelyDraggingGroup, which the original version of this
		/// method did not -- the same C-03 staleness bug ResetSession() fixes for a scene teardown
		/// applies equally here: loading a different folder destroys the old DraggablePart instances
		/// those sets reference, and nothing else in this method used to clear them.
		/// </remarks>
		internal static void OnLoadClicked(string folder)
		{
			CurrentFolder = folder;
			AnimatorHistory.Clear();
			LabSaveUx.ClearDirty();

			foreach (DraggablePart part in loadedParts)
			{
				if (part != null)
				{
					UnityEngine.Object.Destroy(part.gameObject);
				}
			}
			loadedParts.Clear();
			multiSelection.Clear();
			activelyDraggingGroup.Clear();
			selectedPart = null;
			hasTemporaryGroupPivot = false;
			inspectorTarget = InspectorTarget.None;
			massEditEnabled = false;
			frameClipboard = null;
			restPoses.Clear();
			clips.Clear();
			activeClip = null;
			activeFrameIndex = 0;
			activeBakedIndex = 0;
			isPlaying = false;
			hasLoadedCurrentFolder = false;

			if (!Directory.Exists(folder))
			{
				SetStatus("Folder does not exist: " + folder);
				return;
			}

			string rigFolder = Path.Combine(folder, "rig");
			string spritesFolder = Path.Combine(folder, "sprites");

			string[] pngPaths = Directory.Exists(spritesFolder) ? Directory.GetFiles(spritesFolder, "*.png") : Array.Empty<string>();
			if (pngPaths.Length == 0)
			{
				EnsureRequiredClip(clips, "Stand");
				EnsureRequiredClip(clips, "Portrait");
				RefreshTimeline();
				SetStatus("No .png files found in " + spritesFolder + " — Stand/Portrait animations are ready; import parts to start posing them.");
				hasLoadedCurrentFolder = true;
				return;
			}

			List<string> savedPartOrder = new List<string>();
			Dictionary<string, int> maxOccurrenceByBaseName = new Dictionary<string, int>();
			RigLoadService.LoadSavedRig(rigFolder, restPoses, clips, savedPartOrder, maxOccurrenceByBaseName, out int approximatePoseCount);
			DropUnusedAngledAttackClips(clips, CombatModelForFolder(folder));
			Dictionary<string, int> savedLayerByName = new Dictionary<string, int>();
			for (int i = 0; i < savedPartOrder.Count; i++)
			{
				savedLayerByName[savedPartOrder[i]] = i;
			}
			int nextLayerForNewParts = savedPartOrder.Count;

			int columns = Mathf.CeilToInt(Mathf.Sqrt(pngPaths.Length));
			const float spacing = 1.6f;
			float startX = -1f * (columns - 1) * spacing * 0.5f - 3f;
			float startY = (Mathf.CeilToInt((float)pngPaths.Length / columns) - 1) * spacing * 0.5f;

			int restoredCount = 0;
			for (int i = 0; i < pngPaths.Length; i++)
			{
				string partName = Path.GetFileNameWithoutExtension(pngPaths[i]);
				Sprite sprite = ModAPI.Assets.LoadSprite(pngPaths[i], TextureFormat.ARGB32);

				GameObject partObject = new GameObject(partName, typeof(SpriteRenderer), typeof(BoxCollider), typeof(DraggablePart));
				partObject.transform.SetParent(partsRoot, false);
				CharacterLabLayers.ApplyToHierarchy(partObject);

				if (restPoses.ContainsKey(partName))
				{
					restoredCount++;
				}
				else
				{
					int col = i % columns;
					int row = i / columns;
					restPoses[partName] = new RestPose { Position = new Vector2(startX + col * spacing, startY - row * spacing) };
				}

				SpriteRenderer renderer = partObject.GetComponent<SpriteRenderer>();
				renderer.sprite = sprite;

				BoxCollider collider = partObject.GetComponent<BoxCollider>();
				collider.size = new Vector3(sprite.bounds.size.x, sprite.bounds.size.y, 0.1f);

				DraggablePart draggable = partObject.GetComponent<DraggablePart>();
				draggable.PartName = partName;
				draggable.StaticLayer = savedLayerByName.TryGetValue(partName, out int savedLayer) ? savedLayer : nextLayerForNewParts++;
				loadedParts.Add(draggable);
			}

			int duplicatesCreated = 0;
			foreach (KeyValuePair<string, int> entry in maxOccurrenceByBaseName)
			{
				string baseName = entry.Key;
				int maxOccurrence = entry.Value;
				if (maxOccurrence <= 1)
				{
					continue;
				}
				string basePngPath = Path.Combine(spritesFolder, baseName + ".png");
				if (!File.Exists(basePngPath))
				{
					continue;
				}
				RestPose baseRest = GetOrCreateRestPose(baseName);
				for (int occurrence = 2; occurrence <= maxOccurrence; occurrence++)
				{
					string duplicateName = DuplicateName(baseName, occurrence);
					Sprite sprite = ModAPI.Assets.LoadSprite(basePngPath, TextureFormat.ARGB32);

					GameObject partObject = new GameObject(duplicateName, typeof(SpriteRenderer), typeof(BoxCollider), typeof(DraggablePart));
					partObject.transform.SetParent(partsRoot, false);
					CharacterLabLayers.ApplyToHierarchy(partObject);

					restPoses[duplicateName] = new RestPose { Position = baseRest.Position, RotationDegrees = baseRest.RotationDegrees, ScaleX = baseRest.ScaleX, ScaleY = baseRest.ScaleY, PivotOffset = baseRest.PivotOffset };

					SpriteRenderer renderer = partObject.GetComponent<SpriteRenderer>();
					renderer.sprite = sprite;

					BoxCollider collider = partObject.GetComponent<BoxCollider>();
					collider.size = new Vector3(sprite.bounds.size.x, sprite.bounds.size.y, 0.1f);

					DraggablePart draggable = partObject.GetComponent<DraggablePart>();
					draggable.PartName = duplicateName;
					draggable.StaticLayer = nextLayerForNewParts++;
					loadedParts.Add(draggable);
					duplicatesCreated++;
				}
			}

			NormalizeLayers();

			EnsureRequiredClip(clips, "Stand");
			if (!clips.Exists(c => c.Name == "Portrait") && !clips.Exists(c => c.Name == "StandStatic"))
			{
				EnsureRequiredClip(clips, "Portrait");
			}

			ApplyContextPoseToParts();
			RefreshTimeline();
			RebuildPreview();

			string duplicateNote = duplicatesCreated > 0
				? string.Format(" {0} part(s) are drawn more than once per frame in this rig's real data (e.g. one arm sprite reused for both limbs) — added as separate \"(copy N)\" entries in the parts list so both placements show.", duplicatesCreated)
				: string.Empty;
			string approximateNote = approximatePoseCount > 0
				? string.Format(" WARNING: {0} pose(s) used a matrix this editor couldn't decompose at all (genuinely degenerate/singular, not just sheared) — shown read-only; select the affected part to see it flagged in the Scene Tree and Inspector.", approximatePoseCount)
				: string.Empty;
			SetStatus(string.Format("Loaded {0} part(s) from {1} ({2} restored from rig.json, {3} clip(s)). Drag to position; use the Scene Tree and timeline below.{4}{5}",
				pngPaths.Length, folder, restoredCount, clips.Count, duplicateNote, approximateNote));
			hasLoadedCurrentFolder = true;
		}

		/// <summary>Selects a single part (clearing multi-selection and any selected reference) and shows it in the Inspector.</summary>
		internal static void SelectPart(DraggablePart part)
		{
			ApplySelection(part != null ? new[] { part } : Array.Empty<DraggablePart>(), part);
		}

		/// <summary>Selects every given part and highlights them in the viewport. Active is the Inspector's Part target.</summary>
		internal static void SelectParts(IReadOnlyList<DraggablePart> parts, DraggablePart active)
		{
			if (parts == null || parts.Count == 0)
			{
				SelectPart(null);
				return;
			}

			ApplySelection(parts, active != null ? active : parts[0]);
		}

		/// <summary>Clears part selection, multi-selection, and any selected scale-reference overlay.</summary>
		internal static void DeselectAll()
		{
			SelectPart(null);
		}

		/// <summary>Ctrl+click: adds a part to the multi-selection if absent, removes it if present (never dropping to fully empty).</summary>
		/// <remarks>The clicked part always becomes the new active part; on remove, promotes to whichever part remains.</remarks>
		internal static void ToggleMultiSelect(DraggablePart part)
		{
			if (part == null)
			{
				return;
			}
			if (multiSelection.Contains(part))
			{
				if (multiSelection.Count <= 1)
				{
					return;
				}
				List<DraggablePart> remaining = new List<DraggablePart>(multiSelection);
				remaining.Remove(part);
				DraggablePart newActive = part == selectedPart ? remaining[0] : selectedPart;
				ApplySelection(remaining, newActive);
			}
			else
			{
				List<DraggablePart> grown = new List<DraggablePart>(multiSelection) { part };
				ApplySelection(grown, part);
			}
		}

		/// <summary>Ctrl+A: selects every loaded part, including ones not in the active frame.</summary>
		internal static void SelectAllParts()
		{
			List<DraggablePart> all = new List<DraggablePart>();
			foreach (DraggablePart part in loadedParts)
			{
				if (part != null)
				{
					all.Add(part);
				}
			}
			if (all.Count == 0)
			{
				return;
			}
			ApplySelection(all, all[0]);
		}

		/// <summary>Reconciles multiSelection/selectedPart against each part's viewport highlight, diffing old vs new membership.</summary>
		/// <remarks>Runs unconditionally, even with no actual change -- clicking the already-active part again is how the user gets the Inspector back to Part view after it's been showing an animation or frame.</remarks>
		private static void ApplySelection(IEnumerable<DraggablePart> newSelection, DraggablePart newActive)
		{
			ClearReferenceHighlight();
			HashSet<DraggablePart> newSet = new HashSet<DraggablePart>(newSelection);
			bool membershipChanged = newSet.Count != multiSelection.Count;
			if (!membershipChanged)
			{
				foreach (DraggablePart part in multiSelection)
				{
					if (!newSet.Contains(part))
					{
						membershipChanged = true;
						break;
					}
				}
			}

			foreach (DraggablePart part in multiSelection)
			{
				if (part != null && !newSet.Contains(part))
				{
					part.SetSelected(false);
				}
			}
			foreach (DraggablePart part in newSet)
			{
				if (part != null && !multiSelection.Contains(part))
				{
					part.SetSelected(true);
				}
			}
			multiSelection.Clear();
			foreach (DraggablePart part in newSet)
			{
				if (part != null)
				{
					multiSelection.Add(part);
				}
			}

			if (membershipChanged)
			{
				ResetTemporaryGroupPivotFromSelection();
			}

			selectedPart = newActive;
			inspectorTarget = selectedPart != null ? InspectorTarget.Part : InspectorTarget.None;
			RefreshTimeline();
			RefreshPivotHandle();
		}

		/// <summary>Unhighlights and forgets the selected scale-reference overlay, if any.</summary>
		private static void ClearReferenceHighlight()
		{
			if (selectedReference != null)
			{
				selectedReference.SetSelected(false);
				selectedReference = null;
			}
		}

		/// <summary>Clears part selection visuals without touching inspectorTarget -- used by SelectReference so it can then claim the Inspector.</summary>
		private static void ClearPartSelectionVisuals()
		{
			foreach (DraggablePart part in multiSelection)
			{
				if (part != null)
				{
					part.SetSelected(false);
				}
			}
			multiSelection.Clear();
			selectedPart = null;
			hasTemporaryGroupPivot = false;
		}

		/// <summary>Adds a scale-reference overlay to the Main Viewport, defaulting to Gerald, and selects it.</summary>
		/// <remarks>Editor-only: never written to rig.json and not part of undo history. Offset to the left of origin so a newly added Gerald does not sit on top of the character being posed. Each additional overlay is staggered further left.</remarks>
		internal static void AddReference(string metaExoName = null)
		{
			if (referencesRoot == null)
			{
				SetStatus("Animator is not built yet.");
				return;
			}
			if (string.IsNullOrEmpty(metaExoName))
			{
				metaExoName = ReferenceCharacter.DefaultMetaExo;
			}

			GameObject go = new GameObject("ReferenceCharacter", typeof(BoxCollider), typeof(ReferenceCharacter));
			go.transform.SetParent(referencesRoot, false);
			go.transform.position = new Vector3(-1.5f - loadedReferences.Count * 0.35f, 0f, 0f);
			CharacterLabLayers.ApplyToHierarchy(go);

			ReferenceCharacter reference = go.GetComponent<ReferenceCharacter>();
			if (!reference.TryLoad(metaExoName, out string error))
			{
				UnityEngine.Object.Destroy(go);
				SetStatus("Add Reference failed: " + error);
				return;
			}
			loadedReferences.Add(reference);
			SelectReference(reference);
			SetStatus("Added reference '" + reference.DisplayName + "'. Move and rotate it; scale is locked. Select it to change character, pose, or position.");
		}

		/// <summary>Selects a scale-reference overlay (clearing any part selection) and shows it in the Inspector.</summary>
		internal static void SelectReference(ReferenceCharacter reference)
		{
			ClearPartSelectionVisuals();
			if (selectedReference != null && selectedReference != reference)
			{
				selectedReference.SetSelected(false);
			}
			selectedReference = reference;
			if (selectedReference != null)
			{
				selectedReference.SetSelected(true);
				inspectorTarget = InspectorTarget.Reference;
			}
			else
			{
				inspectorTarget = InspectorTarget.None;
			}
			RefreshTimeline();
			RefreshPivotHandle();
		}

		/// <summary>Removes the currently selected scale-reference overlay from the viewport.</summary>
		internal static void RemoveSelectedReference()
		{
			if (selectedReference == null)
			{
				return;
			}
			ReferenceCharacter toRemove = selectedReference;
			loadedReferences.Remove(toRemove);
			selectedReference = null;
			inspectorTarget = InspectorTarget.None;
			if (toRemove != null)
			{
				UnityEngine.Object.Destroy(toRemove.gameObject);
			}
			RefreshTimeline();
			RefreshPivotHandle();
			SetStatus("Removed scale reference.");
		}

		/// <summary>Reloads the selected overlay as a different shipped character, keeping its current position and rotation.</summary>
		internal static void SetSelectedReferenceCharacter(string metaExoName)
		{
			if (selectedReference == null)
			{
				return;
			}
			if (!selectedReference.TryLoad(metaExoName, out string error))
			{
				SetStatus("Could not switch reference: " + error);
				return;
			}
			CharacterLabLayers.ApplyToHierarchy(selectedReference.gameObject);
			RefreshTimeline();
			SetStatus("Reference character set to '" + selectedReference.DisplayName + "'.");
		}

		/// <summary>Sets the selected overlay's world position. Scale is not writable.</summary>
		internal static void SetSelectedReferencePosition(float x, float y)
		{
			if (selectedReference == null)
			{
				return;
			}
			selectedReference.transform.position = new Vector3(x, y, 0f);
			RefreshTimeline();
		}

		/// <summary>Sets the selected overlay's Z rotation in degrees. Scale is not writable.</summary>
		internal static void SetSelectedReferenceRotation(float degrees)
		{
			if (selectedReference == null)
			{
				return;
			}
			selectedReference.RotationDegrees = degrees;
			RefreshTimeline();
		}

		/// <summary>Sets which animation pose the selected overlay shows (first frame of that clip).</summary>
		internal static void SetSelectedReferenceAnimation(int animationIndex)
		{
			if (selectedReference == null)
			{
				return;
			}
			selectedReference.SetAnimationIndex(animationIndex);
			RefreshTimeline();
		}

		/// <summary>Shows or hides the selected overlay without destroying it.</summary>
		internal static void SetSelectedReferenceVisible(bool visible)
		{
			if (selectedReference == null)
			{
				return;
			}
			selectedReference.Visible = visible;
			RefreshTimeline();
		}

		/// <summary>Sets the selected overlay's mesh tint alpha in 0..1.</summary>
		internal static void SetSelectedReferenceOpacity(float opacity)
		{
			if (selectedReference == null)
			{
				return;
			}
			selectedReference.Opacity = opacity;
			RefreshTimeline();
		}

		/// <summary>"Center Selected": moves the multi-selection's centroid to world origin, preserving relative positions, as a single undo step.</summary>
		internal static void CenterSelectedParts()
		{
			if (multiSelection.Count <= 1)
			{
				return;
			}

			List<DraggablePart> movable = new List<DraggablePart>();
			Vector3 centroid = Vector3.zero;
			foreach (DraggablePart part in multiSelection)
			{
				if (part == null || IsPartApproximateInActiveFrame(part.PartName))
				{
					continue;
				}
				movable.Add(part);
				centroid += GetStoredCenter(part);
			}
			if (movable.Count == 0)
			{
				return;
			}
			centroid /= movable.Count;

			AnimatorHistory.CaptureBeforeChange(string.Format("{0} parts centered — {1}", movable.Count,
				activeClip != null ? activeClip.Name + " " + (activeFrameIndex + 1) : "Rest Pose"));
			foreach (DraggablePart part in movable)
			{
				Vector3 current = GetStoredCenter(part);
				part.transform.position = new Vector3(current.x - centroid.x, current.y - centroid.y, 0f);
			}
			CommitCurrentPoseToActiveContext();
			RefreshTimeline();
		}

		/// <summary>Builds an AnimatorHistory description like "Foo moved -- Walk 3" or "Foo mass moved -- Walk" (Mass Edit) or "Foo moved -- Rest Pose".</summary>
		internal static string DescribeVerbContext(DraggablePart part, string verb)
		{
			bool mass = activeClip != null && massEditEnabled && IsPartMultiSelected(part);
			string verbPart = mass ? "mass " + verb : verb;
			string context = activeClip != null
				? activeClip.Name + (mass ? string.Empty : " " + (activeFrameIndex + 1))
				: "Rest Pose";
			return part.PartName + " " + verbPart + " — " + context;
		}

		/// <summary>Same as DescribeVerbContext, for a group drag -- leads with the part count instead of a single name, and says "mass" when Mass Edit will propagate the edit across the clip.</summary>
		internal static string DescribeGroupContext(int count, string verb)
		{
			bool mass = activeClip != null && massEditEnabled;
			string verbPart = mass ? "mass " + verb : verb;
			string context = activeClip != null
				? activeClip.Name + (mass ? string.Empty : " " + (activeFrameIndex + 1))
				: "Rest Pose";
			return count + " parts " + verbPart + " — " + context;
		}

		/// <summary>Sets a part's world position directly, as the Inspector's fields do -- same commit path a Move drag uses, so it's indistinguishable from Undo/Save's perspective.</summary>
		/// <remarks>Guarded against Approximate poses the same way EditorInputController.TryBeginDrag is (see DraggablePart.IsAffinePose), since writing to a part's Transform while it renders through the read-only affine-mesh path would be silently discarded.</remarks>
		internal static void SetPartPosition(DraggablePart part, Vector2 worldPosition)
		{
			if (part == null || IsPartApproximateInActiveFrame(part.PartName))
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(DescribeVerbContext(part, "moved"));
			part.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
			CommitCurrentPoseToActiveContext();
		}

		/// <summary>Sets a part's rotation directly, pivoting around RestPose.PivotOffset like a Rotate drag would.</summary>
		internal static void SetPartRotation(DraggablePart part, float rotationDegrees)
		{
			if (part == null || IsPartApproximateInActiveFrame(part.PartName))
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(DescribeVerbContext(part, "rotated"));
			Vector2 baseAnchor = GetBaseAnchor(part);
			SetPartTransform(part, baseAnchor, rotationDegrees, part.ShearDegrees, part.ScaleX, part.ScaleY);
			CommitCurrentPoseToActiveContext();
		}

		/// <summary>Sets a part's scale directly, pivoting around RestPose.PivotOffset like a Scale drag would.</summary>
		internal static void SetPartScale(DraggablePart part, float scaleX, float scaleY)
		{
			if (part == null || IsPartApproximateInActiveFrame(part.PartName))
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(DescribeVerbContext(part, "scaled"));
			Vector2 baseAnchor = GetBaseAnchor(part);
			SetPartTransform(part, baseAnchor, part.RotationDegrees, part.ShearDegrees, scaleX, scaleY);
			CommitCurrentPoseToActiveContext();
		}

		/// <summary>Sets a part's shear directly -- Inspector-only, since shear has no natural drag gesture.</summary>
		internal static void SetPartShear(DraggablePart part, float shearDegrees)
		{
			if (part == null || IsPartApproximateInActiveFrame(part.PartName))
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(DescribeVerbContext(part, "sheared"));
			Vector2 baseAnchor = GetBaseAnchor(part);
			SetPartTransform(part, baseAnchor, part.RotationDegrees, shearDegrees, part.ScaleX, part.ScaleY);
			CommitCurrentPoseToActiveContext();
		}

		/// <summary>The active frame's DeltaPosition for a part (0 outside a clip), needed to derive a pivot's world position (RestPose.PivotOffset is rest-relative and frame-independent).</summary>
		private static Vector2 GetActiveDeltaPosition(string partName)
		{
			if (activeClip == null)
			{
				return Vector2.zero;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			return frame.Poses.TryGetValue(partName, out PartPose pose) ? pose.DeltaPosition : Vector2.zero;
		}

		/// <summary>The DeltaPosition to use for pivot math -- from the live transform normally, or from the committed pose for Approximate/read-only affine parts (which never touch transform.position).</summary>
		internal static Vector2 GetDeltaPositionForPivotOps(DraggablePart part)
		{
			return part.IsAffinePose ? GetActiveDeltaPosition(part.PartName) : GetCurrentDeltaPosition(part);
		}

		/// <summary>The part's pivot's current world position.</summary>
		internal static Vector2 GetPivotWorldPosition(DraggablePart part)
		{
			RestPose rest = GetOrCreateRestPose(part.PartName);
			return rest.Position + rest.PivotOffset + GetDeltaPositionForPivotOps(part);
		}

		/// <summary>The part's pivot offset alone (rest-relative, frame-independent), for group rotate/scale to derive a pivot from stored pose data rather than the live transform.</summary>
		/// <remarks>Reading the live transform in a multi-tick drag loop would be a feedback loop -- each tick would rotate/scale an already-rotated/scaled result instead of the true starting pose, compounding every frame.</remarks>
		internal static Vector2 GetPivotOffset(DraggablePart part)
		{
			return GetOrCreateRestPose(part.PartName).PivotOffset;
		}

		/// <summary>Sets a part's pivot to a world position, given the DeltaPosition captured before the drag began.</summary>
		/// <remarks>deltaPositionBeforeChange must be captured once before the drag, not re-derived per call, or each write would feed back into the next read and drift the pivot away from the mouse.</remarks>
		internal static void SetPivotWorldPosition(DraggablePart part, Vector2 deltaPositionBeforeChange, Vector2 worldPosition)
		{
			RestPose rest = GetOrCreateRestPose(part.PartName);
			rest.PivotOffset = worldPosition - rest.Position - deltaPositionBeforeChange;
			RefreshPivotHandle();
		}

		/// <summary>Keeps the pivot marker tracking the selected part's pivot, or the multi-select temp pivot.</summary>
		internal static void RefreshPivotHandle()
		{
			if (pivotHandleObject == null)
			{
				return;
			}

			if (AnimatorFeelRules.UseTemporaryGroupPivot(multiSelection.Count, hasTemporaryGroupPivot))
			{
				pivotHandleObject.transform.position = new Vector3(temporaryGroupPivotWorld.x, temporaryGroupPivotWorld.y, -0.5f);
				pivotHandleObject.SetActive(true);
				return;
			}

			if (selectedPart == null)
			{
				pivotHandleObject.SetActive(false);
				return;
			}

			Vector2 pivotWorld = GetPivotWorldPosition(selectedPart);
			pivotHandleObject.transform.position = new Vector3(pivotWorld.x, pivotWorld.y, -0.5f);
			pivotHandleObject.SetActive(true);
		}

		/// <summary>Shared center for group rotate/scale: the session temp pivot when set, otherwise the selection centroid.</summary>
		internal static Vector2 GetGroupPivotWorld(IReadOnlyList<DraggablePart> group)
		{
			if (AnimatorFeelRules.UseTemporaryGroupPivot(group != null ? group.Count : 0, hasTemporaryGroupPivot))
			{
				return temporaryGroupPivotWorld;
			}

			return AnimatorGroupMath.AveragePivotWorld(group);
		}

		/// <summary>Moves the session temp group pivot without writing any part's RestPose.PivotOffset.</summary>
		internal static void SetTemporaryGroupPivotWorld(Vector2 worldPosition)
		{
			hasTemporaryGroupPivot = true;
			temporaryGroupPivotWorld = worldPosition;
			RefreshPivotHandle();
		}

		/// <summary>Places the temp group pivot at the current selection centroid, or clears it when fewer than two parts are selected.</summary>
		private static void ResetTemporaryGroupPivotFromSelection()
		{
			if (multiSelection.Count <= 1)
			{
				hasTemporaryGroupPivot = false;
				return;
			}

			List<DraggablePart> group = new List<DraggablePart>(multiSelection);
			hasTemporaryGroupPivot = true;
			temporaryGroupPivotWorld = AnimatorGroupMath.AveragePivotWorld(group);
		}

		/// <summary>Whether a part is actually present (not excluded) in the active clip/frame; Rest Pose has no exclusion concept, so everything counts as included there.</summary>
		internal static bool IsPartIncludedInActiveFrame(string partName)
		{
			if (activeClip == null)
			{
				return true;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			return !frame.Poses.TryGetValue(partName, out PartPose pose) || pose.Included;
		}

		/// <summary>Whether a part's pose in the active frame is Approximate, surfaced in the parts list to explain why a part can't be dragged.</summary>
		internal static bool IsPartApproximateInActiveFrame(string partName)
		{
			if (activeClip == null)
			{
				return false;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			return frame.Poses.TryGetValue(partName, out PartPose pose) && pose.Approximate;
		}

		/// <summary>Convenience for UI (the "Convert to Editable" button) that acts on whichever part is currently selected.</summary>
		internal static bool IsSelectedPartApproximateInActiveFrame()
		{
			return selectedPart != null && IsPartApproximateInActiveFrame(selectedPart.PartName);
		}

		/// <summary>Discards a frame's degenerate imported matrix in favor of its best-effort rotation/shear/scale fallback, making the pose freely editable from here on.</summary>
		/// <remarks>One-way after a Save (nothing recovers the original raw matrix), though Undo still reverts it within the session. Takes an explicit part since each Frame-inspector row converts its own part, not necessarily the viewport-selected one.</remarks>
		internal static void ConvertPartPoseToEditable(DraggablePart part)
		{
			if (activeClip == null || part == null)
			{
				return;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			if (!frame.Poses.TryGetValue(part.PartName, out PartPose pose) || !pose.Approximate)
			{
				return;
			}

			AnimatorHistory.CaptureBeforeChange(part.PartName + " converted to editable — " + activeClip.Name + " " + (activeFrameIndex + 1));
			pose.Approximate = false;
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
			SetStatus(string.Format(
				"'{0}' is now editable on this frame — converted from its degenerate source matrix to a best-effort rotation/shear/scale fallback. Re-saving will bake this in; the original matrix can't be recovered afterward (Undo still works for now).",
				part.PartName));
		}

		/// <summary>Toggles a part's persistent Visible eye-toggle.</summary>
		internal static void ToggleVisibility(DraggablePart part)
		{
			AnimatorHistory.CaptureBeforeChange(part.PartName + " visibility toggled");
			part.Visible = !part.Visible;
			RefreshTimeline();
		}

		/// <summary>Gives an excluded part a real (default-rest) pose in the active frame, making it visible and editable again.</summary>
		internal static void IncludePartInActiveFrame(DraggablePart part)
		{
			if (activeClip == null)
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(part.PartName + " added — " + activeClip.Name + " " + (activeFrameIndex + 1));
			part.Visible = true;
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			frame.Poses[part.PartName] = DefaultPoseFor(part.PartName);
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
			SetStatus(string.Format("Added '{0}' to this frame at its rest pose — drag it into place.", part.PartName));
		}

		/// <summary>Removes a part from the active frame by setting Included=false (not deleting the entry -- an absent entry falls back to rest/visible, which would make the part reappear).</summary>
		internal static void RemovePartFromActiveFrame(DraggablePart part)
		{
			if (activeClip == null || part == null)
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange(part.PartName + " removed — " + activeClip.Name + " " + (activeFrameIndex + 1));
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			frame.Poses[part.PartName] = new PartPose { Included = false };
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
			SetStatus(string.Format("Removed '{0}' from this frame.", part.PartName));
		}

		/// <summary>Removes a part from every frame of the active clip (not every clip -- scoped like Mass Edit, not a rig-wide change).</summary>
		internal static void MassRemovePartFromClip(DraggablePart part)
		{
			if (activeClip == null || part == null)
			{
				SetStatus("Select a clip first (not Rest Pose) to mass-remove a part.");
				return;
			}
			AnimatorHistory.CaptureBeforeChange(part.PartName + " removed — " + activeClip.Name);
			int removedCount = 0;
			foreach (PoseFrame frame in activeClip.PoseFrames)
			{
				if (frame.Poses.TryGetValue(part.PartName, out PartPose pose) && pose.Included)
				{
					frame.Poses[part.PartName] = new PartPose { Included = false };
					removedCount++;
				}
			}
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
			SetStatus(string.Format("Removed '{0}' from {1} frame(s) of clip '{2}'.", part.PartName, removedCount, activeClip.Name));
		}

		/// <summary>Batch part-swap: copies oldPart's RestPose/StaticLayer onto newPart, then clones oldPart's pose into newPart in every clip, removing oldPart. Rig-wide (all clips), unlike Mass Edit's active-clip-only scope.</summary>
		internal static void MassReplacePart(DraggablePart oldPart, DraggablePart newPart)
		{
			if (oldPart == null || newPart == null || oldPart == newPart)
			{
				return;
			}

			AnimatorHistory.CaptureBeforeChange(oldPart.PartName + " replaced with " + newPart.PartName);

			RestPose oldRest = GetOrCreateRestPose(oldPart.PartName);
			RestPose newRest = GetOrCreateRestPose(newPart.PartName);
			newRest.Position = oldRest.Position;
			newRest.RotationDegrees = oldRest.RotationDegrees;
			newRest.ShearDegrees = oldRest.ShearDegrees;
			newRest.ScaleX = oldRest.ScaleX;
			newRest.ScaleY = oldRest.ScaleY;
			newRest.PivotOffset = oldRest.PivotOffset;
			newPart.StaticLayer = oldPart.StaticLayer;

			int replacedCount = 0;
			foreach (AnimationClip clip in clips)
			{
				foreach (PoseFrame frame in clip.PoseFrames)
				{
					if (frame.Poses.TryGetValue(oldPart.PartName, out PartPose oldPose) && oldPose.Included)
					{
						frame.Poses[newPart.PartName] = ClonePose(oldPose);
						frame.Poses[oldPart.PartName] = new PartPose { Included = false };
						replacedCount++;
					}
				}
			}

			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			SelectPart(newPart);
			SetStatus(string.Format(
				"Replaced '{0}' with '{1}' in {2} frame(s) across {3} clip(s) (plus Rest Pose). '{0}' removed from those frames — use Mass Edit to fine-tune '{1}'.",
				oldPart.PartName, newPart.PartName, replacedCount, clips.Count));
		}

		/// <summary>Field-by-field value copy of a pose (not a reference share -- mutating the clone later must never alias back into the source).</summary>
		private static PartPose ClonePose(PartPose source)
		{
			return new PartPose
			{
				DeltaPosition = source.DeltaPosition,
				RotationDegrees = source.RotationDegrees,
				ShearDegrees = source.ShearDegrees,
				ScaleX = source.ScaleX,
				ScaleY = source.ScaleY,
				Included = true,
				RenderOrderIndex = source.RenderOrderIndex,
				Approximate = source.Approximate,
				RawA = source.RawA,
				RawB = source.RawB,
				RawC = source.RawC,
				RawD = source.RawD,
				RawTranslateX = source.RawTranslateX,
				RawTranslateY = source.RawTranslateY
			};
		}

		/// <summary>Per-frame draw-order reorder for the Frame inspector's parts container, using PartPose.RenderOrderIndex instead of the rig-wide StaticLayer that MovePartLayer swaps.</summary>
		/// <remarks>The list is sorted DESCENDING (front-most first) -- deliberately the opposite of SceneTreePanel's ascending convention -- so direction=-1 moves toward the front. Only meaningful for a real clip frame; Rest Pose has no RenderOrderIndex and reorders via MovePartLayer instead.</remarks>
		internal static void MoveFramePartOrder(DraggablePart part, int direction)
		{
			if (activeClip == null || part == null)
			{
				return;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			NormalizeFrameRenderOrder(frame);

			List<(DraggablePart part, PartPose pose)> included = CollectIncludedFrameParts(frame);
			included.Sort((a, b) => b.pose.RenderOrderIndex.CompareTo(a.pose.RenderOrderIndex));

			int index = included.FindIndex(entry => entry.part == part);
			int swapIndex = index + direction;
			if (index == -1 || swapIndex < 0 || swapIndex >= included.Count)
			{
				return;
			}

			AnimatorHistory.CaptureBeforeChange(part.PartName + " reordered — " + activeClip.Name + " " + (activeFrameIndex + 1));
			int temp = included[index].pose.RenderOrderIndex;
			included[index].pose.RenderOrderIndex = included[swapIndex].pose.RenderOrderIndex;
			included[swapIndex].pose.RenderOrderIndex = temp;
			ApplyContextPoseToParts();
			RefreshTimeline();
		}

		private static List<(DraggablePart part, PartPose pose)> CollectIncludedFrameParts(PoseFrame frame)
		{
			List<(DraggablePart part, PartPose pose)> included = new List<(DraggablePart, PartPose)>();
			foreach (DraggablePart loaded in loadedParts)
			{
				if (loaded != null && frame.Poses.TryGetValue(loaded.PartName, out PartPose pose) && pose.Included)
				{
					included.Add((loaded, pose));
				}
			}
			return included;
		}

		/// <summary>Assigns every included part in this frame a concrete RenderOrderIndex before a swap, so two parts both still defaulting to "-1 (use static layer)" actually exchange their effective order. Idempotent.</summary>
		private static void NormalizeFrameRenderOrder(PoseFrame frame)
		{
			List<(DraggablePart part, PartPose pose)> included = CollectIncludedFrameParts(frame);
			included.Sort((a, b) =>
			{
				int aOrder = a.pose.RenderOrderIndex >= 0 ? a.pose.RenderOrderIndex : a.part.StaticLayer;
				int bOrder = b.pose.RenderOrderIndex >= 0 ? b.pose.RenderOrderIndex : b.part.StaticLayer;
				return aOrder.CompareTo(bOrder);
			});
			for (int i = 0; i < included.Count; i++)
			{
				included[i].pose.RenderOrderIndex = i;
			}
		}

		/// <summary>Rig-wide draw-order reorder, swapping StaticLayer between adjacent parts.</summary>
		internal static void MovePartLayer(DraggablePart part, int direction)
		{
			List<DraggablePart> sorted = new List<DraggablePart>(loadedParts);
			sorted.RemoveAll(p => p == null);
			sorted.Sort((a, b) => a.StaticLayer.CompareTo(b.StaticLayer));

			int index = sorted.IndexOf(part);
			int swapIndex = index + direction;
			if (index == -1 || swapIndex < 0 || swapIndex >= sorted.Count)
			{
				return;
			}

			AnimatorHistory.CaptureBeforeChange(part.PartName + " layer moved");
			int temp = sorted[index].StaticLayer;
			sorted[index].StaticLayer = sorted[swapIndex].StaticLayer;
			sorted[swapIndex].StaticLayer = temp;
			ApplyContextPoseToParts();
			RefreshTimeline();
		}

		/// <summary>Reassigns every loaded part a dense, gap-free StaticLayer (0..count-1) in current sort order.</summary>
		private static void NormalizeLayers()
		{
			List<DraggablePart> sorted = new List<DraggablePart>(loadedParts);
			sorted.RemoveAll(p => p == null);
			sorted.Sort((a, b) => a.StaticLayer.CompareTo(b.StaticLayer));
			for (int i = 0; i < sorted.Count; i++)
			{
				sorted[i].StaticLayer = i;
			}
		}

		/// <summary>Returns the rest pose for the given part name, creating and storing a default one if none exists yet.</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) so RigSaveService's own sidecar writers can read rest-pose data without needing this method's own logic duplicated.</remarks>
		internal static RestPose GetOrCreateRestPose(string partName)
		{
			if (!restPoses.TryGetValue(partName, out RestPose rest))
			{
				rest = new RestPose();
				restPoses[partName] = rest;
			}
			return rest;
		}

		/// <summary>Bumps <see cref="PoseContextGeneration"/> after the active clip or frame changes.</summary>
		private static void BumpPoseContext()
		{
			PoseContextGeneration++;
		}

		/// <summary>Drops a live viewport drag after a clip or frame switch so mouse-up cannot commit into the new context.</summary>
		/// <remarks>
		/// EventSystem runs before EditorInputController.Update, so releasing a drag on a timeline
		/// chip selects the new frame first. CommitCurrentPoseToActiveContext already wrote the old
		/// context; clearing the drag lets ApplyContextPoseToParts apply the new pose to that part.
		/// Do not call from TickPlayback — Mass Edit needs the drag skip while playback advances.
		/// See docs/issues/unresolved/animator-pose-leaks-across-frames.md.
		/// </remarks>
		private static void CancelViewportDragAfterContextSwitch()
		{
			EditorInputController.CancelActiveDrag();
		}

		/// <summary>Writes the parts' current live transforms into whichever context was active before this call (rest pose or active keyframe). Every activeClip/activeFrameIndex switch calls this first, then ApplyContextPoseToParts() after.</summary>
		/// <remarks>Resets a nonzero activeBakedIndex first -- those live transforms are a generated baked sub-frame, not an authored edit, so capturing them here would silently overwrite the real Poses with a computed value. For Rest Pose, subtracts the pivot's own rotation/scale-dependent center offset before storing rest.Position, so a part with a real pivot doesn't get that offset baked in, then compensates clip deltas so existing clips keep their world positions. For a keyframe, skips a part not present in the frame or with an approximate (read-only affine) pose, feeds Mass Edit for every multi-selected part (not only SelectedPart), and preserves the pose's existing draw-order position.</remarks>
		internal static void CommitCurrentPoseToActiveContext()
		{
			if (activeBakedIndex != 0)
			{
				activeBakedIndex = 0;
				ApplyContextPoseToParts();
			}
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null)
				{
					continue;
				}
				RestPose rest = GetOrCreateRestPose(part.PartName);
				if (activeClip == null)
				{
					Vector2 oldPosition = rest.Position;
					Vector2 centerOffset = ComputeCenterOffsetFromPivot(rest.PivotOffset, part.RotationDegrees, part.ShearDegrees, part.ScaleX, part.ScaleY);
					rest.Position = (Vector2)part.transform.position - centerOffset;
					rest.RotationDegrees = part.RotationDegrees;
					rest.ShearDegrees = part.ShearDegrees;
					rest.ScaleX = part.ScaleX;
					rest.ScaleY = part.ScaleY;
					Vector2 restMove = rest.Position - oldPosition;
					if (restMove.sqrMagnitude > 0.0000001f)
					{
						CompensateClipDeltasForPart(part.PartName, restMove);
					}
				}
				else
				{
					PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
					if (frame.Poses.TryGetValue(part.PartName, out PartPose existing) && (!existing.Included || existing.Approximate))
					{
						continue;
					}

					Vector2 newDeltaPosition = GetCurrentDeltaPosition(part);
					float newRotation = part.RotationDegrees;
					float newShear = part.ShearDegrees;
					float newScaleX = part.ScaleX;
					float newScaleY = part.ScaleY;

					if (massEditEnabled && IsPartMultiSelected(part) && existing != null)
					{
						PropagateMassEdit(part.PartName, existing, newDeltaPosition, newRotation, newShear, newScaleX, newScaleY);
					}

					frame.Poses[part.PartName] = new PartPose
					{
						DeltaPosition = newDeltaPosition,
						RotationDegrees = newRotation,
						ShearDegrees = newShear,
						ScaleX = newScaleX,
						ScaleY = newScaleY,
						RenderOrderIndex = existing?.RenderOrderIndex ?? -1
					};
				}
			}
		}

		/// <summary>Keeps authored clip world positions when Rest Pose moves by subtracting that move from every included pose delta for the part.</summary>
		private static void CompensateClipDeltasForPart(string partName, Vector2 restMove)
		{
			foreach (AnimationClip clip in clips)
			{
				foreach (PoseFrame frame in clip.PoseFrames)
				{
					if (!frame.Poses.TryGetValue(partName, out PartPose pose) || !pose.Included || pose.Approximate)
					{
						continue;
					}

					float dx = pose.DeltaPosition.x;
					float dy = pose.DeltaPosition.y;
					AnimatorFeelRules.CompensateClipDelta(ref dx, ref dy, restMove.x, restMove.y);
					pose.DeltaPosition = new Vector2(dx, dy);
				}
			}
		}

		/// <summary>Mass Edit's propagation step: nudges every other frame's stored pose by this commit's change, called once per commit per multi-selected part before that part's own frame's new value is written.</summary>
		/// <remarks>Position/Rotation/Shear propagate as a flat delta; Scale propagates as a ratio instead, since additive scale could push a frame with a very different baseline to zero or negative. No-ops if nothing actually changed. Skips activeFrameIndex itself (which gets the literal new value from the normal commit right after this returns). A group drag with Mass Edit on therefore walks this once per selected part, so a whole-character move/rotate/scale applies across every frame of the clip.</remarks>
		private static void PropagateMassEdit(string partName, PartPose oldPose, Vector2 newDeltaPosition,
			float newRotation, float newShear, float newScaleX, float newScaleY)
		{
			Vector2 positionDelta = newDeltaPosition - oldPose.DeltaPosition;
			float rotationDelta = newRotation - oldPose.RotationDegrees;
			float shearDelta = newShear - oldPose.ShearDegrees;
			float scaleXRatio = Mathf.Abs(oldPose.ScaleX) > 0.0001f ? newScaleX / oldPose.ScaleX : 1f;
			float scaleYRatio = Mathf.Abs(oldPose.ScaleY) > 0.0001f ? newScaleY / oldPose.ScaleY : 1f;

			if (positionDelta == Vector2.zero && Mathf.Approximately(rotationDelta, 0f) && Mathf.Approximately(shearDelta, 0f)
				&& Mathf.Approximately(scaleXRatio, 1f) && Mathf.Approximately(scaleYRatio, 1f))
			{
				return;
			}

			for (int i = 0; i < activeClip.PoseFrames.Count; i++)
			{
				if (i == activeFrameIndex)
				{
					continue;
				}
				PoseFrame otherFrame = activeClip.PoseFrames[i];
				if (!otherFrame.Poses.TryGetValue(partName, out PartPose otherPose) || !otherPose.Included || otherPose.Approximate)
				{
					continue;
				}
				otherPose.DeltaPosition += positionDelta;
				otherPose.RotationDegrees += rotationDelta;
				otherPose.ShearDegrees += shearDelta;
				otherPose.ScaleX *= scaleXRatio;
				otherPose.ScaleY *= scaleYRatio;
			}
		}

		/// <summary>The authoritative, editing-anchored pose application -- always driven by the ACTIVE AUTHORED frame's Poses, never by BakedFrames. Playback/ghost-preview call ApplyPoseFrameToParts directly instead, with a specific baked sub-frame.</summary>
		/// <remarks>Rebakes first so BakedFrames is always current even for a caller that just mutated Poses/Easing/EasingSteps and calls this in the same method, without each call site needing to remember to rebake itself.</remarks>
		private static void ApplyContextPoseToParts()
		{
			RebakeAllClips();
			if (activeClip == null)
			{
				foreach (DraggablePart part in loadedParts)
				{
					if (part == null || part == ActivelyDraggingPart || activelyDraggingGroup.Contains(part))
					{
						continue;
					}
					RestPose rest = GetOrCreateRestPose(part.PartName);
					part.FrameVisible = true;
					part.SortingOrder = part.StaticLayer;
					SetPartTransform(part, rest.Position, rest.RotationDegrees, rest.ShearDegrees, rest.ScaleX, rest.ScaleY);
				}
				RefreshPivotHandle();
				return;
			}
			ApplyPoseFrameToParts(activeClip.PoseFrames[activeFrameIndex]);
		}

		/// <summary>Applies exactly the given PoseFrame's Poses to every part's live transform. Shared by ApplyContextPoseToParts (the active authored frame) and playback/ghost-preview (a specific baked, possibly-interpolated sub-frame).</summary>
		/// <remarks>Takes the frame as a plain parameter rather than resolving activeBakedIndex internally, so a baked/interpolated preview can never accidentally become "the" pose read back out. Never reapplies onto a part mid-drag (or mid-group-drag). RenderOrderIndex overrides StaticLayer per-frame when set; a part with no recorded pose yet defaults to rest.</remarks>
		private static void ApplyPoseFrameToParts(PoseFrame frame)
		{
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null || part == ActivelyDraggingPart || activelyDraggingGroup.Contains(part))
				{
					continue;
				}
				RestPose rest = GetOrCreateRestPose(part.PartName);
				if (frame.Poses.TryGetValue(part.PartName, out PartPose pose))
				{
					if (!pose.Included)
					{
						part.FrameVisible = false;
						continue;
					}
					part.FrameVisible = true;
					part.SortingOrder = pose.RenderOrderIndex >= 0 ? pose.RenderOrderIndex : part.StaticLayer;
					if (pose.Approximate)
					{
						part.SetAffinePose(rest.Position, pose.RawA, pose.RawB, pose.RawC, pose.RawD, pose.RawTranslateX, pose.RawTranslateY);
					}
					else
					{
						SetPartTransform(part, rest.Position + pose.DeltaPosition, pose.RotationDegrees, pose.ShearDegrees, pose.ScaleX, pose.ScaleY);
					}
				}
				else
				{
					part.FrameVisible = true;
					part.SortingOrder = part.StaticLayer;
					SetPartTransform(part, rest.Position, rest.RotationDegrees, rest.ShearDegrees, rest.ScaleX, rest.ScaleY);
				}
			}
			RefreshPivotHandle();
		}

		/// <summary>The offset from a part's pivot to its rendered center (sprite-local vertex 0) under a given rotation/shear/scale, so that transform.position (which Unity always rotates/scales around) can be derived from baseAnchor + this offset.</summary>
		/// <remarks>Formula: center = baseAnchor + (I-M)*PivotOffset. When PivotOffset is zero this reduces to center=baseAnchor (the old pivot-unaware behavior), so parts without a custom pivot render exactly as before.</remarks>
		private static Vector2 ComputeCenterOffsetFromPivot(Vector2 pivotOffset, float rotationDegrees, float shearDegrees, float scaleX, float scaleY)
		{
			AffineMatrixMath.ComposeLinear(rotationDegrees, shearDegrees, scaleX, scaleY, out float mA, out float mB, out float mC, out float mD);
			float x = pivotOffset.x - (mA * pivotOffset.x + mC * pivotOffset.y);
			float y = pivotOffset.y - (mB * pivotOffset.x + mD * pivotOffset.y);
			return new Vector2(x, y);
		}

		/// <summary>Sets a part's live transform from a pivot-relative pose (baseAnchor + rotation/shear/scale), converting baseAnchor into the actual transform.position via ComputeCenterOffsetFromPivot.</summary>
		internal static void SetPartTransform(DraggablePart part, Vector2 baseAnchor, float rotationDegrees, float shearDegrees, float scaleX, float scaleY)
		{
			part.ClearAffinePose();
			RestPose rest = GetOrCreateRestPose(part.PartName);
			Vector2 centerOffset = ComputeCenterOffsetFromPivot(rest.PivotOffset, rotationDegrees, shearDegrees, scaleX, scaleY);
			Vector2 center = baseAnchor + centerOffset;
			part.transform.position = new Vector3(center.x, center.y, 0f);
			part.RotationDegrees = rotationDegrees;
			part.ShearDegrees = shearDegrees;
			part.ScaleX = scaleX;
			part.ScaleY = scaleY;
		}

		/// <summary>Inverts SetPartTransform to recover the current deltaPosition from the part's live transform. A pure read, safe to call repeatedly; only meaningful for the normal Transform-driven path (see GetDeltaPositionForPivotOps for the affine-pose fallback).</summary>
		private static Vector2 GetCurrentDeltaPosition(DraggablePart part)
		{
			RestPose rest = GetOrCreateRestPose(part.PartName);
			Vector2 centerOffset = ComputeCenterOffsetFromPivot(rest.PivotOffset, part.RotationDegrees, part.ShearDegrees, part.ScaleX, part.ScaleY);
			return (Vector2)part.transform.position - rest.Position - centerOffset;
		}

		/// <summary>rest.Position + the part's current deltaPosition -- the baseAnchor SetPartTransform expects. Callers should freeze this once before a rotate/shear/scale change, not re-derive it on every call (same drift hazard as SetPivotWorldPosition).</summary>
		internal static Vector2 GetBaseAnchor(DraggablePart part)
		{
			RestPose rest = GetOrCreateRestPose(part.PartName);
			return rest.Position + GetCurrentDeltaPosition(part);
		}

		/// <summary>Reads the currently active frame's (or Rest Pose's) stored pose for a part, ignoring its live transform -- the opposite of GetCurrentDeltaPosition/GetBaseAnchor, which read the live transform.</summary>
		/// <remarks>Lets drag tools keep tracking the underlying animation's base pose (which can keep changing frame to frame during a drag) instead of a value frozen at drag-start.</remarks>
		internal static void GetStoredPose(DraggablePart part, out Vector2 baseAnchor,
			out float rotation, out float shear, out float scaleX, out float scaleY)
		{
			RestPose rest = GetOrCreateRestPose(part.PartName);
			if (activeClip != null
				&& activeClip.PoseFrames[activeFrameIndex].Poses.TryGetValue(part.PartName, out PartPose pose)
				&& pose.Included && !pose.Approximate)
			{
				baseAnchor = rest.Position + pose.DeltaPosition;
				rotation = pose.RotationDegrees;
				shear = pose.ShearDegrees;
				scaleX = pose.ScaleX;
				scaleY = pose.ScaleY;
				return;
			}
			baseAnchor = rest.Position;
			rotation = rest.RotationDegrees;
			shear = rest.ShearDegrees;
			scaleX = rest.ScaleX;
			scaleY = rest.ScaleY;
		}

		/// <summary>GetStoredPose's baseAnchor plus the same pivot correction SetPartTransform applies -- the part's sprite center per stored data alone. Used by MoveTool, which writes straight to transform.position instead of going through SetPartTransform.</summary>
		internal static Vector3 GetStoredCenter(DraggablePart part)
		{
			GetStoredPose(part, out Vector2 baseAnchor, out float rotation, out float shear, out float scaleX, out float scaleY);
			RestPose rest = GetOrCreateRestPose(part.PartName);
			Vector2 centerOffset = ComputeCenterOffsetFromPivot(rest.PivotOffset, rotation, shear, scaleX, scaleY);
			Vector2 center = baseAnchor + centerOffset;
			return new Vector3(center.x, center.y, 0f);
		}


		/// <summary>Captures a deep-cloned snapshot of rest poses, clips, and static layers for undo/redo.</summary>
		internal static RigSnapshot CaptureSnapshotForHistory()
		{
			RigSnapshot snapshot = new RigSnapshot();
			foreach (KeyValuePair<string, RestPose> entry in restPoses)
			{
				snapshot.RestPoses[entry.Key] = RigSnapshotCloner.Clone(entry.Value);
			}
			foreach (AnimationClip clip in clips)
			{
				snapshot.Clips.Add(RigSnapshotCloner.Clone(clip));
			}
			foreach (DraggablePart part in loadedParts)
			{
				if (part != null)
				{
					snapshot.StaticLayers[part.PartName] = part.StaticLayer;
				}
			}
			snapshot.ActiveClipName = activeClip?.Name;
			snapshot.ActiveFrameIndex = activeFrameIndex;
			return snapshot;
		}

		/// <summary>Restores a previously captured snapshot (undo/redo), then rebuilds BakedFrames and reapplies the active pose.</summary>
		/// <remarks>BakedFrames isn't part of a snapshot (pure derived cache); ApplyContextPoseToParts rebakes it before reading, so no separate rebake call is needed here.</remarks>
		internal static void RestoreSnapshotForHistory(RigSnapshot snapshot)
		{
			restPoses.Clear();
			foreach (KeyValuePair<string, RestPose> entry in snapshot.RestPoses)
			{
				restPoses[entry.Key] = RigSnapshotCloner.Clone(entry.Value);
			}
			clips.Clear();
			foreach (AnimationClip clip in snapshot.Clips)
			{
				clips.Add(RigSnapshotCloner.Clone(clip));
			}
			foreach (DraggablePart part in loadedParts)
			{
				if (part != null && snapshot.StaticLayers.TryGetValue(part.PartName, out int layer))
				{
					part.StaticLayer = layer;
				}
			}

			activeClip = snapshot.ActiveClipName != null ? clips.Find(c => c.Name == snapshot.ActiveClipName) : null;
			activeFrameIndex = activeClip != null ? Mathf.Clamp(snapshot.ActiveFrameIndex, 0, activeClip.PoseFrames.Count - 1) : 0;
			activeBakedIndex = 0;
			isPlaying = false;
			BumpPoseContext();

			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus("Undo/Redo applied.");
		}

		/// <summary>Switches the Inspector/editing context to Rest Pose.</summary>
		/// <remarks>Sets inspectorTarget unconditionally, even if Rest Pose is already active, so re-clicking brings the Inspector back -- same as SelectPart.</remarks>
		internal static void SelectRestPose()
		{
			inspectorTarget = InspectorTarget.Animation;
			if (activeClip == null)
			{
				RefreshTimeline();
				return;
			}
			CommitCurrentPoseToActiveContext();
			activeClip = null;
			activeFrameIndex = 0;
			activeBakedIndex = 0;
			isPlaying = false;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
			SetStatus("Editing Rest Pose (default for new clips).");
		}

		/// <summary>Switches the Inspector/editing context to the given clip's first frame.</summary>
		/// <remarks>Sets inspectorTarget unconditionally, even when clip == activeClip already -- same as SelectRestPose/SelectPart.</remarks>
		internal static void SelectClip(AnimationClip clip)
		{
			inspectorTarget = InspectorTarget.Animation;
			if (clip != activeClip)
			{
				CommitCurrentPoseToActiveContext();
				activeClip = clip;
				activeFrameIndex = 0;
				activeBakedIndex = 0;
				isPlaying = false;
				BumpPoseContext();
				CancelViewportDragAfterContextSwitch();
				ApplyContextPoseToParts();
				SyncPreviewAnimIndexToActiveClip();
				RefreshPreviewFrame();
			}

			RefreshTimeline();
			if (clip != null)
			{
				SetStatus(string.Format("Editing clip '{0}', frame 1/{1}.", clip.Name, clip.PoseFrames.Count));
				Lab.SelectNodeById("clip:" + clip.Name);
			}
		}

		/// <summary>Switches the active frame within the current clip, always jumping to that keyframe's exact authored pose (BakedFrames[0]), never a mid-transition one.</summary>
		internal static void ScrubToFrame(int index)
		{
			if (activeClip == null)
			{
				return;
			}
			inspectorTarget = InspectorTarget.Frame;
			int next = Mathf.Clamp(index, 0, activeClip.PoseFrames.Count - 1);
			CommitCurrentPoseToActiveContext();
			if (next != activeFrameIndex)
			{
				BumpPoseContext();
				CancelViewportDragAfterContextSwitch();
			}
			activeFrameIndex = next;
			activeBakedIndex = 0;
			ApplyContextPoseToParts();
			RefreshPreviewFrame();
			RefreshTimeline();
		}

		/// <summary>Like ScrubToFrame, but jumps to a specific baked sub-frame within the target frame's BakedFrames instead of always resetting to the exact authored pose. The ghost row's per-sub-chip click target.</summary>
		/// <remarks>Preview-only (ApplyPoseFrameToParts, not ApplyContextPoseToParts) since this shows a specific baked/interpolated sub-frame, not the authored pose.</remarks>
		internal static void ScrubToBakedFrame(int frameIndex, int bakedIndex)
		{
			if (activeClip == null)
			{
				return;
			}
			inspectorTarget = InspectorTarget.Frame;
			int nextFrame = Mathf.Clamp(frameIndex, 0, activeClip.PoseFrames.Count - 1);
			CommitCurrentPoseToActiveContext();
			PoseFrame pending = activeClip.PoseFrames[nextFrame];
			RebakeAllClips();
			int nextBaked = Mathf.Clamp(bakedIndex, 0, Mathf.Max(0, pending.BakedFrames.Count - 1));
			if (nextFrame != activeFrameIndex || nextBaked != activeBakedIndex)
			{
				BumpPoseContext();
				CancelViewportDragAfterContextSwitch();
			}
			activeFrameIndex = nextFrame;
			activeBakedIndex = nextBaked;
			PoseFrame targetBaked = pending.BakedFrames.Count > 0 ? pending.BakedFrames[activeBakedIndex] : pending;
			ApplyPoseFrameToParts(targetBaked);
			RefreshPreviewFrame();
			RefreshTimeline();
		}

		/// <summary>Advances/retreats the active frame, wrapping (last -> first, first -> last) rather than clamping, matching how playback loops.</summary>
		internal static void ScrubToAdjacentFrame(int direction)
		{
			if (activeClip == null)
			{
				return;
			}
			int count = activeClip.PoseFrames.Count;
			int newIndex = ((activeFrameIndex + direction) % count + count) % count;
			ScrubToFrame(newIndex);
		}


		/// <summary>Creates a new single-frame clip whose frame 0 is a snapshot of the current Rest Pose.</summary>
		/// <remarks>Later Rest Pose edits do not move this clip: frame 0 stores rest+zero-delta at creation time, and rest-position moves compensate existing clip deltas. Empty-frame save stubs (EnsureRequiredClip) still fall back to live rest.</remarks>
		internal static void CreateNewClip(string name)
		{
			if (loadedParts.Count == 0)
			{
				SetStatus("Load a rig first.");
				return;
			}
			name = (name ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name))
			{
				SetStatus("Enter a clip name first.");
				return;
			}
			if (clips.Exists(c => c.Name == name))
			{
				SetStatus("A clip named '" + name + "' already exists.");
				return;
			}

			AnimatorHistory.CaptureBeforeChange("Created clip '" + name + "'");
			AnimationClip clip = new AnimationClip { Name = name };
			PoseFrame frame0 = new PoseFrame();
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null)
				{
					continue;
				}

				frame0.Poses[part.PartName] = DefaultPoseFor(part.PartName);
			}

			clip.PoseFrames.Add(frame0);
			clips.Add(clip);
			activeClip = clip;
			activeFrameIndex = 0;
			activeBakedIndex = 0;
			isPlaying = false;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus("Created clip '" + name + "' from Rest Pose.");
		}

		/// <summary>Deletes the active clip and switches back to Rest Pose.</summary>
		internal static void DeleteActiveClip()
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose).");
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Deleted clip '" + activeClip.Name + "'");
			string name = activeClip.Name;
			clips.Remove(activeClip);
			activeClip = null;
			activeFrameIndex = 0;
			activeBakedIndex = 0;
			isPlaying = false;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus("Deleted clip '" + name + "'. (Save will re-generate it as a plain rest pose if it was one of the three the game requires.)");
		}

		/// <summary>Inserts a new frame after the active one, duplicating its poses, attach points, duration, and easing as a starting point (small adjustments are the common case).</summary>
		/// <remarks>Events are deliberately not copied forward -- an event is a one-shot trigger (e.g. a footstep sound), and duplicating a frame that has one would silently make it fire twice. CopyActiveFrame / PasteFrameAsNew is the full-fidelity path that does keep events.</remarks>
		internal static void AddFrame()
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose).");
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Added frame — " + activeClip.Name);
			PoseFrame copy = RigSnapshotCloner.Clone(activeClip.PoseFrames[activeFrameIndex]);
			copy.Events.Clear();
			activeClip.PoseFrames.Insert(activeFrameIndex + 1, copy);
			AnimatorFeelRules.InsertRootMotionSample(activeClip.RootMotionPositions, activeFrameIndex + 1);
			activeFrameIndex++;
			activeBakedIndex = 0;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshTimeline();
		}

		/// <summary>Deep-clones the active frame, or Rest Pose as a zero-delta snapshot, onto the session clipboard.</summary>
		/// <remarks>Commits live transforms first so the clipboard matches what is on screen, not a stale stored pose. Rest Pose has no duration/events/attach points; those fields stay PoseFrame defaults so Override onto a clip is a pose-only replace plus default timing.</remarks>
		internal static void CopyActiveFrame()
		{
			CommitCurrentPoseToActiveContext();
			if (activeClip == null)
			{
				frameClipboard = SnapshotRestPoseAsFrame();
				SetStatus("Copied Rest Pose. Paste as New inserts it into a clip; Override replaces the current frame or Rest Pose.");
				RefreshTimeline();
				return;
			}

			frameClipboard = RigSnapshotCloner.Clone(activeClip.PoseFrames[activeFrameIndex]);
			SetStatus(string.Format("Copied frame {0} of '{1}'. Paste as New inserts it; Override replaces the current frame or Rest Pose.",
				activeFrameIndex + 1, activeClip.Name));
			RefreshTimeline();
		}

		/// <summary>Inserts a clone of the frame clipboard after the active frame of the current clip and selects it.</summary>
		/// <remarks>Works across clips -- copy from Walk, select Run, paste -- because the clipboard is session-wide, not tied to the source clip. Additive; does not modify the frame that was current.</remarks>
		internal static void PasteFrameAsNew()
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose).");
				return;
			}
			if (frameClipboard == null)
			{
				SetStatus("Copy a frame first.");
				return;
			}
			CommitCurrentPoseToActiveContext();
			AnimatorHistory.CaptureBeforeChange("Pasted frame — " + activeClip.Name);
			activeClip.PoseFrames.Insert(activeFrameIndex + 1, RigSnapshotCloner.Clone(frameClipboard));
			AnimatorFeelRules.InsertRootMotionSample(activeClip.RootMotionPositions, activeFrameIndex + 1);
			activeFrameIndex++;
			activeBakedIndex = 0;
			BumpPoseContext();
			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus(string.Format("Pasted as new frame {0}/{1} of '{2}'.",
				activeFrameIndex + 1, activeClip.PoseFrames.Count, activeClip.Name));
		}

		/// <summary>Replaces the active frame, or Rest Pose, with the frame clipboard.</summary>
		/// <remarks>On a clip, the whole PoseFrame is replaced (poses, attach points, duration, easing, events). On Rest Pose, only part transforms are applied and clip deltas are compensated so Walk/Attack keep their world positions. PivotOffset is not rewritten. Does not commit live edits first, since those would be discarded by the replacement anyway.</remarks>
		internal static void OverrideActiveFrame()
		{
			if (frameClipboard == null)
			{
				SetStatus("Copy a frame first.");
				return;
			}

			if (activeClip == null)
			{
				AnimatorHistory.CaptureBeforeChange("Overrode Rest Pose");
				ApplyClipboardPosesToRest();
				ApplyContextPoseToParts();
				RefreshTimeline();
				SetStatus("Overrode Rest Pose with the copied frame. Existing clips kept their world positions.");
				return;
			}

			AnimatorHistory.CaptureBeforeChange("Overrode frame " + (activeFrameIndex + 1) + " — " + activeClip.Name);
			activeClip.PoseFrames[activeFrameIndex] = RigSnapshotCloner.Clone(frameClipboard);
			activeBakedIndex = 0;
			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus(string.Format("Overrode frame {0} of '{1}' with the copied frame.",
				activeFrameIndex + 1, activeClip.Name));
		}

		/// <summary>Moves the active frame by delta slots on the timeline (negative = earlier, positive = later) and keeps it selected.</summary>
		/// <remarks>Clamps rather than wrapping -- moving past either end is a no-op with a status message, so mashing « / » cannot accidentally loop a frame from the start to the end. Commits live transforms first so the moved frame keeps in-progress edits.</remarks>
		internal static void MoveActiveFrame(int delta)
		{
			if (activeClip == null)
			{
				SetStatus("Select a frame to move (not Rest Pose).");
				return;
			}
			if (delta == 0)
			{
				return;
			}
			int toIndex = activeFrameIndex + delta;
			if (toIndex < 0 || toIndex >= activeClip.PoseFrames.Count)
			{
				SetStatus(delta < 0 ? "Already the first frame." : "Already the last frame.");
				return;
			}
			CommitCurrentPoseToActiveContext();
			AnimatorHistory.CaptureBeforeChange("Moved frame — " + activeClip.Name);
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			activeClip.PoseFrames.RemoveAt(activeFrameIndex);
			activeClip.PoseFrames.Insert(toIndex, frame);
			AnimatorFeelRules.MoveRootMotionSample(activeClip.RootMotionPositions, activeFrameIndex, toIndex);
			activeFrameIndex = toIndex;
			activeBakedIndex = 0;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshTimeline();
			SetStatus(string.Format("Moved frame to {0}/{1} of '{2}'.",
				activeFrameIndex + 1, activeClip.PoseFrames.Count, activeClip.Name));
		}

		/// <summary>Removes the active frame (a clip must keep at least one).</summary>
		internal static void DeleteActiveFrame()
		{
			if (activeClip == null)
			{
				return;
			}
			if (activeClip.PoseFrames.Count <= 1)
			{
				SetStatus("A clip needs at least one frame.");
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Deleted frame " + (activeFrameIndex + 1) + " — " + activeClip.Name);
			AnimatorFeelRules.RemoveRootMotionSample(activeClip.RootMotionPositions, activeFrameIndex);
			activeClip.PoseFrames.RemoveAt(activeFrameIndex);
			activeFrameIndex = Mathf.Clamp(activeFrameIndex, 0, activeClip.PoseFrames.Count - 1);
			activeBakedIndex = 0;
			BumpPoseContext();
			CancelViewportDragAfterContextSwitch();
			ApplyContextPoseToParts();
			RefreshTimeline();
		}

		/// <summary>Sets the active frame's duration (minimum 0.02s).</summary>
		internal static void SetActiveFrameDuration(float seconds)
		{
			if (activeClip == null)
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Duration changed — " + activeClip.Name + " " + (activeFrameIndex + 1));
			activeClip.PoseFrames[activeFrameIndex].Duration = Mathf.Max(0.02f, seconds);
			RefreshTimeline();
		}

		/// <summary>Cumulative root-motion X in pixels for the active frame, or empty when the clip has no root motion.</summary>
		internal static string GetActiveFrameRootMotionText()
		{
			if (activeClip == null || activeClip.RootMotionPositions.Count == 0)
			{
				return string.Empty;
			}

			int index = Mathf.Clamp(activeFrameIndex, 0, activeClip.RootMotionPositions.Count - 1);
			return activeClip.RootMotionPositions[index].ToString("0.###", CultureInfo.InvariantCulture);
		}

		/// <summary>Sets the active frame's cumulative root-motion X in pixels; blank clears the whole clip curve.</summary>
		internal static void SetActiveFrameRootMotion(string text)
		{
			if (activeClip == null)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(text))
			{
				if (activeClip.RootMotionPositions.Count == 0)
				{
					return;
				}

				AnimatorHistory.CaptureBeforeChange("Cleared root motion — " + activeClip.Name);
				activeClip.RootMotionPositions.Clear();
				RefreshTimeline();
				return;
			}

			if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float pixels))
			{
				SetStatus("Root motion X must be a number (pixels).");
				return;
			}

			AnimatorHistory.CaptureBeforeChange("Root motion — " + activeClip.Name + " " + (activeFrameIndex + 1));
			if (activeClip.RootMotionPositions.Count == 0)
			{
				activeClip.RootMotionPositions.Add(0f);
			}

			AnimatorFeelRules.EnsureRootMotionLength(activeClip.RootMotionPositions, activeClip.PoseFrames.Count);
			int index = Mathf.Clamp(activeFrameIndex, 0, activeClip.RootMotionPositions.Count - 1);
			activeClip.RootMotionPositions[index] = pixels;
			RefreshTimeline();
		}


		/// <summary>Starts or stops playback of the active clip (needs more than one frame).</summary>
		internal static void TogglePlayback()
		{
			if (activeClip == null || activeClip.PoseFrames.Count <= 1)
			{
				SetStatus("Select a clip with more than one frame to play it.");
				return;
			}
			if (!isPlaying)
			{
				CommitCurrentPoseToActiveContext();
			}
			isPlaying = !isPlaying;
			playTimer = 0f;
			RefreshTimeline();
		}

		/// <summary>Called at the start of every edit (via AnimatorHistory.CaptureBeforeChange) so playback doesn't fight an in-progress edit over the same part's live transform. No-op if already paused.</summary>
		/// <remarks>Skipped while Mass Edit is on -- Mass Edit's own ApplyContextPoseToParts guard already protects the edited part (and the whole activelyDraggingGroup on a multi-select drag), and the user wants the rest of the clip to keep animating while nudging the selection live.</remarks>
		internal static void PausePlayback()
		{
			if (isPlaying && !massEditEnabled)
			{
				isPlaying = false;
				RefreshTimeline();
			}
		}

		/// <summary>Advances playback through the active frame's baked sub-frame sequence (PoseFrame.BakedFrames) using each sub-frame's own Duration, making eased transitions visible during Play. Rolls over into the next frame's baked group once the current one is exhausted.</summary>
		/// <remarks>Applies each tick's pose via ApplyPoseFrameToParts (not ApplyContextPoseToParts), since a baked sub-frame should never be read back as if it were an edit; rebakes explicitly since this skips ApplyContextPoseToParts' own rebake step.</remarks>
		internal static void TickPlayback(float deltaTime)
		{
			if (!isPlaying || activeClip == null || activeClip.PoseFrames.Count == 0)
			{
				return;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			List<PoseFrame> baked = frame.BakedFrames;
			if (baked.Count == 0)
			{
				return;
			}
			playTimer += deltaTime;
			PoseFrame currentBaked = baked[Mathf.Clamp(activeBakedIndex, 0, baked.Count - 1)];
			if (playTimer >= currentBaked.Duration)
			{
				playTimer -= currentBaked.Duration;
				activeBakedIndex++;
				if (activeBakedIndex >= baked.Count)
				{
					activeBakedIndex = 0;
					activeFrameIndex = (activeFrameIndex + 1) % activeClip.PoseFrames.Count;
					BumpPoseContext();
				}
				RebakeAllClips();
				PoseFrame newFrame = activeClip.PoseFrames[activeFrameIndex];
				PoseFrame newBaked = newFrame.BakedFrames.Count > 0
					? newFrame.BakedFrames[Mathf.Clamp(activeBakedIndex, 0, newFrame.BakedFrames.Count - 1)]
					: newFrame;
				ApplyPoseFrameToParts(newBaked);
				RefreshPreviewFrame();
				RefreshTimeline();
			}
		}

		/// <summary>Refreshes every panel dependent on editor state (Animations, Timeline, Scene Tree, Inspector). Internal (not private) so EditorInputController can call it once a drag finishes.</summary>
		/// <remarks>
		/// Always rebakes first, since everything below reads BakedFrames. Timeline Refresh
		/// methods no-op until the Timeline bottom panel (or the legacy workstation) has built
		/// them. Scene Tree Refresh no-ops in the shell (Node Tree replaced it).
		/// </remarks>
		internal static void RefreshTimeline()
		{
			RebakeAllClips();
			AnimationsPanel.Refresh(clips, activeClip);
			AnimationTimelinePanel.Refresh(activeClip, activeFrameIndex, activeBakedIndex, isPlaying);
			PortraitFrameGuide.SetVisible(activeClip != null && (activeClip.Name == "Portrait" || activeClip.Name == "StandStatic"));
			SceneTreePanel.Refresh(loadedParts, selectedPart);
			InspectorPanel.Refresh();
			ToolbarPanel.RefreshMassEditToggle();
		}


		/// <summary>Suffix marking a synthetic name for a part drawn more than once in a frame (e.g. a mirrored arm sprite reused for both limbs), since every other editor structure is keyed by one instance per part name. Never legitimately part of a real asset name, so stripping it (BaseName) is unambiguous.</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) so RigLoadService.ParseDuplicateOccurrence (moved out, since LoadSavedRig was its only caller) can still parse the same marker BaseName/DuplicateName use here.</remarks>
		internal const string DuplicateMarker = " (copy ";

		/// <summary>Strips a DuplicateMarker suffix (if any), recovering the real asset name.</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) so RigSaveService can identify duplicate-instance parts the same way this class's own save/selection code does.</remarks>
		internal static string BaseName(string partName)
		{
			int index = partName.IndexOf(DuplicateMarker, StringComparison.Ordinal);
			return index >= 0 ? partName.Substring(0, index) : partName;
		}

		/// <summary>Builds a duplicate part's synthetic name, e.g. "Asst_Arm01 (copy 2)".</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) -- shared between this class's own OnLoadClicked (spawning duplicate-instance DraggableParts) and RigLoadService.LoadSavedRig (parsing them back out of a saved rig.json).</remarks>
		internal static string DuplicateName(string baseName, int occurrence)
		{
			return baseName + DuplicateMarker + occurrence + ")";
		}

		/// <summary>Writes the current rig out to rig.json plus its editor-only sidecars.</summary>
		/// <remarks>
		/// Validates what the user actually authored before EnsureRequiredClip fills in fallback clips. Parts are
		/// written in draw order (StaticLayer), kept constant across every clip/frame. "Stand" and "Portrait"/
		/// "StandStatic" are hard-required by the base game once a rig is assigned to a hero, so whichever the user
		/// hasn't authored is auto-generated from the rest pose. Combat sequence names are then backfilled from
		/// CombatSequenceNames.ForModel(CharacterProfile.Model) -- the names that prefab's own
		/// ExoSkeletonUnitAnimationController components actually FindAnimationIndex, which differ by Model
		/// (HumanArcher uses angled Attack0/45/90; ObeliskLvl4 uses un-angled SpecialAttack). Angled numeric
		/// clips (Attack0, SpecialAttack45, …) that ForModel does not ask for are dropped from the live clip
		/// list before writing, so a leftover HumanArcher stub set does not keep coming back after a Model
		/// switch. Frames missing
		/// Head/Chest/Base get those sockets from the rest-pose bounding box (AttachPointContainerExoSkeleton
		/// looks them up on the swapped custom asset, not the Model prefab). Attack/SpecialAttack/SpellCast clips
		/// missing AbilityAction/AbilityEnd get those events on the first/last authored frame -- AbilityMeleeActivity
		/// never fires the projectile without AbilityAction. Death clips missing AbilityEnd get it on the last
		/// authored frame -- DeathActivity.Update is empty, so without that event the encounter freezes. Every clip
		/// to write (including auto-generated ones,
		/// never part of `clips` and so never covered by RebakeAllClips) is explicitly rebaked immediately before
		/// ExpandClipForSave reads BakedFrames, so Save's correctness never depends on an earlier rebake having
		/// already happened. Duplicate part instances (a part drawn more than once per frame) don't get their own
		/// static "parts" entry -- only a per-frame matrix entry under their shared base name (BaseName, not
		/// part.PartName, so a duplicate writes under its base's real name). Easing is baked at expand time
		/// (ExpandClipForSave), not authored into clip.PoseFrames, so the editor's own frame list stays exactly
		/// what the user placed. Each frame's parts are written in that frame's own RenderOrderIndex draw order,
		/// not always the rig's static StaticLayer order, since real per-frame z-variation can legitimately differ
		/// frame to frame. An Approximate pose skips ComputeFrameMatrix and writes its original raw matrix back
		/// out unchanged, since its DeltaPosition/RotationDegrees/Scale are only ever a display fallback. Attach
		/// points reuse ComputeFrameMatrix with a zero rest/pivot, mirroring LoadSavedRig's decode side. The
		/// animation-source sidecar is saved from the in-memory `clips` list (not `clipsToWrite`), since the
		/// auto-generated fallback clips don't need an entry of their own. Preview does a full rebuild afterward,
		/// since that's the only way it can reflect the just-changed file on disk.
		/// </remarks>
		internal static void OnSaveClicked(string folder)
		{
			TrySaveRig(folder);
		}

		/// <summary>Writes rig.json and sidecars for the given character folder. Returns false when nothing is loaded or the write throws.</summary>
		internal static bool TrySaveRig(string folder)
		{
			CurrentFolder = folder;
			if (loadedParts.Count == 0)
			{
				SetStatus("Nothing loaded — click Load first.");
				return false;
			}

			CommitCurrentPoseToActiveContext();

			AnimatorValidatorRegistry.RegisterDefaults();
			List<string> validationWarnings = AnimatorValidatorRegistry.RunAll(clips);

			List<DraggablePart> orderedParts = new List<DraggablePart>(loadedParts);
			orderedParts.RemoveAll(p => p == null);
			orderedParts.Sort((a, b) => a.StaticLayer.CompareTo(b.StaticLayer));

			string combatModel = CombatModelForFolder(folder);
			int droppedAngled = DropUnusedAngledAttackClips(clips, combatModel);
			if (droppedAngled > 0 && activeClip != null && !clips.Contains(activeClip))
			{
				activeClip = null;
				activeFrameIndex = 0;
				activeBakedIndex = 0;
				isPlaying = false;
			}
			if (droppedAngled > 0)
			{
				RefreshTimeline();
			}

			List<AnimationClip> clipsToWrite = new List<AnimationClip>(clips);
			EnsureRequiredClip(clipsToWrite, "Stand");
			if (!clipsToWrite.Exists(c => c.Name == "Portrait") && !clipsToWrite.Exists(c => c.Name == "StandStatic"))
			{
				EnsureRequiredClip(clipsToWrite, "StandStatic");
			}
			foreach (string combatName in CombatSequenceNames.ForModel(combatModel))
			{
				if (combatName == "Stand" || combatName == "StandStatic")
				{
					continue;
				}
				EnsureRequiredClip(clipsToWrite, combatName);
			}
			EnsureDefaultAttachPoints(clipsToWrite);
			EnsureCombatClipEvents(clipsToWrite);

			foreach (AnimationClip clipToBake in clipsToWrite)
			{
				RebakeClip(clipToBake);
			}

			StringBuilder parts = new StringBuilder();
			bool firstPartEntry = true;
			foreach (DraggablePart part in orderedParts)
			{
				if (BaseName(part.PartName) != part.PartName)
				{
					continue;
				}
				RestPose rest = GetOrCreateRestPose(part.PartName);
				float offsetX = rest.Position.x * PixelsToUnits;
				float offsetY = -1f * rest.Position.y * PixelsToUnits;

				if (!firstPartEntry)
				{
					parts.Append(",");
				}
				firstPartEntry = false;
				parts.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(part.PartName)).Append("\",\"offsetX\":")
					.Append(F(offsetX)).Append(",\"offsetY\":").Append(F(offsetY)).Append("}");
			}

			StringBuilder animationsJson = new StringBuilder();
			for (int clipIndex = 0; clipIndex < clipsToWrite.Count; clipIndex++)
			{
				AnimationClip clip = clipsToWrite[clipIndex];
				if (clipIndex > 0)
				{
					animationsJson.Append(",");
				}
				animationsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(clip.Name)).Append("\",\"frames\":[");

				List<PoseFrame> framesToWrite = RigSaveService.ExpandClipForSave(clip);
				for (int frameIndex = 0; frameIndex < framesToWrite.Count; frameIndex++)
				{
					PoseFrame keyframe = framesToWrite[frameIndex];
					if (frameIndex > 0)
					{
						animationsJson.Append(",");
					}
					animationsJson.Append("{\"duration\":").Append(F(keyframe.Duration)).Append(",\"parts\":[");

					List<(DraggablePart part, PartPose pose)> orderedForFrame = new List<(DraggablePart, PartPose)>();
					foreach (DraggablePart part in orderedParts)
					{
						PartPose pose = keyframe.Poses.TryGetValue(part.PartName, out PartPose found)
							? found
							: DefaultPoseFor(part.PartName);
						if (pose.Included)
						{
							orderedForFrame.Add((part, pose));
						}
					}
					orderedForFrame.Sort((x, y) =>
					{
						int xOrder = x.pose.RenderOrderIndex >= 0 ? x.pose.RenderOrderIndex : x.part.StaticLayer;
						int yOrder = y.pose.RenderOrderIndex >= 0 ? y.pose.RenderOrderIndex : y.part.StaticLayer;
						return xOrder.CompareTo(yOrder);
					});

					bool firstFramePart = true;
					foreach ((DraggablePart part, PartPose pose) in orderedForFrame)
					{
						RestPose rest = GetOrCreateRestPose(part.PartName);

						(float a, float b, float c, float d, float tx, float ty) matrix = pose.Approximate
							? (pose.RawA, -pose.RawB, -pose.RawC, pose.RawD, pose.RawTranslateX * PixelsToUnits, -pose.RawTranslateY * PixelsToUnits)
							: ComputeFrameMatrix(rest.Position, rest.PivotOffset, pose.DeltaPosition, pose.RotationDegrees, pose.ShearDegrees, pose.ScaleX, pose.ScaleY);

						if (!firstFramePart)
						{
							animationsJson.Append(",");
						}
						firstFramePart = false;
						animationsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(BaseName(part.PartName))).Append("\",\"matrix\":[")
							.Append(F(matrix.a)).Append(",").Append(F(matrix.b)).Append(",").Append(F(matrix.c)).Append(",")
							.Append(F(matrix.d)).Append(",").Append(F(matrix.tx)).Append(",").Append(F(matrix.ty)).Append("]}");
					}

					animationsJson.Append("],\"events\":[");
					for (int e = 0; e < keyframe.Events.Count; e++)
					{
						if (e > 0)
						{
							animationsJson.Append(",");
						}
						animationsJson.Append("\"").Append(TextEscaping.JsonEscape(keyframe.Events[e])).Append("\"");
					}

					animationsJson.Append("],\"attachPoints\":[");
					bool firstAttachPoint = true;
					foreach (AttachPointPose attach in keyframe.AttachPoints.Values)
					{
						var attachMatrix = ComputeFrameMatrix(Vector2.zero, Vector2.zero, attach.Position, attach.RotationDegrees, attach.ShearDegrees, attach.ScaleX, attach.ScaleY);
						if (!firstAttachPoint)
						{
							animationsJson.Append(",");
						}
						firstAttachPoint = false;
						animationsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(attach.Name)).Append("\",\"matrix\":[")
							.Append(F(attachMatrix.a)).Append(",").Append(F(attachMatrix.b)).Append(",").Append(F(attachMatrix.c)).Append(",")
							.Append(F(attachMatrix.d)).Append(",").Append(F(attachMatrix.tx)).Append(",").Append(F(attachMatrix.ty))
							.Append("],\"index\":").Append(attach.Index).Append("}");
					}
					animationsJson.Append("]}");
				}
				animationsJson.Append("]}");
			}

			string json = "{\"partsPadding\":0,\"parts\":[" + parts + "],\"animations\":[" + animationsJson + "]"
				+ BuildRootMotionsJson(clipsToWrite) + "}";

			try
			{
				string rigFolder = Path.Combine(folder, "rig");
				Directory.CreateDirectory(rigFolder);
				RigSaveService.WriteAllTextAtomic(Path.Combine(rigFolder, "rig.json"), json);
				RigSaveService.SavePivotsSidecar(rigFolder, orderedParts);
				RigSaveService.SaveAnimationSourceSidecar(rigFolder, clips);
				RebuildPreview();
				string warningNote = validationWarnings.Count > 0
					? " WARNING: " + string.Join(" ", validationWarnings.ToArray())
					: string.Empty;
				SetStatus(string.Format("Saved rig.json to {0} ({1} parts, {2} clip(s)).{3}",
					folder, loadedParts.Count, clipsToWrite.Count, warningNote));
				LabSaveUx.ClearDirty();
				return true;
			}
			catch (Exception ex)
			{
				SetStatus("Failed to save: " + ex.Message);
				return false;
			}
		}

		/// <summary>CharacterProfile.Model for the open folder, or HumanArcher when the sidecar is missing or blank.</summary>
		private static string CombatModelForFolder(string folder)
		{
			string combatModel = "HumanArcher";
			CharacterProfile profile = CharacterProfileSidecar.Load(folder);
			if (profile != null && !string.IsNullOrEmpty(profile.Model))
			{
				combatModel = profile.Model;
			}
			return combatModel;
		}

		/// <summary>Removes HumanArcher-style Attack0/SpecialAttack45 clips that the given Model prefab does not look up. Returns how many clips were dropped.</summary>
		private static int DropUnusedAngledAttackClips(List<AnimationClip> list, string combatModel)
		{
			HashSet<string> needed = new HashSet<string>(CombatSequenceNames.ForModel(combatModel));
			return list.RemoveAll(c => CombatSequenceNames.IsAngledNumericVariant(c.Name) && !needed.Contains(c.Name));
		}

		/// <summary>Adds a one-frame rest-pose clip of the given name when the write list does not already have one.</summary>
		/// <remarks>Empty Poses means every part falls back to rest at expand time.</remarks>
		private static void EnsureRequiredClip(List<AnimationClip> clipsToWrite, string name)
		{
			if (clipsToWrite.Exists(c => c.Name == name))
			{
				return;
			}
			AnimationClip clip = new AnimationClip { Name = name };
			clip.PoseFrames.Add(new PoseFrame());
			clipsToWrite.Add(clip);
		}

		/// <summary>Adds Head/Chest/Base to any authored frame that has none of those sockets, using the rest-pose bounding box.</summary>
		/// <remarks>Does not overwrite a socket the user already placed. Must run before RebakeClip so baked sub-frames inherit the sockets.</remarks>
		private static void EnsureDefaultAttachPoints(List<AnimationClip> clipsToWrite)
		{
			ComputeDefaultAttachLayout(out Vector2 head, out Vector2 chest, out Vector2 basePos);
			Vector2[] positions = { head, chest, basePos };
			foreach (AnimationClip clip in clipsToWrite)
			{
				foreach (PoseFrame frame in clip.PoseFrames)
				{
					for (int i = 0; i < CombatPlaybackRequirements.AttachPointNames.Length; i++)
					{
						string name = CombatPlaybackRequirements.AttachPointNames[i];
						if (frame.AttachPoints.ContainsKey(name))
						{
							continue;
						}
						frame.AttachPoints[name] = new AttachPointPose
						{
							Name = name,
							Position = positions[i],
							ScaleX = 1f,
							ScaleY = 1f,
							Index = frame.AttachPoints.Count
						};
					}
				}
			}
		}

		/// <summary>Places default Head/Chest/Base from the rest-pose bounding box, or vanilla-ranger-like fallbacks when no parts are loaded.</summary>
		private static void ComputeDefaultAttachLayout(out Vector2 head, out Vector2 chest, out Vector2 basePos)
		{
			if (loadedParts.Count == 0)
			{
				basePos = new Vector2(0f, 0f);
				chest = new Vector2(0f, 0.3f);
				head = new Vector2(0f, 0.55f);
				return;
			}
			float minY = float.MaxValue;
			float maxY = float.MinValue;
			float sumX = 0f;
			int count = 0;
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null)
				{
					continue;
				}
				Vector2 position = GetOrCreateRestPose(part.PartName).Position;
				sumX += position.x;
				if (position.y < minY)
				{
					minY = position.y;
				}
				if (position.y > maxY)
				{
					maxY = position.y;
				}
				count++;
			}
			if (count == 0)
			{
				basePos = new Vector2(0f, 0f);
				chest = new Vector2(0f, 0.3f);
				head = new Vector2(0f, 0.55f);
				return;
			}
			float midX = sumX / count;
			basePos = new Vector2(midX, minY);
			chest = new Vector2(midX, Mathf.Lerp(minY, maxY, 0.45f));
			head = new Vector2(midX, Mathf.Lerp(minY, maxY, 0.9f));
		}

		/// <summary>Adds AbilityAction on the first authored frame of Attack/SpecialAttack/SpellCast clips, and AbilityEnd on the last authored frame of those clips plus Death.</summary>
		/// <remarks>Must run before RebakeClip so the events land on BakedFrames[0] of those authored frames. Does not overwrite events the user already authored. Death is AbilityEnd-only -- DeathActivity never looks at AbilityAction, but its Update is empty so a missing AbilityEnd freezes the encounter.</remarks>
		private static void EnsureCombatClipEvents(List<AnimationClip> clipsToWrite)
		{
			foreach (AnimationClip clip in clipsToWrite)
			{
				if (clip.PoseFrames.Count == 0)
				{
					continue;
				}
				bool needsAction = CombatPlaybackRequirements.NeedsCombatEvents(clip.Name);
				bool needsEnd = CombatPlaybackRequirements.NeedsAbilityEndEvent(clip.Name);
				if (!needsAction && !needsEnd)
				{
					continue;
				}
				bool hasAction = false;
				bool hasEnd = false;
				foreach (PoseFrame frame in clip.PoseFrames)
				{
					if (frame.Events.Contains(CombatPlaybackRequirements.AbilityActionEvent))
					{
						hasAction = true;
					}
					if (frame.Events.Contains(CombatPlaybackRequirements.AbilityEndEvent))
					{
						hasEnd = true;
					}
				}
				if (needsAction && !hasAction)
				{
					clip.PoseFrames[0].Events.Add(CombatPlaybackRequirements.AbilityActionEvent);
				}
				if (needsEnd && !hasEnd)
				{
					clip.PoseFrames[clip.PoseFrames.Count - 1].Events.Add(CombatPlaybackRequirements.AbilityEndEvent);
				}
			}
		}


		/// <summary>Advances the active frame's easing type to the next value in EasingType, wrapping around, recording undo history first.</summary>
		internal static void CycleActiveFrameEasing()
		{
			if (activeClip == null)
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Easing changed — " + activeClip.Name + " " + (activeFrameIndex + 1));
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			frame.Easing = (EasingType)(((int)frame.Easing + 1) % Enum.GetValues(typeof(EasingType)).Length);
			RefreshTimeline();
		}

		/// <summary>Parses text as the active frame's step-easing step count (clamped to 0-60) and records undo history, ignoring unparseable input.</summary>
		internal static void SetActiveFrameEasingSteps(string text)
		{
			if (activeClip == null)
			{
				return;
			}
			if (int.TryParse(text, out int steps))
			{
				AnimatorHistory.CaptureBeforeChange("Easing steps changed — " + activeClip.Name + " " + (activeFrameIndex + 1));
				activeClip.PoseFrames[activeFrameIndex].EasingSteps = Mathf.Clamp(steps, 0, 60);
				RefreshTimeline();
			}
		}

		/// <summary>The active frame's event tags, or an empty list when Rest Pose / nothing is selected.</summary>
		internal static List<string> GetActiveFrameEvents()
		{
			if (activeClip == null)
			{
				return new List<string>();
			}
			return new List<string>(activeClip.PoseFrames[activeFrameIndex].Events);
		}

		/// <summary>Adds an event tag to the active frame if it is not already present. Scoped to animation clips only -- Rest Pose has no JSON frame of its own.</summary>
		internal static void AddActiveFrameEvent(string eventName)
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose) to author events.");
				return;
			}
			eventName = (eventName ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(eventName))
			{
				return;
			}
			List<string> events = activeClip.PoseFrames[activeFrameIndex].Events;
			if (events.Contains(eventName))
			{
				SetStatus("Frame already has event '" + eventName + "'.");
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Event '" + eventName + "' added — " + activeClip.Name + " " + (activeFrameIndex + 1));
			events.Add(eventName);
			RefreshTimeline();
			SetStatus("Added event '" + eventName + "'.");
		}

		/// <summary>Removes an event tag from the active frame. A no-op if Rest Pose is selected or the tag is not present.</summary>
		internal static void RemoveActiveFrameEvent(string eventName)
		{
			if (activeClip == null)
			{
				return;
			}
			List<string> events = activeClip.PoseFrames[activeFrameIndex].Events;
			if (!events.Contains(eventName))
			{
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Event '" + eventName + "' removed — " + activeClip.Name + " " + (activeFrameIndex + 1));
			events.Remove(eventName);
			RefreshTimeline();
		}

		/// <summary>The active frame's attach points, sorted by schema index, or an empty list when Rest Pose / nothing is selected.</summary>
		internal static List<AttachPointPose> GetActiveFrameAttachPoints()
		{
			if (activeClip == null)
			{
				return new List<AttachPointPose>();
			}
			List<AttachPointPose> list = new List<AttachPointPose>(activeClip.PoseFrames[activeFrameIndex].AttachPoints.Values);
			list.Sort((a, b) => a.Index.CompareTo(b.Index));
			return list;
		}

		/// <summary>Adds a named socket to the active frame at a default position if it is not already present. Head/Chest/Base use the rest-pose bounding box; other names start at the origin.</summary>
		internal static void AddActiveFrameAttachPoint(string name)
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose) to author attach points.");
				return;
			}
			name = (name ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name))
			{
				return;
			}
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			if (frame.AttachPoints.ContainsKey(name))
			{
				SetStatus("Frame already has attach point '" + name + "'.");
				return;
			}
			Vector2 position = Vector2.zero;
			ComputeDefaultAttachLayout(out Vector2 head, out Vector2 chest, out Vector2 basePos);
			Vector2[] defaults = { head, chest, basePos };
			for (int i = 0; i < CombatPlaybackRequirements.AttachPointNames.Length; i++)
			{
				if (name == CombatPlaybackRequirements.AttachPointNames[i])
				{
					position = defaults[i];
					break;
				}
			}
			SetActiveFrameAttachPoint(name, position.x, position.y, 0f);
		}

		/// <summary>Adds a new attach point, or repositions an existing one of the same name -- one control covers both.</summary>
		internal static void SetActiveFrameAttachPoint(string name, float x, float y, float rotationDegrees)
		{
			if (activeClip == null)
			{
				SetStatus("Select a clip first (not Rest Pose) to author attach points.");
				return;
			}
			name = (name ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(name))
			{
				SetStatus("Enter an attach point name first.");
				return;
			}
			AnimatorHistory.CaptureBeforeChange("Attach point '" + name + "' set — " + activeClip.Name + " " + (activeFrameIndex + 1));
			PoseFrame frame = activeClip.PoseFrames[activeFrameIndex];
			if (!frame.AttachPoints.TryGetValue(name, out AttachPointPose attach))
			{
				attach = new AttachPointPose { Name = name, Index = frame.AttachPoints.Count };
				frame.AttachPoints[name] = attach;
			}
			attach.Position = new Vector2(x, y);
			attach.RotationDegrees = rotationDegrees;
			RefreshTimeline();
			SetStatus(string.Format("Attach point '{0}' set at ({1:0.##}, {2:0.##}).", name, x, y));
		}

		/// <summary>Removes an attach point from the active frame, if present.</summary>
		internal static void RemoveActiveFrameAttachPoint(string name)
		{
			if (activeClip == null)
			{
				return;
			}
			name = (name ?? string.Empty).Trim();
			Dictionary<string, AttachPointPose> attachPoints = activeClip.PoseFrames[activeFrameIndex].AttachPoints;
			if (attachPoints.ContainsKey(name))
			{
				AnimatorHistory.CaptureBeforeChange("Attach point '" + name + "' removed — " + activeClip.Name + " " + (activeFrameIndex + 1));
				attachPoints.Remove(name);
				RefreshTimeline();
				SetStatus("Removed attach point '" + name + "' from this frame.");
			}
		}

		/// <summary>Rebakes one frame's BakedFrames from its current Poses/Easing/EasingSteps and its successor's pose (the transition target).</summary>
		/// <remarks>Deliberately non-incremental: every call re-derives from scratch rather than tracking which edit invalidates which frame's bake (frame i's bake also depends on frame i+1's pose, so almost any edit can invalidate a neighbor). A rig's clips total at most a few hundred Lerp calls combined, so this isn't measurable against Unity's frame budget even called on every context switch/edit/playback tick. EasingSteps &lt;= 0 (regardless of Easing) is the real "no baking" case, producing a single cloned entry. Wraps to index 0 for the last frame, matching how ExoSkeletonAnimator always loops. `next` itself isn't appended here -- it's added as its own frame's plain/eased entry when the loop reaches it, or (wrap-around) is already PoseFrames[0]'s own BakedFrames[0].</remarks>
		private static void RebakeFrame(AnimationClip clip, int i)
		{
			PoseFrame current = clip.PoseFrames[i];
			current.BakedFrames.Clear();

			int count = clip.PoseFrames.Count;
			if (current.EasingSteps <= 0 || count < 2)
			{
				current.BakedFrames.Add(RigSnapshotCloner.Clone(current));
				return;
			}

			PoseFrame next = clip.PoseFrames[(i + 1) % count];
			int segments = current.EasingSteps + 1;
			float segmentDuration = current.Duration / segments;

			PoseFrame shortenedStart = RigSnapshotCloner.Clone(current);
			shortenedStart.Duration = segmentDuration;
			current.BakedFrames.Add(shortenedStart);

			for (int s = 1; s < segments; s++)
			{
				float t = EasingFunctions.Evaluate(current.Easing, (float)s / segments);
				current.BakedFrames.Add(InterpolateFrame(current, next, t, segmentDuration));
			}
		}

		/// <summary>Rebakes every frame of a single clip.</summary>
		private static void RebakeClip(AnimationClip clip)
		{
			for (int i = 0; i < clip.PoseFrames.Count; i++)
			{
				RebakeFrame(clip, i);
			}
		}

		/// <summary>Rebakes every clip in the rig.</summary>
		private static void RebakeAllClips()
		{
			foreach (AnimationClip clip in clips)
			{
				RebakeClip(clip);
			}
		}

		/// <summary>Interpolates between two authored frames' poses at t (0..1), producing one generated in-between baked frame.</summary>
		/// <remarks>ShearDegrees uses a plain Lerp, not LerpAngle, since it isn't a wrapping angle (DecodeFrameMatrix's Atan always keeps it within (-90,90)). Attach points use the same union-of-both-sides approach as Poses, except a socket present on only one side just holds that value rather than lerping toward a synthetic default -- there's no rest-pose equivalent for an attach point to fall back to. Events deliberately stay empty here (a one-shot trigger shouldn't fire once per generated sub-frame); they only carry over via RebakeFrame's "self" clone.</remarks>
		private static PoseFrame InterpolateFrame(PoseFrame a, PoseFrame b, float t, float duration)
		{
			PoseFrame result = new PoseFrame { Duration = duration };
			HashSet<string> partNames = new HashSet<string>(a.Poses.Keys);
			partNames.UnionWith(b.Poses.Keys);
			foreach (string name in partNames)
			{
				PartPose poseA = a.Poses.TryGetValue(name, out PartPose foundA) ? foundA : DefaultPoseFor(name);
				PartPose poseB = b.Poses.TryGetValue(name, out PartPose foundB) ? foundB : DefaultPoseFor(name);
				result.Poses[name] = new PartPose
				{
					DeltaPosition = Vector2.Lerp(poseA.DeltaPosition, poseB.DeltaPosition, t),
					RotationDegrees = Mathf.LerpAngle(poseA.RotationDegrees, poseB.RotationDegrees, t),
					ShearDegrees = Mathf.Lerp(poseA.ShearDegrees, poseB.ShearDegrees, t),
					ScaleX = Mathf.Lerp(poseA.ScaleX, poseB.ScaleX, t),
					ScaleY = Mathf.Lerp(poseA.ScaleY, poseB.ScaleY, t)
				};
			}
			HashSet<string> attachNames = new HashSet<string>(a.AttachPoints.Keys);
			attachNames.UnionWith(b.AttachPoints.Keys);
			foreach (string name in attachNames)
			{
				a.AttachPoints.TryGetValue(name, out AttachPointPose attachA);
				b.AttachPoints.TryGetValue(name, out AttachPointPose attachB);
				result.AttachPoints[name] = InterpolateAttachPoint(attachA, attachB, t);
			}

			return result;
		}

		private static AttachPointPose InterpolateAttachPoint(AttachPointPose a, AttachPointPose b, float t)
		{
			if (a == null)
			{
				return RigSnapshotCloner.Clone(b);
			}
			if (b == null)
			{
				return RigSnapshotCloner.Clone(a);
			}
			return new AttachPointPose
			{
				Name = a.Name,
				Position = Vector2.Lerp(a.Position, b.Position, t),
				RotationDegrees = Mathf.LerpAngle(a.RotationDegrees, b.RotationDegrees, t),
				ShearDegrees = Mathf.Lerp(a.ShearDegrees, b.ShearDegrees, t),
				ScaleX = Mathf.Lerp(a.ScaleX, b.ScaleX, t),
				ScaleY = Mathf.Lerp(a.ScaleY, b.ScaleY, t),
				Index = a.Index
			};
		}

		/// <summary>Builds a PoseFrame whose poses match current Rest Pose (zero delta, rest rotation/scale) for every loaded part.</summary>
		private static PoseFrame SnapshotRestPoseAsFrame()
		{
			PoseFrame frame = new PoseFrame();
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null)
				{
					continue;
				}

				frame.Poses[part.PartName] = DefaultPoseFor(part.PartName);
			}

			return frame;
		}

		/// <summary>Writes clipboard part transforms onto Rest Pose and compensates clip deltas so existing clips keep their world positions.</summary>
		/// <remarks>Skips excluded and approximate clipboard poses. Does not touch PivotOffset, events, attach points, or root motion — Rest Pose has none of those.</remarks>
		private static void ApplyClipboardPosesToRest()
		{
			foreach (DraggablePart part in loadedParts)
			{
				if (part == null)
				{
					continue;
				}

				if (!frameClipboard.Poses.TryGetValue(part.PartName, out PartPose pose) || !pose.Included || pose.Approximate)
				{
					continue;
				}

				RestPose rest = GetOrCreateRestPose(part.PartName);
				Vector2 oldPosition = rest.Position;
				rest.Position = oldPosition + pose.DeltaPosition;
				rest.RotationDegrees = pose.RotationDegrees;
				rest.ShearDegrees = pose.ShearDegrees;
				rest.ScaleX = pose.ScaleX;
				rest.ScaleY = pose.ScaleY;
				Vector2 restMove = rest.Position - oldPosition;
				if (restMove.sqrMagnitude > 0.0000001f)
				{
					CompensateClipDeltasForPart(part.PartName, restMove);
				}
			}
		}

		/// <summary>Rest-relative pose used when snapshotting Rest Pose into a new clip's frame 0 or including a part in a frame.</summary>
		private static PartPose DefaultPoseFor(string partName)
		{
			RestPose rest = GetOrCreateRestPose(partName);
			return new PartPose
			{
				DeltaPosition = Vector2.zero,
				RotationDegrees = rest.RotationDegrees,
				ShearDegrees = rest.ShearDegrees,
				ScaleX = rest.ScaleX,
				ScaleY = rest.ScaleY
			};
		}

		/// <summary>Top-level rig.json rootMotions array, or empty when no clip has authored samples.</summary>
		private static string BuildRootMotionsJson(List<AnimationClip> clipsToWrite)
		{
			StringBuilder json = new StringBuilder();
			bool any = false;
			for (int i = 0; i < clipsToWrite.Count; i++)
			{
				AnimationClip clip = clipsToWrite[i];
				if (clip.RootMotionPositions.Count == 0)
				{
					continue;
				}

				float[] durations = new float[clip.PoseFrames.Count];
				for (int f = 0; f < clip.PoseFrames.Count; f++)
				{
					durations[f] = clip.PoseFrames[f].Duration;
				}

				float[] perFrame;
				if (clip.RootMotionPositions.Count == clip.PoseFrames.Count)
				{
					perFrame = clip.RootMotionPositions.ToArray();
				}
				else
				{
					List<float> padded = new List<float>(clip.RootMotionPositions);
					if (padded.Count == 0)
					{
						continue;
					}

					AnimatorFeelRules.EnsureRootMotionLength(padded, clip.PoseFrames.Count);
					perFrame = padded.ToArray();
				}

				float[] dense = AnimatorFeelRules.ExpandRootMotionPositions(perFrame, durations);
				if (dense.Length < 2)
				{
					continue;
				}

				if (!any)
				{
					json.Append(",\"rootMotions\":[");
					any = true;
				}
				else
				{
					json.Append(",");
				}

				json.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(clip.Name)).Append("\",\"positions\":[");
				for (int p = 0; p < dense.Length; p++)
				{
					if (p > 0)
					{
						json.Append(",");
					}

					json.Append(F(dense[p]));
				}

				json.Append("]}");
			}

			if (any)
			{
				json.Append("]");
			}

			return json.ToString();
		}


		/// <summary>Full rebuild of the Preview rig from whatever's currently on disk. Called automatically after Load and a successful Save so Preview stays live; kept around as a manual "force a refresh" escape hatch.</summary>
		/// <remarks>Thin forward onto RigPreviewService.RebuildPreview() (extracted 2026-08-12, pre-redesign audit P2) -- kept here, under the same name, so MenuBarPanel's external "Refresh Preview" button and every internal call site in this file kept compiling unchanged.</remarks>
		internal static void RebuildPreview() => RigPreviewService.RebuildPreview();

		/// <summary>Re-resolves which animation index within the already-built previewAsset matches the newly-active clip, by name.</summary>
		/// <remarks>Thin forward onto RigPreviewService.SyncPreviewAnimIndexToActiveClip() -- see RebuildPreview's own remarks.</remarks>
		private static void SyncPreviewAnimIndexToActiveClip() => RigPreviewService.SyncPreviewAnimIndexToActiveClip();

		/// <summary>Keeps the Preview viewport showing the same frame as the editable one.</summary>
		/// <remarks>Thin forward onto RigPreviewService.RefreshPreviewFrame() -- see RebuildPreview's own remarks.</remarks>
		private static void RefreshPreviewFrame() => RigPreviewService.RefreshPreviewFrame();


		/// <summary>Builds the raw a,b,c,d,tx,ty matrix rig.json expects for one part's pose, from a rotation-shear-scale decomposition M = R(rotation) * Shear(shear) * diag(scaleX, scaleY).</summary>
		/// <remarks>
		/// The static "parts" section (offsetX/offsetY) is translation-only; a per-frame matrix is applied after
		/// that, directly to the already-translated vertices, and matrix rotation happens around the world origin,
		/// not the part's own position -- so a naive rotation-only matrix would make a part orbit (0,0) instead of
		/// spinning in place. Fix: solve for the translation that keeps the part's pivot point P mapped to the
		/// intended anchor (P + deltaPosition) under M, i.e. `M*(vertex-P)+anchor`, not `M*vertex`, which works out
		/// to translation = anchor - M*P. P defaults to the rest position (pivotOffset zero, this editor's original
		/// only-ever-possible behavior); a non-zero pivotOffset rotates/scales around a chosen point instead, with
		/// translateX/Y still keeping the un-rotated case stationary. Also converts through NewMatrix's
		/// sign-flip/pixelsToUnits convention. Shear(k) = [[1,k],[0,1]] spans every invertible 2x2 linear map
		/// exactly; shearDegrees==0 reproduces every matrix this editor produced before shear existed.
		/// </remarks>
		private static (float a, float b, float c, float d, float tx, float ty) ComputeFrameMatrix(
			Vector2 restPosition, Vector2 pivotOffset, Vector2 deltaPosition, float rotationDegrees, float shearDegrees, float scaleX, float scaleY)
		{
			AffineMatrixMath.ComposeLinear(rotationDegrees, shearDegrees, scaleX, scaleY, out float mA, out float mB, out float mC, out float mD);

			Vector2 pivot = restPosition + pivotOffset;
			Vector2 anchor = pivot + deltaPosition;
			float translateX = anchor.x - (mA * pivot.x + mC * pivot.y);
			float translateY = anchor.y - (mB * pivot.x + mD * pivot.y);

			return (mA, -mB, -mC, mD, translateX * PixelsToUnits, -translateY * PixelsToUnits);
		}

		/// <summary>Formats a float for JSON output, trimming trailing zeros.</summary>
		/// <remarks>internal (not private) since 2026-08-12 (pre-redesign audit P2) so RigSaveService's sidecar writers format numbers identically to rig.json's own writer, still in this class.</remarks>
		internal static string F(float value)
		{
			return value.ToString("0.######", CultureInfo.InvariantCulture);
		}
	}
}
