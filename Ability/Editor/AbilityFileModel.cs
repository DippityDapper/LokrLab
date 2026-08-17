using System.Collections.Generic;

namespace LokrAbilityLab.Editor
{
	/// <summary>One ability: structured envelope, typed body tree, and opaque remainder.</summary>
	internal sealed class AbilityFileModel
	{
		/// <summary>The KV block key / folder name (Abilities/&lt;Id&gt;/ability.txt). Fixed at creation.</summary>
		internal string Id = string.Empty;

		internal List<string> BehaviorFlags = new List<string> { "UNIT_TARGET" };
		internal string LocalizationId = string.Empty;
		internal string TeamFilter = "TEAM_ENEMY";
		internal string CastRange = string.Empty;
		internal string CastMinRange = string.Empty;
		internal string Cooldown = "0";
		internal string PrewarmCooldown = string.Empty;
		internal string APCost = string.Empty;
		internal string CanExecute = string.Empty;
		internal string HitChanceModifier = string.Empty;

		internal string AOEKind = "RANGE_CIRCLE";
		internal string AOETeamFilter = "TEAM_ENEMY";
		internal string AOERange = string.Empty;
		internal string AOEMinRange = string.Empty;
		internal string AOEWidth = string.Empty;
		internal bool AOECenterOnCaster;
		internal bool AOEAffectsCaster;

		internal string Icon = string.Empty;
		internal string AnimationId = string.Empty;
		internal string CastFXId = string.Empty;

		/// <summary>Events, modifiers, AbilitySpecial, AI, and unrecognized top-level blocks.</summary>
		internal AbilityBody Body = new AbilityBody();

		/// <summary>Absolute path this was loaded from / will be saved to.</summary>
		internal string SourceFilePath;

		internal bool IsPassive => BehaviorFlags != null && BehaviorFlags.Contains("PASSIVE");

		/// <summary>Deep copy for Duplicate. SourceFilePath is left empty so save writes a new file.</summary>
		internal AbilityFileModel Clone(string newId)
		{
			return new AbilityFileModel
			{
				Id = newId,
				BehaviorFlags = new List<string>(BehaviorFlags),
				LocalizationId = LocalizationId,
				TeamFilter = TeamFilter,
				CastRange = CastRange,
				CastMinRange = CastMinRange,
				Cooldown = Cooldown,
				PrewarmCooldown = PrewarmCooldown,
				APCost = APCost,
				CanExecute = CanExecute,
				HitChanceModifier = HitChanceModifier,
				AOEKind = AOEKind,
				AOETeamFilter = AOETeamFilter,
				AOERange = AOERange,
				AOEMinRange = AOEMinRange,
				AOEWidth = AOEWidth,
				AOECenterOnCaster = AOECenterOnCaster,
				AOEAffectsCaster = AOEAffectsCaster,
				Icon = Icon,
				AnimationId = AnimationId,
				CastFXId = CastFXId,
				Body = Body != null ? Body.Clone() : new AbilityBody(),
			};
		}
	}
}
