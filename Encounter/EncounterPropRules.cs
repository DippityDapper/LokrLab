using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free prop instance upserts for Encounter Setup place.</summary>
	/// <remarks>
	/// Props are placed instances, not a paint catalog. They do not auto-block
	/// walkability — the author uses Block if the hex should be impassable.
	/// They do not grow the live hex rect. Missing <c>props</c> on v1–v6 files
	/// is an empty list. Missing <c>snap</c> is true (hex center). Free-move
	/// props store world <c>x</c> / <c>y</c> and do not occupy a hex.
	/// </remarks>
	internal static class EncounterPropRules
	{
		/// <summary>Row with this id, or null.</summary>
		internal static EncounterPropModel Find(EncounterFileModel file, string id)
		{
			if (file == null || file.Props == null || string.IsNullOrEmpty(id))
			{
				return null;
			}

			for (int i = 0; i < file.Props.Count; i++)
			{
				EncounterPropModel prop = file.Props[i];
				if (prop != null && string.Equals(prop.Id, id, StringComparison.Ordinal))
				{
					return prop;
				}
			}

			return null;
		}

		/// <summary>Appends a row for this scenario prefab. Null when the name is empty.</summary>
		internal static EncounterPropModel Add(EncounterFileModel file, string prefabName)
		{
			if (file == null || string.IsNullOrEmpty(prefabName))
			{
				return null;
			}

			if (file.Props == null)
			{
				file.Props = new List<EncounterPropModel>();
			}

			string name = prefabName.Trim();
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			EncounterPropModel prop = new EncounterPropModel
			{
				Id = EncounterFileModel.MintCombatantId(name, file.UsedIds()),
				PrefabName = name
			};
			if (EncounterFileModel.ValidateProp(prop, file.UsedIds()) != null)
			{
				return null;
			}

			file.Props.Add(prop);
			return prop;
		}

		/// <summary>Removes the row. False when it was not listed.</summary>
		internal static bool Remove(EncounterFileModel file, string id)
		{
			if (file == null || file.Props == null || string.IsNullOrEmpty(id))
			{
				return false;
			}

			for (int i = 0; i < file.Props.Count; i++)
			{
				if (file.Props[i] != null
					&& string.Equals(file.Props[i].Id, id, StringComparison.Ordinal))
				{
					file.Props.RemoveAt(i);
					return true;
				}
			}

			return false;
		}

		/// <summary>True when this prop snaps to a hex center. Null or missing key is snap.</summary>
		internal static bool SnapsToGrid(EncounterPropModel prop)
		{
			return prop == null || prop.Snap;
		}

		/// <summary>True when the prop can spawn: snap needs col/row; free needs x/y or a hex fallback.</summary>
		internal static bool HasPlacement(EncounterPropModel prop)
		{
			if (prop == null)
			{
				return false;
			}

			if (!prop.Snap && prop.X.HasValue && prop.Y.HasValue)
			{
				return true;
			}

			return prop.Col.HasValue && prop.Row.HasValue;
		}

		/// <summary>Clears hex and world placement. The row stays in the file.</summary>
		internal static void ClearPlacement(EncounterPropModel prop)
		{
			if (prop == null)
			{
				return;
			}

			prop.Col = null;
			prop.Row = null;
			prop.X = null;
			prop.Y = null;
		}

		/// <summary>Error that blocks placing this prop, or null when legal.</summary>
		/// <remarks>
		/// Off-board hexes are refused. Props do not grow the board and do not
		/// require a walkable cell. Another snap prop on the same hex is refused;
		/// a free-move prop or a combatant on that hex is allowed.
		/// </remarks>
		internal static string CanPlace(
			EncounterFileModel file,
			EncounterPropModel prop,
			int col,
			int row,
			int width,
			int height)
		{
			if (prop == null)
			{
				return "Select a prop, then tap a hex.";
			}

			if (width <= 0 || height <= 0)
			{
				return "Board size is unknown.";
			}

			if (col < 0 || col >= width || row < 0 || row >= height)
			{
				return "Unblock to grow the board, then place props.";
			}

			if (file == null || file.Props == null)
			{
				return null;
			}

			for (int i = 0; i < file.Props.Count; i++)
			{
				EncounterPropModel other = file.Props[i];
				if (other == null || other == prop
					|| string.Equals(other.Id, prop.Id, StringComparison.Ordinal)
					|| !other.Snap
					|| !other.Col.HasValue || !other.Row.HasValue)
				{
					continue;
				}

				if (other.Col.Value == col && other.Row.Value == row)
				{
					return "Prop '" + other.Id + "' already uses hex (" + col + ", " + row + ").";
				}
			}

			return null;
		}

		/// <summary>Snap-prop id on this hex, or null. Free-move props do not occupy hexes.</summary>
		internal static string OccupantIdAt(EncounterFileModel file, int col, int row)
		{
			if (file == null || file.Props == null)
			{
				return null;
			}

			for (int i = 0; i < file.Props.Count; i++)
			{
				EncounterPropModel prop = file.Props[i];
				if (prop != null && prop.Snap
					&& prop.Col.HasValue && prop.Row.HasValue
					&& prop.Col.Value == col && prop.Row.Value == row)
				{
					return prop.Id;
				}
			}

			return null;
		}

		/// <summary>Nearest free-move prop within <paramref name="maxDistanceSquared"/>, or null.</summary>
		internal static string NearestFreeId(
			EncounterFileModel file,
			float x,
			float y,
			float maxDistanceSquared)
		{
			if (file == null || file.Props == null || maxDistanceSquared < 0f)
			{
				return null;
			}

			string bestId = null;
			float best = maxDistanceSquared;
			for (int i = 0; i < file.Props.Count; i++)
			{
				EncounterPropModel prop = file.Props[i];
				if (prop == null || prop.Snap || !prop.X.HasValue || !prop.Y.HasValue)
				{
					continue;
				}

				float dx = prop.X.Value - x;
				float dy = prop.Y.Value - y;
				float distance = (dx * dx) + (dy * dy);
				if (distance > best)
				{
					continue;
				}

				best = distance;
				bestId = prop.Id;
			}

			return bestId;
		}
	}
}
