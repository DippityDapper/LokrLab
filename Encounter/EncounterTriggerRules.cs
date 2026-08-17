using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free trigger catalog and sparse painted-cell storage for Encounter Setup paint.</summary>
	/// <remarks>
	/// Mirrors the Terrain catalog / Tile paint split: <see cref="EncounterFileModel.Triggers"/> is
	/// the Node Tree catalog (id + optional target pocket); <see cref="EncounterFileModel.TriggerCells"/>
	/// is the sparse paint. Each trigger owns its own independent hex set — cells are keyed by
	/// (col, row, triggerId), not (col, row) alone, so different triggers' regions may overlap on
	/// the same hex without disturbing each other. A trigger off the live board is stored but does
	/// not grow the rect — Unblock first.
	/// </remarks>
	internal static class EncounterTriggerRules
	{
		private static readonly System.Text.RegularExpressions.Regex LegalIdPattern =
			new System.Text.RegularExpressions.Regex("^[a-z][a-z0-9_]*$");

		// ---- Catalog ----

		/// <summary>Catalog row for this id, or null.</summary>
		internal static EncounterTriggerModel Find(EncounterFileModel file, string id)
		{
			if (file == null || file.Triggers == null || string.IsNullOrEmpty(id))
			{
				return null;
			}

			for (int i = 0; i < file.Triggers.Count; i++)
			{
				EncounterTriggerModel trigger = file.Triggers[i];
				if (trigger != null && string.Equals(trigger.Id, id, StringComparison.Ordinal))
				{
					return trigger;
				}
			}

			return null;
		}

		/// <summary>Adds a new catalog row. False when the id is illegal or already used.</summary>
		internal static bool Add(EncounterFileModel file, string id, string pocketKey)
		{
			if (file == null || string.IsNullOrEmpty(id) || !LegalIdPattern.IsMatch(id) || Find(file, id) != null)
			{
				return false;
			}

			if (file.Triggers == null)
			{
				file.Triggers = new List<EncounterTriggerModel>();
			}

			file.Triggers.Add(new EncounterTriggerModel { Id = id, PocketKey = pocketKey ?? string.Empty });
			return true;
		}

		/// <summary>
		/// Renames a trigger id and cascades the change to every painted cell and every combatant
		/// referencing it. False when the row is missing or the new id is illegal / already used.
		/// </summary>
		internal static bool Rename(EncounterFileModel file, string oldId, string newId)
		{
			EncounterTriggerModel trigger = Find(file, oldId);
			if (trigger == null || string.IsNullOrEmpty(newId) || !LegalIdPattern.IsMatch(newId)
				|| (!string.Equals(oldId, newId, StringComparison.Ordinal) && Find(file, newId) != null))
			{
				return false;
			}

			trigger.Id = newId;
			if (file.TriggerCells != null)
			{
				for (int i = 0; i < file.TriggerCells.Count; i++)
				{
					EncounterHexTrigger cell = file.TriggerCells[i];
					if (cell != null && string.Equals(cell.TriggerId, oldId, StringComparison.Ordinal))
					{
						cell.TriggerId = newId;
					}
				}
			}

			if (file.Combatants != null)
			{
				for (int i = 0; i < file.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = file.Combatants[i];
					if (combatant != null && string.Equals(combatant.TriggerId, oldId, StringComparison.Ordinal))
					{
						combatant.TriggerId = newId;
					}
				}
			}

			return true;
		}

		/// <summary>Removes the catalog row, every painted cell with this id, and clears it from any combatant using it.</summary>
		internal static bool RemoveDefinition(EncounterFileModel file, string id)
		{
			EncounterTriggerModel trigger = Find(file, id);
			if (trigger == null)
			{
				return false;
			}

			file.Triggers.Remove(trigger);
			RemoveAllCells(file, id);
			if (file.Combatants != null)
			{
				for (int i = 0; i < file.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = file.Combatants[i];
					if (combatant != null && string.Equals(combatant.TriggerId, id, StringComparison.Ordinal))
					{
						combatant.TriggerId = string.Empty;
					}
				}
			}

			return true;
		}

		/// <summary>Every catalog id, in authored order.</summary>
		internal static List<string> CatalogIds(EncounterFileModel file)
		{
			List<string> ids = new List<string>();
			if (file == null || file.Triggers == null)
			{
				return ids;
			}

			for (int i = 0; i < file.Triggers.Count; i++)
			{
				EncounterTriggerModel trigger = file.Triggers[i];
				if (trigger != null && !string.IsNullOrEmpty(trigger.Id))
				{
					ids.Add(trigger.Id);
				}
			}

			return ids;
		}

		/// <summary>Every combatant that individually opted into this trigger id, regardless of its pocket.</summary>
		internal static List<EncounterCombatantModel> CombatantsUsing(EncounterFileModel file, string id)
		{
			List<EncounterCombatantModel> combatants = new List<EncounterCombatantModel>();
			if (file == null || file.Combatants == null || string.IsNullOrEmpty(id))
			{
				return combatants;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null && string.Equals(combatant.TriggerId, id, StringComparison.Ordinal))
				{
					combatants.Add(combatant);
				}
			}

			return combatants;
		}

		/// <summary>Every BadSide combatant sharing this trigger's target pocket. Empty when the trigger has no pocket set.</summary>
		internal static List<EncounterCombatantModel> PocketMembers(EncounterFileModel file, EncounterTriggerModel trigger)
		{
			List<EncounterCombatantModel> members = new List<EncounterCombatantModel>();
			if (file == null || file.Combatants == null || trigger == null || string.IsNullOrEmpty(trigger.PocketKey))
			{
				return members;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				if (combatant != null
					&& string.Equals(combatant.Side, EncounterFileModel.BadSide, StringComparison.Ordinal)
					&& string.Equals(
						string.IsNullOrEmpty(combatant.Pocket) ? combatant.Id : combatant.Pocket,
						trigger.PocketKey, StringComparison.Ordinal))
				{
					members.Add(combatant);
				}
			}

			return members;
		}

		/// <summary>Mints <c>trigger_1</c>, <c>trigger_2</c>, … that is unused in the catalog.</summary>
		internal static string MintTriggerId(EncounterFileModel file)
		{
			HashSet<string> used = new HashSet<string>(CatalogIds(file), StringComparer.Ordinal);
			int n = 1;
			string id;
			do
			{
				id = "trigger_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
				n++;
			}
			while (used.Contains(id));

			return id;
		}

		// ---- Painted cells ----
		// Each trigger owns its own independent hex set; the same hex may belong to more
		// than one trigger, so cells are keyed by (col, row, triggerId), not (col, row) alone.

		/// <summary>True when this hex is painted for this specific trigger id (other triggers on the same hex don't count).</summary>
		internal static bool HasCell(EncounterFileModel file, int col, int row, string triggerId)
		{
			if (file == null || file.TriggerCells == null || string.IsNullOrEmpty(triggerId))
			{
				return false;
			}

			for (int i = 0; i < file.TriggerCells.Count; i++)
			{
				EncounterHexTrigger trigger = file.TriggerCells[i];
				if (trigger != null && trigger.Col == col && trigger.Row == row
					&& string.Equals(trigger.TriggerId, triggerId, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Adds this hex to this trigger's region. No-op if already painted for this id — other triggers on the same hex are untouched.</summary>
		internal static void Set(EncounterFileModel file, int col, int row, string triggerId)
		{
			if (file == null || string.IsNullOrEmpty(triggerId) || HasCell(file, col, row, triggerId))
			{
				return;
			}

			if (file.TriggerCells == null)
			{
				file.TriggerCells = new List<EncounterHexTrigger>();
			}

			file.TriggerCells.Add(new EncounterHexTrigger
			{
				Col = col,
				Row = row,
				TriggerId = triggerId
			});
		}

		/// <summary>Removes this hex from this trigger's region only. False when it wasn't painted for this id.</summary>
		internal static bool Clear(EncounterFileModel file, int col, int row, string triggerId)
		{
			if (file == null || file.TriggerCells == null || string.IsNullOrEmpty(triggerId))
			{
				return false;
			}

			for (int i = 0; i < file.TriggerCells.Count; i++)
			{
				EncounterHexTrigger trigger = file.TriggerCells[i];
				if (trigger != null && trigger.Col == col && trigger.Row == row
					&& string.Equals(trigger.TriggerId, triggerId, StringComparison.Ordinal))
				{
					file.TriggerCells.RemoveAt(i);
					return true;
				}
			}

			return false;
		}

		/// <summary>Removes every hex belonging to this trigger id. Count removed.</summary>
		internal static int RemoveAllCells(EncounterFileModel file, string triggerId)
		{
			if (file == null || file.TriggerCells == null || string.IsNullOrEmpty(triggerId))
			{
				return 0;
			}

			return file.TriggerCells.RemoveAll(t => t != null
				&& string.Equals(t.TriggerId, triggerId, StringComparison.Ordinal));
		}

		/// <summary>Every hex (col, row) painted with this trigger id. Empty when none are painted.</summary>
		internal static List<(int Col, int Row)> HexesFor(EncounterFileModel file, string triggerId)
		{
			List<(int Col, int Row)> hexes = new List<(int Col, int Row)>();
			if (file == null || file.TriggerCells == null || string.IsNullOrEmpty(triggerId))
			{
				return hexes;
			}

			for (int i = 0; i < file.TriggerCells.Count; i++)
			{
				EncounterHexTrigger trigger = file.TriggerCells[i];
				if (trigger != null && string.Equals(trigger.TriggerId, triggerId, StringComparison.Ordinal))
				{
					hexes.Add((trigger.Col, trigger.Row));
				}
			}

			return hexes;
		}
	}
}
