using System;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Per-frame sockets and animation events combat actually looks up on a swapped custom rig.</summary>
	/// <remarks>
	/// After UnitViewExoSkeletonPatches swaps the custom ExoSkeletonDataAsset onto the Model prefab, attach points
	/// and frame events come from that asset's current pose -- not from ObeliskLvl4/HumanArcher child transforms.
	/// AttachPointContainerExoSkeleton reads exoSkeletonData.attachPoints by name (vanilla frames always carry
	/// Head/Chest/Base). AbilityMeleeActivity only fires OnAbilityAction / ends the activity when the playing clip
	/// raises the exo-skeleton events "AbilityAction" and "AbilityEnd"; a missing AbilityAction is why an attack
	/// can play its animation and spend AP but never spawn its projectile. ExoSkeletonUnitAnimationController also
	/// names "AbilityStart" (the comparisons there are no-ops); any other string is forwarded as
	/// OnAbilityCustomEvent. KnownEventNames is that closed combat trio for the Frame inspector combobox; the
	/// combobox still accepts typed custom names. DeathActivity also waits on AbilityEnd with an empty Update --
	/// a Death clip with no AbilityEnd event freezes the encounter. Dumped 2026-08-12 from HumanRanger
	/// attach-point names and AbilityMeleeActivity.HandleViewEvent / DeathActivity.HandleViewEvent /
	/// ExoSkeletonUnitAnimationController.HandleAnimationEvent.
	/// </remarks>
	internal static class CombatPlaybackRequirements
	{
		/// <summary>Vanilla combat/dialog sockets every shipped exo frame carries -- Head (speech bubble + many SourcePos), Chest (projectile TargetPos), Base (ground FX).</summary>
		internal static readonly string[] AttachPointNames = { "Head", "Chest", "Base" };

		/// <summary>Exo-skeleton frame event named alongside AbilityAction/AbilityEnd on ExoSkeletonUnitAnimationController -- the comparisons there are no-ops; include it because it is the third of that combat trio.</summary>
		internal const string AbilityStartEvent = "AbilityStart";

		/// <summary>Exo-skeleton frame event AbilityMeleeActivity.HandleViewEvent maps to ability.OnEvent("OnAbilityAction").</summary>
		internal const string AbilityActionEvent = "AbilityAction";

		/// <summary>Exo-skeleton frame event AbilityMeleeActivity and DeathActivity HandleViewEvent map to ending the activity.</summary>
		internal const string AbilityEndEvent = "AbilityEnd";

		/// <summary>The combat event names ExoSkeletonUnitAnimationController / AbilityMeleeActivity actually mention -- Frame inspector combobox suggestions; custom strings still typeable for OnAbilityCustomEvent.</summary>
		internal static readonly string[] KnownEventNames =
		{
			AbilityStartEvent,
			AbilityActionEvent,
			AbilityEndEvent,
		};

		/// <summary>True for clips abilities actually PlayAnimation as AnimationID -- Attack/SpecialAttack/SpellCast variants, not idle/walk. These need both AbilityAction (projectile) and AbilityEnd (activity finishes).</summary>
		internal static bool NeedsCombatEvents(string clipName)
		{
			if (string.IsNullOrEmpty(clipName))
			{
				return false;
			}
			return clipName.StartsWith("Attack", StringComparison.Ordinal)
				|| clipName.StartsWith("SpecialAttack", StringComparison.Ordinal)
				|| clipName.StartsWith("SpellCast", StringComparison.Ordinal);
		}

		/// <summary>True for clips whose activity never finishes without an AbilityEnd frame event -- the attack/spell set plus Death. DeathActivity.Update is empty, so a Death clip with no AbilityEnd freezes the encounter.</summary>
		internal static bool NeedsAbilityEndEvent(string clipName)
		{
			return NeedsCombatEvents(clipName)
				|| string.Equals(clipName, "Death", StringComparison.Ordinal);
		}
	}
}
