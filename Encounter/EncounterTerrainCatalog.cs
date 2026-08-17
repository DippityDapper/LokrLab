using System;
using System.Collections.Generic;
using System.IO;
using Ironhide.AssetBundles;
using Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LokrLab.Encounter
{
	/// <summary>Scans template prefabs for hex-art terrains and resolves a HexaTile to stamp.</summary>
	/// <remarks>
	/// Reads Tilemaps on the prefab asset from the already-loaded <c>templates</c>
	/// bundle. Does not call <c>LoadAssetBundle</c> when the bundle is present —
	/// a second load returns null and vanilla overwrites the cache, so
	/// <c>LevelRoom</c> cannot create the encounter. Does not start a fight embed.
	/// </remarks>
	internal static class EncounterTerrainCatalog
	{
		private static readonly Dictionary<string, List<EncounterTerrainModel>> ScanCache =
			new Dictionary<string, List<EncounterTerrainModel>>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, HexaTile> TileCache =
			new Dictionary<string, HexaTile>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Merges hex-art terrains from the host template. Does not dirty Save.</summary>
		internal static void EnsureHostTerrains(EncounterFileModel file)
		{
			if (file == null)
			{
				return;
			}

			string host = EncounterTemplateRules.Canonical(file.Template);
			EncounterTerrainRules.DropStaleHost(file, host);
			List<EncounterTerrainModel> scanned = ScanTemplate(host);
			for (int i = 0; i < scanned.Count; i++)
			{
				EncounterTerrainModel row = scanned[i];
				if (row == null)
				{
					continue;
				}

				row.Source = EncounterFileModel.TerrainSourceTemplate;
				row.Template = host;
				EncounterTerrainRules.Add(file, row);
			}
		}

		/// <summary>Hex-art terrains on that prefab. Cached per template name.</summary>
		internal static List<EncounterTerrainModel> ScanTemplate(string template)
		{
			string name = EncounterTemplateRules.Canonical(template);
			List<EncounterTerrainModel> cached;
			if (!string.IsNullOrEmpty(name) && ScanCache.TryGetValue(name, out cached) && cached != null)
			{
				return Clone(cached);
			}

			List<EncounterTerrainModel> rows = new List<EncounterTerrainModel>();
			GameObject prefab = LoadPrefab(name);
			if (prefab == null)
			{
				return rows;
			}

			ScanPrefab(prefab, name, rows);
			if (!string.IsNullOrEmpty(name))
			{
				ScanCache[name] = Clone(rows);
			}

			return rows;
		}

		/// <summary>HexaTile for this catalog row, or null when the stage has no hex art.</summary>
		internal static HexaTile ResolveTile(EncounterFileModel file, int terrainId, string sourceTemplate)
		{
			string template = !string.IsNullOrEmpty(sourceTemplate)
				? EncounterTemplateRules.Canonical(sourceTemplate)
				: EncounterTemplateRules.Canonical(file != null ? file.Template : null);
			if (string.IsNullOrEmpty(template) || terrainId == 0)
			{
				return null;
			}

			string key = template + ":" + terrainId;
			HexaTile cached;
			if (TileCache.TryGetValue(key, out cached) && cached != null)
			{
				return cached;
			}

			HexaTile live = FindOnLiveMap(template, file, terrainId);
			if (live != null)
			{
				TileCache[key] = live;
				return live;
			}

			GameObject prefab = LoadPrefab(template);
			if (prefab == null)
			{
				return null;
			}

			HexaTile match;
			HexaTile proto;
			FindTiles(prefab, terrainId, out match, out proto);
			if (match != null)
			{
				TileCache[key] = match;
				return match;
			}

			if (proto == null || proto.terrainData == null || !HasHexSprites(proto.terrainData, terrainId))
			{
				return null;
			}

			HexaTile painted = ScriptableObject.CreateInstance<HexaTile>();
			painted.terrainData = proto.terrainData;
			painted.terrainId = terrainId;
			TileCache[key] = painted;
			return painted;
		}

		/// <summary>True when this TerrainData has neighbor-masked hex sprites for the id.</summary>
		internal static bool HasHexSprites(TerrainData data, int terrainId)
		{
			if (data == null || data.tileInfos == null)
			{
				return false;
			}

			for (int i = 0; i < data.tileInfos.Count; i++)
			{
				TerrainData.TileInfo info = data.tileInfos[i];
				if (info != null && info.sprite != null && info.terrain == terrainId)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>templates-bundle prefab names that look like combat rooms, plus empty-enough hosts.</summary>
		internal static List<string> ListStages()
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<string> names = new List<string>();
			for (int i = 0; i < EncounterTemplateRules.EmptyEnough.Length; i++)
			{
				AddStage(names, seen, EncounterTemplateRules.EmptyEnough[i]);
			}

			AssetBundle bundle = EnsureTemplatesBundle();
			if (bundle == null)
			{
				return names;
			}

			string[] assets = bundle.GetAllAssetNames();
			if (assets == null)
			{
				return names;
			}

			for (int i = 0; i < assets.Length; i++)
			{
				string stage = StageName(assets[i]);
				if (LooksLikeStage(stage))
				{
					AddStage(names, seen, stage);
				}
			}

			names.Sort(StringComparer.OrdinalIgnoreCase);
			return names;
		}

		/// <summary>Drops prefab scans so a later import re-reads the bundle.</summary>
		internal static void Clear()
		{
			ScanCache.Clear();
			TileCache.Clear();
		}

		private static void ScanPrefab(GameObject root, string template, List<EncounterTerrainModel> rows)
		{
			if (root == null || rows == null)
			{
				return;
			}

			HashSet<int> seen = new HashSet<int>();
			HashSet<TerrainData> datas = new HashSet<TerrainData>();
			Tilemap[] maps = root.GetComponentsInChildren<Tilemap>(true);
			if (maps == null)
			{
				return;
			}

			for (int i = 0; i < maps.Length; i++)
			{
				CollectTerrainData(maps[i], datas);
			}

			foreach (TerrainData data in datas)
			{
				if (data == null || data.terrains == null)
				{
					continue;
				}

				for (int i = 0; i < data.terrains.Count; i++)
				{
					TerrainData.Terrain terrain = data.terrains[i];
					if (terrain == null || seen.Contains(terrain.id) || !HasHexSprites(data, terrain.id))
					{
						continue;
					}

					seen.Add(terrain.id);
					rows.Add(new EncounterTerrainModel
					{
						TerrainId = terrain.id,
						Name = string.IsNullOrEmpty(terrain.name) ? "Terrain " + terrain.id : terrain.name,
						Source = EncounterFileModel.TerrainSourceTemplate,
						Template = template ?? string.Empty
					});
				}
			}
		}

		private static void CollectTerrainData(Tilemap tilemap, HashSet<TerrainData> datas)
		{
			if (tilemap == null)
			{
				return;
			}

			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				HexaTile tile = tilemap.GetTile(pos) as HexaTile;
				if (tile != null && tile.terrainData != null)
				{
					datas.Add(tile.terrainData);
				}
			}
		}

		private static void FindTiles(GameObject root, int terrainId, out HexaTile match, out HexaTile proto)
		{
			match = null;
			proto = null;
			if (root == null)
			{
				return;
			}

			Tilemap[] maps = root.GetComponentsInChildren<Tilemap>(true);
			if (maps == null)
			{
				return;
			}

			for (int i = 0; i < maps.Length; i++)
			{
				Tilemap tilemap = maps[i];
				if (tilemap == null)
				{
					continue;
				}

				BoundsInt bounds = tilemap.cellBounds;
				foreach (Vector3Int pos in bounds.allPositionsWithin)
				{
					HexaTile tile = tilemap.GetTile(pos) as HexaTile;
					if (tile == null || tile.terrainData == null)
					{
						continue;
					}

					if (proto == null)
					{
						proto = tile;
					}

					if (tile.terrainId == terrainId)
					{
						match = tile;
						return;
					}
				}
			}
		}

		private static HexaTile FindOnLiveMap(string template, EncounterFileModel file, int terrainId)
		{
			string host = EncounterTemplateRules.Canonical(file != null ? file.Template : null);
			if (!string.Equals(template, host, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			Tilemap tilemap = UnityEngine.Object.FindObjectOfType<Tilemap>();
			if (tilemap == null)
			{
				return null;
			}

			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				HexaTile tile = tilemap.GetTile(pos) as HexaTile;
				if (tile != null && tile.terrainId == terrainId && tile.terrainData != null)
				{
					return tile;
				}
			}

			return null;
		}

		/// <summary>Loads a templates-bundle prefab by name, read-only. Never instantiates it.</summary>
		/// <remarks>Internal (not private) so VanillaEncounterImporter can reuse the same EnsureTemplatesBundle guard rather than loading the templates bundle a second, independent way -- see that guard's own remarks for why a naive second load is dangerous.</remarks>
		internal static GameObject LoadPrefab(string template)
		{
			AssetBundle bundle = EnsureTemplatesBundle();
			if (string.IsNullOrEmpty(template) || bundle == null)
			{
				return null;
			}

			return bundle.LoadAsset<GameObject>(template.ToLowerInvariant());
		}

		/// <summary>The boot-loaded templates bundle, or a first-time load if it is missing.</summary>
		/// <remarks>
		/// <c>AssetBundleManager.LoadAssetBundle</c> always hits disk and stores the
		/// result, even when Unity refuses a duplicate load. Reuse <c>GetBundle</c>
		/// (and Unity's already-loaded list) so we never write null over the cache.
		/// </remarks>
		private static AssetBundle EnsureTemplatesBundle()
		{
			AssetBundle bundle = AssetBundleManager.GetBundle("templates");
			if (bundle != null)
			{
				return bundle;
			}

			foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (loaded != null && string.Equals(loaded.name, "templates", StringComparison.OrdinalIgnoreCase))
				{
					return loaded;
				}
			}

			try
			{
				return AssetBundleManager.LoadAssetBundle("templates");
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string StageName(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
			{
				return string.Empty;
			}

			return Path.GetFileNameWithoutExtension(assetPath.Replace('\\', '/'));
		}

		private static bool LooksLikeStage(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			if (EncounterTemplateRules.IsEmptyEnough(name))
			{
				return true;
			}

			string lower = name.ToLowerInvariant();
			return lower.StartsWith("combat_", StringComparison.Ordinal)
				|| lower.StartsWith("fight", StringComparison.Ordinal)
				|| lower.StartsWith("encounter", StringComparison.Ordinal);
		}

		private static void AddStage(List<string> names, HashSet<string> seen, string name)
		{
			string canonical = EncounterTemplateRules.Canonical(name);
			if (string.IsNullOrEmpty(canonical) || !seen.Add(canonical))
			{
				return;
			}

			names.Add(canonical);
		}

		private static List<EncounterTerrainModel> Clone(List<EncounterTerrainModel> source)
		{
			List<EncounterTerrainModel> copy = new List<EncounterTerrainModel>();
			if (source == null)
			{
				return copy;
			}

			for (int i = 0; i < source.Count; i++)
			{
				EncounterTerrainModel row = source[i];
				if (row == null)
				{
					continue;
				}

				copy.Add(new EncounterTerrainModel
				{
					TerrainId = row.TerrainId,
					Name = row.Name,
					Source = row.Source,
					Template = row.Template
				});
			}

			return copy;
		}
	}
}
