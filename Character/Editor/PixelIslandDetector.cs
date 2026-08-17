using System.Collections.Generic;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>One connected blob of non-transparent pixels in an atlas (see PixelIslandDetector.Detect). Bounds are inclusive, in Unity's native texture pixel space (row 0 = bottom of the image).</summary>
	internal sealed class PixelIsland
	{
		/// <summary>This island's id, matching its index in Detect's returned list.</summary>
		internal int Id;
		/// <summary>Inclusive bounding box of this island's pixels, in texture pixel space.</summary>
		internal int MinX, MinY, MaxX, MaxY;
	}

	/// <summary>Auto-detects "pixel islands" (connected components of non-transparent pixels) in an atlas that isn't a clean grid.</summary>
	/// <remarks>Pure algorithm, no UI/Unity-editor dependency beyond Texture2D itself, so it's followable and testable independent of the picker (IslandAtlasPickerPanel) that consumes it.</remarks>
	internal static class PixelIslandDetector
	{
		/// <summary>Same alpha threshold GridAtlasImporter uses for "is this pixel part of a sprite."</summary>
		private const float AlphaThreshold = 0.01f;

		/// <summary>8-connectivity offsets (diagonal neighbors count as connected) -- hand-drawn/anti-aliased sprite art routinely has diagonal-only pixel connections at its edges; 4-connectivity would fragment a single visual piece into multiple islands.</summary>
		private static readonly int[] NeighborDx = { -1, 0, 1, -1, 1, -1, 0, 1 };
		private static readonly int[] NeighborDy = { -1, -1, -1, 0, 0, 1, 1, 1 };

		/// <summary>Flood-fills the atlas into connected islands. labelMap is width*height, indexed [x + y*width] (matching GetPixels' row-major layout); -1 means background/transparent, otherwise the id of the island occupying that pixel.</summary>
		/// <remarks>Returned islands are sorted in reading order (top-to-bottom, then left-to-right), then their ids remapped (they were originally assigned in discovery order) so island.Id always matches its position in the returned list.</remarks>
		internal static List<PixelIsland> Detect(Texture2D atlas, out int[] labelMap)
		{
			int width = atlas.width;
			int height = atlas.height;
			Color[] pixels = atlas.GetPixels();
			labelMap = new int[width * height];
			for (int i = 0; i < labelMap.Length; i++)
			{
				labelMap[i] = -1;
			}

			List<PixelIsland> islands = new List<PixelIsland>();
			Queue<int> frontier = new Queue<int>();

			for (int startIndex = 0; startIndex < pixels.Length; startIndex++)
			{
				if (labelMap[startIndex] != -1 || pixels[startIndex].a <= AlphaThreshold)
				{
					continue;
				}

				int id = islands.Count;
				PixelIsland island = new PixelIsland { Id = id, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue };

				labelMap[startIndex] = id;
				frontier.Enqueue(startIndex);
				while (frontier.Count > 0)
				{
					int index = frontier.Dequeue();
					int x = index % width;
					int y = index / width;
					if (x < island.MinX) island.MinX = x;
					if (x > island.MaxX) island.MaxX = x;
					if (y < island.MinY) island.MinY = y;
					if (y > island.MaxY) island.MaxY = y;

					for (int n = 0; n < NeighborDx.Length; n++)
					{
						int nx = x + NeighborDx[n];
						int ny = y + NeighborDy[n];
						if (nx < 0 || nx >= width || ny < 0 || ny >= height)
						{
							continue;
						}
						int nIndex = nx + ny * width;
						if (labelMap[nIndex] != -1 || pixels[nIndex].a <= AlphaThreshold)
						{
							continue;
						}
						labelMap[nIndex] = id;
						frontier.Enqueue(nIndex);
					}
				}

				islands.Add(island);
			}

			islands.Sort((a, b) =>
			{
				int topA = height - 1 - a.MaxY;
				int topB = height - 1 - b.MaxY;
				int byTop = topA.CompareTo(topB);
				return byTop != 0 ? byTop : a.MinX.CompareTo(b.MinX);
			});

			int[] idRemap = new int[islands.Count];
			for (int newId = 0; newId < islands.Count; newId++)
			{
				idRemap[islands[newId].Id] = newId;
			}
			for (int i = 0; i < labelMap.Length; i++)
			{
				if (labelMap[i] != -1)
				{
					labelMap[i] = idRemap[labelMap[i]];
				}
			}
			for (int newId = 0; newId < islands.Count; newId++)
			{
				islands[newId].Id = newId;
			}

			return islands;
		}
	}
}
