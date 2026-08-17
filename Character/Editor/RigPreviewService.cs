using System;
using System.IO;
using Ironhide.ExoSkeleton;
using LokrLab.Editor.Animation;
using LokrCharacterLoader.CustomRigs;
using UnityEngine;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Owns the Preview viewport's own rig instance -- building it fresh from whatever's on disk, and looping the active clip through ExoSkeletonAnimator.</summary>
	/// <remarks>
	/// Extracted from RigEditorScene (pre-redesign audit P2 "SRP / god classes" target split, one
	/// of the three RigLoadService/RigSaveService/RigPreviewService pieces -- the most
	/// self-contained of the three, tackled first). RigEditorScene keeps thin forwards under the
	/// same names (RebuildPreview/RefreshPreviewFrame/SyncPreviewAnimIndexToActiveClip) for every
	/// one of this file's own ~13 internal call sites plus MenuBarPanel's external
	/// "Refresh Preview" button, so nothing else needed to change.
	///
	/// Still reaches back into RigEditorScene for CurrentFolder, PreviewRoot, SetStatus, and
	/// ActiveClip (already-existing internal accessors). Preview loops the active clip through
	/// ExoSkeletonAnimator and does not follow the editable viewport's Play/Pause or scrub.
	/// </remarks>
	internal static class RigPreviewService
	{
		private static GameObject previewObject;
		private static ExoSkeletonData previewData;
		private static ExoSkeletonDataAsset previewAsset;
		private static ExoSkeletonAnimator previewAnimator;
		private static int previewAnimIndex;

		/// <summary>Full rebuild of the Preview rig from whatever's currently on disk. Called automatically after Load and a successful Save so Preview stays live; kept around as a manual "force a refresh" escape hatch.</summary>
		/// <remarks>The preview's ExoSkeletonAnimator loops the active clip. Editor Play/Pause and frame scrub stay on the editable viewport only.</remarks>
		internal static void RebuildPreview()
		{
			if (previewObject != null)
			{
				UnityEngine.Object.Destroy(previewObject);
				previewObject = null;
				previewData = null;
				previewAsset = null;
				previewAnimator = null;
			}

			string folder = RigEditorScene.CurrentFolder;
			if (!File.Exists(Path.Combine(folder, "rig", "rig.json")))
			{
				RigEditorScene.SetStatus("No rig\\rig.json in " + folder + " yet — click Save first.");
				return;
			}

			try
			{
				ExoSkeletonDataAsset asset = CustomRigLoader.BuildFromFolder("editor-preview", folder);

				GameObject go = new GameObject("EditorPreviewRig",
					typeof(ExoSkeletonData), typeof(MeshFilter), typeof(MeshRenderer),
					typeof(ExoSkeletonRenderer), typeof(ExoSkeletonAnimator));
				go.transform.SetParent(RigEditorScene.PreviewRoot, false);

				MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
				meshRenderer.material = new Material(Shader.Find("Sprites/Default"));

				ExoSkeletonData data = go.GetComponent<ExoSkeletonData>();
				data.UpdateAsset(asset);

				ExoSkeletonRenderer renderer = go.GetComponent<ExoSkeletonRenderer>();
				renderer.exoSkeletonData = data;

				ExoSkeletonAnimator animator = go.GetComponent<ExoSkeletonAnimator>();
				animator.data = data;
				animator.autoPlay = false;
				animator.enabled = true;

				CharacterLabLayers.ApplyToHierarchy(go);
				previewObject = go;
				previewData = data;
				previewAsset = asset;
				previewAnimator = animator;
				SyncPreviewAnimIndexToActiveClip();

				RigEditorScene.SetStatus(string.Format("Preview built from {0} ({1} animation(s) total). It loops the active clip.",
					folder, asset.animations.Length));
			}
			catch (Exception ex)
			{
				RigEditorScene.SetStatus("Preview failed: " + ex.Message);
				LokrCharacterLabPlugin.Log.LogError("RigPreviewService preview failed: " + ex);
			}
		}

		/// <summary>Re-resolves which animation index within the already-built previewAsset matches the newly-active clip, by name. Lightweight -- previewAsset itself doesn't change when switching clips within the same loaded rig.</summary>
		internal static void SyncPreviewAnimIndexToActiveClip()
		{
			AnimationClip activeClip = RigEditorScene.ActiveClip;
			if (previewAsset == null || activeClip == null)
			{
				PlayPreviewLooping();
				return;
			}
			for (int i = 0; i < previewAsset.animations.Length; i++)
			{
				if (previewAsset.animations[i].name == activeClip.Name)
				{
					previewAnimIndex = i;
					PlayPreviewLooping();
					return;
				}
			}

			PlayPreviewLooping();
		}

		/// <summary>Starts (or restarts) looping playback of the resolved preview animation.</summary>
		private static void PlayPreviewLooping()
		{
			if (previewAnimator == null || previewAsset == null || previewAsset.animations.Length == 0)
			{
				return;
			}

			int index = Mathf.Clamp(previewAnimIndex, 0, previewAsset.animations.Length - 1);
			previewAnimator.enabled = true;
			previewAnimator.Play(index, true, 0f);
		}

		/// <summary>Fallback pose write when the preview animator is not looping (e.g. Rest Pose before Play).</summary>
		/// <remarks>No-ops while the in-engine animator is playing so editor scrub/Play-Pause cannot freeze the preview.</remarks>
		internal static void RefreshPreviewFrame()
		{
			if (previewAnimator != null && previewAnimator.IsPlaying)
			{
				return;
			}

			if (previewObject == null || previewData == null || previewAsset == null)
			{
				return;
			}
			Ironhide.ExoSkeleton.Animation animation = previewAsset.animations[previewAnimIndex];
			if (animation.frames.Length == 0)
			{
				return;
			}
			AnimationClip activeClip = RigEditorScene.ActiveClip;
			int frameIndex = activeClip != null
				? ComputeFlatBakedIndex(activeClip, RigEditorScene.ActiveFrameIndex, RigEditorScene.ActiveBakedIndex)
				: RigEditorScene.ActiveFrameIndex;
			frameIndex = Mathf.Clamp(frameIndex, 0, animation.frames.Length - 1);
			AnimationFrame frame = animation.frames[frameIndex];
			previewData.SetPose(frame.renderOrder, frame.matrices, frame.attachPoints, frame.alphas);
		}

		/// <summary>Sums BakedFrames.Count for every authored frame before authoredIndex, plus bakedIndex -- the flat position within the fully-expanded/saved frame array that (authoredIndex, bakedIndex) corresponds to, matching how ExpandClipForSave flattens BakedFrames.</summary>
		private static int ComputeFlatBakedIndex(AnimationClip clip, int authoredIndex, int bakedIndex)
		{
			int flat = 0;
			for (int k = 0; k < authoredIndex && k < clip.PoseFrames.Count; k++)
			{
				flat += clip.PoseFrames[k].BakedFrames.Count;
			}
			return flat + bakedIndex;
		}
	}
}
