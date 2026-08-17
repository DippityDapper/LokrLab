using System;
using System.Collections.Generic;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The ExoSkeleton clip names a combat-view prefab actually looks up, keyed by CharacterProfile.Model.</summary>
	/// <remarks>
	/// Combat instantiates UnitDefinition.kind (the Model KV) from the units bundle, then UnitViewExoSkeletonPatches
	/// swaps the custom rig onto that prefab. Each ExoSkeletonUnitAnimationController on the prefab caches a
	/// sequenceName (and optional angledAnimations) and FindAnimationIndex's it against the swapped asset -- a
	/// missing name throws from StartAnimation (not a silent skip). Those sequence names are per-prefab, not a
	/// global list: HumanArcher uses angled Attack0/45/90, ObeliskLvl4 uses un-angled SpecialAttack for both its
	/// Attack and SpecialAttack controllers, HumanGeraldLightSeeker uses un-angled Attack plus SpecialAttackA/B/C.
	/// Dumped 2026-08-12 from each prefab's own ExoSkeletonUnitAnimationController components in the units bundle.
	/// Unknown Models fall back to the union so Save still backfills something the prefab might look up.
	/// Add Animation presets are PresetsForModel (that list plus Portrait), not the union -- offering HumanArcher's
	/// Attack0 set to an ObeliskLvl4 character made leftover angled clips look required. Save/Load drop angled
	/// numeric variants that ForModel does not ask for, so a Model switch does not keep writing Attack0 stubs.
	/// </remarks>
	internal static class CombatSequenceNames
	{
		/// <summary>HumanArcher -- CharacterProfile.Model's default, and the Lab's original assumed-universal set.</summary>
		internal static readonly string[] HumanArcher =
		{
			"Stand", "StandStatic", "Walk", "Run",
			"Attack0", "Attack45", "Attack90", "Attack270", "Attack315",
			"SpecialAttack0", "SpecialAttack45", "SpecialAttack90", "SpecialAttack270", "SpecialAttack315",
			"SpellCastA", "SpellCastB", "TakeDamage", "Death", "Dodge",
			"Victory", "Speak", "Angry", "Fear", "Frozen", "Crippled",
		};

		/// <summary>ObeliskLvl4 -- both the Attack and SpecialAttack controllers look up sequenceName "SpecialAttack" (no angle suffix).</summary>
		internal static readonly string[] ObeliskLvl4 =
		{
			"Stand", "StandStatic", "Walk", "Run",
			"SpecialAttack",
			"SpellCastA", "SpellCastB", "TakeDamage", "Death", "Dodge",
			"Victory", "Angry", "Crippled",
		};

		/// <summary>HumanGeraldLightSeeker -- un-angled Attack plus SpecialAttackA/B/C, no numbered Attack0 variants.</summary>
		internal static readonly string[] HumanGeraldLightSeeker =
		{
			"Stand", "StandStatic", "Walk", "Run",
			"Attack", "SpecialAttackA", "SpecialAttackB", "SpecialAttackC",
			"SpellCastA", "SpellCastB", "TakeDamage", "Death", "Dodge",
			"Victory", "Speak", "Angry", "Fear", "Frozen", "Crippled",
		};

		/// <summary>Map-only clip name CustomRigLoader also accepts; not a combat sequenceName on the dumped prefabs.</summary>
		internal const string Portrait = "Portrait";

		private static readonly Dictionary<string, string[]> byModel = new Dictionary<string, string[]>
		{
			{ "HumanArcher", HumanArcher },
			{ "ObeliskLvl4", ObeliskLvl4 },
			{ "HumanGeraldLightSeeker", HumanGeraldLightSeeker },
		};

		private static readonly string[] allCombat;

		static CombatSequenceNames()
		{
			HashSet<string> union = new HashSet<string>();
			foreach (string[] names in byModel.Values)
			{
				foreach (string name in names)
				{
					union.Add(name);
				}
			}
			List<string> combat = new List<string>(union);
			combat.Sort();
			allCombat = combat.ToArray();
		}

		/// <summary>Combat sequence names the given Model prefab looks up, or the union of every known template if the Model has not been dumped yet.</summary>
		internal static IReadOnlyList<string> ForModel(string model)
		{
			if (!string.IsNullOrEmpty(model) && byModel.TryGetValue(model, out string[] names))
			{
				return names;
			}
			return allCombat;
		}

		/// <summary>Add Animation modal presets for a Model: that prefab's ForModel list plus map-only Portrait if it is not already included.</summary>
		internal static IReadOnlyList<string> PresetsForModel(string model)
		{
			List<string> names = new List<string>(ForModel(model));
			if (!names.Contains(Portrait))
			{
				names.Insert(0, Portrait);
			}
			return names;
		}

		/// <summary>True for HumanArcher-style angled clip names (Attack0, SpecialAttack45, …). False for un-angled Attack/SpecialAttack and for SpecialAttackA/B/C.</summary>
		internal static bool IsAngledNumericVariant(string clipName)
		{
			if (string.IsNullOrEmpty(clipName))
			{
				return false;
			}
			const string specialAttack = "SpecialAttack";
			const string attack = "Attack";
			if (clipName.StartsWith(specialAttack, StringComparison.Ordinal) && clipName.Length > specialAttack.Length)
			{
				return AllDigits(clipName.Substring(specialAttack.Length));
			}
			if (clipName.StartsWith(attack, StringComparison.Ordinal) && clipName.Length > attack.Length)
			{
				return AllDigits(clipName.Substring(attack.Length));
			}
			return false;
		}

		private static bool AllDigits(string value)
		{
			if (value.Length == 0)
			{
				return false;
			}
			for (int i = 0; i < value.Length; i++)
			{
				if (!char.IsDigit(value[i]))
				{
					return false;
				}
			}
			return true;
		}
	}
}
