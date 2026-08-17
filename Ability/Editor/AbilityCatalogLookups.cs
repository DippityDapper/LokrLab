using System;
using System.Collections.Generic;
using System.Linq;
using Ironhide.Legends.Model.Game.Units.Abilities;
using LokrCharacterLoader;

namespace LokrAbilityLab.Editor
{
	/// <summary>Catalog membership and merged runtime lists for pickers / warnings.</summary>
	internal static class AbilityCatalogLookups
	{
		internal static bool Contains(string[] catalog, string name)
		{
			return !string.IsNullOrEmpty(name) && Array.IndexOf(catalog, name) >= 0;
		}

		internal static bool IsVanillaFxMega(string name) => Contains(AbilityPickerCatalog.FxMegaNames, name);

		internal static bool IsVanillaProjectile(string name) => Contains(AbilityPickerCatalog.ProjectileModels, name);

		internal static bool IsKnownFxMega(string name)
		{
			return IsVanillaFxMega(name)
				|| CharacterAPI.KnownCustomFxNames.Contains(name);
		}

		internal static bool IsKnownProjectile(string name)
		{
			return IsVanillaProjectile(name)
				|| CharacterAPI.KnownCustomProjectileNames.Contains(name);
		}

		internal static bool IsKnownClip(string name)
		{
			return Contains(AbilityPickerCatalog.AnimationIds, name)
				|| CharacterAPI.KnownCustomClipNames.Contains(name);
		}

		internal static string[] FxMegaOptions()
		{
			return ConcatUnique(AbilityPickerCatalog.FxMegaNames, ToArray(CharacterAPI.KnownCustomFxNames));
		}

		internal static string[] ProjectileOptions()
		{
			return ConcatUnique(AbilityPickerCatalog.ProjectileModels, ToArray(CharacterAPI.KnownCustomProjectileNames));
		}

		internal static string[] AnimationOptions()
		{
			return ConcatUnique(AbilityPickerCatalog.AnimationIds, ToArray(CharacterAPI.KnownCustomClipNames));
		}

		/// <summary>Dump SpawnUnit ids plus currently loaded CharacterAPI unit definitions.</summary>
		internal static string[] UnitOptions()
		{
			List<string> list = new List<string>(AbilityPickerCatalog.SpawnUnitIds);
			foreach (string id in CharacterAPI.KnownUnitDefinitions.Keys)
			{
				if (!list.Contains(id))
				{
					list.Add(id);
				}
			}

			list.Sort(StringComparer.OrdinalIgnoreCase);
			return list.ToArray();
		}

		internal static bool IsKnownUnit(string name)
		{
			name = StripHash(name);
			return Contains(AbilityPickerCatalog.SpawnUnitIds, name)
				|| CharacterAPI.KnownUnitDefinitions.ContainsKey(name);
		}

		/// <summary>True when this id is a nested modifier on the ability or already loaded.</summary>
		internal static bool IsKnownModifier(string name, ICollection<string> localModifierIds)
		{
			name = StripHash(name);
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			if (localModifierIds != null && localModifierIds.Contains(name))
			{
				return true;
			}

			return AbilitiesDefinitions.instance != null
				&& AbilitiesDefinitions.instance.ability_modifiers != null
				&& AbilitiesDefinitions.instance.ability_modifiers.ContainsKey(name);
		}

		/// <summary>Ability KV string literals are often written with a leading <c>#</c>.</summary>
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

		/// <summary>True when this name matches a dumped <see cref="AbilityPickerCatalog.StatRefs"/> entry, with or without #.</summary>
		internal static bool IsKnownStatRef(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			string trimmed = name.Trim();
			if (Contains(AbilityPickerCatalog.StatRefs, trimmed))
			{
				return true;
			}

			string hashed = trimmed[0] == '#' ? trimmed : "#" + trimmed;
			if (Contains(AbilityPickerCatalog.StatRefs, hashed))
			{
				return true;
			}

			string stripped = StripHash(trimmed);
			for (int i = 0; i < AbilityPickerCatalog.StatRefs.Length; i++)
			{
				if (StripHash(AbilityPickerCatalog.StatRefs[i]) == stripped)
				{
					return true;
				}
			}

			return false;
		}

		private static string[] expressionOptions;
		private static string[] unitArgOptions;
		private static readonly Dictionary<ExpressionContext, string[]> functionCache = new Dictionary<ExpressionContext, string[]>();
		private static readonly Dictionary<ExpressionContext, string[]> snippetCache = new Dictionary<ExpressionContext, string[]>();
		private static readonly Dictionary<ExpressionContext, string[]> statCache = new Dictionary<ExpressionContext, string[]>();

		/// <summary>Tokens first, then parser function names, then dump snippets — still typable.</summary>
		internal static string[] ExpressionOptions()
		{
			if (expressionOptions == null)
			{
				expressionOptions = ConcatUnique(
					AbilityPickerCatalog.ContextTokens,
					AbilityPickerCatalog.ExpressionFunctions,
					AbilityPickerCatalog.ExpressionSnippets);
			}

			return expressionOptions;
		}

		/// <summary>Context tokens plus dump unit refs that are themselves tokens.</summary>
		internal static string[] UnitArgOptions()
		{
			return UnitArgOptions(null, null, null);
		}

		/// <summary>Unit-arg tokens filtered by parent card field; <paramref name="current"/> stays visible.</summary>
		internal static string[] UnitArgOptions(string typeId, string fieldKey, string current)
		{
			if (unitArgOptions == null)
			{
				List<string> extras = new List<string>();
				foreach (string value in AbilityPickerCatalog.UnitRefs)
				{
					if (!string.IsNullOrEmpty(value) && value[0] == '%')
					{
						extras.Add(value);
					}
				}

				unitArgOptions = ConcatUnique(AbilityPickerCatalog.ContextTokens, extras.ToArray());
			}

			return AbilityPickerRules.FilterUnitRefs(unitArgOptions, typeId, fieldKey, current);
		}

		/// <summary>Dump snippets and literals for this field role — not the whole expression language.</summary>
		internal static string[] SnippetsFor(ExpressionContext context)
		{
			return SnippetsFor(context, null, null, null);
		}

		/// <summary>Snippets for this field role, filtered when the parent card is a UnitRef / Hit Tags field.</summary>
		internal static string[] SnippetsFor(ExpressionContext context, string typeId, string fieldKey, string current)
		{
			if (!snippetCache.TryGetValue(context, out string[] cached))
			{
				cached = ConcatUnique(SnippetGroups(context));
				snippetCache[context] = cached;
			}

			if (context == ExpressionContext.Unit)
			{
				return AbilityPickerRules.FilterUnitRefs(cached, typeId, fieldKey, current);
			}

			return AbilityPickerRules.KeepCurrent(cached, current);
		}

		/// <summary>(value) plus the functions legal for this field role.</summary>
		internal static string[] FunctionNameOptions(ExpressionContext context)
		{
			if (!functionCache.TryGetValue(context, out string[] cached))
			{
				cached = ConcatUnique(
					new[] { AbilityExpressionField.ValueModeLabel },
					FunctionsFor(context));
				functionCache[context] = cached;
			}

			return cached;
		}

		/// <summary>Stat refs legal as the second argument of <c>stat</c> in this field role.</summary>
		internal static string[] StatsFor(ExpressionContext context)
		{
			if (!statCache.TryGetValue(context, out string[] cached))
			{
				cached = ConcatUnique(StatGroups(context));
				statCache[context] = cached;
			}

			return cached;
		}

		internal static string[] FunctionsFor(ExpressionContext context)
		{
			switch (context)
			{
				case ExpressionContext.Range:
					return new[] { "stat", "expr" };
				case ExpressionContext.Number:
					return new[] { "stat", "expr", "ceil", "floor", "round", "min", "max", "randomBetween", "randomI", "lerp", "power" };
				case ExpressionContext.Position:
					return new[]
					{
						"unitPosition", "hexPosition", "newPoint", "pointAdd", "pointSub", "pointMult",
						"cinematicPosition", "unitHex", "hexNeighbour", "hexNeighbourOrNextFree", "hexInLine", "wrapContext"
					};
				case ExpressionContext.Unit:
					return new[]
					{
						"unitPosition", "unitHex", "hexPosition", "hexNeighbour", "wrapContext", "unitContext",
						"positionHex", "unitByCinematicId", "activeUnit", "hexInLine", "unitHexSide"
					};
				case ExpressionContext.Condition:
					return new[]
					{
						"isOnState", "not", "equal", "safeEquals", "hasTags", "hasModifier", "hasModifierByTag",
						"matchesTeam", "matchesGroup", "hitConnected", "hitIsLegendary", "hitTags", "stat",
						"unitGroup", "hexDistance", "isDiversifierActive", "isNull", "isAI", "playingTutorial",
						"hitDamageOfType", "hitEffectiveDamage", "hitBrokenArmor", "isGridClear"
					};
				case ExpressionContext.Tags:
					return new[] { "stringList" };
				case ExpressionContext.Group:
					return new[] { "unitGroup" };
				default:
					return new[]
					{
						"stat", "expr", "ceil", "floor", "round", "min", "max", "randomBetween", "randomI",
						"unitPosition", "unitHex", "hexPosition", "pointAdd", "newPoint", "getUnitId", "listCount"
					};
			}
		}

		private static string[][] SnippetGroups(ExpressionContext context)
		{
			switch (context)
			{
				case ExpressionContext.Range:
					return new[] { AbilityPickerCatalog.RangeSnippets };
				case ExpressionContext.Number:
					return new[] { AbilityPickerCatalog.NumberSnippets };
				case ExpressionContext.Position:
					return new[] { AbilityPickerCatalog.PositionSnippets };
				case ExpressionContext.Unit:
					return new[] { AbilityPickerCatalog.UnitSnippets };
				case ExpressionContext.Condition:
					return new[] { AbilityPickerCatalog.ConditionSnippets };
				case ExpressionContext.Tags:
					return new[] { AbilityPickerCatalog.TagSnippets };
				case ExpressionContext.Group:
					return new[] { AbilityPickerCatalog.GroupSnippets };
				default:
					return new[]
					{
						AbilityPickerCatalog.NumberSnippets,
						AbilityPickerCatalog.PositionSnippets,
						AbilityPickerCatalog.UnitSnippets
					};
			}
		}

		private static string[][] StatGroups(ExpressionContext context)
		{
			switch (context)
			{
				case ExpressionContext.Range:
					return new[] { AbilityPickerCatalog.RangeStats };
				case ExpressionContext.Number:
					return new[] { AbilityPickerCatalog.NumberStats };
				case ExpressionContext.Condition:
					return new[] { AbilityPickerCatalog.ConditionStats };
				default:
					return new[] { AbilityPickerCatalog.NumberStats, AbilityPickerCatalog.RangeStats, AbilityPickerCatalog.StatRefs };
			}
		}

		/// <summary>Unfiltered catalog for this kind (UnitRef still uses the core-token filter with no parent field).</summary>
		internal static string[] OptionsFor(ActionCardCatalogKind kind)
		{
			return OptionsFor(kind, null, null, null);
		}

		/// <summary>Catalog for this field, with UnitRef lists filtered by parent card.</summary>
		internal static string[] OptionsFor(ActionCardCatalogKind kind, string typeId, string fieldKey, string current)
		{
			string[] all;
			switch (kind)
			{
				case ActionCardCatalogKind.FxMega:
					all = FxMegaOptions();
					break;
				case ActionCardCatalogKind.Projectile:
					all = ProjectileOptions();
					break;
				case ActionCardCatalogKind.Sound:
					all = AbilityPickerCatalog.SoundNames;
					break;
				case ActionCardCatalogKind.CallFunction:
					all = AbilityPickerCatalog.CallFunctions;
					break;
				case ActionCardCatalogKind.Unit:
					all = UnitOptions();
					break;
				case ActionCardCatalogKind.Animation:
					all = AnimationOptions();
					break;
				case ActionCardCatalogKind.Expression:
					all = ExpressionOptions();
					break;
				case ActionCardCatalogKind.UnitRef:
					all = AbilityPickerCatalog.UnitRefs;
					return AbilityPickerRules.FilterUnitRefs(all, typeId, fieldKey, current);
				case ActionCardCatalogKind.Stat:
					all = AbilityPickerCatalog.StatRefs;
					break;
				case ActionCardCatalogKind.DamageType:
					all = AbilityPickerCatalog.DamageTypes;
					break;
				default:
					return Array.Empty<string>();
			}

			return AbilityPickerRules.KeepCurrent(all, current);
		}

		private static string[] ConcatUnique(params string[][] groups)
		{
			List<string> list = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string[] group in groups)
			{
				if (group == null)
				{
					continue;
				}

				foreach (string value in group)
				{
					if (string.IsNullOrEmpty(value) || !seen.Add(value))
					{
						continue;
					}

					list.Add(value);
				}
			}

			return list.ToArray();
		}

		private static string[] ToArray(IReadOnlyCollection<string> values)
		{
			if (values == null || values.Count == 0)
			{
				return Array.Empty<string>();
			}

			List<string> copy = new List<string>(values.Count);
			foreach (string value in values)
			{
				copy.Add(value);
			}

			return copy.ToArray();
		}
	}
}
