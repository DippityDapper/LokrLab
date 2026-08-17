using System;
using System.Collections.Generic;
using Ironhide.ExoSkeleton;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Bakes Stand-pose exo sprites for catalogue cards, one at a time as rows appear.</summary>
	/// <remarks>
	/// Portraits are not used: most units have none. Prop cards use
	/// <c>ExtractPropSprite</c> (no Instantiate). Exo thumbs force
	/// <c>ExoSkeletonRenderer.LateUpdate</c> before ReadPixels so the mesh exists
	/// in the same pump tick. Blank clear-color frames are not cached.
	/// </remarks>
	internal static class EncounterCatalogueThumbnails
	{
		private const float BakeX = 280f;
		private const int BakeSize = 96;

		private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
		private static readonly Queue<BakeRequest> queue = new Queue<BakeRequest>();
		private static readonly Dictionary<string, List<Action<Sprite>>> pending =
			new Dictionary<string, List<Action<Sprite>>>(StringComparer.Ordinal);

		private static Transform worldRoot;
		private static Camera camera;
		private static RenderTexture renderTexture;
		private static EncounterCatalogueBakePump pump;
		private static BakeRequest inflight;
		private static GameObject inflightObject;
		private static readonly HashSet<string> ownsTexture = new HashSet<string>(StringComparer.Ordinal);

		private enum BakeKind
		{
			Character,
			Unit
		}

		private sealed class BakeRequest
		{
			internal BakeKind Kind;
			internal string Key;
			internal string Id;
			internal ProjectReference Character;
		}

		/// <summary>Queues a Character Stand-pose thumbnail. Cache hits invoke immediately.</summary>
		internal static void RequestCharacter(ProjectReference reference, Action<Sprite> done)
		{
			string id = reference != null ? reference.ProjectId : null;
			Enqueue(BakeKind.Character, "character:" + (id ?? string.Empty), id, reference, done);
		}

		/// <summary>Queues a unit Stand-pose thumbnail. Cache hits invoke immediately.</summary>
		internal static void RequestUnit(string unitId, Action<Sprite> done)
		{
			Enqueue(BakeKind.Unit, "unit:" + (unitId ?? string.Empty), unitId, null, done);
		}

		/// <summary>Extracts deco art from the prefab asset. Cache hits invoke immediately.</summary>
		internal static void RequestProp(string prefabName, Action<Sprite> done)
		{
			if (done == null)
			{
				return;
			}

			string key = "prop:" + (prefabName ?? string.Empty);
			if (cache.TryGetValue(key, out Sprite cached))
			{
				done(cached);
				return;
			}

			Sprite sprite = EncounterCatalogueExo.ExtractPropSprite(prefabName);
			if (sprite != null)
			{
				cache[key] = sprite;
			}

			done(sprite);
		}

		/// <summary>Destroys the bake camera and cached sprites when Lab closes.</summary>
		internal static void Dispose()
		{
			queue.Clear();
			pending.Clear();
			ClearInflight();
			if (pump != null)
			{
				UnityEngine.Object.Destroy(pump);
				pump = null;
			}

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

			foreach (KeyValuePair<string, Sprite> entry in cache)
			{
				if (entry.Value == null)
				{
					continue;
				}

				if (ownsTexture.Contains(entry.Key) && entry.Value.texture != null)
				{
					UnityEngine.Object.Destroy(entry.Value.texture);
				}

				UnityEngine.Object.Destroy(entry.Value);
			}

			cache.Clear();
			ownsTexture.Clear();
		}

		/// <summary>Advances the bake queue. Called from the pump's LateUpdate.</summary>
		internal static void Tick()
		{
			if (queue.Count == 0)
			{
				return;
			}

			EnsureWorld();
			inflight = queue.Dequeue();
			string key = inflight.Key;
			inflightObject = Spawn(inflight);
			if (inflightObject == null)
			{
				inflight = null;
				Finish(key, null);
				return;
			}

			EncounterCatalogueExo.ForceExoMesh(inflightObject);
			Sprite sprite = Capture();
			bool owned = sprite != null;
			ClearInflight();
			if (owned)
			{
				ownsTexture.Add(key);
			}

			Finish(key, sprite);
		}

		private static void Enqueue(
			BakeKind kind,
			string key,
			string id,
			ProjectReference character,
			Action<Sprite> done)
		{
			if (done == null || string.IsNullOrEmpty(key))
			{
				return;
			}

			if (cache.TryGetValue(key, out Sprite cached))
			{
				done(cached);
				return;
			}

			if (!pending.TryGetValue(key, out List<Action<Sprite>> waiters))
			{
				waiters = new List<Action<Sprite>>();
				pending[key] = waiters;
				queue.Enqueue(new BakeRequest
				{
					Kind = kind,
					Key = key,
					Id = id,
					Character = character
				});
				EnsurePump();
			}

			waiters.Add(done);
		}

		private static GameObject Spawn(BakeRequest request)
		{
			if (request == null || worldRoot == null)
			{
				return null;
			}

			ExoSkeletonDataAsset asset = request.Kind == BakeKind.Character
				? EncounterCatalogueExo.LoadCharacter(request.Character)
				: EncounterCatalogueExo.LoadUnit(request.Id);
			GameObject exo = EncounterCatalogueExo.SpawnExo(worldRoot, asset);
			if (exo != null)
			{
				EncounterCatalogueExo.ApplyStandPose(exo, asset);
				EncounterCatalogueExo.FrameExo(camera);
			}

			return exo;
		}

		private static Sprite Capture()
		{
			if (camera == null || renderTexture == null)
			{
				return null;
			}

			camera.enabled = true;
			camera.targetTexture = renderTexture;
			camera.Render();
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = renderTexture;
			Texture2D texture = new Texture2D(BakeSize, BakeSize, TextureFormat.ARGB32, false);
			texture.ReadPixels(new Rect(0f, 0f, BakeSize, BakeSize), 0, 0);
			texture.Apply();
			RenderTexture.active = previous;
			camera.enabled = false;
			if (IsBlankBake(texture))
			{
				UnityEngine.Object.Destroy(texture);
				return null;
			}

			texture.name = inflight != null ? inflight.Key : "catalogue-thumb";
			return Sprite.Create(texture, new Rect(0f, 0f, BakeSize, BakeSize), new Vector2(0.5f, 0.5f), 100f);
		}

		private static bool IsBlankBake(Texture2D texture)
		{
			if (texture == null)
			{
				return true;
			}

			Color32[] pixels = texture.GetPixels32();
			if (pixels == null || pixels.Length == 0)
			{
				return true;
			}

			int similar = 0;
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 pixel = pixels[i];
				if (pixel.a < 8
					|| (Near(pixel.r, 20) && Near(pixel.g, 23) && Near(pixel.b, 31)))
				{
					similar++;
				}
			}

			return similar >= (int)(pixels.Length * 0.98f);
		}

		private static bool Near(byte value, byte target)
		{
			int delta = value - target;
			return delta <= 6 && delta >= -6;
		}

		private static void Finish(string key, Sprite sprite)
		{
			if (sprite != null)
			{
				cache[key] = sprite;
			}

			if (!pending.TryGetValue(key, out List<Action<Sprite>> waiters))
			{
				return;
			}

			pending.Remove(key);
			for (int i = 0; i < waiters.Count; i++)
			{
				waiters[i]?.Invoke(sprite);
			}
		}

		private static void ClearInflight()
		{
			if (inflightObject != null)
			{
				UnityEngine.Object.Destroy(inflightObject);
				inflightObject = null;
			}

			inflight = null;
		}

		private static void EnsurePump()
		{
			EnsureWorld();
			if (pump != null || worldRoot == null)
			{
				return;
			}

			pump = worldRoot.gameObject.AddComponent<EncounterCatalogueBakePump>();
		}

		private static void EnsureWorld()
		{
			if (worldRoot != null && camera != null && renderTexture != null)
			{
				return;
			}

			if (renderTexture == null)
			{
				renderTexture = new RenderTexture(BakeSize, BakeSize, 16, RenderTextureFormat.ARGB32);
				renderTexture.name = "EncounterCatalogueBake";
			}

			if (worldRoot == null)
			{
				GameObject rootObject = new GameObject("EncounterCatalogueBakeRoot");
				worldRoot = rootObject.transform;
				worldRoot.position = new Vector3(BakeX, 0f, 0f);
				CharacterLabLayers.ApplyToHierarchy(rootObject);
			}

			if (camera == null)
			{
				GameObject cameraObject = new GameObject("EncounterCatalogueBakeCamera", typeof(Camera));
				camera = cameraObject.GetComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
				camera.cullingMask = CharacterLabLayers.ViewportMask;
				camera.orthographic = true;
				camera.orthographicSize = EncounterCatalogueExo.ExoOrtho;
				camera.nearClipPlane = 0.1f;
				camera.farClipPlane = 50f;
				camera.targetTexture = renderTexture;
				camera.depth = -20f;
				camera.enabled = false;
				cameraObject.transform.SetParent(worldRoot, false);
				cameraObject.transform.localPosition = new Vector3(0f, 0.35f, -10f);
				CharacterLabLayers.ApplyToHierarchy(cameraObject);
			}
		}
	}

	/// <summary>Ticks the catalogue thumbnail baker once per frame, after exo renderers.</summary>
	[DefaultExecutionOrder(10000)]
	internal sealed class EncounterCatalogueBakePump : MonoBehaviour
	{
		private void LateUpdate()
		{
			EncounterCatalogueThumbnails.Tick();
		}
	}
}
