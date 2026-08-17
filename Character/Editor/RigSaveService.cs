using System.Collections.Generic;
using System.IO;
using System.Text;
using LokrLab.Editor.Animation;
using LokrModAPI.Serialization;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Owns the low-level "how does saved rig data actually get serialized and written to disk safely" mechanics -- atomic file writes and both editor-only sidecar files.</summary>
	/// <remarks>
	/// Extracted from RigEditorScene (pre-redesign audit P2 "SRP / god classes" target split, one
	/// of the three RigLoadService/RigSaveService/RigPreviewService pieces). Deliberately narrower
	/// than "everything OnSaveClicked touches": OnSaveClicked itself stays in RigEditorScene,
	/// since it builds rig.json's own content directly inline (not through any separately-callable
	/// method) while also orchestrating validators, dropping unused clip variants (with a real,
	/// occasionally-taken side effect of clearing the active clip/frame state), and backfilling
	/// required clips/attach points/combat events -- all genuinely coupled to RigEditorScene's own
	/// live editing state, not the file-writing mechanics. What moved here is exactly what was
	/// already a separate method with a self-contained parameter list and no hidden dependency on
	/// that live state: the temp-file-plus-rename write primitive (WriteAllTextAtomic, C-02), both
	/// sidecar writers (SavePivotsSidecar/SaveAnimationSourceSidecar), and the pure BakedFrames
	/// flatten (ExpandClipForSave). Still calls back into RigEditorScene.GetOrCreateRestPose/
	/// BaseName/F -- shared utility methods used throughout RigEditorScene's own editing code too,
	/// not save-specific, so they stayed there rather than moving with their callers.
	/// </remarks>
	internal static class RigSaveService
	{
		/// <summary>Writes content to path via a temp file plus rename, instead of truncating and rewriting path in place.</summary>
		/// <remarks>
		/// Fixes part of pre-redesign audit C-02 (non-atomic save): a crash, power loss, or full disk
		/// mid-write to path directly leaves a truncated, unparseable file with no way to tell it's
		/// corrupt short of a failed load next time this character opens. Writing the full content to
		/// a sibling temp file first (itself still non-atomic, but its failure mode is an ignorable
		/// stray .tmp file, not a corrupted rig.json path never even reads) and only then replacing
		/// path with a plain rename means path itself is either the complete old content or the
		/// complete new content at every point in time -- never a partial write -- since a same-volume
		/// File.Move is a metadata-only operation, not a byte-by-byte copy. Used by every hand-built
		/// JSON writer in this file (rig.json itself, written directly by RigEditorScene.OnSaveClicked,
		/// plus both sidecars below) for the same reason.
		///
		/// Does not make a whole OnSaveClicked call atomic across all three of its files (rig.json,
		/// rig.pivots.json, rig.animsource.json) -- a crash between writing one file and the next can
		/// still leave that trio inconsistent with each other. That would need pre-staging every
		/// file's content before touching disk at all and is a larger change than this fix's scope;
		/// what this closes is the more likely and more damaging failure mode, an interrupted write
		/// corrupting the one file being written at that moment.
		/// </remarks>
		internal static void WriteAllTextAtomic(string path, string content)
		{
			string tempPath = path + ".tmp";
			File.WriteAllText(tempPath, content);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(tempPath, path);
		}

		/// <summary>Writes the pivots sidecar, but only if at least one part has a custom pivot; deletes a stale sidecar if every pivot got reset back to (0,0), rather than leaving a misleading empty file.</summary>
		internal static void SavePivotsSidecar(string folder, List<DraggablePart> orderedParts)
		{
			StringBuilder pivots = new StringBuilder();
			bool first = true;
			bool any = false;
			foreach (DraggablePart part in orderedParts)
			{
				if (RigEditorScene.BaseName(part.PartName) != part.PartName)
				{
					continue;
				}
				RestPose rest = RigEditorScene.GetOrCreateRestPose(part.PartName);
				if (rest.PivotOffset == Vector2.zero)
				{
					continue;
				}
				any = true;
				if (!first)
				{
					pivots.Append(",");
				}
				first = false;
				pivots.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(part.PartName)).Append("\",\"x\":")
					.Append(RigEditorScene.F(rest.PivotOffset.x)).Append(",\"y\":").Append(RigEditorScene.F(rest.PivotOffset.y)).Append("}");
			}

			string path = Path.Combine(folder, "rig.pivots.json");
			if (any)
			{
				WriteAllTextAtomic(path, "{\"pivots\":[" + pivots + "]}");
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		/// <summary>Writes a sidecar with the exact editor-authored frame data (Duration/Easing/EasingSteps/Poses/Events/AttachPoints), since rig.json only stores the baked/expanded sub-frames and has no concept of "one authored frame eased over N steps."</summary>
		/// <remarks>Without this, every baked sub-frame would silently become its own new authored frame on next load, permanently losing the original easing setup. Only user-authored clips are written; the auto-generated Stand/StandStatic fallbacks round-trip losslessly through the legacy flat-parse path and don't need an entry.</remarks>
		internal static void SaveAnimationSourceSidecar(string folder, List<AnimationClip> authoredClips)
		{
			StringBuilder clipsJson = new StringBuilder();
			bool firstClip = true;
			bool any = false;
			foreach (AnimationClip clip in authoredClips)
			{
				any = true;
				if (!firstClip)
				{
					clipsJson.Append(",");
				}
				firstClip = false;
				clipsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(clip.Name)).Append("\",\"frames\":[");

				for (int i = 0; i < clip.PoseFrames.Count; i++)
				{
					if (i > 0)
					{
						clipsJson.Append(",");
					}
					PoseFrame frame = clip.PoseFrames[i];
					clipsJson.Append("{\"duration\":").Append(RigEditorScene.F(frame.Duration))
						.Append(",\"easing\":\"").Append(frame.Easing).Append("\"")
						.Append(",\"easingSteps\":").Append(frame.EasingSteps)
						.Append(",\"events\":[");
					for (int e = 0; e < frame.Events.Count; e++)
					{
						if (e > 0)
						{
							clipsJson.Append(",");
						}
						clipsJson.Append("\"").Append(TextEscaping.JsonEscape(frame.Events[e])).Append("\"");
					}
					clipsJson.Append("],\"poses\":[");
					bool firstPose = true;
					foreach (KeyValuePair<string, PartPose> entry in frame.Poses)
					{
						if (!firstPose)
						{
							clipsJson.Append(",");
						}
						firstPose = false;
						PartPose p = entry.Value;
						clipsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(entry.Key)).Append("\"")
							.Append(",\"dx\":").Append(RigEditorScene.F(p.DeltaPosition.x))
							.Append(",\"dy\":").Append(RigEditorScene.F(p.DeltaPosition.y))
							.Append(",\"rot\":").Append(RigEditorScene.F(p.RotationDegrees))
							.Append(",\"shear\":").Append(RigEditorScene.F(p.ShearDegrees))
							.Append(",\"scaleX\":").Append(RigEditorScene.F(p.ScaleX))
							.Append(",\"scaleY\":").Append(RigEditorScene.F(p.ScaleY))
							.Append(",\"included\":").Append(p.Included ? "true" : "false")
							.Append(",\"approximate\":").Append(p.Approximate ? "true" : "false")
							.Append(",\"rawA\":").Append(RigEditorScene.F(p.RawA))
							.Append(",\"rawB\":").Append(RigEditorScene.F(p.RawB))
							.Append(",\"rawC\":").Append(RigEditorScene.F(p.RawC))
							.Append(",\"rawD\":").Append(RigEditorScene.F(p.RawD))
							.Append(",\"rawTx\":").Append(RigEditorScene.F(p.RawTranslateX))
							.Append(",\"rawTy\":").Append(RigEditorScene.F(p.RawTranslateY))
							.Append(",\"renderOrder\":").Append(p.RenderOrderIndex)
							.Append("}");
					}
					clipsJson.Append("],\"attachPoints\":[");
					bool firstAttach = true;
					foreach (AttachPointPose a in frame.AttachPoints.Values)
					{
						if (!firstAttach)
						{
							clipsJson.Append(",");
						}
						firstAttach = false;
						clipsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(a.Name)).Append("\"")
							.Append(",\"x\":").Append(RigEditorScene.F(a.Position.x))
							.Append(",\"y\":").Append(RigEditorScene.F(a.Position.y))
							.Append(",\"rot\":").Append(RigEditorScene.F(a.RotationDegrees))
							.Append(",\"shear\":").Append(RigEditorScene.F(a.ShearDegrees))
							.Append(",\"scaleX\":").Append(RigEditorScene.F(a.ScaleX))
							.Append(",\"scaleY\":").Append(RigEditorScene.F(a.ScaleY))
							.Append(",\"index\":").Append(a.Index)
							.Append("}");
					}
					clipsJson.Append("]}");
				}
				clipsJson.Append("]");
				if (clip.RootMotionPositions.Count > 0)
				{
					clipsJson.Append(",\"rootMotion\":[");
					for (int r = 0; r < clip.RootMotionPositions.Count; r++)
					{
						if (r > 0)
						{
							clipsJson.Append(",");
						}

						clipsJson.Append(RigEditorScene.F(clip.RootMotionPositions[r]));
					}

					clipsJson.Append("]");
				}

				clipsJson.Append("}");
			}

			string sourcePath = Path.Combine(folder, "rig.animsource.json");
			if (any)
			{
				WriteAllTextAtomic(sourcePath, "{\"clips\":[" + clipsJson + "]}");
			}
			else if (File.Exists(sourcePath))
			{
				File.Delete(sourcePath);
			}
		}

		/// <summary>Flattens one clip's authored frames into the flat, baked sub-frame list rig.json actually stores -- a plain concatenation of every authored frame's already-fresh PoseFrame.BakedFrames.</summary>
		internal static List<PoseFrame> ExpandClipForSave(AnimationClip clip)
		{
			List<PoseFrame> expanded = new List<PoseFrame>();
			foreach (PoseFrame frame in clip.PoseFrames)
			{
				expanded.AddRange(frame.BakedFrames);
			}
			return expanded;
		}
	}
}
