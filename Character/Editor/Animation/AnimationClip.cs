using System.Collections.Generic;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor.Animation
{
	/// <summary>One part's pose within a single keyframe.</summary>
	/// <remarks>Position is a delta from that part's RestPose, since the schema's static "parts" offset is one fixed value shared by every animation; rotation/scale carry no such constraint and are stored as plain absolutes.</remarks>
	internal sealed class PartPose
	{
		/// <summary>This part's position in this frame, as an offset from its RestPose.Position.</summary>
		internal Vector2 DeltaPosition;
		/// <summary>This part's absolute rotation, in degrees, for this frame.</summary>
		internal float RotationDegrees;

		/// <summary>Independent per-axis scale (ScaleX == ScaleY reproduces every pose authored before non-uniform scale existed).</summary>
		internal float ScaleX = 1f;
		/// <summary>Y-axis counterpart to ScaleX.</summary>
		internal float ScaleY = 1f;

		/// <summary>Shear angle, in degrees. Together with RotationDegrees/ScaleX/ScaleY, a full rotation-shear-scale decomposition of the matrix's 2x2 linear part. 0 reproduces every pose authored before shear existed.</summary>
		internal float ShearDegrees;

		/// <summary>False means this part is absent from this frame's source data entirely (e.g. an accessory only present in some animations). Defaults true; only RigEditorScene.LoadSavedRig ever sets it false.</summary>
		internal bool Included = true;

		/// <summary>True only when this pose's matrix was too degenerate for DecodeFrameMatrix to recover a rotation at all. When true, RawA..RawTranslateY hold the exact source matrix so the part can still be displayed and re-saved losslessly.</summary>
		internal bool Approximate;
		/// <summary>The exact source 2x3 matrix, only meaningful when Approximate is true.</summary>
		internal float RawA, RawB, RawC, RawD, RawTranslateX, RawTranslateY;

		/// <summary>This frame's actual draw-order position for this part; -1 means "no per-frame order recorded," falling back to the part's persistent DraggablePart.StaticLayer.</summary>
		internal int RenderOrderIndex = -1;
	}

	/// <summary>A named socket at a specific point in a specific frame (e.g. where a held weapon or VFX should spawn from).</summary>
	/// <remarks>Unlike a part, the schema has no static rest offset for attach points -- each frame independently declares its own full list, so Position/RotationDegrees/Scale here are plain absolutes, not deltas.</remarks>
	internal sealed class AttachPointPose
	{
		/// <summary>This attach point's name, matching the schema's AttachPointDef.name.</summary>
		internal string Name;
		/// <summary>This attach point's absolute position for this frame.</summary>
		internal Vector2 Position;
		/// <summary>This attach point's absolute rotation, in degrees, for this frame.</summary>
		internal float RotationDegrees;
		/// <summary>Independent per-axis scale; see PartPose.ScaleX for the same convention.</summary>
		internal float ScaleX = 1f;
		/// <summary>Y-axis counterpart to ScaleX.</summary>
		internal float ScaleY = 1f;
		/// <summary>Shear angle, in degrees.</summary>
		internal float ShearDegrees;

		/// <summary>The schema's AttachPointDef.index, preserved from whatever was loaded so re-Saving imported data doesn't reorder it.</summary>
		internal int Index;
	}

	/// <summary>One authored keyframe of an AnimationClip: duration, per-part poses, events, attach points, and outgoing easing.</summary>
	internal sealed class PoseFrame
	{
		/// <summary>Seconds this frame holds before advancing to the next.</summary>
		internal float Duration = 0.15f;
		/// <summary>This frame's poses, keyed by part name.</summary>
		internal readonly Dictionary<string, PartPose> Poses = new Dictionary<string, PartPose>();

		/// <summary>Flat list of tag strings fired when this frame plays.</summary>
		internal readonly List<string> Events = new List<string>();
		/// <summary>Attach points present in this frame, keyed by name (absent means the socket doesn't exist in this frame).</summary>
		internal readonly Dictionary<string, AttachPointPose> AttachPoints = new Dictionary<string, AttachPointPose>();

		/// <summary>Describes the outgoing transition from this frame to the next in the clip. EasingSteps == 0 is the default, an ordinary hard cut.</summary>
		internal EasingType Easing = EasingType.Linear;
		/// <summary>Number of discrete sub-frames to bake for this transition; 0 is an ordinary hard cut.</summary>
		internal int EasingSteps;

		/// <summary>Pure derived cache of the baked-and-ready-to-play sub-frames for this frame's outgoing transition. Never part of undo/redo snapshots and never hand-edited; only ever replaced wholesale by a rebake.</summary>
		internal readonly List<PoseFrame> BakedFrames = new List<PoseFrame>();
	}

	/// <summary>PartPose/PoseFrame/AnimationClip/RestPose -- the in-memory animation data model.</summary>
	internal sealed class AnimationClip
	{
		/// <summary>This clip's name (e.g. "Stand", "Portrait", "StandStatic", or a custom name).</summary>
		internal string Name;
		/// <summary>This clip's frames, in playback order.</summary>
		internal readonly List<PoseFrame> PoseFrames = new List<PoseFrame>();
		/// <summary>Per-authored-frame cumulative root-motion X in pixels; empty means the clip has no rootMotions entry.</summary>
		/// <remarks>ReloadData turns a dense positions array into Animation.moveCurve. The Lab authors one sample per PoseFrame and expands to 30fps at Save. Independent of part poses — this moves the unit origin, not parts in place.</remarks>
		internal readonly List<float> RootMotionPositions = new List<float>();
	}

	/// <summary>A part's rest pose -- the static offsetX/offsetY plus the rotation/scale used when editing Rest Pose directly and for auto-generating the required Stand/Portrait/StandStatic clips.</summary>
	internal sealed class RestPose
	{
		/// <summary>This part's static offset from the schema's "parts" origin.</summary>
		internal Vector2 Position;
		/// <summary>This part's rest rotation, in degrees.</summary>
		internal float RotationDegrees;
		/// <summary>Independent per-axis scale; see PartPose.ScaleX for the same convention.</summary>
		internal float ScaleX = 1f;
		/// <summary>Y-axis counterpart to ScaleX.</summary>
		internal float ScaleY = 1f;
		/// <summary>Shear angle, in degrees.</summary>
		internal float ShearDegrees;

		/// <summary>The point (relative to Position) rotation/scale pivot around. The game's schema has no pivot concept -- ComputeFrameMatrix/DecodeFrameMatrix fold this into the exported matrix, so this is persisted separately (rig.pivots.json) purely for this editor's own round-tripping.</summary>
		internal Vector2 PivotOffset;
	}
}
