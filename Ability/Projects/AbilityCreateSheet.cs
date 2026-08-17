using LokrLab;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Values collected before minting a new Ability Library folder id.</summary>
	internal sealed class AbilityLibraryCreateRequest
	{
		/// <summary>Display name written to project.json.</summary>
		internal string Name = string.Empty;

		/// <summary>Human stem of the minted library folder id.</summary>
		internal string Slug = string.Empty;
	}

	/// <summary>Ability Library New Project sheet: display name, slug, and Auto.</summary>
	internal static class AbilityCreateSheet
	{
		/// <summary>Last committed create form. Consumed by CreateNew, then cleared.</summary>
		internal static AbilityLibraryCreateRequest Pending;

		private static UiTextField nameField;
		private static UiTextField slugField;

		/// <summary>Builds the library init fields under the shell's New Project wizard.</summary>
		internal static void Build(Transform parent)
		{
			Pending = null;
			nameField = null;
			slugField = null;
			if (parent == null)
			{
				return;
			}

			LabSlugCreateFields.Build(parent, "Library name", "library", out nameField, out slugField, out _, out _, out _, out _, "ability.library.create");
		}

		/// <summary>Validates the form and stores <see cref="Pending"/>. Returns an error, or null to create.</summary>
		internal static string Commit()
		{
			string error = LabSlugCreateFields.Validate(nameField, slugField, null, "library", out string name, out string slug, out _);
			if (error != null)
			{
				return error == "Enter a name." ? "Enter a library name." : error;
			}

			Pending = new AbilityLibraryCreateRequest
			{
				Name = name,
				Slug = slug
			};
			return null;
		}

		/// <summary>Takes and clears the committed request.</summary>
		internal static AbilityLibraryCreateRequest TakePending()
		{
			AbilityLibraryCreateRequest request = Pending;
			Pending = null;
			return request;
		}
	}
}
