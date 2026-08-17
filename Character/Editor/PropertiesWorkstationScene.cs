using System.Collections.Generic;
using LokrLab.Editor.General;
using LokrLab;
using SimpleUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's own orchestrator: a category nav (PropertiesNavPanel) on the left and a shared inspector on the right, showing whichever PropertiesCategoryRegistry section is currently selected. HomeWorkstationScene stays the state owner; this screen and its categories only present/edit it.</summary>
	/// <remarks>Replaced a fixed row of hardcoded side-by-side panels 2026-08-11 -- that shape ran out of room once portraits/stats/appearance/skills/sound/level-chain/states all needed their own space. Every category's section is built once, up front, into the same scrollable inspector content; switching categories just toggles which section's GameObject is active, the same "build everything, toggle Visible" pattern InspectorPanel already uses in the Animator.</remarks>
	internal static class PropertiesWorkstationScene
	{
		private static readonly Dictionary<string, UiElement> sections = new Dictionary<string, UiElement>();
		private static string currentCategory;

		/// <summary>Registers every built-in Properties category. Called once by CharacterLabScene.RegisterBuiltInWorkstations, before this workstation (or any other) builds -- the same timing CharacterCreatorAPI.RegisterWorkstation calls happen at, so a third-party category could register at that same point too.</summary>
		internal static void RegisterBuiltInCategories()
		{
			PropertiesCategoryRegistry.RegisterCategory("General", "General Properties", CharacterGeneralPanel.Build, CharacterGeneralPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("HeroRoster", "Hero Roster", CharacterHeroRosterPanel.Build, CharacterHeroRosterPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Portraits", "Portraits", CharacterPortraitsPanel.Build, CharacterPortraitsPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Levels", "Level Properties", CharacterLevelsPanel.Build, CharacterLevelsPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("States", "States", CharacterStatesPanel.Build, CharacterStatesPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Appearance", "Appearance", CharacterAppearancePanel.Build, CharacterAppearancePanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Skills", "Skills", CharacterSkillsPanel.Build, CharacterSkillsPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Sound", "Sound", CharacterSoundPanel.Build, CharacterSoundPanel.Refresh);
			PropertiesCategoryRegistry.RegisterCategory("Localization", "Localization", CharacterLocalizationPanel.Build, CharacterLocalizationPanel.Refresh);
		}

		/// <summary>Builds the nav panel, every registered category's section into the shared inspector, and the Home button.</summary>
		/// <remarks>The Home button reuses the generic EditorLayout.MenuBarRegion (matching MenuBarPanel's own "‹ Home" row position/style in the Animator) rather than a Properties-specific constant, since this is the only thing on that row here.</remarks>
		internal static void Build(Scene scene, Transform screenRoot)
		{
			Transform contentRoot = Lab.GetWorkstationContentRoot(screenRoot);
			PropertiesNavPanel.Build(contentRoot, Lab.DefaultFont);

			UiPanel inspectorPanel = UiPanel.Create(contentRoot, CharacterLabUi.NestedPanelTheme, "Inspector", region: EditorLayout.PropertiesInspectorRegion);
			UiStack inspectorContent = UiStack.Vertical(inspectorPanel.ContentParent, UiTheme.Default, spacing: 6f, padding: 12f, scrollable: true);
			inspectorPanel.Add(inspectorContent);

			sections.Clear();
			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				UiElement section = category.Build(inspectorContent.ContentTransform, Lab.DefaultFont);
				sections[category.Name] = section;
			}

			Rect menuBarRegion = EditorLayout.MenuBarRegion;
			float rowY = menuBarRegion.y + menuBarRegion.height * 0.5f;
			UiButton backToHomeButton = UiButton.Create(screenRoot, "‹ Home", Lab.SwitchToHome).Name("BackToHomeButton");
			backToHomeButton.Label.fontSize = 20;
			Vector2 anchorCenter = new Vector2(0.93f, rowY);
			backToHomeButton.RectTransform.anchorMin = anchorCenter;
			backToHomeButton.RectTransform.anchorMax = anchorCenter;
			backToHomeButton.RectTransform.sizeDelta = new Vector2(110f, 30f);
			backToHomeButton.RectTransform.anchoredPosition = Vector2.zero;

			if (PropertiesCategoryRegistry.Categories.Count > 0)
			{
				SelectCategory(PropertiesCategoryRegistry.Categories[0].Name);
			}
			RefreshAll(HomeWorkstationScene.CurrentProfile);
		}

		/// <summary>Shows the named category's section and hides every other one. A no-op for an unknown name.</summary>
		internal static void SelectCategory(string name)
		{
			if (!sections.ContainsKey(name))
			{
				return;
			}
			currentCategory = name;
			foreach (KeyValuePair<string, UiElement> section in sections)
			{
				section.Value.GameObject.SetActive(section.Key == name);
			}
		}

		/// <summary>Refreshes every registered category's fields from profile. A no-op if Build() hasn't run yet -- the Properties workstation builds lazily (CharacterCreatorAPI), so Home's own eager refresh can reach this before Properties has ever been shown.</summary>
		internal static void RefreshAll(CharacterProfile profile)
		{
			if (sections.Count == 0)
			{
				PropertiesCategoryHost.RefreshAll(profile);
				return;
			}
			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				category.Refresh(profile);
			}
			PropertiesCategoryHost.RefreshAll(profile);
		}

		/// <summary>Drops cached section widgets from a prior lab session whose GameObjects were unloaded.</summary>
		internal static void ResetSession()
		{
			sections.Clear();
			currentCategory = null;
			PropertiesCategoryHost.ResetSession();
			CharacterGeneralPanel.ResetSession();
			CharacterPortraitsPanel.ResetSession();
			CharacterLevelsPanel.ResetSession();
		}
	}
}
