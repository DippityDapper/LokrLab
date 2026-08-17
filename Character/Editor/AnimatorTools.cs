using System.Collections.Generic;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Translates the selected part (or group) by the accumulated mouse movement since drag start.</summary>
	/// <remarks>
	/// One of the first-party IAnimatorTool implementations (see AnimatorToolRegistry). Applies the mouse delta on
	/// top of the currently active frame's stored center (RigEditorScene.GetStoredCenter), refetched every tick
	/// rather than computed once, so a Mass-Edit-driven playback tick underneath the drag keeps animating instead
	/// of freezing. Group dragging needs no special-casing: translating every part by the same world-space delta
	/// is already a rigid group translation, unlike rotate/scale which need a shared center.
	/// </remarks>
	internal sealed class MoveTool : IAnimatorTool
	{
		/// <summary>The tool's registry name, "Move".</summary>
		public string Name => "Move";
		/// <summary>Activates this tool via the W key.</summary>
		public KeyCode Hotkey => KeyCode.W;
		/// <summary>False; Move edits the pose's position, which an affine/shear pose has no clean position to edit.</summary>
		public bool AllowsAffinePose => false;
		/// <summary>True; translating every selected part by the same world-space delta is already a valid rigid group move.</summary>
		public bool SupportsGroupDrag => true;

		private Vector3 dragStartMouseWorld;

		/// <summary>Records the mouse's starting world position so ContinueDrag can measure a delta from it.</summary>
		public void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			dragStartMouseWorld = mouseWorldPosition;
		}

		/// <summary>Moves the part to its frame's stored center offset by the mouse delta accumulated since drag start.</summary>
		public void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			Vector3 mouseDelta = mouseWorldPosition - dragStartMouseWorld;
			part.transform.position = RigEditorScene.GetStoredCenter(part) + mouseDelta;
		}

		/// <summary>Records the mouse's starting world position so ContinueGroupDrag can measure a delta from it.</summary>
		public void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			dragStartMouseWorld = mouseWorldPosition;
		}

		/// <summary>Moves every part in the group to its own stored center offset by the same accumulated mouse delta.</summary>
		public void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			Vector3 mouseDelta = mouseWorldPosition - dragStartMouseWorld;
			for (int i = 0; i < group.Count; i++)
			{
				DraggablePart part = group[i];
				if (part != null)
				{
					part.transform.position = RigEditorScene.GetStoredCenter(part) + mouseDelta;
				}
			}
		}
	}

	/// <summary>Rotates the selected part (or group) around its pivot, dragged by mouse angle swept from drag start.</summary>
	/// <remarks>
	/// Rotates around the part's pivot (RestPose.PivotOffset), not its rendered center. The drag angle is measured
	/// from the pivot's world position frozen at drag-start, not from the live part.transform.position, which
	/// itself moves every tick once a custom pivot is in play.
	///
	/// Group rotation is genuinely different from several single-part rotations run side by side: the whole
	/// selection turns as one rigid formation around one shared center (the centroid of every selected part's
	/// pivot), so parts orbit that center as well as spinning in place -- simply looping the single-part methods
	/// per member (an earlier version did exactly that) would spin each part in place around its own pivot with no
	/// orbiting, visibly ignoring the other selected parts' positions.
	///
	/// The group math freezes only groupPivotWorld and groupDragStartMouseAngle, refetching each part's current
	/// pose fresh every tick via GetStoredPose so a live Mass Edit drag keeps animating underneath a group rotate.
	/// Each part's current pivot world position is derived from that same storage-backed pose (GetPivotOffset +
	/// baseAnchor), never from the part's live transform -- reading the live transform back inside this same tick
	/// loop, after a previous tick's SetPartTransform already wrote to it, would be a feedback loop that compounds
	/// every frame (a real, reported bug: rotation drifted and "freaked out," scaling drifted the group apart).
	/// GetPivotWorldPosition is still correct for the one-time BeginGroupDrag reads, which happen before this
	/// drag's own writes exist.
	/// </remarks>
	internal sealed class RotateTool : IAnimatorTool
	{
		/// <summary>The tool's registry name, "Rotate".</summary>
		public string Name => "Rotate";
		/// <summary>Activates this tool via the E key.</summary>
		public KeyCode Hotkey => KeyCode.E;
		/// <summary>False; Rotate edits the pose's rotation, which an affine/shear pose has no clean rotation value for.</summary>
		public bool AllowsAffinePose => false;
		/// <summary>True; the whole selection rotates as one rigid formation around its shared pivot centroid.</summary>
		public bool SupportsGroupDrag => true;

		private float dragStartMouseAngle;
		private Vector3 dragPivotWorld;

		/// <summary>Freezes the part's pivot world position and the mouse's starting angle around it, both used to measure the swept rotation.</summary>
		public void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			dragPivotWorld = (Vector3)RigEditorScene.GetPivotWorldPosition(part);
			dragStartMouseAngle = AngleToMouse(dragPivotWorld, mouseWorldPosition);
		}

		/// <summary>Applies the mouse's angle swept since drag start on top of the part's stored rotation, around its frozen pivot.</summary>
		public void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			float rotationOffset = AngleToMouse(dragPivotWorld, mouseWorldPosition) - dragStartMouseAngle;
			RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float storedRotation, out float shear, out float scaleX, out float scaleY);
			RigEditorScene.SetPartTransform(part, baseAnchor, storedRotation + rotationOffset, shear, scaleX, scaleY);
		}

		private Vector2 groupPivotWorld;
		private float groupDragStartMouseAngle;

		/// <summary>Freezes the group's shared pivot centroid and the mouse's starting angle around it, both used for the whole-group rotation.</summary>
		public void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			groupPivotWorld = RigEditorScene.GetGroupPivotWorld(group);
			groupDragStartMouseAngle = AngleToMouse(groupPivotWorld, mouseWorldPosition);
		}

		/// <summary>Spins every part's stored rotation by the same swept angle offset while orbiting its pivot around the frozen shared centroid, keeping the selection rigid.</summary>
		public void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			float rotationOffset = AngleToMouse(groupPivotWorld, mouseWorldPosition) - groupDragStartMouseAngle;
			for (int i = 0; i < group.Count; i++)
			{
				DraggablePart part = group[i];
				if (part == null)
				{
					continue;
				}
				RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float storedRotation, out float shear, out float scaleX, out float scaleY);
				Vector2 pivotOffset = RigEditorScene.GetPivotOffset(part);
				Vector2 currentPivotWorld = baseAnchor + pivotOffset;
				Vector2 newPivotWorld = groupPivotWorld + AnimatorGroupMath.Rotate(currentPivotWorld - groupPivotWorld, rotationOffset);
				Vector2 newBaseAnchor = newPivotWorld - pivotOffset;
				RigEditorScene.SetPartTransform(part, newBaseAnchor, storedRotation + rotationOffset, shear, scaleX, scaleY);
			}
		}

		private static float AngleToMouse(Vector2 fromPosition, Vector3 mouseWorldPosition)
		{
			Vector3 delta = mouseWorldPosition - (Vector3)fromPosition;
			return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
		}
	}

	/// <summary>Uniformly scales the selected part (or group) around its pivot, driven by mouse distance from the pivot.</summary>
	/// <remarks>
	/// Drives ScaleX/ScaleY together (the original Scale tool's exact behavior). Scales around the pivot for the
	/// same reason RotateTool does. Group scale mirrors RotateTool's group design: the whole selection shrinks/
	/// grows as one rigid formation around a shared center, so each part moves toward/away from that center by the
	/// same ratio as well as resizing itself, keeping relative spacing intact -- looping the single-part method per
	/// member instead would resize each part in place with no repositioning, visibly wrong for a formation.
	/// </remarks>
	internal sealed class ScaleTool : IAnimatorTool
	{
		/// <summary>The tool's registry name, "Scale".</summary>
		public string Name => "Scale";
		/// <summary>Activates this tool via the R key.</summary>
		public KeyCode Hotkey => KeyCode.R;
		/// <summary>False; Scale edits the pose's ScaleX/ScaleY pair, which an affine/shear pose has no clean value for.</summary>
		public bool AllowsAffinePose => false;
		/// <summary>True; the whole selection scales as one rigid formation around its shared pivot centroid.</summary>
		public bool SupportsGroupDrag => true;

		private float dragStartMouseDistance;
		private Vector3 dragPivotWorld;

		/// <summary>Freezes the part's pivot world position and the mouse's starting distance from it, both used to measure the scale ratio.</summary>
		public void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			dragPivotWorld = (Vector3)RigEditorScene.GetPivotWorldPosition(part);
			dragStartMouseDistance = Mathf.Max(0.05f, Vector3.Distance(dragPivotWorld, mouseWorldPosition));
		}

		/// <summary>Scales the part's stored ScaleX/ScaleY uniformly by the ratio of the mouse's current distance from the pivot to its distance at drag start.</summary>
		public void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			float distance = Mathf.Max(0.05f, Vector3.Distance(dragPivotWorld, mouseWorldPosition));
			float scaleRatio = distance / dragStartMouseDistance;
			RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float rotation, out float shear, out float storedScaleX, out float _);
			float scale = storedScaleX * scaleRatio;
			RigEditorScene.SetPartTransform(part, baseAnchor, rotation, shear, scale, scale);
		}

		private Vector2 groupPivotWorld;
		private float groupDragStartMouseDistance;

		/// <summary>Freezes the group's shared pivot centroid and the mouse's starting distance from it, both used for the whole-group scale ratio.</summary>
		public void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			groupPivotWorld = RigEditorScene.GetGroupPivotWorld(group);
			groupDragStartMouseDistance = Mathf.Max(0.05f, Vector2.Distance(groupPivotWorld, mouseWorldPosition));
		}

		/// <summary>Scales every part's stored ScaleX/ScaleY by the same ratio while moving its pivot toward/away from the frozen shared centroid by that ratio, keeping relative spacing intact.</summary>
		public void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			float distance = Mathf.Max(0.05f, Vector2.Distance(groupPivotWorld, mouseWorldPosition));
			float scaleRatio = distance / groupDragStartMouseDistance;
			for (int i = 0; i < group.Count; i++)
			{
				DraggablePart part = group[i];
				if (part == null)
				{
					continue;
				}
				RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float rotation, out float shear, out float storedScaleX, out float _);
				Vector2 pivotOffset = RigEditorScene.GetPivotOffset(part);
				Vector2 currentPivotWorld = baseAnchor + pivotOffset;
				Vector2 newPivotWorld = groupPivotWorld + (currentPivotWorld - groupPivotWorld) * scaleRatio;
				Vector2 newBaseAnchor = newPivotWorld - pivotOffset;
				float scale = storedScaleX * scaleRatio;
				RigEditorScene.SetPartTransform(part, newBaseAnchor, rotation, shear, scale, scale);
			}
		}
	}

	/// <summary>Independent X/Y scale around the pivot: horizontal mouse distance from the pivot drives ScaleX, vertical drives ScaleY.</summary>
	/// <remarks>
	/// v1 interaction, axis-aligned rather than along the part's own rotated axes -- simpler to implement correctly
	/// than true draggable corner/edge handles, while still delivering non-uniform scale authoring (not just
	/// read-only display, see DraggablePart.SetAffinePose). Corner-handle dragging remains a reasonable future
	/// refinement of this same tool. Group scale follows ScaleTool's group design exactly, just per axis.
	/// </remarks>
	internal sealed class ScaleXYTool : IAnimatorTool
	{
		/// <summary>The tool's registry name, "Scale XY".</summary>
		public string Name => "Scale XY";
		/// <summary>Activates this tool via the Y key.</summary>
		public KeyCode Hotkey => KeyCode.Y;
		/// <summary>False; Scale XY edits the pose's ScaleX/ScaleY pair independently, which an affine/shear pose has no clean value for.</summary>
		public bool AllowsAffinePose => false;
		/// <summary>True; the whole selection scales on each axis independently as one rigid formation around its shared pivot centroid.</summary>
		public bool SupportsGroupDrag => true;

		private float dragStartDistanceX;
		private float dragStartDistanceY;
		private Vector3 dragPivotWorld;

		/// <summary>Freezes the part's pivot world position and the mouse's starting horizontal/vertical distance from it, both used to measure the per-axis scale ratios.</summary>
		public void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			dragPivotWorld = (Vector3)RigEditorScene.GetPivotWorldPosition(part);
			dragStartDistanceX = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.x - dragPivotWorld.x));
			dragStartDistanceY = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.y - dragPivotWorld.y));
		}

		/// <summary>Scales the part's stored ScaleX and ScaleY independently, each by the ratio of the mouse's current horizontal/vertical distance from the pivot to its distance at drag start.</summary>
		public void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			float distanceX = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.x - dragPivotWorld.x));
			float distanceY = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.y - dragPivotWorld.y));
			float ratioX = distanceX / dragStartDistanceX;
			float ratioY = distanceY / dragStartDistanceY;
			RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float rotation, out float shear, out float storedScaleX, out float storedScaleY);
			RigEditorScene.SetPartTransform(part, baseAnchor, rotation, shear, storedScaleX * ratioX, storedScaleY * ratioY);
		}

		private Vector2 groupPivotWorld;
		private float groupDragStartDistanceX;
		private float groupDragStartDistanceY;

		/// <summary>Freezes the group's shared pivot centroid and the mouse's starting horizontal/vertical distance from it, both used for the whole-group per-axis scale ratios.</summary>
		public void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			groupPivotWorld = RigEditorScene.GetGroupPivotWorld(group);
			groupDragStartDistanceX = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.x - groupPivotWorld.x));
			groupDragStartDistanceY = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.y - groupPivotWorld.y));
		}

		/// <summary>Scales every part's stored ScaleX/ScaleY independently by the same per-axis ratios while moving its pivot toward/away from the frozen shared centroid on each axis by that ratio, keeping relative spacing intact.</summary>
		public void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			float distanceX = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.x - groupPivotWorld.x));
			float distanceY = Mathf.Max(0.05f, Mathf.Abs(mouseWorldPosition.y - groupPivotWorld.y));
			float ratioX = distanceX / groupDragStartDistanceX;
			float ratioY = distanceY / groupDragStartDistanceY;
			for (int i = 0; i < group.Count; i++)
			{
				DraggablePart part = group[i];
				if (part == null)
				{
					continue;
				}
				RigEditorScene.GetStoredPose(part, out Vector2 baseAnchor, out float rotation, out float shear, out float storedScaleX, out float storedScaleY);
				Vector2 pivotOffset = RigEditorScene.GetPivotOffset(part);
				Vector2 currentPivotWorld = baseAnchor + pivotOffset;
				Vector2 newPivotWorld = new Vector2(
					groupPivotWorld.x + (currentPivotWorld.x - groupPivotWorld.x) * ratioX,
					groupPivotWorld.y + (currentPivotWorld.y - groupPivotWorld.y) * ratioY);
				Vector2 newBaseAnchor = newPivotWorld - pivotOffset;
				RigEditorScene.SetPartTransform(part, newBaseAnchor, rotation, shear, storedScaleX * ratioX, storedScaleY * ratioY);
			}
		}
	}

	/// <summary>Edits a part's pivot (RestPose.PivotOffset), or the session temp group pivot when more than one part is selected.</summary>
	/// <remarks>AllowsAffinePose is true here and nowhere else, since a part's pivot is meaningful regardless of whether the viewed frame is an approximate/shear pose. Group drag moves TemporaryGroupPivot only — it does not rewrite each part's rest PivotOffset.</remarks>
	internal sealed class PivotTool : IAnimatorTool
	{
		/// <summary>The tool's registry name, "Pivot".</summary>
		public string Name => "Pivot";
		/// <summary>Activates this tool via the T key.</summary>
		public KeyCode Hotkey => KeyCode.T;
		/// <summary>True; a part's pivot is meaningful regardless of whether the viewed frame is an approximate/shear pose.</summary>
		public bool AllowsAffinePose => true;
		/// <summary>True; with a multi-selection this tool moves the session temp group pivot instead of each part's rest pivot.</summary>
		public bool SupportsGroupDrag => true;

		private Vector3 dragOffset;

		/// <summary>Frozen once at drag-start -- passing this fresh on every ContinueDrag call, re-derived from the very PivotOffset being overwritten, would drift.</summary>
		private Vector2 dragStartDeltaPosition;

		/// <summary>Freezes the part's delta-position and the mouse's offset from the pivot's current world position, both needed to keep the drag anchored under the cursor.</summary>
		public void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			dragStartDeltaPosition = RigEditorScene.GetDeltaPositionForPivotOps(part);
			dragOffset = (Vector3)RigEditorScene.GetPivotWorldPosition(part) - mouseWorldPosition;
		}

		/// <summary>Moves the part's pivot (RestPose.PivotOffset) to track the mouse, offset by the frozen drag-start cursor-to-pivot vector.</summary>
		public void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition)
		{
			RigEditorScene.SetPivotWorldPosition(part, dragStartDeltaPosition, mouseWorldPosition + dragOffset);
		}

		/// <summary>Freezes the mouse offset from the session temp group pivot.</summary>
		public void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			Vector2 pivot = RigEditorScene.GetGroupPivotWorld(group);
			dragOffset = (Vector3)pivot - mouseWorldPosition;
		}

		/// <summary>Moves the session temp group pivot with the mouse; does not write RestPose.PivotOffset.</summary>
		public void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition)
		{
			Vector3 world = mouseWorldPosition + dragOffset;
			RigEditorScene.SetTemporaryGroupPivotWorld(new Vector2(world.x, world.y));
		}
	}

	/// <summary>Shared helpers for the "rigid formation" math RotateTool/ScaleTool/ScaleXYTool's group-drag methods all use.</summary>
	internal static class AnimatorGroupMath
	{
		/// <summary>The shared center a group rotate/scale anchors to: the session temp pivot when set, otherwise the centroid of every selected part's own pivot world position.</summary>
		internal static Vector2 AveragePivotWorld(IReadOnlyList<DraggablePart> group)
		{
			Vector2 sum = Vector2.zero;
			int count = 0;
			for (int i = 0; i < group.Count; i++)
			{
				if (group[i] == null)
				{
					continue;
				}
				sum += RigEditorScene.GetPivotWorldPosition(group[i]);
				count++;
			}
			return count > 0 ? sum / count : Vector2.zero;
		}

		/// <summary>Rotates a 2D vector by the given degrees.</summary>
		internal static Vector2 Rotate(Vector2 v, float degrees)
		{
			float radians = degrees * Mathf.Deg2Rad;
			float cos = Mathf.Cos(radians);
			float sin = Mathf.Sin(radians);
			return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
		}
	}
}
