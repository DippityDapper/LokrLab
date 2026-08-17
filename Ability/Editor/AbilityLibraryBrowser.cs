using System;
using System.Collections.Generic;
using System.IO;
using LokrAbilityLab.Projects;
using LokrLab;
using LokrLabApi;
using LokrModAPI;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Filterable ability grid for the Library viewport when the library root is selected.</summary>
	internal static class AbilityLibraryBrowser
	{
		private enum Chip
		{
			All,
			Melee,
			Ranged,
			Passive,
			Aoe
		}

		private static UiTextField searchField;
		private static Chip chip = Chip.All;
		private static readonly List<UiButton> chipButtons = new List<UiButton>();
		private static UiStack gridHost;
		private static string libraryFolder;
		private static List<AbilityFileModel> cached = new List<AbilityFileModel>();

		internal static void Build(UiStack host, string folder)
		{
			host.Clear();
			libraryFolder = folder;
			chipButtons.Clear();
			ReloadCache();

			host.Add(UiLabel.Create(host.ContentTransform, "Library").FixedHeight(22f));
			host.Add(UiLabel.Create(host.ContentTransform,
				"Filter and open an ability. The Inspector shows the envelope; this viewport holds the action cards.",
				UiTheme.Default, 11).FixedHeight(32f));

			searchField = UiTextField.Create(host.ContentTransform, string.Empty);
			searchField.InputField.onValueChanged.AddListener(_ => RefreshGrid());
			host.Add(searchField.FixedHeight(28f));

			UiStack chips = UiStack.Horizontal(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			host.Add(chips.FixedHeight(28f));
			AddChip(chips, "All", Chip.All);
			AddChip(chips, "Melee", Chip.Melee);
			AddChip(chips, "Ranged", Chip.Ranged);
			AddChip(chips, "Passive", Chip.Passive);
			AddChip(chips, "AOE", Chip.Aoe);
			PaintChips();

			gridHost = UiStack.Vertical(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f, scrollable: true);
			host.Add(gridHost.Grow());
			RefreshGrid();
		}

		private static void AddChip(UiStack row, string label, Chip value)
		{
			Chip picked = value;
			UiButton button = UiButton.Create(row.ContentTransform, label, () =>
			{
				chip = picked;
				PaintChips();
				RefreshGrid();
			}, primary: false);
			row.Add(button.Grow());
			chipButtons.Add(button);
		}

		private static void PaintChips()
		{
			Chip[] order = { Chip.All, Chip.Melee, Chip.Ranged, Chip.Passive, Chip.Aoe };
			for (int i = 0; i < chipButtons.Count && i < order.Length; i++)
			{
				chipButtons[i].Image.color = chip == order[i] ? UiTheme.Default.ButtonColor : UiTheme.Default.RowButtonColor;
			}
		}

		private static void ReloadCache()
		{
			cached = new List<AbilityFileModel>();
			if (string.IsNullOrEmpty(libraryFolder))
			{
				return;
			}

			foreach ((string _, string id) in AbilityLabPaths.EnumerateAbilitiesIn(libraryFolder))
			{
				string path = AbilityLabPaths.AbilityDefinitionPath(libraryFolder, id);
				if (AbilityKvIO.TryLoad(path, out AbilityFileModel model, out _))
				{
					cached.Add(model);
				}
			}

			cached.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
		}

		private static void RefreshGrid()
		{
			if (gridHost == null)
			{
				return;
			}

			gridHost.Clear();
			string filter = searchField != null ? searchField.InputField.text.Trim() : string.Empty;
			int shown = 0;
			foreach (AbilityFileModel model in cached)
			{
				if (!Matches(model, filter))
				{
					continue;
				}

				gridHost.Add(BuildCard(gridHost.ContentTransform, model));
				shown++;
			}

			if (shown == 0)
			{
				gridHost.Add(UiLabel.Create(gridHost.ContentTransform, "No abilities match.").FixedHeight(22f));
			}
		}

		private static bool Matches(AbilityFileModel model, string filter)
		{
			if (!string.IsNullOrEmpty(filter) && model.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}

			switch (chip)
			{
				case Chip.Melee:
					return model.BehaviorFlags.Contains("MELEE");
				case Chip.Ranged:
					return AbilityUsage.HasProjectile(model);
				case Chip.Passive:
					return model.IsPassive;
				case Chip.Aoe:
					return model.BehaviorFlags.Contains("AOE");
				default:
					return true;
			}
		}

		private static UiElement BuildCard(Transform parent, AbilityFileModel model)
		{
			UiStack card = UiStack.Horizontal(parent, UiTheme.Default, spacing: 8f, padding: 6f);
			Sprite icon = LoadIcon(model);
			if (icon != null)
			{
				card.Add(UiImage.Create(card.ContentTransform, icon).FixedWidth(40f).FixedHeight(40f));
			}

			UiStack text = UiStack.Vertical(card.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f);
			card.Add(text.Grow());
			text.Add(UiLabel.Create(text.ContentTransform, model.Id).FixedHeight(18f));
			text.Add(UiLabel.Create(text.ContentTransform, Summary(model), UiTheme.Default, 11).FixedHeight(18f));

			List<string> used = AbilityUsage.CharactersUsing(model.Id);
			if (used.Count > 0)
			{
				string who = used.Count <= 3 ? string.Join(", ", used.ToArray()) : used.Count + " characters";
				text.Add(UiLabel.Create(text.ContentTransform, "Used by " + who, UiTheme.Default, 11).FixedHeight(16f));
			}

			card.Add(UiButton.Create(card.ContentTransform, "Open", () => Open(model.Id), primary: true).FixedWidth(72f));
			if (!LabSlugIds.LooksLikeSlugTokenId(model.Id))
			{
				string leftoverId = model.Id;
				card.Add(UiButton.Create(card.ContentTransform, "Rename", () => AbilityItemRenameModal.Show(libraryFolder, leftoverId, null), primary: false).FixedWidth(72f));
			}

			return card.FixedHeight(used.Count > 0 ? 64f : 52f);
		}

		private static string Summary(AbilityFileModel model)
		{
			List<string> bits = new List<string>();
			if (model.BehaviorFlags.Count > 0)
			{
				bits.Add(string.Join(" · ", model.BehaviorFlags.ToArray()));
			}

			if (!string.IsNullOrEmpty(model.CastRange))
			{
				bits.Add("range " + model.CastRange);
			}

			if (AbilityUsage.HasProjectile(model))
			{
				bits.Add("projectile");
			}

			return bits.Count == 0 ? "ability" : string.Join("  ", bits.ToArray());
		}

		private static void Open(string abilityId)
		{
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (host != null && host.SelectNodeById != null && host.SelectNodeById("ability:" + abilityId))
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session != null)
			{
				LokrLabApi.LokrLabApi.JumpToProject(LokrLabApi.LokrLabApi.AbilityLibraryTypeId, session.FolderPath, "ability:" + abilityId);
			}
		}

		private static Sprite LoadIcon(AbilityFileModel model)
		{
			if (string.IsNullOrEmpty(model.Icon) || string.IsNullOrEmpty(libraryFolder) || ModAPI.Assets == null)
			{
				return null;
			}

			string path = Path.Combine(AbilityLabPaths.AbilityIconsFolder(libraryFolder, model.Id), model.Icon + ".png");
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				return ModAPI.Assets.LoadSprite(path);
			}
			catch
			{
				return null;
			}
		}
	}
}
