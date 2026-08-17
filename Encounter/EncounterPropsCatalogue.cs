using System.Collections.Generic;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Persistent Props-folder inspector: deco catalogue, prefab preview, Add.</summary>
	internal static class EncounterPropsCatalogue
	{
		private static UiStack root;
		private static UiLabel countLabel;
		private static Transform previewHole;
		private static UiCatalogue catalogue;
		private static UiLabel errorLabel;
		private static EncounterSession session;
		private static int thumbGeneration;

		/// <summary>True when the Props folder is the primary selection.</summary>
		internal static bool Matches(IReadOnlyList<LabNode> nodes)
		{
			return PrimaryKind(nodes) == EncounterNodes.PropsKind;
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
			root.Add(UiLabel.Create(root.ContentTransform, "Props", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			countLabel = UiLabel.Create(root.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			root.Add(countLabel.FixedHeight(36f));
			root.Add(UiLabel.Create(root.ContentTransform,
				"Select a deco for preview. Drag onto the board to place, or Add / double-click to append. Place does not block walk.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(48f));

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
			EncounterCataloguePreview.Attach(previewHole);
			RebuildItems();
			PreviewSelected();
		}

		/// <summary>Hides the form and the preview camera.</summary>
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
			previewHole = null;
			catalogue = null;
			errorLabel = null;
			session = null;
		}

		private static void RebuildItems()
		{
			if (catalogue == null)
			{
				return;
			}

			thumbGeneration++;
			IReadOnlyList<string> names = EncounterPropCatalog.ListNames();
			List<UiCatalogueItem> items = new List<UiCatalogueItem>(names.Count);
			for (int i = 0; i < names.Count; i++)
			{
				string name = names[i];
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}

				items.Add(new UiCatalogueItem
				{
					Id = name,
					Name = EncounterPropCatalog.Label(name)
				});
			}

			catalogue.SetItems(items);
			if (names.Count == 0)
			{
				errorLabel.SetText("No deco prefabs in the scenario bundle yet. Show the board once, then open Props again.");
			}
		}

		private static void RequestThumb(UiCatalogueItem item)
		{
			if (item == null || catalogue == null)
			{
				return;
			}

			string id = item.Id;
			int generation = thumbGeneration;
			EncounterCatalogueThumbnails.RequestProp(id, sprite =>
			{
				if (catalogue != null && sprite != null && generation == thumbGeneration)
				{
					catalogue.SetItemImage(id, sprite);
				}
			});
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
			EncounterCatalogueDrop.TryDropProp(session, item.Id, screen);
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

			EncounterCataloguePreview.ShowProp(item.Id);
		}

		private static void AddSelected()
		{
			errorLabel.SetText(string.Empty);
			if (session == null || session.File == null)
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

			EncounterPropModel added = EncounterPropRules.Add(session.File, item.Id);
			if (added == null)
			{
				errorLabel.SetText("Could not add that prop.");
				return;
			}

			EncounterNodes.AfterPropsChanged(session, added.Id);
			LokrLab.Lab.SetStatus("Select the prop, then tap a hex. Props do not block walk.");
		}

		private static void RefreshCount()
		{
			int total = session != null && session.File != null && session.File.Props != null
				? session.File.Props.Count
				: 0;
			if (countLabel != null)
			{
				countLabel.SetText(total + " prop" + (total == 1 ? "" : "s")
					+ " already added. Select one, then Place on the board.");
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
