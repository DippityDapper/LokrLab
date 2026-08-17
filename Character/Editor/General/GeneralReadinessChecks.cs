using System.Collections.Generic;
using System.IO;
using LokrModAPI;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>The identity/roster-registration checklist rows General itself owns. Every file-existence check reads disk directly (never RigEditorScene's live state, never CharacterAPI's already-parsed-at-boot data), since General can show a character never opened in the Animator this session.</summary>
	internal static class GeneralReadinessChecks
	{
		/// <summary>Registers the identity/roster-registration checks.</summary>
		internal static void RegisterDefaults()
		{
			CharacterReadinessRegistry.RegisterCheck("Character ID", CheckCharacterId);
			CharacterReadinessRegistry.RegisterCheck("Character name", CheckCharacterName);
			CharacterReadinessRegistry.RegisterCheck("Description", CheckDescription);
			CharacterReadinessRegistry.RegisterCheck("Unit definition entry", CheckUnitDefinitionEntry);
			CharacterReadinessRegistry.RegisterCheck("Roster entry", CheckRosterEntry);
			CharacterReadinessRegistry.RegisterCheck("Localization entry", CheckLocalizationEntry);
			CharacterReadinessRegistry.RegisterCheck("Roster banner", CheckRosterBanner);
			CharacterReadinessRegistry.RegisterCheck("Map token", CheckMapToken);
			CharacterReadinessRegistry.RegisterCheck("Stats", CheckStats);
			CharacterReadinessRegistry.RegisterCheck("Level 1 skill options", CheckSkillProgression);
			CharacterReadinessRegistry.RegisterCheck("Default skill", CheckDefaultSkill);
		}

		private static IEnumerable<ReadinessItem> CheckCharacterId(string folder, CharacterProfile profile)
		{
			if (string.IsNullOrEmpty(profile.Id))
			{
				yield return new ReadinessItem("No character ID — this shouldn't be possible for a character created through General; try reloading it.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckCharacterName(string folder, CharacterProfile profile)
		{
			if (string.IsNullOrEmpty(profile.Name))
			{
				yield return new ReadinessItem("No character name set — the base game reads this for on-screen display.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckDescription(string folder, CharacterProfile profile)
		{
			if (string.IsNullOrEmpty(profile.Description))
			{
				yield return new ReadinessItem("No description set — cosmetic only, blank is fine.", ReadinessSeverity.Warning);
			}
		}

		private static IEnumerable<ReadinessItem> CheckUnitDefinitionEntry(string folder, CharacterProfile profile)
		{
			string path = Path.Combine(folder, "definition", "rlheroes.txt");
			if (!File.Exists(path))
			{
				yield return new ReadinessItem("No unit definition (definition\\rlheroes.txt is missing) — the hero doesn't exist as a data entity to the base game yet.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckRosterEntry(string folder, CharacterProfile profile)
		{
			if (profile.EntityType != CharacterEntityType.Hero)
			{
				yield break;
			}
			string path = Path.Combine(folder, "roster.json");
			if (!File.Exists(path))
			{
				yield return new ReadinessItem("No roster entry (roster.json is missing) — this is literally what \"selectable\" means; without it the character is never offered to the player.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckLocalizationEntry(string folder, CharacterProfile profile)
		{
			string path = Path.Combine(folder, "localization_en_US.txt");
			string locStem = profile.IsVanillaOverride
				? VanillaOverrideRules.LocStem(profile.VanillaNameStem, profile.VanillaSourceUniqueId)
				: profile.Id;
			if (!File.Exists(path) || !File.ReadAllText(path).Contains("UNIT_" + locStem + "_NAME_0001"))
			{
				yield return new ReadinessItem("No localization entry for the display name — unverified whether the base game actually crashes without one, flagged as an error pending a real end-to-end test (see docs/roadmaps/phasing.md).", ReadinessSeverity.Error);
			}
		}

		/// <remarks>CharacterAPI.RegisterPortraitResolver's "BANNER" slot is what actually renders this (DataHelper.LoadHeroBanner keys off the hero's own id, not rlheroes.txt's "Icon" field, which turns out to be unread by the base game) — see docs/roadmaps/completed/full-port/gaps.md.</remarks>
		private static IEnumerable<ReadinessItem> CheckRosterBanner(string folder, CharacterProfile profile)
		{
			if (profile.EntityType != CharacterEntityType.Hero)
			{
				yield break;
			}
			if (!File.Exists(CharacterPortraitsPanel.SlotPath(profile.Id, "BANNER")))
			{
				yield return new ReadinessItem("No custom roster banner (Properties' Portraits panel, BANNER slot) — falls back to whichever base-game hero's art \"Icon\" happens to name in rlheroes.txt.", ReadinessSeverity.Warning);
			}
		}

		/// <remarks>CharacterAPI.RegisterPortraitResolver's "MAPMINI" slot plus ExoSkeletonDataPatches is what actually renders this on the map party token — see docs/roadmaps/completed/full-port/gaps.md.</remarks>
		private static IEnumerable<ReadinessItem> CheckMapToken(string folder, CharacterProfile profile)
		{
			if (profile.EntityType != CharacterEntityType.Hero)
			{
				yield break;
			}
			if (!File.Exists(CharacterPortraitsPanel.SlotPath(profile.Id, "MAPMINI")))
			{
				yield return new ReadinessItem("No custom map-token art (Properties' Portraits panel, MAPMINI slot) — falls back to whichever base-game hero's party-token part \"UnitOnMap\" happens to name in rlheroes.txt.", ReadinessSeverity.Warning);
			}
		}

		private static IEnumerable<ReadinessItem> CheckStats(string folder, CharacterProfile profile)
		{
			if (profile.Levels.Count == 0 || profile.Levels[0].Stats.Count == 0)
			{
				yield return new ReadinessItem("No stats defined on Level 1 (Properties' Level Properties category) — rlheroes.txt's stats block would be written empty, which the base game almost certainly won't combat-simulate correctly.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckSkillProgression(string folder, CharacterProfile profile)
		{
			if (profile.EntityType != CharacterEntityType.Hero)
			{
				yield break;
			}

			bool has1 = false;
			bool has2 = false;
			bool has3 = false;
			for (int i = 0; i < profile.SkillProgression.Count; i++)
			{
				LevelSkillEntry entry = profile.SkillProgression[i];
				if (entry.SkillIds.Count == 0)
				{
					continue;
				}

				if (entry.Level == 1)
				{
					has1 = true;
				}
				else if (entry.Level == 2)
				{
					has2 = true;
				}
				else if (entry.Level == 3)
				{
					has3 = true;
				}
			}

			if (!has1 || !has2 || !has3)
			{
				yield return new ReadinessItem("skillProgression must have ranks 1, 2, and 3 — the hero room indexes all three and throws KeyNotFoundException if any is missing. New characters get placeholder_skill in a 2 / 3 / 3 layout.", ReadinessSeverity.Error);
			}
		}

		private static IEnumerable<ReadinessItem> CheckDefaultSkill(string folder, CharacterProfile profile)
		{
			if (profile.EntityType != CharacterEntityType.Hero)
			{
				yield break;
			}

			if (string.IsNullOrEmpty(profile.DefaultSkill))
			{
				yield return new ReadinessItem("No defaultSkill (basic attack) — new characters get placeholder_attack. Pick a real attack id that is not also listed in skillProgression.", ReadinessSeverity.Error);
			}
		}
	}
}
