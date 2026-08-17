using System.IO;
using Ironhide.Legends.Utils;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Editor chrome (header + status) around the shared tabbed card form.</summary>
	/// <remarks>
	/// Overlay <see cref="Build"/> scrolls. Inspector <see cref="BuildInto"/> must not: the dock
	/// already scrolls, and a nested ScrollRect collapses preferred height.
	/// </remarks>
	internal static class AbilityEditorPanel
	{
		private static AbilityFileModel current;
		private static UiLabel idLabel;
		private static UiLabel statusLabel;

		/// <summary>Builds the Editor screen's content (overlay path).</summary>
		internal static void Build(Transform screenRoot, Font font)
		{
			Transform contentRoot = AbilityLabScene.GetContentRoot(screenRoot);
			UiPanel panel = UiPanel.Create(contentRoot, UiTheme.Default, "Ability Editor");
			panel.Add(BuildForm(panel.ContentParent, includeListChrome: true, envelopeOnly: false).Grow());
		}

		/// <summary>Builds the shared form into a shell inspector (envelope only; cards are in the Library viewport).</summary>
		internal static void BuildInto(Transform parent)
		{
			BuildForm(parent, includeListChrome: false, envelopeOnly: true);
		}

		private static UiStack BuildForm(Transform parent, bool includeListChrome, bool envelopeOnly)
		{
			UiStack section = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: includeListChrome ? 12f : 0f, scrollable: includeListChrome);

			UiStack header = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(header.FixedHeight(28f));
			idLabel = UiLabel.Create(header.ContentTransform, "-");
			header.Add(idLabel.Grow());
			header.Add(LabClipboard.CreateCopyButton(header.ContentTransform, () => current != null ? current.Id : null));
			if (includeListChrome)
			{
				header.Add(UiButton.Create(header.ContentTransform, "‹ Back to List", OnBackClicked, primary: false).FixedWidth(160f));
			}

			header.Add(UiButton.Create(header.ContentTransform, "Save", OnSaveClicked, primary: true).FixedWidth(90f));
			header.Add(UiButton.Create(header.ContentTransform, "Duplicate", OnDuplicateClicked, primary: false).FixedWidth(100f));
			header.Add(UiButton.Create(header.ContentTransform, "Delete", OnDeleteClicked, primary: false).FixedWidth(90f));

			statusLabel = UiLabel.Create(section.ContentTransform, string.Empty, UiTheme.Default, 11);
			section.Add(statusLabel.FixedHeight(40f));

			if (envelopeOnly)
			{
				AbilityEditorForm.BuildEnvelopeOnly(section);
			}
			else
			{
				AbilityEditorForm.Build(section);
			}

			return section;
		}

		/// <summary>Sets the editor status line (used by custom-asset create errors).</summary>
		internal static void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.SetText(text ?? string.Empty);
			}
		}

		/// <summary>Loads an ability file into the form. Shows a status message and leaves the form on whatever it last displayed if the file can't be parsed.</summary>
		internal static void Load(string filePath)
		{
			if (current != null
				&& !string.IsNullOrEmpty(current.SourceFilePath)
				&& !string.Equals(current.SourceFilePath, filePath, System.StringComparison.OrdinalIgnoreCase)
				&& LabSaveUx.IsDirty
				&& !TrySave())
			{
				statusLabel.SetText("Save the current ability before switching.");
				return;
			}

			if (!AbilityKvIO.TryLoad(filePath, out AbilityFileModel model, out string error))
			{
				statusLabel.SetText("Could not load: " + error);
				return;
			}

			current = model;
			idLabel.SetText(current.Id);
			AbilityEditorForm.Bind(current);
			statusLabel.SetText(AbilityValidation.FormatStatus(string.Empty, AbilityValidation.CollectWarnings(current)));
			LabSaveUx.ClearDirty();
		}

		/// <summary>Writes the open ability to disk and reloads lab content when combat is not active.</summary>
		internal static bool TrySave()
		{
			if (current == null)
			{
				LabSaveUx.ClearDirty();
				return true;
			}

			string savePath = current.SourceFilePath;
			if (string.IsNullOrEmpty(savePath))
			{
				string library = CurrentLibraryFolder();
				savePath = library != null ? AbilityLabPaths.AbilityDefinitionPath(library, current.Id) : null;
			}

			if (string.IsNullOrEmpty(savePath))
			{
				if (statusLabel != null)
				{
					statusLabel.SetText("No library folder for this ability.");
				}

				return false;
			}

			if (!AbilityKvIO.TrySave(current, savePath, out string error))
			{
				if (statusLabel != null)
				{
					statusLabel.SetText(error);
				}

				return false;
			}

			string warnings = AbilityValidation.CollectWarnings(current);
			if (!MonoSingleton<LevelManager>.IsInstanceValid)
			{
				CharacterAPI.ReloadResult result = CharacterAPI.ReloadLabContent(
					CharacterAPI.ReloadScope.Abilities | CharacterAPI.ReloadScope.Visuals);
				if (statusLabel != null)
				{
					statusLabel.SetText(AbilityValidation.FormatStatus(
						result.Success ? "Saved and reloaded." : "Saved. Reload failed: " + result.ErrorMessage,
						warnings));
				}
			}
			else if (statusLabel != null)
			{
				statusLabel.SetText(AbilityValidation.FormatStatus("Saved. Reload skipped — active combat.", warnings));
			}

			LabSaveUx.ClearDirty();
			return true;
		}

		private static void OnSaveClicked()
		{
			TrySave();
		}

		private static void OnDuplicateClicked()
		{
			if (current == null)
			{
				return;
			}

			string library = CurrentLibraryFolder();
			if (string.IsNullOrEmpty(library))
			{
				statusLabel.SetText("No library folder for this ability.");
				return;
			}

			string slug = LokrLab.LabSlugIds.SlugFromId(current.Id, "ability") + "_copy";
			string newId = AbilityLabPaths.GenerateNewAbilityId(slug);

			AbilityFileModel copy = current.Clone(newId);
			Directory.CreateDirectory(AbilityLabPaths.AbilityIconsFolder(library, newId));
			string newPath = AbilityLabPaths.AbilityDefinitionPath(library, newId);
			if (!AbilityKvIO.TrySave(copy, newPath, out string error))
			{
				statusLabel.SetText(error);
				return;
			}

			AbilityTemplates.WriteEnglishLoc(library, newId);
			LokrCharacterLoader.LabAliases.SeedSelf(
				AbilityLabPaths.AbilityFolder(library, newId),
				LokrLab.LabSlugIds.SlugFromId(newId, "ability"),
				newId);
			Load(newPath);
			if (!AbilityLabScene.IsOpen)
			{
				LokrLabApi.LokrLabApi.RequestRefresh();
			}
		}

		private static void OnDeleteClicked()
		{
			if (current != null && !string.IsNullOrEmpty(current.Id))
			{
				AbilityListPanel.DeleteAbility(current.Id);
			}

			current = null;
			if (AbilityLabScene.IsOpen)
			{
				AbilityLabScene.BackToList();
			}
			else
			{
				LokrLabApi.LokrLabApi.Selection.Clear();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}
		}

		private static void OnBackClicked()
		{
			AbilityLabScene.BackToList();
		}

		private static string CurrentLibraryFolder()
		{
			if (current != null && !string.IsNullOrEmpty(current.SourceFilePath))
			{
				string abilityFolder = Path.GetDirectoryName(current.SourceFilePath);
				if (!string.IsNullOrEmpty(abilityFolder))
				{
					return Path.GetDirectoryName(abilityFolder);
				}
			}

			return AbilityLabPaths.FirstLibraryFolder();
		}
	}
}
