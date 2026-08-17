using LokrLab;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's left column: one button per PropertiesCategoryRegistry entry, selecting which section the right-hand inspector shows.</summary>
	internal static class PropertiesNavPanel
	{
		private static readonly Rect Region = EditorLayout.PropertiesNavRegion;

		private static UiList<PropertiesCategoryRegistry.CategoryEntry> categoryList;

		/// <summary>Builds the category button list.</summary>
		/// <remarks>Non-scrollable, the same reasoning as HomeNavPanel's own workstation-button list -- a small, curated set of categories, not an open-ended one.</remarks>
		internal static void Build(Transform screenRoot, Font font)
		{
			UiPanel panel = UiPanel.Create(screenRoot, CharacterLabUi.NestedPanelTheme, "Categories", region: Region);
			UiStack content = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 4f, padding: 12f);
			panel.Add(content);

			categoryList = UiList<PropertiesCategoryRegistry.CategoryEntry>.Create(content.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			content.Add(categoryList);

			categoryList.SetItems(PropertiesCategoryRegistry.Categories, entry => entry.Name, (parent, entry) =>
				UiButton.Create(parent, entry.DisplayLabel, () => PropertiesWorkstationScene.SelectCategory(entry.Name), primary: false).FixedHeight(32f));
		}
	}
}
