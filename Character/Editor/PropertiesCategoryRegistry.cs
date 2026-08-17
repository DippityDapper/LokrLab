using System.Collections.Generic;
using LokrLab.Editor.General;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's own category registry -- one entry per left-nav button, each owning one section of the shared right-hand inspector.</summary>
	/// <remarks>Mirrors CharacterCreatorAPI's own resolver-chain shape one level down: a category is registered once, by name, and PropertiesWorkstationScene builds/shows it without needing to know what's inside. Exists so adding a new Properties field group later means adding one more RegisterCategory call, not touching the workstation's own layout -- the concrete problem this replaced (a fixed row of hardcoded side-by-side panels that ran out of room) is exactly what motivated this.</remarks>
	internal static class PropertiesCategoryRegistry
	{
		/// <summary>Builds this category's section into inspectorContent, returning the section's own root element for later show/hide.</summary>
		internal delegate UiElement CategoryBuildFn(Transform inspectorContent, Font font);
		/// <summary>Refreshes this category's fields from the given profile (or clears them if null). Called for every registered category on every character change, regardless of which one is currently visible -- the same "refresh everything, guard on built" pattern the rest of this plugin's panels already use.</summary>
		internal delegate void CategoryRefreshFn(CharacterProfile profile);

		/// <summary>One registered category: its nav button label plus its build/refresh callbacks.</summary>
		internal sealed class CategoryEntry
		{
			internal string Name;
			internal string DisplayLabel;
			internal CategoryBuildFn Build;
			internal CategoryRefreshFn Refresh;
		}

		private static readonly List<CategoryEntry> categories = new List<CategoryEntry>();
		/// <summary>Every registered category, in registration order.</summary>
		internal static IReadOnlyList<CategoryEntry> Categories => categories;

		/// <summary>Registers a Properties category, replacing any existing one of the same name.</summary>
		/// <remarks>Safe to call every CharacterLabScene.Open(), the same as CharacterCreatorAPI.RegisterWorkstation -- without the RemoveAll, PropertiesWorkstationScene.RegisterBuiltInCategories() (called from Open() every time the Lab is entered) would append duplicate entries on every re-entry, since this list is plain static state that outlives the additive Lab scene's own unload/reload. Found 2026-08-11 after exactly that: PropertiesWorkstationScene.Build() loops every registered entry once, so N duplicate "States"/"Levels"/etc. registrations meant N built-and-orphaned UI sections per category, with only the last one ever tracked for show/hide -- the earlier ones stayed alive, un-hidden, behind whatever category was actually selected.</remarks>
		internal static void RegisterCategory(string name, string displayLabel, CategoryBuildFn build, CategoryRefreshFn refresh)
		{
			categories.RemoveAll(c => c.Name == name);
			categories.Add(new CategoryEntry { Name = name, DisplayLabel = displayLabel, Build = build, Refresh = refresh });
		}
	}
}
