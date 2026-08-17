using LokrLab;

namespace LokrAbilityLab.Editor
{
	/// <summary>Fixed option lists for the ability envelope's own closed enums, hand-copied from the decompiled base game (Ironhide.Legends.Model.Game.Units.Abilities.AbilityBehavior/TeamFilter/AOEKind).</summary>
	/// <remarks>Kept as a separate Ability-facing list rather than sharing CharacterLabOptionsAPI. The two catalogs overlap only by accident.</remarks>
	internal static class AbilityEnvelopeOptions
	{
		/// <summary>Every AbilityBehavior flag, shown as togglable checkboxes rather than a closed single-choice list since the real field is a [Flags] enum.</summary>
		internal static readonly string[] BehaviorFlags =
		{
			"MELEE", "UNIT_TARGET", "POINT_TARGET", "SELF_TARGET", "AOE",
			"POSITIVE_EFFECT", "NEGATIVE_EFFECT", "PASSIVE",
			"NEEDS_CLEAR_TERRAIN", "NEEDS_CLEAR_LINE_OF_SIGHT_EXCEPT_UNITS", "NEEDS_VALID_TERRAIN",
			"CINEMATIC", "HAS_CHANCE_TO_HIT", "DOESNT_CONSUME_MOVE", "FAKE_ACTION",
		};

		/// <summary>AbilityTeamFilter closed enum: all / friendly / enemy.</summary>
		internal static readonly string[] TeamFilters = { "TEAM_ALL", "TEAM_FRIENDLY", "TEAM_ENEMY" };

		/// <summary>Every AOEKind the parser accepts, including RANGE_CONE which combat never fills.</summary>
		internal static readonly string[] AOEKinds = { "RANGE_CIRCLE", "RANGE_TUNNEL", "RANGE_CONE" };

		/// <summary>HitAction.ValidateTags whitelist, with a leading # for picker display.</summary>
		/// <remarks>
		/// Dump-wide HitTags includes #RANGED / #SKULL / #TowerCultist* which the parser rejects.
		/// HEX_BLAST_FIRST_TIME is legal at parse and was missing from that dump list.
		/// </remarks>
		internal static string[] HitValidateTags
		{
			get { return LabCatalogRules.HitValidateTags; }
		}

		/// <summary>AOE kinds offered for a new pick. RANGE_CONE is omitted because CalculateAOE / ActOnHexas / PassesFilter leave it empty.</summary>
		internal static string[] SelectableAOEKinds
		{
			get { return LabCatalogRules.SelectableAoeKinds; }
		}

		/// <summary>Dropdown options that keep a loaded RANGE_CONE value visible instead of rewriting it to RANGE_CIRCLE.</summary>
		internal static string[] DropdownAOEKinds(string currentKind)
		{
			return currentKind == "RANGE_CONE" ? AOEKinds : SelectableAOEKinds;
		}

		/// <summary>True when <paramref name="token"/> (with or without #) is on HitAction.ValidateTags.</summary>
		internal static bool IsLegalHitTag(string token)
		{
			return LabCatalogRules.IsLegalHitTag(token);
		}
	}
}
