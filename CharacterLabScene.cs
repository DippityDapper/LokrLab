using System;
using System.Collections.Generic;
using DG.Tweening;
using Ironhide.Legends;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Screens.Transition;
using LokrLab.Editor;
using LokrLab.Encounter;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LokrLab
{
	/// <summary>Character Lab as a real scene transition — the scene the player was in is genuinely unloaded, not just hidden underneath.</summary>
	/// <remarks>
	/// Reuses the base game's own transition primitives rather than an additive-overlay hack: <see cref="FadeScreen"/>
	/// for the visual fade (the same one every real scene transition in the game already uses -- there is no
	/// separate loading-screen/progress-bar asset), a genuine <see cref="SceneManager.UnloadSceneAsync(string)"/>
	/// of the scene the player came from, and <see cref="TransitionSceneComponent"/> (the base game's real
	/// scene-loading mechanism) to return to it on close. <see cref="TransitionSceneComponent"/> can only target a
	/// real, Build-Settings-registered scene by name -- it cannot target this plugin's own in-memory
	/// <see cref="SceneManager.CreateScene(string)"/> scene, which is why entering the lab is driven by hand
	/// (fade + explicit unload) while leaving it can reuse the base game's own mechanism directly (the destination
	/// there is always a real scene). Because closing the lab now does a real <c>LoadSceneMode.Single</c> load,
	/// the lab's own scene and every GameObject in it get destroyed as a side effect -- <see cref="isBuilt"/> is
	/// reset on close so the next <see cref="Open"/> rebuilds it fresh, the same "safe to call every Open()"
	/// contract project-type plugins already committed to for a different reason. A sandbox fight
	/// uses <see cref="CloseTo"/> into the fight scene, then <see cref="ReopenAfterFight"/> rebuilds
	/// this shell (same project, requested tab) instead of dumping the player back at the original
	/// origin. Open from anywhere via <see cref="CharacterLabAccess"/> / LokrModMenu
	/// (` backquote default; F3 optional).
	/// </remarks>
	internal static class CharacterLabScene
	{
		private const string SceneName = "LokrLab";
		private const int CanvasSortOrder = 20000;

		private static Scene labScene;
		private static bool isBuilt;
		private static bool isOpen;
		/// <summary>True while a fade or scene load for open/close is in flight.</summary>
		private static bool isTransitioning;

		/// <summary>The real scene the player was in before opening the lab, captured fresh on every Open() -- Close()'s own default return destination.</summary>
		private static string originScene;

		/// <summary>Scene unloaded after the lab is built. Usually originScene; after a sandbox fight it is the fight scene while originScene stays the pre-lab scene.</summary>
		private static string sceneToUnloadOnOpen;

		/// <summary>Workspace to activate on the next SwitchToShell (Sandbox after a fight). Cleared after use.</summary>
		private static string pendingWorkspace;

		/// <summary>Pending JumpToProject while the lab is still opening.</summary>
		private static string pendingJumpType;

		/// <summary>Optional folder for a pending jump (null = type FolderRoot).</summary>
		private static string pendingJumpFolder;

		/// <summary>Optional Node Tree id to select after a pending jump.</summary>
		private static string pendingJumpNode;

		/// <summary>Optional Node Tree id to select after the next SwitchToShell.</summary>
		private static string pendingSelectNodeId;

		private static readonly Stack<SessionBookmark> sessionStack = new Stack<SessionBookmark>();

		/// <summary>The real scene the lab was opened from -- exposed so a workstation (e.g. Sandbox Encounter) that redirects CloseTo() elsewhere can still find its way back to the real scene afterward, since SceneManager.GetActiveScene().name from inside the lab's own scene would only ever report the lab's own scene, not this.</summary>
		internal static string OriginScene => originScene;

		/// <summary>The in-memory lab scene, valid while the lab is built.</summary>
		internal static Scene LabScene => labScene;

		/// <summary>Full-screen lab backdrop camera; viewport cameras render above it.</summary>
		internal static Camera BackdropCamera { get; private set; }

		private const string LoadScreen = "Load";
		private const string HomeScreen = "Home";
		private const string BrowserScreen = "Browser";
		private const string ShellScreen = "Shell";
		private static readonly UiScreenSwitcher screens = new UiScreenSwitcher();
		private static Transform canvasRoot;
		private static UiLabel titleLabel;
		private static UiButton closeButton;

		/// <summary>True while the overlay is visible.</summary>
		internal static bool IsOpen => isOpen;

		/// <summary>Root canvas transform, or null before BuildUI.</summary>
		internal static Transform Canvas => canvasRoot;

		/// <summary>Sets the overlay title on leftover Load/Home chrome, and the menu-bar title on shell/browser.</summary>
		internal static void RefreshTitle()
		{
			if (titleLabel != null)
			{
				titleLabel.SetText(LabSaveUx.IsDirty ? "LoKR Lab *" : "LoKR Lab");
			}

			LabMenuBar.RefreshTitle();
		}

		internal static void Toggle()
		{
			if (isOpen)
			{
				Close();
			}
			else
			{
				Open();
			}
		}

		/// <summary>Fades out, unloads the current real scene, then builds/shows the lab as its own scene.</summary>
		internal static void Open()
		{
			if (isOpen && labScene.IsValid() && labScene.isLoaded)
			{
				return;
			}

			bool wasTransitioning = isTransitioning;
			isOpen = false;
			isTransitioning = false;
			string activeName = SceneManager.GetActiveScene().name;
			if (activeName == SceneName && labScene.IsValid() && labScene.isLoaded)
			{
				LokrLabPlugin.Log.LogInfo("LokrLab Open: already in lab scene, finishing chrome.");
				FinishOpen();
				return;
			}

			originScene = string.IsNullOrEmpty(activeName) || activeName == SceneName
				? "krlegendsmainmenu"
				: activeName;
			pendingWorkspace = null;
			LokrLabPlugin.Log.LogInfo(
				"LokrLab Open from '" + originScene + "' (wasTransitioning=" + wasTransitioning + ").");
			BeginOpen(originScene);
		}

		/// <summary>Rebuilds the lab after a sandbox fight ends. Unloads the fight scene, keeps <paramref name="preservedOrigin"/> as Close Lab's destination, and lands on <paramref name="workspace"/> if a project is still open.</summary>
		internal static void ReopenAfterFight(string preservedOrigin, string workspace)
		{
			if (isOpen)
			{
				SwitchToShell();
				if (!string.IsNullOrEmpty(workspace))
				{
					LabShell.ActivateWorkspace(workspace);
				}

				return;
			}

			if (!string.IsNullOrEmpty(preservedOrigin))
			{
				originScene = preservedOrigin;
			}

			pendingWorkspace = workspace;
			BeginOpen(SceneManager.GetActiveScene().name);
		}

		/// <summary>Fades out, then builds the lab and unloads <paramref name="sceneToUnload"/>.</summary>
		private static void BeginOpen(string sceneToUnload)
		{
			isTransitioning = true;
			sceneToUnloadOnOpen = sceneToUnload;
			FadeOutThen(BeginOpenAfterFade);
		}

		/// <summary>Builds and activates the lab scene, then unloads the origin scene.</summary>
		/// <remarks>Builds the lab scene BEFORE unloading the origin scene -- Unity disallows unloading the only remaining loaded scene, so there must always be at least one other scene loaded in between.</remarks>
		private static void BeginOpenAfterFade()
		{
			try
			{
				EnsureBuilt();
				if (labScene.IsValid())
				{
					SceneManager.SetActiveScene(labScene);
				}

				if (!string.IsNullOrEmpty(sceneToUnloadOnOpen) && sceneToUnloadOnOpen != SceneName)
				{
					Scene origin = SceneManager.GetSceneByName(sceneToUnloadOnOpen);
					if (origin.IsValid() && origin.isLoaded)
					{
						SceneManager.UnloadSceneAsync(origin);
					}
				}

				FinishOpen();
			}
			catch (System.Exception ex)
			{
				LokrLabPlugin.Log.LogError("LokrLab open failed: " + ex);
				FinishOpen();
			}
		}

		private static void FinishOpen()
		{
			try
			{
				if (LokrLabApi.LokrLabApi.Host == null && isBuilt)
				{
					BindHost();
					RaiseOpened();
				}

				SetLabRootsActive(true);

				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
				Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

				isOpen = true;
				if (!string.IsNullOrEmpty(pendingJumpType))
				{
					ApplyPendingJump(pushCurrent: false);
				}
				else if (LokrLabApi.LokrLabApi.CurrentSession != null && !string.IsNullOrEmpty(pendingWorkspace))
				{
					SwitchToShell();
				}
				else
				{
					SwitchToBrowser();
				}
			}
			catch (System.Exception ex)
			{
				LokrLabPlugin.Log.LogError("LokrLab FinishOpen failed: " + ex);
				try
				{
					SwitchToBrowser();
				}
				catch (System.Exception browserEx)
				{
					LokrLabPlugin.Log.LogError("LokrLab SwitchToBrowser failed: " + browserEx);
				}
			}
			finally
			{
				isTransitioning = false;
				FadeIn();
			}
		}

		/// <summary>Closes the lab and returns to the real scene it was opened from.</summary>
		internal static void Close()
		{
			LabSaveUx.RequestCloseLab();
		}

		/// <summary>Closes the lab and transitions to the given real scene instead of the one it was opened from -- e.g. Sandbox Encounter closing straight into the "fight" scene rather than back to wherever the lab happened to be opened from.</summary>
		/// <remarks>The lab's own scene is about to be destroyed as a side effect of the LoadSceneMode.Single load TransitionToRealScene performs. Project types drop widget refs from <see cref="LokrLabApi.LokrLabApi.LabClosing"/> before that happens.</remarks>
		internal static void CloseTo(string sceneName)
		{
			if (!isOpen)
			{
				return;
			}

			LokrLabPlugin.Log.LogInfo("LokrLab CloseTo '" + sceneName + "'.");
			isTransitioning = true;
			try
			{
				LabContentReloader.FlushPendingEdits();
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogError("LokrLab CloseTo flush failed: " + ex);
			}

			EmbeddedSceneHost.Stop();
			try
			{
				LokrLabApi.LokrLabApi.RaiseLabClosing();
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogError("LokrLab LabClosing failed: " + ex);
			}

			try
			{
				if (LokrLabPlugin.AutoReloadOnLabClose == null || LokrLabPlugin.AutoReloadOnLabClose.Value)
				{
					LabContentReloader.TryAutoReloadOnLabClose();
				}
				else
				{
					LokrLabPlugin.Log.LogInfo("LokrLab CloseTo: AutoReloadOnLabClose is false, skipping content reload.");
				}
			}
			catch (Exception ex)
			{
				LokrLabPlugin.Log.LogError("LokrLab CloseTo reload failed: " + ex);
			}

			ClearClosedSession();
			isOpen = false;
			LokrLabApi.LokrLabApi.Host = null;

			isBuilt = false;
			screens.Clear();
			canvasRoot = null;

			TransitionToRealScene(sceneName);
		}

		private static void TransitionToRealScene(string sceneName)
		{
			FadeOutThen(() => TransitionSceneComponent.TransitionToNextScene("scenes", sceneName));
		}

		/// <summary>Best-effort state cleanup for an externally-forced scene change (e.g. a crash-recovery path) -- does not itself drive a scene transition, since whatever forced this is presumably already mid-transition on its own.</summary>
		internal static void ForceClose()
		{
			EmbeddedSceneHost.Stop();
			if (isOpen)
			{
				LokrLabApi.LokrLabApi.RaiseLabClosing();
			}

			ClearClosedSession();
			isOpen = false;
			isTransitioning = false;
			LokrLabApi.LokrLabApi.Host = null;
			if (!labScene.IsValid())
			{
				isBuilt = false;
				screens.Clear();
				canvasRoot = null;
				return;
			}

			SetLabRootsActive(false);
		}

		/// <summary>Clears close-in-flight state after a real scene load so the next Open is not ignored.</summary>
		internal static void OnExternalSceneLoaded(string sceneName)
		{
			if (string.Equals(sceneName, "transitionscene", System.StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			isTransitioning = false;
			if (isOpen)
			{
				ForceClose();
			}
		}

		/// <summary>Drops the open project and shell widget refs so the next Open lands on the Project Browser.</summary>
		/// <remarks>Close Lab used to leave CurrentSession set. FinishOpen then SwitchToShell'd against destroyed InspectorDock / LabShell hosts and never reached ShowFadeIn.</remarks>
		private static void ClearClosedSession()
		{
			sessionStack.Clear();
			LokrLabApi.LokrLabApi.CurrentSession = null;
			LokrLabApi.LokrLabApi.Selection.Clear();
			LabShell.ResetSession();
			EncounterCombatantsCatalogue.ResetSession();
			EncounterPropsCatalogue.ResetSession();
			EncounterCatalogueThumbnails.Dispose();
			EncounterCataloguePreview.Dispose();
			EncounterCatalogueExo.Dispose();
			InspectorDock.ResetSession();
			LabMenuBar.ResetSession();
			LabSaveUx.ResetSession();
			ProjectBrowser.ResetSession();
		}

		/// <summary>Builds the lab scene if it isn't already built and still valid.</summary>
		/// <remarks>labScene.IsValid() is a defensive backstop, not the primary guard -- CloseTo() already resets isBuilt explicitly since it knows it's about to trigger a LoadSceneMode.Single load itself, but LoadSceneMode.Single unloads EVERY currently loaded scene, not just whichever one is "active" (see ModMenuOverlay's own identical fix), so this catches any other path that might destroy this scene out from under isBuilt without going through CloseTo() first.</remarks>
		private static void EnsureBuilt()
		{
			if (isBuilt && labScene.IsValid() && labScene.isLoaded)
			{
				return;
			}

			if (labScene.IsValid() && labScene.isLoaded)
			{
				ClearLabSceneRoots();
			}
			else
			{
				labScene = SceneManager.CreateScene(SceneName);
			}

			BuildCamera(labScene);
			BuildEventSystem(labScene);
			Transform canvas = BuildUI(labScene);

			ProjectBrowser.Build(screens.GetRoot(BrowserScreen), canvas);
			LabShell.Build(screens.GetRoot(ShellScreen), canvas);
			BindHost();
			RaiseOpened();

			isBuilt = true;
			SetLabRootsActive(false);
		}

		/// <summary>Destroys leftover lab roots so a close that never unloaded the scene can rebuild in place.</summary>
		private static void ClearLabSceneRoots()
		{
			isBuilt = false;
			screens.Clear();
			LabMenuBar.ResetSession();
			LabSaveUx.ResetSession();
			canvasRoot = null;
			BackdropCamera = null;
			titleLabel = null;
			closeButton = null;
			if (!labScene.IsValid())
			{
				return;
			}

			foreach (GameObject root in labScene.GetRootGameObjects())
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		private static void SetLabRootsActive(bool active)
		{
			if (!isBuilt)
			{
				return;
			}
			foreach (GameObject root in labScene.GetRootGameObjects())
			{
				root.SetActive(active);
			}
		}

		/// <summary>Shows a named lab screen, creating the root if needed.</summary>
		internal static void ShowNamedScreen(string name)
		{
			if (!isOpen || string.IsNullOrEmpty(name))
			{
				return;
			}

			GetOrCreateScreenRoot(name);
			SetLegacyChromeVisible(name != BrowserScreen && name != ShellScreen);
			screens.Show(name);
			LokrLabApi.LokrLabApi.RaiseScreenShown(name);
			RefreshTitle();
		}

		internal static void SwitchToHome()
		{
			SwitchToShell();
		}

		/// <summary>Opens the Project Browser. Kept as SwitchToLoad so existing Home/Load callers keep compiling.</summary>
		internal static void SwitchToLoad()
		{
			SwitchToBrowser();
		}

		/// <summary>Shows the Project Browser (empty state while no project is open).</summary>
		internal static void SwitchToBrowser()
		{
			if (!isOpen)
			{
				return;
			}
			SetLegacyChromeVisible(false);
			ProjectBrowser.Refresh();
			screens.Show(BrowserScreen);
			LokrLabApi.LokrLabApi.RaiseScreenShown(BrowserScreen);
			RefreshTitle();
		}

		/// <summary>Shows the dockable shell for the current project.</summary>
		internal static void SwitchToShell()
		{
			if (!isOpen)
			{
				return;
			}
			SetLegacyChromeVisible(false);
			LabShell.Refresh();
			string workspace = pendingWorkspace;
			pendingWorkspace = null;
			if (string.IsNullOrEmpty(workspace))
			{
				workspace = LabShell.ActiveWorkspaceName;
			}
			if (string.IsNullOrEmpty(workspace))
			{
				workspace = LabShell.FirstWorkspaceName;
			}

			if (!string.IsNullOrEmpty(workspace))
			{
				LabShell.ActivateWorkspace(workspace);
			}
			screens.Show(ShellScreen);
			LokrLabApi.LokrLabApi.RaiseScreenShown(ShellScreen);
			LokrLabApi.LokrLabApi.RaiseShellShown();
			RefreshTitle();
			if (!string.IsNullOrEmpty(pendingSelectNodeId))
			{
				string nodeId = pendingSelectNodeId;
				pendingSelectNodeId = null;
				NodeTreePanel.SelectById(nodeId);
			}
		}

		/// <summary>Clears the current project and returns to the Project Browser.</summary>
		/// <remarks>
		/// Also clears <see cref="LokrLab.Editor.General.CharacterSession"/> when the closing
		/// project was a Character project: that state is Character-specific and this shell method
		/// is project-type-agnostic, so nothing else resets it on close. Leaving it stale let a
		/// later delete-then-close-Lab crash trying to persist into a folder that no longer existed.
		/// </remarks>
		internal static void CloseProject()
		{
			ProjectSession closing = LokrLabApi.LokrLabApi.CurrentSession;
			sessionStack.Clear();
			LokrLabApi.LokrLabApi.CurrentSession = null;
			LokrLabApi.LokrLabApi.Selection.Clear();
			LabShell.UnloadProject();
			LabSaveUx.RefreshChrome();
			if (closing != null && closing.ProjectTypeId == LokrLab.Projects.CharacterProjectType.Id)
			{
				LokrLab.Editor.General.CharacterSession.Clear();
			}
			if (isOpen)
			{
				SwitchToBrowser();
			}
		}

		/// <summary>Wires cross-project jump/return/refresh onto LokrLabApi so third-party project types never reference this assembly.</summary>
		internal static void BindLabApiNavigation()
		{
			LokrLabApi.LokrLabApi.OpenProject = OpenProjectFromApi;
			LokrLabApi.LokrLabApi.RecentProjectFolders = RecentProjectsStore.Load;
			LokrLabApi.LokrLabApi.ReturnToPreviousProject = ReturnFromJump;
			LokrLabApi.LokrLabApi.CanReturnToPreviousProject = () => sessionStack.Count > 0;
			LokrLabApi.LokrLabApi.RefreshShell = () =>
			{
				if (isOpen)
				{
					ProjectBrowser.Refresh();
					LabShell.Refresh();
				}
			};
			RegisterShellMenus();
		}

		private static void RegisterShellMenus()
		{
			LokrLabApi.LokrLabApi.RegisterMenu("File", priority: 0);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Back to Previous Project", ReturnFromJump, priority: 0,
				isVisible: () => LokrLabApi.LokrLabApi.CanReturnToPreviousProject != null
					&& LokrLabApi.LokrLabApi.CanReturnToPreviousProject());
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Save", LabSaveUx.SaveCurrent, priority: 1,
				isVisible: () => LokrLabApi.LokrLabApi.CurrentSession != null);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Close Project", LabSaveUx.RequestCloseProject, priority: 2,
				isVisible: () => LokrLabApi.LokrLabApi.CurrentSession != null);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Delete Project...", ProjectBrowser.PromptDeleteOpenSession, priority: 3,
				isVisible: ProjectBrowser.CanDeleteOpenSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Edit Vanilla Hero...", VanillaHeroPickerModal.Show, priority: 9);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Import Legacy Pack...", ProjectBrowser.PromptLegacyImport, priority: 10);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Close Lab", LabSaveUx.RequestCloseLab, priority: 100);

			LokrLabApi.LokrLabApi.RegisterMenu("View", priority: 20);
			LokrLabApi.LokrLabApi.RegisterMenuItem("View", "File Tree", () => LabShell.FocusPanel("file-tree"), priority: 25,
				isVisible: () => LokrLabApi.LokrLabApi.CurrentSession != null);

			LokrLabApi.LokrLabApi.RegisterMenu("Help", priority: 30);
			LokrLabApi.LokrLabApi.RegisterMenuItem("Help", "About LoKR Lab", LabMenuBar.ShowAbout, priority: 0);
		}

		private static void BindHost()
		{
			LokrLabApi.LokrLabApi.Host = new LabHost
			{
				DefaultFont = DefaultFont,
				LabScene = labScene,
				BackdropCamera = BackdropCamera,
				Canvas = canvasRoot,
				OriginScene = originScene,
				IsOpen = () => isOpen,
				GetActiveWorkspaceName = () => LabShell.ActiveWorkspaceName,
				ActivateWorkspace = LabShell.ActivateWorkspace,
				FocusBottomPanel = LabShell.FocusBottomPanel,
				FocusPanel = id => LabShell.FocusPanel(id),
				SetStatus = LabShell.SetStatus,
				SelectNodeById = NodeTreePanel.SelectById,
				CloseTo = LabSaveUx.RequestCloseTo,
				ReopenAfterFight = ReopenAfterFight,
				CloseProject = LabSaveUx.RequestCloseProject,
				CloseLab = LabSaveUx.RequestCloseLab,
				SwitchToHome = SwitchToHome,
				SwitchToLoad = SwitchToLoad,
				SwitchToShell = SwitchToShell,
				ShowScreen = ShowNamedScreen,
				GetScreenRoot = GetOrCreateScreenRoot,
				GetWorkstationContentRoot = GetWorkstationContentRoot,
				ShowAbout = LabMenuBar.ShowAbout,
				InvalidateInspector = InspectorDock.Invalidate,
				StartEmbeddedScene = LokrLabApi.LokrLabApi.StartEmbeddedScene,
				StopEmbeddedScene = LokrLabApi.LokrLabApi.StopEmbeddedScene,
				IsEmbeddedSceneActive = LokrLabApi.LokrLabApi.IsEmbeddedSceneActive,
				GetEmbeddedSceneCamera = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera,
				StartEmbeddedFight = LokrLabApi.LokrLabApi.StartEmbeddedFight,
				StopEmbeddedFight = LokrLabApi.LokrLabApi.StopEmbeddedFight,
				IsEmbeddedFightActive = LokrLabApi.LokrLabApi.IsEmbeddedFightActive
			};
			EmbeddedSceneHost.BindHost(LokrLabApi.LokrLabApi.Host);
		}

		private static void RaiseOpened()
		{
			LokrLabApi.LokrLabApi.RaiseLabOpened(new LabSceneContext
			{
				Scene = labScene,
				Canvas = canvasRoot,
				DefaultFont = DefaultFont,
				BackdropCamera = BackdropCamera,
				GetScreenRoot = GetOrCreateScreenRoot
			});
		}

		private static Transform GetOrCreateScreenRoot(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			if (screens.TryGetRoot(name, out Transform existing))
			{
				return existing;
			}

			if (canvasRoot == null)
			{
				return null;
			}

			return screens.Register(name, canvasRoot);
		}

		/// <summary>Reloads the open project at a new folder without pushing the old path onto the Back stack.</summary>
		/// <remarks>Used after a slug_token rename: the old folder is gone, so JumpToProject would bookmark a dead path.</remarks>
		internal static void ReloadOpenProject(string projectTypeId, string folder, string selectNodeId)
		{
			SwitchToProject(projectTypeId, folder, selectNodeId, pushCurrent: false);
		}

		/// <summary>Opens a project of the given type, pushing the current session so File → Back can return.</summary>
		internal static void OpenProjectFromApi(string projectTypeId, string folder, string selectNodeId)
		{
			if (string.IsNullOrEmpty(projectTypeId))
			{
				return;
			}

			pendingJumpType = projectTypeId;
			pendingJumpFolder = folder;
			pendingJumpNode = selectNodeId;
			if (!isOpen)
			{
				Open();
				return;
			}

			ApplyPendingJump(pushCurrent: true);
		}

		/// <summary>Restores the session that was open before the last jump.</summary>
		internal static void ReturnFromJump()
		{
			if (sessionStack.Count == 0)
			{
				return;
			}

			LabSaveUx.GuardLeave(() =>
			{
				SessionBookmark bookmark = sessionStack.Pop();
				SwitchToProject(bookmark.ProjectTypeId, bookmark.Folder, bookmark.NodeId, pushCurrent: false);
			});
		}

		private static void ApplyPendingJump(bool pushCurrent)
		{
			string typeId = pendingJumpType;
			string folder = pendingJumpFolder;
			string nodeId = pendingJumpNode;
			pendingJumpType = null;
			pendingJumpFolder = null;
			pendingJumpNode = null;
			SwitchToProject(typeId, folder, nodeId, pushCurrent);
		}

		private static void SwitchToProject(string projectTypeId, string folder, string selectNodeId, bool pushCurrent)
		{
			ProjectTypeRegistration type = LokrLabApi.LokrLabApi.GetProjectType(projectTypeId);
			if (type == null || type.Load == null)
			{
				LabShell.SetStatus("No project type '" + projectTypeId + "' is registered.");
				return;
			}

			ProjectSession current = LokrLabApi.LokrLabApi.CurrentSession;
			string loadFolder = string.IsNullOrEmpty(folder) ? type.FolderRoot : folder;
			if (current != null && current.ProjectTypeId == projectTypeId
				&& string.Equals(current.FolderPath, loadFolder, System.StringComparison.OrdinalIgnoreCase))
			{
				pendingSelectNodeId = selectNodeId;
				SwitchToShell();
				return;
			}

			if (current != null && current.IsDirty)
			{
				LabSaveUx.GuardLeave(() => SwitchToProject(projectTypeId, folder, selectNodeId, pushCurrent));
				return;
			}

			if (pushCurrent)
			{
				PushCurrentSession();
			}

			ProjectSession session = type.Load(loadFolder);
			if (session == null)
			{
				LabShell.SetStatus("Could not open '" + projectTypeId + "'.");
				return;
			}

			LokrLabApi.LokrLabApi.CurrentSession = session;
			LokrLabApi.LokrLabApi.Selection.Clear();
			RecentProjectsStore.Record(session.FolderPath);
			pendingSelectNodeId = selectNodeId;
			IReadOnlyList<WorkspaceRegistration> workspaces = type.Workspaces;
			pendingWorkspace = workspaces.Count > 0 ? workspaces[0].Name : null;
			SwitchToShell();
		}

		private static void PushCurrentSession()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return;
			}

			LabNode primary = LokrLabApi.LokrLabApi.Selection.Primary;
			sessionStack.Push(new SessionBookmark
			{
				ProjectTypeId = session.ProjectTypeId,
				Folder = session.FolderPath,
				NodeId = primary != null ? primary.Id : null
			});
		}

		private struct SessionBookmark
		{
			internal string ProjectTypeId;
			internal string Folder;
			internal string NodeId;
		}

		private static void BuildCamera(Scene scene)
		{
			GameObject cameraObject = new GameObject("LabBackdropCamera", typeof(Camera));
			Camera camera = cameraObject.GetComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
			camera.cullingMask = 0;
			camera.orthographic = true;
			camera.orthographicSize = 5f;
			camera.depth = 1000f;
			camera.rect = new Rect(0f, 0f, 1f, 1f);
			cameraObject.transform.position = new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(cameraObject, scene);
			BackdropCamera = camera;
		}

		private static void BuildEventSystem(Scene scene)
		{
			GameObject eventSystemObject = new GameObject(
				"LabEventSystem",
				typeof(EventSystem),
				typeof(StandaloneInputModule),
				typeof(CharacterLabCursorHelper));
			SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
		}

		private static Transform BuildUI(Scene scene)
		{
			GameObject canvasObject = new GameObject("LabCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = canvasObject.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = CanvasSortOrder;
			CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			SceneManager.MoveGameObjectToScene(canvasObject, scene);

			GameObject backdropObject = new GameObject("LabBackdrop", typeof(Image));
			backdropObject.transform.SetParent(canvasObject.transform, false);
			RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
			backdropRect.anchorMin = Vector2.zero;
			backdropRect.anchorMax = Vector2.one;
			backdropRect.offsetMin = Vector2.zero;
			backdropRect.offsetMax = Vector2.zero;
			// Clear — Animator cameras draw through the overlay via Camera.rect. Dock panels
			// are opaque; the backdrop camera already fills the rest.
			backdropObject.GetComponent<Image>().color = Color.clear;
			backdropObject.GetComponent<Image>().raycastTarget = false;
			backdropObject.transform.SetAsFirstSibling();

			titleLabel = UiLabel.Create(canvasObject.transform, "LoKR Lab", fontSize: 28, alignment: TextAnchor.MiddleCenter).Name("TitleLabel");
			Vector2 titleAnchor = new Vector2(0.5f, 0.968f);
			titleLabel.RectTransform.anchorMin = titleAnchor;
			titleLabel.RectTransform.anchorMax = titleAnchor;
			titleLabel.RectTransform.sizeDelta = new Vector2(700f, 40f);
			titleLabel.RectTransform.anchoredPosition = Vector2.zero;

			closeButton = UiButton.Create(canvasObject.transform, "Close (`)", Close).Name("CloseButton");
			closeButton.Label.fontSize = 20;
			Vector2 closeAnchor = new Vector2(0.5f, 0.04f);
			closeButton.RectTransform.anchorMin = closeAnchor;
			closeButton.RectTransform.anchorMax = closeAnchor;
			closeButton.RectTransform.sizeDelta = new Vector2(280f, 50f);
			closeButton.RectTransform.anchoredPosition = Vector2.zero;

			canvasRoot = canvasObject.transform;
			LabSaveUx.Bind(canvasObject);
			screens.Register(LoadScreen, canvasRoot);
			screens.Register(HomeScreen, canvasRoot);
			screens.Register(BrowserScreen, canvasRoot);
			screens.Register(ShellScreen, canvasRoot);

			return canvasRoot;
		}

		internal static readonly Font DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

		/// <summary>Opaque shell that wraps each workstation's panels (Home nav + checklist, Load list, etc.).</summary>
		internal static Transform GetWorkstationContentRoot(Transform screenRoot)
		{
			Transform existing = screenRoot.Find("ContentFrame");
			if (existing != null)
			{
				return existing;
			}

			GameObject frameObject = new GameObject("ContentFrame", typeof(Image));
			frameObject.transform.SetParent(screenRoot, false);
			frameObject.transform.SetAsFirstSibling();
			RectTransform rect = frameObject.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.015f, 0.11f);
			rect.anchorMax = new Vector2(0.985f, 0.915f);
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			frameObject.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.96f);
			return frameObject.transform;
		}

		private static void SetLegacyChromeVisible(bool visible)
		{
			if (titleLabel != null)
			{
				titleLabel.Visible(visible);
			}
			if (closeButton != null)
			{
				closeButton.Visible(visible);
			}

			RefreshTitle();
		}

		/// <summary>Fades the game load graphic out, or runs next immediately when already faded.</summary>
		/// <remarks>A second ShowFadeOut while alpha is already 1 can leave DOTween's OnComplete unfired, which is the stuck LOADING screen.</remarks>
		private static void FadeOutThen(TweenCallback next)
		{
			if (next == null)
			{
				return;
			}

			if (!MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				next();
				return;
			}

			FadeScreen fade = MonoSingleton<FadeScreen>.Instance;
			if (fade.canvasGroup != null)
			{
				fade.canvasGroup.DOKill();
			}

			Tweener fadeOut = fade.ShowFadeOut(withLoading: true, withTips: false);
			if (fadeOut == null || !fadeOut.IsActive())
			{
				next();
				return;
			}

			fadeOut.OnComplete(next);
		}

		/// <summary>Hides the game load graphic after the lab scene is ready.</summary>
		private static void FadeIn()
		{
			if (!MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				return;
			}

			FadeScreen fade = MonoSingleton<FadeScreen>.Instance;
			if (fade.canvasGroup != null)
			{
				fade.canvasGroup.DOKill();
			}

			fade.ShowFadeIn(withLoading: true);
		}

	}
}
