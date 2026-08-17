using DG.Tweening;
using Ironhide.Legends;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Screens.Transition;
using LokrAbilityLab.Editor;
using SimpleUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LokrAbilityLab
{
	/// <summary>Ability Lab as a real scene transition — the scene the player was in is unloaded, not hidden under an overlay.</summary>
	/// <remarks>
	/// Same primitives as LokrLab's CharacterLabScene: <see cref="FadeScreen"/> (the game's load
	/// graphic; there is no separate progress-bar asset), <see cref="SceneManager.UnloadSceneAsync(string)"/>
	/// of the origin scene, and <see cref="TransitionSceneComponent"/> to return. Enter is fade +
	/// explicit unload because TransitionSceneComponent cannot target a CreateScene scene. Close
	/// uses LoadSceneMode.Single, which destroys this scene — <see cref="isBuilt"/> resets so the
	/// next Open rebuilds. This is the fallback when JumpToProject is not assigned; the primary
	/// path opens the Ability Library inside LokrLab and already uses that host's fade.
	/// </remarks>
	internal static class AbilityLabScene
	{
		private const string SceneName = "LokrAbilityLab";
		private const int CanvasSortOrder = 20000;

		private static Scene labScene;
		private static bool isBuilt;
		private static bool isOpen;
		/// <summary>Fallback-scene canvas, or null when the library is open in the LokrLab shell.</summary>
		internal static Transform Canvas { get; private set; }
		private static string originScene;
		private static string sceneToUnloadOnOpen;

		private const string ListScreen = "List";
		private const string EditorScreen = "Editor";
		private static readonly UiScreenSwitcher screens = new UiScreenSwitcher();

		/// <summary>True while the lab scene is the active editor.</summary>
		internal static bool IsOpen => isOpen;

		internal static void Toggle()
		{
			RecoverIfSceneWasDestroyed();

			if (isOpen)
			{
				Close();
			}
			else
			{
				Open();
			}
		}

		/// <summary>If a Single-mode load elsewhere destroyed this scene, drop stale flags.</summary>
		private static void RecoverIfSceneWasDestroyed()
		{
			if (isBuilt && !labScene.IsValid())
			{
				isBuilt = false;
				isOpen = false;
				screens.Clear();
				Canvas = null;
				UiFileBrowser.ReleaseModal();
			}
		}

		/// <summary>Fades out, unloads the current real scene, then builds/shows the lab.</summary>
		internal static void Open()
		{
			RecoverIfSceneWasDestroyed();

			if (isOpen)
			{
				return;
			}

			originScene = SceneManager.GetActiveScene().name;
			sceneToUnloadOnOpen = originScene;

			if (MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				MonoSingleton<FadeScreen>.Instance.ShowFadeOut(withLoading: true, withTips: false).OnComplete(BeginOpenAfterFade);
			}
			else
			{
				BeginOpenAfterFade();
			}
		}

		/// <summary>Builds the lab before unloading the origin — Unity will not unload the only loaded scene.</summary>
		private static void BeginOpenAfterFade()
		{
			EnsureBuilt();
			SceneManager.SetActiveScene(labScene);

			AsyncOperation unload = SceneManager.UnloadSceneAsync(sceneToUnloadOnOpen);
			if (unload != null)
			{
				unload.completed += _ => FinishOpen();
			}
			else
			{
				FinishOpen();
			}
		}

		private static void FinishOpen()
		{
			SetLabRootsActive(true);

			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

			isOpen = true;
			AbilityListPanel.Refresh();
			screens.Show(ListScreen);

			if (MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				MonoSingleton<FadeScreen>.Instance.ShowFadeIn(withLoading: true);
			}
		}

		/// <summary>Fades out and returns to the real scene this lab was opened from.</summary>
		internal static void Close()
		{
			RecoverIfSceneWasDestroyed();

			if (!isOpen)
			{
				return;
			}

			isOpen = false;
			isBuilt = false;
			screens.Clear();
			Canvas = null;
			UiFileBrowser.ReleaseModal();

			if (MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				MonoSingleton<FadeScreen>.Instance.ShowFadeOut(withLoading: true, withTips: false)
					.OnComplete(() => TransitionSceneComponent.TransitionToNextScene("scenes", originScene));
			}
			else
			{
				TransitionSceneComponent.TransitionToNextScene("scenes", originScene);
			}
		}

		/// <summary>Best-effort cleanup when some other transition already destroyed or is destroying this scene.</summary>
		internal static void ForceClose()
		{
			RecoverIfSceneWasDestroyed();
			isOpen = false;
			if (!labScene.IsValid())
			{
				isBuilt = false;
				screens.Clear();
				Canvas = null;
				UiFileBrowser.ReleaseModal();
				return;
			}

			SetLabRootsActive(false);
		}

		/// <summary>Switches to the Editor screen and loads the given ability file.</summary>
		internal static void OpenAbility(string filePath)
		{
			if (!isOpen)
			{
				return;
			}
			AbilityEditorPanel.Load(filePath);
			screens.Show(EditorScreen);
		}

		/// <summary>Switches back to the List screen, refreshed.</summary>
		internal static void BackToList()
		{
			if (!isOpen)
			{
				return;
			}
			AbilityListPanel.Refresh();
			screens.Show(ListScreen);
		}

		private static void EnsureBuilt()
		{
			if (isBuilt && labScene.IsValid())
			{
				return;
			}

			AbilityLabPaths.EnsureFoldersExist();

			labScene = SceneManager.CreateScene(SceneName);

			BuildCamera(labScene);
			BuildEventSystem(labScene);
			Transform canvas = BuildUI(labScene);

			AbilityListPanel.Build(screens.GetRoot(ListScreen), DefaultFont);
			AbilityEditorPanel.Build(screens.GetRoot(EditorScreen), DefaultFont);

			SetLabRootsActive(false);
			isBuilt = true;
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

		private static void BuildCamera(Scene scene)
		{
			GameObject cameraObject = new GameObject("LabCamera", typeof(Camera));
			Camera camera = cameraObject.GetComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
			camera.orthographic = true;
			camera.orthographicSize = 5f;
			camera.depth = 100f;
			cameraObject.transform.position = new Vector3(0f, 0f, -10f);
			SceneManager.MoveGameObjectToScene(cameraObject, scene);
		}

		private static void BuildEventSystem(Scene scene)
		{
			GameObject eventSystemObject = new GameObject("LabEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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
			Canvas = canvasObject.transform;

			GameObject backdropObject = new GameObject("LabBackdrop", typeof(Image));
			backdropObject.transform.SetParent(canvasObject.transform, false);
			RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
			backdropRect.anchorMin = Vector2.zero;
			backdropRect.anchorMax = Vector2.one;
			backdropRect.offsetMin = Vector2.zero;
			backdropRect.offsetMax = Vector2.zero;
			backdropObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
			backdropObject.transform.SetAsFirstSibling();

			CreateLabel(canvasObject.transform, "TitleLabel", "LoKR Ability Lab",
				28, new Vector2(0.5f, 0.968f), new Vector2(700f, 40f), DefaultFont);

			CreateButton(canvasObject.transform, "CloseButton", "Close",
				new Vector2(0.5f, 0.04f), new Vector2(280f, 50f), DefaultFont, Close);

			screens.Register(ListScreen, canvasObject.transform);
			screens.Register(EditorScreen, canvasObject.transform);

			return canvasObject.transform;
		}

		internal static readonly Font DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

		/// <summary>Opaque shell every screen's content builds under, matching CharacterLabScene's own ContentFrame convention.</summary>
		internal static Transform GetContentRoot(Transform screenRoot)
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

		private static void CreateLabel(Transform parent, string name, string text, int fontSize,
			Vector2 anchorCenter, Vector2 size, Font font)
		{
			GameObject labelObject = new GameObject(name, typeof(Text));
			labelObject.transform.SetParent(parent, false);
			RectTransform rect = labelObject.GetComponent<RectTransform>();
			rect.anchorMin = anchorCenter;
			rect.anchorMax = anchorCenter;
			rect.sizeDelta = size;
			rect.anchoredPosition = Vector2.zero;

			Text label = labelObject.GetComponent<Text>();
			label.text = text;
			label.font = font;
			label.fontSize = fontSize;
			label.alignment = TextAnchor.MiddleCenter;
			label.color = Color.white;
		}

		private static void CreateButton(Transform parent, string name, string label,
			Vector2 anchorCenter, Vector2 size, Font font, UnityEngine.Events.UnityAction onClick)
		{
			GameObject buttonObject = new GameObject(name, typeof(Image), typeof(Button));
			buttonObject.transform.SetParent(parent, false);
			RectTransform rect = buttonObject.GetComponent<RectTransform>();
			rect.anchorMin = anchorCenter;
			rect.anchorMax = anchorCenter;
			rect.sizeDelta = size;
			rect.anchoredPosition = Vector2.zero;

			Image image = buttonObject.GetComponent<Image>();
			image.color = new Color(0.2f, 0.4f, 0.75f);

			Button button = buttonObject.GetComponent<Button>();
			button.onClick.AddListener(onClick);

			CreateLabel(buttonObject.transform, "Label", label, 20, new Vector2(0.5f, 0.5f), size, font);
		}
	}
}
