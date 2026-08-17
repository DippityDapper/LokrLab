using System;
using DG.Tweening;
using HarmonyLib;
using Ironhide.AssetBundles;
using Ironhide.Battlechest.Client.View;
using Ironhide.Legends.Controller.Game;
using Ironhide.Legends.Utils;
using LokrLabApi;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace LokrLab
{
	/// <summary>Additive scene load cropped to a hole <see cref="RectTransform"/> via <see cref="Camera.rect"/>.</summary>
	/// <remarks>
	/// Session 1 chose <see cref="LoadSceneMode.Additive"/> + hole crop (not render-to-texture).
	/// Stop must not call <see cref="CharacterLabScene.ReopenAfterFight"/> — that tears down the lab.
	/// One embed at a time. <see cref="CameraBase"/> is often invalid at first <c>sceneLoaded</c>;
	/// prefer <see cref="Camera.main"/> then the first camera in the loaded scene.
	/// </remarks>
	internal static class EmbeddedSceneHost
	{
		private static EmbeddedSceneRequest request;
		private static EmbeddedSceneRequest pendingStart;
		private static Scene embeddedScene;
		private static Scene labScene;
		private static Camera gameplayCamera;
		private static string expectedSceneName;
		private static bool endedCallbackPending;
		private static AsyncOperation unloadOp;
		private static string lastSceneName;

		/// <summary>True while an additive scene is loaded beside the lab.</summary>
		internal static bool IsActive { get; private set; }

		/// <summary>The loaded additive scene, or default if none.</summary>
		internal static Scene EmbeddedScene => embeddedScene;

		/// <summary>Gameplay camera of the current embed, or null.</summary>
		internal static Camera GameplayCamera => gameplayCamera;

		/// <summary>Lab chrome scene, used to drop Overlay hits over the hole.</summary>
		internal static Scene LabScene => labScene;

		/// <summary>Assigns the static facade so project types can embed before Host is rebuilt.</summary>
		internal static void BindStatic()
		{
			LokrLabApi.LokrLabApi.StartEmbeddedScene = Start;
			LokrLabApi.LokrLabApi.StopEmbeddedScene = Stop;
			LokrLabApi.LokrLabApi.IsEmbeddedSceneActive = () => IsActive;
			LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera = () => gameplayCamera;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
			BindHost(LokrLabApi.LokrLabApi.Host);
		}

		/// <summary>Copies scene-embed delegates onto a newly built Host.</summary>
		internal static void BindHost(LabHost host)
		{
			if (host == null)
			{
				return;
			}

			host.StartEmbeddedScene = Start;
			host.StopEmbeddedScene = Stop;
			host.IsEmbeddedSceneActive = () => IsActive;
			host.GetEmbeddedSceneCamera = () => gameplayCamera;
		}

		/// <summary>Starts an additive load. Returns an immediate error, or null if the load began or was queued behind unload.</summary>
		internal static string Start(EmbeddedSceneRequest sceneRequest)
		{
			if (IsUnloading)
			{
				pendingStart = sceneRequest;
				return null;
			}

			if (IsActive)
			{
				pendingStart = sceneRequest;
				Stop();
				return null;
			}

			if (sceneRequest == null)
			{
				return "No embed request.";
			}

			if (string.IsNullOrEmpty(sceneRequest.BundleId) || string.IsNullOrEmpty(sceneRequest.SceneName))
			{
				return "Bundle id and scene name are required.";
			}

			if (sceneRequest.Hole == null)
			{
				return "No hole RectTransform.";
			}

			request = sceneRequest;
			expectedSceneName = sceneRequest.SceneName;
			lastSceneName = sceneRequest.SceneName;
			labScene = CharacterLabScene.LabScene;
			gameplayCamera = null;
			embeddedScene = default(Scene);
			endedCallbackPending = true;
			IsActive = true;
			EnsureWatchdog(sceneRequest.Hole);
			LokrLabPlugin.Log.LogInfo(
				"EmbeddedSceneHost: additive load of '" + sceneRequest.SceneName
				+ "' from bundle '" + sceneRequest.BundleId + "'.");
			try
			{
				AssetBundleManager.LoadScene(sceneRequest.BundleId, sceneRequest.SceneName, true);
			}
			catch (Exception ex)
			{
				IsActive = false;
				endedCallbackPending = false;
				request = null;
				expectedSceneName = null;
				return "Additive scene load failed: " + ex.Message;
			}

			return null;
		}

		/// <summary>Unloads the embedded scene. No-op if none is running.</summary>
		/// <remarks>
		/// Unload is async. A Start that arrives while the old scene is still loaded is queued
		/// so two fight scenes cannot share CameraBase / hex-grid singletons. Camera restore and
		/// FadeScreen Overlay revert run before the hole reference is cleared: unload would
		/// otherwise leave Camera.main as the zoomed hole camera for the next ShowFadeOut.
		/// </remarks>
		internal static void Stop()
		{
			SandboxFightControls.RestoreCapturedCamera();
			bool wasActive = IsActive;
			Action onEnded = endedCallbackPending && request != null ? request.OnEnded : null;
			IsActive = false;
			endedCallbackPending = false;

			FreezeEmbeddedScene();
			UnbindHole();
			ClearAspectUtility();
			DestroyLeftoverHexGrids();
			DestroyOrphanFightUi();
			ClearFightSingletons();
			EmbeddedSceneHudFitter.RestorePersistentCanvases();
			RestoreLabMainCamera();
			request = null;
			expectedSceneName = null;
			gameplayCamera = null;

			if (embeddedScene.IsValid() && embeddedScene.isLoaded)
			{
				unloadOp = SceneManager.UnloadSceneAsync(embeddedScene);
			}
			else
			{
				UnloadFightSceneByName(lastSceneName);
			}

			embeddedScene = default(Scene);
			if (labScene.IsValid() && labScene.isLoaded)
			{
				try
				{
					SceneManager.SetActiveScene(labScene);
				}
				catch (ArgumentException)
				{
				}
			}

			if (wasActive && onEnded != null)
			{
				onEnded();
			}
		}

		/// <summary>Retries a missed bind and starts a request that waited for unload.</summary>
		internal static void Tick()
		{
			TryBindPending();
			if (unloadOp == null || !unloadOp.isDone)
			{
				return;
			}

			unloadOp = null;
			EmbeddedSceneRequest next = pendingStart;
			pendingStart = null;
			if (next != null)
			{
				Start(next);
			}
		}

		private static bool IsUnloading
		{
			get { return unloadOp != null && !unloadOp.isDone; }
		}

		/// <summary>Binds the hole if the requested scene is already loaded but <c>sceneLoaded</c> was missed.</summary>
		internal static void TryBindPending()
		{
			if (!IsActive || request == null || gameplayCamera != null)
			{
				return;
			}

			int count = SceneManager.sceneCount;
			for (int i = 0; i < count; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && NamesMatch(scene.name, expectedSceneName))
				{
					OnSceneLoaded(scene, LoadSceneMode.Additive);
					return;
				}
			}
		}

		/// <summary>Binds camera and HUD after an additive load that this host started.</summary>
		internal static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (mode != LoadSceneMode.Additive)
			{
				return;
			}

			if (!IsActive || request == null)
			{
				if (NamesMatch(scene.name, lastSceneName))
				{
					LokrLabPlugin.Log.LogInfo(
						"EmbeddedSceneHost: unloading leftover '" + scene.name + "' after stop.");
					unloadOp = SceneManager.UnloadSceneAsync(scene);
				}

				return;
			}

			if (gameplayCamera != null)
			{
				return;
			}

			if (!NamesMatch(scene.name, expectedSceneName))
			{
				LokrLabPlugin.Log.LogInfo(
					"EmbeddedSceneHost: ignoring additive '" + scene.name
					+ "' (waiting for '" + expectedSceneName + "').");
				return;
			}

			embeddedScene = scene;
			LokrLabPlugin.Log.LogInfo("EmbeddedSceneHost: scene loaded (" + scene.name + ").");
			try
			{
				SceneManager.SetActiveScene(scene);
			}
			catch (ArgumentException)
			{
			}

			if (MonoSingleton<LevelManager>.IsInstanceValid)
			{
				MonoSingleton<LevelManager>.Instance.runCinematics = false;
			}

			Camera gameplay = FindGameplayCamera(scene);
			if (gameplay == null)
			{
				Fail("Scene loaded but no gameplay camera was found.");
				return;
			}

			FitScene(scene, gameplay);
			request.OnCamera?.Invoke(gameplay);
			request.OnReady?.Invoke();
		}

		/// <summary>True when a canvas belongs to lab chrome and must not be remapped onto the hole camera.</summary>
		/// <remarks>
		/// Fight HUD can instantiate into the lab scene after Stop leaves the lab active.
		/// Those canvases still need hole remapping, so Icon / EndTurn / settings / forfeit
		/// confirm trees are not lab chrome. Leaving <see cref="UIOptions"/> as Overlay
		/// draws it on top of a Camera-space <see cref="UISimpleModalDialog"/> and the
		/// Forfeit Yes/No cannot be clicked. FadeScreen is DDOL Overlay loading chrome —
		/// remapping it onto the hole camera leaks sandbox zoom into the next transition.
		/// </remarks>
		internal static bool IsLabCanvas(Canvas canvas, Scene lab)
		{
			if (canvas == null)
			{
				return false;
			}

			if (canvas.GetComponentInParent<FadeScreen>() != null)
			{
				return true;
			}

			if (canvas.GetComponentInChildren<Icon>(true) != null
				|| canvas.GetComponentInChildren<EndTurn>(true) != null
				|| canvas.GetComponentInChildren<UISimpleModalDialog>(true) != null
				|| canvas.GetComponentInChildren<UIOptions>(true) != null)
			{
				return false;
			}

			if (lab.IsValid() && canvas.gameObject.scene == lab)
			{
				return true;
			}

			string name = canvas.gameObject.name;
			return name != null && (name.StartsWith("Lab", StringComparison.Ordinal)
				|| name.IndexOf("LokrLab", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static void Fail(string message)
		{
			Action<string> onFailed = request != null ? request.OnFailed : null;
			LokrLabPlugin.Log.LogWarning("EmbeddedSceneHost: " + message);
			endedCallbackPending = false;
			Stop();
			if (onFailed != null)
			{
				onFailed(message);
			}
		}

		private static void EnsureWatchdog(RectTransform hole)
		{
			if (hole == null)
			{
				return;
			}

			if (hole.GetComponent<EmbeddedSceneWatchdog>() == null)
			{
				hole.gameObject.AddComponent<EmbeddedSceneWatchdog>();
			}
		}

		private static Camera FindGameplayCamera(Scene scene)
		{
			if (MonoSingleton<CameraBase>.IsInstanceValid)
			{
				CameraBase cameraBase = MonoSingleton<CameraBase>.Instance;
				if (cameraBase != null && cameraBase.gameObject.activeInHierarchy)
				{
					Camera fromBase = cameraBase.GetComponent<Camera>();
					if (fromBase != null && scene.IsValid() && fromBase.gameObject.scene == scene)
					{
						return fromBase;
					}
				}
			}

			if (!scene.IsValid() || !scene.isLoaded)
			{
				return null;
			}

			Camera named = null;
			Camera first = null;
			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
				for (int j = 0; j < cameras.Length; j++)
				{
					Camera camera = cameras[j];
					if (camera == null)
					{
						continue;
					}

					if (first == null)
					{
						first = camera;
					}

					string name = camera.gameObject.name;
					if (name != null && (name.IndexOf("WorldCamera", StringComparison.OrdinalIgnoreCase) >= 0
						|| name.Equals("Main Camera", StringComparison.OrdinalIgnoreCase)))
					{
						named = camera;
					}
				}
			}

			if (named != null)
			{
				return named;
			}

			Camera main = Camera.main;
			if (main != null && main.gameObject.scene == scene)
			{
				return main;
			}

			return first;
		}

		private static void FitScene(Scene scene, Camera gameplay)
		{
			gameplayCamera = gameplay;
			if (gameplay == null)
			{
				return;
			}

			PromoteGameplayAsMain(gameplay);
			BindCameraBaseMain(gameplay);

			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (host != null && host.BackdropCamera != null)
			{
				gameplay.depth = host.BackdropCamera.depth + 3f;
			}

			Scene lab = host != null ? host.LabScene : labScene;
			if (request != null && request.Hole != null)
			{
				EmbeddedSceneBinder binder = request.Hole.GetComponent<EmbeddedSceneBinder>();
				if (binder == null)
				{
					binder = request.Hole.gameObject.AddComponent<EmbeddedSceneBinder>();
				}

				binder.Bind(gameplay, request.Hole);
			}

			if (request == null || request.FitHud)
			{
				EmbeddedSceneHudFitter fitter = gameplay.GetComponent<EmbeddedSceneHudFitter>();
				if (fitter == null)
				{
					fitter = gameplay.gameObject.AddComponent<EmbeddedSceneHudFitter>();
				}

				fitter.Bind(gameplay, lab);
			}

			if (request != null && !request.DisableExtraCameras)
			{
				return;
			}

			if (!scene.IsValid() || !scene.isLoaded)
			{
				return;
			}

			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				EventSystem[] systems = roots[i].GetComponentsInChildren<EventSystem>(true);
				for (int j = 0; j < systems.Length; j++)
				{
					systems[j].enabled = false;
				}

				Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
				for (int j = 0; j < cameras.Length; j++)
				{
					if (cameras[j] != gameplay && !IsGameplayCamera(cameras[j]))
					{
						cameras[j].enabled = false;
					}
				}

				AudioListener[] listeners = roots[i].GetComponentsInChildren<AudioListener>(true);
				for (int j = 0; j < listeners.Length; j++)
				{
					if (listeners[j].gameObject != gameplay.gameObject)
					{
						listeners[j].enabled = false;
					}
				}
			}

			EnableLabEventSystem();
		}

		/// <summary>Stops fight Update/LateUpdate and kills camera tweens before unload.</summary>
		private static void FreezeEmbeddedScene()
		{
			if (gameplayCamera != null)
			{
				DOTween.Kill(gameplayCamera.transform, false);
			}

			if (MonoSingleton<CameraBase>.IsInstanceValid)
			{
				CameraBase cameraBase = MonoSingleton<CameraBase>.Instance;
				if (cameraBase != null)
				{
					if (cameraBase.reusableTween != null)
					{
						cameraBase.reusableTween.Kill(false);
					}

					if (cameraBase.reusableOrthoSizeTweener != null)
					{
						cameraBase.reusableOrthoSizeTweener.Kill(false);
					}

					if (cameraBase.gameplayCamera != null)
					{
						cameraBase.gameplayCamera.enabled = false;
					}
				}
			}

			if (!embeddedScene.IsValid() || !embeddedScene.isLoaded)
			{
				return;
			}

			GameObject[] roots = embeddedScene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				if (roots[i] == null)
				{
					continue;
				}

				Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
				for (int j = 0; j < transforms.Length; j++)
				{
					DOTween.Kill(transforms[j], false);
				}

				roots[i].SetActive(false);
			}
		}

		/// <summary>Stops writing <see cref="Camera.rect"/> onto a camera that is about to die.</summary>
		private static void UnbindHole()
		{
			if (request == null || request.Hole == null)
			{
				return;
			}

			EmbeddedSceneBinder binder = request.Hole.GetComponent<EmbeddedSceneBinder>();
			if (binder != null)
			{
				binder.Unbind();
			}
		}

		/// <summary>Unloads a fight scene that Stop never tracked (load finished after Stop, or bind was missed).</summary>
		private static void UnloadFightSceneByName(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			int count = SceneManager.sceneCount;
			for (int i = 0; i < count; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && NamesMatch(scene.name, sceneName))
				{
					unloadOp = SceneManager.UnloadSceneAsync(scene);
					return;
				}
			}
		}

		/// <summary>HexGridRoot is created in the active scene (often the lab) and survives unload if never adopted.</summary>
		private static void DestroyLeftoverHexGrids()
		{
			Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if (transform == null || transform.gameObject == null || transform.name != "HexGridRoot")
				{
					continue;
				}

				if (transform.hideFlags != HideFlags.None)
				{
					continue;
				}

				UnityEngine.Object.Destroy(transform.gameObject);
			}
		}

		/// <summary>Removes fight HUD that spawned in the lab scene and would steal or block hole clicks.</summary>
		private static void DestroyOrphanFightUi()
		{
			Scene lab = labScene.IsValid() ? labScene : CharacterLabScene.LabScene;
			if (!lab.IsValid())
			{
				return;
			}

			Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
			for (int i = 0; i < canvases.Length; i++)
			{
				Canvas canvas = canvases[i];
				if (canvas == null || canvas.gameObject == null || canvas.hideFlags != HideFlags.None)
				{
					continue;
				}

				if (canvas.gameObject.scene != lab || IsLabChromeName(canvas.gameObject.name))
				{
					continue;
				}

				if (canvas.GetComponentInChildren<Icon>(true) != null
					|| canvas.GetComponentInChildren<EndTurn>(true) != null)
				{
					UnityEngine.Object.Destroy(canvas.gameObject);
				}
			}

			string[] names = { "SkillsBar", "SkillInfo" };
			Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if (transform == null || transform.gameObject == null || transform.hideFlags != HideFlags.None)
				{
					continue;
				}

				if (transform.gameObject.scene != lab)
				{
					continue;
				}

				string name = transform.gameObject.name;
				for (int n = 0; n < names.Length; n++)
				{
					if (name == names[n] || name.StartsWith(names[n], StringComparison.Ordinal))
					{
						UnityEngine.Object.Destroy(transform.gameObject);
						break;
					}
				}
			}
		}

		private static bool IsLabChromeName(string name)
		{
			return name != null && (name.StartsWith("Lab", StringComparison.Ordinal)
				|| name.IndexOf("LokrLab", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		/// <summary>Fight EventSystems are disabled so they cannot steal <see cref="EventSystem.current"/> from the lab.</summary>
		private static void EnableLabEventSystem()
		{
			GameObject labEs = GameObject.Find("LabEventSystem");
			if (labEs != null)
			{
				labEs.SetActive(false);
				labEs.SetActive(true);
				EventSystem system = labEs.GetComponent<EventSystem>();
				if (system != null)
				{
					system.enabled = true;
				}
			}
		}

		/// <summary>Nulls fight MonoSingletons so the next load does not SetBoard / pan through a disabled leftover.</summary>
		private static void ClearFightSingletons()
		{
			ClearSingleton<HexBoardViewComponent>();
			ClearSingleton<CameraBase>();
			ClearSingleton<LevelManager>();
			ClearSingleton<StageControllerComponent>();
			ClearSingleton<PowerBarsVisibility>();
		}

		/// <summary>Drops a fight singleton so the next additive load binds the new instance.</summary>
		private static void ClearSingleton<T>() where T : MonoSingleton<T>
		{
			Traverse.Create(typeof(MonoSingleton<T>)).Field("m_Instance").SetValue(null);
		}

		/// <summary>Drops the static AspectUtility camera so SetCamera cannot write the old hole rect.</summary>
		private static void ClearAspectUtility()
		{
			Traverse.Create(typeof(AspectUtility)).Field("cam").SetValue(null);
			Camera background = Traverse.Create(typeof(AspectUtility)).Field<Camera>("backgroundCam").Value;
			if (background != null)
			{
				UnityEngine.Object.Destroy(background.gameObject);
				Traverse.Create(typeof(AspectUtility)).Field("backgroundCam").SetValue(null);
			}
		}

		/// <summary>
		/// <see cref="CameraBase.Awake"/> stores <see cref="Camera.main"/> (the lab backdrop) as
		/// <c>mainCamera</c>. Auto-pan then WorldToViewportPoints through that camera and jumps
		/// the fight camera to a lab-space offset.
		/// </summary>
		private static void BindCameraBaseMain(Camera gameplay)
		{
			if (gameplay == null)
			{
				return;
			}

			CameraBase cameraBase = gameplay.GetComponent<CameraBase>();
			if (cameraBase == null && MonoSingleton<CameraBase>.IsInstanceValid)
			{
				cameraBase = MonoSingleton<CameraBase>.Instance;
			}

			if (cameraBase != null)
			{
				Traverse.Create(cameraBase).Field<Camera>("mainCamera").Value = gameplay;
			}
		}

		/// <summary>Makes LeanTouch / <see cref="Camera.main"/> use the hole camera, not a leftover menu camera.</summary>
		private static void PromoteGameplayAsMain(Camera gameplay)
		{
			Camera[] cameras = Camera.allCameras;
			for (int i = 0; i < cameras.Length; i++)
			{
				Camera camera = cameras[i];
				if (camera != null && camera != gameplay && camera.CompareTag("MainCamera"))
				{
					camera.tag = "Untagged";
				}
			}

			if (gameplay != null)
			{
				gameplay.tag = "MainCamera";
			}
		}

		/// <summary>Puts the MainCamera tag back on the lab backdrop after unload.</summary>
		/// <remarks>
		/// PromoteGameplayAsMain leaves the hole camera tagged MainCamera. Unload is async, so
		/// that zoomed camera is still Camera.main when FadeScreen / transitionscene run.
		/// Untag and disable it first so the backdrop (ortho 5) is the only MainCamera.
		/// Must run before Stop clears <c>gameplayCamera</c>.
		/// </remarks>
		private static void RestoreLabMainCamera()
		{
			Camera hole = gameplayCamera;
			if (hole != null)
			{
				hole.enabled = false;
				if (hole.CompareTag("MainCamera"))
				{
					hole.tag = "Untagged";
				}
			}

			LabHost host = LokrLabApi.LokrLabApi.Host;
			Camera backdrop = host != null ? host.BackdropCamera : null;
			if (backdrop == null)
			{
				backdrop = CharacterLabScene.BackdropCamera;
			}

			Camera[] cameras = Camera.allCameras;
			for (int i = 0; i < cameras.Length; i++)
			{
				Camera camera = cameras[i];
				if (camera != null && camera != backdrop && camera.CompareTag("MainCamera"))
				{
					camera.tag = "Untagged";
				}
			}

			if (backdrop != null)
			{
				backdrop.tag = "MainCamera";
				backdrop.orthographicSize = 5f;
				backdrop.rect = new Rect(0f, 0f, 1f, 1f);
			}
		}

		private static bool IsGameplayCamera(Camera camera)
		{
			if (camera == null)
			{
				return false;
			}

			if (camera.GetComponent<CameraBase>() != null)
			{
				return true;
			}

			string name = camera.gameObject.name;
			return name != null && name.IndexOf("WorldCamera", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool NamesMatch(string loaded, string requested)
		{
			if (string.IsNullOrEmpty(loaded) || string.IsNullOrEmpty(requested))
			{
				return false;
			}

			return string.Equals(loaded, requested, StringComparison.OrdinalIgnoreCase);
		}
	}
}
