using System;

namespace LokrAbilityLab.Editor
{
	/// <summary>AbilityEvents / ModifierEvents allow-lists copied from the decompiled engine.</summary>
	internal static class AbilityEventNames
	{
		/// <summary>Hats shown by default on the Events tab.</summary>
		internal static readonly string[] DefaultAbilityEvents =
		{
			"OnAbilityStart",
			"OnAbilityAction",
			"OnAbilityCustomEvent",
			"OnProjectileHitUnit",
			"OnProjectileDestinationReached",
			"OnProjectileMissedUnit",
			"OnCustomTargeting",
			"OnThink",
		};

		/// <summary>Every legal ability-level On* name. Unknown OnX throws at game parse.</summary>
		internal static readonly string[] AllAbilityEvents =
		{
			"OnAbilityStart",
			"OnAbilityAction",
			"OnAbilityCustomEvent",
			"OnAttackStart",
			"OnAttackAction",
			"OnProjectileHitUnit",
			"OnProjectileMissedUnit",
			"OnProjectileDestinationReached",
			"OnAttacked",
			"OnCustomTargeting",
			"OnThink",
		};

		/// <summary>AbilityEvents names with no OnEvent / BroadcastEvent / SendEvent site in the engine.</summary>
		internal static readonly string[] DeadAbilityEvents =
		{
			"OnAttackStart",
			"OnAttackAction",
			"OnAttacked",
		};

		/// <summary>AbilityEvents names combat actually dispatches. Add-event menus use this, not AllAbilityEvents.</summary>
		internal static readonly string[] FiredAbilityEvents =
		{
			"OnAbilityStart",
			"OnAbilityAction",
			"OnAbilityCustomEvent",
			"OnProjectileHitUnit",
			"OnProjectileMissedUnit",
			"OnProjectileDestinationReached",
			"OnCustomTargeting",
			"OnThink",
		};

		/// <summary>Common modifier hats. The rest remain addable if they appear in AllModifierEvents.</summary>
		internal static readonly string[] DefaultModifierEvents =
		{
			"OnAdded",
			"OnRemoved",
			"OnTurnStarted",
			"OnTurnFinished",
			"OnPreHit",
			"OnPostHit",
			"OnHitEnd",
			"OnSpawn",
			"OnStartFight",
			"OnVictory",
		};

		/// <summary>Every legal modifier On* name from ModifierEvents.cs.</summary>
		internal static readonly string[] AllModifierEvents =
		{
			"OnAbilityStart", "OnAbilityAction", "OnAbilityEnd",
			"OnAttackStart", "OnAttackAction", "OnAttackEnd",
			"OnAdded", "OnRemoved", "OnAttacked", "OnDamaged", "OnUnitDamaged",
			"OnTurnStarted", "OnTurnFinished", "OnTurnStartedGlobal", "OnTurnFinishedGlobal",
			"OnEndTurnSkillCheck", "OnPreAttack", "OnPostAttack", "OnAttack",
			"OnUnitMoved", "OnUnitLeavingNode", "OnUnitEnteredNode",
			"OnPreHitGlobal", "OnPreHit", "OnHitStart",
			"OnHitPreProcessDamages", "OnHitPreProcessDamagesGlobal",
			"OnHitPreResult", "OnHitPreResultGlobal", "OnHitEnd",
			"OnPostHit", "OnPostHitGlobal", "OnUnitDiedGlobal", "OnUnitHealedGlobal",
			"OnProjectileHitUnit", "OnProjectileMissedUnit", "OnProjectileDestinationReached",
			"OnVictory", "OnDefeat", "OnFightFinished", "OnStartFight", "OnUnitSpawnedGlobal",
		};

		/// <summary>ModifierEvents names with no fire site. Hats already on a file still render.</summary>
		internal static readonly string[] DeadModifierEvents =
		{
			"OnAttackStart", "OnAttackAction", "OnAttacked",
			"OnAbilityEnd", "OnAttackEnd", "OnPreAttack", "OnPostAttack", "OnAttack",
			"OnUnitMoved", "OnHitPreResultGlobal",
		};

		/// <summary>ModifierEvents names combat actually dispatches. Add-modifier-event menus use this.</summary>
		internal static readonly string[] FiredModifierEvents =
		{
			"OnAbilityStart", "OnAbilityAction",
			"OnAdded", "OnRemoved", "OnDamaged", "OnUnitDamaged",
			"OnTurnStarted", "OnTurnFinished", "OnTurnStartedGlobal", "OnTurnFinishedGlobal",
			"OnEndTurnSkillCheck",
			"OnUnitLeavingNode", "OnUnitEnteredNode",
			"OnPreHitGlobal", "OnPreHit", "OnHitStart",
			"OnHitPreProcessDamages", "OnHitPreProcessDamagesGlobal",
			"OnHitPreResult", "OnHitEnd",
			"OnPostHit", "OnPostHitGlobal", "OnUnitDiedGlobal", "OnUnitHealedGlobal",
			"OnProjectileHitUnit", "OnProjectileMissedUnit", "OnProjectileDestinationReached",
			"OnVictory", "OnDefeat", "OnFightFinished", "OnStartFight", "OnUnitSpawnedGlobal",
		};

		/// <summary>True when this name is on AbilityEvents (including names combat never fires).</summary>
		internal static bool IsAbilityEvent(string name)
		{
			return Array.IndexOf(AllAbilityEvents, name) >= 0;
		}

		/// <summary>True when this name is on ModifierEvents (including names combat never fires).</summary>
		internal static bool IsModifierEvent(string name)
		{
			return Array.IndexOf(AllModifierEvents, name) >= 0;
		}

		/// <summary>True when this ability hat is parse-legal but combat never raises it.</summary>
		internal static bool IsDeadAbilityEvent(string name)
		{
			return Array.IndexOf(DeadAbilityEvents, name) >= 0;
		}

		/// <summary>True when this modifier hat is parse-legal but combat never raises it.</summary>
		internal static bool IsDeadModifierEvent(string name)
		{
			return Array.IndexOf(DeadModifierEvents, name) >= 0;
		}

		/// <summary>True when a key looks like an event name the game parser would consider.</summary>
		internal static bool LooksLikeEventName(string name)
		{
			return name != null && name.Length >= 3 && name.StartsWith("On", StringComparison.Ordinal)
				&& char.IsUpper(name[2]);
		}
	}
}
