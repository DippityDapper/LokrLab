using SimpleUI;
using UnityEngine;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor.Animation
{
	/// <summary>Frame strip for the active clip: one individually-clickable chip per frame, highlighted for the active frame, plus a "+" chip to add a new frame, Copy/Paste/Override/move controls, and a whole-clip Play/Pause transport control.</summary>
	/// <remarks>
	/// Duration/Easing/Events/Attach Points/per-frame part transforms live in InspectorPanel's Frame section
	/// instead, shown for whichever frame was clicked last. Frame clipboard and reorder actions are duplicated
	/// there and on the Edit menu; they live here too because shifting frames is a timeline operation, not a
	/// property of the inspected object. Copy and Override work on Rest Pose; Paste as New does not.
	///
	/// Each frame is its own small vertical group (chip on top, its baked sub-chips row directly beneath), laid
	/// out left-to-right in one scrollable horizontal UiStack. Nesting the ghost row inside its own parent's group
	/// keeps it aligned under that parent as a structural guarantee, rather than needing to be kept in sync via
	/// matching X-coordinate math across two independent flat rows (how this worked before migrating to SimpleUI).
	/// </remarks>
	internal static class AnimationTimelinePanel
	{
		private const float ChipSize = 30f;
		private const float BakedChipHeight = 16f;
		private const float BakedSubGap = 2f;

		private static readonly Color GhostInactiveColor = new Color(0.4f, 0.42f, 0.48f, 0.65f);

		private static UiStack chipRow;
		private static UiButton playPauseButton;
		private static UiButton copyFrameButton;
		private static UiButton pasteFrameButton;
		private static UiButton overrideFrameButton;
		private static UiButton moveFrameLeftButton;
		private static UiButton moveFrameRightButton;

		/// <summary>Builds the frame-chip strip, Copy/Paste/Override/move buttons, and Play/Pause.</summary>
		internal static void Build(Transform canvas, Font labelFont)
		{
			UiPanel panel = UiPanel.Create(canvas, UiTheme.Default, region: EditorLayout.FrameStripRegion);
			panel.Add(BuildBody(panel.ContentParent));
		}

		/// <summary>Builds the frame strip into a layout parent (shell Timeline bottom panel).</summary>
		internal static UiStack BuildInto(Transform parent)
		{
			return BuildBody(parent);
		}

		private static UiStack BuildBody(Transform parent)
		{
			UiStack outer = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 4f);

			chipRow = UiStack.Horizontal(outer.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f, scrollable: true);
			outer.Add(chipRow.FixedHeight(ChipSize + BakedChipHeight + 8f));

			UiStack transport = UiStack.Horizontal(outer.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			outer.Add(transport.FixedHeight(ChipSize));

			copyFrameButton = UiButton.Create(transport.ContentTransform, "Copy Frame", RigEditorScene.CopyActiveFrame, primary: false);
			transport.Add(copyFrameButton.FixedWidth(110f));
			pasteFrameButton = UiButton.Create(transport.ContentTransform, "Paste as New", RigEditorScene.PasteFrameAsNew, primary: false);
			transport.Add(pasteFrameButton.FixedWidth(120f));
			overrideFrameButton = UiButton.Create(transport.ContentTransform, "Override", RigEditorScene.OverrideActiveFrame, primary: false);
			transport.Add(overrideFrameButton.FixedWidth(90f));
			moveFrameLeftButton = UiButton.Create(transport.ContentTransform, "«", () => RigEditorScene.MoveActiveFrame(-1), primary: false);
			transport.Add(moveFrameLeftButton.FixedWidth(36f));
			moveFrameRightButton = UiButton.Create(transport.ContentTransform, "»", () => RigEditorScene.MoveActiveFrame(1), primary: false);
			transport.Add(moveFrameRightButton.FixedWidth(36f));

			UiLabel spacer = UiLabel.Create(transport.ContentTransform, string.Empty);
			spacer.Grow();
			transport.Add(spacer);

			playPauseButton = UiButton.Create(transport.ContentTransform, "Play", RigEditorScene.TogglePlayback, primary: false).FixedWidth(110f);
			transport.Add(playPauseButton);
			LabHoverInfo.Bind(playPauseButton.GameObject, "animator.timeline.Play");
			return outer;
		}

		/// <summary>Drops widget refs after the Timeline dock is destroyed so later refreshes no-op.</summary>
		internal static void Unbind()
		{
			chipRow = null;
			playPauseButton = null;
			copyFrameButton = null;
			pasteFrameButton = null;
			overrideFrameButton = null;
			moveFrameLeftButton = null;
			moveFrameRightButton = null;
		}

		/// <summary>Rebuilds the frame-chip strip for the active clip and updates Play/Pause plus clipboard/reorder button state.</summary>
		/// <remarks>No-ops until <see cref="Build"/> or <see cref="BuildInto"/> has created the strip.</remarks>
		internal static void Refresh(AnimationClip activeClip, int activeFrameIndex, int activeBakedIndex, bool isPlaying)
		{
			if (chipRow == null)
			{
				return;
			}

			chipRow.Clear();

			if (activeClip != null)
			{
				for (int i = 0; i < activeClip.PoseFrames.Count; i++)
				{
					BuildFrameGroup(activeClip.PoseFrames[i], i, activeFrameIndex, activeBakedIndex);
				}
				chipRow.Add(UiButton.Create(chipRow.ContentTransform, "+", RigEditorScene.AddFrame, primary: false).FixedWidth(ChipSize).FixedHeight(ChipSize));
			}

			playPauseButton.SetLabel(isPlaying ? "Pause" : "Play");
			RefreshFrameClipboardButtons();
		}

		/// <summary>Greys out Paste until a clip is active and a frame has been copied; Override needs a clipboard on Rest Pose or a clip; « / » at either end of the clip.</summary>
		private static void RefreshFrameClipboardButtons()
		{
			AnimationClip clip = RigEditorScene.ActiveClip;
			bool hasClip = clip != null;
			bool hasClipboard = RigEditorScene.HasFrameClipboard;
			copyFrameButton.Interactable(true);
			pasteFrameButton.Interactable(hasClip && hasClipboard);
			overrideFrameButton.Interactable(hasClipboard);
			moveFrameLeftButton.Interactable(hasClip && RigEditorScene.ActiveFrameIndex > 0);
			moveFrameRightButton.Interactable(hasClip && RigEditorScene.ActiveFrameIndex < clip.PoseFrames.Count - 1);
		}

		/// <summary>Builds one frame's chip plus its baked sub-chip row (one sub-chip per PoseFrame.BakedFrames entry, sharing the parent chip's slot width). EasingSteps &lt;= 0 always has exactly one baked entry, reading as one solid block; EasingSteps &gt; 0 subdivides it into that many narrower chips.</summary>
		private static void BuildFrameGroup(PoseFrame frame, int frameIndex, int activeFrameIndex, int activeBakedIndex)
		{
			UiStack group = UiStack.Vertical(chipRow.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f);
			chipRow.Add(group.FixedWidth(ChipSize));

			UiButton chip = UiButton.Create(group.ContentTransform, (frameIndex + 1).ToString(), () => RigEditorScene.ScrubToFrame(frameIndex), primary: false);
			chip.SetColor(frameIndex == activeFrameIndex ? UiTheme.Default.AccentColor : UiTheme.Default.RowButtonColor);
			group.Add(chip.FixedHeight(ChipSize));
			LabHoverInfo.Bind(chip.GameObject, "animator.timeline.FrameChip");

			int bakedCount = Mathf.Max(1, frame.BakedFrames.Count);
			float subWidth = (ChipSize - (bakedCount - 1) * BakedSubGap) / bakedCount;
			UiStack bakedRow = UiStack.Horizontal(group.ContentTransform, UiTheme.Default, spacing: BakedSubGap, padding: 0f);
			group.Add(bakedRow.FixedHeight(BakedChipHeight));
			for (int j = 0; j < bakedCount; j++)
			{
				int capturedFrame = frameIndex;
				int capturedBaked = j;
				bool isActive = frameIndex == activeFrameIndex && j == activeBakedIndex;
				UiButton subChip = UiButton.Create(bakedRow.ContentTransform, string.Empty,
					() => RigEditorScene.ScrubToBakedFrame(capturedFrame, capturedBaked), primary: false).FixedWidth(subWidth);
				subChip.SetColor(isActive ? UiTheme.Default.AccentColor : GhostInactiveColor);
				bakedRow.Add(subChip);
				LabHoverInfo.Bind(subChip.GameObject, "animator.timeline.BakedChip");
			}
		}
	}
}
