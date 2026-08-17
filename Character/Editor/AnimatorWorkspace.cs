using LokrLab.Editor.General;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Character Animator workspace: full-size edit camera in the center dock, with an optional preview PIP.</summary>
	/// <remarks>
	/// Inspector stays on the shell dock; Timeline / Checklist / History are Phase 6 bottom
	/// panels. This only owns viewport cameras and ToolbarPanel. Selecting a Part or Clip in
	/// the Node Tree calls SelectPartByName / SelectClipByName so the live InspectorPanel can
	/// refresh. Reloads the rig only when this folder is not already in the runtime, so
	/// switching away and back does not destroy unsaved viewport state. The in-engine preview
	/// is a small bottom-right overlay on the edit viewport, not a second column.
	/// </remarks>
	internal static class AnimatorWorkspace
	{
		private const float PreviewWidth = 280f;
		private const float PreviewHeight = 210f;
		private const float PreviewInset = 8f;
		private const float PreviewBorder = 2f;

		private static GameObject previewBox;
		private static bool previewVisible = true;

		/// <summary>Remembered show/hide for the preview overlay (survives workspace tab switches).</summary>
		internal static bool PreviewEnabled => previewVisible;

		/// <summary>True when the preview PIP is shown and its box is live in the current viewport.</summary>
		internal static bool IsPreviewVisible => previewVisible && previewBox != null && previewBox.activeInHierarchy;

		/// <summary>Builds the full-size edit slot and the bottom-right preview overlay, then binds the shell cameras.</summary>
		internal static GameObject BuildViewport(Transform parent)
		{
			UiTheme theme = UiTheme.Default;
			RectTransform editSlot = CreateStretchSlot(parent, "EditViewport");
			AddSlotLabel(editSlot, "Editable (drag parts here)", theme);

			previewBox = CreatePreviewBox(editSlot, theme);
			RectTransform previewSlot = previewBox.transform.Find("PreviewSlot") as RectTransform;
			AddSlotLabel(previewSlot, "Preview", theme);

			Scene scene = Lab.LabScene;
			RigEditorScene.EnsureShellRuntime(scene, Lab.Canvas);
			RigEditorScene.SetRuntimeActive(true);
			string folder = CharacterSession.Folder;
			if (string.IsNullOrEmpty(folder) && LokrLabApi.LokrLabApi.CurrentSession != null)
			{
				folder = LokrLabApi.LokrLabApi.CurrentSession.FolderPath;
			}

			if (!string.IsNullOrEmpty(folder) && !RigEditorScene.HasLoadedFolder(folder))
			{
				RigEditorScene.OnLoadClicked(folder);
			}

			Bind(editSlot, RigEditorScene.ActiveCamera, isMain: true);
			Bind(previewSlot, RigEditorScene.PreviewCamera, isMain: false);
			ApplyPreviewVisible();
			return editSlot.gameObject;
		}

		/// <summary>Builds the Animator tool strip into the shell toolbar slot.</summary>
		internal static GameObject BuildToolbar(Transform parent)
		{
			UiLabel status = ToolbarPanel.BuildInto(parent);
			RigEditorScene.SetShellStatusLabel(status);
			return status.GameObject;
		}

		/// <summary>Shows or hides the in-engine preview overlay. Remembered for the rest of this lab session.</summary>
		internal static void SetPreviewVisible(bool visible)
		{
			previewVisible = visible;
			ApplyPreviewVisible();
			ToolbarPanel.RefreshPreviewToggle();
		}

		/// <summary>Toggles the in-engine preview overlay.</summary>
		internal static void TogglePreview()
		{
			SetPreviewVisible(!previewVisible);
		}

		/// <summary>Hides cameras and input when leaving the Animator workspace.</summary>
		internal static void OnDeactivated()
		{
			RigEditorScene.SetRuntimeActive(false);
		}

		private static void ApplyPreviewVisible()
		{
			if (previewBox != null)
			{
				previewBox.SetActive(previewVisible);
			}

			if (RigEditorScene.PreviewCamera != null)
			{
				RigEditorScene.PreviewCamera.enabled = previewVisible;
			}
		}

		private static RectTransform CreateStretchSlot(Transform parent, string name)
		{
			GameObject slot = new GameObject(name, typeof(RectTransform));
			slot.transform.SetParent(parent, false);
			RectTransform rect = slot.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			return rect;
		}

		private static GameObject CreatePreviewBox(Transform editSlot, UiTheme theme)
		{
			GameObject box = new GameObject("PreviewBox", typeof(RectTransform));
			box.transform.SetParent(editSlot, false);
			RectTransform boxRect = box.GetComponent<RectTransform>();
			boxRect.anchorMin = new Vector2(1f, 0f);
			boxRect.anchorMax = new Vector2(1f, 0f);
			boxRect.pivot = new Vector2(1f, 0f);
			boxRect.sizeDelta = new Vector2(PreviewWidth, PreviewHeight);
			boxRect.anchoredPosition = new Vector2(-PreviewInset, PreviewInset);

			AddPreviewBorder(box.transform, theme, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, PreviewBorder));
			AddPreviewBorder(box.transform, theme, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, PreviewBorder));
			AddPreviewBorder(box.transform, theme, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(PreviewBorder, 0f));
			AddPreviewBorder(box.transform, theme, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(PreviewBorder, 0f));

			GameObject slot = new GameObject("PreviewSlot", typeof(RectTransform));
			slot.transform.SetParent(box.transform, false);
			RectTransform slotRect = slot.GetComponent<RectTransform>();
			slotRect.anchorMin = Vector2.zero;
			slotRect.anchorMax = Vector2.one;
			slotRect.offsetMin = new Vector2(PreviewBorder, PreviewBorder);
			slotRect.offsetMax = new Vector2(-PreviewBorder, -PreviewBorder);
			return box;
		}

		private static void AddPreviewBorder(Transform parent, UiTheme theme, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
		{
			GameObject edge = new GameObject("Border", typeof(Image));
			edge.transform.SetParent(parent, false);
			Image image = edge.GetComponent<Image>();
			image.color = theme.AccentColor;
			image.raycastTarget = false;
			RectTransform rect = edge.GetComponent<RectTransform>();
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = new Vector2(anchorMin.x == 1f ? 1f : 0f, anchorMin.y == 1f ? 1f : 0f);
			rect.sizeDelta = sizeDelta;
			rect.anchoredPosition = Vector2.zero;
		}

		private static void AddSlotLabel(Transform slot, string text, UiTheme theme)
		{
			if (slot == null)
			{
				return;
			}

			UiLabel label = UiLabel.Create(slot, text, theme, 12, TextAnchor.UpperCenter);
			label.RectTransform.anchorMin = new Vector2(0f, 1f);
			label.RectTransform.anchorMax = new Vector2(1f, 1f);
			label.RectTransform.pivot = new Vector2(0.5f, 1f);
			label.RectTransform.sizeDelta = new Vector2(0f, 22f);
			label.RectTransform.anchoredPosition = Vector2.zero;
		}

		private static void Bind(Transform slot, Camera camera, bool isMain)
		{
			if (slot == null || camera == null)
			{
				return;
			}

			ViewportCameraBinder binder = slot.gameObject.GetComponent<ViewportCameraBinder>();
			if (binder == null)
			{
				binder = slot.gameObject.AddComponent<ViewportCameraBinder>();
			}

			binder.Bind(camera, slot as RectTransform ?? slot.GetComponent<RectTransform>(), isMain);
		}
	}
}
