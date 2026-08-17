using UnityEngine;

namespace LokrLab
{
	/// <summary>Keeps a gameplay camera <see cref="Camera.rect"/> matched to a UI hole.</summary>
	/// <remarks>
	/// Runs after <see cref="EmbeddedSceneHudFitter"/>. Assigning Screen Space Camera on
	/// fight canvases resets <see cref="Camera.rect"/> to the full screen; this writes the hole last.
	/// A zero-size hole is skipped so a collapsed layout cannot leave a fullscreen crop.
	/// </remarks>
	[DefaultExecutionOrder(32767)]
	internal sealed class EmbeddedSceneBinder : MonoBehaviour
	{
		private Camera target;
		private RectTransform slot;

		/// <summary>Binds the gameplay camera to the hole and applies the crop immediately.</summary>
		internal void Bind(Camera camera, RectTransform viewportSlot)
		{
			target = camera;
			slot = viewportSlot;
			if (target != null)
			{
				target.targetTexture = null;
				target.enabled = true;
				target.gameObject.SetActive(true);
			}

			Apply();
		}

		/// <summary>Clears the bound camera so LateUpdate cannot write a dying Camera.rect.</summary>
		internal void Unbind()
		{
			target = null;
			slot = null;
		}

		private void LateUpdate()
		{
			Apply();
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
