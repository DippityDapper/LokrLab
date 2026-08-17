using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Drives tool hotkeys, click-to-select, pan/zoom, and single/group dragging in the viewport.</summary>
	/// <remarks>
	/// Owns input plumbing only (button state, which tool is active, is the pointer over UI) and hands off to
	/// whichever IAnimatorTool is current for the actual per-tool drag math, except for scale-reference overlays
	/// which are moved/rotated directly (they are not DraggableParts and refuse Scale/Pivot). Dragging is driven
	/// purely by mouse button state against whatever's selected (not per-object OnMouseDown/OnMouseDrag), so it
	/// works anywhere in the viewport regardless of what's under the cursor -- unlike the old per-collider
	/// approach, which grabbed whichever overlapping part happened to be on top. Ctrl+C/V/Shift+V and
	/// [ / ] dispatch frame copy/paste/override/reorder (skipped while an InputField is focused so they
	/// cannot steal text-field copy). A plain MonoBehaviour (not living
	/// on the static RigEditorScene) since Update() needs a live component to be called on.
	/// </remarks>
	internal sealed class EditorInputController : MonoBehaviour
	{
		private bool isDragging;
		private bool isGroupDragging;
		private readonly List<DraggablePart> dragGroup = new List<DraggablePart>();
		/// <summary>True while a scale-reference overlay (not a part) is being moved or rotated.</summary>
		private bool isDraggingReference;
		/// <summary>Mouse world position at the start of a reference drag, so ContinueReferenceDrag can measure a delta from it.</summary>
		private Vector3 referenceDragStartMouseWorld;
		/// <summary>The overlay's world position frozen at drag start -- Move applies the mouse delta on top of this rather than accumulating per tick.</summary>
		private Vector3 referenceDragStartPosition;
		/// <summary>Mouse angle around the overlay at drag start, for Rotate.</summary>
		private float referenceDragStartAngle;
		/// <summary>The overlay's RotationDegrees frozen at drag start, for Rotate.</summary>
		private float referenceDragStartRotation;
		private bool isPanning;
		private Vector3 lastPanMousePosition;

		private bool isPanningPreview;
		private Vector3 lastPanMousePositionPreview;
		/// <summary>Preview's pan-bounds center, seeded once from the camera's starting position on first pan/zoom (Preview has no auto-fit of its own).</summary>
		private bool previewHomeInitialized;
		private Vector3 previewHomeCenter;
		/// <summary>The live instance, so clip/frame switches can cancel a drag without FindObjectOfType.</summary>
		private static EditorInputController instance;
		/// <summary><see cref="RigEditorScene.PoseContextGeneration"/> at drag start, so mouse-up can refuse a commit after a clip/frame switch.</summary>
		private int dragPoseGeneration = -1;

		/// <summary>Zoom range: small enough that zooming in still shows something, large enough to pull a whole rig into view.</summary>
		private const float MinOrthoSize = 0.2f;
		private const float MaxOrthoSize = 30f;

		/// <summary>How far the camera's center can pan from world origin, on each axis. Sized so its worst case combined with MaxOrthoSize's reach still lands short of previewRoot's 100-unit offset, keeping the two viewports from bleeding into each other via pan.</summary>
		private const float PanBoundsExtent = 30f;

		/// <summary>Records the live instance so clip/frame switches can cancel a drag.</summary>
		private void OnEnable()
		{
			instance = this;
		}

		/// <summary>Clears the live instance when this controller is disabled.</summary>
		private void OnDisable()
		{
			if (instance == this)
			{
				instance = null;
			}
		}

		/// <summary>Clears an in-progress viewport drag without committing the live transform.</summary>
		/// <remarks>
		/// EventSystem runs before Update, so releasing a Move/Rotate/Scale drag on a timeline
		/// chip or Node Tree clip selects the new context first. The switch site commits the old
		/// frame, then this drop lets ApplyContextPoseToParts write the new pose onto the part.
		/// TickPlayback must not call this — Mass Edit needs the drag skip while playback runs.
		/// See docs/issues/unresolved/animator-pose-leaks-across-frames.md.
		/// </remarks>
		internal static void CancelActiveDrag()
		{
			if (instance == null)
			{
				RigEditorScene.ActivelyDraggingPart = null;
				RigEditorScene.ClearActivelyDraggingGroup();
				return;
			}

			instance.isDragging = false;
			instance.isGroupDragging = false;
			instance.isDraggingReference = false;
			instance.dragGroup.Clear();
			RigEditorScene.ActivelyDraggingPart = null;
			RigEditorScene.ClearActivelyDraggingGroup();
		}

		/// <summary>Per-frame input handling: pan/zoom, hotkeys, undo/redo, frame copy/paste/override/reorder, and drag begin/continue/commit.</summary>
		/// <remarks>Commits a finished drag immediately (not at the next lazy commit point), since Mass Edit can leave playback running through a drag -- otherwise the next TickPlayback tick would clear ActivelyDraggingPart's protection and silently reapply the pre-drag pose. This is also what triggers Mass Edit's own propagation.</remarks>
		private void Update()
		{
			HandleViewportPan();
			HandleViewportZoom();
			HandlePreviewPan();
			HandlePreviewZoom();

			GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			bool typingInField = focused != null && focused.GetComponent<InputField>() != null;

			if (!typingInField)
			{
				if (Input.GetKeyDown(KeyCode.Q))
				{
					RigEditorScene.SetTool(RigEditorScene.SelectToolName);
				}
				else
				{
					foreach (IAnimatorTool tool in AnimatorToolRegistry.Tools)
					{
						if (Input.GetKeyDown(tool.Hotkey))
						{
							RigEditorScene.SetTool(tool.Name);
							break;
						}
					}
				}
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				RigEditorScene.DeselectAll();
			}

			bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
			bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			if (!typingInField && ctrlHeld)
			{
				if (Input.GetKeyDown(KeyCode.Z))
				{
					AnimatorHistory.Undo();
				}
				else if (Input.GetKeyDown(KeyCode.Y))
				{
					AnimatorHistory.Redo();
				}
				else if (Input.GetKeyDown(KeyCode.A))
				{
					RigEditorScene.SelectAllParts();
				}
				else if (Input.GetKeyDown(KeyCode.C))
				{
					RigEditorScene.CopyActiveFrame();
				}
				else if (Input.GetKeyDown(KeyCode.V))
				{
					if (shiftHeld)
					{
						RigEditorScene.OverrideActiveFrame();
					}
					else
					{
						RigEditorScene.PasteFrameAsNew();
					}
				}
			}

			if (!typingInField && !ctrlHeld)
			{
				if (Input.GetKeyDown(KeyCode.LeftBracket))
				{
					RigEditorScene.MoveActiveFrame(-1);
				}
				else if (Input.GetKeyDown(KeyCode.RightBracket))
				{
					RigEditorScene.MoveActiveFrame(1);
				}
			}

			if (Input.GetMouseButtonDown(0) && !IsPointerOverBlockingUI())
			{
				TryBeginDrag();
			}
			if (isDragging && Input.GetMouseButton(0))
			{
				ContinueDrag();
			}
			if (Input.GetMouseButtonUp(0))
			{
				if (isDragging)
				{
					if (!isDraggingReference)
					{
						bool sameContext = dragPoseGeneration == RigEditorScene.PoseContextGeneration;
						if (sameContext || RigEditorScene.MassEditEnabled)
						{
							RigEditorScene.CommitCurrentPoseToActiveContext();
							RigEditorScene.RefreshTimeline();
						}
					}
					else
					{
						InspectorPanel.Refresh();
					}
				}
				isDragging = false;
				isGroupDragging = false;
				isDraggingReference = false;
				dragGroup.Clear();
				RigEditorScene.ActivelyDraggingPart = null;
				RigEditorScene.ClearActivelyDraggingGroup();
			}
		}

		/// <summary>Maps a registered tool's display name to the past-tense verb AnimatorHistory descriptions use, matching the Inspector's own phrasing rather than surfacing the tool's UI label verbatim.</summary>
		private static string DragVerbForTool(string toolName)
		{
			switch (toolName)
			{
				case "Move": return "moved";
				case "Rotate": return "rotated";
				case "Scale": return "scaled";
				case "Scale XY": return "scaled";
				case "Pivot": return "pivot moved";
				default: return "edited";
			}
		}

		/// <summary>Starts a drag (or, in Select mode, a click-to-select) on mouse-down.</summary>
		/// <remarks>Select mode has no registered tool and never drags -- clicking picks whatever part or scale-reference is under the cursor instead, so the drag tools can click an already-selected part to start dragging it rather than reselecting out from under it. A selected scale-reference overlay is moved/rotated as one piece and refuses Scale/Pivot with a status message rather than a silent no-op. Group-drag activates for any tool opting into IAnimatorTool.SupportsGroupDrag when more than one part is multi-selected. Affine poses are read-only for every tool except Pivot (which edits RestPose.PivotOffset directly, never the pose itself), since there's no rotation/shear/scale combination that could represent an edit to a degenerate source matrix; reported via status, not a silent no-op.</remarks>
		private void TryBeginDrag()
		{
			if (RigEditorScene.CurrentToolName == RigEditorScene.SelectToolName)
			{
				TrySelectUnderCursor();
				isDragging = false;
				return;
			}

			if (RigEditorScene.SelectedReference != null)
			{
				TryBeginReferenceDrag();
				return;
			}

			DraggablePart part = RigEditorScene.SelectedPart;
			if (part == null)
			{
				isDragging = false;
				return;
			}

			IAnimatorTool tool = AnimatorToolRegistry.Find(RigEditorScene.CurrentToolName);
			if (tool == null)
			{
				isDragging = false;
				return;
			}

			if (part.IsAffinePose && !tool.AllowsAffinePose)
			{
				isDragging = false;
				RigEditorScene.SetStatus(string.Format(
					"'{0}' can't be {1}d on this frame — its pose here comes from a degenerate matrix this editor can't decompose, shown read-only. Switch to Pivot to adjust its pivot, use Inspector's \"Convert to Editable\" to author a replacement pose, or edit a different frame/clip.",
					part.PartName, RigEditorScene.CurrentToolName.ToLowerInvariant()));
				return;
			}

			isGroupDragging = tool.SupportsGroupDrag && RigEditorScene.MultiSelection.Count > 1;
			if (isGroupDragging)
			{
				dragGroup.Clear();
				foreach (DraggablePart member in RigEditorScene.MultiSelection)
				{
					if (member != null && !(member.IsAffinePose && !tool.AllowsAffinePose))
					{
						dragGroup.Add(member);
					}
				}
				if (dragGroup.Count == 0)
				{
					isGroupDragging = false;
					isDragging = false;
					return;
				}

				AnimatorHistory.CaptureBeforeChange(RigEditorScene.DescribeGroupContext(dragGroup.Count, DragVerbForTool(tool.Name)));
				isDragging = true;
				dragPoseGeneration = RigEditorScene.PoseContextGeneration;
				RigEditorScene.SetActivelyDraggingGroup(dragGroup);
				tool.BeginGroupDrag(dragGroup, GetMouseWorldPosition());
				return;
			}

			AnimatorHistory.CaptureBeforeChange(RigEditorScene.DescribeVerbContext(part, DragVerbForTool(tool.Name)));
			isDragging = true;
			dragPoseGeneration = RigEditorScene.PoseContextGeneration;
			RigEditorScene.ActivelyDraggingPart = part;
			tool.BeginDrag(part, GetMouseWorldPosition());
		}

		/// <summary>Continues an in-progress drag for the current frame.</summary>
		/// <remarks>Refreshes the pivot handle unconditionally afterward, so it keeps tracking the part's live transform during the drag rather than special-casing which tools need it.</remarks>
		private void ContinueDrag()
		{
			if (isDraggingReference)
			{
				ContinueReferenceDrag();
				return;
			}

			IAnimatorTool tool = AnimatorToolRegistry.Find(RigEditorScene.CurrentToolName);
			if (tool == null)
			{
				isDragging = false;
				return;
			}

			if (isGroupDragging)
			{
				tool.ContinueGroupDrag(dragGroup, GetMouseWorldPosition());
				RigEditorScene.RefreshPivotHandle();
				return;
			}

			DraggablePart part = RigEditorScene.SelectedPart;
			if (part == null || (part.IsAffinePose && !tool.AllowsAffinePose))
			{
				isDragging = false;
				return;
			}

			tool.ContinueDrag(part, GetMouseWorldPosition());
			RigEditorScene.RefreshPivotHandle();
		}

		/// <summary>Click-to-select via a raycast against part BoxColliders and scale-reference overlays, scoped to the Main Viewport (not UI or Preview) -- gives control over which part wins when several overlap, unlike per-part OnMouseDown.</summary>
		/// <remarks>Every part sits at the same world Z, so a plain "closest to camera" hit ties arbitrarily when parts overlap; picks by SortingOrder instead, matching what's actually visible on top. Hidden parts keep their collider enabled but are never pickable. A visible part under the cursor always wins over a scale-reference overlay, since the overlay is a comparison guide sitting behind the work.</remarks>
		private static void TrySelectUnderCursor()
		{
			if (IsPointerOverBlockingUI() || !IsPointerOverMainViewport())
			{
				return;
			}

			Camera camera = RigEditorScene.ActiveCamera;
			Ray ray = camera.ScreenPointToRay(Input.mousePosition);
			RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);
			if (hits.Length == 0)
			{
				return;
			}

			DraggablePart bestPart = null;
			int bestPartOrder = int.MinValue;
			ReferenceCharacter bestReference = null;
			foreach (RaycastHit hit in hits)
			{
				DraggablePart part = hit.collider.GetComponent<DraggablePart>();
				if (part != null && part.Visible && part.FrameVisible)
				{
					if (bestPart == null || part.SortingOrder > bestPartOrder)
					{
						bestPart = part;
						bestPartOrder = part.SortingOrder;
					}
					continue;
				}
				ReferenceCharacter reference = hit.collider.GetComponent<ReferenceCharacter>();
				if (reference != null && reference.Visible)
				{
					bestReference = reference;
				}
			}

			if (bestPart != null)
			{
				if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
				{
					RigEditorScene.ToggleMultiSelect(bestPart);
					return;
				}
				RigEditorScene.SelectPart(bestPart);
				return;
			}

			if (bestReference != null)
			{
				RigEditorScene.SelectReference(bestReference);
			}
		}

		/// <summary>Starts a move or rotate drag on the selected scale-reference overlay. Scale and Pivot are refused with a status message -- the overlay exists as a known-size comparison, so resizing it would defeat the point.</summary>
		private void TryBeginReferenceDrag()
		{
			ReferenceCharacter reference = RigEditorScene.SelectedReference;
			if (reference == null)
			{
				isDragging = false;
				return;
			}

			string toolName = RigEditorScene.CurrentToolName;
			if (toolName != "Move" && toolName != "Rotate")
			{
				isDragging = false;
				RigEditorScene.SetStatus("Reference characters can be moved and rotated, but not scaled. Switch to Move (W) or Rotate (E).");
				return;
			}

			isDragging = true;
			isDraggingReference = true;
			Vector3 mouseWorld = GetMouseWorldPosition();
			referenceDragStartMouseWorld = mouseWorld;
			referenceDragStartPosition = reference.transform.position;
			referenceDragStartRotation = reference.RotationDegrees;
			referenceDragStartAngle = AngleToMouse(reference.transform.position, mouseWorld);
		}

		/// <summary>Continues a live move/rotate of the selected scale-reference overlay. Does not commit into the rig -- overlays are editor-only and not part of undo history.</summary>
		private void ContinueReferenceDrag()
		{
			ReferenceCharacter reference = RigEditorScene.SelectedReference;
			if (reference == null)
			{
				isDragging = false;
				isDraggingReference = false;
				return;
			}

			Vector3 mouseWorld = GetMouseWorldPosition();
			if (RigEditorScene.CurrentToolName == "Move")
			{
				Vector3 delta = mouseWorld - referenceDragStartMouseWorld;
				reference.transform.position = referenceDragStartPosition + new Vector3(delta.x, delta.y, 0f);
			}
			else if (RigEditorScene.CurrentToolName == "Rotate")
			{
				float rotationOffset = AngleToMouse(referenceDragStartPosition, mouseWorld) - referenceDragStartAngle;
				reference.RotationDegrees = referenceDragStartRotation + rotationOffset;
			}
			InspectorPanel.Refresh();
		}

		/// <summary>Signed angle from a world-space pivot to the mouse, in degrees -- the same measure RotateTool uses for parts.</summary>
		private static float AngleToMouse(Vector3 fromPosition, Vector3 mouseWorldPosition)
		{
			Vector3 delta = mouseWorldPosition - fromPosition;
			return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
		}

		/// <summary>The mouse cursor's current position in world space, on the Main Viewport camera's own Z plane.</summary>
		internal static Vector3 GetMouseWorldPosition()
		{
			Camera camera = RigEditorScene.ActiveCamera;
			Vector3 world = ScreenToWorld(camera, ViewportCameraBinder.Main, Input.mousePosition);
			world.z = 0f;
			return world;
		}

		/// <summary>Middle-mouse-drag camera pan for the Main Viewport. Must start over the viewport (not UI), but keeps panning if the cursor leaves it mid-drag, same as the left-click drag tools.</summary>
		private void HandleViewportPan()
		{
			if (Input.GetMouseButtonDown(2) && !IsPointerOverBlockingUI() && IsPointerOverMainViewport())
			{
				isPanning = true;
				lastPanMousePosition = Input.mousePosition;
			}
			if (isPanning && Input.GetMouseButton(2))
			{
				Camera camera = RigEditorScene.ActiveCamera;
				Vector3 currentMousePosition = Input.mousePosition;
				Vector3 screenDelta = currentMousePosition - lastPanMousePosition;
				float viewportPixelHeight = PixelHeight(ViewportCameraBinder.Main, camera);
				if (viewportPixelHeight > 0f)
				{
					float worldUnitsPerPixel = (camera.orthographicSize * 2f) / viewportPixelHeight;
					camera.transform.position -= new Vector3(screenDelta.x, screenDelta.y, 0f) * worldUnitsPerPixel;
					ClampCameraPosition(camera);
				}
				lastPanMousePosition = currentMousePosition;
			}
			if (Input.GetMouseButtonUp(2))
			{
				isPanning = false;
			}
		}

		/// <summary>Scroll-wheel zoom centered on the cursor (the world point under the mouse stays under the mouse), active only over the Main Viewport so it can't fight the UI panels' own ScrollRects.</summary>
		private void HandleViewportZoom()
		{
			float scroll = Input.mouseScrollDelta.y;
			if (Mathf.Abs(scroll) < 0.01f || IsPointerOverBlockingUI() || !IsPointerOverMainViewport())
			{
				return;
			}

			Camera camera = RigEditorScene.ActiveCamera;
			Vector3 worldBefore = ScreenToWorld(camera, ViewportCameraBinder.Main, Input.mousePosition);

			camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * Mathf.Pow(0.9f, scroll), MinOrthoSize, MaxOrthoSize);

			Vector3 worldAfter = ScreenToWorld(camera, ViewportCameraBinder.Main, Input.mousePosition);
			camera.transform.position += worldBefore - worldAfter;
			ClampCameraPosition(camera);
		}

		/// <summary>Bounds the Main Viewport camera's center (PanBoundsExtent), applied after both pan and zoom (zoom-to-cursor shifts position too), so it can never be pushed toward previewRoot.</summary>
		private static void ClampCameraPosition(Camera camera)
		{
			Vector3 position = camera.transform.position;
			position.x = Mathf.Clamp(position.x, -PanBoundsExtent, PanBoundsExtent);
			position.y = Mathf.Clamp(position.y, -PanBoundsExtent, PanBoundsExtent);
			camera.transform.position = position;
		}

		/// <summary>Mirrors HandleViewportPan for the Preview camera, gated to IsPointerOverPreviewViewport instead.</summary>
		private void HandlePreviewPan()
		{
			if (Input.GetMouseButtonDown(2) && !IsPointerOverBlockingUI() && IsPointerOverPreviewViewport())
			{
				isPanningPreview = true;
				lastPanMousePositionPreview = Input.mousePosition;
			}
			if (isPanningPreview && Input.GetMouseButton(2))
			{
				Camera camera = RigEditorScene.PreviewCamera;
				if (camera != null)
				{
					Vector3 currentMousePosition = Input.mousePosition;
					Vector3 screenDelta = currentMousePosition - lastPanMousePositionPreview;
					float viewportPixelHeight = PixelHeight(ViewportCameraBinder.Preview, camera);
					if (viewportPixelHeight > 0f)
					{
						float worldUnitsPerPixel = (camera.orthographicSize * 2f) / viewportPixelHeight;
						camera.transform.position -= new Vector3(screenDelta.x, screenDelta.y, 0f) * worldUnitsPerPixel;
						ClampPreviewCamera(camera);
					}
					lastPanMousePositionPreview = currentMousePosition;
				}
			}
			if (Input.GetMouseButtonUp(2))
			{
				isPanningPreview = false;
			}
		}

		/// <summary>Mirrors HandleViewportZoom for the Preview camera, sharing the same MinOrthoSize/MaxOrthoSize range as the Main Viewport.</summary>
		private void HandlePreviewZoom()
		{
			float scroll = Input.mouseScrollDelta.y;
			if (Mathf.Abs(scroll) < 0.01f || IsPointerOverBlockingUI() || !IsPointerOverPreviewViewport())
			{
				return;
			}

			Camera camera = RigEditorScene.PreviewCamera;
			if (camera == null)
			{
				return;
			}

			Vector3 worldBefore = ScreenToWorld(camera, ViewportCameraBinder.Preview, Input.mousePosition);

			camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * Mathf.Pow(0.9f, scroll), MinOrthoSize, MaxOrthoSize);

			Vector3 worldAfter = ScreenToWorld(camera, ViewportCameraBinder.Preview, Input.mousePosition);
			camera.transform.position += worldBefore - worldAfter;
			ClampPreviewCamera(camera);
		}

		/// <summary>Bounds Preview's camera to the same size box as ClampCameraPosition, re-centered on Preview's own starting position (previewHomeCenter) instead of world origin.</summary>
		private void ClampPreviewCamera(Camera camera)
		{
			EnsurePreviewHomeInitialized(camera);
			Vector3 position = camera.transform.position;
			position.x = Mathf.Clamp(position.x, previewHomeCenter.x - PanBoundsExtent, previewHomeCenter.x + PanBoundsExtent);
			position.y = Mathf.Clamp(position.y, previewHomeCenter.y - PanBoundsExtent, previewHomeCenter.y + PanBoundsExtent);
			camera.transform.position = position;
		}

		/// <summary>Seeds previewHomeCenter from the camera's current position, once.</summary>
		private void EnsurePreviewHomeInitialized(Camera camera)
		{
			if (previewHomeInitialized)
			{
				return;
			}
			previewHomeCenter = camera.transform.position;
			previewHomeInitialized = true;
		}

		/// <summary>Whether the cursor is over the Preview viewport's camera rect.</summary>
		private static bool IsPointerOverPreviewViewport()
		{
			if (!AnimatorWorkspace.IsPreviewVisible)
			{
				return false;
			}

			if (ViewportCameraBinder.Preview != null)
			{
				return ViewportCameraBinder.Preview.ContainsScreenPoint(Input.mousePosition);
			}

			Camera camera = RigEditorScene.PreviewCamera;
			return camera != null && camera.pixelRect.Contains(Input.mousePosition);
		}

		/// <summary>Whether the cursor is over the Main Viewport's camera rect, excluding the preview overlay that sits on top of it.</summary>
		private static bool IsPointerOverMainViewport()
		{
			if (IsPointerOverPreviewViewport())
			{
				return false;
			}

			if (ViewportCameraBinder.Main != null)
			{
				return ViewportCameraBinder.Main.ContainsScreenPoint(Input.mousePosition);
			}

			Camera camera = RigEditorScene.ActiveCamera;
			return camera != null && camera.pixelRect.Contains(Input.mousePosition);
		}

		private static Vector3 ScreenToWorld(Camera camera, ViewportCameraBinder binder, Vector3 screen)
		{
			if (binder != null)
			{
				return binder.ScreenToWorld(screen);
			}

			if (camera == null)
			{
				return screen;
			}

			screen.z = -camera.transform.position.z;
			return camera.ScreenToWorldPoint(screen);
		}

		private static float PixelHeight(ViewportCameraBinder binder, Camera camera)
		{
			if (binder != null)
			{
				return binder.PixelHeight;
			}

			return camera != null ? camera.pixelRect.height : 0f;
		}

		/// <summary>True when UI should consume input instead of the viewport (viewport regions win over the dimmed backdrop).</summary>
		private static bool IsPointerOverBlockingUI()
		{
			if (IsPointerOverMainViewport() || IsPointerOverPreviewViewport())
			{
				return false;
			}

			return IsPointerOverUI();
		}

		/// <summary>Whether the cursor is over a UI element.</summary>
		private static bool IsPointerOverUI()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}
	}
}
