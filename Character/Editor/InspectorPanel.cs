using System.Collections.Generic;
using System.Globalization;
using LokrLab.Editor.Animation;
using LokrLab.Projects;
using LokrLabApi;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Godot-Inspector-style "select something, see and edit every property by value" panel -- the right dock column.</summary>
	/// <remarks>
	/// Dispatches on LabNode.Kind strings (mapped from RigEditorScene.CurrentInspectorTarget) between four
	/// mutually exclusive built-in sections that stay as persistent widgets: Part (frame-independent info --
	/// name, layer, visibility, pivot, not position/rotation/shear/scale which are a property of a specific
	/// frame's pose), AnimationClip (a real clip's name/frame-count + Delete Animation), Frame (a real frame's
	/// Duration/Easing/Events/Attach Points + Copy/Paste/Override/reorder + Delete Frame, plus the
	/// parts-in-this-context lists shared with Rest Pose, which reuses this trimmed section instead of the
	/// AnimationClip section since it isn't a real clip), and Reference (a scale-reference overlay's character,
	/// pose, position, rotation -- never scale, which is locked so the overlay stays a known in-game size).
	/// RegisterInspectorSection contributions stack under the built-in section and rebuild only when the
	/// inspected kind+id changes -- never on a playback-tick Refresh. The shell InspectorDock uses the same
	/// kind strings via RegisterInspectorDrawer. Session-wide editing modes (Mass Edit, tool mode) live on
	/// ToolbarPanel, not here.
	///
	/// Built on SimpleUI: the whole panel is one outer scrollable UiStack that grows to fit whichever section is
	/// active. The parts lists are UiList&lt;DraggablePart&gt;, keyed by a string folding in expanded/approximate/
	/// showRemove, so a row rebuilds via key-diffing exactly when its shape changes and is otherwise left alone
	/// across Refresh() calls -- this matters because Refresh runs on every playback tick, and destroying a row's
	/// GameObject out from under an in-progress Button click is what made the Up/Down reorder arrows unreliable
	/// before this fix. Frame events use the same UiList pattern (keyed by event name) plus a UiComboBox of
	/// CombatPlaybackRequirements.KnownEventNames; the combobox stays typeable for OnAbilityCustomEvent strings.
	/// Attach points use the same pattern: a UiComboBox of CombatPlaybackRequirements.AttachPointNames plus a
	/// UiList of the frame's sockets with live X/Y/Rot fields, keyed by name so playback Refresh does not tear
	/// out a row mid-edit.
	/// </remarks>
	internal static class InspectorPanel
	{
		private static UiPanel rootPanel;
		private static UiStack hostedRoot;
		private static UiLabel emptyStateLabel;

		private static UiStack partSection;
		private static UiLabel partNameValueLabel;
		private static UiLabel partLayerValueLabel;
		private static UiToggle partVisibleToggle;
		private static UiTextField partPivotXField;
		private static UiTextField partPivotYField;
		private static UiButton centerSelectedButton;

		private static UiStack animationSection;
		private static UiLabel animationTitleLabel;
		private static UiLabel animationFrameCountLabel;

		private static UiStack frameSection;
		private static UiLabel frameTitleLabel;
		private static UiLabel restPoseHintLabel;
		private static UiStack frameOnlyControlsRoot;
		private static UiTextField frameDurationField;
		private static UiTextField frameRootMotionField;
		private static UiButton easingButton;
		private static UiTextField frameEasingStepsField;
		private static UiComboBox frameEventCombo;
		private static UiList<string> frameEventsList;
		private static UiComboBox frameAttachCombo;
		private static UiList<AttachPointPose> frameAttachPointsList;
		private static UiButton copyFrameButton;
		private static UiButton pasteFrameButton;
		private static UiButton overrideFrameButton;
		private static UiButton moveFrameLeftButton;
		private static UiButton moveFrameRightButton;
		private static UiLabel excludedHeaderLabel;
		private static UiList<DraggablePart> includedPartsList;
		private static UiList<DraggablePart> excludedPartsList;

		private static UiStack referenceSection;
		private static UiLabel referenceTitleLabel;
		private static UiLabel referenceCharacterLabel;
		private static UiDropdown referenceAnimationDropdown;
		private static UiTextField referencePosXField;
		private static UiTextField referencePosYField;
		private static UiTextField referenceRotField;
		private static UiToggle referenceVisibleToggle;
		private static UiTextField referenceOpacityField;
		/// <summary>Last metaExo whose animation names were written into the dropdown -- SetOptions is skipped while this still matches, so a Refresh during playback does not rebuild (and fire) the dropdown.</summary>
		private static string lastReferenceMetaExo;

		private static UiStack extraSectionsHost;
		/// <summary>Kind+id last used to build extra RegisterInspectorSection widgets -- compared so playback Refresh does not Clear() them.</summary>
		private static string lastExtraIdentity;

		/// <summary>Expand/collapse state for the parts lists' per-part rows, keyed by part name. Persists across an in-place Refresh() of the same frame/Rest Pose; cleared when the active clip/frame identity changes.</summary>
		private static readonly HashSet<string> expandedPartNames = new HashSet<string>();
		private static AnimationClip lastFrameClip;
		private static int lastFrameIndex = -1;
		private static bool lastWasRestPose;

		private sealed class PartFieldRefs
		{
			/// <summary>Cached references to this row's editable position/rotation/shear/scale fields, so UpdateFieldRefs can refresh their text in place instead of rebuilding the row.</summary>
			internal UiTextField posX, posY, rot, shear, scaleX, scaleY;
		}
		private sealed class AttachFieldRefs
		{
			/// <summary>Cached X/Y/Rot fields for one attach-point row, so playback Refresh can update text in place.</summary>
			internal UiTextField x, y, rot;
		}
		private static readonly Dictionary<string, AttachFieldRefs> attachFieldRefs = new Dictionary<string, AttachFieldRefs>();
		private static readonly Dictionary<string, PartFieldRefs> partFieldRefs = new Dictionary<string, PartFieldRefs>();

		/// <summary>True after Build or BuildInto has created the persistent section widgets.</summary>
		internal static bool IsBuilt
		{
			get
			{
				return (rootPanel != null && rootPanel.GameObject != null)
					|| (hostedRoot != null && hostedRoot.GameObject != null);
			}
		}

		/// <summary>Drops the hosted panel ref after the lab scene is torn down.</summary>
		internal static void ResetSession()
		{
			rootPanel = null;
			hostedRoot = null;
			extraSectionsHost = null;
			lastExtraIdentity = null;
		}

		/// <summary>Shows or hides the whole panel without destroying widgets (shell workspace swap).</summary>
		internal static void Visible(bool visible)
		{
			if (rootPanel != null && rootPanel.GameObject != null)
			{
				rootPanel.Visible(visible);
			}

			if (hostedRoot != null && hostedRoot.GameObject != null)
			{
				hostedRoot.Visible(visible);
			}
		}

		/// <summary>Builds the panel into the dock row's Inspector slot.</summary>
		internal static void Build(Transform parent, Font labelFont)
		{
			if (IsBuilt)
			{
				return;
			}

			ResetSession();
			UiPanel panel = UiPanel.Create(parent, UiTheme.Default, "Inspector");
			rootPanel = panel;
			UiStack outer = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 4f, padding: 8f, scrollable: true);
			outer.Grow();
			panel.Add(outer);
			BuildSections(outer);
			Refresh();
		}

		/// <summary>Builds the live Animator sections into the shell inspector (no nested titled panel or inner scroll).</summary>
		/// <remarks>
		/// InspectorDock's animator host is already a Grow() scroll view. A titled UiPanel plus
		/// another stretch-to-fill ScrollRect inside it reports no preferred height and collapses
		/// the form — the same nested-scroll bug as the Ability Library inspector.
		/// </remarks>
		internal static void BuildInto(Transform parent)
		{
			if (IsBuilt)
			{
				return;
			}

			ResetSession();
			hostedRoot = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 8f, scrollable: false);
			BuildSections(hostedRoot);
			Refresh();
		}

		private static void BuildSections(UiStack outer)
		{
			emptyStateLabel = UiLabel.Create(outer.ContentTransform, "Nothing selected.", UiTheme.Default, alignment: TextAnchor.UpperLeft);
			outer.Add(emptyStateLabel.FixedHeight(24f));

			partSection = BuildPartSection(outer.ContentTransform);
			outer.Add(partSection);

			animationSection = BuildAnimationSection(outer.ContentTransform);
			outer.Add(animationSection);

			frameSection = BuildFrameSection(outer.ContentTransform);
			outer.Add(frameSection);

			referenceSection = BuildReferenceSection(outer.ContentTransform);
			outer.Add(referenceSection);

			extraSectionsHost = UiStack.Vertical(outer.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
			outer.Add(extraSectionsHost);
		}

		private static UiStack BuildPartSection(Transform parent)
		{
			UiStack section = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f);

			partNameValueLabel = UiLabel.Create(section.ContentTransform, "-", UiTheme.Default, 13);
			section.Add(partNameValueLabel.FixedHeight(24f));
			LabHoverInfo.Bind(partNameValueLabel.GameObject, "animator.part.Name");

			UiStack layerRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(layerRow.FixedHeight(26f));
			layerRow.Add(UiLabel.Create(layerRow.ContentTransform, "Layer:").FixedWidth(60f));
			partLayerValueLabel = UiLabel.Create(layerRow.ContentTransform, "-");
			layerRow.Add(partLayerValueLabel.FixedWidth(36f));
			layerRow.Add(UiButton.Create(layerRow.ContentTransform, "+1",
				() => RigEditorScene.MovePartLayer(RigEditorScene.SelectedPart, 1), primary: false).FixedWidth(36f).FixedHeight(24f));
			layerRow.Add(UiButton.Create(layerRow.ContentTransform, "-1",
				() => RigEditorScene.MovePartLayer(RigEditorScene.SelectedPart, -1), primary: false).FixedWidth(36f).FixedHeight(24f));
			LabHoverInfo.Bind(layerRow.GameObject, "animator.part.Layer");

			partVisibleToggle = UiToggle.Create(section.ContentTransform, "Visible", true);
			partVisibleToggle.OnValueChanged(OnVisibleChanged);
			section.Add(partVisibleToggle.FixedHeight(24f));
			LabHoverInfo.Bind(partVisibleToggle.GameObject, "animator.part.Visible");

			UiStack pivotRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(pivotRow.FixedHeight(30f));
			pivotRow.Add(UiLabel.Create(pivotRow.ContentTransform, "Pivot:").FixedWidth(50f));
			partPivotXField = UiTextField.Create(pivotRow.ContentTransform);
			partPivotXField.OnEndEdit(OnPartPivotChanged);
			pivotRow.Add(partPivotXField.FixedWidth(90f));
			partPivotYField = UiTextField.Create(pivotRow.ContentTransform);
			partPivotYField.OnEndEdit(OnPartPivotChanged);
			pivotRow.Add(partPivotYField.FixedWidth(90f));
			LabHoverInfo.Bind(pivotRow.GameObject, "animator.part.Pivot");

			UiButton replace = UiButton.Create(section.ContentTransform, "Replace...", OnReplaceClicked, primary: false);
			section.Add(replace.FixedHeight(28f));
			LabHoverInfo.Bind(replace.GameObject, "animator.part.Replace");
			UiButton removeFromClip = UiButton.Create(section.ContentTransform, "Remove from Clip", OnMassRemoveClicked, primary: false);
			section.Add(removeFromClip.FixedHeight(28f));
			LabHoverInfo.Bind(removeFromClip.GameObject, "animator.part.RemoveFromClip");
			centerSelectedButton = UiButton.Create(section.ContentTransform, "Center Selected", RigEditorScene.CenterSelectedParts, primary: false);
			section.Add(centerSelectedButton.FixedHeight(28f));
			LabHoverInfo.Bind(centerSelectedButton.GameObject, "animator.part.CenterSelected");

			return section;
		}

		private static UiStack BuildAnimationSection(Transform parent)
		{
			UiStack section = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f);
			animationTitleLabel = UiLabel.Create(section.ContentTransform, "-", UiTheme.Default, UiTheme.Default.TitleFontSize);
			section.Add(animationTitleLabel.FixedHeight(26f));
			LabHoverInfo.Bind(animationTitleLabel.GameObject, "animator.clip.Name");
			animationFrameCountLabel = UiLabel.Create(section.ContentTransform, "-");
			section.Add(animationFrameCountLabel.FixedHeight(22f));
			LabHoverInfo.Bind(animationFrameCountLabel.GameObject, "animator.clip.FrameCount");
			section.Add(UiLabel.Create(section.ContentTransform, "Root motion X is per frame (pixels, cumulative). Empty = none.", UiTheme.Default, 11).FixedHeight(32f));
			section.Add(UiButton.Create(section.ContentTransform, "Delete Animation", RigEditorScene.DeleteActiveClip, primary: false).FixedHeight(28f));
			return section;
		}

		/// <summary>Builds the Frame section, shared by real frames and Rest Pose.</summary>
		/// <remarks>frameOnlyControlsRoot (Duration/Easing/Events/Attach Points/reorder/Delete Frame) is hidden as a group for Rest Pose, which has no corresponding PoseFrame to hold any of it. Copy / Override stay visible on Rest Pose; Paste New stays visible but greyed. The parts lists below are sorted DESCENDING by effective draw order (front-most first, top of the list), the opposite of SceneTreePanel's ascending/back-to-front convention.</remarks>
		private static UiStack BuildFrameSection(Transform parent)
		{
			UiStack section = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f);

			frameTitleLabel = UiLabel.Create(section.ContentTransform, "-", UiTheme.Default, UiTheme.Default.TitleFontSize);
			section.Add(frameTitleLabel.FixedHeight(26f));
			LabHoverInfo.Bind(frameTitleLabel.GameObject, "animator.frame.Title");
			restPoseHintLabel = UiLabel.Create(section.ContentTransform, "Default for new clips. Later Rest Pose edits do not move Walk / Attack / other clips.", UiTheme.Default, 11);
			section.Add(restPoseHintLabel.FixedHeight(36f));

			UiStack clipboardRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(clipboardRow.FixedHeight(28f));
			copyFrameButton = UiButton.Create(clipboardRow.ContentTransform, "Copy", RigEditorScene.CopyActiveFrame, primary: false);
			copyFrameButton.Grow();
			clipboardRow.Add(copyFrameButton);
			LabHoverInfo.Bind(copyFrameButton.GameObject, "animator.frame.Copy");
			pasteFrameButton = UiButton.Create(clipboardRow.ContentTransform, "Paste New", RigEditorScene.PasteFrameAsNew, primary: false);
			pasteFrameButton.Grow();
			clipboardRow.Add(pasteFrameButton);
			LabHoverInfo.Bind(pasteFrameButton.GameObject, "animator.frame.PasteNew");
			overrideFrameButton = UiButton.Create(clipboardRow.ContentTransform, "Override", RigEditorScene.OverrideActiveFrame, primary: false);
			overrideFrameButton.Grow();
			clipboardRow.Add(overrideFrameButton);
			LabHoverInfo.Bind(overrideFrameButton.GameObject, "animator.frame.Override");

			frameOnlyControlsRoot = UiStack.Vertical(section.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
			section.Add(frameOnlyControlsRoot);

			UiStack durationRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(durationRow.FixedHeight(28f));
			durationRow.Add(UiLabel.Create(durationRow.ContentTransform, "Duration:").FixedWidth(70f));
			frameDurationField = UiTextField.Create(durationRow.ContentTransform, "0.15");
			frameDurationField.OnEndEdit(OnDurationChanged);
			durationRow.Add(frameDurationField.FixedWidth(90f));
			LabHoverInfo.Bind(durationRow.GameObject, "animator.frame.Duration");

			UiStack rootMotionRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(rootMotionRow.FixedHeight(28f));
			rootMotionRow.Add(UiLabel.Create(rootMotionRow.ContentTransform, "Root X (px):").FixedWidth(90f));
			frameRootMotionField = UiTextField.Create(rootMotionRow.ContentTransform);
			frameRootMotionField.OnEndEdit(OnRootMotionChanged);
			rootMotionRow.Add(frameRootMotionField.Grow());
			LabHoverInfo.Bind(rootMotionRow.GameObject, "animator.frame.RootMotionX");

			UiStack easingRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(easingRow.FixedHeight(28f));
			easingButton = UiButton.Create(easingRow.ContentTransform, "Easing: Linear", RigEditorScene.CycleActiveFrameEasing, primary: false);
			easingRow.Add(easingButton.FixedWidth(160f));
			LabHoverInfo.Bind(easingButton.GameObject, "animator.frame.Easing");
			easingRow.Add(UiLabel.Create(easingRow.ContentTransform, "Steps:").FixedWidth(45f));
			frameEasingStepsField = UiTextField.Create(easingRow.ContentTransform, "0");
			frameEasingStepsField.OnEndEdit(RigEditorScene.SetActiveFrameEasingSteps);
			easingRow.Add(frameEasingStepsField.FixedWidth(60f));
			LabHoverInfo.Bind(frameEasingStepsField.GameObject, "animator.frame.EasingSteps");

			frameOnlyControlsRoot.Add(UiLabel.Create(frameOnlyControlsRoot.ContentTransform, "Events:").FixedHeight(22f));
			UiStack eventsRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(eventsRow.FixedHeight(28f));
			frameEventCombo = UiComboBox.Create(eventsRow.ContentTransform, CombatPlaybackRequirements.KnownEventNames);
			frameEventCombo.OnEndEdit(OnFrameEventComboCommitted);
			eventsRow.Add(frameEventCombo.Grow());
			eventsRow.Add(UiButton.Create(eventsRow.ContentTransform, "Add", OnAddFrameEventClicked, primary: false).FixedWidth(60f));
			LabHoverInfo.Bind(eventsRow.GameObject, "animator.frame.AddEvent");
			frameEventsList = UiList<string>.Create(frameOnlyControlsRoot.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			frameOnlyControlsRoot.Add(frameEventsList);
			LabHoverInfo.Bind(frameEventsList.GameObject, "animator.frame.Events");

			frameOnlyControlsRoot.Add(UiLabel.Create(frameOnlyControlsRoot.ContentTransform, "Attach points:").FixedHeight(22f));
			UiStack attachAddRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(attachAddRow.FixedHeight(28f));
			frameAttachCombo = UiComboBox.Create(attachAddRow.ContentTransform, CombatPlaybackRequirements.AttachPointNames);
			frameAttachCombo.OnEndEdit(OnFrameAttachComboCommitted);
			attachAddRow.Add(frameAttachCombo.Grow());
			attachAddRow.Add(UiButton.Create(attachAddRow.ContentTransform, "Add", OnAddFrameAttachClicked, primary: false).FixedWidth(60f));
			LabHoverInfo.Bind(attachAddRow.GameObject, "animator.frame.AttachPoints");
			frameAttachPointsList = UiList<AttachPointPose>.Create(frameOnlyControlsRoot.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			frameOnlyControlsRoot.Add(frameAttachPointsList);
			LabHoverInfo.Bind(frameAttachPointsList.GameObject, "animator.frame.AttachPose");

			UiStack reorderRow = UiStack.Horizontal(frameOnlyControlsRoot.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			frameOnlyControlsRoot.Add(reorderRow.FixedHeight(28f));
			moveFrameLeftButton = UiButton.Create(reorderRow.ContentTransform, "«", () => RigEditorScene.MoveActiveFrame(-1), primary: false);
			reorderRow.Add(moveFrameLeftButton.FixedWidth(36f));
			moveFrameRightButton = UiButton.Create(reorderRow.ContentTransform, "»", () => RigEditorScene.MoveActiveFrame(1), primary: false);
			reorderRow.Add(moveFrameRightButton.FixedWidth(36f));
			UiButton deleteFrameButton = UiButton.Create(reorderRow.ContentTransform, "Delete Frame", RigEditorScene.DeleteActiveFrame, primary: false);
			deleteFrameButton.Grow();
			reorderRow.Add(deleteFrameButton);

			section.Add(UiLabel.Create(section.ContentTransform, "Parts (top=front, bottom=back):", UiTheme.Default, 12).FixedHeight(22f));
			includedPartsList = UiList<DraggablePart>.Create(section.ContentTransform, UiOrientation.Vertical, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: false);
			section.Add(includedPartsList);

			excludedHeaderLabel = UiLabel.Create(section.ContentTransform, "Not in this frame:", UiTheme.Default, 11);
			section.Add(excludedHeaderLabel.FixedHeight(20f));
			excludedPartsList = UiList<DraggablePart>.Create(section.ContentTransform, UiOrientation.Vertical, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: false);
			section.Add(excludedPartsList);

			return section;
		}

		/// <summary>Builds the Reference section for a selected scale-reference overlay (character, pose, position, rotation -- not scale).</summary>
		private static UiStack BuildReferenceSection(Transform parent)
		{
			UiStack section = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f);

			referenceTitleLabel = UiLabel.Create(section.ContentTransform, "Reference", UiTheme.Default, UiTheme.Default.TitleFontSize);
			section.Add(referenceTitleLabel.FixedHeight(26f));

			referenceCharacterLabel = UiLabel.Create(section.ContentTransform, "-", UiTheme.Default, 13);
			section.Add(referenceCharacterLabel.FixedHeight(24f));

			section.Add(UiButton.Create(section.ContentTransform, "Choose Character...", OnChooseReferenceCharacterClicked, primary: false).FixedHeight(28f));

			UiStack animRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(animRow.FixedHeight(30f));
			animRow.Add(UiLabel.Create(animRow.ContentTransform, "Pose:").FixedWidth(50f));
			referenceAnimationDropdown = UiDropdown.Create(animRow.ContentTransform, new[] { "Stand" });
			referenceAnimationDropdown.OnValueChanged(OnReferenceAnimationChanged);
			animRow.Add(referenceAnimationDropdown);
			LabHoverInfo.Bind(animRow.GameObject, "animator.reference.Pose");

			UiStack posRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(posRow.FixedHeight(30f));
			posRow.Add(UiLabel.Create(posRow.ContentTransform, "Pos:").FixedWidth(50f));
			referencePosXField = UiTextField.Create(posRow.ContentTransform);
			referencePosXField.OnEndEdit(OnReferenceTransformChanged);
			posRow.Add(referencePosXField.FixedWidth(90f));
			referencePosYField = UiTextField.Create(posRow.ContentTransform);
			referencePosYField.OnEndEdit(OnReferenceTransformChanged);
			posRow.Add(referencePosYField.FixedWidth(90f));
			LabHoverInfo.Bind(posRow.GameObject, "animator.reference.Pos");

			UiStack rotRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(rotRow.FixedHeight(30f));
			rotRow.Add(UiLabel.Create(rotRow.ContentTransform, "Rot:").FixedWidth(50f));
			referenceRotField = UiTextField.Create(rotRow.ContentTransform);
			referenceRotField.OnEndEdit(OnReferenceTransformChanged);
			rotRow.Add(referenceRotField.FixedWidth(90f));
			LabHoverInfo.Bind(rotRow.GameObject, "animator.reference.Rot");

			referenceVisibleToggle = UiToggle.Create(section.ContentTransform, "Visible", true);
			referenceVisibleToggle.OnValueChanged(OnReferenceVisibleChanged);
			section.Add(referenceVisibleToggle.FixedHeight(24f));

			UiStack opacityRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(opacityRow.FixedHeight(30f));
			opacityRow.Add(UiLabel.Create(opacityRow.ContentTransform, "Opacity:").FixedWidth(70f));
			referenceOpacityField = UiTextField.Create(opacityRow.ContentTransform);
			referenceOpacityField.OnEndEdit(OnReferenceOpacityChanged);
			opacityRow.Add(referenceOpacityField.FixedWidth(90f));
			LabHoverInfo.Bind(opacityRow.GameObject, "animator.reference.Opacity");

			section.Add(UiLabel.Create(section.ContentTransform, "Scale is locked — this overlay is a known in-game size.", UiTheme.Default, 11).FixedHeight(22f));
			section.Add(UiButton.Create(section.ContentTransform, "Remove Reference", RigEditorScene.RemoveSelectedReference, primary: false).FixedHeight(28f));

			return section;
		}

		/// <summary>Refreshes whichever built-in section is currently shown, then extra registered sections if the kind+id changed.</summary>
		/// <remarks>No-ops until <see cref="Build"/> runs. The shell builds this lazily when a Part/Clip/Frame/Reference is selected in the Animator workspace.</remarks>
		internal static void Refresh()
		{
			if (!IsBuilt)
			{
				return;
			}

			string kind = KindFromCurrentTarget();
			bool showPart = kind == CharacterNodeKinds.Part && RigEditorScene.SelectedPart != null;
			bool showAnimation = kind == CharacterNodeKinds.AnimationClip && RigEditorScene.ActiveClip != null;
			bool showFrame = kind == CharacterNodeKinds.Frame;
			bool showReference = kind == CharacterNodeKinds.Reference && RigEditorScene.SelectedReference != null;
			bool hasSelection = showPart || showAnimation || showFrame || showReference;

			emptyStateLabel.Visible(!hasSelection);
			partSection.Visible(showPart);
			animationSection.Visible(showAnimation);
			frameSection.Visible(showFrame);
			referenceSection.Visible(showReference);

			if (showPart)
			{
				RefreshPartSection();
			}
			else if (showAnimation)
			{
				RefreshAnimationSection();
			}
			else if (showFrame)
			{
				RefreshFrameSection();
			}
			else if (showReference)
			{
				RefreshReferenceSection();
			}

			RefreshExtraSections(kind, hasSelection);
		}

		/// <summary>Maps the Animator's InspectorTarget to the same LabNode.Kind strings the shell drawers register.</summary>
		/// <remarks>
		/// Rest Pose is InspectorTarget.Animation with a null ActiveClip; it keeps using the Frame
		/// section (and the Frame kind) because it is not a real clip.
		/// </remarks>
		internal static string KindFromCurrentTarget()
		{
			switch (RigEditorScene.CurrentInspectorTarget)
			{
				case RigEditorScene.InspectorTarget.Part:
					return RigEditorScene.SelectedPart != null ? CharacterNodeKinds.Part : null;
				case RigEditorScene.InspectorTarget.Animation:
					return RigEditorScene.ActiveClip != null ? CharacterNodeKinds.AnimationClip : CharacterNodeKinds.Frame;
				case RigEditorScene.InspectorTarget.Frame:
					return CharacterNodeKinds.Frame;
				case RigEditorScene.InspectorTarget.Reference:
					return RigEditorScene.SelectedReference != null ? CharacterNodeKinds.Reference : null;
				default:
					return null;
			}
		}

		/// <summary>Rebuilds RegisterInspectorSection widgets only when the inspected kind+id changes, never on a playback tick of the same object.</summary>
		private static void RefreshExtraSections(string kind, bool hasSelection)
		{
			if (extraSectionsHost == null)
			{
				return;
			}

			LabNode node = hasSelection ? NodeForCurrentTarget(kind) : null;
			string identity = node != null ? kind + "|" + node.Id : "";
			if (identity == lastExtraIdentity)
			{
				return;
			}

			lastExtraIdentity = identity;
			extraSectionsHost.Clear();
			if (node == null || string.IsNullOrEmpty(kind))
			{
				extraSectionsHost.Visible(false);
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: LokrLabApi.LokrLabApi.GetProjectType(CharacterProjectType.Id);
			if (type == null)
			{
				extraSectionsHost.Visible(false);
				return;
			}

			IReadOnlyList<InspectorDrawer> sections = type.FindInspectorSections(kind);
			for (int i = 0; i < sections.Count; i++)
			{
				sections[i](node, session, extraSectionsHost.ContentTransform);
			}

			extraSectionsHost.Visible(sections.Count > 0);
		}

		/// <summary>Synthetic LabNode so extra sections see the same Payload the shell drawers would.</summary>
		private static LabNode NodeForCurrentTarget(string kind)
		{
			if (kind == CharacterNodeKinds.Part && RigEditorScene.SelectedPart != null)
			{
				DraggablePart part = RigEditorScene.SelectedPart;
				return new LabNode
				{
					Id = "part:" + part.PartName,
					DisplayName = part.PartName,
					Kind = CharacterNodeKinds.Part,
					IconKey = "Part",
					Payload = part
				};
			}

			if (kind == CharacterNodeKinds.AnimationClip && RigEditorScene.ActiveClip != null)
			{
				AnimationClip clip = RigEditorScene.ActiveClip;
				return new LabNode
				{
					Id = "clip:" + clip.Name,
					DisplayName = clip.Name,
					Kind = CharacterNodeKinds.AnimationClip,
					IconKey = "Clip",
					Payload = clip
				};
			}

			if (kind == CharacterNodeKinds.Frame)
			{
				AnimationClip clip = RigEditorScene.ActiveClip;
				int index = RigEditorScene.ActiveFrameIndex;
				string clipName = clip != null ? clip.Name : "rest";
				return new LabNode
				{
					Id = "frame:" + clipName + ":" + index.ToString(CultureInfo.InvariantCulture),
					DisplayName = clip != null ? clip.Name + " / " + (index + 1).ToString(CultureInfo.InvariantCulture) : "Rest Pose",
					Kind = CharacterNodeKinds.Frame,
					IconKey = "Frame",
					Payload = clip
				};
			}

			if (kind == CharacterNodeKinds.Reference && RigEditorScene.SelectedReference != null)
			{
				ReferenceCharacter reference = RigEditorScene.SelectedReference;
				return new LabNode
				{
					Id = "ref:" + reference.MetaExoName,
					DisplayName = reference.DisplayName,
					Kind = CharacterNodeKinds.Reference,
					IconKey = "Ref",
					Payload = reference
				};
			}

			return null;
		}

		private static void RefreshPartSection()
		{
			DraggablePart part = RigEditorScene.SelectedPart;
			partNameValueLabel.SetText(part.PartName);
			partLayerValueLabel.SetText(part.StaticLayer.ToString(CultureInfo.InvariantCulture));
			partVisibleToggle.SetValueSilently(part.Visible);

			Vector2 pivotWorld = RigEditorScene.GetPivotWorldPosition(part);
			partPivotXField.SetText(FormatFloat(pivotWorld.x));
			partPivotYField.SetText(FormatFloat(pivotWorld.y));

			int multiSelectedCount = RigEditorScene.MultiSelection.Count;
			centerSelectedButton.Interactable(multiSelectedCount > 1);
			centerSelectedButton.SetLabel(multiSelectedCount > 1
				? "Center Selected (" + multiSelectedCount.ToString(CultureInfo.InvariantCulture) + ")"
				: "Center Selected");
		}

		private static void RefreshAnimationSection()
		{
			AnimationClip clip = RigEditorScene.ActiveClip;
			animationTitleLabel.SetText("Animation: " + clip.Name);
			animationFrameCountLabel.SetText(clip.PoseFrames.Count.ToString(CultureInfo.InvariantCulture) + " frame(s)");
		}

		private static void RefreshReferenceSection()
		{
			ReferenceCharacter reference = RigEditorScene.SelectedReference;
			if (reference == null)
			{
				return;
			}

			referenceTitleLabel.SetText("Reference");
			referenceCharacterLabel.SetText(reference.DisplayName + "  (" + reference.MetaExoName + ")");

			if (reference.MetaExoName != lastReferenceMetaExo)
			{
				List<string> names = new List<string>();
				foreach (string name in reference.AnimationNames)
				{
					names.Add(name);
				}
				if (names.Count == 0)
				{
					names.Add("Stand");
				}
				referenceAnimationDropdown.SetOptions(names);
				lastReferenceMetaExo = reference.MetaExoName;
			}
			referenceAnimationDropdown.SetValueSilently(reference.AnimationIndex);

			if (!referencePosXField.InputField.isFocused)
			{
				referencePosXField.SetText(FormatFloat(reference.transform.position.x));
			}
			if (!referencePosYField.InputField.isFocused)
			{
				referencePosYField.SetText(FormatFloat(reference.transform.position.y));
			}
			if (!referenceRotField.InputField.isFocused)
			{
				referenceRotField.SetText(FormatFloat(reference.RotationDegrees));
			}
			referenceVisibleToggle.SetValueSilently(reference.Visible);
			if (!referenceOpacityField.InputField.isFocused)
			{
				referenceOpacityField.SetText(FormatFloat(reference.Opacity));
			}
		}

		private static void RefreshFrameSection()
		{
			AnimationClip activeClip = RigEditorScene.ActiveClip;
			int activeFrameIndex = RigEditorScene.ActiveFrameIndex;
			bool isRestPose = activeClip == null;

			if (activeClip != lastFrameClip || activeFrameIndex != lastFrameIndex || isRestPose != lastWasRestPose)
			{
				expandedPartNames.Clear();
				lastFrameClip = activeClip;
				lastFrameIndex = activeFrameIndex;
				lastWasRestPose = isRestPose;
				frameEventCombo.SetText(string.Empty);
				frameAttachCombo.SetText(string.Empty);
			}

			frameOnlyControlsRoot.Visible(!isRestPose);
			restPoseHintLabel.Visible(isRestPose);

			bool hasClipboard = RigEditorScene.HasFrameClipboard;
			copyFrameButton.Interactable(true);
			pasteFrameButton.Interactable(!isRestPose && hasClipboard);
			overrideFrameButton.Interactable(hasClipboard);

			PoseFrame frame = null;
			if (isRestPose)
			{
				frameTitleLabel.SetText("Rest Pose");
			}
			else
			{
				frame = activeClip.PoseFrames[activeFrameIndex];
				frameTitleLabel.SetText(string.Format("Frame {0} / {1} — {2}", activeFrameIndex + 1, activeClip.PoseFrames.Count, activeClip.Name));
				frameDurationField.SetText(frame.Duration.ToString("0.###", CultureInfo.InvariantCulture));
				if (frameRootMotionField != null && !frameRootMotionField.InputField.isFocused)
				{
					frameRootMotionField.SetText(RigEditorScene.GetActiveFrameRootMotionText());
				}
				easingButton.SetLabel("Easing: " + frame.Easing);
				frameEasingStepsField.SetText(frame.EasingSteps.ToString(CultureInfo.InvariantCulture));
				frameEventsList.SetItems(RigEditorScene.GetActiveFrameEvents(), eventName => eventName, BuildFrameEventRow);
				RefreshAttachPointList();
				moveFrameLeftButton.Interactable(activeFrameIndex > 0);
				moveFrameRightButton.Interactable(activeFrameIndex < activeClip.PoseFrames.Count - 1);
			}

			RefreshPartsContainer(isRestPose, frame);
		}


		/// <summary>Rebuilds the included/excluded parts lists for the current frame or Rest Pose. In Rest Pose every part always exists (no add/remove), reordering via the rig-wide StaticLayer since Rest Pose has no per-frame RenderOrderIndex.</summary>
		private static void RefreshPartsContainer(bool isRestPose, PoseFrame frame)
		{
			List<DraggablePart> parts = new List<DraggablePart>(RigEditorScene.LoadedParts);
			parts.RemoveAll(p => p == null);

			List<DraggablePart> includedParts;
			List<DraggablePart> excludedParts = new List<DraggablePart>();

			if (isRestPose)
			{
				parts.Sort((a, b) => b.StaticLayer.CompareTo(a.StaticLayer));
				includedParts = parts;
			}
			else
			{
				includedParts = new List<DraggablePart>();
				foreach (DraggablePart part in parts)
				{
					if (frame.Poses.TryGetValue(part.PartName, out PartPose pose) && pose.Included)
					{
						includedParts.Add(part);
					}
					else
					{
						excludedParts.Add(part);
					}
				}
				includedParts.Sort((a, b) => EffectiveOrder(b, frame).CompareTo(EffectiveOrder(a, frame)));
			}

			bool showRemove = !isRestPose;
			bool capturedIsRestPose = isRestPose;
			includedPartsList.SetItems(includedParts,
				part => PartRowKey(part, capturedIsRestPose, showRemove),
				(parent, part) => BuildIncludedPartRow(parent, part, capturedIsRestPose, showRemove));

			foreach (DraggablePart part in includedParts)
			{
				if (expandedPartNames.Contains(part.PartName) && partFieldRefs.TryGetValue(part.PartName, out PartFieldRefs refs))
				{
					UpdateFieldRefs(refs, part);
				}
			}

			bool hasExcluded = !isRestPose && excludedParts.Count > 0;
			excludedHeaderLabel.Visible(hasExcluded);
			excludedPartsList.Visible(hasExcluded);
			if (hasExcluded)
			{
				excludedPartsList.SetItems(excludedParts, part => part.PartName, BuildExcludedPartRow);
			}
			else
			{
				excludedPartsList.Clear();
			}
		}

		private static string PartRowKey(DraggablePart part, bool isRestPose, bool showRemove)
		{
			bool expanded = expandedPartNames.Contains(part.PartName);
			bool approximate = !isRestPose && RigEditorScene.IsPartApproximateInActiveFrame(part.PartName);
			return part.PartName + "|" + expanded + "|" + approximate + "|" + showRemove;
		}

		private static int EffectiveOrder(DraggablePart part, PoseFrame frame)
		{
			if (frame.Poses.TryGetValue(part.PartName, out PartPose pose) && pose.RenderOrderIndex >= 0)
			{
				return pose.RenderOrderIndex;
			}
			return part.StaticLayer;
		}

		private static UiElement BuildIncludedPartRow(Transform parent, DraggablePart part, bool isRestPose, bool showRemove)
		{
			bool expanded = expandedPartNames.Contains(part.PartName);
			bool approximate = !isRestPose && RigEditorScene.IsPartApproximateInActiveFrame(part.PartName);

			UiStack row = UiStack.Vertical(parent, UiTheme.Default, spacing: 2f, padding: 0f);

			UiStack header = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f);
			row.Add(header.FixedHeight(24f));
			header.Add(UiButton.Create(header.ContentTransform, "^", () => OnMoveFramePartUp(part, isRestPose), primary: false).FixedWidth(22f));
			header.Add(UiButton.Create(header.ContentTransform, "v", () => OnMoveFramePartDown(part, isRestPose), primary: false).FixedWidth(22f));
			UiButton nameButton = UiButton.Create(header.ContentTransform, (expanded ? "v " : "> ") + part.PartName,
				() => OnTogglePartExpanded(part.PartName), primary: false);
			nameButton.Label.alignment = TextAnchor.MiddleLeft;
			nameButton.Label.fontSize = 11;
			nameButton.Grow();
			header.Add(nameButton);
			if (showRemove)
			{
				UiButton remove = UiButton.Create(header.ContentTransform, "-", () => RigEditorScene.RemovePartFromActiveFrame(part), primary: false);
				header.Add(remove.FixedWidth(24f));
				LabHoverInfo.Bind(remove.GameObject, "animator.frame.ExcludePart");
			}

			if (expanded)
			{
				BuildPartFieldRows(row, part, approximate);
			}

			return row;
		}

		private static UiElement BuildExcludedPartRow(Transform parent, DraggablePart part)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			row.FixedHeight(24f);
			UiLabel name = UiLabel.Create(row.ContentTransform, part.PartName, UiTheme.Default, 11);
			name.Grow();
			row.Add(name);
			row.Add(UiButton.Create(row.ContentTransform, "+", () => RigEditorScene.IncludePartInActiveFrame(part), primary: false).FixedWidth(24f));
			LabHoverInfo.Bind(row.GameObject, "animator.frame.IncludePart");
			return row;
		}

		/// <summary>Refreshes an already-built expanded row's live field text in place from the part's current transform. Skipped per-field while the user has it focused, so an in-progress edit never gets silently overwritten by an unrelated refresh landing mid-edit.</summary>
		private static void UpdateFieldRefs(PartFieldRefs refs, DraggablePart part)
		{
			if (refs.posX != null && !refs.posX.InputField.isFocused) refs.posX.SetText(FormatFloat(part.transform.position.x));
			if (refs.posY != null && !refs.posY.InputField.isFocused) refs.posY.SetText(FormatFloat(part.transform.position.y));
			if (refs.rot != null && !refs.rot.InputField.isFocused) refs.rot.SetText(FormatFloat(part.RotationDegrees));
			if (refs.shear != null && !refs.shear.InputField.isFocused) refs.shear.SetText(FormatFloat(part.ShearDegrees));
			if (refs.scaleX != null && !refs.scaleX.InputField.isFocused) refs.scaleX.SetText(FormatFloat(part.ScaleX));
			if (refs.scaleY != null && !refs.scaleY.InputField.isFocused) refs.scaleY.SetText(FormatFloat(part.ScaleY));
		}

		/// <summary>Builds Position/Rotation/Shear/Scale fields for one part's expanded row, reading the part's live transform and writing back through the same explicit-part RigEditorScene setters the drag tools use.</summary>
		/// <remarks>Only called once per row build; subsequent refreshes update these fields' text via UpdateFieldRefs instead of rebuilding them.</remarks>
		private static void BuildPartFieldRows(UiStack row, DraggablePart part, bool approximate)
		{
			PartFieldRefs refs = new PartFieldRefs();

			UiStack posRow = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(posRow.FixedHeight(24f));
			posRow.Add(UiLabel.Create(posRow.ContentTransform, "Pos:", UiTheme.Default, 11).FixedWidth(35f));
			refs.posX = UiTextField.Create(posRow.ContentTransform, FormatFloat(part.transform.position.x));
			posRow.Add(refs.posX.FixedWidth(60f));
			refs.posY = UiTextField.Create(posRow.ContentTransform, FormatFloat(part.transform.position.y));
			posRow.Add(refs.posY.FixedWidth(60f));
			refs.posX.InputField.interactable = !approximate;
			refs.posY.InputField.interactable = !approximate;
			UnityAction<string> onPositionChanged = _ =>
			{
				if (TryParse(refs.posX.InputField.text, out float x) && TryParse(refs.posY.InputField.text, out float y))
				{
					RigEditorScene.SetPartPosition(part, new Vector2(x, y));
				}
			};
			BindPoseField(refs.posX, onPositionChanged);
			BindPoseField(refs.posY, onPositionChanged);
			LabHoverInfo.Bind(posRow.GameObject, "animator.frame.PartPos");

			UiStack rotShearRow = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(rotShearRow.FixedHeight(24f));
			rotShearRow.Add(UiLabel.Create(rotShearRow.ContentTransform, "Rot:", UiTheme.Default, 11).FixedWidth(35f));
			refs.rot = UiTextField.Create(rotShearRow.ContentTransform, FormatFloat(part.RotationDegrees));
			rotShearRow.Add(refs.rot.FixedWidth(55f));
			refs.rot.InputField.interactable = !approximate;
			BindPoseField(refs.rot, text => { if (TryParse(text, out float value)) RigEditorScene.SetPartRotation(part, value); });
			rotShearRow.Add(UiLabel.Create(rotShearRow.ContentTransform, "Shr:", UiTheme.Default, 11).FixedWidth(32f));
			refs.shear = UiTextField.Create(rotShearRow.ContentTransform, FormatFloat(part.ShearDegrees));
			rotShearRow.Add(refs.shear.FixedWidth(55f));
			refs.shear.InputField.interactable = !approximate;
			BindPoseField(refs.shear, text => { if (TryParse(text, out float value)) RigEditorScene.SetPartShear(part, value); });
			LabHoverInfo.Bind(rotShearRow.GameObject, "animator.frame.PartRot");
			LabHoverInfo.Bind(refs.shear.GameObject, "animator.frame.PartShear");

			UiStack scaleRow = UiStack.Horizontal(row.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(scaleRow.FixedHeight(24f));
			scaleRow.Add(UiLabel.Create(scaleRow.ContentTransform, "Scl:", UiTheme.Default, 11).FixedWidth(35f));
			refs.scaleX = UiTextField.Create(scaleRow.ContentTransform, FormatFloat(part.ScaleX));
			scaleRow.Add(refs.scaleX.FixedWidth(55f));
			refs.scaleY = UiTextField.Create(scaleRow.ContentTransform, FormatFloat(part.ScaleY));
			scaleRow.Add(refs.scaleY.FixedWidth(55f));
			refs.scaleX.InputField.interactable = !approximate;
			refs.scaleY.InputField.interactable = !approximate;
			UnityAction<string> onScaleChanged = _ =>
			{
				if (TryParse(refs.scaleX.InputField.text, out float x) && TryParse(refs.scaleY.InputField.text, out float y))
				{
					RigEditorScene.SetPartScale(part, x, y);
				}
			};
			BindPoseField(refs.scaleX, onScaleChanged);
			BindPoseField(refs.scaleY, onScaleChanged);
			LabHoverInfo.Bind(scaleRow.GameObject, "animator.frame.PartScale");

			if (approximate)
			{
				UiLabel notice = UiLabel.Create(row.ContentTransform, "Read-only pose (degenerate matrix).", UiTheme.Default, 10, TextAnchor.UpperLeft);
				notice.SetColor(new Color(1f, 0.6f, 0.4f));
				row.Add(notice.FixedHeight(20f));
				row.Add(UiButton.Create(row.ContentTransform, "Convert to Editable", () => RigEditorScene.ConvertPartPoseToEditable(part), primary: false).FixedHeight(26f));
			}

			partFieldRefs[part.PartName] = refs;
		}

		/// <summary>Moves a part toward the front of the draw order (up arrow). MovePartLayer's ascending list means +1 moves toward the front; MoveFramePartOrder's descending list means -1 does, so both end up meaning "up = toward the front" here.</summary>
		private static void OnMoveFramePartUp(DraggablePart part, bool isRestPose)
		{
			if (isRestPose)
			{
				RigEditorScene.MovePartLayer(part, 1);
			}
			else
			{
				RigEditorScene.MoveFramePartOrder(part, -1);
			}
		}

		private static void OnMoveFramePartDown(DraggablePart part, bool isRestPose)
		{
			if (isRestPose)
			{
				RigEditorScene.MovePartLayer(part, -1);
			}
			else
			{
				RigEditorScene.MoveFramePartOrder(part, 1);
			}
		}

		private static void OnTogglePartExpanded(string partName)
		{
			if (!expandedPartNames.Remove(partName))
			{
				expandedPartNames.Add(partName);
			}
			Refresh();
		}

		private static void OnPartPivotChanged(string ignored)
		{
			DraggablePart part = RigEditorScene.SelectedPart;
			if (part == null)
			{
				return;
			}
			if (TryParse(partPivotXField.InputField.text, out float x) && TryParse(partPivotYField.InputField.text, out float y))
			{
				Vector2 deltaPosition = RigEditorScene.GetDeltaPositionForPivotOps(part);
				RigEditorScene.SetPivotWorldPosition(part, deltaPosition, new Vector2(x, y));
			}
		}

		private static void OnVisibleChanged(bool value)
		{
			DraggablePart part = RigEditorScene.SelectedPart;
			if (part != null)
			{
				RigEditorScene.ToggleVisibility(part);
			}
		}

		private static void OnReplaceClicked()
		{
			DraggablePart part = RigEditorScene.SelectedPart;
			if (part != null)
			{
				ReplacePartPickerPanel.Open(part);
			}
		}

		private static void OnMassRemoveClicked()
		{
			DraggablePart part = RigEditorScene.SelectedPart;
			if (part != null)
			{
				RigEditorScene.MassRemovePartFromClip(part);
			}
		}

		private static void OnChooseReferenceCharacterClicked()
		{
			MetaExoPickerPanel.Open(RigEditorScene.SetSelectedReferenceCharacter);
		}

		private static void OnReferenceAnimationChanged(int index)
		{
			ReferenceCharacter reference = RigEditorScene.SelectedReference;
			if (reference == null || reference.AnimationIndex == index)
			{
				return;
			}
			RigEditorScene.SetSelectedReferenceAnimation(index);
		}

		private static void OnReferenceTransformChanged(string unused)
		{
			TryParse(referencePosXField.InputField.text, out float x);
			TryParse(referencePosYField.InputField.text, out float y);
			TryParse(referenceRotField.InputField.text, out float rotation);
			RigEditorScene.SetSelectedReferencePosition(x, y);
			RigEditorScene.SetSelectedReferenceRotation(rotation);
		}

		private static void OnReferenceVisibleChanged(bool visible)
		{
			RigEditorScene.SetSelectedReferenceVisible(visible);
		}

		private static void OnReferenceOpacityChanged(string text)
		{
			if (TryParse(text, out float opacity))
			{
				RigEditorScene.SetSelectedReferenceOpacity(opacity);
			}
		}

		private static void OnDurationChanged(string text)
		{
			if (TryParse(text, out float seconds))
			{
				RigEditorScene.SetActiveFrameDuration(seconds);
			}
		}

		private static void OnRootMotionChanged(string text)
		{
			RigEditorScene.SetActiveFrameRootMotion(text);
		}

		/// <summary>Commits the Events combobox (dropdown pick, Enter, or focus loss) as a new tag on the active frame.</summary>
		private static void OnFrameEventComboCommitted(string value)
		{
			string eventName = (value ?? string.Empty).Trim();
			if (eventName.Length == 0)
			{
				return;
			}
			RigEditorScene.AddActiveFrameEvent(eventName);
			frameEventCombo.SetText(string.Empty);
		}

		/// <summary>Adds the Events combobox text as a tag. Duplicate of OnFrameEventComboCommitted for the Add button, which may fire after EndEdit already cleared the field.</summary>
		private static void OnAddFrameEventClicked()
		{
			OnFrameEventComboCommitted(frameEventCombo.InputField.text);
		}

		/// <summary>One authored event tag plus a remove button. Keyed by event name so Refresh during playback does not rebuild the row mid-click.</summary>
		private static UiElement BuildFrameEventRow(Transform parent, string eventName)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			row.Add(UiLabel.Create(row.ContentTransform, eventName).Grow());
			row.Add(UiButton.Create(row.ContentTransform, "x", () => RigEditorScene.RemoveActiveFrameEvent(eventName), primary: false).FixedWidth(28f));
			return row.FixedHeight(28f);
		}

		/// <summary>Commits the Attach points combobox as a new socket on the active frame.</summary>
		private static void OnFrameAttachComboCommitted(string value)
		{
			string name = (value ?? string.Empty).Trim();
			if (name.Length == 0)
			{
				return;
			}
			RigEditorScene.AddActiveFrameAttachPoint(name);
			frameAttachCombo.SetText(string.Empty);
		}

		/// <summary>Adds the Attach points combobox text as a socket. Duplicate of OnFrameAttachComboCommitted for the Add button.</summary>
		private static void OnAddFrameAttachClicked()
		{
			OnFrameAttachComboCommitted(frameAttachCombo.InputField.text);
		}

		/// <summary>Diffs the attach-point list by name, then refreshes X/Y/Rot text on reused rows in place.</summary>
		private static void RefreshAttachPointList()
		{
			List<AttachPointPose> attachPoints = RigEditorScene.GetActiveFrameAttachPoints();
			HashSet<string> liveNames = new HashSet<string>();
			foreach (AttachPointPose attach in attachPoints)
			{
				liveNames.Add(attach.Name);
			}
			List<string> stale = null;
			foreach (string key in attachFieldRefs.Keys)
			{
				if (!liveNames.Contains(key))
				{
					(stale ?? (stale = new List<string>())).Add(key);
				}
			}
			if (stale != null)
			{
				foreach (string key in stale)
				{
					attachFieldRefs.Remove(key);
				}
			}
			frameAttachPointsList.SetItems(attachPoints, attach => attach.Name, BuildAttachPointRow);
			foreach (AttachPointPose attach in attachPoints)
			{
				if (attachFieldRefs.TryGetValue(attach.Name, out AttachFieldRefs refs))
				{
					if (refs.x != null && !refs.x.InputField.isFocused) refs.x.SetText(FormatFloat(attach.Position.x));
					if (refs.y != null && !refs.y.InputField.isFocused) refs.y.SetText(FormatFloat(attach.Position.y));
					if (refs.rot != null && !refs.rot.InputField.isFocused) refs.rot.SetText(FormatFloat(attach.RotationDegrees));
				}
			}
		}

		/// <summary>One attach-point socket: name, remove, and editable X/Y/Rot. Keyed by name so playback Refresh does not rebuild the row mid-edit.</summary>
		private static UiElement BuildAttachPointRow(Transform parent, AttachPointPose attach)
		{
			string name = attach.Name;
			AttachFieldRefs refs = new AttachFieldRefs();
			UiStack column = UiStack.Vertical(parent, UiTheme.Default, spacing: 2f, padding: 0f);

			UiStack header = UiStack.Horizontal(column.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			column.Add(header.FixedHeight(24f));
			header.Add(UiLabel.Create(header.ContentTransform, name).Grow());
			header.Add(UiButton.Create(header.ContentTransform, "x", () => RigEditorScene.RemoveActiveFrameAttachPoint(name), primary: false).FixedWidth(28f));

			UiStack props = UiStack.Horizontal(column.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			column.Add(props.FixedHeight(24f));
			props.Add(UiLabel.Create(props.ContentTransform, "X:", UiTheme.Default, 11).FixedWidth(18f));
			refs.x = UiTextField.Create(props.ContentTransform, FormatFloat(attach.Position.x));
			props.Add(refs.x.FixedWidth(55f));
			props.Add(UiLabel.Create(props.ContentTransform, "Y:", UiTheme.Default, 11).FixedWidth(18f));
			refs.y = UiTextField.Create(props.ContentTransform, FormatFloat(attach.Position.y));
			props.Add(refs.y.FixedWidth(55f));
			props.Add(UiLabel.Create(props.ContentTransform, "Rot:", UiTheme.Default, 11).FixedWidth(28f));
			refs.rot = UiTextField.Create(props.ContentTransform, FormatFloat(attach.RotationDegrees));
			props.Add(refs.rot.FixedWidth(55f));

			UnityAction<string> commit = _ => CommitAttachPoint(name, refs);
			BindPoseField(refs.x, commit);
			BindPoseField(refs.y, commit);
			BindPoseField(refs.rot, commit);

			attachFieldRefs[name] = refs;
			return column.FixedHeight(52f);
		}

		/// <summary>Writes one attach-point row's X/Y/Rot back onto the active frame.</summary>
		private static void CommitAttachPoint(string name, AttachFieldRefs refs)
		{
			TryParse(refs.x.InputField.text, out float x);
			TryParse(refs.y.InputField.text, out float y);
			TryParse(refs.rot.InputField.text, out float rotation);
			RigEditorScene.SetActiveFrameAttachPoint(name, x, y, rotation);
		}

		/// <summary>Records the pose context when the field is focused and drops OnEndEdit if the clip or frame changed first.</summary>
		/// <remarks>
		/// Timeline / Node Tree clicks deselect the field after they switch context. A raw
		/// <c>onEndEdit</c> would then SetPart* into the new frame. See
		/// <see cref="RigEditorScene.PoseContextGeneration"/>.
		/// </remarks>
		private static void BindPoseField(UiTextField field, UnityAction<string> onCommit)
		{
			if (field == null || onCommit == null)
			{
				return;
			}

			int generation = -1;
			EventTrigger trigger = field.GameObject.GetComponent<EventTrigger>();
			if (trigger == null)
			{
				trigger = field.GameObject.AddComponent<EventTrigger>();
			}

			EventTrigger.Entry select = new EventTrigger.Entry { eventID = EventTriggerType.Select };
			select.callback.AddListener(_ => generation = RigEditorScene.PoseContextGeneration);
			trigger.triggers.Add(select);
			EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
			pointerDown.callback.AddListener(_ => generation = RigEditorScene.PoseContextGeneration);
			trigger.triggers.Add(pointerDown);

			field.OnEndEdit(text =>
			{
				if (generation >= 0 && generation != RigEditorScene.PoseContextGeneration)
				{
					return;
				}

				onCommit(text);
			});
		}

		private static bool TryParse(string text, out float value)
		{
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		private static string FormatFloat(float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}
	}
}
