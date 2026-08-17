using System.Collections.Generic;
using LokrCharacterLab;
using LokrLab;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Editor
{
	/// <summary>Home's hub: a read-only summary of whichever character is currently active, plus the buttons that reach every registered workstation (CharacterCreatorAPI.RegisterWorkstation) -- built-in or third-party alike, no special case for either.</summary>
	internal static class HomeNavPanel
	{
		private static readonly Rect Region = EditorLayout.HomeNavRegion;

		private static UiLabel idValueLabel;
		private static UiLabel nameValueLabel;
		private static UiList<CharacterCreatorAPI.WorkstationEntry> workstationList;

		/// <summary>Builds the summary labels, the dynamic workstation-button list, and the Switch Character button.</summary>
		/// <remarks>workstationList is deliberately non-scrollable: a scrollable UiList has no self-sizing behavior of its own (it just fills whatever height its parent hands it, per UiStack's own remarks), which collapses to zero inside this panel's VerticalLayoutGroup without an explicit FixedHeight/Grow(). Non-scrollable gets a ContentSizeFitter instead, sizing this list to exactly its buttons' combined height -- correct here since the workstation count is small and curated by CharacterCreatorAPI.RegisterWorkstation, not user-driven/unbounded like CharacterListPanel's recent-characters list.</remarks>
		internal static void Build(Transform screenRoot, Font font)
		{
			UiPanel panel = UiPanel.Create(screenRoot, CharacterLabUi.NestedPanelTheme, "Home", region: Region);
			UiStack content = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 6f, padding: 12f);
			panel.Add(content);

			content.Add(UiLabel.Create(content.ContentTransform, "Character ID:").FixedHeight(20f));
			idValueLabel = UiLabel.Create(content.ContentTransform, "-");
			content.Add(idValueLabel.FixedHeight(24f));

			content.Add(UiLabel.Create(content.ContentTransform, "Name:").FixedHeight(20f));
			nameValueLabel = UiLabel.Create(content.ContentTransform, "-");
			content.Add(nameValueLabel.FixedHeight(24f));

			workstationList = UiList<CharacterCreatorAPI.WorkstationEntry>.Create(content.ContentTransform, spacing: 4f, padding: 0f, scrollable: false);
			content.Add(workstationList);

			UiButton reload = UiButton.Create(content.ContentTransform, "Reload in Game", HomeWorkstationScene.OnReloadInGameClicked, primary: false);
			content.Add(reload.FixedHeight(32f));
			LabHoverInfo.Bind(reload.GameObject, "character.home.Reload");
			content.Add(UiButton.Create(content.ContentTransform, "‹ Switch Character", HomeWorkstationScene.OnSwitchCharacterClicked, primary: false).FixedHeight(32f));
		}

		/// <summary>Updates the summary labels from a profile (or blanks them if null), and rebuilds the workstation-button list from CharacterCreatorAPI's current registry, gated on whether a character is loaded.</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			if (idValueLabel == null || nameValueLabel == null || workstationList == null
				|| idValueLabel.GameObject == null || nameValueLabel.GameObject == null
				|| workstationList.GameObject == null)
			{
				return;
			}

			idValueLabel.SetText(profile != null ? profile.Id : "-");
			nameValueLabel.SetText(profile != null && !string.IsNullOrEmpty(profile.Name) ? profile.Name : "-");

			bool characterLoaded = profile != null;
			List<CharacterCreatorAPI.WorkstationEntry> visible = new List<CharacterCreatorAPI.WorkstationEntry>();
			foreach (CharacterCreatorAPI.WorkstationEntry entry in CharacterCreatorAPI.Workstations)
			{
				if (!entry.RequiresCharacterLoaded || characterLoaded)
				{
					visible.Add(entry);
				}
			}
			workstationList.SetItems(visible, entry => entry.Name, (parent, entry) =>
				UiButton.Create(parent, entry.DisplayLabel, () => CharacterWorkstations.Show(entry.Name), primary: false).FixedHeight(32f));
		}
	}
}
