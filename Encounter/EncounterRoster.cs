using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ironhide.Battlechest.Common.Game.Gameboard;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.View.Game.Units;
using LokrLab.Editor;

namespace LokrLab.Encounter
{
	/// <summary>Spawns every authored combatant onto the embed Stage before StartFight.</summary>
	internal static class EncounterRoster
	{
		private static Dictionary<string, Unit> lastSpawned;

		/// <summary>Play-blocking error, or null when the in-memory payload is ready to start.</summary>
		internal static string ValidateReady(EncounterSession session)
		{
			if (session == null || session.File == null)
			{
				return "No encounter payload.";
			}

			string error = EncounterPlayRules.CanPlay(session.File);
			if (error != null)
			{
				return error;
			}

			for (int i = 0; i < session.File.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = session.File.Combatants[i];
				if (combatant == null
					|| !string.Equals(combatant.Source, EncounterFileModel.SourceCharacter, StringComparison.Ordinal))
				{
					continue;
				}

				if (!CharacterFolderExists(combatant.ProjectId))
				{
					return "Character folder '" + combatant.ProjectId + "' is missing.";
				}
			}

			return null;
		}

		/// <summary>
		/// Applies walkability, floor tiles, and props, then spawns each combatant at its authored
		/// hex or a center-offset slot. Optional map is combatant id to spawned unit.
		/// </summary>
		/// <remarks>
		/// <paramref name="fillUnitId"/> spawns into the first hero spawn point
		/// (<see cref="EncounterFileModel.SourceSpawn"/>) — used by Sandbox loading an Encounter to
		/// place the currently open character. Null leaves every spawn point empty (standalone Play).
		/// Later spawn points are not filled; a full-party fill is a future Adventures concern.
		/// <paramref name="firstGoodLevelOverride"/> is the Sandbox Level dropdown: it ranks the
		/// first GoodSide hero for this run without writing the combatant's saved Level. Setup
		/// omits it so preview keeps the authored rank.
		/// </remarks>
		internal static void Spawn(
			Stage stage,
			EncounterFileModel file,
			IDictionary<string, Unit> spawnedById = null,
			string fillUnitId = null,
			int fillLevel = 1,
			int? firstGoodLevelOverride = null)
		{
			if (stage == null || file == null || file.Combatants == null || stage.board == null)
			{
				return;
			}

			lastSpawned = new Dictionary<string, Unit>(StringComparer.Ordinal);
			EncounterEdit.ApplyAuthoredBoard(stage, file);
			EncounterTiles.ApplyAuthoredTiles(file);
			EncounterProps.Apply(file);
			EncounterDecorations.Apply(file);
			EncounterCombatantModel firstGood = EncounterPlayRules.FirstGoodSide(file);
			HashSet<string> occupied = new HashSet<string>(StringComparer.Ordinal);
			int goodUnset = 0;
			int badUnset = 0;
			bool fillConsumed = string.IsNullOrEmpty(fillUnitId);

			for (int pass = 0; pass < 2; pass++)
			{
				bool placedPass = pass == 0;
				for (int i = 0; i < file.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = file.Combatants[i];
					if (combatant == null)
					{
						continue;
					}

					bool placed = EncounterPlacementRules.HasPlacement(combatant);
					if (placed != placedPass)
					{
						continue;
					}

					bool isSpawnPoint = string.Equals(
						combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal);
					bool fillsThisRow = isSpawnPoint && !fillConsumed;
					if (isSpawnPoint && !fillsThisRow)
					{
						// No fill available (standalone Play) or already used on an earlier spawn
						// point — an intentionally empty slot, not an error, so no warning.
						continue;
					}

					string unitId = fillsThisRow ? fillUnitId : EncounterPlayRules.UnitId(combatant);
					int level = fillsThisRow ? fillLevel : combatant.Level;
					if (!fillsThisRow
						&& firstGoodLevelOverride.HasValue
						&& firstGood != null
						&& string.Equals(combatant.Id, firstGood.Id, StringComparison.Ordinal))
					{
						level = firstGoodLevelOverride.Value;
					}
					UnitDefinition definition = SandboxRoster.ResolveDefinitionAtLevel(unitId, level);
					if (definition == null && UnityDefinitionsParser.instance != null)
					{
						definition = UnityDefinitionsParser.instance.GetDefinition(unitId);
					}

					if (definition == null)
					{
						LokrLabPlugin.Log.LogWarning(
							"EncounterRoster: no unit definition for '" + unitId + "' (" + combatant.Id + ").");
						continue;
					}

					bool good = string.Equals(combatant.Side, EncounterFileModel.GoodSide, StringComparison.Ordinal);
					HexGridItem<GameHexGridItemData> hex = placed
						? PlacedHex(stage.board, combatant, occupied)
						: HeuristicHex(stage.board, good, good ? goodUnset++ : badUnset++, occupied);
					if (hex == null)
					{
						LokrLabPlugin.Log.LogWarning(
							"EncounterRoster: no hex for '" + combatant.Id + "'.");
						continue;
					}

					bool isHero = (firstGood != null && string.Equals(combatant.Id, firstGood.Id, StringComparison.Ordinal))
						|| (firstGood == null && fillsThisRow);
					UnitGroup group = good ? UnitGroup.GoodSide : UnitGroup.BadSide;
					Unit unit = SandboxRoster.SpawnAt(
						stage, definition, hex, group, combatant.Flipped, isHero, level);
					if (unit != null)
					{
						unit.isAI = !good;
						if (!string.IsNullOrEmpty(combatant.Id))
						{
							lastSpawned[combatant.Id] = unit;
							if (spawnedById != null)
							{
								spawnedById[combatant.Id] = unit;
							}
						}

						if (fillsThisRow)
						{
							fillConsumed = true;
						}
					}
				}
			}

			if (file.Exploration && EncounterSandbox.IsArmed)
			{
				BeginExploration(file);
			}
		}

		/// <summary>
		/// Parks every BadSide unit and groups them into pockets by <c>Pocket</c> (or a solo pocket per
		/// id). Live Encounter fight (Play or Sandbox Load Encounter) only — Setup preview keeps
		/// today's behavior (turns are already frozen there).
		/// </summary>
		private static void BeginExploration(EncounterFileModel file)
		{
			Dictionary<string, List<EncounterExploration.Member>> pocketMap = EncounterExploration.NewPocketMap();
			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				Unit unit;
				if (combatant == null
					|| !string.Equals(combatant.Side, EncounterFileModel.BadSide, StringComparison.Ordinal)
					|| !lastSpawned.TryGetValue(combatant.Id, out unit) || unit == null)
				{
					continue;
				}

				if (unit.states.IsOn("NOT_IN_INITIATIVE_BAR"))
				{
					// Already excluded by its own UnitDefinition (dummy / hazard / trap kinds ship
					// with NOT_IN_INITIATIVE_BAR + NON_TARGETABLE baked into "states"). Leave it
					// exactly as authored — do not track or wake it as an exploration pocket member.
					continue;
				}

				unit.states.Enable("NOT_IN_INITIATIVE_BAR");
				string pocketKey = !string.IsNullOrEmpty(combatant.Pocket) ? combatant.Pocket : combatant.Id;
				int radius = combatant.AggroRadius.HasValue && combatant.AggroRadius.Value > 0
					? combatant.AggroRadius.Value
					: file.DefaultAggroRadius;
				HashSet<EncounterExplorationRules.HexPos> region = ResolveTriggerRegion(file, combatant.TriggerId);
				EncounterExploration.AddMember(pocketMap, pocketKey, unit, radius, region);
			}

			EncounterExploration.BeginTracking(pocketMap);
			EncounterExploration.BeginPocketTriggers(BuildPocketTriggerRegions(file));
			int memberCount = 0;
			foreach (KeyValuePair<string, List<EncounterExploration.Member>> pocket in pocketMap)
			{
				memberCount += pocket.Value.Count;
			}

			LokrLabPlugin.Log.LogInfo(
				"EncounterRoster: exploration armed with " + pocketMap.Count + " pocket(s), " + memberCount + " parked unit(s).");
		}

		/// <summary>Resolves every catalog trigger with a <c>PocketKey</c> into a pocket → region map.</summary>
		private static Dictionary<string, List<HashSet<EncounterExplorationRules.HexPos>>> BuildPocketTriggerRegions(
			EncounterFileModel file)
		{
			Dictionary<string, List<HashSet<EncounterExplorationRules.HexPos>>> map =
				new Dictionary<string, List<HashSet<EncounterExplorationRules.HexPos>>>(StringComparer.Ordinal);
			if (file.Triggers == null)
			{
				return map;
			}

			for (int i = 0; i < file.Triggers.Count; i++)
			{
				EncounterTriggerModel trigger = file.Triggers[i];
				if (trigger == null || string.IsNullOrEmpty(trigger.PocketKey))
				{
					continue;
				}

				HashSet<EncounterExplorationRules.HexPos> region = ResolveTriggerRegion(file, trigger.Id);
				if (region == null)
				{
					continue;
				}

				List<HashSet<EncounterExplorationRules.HexPos>> regions;
				if (!map.TryGetValue(trigger.PocketKey, out regions))
				{
					regions = new List<HashSet<EncounterExplorationRules.HexPos>>();
					map[trigger.PocketKey] = regions;
				}

				regions.Add(region);
			}

			return map;
		}

		/// <summary>Converts a combatant's painted trigger region into cube hex positions, or null when unset or empty.</summary>
		private static HashSet<EncounterExplorationRules.HexPos> ResolveTriggerRegion(EncounterFileModel file, string triggerId)
		{
			if (string.IsNullOrEmpty(triggerId))
			{
				return null;
			}

			List<(int Col, int Row)> cells = EncounterTriggerRules.HexesFor(file, triggerId);
			if (cells.Count == 0)
			{
				return null;
			}

			HashSet<EncounterExplorationRules.HexPos> region =
				new HashSet<EncounterExplorationRules.HexPos>();
			for (int i = 0; i < cells.Count; i++)
			{
				HexCoord cube = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(cells[i].Col, cells[i].Row));
				region.Add(new EncounterExplorationRules.HexPos(cube.q, cube.r, cube.s));
			}

			return region;
		}

		/// <summary>Spawns one placed combatant onto the live Stage. Null when the definition or hex is missing.</summary>
		internal static Unit TrySpawnOne(Stage stage, EncounterFileModel file, EncounterCombatantModel combatant)
		{
			if (stage == null || file == null || combatant == null || stage.board == null
				|| !EncounterPlacementRules.HasPlacement(combatant))
			{
				return null;
			}

			if (string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
			{
				// Hero spawn points never spawn a unit on their own — see EncounterEdit.SpawnPreviewCombatant.
				return null;
			}

			string unitId = EncounterPlayRules.UnitId(combatant);
			UnitDefinition definition = SandboxRoster.ResolveDefinitionAtLevel(unitId, combatant.Level);
			if (definition == null && UnityDefinitionsParser.instance != null)
			{
				definition = UnityDefinitionsParser.instance.GetDefinition(unitId);
			}

			if (definition == null)
			{
				LokrLabPlugin.Log.LogWarning(
					"EncounterRoster: no unit definition for '" + unitId + "' (" + combatant.Id + ").");
				return null;
			}

			int col = combatant.Col.Value;
			int row = combatant.Row.Value;
			if (col < 0 || col >= stage.board.width || row < 0 || row >= stage.board.height)
			{
				return null;
			}

			HexGridItem<GameHexGridItemData> hex = stage.board.GetHexItem(new OffsetCoord(col, row));
			if (hex == null)
			{
				return null;
			}

			bool good = string.Equals(combatant.Side, EncounterFileModel.GoodSide, StringComparison.Ordinal);
			EncounterCombatantModel firstGood = EncounterPlayRules.FirstGoodSide(file);
			bool isHero = firstGood != null
				&& string.Equals(combatant.Id, firstGood.Id, StringComparison.Ordinal);
			Unit unit = SandboxRoster.SpawnAt(
				stage, definition, hex, good ? UnitGroup.GoodSide : UnitGroup.BadSide,
				combatant.Flipped, isHero, combatant.Level);
			if (unit == null)
			{
				return null;
			}

			unit.isAI = !good;
			if (lastSpawned == null)
			{
				lastSpawned = new Dictionary<string, Unit>(StringComparer.Ordinal);
			}

			if (!string.IsNullOrEmpty(combatant.Id))
			{
				lastSpawned[combatant.Id] = unit;
			}

			return unit;
		}

		/// <summary>Drops a combatant id from the last spawn map after a live despawn.</summary>
		internal static void Forget(string combatantId)
		{
			if (lastSpawned == null || string.IsNullOrEmpty(combatantId))
			{
				return;
			}

			lastSpawned.Remove(combatantId);
		}

		/// <summary>Writes authored facing onto spawned units after StartFight may LookAt them.</summary>
		internal static void ApplyAuthoredFacing(EncounterFileModel file)
		{
			if (file == null || file.Combatants == null || lastSpawned == null)
			{
				return;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = file.Combatants[i];
				Unit unit;
				if (combatant == null || string.IsNullOrEmpty(combatant.Id)
					|| !lastSpawned.TryGetValue(combatant.Id, out unit) || unit == null)
				{
					continue;
				}

				unit.isFlipped = combatant.Flipped;
				if (unit.unitView != null)
				{
					unit.unitView.UpdatePositionAndFlip();
				}
			}
		}

		private static bool CharacterFolderExists(string projectId)
		{
			if (string.IsNullOrEmpty(projectId))
			{
				return false;
			}

			return Directory.Exists(System.IO.Path.Combine(CharacterLabPaths.CharactersRoot, projectId));
		}

		private static HexGridItem<GameHexGridItemData> PlacedHex(
			HexBoard board,
			EncounterCombatantModel combatant,
			HashSet<string> occupied)
		{
			int col = combatant.Col.Value;
			int row = combatant.Row.Value;
			if (col < 0 || col >= board.width || row < 0 || row >= board.height)
			{
				return null;
			}

			string key = CoordKey(col, row);
			if (!occupied.Add(key))
			{
				return null;
			}

			return board.GetHexItem(new OffsetCoord(col, row));
		}

		private static HexGridItem<GameHexGridItemData> HeuristicHex(
			HexBoard board,
			bool good,
			int slot,
			HashSet<string> occupied)
		{
			int direction = good ? SandboxRoster.HeroDirection : SandboxRoster.EnemyDirection;
			int start = SandboxRoster.SpawnDistanceFromCenter + slot;
			for (int extra = 0; extra < 16; extra++)
			{
				HexGridItem<GameHexGridItemData> hex = SandboxRoster.TryOffsetFromCenter(
					board, direction, start + extra);
				if (TryClaim(hex, occupied))
				{
					return hex;
				}
			}

			HexGridItem<GameHexGridItemData> center = SandboxRoster.CenterHex(board);
			return TryClaim(center, occupied) ? center : null;
		}

		private static bool TryClaim(HexGridItem<GameHexGridItemData> hex, HashSet<string> occupied)
		{
			if (hex == null)
			{
				return false;
			}

			OffsetCoord offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, hex.coord);
			return occupied.Add(CoordKey(offset.col, offset.row));
		}

		private static string CoordKey(int col, int row)
		{
			return col.ToString(CultureInfo.InvariantCulture) + "," + row.ToString(CultureInfo.InvariantCulture);
		}
	}
}
