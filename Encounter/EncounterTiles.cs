using System.Collections.Generic;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LokrLab.Encounter
{
	/// <summary>Applies authored floor tiles onto the template Tilemap.</summary>
	/// <remarks>
	/// Reuses a <see cref="HexaTile"/> already on the map so neighbor-masked
	/// <c>terrainData</c> stays valid. Each combat hex is two rectangular cells
	/// (left + right A/B sprites). Stamp both or the floor looks like a kitchen
	/// tile — only the left half filled. Does not grow the hex board — Unblock first.
	/// </remarks>
	internal static class EncounterTiles
	{
		private static readonly Dictionary<int, HexaTile> Prototypes = new Dictionary<int, HexaTile>();
		private static readonly Dictionary<int, TileBase> Originals = new Dictionary<int, TileBase>();
		private static bool haveOrigin;
		private static int originX;
		private static int originY;

		/// <summary>Terrain id chosen from the Node Tree.</summary>
		internal static int SelectedTerrainId { get; set; }

		/// <summary>Source-stage prefab for the selected terrain. Empty means the host template.</summary>
		internal static string SelectedSourceTemplate { get; set; } = string.Empty;

		/// <summary>Selects this catalog row for Tile paint.</summary>
		internal static void Select(EncounterTerrainModel terrain)
		{
			if (terrain == null)
			{
				SelectedTerrainId = 0;
				SelectedSourceTemplate = string.Empty;
				return;
			}

			SelectedTerrainId = terrain.TerrainId;
			SelectedSourceTemplate = terrain.Template ?? string.Empty;
		}

		/// <summary>Writes every authored tile onto the live Tilemap.</summary>
		/// <remarks>
		/// Calibrates origin from the template floor first. When
		/// <see cref="EncounterFileModel.TilesDefault"/> is false, that floor is
		/// stripped so a new encounter is a blank slate. Erase then restores empty,
		/// not the host ice.
		/// </remarks>
		internal static void ApplyAuthoredTiles(EncounterFileModel file)
		{
			Tilemap tilemap = FindTilemap();
			if (tilemap == null || file == null || Stage.instance == null || Stage.instance.board == null)
			{
				return;
			}

			EnsurePrototype(tilemap);
			EnsureOrigin(tilemap);
			if (!file.TilesDefault)
			{
				ClearTemplateTiles(tilemap);
			}

			if (file.Tiles != null)
			{
				for (int i = 0; i < file.Tiles.Count; i++)
				{
					EncounterHexTile tile = file.Tiles[i];
					if (tile == null)
					{
						continue;
					}

					PaintCell(file, tilemap, tile.Col, tile.Row, tile.TerrainId, tile.Template);
				}
			}

			tilemap.RefreshAllTiles();
		}

		/// <summary>Paints one live hex and stores the override. False when off-board or no Tilemap.</summary>
		internal static bool TryPaint(EncounterFileModel file, int col, int row, int terrainId)
		{
			if (file == null || !IsOnLiveBoard(col, row))
			{
				return false;
			}

			Tilemap tilemap = FindTilemap();
			if (tilemap == null || !PaintCell(file, tilemap, col, row, terrainId, SelectedSourceTemplate))
			{
				return false;
			}

			EncounterTileRules.Set(file, col, row, terrainId, SelectedSourceTemplate);
			RefreshCell(tilemap, col, row);
			return true;
		}

		/// <summary>Restores the blank or template floor and drops the override. False when nothing was authored.</summary>
		internal static bool TryErase(EncounterFileModel file, int col, int row)
		{
			if (file == null || !IsOnLiveBoard(col, row))
			{
				return false;
			}

			Tilemap tilemap = FindTilemap();
			if (tilemap == null || !EnsureOrigin(tilemap))
			{
				return false;
			}

			if (!EncounterTileRules.Clear(file, col, row)
				&& !Originals.ContainsKey(CellKey(col, row, 0))
				&& !Originals.ContainsKey(CellKey(col, row, 1)))
			{
				return false;
			}

			int leftX;
			int rightX;
			int cellY;
			EncounterTileRules.HexToTileCells(col, row, originX, originY, out leftX, out rightX, out cellY);
			RestoreHalf(tilemap, col, row, 0, leftX, cellY);
			RestoreHalf(tilemap, col, row, 1, rightX, cellY);
			RefreshCell(tilemap, col, row);
			return true;
		}

		/// <summary>Rebuilds every Tilemap sprite after a stamp so a new terrain replaces the old one.</summary>
		internal static void RefreshMap()
		{
			Tilemap tilemap = FindTilemap();
			if (tilemap != null)
			{
				tilemap.RefreshAllTiles();
			}
		}

		/// <summary>True when the loaded arena has a Tilemap we can stamp.</summary>
		internal static bool HasTilemap()
		{
			return FindTilemap() != null;
		}

		/// <summary>True when the selected catalog row is a custom stub with no floor art yet.</summary>
		internal static bool IsCustomSelected(EncounterFileModel file)
		{
			EncounterTerrainModel row = EncounterTerrainRules.Find(file, SelectedTerrainId, SelectedSourceTemplate);
			return row != null
				&& string.Equals(row.Source, EncounterFileModel.TerrainSourceCustom, System.StringComparison.Ordinal);
		}

		/// <summary>True when this terrain has neighbor-masked hex sprites, not just an error icon.</summary>
		internal static bool HasHexSprites(int terrainId)
		{
			return HasHexSprites((EncounterFileModel)null, terrainId);
		}

		/// <summary>True when the selected source stage has hex floor art for this id.</summary>
		internal static bool HasHexSprites(EncounterFileModel file, int terrainId)
		{
			if (terrainId == 0 || IsCustomSelected(file))
			{
				return false;
			}

			HexaTile fromCatalog = EncounterTerrainCatalog.ResolveTile(file, terrainId, SelectedSourceTemplate);
			if (fromCatalog != null)
			{
				return EncounterTerrainCatalog.HasHexSprites(fromCatalog.terrainData, terrainId);
			}

			Tilemap tilemap = FindTilemap();
			HexaTile proto = EnsurePrototype(tilemap);
			return proto != null && HasHexSprites(proto.terrainData, terrainId);
		}

		/// <summary>A full hex floor sprite for this tile when neighbor masks miss, or null.</summary>
		internal static Sprite TryPickHexSprite(HexaTile tile, Vector3Int position)
		{
			if (tile == null || tile.terrainData == null || tile.terrainData.tileInfos == null)
			{
				return null;
			}

			bool parityA = EncounterTileRules.TileParityA(position.x, position.y);
			Sprite interior = null;
			Sprite any = null;
			for (int i = 0; i < tile.terrainData.tileInfos.Count; i++)
			{
				TerrainData.TileInfo info = tile.terrainData.tileInfos[i];
				if (info == null || info.sprite == null || info.terrain != tile.terrainId || info.A != parityA)
				{
					continue;
				}

				if (any == null)
				{
					any = info.sprite;
				}

				if (info.top == tile.terrainId && (info.side & tile.terrainId) == tile.terrainId)
				{
					interior = info.sprite;
				}
			}

			return interior != null ? interior : any;
		}

		/// <summary>Terrain labels that have hex floor art. Terrains already on the map come first.</summary>
		internal static void ListTerrains(List<int> ids, List<string> labels)
		{
			ids.Clear();
			labels.Clear();
			Tilemap tilemap = FindTilemap();
			HexaTile proto = EnsurePrototype(tilemap);
			if (proto == null || proto.terrainData == null || proto.terrainData.terrains == null)
			{
				return;
			}

			HashSet<int> onMap = new HashSet<int>();
			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				HexaTile existing = tilemap.GetTile(pos) as HexaTile;
				if (existing != null)
				{
					onMap.Add(existing.terrainId);
				}
			}

			AppendTerrains(proto.terrainData, onMap, true, ids, labels);
			AppendTerrains(proto.terrainData, onMap, false, ids, labels);
		}

		/// <summary>Drops cloned tile assets and the hex-to-cell origin when the embed unloads.</summary>
		internal static void Clear()
		{
			Prototypes.Clear();
			Originals.Clear();
			haveOrigin = false;
		}

		private static bool HasHexSprites(TerrainData data, int terrainId)
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

		private static void AppendTerrains(
			TerrainData data,
			HashSet<int> onMap,
			bool requireOnMap,
			List<int> ids,
			List<string> labels)
		{
			for (int i = 0; i < data.terrains.Count; i++)
			{
				TerrainData.Terrain terrain = data.terrains[i];
				if (terrain == null || !HasHexSprites(data, terrain.id))
				{
					continue;
				}

				if (onMap.Contains(terrain.id) != requireOnMap)
				{
					continue;
				}

				ids.Add(terrain.id);
				labels.Add(string.IsNullOrEmpty(terrain.name) ? "Terrain " + terrain.id : terrain.name);
			}
		}

		private static bool PaintCell(
			EncounterFileModel file,
			Tilemap tilemap,
			int col,
			int row,
			int terrainId,
			string sourceTemplate)
		{
			if (!EnsureOrigin(tilemap))
			{
				return false;
			}

			HexaTile painted = EncounterTerrainCatalog.ResolveTile(file, terrainId, sourceTemplate);
			if (painted == null || !EncounterTerrainCatalog.HasHexSprites(painted.terrainData, terrainId))
			{
				HexaTile proto = EnsurePrototype(tilemap);
				if (proto == null || !HasHexSprites(proto.terrainData, terrainId))
				{
					return false;
				}

				painted = ResolvePrototype(tilemap, proto, terrainId);
			}

			if (painted == null)
			{
				return false;
			}

			int leftX;
			int rightX;
			int cellY;
			EncounterTileRules.HexToTileCells(col, row, originX, originY, out leftX, out rightX, out cellY);
			StampHalf(tilemap, col, row, 0, leftX, cellY, painted);
			StampHalf(tilemap, col, row, 1, rightX, cellY, painted);
			return true;
		}

		private static void StampHalf(
			Tilemap tilemap,
			int col,
			int row,
			int half,
			int cellX,
			int cellY,
			TileBase painted)
		{
			Vector3Int cell = new Vector3Int(cellX, cellY, 0);
			int key = CellKey(col, row, half);
			if (!Originals.ContainsKey(key))
			{
				Originals[key] = tilemap.GetTile(cell);
			}

			if (tilemap.GetTile(cell) != painted)
			{
				tilemap.SetTile(cell, null);
			}

			tilemap.SetTile(cell, painted);
		}

		private static void RestoreHalf(Tilemap tilemap, int col, int row, int half, int cellX, int cellY)
		{
			TileBase original;
			if (!Originals.TryGetValue(CellKey(col, row, half), out original))
			{
				original = null;
			}

			tilemap.SetTile(new Vector3Int(cellX, cellY, 0), original);
		}

		private static HexaTile ResolvePrototype(Tilemap tilemap, HexaTile proto, int terrainId)
		{
			HexaTile painted;
			if (Prototypes.TryGetValue(terrainId, out painted) && painted != null)
			{
				return painted;
			}

			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				HexaTile existing = tilemap.GetTile(pos) as HexaTile;
				if (existing != null && existing.terrainId == terrainId && existing.terrainData != null)
				{
					Prototypes[terrainId] = existing;
					return existing;
				}
			}

			painted = ScriptableObject.CreateInstance<HexaTile>();
			painted.terrainData = proto.terrainData;
			painted.terrainId = terrainId;
			Prototypes[terrainId] = painted;
			return painted;
		}

		private static HexaTile EnsurePrototype(Tilemap tilemap)
		{
			if (tilemap == null)
			{
				return null;
			}

			TileTestController controller = tilemap.GetComponent<TileTestController>();
			if (controller != null)
			{
				controller.showTerrain = false;
			}

			foreach (HexaTile cached in Prototypes.Values)
			{
				if (cached != null && cached.terrainData != null)
				{
					return cached;
				}
			}

			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				HexaTile tile = tilemap.GetTile(pos) as HexaTile;
				if (tile != null && tile.terrainData != null)
				{
					if (!Prototypes.ContainsKey(tile.terrainId))
					{
						Prototypes[tile.terrainId] = tile;
					}

					return tile;
				}
			}

			return null;
		}

		private static bool EnsureOrigin(Tilemap tilemap)
		{
			if (haveOrigin)
			{
				return true;
			}

			Stage stage = Stage.instance;
			if (tilemap == null || stage == null || stage.board == null)
			{
				return false;
			}

			Dictionary<long, int> votes = new Dictionary<long, int>();
			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				if (!(tilemap.GetTile(pos) is HexaTile))
				{
					continue;
				}

				Vector3 center = tilemap.GetCellCenterWorld(pos);
				HexCoord cube = stage.board.PointToHex(new Point(center.x, center.y));
				OffsetCoord hex = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, cube);
				if (hex.col < 0 || hex.row < 0)
				{
					continue;
				}

				int ox;
				int oy;
				EncounterTileRules.TileOrigin(hex.col, hex.row, pos.x, pos.y, out ox, out oy);
				TallyOrigin(votes, ox, oy);
				EncounterTileRules.TileOrigin(hex.col, hex.row, pos.x - 1, pos.y, out ox, out oy);
				TallyOrigin(votes, ox, oy);
			}

			if (TryPickOrigin(votes, out originX, out originY))
			{
				haveOrigin = true;
				return true;
			}

			Vector3 hexWorld = HexWorld(0, 0);
			Vector3Int guess = tilemap.WorldToCell(hexWorld);
			Vector3Int best = ClosestCell(tilemap, hexWorld, guess);
			originX = best.x;
			originY = best.y;
			haveOrigin = true;
			return true;
		}

		private static Vector3Int ClosestCell(Tilemap tilemap, Vector3 world, Vector3Int guess)
		{
			Vector3Int best = guess;
			float bestDist = (tilemap.GetCellCenterWorld(guess) - world).sqrMagnitude;
			for (int dy = -2; dy <= 2; dy++)
			{
				for (int dx = -2; dx <= 2; dx++)
				{
					if (dx == 0 && dy == 0)
					{
						continue;
					}

					Vector3Int cell = new Vector3Int(guess.x + dx, guess.y + dy, 0);
					float dist = (tilemap.GetCellCenterWorld(cell) - world).sqrMagnitude;
					if (dist < bestDist)
					{
						bestDist = dist;
						best = cell;
					}
				}
			}

			return best;
		}

		private static void TallyOrigin(Dictionary<long, int> votes, int ox, int oy)
		{
			long key = ((long)ox << 32) | (uint)oy;
			int count;
			votes.TryGetValue(key, out count);
			votes[key] = count + 1;
		}

		private static bool TryPickOrigin(Dictionary<long, int> votes, out int ox, out int oy)
		{
			ox = 0;
			oy = 0;
			if (votes.Count == 0)
			{
				return false;
			}

			int best = -1;
			foreach (KeyValuePair<long, int> pair in votes)
			{
				int candidateX = (int)(pair.Key >> 32);
				int candidateY = (int)pair.Key;
				if (pair.Value > best || (pair.Value == best && candidateX < ox))
				{
					best = pair.Value;
					ox = candidateX;
					oy = candidateY;
				}
			}

			return best > 0;
		}

		private static void ClearTemplateTiles(Tilemap tilemap)
		{
			if (tilemap == null)
			{
				return;
			}

			BoundsInt bounds = tilemap.cellBounds;
			foreach (Vector3Int pos in bounds.allPositionsWithin)
			{
				if (tilemap.GetTile(pos) is HexaTile)
				{
					tilemap.SetTile(pos, null);
				}
			}
		}

		private static void RefreshCell(Tilemap tilemap, int col, int row)
		{
			int leftX;
			int rightX;
			int cellY;
			EncounterTileRules.HexToTileCells(col, row, originX, originY, out leftX, out rightX, out cellY);
			tilemap.RefreshTile(new Vector3Int(leftX, cellY, 0));
			tilemap.RefreshTile(new Vector3Int(rightX, cellY, 0));
		}

		private static Vector3 HexWorld(int col, int row)
		{
			HexCoord cube = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(col, row));
			Point center = Stage.instance.board.HexToCenter(cube);
			return new Vector3((float)center.x, (float)center.y, 0f);
		}

		private static int CellKey(int col, int row, int half)
		{
			return (col << 16) ^ ((row & 255) << 8) ^ (half & 1);
		}

		private static bool IsOnLiveBoard(int col, int row)
		{
			Stage stage = Stage.instance;
			return stage != null && stage.board != null
				&& col >= 0 && row >= 0
				&& col < stage.board.width && row < stage.board.height;
		}

		private static Tilemap FindTilemap()
		{
			TileTestController controller = Object.FindObjectOfType<TileTestController>();
			if (controller != null)
			{
				Tilemap fromController = controller.GetComponent<Tilemap>();
				if (fromController != null)
				{
					return fromController;
				}
			}

			return Object.FindObjectOfType<Tilemap>();
		}
	}
}
