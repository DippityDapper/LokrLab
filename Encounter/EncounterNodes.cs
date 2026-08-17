using System;
using System.Collections.Generic;
using System.IO;
using LokrLab.Editor;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Node kinds, tree contributor, and inspector drawers for an Encounter project.</summary>
	internal static class EncounterNodes
	{
		/// <summary>Root node for the open encounter.</summary>
		internal const string EncounterKind = "Encounter";

		/// <summary>Folder that holds combatant children.</summary>
		internal const string CombatantsKind = "EncounterCombatants";

		/// <summary>One authored combatant row.</summary>
		internal const string CombatantKind = "Combatant";

		/// <summary>Folder that holds terrain catalog children.</summary>
		internal const string TerrainsKind = "EncounterTerrains";

		/// <summary>One terrain catalog row.</summary>
		internal const string TerrainKind = "EncounterTerrain";

		/// <summary>Folder that holds placed scenario deco children.</summary>
		internal const string PropsKind = "EncounterProps";

		/// <summary>One authored prop instance.</summary>
		internal const string PropKind = "EncounterProp";

		/// <summary>Folder that holds decorative (non-combat) unit children.</summary>
		internal const string DecorationsKind = "EncounterDecorations";

		/// <summary>One authored decorative unit row.</summary>
		internal const string DecorationKind = "EncounterDecoration";

		/// <summary>Folder that holds painted trigger-region children.</summary>
		internal const string TriggersKind = "EncounterTriggers";

		/// <summary>One trigger-region id.</summary>
		internal const string TriggerKind = "EncounterTrigger";

		/// <summary>Folder that holds hero spawn point children.</summary>
		internal const string SpawnPointsKind = "EncounterSpawnPoints";

		/// <summary>One hero spawn point row.</summary>
		internal const string SpawnPointKind = "EncounterSpawnPoint";

		/// <summary>This encounter folder's aliases.json.</summary>
		internal const string AliasesKind = "EncounterAliases";

		/// <summary>Root, Combatants, Terrains, and Aliases.</summary>
		internal static IEnumerable<LabNode> Contribute(ProjectSession session)
		{
			EncounterSession encounter = session as EncounterSession;
			if (encounter == null || string.IsNullOrEmpty(encounter.FolderPath))
			{
				yield break;
			}

			LabNode root = new LabNode
			{
				Id = "encounter:" + encounter.Id,
				DisplayName = encounter.DisplayName ?? "Encounter",
				Kind = EncounterKind,
				IconKey = "Enc",
				Payload = encounter
			};
			LabNode folder = new LabNode
			{
				Id = "encounter-combatants:" + encounter.Id,
				DisplayName = "Combatants",
				Kind = CombatantsKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File != null && encounter.File.Combatants != null)
			{
				for (int i = 0; i < encounter.File.Combatants.Count; i++)
				{
					EncounterCombatantModel combatant = encounter.File.Combatants[i];
					if (combatant == null || string.IsNullOrEmpty(combatant.Id)
						|| string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
					{
						continue;
					}

					folder.Children.Add(new LabNode
					{
						Id = CombatantNodeId(encounter, combatant.Id),
						DisplayName = LabelFor(combatant),
						Kind = CombatantKind,
						IconKey = "Enc",
						Payload = combatant
					});
				}
			}

			root.Children.Add(folder);
			root.Children.Add(ContributeTerrains(encounter));
			root.Children.Add(ContributeProps(encounter));
			root.Children.Add(ContributeDecorations(encounter));
			root.Children.Add(ContributeTriggers(encounter));
			root.Children.Add(ContributeSpawnPoints(encounter));
			yield return root;
			yield return new LabNode
			{
				Id = "encounter-aliases:" + encounter.Id,
				DisplayName = "Aliases",
				Kind = AliasesKind,
				IconKey = "Abil",
				Payload = encounter.FolderPath
			};
		}

		/// <summary>Node Tree id for the Combatants folder.</summary>
		internal static string CombatantsFolderId(EncounterSession encounter)
		{
			return encounter == null ? null : "encounter-combatants:" + encounter.Id;
		}

		/// <summary>Node Tree id for the Props folder.</summary>
		internal static string PropsFolderId(EncounterSession encounter)
		{
			return encounter == null ? null : "encounter-props:" + encounter.Id;
		}

		/// <summary>Node Tree id for the Decorations folder.</summary>
		internal static string DecorationsFolderId(EncounterSession encounter)
		{
			return encounter == null ? null : "encounter-decorations:" + encounter.Id;
		}

		/// <summary>Node Tree id for the Triggers folder.</summary>
		internal static string TriggersFolderId(EncounterSession encounter)
		{
			return encounter == null ? null : "encounter-triggers:" + encounter.Id;
		}

		/// <summary>Node Tree id for one trigger row.</summary>
		internal static string TriggerNodeId(EncounterSession encounter, string triggerId)
		{
			return encounter == null ? null : "encounter-trigger:" + encounter.Id + ":" + triggerId;
		}

		/// <summary>Node Tree id for the Spawn Points folder.</summary>
		internal static string SpawnPointsFolderId(EncounterSession encounter)
		{
			return encounter == null ? null : "encounter-spawn-points:" + encounter.Id;
		}

		/// <summary>Node Tree id for one hero spawn point row.</summary>
		internal static string SpawnPointNodeId(EncounterSession encounter, string spawnPointId)
		{
			return encounter == null ? null : "encounter-spawn-point:" + encounter.Id + ":" + spawnPointId;
		}

		/// <summary>Selects the Triggers folder.</summary>
		internal static void FocusTriggersFolder(EncounterSession encounter)
		{
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null
				|| LokrLabApi.LokrLabApi.Host.SelectNodeById == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(TriggersFolderId(encounter));
		}

		/// <summary>Selects the Combatants folder so the visual catalogue is the picker.</summary>
		internal static void FocusCombatantsFolder(EncounterSession encounter)
		{
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null
				|| LokrLabApi.LokrLabApi.Host.SelectNodeById == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(CombatantsFolderId(encounter));
		}

		/// <summary>Selects the Props folder so the visual catalogue is the picker.</summary>
		internal static void FocusPropsFolder(EncounterSession encounter)
		{
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null
				|| LokrLabApi.LokrLabApi.Host.SelectNodeById == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(PropsFolderId(encounter));
		}

		/// <summary>Selects the Decorations folder.</summary>
		internal static void FocusDecorationsFolder(EncounterSession encounter)
		{
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null
				|| LokrLabApi.LokrLabApi.Host.SelectNodeById == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(DecorationsFolderId(encounter));
		}

		/// <summary>Selects the Combatants folder catalogue. The node appears after Add.</summary>
		internal static LabNode CreateCombatant(LabNode parent, ProjectSession session)
		{
			FocusCombatantsFolder(session as EncounterSession);
			return null;
		}

		/// <summary>Opens the Import Terrains sheet. The nodes appear after confirm.</summary>
		internal static LabNode CreateTerrain(LabNode parent, ProjectSession session)
		{
			EncounterImportTerrainModal.Show(session as EncounterSession);
			return null;
		}

		/// <summary>Selects the Props folder catalogue. The node appears after Add.</summary>
		internal static LabNode CreateProp(LabNode parent, ProjectSession session)
		{
			FocusPropsFolder(session as EncounterSession);
			return null;
		}

		/// <summary>Mints a blank decorative unit row and selects it. No catalogue -- type a unit id in the inspector.</summary>
		internal static LabNode CreateDecoration(LabNode parent, ProjectSession session)
		{
			EncounterSession encounter = session as EncounterSession;
			if (encounter == null || encounter.File == null)
			{
				return null;
			}

			EncounterDecorationModel decoration = EncounterDecorationRules.Add(encounter.File, string.Empty);
			if (decoration == null)
			{
				LokrLab.Lab.SetStatus("Could not add a decorative unit.");
				return null;
			}

			AfterDecorationsChanged(encounter, decoration.Id);
			LokrLab.Lab.SetStatus("New decoration '" + decoration.Id + "' -- set its unit id and hex below.");
			return null;
		}

		/// <summary>Mints a trigger id into the catalog, arms the Trigger tool with it, and selects the new node.</summary>
		internal static LabNode CreateTrigger(LabNode parent, ProjectSession session)
		{
			EncounterSession encounter = session as EncounterSession;
			if (encounter == null || encounter.File == null)
			{
				return null;
			}

			string id = EncounterTriggerRules.MintTriggerId(encounter.File);
			if (!EncounterTriggerRules.Add(encounter.File, id, string.Empty))
			{
				return null;
			}

			ArmTrigger(id);
			AfterTriggersChanged(encounter, id);
			LokrLab.Lab.SetStatus("New trigger '" + id + "' — left-drag on the board to paint it.");
			return null;
		}

		/// <summary>Selects this trigger for painting and switches Setup to the Trigger tool.</summary>
		private static void ArmTrigger(string id)
		{
			EncounterEdit.SelectedTriggerId = id;
			EncounterEdit.Tool = EncounterEditTool.Trigger;
			EncounterEdit.ShowFullGrid();
			EncounterSetupViewport.Refresh();
		}

		/// <summary>Dirties Save, refreshes the tree, and selects a trigger node.</summary>
		private static void AfterTriggersChanged(EncounterSession encounter, string selectTriggerId)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null || string.IsNullOrEmpty(selectTriggerId))
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(TriggerNodeId(encounter, selectTriggerId));
		}

		/// <summary>Mints a hero spawn point and selects its node. No unit spawns there in Setup preview.</summary>
		internal static LabNode CreateSpawnPoint(LabNode parent, ProjectSession session)
		{
			EncounterSession encounter = session as EncounterSession;
			if (encounter == null)
			{
				return null;
			}

			EncounterCombatantModel spawnPoint = encounter.TryAddSpawnPoint();
			if (spawnPoint == null)
			{
				LokrLab.Lab.SetStatus("Could not add a hero spawn point.");
				return null;
			}

			AfterSpawnPointsChanged(encounter, spawnPoint.Id);
			LokrLab.Lab.SetStatus("New hero spawn point '" + spawnPoint.Id + "' — set its hex below.");
			return null;
		}

		/// <summary>Dirties Save, refreshes the tree and board markers, and selects a spawn point node.</summary>
		/// <remarks>No unit spawns for a hero spawn point, so this never restarts the live preview.</remarks>
		internal static void AfterSpawnPointsChanged(EncounterSession encounter, string selectSpawnPointId)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			EncounterEdit.RefreshSpawnMarkers();
			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || string.IsNullOrEmpty(selectSpawnPointId) || LokrLabApi.LokrLabApi.Host == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(SpawnPointNodeId(encounter, selectSpawnPointId));
		}

		/// <summary>Remembers the terrain. Switches to Tile only when the pick is a new row.</summary>
		/// <remarks>
		/// Tile paint and erase refresh the tree; that re-selects the same node.
		/// Forcing Tile on every selection would steal Erase mid-stroke.
		/// </remarks>
		internal static void OnSelectionChanged(IReadOnlyList<LabNode> nodes)
		{
			EncounterEdit.RefreshSelectionOverlay();
			if (nodes == null || nodes.Count == 0 || nodes[0] == null || nodes[0].Kind != TerrainKind)
			{
				return;
			}

			EncounterTerrainModel terrain = nodes[0].Payload as EncounterTerrainModel;
			if (terrain == null || IsAlreadySelected(terrain))
			{
				return;
			}

			UseTerrain(terrain);
		}

		/// <summary>Double-click a terrain node to paint with it.</summary>
		internal static void OnNodeActivated(LabNode node)
		{
			if (node == null || node.Kind != TerrainKind)
			{
				return;
			}

			UseTerrain(node.Payload as EncounterTerrainModel);
		}

		/// <summary>Marks dirty, rebuilds the tree, and selects the new or remaining combatant.</summary>
		internal static void AfterCombatantsChanged(
			EncounterSession encounter,
			string selectCombatantId,
			bool restartPreview = true)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			if (restartPreview)
			{
				EncounterSetupViewport.RestartPreviewIfShowing();
			}

			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || string.IsNullOrEmpty(selectCombatantId) || LokrLabApi.LokrLabApi.Host == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(CombatantNodeId(encounter, selectCombatantId));
		}

		/// <summary>Marks dirty, rebuilds the tree, and selects the prop row.</summary>
		internal static void AfterPropsChanged(EncounterSession encounter, string selectPropId)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			if (EncounterEdit.IsArmed)
			{
				EncounterProps.Apply(encounter != null ? encounter.File : null);
			}

			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || string.IsNullOrEmpty(selectPropId) || LokrLabApi.LokrLabApi.Host == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(PropNodeId(encounter, selectPropId));
		}

		/// <summary>Marks dirty, rebuilds the tree, and selects the decoration row.</summary>
		internal static void AfterDecorationsChanged(EncounterSession encounter, string selectDecorationId)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			if (EncounterEdit.IsArmed)
			{
				EncounterDecorations.Apply(encounter != null ? encounter.File : null);
			}

			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || string.IsNullOrEmpty(selectDecorationId) || LokrLabApi.LokrLabApi.Host == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(DecorationNodeId(encounter, selectDecorationId));
		}

		/// <summary>Marks dirty, rebuilds the tree, and selects the terrain row.</summary>
		internal static void AfterTerrainsChanged(
			EncounterSession encounter,
			int selectTerrainId,
			string template)
		{
			LabSaveUx.MarkDirty();
			EncounterSetupViewport.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
			if (encounter == null || LokrLabApi.LokrLabApi.Host == null)
			{
				return;
			}

			LokrLabApi.LokrLabApi.Host.SelectNodeById(TerrainNodeId(encounter, selectTerrainId, template));
		}

		/// <summary>Encounter root: name, template, and combatant counts.</summary>
		internal static void DrawEncounter(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, encounter != null ? encounter.DisplayName : "Encounter",
				theme, theme.TitleFontSize).FixedHeight(26f));
			if (encounter != null)
			{
				LokrLab.LabClipboard.AddIdRow(section, encounter.Id);
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Display name", theme, 13).FixedHeight(20f));
			UiTextField nameField = UiTextField.Create(section.ContentTransform,
				encounter != null ? encounter.DisplayName : string.Empty, theme);
			section.Add(nameField.FixedHeight(28f));
			nameField.OnEndEdit(value =>
			{
				if (encounter == null)
				{
					return;
				}

				string name = (value ?? string.Empty).Trim();
				if (string.IsNullOrEmpty(name) || name == encounter.DisplayName)
				{
					return;
				}

				encounter.DisplayName = name;
				LabSaveUx.MarkDirty();
				LokrLabApi.LokrLabApi.RequestRefresh();
			});

			EncounterFileModel file = encounter != null ? encounter.File : null;
			DrawTemplateField(section, encounter, file, theme);
			DrawExplorationFields(section, encounter, file, theme);

			int good = file != null ? file.CountSide(EncounterFileModel.GoodSide) : 0;
			int bad = file != null ? file.CountSide(EncounterFileModel.BadSide) : 0;
			int total = file != null && file.Combatants != null ? file.Combatants.Count : 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " combatant" + (total == 1 ? "" : "s") + " (" + good + " GoodSide, " + bad + " BadSide).",
				theme, 13).FixedHeight(22f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Combatant",
				() => FocusCombatantsFolder(encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.combatants.Add");
			int props = file != null && file.Props != null ? file.Props.Count : 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				props + " prop" + (props == 1 ? "" : "s") + ".",
				theme, 13).FixedHeight(22f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Prop",
				() => FocusPropsFolder(encounter), theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.props.Add");
			DrawCameraFields(section, encounter, file, theme);
		}

		/// <summary>Exploration toggle and the file's default aggro radius (hexes).</summary>
		private static void DrawExplorationFields(
			UiStack section,
			EncounterSession encounter,
			EncounterFileModel file,
			UiTheme theme)
		{
			if (file == null)
			{
				return;
			}

			UiToggle exploration = UiToggle.Create(section.ContentTransform, "Exploration", file.Exploration, theme);
			exploration.OnValueChanged(on =>
			{
				if (on == file.Exploration)
				{
					return;
				}

				file.Exploration = on;
				LabSaveUx.MarkDirty();
				LokrLabApi.LokrLabApi.RequestRefresh();
			});
			section.Add(exploration.FixedHeight(28f));
			LabHoverInfo.Bind(exploration.GameObject, "encounter.exploration.Enabled");

			if (!file.Exploration)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform,
				"Default aggro radius (hexes; a combatant's own radius wins if set)",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(28f));
			UiTextField radiusField = UiTextField.Create(section.ContentTransform,
				file.DefaultAggroRadius.ToString(System.Globalization.CultureInfo.InvariantCulture), theme);
			radiusField.OnEndEdit(value =>
			{
				int parsed;
				if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out parsed) || parsed <= 0)
				{
					return;
				}

				file.DefaultAggroRadius = parsed;
				LabSaveUx.MarkDirty();
			});
			section.Add(radiusField.FixedHeight(28f));
			LabHoverInfo.Bind(radiusField.GameObject, "encounter.exploration.DefaultAggroRadius");
		}

		/// <summary>Play camera rect: current view, numeric edges, lock zoom, clear.</summary>
		private static void DrawCameraFields(
			UiStack section,
			EncounterSession encounter,
			EncounterFileModel file,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform, "Play camera", theme, 13).FixedHeight(20f));
			UiStack actions = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			actions.Add(UiButton.Create(actions.ContentTransform, "Use current view",
				() => CaptureCameraView(encounter, file), theme, primary: true).Grow());
			actions.Add(UiButton.Create(actions.ContentTransform, "Clear",
				() => ClearCamera(encounter, file), theme, primary: false).Grow());
			section.Add(actions.FixedHeight(28f));
			LabHoverInfo.Bind(actions.GameObject, "encounter.camera.View");
			if (file == null || !EncounterCameraRules.HasBounds(file.Camera))
			{
				section.Add(UiLabel.Create(section.ContentTransform,
					"No bounds yet. Show the board, frame the shot, then Use current view — or use the Camera tool.",
					theme, 11, TextAnchor.UpperLeft).FixedHeight(36f));
				return;
			}

			EncounterCameraModel camera = file.Camera;
			AddCameraFloat(section, theme, "min X", camera.MinX, value => camera.MinX = value, encounter);
			AddCameraFloat(section, theme, "min Y", camera.MinY, value => camera.MinY = value, encounter);
			AddCameraFloat(section, theme, "max X", camera.MaxX, value => camera.MaxX = value, encounter);
			AddCameraFloat(section, theme, "max Y", camera.MaxY, value => camera.MaxY = value, encounter);
			UiToggle lockZoom = UiToggle.Create(section.ContentTransform, "Lock zoom", camera.LockZoom, theme);
			lockZoom.OnValueChanged(value =>
			{
				camera.LockZoom = value;
				LabSaveUx.MarkDirty();
			});
			section.Add(lockZoom.FixedHeight(28f));
			LabHoverInfo.Bind(lockZoom.GameObject, "encounter.camera.LockZoom");
			string orthoText = camera.OrthoSize.HasValue
				? camera.OrthoSize.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
				: string.Empty;
			section.Add(UiLabel.Create(section.ContentTransform, "Ortho (empty = fit the rect)", theme, 11)
				.FixedHeight(18f));
			UiTextField orthoField = UiTextField.Create(section.ContentTransform, orthoText, theme);
			orthoField.OnEndEdit(value =>
			{
				float parsed;
				if (string.IsNullOrWhiteSpace(value))
				{
					camera.OrthoSize = null;
				}
				else if (float.TryParse(value, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed > 0.01f)
				{
					camera.OrthoSize = parsed;
				}
				else
				{
					return;
				}

				LabSaveUx.MarkDirty();
				EncounterCameraGizmo.Refresh();
			});
			section.Add(orthoField.FixedHeight(28f));
			LabHoverInfo.Bind(orthoField.GameObject, "encounter.camera.Ortho");
		}

		/// <summary>One numeric camera-edge field that normalizes and dirties Save.</summary>
		private static void AddCameraFloat(
			UiStack section,
			UiTheme theme,
			string label,
			float current,
			Action<float> assign,
			EncounterSession encounter)
		{
			section.Add(UiLabel.Create(section.ContentTransform, label, theme, 11).FixedHeight(16f));
			UiTextField field = UiTextField.Create(section.ContentTransform,
				current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), theme);
			field.OnEndEdit(value =>
			{
				float parsed;
				if (encounter == null || encounter.File == null || encounter.File.Camera == null
					|| !float.TryParse(value, System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out parsed))
				{
					return;
				}

				assign(parsed);
				EncounterCameraRules.Normalize(encounter.File.Camera);
				LabSaveUx.MarkDirty();
				EncounterCameraGizmo.Refresh();
				LokrLabApi.LokrLabApi.RequestRefresh();
			});
			section.Add(field.FixedHeight(26f));
		}

		/// <summary>Copies the Setup hole frustum into <c>camera</c>.</summary>
		private static void CaptureCameraView(EncounterSession encounter, EncounterFileModel file)
		{
			if (file == null)
			{
				return;
			}

			Camera hole = EncounterCamera.ResolveHoleCamera();
			if (!EncounterCamera.CaptureCurrentView(file, hole))
			{
				LokrLab.Lab.SetStatus("Show the board first, then Use current view.");
				return;
			}

			LabSaveUx.MarkDirty();
			EncounterCameraGizmo.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
			LokrLab.Lab.SetStatus("Play camera set from the current Setup view.");
		}

		/// <summary>Drops authored Play bounds so the embed stays unclamped.</summary>
		private static void ClearCamera(EncounterSession encounter, EncounterFileModel file)
		{
			if (file == null || file.Camera == null)
			{
				return;
			}

			file.Camera = null;
			LabSaveUx.MarkDirty();
			EncounterCameraGizmo.Refresh();
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		/// <summary>Empty-enough template picker, or a read-only name when only one host exists.</summary>
		private static void DrawTemplateField(
			UiStack section,
			EncounterSession encounter,
			EncounterFileModel file,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform, "Template", theme, 13).FixedHeight(20f));
			string current = file != null ? file.Template : EncounterFileModel.DefaultTemplate;
			if (EncounterTemplateRules.EmptyEnough.Length <= 1)
			{
				section.Add(UiLabel.Create(section.ContentTransform, EncounterTemplateRules.Label(current), theme, 13)
					.FixedHeight(22f));
				return;
			}

			string[] options = EncounterTemplateRules.Options(current);
			string[] labels = new string[options.Length];
			for (int i = 0; i < options.Length; i++)
			{
				labels[i] = EncounterTemplateRules.Label(options[i]);
			}

			UiDropdown templateField = UiDropdown.Create(section.ContentTransform, labels, theme);
			templateField.SetValueSilently(EncounterTemplateRules.IndexOf(options, current));
			templateField.OnValueChanged(index =>
			{
				if (file == null || index < 0 || index >= options.Length)
				{
					return;
				}

				ApplyTemplate(encounter, file, options[index]);
			});
			section.Add(templateField.FixedHeight(28f));
			LabHoverInfo.Bind(templateField.GameObject, "encounter.template");
		}

		/// <summary>Writes the template, clamps placement to the new live board, and dirties Save.</summary>
		private static void ApplyTemplate(EncounterSession encounter, EncounterFileModel file, string template)
		{
			string next = EncounterTemplateRules.Canonical(template);
			if (file == null || string.Equals(file.Template, next, StringComparison.Ordinal))
			{
				return;
			}

			file.Template = next;
			EncounterGrowRules.Normalize(file);
			EncounterGrowRules.ClampCombatants(file);
			EncounterTerrainCatalog.EnsureHostTerrains(file);

			LabSaveUx.MarkDirty();
			EncounterSetupViewport.NotifyTemplateChanged();
			if (encounter != null)
			{
				LokrLabApi.LokrLabApi.RequestRefresh();
			}
		}

		/// <summary>Terrains folder: import from another stage, plus a custom stub.</summary>
		internal static void DrawTerrains(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Terrains", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = encounter != null && encounter.File != null && encounter.File.Terrains != null
				? encounter.File.Terrains.Count
				: 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " terrain" + (total == 1 ? "" : "s")
				+ ". Select one, then Tile on the board. Custom rows have no floor art yet.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(48f));
			section.Add(UiButton.Create(section.ContentTransform, "Import from stage",
				() => CreateTerrain(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.terrains.Import");
			section.Add(UiButton.Create(section.ContentTransform, "Add custom terrain",
				() => AddCustomTerrain(encounter), theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.terrains.Custom");
		}

		/// <summary>One terrain: name, source, Use for Paint, remove for import/custom.</summary>
		internal static void DrawTerrain(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterTerrainModel terrain = node != null ? node.Payload as EncounterTerrainModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				terrain != null ? LabelFor(terrain) : "Terrain", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (terrain == null || encounter == null || encounter.File == null)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Name", theme, 13).FixedHeight(20f));
			UiTextField nameField = UiTextField.Create(section.ContentTransform, terrain.Name, theme);
			section.Add(nameField.FixedHeight(28f));
			nameField.OnEndEdit(value =>
			{
				string name = (value ?? string.Empty).Trim();
				if (string.IsNullOrEmpty(name) || name == terrain.Name)
				{
					return;
				}

				terrain.Name = name;
				AfterTerrainsChanged(encounter, terrain.TerrainId, terrain.Template);
			});
			LabHoverInfo.Bind(nameField.GameObject, "encounter.terrain.Name");

			section.Add(UiLabel.Create(section.ContentTransform,
				"Id " + terrain.TerrainId + " · " + EncounterTerrainRules.SourceLabel(terrain.Source)
				+ (string.IsNullOrEmpty(terrain.Template) ? "" : " · " + terrain.Template),
				theme, 12, TextAnchor.UpperLeft).FixedHeight(32f));
			LabHoverInfo.Bind(section.GameObject, "encounter.terrain.Source");

			section.Add(UiButton.Create(section.ContentTransform, "Use for Paint",
				() => UseTerrain(terrain), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.terrain.Use");

			if (!string.Equals(terrain.Source, EncounterFileModel.TerrainSourceTemplate, StringComparison.Ordinal))
			{
				section.Add(UiButton.Create(section.ContentTransform, "Remove Terrain", () =>
				{
					if (!EncounterTerrainRules.Remove(encounter.File, terrain.TerrainId, terrain.Template))
					{
						return;
					}

					LabSaveUx.MarkDirty();
					EncounterSetupViewport.Refresh();
					LokrLabApi.LokrLabApi.RequestRefresh();
				}, theme, primary: false).FixedHeight(28f));
				LabHoverInfo.Bind(section.GameObject, "encounter.terrain.Remove");
			}
		}

		/// <summary>Triggers folder: painted regions that wake a named pocket, or that a combatant opts into individually.</summary>
		internal static void DrawTriggers(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Triggers", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = encounter != null && encounter.File != null && encounter.File.Triggers != null
				? encounter.File.Triggers.Count
				: 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " trigger" + (total == 1 ? "" : "s")
				+ ". Set a trigger's Pocket to wake that whole pocket when a GoodSide unit steps onto a painted hex. A combatant can also opt into a trigger individually from its own Trigger field.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(68f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Trigger",
				() => CreateTrigger(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.triggers.Add");
		}

		/// <summary>One trigger: rename, target pocket, cell count, Paint, connected units, Remove.</summary>
		internal static void DrawTrigger(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterTriggerModel trigger = node != null ? node.Payload as EncounterTriggerModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				trigger != null ? trigger.Id : "Trigger", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (trigger == null || encounter == null || encounter.File == null)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Id (rename)", theme, 13).FixedHeight(20f));
			UiTextField idField = UiTextField.Create(section.ContentTransform, trigger.Id, theme);
			idField.OnEndEdit(value =>
			{
				string next = (value ?? string.Empty).Trim();
				if (next == trigger.Id)
				{
					return;
				}

				string oldId = trigger.Id;
				if (!EncounterTriggerRules.Rename(encounter.File, oldId, next))
				{
					LokrLab.Lab.SetStatus("Trigger id must be a legal, unused slug.");
					return;
				}

				if (string.Equals(EncounterEdit.SelectedTriggerId, oldId, StringComparison.Ordinal))
				{
					EncounterEdit.SelectedTriggerId = next;
				}

				AfterTriggersChanged(encounter, next);
			});
			section.Add(idField.FixedHeight(28f));
			LabHoverInfo.Bind(idField.GameObject, "encounter.trigger.Rename");

			section.Add(UiLabel.Create(section.ContentTransform,
				"Pocket this trigger wakes (blank = none — only individual opt-ins wake)", theme, 13, TextAnchor.UpperLeft)
				.FixedHeight(32f));
			UiTextField pocketField = UiTextField.Create(section.ContentTransform, trigger.PocketKey ?? string.Empty, theme);
			pocketField.OnEndEdit(value =>
			{
				string next = (value ?? string.Empty).Trim();
				if (next == trigger.PocketKey)
				{
					return;
				}

				trigger.PocketKey = next;
				AfterTriggersChanged(encounter, trigger.Id);
			});
			section.Add(pocketField.FixedHeight(28f));
			LabHoverInfo.Bind(pocketField.GameObject, "encounter.trigger.Pocket");

			int cells = EncounterTriggerRules.HexesFor(encounter.File, trigger.Id).Count;
			section.Add(UiLabel.Create(section.ContentTransform,
				cells + " hex" + (cells == 1 ? "" : "es") + " painted.", theme, 13).FixedHeight(20f));

			bool selected = string.Equals(EncounterEdit.SelectedTriggerId, trigger.Id, StringComparison.Ordinal)
				&& EncounterEdit.Tool == EncounterEditTool.Trigger;
			section.Add(UiButton.Create(section.ContentTransform, selected ? "Painting" : "Paint", () =>
			{
				ArmTrigger(trigger.Id);
				LokrLab.Lab.SetStatus("Trigger: " + trigger.Id + ".");
			}, theme, primary: !selected).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.trigger.Paint");

			List<EncounterCombatantModel> pocketMembers = EncounterTriggerRules.PocketMembers(encounter.File, trigger);
			List<EncounterCombatantModel> individual = EncounterTriggerRules.CombatantsUsing(encounter.File, trigger.Id);
			section.Add(UiLabel.Create(section.ContentTransform,
				string.IsNullOrEmpty(trigger.PocketKey)
					? "Pocket: none set."
					: "Pocket '" + trigger.PocketKey + "': " + pocketMembers.Count + " member"
						+ (pocketMembers.Count == 1 ? "" : "s") + (pocketMembers.Count == 0 ? " (no combatants use this pocket yet)." : "."),
				theme, 12, TextAnchor.UpperLeft).FixedHeight(32f));
			for (int i = 0; i < pocketMembers.Count; i++)
			{
				section.Add(UiLabel.Create(section.ContentTransform, "  · " + LabelFor(pocketMembers[i]), theme, 11)
					.FixedHeight(16f));
			}

			section.Add(UiLabel.Create(section.ContentTransform,
				"Individually opted in: " + individual.Count + (individual.Count == 0 ? " (none)." : "."),
				theme, 12, TextAnchor.UpperLeft).FixedHeight(20f));
			for (int i = 0; i < individual.Count; i++)
			{
				section.Add(UiLabel.Create(section.ContentTransform, "  · " + LabelFor(individual[i]), theme, 11)
					.FixedHeight(16f));
			}

			section.Add(UiButton.Create(section.ContentTransform, "Remove Trigger", () =>
			{
				string removedId = trigger.Id;
				EncounterTriggerRules.RemoveDefinition(encounter.File, removedId);
				if (string.Equals(EncounterEdit.SelectedTriggerId, removedId, StringComparison.Ordinal))
				{
					EncounterEdit.SelectedTriggerId = string.Empty;
					EncounterEdit.ShowFullGrid();
				}

				LabSaveUx.MarkDirty();
				EncounterSetupViewport.Refresh();
				LokrLabApi.LokrLabApi.RequestRefresh();
				LokrLab.Lab.SetStatus("Removed trigger '" + removedId + "'.");
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.trigger.Remove");
		}

		/// <summary>Spawn Points folder: count, explanation, Add.</summary>
		internal static void DrawSpawnPoints(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Spawn Points", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = CountSpawnPoints(encounter);
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " hero spawn point" + (total == 1 ? "" : "s")
				+ ". A spawn point is just a hex position, always GoodSide, with no fixed hero — whoever loads"
				+ " this encounter (Sandbox, later Adventures) spawns a character there at Play time.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(64f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Spawn Point",
				() => CreateSpawnPoint(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.spawnpoints.Add");
		}

		/// <summary>One hero spawn point: hex, facing, remove. No source/side/level fields — always a bare GoodSide slot.</summary>
		internal static void DrawSpawnPoint(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterCombatantModel combatant = node != null ? node.Payload as EncounterCombatantModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				combatant != null ? LabelFor(combatant) : "Spawn Point", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (combatant != null)
			{
				LokrLab.LabClipboard.AddIdRow(section, combatant.Id);
			}

			if (combatant == null || encounter == null)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform,
				"No fixed hero — whoever loads this encounter (Sandbox, later Adventures) spawns their character here. Always GoodSide.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(44f));

			DrawPlacementFields(section, encounter, combatant, theme);

			section.Add(UiButton.Create(section.ContentTransform, "Remove Spawn Point", () =>
			{
				if (!encounter.TryRemove(combatant.Id))
				{
					return;
				}

				EncounterEdit.RemovePreviewCombatant(combatant.Id);
				LabSaveUx.MarkDirty();
				EncounterSetupViewport.Refresh();
				EncounterEdit.RefreshSpawnMarkers();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.spawnpoint.Remove");
		}

		private static int CountSpawnPoints(EncounterSession encounter)
		{
			if (encounter == null || encounter.File == null || encounter.File.Combatants == null)
			{
				return 0;
			}

			int total = 0;
			for (int i = 0; i < encounter.File.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = encounter.File.Combatants[i];
				if (combatant != null
					&& string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
				{
					total++;
				}
			}

			return total;
		}

		/// <summary>Fallback Props folder drawer. The persistent catalogue host usually wins.</summary>
		internal static void DrawProps(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Props", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = encounter != null && encounter.File != null && encounter.File.Props != null
				? encounter.File.Props.Count
				: 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " prop" + (total == 1 ? "" : "s")
				+ ". Select one, then Place. Snap sits on a hex; free-move follows the cursor. Props do not block walk.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(48f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Prop",
				() => CreateProp(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.props.Add");
		}

		/// <summary>One prop: prefab, snap or free place, facing, remove.</summary>
		internal static void DrawProp(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterPropModel prop = node != null ? node.Payload as EncounterPropModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				prop != null ? LabelFor(prop) : "Prop", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (prop != null)
			{
				LokrLab.LabClipboard.AddIdRow(section, prop.Id);
			}

			if (prop == null || encounter == null || encounter.File == null)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Prefab", theme, 13).FixedHeight(20f));
			section.Add(UiLabel.Create(section.ContentTransform, prop.PrefabName, theme, 12)
				.FixedHeight(22f));
			LabHoverInfo.Bind(section.GameObject, "encounter.prop.Prefab");

			UiToggle snap = UiToggle.Create(section.ContentTransform, "Snap to grid", prop.Snap, theme);
			snap.OnValueChanged(value =>
			{
				if (value == prop.Snap)
				{
					return;
				}

				if (!EncounterEdit.TrySetPropSnap(prop, value))
				{
					LokrLabApi.LokrLabApi.RequestRefresh();
					return;
				}

				AfterPropsChanged(encounter, prop.Id);
			});
			section.Add(snap.FixedHeight(28f));
			LabHoverInfo.Bind(snap.GameObject, "encounter.prop.Snap");

			if (prop.Snap)
			{
				DrawPropPlacementFields(section, encounter, prop, theme);
			}
			else
			{
				DrawPropWorldFields(section, encounter, prop, theme);
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Facing", theme, 13).FixedHeight(20f));
			UiToggle flip = UiToggle.Create(section.ContentTransform, "Flipped", prop.Flipped, theme);
			flip.OnValueChanged(value =>
			{
				if (value == prop.Flipped)
				{
					return;
				}

				prop.Flipped = value;
				AfterPropsChanged(encounter, prop.Id);
			});
			section.Add(flip.FixedHeight(28f));
			LabHoverInfo.Bind(flip.GameObject, "encounter.prop.Flipped");

			section.Add(UiButton.Create(section.ContentTransform, "Clear placement", () =>
			{
				EncounterPropRules.ClearPlacement(prop);
				AfterPropsChanged(encounter, prop.Id);
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.prop.Clear");

			section.Add(UiButton.Create(section.ContentTransform, "Remove Prop", () =>
			{
				if (!EncounterPropRules.Remove(encounter.File, prop.Id))
				{
					return;
				}

				AfterPropsChanged(encounter, null);
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.prop.Remove");
		}

		/// <summary>Decorations folder: count, Add.</summary>
		internal static void DrawDecorations(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Decorations", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = encounter != null && encounter.File != null && encounter.File.Decorations != null
				? encounter.File.Decorations.Count
				: 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " decoration" + (total == 1 ? "" : "s")
				+ ". Ambient units with a rig and idle animation that never join the fight -- villagers, farmers, and the like. Not a Combatant (no side, no initiative) and not a Prop (real unit, not a static mesh).",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(64f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Decorative Unit",
				() => CreateDecoration(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.decorations.Add");
		}

		/// <summary>One decorative unit: unit id, hex, facing, remove.</summary>
		internal static void DrawDecoration(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterDecorationModel decoration = node != null ? node.Payload as EncounterDecorationModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				decoration != null ? LabelFor(decoration) : "Decoration", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (decoration != null)
			{
				LokrLab.LabClipboard.AddIdRow(section, decoration.Id);
			}

			if (decoration == null || encounter == null || encounter.File == null)
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Unit id", theme, 13).FixedHeight(20f));
			UiComboBox unitField = UiComboBox.Create(section.ContentTransform, AbilityCatalogLookupsSafe(),
				decoration.UnitId, theme);
			section.Add(unitField.FixedHeight(28f));
			LabHoverInfo.Bind(unitField.GameObject, "encounter.decoration.UnitId");
			unitField.OnEndEdit(value =>
			{
				string unitId = (value ?? string.Empty).Trim();
				if (unitId == decoration.UnitId)
				{
					return;
				}

				decoration.UnitId = unitId;
				AfterDecorationsChanged(encounter, decoration.Id);
			});

			DrawDecorationPlacementFields(section, encounter, decoration, theme);

			section.Add(UiLabel.Create(section.ContentTransform, "Facing", theme, 13).FixedHeight(20f));
			UiToggle flip = UiToggle.Create(section.ContentTransform, "Flipped", decoration.Flipped, theme);
			flip.OnValueChanged(value =>
			{
				if (value == decoration.Flipped)
				{
					return;
				}

				decoration.Flipped = value;
				AfterDecorationsChanged(encounter, decoration.Id);
			});
			section.Add(flip.FixedHeight(28f));
			LabHoverInfo.Bind(flip.GameObject, "encounter.decoration.Flipped");

			section.Add(UiButton.Create(section.ContentTransform, "Clear placement", () =>
			{
				EncounterDecorationRules.ClearPlacement(decoration);
				AfterDecorationsChanged(encounter, decoration.Id);
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.decoration.Clear");

			section.Add(UiButton.Create(section.ContentTransform, "Remove Decoration", () =>
			{
				if (!EncounterDecorationRules.Remove(encounter.File, decoration.Id))
				{
					return;
				}

				AfterDecorationsChanged(encounter, null);
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.decoration.Remove");
		}

		private static void DrawDecorationPlacementFields(
			UiStack section,
			EncounterSession encounter,
			EncounterDecorationModel decoration,
			UiTheme theme)
		{
			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			section.Add(UiLabel.Create(section.ContentTransform,
				"Hex (live " + width + "×" + height + "; empty = not on the board)",
				theme, 13).FixedHeight(20f));
			UiStack hexRow = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Col", theme, 13).FixedWidth(28f));
			UiTextField colField = UiTextField.Create(hexRow.ContentTransform,
				decoration.Col.HasValue ? decoration.Col.Value.ToString() : string.Empty, theme);
			hexRow.Add(colField.Grow());
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Row", theme, 13).FixedWidth(32f));
			UiTextField rowField = UiTextField.Create(hexRow.ContentTransform,
				decoration.Row.HasValue ? decoration.Row.Value.ToString() : string.Empty, theme);
			hexRow.Add(rowField.Grow());
			section.Add(hexRow.FixedHeight(28f));
			LabHoverInfo.Bind(colField.GameObject, "encounter.decoration.Col");
			LabHoverInfo.Bind(rowField.GameObject, "encounter.decoration.Row");
			colField.OnEndEdit(value => ApplyDecorationCoord(encounter, decoration, value, isCol: true));
			rowField.OnEndEdit(value => ApplyDecorationCoord(encounter, decoration, value, isCol: false));
		}

		private static void ApplyDecorationCoord(
			EncounterSession encounter,
			EncounterDecorationModel decoration,
			string text,
			bool isCol)
		{
			int? parsed;
			if (!EncounterPlacementRules.TryParseCoord(text, out parsed))
			{
				return;
			}

			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			int? nextCol = decoration.Col;
			int? nextRow = decoration.Row;
			if (isCol)
			{
				nextCol = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, width) : (int?)null;
				if (nextCol.HasValue && !nextRow.HasValue)
				{
					nextRow = height / 2;
				}
			}
			else
			{
				nextRow = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, height) : (int?)null;
				if (nextRow.HasValue && !nextCol.HasValue)
				{
					nextCol = width / 2;
				}
			}

			decoration.Col = nextCol;
			decoration.Row = nextRow;
			AfterDecorationsChanged(encounter, decoration.Id);
		}

		/// <summary>Fallback Combatants folder drawer. The persistent catalogue host usually wins.</summary>
		internal static void DrawCombatants(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Combatants", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			int total = encounter != null && encounter.File != null && encounter.File.Combatants != null
				? encounter.File.Combatants.Count
				: 0;
			section.Add(UiLabel.Create(section.ContentTransform,
				total + " combatant" + (total == 1 ? "" : "s") + ". Empty is legal to save; Play will refuse zero GoodSide.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(36f));
			section.Add(UiButton.Create(section.ContentTransform, "Add Combatant",
				() => CreateCombatant(node, encounter), theme, primary: true).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.combatants.Add");
		}

		/// <summary>One combatant: source, side, level, hex, facing, Jump, remove.</summary>
		internal static void DrawCombatant(LabNode node, ProjectSession session, Transform contentParent)
		{
			EncounterSession encounter = session as EncounterSession;
			EncounterCombatantModel combatant = node != null ? node.Payload as EncounterCombatantModel : null;
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				combatant != null ? LabelFor(combatant) : "Combatant", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			if (combatant != null)
			{
				LokrLab.LabClipboard.AddIdRow(section, combatant.Id);
			}

			if (combatant == null || encounter == null)
			{
				return;
			}

			bool isCharacter = string.Equals(combatant.Source, EncounterFileModel.SourceCharacter, StringComparison.Ordinal);
			section.Add(UiLabel.Create(section.ContentTransform,
				isCharacter ? "Source: Character" : "Source: Unit",
				theme, 13).FixedHeight(20f));

			if (isCharacter)
			{
				DrawCharacterFields(section, encounter, combatant, theme);
			}
			else
			{
				DrawUnitFields(section, encounter, combatant, theme);
			}

			section.Add(UiLabel.Create(section.ContentTransform, "Side", theme, 13).FixedHeight(20f));
			UiDropdown sideField = UiDropdown.Create(section.ContentTransform,
				new[] { EncounterFileModel.GoodSide, EncounterFileModel.BadSide }, theme);
			sideField.SetValueSilently(combatant.Side == EncounterFileModel.BadSide ? 1 : 0);
			sideField.OnValueChanged(index =>
			{
				string next = index == 1 ? EncounterFileModel.BadSide : EncounterFileModel.GoodSide;
				if (next == combatant.Side)
				{
					return;
				}

				combatant.Side = next;
				AfterCombatantsChanged(encounter, combatant.Id);
			});
			section.Add(sideField.FixedHeight(28f));
			LabHoverInfo.Bind(sideField.GameObject, "encounter.combatant.Side");

			if (isCharacter)
			{
				DrawLevelField(section, encounter, combatant, theme);
			}

			DrawPlacementFields(section, encounter, combatant, theme);

			if (string.Equals(combatant.Side, EncounterFileModel.BadSide, StringComparison.Ordinal)
				&& encounter.File != null && encounter.File.Exploration)
			{
				DrawExplorationCombatantFields(section, encounter, combatant, theme);
			}

			section.Add(UiButton.Create(section.ContentTransform, "Remove Combatant", () =>
			{
				if (!encounter.TryRemove(combatant.Id))
				{
					return;
				}

				EncounterEdit.RemovePreviewCombatant(combatant.Id);
				LabSaveUx.MarkDirty();
				EncounterSetupViewport.Refresh();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}, theme, primary: false).FixedHeight(28f));
			LabHoverInfo.Bind(section.GameObject, "encounter.combatant.Remove");
		}

		/// <summary>This encounter folder's aliases.json list.</summary>
		internal static void DrawAliases(LabNode node, ProjectSession session, Transform contentParent)
		{
			string folder = node != null ? node.Payload as string : null;
			if (string.IsNullOrEmpty(folder) && session != null)
			{
				folder = session.FolderPath;
			}

			LokrLab.LabAliasesInspector.Draw(folder, contentParent);
		}

		private static void DrawCharacterFields(
			UiStack section,
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			UiTheme theme)
		{
			ProjectReference reference = FindCharacter(combatant.ProjectId);
			bool missing = reference == null || string.IsNullOrEmpty(reference.FolderPath)
				|| !Directory.Exists(reference.FolderPath);
			section.Add(UiLabel.Create(section.ContentTransform,
				"Character: " + (string.IsNullOrEmpty(combatant.ProjectId) ? "(none)" : combatant.ProjectId),
				theme, 13).FixedHeight(20f));
			if (missing)
			{
				section.Add(UiLabel.Create(section.ContentTransform,
					"That Character folder is missing. The row stays listed; Play will error.",
					theme, 11, TextAnchor.UpperLeft).FixedHeight(32f));
			}

			UiStack row = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			row.Add(UiButton.Create(row.ContentTransform, "Change Character", () =>
			{
				ProjectReferencePickerModal.Show(LokrLabApi.LokrLabApi.CharacterTypeId, picked =>
				{
					if (picked == null || string.IsNullOrEmpty(picked.ProjectId))
					{
						return;
					}

					combatant.ProjectId = picked.ProjectId;
					AfterCombatantsChanged(encounter, combatant.Id);
				});
			}, theme, primary: false).Grow());
			row.Add(UiButton.Create(row.ContentTransform, "Jump to Character", () =>
			{
				if (missing)
				{
					LokrLab.Lab.SetStatus("Character folder '" + combatant.ProjectId + "' is missing.");
					return;
				}

				LokrLabApi.LokrLabApi.JumpToProject(
					LokrLabApi.LokrLabApi.CharacterTypeId,
					reference.FolderPath,
					null);
			}, theme, primary: !missing).Grow());
			section.Add(row.FixedHeight(28f));
			LabHoverInfo.Bind(row.GameObject, "encounter.combatant.Project");
		}

		private static void DrawUnitFields(
			UiStack section,
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform, "Unit id", theme, 13).FixedHeight(20f));
			UiComboBox unitField = UiComboBox.Create(section.ContentTransform, AbilityCatalogLookupsSafe(),
				combatant.UnitId, theme);
			section.Add(unitField.FixedHeight(28f));
			LabHoverInfo.Bind(unitField.GameObject, "encounter.combatant.UnitId");
			unitField.OnEndEdit(value =>
			{
				string unitId = (value ?? string.Empty).Trim();
				if (string.IsNullOrEmpty(unitId) || unitId == combatant.UnitId)
				{
					return;
				}

				combatant.UnitId = unitId;
				AfterCombatantsChanged(encounter, combatant.Id);
			});
		}

		private static void DrawLevelField(
			UiStack section,
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform, "Level", theme, 13).FixedHeight(20f));
			List<int> levels = LokrLab.SandboxRoster.ListAvailableLevels(combatant.ProjectId);
			if (combatant.Level >= 1 && !levels.Contains(combatant.Level))
			{
				levels.Add(combatant.Level);
				levels.Sort();
			}

			List<string> labels = new List<string>();
			int selected = 0;
			for (int i = 0; i < levels.Count; i++)
			{
				labels.Add(levels[i].ToString());
				if (levels[i] == combatant.Level)
				{
					selected = i;
				}
			}

			if (labels.Count == 0)
			{
				labels.Add("1");
			}

			UiDropdown levelField = UiDropdown.Create(section.ContentTransform, labels, theme);
			levelField.SetValueSilently(selected);
			levelField.OnValueChanged(index =>
			{
				if (index < 0 || index >= levels.Count)
				{
					return;
				}

				int next = levels[index];
				if (next == combatant.Level)
				{
					return;
				}

				combatant.Level = next;
				LabSaveUx.MarkDirty();
				EncounterSetupViewport.Refresh();
			});
			section.Add(levelField.FixedHeight(28f));
			LabHoverInfo.Bind(levelField.GameObject, "encounter.combatant.Level");
		}

		/// <summary>Pocket tag and per-unit aggro radius override. BadSide only, shown while Exploration is on.</summary>
		private static void DrawExplorationCombatantFields(
			UiStack section,
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform,
				"Pocket (blank = wakes alone)", theme, 13).FixedHeight(20f));
			UiTextField pocketField = UiTextField.Create(section.ContentTransform, combatant.Pocket ?? string.Empty, theme);
			pocketField.OnEndEdit(value =>
			{
				string next = (value ?? string.Empty).Trim();
				if (next == combatant.Pocket)
				{
					return;
				}

				combatant.Pocket = next;
				LabSaveUx.MarkDirty();
			});
			section.Add(pocketField.FixedHeight(28f));
			LabHoverInfo.Bind(pocketField.GameObject, "encounter.combatant.Pocket");

			section.Add(UiLabel.Create(section.ContentTransform,
				"Aggro radius (blank = file default)", theme, 13).FixedHeight(20f));
			UiTextField radiusField = UiTextField.Create(section.ContentTransform,
				combatant.AggroRadius.HasValue ? combatant.AggroRadius.Value.ToString() : string.Empty, theme);
			radiusField.OnEndEdit(value =>
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					if (!combatant.AggroRadius.HasValue)
					{
						return;
					}

					combatant.AggroRadius = null;
					LabSaveUx.MarkDirty();
					return;
				}

				int parsed;
				if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out parsed) || parsed <= 0)
				{
					return;
				}

				combatant.AggroRadius = parsed;
				LabSaveUx.MarkDirty();
			});
			section.Add(radiusField.FixedHeight(28f));
			LabHoverInfo.Bind(radiusField.GameObject, "encounter.combatant.AggroRadius");

			section.Add(UiLabel.Create(section.ContentTransform,
				"Trigger (wins over radius when set)", theme, 13).FixedHeight(20f));
			List<string> ids = EncounterTriggerRules.CatalogIds(encounter.File);
			List<string> labels = new List<string> { "None" };
			labels.AddRange(ids);
			int selected = string.IsNullOrEmpty(combatant.TriggerId) ? 0 : ids.IndexOf(combatant.TriggerId) + 1;
			UiDropdown triggerField = UiDropdown.Create(section.ContentTransform, labels, theme);
			triggerField.SetValueSilently(selected < 0 ? 0 : selected);
			triggerField.OnValueChanged(index =>
			{
				string next = index <= 0 || index > ids.Count ? string.Empty : ids[index - 1];
				if (string.Equals(next, combatant.TriggerId ?? string.Empty, StringComparison.Ordinal))
				{
					return;
				}

				combatant.TriggerId = next;
				LabSaveUx.MarkDirty();
			});
			section.Add(triggerField.FixedHeight(28f));
			LabHoverInfo.Bind(triggerField.GameObject, "encounter.combatant.TriggerId");
		}

		private static void DrawPlacementFields(
			UiStack section,
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			UiTheme theme)
		{
			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			section.Add(UiLabel.Create(section.ContentTransform,
				"Hex (live " + width + "×" + height + "; empty = Play center-offset)",
				theme, 13).FixedHeight(20f));
			UiStack hexRow = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Col", theme, 13).FixedWidth(28f));
			UiTextField colField = UiTextField.Create(hexRow.ContentTransform,
				combatant.Col.HasValue ? combatant.Col.Value.ToString() : string.Empty, theme);
			hexRow.Add(colField.Grow());
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Row", theme, 13).FixedWidth(32f));
			UiTextField rowField = UiTextField.Create(hexRow.ContentTransform,
				combatant.Row.HasValue ? combatant.Row.Value.ToString() : string.Empty, theme);
			hexRow.Add(rowField.Grow());
			section.Add(hexRow.FixedHeight(28f));
			LabHoverInfo.Bind(colField.GameObject, "encounter.combatant.Col");
			LabHoverInfo.Bind(rowField.GameObject, "encounter.combatant.Row");
			colField.OnEndEdit(value => ApplyCoord(encounter, combatant, value, isCol: true));
			rowField.OnEndEdit(value => ApplyCoord(encounter, combatant, value, isCol: false));

			UiToggle flipped = UiToggle.Create(section.ContentTransform, "Flipped", combatant.Flipped, theme);
			flipped.OnValueChanged(on =>
			{
				if (on == combatant.Flipped)
				{
					return;
				}

				combatant.Flipped = on;
				LabSaveUx.MarkDirty();
				EncounterEdit.ApplyFacing(combatant);
				EncounterSetupViewport.Refresh();
			});
			section.Add(flipped.FixedHeight(28f));
			LabHoverInfo.Bind(flipped.GameObject, "encounter.combatant.Flipped");

			if (EncounterPlacementRules.HasPlacement(combatant) || EncounterPlacementRules.HasPartialPlacement(combatant))
			{
				UiButton clear = UiButton.Create(section.ContentTransform, "Clear placement", () =>
				{
					combatant.Col = null;
					combatant.Row = null;
					EncounterEdit.RemovePreviewCombatant(combatant.Id);
					AfterCombatantsChanged(encounter, combatant.Id, restartPreview: false);
					EncounterEdit.RefreshSpawnMarkers();
				}, theme, primary: false);
				section.Add(clear.FixedHeight(28f));
				LabHoverInfo.Bind(clear.GameObject, "encounter.combatant.Clear");
			}

			string warning = EncounterPlacementRules.Warning(encounter.File, combatant);
			if (!string.IsNullOrEmpty(warning))
			{
				section.Add(UiLabel.Create(section.ContentTransform, warning, theme, 11, TextAnchor.UpperLeft)
					.FixedHeight(36f));
			}
		}

		private static void ApplyCoord(
			EncounterSession encounter,
			EncounterCombatantModel combatant,
			string text,
			bool isCol)
		{
			int? parsed;
			if (!EncounterPlacementRules.TryParseCoord(text, out parsed))
			{
				return;
			}

			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			int? nextCol = combatant.Col;
			int? nextRow = combatant.Row;
			if (isCol)
			{
				nextCol = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, width) : (int?)null;
				if (nextCol.HasValue && !nextRow.HasValue)
				{
					nextRow = height / 2;
				}
			}
			else
			{
				nextRow = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, height) : (int?)null;
				if (nextRow.HasValue && !nextCol.HasValue)
				{
					nextCol = width / 2;
				}
			}

			if (nextCol.HasValue && nextRow.HasValue
				&& EncounterEdit.IsArmed
				&& !EncounterEdit.IsHexWalkable(
					EncounterPlacementRules.ClampCoord(nextCol.Value, width),
					EncounterPlacementRules.ClampCoord(nextRow.Value, height)))
			{
				LokrLab.Lab.SetStatus("Hex is not walkable.");
				return;
			}

			combatant.Col = nextCol;
			combatant.Row = nextRow;
			if (!combatant.Col.HasValue && !combatant.Row.HasValue)
			{
				EncounterEdit.RemovePreviewCombatant(combatant.Id);
				AfterCombatantsChanged(encounter, combatant.Id, restartPreview: false);
				EncounterEdit.RefreshSpawnMarkers();
				return;
			}

			if (EncounterPlacementRules.HasPlacement(combatant))
			{
				EncounterPlacementRules.ClampToLiveBoard(combatant, encounter.File);
			}

			AfterCombatantsChanged(encounter, combatant.Id, restartPreview: false);
			EncounterEdit.TryMovePreview(combatant);
			EncounterEdit.RefreshSpawnMarkers();
		}

		private static string[] AbilityCatalogLookupsSafe()
		{
			try
			{
				return LokrAbilityLab.Editor.AbilityCatalogLookups.UnitOptions();
			}
			catch (Exception)
			{
				return new[] { LokrLab.SandboxRoster.DefaultEnemyUnitId };
			}
		}

		private static ProjectReference FindCharacter(string projectId)
		{
			if (string.IsNullOrEmpty(projectId))
			{
				return null;
			}

			List<ProjectReference> rows = ProjectBrowser.ListProjectReferences(LokrLabApi.LokrLabApi.CharacterTypeId);
			for (int i = 0; i < rows.Count; i++)
			{
				if (string.Equals(rows[i].ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
				{
					return rows[i];
				}
			}

			string folder = Path.Combine(CharacterLabPaths.CharactersRoot, projectId);
			if (Directory.Exists(folder))
			{
				return new ProjectReference(LokrLabApi.LokrLabApi.CharacterTypeId, projectId, folder, projectId);
			}

			return null;
		}

		private static LabNode ContributeTerrains(EncounterSession encounter)
		{
			LabNode folder = new LabNode
			{
				Id = "encounter-terrains:" + encounter.Id,
				DisplayName = "Terrains",
				Kind = TerrainsKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File == null)
			{
				return folder;
			}

			try
			{
				EncounterTerrainCatalog.EnsureHostTerrains(encounter.File);
			}
			catch (Exception)
			{
			}

			if (encounter.File.Terrains == null)
			{
				return folder;
			}

			for (int i = 0; i < encounter.File.Terrains.Count; i++)
			{
				EncounterTerrainModel terrain = encounter.File.Terrains[i];
				if (terrain == null)
				{
					continue;
				}

				folder.Children.Add(new LabNode
				{
					Id = TerrainNodeId(encounter, terrain.TerrainId, terrain.Template),
					DisplayName = LabelFor(terrain),
					Kind = TerrainKind,
					IconKey = "Enc",
					Payload = terrain
				});
			}

			return folder;
		}

		private static LabNode ContributeTriggers(EncounterSession encounter)
		{
			LabNode folder = new LabNode
			{
				Id = TriggersFolderId(encounter),
				DisplayName = "Triggers",
				Kind = TriggersKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File == null)
			{
				return folder;
			}

			if (encounter.File.Triggers == null)
			{
				return folder;
			}

			for (int i = 0; i < encounter.File.Triggers.Count; i++)
			{
				EncounterTriggerModel trigger = encounter.File.Triggers[i];
				if (trigger == null || string.IsNullOrEmpty(trigger.Id))
				{
					continue;
				}

				folder.Children.Add(new LabNode
				{
					Id = TriggerNodeId(encounter, trigger.Id),
					DisplayName = trigger.Id,
					Kind = TriggerKind,
					IconKey = "Enc",
					Payload = trigger
				});
			}

			return folder;
		}

		private static LabNode ContributeSpawnPoints(EncounterSession encounter)
		{
			LabNode folder = new LabNode
			{
				Id = SpawnPointsFolderId(encounter),
				DisplayName = "Spawn Points",
				Kind = SpawnPointsKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File == null || encounter.File.Combatants == null)
			{
				return folder;
			}

			for (int i = 0; i < encounter.File.Combatants.Count; i++)
			{
				EncounterCombatantModel combatant = encounter.File.Combatants[i];
				if (combatant == null || string.IsNullOrEmpty(combatant.Id)
					|| !string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
				{
					continue;
				}

				folder.Children.Add(new LabNode
				{
					Id = SpawnPointNodeId(encounter, combatant.Id),
					DisplayName = LabelFor(combatant),
					Kind = SpawnPointKind,
					IconKey = "Enc",
					Payload = combatant
				});
			}

			return folder;
		}

		private static LabNode ContributeProps(EncounterSession encounter)
		{
			LabNode folder = new LabNode
			{
				Id = "encounter-props:" + encounter.Id,
				DisplayName = "Props",
				Kind = PropsKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File == null || encounter.File.Props == null)
			{
				return folder;
			}

			for (int i = 0; i < encounter.File.Props.Count; i++)
			{
				EncounterPropModel prop = encounter.File.Props[i];
				if (prop == null || string.IsNullOrEmpty(prop.Id))
				{
					continue;
				}

				folder.Children.Add(new LabNode
				{
					Id = PropNodeId(encounter, prop.Id),
					DisplayName = LabelFor(prop),
					Kind = PropKind,
					IconKey = "Enc",
					Payload = prop
				});
			}

			return folder;
		}

		private static LabNode ContributeDecorations(EncounterSession encounter)
		{
			LabNode folder = new LabNode
			{
				Id = "encounter-decorations:" + encounter.Id,
				DisplayName = "Decorations",
				Kind = DecorationsKind,
				IconKey = "Enc",
				Payload = encounter
			};
			if (encounter.File == null || encounter.File.Decorations == null)
			{
				return folder;
			}

			for (int i = 0; i < encounter.File.Decorations.Count; i++)
			{
				EncounterDecorationModel decoration = encounter.File.Decorations[i];
				if (decoration == null || string.IsNullOrEmpty(decoration.Id))
				{
					continue;
				}

				folder.Children.Add(new LabNode
				{
					Id = DecorationNodeId(encounter, decoration.Id),
					DisplayName = LabelFor(decoration),
					Kind = DecorationKind,
					IconKey = "Enc",
					Payload = decoration
				});
			}

			return folder;
		}

		private static void DrawPropPlacementFields(
			UiStack section,
			EncounterSession encounter,
			EncounterPropModel prop,
			UiTheme theme)
		{
			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			section.Add(UiLabel.Create(section.ContentTransform,
				"Hex (live " + width + "×" + height + "; empty = not on the board)",
				theme, 13).FixedHeight(20f));
			UiStack hexRow = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Col", theme, 13).FixedWidth(28f));
			UiTextField colField = UiTextField.Create(hexRow.ContentTransform,
				prop.Col.HasValue ? prop.Col.Value.ToString() : string.Empty, theme);
			hexRow.Add(colField.Grow());
			hexRow.Add(UiLabel.Create(hexRow.ContentTransform, "Row", theme, 13).FixedWidth(32f));
			UiTextField rowField = UiTextField.Create(hexRow.ContentTransform,
				prop.Row.HasValue ? prop.Row.Value.ToString() : string.Empty, theme);
			hexRow.Add(rowField.Grow());
			section.Add(hexRow.FixedHeight(28f));
			LabHoverInfo.Bind(colField.GameObject, "encounter.prop.Col");
			LabHoverInfo.Bind(rowField.GameObject, "encounter.prop.Row");
			colField.OnEndEdit(value => ApplyPropCoord(encounter, prop, value, isCol: true));
			rowField.OnEndEdit(value => ApplyPropCoord(encounter, prop, value, isCol: false));
		}

		private static void ApplyPropCoord(
			EncounterSession encounter,
			EncounterPropModel prop,
			string text,
			bool isCol)
		{
			int? parsed;
			if (!EncounterPlacementRules.TryParseCoord(text, out parsed))
			{
				return;
			}

			EncounterPlacementRules.LiveSize(encounter.File, out int width, out int height);
			int? nextCol = prop.Col;
			int? nextRow = prop.Row;
			if (isCol)
			{
				nextCol = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, width) : (int?)null;
				if (nextCol.HasValue && !nextRow.HasValue)
				{
					nextRow = height / 2;
				}
			}
			else
			{
				nextRow = parsed.HasValue ? EncounterPlacementRules.ClampCoord(parsed.Value, height) : (int?)null;
				if (nextRow.HasValue && !nextCol.HasValue)
				{
					nextCol = width / 2;
				}
			}

			if (nextCol.HasValue && nextRow.HasValue)
			{
				string error = EncounterPropRules.CanPlace(
					encounter.File, prop, nextCol.Value, nextRow.Value, width, height);
				if (error != null)
				{
					LokrLab.Lab.SetStatus(error);
					return;
				}
			}

			prop.Snap = true;
			prop.Col = nextCol;
			prop.Row = nextRow;
			prop.X = null;
			prop.Y = null;
			AfterPropsChanged(encounter, prop.Id);
		}

		private static void DrawPropWorldFields(
			UiStack section,
			EncounterSession encounter,
			EncounterPropModel prop,
			UiTheme theme)
		{
			section.Add(UiLabel.Create(section.ContentTransform,
				"World (tap or drag in the hole; empty = not on the board)",
				theme, 13).FixedHeight(20f));
			UiStack worldRow = UiStack.Horizontal(section.ContentTransform, theme, spacing: 8f, padding: 0f);
			worldRow.Add(UiLabel.Create(worldRow.ContentTransform, "X", theme, 13).FixedWidth(20f));
			UiTextField xField = UiTextField.Create(worldRow.ContentTransform,
				prop.X.HasValue
					? prop.X.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
					: string.Empty, theme);
			worldRow.Add(xField.Grow());
			worldRow.Add(UiLabel.Create(worldRow.ContentTransform, "Y", theme, 13).FixedWidth(20f));
			UiTextField yField = UiTextField.Create(worldRow.ContentTransform,
				prop.Y.HasValue
					? prop.Y.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
					: string.Empty, theme);
			worldRow.Add(yField.Grow());
			section.Add(worldRow.FixedHeight(28f));
			LabHoverInfo.Bind(xField.GameObject, "encounter.prop.X");
			LabHoverInfo.Bind(yField.GameObject, "encounter.prop.Y");
			xField.OnEndEdit(value => ApplyPropWorld(encounter, prop, value, isX: true));
			yField.OnEndEdit(value => ApplyPropWorld(encounter, prop, value, isX: false));
		}

		private static void ApplyPropWorld(
			EncounterSession encounter,
			EncounterPropModel prop,
			string text,
			bool isX)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				if (isX)
				{
					prop.X = null;
				}
				else
				{
					prop.Y = null;
				}

				prop.Col = null;
				prop.Row = null;
				AfterPropsChanged(encounter, prop.Id);
				return;
			}

			float parsed;
			if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out parsed))
			{
				return;
			}

			prop.Snap = false;
			prop.Col = null;
			prop.Row = null;
			if (isX)
			{
				prop.X = parsed;
			}
			else
			{
				prop.Y = parsed;
			}

			AfterPropsChanged(encounter, prop.Id);
		}

		private static void AddCustomTerrain(EncounterSession encounter)
		{
			if (encounter == null || encounter.File == null)
			{
				return;
			}

			EncounterTerrainModel added = EncounterTerrainRules.AddCustom(encounter.File);
			if (added == null)
			{
				return;
			}

			AfterTerrainsChanged(encounter, added.TerrainId, added.Template);
			LokrLab.Lab.SetStatus("Custom terrain art is later. Import from a stage to paint.");
		}

		private static bool IsAlreadySelected(EncounterTerrainModel terrain)
		{
			return terrain.TerrainId == EncounterTiles.SelectedTerrainId
				&& string.Equals(
					terrain.Template ?? string.Empty,
					EncounterTiles.SelectedSourceTemplate ?? string.Empty,
					StringComparison.Ordinal);
		}

		private static void UseTerrain(EncounterTerrainModel terrain)
		{
			if (terrain == null)
			{
				return;
			}

			EncounterTiles.Select(terrain);
			EncounterEdit.Tool = EncounterEditTool.PaintTerrain;
			EncounterSetupViewport.Refresh();
			if (string.Equals(terrain.Source, EncounterFileModel.TerrainSourceCustom, StringComparison.Ordinal))
			{
				LokrLab.Lab.SetStatus("Custom terrain art is later. Import from a stage to paint.");
				return;
			}

			LokrLab.Lab.SetStatus("Paint Terrain: " + LabelFor(terrain) + ".");
		}

		/// <summary>Node Tree id for one combatant row in this encounter.</summary>
		internal static string CombatantNodeId(EncounterSession encounter, string combatantId)
		{
			return "encounter-combatant:" + encounter.Id + ":" + combatantId;
		}

		/// <summary>Node Tree id for one terrain row in this encounter.</summary>
		internal static string TerrainNodeId(EncounterSession encounter, int terrainId, string template)
		{
			return "encounter-terrain:" + encounter.Id + ":" + terrainId + ":" + (template ?? string.Empty);
		}

		/// <summary>Node Tree id for one prop row in this encounter.</summary>
		internal static string PropNodeId(EncounterSession encounter, string propId)
		{
			return "encounter-prop:" + encounter.Id + ":" + propId;
		}

		/// <summary>Node Tree id for one decoration row in this encounter.</summary>
		internal static string DecorationNodeId(EncounterSession encounter, string decorationId)
		{
			return "encounter-decoration:" + encounter.Id + ":" + decorationId;
		}

		private static string LabelFor(EncounterCombatantModel combatant)
		{
			if (combatant == null)
			{
				return "Combatant";
			}

			if (string.Equals(combatant.Source, EncounterFileModel.SourceCharacter, StringComparison.Ordinal)
				&& !string.IsNullOrEmpty(combatant.ProjectId))
			{
				return combatant.ProjectId + " (" + combatant.Side + ")";
			}

			if (!string.IsNullOrEmpty(combatant.UnitId))
			{
				return combatant.UnitId + " (" + combatant.Side + ")";
			}

			if (string.Equals(combatant.Source, EncounterFileModel.SourceSpawn, StringComparison.Ordinal))
			{
				return "Hero Spawn Point (" + combatant.Id + ")";
			}

			return combatant.Id;
		}

		private static string LabelFor(EncounterTerrainModel terrain)
		{
			if (terrain == null)
			{
				return "Terrain";
			}

			string name = string.IsNullOrEmpty(terrain.Name) ? "Terrain " + terrain.TerrainId : terrain.Name;
			string source = EncounterTerrainRules.SourceLabel(terrain.Source);
			if (string.Equals(source, "template", StringComparison.Ordinal))
			{
				return name;
			}

			return name + " (" + source + ")";
		}

		private static string LabelFor(EncounterPropModel prop)
		{
			if (prop == null)
			{
				return "Prop";
			}

			if (!string.IsNullOrEmpty(prop.PrefabName))
			{
				return prop.PrefabName;
			}

			return prop.Id;
		}

		private static string LabelFor(EncounterDecorationModel decoration)
		{
			if (decoration == null)
			{
				return "Decoration";
			}

			return string.IsNullOrEmpty(decoration.UnitId) ? decoration.Id : decoration.UnitId;
		}
	}
}
