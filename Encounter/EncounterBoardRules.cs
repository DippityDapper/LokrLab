using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free sparse walkability overrides for Encounter Setup paint.</summary>
	internal static class EncounterBoardRules
	{
		/// <summary>Override for this hex, or null when the template cell stands.</summary>
		internal static EncounterHexOverride FindOverride(EncounterFileModel file, int col, int row)
		{
			if (file == null || file.Overrides == null)
			{
				return null;
			}

			for (int i = 0; i < file.Overrides.Count; i++)
			{
				EncounterHexOverride hex = file.Overrides[i];
				if (hex != null && hex.Col == col && hex.Row == row)
				{
					return hex;
				}
			}

			return null;
		}

		/// <summary>Authored passable flag when an override exists, otherwise the file default or template cell.</summary>
		internal static bool EffectiveWalkable(EncounterFileModel file, int col, int row, bool templatePassable)
		{
			EncounterHexOverride hex = FindOverride(file, col, row);
			if (hex != null)
			{
				return hex.Walkable;
			}

			if (file != null && !file.WalkableDefault)
			{
				return false;
			}

			return templatePassable;
		}

		/// <summary>Writes or replaces the override for this hex.</summary>
		internal static void SetOverride(EncounterFileModel file, int col, int row, bool walkable)
		{
			if (file == null)
			{
				return;
			}

			if (file.Overrides == null)
			{
				file.Overrides = new List<EncounterHexOverride>();
			}

			EncounterHexOverride existing = FindOverride(file, col, row);
			if (existing != null)
			{
				existing.Walkable = walkable;
				return;
			}

			file.Overrides.Add(new EncounterHexOverride
			{
				Col = col,
				Row = row,
				Walkable = walkable
			});
		}

		/// <summary>True when an authored combatant sits on this hex.</summary>
		internal static bool HasPlacementAt(EncounterFileModel file, int col, int row)
		{
			if (file == null || file.Combatants == null)
			{
				return false;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null && EncounterPlacementRules.HasPlacement(combatant)
					&& combatant.Col.Value == col && combatant.Row.Value == row)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Forces every placed combatant's hex walkable so Block / StampAll cannot erase it.</summary>
		internal static void EnsurePlacementsWalkable(EncounterFileModel file)
		{
			if (file == null || file.Combatants == null)
			{
				return;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null && EncounterPlacementRules.HasPlacement(combatant))
				{
					SetOverride(file, combatant.Col.Value, combatant.Row.Value, true);
				}
			}
		}
	}
}
