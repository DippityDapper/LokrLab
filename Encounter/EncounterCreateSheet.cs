using System.Collections.Generic;
using LokrCharacterLoader;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Values collected before minting a new Encounter folder id.</summary>
	internal sealed class EncounterCreateRequest
	{
		/// <summary>Display name written to project.json.</summary>
		internal string Name = string.Empty;

		/// <summary>Human stem of the minted folder id.</summary>
		internal string Slug = string.Empty;

		/// <summary>Self-alias key written to aliases.json.</summary>
		internal string Alias = string.Empty;
	}

	/// <summary>Encounter New Project sheet: name, slug, alias, Auto, and blank-floor note.</summary>
	internal static class EncounterCreateSheet
	{
		/// <summary>Last committed create form. Consumed by CreateNew, then cleared.</summary>
		internal static EncounterCreateRequest Pending;

		private static UiTextField nameField;
		private static UiTextField slugField;
		private static UiTextField aliasField;

		/// <summary>Builds the encounter init fields under the shell's New Project wizard.</summary>
		internal static void Build(Transform parent)
		{
			Pending = null;
			nameField = null;
			slugField = null;
			aliasField = null;
			if (parent == null)
			{
				return;
			}

			LokrLab.LabSlugCreateFields.Build(parent, "Name", "encounter", out nameField, out slugField, out aliasField, out _, out _, out _, "encounter.create");
			UiTheme theme = UiTheme.Default;
			UiLabel blank = UiLabel.Create(parent, "Starts with a blank floor. Unblock hexes, then paint tiles from the Node Tree.",
				theme, 11, TextAnchor.UpperLeft);
			blank.FixedHeight(36f);
			LabHoverInfo.Bind(blank.GameObject, "encounter.create.Blank");
		}

		/// <summary>Validates the form and stores <see cref="Pending"/>. Returns an error, or null to create.</summary>
		internal static string Commit()
		{
			string error = LokrLab.LabSlugCreateFields.Validate(nameField, slugField, aliasField, "encounter", out string name, out string slug, out string alias);
			if (error != null)
			{
				return error;
			}

			Pending = new EncounterCreateRequest
			{
				Name = name,
				Slug = slug,
				Alias = alias
			};
			return null;
		}

		/// <summary>Takes and clears the committed request.</summary>
		internal static EncounterCreateRequest TakePending()
		{
			EncounterCreateRequest request = Pending;
			Pending = null;
			return request;
		}

		/// <summary>Writes the self-alias when the create sheet supplied one.</summary>
		internal static void WriteAlias(string folder, string id, string alias)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(alias))
			{
				return;
			}

			Dictionary<string, string> map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
			{
				[alias] = id
			};
			LabAliases.Save(folder, map);
		}
	}
}
