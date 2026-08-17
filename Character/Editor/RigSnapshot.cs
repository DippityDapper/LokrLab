using System.Collections.Generic;
using LokrLab.Editor.Animation;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Deep-cloned snapshot data and cloner backing the Undo/Redo history stack.</summary>
	/// <remarks>RestPoses/Clips are exactly the data Save/Load already round-trip through rig.json, plus each part's StaticLayer (persistent draw order, not itself stored on RestPose/PartPose) and which clip/frame was active, so undoing also puts the editor back in the same viewing context. Deliberately does not include DraggablePart.Visible (the eye-toggle) -- a pure display convenience never persisted to disk either, not really "animation data" to undo.</remarks>
	internal sealed class RigSnapshot
	{
		/// <summary>Snapshot of every part's rest pose, keyed by part name.</summary>
		internal readonly Dictionary<string, RestPose> RestPoses = new Dictionary<string, RestPose>();
		/// <summary>Snapshot of every animation clip.</summary>
		internal readonly List<AnimationClip> Clips = new List<AnimationClip>();
		/// <summary>Snapshot of each part's persistent draw order, keyed by part name.</summary>
		internal readonly Dictionary<string, int> StaticLayers = new Dictionary<string, int>();
		/// <summary>Name of the clip that was active when this snapshot was taken.</summary>
		internal string ActiveClipName;
		/// <summary>Index into ActiveClipName's PoseFrames that was active when this snapshot was taken.</summary>
		internal int ActiveFrameIndex;
	}

	/// <summary>Deep-clone helpers for every type RigSnapshot holds, kept separate from those types' own definitions (Editor/Animation/AnimationClip.cs) since cloning is purely an undo/redo concern.</summary>
	internal static class RigSnapshotCloner
	{
		/// <summary>Deep-clones a RestPose (all value-type fields) into a new instance, so edits to the live pose after this snapshot is taken don't retroactively change it.</summary>
		internal static RestPose Clone(RestPose source)
		{
			return new RestPose
			{
				Position = source.Position,
				RotationDegrees = source.RotationDegrees,
				ScaleX = source.ScaleX,
				ScaleY = source.ScaleY,
				ShearDegrees = source.ShearDegrees,
				PivotOffset = source.PivotOffset
			};
		}

		/// <summary>Deep-clones an AnimationClip's Name, PoseFrames, and root-motion samples.</summary>
		/// <remarks>Recursively clones each PoseFrame rather than copying the list reference, since PoseFrame is a mutable reference type and sharing frames would let future edits to the live clip mutate this snapshot. RootMotionPositions is copied by value for the same reason.</remarks>
		internal static AnimationClip Clone(AnimationClip source)
		{
			AnimationClip clip = new AnimationClip { Name = source.Name };
			foreach (PoseFrame frame in source.PoseFrames)
			{
				clip.PoseFrames.Add(Clone(frame));
			}
			clip.RootMotionPositions.AddRange(source.RootMotionPositions);
			return clip;
		}

		/// <summary>Deep-clones a PoseFrame's timing/easing fields, per-part Poses, event tags, and AttachPoints.</summary>
		/// <remarks>Poses and AttachPoints are cloned entry-by-entry since PartPose/AttachPointPose are mutable reference types; Events is safe to bulk-copy since strings are immutable. Deliberately omits BakedFrames -- per its own doc comment, that's a derived rebake cache, not undo-worthy data. CopyActiveFrame uses this same clone so the session clipboard is a full-fidelity authored frame, not a live alias of the source.</remarks>
		internal static PoseFrame Clone(PoseFrame source)
		{
			PoseFrame frame = new PoseFrame { Duration = source.Duration, Easing = source.Easing, EasingSteps = source.EasingSteps };
			foreach (KeyValuePair<string, PartPose> entry in source.Poses)
			{
				frame.Poses[entry.Key] = Clone(entry.Value);
			}
			frame.Events.AddRange(source.Events);
			foreach (KeyValuePair<string, AttachPointPose> entry in source.AttachPoints)
			{
				frame.AttachPoints[entry.Key] = Clone(entry.Value);
			}
			return frame;
		}

		/// <summary>Deep-clones a PartPose's transform, raw-matrix, and flag fields (all value types) into a new instance.</summary>
		internal static PartPose Clone(PartPose source)
		{
			return new PartPose
			{
				DeltaPosition = source.DeltaPosition,
				RotationDegrees = source.RotationDegrees,
				ScaleX = source.ScaleX,
				ScaleY = source.ScaleY,
				ShearDegrees = source.ShearDegrees,
				Included = source.Included,
				Approximate = source.Approximate,
				RawA = source.RawA,
				RawB = source.RawB,
				RawC = source.RawC,
				RawD = source.RawD,
				RawTranslateX = source.RawTranslateX,
				RawTranslateY = source.RawTranslateY,
				RenderOrderIndex = source.RenderOrderIndex
			};
		}

		/// <summary>Deep-clones an AttachPointPose's name, transform, and index fields into a new instance.</summary>
		internal static AttachPointPose Clone(AttachPointPose source)
		{
			return new AttachPointPose
			{
				Name = source.Name,
				Position = source.Position,
				RotationDegrees = source.RotationDegrees,
				ScaleX = source.ScaleX,
				ScaleY = source.ScaleY,
				ShearDegrees = source.ShearDegrees,
				Index = source.Index
			};
		}
	}
}
