using System;
using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Battlechest.Client.View;
using Ironhide.Battlechest.Common.Game.Gameboard;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Hud;
using LokrLab.Shell;
using LokrLabApi;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LokrLab.Encounter
{
	/// <summary>Setup board tool. Place writes combatant hexes or prop snap/free positions; Draw Hex paints walkability; Paint Terrain stamps floor tiles; Camera draws Play bounds.</summary>
	internal enum EncounterEditTool
	{
		/// <summary>Click-to-place a selected combatant or snap prop; free-move props follow the cursor.</summary>
		Place,

		/// <summary>Left-drag paints walkable hexes; right-drag blocks. Off-board left-drag grows the live rect.</summary>
		DrawHex,

		/// <summary>Left-drag paints floor terrain; right-drag restores the template. Does not grow the board.</summary>
		PaintTerrain,

		/// <summary>Left-drag paints the selected trigger region; right-drag erases it. Does not grow the board.</summary>
		Trigger,

		/// <summary>Drag a world-space Play camera rect. Does not paint hexes.</summary>
		Camera
	}

	/// <summary>Armed Encounter preview for the Setup board hole. Cleared on Hide or Stop.</summary>
	/// <remarks>
	/// Parallel to <see cref="EncounterSandbox"/>. Mutually exclusive so a 1v1 Sandbox stays 1v1.
	/// Do not grow <c>EmbeddedFightRequest</c>.
	/// </remarks>
	internal static class EncounterEdit
	{
		private static Dictionary<string, Unit> previewById;
		private static bool strokeActive;
		private static bool strokeDrawing;
		private static bool strokeErasing;
		private static int strokeCol;
		private static int strokeRow;
		private static int hoverCol = int.MinValue;
		private static int hoverRow = int.MinValue;
		private static GameObject hoverGhost;
		private static bool freePropDragActive;
		private static readonly Dictionary<string, GameObject> spawnMarkers = new Dictionary<string, GameObject>(StringComparer.Ordinal);

		/// <summary>True while Encounter Setup owns the next or current embed spawn.</summary>
		internal static bool IsArmed { get; private set; }

		/// <summary>Current Setup board tool. Default Place.</summary>
		internal static EncounterEditTool Tool { get; set; }

		/// <summary>Trigger id chosen from the Node Tree for the Trigger tool. Empty means none selected.</summary>
		internal static string SelectedTriggerId { get; set; } = string.Empty;

		/// <summary>When false, walk and occupied-hex overlays hide. The hover hex ghost stays.</summary>
		internal static bool GridVisible { get; set; } = true;

		/// <summary>Arena prefab name used when the host enqueues the ephemeral quest.</summary>
		internal static string Template { get; private set; }

		/// <summary>In-memory payload spawned as a preview before <c>StartFight</c>.</summary>
		internal static EncounterFileModel File { get; private set; }

		/// <summary>Marks the next embed as an Encounter Setup preview. Disarms the live Encounter fight.</summary>
		internal static void Arm(EncounterFileModel file)
		{
			EncounterSandbox.Disarm();
			File = file;
			Template = file != null && !string.IsNullOrEmpty(file.Template)
				? file.Template
				: EncounterFileModel.DefaultTemplate;
			IsArmed = true;
		}

		/// <summary>Clears Encounter edit mode so Play, Sandbox, and Stage stay 1v1.</summary>
		internal static void Disarm()
		{
			IsArmed = false;
			File = null;
			Template = null;
			previewById = null;
			ClearPointerState();
			ClearSpawnMarkers();
			EncounterTiles.Clear();
			EncounterProps.Clear();
			EncounterCameraGizmo.Dispose();
		}

		/// <summary>Stores spawned preview units so a later tap can move or select them.</summary>
		internal static void BindPreview(IDictionary<string, Unit> spawnedById)
		{
			if (spawnedById == null)
			{
				previewById = null;
				return;
			}

			previewById = new Dictionary<string, Unit>(spawnedById, StringComparer.Ordinal);
		}

		/// <summary>Stops turns, hides fight HUD, and paints every hex. Call after the preview roster exists.</summary>
		/// <remarks>
		/// Show board reuses the fight embed. Without this, AI can take a turn and
		/// <c>moveController.Calculate</c> paints a walk range instead of the full grid.
		/// </remarks>
		internal static void ApplyPreviewPresentation(Stage stage)
		{
			if (!IsArmed)
			{
				return;
			}

			ApplyWalkability(stage, File);
			EncounterTiles.ApplyAuthoredTiles(File);
			int terrainCount = File != null && File.Terrains != null ? File.Terrains.Count : 0;
			EncounterTerrainCatalog.EnsureHostTerrains(File);
			if (File != null && File.Terrains != null && File.Terrains.Count != terrainCount)
			{
				LokrLabApi.LokrLabApi.RequestRefresh();
			}

			ApplyAuthoredFacing();
			FreezeTurns(stage);
			HideFightHud();
			EncounterForeground.Hide();
			EncounterProps.Apply(File);
			ScheduleShowFullGrid();
			EncounterCameraGizmo.Refresh();
			RefreshSpawnMarkers();
			EncounterSetupViewport.NotifyBoardReady();
		}

		/// <summary>Shows a persistent marker at every placed hero spawn point (no unit spawns there in Setup preview).</summary>
		internal static void RefreshSpawnMarkers()
		{
			if (!IsArmed || File == null || File.Combatants == null)
			{
				ClearSpawnMarkers();
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			HashSet<string> keep = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < File.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = File.Combatants[i];
				if (combatant == null
					|| !string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal)
					|| !EncounterPlacementRules.HasPlacement(combatant))
				{
					continue;
				}

				keep.Add(combatant.Id);
				GameObject marker = EnsureSpawnMarker(combatant.Id);
				if (marker == null)
				{
					continue;
				}

				HexCoord cube = OffsetCoord.RoffsetToCube(
					OffsetCoord.ODD, new OffsetCoord(combatant.Col.Value, combatant.Row.Value));
				Point center = stage.board.HexToCenter(cube);
				marker.transform.position = new Vector3((float)center.x, (float)center.y, -0.1f);
				marker.SetActive(true);
			}

			List<string> stale = null;
			foreach (KeyValuePair<string, GameObject> pair in spawnMarkers)
			{
				if (keep.Contains(pair.Key))
				{
					continue;
				}

				if (stale == null)
				{
					stale = new List<string>();
				}

				stale.Add(pair.Key);
			}

			if (stale == null)
			{
				return;
			}

			for (int i = 0; i < stale.Count; i++)
			{
				GameObject marker;
				if (spawnMarkers.TryGetValue(stale[i], out marker) && marker != null)
				{
					UnityEngine.Object.Destroy(marker);
				}

				spawnMarkers.Remove(stale[i]);
			}
		}

		private static GameObject EnsureSpawnMarker(string id)
		{
			GameObject marker;
			if (spawnMarkers.TryGetValue(id, out marker) && marker != null)
			{
				return marker;
			}

			HexGridItemComponent sample = FindSampleHexView();
			if (sample == null || sample.gridItemAsset == null)
			{
				return null;
			}

			SpriteRenderer sampleRenderer = sample.GetComponentInChildren<SpriteRenderer>();
			Sprite sprite = sample.gridItemAsset.GetSpriteState(HexGridItemComponentState.SELECTED_TARGET);
			if (sprite == null && sampleRenderer != null)
			{
				sprite = sampleRenderer.sprite;
			}

			if (sprite == null)
			{
				return null;
			}

			marker = new GameObject("EncounterSpawnMarker:" + id);
			if (sample.transform.parent != null)
			{
				marker.transform.SetParent(sample.transform.parent, false);
			}

			SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			// Cyan-ish, distinct from the yellow hover ghost and the red trigger overlay.
			renderer.color = new Color(0.3f, 0.85f, 1f, 0.9f);
			if (sampleRenderer != null)
			{
				renderer.sortingLayerID = sampleRenderer.sortingLayerID;
				renderer.sortingOrder = sampleRenderer.sortingOrder + 8;
				marker.transform.localScale = sampleRenderer.transform.localScale;
			}

			spawnMarkers[id] = marker;
			return marker;
		}

		private static void ClearSpawnMarkers()
		{
			if (spawnMarkers.Count == 0)
			{
				return;
			}

			foreach (KeyValuePair<string, GameObject> pair in spawnMarkers)
			{
				if (pair.Value != null)
				{
					UnityEngine.Object.Destroy(pair.Value);
				}
			}

			spawnMarkers.Clear();
		}

		/// <summary>Writes authored facing onto the live preview unit and its view.</summary>
		internal static void ApplyFacing(EncounterCombatantModel combatant)
		{
			if (!IsArmed || combatant == null || string.IsNullOrEmpty(combatant.Id)
				|| previewById == null)
			{
				return;
			}

			Unit unit;
			if (!previewById.TryGetValue(combatant.Id, out unit) || unit == null)
			{
				return;
			}

			unit.isFlipped = combatant.Flipped;
			if (unit.unitView != null)
			{
				unit.unitView.UpdatePositionAndFlip();
			}
		}

		/// <summary>Re-applies every combatant's authored facing after spawn or StartFight LookAt.</summary>
		internal static void ApplyAuthoredFacing()
		{
			if (File == null || File.Combatants == null)
			{
				return;
			}

			for (int i = 0; i < File.Combatants.Count; i++)
			{
				ApplyFacing(File.Combatants[i]);
			}
		}

		/// <summary>Resizes the live HexBoard to the walkable extent, then stamps overrides.</summary>
		/// <remarks>
		/// Size comes from walkable overrides and placements, not stored width/height.
		/// Setup adds a one-hex impassable halo so Unblock can grow. New cells start
		/// blocked. Vanilla already <c>CreateBoard</c>'d the padded template; recreate
		/// with the same hex size and offset so (0,0) stays put.
		/// Call before <see cref="EncounterRoster.Spawn"/>.
		/// </remarks>
		internal static void ApplyAuthoredBoard(Stage stage, EncounterFileModel file)
		{
			if (stage == null || stage.board == null || file == null)
			{
				return;
			}

			int wantWidth;
			int wantHeight;
			if (IsArmed)
			{
				EncounterGrowRules.SetupLiveSize(file, out wantWidth, out wantHeight);
			}
			else
			{
				EncounterGrowRules.EffectiveLiveSize(file, out wantWidth, out wantHeight);
			}

			HexBoard old = stage.board;
			if (old.width != wantWidth || old.height != wantHeight)
			{
				int oldWidth = old.width;
				int oldHeight = old.height;
				bool[,] passable;
				bool[,] obstacle;
				SnapshotCells(old, out passable, out obstacle);
				stage.CreateBoard(old.size, old.offset, wantWidth, wantHeight);
				RestoreCells(stage.board, passable, obstacle);
				StampNewCellsBlocked(stage.board, oldWidth, oldHeight);
				RefreshBoardView(stage.board);
			}

			ApplyWalkability(stage, file);
		}

		/// <summary>Writes authored walkability onto live cells. Bounds-check before <c>GetHexItem</c>.</summary>
		/// <remarks>
		/// Call before <see cref="EncounterRoster.Spawn"/>. Do not use <c>ModifyHexPassable</c>.
		/// Preview snaps do not use <c>Unit.Move</c> — that call leaves <c>hasObstacle</c>
		/// hexes impassable and paints a leftover invalid overlay on the previous cell.
		/// </remarks>
		internal static void ApplyWalkability(Stage stage, EncounterFileModel file)
		{
			if (stage == null || stage.board == null || file == null)
			{
				return;
			}

			EncounterBoardRules.EnsurePlacementsWalkable(file);
			if (!file.WalkableDefault)
			{
				StampAllBlocked(stage.board);
			}

			if (file.Overrides == null)
			{
				StampOccupiedWalkable(stage, file);
				return;
			}

			for (int i = 0; i < file.Overrides.Count; i++)
			{
				EncounterHexOverride authored = file.Overrides[i];
				if (authored == null
					|| authored.Col < 0 || authored.Col >= stage.board.width
					|| authored.Row < 0 || authored.Row >= stage.board.height)
				{
					continue;
				}

				HexGridItem<GameHexGridItemData> cell = stage.board.GetHexItem(
					new OffsetCoord(authored.Col, authored.Row));
				if (cell != null && cell.data != null)
				{
					cell.data.isPassable = authored.Walkable;
					cell.data.hasObstacle = !authored.Walkable;
				}
			}

			StampOccupiedWalkable(stage, file);
		}

		/// <summary>Blocks input/AI and interrupts any activity already started by StartFight.</summary>
		internal static void FreezeTurns(Stage stage)
		{
			if (stage != null)
			{
				stage.CanHandleInputOrAI = false;
			}

			if (stage == null || stage.units == null)
			{
				return;
			}

			for (int i = 0; i < stage.units.Count; i++)
			{
				Unit unit = stage.units[i];
				if (unit == null)
				{
					continue;
				}

				unit.isAI = false;
				if (unit.IsStuffHappening)
				{
					unit.InterruptActivity();
				}
			}
		}

		/// <summary>Paints the walk overlay now and again for a few LateUpdates after vanilla SetBoard.</summary>
		/// <remarks>
		/// <c>HexBoardViewComponent.SetBoard</c> calls <c>SetStatus(false)</c> on every
		/// cell. Fight start can do that after the first paint, so overlays look
		/// missing until hover re-stamps a cell. LateUpdate retries catch that wipe.
		/// </remarks>
		internal static void ScheduleShowFullGrid()
		{
			ShowFullGrid();
			if (!MonoSingleton<HexBoardViewComponent>.IsInstanceValid)
			{
				return;
			}

			GameObject host = MonoSingleton<HexBoardViewComponent>.Instance.gameObject;
			GridPaintPump pump = host.GetComponent<GridPaintPump>();
			if (pump == null)
			{
				pump = host.AddComponent<GridPaintPump>();
			}

			pump.Arm(8);
		}

		/// <summary>Shows the walk overlay on passable hexes and on occupied cells. Empty blocked cells stay blank.</summary>
		/// <remarks>
		/// A new encounter starts fully blocked. The invalid overlay on every empty
		/// cell filled the hole. Hover still marks the current hex.
		/// </remarks>
		internal static void ShowFullGrid()
		{
			if (!IsArmed)
			{
				return;
			}

			if (!GridVisible)
			{
				HideGridOverlays();
				PaintSelectedHex();
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null || stage.board.HexGrid == null)
			{
				return;
			}

			HexGridItem<GameHexGridItemData>[] map = stage.board.HexGrid.mapData;
			if (map == null)
			{
				return;
			}

			for (int i = 0; i < map.Length; i++)
			{
				PaintGridCell(map[i]);
			}
		}

		/// <summary>Toggles walk overlays. The hover hex indicator stays.</summary>
		internal static void ToggleGrid()
		{
			GridVisible = !GridVisible;
			ShowFullGrid();
			if (hoverCol != int.MinValue)
			{
				PaintHoverVisual(new OffsetCoord(hoverCol, hoverRow));
			}
		}

		/// <summary>Turns off hex overlay sprites. Does not destroy views or the hover ghost.</summary>
		private static void HideGridOverlays()
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null || stage.board.HexGrid == null)
			{
				return;
			}

			HexGridItem<GameHexGridItemData>[] map = stage.board.HexGrid.mapData;
			if (map == null)
			{
				return;
			}

			for (int i = 0; i < map.Length; i++)
			{
				HexGridItem<GameHexGridItemData> hex = map[i];
				if (hex == null || hex.data == null || hex.data.view == null)
				{
					continue;
				}

				SetMouseOver(hex.data.view, false);
				hex.data.view.SetStatus(false);
			}
		}

		/// <summary>Hides initiative, skills, and End Turn so Setup does not look like a live fight.</summary>
		internal static void HideFightHud()
		{
			if (MonoSingleton<PowerBarsVisibility>.IsInstanceValid)
			{
				PowerBarsVisibility hud = MonoSingleton<PowerBarsVisibility>.Instance;
				hud.hidingHUD = true;
				hud.HideHud(waitless: true);
				if (hud.gameObject != null)
				{
					hud.gameObject.SetActive(false);
				}
			}

			if (InitiativeBar.initiativeInst != null)
			{
				InitiativeBar.initiativeInst.gameObject.SetActive(false);
			}

			if (SkillsBar.InstSkill != null)
			{
				SkillsBar.InstSkill.gameObject.SetActive(false);
			}

			EndTurn endTurn = UnityEngine.Object.FindObjectOfType<EndTurn>();
			if (endTurn != null)
			{
				endTurn.gameObject.SetActive(false);
			}
		}

		/// <summary>Hover highlight plus Draw Hex / Paint Terrain drag-paint. Call once per Setup Stage.Update.</summary>
		/// <remarks>
		/// Vanilla <c>OnFingerTap</c> is one-shot and <c>PointToHexItem</c> clamps off-board
		/// clicks to the edge. Tick uses <c>PointToHex</c> so a click five hexes below grows
		/// in one stroke, and left-drag paints every hex on the line.
		/// </remarks>
		internal static void TickPointer()
		{
			if (!IsArmed || File == null)
			{
				return;
			}

			Vector3 world;
			if (!TryHoleWorld(out world))
			{
				ClearHover();
				if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
				{
					strokeActive = false;
					if (Tool == EncounterEditTool.Camera)
					{
						EncounterCameraGizmo.Tick(Vector3.zero, false);
					}
				}

				EncounterCameraGizmo.Refresh();
				EndFreePropDrag();
				return;
			}

			OffsetCoord offset = WorldToOffset(world);
			bool left = Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);
			bool right = Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !Input.GetMouseButton(2);
			if (Tool == EncounterEditTool.Camera)
			{
				EncounterCameraGizmo.Tick(world, left);
				strokeActive = false;
				ClearHover();
				return;
			}

			if (Tool == EncounterEditTool.DrawHex)
			{
				if (left || right)
				{
					BeginHexStroke(left);
					PaintStroke(offset.col, offset.row, left);
				}
				else
				{
					strokeActive = false;
				}
			}
			else if (Tool == EncounterEditTool.PaintTerrain && left)
			{
				BeginTileStroke(false);
				PaintTileStroke(offset.col, offset.row);
			}
			else if (Tool == EncounterEditTool.PaintTerrain && right)
			{
				BeginTileStroke(true);
				EraseTileStroke(offset.col, offset.row);
			}
			else if (Tool == EncounterEditTool.Trigger && left)
			{
				BeginTileStroke(false);
				PaintTriggerStroke(offset.col, offset.row);
			}
			else if (Tool == EncounterEditTool.Trigger && right)
			{
				BeginTileStroke(true);
				EraseTriggerStroke(offset.col, offset.row);
			}
			else if (Tool == EncounterEditTool.Place)
			{
				strokeActive = false;
				TickFreePropDrag(world, left);
			}
			else
			{
				strokeActive = false;
				EndFreePropDrag();
			}

			UpdateHover(offset);
		}

		/// <summary>Selects the preview unit on this world point, or writes Place col/row.</summary>
		/// <remarks>
		/// Uses unclamped <c>PointToHex</c>. Paint tools are handled by <see cref="TickPointer"/>
		/// so a tap does not double-apply the first cell.
		/// </remarks>
		internal static bool TryHandleWorld(Vector3 world)
		{
			if (!IsArmed || File == null)
			{
				return false;
			}

			OffsetCoord offset = WorldToOffset(world);
			string occupiedId = OccupantIdAt(offset.col, offset.row);
			if (!string.IsNullOrEmpty(occupiedId))
			{
				SelectCombatant(occupiedId);
				if (Tool == EncounterEditTool.DrawHex)
				{
					LokrLab.Lab.SetStatus("Move the combatant off this hex first.");
				}

				return true;
			}

			if (Tool == EncounterEditTool.Place)
			{
				string propId = EncounterPropRules.OccupantIdAt(File, offset.col, offset.row);
				if (string.IsNullOrEmpty(propId))
				{
					propId = EncounterPropRules.NearestFreeId(File, world.x, world.y, PropPickRadiusSquared());
				}

				if (!string.IsNullOrEmpty(propId))
				{
					EncounterPropModel already = SelectedProp();
					if (already != null && !already.Snap
						&& string.Equals(propId, already.Id, StringComparison.Ordinal))
					{
						return TryPlaceFreeProp(already, world, live: false);
					}

					SelectProp(propId);
					return true;
				}
			}

			if (Tool == EncounterEditTool.DrawHex
				|| Tool == EncounterEditTool.PaintTerrain
				|| Tool == EncounterEditTool.Camera)
			{
				return true;
			}

			EncounterPropModel selectedProp = SelectedProp();
			if (selectedProp != null && !selectedProp.Snap)
			{
				return TryPlaceFreeProp(selectedProp, world, live: false);
			}

			int col;
			int row;
			if (!EncounterPaintRules.TryClampCell(offset.col, offset.row, out col, out row))
			{
				LokrLab.Lab.SetStatus("Board grows from (0,0). Paint toward +col / +row.");
				return true;
			}

			if (selectedProp != null)
			{
				return TryPlaceProp(selectedProp, col, row);
			}

			EncounterCombatantModel selected = SelectedCombatant();
			int width;
			int height;
			EncounterGrowRules.EffectiveLiveSize(File, out width, out height);
			if (col + 1 > width)
			{
				width = col + 1;
			}

			if (row + 1 > height)
			{
				height = row + 1;
			}

			bool passable = IsHexWalkable(col, row) || !IsOnLiveBoard(col, row);
			string error = EncounterEditRules.CanPlace(File, selected, col, row, width, height, passable);
			if (error != null)
			{
				LokrLab.Lab.SetStatus(error);
				return true;
			}

			if (selected.Col.HasValue && selected.Col.Value == col
				&& selected.Row.HasValue && selected.Row.Value == row)
			{
				return true;
			}

			AssignCombatantHex(selected, col, row);
			return true;
		}

		/// <summary>Maps a screen point to a live-board OffsetCoord when it is over the Setup hole.</summary>
		/// <remarks>
		/// Does not use EventSystem hover. A catalogue drag still has the card
		/// under the pointer on press; release over the hole must win.
		/// </remarks>
		internal static bool TryScreenToOffset(Vector2 screen, out OffsetCoord offset, out bool overHole)
		{
			offset = new OffsetCoord(0, 0);
			overHole = false;
			Camera camera = ResolveHoleCamera();
			if (camera == null || !camera.pixelRect.Contains(screen))
			{
				overHole = EncounterSetupViewport.HoleContainsScreen(screen);
				return false;
			}

			overHole = true;
			float depth = Mathf.Abs(camera.transform.position.z);
			if (depth < 0.01f)
			{
				depth = 10f;
			}

			Vector3 world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
			world.z = 0f;
			if (Stage.instance == null || Stage.instance.board == null)
			{
				return false;
			}

			offset = WorldToOffset(world);
			return true;
		}

		/// <summary>Error for dropping a new combatant on this hex, or null when legal.</summary>
		internal static string CanDropNewCombatant(int col, int row)
		{
			if (File == null)
			{
				return "Open an Encounter project.";
			}

			int width;
			int height;
			EncounterGrowRules.EffectiveLiveSize(File, out width, out height);
			if (col + 1 > width)
			{
				width = col + 1;
			}

			if (row + 1 > height)
			{
				height = row + 1;
			}

			bool passable = IsHexWalkable(col, row) || !IsOnLiveBoard(col, row);
			EncounterCombatantModel probe = new EncounterCombatantModel { Id = "__catalogue_drop__" };
			return EncounterEditRules.CanPlace(File, probe, col, row, width, height, passable);
		}

		/// <summary>Error for dropping a new prop on this hex, or null when legal.</summary>
		internal static string CanDropNewProp(int col, int row)
		{
			if (File == null)
			{
				return "Open an Encounter project.";
			}

			int width;
			int height;
			EncounterGrowRules.EffectiveLiveSize(File, out width, out height);
			EncounterPropModel probe = new EncounterPropModel { Id = "__catalogue_drop__" };
			return EncounterPropRules.CanPlace(File, probe, col, row, width, height);
		}

		/// <summary>Writes col/row, grows if needed, and moves or spawns the preview unit.</summary>
		/// <remarks>
		/// A catalogue drop must not restart the embed. Spawn the new unit onto
		/// the live Stage when the board is already showing.
		/// </remarks>
		internal static void AssignCombatantHex(EncounterCombatantModel combatant, int col, int row)
		{
			if (File == null || combatant == null)
			{
				return;
			}

			combatant.Col = col;
			combatant.Row = row;
			EncounterBoardRules.SetOverride(File, col, row, true);
			LabSaveUx.MarkDirty();
			Stage stage = Stage.instance;
			if (stage != null && stage.board != null)
			{
				ApplyAuthoredBoard(stage, File);
				ReseatPreviewUnits();
				Unit existing;
				if (previewById == null
					|| !previewById.TryGetValue(combatant.Id, out existing)
					|| existing == null)
				{
					SpawnPreviewCombatant(combatant);
				}

				ShowFullGrid();
			}

			UpdateHover(new OffsetCoord(col, row));
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		/// <summary>Removes a preview unit from the live Stage and destroys its view.</summary>
		/// <remarks>
		/// <c>Stage.RemoveUnit</c> does not destroy the view and can clear
		/// <c>isPassable</c> on the vacated cell. Re-stamp authored walkability
		/// and paint that hex or the walk overlay stays blank until hover.
		/// </remarks>
		internal static void RemovePreviewCombatant(string combatantId)
		{
			if (string.IsNullOrEmpty(combatantId))
			{
				return;
			}

			EncounterRoster.Forget(combatantId);
			Unit unit = null;
			if (previewById != null && previewById.TryGetValue(combatantId, out unit))
			{
				previewById.Remove(combatantId);
			}

			if (unit == null)
			{
				return;
			}

			OffsetCoord vacated;
			bool haveHex = TryUnitOffset(unit, out vacated);
			Stage stage = Stage.instance;
			if (stage != null)
			{
				stage.RemoveUnit(unit);
			}

			if (unit.unitView != null)
			{
				UnityEngine.Object.Destroy(unit.unitView.gameObject);
				unit.unitView = null;
			}

			if (haveHex && File != null && vacated.col >= 0 && vacated.row >= 0)
			{
				EncounterBoardRules.SetOverride(File, vacated.col, vacated.row, true);
			}

			if (stage != null && File != null)
			{
				ApplyWalkability(stage, File);
			}

			ShowFullGrid();
			if (haveHex && stage != null && stage.board != null && IsOnLiveBoard(vacated.col, vacated.row))
			{
				PaintGridCell(stage.board.GetHexItem(new OffsetCoord(vacated.col, vacated.row)));
			}
		}

		/// <summary>Spawns one preview unit for a newly placed combatant without reloading the embed.</summary>
		internal static void SpawnPreviewCombatant(EncounterCombatantModel combatant)
		{
			if (!IsArmed || combatant == null)
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null)
			{
				return;
			}

			if (string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
			{
				// Hero spawn points show only a position marker in Setup preview; no unit spawns there.
				RefreshSpawnMarkers();
				return;
			}

			Unit unit = EncounterRoster.TrySpawnOne(stage, File, combatant);
			if (unit == null)
			{
				return;
			}

			if (previewById == null)
			{
				previewById = new Dictionary<string, Unit>(StringComparer.Ordinal);
			}

			previewById[combatant.Id] = unit;
			ApplyFacing(combatant);
			FreezeTurns(stage);
		}

		/// <summary>Writes a prop hex when the drop is legal. Does not grow the board.</summary>
		internal static bool TryAssignPropHex(EncounterPropModel prop, int col, int row)
		{
			return TryPlaceProp(prop, col, row);
		}

		/// <summary>Selects the preview unit on this hex, paints walkability, or writes col/row.</summary>
		internal static bool TryHandleTap(HexGridItem<GameHexGridItemData> hex)
		{
			if (!IsArmed || hex == null || File == null)
			{
				return false;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return false;
			}

			Point center = stage.board.HexToCenter(hex.coord);
			return TryHandleWorld(new Vector3((float)center.x, (float)center.y, 0f));
		}

		/// <summary>Moves the preview unit to the authored hex when the board is showing.</summary>
		internal static void TryMovePreview(EncounterCombatantModel combatant)
		{
			if (!IsArmed || combatant == null || string.IsNullOrEmpty(combatant.Id)
				|| !EncounterPlacementRules.HasPlacement(combatant)
				|| previewById == null)
			{
				return;
			}

			Unit unit;
			if (!previewById.TryGetValue(combatant.Id, out unit) || unit == null)
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			int col = combatant.Col.Value;
			int row = combatant.Row.Value;
			if (col < 0 || col >= stage.board.width || row < 0 || row >= stage.board.height
				|| !IsHexWalkable(col, row))
			{
				return;
			}

			HexGridItem<GameHexGridItemData> hex = stage.board.GetHexItem(new OffsetCoord(col, row));
			if (hex == null)
			{
				return;
			}

			SnapPreviewUnit(unit, hex);
			ApplyFacing(combatant);
			ApplyWalkability(stage, File);
			ShowFullGrid();
		}

		/// <summary>Puts the preview unit on this hex without <c>Unit.Move</c> occupancy toggles.</summary>
		/// <remarks>
		/// <c>Unit.Move</c> sets the old cell <c>isPassable = !hasObstacle</c>. Setup stamps
		/// blocked cells as obstacles, so the cell they left stays invalid and the red
		/// overlay lags one hex behind. Assign <c>HexGridItem</c> from the live board instead.
		/// </remarks>
		private static void SnapPreviewUnit(Unit unit, HexGridItem<GameHexGridItemData> hex)
		{
			if (unit == null || hex == null || hex.data == null)
			{
				return;
			}

			Point center = Stage.instance.board.HexToCenter(hex.coord);
			unit.HexGridItem = hex;
			unit.position = new Vector2((float)center.x, (float)center.y);
			if (unit.unitView != null)
			{
				unit.unitView.UpdatePositionAndFlip();
			}
		}

		/// <summary>Starts or continues a Draw Hex stroke. Switching left/right ends the previous stroke.</summary>
		private static void BeginHexStroke(bool drawing)
		{
			if (strokeActive && strokeDrawing != drawing)
			{
				strokeActive = false;
			}

			strokeDrawing = drawing;
		}

		private static void PaintStroke(int col, int row, bool walkable)
		{
			if (!strokeActive)
			{
				PaintCells(col, row, col, row, walkable);
				strokeActive = true;
				strokeCol = col;
				strokeRow = row;
				return;
			}

			if (col == strokeCol && row == strokeRow)
			{
				return;
			}

			PaintCells(strokeCol, strokeRow, col, row, walkable);
			strokeCol = col;
			strokeRow = row;
		}

		private static void BeginTileStroke(bool erasing)
		{
			if (strokeActive && strokeErasing != erasing)
			{
				strokeActive = false;
			}

			strokeErasing = erasing;
		}

		private static void PaintTileStroke(int col, int row)
		{
			if (!strokeActive)
			{
				PaintTileLine(col, row, col, row);
				strokeActive = true;
				strokeCol = col;
				strokeRow = row;
				return;
			}

			if (col == strokeCol && row == strokeRow)
			{
				return;
			}

			PaintTileLine(strokeCol, strokeRow, col, row);
			strokeCol = col;
			strokeRow = row;
		}

		private static void PaintTileLine(int col0, int row0, int col1, int row1)
		{
			if (File == null)
			{
				return;
			}

			bool painted = false;
			bool offBoard = false;
			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!IsOnLiveBoard(col, row))
				{
					offBoard = true;
					return;
				}

				EncounterHexTile existing = EncounterTileRules.Find(File, col, row);
				if (EncounterTileRules.IsSameStamp(File, existing, EncounterTiles.SelectedTerrainId,
					EncounterTiles.SelectedSourceTemplate))
				{
					return;
				}

				if (EncounterTiles.TryPaint(File, col, row, EncounterTiles.SelectedTerrainId))
				{
					painted = true;
				}
			});
			if (!painted)
			{
				if (!EncounterTiles.HasTilemap())
				{
					LokrLab.Lab.SetStatus("This template has no floor tilemap.");
					return;
				}

				if (EncounterTiles.SelectedTerrainId == 0)
				{
					LokrLab.Lab.SetStatus("Select a terrain in the Node Tree, then left-drag.");
					return;
				}

				if (EncounterTiles.IsCustomSelected(File))
				{
					LokrLab.Lab.SetStatus("Custom terrain art is later. Import from a stage to paint.");
					return;
				}

				if (!EncounterTiles.HasHexSprites(File, EncounterTiles.SelectedTerrainId))
				{
					LokrLab.Lab.SetStatus("This terrain has no hex floor art. Import from a stage or pick a host terrain.");
					return;
				}

				if (offBoard)
				{
					LokrLab.Lab.SetStatus("Draw Hex (left-click) to grow the board, then paint terrain.");
				}

				return;
			}

			EncounterTiles.RefreshMap();
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
		}

		private static void EraseTileStroke(int col, int row)
		{
			if (!strokeActive)
			{
				EraseTileLine(col, row, col, row);
				strokeActive = true;
				strokeCol = col;
				strokeRow = row;
				return;
			}

			if (col == strokeCol && row == strokeRow)
			{
				return;
			}

			EraseTileLine(strokeCol, strokeRow, col, row);
			strokeCol = col;
			strokeRow = row;
		}

		private static void EraseTileLine(int col0, int row0, int col1, int row1)
		{
			if (File == null)
			{
				return;
			}

			bool erased = false;
			bool offBoard = false;
			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!IsOnLiveBoard(col, row))
				{
					offBoard = true;
					return;
				}

				if (EncounterTiles.TryErase(File, col, row))
				{
					erased = true;
				}
			});
			if (!erased)
			{
				if (!EncounterTiles.HasTilemap())
				{
					LokrLab.Lab.SetStatus("This template has no floor tilemap.");
					return;
				}

				if (offBoard)
				{
					LokrLab.Lab.SetStatus("Draw Hex (left-click) to grow the board, then paint terrain.");
				}

				return;
			}

			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
		}

		private static void PaintTriggerStroke(int col, int row)
		{
			if (!strokeActive)
			{
				PaintTriggerLine(col, row, col, row);
				strokeActive = true;
				strokeCol = col;
				strokeRow = row;
				return;
			}

			if (col == strokeCol && row == strokeRow)
			{
				return;
			}

			PaintTriggerLine(strokeCol, strokeRow, col, row);
			strokeCol = col;
			strokeRow = row;
		}

		private static void PaintTriggerLine(int col0, int row0, int col1, int row1)
		{
			if (File == null)
			{
				return;
			}

			if (string.IsNullOrEmpty(SelectedTriggerId))
			{
				LokrLab.Lab.SetStatus("Select or add a trigger in the Node Tree, then left-drag.");
				return;
			}

			bool painted = false;
			bool offBoard = false;
			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!IsOnLiveBoard(col, row))
				{
					offBoard = true;
					return;
				}

				if (EncounterTriggerRules.HasCell(File, col, row, SelectedTriggerId))
				{
					return;
				}

				EncounterTriggerRules.Set(File, col, row, SelectedTriggerId);
				painted = true;
			});
			if (!painted)
			{
				if (offBoard)
				{
					LokrLab.Lab.SetStatus("Draw Hex (left-click) to grow the board, then paint the trigger.");
				}

				return;
			}

			LabSaveUx.MarkDirty();
			ShowFullGrid();
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		private static void EraseTriggerStroke(int col, int row)
		{
			if (!strokeActive)
			{
				EraseTriggerLine(col, row, col, row);
				strokeActive = true;
				strokeCol = col;
				strokeRow = row;
				return;
			}

			if (col == strokeCol && row == strokeRow)
			{
				return;
			}

			EraseTriggerLine(strokeCol, strokeRow, col, row);
			strokeCol = col;
			strokeRow = row;
		}

		private static void EraseTriggerLine(int col0, int row0, int col1, int row1)
		{
			if (File == null)
			{
				return;
			}

			bool erased = false;
			bool offBoard = false;
			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!IsOnLiveBoard(col, row))
				{
					offBoard = true;
					return;
				}

				if (EncounterTriggerRules.Clear(File, col, row, SelectedTriggerId))
				{
					erased = true;
				}
			});
			if (!erased)
			{
				if (offBoard)
				{
					LokrLab.Lab.SetStatus("Draw Hex (left-click) to grow the board, then erase the trigger.");
				}

				return;
			}

			LabSaveUx.MarkDirty();
			ShowFullGrid();
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		private static void PaintCells(int col0, int row0, int col1, int row1, bool walkable)
		{
			bool painted = false;
			bool hitOccupant = false;
			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!walkable && !IsOnLiveBoard(col, row))
				{
					return;
				}

				if (!string.IsNullOrEmpty(OccupantIdAt(col, row)))
				{
					hitOccupant = true;
					return;
				}

				EncounterHexOverride existing = EncounterBoardRules.FindOverride(File, col, row);
				if (existing != null && existing.Walkable == walkable && IsOnLiveBoard(col, row)
					&& IsHexWalkable(col, row) == walkable)
				{
					return;
				}

				EncounterBoardRules.SetOverride(File, col, row, walkable);
				painted = true;
			});
			EncounterBoardRules.EnsurePlacementsWalkable(File);
			if (hitOccupant)
			{
				StampOccupiedWalkable(Stage.instance, File);
				LokrLab.Lab.SetStatus("Move the combatant off this hex first.");
			}

			if (!painted)
			{
				if (hitOccupant)
				{
					ShowFullGrid();
				}

				return;
			}

			LabSaveUx.MarkDirty();
			Stage stage = Stage.instance;
			int wantWidth;
			int wantHeight;
			EncounterGrowRules.SetupLiveSize(File, out wantWidth, out wantHeight);
			bool resize = stage != null && stage.board != null
				&& (stage.board.width != wantWidth || stage.board.height != wantHeight);
				if (resize)
				{
					ApplyAuthoredBoard(stage, File);
					EncounterTiles.ApplyAuthoredTiles(File);
					ReseatPreviewUnits();
					ScheduleShowFullGrid();
				}
			else
			{
				StampPaintedLine(col0, row0, col1, row1, walkable);
			}

			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		private static void StampPaintedLine(int col0, int row0, int col1, int row1, bool walkable)
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			EncounterPaintRules.ForEachOnLine(col0, row0, col1, row1, (col, row) =>
			{
				if (!IsOnLiveBoard(col, row))
				{
					return;
				}

				HexGridItem<GameHexGridItemData> hex = stage.board.GetHexItem(new OffsetCoord(col, row));
				if (hex == null || hex.data == null)
				{
					return;
				}

				hex.data.isPassable = walkable;
				hex.data.hasObstacle = !walkable;
				PaintGridCell(hex);
			});
		}

		private static OffsetCoord WorldToOffset(Vector3 world)
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return new OffsetCoord(0, 0);
			}

			HexCoord cube = stage.board.PointToHex(new Point(world.x, world.y));
			return OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, cube);
		}

		private static bool TryHoleWorld(out Vector3 world)
		{
			world = Vector3.zero;
			if (IsPointerOverLabUi())
			{
				return false;
			}

			Camera camera = ResolveHoleCamera();
			Vector2 screen = Input.mousePosition;
			if (camera == null || !camera.pixelRect.Contains(screen))
			{
				return false;
			}

			float depth = Mathf.Abs(camera.transform.position.z);
			if (depth < 0.01f)
			{
				depth = 10f;
			}

			world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
			world.z = 0f;
			return Stage.instance != null && Stage.instance.board != null;
		}

		private static bool IsPointerOverLabUi()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}

		private static Camera ResolveHoleCamera()
		{
			Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			return getter != null ? getter() : Camera.main;
		}

		private static void UpdateHover(OffsetCoord offset)
		{
			if (hoverCol == offset.col && hoverRow == offset.row)
			{
				PaintHoverVisual(offset);
				return;
			}

			RestoreHoverHex();
			hoverCol = offset.col;
			hoverRow = offset.row;
			PaintHoverVisual(offset);
			if (offset.col < 0 || offset.row < 0)
			{
				LokrLab.Lab.SetStatus("Hex (" + offset.col + ", " + offset.row
					+ ") — board grows from (0,0).");
				return;
			}

			string tool = Tool == EncounterEditTool.DrawHex
				? "Draw Hex"
				: Tool == EncounterEditTool.PaintTerrain
					? "Paint Terrain"
					: "Edit Unit";
			string extra = IsOnLiveBoard(offset.col, offset.row)
				? string.Empty
				: Tool == EncounterEditTool.PaintTerrain
					? " — Draw Hex (left-click) to grow, then paint"
					: " — grows the board";
			LokrLab.Lab.SetStatus(tool + " (" + offset.col + ", " + offset.row + ")" + extra);
		}

		private static void PaintHoverVisual(OffsetCoord offset)
		{
			if (GridVisible && IsOnLiveBoard(offset.col, offset.row))
			{
				SetGhostVisible(false);
				HexGridItem<GameHexGridItemData> hex = Stage.instance.board.GetHexItem(offset);
				if (hex != null && hex.data != null && hex.data.view != null)
				{
					hex.data.view.ForceState(HexGridItemComponentState.SELECTED_TARGET);
					hex.data.view.SetStatus(true);
					SetMouseOver(hex.data.view, true);
				}

				return;
			}

			ShowGhost(offset);
		}

		private static void RestoreHoverHex()
		{
			if (!IsOnLiveBoard(hoverCol, hoverRow))
			{
				return;
			}

			HexGridItem<GameHexGridItemData> hex = Stage.instance.board.GetHexItem(
				new OffsetCoord(hoverCol, hoverRow));
			if (hex == null || hex.data == null || hex.data.view == null)
			{
				return;
			}

			SetMouseOver(hex.data.view, false);
			PaintGridCell(hex);
		}

		/// <summary>Walk overlay on a passable or occupied hex when Grid is on; nothing on empty blocked cells.</summary>
		/// <remarks>
		/// The selected combatant or prop uses <c>ACTIVE_UNIT</c> (the blue current-turn
		/// hex) so the pick is visible on the board, including when Grid is off.
		/// </remarks>
		private static void PaintGridCell(HexGridItem<GameHexGridItemData> hex)
		{
			if (hex == null || hex.data == null || hex.data.view == null)
			{
				return;
			}

			if (IsSelectedHex(hex))
			{
				hex.data.view.ForceState(HexGridItemComponentState.ACTIVE_UNIT);
				hex.data.view.SetStatus(true);
				return;
			}

			if (Tool == EncounterEditTool.Trigger && !string.IsNullOrEmpty(SelectedTriggerId) && IsTriggerHex(hex))
			{
				hex.data.view.ForceState(HexGridItemComponentState.AREA_DAMAGE);
				hex.data.view.SetStatus(true);
				return;
			}

			if (!GridVisible || (!hex.data.isPassable && !IsOccupiedHex(hex)))
			{
				hex.data.view.SetStatus(false);
				return;
			}

			hex.data.view.ForceState(HexGridItemComponentState.SHORT_WALK);
			hex.data.view.SetStatus(true);
		}

		/// <summary>True when this hex belongs to <see cref="SelectedTriggerId"/>.</summary>
		private static bool IsTriggerHex(HexGridItem<GameHexGridItemData> hex)
		{
			if (hex == null || File == null)
			{
				return false;
			}

			OffsetCoord offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, hex.coord);
			return EncounterTriggerRules.HasCell(File, offset.col, offset.row, SelectedTriggerId);
		}

		/// <summary>Paints the blue current-turn overlay on the selected combatant or prop hex.</summary>
		internal static void RefreshSelectionOverlay()
		{
			if (!IsArmed)
			{
				return;
			}

			ShowFullGrid();
			if (hoverCol != int.MinValue)
			{
				PaintHoverVisual(new OffsetCoord(hoverCol, hoverRow));
			}
		}

		private static void PaintSelectedHex()
		{
			int col;
			int row;
			if (!TrySelectedOffset(out col, out row) || !IsOnLiveBoard(col, row))
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			PaintGridCell(stage.board.GetHexItem(new OffsetCoord(col, row)));
		}

		private static bool IsSelectedHex(HexGridItem<GameHexGridItemData> hex)
		{
			if (hex == null)
			{
				return false;
			}

			OffsetCoord offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, hex.coord);
			int col;
			int row;
			return TrySelectedOffset(out col, out row) && offset.col == col && offset.row == row;
		}

		private static bool TrySelectedOffset(out int col, out int row)
		{
			col = 0;
			row = 0;
			EncounterCombatantModel combatant = SelectedCombatant();
			if (combatant != null && EncounterPlacementRules.HasPlacement(combatant))
			{
				col = combatant.Col.Value;
				row = combatant.Row.Value;
				return true;
			}

			EncounterPropModel prop = SelectedProp();
			if (prop != null && EncounterPropRules.HasPlacement(prop))
			{
				if (!prop.Snap && prop.X.HasValue && prop.Y.HasValue)
				{
					OffsetCoord hint = WorldToOffset(new Vector3(prop.X.Value, prop.Y.Value, 0f));
					col = hint.col;
					row = hint.row;
					return true;
				}

				if (prop.Col.HasValue && prop.Row.HasValue)
				{
					col = prop.Col.Value;
					row = prop.Row.Value;
					return true;
				}
			}

			return false;
		}

		/// <summary>True when a preview combatant or live unit stands on this hex.</summary>
		private static bool IsOccupiedHex(HexGridItem<GameHexGridItemData> hex)
		{
			if (hex == null)
			{
				return false;
			}

			OffsetCoord offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, hex.coord);
			return !string.IsNullOrEmpty(OccupantIdAt(offset.col, offset.row));
		}

		private static void ShowGhost(OffsetCoord offset)
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				SetGhostVisible(false);
				return;
			}

			EnsureHoverGhost();
			if (hoverGhost == null)
			{
				return;
			}

			HexCoord cube = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, offset);
			Point center = stage.board.HexToCenter(cube);
			hoverGhost.transform.position = new Vector3((float)center.x, (float)center.y, -0.1f);
			SetGhostVisible(true);
		}

		private static void EnsureHoverGhost()
		{
			if (hoverGhost != null)
			{
				return;
			}

			HexGridItemComponent sample = FindSampleHexView();
			if (sample == null || sample.gridItemAsset == null)
			{
				return;
			}

			SpriteRenderer sampleRenderer = sample.GetComponentInChildren<SpriteRenderer>();
			Sprite sprite = sample.gridItemAsset.GetSpriteState(HexGridItemComponentState.SELECTED_TARGET);
			if (sprite == null && sampleRenderer != null)
			{
				sprite = sampleRenderer.sprite;
			}

			if (sprite == null)
			{
				return;
			}

			hoverGhost = new GameObject("EncounterHoverGhost");
			if (sample.transform.parent != null)
			{
				hoverGhost.transform.SetParent(sample.transform.parent, false);
			}

			SpriteRenderer renderer = hoverGhost.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.color = new Color(1f, 0.85f, 0.2f, 0.9f);
			if (sampleRenderer != null)
			{
				renderer.sortingLayerID = sampleRenderer.sortingLayerID;
				renderer.sortingOrder = sampleRenderer.sortingOrder + 8;
				hoverGhost.transform.localScale = sampleRenderer.transform.localScale;
			}
		}

		private static HexGridItemComponent FindSampleHexView()
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null || stage.board.HexGrid == null)
			{
				return null;
			}

			HexGridItem<GameHexGridItemData>[] map = stage.board.HexGrid.mapData;
			if (map == null)
			{
				return null;
			}

			for (int i = 0; i < map.Length; i++)
			{
				if (map[i] != null && map[i].data != null && map[i].data.view != null)
				{
					return map[i].data.view;
				}
			}

			return null;
		}

		private static void SetGhostVisible(bool visible)
		{
			if (hoverGhost != null)
			{
				hoverGhost.SetActive(visible);
			}
		}

		private static void SetMouseOver(HexGridItemComponent view, bool on)
		{
			if (view == null)
			{
				return;
			}

			SpriteRenderer mouseOver = Traverse.Create(view)
				.Field<SpriteRenderer>("mouseOverSpirteRenderer").Value;
			if (mouseOver != null)
			{
				mouseOver.enabled = on;
			}
		}

		private static void ClearHover()
		{
			RestoreHoverHex();
			hoverCol = int.MinValue;
			hoverRow = int.MinValue;
			SetGhostVisible(false);
		}

		private static void ClearPointerState()
		{
			strokeActive = false;
			freePropDragActive = false;
			ClearHover();
			if (hoverGhost != null)
			{
				UnityEngine.Object.Destroy(hoverGhost);
				hoverGhost = null;
			}
		}

		/// <summary>True when the live HexBoard contains this OffsetCoord.</summary>
		internal static bool IsOnLiveBoard(int col, int row)
		{
			Stage stage = Stage.instance;
			return stage != null && stage.board != null
				&& col >= 0 && row >= 0
				&& col < stage.board.width && row < stage.board.height;
		}

		/// <summary>Re-snaps preview units after a live board resize.</summary>
		private static void ReseatPreviewUnits()
		{
			if (File == null || File.Combatants == null)
			{
				return;
			}

			for (int i = 0; i < File.Combatants.Count; i++)
			{
				TryMovePreview(File.Combatants[i]);
			}
		}

		private static string OccupantIdAt(int col, int row)
		{
			if (EncounterBoardRules.HasPlacementAt(File, col, row))
			{
				for (int i = 0; i < File.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = File.Combatants[i];
					if (combatant != null && EncounterPlacementRules.HasPlacement(combatant)
						&& combatant.Col.Value == col && combatant.Row.Value == row)
					{
						return combatant.Id;
					}
				}
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.units == null)
			{
				return null;
			}

			for (int i = 0; i < stage.units.Count; i++)
			{
				Unit unit = stage.units[i];
				if (unit == null || IsTurnMarker(unit))
				{
					continue;
				}

				OffsetCoord at;
				if (!TryUnitOffset(unit, out at))
				{
					continue;
				}

				if (at.col == col && at.row == row)
				{
					return unit.ToString();
				}
			}

			return null;
		}

		private static bool TryUnitOffset(Unit unit, out OffsetCoord offset)
		{
			offset = new OffsetCoord(0, 0);
			if (unit == null)
			{
				return false;
			}

			if (unit.HexGridItem != null)
			{
				offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, unit.HexGridItem.coord);
				return true;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return false;
			}

			HexCoord cube = stage.board.PointToHex(new Point(unit.position.x, unit.position.y));
			offset = OffsetCoord.RoffsetFromCube(OffsetCoord.ODD, cube);
			return true;
		}

		private static bool IsTurnMarker(Unit unit)
		{
			string name = unit.ToString();
			return name != null && name.IndexOf("TurnMarker", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void StampOccupiedWalkable(Stage stage, EncounterFileModel file)
		{
			if (stage == null || stage.board == null)
			{
				return;
			}

			EncounterBoardRules.EnsurePlacementsWalkable(file);
			if (file != null && file.Combatants != null)
			{
				for (int i = 0; i < file.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = file.Combatants[i];
					if (combatant == null || !EncounterPlacementRules.HasPlacement(combatant))
					{
						continue;
					}

					StampCellWalkable(stage.board, combatant.Col.Value, combatant.Row.Value);
				}
			}

			if (stage.units == null)
			{
				return;
			}

			for (int i = 0; i < stage.units.Count; i++)
			{
				Unit unit = stage.units[i];
				OffsetCoord at;
				if (unit == null || IsTurnMarker(unit) || !TryUnitOffset(unit, out at))
				{
					continue;
				}

				StampCellWalkable(stage.board, at.col, at.row);
			}
		}

		private static void StampCellWalkable(HexBoard board, int col, int row)
		{
			if (board == null || col < 0 || row < 0 || col >= board.width || row >= board.height)
			{
				return;
			}

			HexGridItem<GameHexGridItemData> cell = board.GetHexItem(new OffsetCoord(col, row));
			if (cell != null && cell.data != null)
			{
				cell.data.isPassable = true;
				cell.data.hasObstacle = false;
			}
		}

		private static EncounterCombatantModel SelectedCombatant()
		{
			EditorSelection selection = LokrLabApi.LokrLabApi.Selection;
			LabNode primary = selection != null ? selection.Primary : null;
			return primary != null ? primary.Payload as EncounterCombatantModel : null;
		}

		private static EncounterPropModel SelectedProp()
		{
			EditorSelection selection = LokrLabApi.LokrLabApi.Selection;
			LabNode primary = selection != null ? selection.Primary : null;
			return primary != null ? primary.Payload as EncounterPropModel : null;
		}

		private static void SelectProp(string propId)
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (session == null || host == null || string.IsNullOrEmpty(propId))
			{
				return;
			}

			host.SelectNodeById(EncounterNodes.PropNodeId(session, propId));
		}

		/// <summary>Turns snap on or off, converting hex center and world x/y when the board is live.</summary>
		internal static bool TrySetPropSnap(EncounterPropModel prop, bool snap)
		{
			if (prop == null || prop.Snap == snap)
			{
				return true;
			}

			if (snap)
			{
				if (prop.X.HasValue && prop.Y.HasValue)
				{
					OffsetCoord offset = WorldToOffset(new Vector3(prop.X.Value, prop.Y.Value, 0f));
					int col;
					int row;
					if (!EncounterPaintRules.TryClampCell(offset.col, offset.row, out col, out row))
					{
						LokrLab.Lab.SetStatus("Unblock to grow the board, then snap this prop.");
						return false;
					}

					int width;
					int height;
					EncounterGrowRules.EffectiveLiveSize(File, out width, out height);
					string error = EncounterPropRules.CanPlace(File, prop, col, row, width, height);
					if (error != null)
					{
						LokrLab.Lab.SetStatus(error);
						return false;
					}

					prop.Col = col;
					prop.Row = row;
				}

				prop.X = null;
				prop.Y = null;
				prop.Snap = true;
				return true;
			}

			if (prop.Col.HasValue && prop.Row.HasValue)
			{
				Vector3 world;
				if (EncounterProps.TryHexWorld(prop.Col.Value, prop.Row.Value, out world))
				{
					prop.X = world.x;
					prop.Y = world.y;
				}
			}

			prop.Snap = false;
			return true;
		}

		private static void TickFreePropDrag(Vector3 world, bool left)
		{
			if (!left)
			{
				EndFreePropDrag();
				return;
			}

			EncounterPropModel selected = SelectedProp();
			if (selected == null || selected.Snap)
			{
				EndFreePropDrag();
				return;
			}

			if (!freePropDragActive)
			{
				string hit = HitSelectableId(world);
				if (!string.IsNullOrEmpty(hit) && !string.Equals(hit, selected.Id, StringComparison.Ordinal))
				{
					return;
				}

				freePropDragActive = true;
			}

			TryPlaceFreeProp(selected, world, live: true);
		}

		private static void EndFreePropDrag()
		{
			if (!freePropDragActive)
			{
				return;
			}

			freePropDragActive = false;
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		private static string HitSelectableId(Vector3 world)
		{
			OffsetCoord offset = WorldToOffset(world);
			string combatantId = OccupantIdAt(offset.col, offset.row);
			if (!string.IsNullOrEmpty(combatantId))
			{
				return combatantId;
			}

			string snapId = EncounterPropRules.OccupantIdAt(File, offset.col, offset.row);
			if (!string.IsNullOrEmpty(snapId))
			{
				return snapId;
			}

			return EncounterPropRules.NearestFreeId(File, world.x, world.y, PropPickRadiusSquared());
		}

		private static float PropPickRadiusSquared()
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return 0.56f;
			}

			HexCoord a = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(0, 0));
			HexCoord b = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(1, 0));
			Point centerA = stage.board.HexToCenter(a);
			Point centerB = stage.board.HexToCenter(b);
			float dx = (float)(centerB.x - centerA.x);
			float dy = (float)(centerB.y - centerA.y);
			float radius = Mathf.Sqrt((dx * dx) + (dy * dy)) * 0.45f;
			return radius * radius;
		}

		private static bool TryPlaceFreeProp(EncounterPropModel prop, Vector3 world, bool live)
		{
			if (prop == null)
			{
				return true;
			}

			if (prop.X.HasValue && prop.Y.HasValue
				&& Mathf.Abs(prop.X.Value - world.x) < 0.0001f
				&& Mathf.Abs(prop.Y.Value - world.y) < 0.0001f)
			{
				return true;
			}

			prop.Snap = false;
			prop.X = world.x;
			prop.Y = world.y;
			prop.Col = null;
			prop.Row = null;
			LabSaveUx.MarkDirty();
			EncounterProps.Ensure(prop);
			UpdateHover(WorldToOffset(world));
			if (!live)
			{
				EncounterSetupViewport.Refresh();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}

			return true;
		}

		private static bool TryPlaceProp(EncounterPropModel prop, int col, int row)
		{
			int width;
			int height;
			EncounterGrowRules.EffectiveLiveSize(File, out width, out height);
			string error = EncounterPropRules.CanPlace(File, prop, col, row, width, height);
			if (error != null)
			{
				LokrLab.Lab.SetStatus(error);
				return true;
			}

			if (prop.Snap
				&& prop.Col.HasValue && prop.Col.Value == col
				&& prop.Row.HasValue && prop.Row.Value == row)
			{
				return true;
			}

			prop.Snap = true;
			prop.Col = col;
			prop.Row = row;
			prop.X = null;
			prop.Y = null;
			LabSaveUx.MarkDirty();
			EncounterProps.Ensure(prop);
			UpdateHover(new OffsetCoord(col, row));
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
			return true;
		}

		private static void SelectCombatant(string combatantId)
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (session == null || host == null || string.IsNullOrEmpty(combatantId))
			{
				return;
			}

			host.SelectNodeById(EncounterNodes.CombatantNodeId(session, combatantId));
		}

		/// <summary>True when the live board cell is passable, or when no board is loaded.</summary>
		internal static bool IsHexWalkable(int col, int row)
		{
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return true;
			}

			if (col < 0 || col >= stage.board.width || row < 0 || row >= stage.board.height)
			{
				return false;
			}

			HexGridItem<GameHexGridItemData> hex = stage.board.GetHexItem(new OffsetCoord(col, row));
			return hex != null && hex.data != null && hex.data.isPassable;
		}

		private static void LiveBoardSize(out int width, out int height)
		{
			Stage stage = Stage.instance;
			if (stage != null && stage.board != null)
			{
				width = stage.board.width;
				height = stage.board.height;
				return;
			}

			if (File != null)
			{
				EncounterPlacementRules.LiveSize(File, out width, out height);
				return;
			}

			EncounterPlacementRules.LiveSize(Template, out width, out height);
		}

		private static void SnapshotCells(HexBoard board, out bool[,] passable, out bool[,] obstacle)
		{
			passable = new bool[board.width, board.height];
			obstacle = new bool[board.width, board.height];
			for (int row = 0; row < board.height; row++)
			{
				for (int col = 0; col < board.width; col++)
				{
					HexGridItem<GameHexGridItemData> cell = board.GetHexItem(new OffsetCoord(col, row));
					if (cell != null && cell.data != null)
					{
						passable[col, row] = cell.data.isPassable;
						obstacle[col, row] = cell.data.hasObstacle;
					}
					else
					{
						passable[col, row] = true;
					}
				}
			}
		}

		private static void StampAllBlocked(HexBoard board)
		{
			if (board == null || board.HexGrid == null || board.HexGrid.mapData == null)
			{
				return;
			}

			HexGridItem<GameHexGridItemData>[] map = board.HexGrid.mapData;
			for (int i = 0; i < map.Length; i++)
			{
				HexGridItem<GameHexGridItemData> cell = map[i];
				if (cell == null || cell.data == null)
				{
					continue;
				}

				cell.data.isPassable = false;
				cell.data.hasObstacle = true;
			}
		}

		private static void StampNewCellsBlocked(HexBoard board, int oldWidth, int oldHeight)
		{
			if (board == null)
			{
				return;
			}

			for (int row = 0; row < board.height; row++)
			{
				for (int col = 0; col < board.width; col++)
				{
					if (col < oldWidth && row < oldHeight)
					{
						continue;
					}

					HexGridItem<GameHexGridItemData> cell = board.GetHexItem(new OffsetCoord(col, row));
					if (cell == null || cell.data == null)
					{
						continue;
					}

					cell.data.isPassable = false;
					cell.data.hasObstacle = true;
				}
			}
		}

		private static void RestoreCells(HexBoard board, bool[,] passable, bool[,] obstacle)
		{
			if (board == null || passable == null)
			{
				return;
			}

			int width = Math.Min(board.width, passable.GetLength(0));
			int height = Math.Min(board.height, passable.GetLength(1));
			for (int row = 0; row < height; row++)
			{
				for (int col = 0; col < width; col++)
				{
					HexGridItem<GameHexGridItemData> cell = board.GetHexItem(new OffsetCoord(col, row));
					if (cell == null || cell.data == null)
					{
						continue;
					}

					cell.data.isPassable = passable[col, row];
					if (obstacle != null)
					{
						cell.data.hasObstacle = obstacle[col, row];
					}
				}
			}
		}

		private static void RefreshBoardView(HexBoard board)
		{
			if (!MonoSingleton<HexBoardViewComponent>.IsInstanceValid || board == null)
			{
				return;
			}

			HexBoardViewComponent view = MonoSingleton<HexBoardViewComponent>.Instance;
			view.SetBoard(board);
			List<HexGridItemComponent> pool = Traverse.Create(view)
				.Field<List<HexGridItemComponent>>("gridItemPool").Value;
			if (pool == null)
			{
				return;
			}

			int used = board.HexGrid != null && board.HexGrid.mapData != null
				? board.HexGrid.mapData.Length
				: 0;
			for (int i = 0; i < pool.Count; i++)
			{
				HexGridItemComponent item = pool[i];
				if (item == null)
				{
					continue;
				}

				if (i < used)
				{
					item.gameObject.SetActive(true);
					continue;
				}

				HidePooledHex(item);
			}
		}

		/// <summary>Turns off leftover pool sprites after shrink. SetStatus alone leaves the GameObject and MouseOver in the void.</summary>
		private static void HidePooledHex(HexGridItemComponent item)
		{
			item.SetStatus(false);
			item.SetStatusDebug(false);
			SetMouseOver(item, false);
			item.gameObject.SetActive(false);
		}

		/// <summary>Re-paints walk overlays after vanilla SetBoard can hide them.</summary>
		private sealed class GridPaintPump : MonoBehaviour
		{
			private int remaining;

			/// <summary>Runs LateUpdate this many more times.</summary>
			internal void Arm(int frames)
			{
				remaining = frames < 1 ? 1 : frames;
				enabled = true;
			}

			private void LateUpdate()
			{
				ShowFullGrid();
				remaining--;
				if (remaining <= 0)
				{
					enabled = false;
				}
			}
		}

	}
}
