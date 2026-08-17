using System;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free live board size derived from walkable hexes.</summary>
	/// <remarks>
	/// Size is the template live minimum grown to cover walkable overrides and
	/// placed combatants. Do not store width/height. A leftover authored size
	/// is expanded into walkable overrides so Unblock-to-grow survives reload.
	/// Cap is <see cref="MaxLive"/> — pathfinding throws above 2^18 nodes.
	/// </remarks>
	internal static class EncounterGrowRules
	{
		/// <summary>Largest live width or height Phase 11 will author.</summary>
		internal const int MaxLive = 64;

		/// <summary>Live HexBoard size for Play and inspector clamp (no Setup halo).</summary>
		internal static void EffectiveLiveSize(EncounterFileModel file, out int width, out int height)
		{
			ContentExtent(file, out width, out height);
		}

		/// <summary>Setup size: content plus a one-hex impassable ring so Unblock can grow.</summary>
		internal static void SetupLiveSize(EncounterFileModel file, out int width, out int height)
		{
			ContentExtent(file, out width, out height);
			if (width < MaxLive)
			{
				width++;
			}

			if (height < MaxLive)
			{
				height++;
			}
		}

		/// <summary>Pins one axis to the template live minimum and <see cref="MaxLive"/>.</summary>
		internal static int ClampAxis(int value, int minLive)
		{
			if (value < minLive)
			{
				return minLive;
			}

			if (value > MaxLive)
			{
				return MaxLive;
			}

			return value;
		}

		/// <summary>True when walkable hexes or placements extend past the template live size.</summary>
		internal static bool HasGrown(EncounterFileModel file)
		{
			ContentExtent(file, out int width, out int height);
			EncounterPlacementRules.LiveSize(file != null ? file.Template : EncounterFileModel.DefaultTemplate,
				out int minWidth, out int minHeight);
			return width > minWidth || height > minHeight;
		}

		/// <summary>Turns leftover width/height into walkable overrides, then drops the size fields.</summary>
		internal static void Normalize(EncounterFileModel file)
		{
			if (file == null)
			{
				return;
			}

			ExpandAuthoredSizeIntoOverrides(file);
			file.Width = null;
			file.Height = null;
		}

		/// <summary>Clamps every placed combatant onto the effective live board.</summary>
		internal static void ClampCombatants(EncounterFileModel file)
		{
			if (file == null || file.Combatants == null)
			{
				return;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterPlacementRules.ClampToLiveBoard(file.Combatants[i], file);
			}
		}

		private static void ContentExtent(EncounterFileModel file, out int width, out int height)
		{
			string template = file != null ? file.Template : EncounterFileModel.DefaultTemplate;
			EncounterPlacementRules.LiveSize(template, out int minWidth, out int minHeight);
			int maxWidth = minWidth;
			int maxHeight = minHeight;
			IncludeAuthoredSize(file, ref maxWidth, ref maxHeight);
			IncludeWalkableOverrides(file, ref maxWidth, ref maxHeight);
			IncludePlacements(file, ref maxWidth, ref maxHeight);
			width = ClampAxis(maxWidth, minWidth);
			height = ClampAxis(maxHeight, minHeight);
		}

		private static void IncludeAuthoredSize(EncounterFileModel file, ref int width, ref int height)
		{
			if (file == null)
			{
				return;
			}

			if (file.Width.HasValue && file.Width.Value > width)
			{
				width = file.Width.Value;
			}

			if (file.Height.HasValue && file.Height.Value > height)
			{
				height = file.Height.Value;
			}
		}

		private static void IncludeWalkableOverrides(EncounterFileModel file, ref int width, ref int height)
		{
			if (file == null || file.Overrides == null)
			{
				return;
			}

			for (int i = 0; i < file.Overrides.Count; i++)
			{
				EncounterHexOverride hex = file.Overrides[i];
				if (hex == null || !hex.Walkable || hex.Col < 0 || hex.Row < 0)
				{
					continue;
				}

				if (hex.Col + 1 > width)
				{
					width = hex.Col + 1;
				}

				if (hex.Row + 1 > height)
				{
					height = hex.Row + 1;
				}
			}
		}

		private static void IncludePlacements(EncounterFileModel file, ref int width, ref int height)
		{
			if (file == null || file.Combatants == null)
			{
				return;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (!EncounterPlacementRules.HasPlacement(combatant))
				{
					continue;
				}

				if (combatant.Col.Value + 1 > width)
				{
					width = combatant.Col.Value + 1;
				}

				if (combatant.Row.Value + 1 > height)
				{
					height = combatant.Row.Value + 1;
				}
			}
		}

		private static void ExpandAuthoredSizeIntoOverrides(EncounterFileModel file)
		{
			if (file == null || (!file.Width.HasValue && !file.Height.HasValue))
			{
				return;
			}

			EncounterPlacementRules.LiveSize(file.Template, out int minWidth, out int minHeight);
			int authoredWidth = file.Width.HasValue ? ClampAxis(file.Width.Value, minWidth) : minWidth;
			int authoredHeight = file.Height.HasValue ? ClampAxis(file.Height.Value, minHeight) : minHeight;
			if (authoredWidth <= minWidth && authoredHeight <= minHeight)
			{
				return;
			}

			for (int col = minWidth; col < authoredWidth; col++)
			{
				for (int row = 0; row < authoredHeight; row++)
				{
					if (EncounterBoardRules.FindOverride(file, col, row) == null)
					{
						EncounterBoardRules.SetOverride(file, col, row, true);
					}
				}
			}

			for (int row = minHeight; row < authoredHeight; row++)
			{
				for (int col = 0; col < minWidth; col++)
				{
					if (EncounterBoardRules.FindOverride(file, col, row) == null)
					{
						EncounterBoardRules.SetOverride(file, col, row, true);
					}
				}
			}
		}
	}
}
