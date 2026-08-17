using System.Collections.Generic;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab.Shell
{
	/// <summary>The dockable editor chrome shown while a project is open: menu bar, workspace tabs, UiDockSpace, status bar.</summary>
	/// <remarks>
	/// Phase 7 adds a File Tree left-dock tab (on-disk project folder). Bottom panels
	/// rebuild when CurrentSession.ProjectTypeId changes — Character hosts Timeline /
	/// Checklist / History; Ability Library hosts none and the zone collapses.
	/// File / Edit / View / Help stay as in Phase 6. Home is retired; name/id live on the status
	/// bar. Scene-transition workspaces call <see cref="WorkspaceRegistration.EnterViaSceneTransition"/>.
	/// Phase 8 reopens this shell (requested tab, same project) when a fight ends.
	/// Ability overhaul Phase 10 adds a hover-info strip above the status bar.
	/// </remarks>
	internal static class LabShell
	{
		private static UiStatusBar status;
		private static UiPanel hoverInfo;
		private static UiDockSpace dock;
		private static UiToolbar workspaceTabs;
		private static UiStack toolbarSlot;
		private static RectTransform viewportBody;
		private static UiDockPanel viewportPanel;
		private static Transform canvas;
		private static WorkspaceRegistration activeWorkspace;
		private static string lastWorkspaceTypeId;
		private static readonly List<BottomPanelRegistration> hostedBottomPanels = new List<BottomPanelRegistration>();

		/// <summary>The root canvas, used by Animator pickers and File dropdowns.</summary>
		internal static Transform Canvas => canvas;

		/// <summary>The currently visible in-shell workspace, or null before the first ActivateWorkspace.</summary>
		internal static WorkspaceRegistration ActiveWorkspace => activeWorkspace;

		/// <summary>Name of the active workspace, or empty.</summary>
		internal static string ActiveWorkspaceName => activeWorkspace != null ? activeWorkspace.Name : string.Empty;

		/// <summary>First workspace registered on the open project type, or empty when none is open.</summary>
		internal static string FirstWorkspaceName
		{
			get
			{
				ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
				ProjectTypeRegistration type = session != null
					? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
					: null;
				return type != null && type.Workspaces.Count > 0 ? type.Workspaces[0].Name : string.Empty;
			}
		}

		/// <summary>Drops widget refs after the lab scene is destroyed. Does not Unbind — those objects are already gone.</summary>
		internal static void ResetSession()
		{
			hostedBottomPanels.Clear();
			status = null;
			hoverInfo = null;
			LabHoverInfo.ResetSession();
			dock = null;
			workspaceTabs = null;
			toolbarSlot = null;
			viewportBody = null;
			viewportPanel = null;
			canvas = null;
			activeWorkspace = null;
			lastWorkspaceTypeId = null;
		}

		/// <summary>Builds the shell into the given screen root. canvas is the root canvas (for the File dropdown).</summary>
		internal static void Build(Transform screenRoot, Transform canvasRoot)
		{
			ResetSession();
			canvas = canvasRoot;
			UiTheme theme = UiTheme.Default;
			UiPanel root = UiPanel.Create(screenRoot, theme);
			MakeSeeThrough(root.GameObject);
			UiStack column = UiStack.Vertical(root.ContentParent, theme, spacing: 0f, padding: 0f);
			root.Add(column);

			UiToolbar menuBar = LabMenuBar.Build(column.ContentTransform, canvas, theme);
			column.Add(menuBar.FixedHeight(theme.ToolbarHeight));

			workspaceTabs = UiToolbar.Create(column.ContentTransform, theme);
			column.Add(workspaceTabs.FixedHeight(theme.ToolbarHeight));

			toolbarSlot = UiStack.Vertical(column.ContentTransform, theme, spacing: 0f, padding: 0f);
			column.Add(toolbarSlot.FixedHeight(theme.ToolbarHeight));
			toolbarSlot.Visible(false);

			dock = UiDockSpace.Create(column.ContentTransform, theme);
			MakeSeeThrough(dock.GameObject);
			column.Add(dock.Grow());

			hoverInfo = LabHoverInfo.Build(column.ContentTransform, theme);
			column.Add(hoverInfo.FixedHeight(56f));

			status = UiStatusBar.Create(column.ContentTransform, theme);
			column.Add(status.FixedHeight(theme.StatusBarHeight));

			UiDockPanel nodePanel = UiDockPanel.Create(dock.GameObject.transform, "node-tree", "Node Tree", theme, closable: false, pinnable: false);
			nodePanel.Add(NodeTreePanel.Build(nodePanel.ContentParent, canvas, theme));
			dock.AddPanel(nodePanel, DockZone.Left);

			UiDockPanel filePanel = UiDockPanel.Create(dock.GameObject.transform, "file-tree", "File Tree", theme, closable: false, pinnable: false);
			filePanel.Add(FileTreePanel.Build(filePanel.ContentParent, theme));
			dock.AddPanel(filePanel, DockZone.Left);

			viewportPanel = UiDockPanel.Create(dock.GameObject.transform, "viewport", "Viewport", theme, closable: false, pinnable: false);
			MakeSeeThrough(viewportPanel.GameObject);

			GameObject viewportBodyObject = new GameObject("ViewportBody", typeof(RectTransform));
			viewportBodyObject.transform.SetParent(viewportPanel.ContentParent, false);
			viewportBody = viewportBodyObject.GetComponent<RectTransform>();
			viewportBody.anchorMin = Vector2.zero;
			viewportBody.anchorMax = Vector2.one;
			viewportBody.offsetMin = Vector2.zero;
			viewportBody.offsetMax = Vector2.zero;
			dock.AddPanel(viewportPanel, DockZone.Center);

			UiDockPanel inspector = UiDockPanel.Create(dock.GameObject.transform, "inspector", "Inspector", theme, closable: false, pinnable: false);
			inspector.Add(InspectorDock.Build(inspector.ContentParent, theme));
			dock.AddPanel(inspector, DockZone.Right);

			RebuildWorkspaceTabs();
			Refresh();
		}

		/// <summary>Selects a registered bottom panel by display name. Returns false if that tab is not in the dock.</summary>
		internal static bool FocusBottomPanel(string name)
		{
			return FocusPanel(string.IsNullOrEmpty(name) ? null : name.ToLowerInvariant());
		}

		/// <summary>Selects a dock panel by id (e.g. "file-tree", "timeline").</summary>
		internal static bool FocusPanel(string panelId)
		{
			if (dock == null || string.IsNullOrEmpty(panelId))
			{
				return false;
			}

			bool selected = dock.SelectPanel(panelId);
			if (selected)
			{
				RefreshHostedBottomPanel(panelId);
			}

			return selected;
		}

		/// <summary>Drops the active workspace (cameras, viewport, toolbar) after the session is cleared.</summary>
		internal static void UnloadProject()
		{
			if (activeWorkspace != null)
			{
				activeWorkspace.OnDeactivated?.Invoke();
				activeWorkspace = null;
			}

			lastWorkspaceTypeId = null;
			ClearViewport();
			if (toolbarSlot != null)
			{
				toolbarSlot.Clear();
				toolbarSlot.Visible(false);
			}

			Refresh();
		}

		/// <summary>Updates the status bar, Node Tree, File Tree, and inspector from the current session.</summary>
		internal static void Refresh()
		{
			RebuildWorkspaceTabs();
			NodeTreePanel.Refresh();
			FileTreePanel.Refresh();
			RefreshHostedBottomPanels();
			RefreshStatus();
			ShowSelection(LokrLabApi.LokrLabApi.Selection.All);
			LabMenuBar.Refresh();
		}

		/// <summary>Updates the status-bar dirty marker without rebuilding docks.</summary>
		internal static void RefreshDirtyIndicator()
		{
			RefreshStatus();
		}

		/// <summary>Writes a left-status message (Add Node failures, etc.).</summary>
		internal static void SetStatus(string message)
		{
			if (status != null)
			{
				status.SetText(message ?? string.Empty);
			}
		}

		/// <summary>Switches the in-shell workspace, or drives a scene-transition workstation when <c>RequiresSceneTransition</c> returns true.</summary>
		internal static void ActivateWorkspace(string name)
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;
			WorkspaceRegistration workspace = type != null ? type.FindWorkspace(name) : null;
			if (workspace == null)
			{
				SetStatus("No workspace named '" + name + "'.");
				return;
			}

			if (workspace.RequiresSceneTransition != null && workspace.RequiresSceneTransition(session))
			{
				workspace.EnterViaSceneTransition?.Invoke(session);
				return;
			}

			if (activeWorkspace != null && activeWorkspace.Name == name && viewportBody != null
				&& viewportBody.childCount > 0)
			{
				return;
			}

			if (activeWorkspace != null && activeWorkspace != workspace)
			{
				activeWorkspace.OnDeactivated?.Invoke();
			}

			activeWorkspace = workspace;
			if (workspaceTabs != null)
			{
				workspaceTabs.SetActive("ws-" + name);
			}

			toolbarSlot.Clear();
			if (workspace.BuildToolbar != null)
			{
				workspace.BuildToolbar(toolbarSlot.ContentTransform);
				toolbarSlot.Visible(true);
			}
			else
			{
				toolbarSlot.Visible(false);
			}

			ClearViewport();
			if (workspace.BuildViewport != null)
			{
				workspace.BuildViewport(viewportBody);
			}

			workspace.OnActivated?.Invoke();
			InspectorDock.Invalidate();
			ShowSelection(LokrLabApi.LokrLabApi.Selection.All);
			RefreshStatus();
			AutoFocusBottomPanel();
			LabMenuBar.Refresh();
		}

		/// <summary>Dispatches inspector drawers, type-specific selection sync, and the status line.</summary>
		internal static void ShowSelection(IReadOnlyList<LabNode> nodes)
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;
			type?.OnSelectionChanged?.Invoke(nodes);
			InspectorDock.Refresh();
			RefreshStatus();

			if (status == null || nodes == null || nodes.Count == 0)
			{
				return;
			}

			LabNode primary = nodes[0];
			string extra = nodes.Count > 1 ? " (+" + (nodes.Count - 1) + " more)" : "";
			status.SetText("Selected " + primary.Kind + " '" + primary.DisplayName + "'" + extra);
		}

		private static void RebuildWorkspaceTabs()
		{
			if (workspaceTabs == null)
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;
			string typeId = type != null ? type.Id : string.Empty;
			if (typeId == lastWorkspaceTypeId && workspaceTabs.TryGetButton("ws-" + (activeWorkspace != null ? activeWorkspace.Name : "Properties"), out _))
			{
				if (activeWorkspace != null)
				{
					workspaceTabs.SetActive("ws-" + activeWorkspace.Name);
				}
				return;
			}

			activeWorkspace?.OnDeactivated?.Invoke();

			workspaceTabs.Clear();
			activeWorkspace = null;
			lastWorkspaceTypeId = typeId;
			RebuildBottomPanels(type);
			if (type == null)
			{
				return;
			}

			foreach (WorkspaceRegistration workspace in type.Workspaces)
			{
				if (workspace.BuildViewport == null && workspace.BuildToolbar == null
					&& workspace.RequiresSceneTransition == null)
				{
					continue;
				}

				string workspaceName = workspace.Name;
				workspaceTabs.AddButton("ws-" + workspaceName, workspaceName, () => ActivateWorkspace(workspaceName))
					.FixedWidth(120f);
			}

			if (activeWorkspace != null)
			{
				workspaceTabs.SetActive("ws-" + activeWorkspace.Name);
			}
		}

		private static void RefreshStatus()
		{
			if (status == null)
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				status.SetText("No project open.");
				status.SetRightText("");
				return;
			}

			string dirty = session.IsDirty ? " *" : "";
			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId);
			string typeName = type != null ? type.DisplayName : session.ProjectTypeId;
			string workspaceName = activeWorkspace != null ? activeWorkspace.Name : "";
			status.SetRightText(typeName + " / " + session.Id + (workspaceName.Length > 0 ? " / " + workspaceName : "") + dirty);
			if (LokrLabApi.LokrLabApi.Selection.Primary == null)
			{
				status.SetText(session.DisplayName + dirty);
			}
		}

		/// <summary>Replaces the bottom dock with the open project type's panels only. An empty list collapses the zone.</summary>
		private static void RebuildBottomPanels(ProjectTypeRegistration type)
		{
			if (dock == null)
			{
				return;
			}

			for (int i = 0; i < hostedBottomPanels.Count; i++)
			{
				BottomPanelRegistration previous = hostedBottomPanels[i];
				previous.Unbind?.Invoke();
				UiDockPanel removed = dock.RemovePanel(previous.Name.ToLowerInvariant());
				if (removed != null)
				{
					Object.Destroy(removed.GameObject);
				}
			}

			hostedBottomPanels.Clear();
			if (type == null)
			{
				return;
			}

			UiTheme theme = UiTheme.Default;
			foreach (BottomPanelRegistration registration in type.BottomPanels)
			{
				string id = registration.Name.ToLowerInvariant();
				UiDockPanel panel = UiDockPanel.Create(dock.GameObject.transform, id, registration.Name, theme,
					closable: false, pinnable: false);
				registration.Builder(panel.ContentParent);
				dock.AddPanel(panel, DockZone.Bottom);
				hostedBottomPanels.Add(registration);
			}
		}

		/// <summary>Clears a widget Image and disables raycasts so Camera.rect can show through the overlay.</summary>
		private static void MakeSeeThrough(GameObject gameObject)
		{
			if (gameObject == null)
			{
				return;
			}

			Image image = gameObject.GetComponent<Image>();
			if (image != null)
			{
				image.color = Color.clear;
				image.raycastTarget = false;
			}
		}

		/// <summary>Destroys every workspace-owned child of the center viewport host.</summary>
		private static void ClearViewport()
		{
			if (viewportBody == null)
			{
				return;
			}

			for (int i = viewportBody.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(viewportBody.GetChild(i).gameObject);
			}
		}

		private static void RefreshHostedBottomPanels()
		{
			for (int i = 0; i < hostedBottomPanels.Count; i++)
			{
				hostedBottomPanels[i].Refresh?.Invoke();
			}
		}

		private static void RefreshHostedBottomPanel(string panelId)
		{
			for (int i = 0; i < hostedBottomPanels.Count; i++)
			{
				BottomPanelRegistration registration = hostedBottomPanels[i];
				if (registration.Name.ToLowerInvariant() == panelId)
				{
					registration.Refresh?.Invoke();
					return;
				}
			}
		}

		private static void AutoFocusBottomPanel()
		{
			if (dock == null)
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;
			if (type == null)
			{
				return;
			}

			foreach (BottomPanelRegistration registration in type.BottomPanels)
			{
				if (registration.IsRelevant != null
					&& registration.IsRelevant(activeWorkspace, LokrLabApi.LokrLabApi.Selection))
				{
					dock.SelectPanel(registration.Name.ToLowerInvariant());
					return;
				}
			}
		}
	}
}
