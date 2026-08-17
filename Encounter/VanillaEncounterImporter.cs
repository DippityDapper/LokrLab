using System;
using System.Collections.Generic;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game.Units;
using LokrLab.Shell;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Imports a shipped combat-room prefab into a new Lab Encounter project -- Phase 1 of the Vanilla Encounter Edit track.</summary>
	/// <remarks>
	/// docs/roadmaps/started/vanilla-encounter-edit.md Phase 1. Read-only against the templates
	/// bundle prefab -- never instantiates it, never starts a fight embed, so this works from the
	/// Project Browser with nothing else open. Reuses EncounterTerrainCatalog.LoadPrefab (its
	/// EnsureTemplatesBundle guard against AssetBundleManager.LoadAsset's cache-clobbering bug
	/// applies here too) rather than loading the templates bundle a second, independent way.
	///
	/// The prefab's own EncounterDefinition/EncounterBkgDefinition selection fields
	/// (EncounterTemplate.selectedEncounterDef / selectedEncounterBkg) are never populated on a cold
	/// prefab -- both are only assigned by EncounterTemplate.InitializeEncounterTemplate, which
	/// requires a live LevelManager.CurrentRoom and randomly indexes both lists. This reads index 0
	/// of each list directly instead, logging a warning when either list has more than one entry.
	/// 609/610 shipped rooms have exactly one EncounterDefinition (a prior AssetStudio catalog pass,
	/// not re-verified here); EncounterBkgDefinition variant count is not confirmed by that same
	/// catalog and is the real remaining risk for a specific room's board import.
	///
	/// Hex conversion does not depend on a live Stage/HexBoard (none exists during a cold inspect) --
	/// it constructs the same Layout LevelManager.CreateLevelFromFile uses, with the same hardcoded
	/// hex-size constant, and applies the confirmed +4 col / +2 row live-board pad shortcut (exact
	/// only because the row shift is even, preserving odd-r parity -- does not generalize to an odd
	/// shift). Out-of-bounds results are dropped and counted, not silently clamped -- vanilla's own
	/// PointToHexItem convenience method clamps, which is exactly the failure mode Phase 1 wants to
	/// avoid (a spawn that lands off-board should be a loss-list entry).
	///
	/// CheckCanSpawn's four gating axes (quest-vs-editor, variant-chance, darkness band,
	/// quest-context) all read live MetagameManager state that isn't meaningful during a cold
	/// inspect, so the raw prefab read is a strict superset of what any one playthrough sees. This
	/// imports everything and flags gated entries in the result rather than trying to filter --
	/// filtering is impossible anyway (variant-chance is nondeterministic, quest-context needs live
	/// state).
	/// </remarks>
	internal static class VanillaEncounterImporter
	{
		private static readonly Layout HexLayout = new Layout(Layout.pointy, new Point(0.55, -0.33275), new Point(0.0, 0.0));

		private const int LivePadCol = 4;
		private const int LivePadRow = 2;

		/// <summary>What Phase 1's import actually produced, for a loss-list report.</summary>
		internal sealed class ImportResult
		{
			internal string Folder;
			internal int HeroesImported;
			internal int EnemiesImported;
			internal int CinematicDropped;
			internal int GatedFlagged;
			internal int OutOfBounds;
			internal int EncounterDefinitionVariantCount;
			internal int BkgDefinitionVariantCount;
			internal readonly List<string> Warnings = new List<string>();
		}

		/// <summary>Imports templateName into a newly minted Encounter Lab project. Returns null and sets error on failure.</summary>
		internal static ImportResult TryImport(string templateName, out string error)
		{
			error = null;
			string name = (templateName ?? string.Empty).Trim().ToLowerInvariant();
			if (string.IsNullOrEmpty(name))
			{
				error = "No template name given.";
				return null;
			}

			GameObject prefab = EncounterTerrainCatalog.LoadPrefab(name);
			if (prefab == null)
			{
				error = "'" + name + "' was not found in the templates bundle.";
				return null;
			}

			EncounterTemplate template = prefab.GetComponent<EncounterTemplate>();
			if (template == null || template.encounterDefinitions == null || template.encounterDefinitions.Count == 0)
			{
				error = "'" + name + "' has no EncounterTemplate / EncounterDefinition.";
				return null;
			}

			EncounterDefinition definition = template.encounterDefinitions[0];
			EncounterBkgDefinition bkg = template.encounterBkgDefinitions != null && template.encounterBkgDefinitions.Count > 0
				? template.encounterBkgDefinitions[0]
				: null;

			if (definition == null || definition.encounterData == null)
			{
				error = "'" + name + "' has no encounter data on its first EncounterDefinition.";
				return null;
			}

			ImportResult result = new ImportResult
			{
				EncounterDefinitionVariantCount = template.encounterDefinitions.Count,
				BkgDefinitionVariantCount = template.encounterBkgDefinitions != null ? template.encounterBkgDefinitions.Count : 0,
			};

			if (result.EncounterDefinitionVariantCount > 1)
			{
				result.Warnings.Add(name + " has " + result.EncounterDefinitionVariantCount
					+ " EncounterDefinition variants; imported variant 0 only.");
			}

			if (result.BkgDefinitionVariantCount > 1)
			{
				result.Warnings.Add(name + " has " + result.BkgDefinitionVariantCount
					+ " EncounterBkgDefinition (board) variants; imported variant 0 only.");
			}

			int authoredWidth = EncounterPlacementRules.DefaultAuthoredWidth;
			int authoredHeight = EncounterPlacementRules.DefaultAuthoredHeight;
			List<LevelEditorSave.LevelBoardSaveItem> boardState = null;
			if (bkg != null && bkg.boardMetadata != null)
			{
				authoredWidth = bkg.boardMetadata.hexWidth;
				authoredHeight = bkg.boardMetadata.hexHeight;
				boardState = bkg.boardMetadata.boardState;
			}
			else
			{
				result.Warnings.Add(name + " has no boardMetadata; board size defaults to "
					+ authoredWidth + "x" + authoredHeight + " and no impassable overrides were imported.");
			}

			EncounterPlacementRules.RegisterAuthoredSize(name, authoredWidth, authoredHeight);
			int liveWidth = authoredWidth + EncounterPlacementRules.PadWidth;
			int liveHeight = authoredHeight + EncounterPlacementRules.PadHeight;

			EncounterFileModel file = EncounterFileModel.CreateEmpty();
			file.Template = name;

			HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
			ImportSpawns(definition.encounterData.spawnDataHeroes, true, liveWidth, liveHeight, file, usedIds, result);
			ImportSpawns(definition.encounterData.spawnDataEnemies, false, liveWidth, liveHeight, file, usedIds, result);
			result.CinematicDropped = definition.encounterData.spawnDataCinematicUnits != null
				? definition.encounterData.spawnDataCinematicUnits.Count
				: 0;

			if (boardState != null)
			{
				foreach (LevelEditorSave.LevelBoardSaveItem item in boardState)
				{
					if (item == null || item.walkable)
					{
						continue;
					}

					OffsetCoord roomLocal = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, item.coord);
					int col = roomLocal.col + LivePadCol;
					int row = roomLocal.row + LivePadRow;
					if (col < 0 || col >= liveWidth || row < 0 || row >= liveHeight)
					{
						continue;
					}

					file.Overrides.Add(new EncounterHexOverride { Col = col, Row = row, Walkable = false });
				}
			}

			EncounterTerrainCatalog.EnsureHostTerrains(file);

			string slug = LokrLab.LabSlugIds.LegalizeSlug(name, "encounter");
			string id = EncounterLabPaths.GenerateNewId(slug);
			string folder = EncounterLabPaths.EncounterFolder(id);
			ProjectMarker.Write(folder, LokrLabApi.LokrLabApi.EncounterTypeId, name);
			if (!file.TryWrite(folder))
			{
				error = "Could not write the imported encounter to disk.";
				return null;
			}

			result.Folder = folder;
			return result;
		}

		private static void ImportSpawns(
			List<SpawnUnitData> spawns,
			bool isHeroList,
			int liveWidth,
			int liveHeight,
			EncounterFileModel file,
			HashSet<string> usedIds,
			ImportResult result)
		{
			if (spawns == null)
			{
				return;
			}

			foreach (SpawnUnitData data in spawns)
			{
				if (data == null)
				{
					continue;
				}

				bool cinematic = data.config != null && data.config.GetBool("isCinematic", null, false);
				if (cinematic)
				{
					result.CinematicDropped++;
					continue;
				}

				bool gated = data.config != null
					&& (!string.IsNullOrEmpty(data.config.GetConfig("notInQuest", null))
						|| !string.IsNullOrEmpty(data.config.GetConfig("variant-chance", null))
						|| !string.IsNullOrEmpty(data.config.GetConfig("variant-quest-context", null)));
				if (gated)
				{
					result.GatedFlagged++;
				}

				HexCoord cube = FractionalHexCoord.HexRound(Layout.PixelToHex(HexLayout, data.spawnPos));
				OffsetCoord roomLocal = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, cube);
				int col = roomLocal.col + LivePadCol;
				int row = roomLocal.row + LivePadRow;
				if (col < 0 || col >= liveWidth || row < 0 || row >= liveHeight)
				{
					result.OutOfBounds++;
					continue;
				}

				string idStem = isHeroList ? "hero" : (!string.IsNullOrEmpty(data.unitId) ? data.unitId : "unit");
				string combatantId = EncounterFileModel.MintCombatantId(idStem, usedIds);
				usedIds.Add(combatantId);

				EncounterCombatantModel combatant = new EncounterCombatantModel
				{
					Id = combatantId,
					Col = col,
					Row = row,
					Flipped = data.flipped,
				};

				if (isHeroList)
				{
					combatant.Side = EncounterFileModel.GoodSide;
					combatant.Source = EncounterFileModel.SourceSpawn;
					result.HeroesImported++;
				}
				else
				{
					UnitGroup group = data.config != null
						? data.config.GetEnum<UnitGroup>("unitGroup", null, UnitGroup.BadSide)
						: UnitGroup.BadSide;
					combatant.Side = group == UnitGroup.GoodSide ? EncounterFileModel.GoodSide : EncounterFileModel.BadSide;
					combatant.Source = EncounterFileModel.SourceUnit;
					combatant.UnitId = data.unitId ?? string.Empty;
					result.EnemiesImported++;
				}

				file.Combatants.Add(combatant);
			}
		}
	}
}
