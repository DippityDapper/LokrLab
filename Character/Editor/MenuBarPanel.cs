using System;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor
{
	/// <summary>Very top strip: File / Edit / Help, each toggling a dropdown panel directly below (only one open at a time).</summary>
	/// <remarks>A lightweight hand-built "menu" (toggle-a-panel), not a real dropdown widget. Every dropdown is a flat list of buttons; File's buttons that need input (Save, Import, Slice Atlas) instead open a small dedicated popup, closing the File dropdown itself first so only one thing is ever open at a time. Edit also exposes frame copy/paste/override/reorder, matching AnimationTimelinePanel and InspectorPanel's Frame section.</remarks>
	internal static class MenuBarPanel
	{
		private static UiPanel fileDropdown;
		private static UiPanel editDropdown;
		private static UiPanel helpDropdown;

		/// <summary>Shared by Save/Import -- both are "one field, an optional Browse/Choose button, Confirm/Cancel" in shape, just with different titles/callbacks. Reused rather than two separate popups, since only one can ever be open at once.</summary>
		private static UiModal singleFieldModal;
		private static UiLabel singleFieldTitleLabel;
		private static UiTextField singleFieldInput;
		private static UiButton singleFieldSideButton;
		private static UiButton singleFieldConfirmButton;
		private static Action singleFieldSideAction;
		private static Action<string> singleFieldConfirmAction;

		/// <summary>Slice Atlas needs three fields (path, rows, cols), so it gets its own small popup rather than the single-field shape above.</summary>
		private static UiModal atlasModal;
		private static UiTextField atlasPathInput;
		private static UiTextField atlasRowsInput;
		private static UiTextField atlasColsInput;

		/// <summary>Builds the menu bar, its three dropdowns (hidden), and the shared popups.</summary>
		internal static void Build(Transform canvas, Font font)
		{
			UiPanel bar = UiPanel.Create(canvas, UiTheme.Default, region: EditorLayout.MenuBarRegion);
			UiStack row = UiStack.Horizontal(bar.ContentParent, UiTheme.Default, spacing: 8f, padding: 4f);
			bar.Add(row);

			row.Add(UiButton.Create(row.ContentTransform, "File", ToggleFile, primary: false).FixedWidth(90f));
			row.Add(UiButton.Create(row.ContentTransform, "Edit", ToggleEdit, primary: false).FixedWidth(90f));
			row.Add(UiButton.Create(row.ContentTransform, "Help", ToggleHelp, primary: false).FixedWidth(90f));

			UiLabel spacer = UiLabel.Create(row.ContentTransform, string.Empty);
			spacer.Grow();
			row.Add(spacer);

			row.Add(UiButton.Create(row.ContentTransform, "‹ Home", Lab.SwitchToHome, primary: false).FixedWidth(110f));

			fileDropdown = BuildFileDropdown(canvas);
			editDropdown = BuildEditDropdown(canvas);
			helpDropdown = BuildHelpDropdown(canvas);

			fileDropdown.Visible(false);
			editDropdown.Visible(false);
			helpDropdown.Visible(false);

			EnsurePopups(canvas);
		}

		/// <summary>Drops dropdown and modal refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			fileDropdown = null;
			editDropdown = null;
			helpDropdown = null;
			singleFieldModal = null;
			singleFieldTitleLabel = null;
			singleFieldInput = null;
			singleFieldSideButton = null;
			singleFieldConfirmButton = null;
			singleFieldSideAction = null;
			singleFieldConfirmAction = null;
			atlasModal = null;
			atlasPathInput = null;
			atlasRowsInput = null;
			atlasColsInput = null;
		}

		/// <summary>Builds the Save/Import/Atlas modals onto canvas if they are missing or Unity-destroyed.</summary>
		/// <remarks>UiModal is a C# wrapper, not a UnityEngine.Object. Close Lab destroys the GameObjects; C# refs stay non-null, so a plain null check skips rebuild and Slice Atlas Show NREs on the dead backdrop.</remarks>
		internal static void EnsurePopups(Transform canvas)
		{
			if (canvas == null)
			{
				return;
			}

			FileBrowserPanel.EnsureBuilt(canvas);
			if (!IsLive(singleFieldModal))
			{
				BuildSingleFieldPopup(canvas);
			}
			if (!IsLive(atlasModal))
			{
				BuildAtlasPopup(canvas);
			}
		}

		/// <summary>Opens the Save Rig folder prompt. Used by the shell File menu when the Animator workspace is active.</summary>
		internal static void PromptSave()
		{
			OnSaveMenuClicked();
		}

		/// <summary>Opens the Import Character prompt.</summary>
		internal static void PromptImport()
		{
			OnImportMenuClicked();
		}

		/// <summary>Opens the Slice Atlas prompt.</summary>
		internal static void PromptSliceAtlas()
		{
			OnAtlasMenuClicked();
		}

		private static void ToggleFile()
		{
			SetOnly(fileDropdown, !fileDropdown.GameObject.activeSelf);
		}

		private static void ToggleEdit()
		{
			SetOnly(editDropdown, !editDropdown.GameObject.activeSelf);
		}

		private static void ToggleHelp()
		{
			SetOnly(helpDropdown, !helpDropdown.GameObject.activeSelf);
		}

		private static void CloseAllDropdowns()
		{
			if (fileDropdown != null)
			{
				fileDropdown.Visible(false);
			}
			if (editDropdown != null)
			{
				editDropdown.Visible(false);
			}
			if (helpDropdown != null)
			{
				helpDropdown.Visible(false);
			}
		}

		private static void SetOnly(UiPanel target, bool active)
		{
			CloseAllDropdowns();
			target.Visible(active);
		}

		private static UiPanel BuildDropdownPanel(Transform canvas, out UiStack content)
		{
			UiPanel panel = UiPanel.Create(canvas, UiTheme.Default, region: EditorLayout.MenuDropdownRegion);
			panel.GameObject.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
			content = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 4f, padding: 10f);
			panel.Add(content);
			return panel;
		}

		/// <summary>Builds the File dropdown (buttons only). Load/Recent are gone -- the Load workstation is the only place a character/rig folder gets picked now, and the Animator is always entered already pointed at one; Save writes back to that same folder.</summary>
		private static UiPanel BuildFileDropdown(Transform canvas)
		{
			UiPanel panel = BuildDropdownPanel(canvas, out UiStack content);
			content.Add(UiButton.Create(content.ContentTransform, "Save...", OnSaveMenuClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Import...", OnImportMenuClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Slice Atlas...", OnAtlasMenuClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Refresh Preview", OnRefreshPreviewMenuClicked, primary: false).FixedHeight(32f));
			return panel;
		}

		private static void OnSaveMenuClicked()
		{
			CloseAllDropdowns();
			OpenSingleFieldPopup("Save Rig — folder:", RigEditorScene.CurrentFolder, "Browse...",
				() => FileBrowserPanel.OpenForFolder(singleFieldInput.InputField.text, path => singleFieldInput.SetText(path)),
				"Save", RigEditorScene.OnSaveClicked);
		}

		private static void OnHistoryMenuClicked()
		{
			CloseAllDropdowns();
			EditHistoryPanel.Open();
		}

		/// <summary>Opens the Import popup. "Choose..." (MetaExoPickerPanel) sources from CharacterAPI.KnownUnitDefinitions, an asset-bundle lookup key rather than a real file, so it's a convenience list rather than a Browse dialog; typing an id directly still works either way.</summary>
		private static void OnImportMenuClicked()
		{
			CloseAllDropdowns();
			OpenSingleFieldPopup("Import Character — metaExo id:", RigEditorScene.CurrentImportId, "Choose...",
				() => MetaExoPickerPanel.Open(id => singleFieldInput.SetText(id)),
				"Import", RigEditorScene.OnImportClicked);
		}

		private static void OnRefreshPreviewMenuClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.RebuildPreview();
		}

		private static void BuildSingleFieldPopup(Transform canvas)
		{
			singleFieldModal = UiModal.Create(canvas, UiTheme.Default, null, 700f, 260f);
			UiStack content = UiStack.Vertical(singleFieldModal.ContentParent, UiTheme.Default, spacing: 10f, padding: 14f);
			singleFieldModal.Add(content);

			singleFieldTitleLabel = UiLabel.Create(content.ContentTransform, "-", UiTheme.Default, 16);
			content.Add(singleFieldTitleLabel.FixedHeight(28f));

			UiStack fieldRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(fieldRow.FixedHeight(34f));
			singleFieldInput = UiTextField.Create(fieldRow.ContentTransform);
			singleFieldInput.Grow();
			fieldRow.Add(singleFieldInput);
			singleFieldSideButton = UiButton.Create(fieldRow.ContentTransform, "Browse...", OnSingleFieldSideButtonClicked, primary: false);
			fieldRow.Add(singleFieldSideButton.FixedWidth(120f));

			UiStack buttonsRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(buttonsRow.FixedHeight(36f));
			singleFieldConfirmButton = UiButton.Create(buttonsRow.ContentTransform, "OK", OnSingleFieldConfirmClicked, primary: false);
			buttonsRow.Add(singleFieldConfirmButton.FixedWidth(150f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Cancel", singleFieldModal.Hide, primary: false).FixedWidth(110f));
		}

		private static void OpenSingleFieldPopup(string title, string initialValue, string sideButtonLabel,
			Action sideAction, string confirmLabel, Action<string> onConfirm)
		{
			EnsurePopups(Lab.Canvas);
			if (!IsLive(singleFieldModal))
			{
				return;
			}

			singleFieldTitleLabel.SetText(title);
			singleFieldInput.SetText(initialValue);
			singleFieldSideButton.Visible(sideAction != null);
			if (sideAction != null)
			{
				singleFieldSideButton.SetLabel(sideButtonLabel);
			}
			singleFieldSideAction = sideAction;
			singleFieldConfirmButton.SetLabel(confirmLabel);
			singleFieldConfirmAction = onConfirm;
			singleFieldModal.Show();
		}

		private static void OnSingleFieldSideButtonClicked()
		{
			singleFieldSideAction?.Invoke();
		}

		private static void OnSingleFieldConfirmClicked()
		{
			string value = singleFieldInput.InputField.text;
			singleFieldModal.Hide();
			singleFieldConfirmAction?.Invoke(value);
		}

		/// <summary>Builds the Slice Atlas popup (path + rows + cols), which also hosts "Pick Islands..." (IslandAtlasPickerPanel) for non-uniform atlases, reusing this popup's path field/Browse button rather than a second file-picking UI.</summary>
		private static void BuildAtlasPopup(Transform canvas)
		{
			atlasModal = UiModal.Create(canvas, UiTheme.Default, "Slice Atlas Into Folder", 700f, 260f);
			UiStack content = UiStack.Vertical(atlasModal.ContentParent, UiTheme.Default, spacing: 10f, padding: 14f);
			atlasModal.Add(content);

			UiStack pathRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(pathRow.FixedHeight(32f));
			pathRow.Add(UiLabel.Create(pathRow.ContentTransform, "Atlas file:").FixedWidth(90f));
			atlasPathInput = UiTextField.Create(pathRow.ContentTransform);
			atlasPathInput.Grow();
			pathRow.Add(atlasPathInput);
			pathRow.Add(UiButton.Create(pathRow.ContentTransform, "Browse...", OnAtlasBrowseClicked, primary: false).FixedWidth(110f));

			UiStack rowsColsRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(rowsColsRow.FixedHeight(32f));
			rowsColsRow.Add(UiLabel.Create(rowsColsRow.ContentTransform, "Rows:").FixedWidth(50f));
			atlasRowsInput = UiTextField.Create(rowsColsRow.ContentTransform, "1");
			rowsColsRow.Add(atlasRowsInput.FixedWidth(60f));
			rowsColsRow.Add(UiLabel.Create(rowsColsRow.ContentTransform, "Cols:").FixedWidth(50f));
			atlasColsInput = UiTextField.Create(rowsColsRow.ContentTransform, "1");
			rowsColsRow.Add(atlasColsInput.FixedWidth(60f));
			LabHoverInfo.Bind(rowsColsRow.GameObject, "animator.file.AtlasGrid");

			UiStack buttonsRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(buttonsRow.FixedHeight(36f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Slice", OnAtlasConfirmClicked, primary: false).FixedWidth(120f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Cancel", atlasModal.Hide, primary: false).FixedWidth(100f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Pick Islands...", OnPickIslandsClicked, primary: false).FixedWidth(150f));
			LabHoverInfo.Bind(buttonsRow.GameObject, "animator.file.PickIslands");
		}

		private static void OnAtlasMenuClicked()
		{
			CloseAllDropdowns();
			EnsurePopups(Lab.Canvas);
			if (!IsLive(atlasModal))
			{
				return;
			}

			atlasPathInput.SetText(RigEditorScene.CurrentAtlasPath);
			atlasRowsInput.SetText(RigEditorScene.CurrentAtlasRows);
			atlasColsInput.SetText(RigEditorScene.CurrentAtlasCols);
			atlasModal.Show();
		}

		private static void OnAtlasBrowseClicked()
		{
			FileBrowserPanel.OpenForFile("Select an atlas image", atlasPathInput.InputField.text,
				new[] { ".png", ".jpg", ".jpeg" }, selected => atlasPathInput.SetText(selected));
		}

		private static void OnPickIslandsClicked()
		{
			string path = atlasPathInput.InputField.text;
			atlasModal.Hide();
			IslandAtlasPickerPanel.Open(path);
		}

		private static void OnAtlasConfirmClicked()
		{
			string path = atlasPathInput.InputField.text;
			string rows = atlasRowsInput.InputField.text;
			string cols = atlasColsInput.InputField.text;
			atlasModal.Hide();
			RigEditorScene.OnSliceAtlasClicked(path, rows, cols);
		}

		private static UiPanel BuildEditDropdown(Transform canvas)
		{
			UiPanel panel = BuildDropdownPanel(canvas, out UiStack content);
			content.Add(UiButton.Create(content.ContentTransform, "Add Reference", OnAddReferenceClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Copy Frame", OnCopyFrameClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Paste Frame as New", OnPasteFrameClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Override Frame", OnOverrideFrameClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Move Frame Left", OnMoveFrameLeftClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Move Frame Right", OnMoveFrameRightClicked, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Undo", AnimatorHistory.Undo, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "Redo", AnimatorHistory.Redo, primary: false).FixedHeight(32f));
			content.Add(UiButton.Create(content.ContentTransform, "History...", OnHistoryMenuClicked, primary: false).FixedHeight(32f));
			return panel;
		}

		private static void OnAddReferenceClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.AddReference();
		}

		private static void OnCopyFrameClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.CopyActiveFrame();
		}

		private static void OnPasteFrameClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.PasteFrameAsNew();
		}

		private static void OnOverrideFrameClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.OverrideActiveFrame();
		}

		private static void OnMoveFrameLeftClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.MoveActiveFrame(-1);
		}

		private static void OnMoveFrameRightClicked()
		{
			CloseAllDropdowns();
			RigEditorScene.MoveActiveFrame(1);
		}

		private static UiPanel BuildHelpDropdown(Transform canvas)
		{
			UiPanel panel = BuildDropdownPanel(canvas, out UiStack content);
			content.Add(UiLabel.Create(content.ContentTransform,
				"LoKR Character Lab — Rig Editor. See docs/ in the plugin's repo for details on every workflow (import, animate, pivots, attach points, undo).",
				UiTheme.Default, alignment: TextAnchor.UpperLeft).FixedHeight(70f));
			return panel;
		}

		/// <summary>True when the widget still exists in the current lab scene.</summary>
		private static bool IsLive(UiElement element)
		{
			return element != null && element.GameObject != null;
		}
	}
}
