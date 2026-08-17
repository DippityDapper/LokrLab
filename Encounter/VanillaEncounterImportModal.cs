using System;
using System.Collections.Generic;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>File → Import Vanilla Encounter... picks a shipped combat-room prefab and imports it into a new Lab Encounter project.</summary>
	/// <remarks>
	/// Phase 1 (import spike) of the Vanilla Encounter Edit track
	/// (docs/roadmaps/started/vanilla-encounter-edit.md). Unlike the Ability track's vanilla browser,
	/// there's no separate read-only "browse" step before committing -- a read-only prefab inspect
	/// here would need most of the same work TryImport already does (load prefab, read
	/// EncounterDefinition/EncounterBkgDefinition), and importing always creates a brand-new project
	/// rather than touching anything existing, so there's no "Override is global, are you sure"
	/// blast-radius concern the way ability/character overrides have. Picking a name imports it
	/// immediately; the result (or error) shows in the status label, and the loss-list summary
	/// (gated/cinematic/out-of-bounds counts, variant warnings) is logged for full detail.
	/// Registered with no isVisible guard -- reachable from the Project Browser, matching
	/// File -&gt; Edit Vanilla Hero...'s pattern (extraction directly creates the project).
	/// </remarks>
	internal static class VanillaEncounterImportModal
	{
		private static UiModal modal;
		private static UiTextField searchField;
		private static UiStack listRows;
		private static UiLabel emptyLabel;
		private static UiLabel statusLabel;

		internal static void Show()
		{
			if (!EnsureModal())
			{
				return;
			}

			RefreshList();
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
			modal = UiModal.Create(canvas, theme, "Import Vanilla Encounter", 560f, 520f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);

			UiLabel explainer = UiLabel.Create(content.ContentTransform,
				"Read-only import spike. Picking a room reconstructs its combatants and impassable hexes into a brand-new Lab Encounter project -- vanilla is never touched. Lua, cinematics, and quest-gated variants are not imported; see the log for the full loss-list report.",
				theme, 12, TextAnchor.UpperLeft);
			content.Add(explainer.FixedHeight(56f));
			LabHoverInfo.Bind(explainer.GameObject, "encounter.vanilla.Import");

			searchField = UiTextField.Create(content.ContentTransform, string.Empty, theme);
			searchField.InputField.onValueChanged.AddListener(_ => RefreshList());
			content.Add(searchField.FixedHeight(28f));

			emptyLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			content.Add(emptyLabel.FixedHeight(24f));

			listRows = UiStack.Vertical(content.ContentTransform, theme, spacing: 2f, padding: 0f, scrollable: true);
			content.Add(listRows.Grow());

			statusLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			content.Add(statusLabel.FixedHeight(36f));

			content.Add(UiButton.Create(content.ContentTransform, "Close", Hide, theme, primary: false).FixedHeight(32f));

			return true;
		}

		private static void RefreshList()
		{
			if (listRows == null)
			{
				return;
			}

			listRows.Clear();
			string filter = searchField != null ? searchField.InputField.text.Trim() : string.Empty;
			List<string> stages = EncounterTerrainCatalog.ListStages();
			int shown = 0;
			for (int i = 0; i < stages.Count; i++)
			{
				string stage = stages[i];
				if (!string.IsNullOrEmpty(filter) && stage.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				string captured = stage;
				listRows.Add(UiButton.Create(listRows.ContentTransform, stage, () => Import(captured), theme: UiTheme.Default, primary: false)
					.FixedHeight(26f));
				shown++;
			}

			emptyLabel.SetText(shown == 0 ? "No shipped rooms match." : string.Empty);
			emptyLabel.Visible(shown == 0);
		}

		private static void Import(string templateName)
		{
			VanillaEncounterImporter.ImportResult result = VanillaEncounterImporter.TryImport(templateName, out string error);
			if (result == null)
			{
				statusLabel.SetText(error ?? "Import failed.");
				return;
			}

			LokrLabApi.LokrLabApi.RequestRefresh();
			statusLabel.SetText(string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"Imported to {0}. {1} hero, {2} enemy spawns, {3} props; {4} cinematic dropped, {5} gated, {6} out of bounds.",
				result.Folder, result.HeroesImported, result.EnemiesImported, result.PropsImported,
				result.CinematicDropped, result.GatedFlagged, result.OutOfBounds));

			foreach (string warning in result.Warnings)
			{
				LokrLabPlugin.Log.LogWarning("Vanilla encounter import '" + templateName + "': " + warning);
			}

			LokrLabPlugin.Log.LogInfo(string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"Vanilla encounter import '{0}' -> {1}: {2} hero, {3} enemy, {4} props ({5} with dropped children), {6} cinematic dropped, {7} gated, {8} out of bounds, {9} EncounterDefinition variant(s), {10} Bkg variant(s).",
				templateName, result.Folder, result.HeroesImported, result.EnemiesImported,
				result.PropsImported, result.PropsWithChildrenFlattened,
				result.CinematicDropped, result.GatedFlagged, result.OutOfBounds,
				result.EncounterDefinitionVariantCount, result.BkgDefinitionVariantCount));
		}

		private static void Hide()
		{
			if (modal != null)
			{
				modal.Hide();
			}
		}
	}
}
