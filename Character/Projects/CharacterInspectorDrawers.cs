using System.Globalization;
using System.IO;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Projects
{
	/// <summary>Character project type's registered inspector drawers, keyed by LabNode.Kind.</summary>
	/// <remarks>
	/// These are the shell-side port of InspectorPanel's four sections (Part / AnimationClip /
	/// Frame / Reference) plus identity drawers for Character / Rig / Animator. Live pose, pivot,
	/// visibility, events, and attach-point editing stay on InspectorPanel — that panel refreshes
	/// on every playback tick with row reuse and per-field focus-skip, and rebuilding those
	/// widgets from a drawer callback would regress that. Shell drawers read session / rig.json
	/// and jump to Properties or Animator for the fields that need live RigEditorScene state.
	/// </remarks>
	internal static class CharacterInspectorDrawers
	{
		/// <summary>Registers the built-in Character drawers on the project type (priority 0).</summary>
		internal static void Register(ProjectTypeRegistration registration)
		{
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Character, DrawCharacter);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Rig, DrawRig);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Part, DrawPart);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Animator, DrawAnimator);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.AnimationClip, DrawAnimationClip);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Frame, DrawFrame);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Reference, DrawReference);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.PropertiesCategory, DrawPropertiesCategory);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Abilities, DrawAbilities);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.AbilityRef, DrawAbilityRef);
			registration.RegisterInspectorDrawer(CharacterNodeKinds.Aliases, DrawAliases);
		}

		/// <summary>Character identity from the open session / profile.</summary>
		internal static void DrawCharacter(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Character: " + Display(session != null ? session.DisplayName : node.DisplayName));
			LabClipboard.AddIdRow(section, session != null ? session.Id : node.Id);
			if (session != null && !string.IsNullOrEmpty(session.FolderPath))
			{
				AddLine(section, "Folder: " + session.FolderPath, 11);
			}

			CharacterProfile profile = CharacterSession.Profile;
			if (profile != null)
			{
				AddLine(section, "Type: " + profile.EntityType + " / " + profile.Tier);
				if (!string.IsNullOrEmpty(profile.Model))
				{
					AddLine(section, "Combat prefab: " + profile.Model);
				}
			}

			if (session != null)
			{
				int parts = CharacterRigOutline.ReadPartNames(session.FolderPath).Count;
				int clips = CharacterRigOutline.ReadAnimationNames(session.FolderPath).Count;
				AddLine(section, parts.ToString(CultureInfo.InvariantCulture) + " part(s), "
					+ clips.ToString(CultureInfo.InvariantCulture) + " clip(s)");
			}

			AddLine(section, "Name, stats, skills, and portraits are edited in Properties.", 11);
			AddButton(section, "Open Properties", () => Lab.ActivateWorkspace("Properties"));
			DrawLeftoverIdRename(section, session);
		}

		/// <summary>Slug / alias / Rename when the open folder is not already a <c>slug_token</c> id.</summary>
		private static void DrawLeftoverIdRename(UiStack section, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.Id) || LabSlugIds.LooksLikeSlugTokenId(session.Id))
			{
				return;
			}

			AddLine(section, "This folder still uses a leftover id. Rename onto slug_token to match new creates.", 11);
			string displayName = session.DisplayName;
			string initialSlug = LabSlugIds.LegalizeSlug(
				!string.IsNullOrEmpty(displayName) ? displayName : session.Id,
				"character");
			string initialAlias = LokrCharacterLoader.LabAliases.FindKeyForId(
				LokrCharacterLoader.LabAliases.Load(session.FolderPath), session.Id) ?? initialSlug;
			LabSlugCreateFields.BuildRename(
				section.ContentTransform,
				"character",
				displayName,
				initialSlug,
				initialAlias,
				out UiTextField slugField,
				out UiTextField aliasField,
				out _,
				out _,
				out _);
			UiLabel errorLabel = UiLabel.Create(section.ContentTransform, string.Empty, UiTheme.Default, 12);
			section.Add(errorLabel.FixedHeight(22f));
			string folder = session.FolderPath;
			string name = displayName;
			AddButton(section, "Rename", () =>
			{
				string error = LabSlugCreateFields.ValidateRename(
					slugField, aliasField, "character", name, out string slug, out string alias);
				if (error != null)
				{
					errorLabel.SetText(error);
					return;
				}

				string oldFolder = folder;
				if (!CharacterIdentityRekey.TryApplyToSlugToken(oldFolder, slug, alias, out string newFolder, out error))
				{
					errorLabel.SetText(error);
					return;
				}

				HomeWorkstationScene.RemoveRecentCharacter(oldFolder);
				RecentProjectsStore.Remove(oldFolder);
				CharacterLabScene.ReloadOpenProject(CharacterProjectType.Id, newFolder, "character:" + Path.GetFileName(newFolder));
			});
		}

		/// <summary>Rig folder summary — part count. Add Part stays on the Node Tree context menu.</summary>
		internal static void DrawRig(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Rig");
			int parts = session != null ? CharacterRigOutline.ReadPartNames(session.FolderPath).Count : 0;
			AddLine(section, parts.ToString(CultureInfo.InvariantCulture) + " part(s)");
			AddLine(section, "Right-click Rig in the Node Tree to add a part on an unauthored rig. Layer, pivot, and visibility are edited in the Animator.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Port of InspectorPanel's Part section: name and rest offsets from rig.json.</summary>
		internal static void DrawPart(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			string name = PartName(node);
			AddTitle(section, "Part: " + Display(name));
			AddLine(section, "Id: " + node.Id);

			if (session != null && CharacterRigOutline.TryReadPart(session.FolderPath, name, out float offsetX, out float offsetY))
			{
				AddLine(section, "Offset: " + FormatFloat(offsetX) + ", " + FormatFloat(offsetY));
			}

			AddLine(section, "Layer, visibility, pivot, Replace, and Remove from Clip stay in the Animator — those fields refresh on every playback tick and cannot be rebuilt from this drawer.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Animator folder summary — clip count.</summary>
		internal static void DrawAnimator(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Animator");
			int clips = session != null ? CharacterRigOutline.ReadAnimationNames(session.FolderPath).Count : 0;
			AddLine(section, clips.ToString(CultureInfo.InvariantCulture) + " clip(s)");
			AddLine(section, "Right-click Animator in the Node Tree to add a clip on an unauthored rig. Frame timing and poses are edited in the Animator.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Port of InspectorPanel's Animation section: name and frame count from rig.json.</summary>
		internal static void DrawAnimationClip(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			string name = ClipName(node);
			AddTitle(section, "Animation: " + Display(name));
			AddLine(section, "Id: " + node.Id);

			if (session != null)
			{
				int frames = CharacterRigOutline.ReadAnimationFrameCount(session.FolderPath, name);
				if (frames >= 0)
				{
					AddLine(section, frames.ToString(CultureInfo.InvariantCulture) + " frame(s)");
				}
			}

			AddLine(section, "Delete Animation and per-frame editing stay in the Animator.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Port of InspectorPanel's Frame section — live Duration/Easing/Events stay in the Animator.</summary>
		internal static void DrawFrame(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Frame: " + Display(node.DisplayName));
			AddLine(section, "Id: " + node.Id);
			AddLine(section, "Duration, easing, events, attach points, copy/paste, and per-frame part poses refresh on every playback tick in the Animator inspector. This drawer does not rebuild those rows.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Port of InspectorPanel's Reference section — overlay transform stays in the Animator.</summary>
		internal static void DrawReference(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Reference: " + Display(node.DisplayName));
			AddLine(section, "Id: " + node.Id);
			AddLine(section, "Character, pose, position, rotation, visibility, and opacity are edited on the scale-reference overlay in the Animator. Scale stays locked so the overlay remains a known in-game size.", 11);
			AddButton(section, "Open Animator", () => Lab.ActivateWorkspace("Animator"));
		}

		/// <summary>Properties category — the persistent host in InspectorDock shows the real fields.</summary>
		internal static void DrawPropertiesCategory(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, Display(node.DisplayName));
			AddLine(section, "Fields for this category are shown below. They persist across selection so PersistAndSync can refresh them in place.", 11);
		}

		private static string PartName(LabNode node)
		{
			return node.Payload as string ?? node.DisplayName;
		}

		/// <summary>Folder of ability ids this character references.</summary>
		internal static void DrawAbilities(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			AddTitle(section, "Abilities");
			int count = node != null && node.Children != null ? node.Children.Count : 0;
			AddLine(section, count.ToString(CultureInfo.InvariantCulture) + " referenced ability id(s) from skills / defaultSkill / skillProgression.");
			AddLine(section, "Select a child and Open in Ability Library (or double-click) to edit it. Ability Lab does not need to be a Character workspace.", 11);
		}

		/// <summary>This character folder's aliases.json list.</summary>
		internal static void DrawAliases(LabNode node, ProjectSession session, Transform contentParent)
		{
			string folder = session != null ? session.FolderPath : null;
			if (node != null && node.Payload is string payload && !string.IsNullOrEmpty(payload))
			{
				folder = payload;
			}

			LabAliasesInspector.Draw(folder, contentParent);
		}

		/// <summary>One referenced ability id plus a jump into the Ability Library project.</summary>
		internal static void DrawAbilityRef(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = Section(contentParent);
			string abilityId = node != null ? (node.Payload as string ?? node.DisplayName) : "-";
			AddTitle(section, "Ability: " + Display(abilityId));
			LabClipboard.AddIdRow(section, abilityId);
			AddLine(section, "Referenced by this character. The Ability Library is a separate singleton project.");
			bool libraryPresent = LokrLabApi.LokrLabApi.GetProjectType(LokrLabApi.LokrLabApi.AbilityLibraryTypeId) != null;
			if (!libraryPresent)
			{
				AddLine(section, "Ability Lab is not installed — this is an id only.", 11);
				return;
			}

			AddButton(section, "Open in Ability Library", () => CharacterNodeContributors.JumpToAbility(abilityId));
		}

		private static string ClipName(LabNode node)
		{
			return node.Payload as string ?? node.DisplayName;
		}

		private static string Display(string value)
		{
			return string.IsNullOrEmpty(value) ? "-" : value;
		}

		private static string FormatFloat(float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		private static UiStack Section(Transform parent)
		{
			return UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 0f);
		}

		private static void AddTitle(UiStack section, string text)
		{
			section.Add(UiLabel.Create(section.ContentTransform, text, UiTheme.Default, UiTheme.Default.TitleFontSize)
				.FixedHeight(26f));
		}

		private static void AddLine(UiStack section, string text, int fontSize = 13)
		{
			UiLabel label = UiLabel.Create(section.ContentTransform, text, UiTheme.Default, fontSize, TextAnchor.UpperLeft);
			label.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
			label.Text.verticalOverflow = VerticalWrapMode.Overflow;
			int lines = 1;
			if (text != null)
			{
				lines = 1 + (text.Length / 42);
			}
			section.Add(label.FixedHeight(22f * lines));
		}

		private static void AddButton(UiStack section, string label, UnityEngine.Events.UnityAction onClick)
		{
			section.Add(UiButton.Create(section.ContentTransform, label, onClick, primary: false).FixedHeight(28f));
		}
	}
}
