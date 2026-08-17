using System.Collections.Generic;
using SimpleUI;
using UnityEngine;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor
{
	/// <summary>Tool-mode buttons (Select + every AnimatorToolRegistry.Tools entry), session-wide Mass Edit, Preview overlay toggle, Add Reference, a status label, and Undo/Redo/History -- the thin strip directly below MenuBarPanel.</summary>
	/// <remarks>Mass Edit lives here rather than in InspectorPanel's Part section because it is a global editing mode (applies to every multi-selected part across every frame of the active clip), not a property of whichever part the Inspector happens to be showing.</remarks>
	internal static class ToolbarPanel
	{
		private static readonly Dictionary<string, UiButton> modeButtons = new Dictionary<string, UiButton>();
		private static UiToggle massEditToggle;
		private static UiToggle previewToggle;

		/// <summary>Builds the toolbar, returning the status label for RigEditorScene.SetStatus to write to.</summary>
		internal static UiLabel Build(Transform screenRoot)
		{
			UiPanel panel = UiPanel.Create(screenRoot, UiTheme.Default, region: EditorLayout.ToolbarRegion);
			UiStack row = UiStack.Horizontal(panel.ContentParent, UiTheme.Default, spacing: 8f, padding: 8f);
			panel.Add(row);
			return FillRow(row);
		}

		/// <summary>Builds the toolbar into a layout parent (shell workspace toolbar slot) instead of an absolute canvas region.</summary>
		internal static UiLabel BuildInto(Transform parent)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 8f, padding: 4f);
			return FillRow(row);
		}

		private static UiLabel FillRow(UiStack row)
		{

			modeButtons.Clear();
			AnimatorToolRegistry.RegisterDefaults();

			AddModeButton(row, RigEditorScene.SelectToolName, "Select (Q)");
			foreach (IAnimatorTool tool in AnimatorToolRegistry.Tools)
			{
				AddModeButton(row, tool.Name, string.Format("{0} ({1})", tool.Name, tool.Hotkey));
			}

			massEditToggle = UiToggle.Create(row.ContentTransform, "Mass Edit", false);
			massEditToggle.OnValueChanged(OnMassEditChanged);
			row.Add(massEditToggle.FixedWidth(120f));
			LabHoverInfo.Bind(massEditToggle.GameObject, "animator.toolbar.MassEdit");

			previewToggle = UiToggle.Create(row.ContentTransform, "Preview", AnimatorWorkspace.PreviewEnabled);
			previewToggle.OnValueChanged(AnimatorWorkspace.SetPreviewVisible);
			row.Add(previewToggle.FixedWidth(100f));
			LabHoverInfo.Bind(previewToggle.GameObject, "animator.toolbar.Preview");

			UiLabel statusLabel = UiLabel.Create(row.ContentTransform,
				"Select a part in the Scene Tree, or use the File menu to Load a rig.",
				UiTheme.Default, fontSize: 13, alignment: TextAnchor.MiddleCenter);
			statusLabel.Grow();
			row.Add(statusLabel);

			UiButton addReference = UiButton.Create(row.ContentTransform, "Add Reference", () => RigEditorScene.AddReference(), primary: false);
			row.Add(addReference.FixedWidth(130f));
			LabHoverInfo.Bind(addReference.GameObject, "animator.toolbar.AddReference");
			UiButton history = UiButton.Create(row.ContentTransform, "History", EditHistoryPanel.Open, primary: false);
			row.Add(history.FixedWidth(64f));
			LabHoverInfo.Bind(history.GameObject, "animator.toolbar.History");
			UiButton undo = UiButton.Create(row.ContentTransform, "Undo", AnimatorHistory.Undo, primary: false);
			row.Add(undo.FixedWidth(64f));
			LabHoverInfo.Bind(undo.GameObject, "animator.toolbar.Undo");
			UiButton redo = UiButton.Create(row.ContentTransform, "Redo", AnimatorHistory.Redo, primary: false);
			row.Add(redo.FixedWidth(64f));
			LabHoverInfo.Bind(redo.GameObject, "animator.toolbar.Undo");

			RefreshModeButtons();
			RefreshMassEditToggle();
			return statusLabel;
		}

		private static void AddModeButton(UiStack row, string toolName, string label)
		{
			UiButton button = UiButton.Create(row.ContentTransform, label, () => RigEditorScene.SetTool(toolName), primary: false).FixedWidth(140f);
			row.Add(button);
			modeButtons[toolName] = button;
			LabHoverInfo.Bind(button.GameObject, ToolbarHoverKey(toolName));
		}

		/// <summary>Maps a tool display name to its hover-info key.</summary>
		private static string ToolbarHoverKey(string toolName)
		{
			if (toolName == "Scale XY")
			{
				return "animator.toolbar.ScaleXY";
			}

			return "animator.toolbar." + toolName;
		}

		/// <summary>Highlights whichever mode button matches RigEditorScene.CurrentToolName.</summary>
		internal static void RefreshModeButtons()
		{
			foreach (KeyValuePair<string, UiButton> entry in modeButtons)
			{
				entry.Value.SetColor(entry.Key == RigEditorScene.CurrentToolName ? UiTheme.Default.AccentColor : UiTheme.Default.ButtonColor);
			}
		}

		/// <summary>Syncs the Preview toggle to the remembered overlay visibility without re-firing OnValueChanged.</summary>
		internal static void RefreshPreviewToggle()
		{
			if (previewToggle != null)
			{
				previewToggle.SetValueSilently(AnimatorWorkspace.PreviewEnabled);
			}
		}

		/// <summary>Syncs the Mass Edit toggle to RigEditorScene.MassEditEnabled without re-firing OnValueChanged.</summary>
		internal static void RefreshMassEditToggle()
		{
			if (massEditToggle != null)
			{
				massEditToggle.SetValueSilently(RigEditorScene.MassEditEnabled);
			}
		}

		private static void OnMassEditChanged(bool value)
		{
			RigEditorScene.SetMassEditEnabled(value);
		}
	}
}
