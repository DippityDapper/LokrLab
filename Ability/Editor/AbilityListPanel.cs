using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>The List screen: a browsable, searchable list of every ability in this plugin's own library, plus create/delete.</summary>
	/// <remarks>Overlay fallback: lists abilities from every library under AbilityLabPaths.LibrariesRoot. Create without an open library uses the first library. No search/filter widget exists in SimpleUI -- hand-built here via UiTextField.InputField.onValueChanged (OnEndEdit-only) driving UiList&lt;T&gt;.SetItems on every keystroke.</remarks>
	internal static class AbilityListPanel
	{
		private static UiTextField searchField;
		private static UiDropdown templateField;
		private static UiList<string> list;
		private static UiLabel statusLabel;
		private static List<string> allIds = new List<string>();

		/// <summary>Builds the List screen's content.</summary>
		internal static void Build(Transform screenRoot, Font font)
		{
			Transform contentRoot = AbilityLabScene.GetContentRoot(screenRoot);
			UiPanel panel = UiPanel.Create(contentRoot, UiTheme.Default, "Abilities");
			UiStack section = UiStack.Vertical(panel.ContentParent, UiTheme.Default, spacing: 6f, padding: 12f);
			panel.Add(section);

			searchField = UiTextField.Create(section.ContentTransform, string.Empty);
			searchField.InputField.onValueChanged.AddListener(_ => ApplyFilter());
			section.Add(searchField.FixedHeight(30f));

			UiStack createRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(createRow.FixedHeight(30f));
			templateField = UiDropdown.Create(createRow.ContentTransform, AbilityTemplates.Labels);
			templateField.SetValueSilently(AbilityTemplates.IndexOf(AbilityTemplates.SelectedId));
			templateField.OnValueChanged(index =>
			{
				if (index >= 0 && index < AbilityTemplates.Ids.Length)
				{
					AbilityTemplates.SelectedId = AbilityTemplates.Ids[index];
				}
			});
			createRow.Add(templateField.Grow());
			createRow.Add(UiButton.Create(createRow.ContentTransform, "New Ability", OnCreateClicked, primary: true).FixedWidth(140f));

			statusLabel = UiLabel.Create(section.ContentTransform, string.Empty);
			section.Add(statusLabel.FixedHeight(20f));

			list = UiList<string>.Create(section.ContentTransform, spacing: 4f, padding: 0f, scrollable: true);
			section.Add(list.Grow());
		}

		/// <summary>Rescans every library and reapplies the current search filter.</summary>
		internal static void Refresh()
		{
			AbilityLabPaths.EnsureFoldersExist();
			allIds = new List<string>();
			foreach ((string _, string id) in AbilityLabPaths.EnumerateAbilities())
			{
				if (!allIds.Contains(id))
				{
					allIds.Add(id);
				}
			}

			allIds.Sort(StringComparer.OrdinalIgnoreCase);
			ApplyFilter();
		}

		private static void ApplyFilter()
		{
			if (list == null)
			{
				return;
			}
			string filter = searchField != null ? searchField.InputField.text.Trim() : string.Empty;
			List<string> filtered = filter.Length == 0
				? allIds
				: allIds.Where(id => id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

			list.SetItems(filtered, id => id, BuildRow);
			statusLabel.SetText(filtered.Count + " of " + allIds.Count + " abilities");
		}

		private static UiElement BuildRow(Transform parent, string id)
		{
			UiStack row = UiStack.Horizontal(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			string library = AbilityLabPaths.FindLibraryFolderForAbility(id);
			string filePath = library != null ? AbilityLabPaths.AbilityDefinitionPath(library, id) : null;
			row.Add(UiButton.Create(row.ContentTransform, id, () =>
			{
				if (!string.IsNullOrEmpty(filePath))
				{
					AbilityLabScene.OpenAbility(filePath);
				}
			}, primary: false).Grow());
			row.Add(UiButton.Create(row.ContentTransform, "x", () => OnDeleteClicked(id), primary: false).FixedWidth(28f));
			return row.FixedHeight(30f);
		}

		/// <summary>Creates a new ability folder and default ability.txt. Returns false and an error when the id is invalid or already exists.</summary>
		internal static bool TryCreateAbility(string libraryFolder, string id, out string error, string displayName = null)
		{
			id = id != null ? id.Trim() : string.Empty;
			if (id.Length == 0)
			{
				error = "Enter an ability id first.";
				return false;
			}
			if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("\""))
			{
				error = "Ability id has invalid characters.";
				return false;
			}
			if (string.IsNullOrEmpty(libraryFolder))
			{
				error = "No ability library is open.";
				return false;
			}
			if (AbilityLabPaths.AbilityIdExists(id))
			{
				error = "An ability with that id already exists.";
				return false;
			}

			if (!AbilityTemplates.TryWrite(libraryFolder, id, AbilityTemplates.SelectedId, out error, displayName))
			{
				return false;
			}

			return true;
		}

		private static void OnCreateClicked()
		{
			string library = AbilityLabPaths.FirstLibraryFolder();
			if (string.IsNullOrEmpty(library))
			{
				statusLabel.SetText("Create an Ability Library in LokrLab first.");
				return;
			}

			LokrAbilityLab.Projects.AbilityItemCreateModal.Show(library, id =>
			{
				Refresh();
				AbilityLabScene.OpenAbility(AbilityLabPaths.AbilityDefinitionPath(library, id));
			});
		}

		/// <summary>Deletes one ability folder (definition, icons, localization) without touching other abilities.</summary>
		/// <remarks>Re-derives the folder from abilityId via FindLibraryFolderForAbility, which assumes the folder name equals the id -- true for every Lab-authored ability, but not for a vanilla Override copy (VanillaAbilityImporter mints the folder name while intentionally keeping the vanilla KV block key inside ability.txt). Callers that already know the exact folder (e.g. from AbilityFileModel.SourceFilePath) should call DeleteAbilityFolder directly instead -- see AbilityEditorPanel.OnDeleteClicked.</remarks>
		internal static void DeleteAbility(string abilityId)
		{
			string library = AbilityLabPaths.FindLibraryFolderForAbility(abilityId);
			if (string.IsNullOrEmpty(library))
			{
				return;
			}

			DeleteAbilityFolder(AbilityLabPaths.AbilityFolder(library, abilityId));
		}

		/// <summary>Deletes the given ability folder outright, without re-deriving it from an id.</summary>
		internal static void DeleteAbilityFolder(string folder)
		{
			if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
			{
				Directory.Delete(folder, true);
			}
		}

		private static void OnDeleteClicked(string id)
		{
			DeleteAbility(id);
			Refresh();
		}
	}
}
