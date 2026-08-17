using HarmonyLib;
using LokrCharacterLab;
using Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Uses a full hex floor sprite when vanilla neighbor masks miss during Encounter paint.</summary>
	/// <remarks>
	/// <c>HexaTile.GetTileData</c> shows <c>errorSprite</c> (a 1-cell icon, often a red X)
	/// when the cell has no matching neighbor mask — empty canvas or a different terrain
	/// beside ice. Runs for Setup, Play, and Sandbox-loaded Encounter. Campaign fights
	/// keep vanilla. Do not enable <c>showTerrain</c>.
	/// </remarks>
	[HarmonyPatch(typeof(HexaTile), nameof(HexaTile.GetTileData))]
	internal static class EncounterHexaTileSpritePatch
	{
		private static void Postfix(HexaTile __instance, Vector3Int position, ITilemap tilemap, ref TileData tileData)
		{
			if (!EmbeddedFightHost.IsActive || !EmbeddedFightHost.EncounterOwnsEmbed)
			{
				return;
			}

			if (__instance == null || tileData.sprite == null)
			{
				return;
			}

			Sprite hex = EncounterTiles.TryPickHexSprite(__instance, position);
			if (hex == null || tileData.sprite == hex)
			{
				return;
			}

			if (IsHexTileSprite(__instance, tileData.sprite))
			{
				return;
			}

			tileData.sprite = hex;
			tileData.transform = Matrix4x4.identity;
		}

		private static bool IsHexTileSprite(HexaTile tile, Sprite sprite)
		{
			if (tile.terrainData == null || tile.terrainData.tileInfos == null)
			{
				return false;
			}

			for (int i = 0; i < tile.terrainData.tileInfos.Count; i++)
			{
				TerrainData.TileInfo info = tile.terrainData.tileInfos[i];
				if (info != null && info.sprite == sprite)
				{
					return true;
				}
			}

			return false;
		}
	}
}
