using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free decorative-unit row upserts for Encounter Setup place.</summary>
	/// <remarks>
	/// Mirrors <see cref="EncounterPropRules"/>, but decorations always snap to a hex (no
	/// free-move) since they spawn a real <c>Unit</c> the way a combatant does, not a static
	/// mesh. Missing <c>decorations</c> on v1–v13 files is an empty list.
	/// </remarks>
	internal static class EncounterDecorationRules
	{
		/// <summary>Row with this id, or null.</summary>
		internal static EncounterDecorationModel Find(EncounterFileModel file, string id)
		{
			if (file == null || file.Decorations == null || string.IsNullOrEmpty(id))
			{
				return null;
			}

			for (int i = 0; i < file.Decorations.Count; i++)
			{
				EncounterDecorationModel decoration = file.Decorations[i];
				if (decoration != null && string.Equals(decoration.Id, id, StringComparison.Ordinal))
				{
					return decoration;
				}
			}

			return null;
		}

		/// <summary>Appends a row, minting an id from <paramref name="unitId"/> (or a generic stem when blank). Null only when the file is missing.</summary>
		internal static EncounterDecorationModel Add(EncounterFileModel file, string unitId)
		{
			if (file == null)
			{
				return null;
			}

			if (file.Decorations == null)
			{
				file.Decorations = new List<EncounterDecorationModel>();
			}

			string name = (unitId ?? string.Empty).Trim();
			EncounterDecorationModel decoration = new EncounterDecorationModel
			{
				Id = EncounterFileModel.MintCombatantId(!string.IsNullOrEmpty(name) ? name : "decoration", file.UsedIds()),
				UnitId = name
			};
			if (EncounterFileModel.ValidateDecoration(decoration, file.UsedIds()) != null)
			{
				return null;
			}

			file.Decorations.Add(decoration);
			return decoration;
		}

		/// <summary>Removes the row. False when it was not listed.</summary>
		internal static bool Remove(EncounterFileModel file, string id)
		{
			if (file == null || file.Decorations == null || string.IsNullOrEmpty(id))
			{
				return false;
			}

			for (int i = 0; i < file.Decorations.Count; i++)
			{
				if (file.Decorations[i] != null
					&& string.Equals(file.Decorations[i].Id, id, StringComparison.Ordinal))
				{
					file.Decorations.RemoveAt(i);
					return true;
				}
			}

			return false;
		}

		/// <summary>True when this row has a hex to spawn at.</summary>
		internal static bool HasPlacement(EncounterDecorationModel decoration)
		{
			return decoration != null && decoration.Col.HasValue && decoration.Row.HasValue;
		}

		/// <summary>Clears hex placement. The row stays in the file.</summary>
		internal static void ClearPlacement(EncounterDecorationModel decoration)
		{
			if (decoration == null)
			{
				return;
			}

			decoration.Col = null;
			decoration.Row = null;
		}
	}
}
