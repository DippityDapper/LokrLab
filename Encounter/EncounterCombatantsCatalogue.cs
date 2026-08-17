using System;
using System.Collections.Generic;
using Ironhide.Legends.Model.Game.Units;
using LokrAbilityLab.Editor;
using LokrCharacterLoader;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Persistent Combatants-folder inspector: Character/Unit catalogue, Stand preview, Add.</summary>
	internal static class EncounterCombatantsCatalogue
	{
		private static UiStack root;
		private static UiLabel countLabel;
		private static UiLabel hintLabel;
		private static UiButton characterButton;
		private static UiButton unitButton;
		private static UiDropdown sideField;
		private static Transform previewHole;
		private static UiCatalogue catalogue;
		private static UiLabel errorLabel;
		private static EncounterSession session;
		private static string source = EncounterFileModel.SourceCharacter;
		private static string side = EncounterFileModel.GoodSide;
		private static readonly Dictionary<string, ProjectReference> charactersById =
			new Dictionary<string, ProjectReference>();
		private static int thumbGeneration;

		/// <summary>True when the Combatants folder is the primary selection.</summary>
		internal static bool Matches(IReadOnlyList<LabNode> nodes)
		{
			return PrimaryKind(nodes) == EncounterNodes.CombatantsKind;
		}

		/// <summary>Builds the form once into the non-scroll persistent host.</summary>
		internal static void Build(Transform parent)
		{
			if (root != null && root.GameObject != null)
			{
				return;
			}

			ResetSession();
			UiTheme theme = UiTheme.Default;
			root = UiStack.Vertical(parent, theme, spacing: 6f, padding: 0f, scrollable: false);
			root.Grow();
			root.Add(UiLabel.Create(root.ContentTransform, "Combatants", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			countLabel = UiLabel.Create(root.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			root.Add(countLabel.FixedHeight(32f));
			UiStack sourceRow = UiStack.Horizontal(root.ContentTransform, theme, spacing: 8f, padding: 0f);
			characterButton = UiButton.Create(sourceRow.ContentTransform, "Character",
				() => SetSource(EncounterFileModel.SourceCharacter), theme, primary: true);
			unitButton = UiButton.Create(sourceRow.ContentTransform, "Unit",
				() => SetSource(EncounterFileModel.SourceUnit), theme, primary: false);
			sourceRow.Add(characterButton.Grow());
			sourceRow.Add(unitButton.Grow());
			root.Add(sourceRow.FixedHeight(28f));
			LabHoverInfo.Bind(sourceRow.GameObject, "encounter.combatant.Source");

			root.Add(UiLabel.Create(root.ContentTransform, "Side", theme, 13).FixedHeight(18f));
			sideField = UiDropdown.Create(root.ContentTransform,
				new[] { EncounterFileModel.GoodSide, EncounterFileModel.BadSide }, theme);
			sideField.OnValueChanged(index =>
			{
				side = index == 1 ? EncounterFileModel.BadSide : EncounterFileModel.GoodSide;
			});
			root.Add(sideField.FixedHeight(28f));
			LabHoverInfo.Bind(sideField.GameObject, "encounter.combatant.Side");

			hintLabel = UiLabel.Create(root.ContentTransform,
				"Select a card for Stand preview. Drag onto the board to place, or Add / double-click to append.",
				theme, 11, TextAnchor.UpperLeft);
			root.Add(hintLabel.FixedHeight(48f));

			UiStack previewBox = UiStack.Vertical(root.ContentTransform, theme, spacing: 0f, padding: 0f);
			previewHole = previewBox.ContentTransform;
			root.Add(previewBox.FixedHeight(180f));
			EncounterCataloguePreview.Attach(previewHole);
			LabHoverInfo.Bind(previewBox.GameObject, "encounter.catalogue.Preview");

			root.Add(UiButton.Create(root.ContentTransform, "Add selected", AddSelected, theme, primary: true)
				.FixedHeight(28f));
			LabHoverInfo.Bind(root.GameObject, "encounter.catalogue.Add");

			errorLabel = UiLabel.Create(root.ContentTransform, string.Empty, theme, 11);
			root.Add(errorLabel.FixedHeight(20f));

			catalogue = UiCatalogue.Create(root.ContentTransform, theme, scrollable: true);
			catalogue.OnSelected(OnCardSelected);
			catalogue.OnActivated(_ => AddSelected());
			catalogue.OnDropped(OnCardDropped);
			catalogue.OnItemShown(RequestThumb);
			root.Add(catalogue.Grow());
			LabHoverInfo.Bind(catalogue.SearchField.GameObject, "encounter.catalogue.Search");
		}

		/// <summary>Shows the form for the open Encounter session and rebuilds cards.</summary>
		internal static void Show(IReadOnlyList<LabNode> nodes)
		{
			session = CurrentEncounter(nodes);
			if (root == null || root.GameObject == null)
			{
				return;
			}

			root.Visible(true);
			errorLabel.SetText(string.Empty);
			RefreshCount();
			RefreshSourceButtons();
			SyncSideField();
			EncounterCataloguePreview.Attach(previewHole);
			RebuildItems();
			PreviewSelected();
		}

		/// <summary>Hides the form and the Stand camera.</summary>
		internal static void Hide()
		{
			if (root != null && root.GameObject != null)
			{
				root.Visible(false);
			}

			EncounterCataloguePreview.Hide();
		}

		/// <summary>Drops widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			root = null;
			countLabel = null;
			hintLabel = null;
			characterButton = null;
			unitButton = null;
			sideField = null;
			previewHole = null;
			catalogue = null;
			errorLabel = null;
			session = null;
			charactersById.Clear();
		}

		private static void SetSource(string next)
		{
			if (source == next)
			{
				return;
			}

			source = next;
			if (source == EncounterFileModel.SourceCharacter && side == EncounterFileModel.BadSide)
			{
				side = EncounterFileModel.GoodSide;
			}
			else if (source == EncounterFileModel.SourceUnit && side == EncounterFileModel.GoodSide)
			{
				side = EncounterFileModel.BadSide;
			}

			RefreshSourceButtons();
			SyncSideField();
			if (catalogue != null)
			{
				catalogue.SetSelectedId(null);
			}

			RebuildItems();
			EncounterCataloguePreview.Hide();
		}

		private static void RebuildItems()
		{
			if (catalogue == null)
			{
				return;
			}

			thumbGeneration++;
			List<UiCatalogueItem> items = new List<UiCatalogueItem>();
			charactersById.Clear();
			if (source == EncounterFileModel.SourceCharacter)
			{
				List<ProjectReference> rows = ProjectBrowser.ListProjectReferences(LokrLabApi.LokrLabApi.CharacterTypeId);
				for (int i = 0; i < rows.Count; i++)
				{
					ProjectReference row = rows[i];
					if (row == null || string.IsNullOrEmpty(row.ProjectId))
					{
						continue;
					}

					charactersById[row.ProjectId] = row;
					items.Add(new UiCatalogueItem
					{
						Id = row.ProjectId,
						Name = string.IsNullOrEmpty(row.DisplayName) ? row.ProjectId : row.DisplayName
					});
				}
			}
			else
			{
				AbilityUsage.EnsureDefinitionsLoaded();
				foreach (KeyValuePair<string, UnitDefinition> entry in CharacterAPI.KnownUnitDefinitions)
				{
					string id = entry.Key;
					if (!EncounterCatalogueRules.IsLikelyVisualUnit(id))
					{
						continue;
					}

					items.Add(new UiCatalogueItem
					{
						Id = id,
						Name = AbilityUsage.DisplayName(id)
					});
				}

				items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			}

			catalogue.SetItems(items);
		}

		private static void RequestThumb(UiCatalogueItem item)
		{
			if (item == null || catalogue == null)
			{
				return;
			}

			string id = item.Id;
			int generation = thumbGeneration;
			if (source == EncounterFileModel.SourceCharacter)
			{
				charactersById.TryGetValue(id, out ProjectReference reference);
				EncounterCatalogueThumbnails.RequestCharacter(
					reference ?? new ProjectReference(LokrLabApi.LokrLabApi.CharacterTypeId, id, null, item.Name),
					sprite => ApplyThumb(generation, id, sprite));
				return;
			}

			EncounterCatalogueThumbnails.RequestUnit(id, sprite => ApplyThumb(generation, id, sprite));
		}

		private static void ApplyThumb(int generation, string id, Sprite sprite)
		{
			if (catalogue == null || sprite == null || generation != thumbGeneration)
			{
				return;
			}

			catalogue.SetItemImage(id, sprite);
		}

		private static void OnCardSelected(UiCatalogueItem item)
		{
			errorLabel.SetText(string.Empty);
			PreviewSelected();
		}

		private static void OnCardDropped(UiCatalogueItem item, Vector2 screen)
		{
			if (item == null)
			{
				return;
			}

			errorLabel.SetText(string.Empty);
			EncounterCatalogueDrop.TryDropCombatant(session, source, side, item.Id, screen);
			RefreshCount();
		}

		private static void PreviewSelected()
		{
			UiCatalogueItem item = catalogue != null ? catalogue.SelectedItem : null;
			if (item == null)
			{
				EncounterCataloguePreview.Hide();
				return;
			}

			if (source == EncounterFileModel.SourceCharacter)
			{
				charactersById.TryGetValue(item.Id, out ProjectReference reference);
				EncounterCataloguePreview.ShowCharacter(reference ?? new ProjectReference(
					LokrLabApi.LokrLabApi.CharacterTypeId, item.Id, null, item.Name));
				return;
			}

			EncounterCataloguePreview.ShowUnit(item.Id);
		}

		private static void AddSelected()
		{
			errorLabel.SetText(string.Empty);
			if (session == null)
			{
				errorLabel.SetText("No Encounter project is open.");
				return;
			}

			UiCatalogueItem item = catalogue != null ? catalogue.SelectedItem : null;
			if (item == null)
			{
				errorLabel.SetText("Select a card first.");
				return;
			}

			EncounterCombatantModel added = source == EncounterFileModel.SourceCharacter
				? session.TryAddCharacter(item.Id, side)
				: session.TryAddUnit(item.Id, side);
			if (added == null)
			{
				errorLabel.SetText("Could not add that combatant.");
				return;
			}

			EncounterNodes.AfterCombatantsChanged(session, added.Id);
		}

		private static void RefreshCount()
		{
			int total = session != null && session.File != null && session.File.Combatants != null
				? session.File.Combatants.Count
				: 0;
			if (countLabel != null)
			{
				countLabel.SetText(total + " combatant" + (total == 1 ? "" : "s")
					+ " already added. Empty is legal to save; Play will refuse zero GoodSide.");
			}
		}

		private static void RefreshSourceButtons()
		{
			if (characterButton != null)
			{
				characterButton.SetColor(source == EncounterFileModel.SourceCharacter
					? UiTheme.Default.ButtonColor : UiTheme.Default.RowButtonColor);
			}

			if (unitButton != null)
			{
				unitButton.SetColor(source == EncounterFileModel.SourceUnit
					? UiTheme.Default.ButtonColor : UiTheme.Default.RowButtonColor);
			}
		}

		private static void SyncSideField()
		{
			if (sideField != null)
			{
				sideField.SetValueSilently(side == EncounterFileModel.BadSide ? 1 : 0);
			}
		}

		private static EncounterSession CurrentEncounter(IReadOnlyList<LabNode> nodes)
		{
			LabNode primary = Primary(nodes);
			EncounterSession fromNode = primary != null ? primary.Payload as EncounterSession : null;
			return fromNode ?? LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
		}

		private static LabNode Primary(IReadOnlyList<LabNode> nodes)
		{
			if (nodes == null || nodes.Count == 0)
			{
				return null;
			}

			return LokrLabApi.LokrLabApi.Selection.Primary ?? nodes[0];
		}

		private static string PrimaryKind(IReadOnlyList<LabNode> nodes)
		{
			LabNode primary = Primary(nodes);
			return primary != null ? primary.Kind : null;
		}
	}
}
