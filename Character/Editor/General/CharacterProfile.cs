using System.Collections.Generic;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Which HeroRoster list a character's roster.json entry belongs in.</summary>
	/// <remarks>Real shipped legends carry a "states":{"LEGEND":"1"} flag the RLHeroes placeholder block doesn't set, so new, blank characters default to the simpler Companion tier; an imported character instead preserves whichever tier its old mod's own HeroRoster filename used.</remarks>
	internal enum CharacterTier
	{
		Companion,
		Legend
	}

	/// <summary>Whether this character is a playable roster hero, or a non-playable enemy/summon unit (e.g. an ability's summoned creature).</summary>
	/// <remarks>Both are authored with the same General/Animator tooling -- an enemy/summon is materially as complex as a real playable unit (stats/states/skills), not a simple prop -- per docs/roadmaps/completed/full-port/gaps.md. The distinction only changes what RLHeroesGenerator writes (no UniqueId/roster-only identity fields, InheritsFrom "Base" not "Hero", no roster.json) and which CharacterAPI.UnitDefinitionsBuilder method the loader contributes through (AddEnemyDefinition vs AddHeroDefinition) -- confirmed via decompiled UnityDefinitionsParser.GetDefinition that a unit definition with no UniqueId and no roster entry is already a normal, fully-supported base-game case, not something needing new engine accommodation.</remarks>
	internal enum CharacterEntityType
	{
		Hero,
		EnemySummon
	}

	/// <summary>One "name" "value" line of a character's rlheroes.txt stats block.</summary>
	/// <remarks>Name is an arbitrary KV key, not a fixed dropdown -- combat only cares that an ability referencing stat(%CASTER, #fieldName) finds a matching entry, the same "resolver-chain, not a closed list" principle this codebase applies elsewhere.</remarks>
	internal sealed class StatEntry
	{
		/// <summary>The stat's KV key (e.g. "health_max", or any custom name an ability references).</summary>
		internal string Name = string.Empty;
		/// <summary>The stat's value, written as an integer in rlheroes.txt when whole.</summary>
		internal float Value;
	}

	/// <summary>One rank of a character's rlheroes.txt archetype chain -- a real, separate top-level KV block in the file, linked to its neighbors via InheritsFrom/nextLevelArchetype.</summary>
	/// <remarks>Level 1 is the base block and always exists (RLHeroesGenerator writes every identity/skills/sound/states field only on it); level 2+ blocks are override-only diffs, carrying just the stats that actually change at that rank plus the chain links -- exactly how real shipped characters like Onagro work (rank-up growth), confirmed 2026-08-11 by round-tripping Onagro's own file through RLHeroesParser/RLHeroesGenerator before trusting this against real data.</remarks>
	internal sealed class CharacterLevel
	{
		/// <summary>This rank's number, 1-based. Levels are generated/parsed in ascending order; the chain's block keys and InheritsFrom/nextLevelArchetype links are always derived from this, never hand-typed.</summary>
		internal int Level;
		/// <summary>This rank's stats -- every field for level 1, only the fields that change at this rank for level 2+.</summary>
		internal List<StatEntry> Stats = new List<StatEntry>();
	}

	/// <summary>One level's worth of a character's rlheroes.txt skillProgression block -- the skill ids granted at that level.</summary>
	internal sealed class LevelSkillEntry
	{
		/// <summary>The level this entry applies to (skillProgression's own numeric block key).</summary>
		internal int Level;
		/// <summary>The skill ids granted at this level.</summary>
		internal List<string> SkillIds = new List<string>();
	}

	/// <summary>One non-English locale's Name/Description override for a character.</summary>
	/// <remarks>English itself lives on CharacterProfile.Name/Description directly, not as an entry here -- see LocaleCodes.AllNonEnglish for the full set of keys this can hold.</remarks>
	internal sealed class CharacterLocalizedText
	{
		/// <summary>This locale's display name. Falls back to the English Name at load if left blank (RLHeroesGenerator still writes whatever's here, even empty).</summary>
		internal string Name = string.Empty;
		/// <summary>This locale's flavor-text description.</summary>
		internal string Description = string.Empty;
	}

	/// <summary>A character's own identity -- everything the General workstation owns directly.</summary>
	/// <remarks>Persisted via CharacterProfileSidecar as character.json, directly inside the character's own folder (CharacterLabPaths.CharacterRigsRoot/&lt;Id&gt;/) alongside its rig/, sprites/, definition/, etc. subfolders. Every field here is real, editable Lab-owned data -- RLHeroesGenerator.Sync always regenerates rlheroes.txt from this profile, for imported and freshly-created characters alike (resolved 2026-08-11; see RLHeroesGenerator's own remarks for the "why" behind that change).</remarks>
	internal sealed class CharacterProfile
	{
		/// <summary>The folder name -- a <c>slug_token</c> id (or a leftover 18-digit id). Set once at creation, never edited afterward.</summary>
		internal string Id = string.Empty;

		/// <summary>The character's display name, shown in-game and throughout the Lab.</summary>
		internal string Name = string.Empty;
		/// <summary>The character's flavor-text description (mirrors the base game's LORE localization line).</summary>
		internal string Description = string.Empty;

		/// <summary>Per-locale Name/Description overrides, keyed by locale-suffix (see LocaleCodes.AllNonEnglish) -- e.g. "es", "zh-Hans". English itself is Name/Description above, not an entry here. Absent from this dictionary means no translation has been authored for that locale yet, same as any other mod-contributed content.</summary>
		internal Dictionary<string, CharacterLocalizedText> Localizations = new Dictionary<string, CharacterLocalizedText>();

		/// <summary>Mirrors the real HeroRoster schema's own "locked" field -- a companion entry with no "locked" key at all is treated as unlocked; an explicit "locked":true gates it behind its unlockAchievement. Defaults locked, since a freshly created character isn't meant to appear unlocked in a live save with no unlock condition wired up yet.</summary>
		internal bool Locked = true;

		/// <summary>The base-game achievement id that unlocks this character, or empty for no achievement gate. Mirrors the old system's own "unlockAchievement" roster field, which most real shipped characters use instead of a plain locked/unlocked boolean; only meaningful while Locked is true.</summary>
		internal string UnlockAchievement = string.Empty;

		/// <summary>Which HeroRoster list this character belongs in. Meaningless while EntityType is EnemySummon (no roster entry is written at all).</summary>
		internal CharacterTier Tier = CharacterTier.Companion;

		/// <summary>Whether this is a playable hero (gets a roster.json entry, contributed via CharacterAPI.AddHeroDefinition) or a non-playable enemy/summon unit (no roster entry, contributed via CharacterAPI.AddEnemyDefinition).</summary>
		internal CharacterEntityType EntityType = CharacterEntityType.Hero;

		/// <summary>True only for a character created via LegacyModImporter. Historical provenance only as of 2026-08-11 -- it no longer changes how RLHeroesGenerator.Sync behaves (every character's rlheroes.txt is always regenerated from this profile's own fields).</summary>
		internal bool ImportedFromLegacyMod;

		/// <summary>Shipped UniqueId this folder replaces (Gerald). Empty for a normal Lab fork.</summary>
		internal string VanillaSourceUniqueId = string.Empty;

		/// <summary>Shipped KV Name stem (GERALD_LIGHTSEEKER) used for UNIT_* loc keys on an override.</summary>
		internal string VanillaNameStem = string.Empty;

		/// <summary>Shipped MetaExo asset name used to reconstruct the Lab rig on extract.</summary>
		internal string VanillaMetaExo = string.Empty;

		/// <summary>Shipped block keys for each rank (RLHumanGeraldLightSeekerLvl1…).</summary>
		internal List<string> VanillaBlockKeys = new List<string>();

		/// <summary>True when this project replaces a shipped hero in place.</summary>
		internal bool IsVanillaOverride => VanillaOverrideRules.IsOverride(VanillaSourceUniqueId);

		/// <summary>Sandbox / GetDefinition lookup: vanilla UniqueId on override, otherwise the folder id.</summary>
		internal string SpawnUnitId => VanillaOverrideRules.EngineUniqueId(VanillaSourceUniqueId, Id);

		/// <summary>This character's archetype chain, one entry per rank -- see CharacterLevel's own remarks. Always at least one entry (level 1).</summary>
		internal List<CharacterLevel> Levels = new List<CharacterLevel>
		{
			new CharacterLevel
			{
				Level = 1,
				Stats = new List<StatEntry>
				{
					new StatEntry { Name = "level", Value = 1f },
					new StatEntry { Name = "health_max", Value = 4f },
					new StatEntry { Name = "armor_max", Value = 0f },
					new StatEntry { Name = "armor_regen_per_turn", Value = 0f },
					new StatEntry { Name = "baseDamage", Value = 4f },
					new StatEntry { Name = "rangedAttackRange", Value = 4f },
					new StatEntry { Name = "rangedAttackMinRange", Value = 2f },
					new StatEntry { Name = "walkSpeed", Value = 2f },
				},
			},
		};

		/// <summary>The "states" KV block on level 1 (LEGEND, combat-immunity flags, behavior flags, etc.) -- present-and-true keys are written, everything else is omitted, matching the base game's own "absent means off" convention.</summary>
		internal Dictionary<string, bool> States = new Dictionary<string, bool>();

		/// <summary>The "Model" KV field -- the vanilla units-bundle prefab combat instantiates as this character's view (UnitDefinition.kind). New characters default HumanArcher; import copies the old file's value.</summary>
		internal string Model = string.Empty;
		/// <summary>The "AttackType" KV field (e.g. "RANGED", "MELEE").</summary>
		internal string AttackType = string.Empty;
		/// <summary>The "Icon" KV field. Unread by the base game (see docs/roadmaps/completed/full-port/gaps.md) -- preserved for round-trip fidelity, not for any rendering effect.</summary>
		internal string Icon = string.Empty;
		/// <summary>The "Background" KV field. Unread by the base game as far as this project's investigation found (see docs/roadmaps/completed/full-port/gaps.md) -- preserved for round-trip fidelity, not for any confirmed rendering effect.</summary>
		internal string Background = string.Empty;
		/// <summary>The "UnitOnMap" KV field -- the ExoSkeleton party-token part name PartyTokenComponent.UpdateHeroes swaps in. Only matters when this character has no MAPMINI portrait override (CharacterPortraitsPanel takes priority when set).</summary>
		internal string UnitOnMap = string.Empty;
		/// <summary>The "PortraitBackgroundColor" KV field -- a 6-digit hex string tinting the map hero-bar portrait background (MapHeroBarPortraitComponent.SetHero).</summary>
		internal string PortraitBackgroundColor = string.Empty;
		/// <summary>The "cinematicTags" KV field as an ordered tag list (written pipe-delimited to rlheroes.txt). Distinct from States -- tags drive cinematic/combat-classification behavior (Hero vs. Minion, damage-type tags, etc.).</summary>
		internal List<string> CinematicTags = new List<string>();
		/// <summary>The "skills" KV block's values (trait/passive skill ids) -- the block's own numeric keys are discarded on parse and regenerated sequentially, since UnityDefinitionsParser.ParseText never reads them either.</summary>
		internal List<string> Skills = new List<string>();
		/// <summary>The "defaultSkill" KV field.</summary>
		internal string DefaultSkill = string.Empty;
		/// <summary>The "skillProgression" KV block, one entry per level that grants new skills.</summary>
		internal List<LevelSkillEntry> SkillProgression = new List<LevelSkillEntry>();
		/// <summary>The "soundConfig" KV block's "assetId" field -- a shared, existing base-game sound-group name.</summary>
		internal string SoundAssetId = string.Empty;
		/// <summary>The "soundConfig" KV block's "sounds" sub-block, combat-event name to sound-clip name.</summary>
		internal Dictionary<string, string> SoundClips = new Dictionary<string, string>();
	}
}
