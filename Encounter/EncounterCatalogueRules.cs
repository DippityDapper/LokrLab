using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>One TexturePacker sprite cell. X/Y are top-left atlas pixels.</summary>
	internal struct EncounterPackerRect
	{
		/// <summary>Left edge in atlas pixels.</summary>
		internal int X;

		/// <summary>Top edge in atlas pixels.</summary>
		internal int Y;

		/// <summary>Cell width in atlas pixels.</summary>
		internal int Width;

		/// <summary>Cell height in atlas pixels.</summary>
		internal int Height;
	}

	/// <summary>Unity-free name/id filter, scroll-batch math, and TexturePacker helpers for Encounter catalogues.</summary>
	internal static class EncounterCatalogueRules
	{
		/// <summary>True when query is empty or it appears in id and/or name (case-insensitive).</summary>
		internal static bool Matches(string id, string name, string query)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				return true;
			}

			string needle = query.Trim();
			return ContainsIgnoreCase(id, needle) || ContainsIgnoreCase(name, needle);
		}

		/// <summary>False for dump tokens, dummy units, and diversifier/control units that have no exo.</summary>
		internal static bool IsLikelyVisualUnit(string id)
		{
			if (string.IsNullOrEmpty(id) || id[0] == '#')
			{
				return false;
			}

			return !ContainsIgnoreCase(id, "Dummy")
				&& !ContainsIgnoreCase(id, "Diversifier")
				&& !ContainsIgnoreCase(id, "ControlUnit");
		}

		/// <summary>First <c>_</c>-delimited token of a deco sprite or prefab name (the spritesheet stem).</summary>
		internal static string SpritesheetPrefix(string spriteOrPrefabName)
		{
			if (string.IsNullOrEmpty(spriteOrPrefabName))
			{
				return null;
			}

			int under = spriteOrPrefabName.IndexOf('_');
			if (under <= 0)
			{
				return null;
			}

			return spriteOrPrefabName.Substring(0, under);
		}

		/// <summary>Parses every <c>n</c>/<c>s</c> cell from a tk2d TexturePacker text asset into dest.</summary>
		internal static int AddTexturePackerRects(string contents, IDictionary<string, EncounterPackerRect> dest)
		{
			if (string.IsNullOrEmpty(contents) || dest == null)
			{
				return 0;
			}

			int added = 0;
			string current = null;
			int x = 0;
			int y = 0;
			int width = 0;
			int height = 0;
			bool haveRect = false;
			string[] lines = contents.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (string.IsNullOrEmpty(line))
				{
					continue;
				}

				char first = line[0];
				if (first == '#')
				{
					continue;
				}

				if (first == 'n' && line.Length > 2)
				{
					FlushPackerSprite(dest, current, haveRect, x, y, width, height, ref added);
					current = PackerFileName(line.Substring(2).Trim());
					haveRect = false;
					continue;
				}

				if (first == 's' && line.Length > 2)
				{
					haveRect = TryParsePackerRect(line, out x, out y, out width, out height);
					continue;
				}

				if (first == '~')
				{
					FlushPackerSprite(dest, current, haveRect, x, y, width, height, ref added);
					current = null;
					haveRect = false;
				}
			}

			FlushPackerSprite(dest, current, haveRect, x, y, width, height, ref added);
			return added;
		}

		/// <summary>True when contents contain a cell whose name matches spriteName (case-insensitive).</summary>
		internal static bool TryGetTexturePackerRect(
			string contents,
			string spriteName,
			out EncounterPackerRect rect)
		{
			rect = default;
			if (string.IsNullOrEmpty(spriteName))
			{
				return false;
			}

			Dictionary<string, EncounterPackerRect> map =
				new Dictionary<string, EncounterPackerRect>(StringComparer.OrdinalIgnoreCase);
			AddTexturePackerRects(contents, map);
			return map.TryGetValue(spriteName, out rect);
		}

		/// <summary>End index of the next reveal batch, clamped to total.</summary>
		internal static int NextBatchEnd(int revealed, int total, int batchSize)
		{
			if (revealed < 0)
			{
				revealed = 0;
			}

			if (total < 0)
			{
				total = 0;
			}

			if (batchSize < 1)
			{
				batchSize = 1;
			}

			if (revealed >= total)
			{
				return total;
			}

			int next = revealed + batchSize;
			return next > total ? total : next;
		}

		private static bool ContainsIgnoreCase(string value, string needle)
		{
			return !string.IsNullOrEmpty(value)
				&& value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void FlushPackerSprite(
			IDictionary<string, EncounterPackerRect> dest,
			string name,
			bool haveRect,
			int x,
			int y,
			int width,
			int height,
			ref int added)
		{
			if (!haveRect || string.IsNullOrEmpty(name) || dest.ContainsKey(name))
			{
				return;
			}

			dest[name] = new EncounterPackerRect
			{
				X = x,
				Y = y,
				Width = width,
				Height = height
			};
			added++;
		}

		private static bool TryParsePackerRect(string line, out int x, out int y, out int width, out int height)
		{
			x = 0;
			y = 0;
			width = 0;
			height = 0;
			string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 5)
			{
				return false;
			}

			return int.TryParse(parts[1], out x)
				&& int.TryParse(parts[2], out y)
				&& int.TryParse(parts[3], out width)
				&& int.TryParse(parts[4], out height)
				&& width > 0
				&& height > 0;
		}

		private static string PackerFileName(string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return raw;
			}

			int slash = raw.LastIndexOf('/');
			int back = raw.LastIndexOf('\\');
			int cut = slash > back ? slash : back;
			if (cut >= 0)
			{
				raw = raw.Substring(cut + 1);
			}

			int dot = raw.LastIndexOf('.');
			if (dot > 0)
			{
				raw = raw.Substring(0, dot);
			}

			return raw;
		}
	}
}
