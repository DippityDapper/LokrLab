using System;
using System.Collections.Generic;
using Ironhide.AssetBundles;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLoader;
using LokrCharacterLoader.CustomRigs;
using LokrLabApi;
using SpaceRush;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Shared exo / prop helpers for the catalogue preview and card baker.</summary>
	/// <remarks>
	/// Prop thumbs read TexturePacker cells from the <c>spritesheets</c> bundle
	/// (<c>Arena-hd</c>, <c>Forest-hd</c>, …). Instantiating a deco runs
	/// <c>NamedSpriteComponent.Awake</c> → <c>SpriteCache.GetSprite</c> and throws
	/// when Lab has not loaded that sheet.
	/// </remarks>
	internal static class EncounterCatalogueExo
	{
		/// <summary>Default ortho size matching Character Lab's preview camera.</summary>
		internal const float ExoOrtho = 1.35f;

		private static readonly string[] StandNames = { "Stand", "stand", "StandStatic", "Idle", "Rest" };

		private static Dictionary<string, tk2dSpriteDefinition> scenarioSpriteIndex;
		private static readonly Dictionary<string, Sprite> spritesheetSprites =
			new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> loadedSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Builds a Character project's custom rig, or falls back to its unit-bundle exo.</summary>
		internal static ExoSkeletonDataAsset LoadCharacter(ProjectReference reference)
		{
			if (reference == null)
			{
				return null;
			}

			string folder = reference.FolderPath;
			if (!string.IsNullOrEmpty(folder)
				&& System.IO.File.Exists(System.IO.Path.Combine(folder, "rig", "rig.json")))
			{
				try
				{
					return CustomRigLoader.BuildFromFolder(reference.ProjectId, folder);
				}
				catch (Exception ex)
				{
					LokrLabPlugin.Log.LogWarning("Catalogue exo from folder failed: " + ex.Message);
				}
			}

			return LoadUnit(reference.ProjectId);
		}

		/// <summary>Loads a hero metaExo asset, or the exo attached to an enemy kind prefab.</summary>
		/// <remarks>
		/// Heroes store <c>metaExo</c> as a top-level <c>units</c> asset. Enemies usually
		/// leave that empty and keep the exo on <c>UnitViewManager.FindPrefab(kind)</c>.
		/// </remarks>
		internal static ExoSkeletonDataAsset LoadUnit(string unitId)
		{
			if (string.IsNullOrEmpty(unitId))
			{
				return null;
			}

			ResolveUnitVisuals(unitId, out string metaExo, out string kind);
			ExoSkeletonDataAsset asset = LoadUnitsAsset(metaExo);
			if (asset != null)
			{
				return asset;
			}

			if (!string.IsNullOrEmpty(kind))
			{
				asset = LoadExoFromKindPrefab(kind);
				if (asset != null)
				{
					return asset;
				}
			}

			if (string.IsNullOrEmpty(metaExo)
				&& !string.Equals(kind, unitId, StringComparison.OrdinalIgnoreCase))
			{
				asset = LoadUnitsAsset(unitId);
				if (asset != null)
				{
					return asset;
				}
			}

			return string.Equals(kind, unitId, StringComparison.OrdinalIgnoreCase)
				? null
				: LoadExoFromKindPrefab(unitId);
		}

		/// <summary>Drops spritesheet-index sprites when Lab closes. Does not destroy atlas textures.</summary>
		internal static void Dispose()
		{
			foreach (KeyValuePair<string, Sprite> entry in spritesheetSprites)
			{
				if (entry.Value != null)
				{
					UnityEngine.Object.Destroy(entry.Value);
				}
			}

			spritesheetSprites.Clear();
			loadedSheets.Clear();
			scenarioSpriteIndex = null;
		}

		/// <summary>Instantiates an exo under parent with Sprites/Default. Does not play a clip.</summary>
		internal static GameObject SpawnExo(Transform parent, ExoSkeletonDataAsset asset)
		{
			if (asset == null || parent == null)
			{
				return null;
			}

			GameObject go = new GameObject("CatalogueExo",
				typeof(ExoSkeletonData), typeof(MeshFilter), typeof(MeshRenderer),
				typeof(ExoSkeletonRenderer), typeof(ExoSkeletonAnimator));
			go.transform.SetParent(parent, false);
			MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				meshRenderer.material = new Material(shader);
			}

			ExoSkeletonData data = go.GetComponent<ExoSkeletonData>();
			data.UpdateAsset(asset);
			ExoSkeletonRenderer renderer = go.GetComponent<ExoSkeletonRenderer>();
			renderer.exoSkeletonData = data;
			ExoSkeletonAnimator animator = go.GetComponent<ExoSkeletonAnimator>();
			animator.data = data;
			animator.autoPlay = false;
			animator.enabled = true;
			CharacterLabLayers.ApplyToHierarchy(go);
			return go;
		}

		/// <summary>Writes the first Stand (or first clip) frame so a still thumbnail does not wait on the animator.</summary>
		internal static void ApplyStandPose(GameObject exo, ExoSkeletonDataAsset asset)
		{
			if (exo == null || asset == null || asset.animations == null || asset.animations.Length == 0)
			{
				return;
			}

			int index = FindStandIndex(asset);
			Ironhide.ExoSkeleton.Animation animation = asset.animations[index];
			if (animation == null || animation.frames == null || animation.frames.Length == 0)
			{
				return;
			}

			ExoSkeletonData data = exo.GetComponent<ExoSkeletonData>();
			if (data == null)
			{
				return;
			}

			AnimationFrame frame = animation.frames[0];
			data.SetPose(frame.renderOrder, frame.matrices, frame.attachPoints, frame.alphas);
		}

		/// <summary>Loops Stand (or the first clip) on a live preview exo.</summary>
		internal static void PlayStand(ExoSkeletonAnimator animator, ExoSkeletonDataAsset asset)
		{
			if (animator == null || asset == null || asset.animations == null || asset.animations.Length == 0)
			{
				return;
			}

			animator.Play(FindStandIndex(asset), true, 0f);
		}

		/// <summary>Index of Stand / stand / StandStatic / Idle / Rest, else 0.</summary>
		internal static int FindStandIndex(ExoSkeletonDataAsset asset)
		{
			if (asset == null || asset.animations == null)
			{
				return 0;
			}

			for (int n = 0; n < StandNames.Length; n++)
			{
				int found = FindAnimation(asset, StandNames[n]);
				if (found >= 0)
				{
					return found;
				}
			}

			return 0;
		}

		/// <summary>Rebuilds the exo mesh now so a same-frame camera capture is not empty.</summary>
		/// <remarks>
		/// <c>ExoSkeletonRenderer</c> only fills the mesh in <c>LateUpdate</c>. The baker's
		/// pump is also a LateUpdate, so capture can run first and bake the clear color.
		/// </remarks>
		internal static void ForceExoMesh(GameObject exo)
		{
			if (exo == null)
			{
				return;
			}

			ExoSkeletonRenderer renderer = exo.GetComponent<ExoSkeletonRenderer>();
			if (renderer == null)
			{
				return;
			}

			try
			{
				renderer.LateUpdate();
			}
			catch (Exception)
			{
			}
		}

		/// <summary>Unity sprite from the deco prefab's tk2d art, without Instantiate.</summary>
		internal static Sprite ExtractPropSprite(string prefabName)
		{
			GameObject prefab = EncounterPropCatalog.Load(prefabName);
			if (prefab == null)
			{
				return null;
			}

			SpriteRenderer unitySprite = prefab.GetComponentInChildren<SpriteRenderer>(true);
			if (unitySprite != null && unitySprite.sprite != null)
			{
				return unitySprite.sprite;
			}

			tk2dSprite[] sprites = prefab.GetComponentsInChildren<tk2dSprite>(true);
			if (sprites != null)
			{
				for (int i = 0; i < sprites.Length; i++)
				{
					Sprite fromTk = FromTk2dSprite(sprites[i]);
					if (fromTk != null)
					{
						return fromTk;
					}
				}
			}

			NamedSpriteComponent[] named = prefab.GetComponentsInChildren<NamedSpriteComponent>(true);
			if (named != null)
			{
				for (int i = 0; i < named.Length; i++)
				{
					Sprite fromNamed = FromNamedSprite(named[i], sprites);
					if (fromNamed != null)
					{
						return fromNamed;
					}
				}
			}

			Sprite fromPrefabName = FromSpritesheetName(prefabName);
			return fromPrefabName != null ? fromPrefabName : FromScenarioSpriteName(prefabName);
		}

		/// <summary>Billboard showing extracted deco art. Does not instantiate the prefab.</summary>
		internal static GameObject SpawnPropSprite(Transform parent, Sprite sprite)
		{
			if (parent == null || sprite == null)
			{
				return null;
			}

			GameObject go = new GameObject("CataloguePropSprite", typeof(SpriteRenderer));
			go.transform.SetParent(parent, false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				renderer.sharedMaterial = new Material(shader);
			}

			CharacterLabLayers.ApplyToHierarchy(go);
			return go;
		}

		/// <summary>Places the bake/preview camera for a typical exo (feet at origin).</summary>
		internal static void FrameExo(Camera camera)
		{
			if (camera == null)
			{
				return;
			}

			camera.orthographicSize = ExoOrtho;
			camera.transform.localPosition = new Vector3(0f, 0.35f, -10f);
		}

		/// <summary>Fits the camera to the prefab's renderer bounds.</summary>
		internal static void FrameProp(Camera camera, Transform worldRoot, GameObject prop)
		{
			if (camera == null || prop == null)
			{
				return;
			}

			Renderer[] renderers = prop.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
			{
				FrameExo(camera);
				return;
			}

			Bounds bounds = renderers[0].bounds;
			bool any = renderers[0].enabled && renderers[0].bounds.size.sqrMagnitude > 0.0001f;
			for (int i = 1; i < renderers.Length; i++)
			{
				if (renderers[i] == null || !renderers[i].enabled)
				{
					continue;
				}

				if (!any)
				{
					bounds = renderers[i].bounds;
					any = true;
					continue;
				}

				bounds.Encapsulate(renderers[i].bounds);
			}

			if (!any)
			{
				FrameExo(camera);
				return;
			}

			float size = Mathf.Max(bounds.extents.x, bounds.extents.y, 0.25f) * 1.2f;
			camera.orthographicSize = size;
			Vector3 center = worldRoot != null
				? worldRoot.InverseTransformPoint(bounds.center)
				: bounds.center;
			camera.transform.localPosition = new Vector3(center.x, center.y, -10f);
		}

		private static int FindAnimation(ExoSkeletonDataAsset asset, string name)
		{
			for (int i = 0; i < asset.animations.Length; i++)
			{
				if (asset.animations[i] != null
					&& string.Equals(asset.animations[i].name, name, StringComparison.Ordinal))
				{
					return i;
				}
			}

			return -1;
		}

		private static Sprite FromTk2dSprite(tk2dSprite sprite)
		{
			if (sprite == null || sprite.Collection == null || sprite.Collection.spriteDefinitions == null)
			{
				return null;
			}

			int id = sprite.spriteId;
			if (id < 0 || id >= sprite.Collection.spriteDefinitions.Length)
			{
				return null;
			}

			return FromDefinition(sprite.Collection.spriteDefinitions[id]);
		}

		private static Sprite FromNamedSprite(NamedSpriteComponent named, tk2dSprite[] sprites)
		{
			if (named == null || string.IsNullOrEmpty(named.spriteName))
			{
				return null;
			}

			if (SpriteCache.Instance != null
				&& SpriteCache.Instance.TryGetSprite(named.spriteName, out SpriteHandle handle))
			{
				Sprite fromCache = FromDefinition(handle.GetSpriteDefinition());
				if (fromCache != null)
				{
					return fromCache;
				}
			}

			if (sprites != null)
			{
				for (int i = 0; i < sprites.Length; i++)
				{
					tk2dSprite sprite = sprites[i];
					if (sprite == null || sprite.Collection == null || sprite.Collection.spriteDefinitions == null)
					{
						continue;
					}

					tk2dSpriteDefinition match = FindDefinition(sprite.Collection.spriteDefinitions, named.spriteName);
					Sprite fromCollection = FromDefinition(match);
					if (fromCollection != null)
					{
						return fromCollection;
					}
				}
			}

			Sprite fromSheet = FromSpritesheetName(named.spriteName);
			return fromSheet != null ? fromSheet : FromScenarioSpriteName(named.spriteName);
		}

		private static Sprite FromScenarioSpriteName(string spriteName)
		{
			if (string.IsNullOrEmpty(spriteName))
			{
				return null;
			}

			EnsureScenarioSpriteIndex();
			if (scenarioSpriteIndex != null && scenarioSpriteIndex.TryGetValue(spriteName, out tk2dSpriteDefinition definition))
			{
				return FromDefinition(definition);
			}

			if (scenarioSpriteIndex == null)
			{
				return null;
			}

			foreach (KeyValuePair<string, tk2dSpriteDefinition> entry in scenarioSpriteIndex)
			{
				if (string.Equals(entry.Key, spriteName, StringComparison.OrdinalIgnoreCase))
				{
					return FromDefinition(entry.Value);
				}
			}

			return null;
		}

		private static void EnsureScenarioSpriteIndex()
		{
			if (scenarioSpriteIndex != null)
			{
				return;
			}

			AssetBundle bundle = EncounterPropCatalog.GetBundle();
			if (bundle == null)
			{
				return;
			}

			scenarioSpriteIndex = new Dictionary<string, tk2dSpriteDefinition>(StringComparer.Ordinal);
			try
			{
				tk2dSpriteCollectionData[] collections = bundle.LoadAllAssets<tk2dSpriteCollectionData>();
				if (collections == null)
				{
					return;
				}

				for (int i = 0; i < collections.Length; i++)
				{
					tk2dSpriteCollectionData collection = collections[i];
					if (collection == null || collection.spriteDefinitions == null)
					{
						continue;
					}

					for (int d = 0; d < collection.spriteDefinitions.Length; d++)
					{
						tk2dSpriteDefinition definition = collection.spriteDefinitions[d];
						if (definition == null || string.IsNullOrEmpty(definition.name)
							|| scenarioSpriteIndex.ContainsKey(definition.name))
						{
							continue;
						}

						scenarioSpriteIndex[definition.name] = definition;
					}
				}
			}
			catch (Exception)
			{
			}
		}

		private static tk2dSpriteDefinition FindDefinition(tk2dSpriteDefinition[] definitions, string name)
		{
			if (definitions == null || string.IsNullOrEmpty(name))
			{
				return null;
			}

			for (int i = 0; i < definitions.Length; i++)
			{
				if (definitions[i] != null
					&& string.Equals(definitions[i].name, name, StringComparison.Ordinal))
				{
					return definitions[i];
				}
			}

			return null;
		}

		private static Sprite FromDefinition(tk2dSpriteDefinition definition)
		{
			if (definition == null || definition.uvs == null || definition.uvs.Length == 0)
			{
				return null;
			}

			Texture texture = null;
			if (definition.materialInst != null)
			{
				texture = definition.materialInst.mainTexture;
			}

			if (texture == null && definition.material != null)
			{
				texture = definition.material.mainTexture;
			}

			Texture2D texture2d = texture as Texture2D;
			if (texture2d == null)
			{
				return null;
			}

			float uMin = definition.uvs[0].x;
			float uMax = definition.uvs[0].x;
			float vMin = definition.uvs[0].y;
			float vMax = definition.uvs[0].y;
			for (int i = 1; i < definition.uvs.Length; i++)
			{
				Vector2 uv = definition.uvs[i];
				if (uv.x < uMin)
				{
					uMin = uv.x;
				}

				if (uv.x > uMax)
				{
					uMax = uv.x;
				}

				if (uv.y < vMin)
				{
					vMin = uv.y;
				}

				if (uv.y > vMax)
				{
					vMax = uv.y;
				}
			}

			int x = Mathf.Clamp(Mathf.RoundToInt(uMin * texture2d.width), 0, texture2d.width - 1);
			int y = Mathf.Clamp(Mathf.RoundToInt(vMin * texture2d.height), 0, texture2d.height - 1);
			int width = Mathf.Clamp(Mathf.RoundToInt((uMax - uMin) * texture2d.width), 1, texture2d.width - x);
			int height = Mathf.Clamp(Mathf.RoundToInt((vMax - vMin) * texture2d.height), 1, texture2d.height - y);
			try
			{
				return Sprite.Create(texture2d, new Rect(x, y, width, height), new Vector2(0.5f, 0.5f), 100f);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void ResolveUnitVisuals(string unitId, out string metaExo, out string kind)
		{
			metaExo = null;
			kind = null;
			if (CharacterAPI.KnownUnitDefinitions.TryGetValue(unitId, out UnitDefinition known)
				&& known != null)
			{
				metaExo = known.metaExo;
				kind = known.kind;
			}

			if ((!string.IsNullOrEmpty(metaExo) && !string.IsNullOrEmpty(kind))
				|| UnityDefinitionsParser.instance == null)
			{
				return;
			}

			try
			{
				UnitDefinition definition = UnityDefinitionsParser.instance.GetDefinition(unitId);
				if (definition == null
					|| (!string.Equals(definition.id, unitId, StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(definition.uniqueId, unitId, StringComparison.OrdinalIgnoreCase)))
				{
					return;
				}

				if (string.IsNullOrEmpty(metaExo))
				{
					metaExo = definition.metaExo;
				}

				if (string.IsNullOrEmpty(kind))
				{
					kind = definition.kind;
				}
			}
			catch (Exception)
			{
			}
		}

		private static ExoSkeletonDataAsset LoadExoFromKindPrefab(string kind)
		{
			if (string.IsNullOrEmpty(kind) || !EnsureUnitsBundle())
			{
				return null;
			}

			try
			{
				AssetBundle bundle = UnitsBundle();
				if (bundle == null)
				{
					return null;
				}

				GameObject prefab = bundle.LoadAsset<GameObject>(kind.ToLowerInvariant());
				if (prefab == null)
				{
					return null;
				}

				ExoSkeletonData data = prefab.GetComponentInChildren<ExoSkeletonData>(true);
				return data != null ? data.asset : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Sprite FromSpritesheetName(string spriteName)
		{
			if (string.IsNullOrEmpty(spriteName))
			{
				return null;
			}

			if (spritesheetSprites.TryGetValue(spriteName, out Sprite cached))
			{
				return cached;
			}

			string prefix = EncounterCatalogueRules.SpritesheetPrefix(spriteName);
			if (string.IsNullOrEmpty(prefix))
			{
				return null;
			}

			TryLoadSpritesheet(prefix + "-hd");
			if (spritesheetSprites.TryGetValue(spriteName, out cached))
			{
				return cached;
			}

			TryLoadSpritesheet(prefix + "-sd");
			spritesheetSprites.TryGetValue(spriteName, out cached);
			return cached;
		}

		private static void TryLoadSpritesheet(string sheetName)
		{
			if (string.IsNullOrEmpty(sheetName) || !loadedSheets.Add(sheetName))
			{
				return;
			}

			AssetBundle bundle = EnsureSpritesheetsBundle();
			if (bundle == null)
			{
				return;
			}

			string key = sheetName.ToLowerInvariant();
			Texture2D texture = bundle.LoadAsset<Texture2D>(key);
			TextAsset packer = bundle.LoadAsset<TextAsset>(key);
			if (texture == null || packer == null || string.IsNullOrEmpty(packer.text))
			{
				return;
			}

			Dictionary<string, EncounterPackerRect> rects =
				new Dictionary<string, EncounterPackerRect>(StringComparer.OrdinalIgnoreCase);
			EncounterCatalogueRules.AddTexturePackerRects(packer.text, rects);
			foreach (KeyValuePair<string, EncounterPackerRect> entry in rects)
			{
				if (spritesheetSprites.ContainsKey(entry.Key))
				{
					continue;
				}

				Sprite sprite = SpriteFromPackerRect(texture, entry.Value);
				if (sprite != null)
				{
					sprite.name = entry.Key;
					spritesheetSprites[entry.Key] = sprite;
				}
			}
		}

		private static Sprite SpriteFromPackerRect(Texture2D texture, EncounterPackerRect rect)
		{
			if (texture == null || rect.Width < 1 || rect.Height < 1)
			{
				return null;
			}

			int x = Mathf.Clamp(rect.X, 0, texture.width - 1);
			int yTop = Mathf.Clamp(rect.Y, 0, texture.height - 1);
			int width = Mathf.Clamp(rect.Width, 1, texture.width - x);
			int height = Mathf.Clamp(rect.Height, 1, texture.height - yTop);
			int y = texture.height - yTop - height;
			if (y < 0)
			{
				y = 0;
			}

			try
			{
				return Sprite.Create(texture, new Rect(x, y, width, height), new Vector2(0.5f, 0.5f), 100f);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static AssetBundle EnsureSpritesheetsBundle()
		{
			AssetBundle bundle = AssetBundleManager.GetBundle("spritesheets");
			if (bundle != null)
			{
				return bundle;
			}

			foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (loaded != null && string.Equals(loaded.name, "spritesheets", StringComparison.OrdinalIgnoreCase))
				{
					return loaded;
				}
			}

			try
			{
				return AssetBundleManager.LoadAssetBundle("spritesheets");
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static ExoSkeletonDataAsset LoadUnitsAsset(string name)
		{
			if (string.IsNullOrEmpty(name) || !EnsureUnitsBundle())
			{
				return null;
			}

			try
			{
				AssetBundle bundle = UnitsBundle();
				return bundle != null
					? bundle.LoadAsset<ExoSkeletonDataAsset>(name.ToLowerInvariant())
					: null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static AssetBundle UnitsBundle()
		{
			AssetBundle bundle = AssetBundleManager.GetBundle("units");
			if (bundle != null)
			{
				return bundle;
			}

			foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (loaded != null && string.Equals(loaded.name, "units", StringComparison.OrdinalIgnoreCase))
				{
					return loaded;
				}
			}

			return null;
		}

		private static bool EnsureUnitsBundle()
		{
			AssetBundle bundle = AssetBundleManager.GetBundle("units");
			if (bundle != null)
			{
				return true;
			}

			foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (loaded != null && string.Equals(loaded.name, "units", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			try
			{
				return AssetBundleManager.LoadAssetBundle("units") != null;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
