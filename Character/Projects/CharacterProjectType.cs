using System;
using System.Collections.Generic;
using System.IO;
using LokrCharacterLab;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrLabApi;
using LokrModAPI;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Projects
{
	/// <summary>Registers the Character project type on LokrLabApi — Properties, Animator, and Sandbox.</summary>
	internal static class CharacterProjectType
	{
		/// <summary>Stable project type id written into project.json.</summary>
		internal const string Id = "character";

		/// <summary>Registers the type, its create/load callbacks, workspaces, bottom panels, and File/Edit/View/Help menus.</summary>
		internal static void Register()
		{
			PropertiesWorkstationScene.RegisterBuiltInCategories();
			ProjectTypeRegistration registration = LokrLabApi.LokrLabApi.RegisterProjectType(
				Id,
				"Character",
				"character",
				CharacterLabPaths.CharactersRoot);
			registration.CreateNew = CreateNew;
			registration.BuildCreateSheet = CharacterCreateSheet.Build;
			registration.CommitCreateSheet = CharacterCreateSheet.Commit;
			registration.Load = Load;
			registration.Delete = DeleteCharacter;
			registration.OnDeleted = OnCharacterDeleted;
			registration.ScanCategory = CharacterLabPaths.CharactersCategory;
			registration.OnSelectionChanged = SyncAnimatorSelection;
			registration.OnNodeActivated = OnNodeActivated;
			registration.ResolveDisplayName = ResolveCharacterDisplayName;
			registration.RegisterNodeTreeContributor(CharacterNodeContributors.ContributeCharacter, priority: 0);
			registration.RegisterNodeTreeContributor(CharacterNodeContributors.ContributeRig, priority: 10);
			registration.RegisterNodeTreeContributor(CharacterNodeContributors.ContributeAnimator, priority: 20);
			registration.RegisterNodeTreeContributor(CharacterNodeContributors.ContributeAbilities, priority: 30);
			registration.RegisterNodeTreeContributor(CharacterNodeContributors.ContributeAliases, priority: 35);
			registration.RegisterNodeFactory(CharacterNodeKinds.Part, new[] { CharacterNodeKinds.Rig }, CharacterNodeContributors.CreatePart);
			registration.RegisterNodeFactory(CharacterNodeKinds.AnimationClip, new[] { CharacterNodeKinds.Animator }, CharacterNodeContributors.CreateAnimationClip);
			CharacterInspectorDrawers.Register(registration);
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = "Properties",
				IconKey = "Props",
				Priority = 0,
				BuildViewport = BuildPropertiesViewport
			});
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = "Animator",
				IconKey = "Anim",
				Priority = 10,
				BuildViewport = AnimatorWorkspace.BuildViewport,
				BuildToolbar = AnimatorWorkspace.BuildToolbar,
				OnDeactivated = AnimatorWorkspace.OnDeactivated
			});
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = "Sandbox",
				IconKey = "Sandbox",
				Priority = 20,
				BuildViewport = SandboxWorkstationScene.BuildViewport,
				OnDeactivated = SandboxWorkstationScene.OnDeactivated
			});
			registration.RegisterBottomPanel("Timeline", "Timeline", TimelineBottomPanel.Build,
				(workspace, _) => workspace != null && workspace.Name == "Animator",
				unbind: TimelineBottomPanel.Unbind);
			registration.RegisterBottomPanel("Checklist", "Checklist", ReadinessChecklistPanel.BuildInto,
				(workspace, _) => workspace != null && workspace.Name == "Properties",
				refresh: HomeWorkstationScene.RefreshReadinessChecklist,
				unbind: ReadinessChecklistPanel.Unbind);
			registration.RegisterBottomPanel("History", "History", EditHistoryPanel.BuildInto,
				refresh: EditHistoryPanel.Refresh,
				unbind: EditHistoryPanel.UnbindDock);
			RegisterPersistentInspectors(registration);

			LokrLabApi.LokrLabApi.RegisterMenu("File", priority: 0);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Save Rig...", MenuBarPanel.PromptSave, priority: 10,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Import Character...", MenuBarPanel.PromptImport, priority: 20,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Slice Atlas...", MenuBarPanel.PromptSliceAtlas, priority: 30,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Sandbox", () => Lab.ActivateWorkspace("Sandbox"), priority: 40,
				isVisible: IsCharacterSession);

			LokrLabApi.LokrLabApi.RegisterMenu("Edit", priority: 10);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Undo", AnimatorHistory.Undo, priority: 0,
				isEnabled: () => AnimatorHistory.CanUndo, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Redo", AnimatorHistory.Redo, priority: 10,
				isEnabled: () => AnimatorHistory.CanRedo, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "History...", () => Lab.FocusBottomPanel("History"), priority: 20,
				isVisible: IsCharacterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Add Reference", () => RigEditorScene.AddReference(), priority: 30,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Copy Frame", RigEditorScene.CopyActiveFrame, priority: 40,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Paste Frame as New", RigEditorScene.PasteFrameAsNew, priority: 50,
				isEnabled: () => RigEditorScene.IsRuntimeLive && RigEditorScene.HasFrameClipboard,
				isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Override Frame", RigEditorScene.OverrideActiveFrame, priority: 60,
				isEnabled: () => RigEditorScene.IsRuntimeLive && RigEditorScene.HasFrameClipboard,
				isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Move Frame Left", () => RigEditorScene.MoveActiveFrame(-1), priority: 70,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Edit", "Move Frame Right", () => RigEditorScene.MoveActiveFrame(1), priority: 80,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);

			LokrLabApi.LokrLabApi.RegisterMenu("View", priority: 20);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "Timeline", () => Lab.FocusBottomPanel("Timeline"), priority: 0,
				isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "Checklist", () => Lab.FocusBottomPanel("Checklist"), priority: 10,
				isVisible: IsPropertiesWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "History", () => Lab.FocusBottomPanel("History"), priority: 20,
				isVisible: IsCharacterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "Preview", AnimatorWorkspace.TogglePreview, priority: 28,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "Refresh Preview", RigEditorScene.RebuildPreview, priority: 30,
				isEnabled: () => RigEditorScene.IsRuntimeLive, isVisible: IsAnimatorWorkspace);
		}

		/// <summary>True when the open session is a Character project (gates Character-only menus).</summary>
		private static bool IsCharacterSession()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			return session != null && session.ProjectTypeId == Id;
		}

		/// <summary>True when a Character project is on the Animator workspace.</summary>
		private static bool IsAnimatorWorkspace()
		{
			return IsCharacterSession() && Lab.ActiveWorkspaceName == "Animator";
		}

		/// <summary>True when a Character project is on the Properties workspace.</summary>
		private static bool IsPropertiesWorkspace()
		{
			return IsCharacterSession() && Lab.ActiveWorkspaceName == "Properties";
		}

		private static ProjectSession CreateNew()
		{
			HomeWorkstationScene.OnCreateCharacterConfirmed();
			if (string.IsNullOrEmpty(CharacterSession.Folder))
			{
				return null;
			}

			WriteProjectMarker(CharacterSession.Folder);
			return CharacterProjectSession.FromLoaded();
		}

		private static string DeleteCharacter(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return "Folder does not exist.";
			}

			try
			{
				Directory.Delete(folder, true);
			}
			catch (Exception ex)
			{
				return ex.Message;
			}

			HomeWorkstationScene.RemoveRecentCharacter(folder);
			return null;
		}

		/// <summary>Reloads merged unit-definition / roster content after a Character project is deleted.</summary>
		/// <remarks>
		/// Without this, deleting a vanilla-override folder (e.g. a Gerald override) left the last
		/// merged state in memory — the game kept using the deleted override until a full restart
		/// instead of falling back to vanilla or another remaining override.
		/// </remarks>
		private static void OnCharacterDeleted(string folder)
		{
			CharacterAPI.ReloadResult result = CharacterAPI.ReloadLabContent(CharacterAPI.ReloadScope.All);
			if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
			{
				LokrCharacterLabPlugin.Log.LogWarning("Reload after character delete: " + result.ErrorMessage);
			}
		}

		private static ProjectSession Load(string folder)
		{
			HomeWorkstationScene.OnLoadCharacterSelected(folder);
			if (string.IsNullOrEmpty(CharacterSession.Folder))
			{
				return null;
			}

			WriteProjectMarker(CharacterSession.Folder);
			return CharacterProjectSession.FromLoaded();
		}

		/// <summary>Moves leftover Characters/ and Mods/LokrLab character roots, then writes project.json onto Lab character folders that lack one.</summary>
		internal static void MigrateExistingCharacterFolders()
		{
			CharacterLabPaths.MigrateLegacyCharactersRoot();
			int written = 0;
			foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(CharacterLabPaths.CharactersCategory))
			{
				if (File.Exists(Path.Combine(itemFolder, "project.json")))
				{
					continue;
				}

				WriteProjectMarker(itemFolder);
				written++;
			}

			if (written > 0)
			{
				LokrCharacterLabPlugin.Log.LogInfo("Wrote project.json onto " + written + " existing character folder(s).");
			}
		}

		private static void WriteProjectMarker(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return;
			}

			Directory.CreateDirectory(folder);
			string display = string.Empty;
			string vanilla = string.Empty;
			if (CharacterSession.Profile != null
				&& string.Equals(CharacterSession.Folder, folder, StringComparison.Ordinal))
			{
				display = CharacterSession.Profile.Name ?? string.Empty;
				vanilla = CharacterSession.Profile.VanillaSourceUniqueId ?? string.Empty;
			}

			string json = "{\"projectType\":\"character\",\"schemaVersion\":1";
			if (!string.IsNullOrEmpty(display))
			{
				json += ",\"displayName\":\"" + EscapeJson(display) + "\"";
			}

			if (!string.IsNullOrEmpty(vanilla))
			{
				json += ",\"vanillaSourceUniqueId\":\"" + EscapeJson(vanilla) + "\"";
			}

			json += "}";
			File.WriteAllText(Path.Combine(folder, "project.json"), json);
		}

		private static string EscapeJson(string value)
		{
			return LokrModAPI.Serialization.TextEscaping.JsonEscape(value ?? string.Empty);
		}

		private static void RegisterPersistentInspectors(ProjectTypeRegistration registration)
		{
			registration.RegisterPersistentInspector(new PersistentInspectorRegistration
			{
				Id = "properties",
				Matches = nodes => PrimaryKind(nodes) == CharacterNodeKinds.PropertiesCategory,
				EnsureBuilt = PropertiesCategoryHost.Build,
				Show = nodes =>
				{
					LabNode primary = Primary(nodes);
					string name = primary != null ? (primary.Payload as string ?? primary.DisplayName) : null;
					PropertiesCategoryHost.Show(name);
				},
				Hide = PropertiesCategoryHost.Hide
			});
			registration.RegisterPersistentInspector(new PersistentInspectorRegistration
			{
				Id = "animator-live",
				Matches = UseLiveAnimatorInspector,
				EnsureBuilt = parent =>
				{
					if (!InspectorPanel.IsBuilt)
					{
						InspectorPanel.BuildInto(parent);
					}
				},
				Show = _ =>
				{
					InspectorPanel.Visible(true);
					InspectorPanel.Refresh();
				},
				Hide = () => InspectorPanel.Visible(false),
				Refresh = InspectorPanel.Refresh
			});
		}

		private static bool UseLiveAnimatorInspector(IReadOnlyList<LabNode> nodes)
		{
			if (Lab.ActiveWorkspaceName != "Animator" || !RigEditorScene.IsRuntimeLive
				|| nodes == null || nodes.Count == 0)
			{
				return false;
			}

			string kind = nodes[0].Kind;
			return kind == CharacterNodeKinds.Part
				|| kind == CharacterNodeKinds.AnimationClip
				|| kind == CharacterNodeKinds.Frame
				|| kind == CharacterNodeKinds.Reference;
		}

		private static void SyncAnimatorSelection(IReadOnlyList<LabNode> nodes)
		{
			if (!RigEditorScene.IsRuntimeLive || nodes == null || nodes.Count == 0)
			{
				return;
			}

			LabNode primary = LokrLabApi.LokrLabApi.Selection.Primary ?? nodes[0];
			if (primary.Kind == CharacterNodeKinds.Part)
			{
				List<DraggablePart> parts = new List<DraggablePart>();
				for (int i = 0; i < nodes.Count; i++)
				{
					LabNode node = nodes[i];
					if (node == null || node.Kind != CharacterNodeKinds.Part)
					{
						continue;
					}

					DraggablePart part = RigEditorScene.FindPartByName(node.Payload as string ?? node.DisplayName);
					if (part != null)
					{
						parts.Add(part);
					}
				}

				DraggablePart active = RigEditorScene.FindPartByName(primary.Payload as string ?? primary.DisplayName);
				RigEditorScene.SelectParts(parts, active);
			}
			else if (primary.Kind == CharacterNodeKinds.AnimationClip)
			{
				RigEditorScene.SelectClipByName(primary.Payload as string ?? primary.DisplayName);
			}
		}

		private static void OnNodeActivated(LabNode node)
		{
			if (node != null && node.Kind == CharacterNodeKinds.AbilityRef)
			{
				CharacterNodeContributors.JumpToAbility(node.Payload as string ?? node.DisplayName);
			}
		}

		private static string ResolveCharacterDisplayName(string folder)
		{
			CharacterProfile profile = CharacterProfileSidecar.Load(folder);
			return profile != null ? profile.Name : null;
		}

		private static LabNode Primary(IReadOnlyList<LabNode> nodes)
		{
			if (nodes == null || nodes.Count == 0)
			{
				return null;
			}

			return LokrLabApi.LokrLabApi.Selection.Primary ?? nodes[0];
		}

		private static string PrimaryKind(IReadOnlyList<LabNode> nodes)
		{
			LabNode node = Primary(nodes);
			return node != null ? node.Kind : null;
		}

		private static GameObject BuildPropertiesViewport(Transform parent)
		{
			UiStack stack = UiStack.Vertical(parent, UiTheme.Default, spacing: 8f, padding: 12f);
			stack.Add(UiLabel.Create(stack.ContentTransform,
				"Select a Properties category under the character in the Node Tree. Fields appear in the Inspector.",
				UiTheme.Default, 14, TextAnchor.UpperLeft).Grow());
			return stack.GameObject;
		}
	}
}
