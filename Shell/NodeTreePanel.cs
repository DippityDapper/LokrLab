using System.Collections.Generic;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LokrLab.Shell
{
	/// <summary>Shell Node Tree: concatenates the open project type's contributors into one UiTree and writes EditorSelection.</summary>
	internal static class NodeTreePanel
	{
		private static UiTree tree;
		private static UiContextMenu menu;
		private static UiLabel emptyLabel;
		private static List<LabNode> lastRoots = new List<LabNode>();

		/// <summary>Builds the tree into a dock panel's content area and returns the root stack so the panel can Add it.</summary>
		internal static UiStack Build(Transform contentParent, Transform canvas, UiTheme theme)
		{
			UiStack column = UiStack.Vertical(contentParent, theme, spacing: 4f, padding: 4f);
			emptyLabel = UiLabel.Create(column.ContentTransform, "No contributors for this project type.", theme, 12);
			column.Add(emptyLabel.FixedHeight(22f));

			tree = UiTree.Create(column.ContentTransform, theme);
			tree.OnSelectionChanged(OnTreeSelectionChanged);
			tree.OnRowRightClick(OnRowRightClick);
			tree.OnRowActivated(OnRowActivated);
			tree.OnReordered((_, __, ___) => Refresh());
			column.Add(tree.Grow());

			menu = UiContextMenu.Create(canvas, theme);
			return column;
		}

		/// <summary>Selects a node by id and writes EditorSelection. Returns false if the id is not in the tree.</summary>
		internal static bool SelectById(string id)
		{
			if (tree == null || string.IsNullOrEmpty(id) || tree.FindById(id) == null)
			{
				return false;
			}

			if (tree.SelectedItems.Count == 1 && tree.SelectedItems[0].Id == id)
			{
				return true;
			}

			tree.Select(id);
			return true;
		}

		/// <summary>First node id whose DisplayName matches, or null.</summary>
		internal static string FindIdByDisplayName(string displayName)
		{
			if (string.IsNullOrEmpty(displayName))
			{
				return null;
			}

			return FindIdByDisplayName(lastRoots, displayName);
		}

		private static string FindIdByDisplayName(IReadOnlyList<LabNode> nodes, string displayName)
		{
			if (nodes == null)
			{
				return null;
			}

			foreach (LabNode node in nodes)
			{
				if (node != null && node.DisplayName == displayName)
				{
					return node.Id;
				}

				string child = FindIdByDisplayName(node != null ? node.Children : null, displayName);
				if (child != null)
				{
					return child;
				}
			}

			return null;
		}

		/// <summary>Rebuilds the tree from the current session's project-type contributors.</summary>
		internal static void Refresh()
		{
			if (tree == null)
			{
				return;
			}

			List<string> selectedIds = new List<string>();
			foreach (UiTreeItem item in tree.SelectedItems)
			{
				selectedIds.Add(item.Id);
			}

			lastRoots = CollectRoots();
			emptyLabel.Visible(lastRoots.Count == 0);
			tree.SetRoots(ToTreeItems(lastRoots));

			InspectorDock.Invalidate();
			if (selectedIds.Count > 0 && tree.FindById(selectedIds[0]) != null)
			{
				tree.Select(selectedIds[0]);
			}
			else
			{
				ApplySelection(new List<LabNode>());
			}
		}

		private static List<LabNode> CollectRoots()
		{
			List<LabNode> roots = new List<LabNode>();
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return roots;
			}

			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId);
			if (type == null)
			{
				return roots;
			}

			foreach (NodeTreeContributor contributor in type.NodeTreeContributors)
			{
				IEnumerable<LabNode> contributed = contributor(session);
				if (contributed == null)
				{
					continue;
				}

				foreach (LabNode node in contributed)
				{
					if (node != null)
					{
						roots.Add(node);
					}
				}
			}

			return roots;
		}

		private static List<UiTreeItem> ToTreeItems(List<LabNode> nodes)
		{
			List<UiTreeItem> items = new List<UiTreeItem>(nodes.Count);
			foreach (LabNode node in nodes)
			{
				items.Add(ToTreeItem(node));
			}

			return items;
		}

		private static UiTreeItem ToTreeItem(LabNode node)
		{
			UiTreeItem item = new UiTreeItem
			{
				Id = node.Id,
				Label = node.DisplayName,
				IconKey = node.IconKey,
				UserData = node,
				Expanded = true
			};
			foreach (LabNode child in node.Children)
			{
				item.Children.Add(ToTreeItem(child));
			}

			return item;
		}

		private static void OnTreeSelectionChanged(IReadOnlyList<UiTreeItem> items)
		{
			List<LabNode> nodes = new List<LabNode>();
			foreach (UiTreeItem item in items)
			{
				if (item.UserData is LabNode node)
				{
					nodes.Add(node);
				}
			}

			ApplySelection(nodes);
		}

		private static void ApplySelection(List<LabNode> nodes)
		{
			LokrLabApi.LokrLabApi.Selection.Set(nodes);
			LabShell.ShowSelection(nodes);
		}

		private static void OnRowActivated(UiTreeItem item)
		{
			if (item == null || !(item.UserData is LabNode node))
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;
			type?.OnNodeActivated?.Invoke(node);
		}

		private static void OnRowRightClick(UiTreeItem item, PointerEventData eventData)
		{
			if (menu == null)
			{
				return;
			}

			LabNode node = item.UserData as LabNode;
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			ProjectTypeRegistration type = session != null
				? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId)
				: null;

			menu.ClearItems();
			bool any = false;
			if (type != null && node != null)
			{
				foreach ((string kind, NodeFactory factory) in type.FactoriesForParent(node.Kind))
				{
					string addKind = kind;
					NodeFactory addFactory = factory;
					menu.AddItem("Add " + addKind, () => RunFactory(addFactory, node, session));
					any = true;
				}
			}

			if (!any)
			{
				menu.AddItem("(no actions)", null, enabled: false);
			}

			menu.Show(eventData.position);
		}

		private static void RunFactory(NodeFactory factory, LabNode parent, ProjectSession session)
		{
			if (factory == null)
			{
				return;
			}

			LabNode created = factory(parent, session);
			Refresh();
			if (created != null)
			{
				tree.Select(created.Id);
			}
		}
	}
}
