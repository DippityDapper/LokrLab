using System.Collections.Generic;
using Ironhide.Battlechest.Common.Game.Gameboard;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;

namespace LokrLab.Encounter
{
	/// <summary>Spawns authored decorative (non-combat) units onto the live embed board.</summary>
	/// <remarks>
	/// Mirrors <see cref="EncounterProps"/>, but instantiates a real <c>Unit</c> via
	/// <c>SandboxRoster.SpawnAt</c> (the same rig-spawn path a combatant uses) rather than a
	/// static prefab, then applies the same cinematic state flags vanilla's own
	/// <c>EncounterDefinition.CreateUnit</c> sets for <c>spawnDataCinematicUnits</c> entries:
	/// <c>NON_TARGETABLE</c>, <c>NOT_IN_INITIATIVE_BAR</c>, <c>IS_CINEMATIC</c>,
	/// <c>UnitGroup.CinematicSide</c>. Clear despawns via <c>Stage.RemoveUnit</c> like any other
	/// live unit removal.
	/// </remarks>
	internal static class EncounterDecorations
	{
		private static readonly Dictionary<string, Unit> spawnedById =
			new Dictionary<string, Unit>();

		/// <summary>Despawns tracked instances and forgets them.</summary>
		internal static void Clear()
		{
			Stage stage = Stage.instance;
			foreach (KeyValuePair<string, Unit> pair in spawnedById)
			{
				if (pair.Value != null && stage != null)
				{
					stage.RemoveUnit(pair.Value);
				}
			}

			spawnedById.Clear();
		}

		/// <summary>Rebuilds every placed decoration on the current Stage board.</summary>
		internal static void Apply(EncounterFileModel file)
		{
			Clear();
			if (file == null || file.Decorations == null)
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			for (int i = 0; i < file.Decorations.Count; i++)
			{
				EncounterDecorationModel decoration = file.Decorations[i];
				if (decoration == null || !EncounterDecorationRules.HasPlacement(decoration))
				{
					continue;
				}

				Unit unit = Spawn(stage, decoration);
				if (unit != null)
				{
					spawnedById[decoration.Id] = unit;
				}
			}
		}

		private static Unit Spawn(Stage stage, EncounterDecorationModel decoration)
		{
			if (string.IsNullOrEmpty(decoration.UnitId))
			{
				return null;
			}

			UnitDefinition definition = SandboxRoster.ResolveDefinitionAtLevel(decoration.UnitId, 1);
			if (definition == null && UnityDefinitionsParser.instance != null)
			{
				definition = UnityDefinitionsParser.instance.GetDefinition(decoration.UnitId);
			}

			if (definition == null)
			{
				string message = "EncounterDecorations: no unit definition for '" + decoration.UnitId + "' (" + decoration.Id + ").";
				LokrLab.Lab.SetStatus(message);
				LokrLabPlugin.Log.LogWarning(message);
				return null;
			}

			int col = decoration.Col.Value;
			int row = decoration.Row.Value;
			if (col < 0 || col >= stage.board.width || row < 0 || row >= stage.board.height)
			{
				return null;
			}

			HexGridItem<GameHexGridItemData> hex = stage.board.GetHexItem(new OffsetCoord(col, row));
			if (hex == null)
			{
				return null;
			}

			Unit unit = SandboxRoster.SpawnAt(stage, definition, hex, UnitGroup.CinematicSide, decoration.Flipped, false, 1);
			if (unit == null)
			{
				return null;
			}

			unit.isAI = false;
			unit.isCinematic = true;
			unit.states.Enable("NON_TARGETABLE");
			unit.states.Enable("NOT_IN_INITIATIVE_BAR");
			unit.states.Enable("IS_CINEMATIC");
			return unit;
		}
	}
}
