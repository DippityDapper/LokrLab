using System;
using System.Collections.Generic;
using System.IO;
using LokrLab.Editor.Animation;
using SimpleJSON;
using UnityEngine;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Owns parsing a saved rig.json plus its two editor-only sidecars (pivots, animation source) back into in-memory rest poses, clips, and duplicate-occurrence data.</summary>
	/// <remarks>
	/// Extracted from RigEditorScene (pre-redesign audit P2 "SRP / god classes" target split, the
	/// last of the three RigLoadService/RigSaveService/RigPreviewService pieces, tackled last with
	/// the other two's lessons in hand). Unlike RigSaveService's boundary (drawn around what was
	/// already a separate, self-contained method), LoadSavedRig here was already effectively pure
	/// relative to RigEditorScene's own live state before this move: it takes folder plus several
	/// out/mutable-collection parameters and only ever writes into those, never into RigEditorScene's
	/// own fields directly -- OnLoadClicked (which stays in RigEditorScene, for the same reasons
	/// OnSaveClicked did: it spawns DraggablePart GameObjects under partsRoot and drives selection/
	/// preview/timeline refresh, all genuinely tied to the live editing session) passes its own
	/// restPoses/clips fields in as the out arguments. That made this the cleanest of the three
	/// extractions -- no RigEditorScene state needed exposing beyond DuplicateName/DuplicateMarker/
	/// PixelsToUnits, already-simple constants/utilities OnLoadClicked's own duplicate-instance
	/// spawning needs regardless of this move.
	/// </remarks>
	internal static class RigLoadService
	{
		/// <summary>Loads editor-only pivot data (RestPose.PivotOffset) from its own rig.pivots.json sidecar -- the game's own rig.json schema has no such field.</summary>
		private static Dictionary<string, Vector2> LoadPivotsSidecar(string folder)
		{
			Dictionary<string, Vector2> result = new Dictionary<string, Vector2>();
			string path = Path.Combine(folder, "rig.pivots.json");
			if (!File.Exists(path))
			{
				return result;
			}
			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(path));
				foreach (JSONNode node in root["pivots"].Children)
				{
					string name = node["name"].Value;
					if (!string.IsNullOrEmpty(name))
					{
						result[name] = new Vector2(node["x"].AsFloat, node["y"].AsFloat);
					}
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("RigLoadService: failed to parse rig.pivots.json at " + path + ": " + ex.Message);
			}
			return result;
		}

		/// <summary>Inverse of RigSaveService.SaveAnimationSourceSidecar, keyed by clip name. A clip absent from the result (externally-authored, or saved before this sidecar existed) tells LoadSavedRig to fall back to its legacy "one PoseFrame per rig.json frame" parsing for that clip only.</summary>
		private static Dictionary<string, List<PoseFrame>> LoadAnimationSourceSidecar(string folder, out Dictionary<string, List<float>> rootMotionByClip)
		{
			Dictionary<string, List<PoseFrame>> result = new Dictionary<string, List<PoseFrame>>();
			rootMotionByClip = new Dictionary<string, List<float>>();
			string path = Path.Combine(folder, "rig.animsource.json");
			if (!File.Exists(path))
			{
				return result;
			}
			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(path));
				foreach (JSONNode clipNode in root["clips"].Children)
				{
					string clipName = clipNode["name"].Value;
					if (string.IsNullOrEmpty(clipName))
					{
						continue;
					}
					List<PoseFrame> frames = new List<PoseFrame>();
					foreach (JSONNode frameNode in clipNode["frames"].Children)
					{
						PoseFrame frame = new PoseFrame
						{
							Duration = frameNode["duration"].AsFloat,
							EasingSteps = frameNode["easingSteps"].AsInt
						};
						if (!Enum.TryParse(frameNode["easing"].Value, out EasingType easing))
						{
							easing = EasingType.Linear;
						}
						frame.Easing = easing;

						foreach (JSONNode eventNode in frameNode["events"].Children)
						{
							if (!string.IsNullOrEmpty(eventNode.Value))
							{
								frame.Events.Add(eventNode.Value);
							}
						}

						foreach (JSONNode poseNode in frameNode["poses"].Children)
						{
							string name = poseNode["name"].Value;
							if (string.IsNullOrEmpty(name))
							{
								continue;
							}
							frame.Poses[name] = new PartPose
							{
								DeltaPosition = new Vector2(poseNode["dx"].AsFloat, poseNode["dy"].AsFloat),
								RotationDegrees = poseNode["rot"].AsFloat,
								ShearDegrees = poseNode["shear"].AsFloat,
								ScaleX = poseNode["scaleX"].AsFloat,
								ScaleY = poseNode["scaleY"].AsFloat,
								Included = poseNode["included"].AsBool,
								Approximate = poseNode["approximate"].AsBool,
								RawA = poseNode["rawA"].AsFloat,
								RawB = poseNode["rawB"].AsFloat,
								RawC = poseNode["rawC"].AsFloat,
								RawD = poseNode["rawD"].AsFloat,
								RawTranslateX = poseNode["rawTx"].AsFloat,
								RawTranslateY = poseNode["rawTy"].AsFloat,
								RenderOrderIndex = poseNode["renderOrder"].AsInt
							};
						}

						foreach (JSONNode attachNode in frameNode["attachPoints"].Children)
						{
							string attachName = attachNode["name"].Value;
							if (string.IsNullOrEmpty(attachName))
							{
								continue;
							}
							frame.AttachPoints[attachName] = new AttachPointPose
							{
								Name = attachName,
								Position = new Vector2(attachNode["x"].AsFloat, attachNode["y"].AsFloat),
								RotationDegrees = attachNode["rot"].AsFloat,
								ShearDegrees = attachNode["shear"].AsFloat,
								ScaleX = attachNode["scaleX"].AsFloat,
								ScaleY = attachNode["scaleY"].AsFloat,
								Index = attachNode["index"].AsInt
							};
						}

						frames.Add(frame);
					}
					if (frames.Count > 0)
					{
						result[clipName] = frames;
					}

					JSONArray rootMotionArray = clipNode["rootMotion"].AsArray;
					if (rootMotionArray != null)
					{
						List<float> samples = new List<float>();
						foreach (JSONNode sampleNode in rootMotionArray.Children)
						{
							samples.Add(sampleNode.AsFloat);
						}

						if (samples.Count > 0)
						{
							rootMotionByClip[clipName] = samples;
						}
					}
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("RigLoadService: failed to parse rig.animsource.json at " + path + ": " + ex.Message);
			}
			return result;
		}

		/// <summary>Parses a saved rig.json plus its editor-only sidecars (pivots, animation source) into rest poses, clips, part order, and duplicate-occurrence counts.</summary>
		/// <remarks>Loads pivots before decoding any frame matrix, since DecodeFrameMatrix needs to know the pivot a matrix was encoded against. Prefers the animation-source sidecar's original (pre-baking) frame list per clip when present, falling back to treating rig.json's own expanded frames as separately authored otherwise; duplicate-part occurrence keys are tracked from sidecar-sourced frames too, so a duplicate only referenced there still gets its DraggablePart spawned. A frame's parts-array position is recorded as its RenderOrderIndex, since CharacterImporter writes parts in exactly that draw order. Attach points reuse ComputeFrameMatrix/DecodeFrameMatrix with a zero rest/pivot, since they have no static rest offset in the schema. Once every frame is scanned, a part-slot a given frame's JSON never mentioned is marked explicitly excluded (deferred to the end since a duplicate's full occurrence count isn't known until then); rigs this tool itself saves never trigger this. Rest Pose's rotation/shear/scale defaults (position comes from offsetX/offsetY directly) are pulled from the "Stand" clip's first frame (or whichever clip loaded first), matching what Rest Pose always implied before clips existed.</remarks>
		internal static void LoadSavedRig(string folder, Dictionary<string, RestPose> outRestPoses, List<AnimationClip> outClips,
			List<string> outPartOrder, Dictionary<string, int> outMaxOccurrenceByBaseName, out int approximatePoseCount)
		{
			approximatePoseCount = 0;
			string rigJsonPath = Path.Combine(folder, "rig.json");
			if (!File.Exists(rigJsonPath))
			{
				return;
			}

			try
			{
				JSONNode root = JSON.Parse(File.ReadAllText(rigJsonPath));
				int approximateCount = 0;

				Dictionary<string, Vector2> restWorldByName = new Dictionary<string, Vector2>();
				foreach (JSONNode partNode in root["parts"].Children)
				{
					string name = partNode["name"].Value;
					if (string.IsNullOrEmpty(name))
					{
						continue;
					}
					Vector2 restWorld = new Vector2(
						partNode["offsetX"].AsFloat / RigEditorScene.PixelsToUnits,
						-1f * partNode["offsetY"].AsFloat / RigEditorScene.PixelsToUnits);
					restWorldByName[name] = restWorld;
					outRestPoses[name] = new RestPose { Position = restWorld };
					outPartOrder.Add(name);
					outMaxOccurrenceByBaseName[name] = 1;
				}

				Dictionary<string, Vector2> pivotByName = LoadPivotsSidecar(folder);
				foreach (KeyValuePair<string, Vector2> entry in pivotByName)
				{
					if (outRestPoses.TryGetValue(entry.Key, out RestPose restForPivot))
					{
						restForPivot.PivotOffset = entry.Value;
					}
				}

				Dictionary<string, List<PoseFrame>> animationSource = LoadAnimationSourceSidecar(folder, out Dictionary<string, List<float>> sidecarRootMotion);

				foreach (JSONNode animNode in root["animations"].Children)
				{
					string clipName = animNode["name"].Value;
					AnimationClip clip = new AnimationClip { Name = clipName };

					if (animationSource.TryGetValue(clipName, out List<PoseFrame> authoredFrames))
					{
						foreach (PoseFrame authoredFrame in authoredFrames)
						{
							foreach (KeyValuePair<string, PartPose> poseEntry in authoredFrame.Poses)
							{
								if (poseEntry.Value.Approximate)
								{
									approximateCount++;
								}
								string baseName = RigEditorScene.BaseName(poseEntry.Key);
								int occurrence = baseName == poseEntry.Key ? 1 : ParseDuplicateOccurrence(poseEntry.Key, baseName);
								if (outMaxOccurrenceByBaseName.TryGetValue(baseName, out int existingOccurrence)
									&& occurrence > existingOccurrence)
								{
									outMaxOccurrenceByBaseName[baseName] = occurrence;
								}
							}
							clip.PoseFrames.Add(authoredFrame);
						}
						if (clip.PoseFrames.Count == 0)
						{
							clip.PoseFrames.Add(new PoseFrame());
						}
						outClips.Add(clip);
						continue;
					}

					foreach (JSONNode frameNode in animNode["frames"].Children)
					{
						PoseFrame keyframe = new PoseFrame { Duration = frameNode["duration"].AsFloat };
						int renderIndex = 0;
						Dictionary<string, int> occurrenceSoFar = new Dictionary<string, int>();
						foreach (JSONNode partNode in frameNode["parts"].Children)
						{
							string baseName = partNode["name"].Value;
							JSONArray matrix = partNode["matrix"].AsArray;
							if (string.IsNullOrEmpty(baseName) || matrix == null || matrix.Count < 6
								|| !restWorldByName.TryGetValue(baseName, out Vector2 restWorld))
							{
								continue;
							}

							occurrenceSoFar.TryGetValue(baseName, out int occurrence);
							occurrence++;
							occurrenceSoFar[baseName] = occurrence;
							string key = occurrence == 1 ? baseName : RigEditorScene.DuplicateName(baseName, occurrence);
							if (occurrence > outMaxOccurrenceByBaseName[baseName])
							{
								outMaxOccurrenceByBaseName[baseName] = occurrence;
							}

							pivotByName.TryGetValue(baseName, out Vector2 pivotOffset);
							var decoded = DecodeFrameMatrix(
								restWorld, pivotOffset, matrix[0].AsFloat, matrix[1].AsFloat, matrix[2].AsFloat,
								matrix[3].AsFloat, matrix[4].AsFloat, matrix[5].AsFloat);
							if (decoded.approximate)
							{
								approximateCount++;
							}

							keyframe.Poses[key] = new PartPose
							{
								DeltaPosition = decoded.deltaPosition,
								RotationDegrees = decoded.rotationDegrees,
								ShearDegrees = decoded.shearDegrees,
								ScaleX = decoded.scaleX,
								ScaleY = decoded.scaleY,
								Approximate = decoded.approximate,
								RawA = decoded.rawA,
								RawB = decoded.rawB,
								RawC = decoded.rawC,
								RawD = decoded.rawD,
								RawTranslateX = decoded.rawTranslateX,
								RawTranslateY = decoded.rawTranslateY,
								RenderOrderIndex = renderIndex++
							};
						}

						foreach (JSONNode eventNode in frameNode["events"].Children)
						{
							if (!string.IsNullOrEmpty(eventNode.Value))
							{
								keyframe.Events.Add(eventNode.Value);
							}
						}

						foreach (JSONNode attachNode in frameNode["attachPoints"].Children)
						{
							string attachName = attachNode["name"].Value;
							JSONArray attachMatrix = attachNode["matrix"].AsArray;
							if (string.IsNullOrEmpty(attachName) || attachMatrix == null || attachMatrix.Count < 6)
							{
								continue;
							}
							var decodedAttach = DecodeFrameMatrix(
								Vector2.zero, Vector2.zero, attachMatrix[0].AsFloat, attachMatrix[1].AsFloat, attachMatrix[2].AsFloat,
								attachMatrix[3].AsFloat, attachMatrix[4].AsFloat, attachMatrix[5].AsFloat);
							keyframe.AttachPoints[attachName] = new AttachPointPose
							{
								Name = attachName,
								Position = decodedAttach.deltaPosition,
								RotationDegrees = decodedAttach.rotationDegrees,
								ShearDegrees = decodedAttach.shearDegrees,
								ScaleX = decodedAttach.scaleX,
								ScaleY = decodedAttach.scaleY,
								Index = attachNode["index"].AsInt
							};
						}

						clip.PoseFrames.Add(keyframe);
					}
					if (clip.PoseFrames.Count == 0)
					{
						clip.PoseFrames.Add(new PoseFrame());
					}
					outClips.Add(clip);
				}

				foreach (AnimationClip loadedClip in outClips)
				{
					if (sidecarRootMotion.TryGetValue(loadedClip.Name, out List<float> sidecarSamples))
					{
						loadedClip.RootMotionPositions.AddRange(sidecarSamples);
						AnimatorFeelRules.EnsureRootMotionLength(loadedClip.RootMotionPositions, loadedClip.PoseFrames.Count);
					}
				}

				ApplyRootMotionsFromRigJson(root, outClips);

				foreach (AnimationClip clip in outClips)
				{
					foreach (PoseFrame keyframe in clip.PoseFrames)
					{
						foreach (KeyValuePair<string, int> entry in outMaxOccurrenceByBaseName)
						{
							for (int occurrence = 1; occurrence <= entry.Value; occurrence++)
							{
								string key = occurrence == 1 ? entry.Key : RigEditorScene.DuplicateName(entry.Key, occurrence);
								if (!keyframe.Poses.ContainsKey(key))
								{
									keyframe.Poses[key] = new PartPose { Included = false };
								}
							}
						}
					}
				}

				AnimationClip defaultsSource = outClips.Find(c => c.Name == "Stand");
				if (defaultsSource == null && outClips.Count > 0)
				{
					defaultsSource = outClips[0];
				}
				if (defaultsSource != null && defaultsSource.PoseFrames.Count > 0)
				{
					foreach (KeyValuePair<string, PartPose> entry in defaultsSource.PoseFrames[0].Poses)
					{
						if (entry.Value.Included && outRestPoses.TryGetValue(entry.Key, out RestPose rest))
						{
							rest.RotationDegrees = entry.Value.RotationDegrees;
							rest.ShearDegrees = entry.Value.ShearDegrees;
							rest.ScaleX = entry.Value.ScaleX;
							rest.ScaleY = entry.Value.ScaleY;
						}
					}
				}

				approximatePoseCount = approximateCount;
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("RigLoadService: failed to parse existing rig.json at " + rigJsonPath + ": " + ex.Message);
			}
		}

		/// <summary>Fills empty clip RootMotionPositions from rig.json's top-level rootMotions (vanilla / imported rigs without a sidecar curve).</summary>
		private static void ApplyRootMotionsFromRigJson(JSONNode root, List<AnimationClip> clips)
		{
			JSONArray rootMotions = root["rootMotions"].AsArray;
			if (rootMotions == null)
			{
				return;
			}

			foreach (JSONNode motionNode in rootMotions.Children)
			{
				string name = motionNode["name"].Value;
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}

				AnimationClip clip = clips.Find(c => c.Name == name);
				if (clip == null || clip.RootMotionPositions.Count > 0)
				{
					continue;
				}

				List<float> dense = new List<float>();
				JSONArray positions = motionNode["positions"].AsArray;
				if (positions == null)
				{
					continue;
				}

				foreach (JSONNode sampleNode in positions.Children)
				{
					dense.Add(sampleNode.AsFloat);
				}

				if (dense.Count == 0)
				{
					continue;
				}

				float[] durations = new float[clip.PoseFrames.Count];
				for (int i = 0; i < clip.PoseFrames.Count; i++)
				{
					durations[i] = clip.PoseFrames[i].Duration;
				}

				float[] sampled = AnimatorFeelRules.SampleRootMotionAtFrameStarts(dense.ToArray(), durations);
				clip.RootMotionPositions.AddRange(sampled);
			}
		}

		/// <summary>Inverse of DuplicateName -- recovers the occurrence number encoded in partName, given its BaseName.</summary>
		private static int ParseDuplicateOccurrence(string partName, string baseName)
		{
			int tailStart = baseName.Length + RigEditorScene.DuplicateMarker.Length;
			string tail = partName.Substring(tailStart, partName.Length - tailStart - 1);
			return int.TryParse(tail, out int occurrence) ? occurrence : 1;
		}

		/// <summary>Inverse of ComputeFrameMatrix via Gram-Schmidt on the matrix's two columns, recovering deltaPosition/rotation/shear/scale (and a raw-matrix fallback for the degenerate case).</summary>
		/// <remarks>
		/// scaleX/rotation come from column 1; column 2 splits into its component along column 1's direction (the
		/// shear) and its perpendicular component (scaleY) -- exact for any invertible 2x2 matrix, not just
		/// rotation+scale. `approximate` is true only for a genuinely degenerate matrix (near-zero scale on column
		/// 1, where rotation is undefined), not "any shear at all," so real shipped animation data that used to
		/// fall back to a lossy approximation now decodes exactly. pivotOffset must be the same value
		/// ComputeFrameMatrix (still in RigEditorScene, used by OnSaveClicked) used to produce this matrix --
		/// decoding with the wrong pivot only corrupts deltaPosition (rotation/shear/scale are pivot-independent).
		/// scaleY/shear are recovered via column 2's dot products with the orthonormal basis (cos,sin)/(-sin,cos)
		/// that column 1 defines.
		/// </remarks>
		private static (Vector2 deltaPosition, float rotationDegrees, float shearDegrees, float scaleX, float scaleY, bool approximate,
			float rawA, float rawB, float rawC, float rawD, float rawTranslateX, float rawTranslateY) DecodeFrameMatrix(
			Vector2 restPosition, Vector2 pivotOffset, float a, float b, float c, float d, float tx, float ty)
		{
			float mA = a, mB = -b, mC = -c, mD = d;
			float translateX = tx / RigEditorScene.PixelsToUnits;
			float translateY = -ty / RigEditorScene.PixelsToUnits;

			Vector2 pivot = restPosition + pivotOffset;
			float anchorX = translateX + (mA * pivot.x + mC * pivot.y);
			float anchorY = translateY + (mB * pivot.x + mD * pivot.y);
			Vector2 deltaPosition = new Vector2(anchorX, anchorY) - pivot;

			float scaleX = Mathf.Sqrt(mA * mA + mB * mB);
			bool approximate = scaleX <= 0.0001f;
			float safeScaleX = approximate ? 1f : scaleX;
			float rotationDegrees = approximate ? 0f : Mathf.Atan2(mB, mA) * Mathf.Rad2Deg;
			float cos = approximate ? 1f : mA / safeScaleX;
			float sin = approximate ? 0f : mB / safeScaleX;

			float scaleY = -mC * sin + mD * cos;
			float safeScaleY = Mathf.Abs(scaleY) > 0.0001f ? scaleY : 1f;
			float shear = approximate ? 0f : (mC * cos + mD * sin) / safeScaleY;
			float shearDegrees = Mathf.Atan(shear) * Mathf.Rad2Deg;

			return (deltaPosition, rotationDegrees, shearDegrees, safeScaleX, safeScaleY, approximate, mA, mB, mC, mD, translateX, translateY);
		}
	}
}
