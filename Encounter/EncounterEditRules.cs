using System;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free click-to-place checks for Encounter Setup.</summary>
	internal static class EncounterEditRules
	{
		/// <summary>Error that blocks placing <paramref name="combatant"/> on this hex, or null when legal.</summary>
		/// <remarks>
		/// Width and height are the live board, not the 24×24 estimate. The combatant's own hex
		/// is allowed so a re-tap is a no-op instead of a false duplicate. Authored walkability
		/// paint is later; <paramref name="isPassable"/> is the template cell as loaded.
		/// </remarks>
		internal static string CanPlace(
			EncounterFileModel file,
			EncounterCombatantModel combatant,
			int col,
			int row,
			int width,
			int height,
			bool isPassable = true)
		{
			if (combatant == null)
			{
				return "Select a combatant, then tap a hex.";
			}

			if (width <= 0 || height <= 0)
			{
				return "Board size is unknown.";
			}

			if (col < 0 || col >= width || row < 0 || row >= height)
			{
				return "Hex is outside the live " + width + "×" + height + " board.";
			}

			if (!isPassable)
			{
				return "Hex is not walkable.";
			}

			if (file == null || file.Combatants == null)
			{
				return null;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel other = file.Combatants[i];
				if (other == null || other == combatant
					|| string.Equals(other.Id, combatant.Id, StringComparison.Ordinal)
					|| !EncounterPlacementRules.HasPlacement(other))
				{
					continue;
				}

				if (other.Col.Value == col && other.Row.Value == row)
				{
					return "Combatant '" + other.Id + "' already uses hex (" + col + ", " + row + ").";
				}
			}

			return null;
		}
	}
}
