using Ironhide.Battlechest.Common.Hexes;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Places a catalogue card on the Setup hex under the pointer, or appends it if the board is still loading.</summary>
	/// <remarks>
	/// Release outside the hole is ignored so scrolling the list does not add
	/// rows. A drop onto an armed board writes col/row and spawns or applies
	/// that instance without restarting the embed.
	/// </remarks>
	internal static class EncounterCatalogueDrop
	{
		/// <summary>Adds and places a combatant when the pointer is over a legal Setup hex.</summary>
		internal static void TryDropCombatant(
			EncounterSession session,
			string source,
			string side,
			string id,
			Vector2 screen)
		{
			if (session == null || session.File == null || string.IsNullOrEmpty(id))
			{
				return;
			}

			OffsetCoord offset;
			bool overHole;
			bool haveHex = EncounterEdit.TryScreenToOffset(screen, out offset, out overHole);
			if (!overHole)
			{
				return;
			}

			if (!haveHex)
			{
				EncounterCombatantModel loading = AddCombatant(session, source, side, id);
				if (loading == null)
				{
					LokrLab.Lab.SetStatus("Could not add that combatant.");
					return;
				}

				EncounterNodes.AfterCombatantsChanged(session, loading.Id, restartPreview: false);
				LokrLab.Lab.SetStatus("Board is still loading — added to the list.");
				return;
			}

			int col;
			int row;
			if (!EncounterPaintRules.TryClampCell(offset.col, offset.row, out col, out row))
			{
				LokrLab.Lab.SetStatus("Board grows from (0,0). Paint toward +col / +row.");
				return;
			}

			string error = EncounterEdit.CanDropNewCombatant(col, row);
			if (error != null)
			{
				LokrLab.Lab.SetStatus(error);
				return;
			}

			EncounterCombatantModel added = AddCombatant(session, source, side, id);
			if (added == null)
			{
				LokrLab.Lab.SetStatus("Could not add that combatant.");
				return;
			}

			EncounterEdit.AssignCombatantHex(added, col, row);
			EncounterNodes.AfterCombatantsChanged(session, added.Id, restartPreview: false);
			LokrLab.Lab.SetStatus("Placed " + added.Id + " at (" + col + ", " + row + ").");
		}

		/// <summary>Adds and places a prop when the pointer is over a legal Setup hex.</summary>
		internal static void TryDropProp(EncounterSession session, string prefabName, Vector2 screen)
		{
			if (session == null || session.File == null || string.IsNullOrEmpty(prefabName))
			{
				return;
			}

			OffsetCoord offset;
			bool overHole;
			bool haveHex = EncounterEdit.TryScreenToOffset(screen, out offset, out overHole);
			if (!overHole)
			{
				return;
			}

			if (!haveHex)
			{
				EncounterPropModel loading = EncounterPropRules.Add(session.File, prefabName);
				if (loading == null)
				{
					LokrLab.Lab.SetStatus("Could not add that prop.");
					return;
				}

				EncounterNodes.AfterPropsChanged(session, loading.Id);
				LokrLab.Lab.SetStatus("Board is still loading — added to the list.");
				return;
			}

			int col;
			int row;
			if (!EncounterPaintRules.TryClampCell(offset.col, offset.row, out col, out row))
			{
				LokrLab.Lab.SetStatus("Unblock to grow the board, then place props.");
				return;
			}

			string error = EncounterEdit.CanDropNewProp(col, row);
			if (error != null)
			{
				LokrLab.Lab.SetStatus(error);
				return;
			}

			EncounterPropModel added = EncounterPropRules.Add(session.File, prefabName);
			if (added == null)
			{
				LokrLab.Lab.SetStatus("Could not add that prop.");
				return;
			}

			EncounterEdit.TryAssignPropHex(added, col, row);
			EncounterNodes.AfterPropsChanged(session, added.Id);
			LokrLab.Lab.SetStatus("Placed " + added.Id + " at (" + col + ", " + row + ").");
		}

		private static EncounterCombatantModel AddCombatant(
			EncounterSession session,
			string source,
			string side,
			string id)
		{
			return source == EncounterFileModel.SourceCharacter
				? session.TryAddCharacter(id, side)
				: session.TryAddUnit(id, side);
		}
	}
}
