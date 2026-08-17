using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free sparse floor-tile overrides for Encounter Setup paint.</summary>
	/// <remarks>
	/// Size still comes from walkable hexes. A tile off the live board is stored but
	/// does not grow the rect — Unblock first. Missing tiles mean the template Tilemap.
	/// </remarks>
	internal static class EncounterTileRules
	{
		/// <summary>Override for this hex, or null when the template tile stands.</summary>
		internal static EncounterHexTile Find(EncounterFileModel file, int col, int row)
		{
			if (file == null || file.Tiles == null)
			{
				return null;
			}

			for (int i = 0; i < file.Tiles.Count; i++)
			{
				EncounterHexTile tile = file.Tiles[i];
				if (tile != null && tile.Col == col && tile.Row == row)
				{
					return tile;
				}
			}

			return null;
		}

		/// <summary>Writes or replaces the terrain id for this hex.</summary>
		internal static void Set(EncounterFileModel file, int col, int row, int terrainId)
		{
			Set(file, col, row, terrainId, null);
		}

		/// <summary>Writes or replaces the terrain id and optional source-stage template.</summary>
		internal static void Set(EncounterFileModel file, int col, int row, int terrainId, string template)
		{
			if (file == null)
			{
				return;
			}

			if (file.Tiles == null)
			{
				file.Tiles = new List<EncounterHexTile>();
			}

			string stored = StoredTemplate(file, template);
			EncounterHexTile existing = Find(file, col, row);
			if (existing != null)
			{
				existing.TerrainId = terrainId;
				existing.Template = stored;
				return;
			}

			file.Tiles.Add(new EncounterHexTile
			{
				Col = col,
				Row = row,
				TerrainId = terrainId,
				Template = stored
			});
		}

		/// <summary>Removes the override so the template tile stands. False when none was stored.</summary>
		internal static bool Clear(EncounterFileModel file, int col, int row)
		{
			EncounterHexTile existing = Find(file, col, row);
			if (existing == null || file.Tiles == null)
			{
				return false;
			}

			return file.Tiles.Remove(existing);
		}

		/// <summary>True when this hex already stores this terrain id and source stage.</summary>
		/// <remarks>
		/// Vanilla rooms reuse the same bit-flag ids. Import Ice and host Ice are
		/// different stamps. Skip only when both id and stored template match.
		/// </remarks>
		internal static bool IsSameStamp(EncounterFileModel file, EncounterHexTile tile, int terrainId, string template)
		{
			if (tile == null || tile.TerrainId != terrainId)
			{
				return false;
			}

			return string.Equals(tile.Template ?? string.Empty, StoredTemplate(file, template),
				System.StringComparison.Ordinal);
		}

		/// <summary>Empty when the source is the host template; otherwise the canonical prefab name.</summary>
		internal static string StoredTemplate(EncounterFileModel file, string template)
		{
			if (string.IsNullOrEmpty(template) || file == null)
			{
				return string.Empty;
			}

			string canonical = EncounterTemplateRules.Canonical(template);
			if (string.Equals(canonical, EncounterTemplateRules.Canonical(file.Template),
				System.StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}

			return canonical;
		}

		/// <summary>Left Tilemap cell of the two-cell pair that covers this odd-r hex.</summary>
		/// <remarks>
		/// Combat rooms use a Rectangle Grid (0.55 x 0.5), not a hex Grid. Each combat
		/// hex is two cells wide (A/B sprites). Hex columns skip a cell; odd rows shift
		/// +1. <paramref name="originX"/> / <paramref name="originY"/> are the left cell
		/// of hex (0,0). The right cell is <c>cellX + 1</c>.
		/// </remarks>
		internal static void HexToTileCell(int col, int row, int originX, int originY, out int cellX, out int cellY)
		{
			cellX = originX + (col * 2) + (row & 1);
			cellY = originY - row;
		}

		/// <summary>Left and right Tilemap cells that together fill this combat hex.</summary>
		internal static void HexToTileCells(
			int col,
			int row,
			int originX,
			int originY,
			out int leftX,
			out int rightX,
			out int cellY)
		{
			HexToTileCell(col, row, originX, originY, out leftX, out cellY);
			rightX = leftX + 1;
		}

		/// <summary>Inverts <see cref="HexToTileCell"/> so a known tile can calibrate the origin.</summary>
		internal static void TileOrigin(int col, int row, int cellX, int cellY, out int originX, out int originY)
		{
			originX = cellX - (col * 2) - (row & 1);
			originY = cellY + row;
		}

		/// <summary>Vanilla HexaTile A/B parity for a rectangular Tilemap cell.</summary>
		internal static bool TileParityA(int cellX, int cellY)
		{
			int rowParity = cellY % 2;
			if (rowParity < 0)
			{
				rowParity += 2;
			}

			int colParity = cellX % 2;
			if (colParity < 0)
			{
				colParity += 2;
			}

			return (colParity == 0) ^ (rowParity == 0);
		}
	}
}
