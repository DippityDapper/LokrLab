using System;
using System.Collections.Generic;
using System.IO;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Shell
{
	/// <summary>Left-dock File Tree: the open project's folder on disk. Double-click jumps to a matching Node Tree row when one exists.</summary>
	/// <remarks>
	/// File Tree is "what is on disk"; Node Tree is "what this project logically contains."
	/// FileBrowserPanel stays the modal picker for Save/Import/Atlas. This dock does not move files.
	/// </remarks>
	internal static class FileTreePanel
	{
		private static UiTree tree;
		private static UiLabel emptyLabel;

		/// <summary>Builds the File Tree into a dock panel and returns the root stack for Add.</summary>
		internal static UiStack Build(Transform contentParent, UiTheme theme)
		{
			UiStack column = UiStack.Vertical(contentParent, theme, spacing: 4f, padding: 4f);
			emptyLabel = UiLabel.Create(column.ContentTransform, "No project folder open.", theme, 12);
			column.Add(emptyLabel.FixedHeight(22f));

			tree = UiTree.Create(column.ContentTransform, theme);
			tree.SetReorderable(false);
			tree.OnRowActivated(OnRowActivated);
			column.Add(tree.Grow());
			return column;
		}

		/// <summary>Rebuilds the tree from the current session's folder.</summary>
		internal static void Refresh()
		{
			if (tree == null)
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null || string.IsNullOrEmpty(session.FolderPath) || !Directory.Exists(session.FolderPath))
			{
				emptyLabel.Visible(true);
				emptyLabel.SetText("No project folder open.");
				tree.SetRoots(Array.Empty<UiTreeItem>());
				return;
			}

			emptyLabel.Visible(false);
			tree.SetRoots(new[] { BuildFolderItem(session.FolderPath, session.FolderPath, isRoot: true) });
		}

		private static UiTreeItem BuildFolderItem(string folder, string projectRoot, bool isRoot)
		{
			string label = isRoot ? Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(folder);
			if (string.IsNullOrEmpty(label))
			{
				label = folder;
			}

			UiTreeItem item = new UiTreeItem
			{
				Id = RelativeId(projectRoot, folder),
				Label = label,
				IconKey = "Dir",
				UserData = folder,
				Expanded = isRoot
			};

			List<string> directories = new List<string>();
			List<string> files = new List<string>();
			try
			{
				directories.AddRange(Directory.GetDirectories(folder));
				files.AddRange(Directory.GetFiles(folder));
			}
			catch (Exception)
			{
				return item;
			}

			directories.Sort(StringComparer.OrdinalIgnoreCase);
			files.Sort(StringComparer.OrdinalIgnoreCase);

			foreach (string directory in directories)
			{
				if (IsHiddenName(Path.GetFileName(directory)))
				{
					continue;
				}

				item.Children.Add(BuildFolderItem(directory, projectRoot, isRoot: false));
			}

			foreach (string file in files)
			{
				string name = Path.GetFileName(file);
				if (IsHiddenName(name))
				{
					continue;
				}

				item.Children.Add(new UiTreeItem
				{
					Id = RelativeId(projectRoot, file),
					Label = name,
					IconKey = "File",
					UserData = file,
					Expanded = false
				});
			}

			return item;
		}

		private static void OnRowActivated(UiTreeItem item)
		{
			if (item == null || !(item.UserData is string path))
			{
				return;
			}

			if (Directory.Exists(path))
			{
				item.Expanded = !item.Expanded;
				tree.Refresh();
				return;
			}

			string nodeId = GuessNodeId(item.Id, Path.GetFileName(path));
			if (nodeId != null && NodeTreePanel.SelectById(nodeId))
			{
				LabShell.SetStatus("Selected Node Tree '" + nodeId + "'.");
				return;
			}

			LabShell.SetStatus("No Node Tree entry for '" + Path.GetFileName(path) + "'.");
		}

		private static string GuessNodeId(string relativeId, string fileName)
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			string norm = (relativeId ?? string.Empty).Replace('\\', '/').Trim('/');
			string name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
			string directory = Path.GetDirectoryName(norm) ?? string.Empty;
			directory = directory.Replace('\\', '/');

			if (string.Equals(fileName, "project.json", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(fileName, "character.json", StringComparison.OrdinalIgnoreCase))
			{
				if (session != null && session.ProjectTypeId == LokrLabApi.LokrLabApi.CharacterTypeId)
				{
					return "character:" + session.Id;
				}

				return NodeTreePanel.FindIdByDisplayName(session != null ? session.DisplayName : name);
			}

			if (string.Equals(fileName, "ability.txt", StringComparison.OrdinalIgnoreCase))
			{
				string abilityId = Path.GetFileName(directory);
				return !string.IsNullOrEmpty(abilityId) ? NodeTreePanel.FindIdByDisplayName(abilityId) : null;
			}

			if (string.Equals(fileName, "rig.json", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(norm, "rig", StringComparison.OrdinalIgnoreCase))
			{
				return session != null ? "rig:" + session.Id : null;
			}

			if (string.Equals(directory, "sprites", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name))
			{
				return "part:" + name;
			}

			if (directory.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				string portraits = NodeTreePanel.FindIdByDisplayName("Portraits");
				if (portraits != null)
				{
					return portraits;
				}
			}

			return NodeTreePanel.FindIdByDisplayName(name);
		}

		private static string RelativeId(string projectRoot, string path)
		{
			string root = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && path.Length > root.Length)
			{
				return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					.Replace('\\', '/');
			}

			return Path.GetFileName(path);
		}

		private static bool IsHiddenName(string name)
		{
			return string.IsNullOrEmpty(name) || name[0] == '.' || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
		}
	}
}
