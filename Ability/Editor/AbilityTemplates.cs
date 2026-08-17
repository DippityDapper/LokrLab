using System.Collections.Generic;
using System.IO;

namespace LokrAbilityLab.Editor
{
	/// <summary>Create-sheet seeds for the five high-count vanilla shapes.</summary>
	internal static class AbilityTemplates
	{
		internal const string Melee = "melee";
		internal const string Ranged = "ranged";
		internal const string AllyBuff = "ally_buff";
		internal const string Passive = "passive";
		internal const string PointAoe = "point_aoe";

		/// <summary>Last picked template id (overlay dropdown and File → New Ability).</summary>
		internal static string SelectedId = Melee;

		internal static readonly string[] Ids = { Melee, Ranged, AllyBuff, Passive, PointAoe };

		internal static readonly string[] Labels =
		{
			"Melee hit",
			"Ranged projectile",
			"Ally buff",
			"Passive trait",
			"Point AOE",
		};

		internal static int IndexOf(string id)
		{
			int index = System.Array.IndexOf(Ids, id);
			return index >= 0 ? index : 0;
		}

		/// <summary>Builds a new ability model. Does not write disk.</summary>
		internal static AbilityFileModel Build(string templateId, string abilityId)
		{
			switch (templateId)
			{
				case Ranged:
					return RangedModel(abilityId);
				case AllyBuff:
					return AllyBuffModel(abilityId);
				case Passive:
					return PassiveModel(abilityId);
				case PointAoe:
					return PointAoeModel(abilityId);
				default:
					return MeleeModel(abilityId);
			}
		}

		/// <summary>Writes ability.txt + English loc from a template. Falls back to new-ability.txt on failure.</summary>
		internal static bool TryWrite(string libraryFolder, string id, string templateId, out string error, string displayName = null)
		{
			AbilityFileModel model = Build(templateId ?? SelectedId, id);
			Directory.CreateDirectory(AbilityLabPaths.AbilityIconsFolder(libraryFolder, id));
			string path = AbilityLabPaths.AbilityDefinitionPath(libraryFolder, id);
			if (!AbilityKvIO.TrySave(model, path, out error))
			{
				return AbilityPlaceholders.TryWriteNewAbilityFromFile(libraryFolder, id, out error);
			}

			WriteEnglishLoc(libraryFolder, id, displayName);
			error = null;
			return true;
		}

		/// <summary>Writes SKILL_* English lines. Display name defaults to the folder id.</summary>
		internal static void WriteEnglishLoc(string libraryFolder, string id, string displayName = null)
		{
			string destFolder = AbilityLabPaths.AbilityFolder(libraryFolder, id);
			string shown = string.IsNullOrEmpty(displayName) ? id : displayName;
			File.WriteAllText(Path.Combine(destFolder, "localization_en_US.txt"),
				"\"SKILL_" + id + "_NAME\" = \"" + shown + "\"\n" +
				"\"SKILL_" + id + "_DESCRIPTION\" = \"A new ability.\"\n");
		}

		private static AbilityFileModel MeleeModel(string id)
		{
			AbilityFileModel model = BaseCombat(id, new List<string> { "MELEE", "HAS_CHANCE_TO_HIT" });
			model.CastRange = "1";
			model.CastFXId = "CommonSlashSoundFXMega";
			model.Icon = "BasicSword";
			ActionCard hit = AbilityCardFactory.Create("Hit");
			hit.Fields["EffectName"] = "GenericHitFXMega";
			hit.Fields["Tags"] = "stringList(#MELEE, #TARGETED)";
			hit.Stack("InitActions").Add(AbilityCardFactory.Create("AddDamage"));
			model.Body.Event("OnAbilityAction").Cards.Add(hit);
			return model;
		}

		private static AbilityFileModel RangedModel(string id)
		{
			AbilityFileModel model = BaseCombat(id, new List<string> { "UNIT_TARGET" });
			model.CastRange = "stat(%CASTER, #rangedAttackRange)";
			model.CastFXId = "ArcaneMagicMissileCastFXMega";
			model.Icon = "BasicSword";
			ActionCard projectile = AbilityCardFactory.Create("TrackingProjectile");
			projectile.Fields["Model"] = "SimpleArrowProjectile";
			model.Body.Event("OnAbilityAction").Cards.Add(projectile);
			ActionCard hit = AbilityCardFactory.Create("Hit");
			hit.Fields["EffectName"] = "GenericHitFXMega";
			hit.Fields["Tags"] = "stringList(#PROJECTILE, #TARGETED)";
			hit.Stack("InitActions").Add(AbilityCardFactory.Create("AddDamage"));
			model.Body.Event("OnProjectileHitUnit").Cards.Add(hit);
			return model;
		}

		private static AbilityFileModel AllyBuffModel(string id)
		{
			AbilityFileModel model = BaseCombat(id, new List<string> { "UNIT_TARGET", "POSITIVE_EFFECT" });
			model.TeamFilter = "TEAM_FRIENDLY";
			model.CastRange = "3";
			model.Cooldown = "3";
			model.Icon = "AvengingShield";
			ActionCard apply = AbilityCardFactory.Create("ApplyModifier");
			apply.Fields["ModifierName"] = "new_buff";
			apply.Fields["Duration"] = "2";
			model.Body.Event("OnAbilityAction").Cards.Add(apply);
			model.Body.Modifiers.Add(new ModifierDef { Id = "new_buff" });
			return model;
		}

		private static AbilityFileModel PassiveModel(string id)
		{
			return new AbilityFileModel
			{
				Id = id,
				BehaviorFlags = new List<string> { "PASSIVE", "POSITIVE_EFFECT" },
				Icon = "ArmorOfThorns",
				Body = new AbilityBody
				{
					Modifiers = { new ModifierDef { Id = "new_trait", Passive = true } },
				},
			};
		}

		private static AbilityFileModel PointAoeModel(string id)
		{
			AbilityFileModel model = BaseCombat(id, new List<string> { "POINT_TARGET", "AOE" });
			model.CastRange = "4";
			model.Cooldown = "2";
			model.APCost = "2";
			model.AOEKind = "RANGE_CIRCLE";
			model.AOETeamFilter = "TEAM_ENEMY";
			model.AOERange = "2";
			model.Icon = "BigBadaboom";
			ActionCard iterate = AbilityCardFactory.Create("ActOnTargets");
			iterate.Fields["IteratorName"] = "#newTarget";
			iterate.ExtraKv = "\"Target\"\n{\n\t\"Center\"\t\"%TARGET\"\n\t\"Radius\"\t\"2\"\n\t\"Teams\"\t\"TEAM_ENEMY\"\n}\n";
			ActionCard hit = AbilityCardFactory.Create("Hit");
			hit.Fields["Target"] = "%newTarget";
			hit.Fields["EffectName"] = "GenericHitFXMega";
			hit.Stack("InitActions").Add(AbilityCardFactory.Create("AddDamage"));
			iterate.Stack("Actions").Add(hit);
			model.Body.Event("OnAbilityAction").Cards.Add(iterate);
			return model;
		}

		private static AbilityFileModel BaseCombat(string id, List<string> flags)
		{
			return new AbilityFileModel
			{
				Id = id,
				BehaviorFlags = flags,
				TeamFilter = "TEAM_ENEMY",
				CastRange = "1",
				Cooldown = "1",
				APCost = "1",
				AnimationId = "Attack",
			};
		}
	}
}
