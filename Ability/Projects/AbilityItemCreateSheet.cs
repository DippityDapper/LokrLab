using LokrLab;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Values collected before minting a new ability folder id.</summary>
	internal sealed class AbilityItemCreateRequest
	{
		/// <summary>Display name written to SKILL_*_NAME.</summary>
		internal string Name = string.Empty;

		/// <summary>Human stem of the minted folder id.</summary>
		internal string Slug = string.Empty;

		/// <summary>Self-alias key written to aliases.json. Auto-filled from Slug unless Alias Auto is off.</summary>
		internal string Alias = string.Empty;
	}

	/// <summary>Name / slug / alias / Auto / id preview for a new ability folder (not a library).</summary>
	internal static class AbilityItemCreateSheet
	{
		/// <summary>Last committed create form. Consumed by the create modal, then cleared.</summary>
		internal static AbilityItemCreateRequest Pending;

		private static UiTextField nameField;
		private static UiTextField slugField;
		private static UiTextField aliasField;

		/// <summary>Builds the ability identity fields under a New Ability modal.</summary>
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

			LabSlugCreateFields.Build(parent, "Name", "ability", out nameField, out slugField, out aliasField, out _, out _, out _, "ability.create");
		}

		/// <summary>Validates the form and stores <see cref="Pending"/>. Returns an error, or null to create.</summary>
		internal static string Commit()
		{
			string error = LabSlugCreateFields.Validate(nameField, slugField, aliasField, "ability", out string name, out string slug, out string alias);
			if (error != null)
			{
				return error;
			}

			Pending = new AbilityItemCreateRequest
			{
				Name = name,
				Slug = slug,
				Alias = alias
			};
			return null;
		}

		/// <summary>Takes and clears the committed request.</summary>
		internal static AbilityItemCreateRequest TakePending()
		{
			AbilityItemCreateRequest request = Pending;
			Pending = null;
			return request;
		}
	}
}
