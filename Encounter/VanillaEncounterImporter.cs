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
		/// <summary>The exact Layout LevelManager.CreateLevelFromFile builds for a live single-room board -- same hex size, same origin (the negated pad displacement, offset back by one hex size; see LevelManager.cs:266-291). World positions (spawnPos) convert straight through this, with no further pad math -- the pad is already baked into the origin. Do not reuse this for boardMetadata's room-local CUBE coords (see LivePadCol/LivePadRow below); those are a separate, origin-independent conversion.</summary>
		private static readonly Layout LiveLayout = new Layout(Layout.pointy, new Point(0.55, -0.33275), new Point(-3.85, 0.6655));

		/// <summary>Half-extent of one hex in world X (Layout.size.x = 0.55 / cos(30 deg)), for padding a hex center out to its bounding box.</summary>
		private const float HexHalfExtentX = 0.635085f;

		/// <summary>Half-extent of one hex in world Y.</summary>
		private const float HexHalfExtentY = 0.33275f;

		/// <summary>boardMetadata's room-local cube-coord pad, added directly to the OffsetCoord (not the Layout origin) -- exact only because the row shift (2) is even, preserving odd-r parity. See the roadmap doc's Phase 1 notes for why this does not generalize.</summary>
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
			internal int PropsImported;
			internal int PropsWithChildrenFlattened;
			internal int PropsUnresolved;
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
			file.WalkableDefault = true;
			file.TilesDefault = true;
			file.Width = liveWidth;
			file.Height = liveHeight;

			HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
			ImportSpawns(definition.encounterData.spawnDataHeroes, true, liveWidth, liveHeight, file, usedIds, result);
			ImportSpawns(definition.encounterData.spawnDataEnemies, false, liveWidth, liveHeight, file, usedIds, result);
			result.CinematicDropped += definition.encounterData.spawnDataCinematicUnits != null
				? definition.encounterData.spawnDataCinematicUnits.Count
				: 0;
			ImportProps(definition.encounterData.encounterObjs, file, usedIds, result);

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

			ComputeCamera(bkg, liveWidth, liveHeight, file, result);

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

				HexCoord cube = FractionalHexCoord.HexRound(Layout.PixelToHex(LiveLayout, data.spawnPos));
				OffsetCoord live = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, cube);
				int col = live.col;
				int row = live.row;
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

		/// <summary>Imports EncounterData.encounterObjs (scenery -- barrels, statues, etc; a separate list from the spawn lists) as free-placed, unsnapped props. xPos/yPos are raw local-position data on TemplateObjectData, in the same room-local frame data.spawnPos uses for combatants, so they carry over directly with no hex conversion and no pad adjustment (a continuous world position doesn't need the pad's cell-index shift the way a hex hex Col/Row does). Nested TemplateObjectDataChild entries (a prop's own attached sub-decorations) are not imported -- they're addressed only by a GameObject reference, not a bundle name string, so there is no prefab name to write into EncounterPropModel.PrefabName; entries with children are flagged in the result instead of silently dropping the child detail.</summary>
		/// <remarks>
		/// TemplateObjectData.prefabName is not read anywhere in vanilla's own runtime
		/// (grepped -- only EncounterEntitiesGenerator.InstantiateObject uses
		/// prefabReference to spawn) so it is likely an editor-only, possibly-blank
		/// convenience field, not a reliable load key. prefabReference.name (the actual
		/// Unity object the game itself instantiates) is preferred whenever the reference
		/// resolved; the string field is only a fallback. Ensures the scenario bundle is
		/// loaded first so a cross-bundle prefabReference has a chance to resolve rather
		/// than reading null off a templates-only load.
		/// </remarks>
		private static void ImportProps(List<TemplateObjectData> objs, EncounterFileModel file, HashSet<string> usedIds, ImportResult result)
		{
			if (objs == null)
			{
				return;
			}

			EncounterPropCatalog.GetBundle();

			foreach (TemplateObjectData data in objs)
			{
				string prefabName = ResolvePropPrefabName(data);
				if (string.IsNullOrEmpty(prefabName))
				{
					result.PropsUnresolved++;
					continue;
				}

				string idStem = !string.IsNullOrEmpty(data.propId) ? data.propId : prefabName;
				string propId = EncounterFileModel.MintCombatantId(idStem, usedIds);
				usedIds.Add(propId);

				file.Props.Add(new EncounterPropModel
				{
					Id = propId,
					PrefabName = prefabName,
					Snap = false,
					X = data.xPos,
					Y = data.yPos,
					Flipped = data.flipX,
				});
				result.PropsImported++;

				if (data.children != null && data.children.Count > 0)
				{
					result.PropsWithChildrenFlattened++;
				}
			}

			if (result.PropsWithChildrenFlattened > 0)
			{
				result.Warnings.Add(result.PropsWithChildrenFlattened
					+ " prop(s) have attached sub-decorations (TemplateObjectDataChild) that were not imported -- only the parent prop was.");
			}

			if (result.PropsUnresolved > 0)
			{
				result.Warnings.Add(result.PropsUnresolved
					+ " prop(s) had neither a resolved prefabReference nor a prefabName and were skipped.");
			}
		}

		/// <summary>The name the game itself would instantiate (prefabReference.name) when resolved, else the possibly-blank editor-only prefabName string.</summary>
		private static string ResolvePropPrefabName(TemplateObjectData data)
		{
			if (data.prefabReference != null && !string.IsNullOrEmpty(data.prefabReference.name))
			{
				return data.prefabReference.name.Trim().ToLowerInvariant();
			}

			return (data.prefabName ?? string.Empty).Trim().ToLowerInvariant();
		}

		/// <summary>Sets file.Camera from the board prefab's authored art bounds, or a hex-extent estimate when no board art is found.</summary>
		private static void ComputeCamera(EncounterBkgDefinition bkg, int liveWidth, int liveHeight, EncounterFileModel file, ImportResult result)
		{
			float x0, y0, x1, y1;
			if (TryComputeCameraFromTiles(bkg, out x0, out y0, out x1, out y1))
			{
				file.Camera = EncounterCameraRules.FromCorners(x0, y0, x1, y1);
				return;
			}

			ComputeCameraFromBoardCorners(liveWidth, liveHeight, out x0, out y0, out x1, out y1);
			file.Camera = EncounterCameraRules.FromCorners(x0, y0, x1, y1);
			result.Warnings.Add(bkg != null
				? "No TileTestController found on the board prefab; camera bounds were estimated from the board's hex extents rather than the authored art bounds."
				: "No board prefab (boardMetadata) found; camera bounds were estimated from the board's hex extents rather than the authored art bounds.");
		}

		/// <summary>Replicates EncounterBkgDefinition.Initialize's backgroundLimits math (TileTestController width/height * Grid.cellSize, offset by the tile grid's local position) directly off the cold prefab, without instantiating it.</summary>
		private static bool TryComputeCameraFromTiles(EncounterBkgDefinition bkg, out float x0, out float y0, out float x1, out float y1)
		{
			x0 = y0 = x1 = y1 = 0f;
			if (bkg == null)
			{
				return false;
			}

			TileTestController tiles = bkg.GetComponentInChildren<TileTestController>();
			if (tiles == null)
			{
				return false;
			}

			Grid grid = tiles.GetComponentInChildren<Grid>();
			if (grid == null)
			{
				return false;
			}

			Vector2 size = new Vector2(tiles.width * grid.cellSize.x, tiles.height * grid.cellSize.y);
			Vector2 offset = new Vector2(size.x / 2f, -size.y / 2f) + (Vector2)tiles.transform.localPosition;
			Vector2 center = (Vector2)bkg.transform.position + offset;
			x0 = center.x - size.x / 2f;
			x1 = center.x + size.x / 2f;
			y0 = center.y - size.y / 2f;
			y1 = center.y + size.y / 2f;
			return true;
		}

		/// <summary>Fallback camera estimate: the live board's four hex corners run through LiveLayout, padded by one hex's half-extent so the outer ring isn't clipped.</summary>
		private static void ComputeCameraFromBoardCorners(int liveWidth, int liveHeight, out float x0, out float y0, out float x1, out float y1)
		{
			x0 = float.MaxValue;
			y0 = float.MaxValue;
			x1 = float.MinValue;
			y1 = float.MinValue;

			int[] cols = { 0, liveWidth - 1 };
			int[] rows = { 0, liveHeight - 1 };
			foreach (int col in cols)
			{
				foreach (int row in rows)
				{
					HexCoord cube = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(col, row));
					Point p = Layout.HexToPixel(LiveLayout, cube);
					float px = (float)p.x;
					float py = (float)p.y;
					x0 = Math.Min(x0, px - HexHalfExtentX);
					x1 = Math.Max(x1, px + HexHalfExtentX);
					y0 = Math.Min(y0, py - HexHalfExtentY);
					y1 = Math.Max(y1, py + HexHalfExtentY);
				}
			}
		}
	}
}
