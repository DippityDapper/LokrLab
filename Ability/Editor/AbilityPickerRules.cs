using System;
using System.Collections.Generic;

namespace LokrAbilityLab.Editor
{
	/// <summary>Unity-free Ability Lab picker filters (unit tokens, hit tags) for the editor and xUnit.</summary>
	/// <remarks>
	/// AbilityPickerCatalog dumps every vanilla token. This table is the hand-edited allow-list the
	/// editor actually shows. Unknown current values stay visible so a loaded boss token is not stripped.
	/// </remarks>
	internal static class AbilityPickerRules
	{
		/// <summary>Unit tokens offered on a new Target / Unit pick, regardless of parent action.</summary>
		internal static readonly string[] CoreUnitTokens =
		{
			"%CASTER",
			"%SOURCE",
			"%TARGET",
			"%UNIT",
			"%unit",
			"%ATTACKER",
			"%ATTACKED",
			"%HITSOURCE",
			"%HITTARGET",
			"%newTarget",
		};

		/// <summary>Unit functions offered next to the core tokens (not hex / position dumps).</summary>
		internal static readonly string[] CoreUnitFunctions =
		{
			"activeUnit()",
		};

		/// <summary>True when <paramref name="value"/> is a core unit token or allow-listed unit function.</summary>
		internal static bool IsCoreUnitPick(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			if (Contains(CoreUnitTokens, value) || Contains(CoreUnitFunctions, value))
			{
				return true;
			}

			return false;
		}

		/// <summary>Extra unit tokens legal for this card field beyond the core set.</summary>
		internal static string[] ExtraUnitTokens(string typeId, string fieldKey)
		{
			if (typeId == "Knockback" && fieldKey == "Center")
			{
				return new[] { "%knockbackCenter" };
			}

			return Array.Empty<string>();
		}

		/// <summary>True when this dump value should appear in a UnitRef picker for the given field.</summary>
		internal static bool IsAllowedUnitPick(string value, string typeId, string fieldKey)
		{
			if (IsCoreUnitPick(value))
			{
				return true;
			}

			string[] extra = ExtraUnitTokens(typeId, fieldKey);
			return extra.Length > 0 && Contains(extra, value);
		}

		/// <summary>Filters a dump list to core / extra unit picks, then keeps <paramref name="current"/> if missing.</summary>
		internal static string[] FilterUnitRefs(string[] all, string typeId, string fieldKey, string current)
		{
			List<string> kept = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			if (all != null)
			{
				foreach (string value in all)
				{
					if (string.IsNullOrEmpty(value) || !seen.Add(value))
					{
						continue;
					}

					if (IsAllowedUnitPick(value, typeId, fieldKey))
					{
						kept.Add(value);
					}
				}
			}

			return KeepCurrent(kept.ToArray(), current);
		}

		/// <summary>Prepends <paramref name="current"/> when it is non-empty and not already in <paramref name="filtered"/>.</summary>
		internal static string[] KeepCurrent(string[] filtered, string current)
		{
			if (string.IsNullOrEmpty(current))
			{
				return filtered ?? Array.Empty<string>();
			}

			if (filtered != null && Contains(filtered, current))
			{
				return filtered;
			}

			List<string> list = new List<string>();
			list.Add(current);
			if (filtered != null)
			{
				foreach (string value in filtered)
				{
					if (!string.IsNullOrEmpty(value) && value != current)
					{
						list.Add(value);
					}
				}
			}

			return list.ToArray();
		}

		private static bool Contains(string[] catalog, string value)
		{
			if (catalog == null || string.IsNullOrEmpty(value))
			{
				return false;
			}

			for (int i = 0; i < catalog.Length; i++)
			{
				if (catalog[i] == value)
				{
					return true;
				}
			}

			return false;
		}
	}
}
