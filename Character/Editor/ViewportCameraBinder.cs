using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Keeps a Camera.rect matched to a UI RectTransform every frame (Screen Space Overlay).</summary>
	/// <remarks>
	/// The center Viewport host is a stretch rect with a cleared dock background, so the camera
	/// output is visible through the overlay. LateUpdate runs after layout so splitter resize
	/// and the preview PIP stay aligned. The preview overlay is a transparent hole with a border;
	/// its camera has a higher depth and draws on top of the edit camera in that corner.
	/// </remarks>
	internal sealed class ViewportCameraBinder : MonoBehaviour
	{
		private Camera target;
		private RectTransform slot;

		/// <summary>Binder for the Editable (main) slot, or null when Animator is not the active workspace.</summary>
		internal static ViewportCameraBinder Main { get; private set; }

		/// <summary>Binder for the Preview slot, or null when Animator is not the active workspace.</summary>
		internal static ViewportCameraBinder Preview { get; private set; }

		/// <summary>Binds camera to slot. Replaces any previous binding on this component.</summary>
		internal void Bind(Camera camera, RectTransform viewportSlot, bool isMain)
		{
			target = camera;
			slot = viewportSlot;
			if (isMain)
			{
				Main = this;
			}
			else
			{
				Preview = this;
			}

			if (target != null)
			{
				target.targetTexture = null;
				target.enabled = true;
				target.gameObject.SetActive(true);
			}

			Apply();
		}

		/// <summary>True when the screen point falls inside this slot.</summary>
		internal bool ContainsScreenPoint(Vector2 screen)
		{
			return slot != null && RectTransformUtility.RectangleContainsScreenPoint(slot, screen, null);
		}

		/// <summary>Slot height in screen pixels (canvas scale applied), or 0 if unbound.</summary>
		internal float PixelHeight
		{
			get
			{
				if (target != null && target.pixelRect.height > 1f)
				{
					return target.pixelRect.height;
				}

				if (slot == null)
				{
					return 0f;
				}

				Canvas canvas = slot.GetComponentInParent<Canvas>();
				float scale = canvas != null ? canvas.scaleFactor : 1f;
				return Mathf.Max(1f, slot.rect.height * scale);
			}
		}

		/// <summary>Converts a screen-space mouse position to world on the bound camera.</summary>
		internal Vector3 ScreenToWorld(Vector3 screen)
		{
			if (target == null)
			{
				return screen;
			}

			screen.z = -target.transform.position.z;
			return target.ScreenToWorldPoint(screen);
		}

		private void LateUpdate()
		{
			Apply();
		}

		private void OnDestroy()
		{
			if (Main == this)
			{
				Main = null;
			}

			if (Preview == this)
			{
				Preview = null;
			}

			if (target != null)
			{
				target.enabled = false;
				target.targetTexture = null;
			}
		}

		private void Apply()
		{
			if (target == null || slot == null)
			{
				return;
			}

			Vector3[] corners = new Vector3[4];
			slot.GetWorldCorners(corners);
			float xMin = corners[0].x / Screen.width;
			float yMin = corners[0].y / Screen.height;
			float width = (corners[2].x - corners[0].x) / Screen.width;
			float height = (corners[2].y - corners[0].y) / Screen.height;
			if (width <= 0f || height <= 0f)
			{
				return;
			}

			target.targetTexture = null;
			target.rect = new Rect(xMin, yMin, width, height);
		}
	}
}
