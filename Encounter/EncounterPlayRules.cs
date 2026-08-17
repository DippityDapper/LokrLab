using System;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free Sandbox validation: GoodSide or a filled spawn point, no duplicate hexes.</summary>
	internal static class EncounterPlayRules
	{
		/// <summary>Resolved unit-definition id for a combatant row, or empty.</summary>
		internal static string UnitId(EncounterCombatantModel combatant)
		{
			if (combatant == null)
			{
				return string.Empty;
			}

			if (string.Equals(combatant.Source, EncounterFileModel.SourceCharacter, StringComparison.Ordinal))
			{
				return combatant.ProjectId ?? string.Empty;
			}

			return combatant.UnitId ?? string.Empty;
		}

		/// <summary>First real (non-spawn-point) GoodSide combatant, or null.</summary>
		internal static EncounterCombatantModel FirstGoodSide(EncounterFileModel file)
		{
			if (file == null || file.Combatants == null)
			{
				return null;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null
					&& string.Equals(combatant.Side, EncounterFileModel.GoodSide, StringComparison.Ordinal)
					&& !string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
				{
					return combatant;
				}
			}

			return null;
		}

		/// <summary>First hero spawn point (source == spawn), or null.</summary>
		internal static EncounterCombatantModel FirstSpawnPoint(EncounterFileModel file)
		{
			if (file == null || file.Combatants == null)
			{
				return null;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null
					&& string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
				{
					return combatant;
				}
			}

			return null;
		}

		/// <summary>Error that blocks a no-fill Sandbox start, or null when the payload is legal.</summary>
		internal static string CanPlay(EncounterFileModel file)
		{
			return CanStart(file, false);
		}

		/// <summary>Error that blocks Sandbox, or null when the payload is legal to start.</summary>
		/// <param name="hasFill">True when a character will occupy the first hero spawn point.</param>
		internal static string CanStart(EncounterFileModel file, bool hasFill)
		{
			if (file == null)
			{
				return "No encounter payload.";
			}

			if (file.Combatants == null || file.Combatants.Count == 0)
			{
				return "Add at least one GoodSide combatant or Hero Spawn Point before Sandbox.";
			}

			bool hasRealGoodSide = FirstGoodSide(file) != null;
			bool hasSpawn = FirstSpawnPoint(file) != null;
			if (!hasRealGoodSide && !hasSpawn)
			{
				return "Sandbox needs at least one GoodSide combatant or a Hero Spawn Point.";
			}

			if (!hasRealGoodSide && hasSpawn && !hasFill)
			{
				return "Pick a character to fill the Hero Spawn Point.";
			}

			EncounterPlacementRules.LiveSize(file, out int width, out int height);
			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				string rowError = EncounterFileModel.ValidateCombatant(combatant, null);
				if (rowError != null)
				{
					return rowError;
				}

				bool isSpawnPoint = string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal);
				if (isSpawnPoint)
				{
					if (!EncounterPlacementRules.HasPlacement(combatant))
					{
						return "Hero spawn point '" + combatant.Id + "' needs a hex — place it before Sandbox.";
					}
				}
				else if (string.IsNullOrEmpty(UnitId(combatant)))
				{
					return "Combatant '" + combatant.Id + "' has no unit id.";
				}

				if (EncounterPlacementRules.HasPartialPlacement(combatant))
				{
					return "Combatant '" + combatant.Id + "' has only col or only row.";
				}

				if (EncounterPlacementRules.HasPlacement(combatant)
					&& (combatant.Col.Value < 0 || combatant.Col.Value >= width
						|| combatant.Row.Value < 0 || combatant.Row.Value >= height))
				{
					return "Combatant '" + combatant.Id + "' hex is outside the live "
						+ width + "×" + height + " board.";
				}

				string other = EncounterPlacementRules.DuplicateHexId(file, combatant);
				if (other != null)
				{
					return "Combatants '" + combatant.Id + "' and '" + other + "' share a hex.";
				}
			}

			return null;
		}
	}
}
