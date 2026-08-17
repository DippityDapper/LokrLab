using System;
using LokrLab;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrLabApi;
using UnityEngine;

namespace LokrCharacterLab
{
	/// <summary>Wires Character popups and legacy screens into the shell lab scene.</summary>
	internal static class CharacterLabHooks
	{
		/// <summary>Assigns File → Import Legacy Pack before the lab scene exists so the Project Browser never hides it.</summary>
		internal static void Register()
		{
			AssignLegacyImport();
		}

		/// <summary>Points PromptLegacyImport at the folder picker. Safe to call from Awake; the picker uses Host.Canvas at click time.</summary>
		private static void AssignLegacyImport()
		{
			LokrLabApi.LokrLabApi.ImportLegacyFolder = folder =>
			{
				LegacyModImportPanel.RunImport(folder);
			};
			LokrLabApi.LokrLabApi.PromptLegacyImport = () =>
			{
				Transform canvas = Lab.Canvas;
				if (canvas != null)
				{
					FileBrowserPanel.EnsureBuilt(canvas);
				}

				FileBrowserPanel.OpenForFolder(CharacterLabPaths.GameModsRoot, folder =>
				{
					LegacyModImportPanel.RunImport(folder);
					LokrLabApi.LokrLabApi.RequestRefresh();
				});
			};
		}

		/// <summary>Builds File Browser, Home/Load screens, and built-in workstations after the shell scene exists.</summary>
		internal static void OnLabOpened(LabSceneContext context)
		{
			AssignLegacyImport();
			EmbeddedFightHost.BindStatic();
			if (context == null)
			{
				return;
			}

			try
			{
				FileBrowserPanel.EnsureBuilt(context.Canvas);
				MenuBarPanel.EnsurePopups(context.Canvas);
				if (context.GetScreenRoot != null)
				{
					HomeWorkstationScene.Build(context.Scene, context.GetScreenRoot("Home"));
					LoadWorkstationScene.Build(context.Scene, context.GetScreenRoot("Load"));
				}

				PropertiesWorkstationScene.RegisterBuiltInCategories();
				CharacterCreatorAPI.RegisterWorkstation("Properties", "Properties", PropertiesWorkstationScene.Build);
				CharacterCreatorAPI.RegisterWorkstation("Animator", "Open Rig Editor", RigEditorScene.Build,
					onShow: () =>
					{
						RigEditorScene.OnLoadClicked(CharacterSession.Folder);
						RigEditorScene.SetRuntimeActive(true);
					},
					onHide: () => RigEditorScene.SetRuntimeActive(false));
				CharacterCreatorAPI.RegisterWorkstation("Sandbox", "Sandbox", SandboxWorkstationScene.Build);
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogError("CharacterLabHooks.OnLabOpened: " + ex);
			}

			EmbeddedFightHost.BindHost(LokrLabApi.LokrLabApi.Host);
		}

		/// <summary>Drops Character widget refs (including Edit History) before the lab scene is destroyed.</summary>
		/// <remarks>
		/// Content reload runs from <see cref="CharacterLabScene.CloseTo"/> after this handler
		/// returns, so a ResetSession throw cannot skip localization. Description flush still
		/// happens here first so disk is current before that reload.
		/// </remarks>
		internal static void OnLabClosing()
		{
			try
			{
				LokrCharacterLabPlugin.Log.LogInfo("CharacterLabHooks.OnLabClosing");
				LabContentReloader.FlushPendingEdits();
				EmbeddedFightHost.Stop();
				CharacterWorkstations.Reset();
				RigEditorScene.SetRuntimeActive(false);
				RigEditorScene.ResetSession();
				PropertiesWorkstationScene.ResetSession();
				InspectorPanel.ResetSession();
				PropertiesCategoryHost.ResetSession();
				CharacterListPanel.ResetSession();
				LegacyModImportPanel.ResetSession();
				FileBrowserPanel.ResetSession();
				MenuBarPanel.ResetSession();
				IslandAtlasPickerPanel.ResetSession();
				EditHistoryPanel.ResetSession();
				ReadinessChecklistPanel.Unbind();
				HomeWorkstationScene.ResetSession();
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogError("CharacterLabHooks.OnLabClosing: " + ex);
			}
		}

		/// <summary>Refreshes Home/checklist when the shell is shown after leaving the Animator.</summary>
		internal static void OnShellShown()
		{
			HomeWorkstationScene.RefreshForReturnFromAnimator();
		}

		/// <summary>Drops Properties inspector widget refs when Close Project returns to the Project Browser.</summary>
		/// <remarks>InspectorDock destroys persistent hosts on session clear. LabClosing does not run for Close Project, so the next Load must not RefreshAll against the destroyed lists.</remarks>
		internal static void OnScreenShown(string name)
		{
			if (name == "Browser")
			{
				LabContentReloader.FlushPendingEdits();
				PropertiesWorkstationScene.ResetSession();
			}

			CharacterWorkstations.OnScreenShown(name);
		}
	}
}
