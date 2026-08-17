using System;
using System.Text.RegularExpressions;

namespace LokrLab
{
	/// <summary>Unity-free Lab catalog filters (hit tags, AOE kinds, loc stems, corrupt KV) for the editor and xUnit.</summary>
	internal static class LabCatalogRules
	{
		/// <summary>HitAction.ValidateTags whitelist, with a leading # for picker display.</summary>
		internal static readonly string[] HitValidateTags =
		{
			"#MELEE",
			"#TARGETED",
			"#PROJECTILE",
			"#MAGICAL",
			"#AOE",
			"#MODIFIER",
			"#ENVIRONMENTAL",
			"#RAY",
			"#REFLECTED",
			"#INTERNAL",
			"#FREEZEDAMAGE",
			"#BURNDAMAGE",
			"#CANTBEBLOCKED",
			"#CANTBEDODGED",
			"#CANTBESHIELDED",
			"#GLARE_SUPER_RAY",
			"#GLARE_TOWER_OR_CRYSTAL",
			"#HEX_BLAST_FIRST_TIME",
		};

		/// <summary>AOE kinds offered for a new pick. RANGE_CONE is omitted because combat never fills it.</summary>
		internal static readonly string[] SelectableAoeKinds = { "RANGE_CIRCLE", "RANGE_TUNNEL" };

		private static readonly Regex ExtraClosingQuote = new Regex("\"[^\"]+\"\"");

		/// <summary>Strips a leading # from an ability KV token.</summary>
		internal static string StripHash(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return name;
			}

			string trimmed = name.Trim();
			if (trimmed.Length > 1 && trimmed[0] == '#')
			{
				return trimmed.Substring(1);
			}

			return trimmed;
		}

		/// <summary>True when <paramref name="token"/> (with or without #) is on HitAction.ValidateTags.</summary>
		internal static bool IsLegalHitTag(string token)
		{
			string stripped = StripHash(token);
			if (string.IsNullOrEmpty(stripped))
			{
				return false;
			}

			for (int i = 0; i < HitValidateTags.Length; i++)
			{
				if (StripHash(HitValidateTags[i]) == stripped)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>True when this AOE kind is offered for a new pick (RANGE_CONE is not).</summary>
		internal static bool IsSelectableAoeKind(string kind)
		{
			if (string.IsNullOrEmpty(kind))
			{
				return false;
			}

			for (int i = 0; i < SelectableAoeKinds.Length; i++)
			{
				if (SelectableAoeKinds[i] == kind)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>True when Lab should warn that RANGE_CONE never fills cone hexes in combat.</summary>
		internal static bool ShouldWarnRangeCone(string aoeKind)
		{
			return aoeKind == "RANGE_CONE";
		}

		/// <summary>Roster loc key for a character display name. Stem is the unique id, not $alias.</summary>
		internal static string UnitNameLocKey(string uniqueId)
		{
			return "UNIT_" + uniqueId + "_NAME_0001";
		}

		/// <summary>Roster loc key for a character lore line. Stem is the unique id, not $alias.</summary>
		internal static string UnitLoreLocKey(string uniqueId)
		{
			return "UNIT_" + uniqueId + "_LORE";
		}

		/// <summary>True when ability KV looks like the Official Pack extra-quote / spaced-key corruption.</summary>
		internal static bool LooksLikeCorruptAbilityKv(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}

			if (text.IndexOf("\"   \"", StringComparison.Ordinal) >= 0)
			{
				return true;
			}

			return ExtraClosingQuote.IsMatch(text);
		}
	}
}
