using System.Collections.Generic;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Shell
{
	/// <summary>Renders LokrLabApi menu registrations as a toolbar of dropdowns.</summary>
	/// <remarks>The Project Browser and the dock shell each own a bar. One static toolbar would make File disappear on the empty state, which is where Import Legacy Pack has to live.</remarks>
	internal static class LabMenuBar
	{
		private static readonly List<Instance> instances = new List<Instance>();
		private static UiModal aboutModal;

		/// <summary>Drops menu-bar widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			instances.Clear();
			aboutModal = null;
		}

		/// <summary>Builds another menu bar into parent and returns the toolbar widget.</summary>
		internal static UiToolbar Build(Transform parent, Transform canvas, UiTheme theme)
		{
			Instance instance = new Instance
			{
				Dropdown = UiContextMenu.Create(canvas, theme),
				Bar = UiToolbar.Create(parent, theme)
			};
			foreach (MenuRegistration menu in LokrLabApi.LokrLabApi.Menus)
			{
				string menuName = menu.Name;
				instance.Bar.AddButton("menu-" + menuName, menuName, () => OpenMenu(instance, menuName)).FixedWidth(90f);
			}

			instance.Bar.AddSpacer();
			instance.Title = instance.Bar.AddLabel("LoKR Lab");
			instance.Title.FixedWidth(180f);

			instances.Add(instance);
			RefreshBar(instance);
			return instance.Bar;
		}

		/// <summary>Hides top-level menus that currently have no visible items and refreshes the title star.</summary>
		internal static void Refresh()
		{
			for (int i = 0; i < instances.Count; i++)
			{
				RefreshBar(instances[i]);
			}

			RefreshTitle();
		}

		/// <summary>Sets each bar's LoKR Lab label, including the dirty <c>*</c>.</summary>
		internal static void RefreshTitle()
		{
			string text = LabSaveUx.IsDirty ? "LoKR Lab *" : "LoKR Lab";
			for (int i = 0; i < instances.Count; i++)
			{
				Instance instance = instances[i];
				if (instance != null && instance.Title != null)
				{
					instance.Title.SetText(text);
				}
			}
		}

		private static void RefreshBar(Instance instance)
		{
			if (instance == null || instance.Bar == null)
			{
				return;
			}

			foreach (MenuRegistration menu in LokrLabApi.LokrLabApi.Menus)
			{
				if (instance.Bar.TryGetButton("menu-" + menu.Name, out UiButton button))
				{
					button.Visible(CountVisible(menu) > 0);
				}
			}
		}

		private static void OpenMenu(Instance instance, string menuName)
		{
			MenuRegistration menu = null;
			foreach (MenuRegistration candidate in LokrLabApi.LokrLabApi.Menus)
			{
				if (candidate.Name == menuName)
				{
					menu = candidate;
					break;
				}
			}

			if (menu == null || instance == null || instance.Dropdown == null)
			{
				return;
			}

			instance.Dropdown.ClearItems();
			int added = 0;
			foreach (MenuItemRegistration item in menu.Items)
			{
				if (item.IsVisible != null && !item.IsVisible())
				{
					continue;
				}

				bool enabled = item.IsEnabled == null || item.IsEnabled();
				instance.Dropdown.AddItem(item.Label, item.OnClick, enabled);
				added++;
			}

			if (added == 0)
			{
				return;
			}

			instance.Dropdown.Show(Input.mousePosition);
		}

		private static int CountVisible(MenuRegistration menu)
		{
			int count = 0;
			foreach (MenuItemRegistration item in menu.Items)
			{
				if (item.IsVisible == null || item.IsVisible())
				{
					count++;
				}
			}

			return count;
		}

		/// <summary>Shows the Help → About modal.</summary>
		internal static void ShowAbout()
		{
			if (aboutModal == null)
			{
				Transform canvas = LabShell.Canvas;
				if (canvas == null)
				{
					return;
				}

				aboutModal = UiModal.Create(canvas, UiTheme.Default, "About LoKR Lab", 520f, 220f);
				UiStack content = UiStack.Vertical(aboutModal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
				aboutModal.Add(content);
				content.Add(UiLabel.Create(content.ContentTransform,
					"In-game editor shell. Character Lab and Ability Lab register as project types. "
					+ "See each plugin's docs/ for authoring details.",
					UiTheme.Default, 14, TextAnchor.UpperLeft).Grow());
				content.Add(UiButton.Create(content.ContentTransform, "Close", aboutModal.Hide, primary: true).FixedHeight(36f));
			}

			aboutModal.Show();
		}

		/// <summary>One rendered menu bar (Project Browser or dock shell).</summary>
		private sealed class Instance
		{
			/// <summary>Dropdown shown when a top-level menu button is clicked.</summary>
			internal UiContextMenu Dropdown;

			/// <summary>The File / Edit / View / Help toolbar for this screen.</summary>
			internal UiToolbar Bar;

			/// <summary>Right-side LoKR Lab title, including the dirty star.</summary>
			internal UiLabel Title;
		}
	}
}
