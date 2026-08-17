using Ironhide.ExoSkeleton;
using LokrLabApi;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab.Encounter
{
	/// <summary>Stand-pose exo (or extracted deco sprite) preview for the selected Encounter catalogue card.</summary>
	/// <remarks>
	/// One off-board instance on layer 31, drawn into a RenderTexture so Overlay inspector chrome
	/// does not cover it. Does not spawn a live exo per card. Prop preview is a billboard of the
	/// prefab's tk2d art — instantiating the deco hits SpriteCache before sheets are loaded.
	/// Hide disables the camera; Dispose destroys the world root when Lab closes.
	/// </remarks>
	internal static class EncounterCataloguePreview
	{
		private const float PreviewX = 250f;

		private static Transform hole;
		private static RawImage rawImage;
		private static RenderTexture renderTexture;
		private static Camera camera;
		private static Transform worldRoot;
		private static GameObject instance;
		private static string shownKey;

		/// <summary>Builds the RawImage hole and world camera once under the given inspector slot.</summary>
		internal static void Attach(Transform previewHole)
		{
			if (previewHole == null)
			{
				return;
			}

			if (hole == previewHole && rawImage != null && camera != null)
			{
				return;
			}

			DisposeUi();
			hole = previewHole;
			GameObject imageObject = new GameObject("CataloguePreviewImage", typeof(RectTransform), typeof(RawImage));
			imageObject.transform.SetParent(previewHole, false);
			RectTransform rect = imageObject.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			rawImage = imageObject.GetComponent<RawImage>();
			rawImage.color = Color.white;
			rawImage.raycastTarget = false;
			EnsureWorld();
			rawImage.texture = renderTexture;
		}

		/// <summary>Shows the Character project's custom rig, or its unit-bundle exo, in Stand.</summary>
		internal static void ShowCharacter(ProjectReference reference)
		{
			string id = reference != null ? reference.ProjectId : null;
			string key = "character:" + (id ?? string.Empty);
			if (key == shownKey && instance != null)
			{
				SetVisible(true);
				return;
			}

			ClearInstance();
			shownKey = key;
			ExoSkeletonDataAsset asset = EncounterCatalogueExo.LoadCharacter(reference);
			SpawnLiveExo(asset);
			SetVisible(true);
		}

		/// <summary>Shows the unit's metaExo (or the unit id) from the units bundle in Stand.</summary>
		internal static void ShowUnit(string unitId)
		{
			string key = "unit:" + (unitId ?? string.Empty);
			if (key == shownKey && instance != null)
			{
				SetVisible(true);
				return;
			}

			ClearInstance();
			shownKey = key;
			SpawnLiveExo(EncounterCatalogueExo.LoadUnit(unitId));
			SetVisible(true);
		}

		/// <summary>Shows extracted deco art in the preview hole without instantiating the prefab.</summary>
		internal static void ShowProp(string prefabName)
		{
			string key = "prop:" + (prefabName ?? string.Empty);
			if (key == shownKey && instance != null)
			{
				SetVisible(true);
				return;
			}

			ClearInstance();
			shownKey = key;
			instance = EncounterCatalogueExo.SpawnPropSprite(worldRoot, EncounterCatalogueExo.ExtractPropSprite(prefabName));
			if (instance != null)
			{
				EncounterCatalogueExo.FrameProp(camera, worldRoot, instance);
			}

			SetVisible(true);
		}

		/// <summary>Hides the camera without destroying the last instance.</summary>
		internal static void Hide()
		{
			SetVisible(false);
		}

		/// <summary>Destroys the world preview and UI hole refs. Call when Lab closes.</summary>
		internal static void Dispose()
		{
			ClearInstance();
			shownKey = null;
			DisposeUi();
			if (camera != null)
			{
				UnityEngine.Object.Destroy(camera.gameObject);
				camera = null;
			}

			if (worldRoot != null)
			{
				UnityEngine.Object.Destroy(worldRoot.gameObject);
				worldRoot = null;
			}

			if (renderTexture != null)
			{
				renderTexture.Release();
				UnityEngine.Object.Destroy(renderTexture);
				renderTexture = null;
			}
		}

		private static void SpawnLiveExo(ExoSkeletonDataAsset asset)
		{
			instance = EncounterCatalogueExo.SpawnExo(worldRoot, asset);
			if (instance == null)
			{
				return;
			}

			EncounterCatalogueExo.FrameExo(camera);
			EncounterCatalogueExo.PlayStand(instance.GetComponent<ExoSkeletonAnimator>(), asset);
			EncounterCatalogueExo.ForceExoMesh(instance);
		}

		private static void EnsureWorld()
		{
			if (worldRoot != null && camera != null && renderTexture != null)
			{
				return;
			}

			if (renderTexture == null)
			{
				renderTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
				renderTexture.name = "EncounterCataloguePreview";
			}

			if (worldRoot == null)
			{
				GameObject rootObject = new GameObject("EncounterCataloguePreviewRoot");
				worldRoot = rootObject.transform;
				worldRoot.position = new Vector3(PreviewX, 0f, 0f);
				CharacterLabLayers.ApplyToHierarchy(rootObject);
			}

			if (camera == null)
			{
				GameObject cameraObject = new GameObject("EncounterCataloguePreviewCamera", typeof(Camera));
				camera = cameraObject.GetComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
				camera.cullingMask = CharacterLabLayers.ViewportMask;
				camera.orthographic = true;
				camera.orthographicSize = EncounterCatalogueExo.ExoOrtho;
				camera.nearClipPlane = 0.1f;
				camera.farClipPlane = 50f;
				camera.targetTexture = renderTexture;
				camera.depth = -10f;
				camera.enabled = false;
				cameraObject.transform.SetParent(worldRoot, false);
				cameraObject.transform.localPosition = new Vector3(0f, 0.35f, -10f);
				CharacterLabLayers.ApplyToHierarchy(cameraObject);
			}
		}

		private static void ClearInstance()
		{
			if (instance != null)
			{
				UnityEngine.Object.Destroy(instance);
				instance = null;
			}
		}

		private static void SetVisible(bool visible)
		{
			if (camera != null)
			{
				camera.enabled = visible;
				if (visible)
				{
					camera.targetTexture = renderTexture;
				}
			}

			if (rawImage != null)
			{
				rawImage.enabled = visible;
			}
		}

		private static void DisposeUi()
		{
			if (rawImage != null && rawImage.gameObject != null)
			{
				UnityEngine.Object.Destroy(rawImage.gameObject);
			}

			rawImage = null;
			hole = null;
		}
	}
}
