using System.Collections.Generic;
using System.IO;
using LokrLab.Editor.General;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Properties workstation's Portraits category: one row per CharacterAPI.RegisterPortraitResolver slot (MINI/BIG/BANNER/MAP/MAPMINI/CHALLENGE) over the Mods/*/Portraits/&lt;id&gt;/&lt;id&gt;_&lt;slot&gt;.png convention.</summary>
	/// <remarks>
	/// Doesn't touch character.json or rlheroes.txt at all -- portrait resolution is pure file-convention lookup (PortraitPatches.RegisterDefaults), so this panel is just a friendlier way to place files the resolver already knows how to find, and works identically for imported and freshly-created characters alike.
	///
	/// MAP and CHALLENGE get a "Use custom image" toggle instead of a plain Browse/Clear pair (added 2026-08-11) -- both slots already run the vanilla animated exoskeleton Portrait/StandStatic pose first and only swap in a flat image when CharacterAPI.ResolvePortrait finds one (MapHeroBarPortraitComponent_SetHero_Patch, RewardViewComponent_SetTargetPortrait_Patch, UIBuffStoreItem_SetItem_Patch -- see PortraitPatches.cs), so "no file" already meant "animated portrait" at the resolver level; this toggle just makes that choice explicit instead of implicit-via-file-presence, and turning it off is exactly RemovePortrait (not a separate persisted flag). MINI/BIG/BANNER (flat asset-bundle sprite lookups, DataHelper.cs) and MAPMINI (an ExoSkeleton part *swap*, not a rendered portrait pose -- see ExoSkeletonDataPatches.cs) have no such animated alternative, so they keep the plain Browse/Clear shape.
	/// </remarks>
	internal static class CharacterPortraitsPanel
	{
		private static readonly string[] PlainSlots = { "MINI", "BIG", "BANNER", "MAPMINI" };
		private static readonly string[] ToggleableSlots = { "MAP", "CHALLENGE" };

		private static readonly Dictionary<string, UiLabel> statusLabels = new Dictionary<string, UiLabel>();
		private static readonly Dictionary<string, UiToggle> customImageToggles = new Dictionary<string, UiToggle>();
		private static readonly Dictionary<string, UiButton> browseButtons = new Dictionary<string, UiButton>();
		private static bool suppressToggleEvents;

		/// <summary>Builds every portrait slot's row into the shared inspector content.</summary>
		internal static UiElement Build(Transform inspectorContent, Font font)
		{
			UiStack section = UiStack.Vertical(inspectorContent, UiTheme.Default, spacing: 6f, padding: 0f);

			statusLabels.Clear();
			customImageToggles.Clear();
			browseButtons.Clear();

			foreach (string slot in PlainSlots)
			{
				BuildPlainRow(section, slot);
			}
			foreach (string slot in ToggleableSlots)
			{
				BuildToggleableRow(section, slot);
			}

			return section;
		}

		private static void BuildPlainRow(UiStack section, string slot)
		{
			UiStack row = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(row.FixedHeight(28f));

			row.Add(UiLabel.Create(row.ContentTransform, slot).FixedWidth(80f));

			UiLabel status = UiLabel.Create(row.ContentTransform, "-");
			statusLabels[slot] = status;
			row.Add(status.Grow());

			row.Add(UiButton.Create(row.ContentTransform, "Browse", () => OnBrowseClicked(slot), primary: false).FixedWidth(70f));
			row.Add(UiButton.Create(row.ContentTransform, "Clear", () => OnClearClicked(slot), primary: false).FixedWidth(50f));
			LabHoverInfo.Bind(row.GameObject, "character.portraits." + slot);
		}

		/// <summary>Builds a MAP/CHALLENGE row: label, "Use custom image" toggle, and a Browse button shown only while the toggle is on.</summary>
		private static void BuildToggleableRow(UiStack section, string slot)
		{
			UiStack row = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(row.FixedHeight(28f));

			row.Add(UiLabel.Create(row.ContentTransform, slot).FixedWidth(80f));

			UiToggle toggle = UiToggle.Create(row.ContentTransform, "Use custom image (off = animated portrait)", false);
			toggle.OnValueChanged(value => OnCustomImageToggleChanged(slot, value));
			toggle.Grow();
			row.Add(toggle);
			customImageToggles[slot] = toggle;
			LabHoverInfo.Bind(row.GameObject, "character.portraits." + slot);

			UiButton browse = UiButton.Create(row.ContentTransform, "Browse", () => OnBrowseClicked(slot), primary: false).FixedWidth(70f);
			row.Add(browse);
			browseButtons[slot] = browse;
		}

		/// <summary>Refreshes every slot's status/toggle state from disk. A no-op if profile is null (nothing to check against).</summary>
		internal static void Refresh(CharacterProfile profile)
		{
			if (profile == null || statusLabels.Count == 0)
			{
				return;
			}
			foreach (string slot in PlainSlots)
			{
				if (!statusLabels.TryGetValue(slot, out UiLabel status) || status?.GameObject == null)
				{
					continue;
				}
				status.SetText(File.Exists(SlotPath(profile.Id, slot)) ? "Set" : "Not set");
			}
			suppressToggleEvents = true;
			foreach (string slot in ToggleableSlots)
			{
				if (!customImageToggles.TryGetValue(slot, out UiToggle toggle) || toggle?.GameObject == null)
				{
					continue;
				}
				if (!browseButtons.TryGetValue(slot, out UiButton browse) || browse?.GameObject == null)
				{
					continue;
				}
				bool hasCustomImage = File.Exists(SlotPath(profile.Id, slot));
				toggle.SetValueSilently(hasCustomImage);
				browse.Visible(hasCustomImage);
			}
			suppressToggleEvents = false;
		}

		/// <summary>Clears cached widget refs after the lab scene unloads.</summary>
		internal static void ResetSession()
		{
			statusLabels.Clear();
			customImageToggles.Clear();
			browseButtons.Clear();
		}

		/// <summary>The file path this panel writes/checks for one character's slot -- inside that character's own folder (CharactersRoot/&lt;id&gt;/portraits/), which LokrCharacterLoader's PortraitPatches resolver checks first, ahead of the flat mod-wide Portraits/&lt;id&gt;/ convention it falls back to for hand-authored, non-Lab mods.</summary>
		internal static string SlotPath(string characterId, string slot)
		{
			return Path.Combine(CharacterLabPaths.CharacterPortraitsFolder(characterId), characterId + "_" + slot + ".png");
		}

		private static void OnCustomImageToggleChanged(string slot, bool useCustomImage)
		{
			if (suppressToggleEvents)
			{
				return;
			}
			if (useCustomImage)
			{
				OnBrowseClicked(slot);
			}
			else
			{
				HomeWorkstationScene.RemovePortrait(slot);
			}
		}

		private static void OnBrowseClicked(string slot)
		{
			if (HomeWorkstationScene.CurrentProfile == null)
			{
				return;
			}
			FileBrowserPanel.OpenForFile("Select " + slot + " portrait (PNG)", "", new[] { ".png" },
				selected => HomeWorkstationScene.SetPortrait(slot, selected));
		}

		private static void OnClearClicked(string slot)
		{
			HomeWorkstationScene.RemovePortrait(slot);
		}
	}
}
