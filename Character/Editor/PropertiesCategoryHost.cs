using System.Collections.Generic;
using LokrLab.Editor.General;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Persistent Properties category sections for the shell inspector — build once, show/hide by name.</summary>
	/// <remarks>
	/// Same "build everything, toggle Visible" pattern as PropertiesWorkstationScene, so
	/// PersistAndSync → RefreshAll still hits live widget refs. InspectorDock does not rebuild
	/// these on selection identity change; it only asks this host to Show the named category.
	/// </remarks>
	internal static class PropertiesCategoryHost
	{
		private static readonly Dictionary<string, UiElement> sections = new Dictionary<string, UiElement>();
		private static UiStack root;
		private static string currentCategory;

		/// <summary>Builds every registered category into parent. Safe to call once per lab session.</summary>
		/// <remarks>
		/// InspectorDock's properties host is already a Grow() scroll view. A stretch-to-fill
		/// ScrollRect inside it reports no preferred height and collapses the form — the same
		/// nested-scroll bug as InspectorPanel before BuildInto.
		/// </remarks>
		internal static void Build(Transform parent)
		{
			if (!IsLive())
			{
				ResetSession();
			}

			if (root != null)
			{
				return;
			}

			root = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f, scrollable: false);
			sections.Clear();
			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				UiElement section = category.Build(root.ContentTransform, Lab.DefaultFont);
				root.Add(section);
				sections[category.Name] = section;
				section.GameObject.SetActive(false);
			}

			RefreshAll(CharacterSession.Profile);
		}

		/// <summary>Shows the named category and hides the others. Accepts a registry Name or DisplayLabel.</summary>
		/// <returns>True when a matching section was shown.</returns>
		internal static bool Show(string name)
		{
			if (!IsLive())
			{
				ResetSession();
				return false;
			}

			string key = ResolveKey(name);
			if (key == null)
			{
				return false;
			}

			currentCategory = key;
			root.GameObject.SetActive(true);
			foreach (KeyValuePair<string, UiElement> section in sections)
			{
				section.Value.GameObject.SetActive(section.Key == key);
			}

			return true;
		}

		/// <summary>Maps a Node Tree payload or display label onto a registered category Name.</summary>
		private static string ResolveKey(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			if (sections.ContainsKey(name))
			{
				return name;
			}

			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				if (category.DisplayLabel == name && sections.ContainsKey(category.Name))
				{
					return category.Name;
				}
			}

			return null;
		}

		/// <summary>Hides the host without destroying sections.</summary>
		internal static void Hide()
		{
			if (!IsLive())
			{
				ResetSession();
				return;
			}

			root.GameObject.SetActive(false);
		}

		/// <summary>Refreshes every category from the current profile.</summary>
		internal static void RefreshAll(CharacterProfile profile)
		{
			if (!IsLive())
			{
				ResetSession();
				return;
			}

			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				category.Refresh(profile);
			}
		}

		/// <summary>Drops cached widgets after the lab scene is torn down.</summary>
		internal static void ResetSession()
		{
			sections.Clear();
			root = null;
			currentCategory = null;
		}

		/// <summary>True when the host stack still exists in the current lab scene.</summary>
		/// <remarks>Close Project destroys InspectorDock persistent hosts without going through LabClosing. The C# refs stay non-null (Unity fake-null) until this check, and a RefreshAll against those lists is the Close-then-reopen NRE.</remarks>
		private static bool IsLive()
		{
			return root != null && root.GameObject != null;
		}
	}
}
