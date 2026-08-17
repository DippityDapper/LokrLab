using System;
using System.Collections.Generic;

namespace LokrLab
{
	/// <summary>Unity-free rules for a Lab folder that replaces a shipped hero in place.</summary>
	/// <remarks>
	/// Engine UniqueId / block keys / loc stem stay vanilla so saves and the campaign still
	/// see Gerald. The Lab folder is always a minted <c>slug_token</c> so two Gerald overrides
	/// do not share a directory. MetaExo is that folder id once a Lab rig exists, otherwise the
	/// shipped exo. See docs/roadmaps/started/vanilla-character-edit.md.
	/// </remarks>
	internal static class VanillaOverrideRules
	{
		/// <summary>True when <paramref name="vanillaSourceUniqueId"/> marks an override project.</summary>
		internal static bool IsOverride(string vanillaSourceUniqueId)
		{
			return !string.IsNullOrEmpty(vanillaSourceUniqueId);
		}

		/// <summary>KV Name stem used for UNIT_* loc keys. Prefers the shipped Name field over UniqueId.</summary>
		internal static string LocStem(string unitNameField, string uniqueId)
		{
			if (!string.IsNullOrEmpty(unitNameField))
			{
				return unitNameField;
			}

			return uniqueId ?? string.Empty;
		}

		/// <summary>Block key at a 0-based level index, or a UniqueId / UniqueId_LvlN fallback.</summary>
		internal static string BlockKeyAt(IReadOnlyList<string> vanillaBlockKeys, int levelIndex, string uniqueId)
		{
			if (vanillaBlockKeys != null
				&& levelIndex >= 0
				&& levelIndex < vanillaBlockKeys.Count
				&& !string.IsNullOrEmpty(vanillaBlockKeys[levelIndex]))
			{
				return vanillaBlockKeys[levelIndex];
			}

			if (string.IsNullOrEmpty(uniqueId))
			{
				return string.Empty;
			}

			return levelIndex <= 0 ? uniqueId : uniqueId + "_Lvl" + (levelIndex + 1);
		}

		/// <summary>UniqueId written to rlheroes / roster when the project is an override.</summary>
		internal static string EngineUniqueId(string vanillaSourceUniqueId, string folderId)
		{
			return IsOverride(vanillaSourceUniqueId) ? vanillaSourceUniqueId : folderId;
		}

		/// <summary>MetaExo written to rlheroes: Lab folder id when a reconstructed rig is present, else the shipped exo on override.</summary>
		/// <remarks>
		/// CustomRigLoader indexes by folder name. Animation edits only reach Sandbox and the
		/// campaign when MetaExo is that folder. With no rig.json the shipped units-bundle exo
		/// still resolves.
		/// </remarks>
		internal static string EngineMetaExo(
			string vanillaSourceUniqueId,
			string vanillaMetaExo,
			string folderId,
			bool labRigPresent)
		{
			if (labRigPresent && !string.IsNullOrEmpty(folderId))
			{
				return folderId;
			}

			if (IsOverride(vanillaSourceUniqueId) && !string.IsNullOrEmpty(vanillaMetaExo))
			{
				return vanillaMetaExo;
			}

			return folderId;
		}

		/// <summary>True when a Lab folder already claims this UniqueId as an override.</summary>
		internal static bool FolderClaimsUniqueId(
			string vanillaSourceUniqueId,
			string characterJsonId,
			string rosterId,
			string uniqueId)
		{
			if (string.IsNullOrEmpty(uniqueId))
			{
				return false;
			}

			return string.Equals(vanillaSourceUniqueId, uniqueId, StringComparison.Ordinal)
				|| string.Equals(characterJsonId, uniqueId, StringComparison.Ordinal)
				|| string.Equals(rosterId, uniqueId, StringComparison.Ordinal);
		}
	}
}
