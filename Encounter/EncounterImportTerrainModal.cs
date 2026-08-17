using System.Collections.Generic;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Import Terrains sheet: pick a templates prefab and add its hex-art terrains.</summary>
	internal static class EncounterImportTerrainModal
	{
		private static UiModal modal;
		private static UiComboBox stageField;
		private static UiLabel errorLabel;
		private static EncounterSession session;

		/// <summary>Opens the sheet for the current Encounter session.</summary>
		internal static void Show(EncounterSession encounter)
		{
			session = encounter;
			if (!EnsureModal())
			{
				return;
			}

			errorLabel.SetText(string.Empty);
			List<string> stages = EncounterTerrainCatalog.ListStages();
			string current = encounter != null && encounter.File != null
				? EncounterTemplateRules.Canonical(encounter.File.Template)
				: EncounterFileModel.DefaultTemplate;
			if (stageField != null)
			{
				stageField.SetOptions(stages);
				stageField.SetText(current);
			}

			modal.Show();
		}

		private static bool EnsureModal()
		{
			if (modal != null && modal.GameObject != null)
			{
				return true;
			}

			Transform canvas = LokrLab.Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			modal = UiModal.Create(canvas, theme, "Import Terrains", 560f, 280f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);
			content.Add(UiLabel.Create(content.ContentTransform,
				"Scan another templates prefab and add terrains that have hex floor art.",
				theme, 12, TextAnchor.UpperLeft).FixedHeight(36f));
			content.Add(UiLabel.Create(content.ContentTransform, "Stage", theme, 13).FixedHeight(20f));
			stageField = UiComboBox.Create(content.ContentTransform, EncounterTerrainCatalog.ListStages(),
				EncounterFileModel.DefaultTemplate, theme);
			content.Add(stageField.FixedHeight(28f));
			LabHoverInfo.Bind(stageField.GameObject, "encounter.terrains.Import");
			errorLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12);
			content.Add(errorLabel.FixedHeight(22f));
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Import", OnConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", modal.Hide, theme, primary: false)
				.FixedWidth(120f));
			content.Add(buttons.FixedHeight(36f));
			return true;
		}

		private static void OnConfirmed()
		{
			if (session == null || session.File == null)
			{
				errorLabel.SetText("No Encounter project is open.");
				return;
			}

			string stage = stageField != null
				? EncounterTemplateRules.Canonical(stageField.InputField != null
					? stageField.InputField.text
					: null)
				: string.Empty;
			if (string.IsNullOrEmpty(stage))
			{
				errorLabel.SetText("Pick a stage prefab name.");
				return;
			}

			List<EncounterTerrainModel> scanned = EncounterTerrainCatalog.ScanTemplate(stage);
			if (scanned.Count == 0)
			{
				errorLabel.SetText("No hex-art terrains on '" + stage + "'.");
				return;
			}

			string host = EncounterTemplateRules.Canonical(session.File.Template);
			bool fromHost = string.Equals(stage, host, System.StringComparison.OrdinalIgnoreCase);
			int added = 0;
			EncounterTerrainModel first = null;
			for (int i = 0; i < scanned.Count; i++)
			{
				EncounterTerrainModel row = scanned[i];
				if (row == null)
				{
					continue;
				}

				row.Source = fromHost
					? EncounterFileModel.TerrainSourceTemplate
					: EncounterFileModel.TerrainSourceImport;
				row.Template = stage;
				if (!EncounterTerrainRules.Add(session.File, row))
				{
					continue;
				}

				if (first == null)
				{
					first = row;
				}

				added++;
			}

			if (added == 0 || first == null)
			{
				errorLabel.SetText("Those terrains are already listed.");
				return;
			}

			modal.Hide();
			EncounterNodes.AfterTerrainsChanged(session, first.TerrainId, stage);
			LokrLab.Lab.SetStatus("Imported " + added + " terrain" + (added == 1 ? "" : "s") + " from " + stage + ".");
		}
	}
}
