using System.Collections.Generic;
using System.Text;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LokrLab
{
	/// <summary>Keeps Overlay HUD canvases inside the embed hole.</summary>
	/// <remarks>
	/// Assigning <see cref="Canvas.renderMode"/> every frame resets <see cref="Camera.rect"/>
	/// (full-screen grass behind the lab) and can disable PortraitSkill long enough
	/// to miss <c>UnitEndedActivity</c> (skills stay gray). Only write when a value
	/// actually changed, and scale from the last hole rect — not a full-screen
	/// <see cref="Camera.pixelRect"/>. FadeScreen is DDOL Overlay chrome: remapping it
	/// onto the hole camera makes the next loading graphic inherit sandbox zoom.
	/// See docs/issues/resolved/sandbox-zoom-leaks-into-loading-ui.md.
	/// </remarks>
	[DefaultExecutionOrder(32000)]
	internal sealed class EmbeddedSceneHudFitter : MonoBehaviour
	{
		/// <summary>Above typical fight HUD so the forfeit confirm is not under settings.</summary>
		private const int ModalSortingOrder = 500;

		/// <summary>Closer to the hole camera than the default FitCanvas plane (1).</summary>
		private const float ModalPlaneDistance = 0.5f;

		private Camera gameplay;
		private Scene labScene;
		private Rect lastHole;
		/// <summary>True after the first visible forfeit confirm has been logged this embed.</summary>
		private bool loggedConfirm;
		/// <summary>True while settings CanvasGroups are muted for a visible confirm.</summary>
		private bool optionsMuted;
		/// <summary>Settings CanvasGroup values captured just before mute, restored on dismiss.</summary>
		private readonly List<CanvasGroupSnapshot> mutedOptions = new List<CanvasGroupSnapshot>();

		private static bool fadeScreenSnapshotTaken;
		private static RenderMode savedFadeRenderMode;
		private static Camera savedFadeWorldCamera;
		private static bool savedFadeHasScaler;
		private static CanvasScaler.ScaleMode savedFadeScaleMode;
		private static float savedFadeScaleFactor;
		private static Vector2 savedFadeReferenceResolution;
		private static CanvasScaler.ScreenMatchMode savedFadeScreenMatchMode;
		private static float savedFadeMatchWidthOrHeight;

		/// <summary>Remembers the gameplay camera and lab scene, then fits canvases once.</summary>
		internal void Bind(Camera camera, Scene lab)
		{
			gameplay = camera;
			labScene = lab;
			CaptureFadeScreenIfNeeded();
			Apply();
		}

		/// <summary>Puts FadeScreen back to Overlay (snapshot if captured, otherwise force Overlay).</summary>
		/// <remarks>
		/// FitCanvas used to rewrite every non-lab Overlay canvas, including DDOL FadeScreen.
		/// Stop must undo that before Close Lab's ShowFadeOut, or the loading graphic keeps
		/// the hole camera's orthographicSize. See docs/issues/resolved/sandbox-zoom-leaks-into-loading-ui.md.
		/// </remarks>
		internal static void RestorePersistentCanvases()
		{
			Canvas canvas = FindFadeScreenCanvas();
			if (fadeScreenSnapshotTaken)
			{
				if (canvas != null)
				{
					canvas.renderMode = savedFadeRenderMode;
					canvas.worldCamera = savedFadeWorldCamera;
					CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
					if (savedFadeHasScaler)
					{
						if (scaler == null)
						{
							scaler = canvas.gameObject.AddComponent<CanvasScaler>();
						}

						scaler.uiScaleMode = savedFadeScaleMode;
						scaler.scaleFactor = savedFadeScaleFactor;
						scaler.referenceResolution = savedFadeReferenceResolution;
						scaler.screenMatchMode = savedFadeScreenMatchMode;
						scaler.matchWidthOrHeight = savedFadeMatchWidthOrHeight;
					}
				}

				fadeScreenSnapshotTaken = false;
				return;
			}

			if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
			{
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.worldCamera = null;
			}
		}

		private void LateUpdate()
		{
			Apply();
		}

		private void Apply()
		{
			if (gameplay == null)
			{
				return;
			}

			CaptureFadeScreenIfNeeded();
			RememberHole(gameplay.rect);
			DisableStrayCameras();
			Rect pixel = HolePixels();
			if (pixel.width < 2f || pixel.height < 2f)
			{
				return;
			}

			Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
			for (int i = 0; i < canvases.Length; i++)
			{
				FitCanvas(canvases[i], pixel);
			}

			MuteSettingsWhileConfirmVisible();
			SandboxFightControls.BindConfirmCanvases(gameplay);
		}

		private void RememberHole(Rect normalized)
		{
			if (normalized.width > 0.02f && normalized.height > 0.02f
				&& (normalized.width < 0.98f || normalized.height < 0.98f))
			{
				lastHole = normalized;
			}
		}

		private Rect HolePixels()
		{
			Rect normalized = lastHole.width > 0.02f ? lastHole : gameplay.rect;
			return new Rect(
				normalized.x * Screen.width,
				normalized.y * Screen.height,
				normalized.width * Screen.width,
				normalized.height * Screen.height);
		}

		private void DisableStrayCameras()
		{
			Camera[] cameras = Camera.allCameras;
			for (int i = 0; i < cameras.Length; i++)
			{
				Camera camera = cameras[i];
				if (camera == null || camera == gameplay || !camera.enabled)
				{
					continue;
				}

				if (camera.cullingMask == 0
					|| (labScene.IsValid() && camera.gameObject.scene == labScene))
				{
					continue;
				}

				string name = camera.gameObject.name;
				if (name != null && (name.StartsWith("Lab", System.StringComparison.Ordinal)
					|| name.IndexOf("LokrLab", System.StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("BackgroundCam", System.StringComparison.Ordinal) >= 0
					|| name.IndexOf("WorldCamera", System.StringComparison.OrdinalIgnoreCase) >= 0))
				{
					continue;
				}

				if (camera.GetComponent<CameraBase>() != null)
				{
					continue;
				}

				camera.enabled = false;
			}
		}

		private void FitCanvas(Canvas canvas, Rect pixel)
		{
			if (canvas == null || canvas.renderMode == RenderMode.WorldSpace
				|| EmbeddedSceneHost.IsLabCanvas(canvas, labScene)
				|| IsFadeScreenCanvas(canvas))
			{
				return;
			}

			if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != gameplay)
			{
				Rect saved = gameplay.rect;
				canvas.renderMode = RenderMode.ScreenSpaceCamera;
				canvas.worldCamera = gameplay;
				canvas.planeDistance = 1f;
				gameplay.rect = saved;
			}

			CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
			if (scaler == null)
			{
				scaler = canvas.gameObject.AddComponent<CanvasScaler>();
				scaler.referenceResolution = new Vector2(1920f, 1080f);
			}

			float refW = scaler.referenceResolution.x > 1f ? scaler.referenceResolution.x : 1920f;
			float refH = scaler.referenceResolution.y > 1f ? scaler.referenceResolution.y : 1080f;
			float scale = Mathf.Max(0.05f, Mathf.Min(pixel.width / refW, pixel.height / refH));
			if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize
				|| !Mathf.Approximately(scaler.scaleFactor, scale))
			{
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
				scaler.scaleFactor = scale;
			}

			PromoteVisibleModal(canvas);
		}

		/// <summary>Stacks a visible <see cref="UISimpleModalDialog"/> above the settings panel.</summary>
		/// <remarks>
		/// Remapping Overlay HUD to Screen Space Camera puts every fight canvas on
		/// <c>planeDistance</c> 1. Settings then draws over Forfeit Yes/No. Sibling
		/// order covers a shared canvas; sort + plane cover a separate confirm canvas.
		/// See docs/issues/unresolved/sandbox-forfeit-confirm-behind-settings.md.
		/// </remarks>
		private void PromoteVisibleModal(Canvas canvas)
		{
			UISimpleModalDialog modal = canvas.GetComponentInChildren<UISimpleModalDialog>(true);
			if (modal == null || !modal.IsVisible)
			{
				return;
			}

			modal.transform.SetAsLastSibling();
			if (!canvas.overrideSorting)
			{
				canvas.overrideSorting = true;
			}

			if (canvas.sortingOrder < ModalSortingOrder)
			{
				canvas.sortingOrder = ModalSortingOrder;
			}

			if (canvas.renderMode == RenderMode.ScreenSpaceCamera
				&& !Mathf.Approximately(canvas.planeDistance, ModalPlaneDistance))
			{
				canvas.planeDistance = ModalPlaneDistance;
			}
		}

		/// <summary>Hides settings raycasts while Forfeit Yes/No is up, then restores them.</summary>
		/// <remarks>
		/// UIOptions keeps a full-screen CanvasGroup that still covers Yes/No after the 0.12.6
		/// sort/plane promotion. Muting that sheet (not another overrideSorting) is the Lab
		/// guard. CloseWindow is not called — that can drop the forfeit callback. See
		/// docs/issues/unresolved/sandbox-forfeit-confirm-behind-settings.md.
		/// </remarks>
		private void MuteSettingsWhileConfirmVisible()
		{
			UISimpleModalDialog modal = FindVisibleConfirm();
			if (modal == null)
			{
				RestoreMutedOptions();
				return;
			}

			if (modal.GetComponentInParent<UIOptions>() != null)
			{
				Canvas dest = FindHoleHudCanvasExcludingOptions();
				if (dest != null)
				{
					modal.transform.SetParent(dest.transform, true);
				}
			}

			LogConfirmOnce(modal);
			MuteOptions();
		}

		/// <summary>The first visible <see cref="UISimpleModalDialog"/>, or null.</summary>
		private static UISimpleModalDialog FindVisibleConfirm()
		{
			UISimpleModalDialog[] modals = Object.FindObjectsOfType<UISimpleModalDialog>(true);
			for (int i = 0; i < modals.Length; i++)
			{
				UISimpleModalDialog modal = modals[i];
				if (modal != null && modal.IsVisible)
				{
					return modal;
				}
			}

			return null;
		}

		/// <summary>A hole-fitted fight HUD canvas that is not the settings sheet.</summary>
		private Canvas FindHoleHudCanvasExcludingOptions()
		{
			EndTurn endTurn = Object.FindObjectOfType<EndTurn>(true);
			if (endTurn != null)
			{
				Canvas canvas = endTurn.GetComponentInParent<Canvas>();
				if (canvas != null && canvas.GetComponentInChildren<UIOptions>(true) == null)
				{
					return canvas;
				}
			}

			Icon[] icons = Object.FindObjectsOfType<Icon>(true);
			for (int i = 0; i < icons.Length; i++)
			{
				if (icons[i] == null)
				{
					continue;
				}

				Canvas canvas = icons[i].GetComponentInParent<Canvas>();
				if (canvas != null && canvas.GetComponentInChildren<UIOptions>(true) == null)
				{
					return canvas;
				}
			}

			return null;
		}

		/// <summary>Sets every UIOptions CanvasGroup to invisible and non-raycasting.</summary>
		private void MuteOptions()
		{
			if (optionsMuted)
			{
				return;
			}

			UIOptions[] options = Object.FindObjectsOfType<UIOptions>(true);
			for (int i = 0; i < options.Length; i++)
			{
				UIOptions sheet = options[i];
				if (sheet == null)
				{
					continue;
				}

				CanvasGroup group = sheet.GetComponent<CanvasGroup>();
				if (group == null)
				{
					group = sheet.GetComponentInChildren<CanvasGroup>(true);
				}

				if (group == null)
				{
					group = sheet.gameObject.AddComponent<CanvasGroup>();
				}

				mutedOptions.Add(new CanvasGroupSnapshot
				{
					Group = group,
					Alpha = group.alpha,
					Blocks = group.blocksRaycasts,
					Interactable = group.interactable,
				});
				group.alpha = 0f;
				group.blocksRaycasts = false;
				group.interactable = false;
			}

			optionsMuted = true;
		}

		/// <summary>Restores UIOptions CanvasGroups captured by <see cref="MuteOptions"/>.</summary>
		private void RestoreMutedOptions()
		{
			if (!optionsMuted)
			{
				return;
			}

			for (int i = 0; i < mutedOptions.Count; i++)
			{
				CanvasGroupSnapshot snap = mutedOptions[i];
				if (snap.Group == null)
				{
					continue;
				}

				snap.Group.alpha = snap.Alpha;
				snap.Group.blocksRaycasts = snap.Blocks;
				snap.Group.interactable = snap.Interactable;
			}

			mutedOptions.Clear();
			optionsMuted = false;
		}

		/// <summary>One-shot hierarchy dump the first time a confirm is visible in this embed.</summary>
		private void LogConfirmOnce(UISimpleModalDialog modal)
		{
			if (loggedConfirm || modal == null)
			{
				return;
			}

			loggedConfirm = true;
			Canvas canvas = modal.GetComponentInParent<Canvas>();
			UIOptions underOptions = modal.GetComponentInParent<UIOptions>();
			CanvasGroup optionsGroup = null;
			UIOptions[] sheets = Object.FindObjectsOfType<UIOptions>(true);
			if (sheets.Length > 0 && sheets[0] != null)
			{
				optionsGroup = sheets[0].GetComponent<CanvasGroup>();
				if (optionsGroup == null)
				{
					optionsGroup = sheets[0].GetComponentInChildren<CanvasGroup>(true);
				}
			}

			StringBuilder line = new StringBuilder();
			line.Append("EmbeddedSceneHudFitter: forfeit confirm visible. type=")
				.Append(modal.GetType().Name)
				.Append(" name=").Append(modal.gameObject.name)
				.Append(" parent=").Append(TransformPath(modal.transform))
				.Append(" underUIOptions=").Append(underOptions != null);
			if (canvas != null)
			{
				line.Append(" canvasMode=").Append(canvas.renderMode)
					.Append(" sortingOrder=").Append(canvas.sortingOrder)
					.Append(" planeDistance=").Append(canvas.planeDistance)
					.Append(" overrideSorting=").Append(canvas.overrideSorting)
					.Append(" sibling=").Append(modal.transform.GetSiblingIndex());
			}

			if (optionsGroup != null)
			{
				line.Append(" UIOptions.alpha=").Append(optionsGroup.alpha)
					.Append(" blocksRaycasts=").Append(optionsGroup.blocksRaycasts)
					.Append(" interactable=").Append(optionsGroup.interactable);
			}

			LokrLabPlugin.Log.LogInfo(line.ToString());
		}

		/// <summary>Root-to-leaf GameObject names for a transform.</summary>
		private static string TransformPath(Transform transform)
		{
			if (transform == null)
			{
				return string.Empty;
			}

			string path = transform.name;
			Transform parent = transform.parent;
			while (parent != null)
			{
				path = parent.name + "/" + path;
				parent = parent.parent;
			}

			return path;
		}

		private static void CaptureFadeScreenIfNeeded()
		{
			if (fadeScreenSnapshotTaken)
			{
				return;
			}

			Canvas canvas = FindFadeScreenCanvas();
			if (canvas == null)
			{
				return;
			}

			fadeScreenSnapshotTaken = true;
			savedFadeRenderMode = canvas.renderMode;
			savedFadeWorldCamera = canvas.worldCamera;
			CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
			savedFadeHasScaler = scaler != null;
			if (scaler != null)
			{
				savedFadeScaleMode = scaler.uiScaleMode;
				savedFadeScaleFactor = scaler.scaleFactor;
				savedFadeReferenceResolution = scaler.referenceResolution;
				savedFadeScreenMatchMode = scaler.screenMatchMode;
				savedFadeMatchWidthOrHeight = scaler.matchWidthOrHeight;
			}
		}

		private static Canvas FindFadeScreenCanvas()
		{
			if (!MonoSingleton<FadeScreen>.IsInstanceValid)
			{
				return null;
			}

			FadeScreen fade = MonoSingleton<FadeScreen>.Instance;
			if (fade == null)
			{
				return null;
			}

			Canvas canvas = fade.GetComponent<Canvas>();
			return canvas != null ? canvas : fade.GetComponentInChildren<Canvas>(true);
		}

		private static bool IsFadeScreenCanvas(Canvas canvas)
		{
			return canvas != null && canvas.GetComponentInParent<FadeScreen>() != null;
		}

		/// <summary>Saved <see cref="CanvasGroup"/> fields for one muted settings sheet.</summary>
		private struct CanvasGroupSnapshot
		{
			/// <summary>The group that was muted.</summary>
			internal CanvasGroup Group;
			/// <summary>Alpha before mute.</summary>
			internal float Alpha;
			/// <summary>blocksRaycasts before mute.</summary>
			internal bool Blocks;
			/// <summary>interactable before mute.</summary>
			internal bool Interactable;
		}
	}
}
