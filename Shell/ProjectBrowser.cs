using System;
using System.Collections.Generic;
using System.IO;
using LokrLabApi;
using LokrModAPI;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Shell
{
	/// <summary>The shell's empty state: scan-and-open any registered project type, or create a new one.</summary>
	/// <remarks>
	/// Replaces Load as the first screen. The list is grouped by project type; Show toggles hide
	/// types (persisted in HiddenProjectTypes). Recents order rows inside each section. The Recent
	/// section lists the five newest surviving folders; '-' unpins one from recents without
	/// deleting the project. A directory scan of every registered FolderRoot plus
	/// Mods/*/Characters/ guarantees folders copied in by hand still appear. "New Project" always
	/// opens a wizard: type dropdown plus that type's optional create sheet. File lives on this
	/// screen too so Import Legacy Pack does not require opening or creating a character first.
	/// </remarks>
	internal static class ProjectBrowser
	{
		private const int RecentMenuLimit = 5;

		private static Transform screenRoot;
		private static UiList<BrowserRow> projectList;
		private static UiStack filterRow;
		private static string filterSignature;
		private static UiButton importButton;
		private static UiLabel emptyLabel;
		private static UiLabel statusLabel;
		private static UiModal createModal;
		private static UiDropdown typeDropdown;
		private static UiStack sheetHost;
		private static UiLabel createErrorLabel;
		private static List<ProjectTypeRegistration> createTypes;
		private static UiModal deleteModal;
		private static UiLabel deleteMessageLabel;
		private static BrowserRow pendingDelete;

		/// <summary>Drops Project Browser widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			screenRoot = null;
			projectList = null;
			filterRow = null;
			filterSignature = null;
			importButton = null;
			emptyLabel = null;
			statusLabel = null;
			createModal = null;
			typeDropdown = null;
			sheetHost = null;
			createErrorLabel = null;
			createTypes = null;
			deleteModal = null;
			deleteMessageLabel = null;
			pendingDelete = null;
		}

		/// <summary>Builds the browser into the given screen root.</summary>
		internal static void Build(Transform screenRoot, Transform canvas)
		{
			ResetSession();
			ProjectBrowser.screenRoot = screenRoot;
			createModal = null;
			typeDropdown = null;
			sheetHost = null;
			createErrorLabel = null;
			createTypes = null;
			deleteModal = null;
			deleteMessageLabel = null;
			pendingDelete = null;
			UiTheme theme = UiTheme.Default;
			UiPanel root = UiPanel.Create(screenRoot, theme, "Projects");
			UiStack column = UiStack.Vertical(root.ContentParent, theme, spacing: 8f, padding: 16f);
			root.Add(column);

			if (canvas != null)
			{
				UiToolbar menuBar = LabMenuBar.Build(column.ContentTransform, canvas, theme);
				column.Add(menuBar.FixedHeight(theme.ToolbarHeight));
			}

			column.Add(UiLabel.Create(column.ContentTransform, "LokrLab", theme, 22, TextAnchor.MiddleLeft).FixedHeight(32f));
			column.Add(UiLabel.Create(column.ContentTransform, "Open a project, create a new one, or File → Import Legacy Pack. Legacy Home / Properties / Animator stay reachable from File once a project is open.", theme, 13).FixedHeight(36f));

			UiStack actions = UiStack.Horizontal(column.ContentTransform, theme, spacing: 8f, padding: 0f);
			actions.Add(UiButton.Create(actions.ContentTransform, "+ New Project", OnNewProject, theme, primary: true).FixedWidth(180f).FixedHeight(32f));
			importButton = UiButton.Create(actions.ContentTransform, "Import Legacy Pack...", OnImportLegacy, theme, primary: false);
			actions.Add(importButton.FixedWidth(200f).FixedHeight(32f));
			actions.Add(UiButton.Create(actions.ContentTransform, "Close Lab", CharacterLabScene.Close, theme, primary: false).FixedWidth(120f).FixedHeight(32f));
			column.Add(actions.FixedHeight(36f));

			filterRow = UiStack.Horizontal(column.ContentTransform, theme, spacing: 8f, padding: 0f, scrollable: true);
			column.Add(filterRow.FixedHeight(32f));
			filterSignature = null;

			emptyLabel = UiLabel.Create(column.ContentTransform, "No projects found — create one above.", theme, 13);
			column.Add(emptyLabel.FixedHeight(22f));

			projectList = UiList<BrowserRow>.Create(column.ContentTransform, spacing: 2f, padding: 0f);
			column.Add(projectList.Grow());

			statusLabel = UiLabel.Create(column.ContentTransform, "", theme, 12);
			column.Add(statusLabel.FixedHeight(22f));
		}

		/// <summary>Rescans disk and rebuilds the project list.</summary>
		internal static void Refresh()
		{
			if (projectList == null)
			{
				return;
			}

			if (importButton != null)
			{
				importButton.Visible(true);
			}

			LabMenuBar.Refresh();
			RebuildFilterIfNeeded();

			List<BrowserRow> rows = BuildVisibleRows();
			int projectCount = CountProjects(rows);
			bool noneVisible = projectCount == 0;
			emptyLabel.Visible(noneVisible);
			if (noneVisible && !HasVisibleType())
			{
				emptyLabel.SetText("All project types are hidden — enable one above.");
			}
			else
			{
				emptyLabel.SetText("No projects found — create one above.");
			}

			projectList.SetItems(rows, RowKey, BuildRow);
			SetStatus(noneVisible
				? (HasVisibleType() ? "No projects on disk." : "No project types selected.")
				: projectCount + " project(s).");
		}

		private static string RowKey(BrowserRow row)
		{
			if (row.IsSection)
			{
				return "section:" + row.ProjectTypeId;
			}

			return row.IsRecent ? "recent:" + row.Folder : row.Folder;
		}

		private static UiElement BuildRow(Transform parent, BrowserRow row)
		{
			UiTheme theme = UiTheme.Default;
			if (row.IsSection)
			{
				UiStack header = UiStack.Horizontal(parent, theme, spacing: 8f, padding: 0f);
				string title = row.TypeDisplayName + "  (" + row.SectionCount + ")";
				header.Add(UiLabel.Create(header.ContentTransform, title, theme, 15, TextAnchor.MiddleLeft).Grow());
				return header.FixedHeight(26f);
			}

			UiStack line = UiStack.Horizontal(parent, theme, spacing: 8f, padding: 0f);
			string label = row.DisplayName;
			if (!string.Equals(row.DisplayName, row.Id, StringComparison.Ordinal))
			{
				label = row.DisplayName + " (" + row.Id + ")";
			}

			if (row.IsRecent && !string.IsNullOrEmpty(row.TypeDisplayName))
			{
				label += "  [" + row.TypeDisplayName + "]";
			}

			line.Add(UiButton.Create(line.ContentTransform, label, () => OpenRow(row), primary: false).Grow());
			if (row.IsRecent)
			{
				line.Add(UiButton.Create(line.ContentTransform, "-", () => RemoveRecent(row), primary: false).FixedWidth(28f));
			}
			else if (CanDelete(row))
			{
				line.Add(UiButton.Create(line.ContentTransform, "x", () => PromptDelete(row), primary: false).FixedWidth(28f));
			}

			return line.FixedHeight(28f);
		}

		/// <summary>Unpins a folder from recents without deleting the project on disk.</summary>
		private static void RemoveRecent(BrowserRow row)
		{
			if (row == null || string.IsNullOrEmpty(row.Folder))
			{
				return;
			}

			RecentProjectsStore.Remove(row.Folder);
			Refresh();
		}

		private static void OnNewProject()
		{
			List<ProjectTypeRegistration> types = new List<ProjectTypeRegistration>();
			foreach (ProjectTypeRegistration type in LokrLabApi.LokrLabApi.ProjectTypes)
			{
				if (type.CreateNew != null)
				{
					types.Add(type);
				}
			}

			if (types.Count == 0)
			{
				SetStatus("No creatable project types are registered.");
				return;
			}

			ShowCreateWizard(types);
		}

		private static void ShowCreateWizard(List<ProjectTypeRegistration> types)
		{
			createTypes = types;
			if (!EnsureCreateModal())
			{
				SetStatus("Could not open the New Project dialog.");
				return;
			}

			List<string> names = new List<string>(types.Count);
			for (int i = 0; i < types.Count; i++)
			{
				names.Add(types[i].DisplayName);
			}

			typeDropdown.SetOptions(names);
			typeDropdown.SetValueSilently(0);
			RebuildCreateSheet(0);
			createErrorLabel.SetText(string.Empty);
			createModal.Show();
		}

		private static bool EnsureCreateModal()
		{
			if (createModal != null && createModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = null;
			if (screenRoot != null)
			{
				Canvas found = screenRoot.GetComponentInParent<Canvas>();
				if (found != null)
				{
					canvas = found.transform;
				}
			}

			if (canvas == null)
			{
				canvas = LabShell.Canvas;
			}

			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			createModal = UiModal.Create(canvas, theme, "New Project", 640f, 720f);
			UiStack content = UiStack.Vertical(createModal.ContentParent, theme, spacing: 8f, padding: 12f);
			createModal.Add(content);

			content.Add(UiLabel.Create(content.ContentTransform, "Project type", theme, 13).FixedHeight(20f));
			typeDropdown = UiDropdown.Create(content.ContentTransform, new[] { "Character" }, theme);
			typeDropdown.OnValueChanged(RebuildCreateSheet);
			content.Add(typeDropdown.FixedHeight(28f));

			sheetHost = UiStack.Vertical(content.ContentTransform, theme, spacing: 0f, padding: 0f);
			content.Add(sheetHost.Grow());

			createErrorLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12);
			content.Add(createErrorLabel.FixedHeight(22f));

			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Create", OnCreateConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", createModal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void RebuildCreateSheet(int index)
		{
			if (sheetHost == null || createTypes == null || index < 0 || index >= createTypes.Count)
			{
				return;
			}

			sheetHost.Clear();
			if (createErrorLabel != null)
			{
				createErrorLabel.SetText(string.Empty);
			}

			ProjectTypeRegistration type = createTypes[index];
			if (type.BuildCreateSheet != null)
			{
				type.BuildCreateSheet(sheetHost.ContentTransform);
				return;
			}

			sheetHost.Add(UiLabel.Create(sheetHost.ContentTransform,
				"No extra details for this project type.",
				UiTheme.Default, 13).FixedHeight(22f));
		}

		private static void OnCreateConfirmed()
		{
			if (createTypes == null || typeDropdown == null)
			{
				return;
			}

			int index = typeDropdown.Dropdown.value;
			if (index < 0 || index >= createTypes.Count)
			{
				return;
			}

			ProjectTypeRegistration type = createTypes[index];
			if (type.CommitCreateSheet != null)
			{
				string error = type.CommitCreateSheet();
				if (!string.IsNullOrEmpty(error))
				{
					if (createErrorLabel != null)
					{
						createErrorLabel.SetText(error);
					}

					return;
				}
			}

			if (createModal != null)
			{
				createModal.Hide();
			}

			BeginSession(type.CreateNew);
		}

		/// <summary>True when this row is a disposable project (not a singleton library).</summary>
		internal static bool CanDeleteOpenSession()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return false;
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId);
			return type != null && !type.IsSingleton;
		}

		/// <summary>Confirms and deletes the currently open project, then returns to the Project Browser.</summary>
		internal static void PromptDeleteOpenSession()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return;
			}

			PromptDelete(new BrowserRow
			{
				Folder = session.FolderPath,
				Id = session.Id,
				DisplayName = session.DisplayName,
				ProjectTypeId = session.ProjectTypeId,
				TypeDisplayName = session.ProjectTypeId
			});
		}

		private static bool CanDelete(BrowserRow row)
		{
			if (row == null || row.IsSection)
			{
				return false;
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(row.ProjectTypeId);
			return type != null && !type.IsSingleton;
		}

		private static void PromptDelete(BrowserRow row)
		{
			if (!CanDelete(row) || !EnsureDeleteModal())
			{
				return;
			}

			pendingDelete = row;
			string shown = row.DisplayName ?? row.Id ?? row.Folder;
			if (!string.Equals(row.DisplayName, row.Id, StringComparison.Ordinal) && !string.IsNullOrEmpty(row.Id))
			{
				shown = row.DisplayName + " (" + row.Id + ")";
			}

			deleteMessageLabel.SetText("Delete " + shown + "? This permanently removes the folder and cannot be undone.");
			deleteModal.Show();
		}

		private static bool EnsureDeleteModal()
		{
			if (deleteModal != null && deleteModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = null;
			if (screenRoot != null)
			{
				Canvas found = screenRoot.GetComponentInParent<Canvas>();
				if (found != null)
				{
					canvas = found.transform;
				}
			}

			if (canvas == null)
			{
				canvas = LabShell.Canvas;
			}

			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			deleteModal = UiModal.Create(canvas, theme, "Delete Project", 560f, 220f);
			UiStack content = UiStack.Vertical(deleteModal.ContentParent, theme, spacing: 8f, padding: 12f);
			deleteModal.Add(content);
			deleteMessageLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 14, TextAnchor.UpperLeft);
			content.Add(deleteMessageLabel.Grow());
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Delete", OnDeleteConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", deleteModal.Hide, theme, primary: false).FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void OnDeleteConfirmed()
		{
			if (deleteModal != null)
			{
				deleteModal.Hide();
			}

			BrowserRow row = pendingDelete;
			pendingDelete = null;
			if (row == null)
			{
				return;
			}

			ProjectSession current = LokrLabApi.LokrLabApi.CurrentSession;
			bool deletingOpen = current != null
				&& string.Equals(current.FolderPath, row.Folder, StringComparison.OrdinalIgnoreCase);
			if (deletingOpen)
			{
				CharacterLabScene.CloseProject();
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(row.ProjectTypeId);
			string error = null;
			if (type != null && type.Delete != null)
			{
				error = type.Delete(row.Folder);
			}
			else if (!string.IsNullOrEmpty(row.Folder) && Directory.Exists(row.Folder))
			{
				try
				{
					Directory.Delete(row.Folder, true);
				}
				catch (Exception ex)
				{
					error = ex.Message;
				}
			}

			if (string.IsNullOrEmpty(error))
			{
				RecentProjectsStore.Remove(row.Folder);
				type?.OnDeleted?.Invoke(row.Folder);
			}

			Refresh();
			SetStatus(string.IsNullOrEmpty(error) ? "Deleted " + (row.DisplayName ?? row.Id) + "." : error);
		}

		/// <summary>Opens the legacy-pack folder picker when Character has assigned PromptLegacyImport.</summary>
		internal static void PromptLegacyImport()
		{
			if (LokrLabApi.LokrLabApi.PromptLegacyImport == null)
			{
				SetStatus("No legacy importer is registered.");
				return;
			}

			LokrLabApi.LokrLabApi.PromptLegacyImport();
		}

		private static void OnImportLegacy()
		{
			PromptLegacyImport();
		}

		private static void OpenRow(BrowserRow row)
		{
			if (row == null || row.IsSection)
			{
				return;
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(row.ProjectTypeId);
			if (type == null || type.Load == null)
			{
				SetStatus("No loader registered for project type '" + row.ProjectTypeId + "'.");
				return;
			}

			BeginSession(() => type.Load(row.Folder));
		}

		private static void BeginSession(Func<ProjectSession> factory)
		{
			if (factory == null)
			{
				SetStatus("This project type has no create/load callback.");
				return;
			}

			ProjectSession session = factory();
			if (session == null)
			{
				SetStatus("Create/load was cancelled.");
				return;
			}

			LokrLabApi.LokrLabApi.CurrentSession = session;
			RecentProjectsStore.Record(session.FolderPath);
			CharacterLabScene.SwitchToShell();
		}

		/// <summary>Every discovered project of <paramref name="projectTypeId"/>, sorted by display name.</summary>
		internal static List<ProjectReference> ListProjectReferences(string projectTypeId)
		{
			List<ProjectReference> result = new List<ProjectReference>();
			if (string.IsNullOrEmpty(projectTypeId))
			{
				return result;
			}

			foreach (BrowserRow row in DiscoverProjects())
			{
				if (!string.Equals(row.ProjectTypeId, projectTypeId, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				result.Add(new ProjectReference(row.ProjectTypeId, row.Id, row.Folder, row.DisplayName));
			}

			result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
			return result;
		}

		private static List<BrowserRow> DiscoverProjects()
		{
			Dictionary<string, BrowserRow> byFolder = new Dictionary<string, BrowserRow>(StringComparer.OrdinalIgnoreCase);

			foreach (ProjectTypeRegistration type in LokrLabApi.LokrLabApi.ProjectTypes)
			{
				if (type.IsSingleton)
				{
					AddRow(byFolder, type.FolderRoot, type);
					continue;
				}

				if (!string.IsNullOrEmpty(type.FolderRoot) && Directory.Exists(type.FolderRoot))
				{
					foreach (string folder in Directory.GetDirectories(type.FolderRoot))
					{
						AddRow(byFolder, folder, type);
					}
				}

				if (!string.IsNullOrEmpty(type.ScanCategory))
				{
					foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(type.ScanCategory))
					{
						AddRow(byFolder, itemFolder, type);
					}
				}
			}

			return new List<BrowserRow>(byFolder.Values);
		}

		private static List<BrowserRow> BuildVisibleRows()
		{
			List<BrowserRow> discovered = DiscoverProjects();
			Dictionary<string, List<BrowserRow>> byType = new Dictionary<string, List<BrowserRow>>(StringComparer.OrdinalIgnoreCase);
			foreach (BrowserRow row in discovered)
			{
				List<BrowserRow> group;
				if (!byType.TryGetValue(row.ProjectTypeId, out group))
				{
					group = new List<BrowserRow>();
					byType[row.ProjectTypeId] = group;
				}

				group.Add(row);
			}

			IReadOnlyList<string> recents = LokrLabApi.LokrLabApi.RecentProjectFolders != null
				? LokrLabApi.LokrLabApi.RecentProjectFolders()
				: Array.Empty<string>();

			List<BrowserRow> rows = new List<BrowserRow>();
			Dictionary<string, BrowserRow> byFolder = new Dictionary<string, BrowserRow>(StringComparer.OrdinalIgnoreCase);
			foreach (BrowserRow row in discovered)
			{
				if (!string.IsNullOrEmpty(row.Folder) && !byFolder.ContainsKey(row.Folder))
				{
					byFolder[row.Folder] = row;
				}
			}

			List<BrowserRow> recentRows = new List<BrowserRow>();
			foreach (string folder in recents)
			{
				if (recentRows.Count >= RecentMenuLimit)
				{
					break;
				}

				BrowserRow match;
				if (!byFolder.TryGetValue(folder, out match))
				{
					continue;
				}

				recentRows.Add(new BrowserRow
				{
					IsRecent = true,
					Folder = match.Folder,
					Id = match.Id,
					DisplayName = match.DisplayName,
					ProjectTypeId = match.ProjectTypeId,
					TypeDisplayName = match.TypeDisplayName
				});
			}

			if (recentRows.Count > 0)
			{
				rows.Add(new BrowserRow
				{
					IsSection = true,
					ProjectTypeId = "__recent",
					TypeDisplayName = "Recent",
					SectionCount = recentRows.Count
				});
				rows.AddRange(recentRows);
			}

			foreach (ProjectTypeRegistration type in SortedTypes())
			{
				if (!IsTypeVisible(type.Id))
				{
					continue;
				}

				List<BrowserRow> group;
				if (!byType.TryGetValue(type.Id, out group))
				{
					group = new List<BrowserRow>();
				}

				SortProjectRows(group, recents);
				rows.Add(new BrowserRow
				{
					IsSection = true,
					ProjectTypeId = type.Id,
					TypeDisplayName = type.DisplayName,
					SectionCount = group.Count
				});
				rows.AddRange(group);
			}

			return rows;
		}

		private static void SortProjectRows(List<BrowserRow> rows, IReadOnlyList<string> recents)
		{
			rows.Sort((a, b) =>
			{
				int aRecent = IndexOfRecent(recents, a.Folder);
				int bRecent = IndexOfRecent(recents, b.Folder);
				if (aRecent != bRecent)
				{
					if (aRecent < 0)
					{
						return 1;
					}
					if (bRecent < 0)
					{
						return -1;
					}
					return aRecent.CompareTo(bRecent);
				}

				return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
			});
		}

		private static List<ProjectTypeRegistration> SortedTypes()
		{
			List<ProjectTypeRegistration> types = new List<ProjectTypeRegistration>();
			foreach (ProjectTypeRegistration type in LokrLabApi.LokrLabApi.ProjectTypes)
			{
				types.Add(type);
			}

			types.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
			return types;
		}

		private static void RebuildFilterIfNeeded()
		{
			if (filterRow == null)
			{
				return;
			}

			List<ProjectTypeRegistration> types = SortedTypes();
			string signature = string.Empty;
			for (int i = 0; i < types.Count; i++)
			{
				if (i > 0)
				{
					signature += ",";
				}

				signature += types[i].Id;
			}

			if (signature == filterSignature)
			{
				return;
			}

			filterSignature = signature;
			filterRow.Clear();
			UiTheme theme = UiTheme.Default;
			filterRow.Add(UiLabel.Create(filterRow.ContentTransform, "Show:", theme, 13).FixedWidth(48f));
			foreach (ProjectTypeRegistration type in types)
			{
				string typeId = type.Id;
				float width = 12f * type.DisplayName.Length + 40f;
				if (width < 110f)
				{
					width = 110f;
				}
				if (width > 220f)
				{
					width = 220f;
				}

				UiToggle toggle = UiToggle.Create(filterRow.ContentTransform, type.DisplayName, IsTypeVisible(typeId), theme)
					.FixedWidth(width)
					.FixedHeight(28f)
					.OnValueChanged(visible => SetTypeVisible(typeId, visible));
				filterRow.Add(toggle);
			}
		}

		private static bool HasVisibleType()
		{
			foreach (ProjectTypeRegistration type in LokrLabApi.LokrLabApi.ProjectTypes)
			{
				if (IsTypeVisible(type.Id))
				{
					return true;
				}
			}

			return false;
		}

		private static int CountProjects(List<BrowserRow> rows)
		{
			int count = 0;
			foreach (BrowserRow row in rows)
			{
				if (!row.IsSection && !row.IsRecent)
				{
					count++;
				}
			}

			return count;
		}

		private static bool IsTypeVisible(string typeId)
		{
			if (string.IsNullOrEmpty(typeId) || LokrLabPlugin.HiddenProjectTypes == null)
			{
				return true;
			}

			string raw = LokrLabPlugin.HiddenProjectTypes.Value;
			if (string.IsNullOrEmpty(raw))
			{
				return true;
			}

			string[] parts = raw.Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				if (string.Equals(parts[i].Trim(), typeId, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}

			return true;
		}

		private static void SetTypeVisible(string typeId, bool visible)
		{
			if (string.IsNullOrEmpty(typeId) || LokrLabPlugin.HiddenProjectTypes == null)
			{
				return;
			}

			List<string> hidden = new List<string>();
			string raw = LokrLabPlugin.HiddenProjectTypes.Value;
			if (!string.IsNullOrEmpty(raw))
			{
				string[] parts = raw.Split(',');
				for (int i = 0; i < parts.Length; i++)
				{
					string id = parts[i].Trim();
					if (id.Length > 0)
					{
						hidden.Add(id);
					}
				}
			}

			if (visible)
			{
				hidden.RemoveAll(id => string.Equals(id, typeId, StringComparison.OrdinalIgnoreCase));
			}
			else
			{
				bool already = false;
				foreach (string id in hidden)
				{
					if (string.Equals(id, typeId, StringComparison.OrdinalIgnoreCase))
					{
						already = true;
						break;
					}
				}

				if (!already)
				{
					hidden.Add(typeId);
				}
			}

			LokrLabPlugin.HiddenProjectTypes.Value = string.Join(",", hidden.ToArray());
			Refresh();
		}

		private static void AddRow(Dictionary<string, BrowserRow> byFolder, string folder, ProjectTypeRegistration type)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || byFolder.ContainsKey(folder))
			{
				return;
			}

			string markedType = ProjectMarker.TryReadProjectType(folder);
			if (markedType != null && !string.Equals(markedType, type.Id, StringComparison.OrdinalIgnoreCase))
			{
				ProjectTypeRegistration marked = LokrLabApi.LokrLabApi.GetProjectType(markedType);
				if (marked != null)
				{
					type = marked;
				}
			}

			string id = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string display = type.IsSingleton ? type.DisplayName : id;
			if (type.IsSingleton)
			{
				id = type.Id;
			}
			if (type.ResolveDisplayName != null)
			{
				string resolved = type.ResolveDisplayName(folder);
				if (!string.IsNullOrEmpty(resolved))
				{
					display = resolved;
				}
			}

			byFolder[folder] = new BrowserRow
			{
				Folder = folder,
				Id = id,
				DisplayName = display,
				ProjectTypeId = type.Id,
				TypeDisplayName = type.DisplayName
			};
		}

		private static int IndexOfRecent(IReadOnlyList<string> recents, string folder)
		{
			for (int i = 0; i < recents.Count; i++)
			{
				if (string.Equals(recents[i], folder, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			return -1;
		}

		private static void SetStatus(string message)
		{
			if (statusLabel != null)
			{
				statusLabel.SetText(message ?? string.Empty);
			}
		}

		private sealed class BrowserRow
		{
			internal bool IsSection;
			internal bool IsRecent;
			internal int SectionCount;
			internal string Folder;
			internal string Id;
			internal string DisplayName;
			internal string ProjectTypeId;
			internal string TypeDisplayName;
		}
	}
}
