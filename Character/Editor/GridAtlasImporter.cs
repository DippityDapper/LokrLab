using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>v1 atlas import: grid slicing only. The user gives a row/column count; every non-empty cell gets trimmed to its own alpha bounding box and written out as its own PNG into the target rig folder, exactly the shape OnLoadClicked already reads.</summary>
	/// <remarks>Cells are auto-named Part_01, Part_02, ... in raster order rather than prompting for a name per cell -- Load matches a part's name to its PNG's filename, so renaming a generated file in Explorer before clicking Load is simpler than a dedicated per-cell rename dialog.</remarks>
	internal static class GridAtlasImporter
	{
		private const float AlphaThreshold = 0.01f;

		/// <summary>Slices an atlas into a rows x cols grid, writing each non-empty cell's alpha-trimmed content as its own PNG.</summary>
		/// <remarks>Cell row 0 is the top of the image, matching how a person reads a grid; Unity texture pixel rows run bottom-to-top, so the cell Y lookup inverts.</remarks>
		internal static bool Run(string targetFolder, IReadOnlyDictionary<string, string> parameters, out string message)
		{
			if (parameters == null || !parameters.TryGetValue("atlasPath", out string atlasPath) || string.IsNullOrEmpty(atlasPath))
			{
				message = "No atlas file specified.";
				return false;
			}
			if (!File.Exists(atlasPath))
			{
				message = "Atlas file not found: " + atlasPath;
				return false;
			}
			if (!parameters.TryGetValue("rows", out string rowsText) || !int.TryParse(rowsText, out int rows) || rows < 1
				|| !parameters.TryGetValue("cols", out string colsText) || !int.TryParse(colsText, out int cols) || cols < 1)
			{
				message = "Rows/Cols must both be whole numbers of 1 or more.";
				return false;
			}

			Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!atlas.LoadImage(File.ReadAllBytes(atlasPath)))
			{
				message = "Could not decode '" + atlasPath + "' as an image.";
				Object.Destroy(atlas);
				return false;
			}

			try
			{
				Directory.CreateDirectory(targetFolder);
				int cellWidth = atlas.width / cols;
				int cellHeight = atlas.height / rows;
				if (cellWidth < 1 || cellHeight < 1)
				{
					message = string.Format("Atlas is {0}x{1}px — too small for a {2}x{3} grid.", atlas.width, atlas.height, cols, rows);
					return false;
				}

				int written = 0;
				for (int row = 0; row < rows; row++)
				{
					for (int col = 0; col < cols; col++)
					{
						int cellX = col * cellWidth;
						int cellY = atlas.height - (row + 1) * cellHeight;
						Color[] cellPixels = atlas.GetPixels(cellX, cellY, cellWidth, cellHeight);

						if (!TryGetAlphaBounds(cellPixels, cellWidth, cellHeight, out int minX, out int minY, out int maxX, out int maxY))
						{
							continue;
						}

						int trimmedWidth = maxX - minX + 1;
						int trimmedHeight = maxY - minY + 1;
						Color[] trimmedPixels = new Color[trimmedWidth * trimmedHeight];
						for (int y = 0; y < trimmedHeight; y++)
						{
							for (int x = 0; x < trimmedWidth; x++)
							{
								trimmedPixels[y * trimmedWidth + x] = cellPixels[(y + minY) * cellWidth + (x + minX)];
							}
						}

						Texture2D trimmed = new Texture2D(trimmedWidth, trimmedHeight, TextureFormat.RGBA32, false);
						trimmed.SetPixels(trimmedPixels);
						trimmed.Apply();

						written++;
						string partName = "Part_" + written.ToString("00", CultureInfo.InvariantCulture);
						File.WriteAllBytes(Path.Combine(targetFolder, partName + ".png"), trimmed.EncodeToPNG());
						Object.Destroy(trimmed);
					}
				}

				if (written > 0)
				{
					string lastName = "Part_" + written.ToString("00", CultureInfo.InvariantCulture);
					message = string.Format(
						"Sliced {0} non-empty cell(s) from a {1}x{2} grid into {3} as Part_01..{4} — rename the PNGs in Explorer if you want real part names, then click Load.",
						written, cols, rows, targetFolder, lastName);
				}
				else
				{
					message = string.Format("No non-empty cells found in a {0}x{1} grid of '{2}'.", cols, rows, atlasPath);
				}
				return written > 0;
			}
			finally
			{
				Object.Destroy(atlas);
			}
		}

		/// <summary>Finds the bounding box of non-transparent pixels. Returns false (maxX &lt; minX) if the cell is fully transparent -- not a part.</summary>
		private static bool TryGetAlphaBounds(Color[] pixels, int width, int height, out int minX, out int minY, out int maxX, out int maxY)
		{
			minX = int.MaxValue;
			minY = int.MaxValue;
			maxX = int.MinValue;
			maxY = int.MinValue;
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (pixels[y * width + x].a > AlphaThreshold)
					{
						if (x < minX) minX = x;
						if (x > maxX) maxX = x;
						if (y < minY) minY = y;
						if (y > maxY) maxY = y;
					}
				}
			}
			return maxX >= minX;
		}
	}
}
