using System.Collections.Generic;

namespace LokrAbilityLab.Editor
{
	/// <summary>Registers the visual-editor v1 (and cheap Advanced) action cards.</summary>
	internal static class AbilityCardDescriptors
	{
		/// <summary>Called from plugin Awake before any ability is opened.</summary>
		internal static void RegisterBuiltins()
		{
			Add("Hit", "Hit",
				new[] { "Target", "EffectName", "Tags", "Enqueue", "Backstab" },
				new[] { "InitActions", "Actions", "AlwaysActions" },
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("EffectName", ActionCardCatalogKind.FxMega),
					("Tags", ActionCardCatalogKind.Expression),
					("Enqueue", ActionCardCatalogKind.Expression),
					("Backstab", ActionCardCatalogKind.Expression)));
			Add("AddDamage", "Add Damage",
				new[] { "Type", "Damage" },
				null,
				catalogs: Cats(
					("Type", ActionCardCatalogKind.DamageType),
					("Damage", ActionCardCatalogKind.Expression)));
			Add("ApplyModifier", "Apply Modifier",
				new[] { "ModifierName", "Target", "Duration", "Source", "Refresh" },
				null,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("Duration", ActionCardCatalogKind.Expression),
					("Source", ActionCardCatalogKind.UnitRef),
					("Refresh", ActionCardCatalogKind.Expression)));
			Add("RemoveModifier", "Remove Modifier",
				new[] { "ModifierName", "Target" },
				null,
				catalogs: Cats(("Target", ActionCardCatalogKind.UnitRef)));
			Add("AttachEffect", "Attach Effect",
				new[] { "EffectName", "Target" },
				null,
				catalogs: Cats(
					("EffectName", ActionCardCatalogKind.FxMega),
					("Target", ActionCardCatalogKind.UnitRef)));
			Add("TrackingProjectile", "Tracking Projectile",
				new[] { "Model", "Target", "SourcePos", "TargetPos", "TargetAttach" },
				null,
				catalogs: Cats(
					("Model", ActionCardCatalogKind.Projectile),
					("Target", ActionCardCatalogKind.UnitRef),
					("SourcePos", ActionCardCatalogKind.Expression),
					("TargetPos", ActionCardCatalogKind.Expression)));
			Add("Heal", "Heal",
				new[] { "Target", "HealAmount" },
				null,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("HealAmount", ActionCardCatalogKind.Expression)));
			Add("Knockback", "Knockback",
				new[] { "Target", "Center", "Strength", "Animation" },
				null,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("Center", ActionCardCatalogKind.UnitRef),
					("Strength", ActionCardCatalogKind.Expression)));
			Add("Conditional", "Conditional",
				new[] { "Condition" },
				new[] { "Actions", "ElseActions" },
				catalogs: Cats(("Condition", ActionCardCatalogKind.Expression)));
			Add("ActOnTargets", "Act On Targets",
				new[] { "IteratorName", "IteratorIndexName" },
				new[] { "Actions", "ActionsIfFound", "ActionsIfNotFound" });
			Add("Delay", "Delay",
				new[] { "Time" },
				new[] { "Actions" },
				catalogs: Cats(("Time", ActionCardCatalogKind.Expression)));
			Add("PlaySound", "Play Sound",
				new[] { "Sound", "Unit" },
				null,
				catalogs: Cats(
					("Sound", ActionCardCatalogKind.Sound),
					("Unit", ActionCardCatalogKind.UnitRef)));
			Add("StopSound", "Stop Sound",
				new[] { "Sound", "Unit" },
				null,
				catalogs: Cats(
					("Sound", ActionCardCatalogKind.Sound),
					("Unit", ActionCardCatalogKind.UnitRef)));
			Add("SpawnUnit", "Spawn Unit",
				new[] { "UnitName", "Position", "UnitGroup", "IsAI" },
				new[] { "OnSpawn" },
				catalogs: Cats(
					("UnitName", ActionCardCatalogKind.Unit),
					("Position", ActionCardCatalogKind.Expression),
					("UnitGroup", ActionCardCatalogKind.Expression)));
			Add("SetStat", "Set Stat",
				new[] { "Target", "Stat", "Value" },
				null,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("Stat", ActionCardCatalogKind.Stat),
					("Value", ActionCardCatalogKind.Expression)));
			Add("CallFunction", "Call Function",
				new[] { "Function" },
				null,
				catalogs: Cats(("Function", ActionCardCatalogKind.CallFunction)));
			Add("PlayActivityAnimation", "Play Activity Animation",
				new[] { "Animation" },
				null,
				catalogs: Cats(("Animation", ActionCardCatalogKind.Animation)));

			Add("PlayAnimation", "Play Animation",
				new[] { "Animation", "Target" },
				null,
				advanced: true,
				catalogs: Cats(
					("Animation", ActionCardCatalogKind.Animation),
					("Target", ActionCardCatalogKind.UnitRef)));
			Add("OverrideAnimation", "Override Animation",
				new[] { "Animation", "Target" },
				null,
				advanced: true,
				catalogs: Cats(
					("Animation", ActionCardCatalogKind.Animation),
					("Target", ActionCardCatalogKind.UnitRef)));
			Add("GiveArmor", "Give Armor",
				new[] { "Target", "ArmorAmount" },
				null,
				advanced: true,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("ArmorAmount", ActionCardCatalogKind.Expression)));
			Add("KillUnit", "Kill Unit",
				new[] { "Target" },
				null,
				advanced: true,
				catalogs: Cats(("Target", ActionCardCatalogKind.UnitRef)));
			Add("MoveUnit", "Move Unit",
				new[] { "Target", "Position" },
				null,
				advanced: true,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("Position", ActionCardCatalogKind.Expression)));
			Add("Times", "Times",
				new[] { "Times" },
				new[] { "Actions" },
				advanced: true,
				catalogs: Cats(("Times", ActionCardCatalogKind.Expression)));
			Add("TriggerSkill", "Trigger Skill",
				new[] { "Skill", "Target" },
				null,
				advanced: true,
				catalogs: Cats(("Target", ActionCardCatalogKind.UnitRef)));
			Add("ResetCooldown", "Reset Cooldown",
				new[] { "Target", "Skill" },
				null,
				advanced: true,
				catalogs: Cats(("Target", ActionCardCatalogKind.UnitRef)));
			Add("OffsetCooldown", "Offset Cooldown",
				new[] { "Target", "Skill", "Offset" },
				null,
				advanced: true,
				catalogs: Cats(
					("Target", ActionCardCatalogKind.UnitRef),
					("Offset", ActionCardCatalogKind.Expression)));
			Add("Lua", "Lua",
				new[] { "Action" },
				null,
				advanced: true);
		}

		private static Dictionary<string, ActionCardCatalogKind> Cats(params (string key, ActionCardCatalogKind kind)[] pairs)
		{
			Dictionary<string, ActionCardCatalogKind> map = new Dictionary<string, ActionCardCatalogKind>();
			foreach ((string key, ActionCardCatalogKind kind) pair in pairs)
			{
				map[pair.key] = pair.kind;
			}

			return map;
		}

		private static void Add(
			string typeId,
			string label,
			string[] fields,
			string[] stacks,
			bool advanced = false,
			Dictionary<string, ActionCardCatalogKind> catalogs = null)
		{
			ActionCardDescriptor descriptor = new ActionCardDescriptor
			{
				TypeId = typeId,
				DisplayLabel = label,
				FieldKeys = fields ?? System.Array.Empty<string>(),
				ChildStackNames = stacks ?? System.Array.Empty<string>(),
				Advanced = advanced,
			};
			if (catalogs != null)
			{
				foreach (KeyValuePair<string, ActionCardCatalogKind> pair in catalogs)
				{
					descriptor.FieldCatalogs[pair.Key] = pair.Value;
				}
			}

			AbilityLabAPI.RegisterActionCard(typeId, descriptor, priority: 0);
		}
	}
}
