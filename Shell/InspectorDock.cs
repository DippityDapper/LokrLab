using System.Collections.Generic;
using System.Text;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab.Shell
{
	/// <summary>Shell Inspector: registry drawers or a project type's persistent inspector hosts.</summary>
	/// <remarks>
	/// Rebuilds registry drawers only when selection identity or the active workspace changes.
	/// Persistent hosts (one Grow() host per registration id) stay alive so playback-tick
	/// refresh and PersistAndSync keep working. Default hosts scroll; set
	/// <see cref="PersistentInspectorRegistration.Scrollable"/> false when the inner form
	/// owns the only ScrollRect. Inner content must not nest another ScrollRect.
	/// Extra RegisterInspectorSection callbacks stack under registry drawers on the same identity change.
	/// </remarks>
	internal static class InspectorDock
	{
		private static UiLabel emptyLabel;
		private static UiStack column;
		private static UiStack drawerHost;
		private static readonly Dictionary<string, UiStack> persistentHosts = new Dictionary<string, UiStack>();
		private static string lastPersistentTypeId;
		private static string lastIdentity;

		/// <summary>Drops inspector widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			persistentHosts.Clear();
			emptyLabel = null;
			column = null;
			drawerHost = null;
			lastPersistentTypeId = null;
			lastIdentity = null;
		}

		/// <summary>Builds the inspector body into a dock panel and returns the root stack for Add.</summary>
		internal static UiStack Build(Transform contentParent, UiTheme theme)
		{
			ResetSession();
			column = UiStack.Vertical(contentParent, theme, spacing: 8f, padding: 12f);
			emptyLabel = UiLabel.Create(column.ContentTransform,
				"Select a node in the Node Tree.", theme, 14, TextAnchor.UpperLeft);
			emptyLabel.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
			emptyLabel.Text.verticalOverflow = VerticalWrapMode.Overflow;
			column.Add(emptyLabel.FixedHeight(48f));

			drawerHost = UiStack.Vertical(column.ContentTransform, theme, spacing: 8f, padding: 0f, scrollable: true);
			column.Add(drawerHost.Grow());

			lastPersistentTypeId = null;
			EnsurePersistentHosts();
			Refresh();
			return column;
		}

		/// <summary>Dispatches the inspector for the current selection, or no-ops when identity is unchanged.</summary>
		internal static void Refresh()
		{
			if (drawerHost == null)
			{
				return;
			}

			IReadOnlyList<LabNode> nodes = LokrLabApi.LokrLabApi.Selection.All;
			string identity = IdentityKey(nodes);
			if (identity == lastIdentity)
			{
				TickPersistent(nodes);
				return;
			}

			lastIdentity = identity;
			ShowMode(nodes);
		}

		/// <summary>Forces the next Refresh to rebuild even if the selection ids have not changed.</summary>
		internal static void Invalidate()
		{
			lastIdentity = null;
		}

		private static void ShowMode(IReadOnlyList<LabNode> nodes)
		{
			EnsurePersistentHosts();
			drawerHost.Clear();
			drawerHost.Visible(false);
			HidePersistentHosts();

			if (nodes == null || nodes.Count == 0)
			{
				emptyLabel.SetText("Select a node in the Node Tree.");
				emptyLabel.Visible(true);
				return;
			}

			if (TryShowPersistent(nodes))
			{
				emptyLabel.Visible(false);
				return;
			}

			LabNode primary = LokrLabApi.LokrLabApi.Selection.Primary ?? nodes[0];
			ProjectTypeRegistration type = CurrentType();
			InspectorDrawer drawer = type != null ? type.FindInspectorDrawer(primary.Kind) : null;
			if (drawer == null)
			{
				string extra = nodes.Count > 1 ? " (+" + (nodes.Count - 1) + " more)" : "";
				emptyLabel.SetText("No inspector drawer for kind '" + primary.Kind + "'.\n"
					+ primary.Kind + ": " + primary.DisplayName + extra + "\nId: " + primary.Id);
				emptyLabel.Visible(true);
				return;
			}

			emptyLabel.Visible(false);
			drawerHost.Visible(true);
			if (nodes.Count > 1)
			{
				drawerHost.Add(UiLabel.Create(drawerHost.ContentTransform,
					nodes.Count.ToString() + " selected — showing " + primary.Kind + " '" + primary.DisplayName + "'",
					UiTheme.Default, 11).FixedHeight(20f));
			}

			drawer(primary, LokrLabApi.LokrLabApi.CurrentSession, drawerHost.ContentTransform);
			if (type != null)
			{
				foreach (InspectorDrawer section in type.FindInspectorSections(primary.Kind))
				{
					section(primary, LokrLabApi.LokrLabApi.CurrentSession, drawerHost.ContentTransform);
				}
			}
		}

		private static bool TryShowPersistent(IReadOnlyList<LabNode> nodes)
		{
			ProjectTypeRegistration type = CurrentType();
			if (type == null)
			{
				return false;
			}

			foreach (PersistentInspectorRegistration registration in type.PersistentInspectors)
			{
				if (registration.Matches == null || !registration.Matches(nodes))
				{
					continue;
				}

				if (!persistentHosts.TryGetValue(registration.Id, out UiStack host))
				{
					continue;
				}

				host.Visible(true);
				registration.EnsureBuilt?.Invoke(host.ContentTransform);
				registration.Show?.Invoke(nodes);
				return true;
			}

			return false;
		}

		private static void TickPersistent(IReadOnlyList<LabNode> nodes)
		{
			ProjectTypeRegistration type = CurrentType();
			if (type == null)
			{
				return;
			}

			foreach (PersistentInspectorRegistration registration in type.PersistentInspectors)
			{
				if (registration.Matches != null && registration.Matches(nodes))
				{
					registration.Refresh?.Invoke();
					return;
				}
			}
		}

		private static void EnsurePersistentHosts()
		{
			ProjectTypeRegistration type = CurrentType();
			string typeId = type != null ? type.Id : string.Empty;
			if (typeId == lastPersistentTypeId || column == null)
			{
				return;
			}

			HidePersistentHosts();
			lastPersistentTypeId = typeId;
			foreach (UiStack host in persistentHosts.Values)
			{
				if (host != null && host.GameObject != null)
				{
					Object.Destroy(host.GameObject);
				}
			}

			persistentHosts.Clear();
			if (type == null)
			{
				return;
			}

			foreach (PersistentInspectorRegistration registration in type.PersistentInspectors)
			{
				bool scrollable = registration.Scrollable;
				UiStack host = UiStack.Vertical(column.ContentTransform, UiTheme.Default, spacing: 0f, padding: 0f,
					scrollable: scrollable);
				column.Add(host.Grow());
				if (!scrollable)
				{
					ContentSizeFitter fitter = host.GameObject.GetComponent<ContentSizeFitter>();
					if (fitter != null)
					{
						fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
					}
				}

				host.Visible(false);
				persistentHosts[registration.Id] = host;
			}
		}

		private static void HidePersistentHosts()
		{
			ProjectTypeRegistration type = CurrentType();
			if (type != null)
			{
				foreach (PersistentInspectorRegistration registration in type.PersistentInspectors)
				{
					registration.Hide?.Invoke();
				}
			}

			foreach (UiStack host in persistentHosts.Values)
			{
				if (host != null && host.GameObject != null)
				{
					host.Visible(false);
				}
			}
		}

		private static ProjectTypeRegistration CurrentType()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			return session != null ? LokrLabApi.LokrLabApi.GetProjectType(session.ProjectTypeId) : null;
		}

		private static string IdentityKey(IReadOnlyList<LabNode> nodes)
		{
			StringBuilder key = new StringBuilder();
			key.Append(LabShell.ActiveWorkspaceName).Append('#');
			if (nodes == null || nodes.Count == 0)
			{
				return key.ToString();
			}

			LabNode primary = LokrLabApi.LokrLabApi.Selection.Primary;
			if (primary != null)
			{
				key.Append(primary.Id).Append('|').Append(primary.Kind).Append(';');
			}

			for (int i = 0; i < nodes.Count; i++)
			{
				LabNode node = nodes[i];
				if (node != null)
				{
					key.Append(node.Id).Append('/').Append(node.Kind).Append(';');
				}
			}

			return key.ToString();
		}
	}
}
