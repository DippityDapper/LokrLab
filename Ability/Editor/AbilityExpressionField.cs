using System;
using System.Collections.Generic;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Which role an expression field plays — catalogs and allowed functions follow this.</summary>
	internal enum ExpressionContext
	{
		Number,
		Range,
		Position,
		Unit,
		Condition,
		Tags,
		Group,
		General
	}

	/// <summary>One-level expression composer: function + argument slots, plus an assembled typable line.</summary>
	internal sealed class AbilityExpressionField
	{
		internal const string ValueModeLabel = "(value)";

		private const string DefaultUnit = "%TARGET";

		private readonly ExpressionContext context;
		private readonly Action<string> onChanged;
		private readonly UiStack slotRow;
		private readonly UiComboBox assembled;
		private readonly string typeId;
		private readonly string fieldKey;
		private bool suppress;
		private string text = string.Empty;
		private bool forceLiteral;

		private AbilityExpressionField(UiStack root, UiStack slotRow, UiComboBox assembled, Action<string> onChanged, ExpressionContext context, string typeId, string fieldKey)
		{
			Root = root;
			this.slotRow = slotRow;
			this.assembled = assembled;
			this.onChanged = onChanged;
			this.context = context;
			this.typeId = typeId;
			this.fieldKey = fieldKey;
		}

		internal UiStack Root { get; }

		internal string Text => text;

		/// <summary>Builds a composer parented under <paramref name="parent"/>.</summary>
		internal static AbilityExpressionField Create(Transform parent, string initial, Action<string> onChanged, ExpressionContext context, string typeId = null, string fieldKey = null)
		{
			UiStack root = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 0f);
			UiStack slotRow = UiStack.Horizontal(root.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			root.Add(slotRow.FixedHeight(28f));
			UiComboBox assembled = UiComboBox.Create(root.ContentTransform, AbilityCatalogLookups.SnippetsFor(context, typeId, fieldKey, initial), initial ?? string.Empty);
			root.Add(assembled.FixedHeight(28f));

			AbilityExpressionField field = new AbilityExpressionField(root, slotRow, assembled, onChanged, context, typeId, fieldKey);
			assembled.OnEndEdit(value => field.OnAssembledEdit(value));
			field.SetText(initial ?? string.Empty);
			return field;
		}

		/// <summary>Pushes a value into the slots without firing <c>onChanged</c>.</summary>
		internal void SetText(string value)
		{
			suppress = true;
			Apply(value ?? string.Empty, notify: false);
			suppress = false;
		}

		private void OnAssembledEdit(string value)
		{
			if (suppress)
			{
				return;
			}

			Apply(value ?? string.Empty, notify: true);
		}

		private void Commit(string value)
		{
			if (suppress)
			{
				return;
			}

			Apply(value ?? string.Empty, notify: true);
		}

		private void Apply(string value, bool notify)
		{
			text = value ?? string.Empty;
			string name = null;
			List<string> args = new List<string>();
			bool call = !forceLiteral && TryParseCall(text, out name, out args);
			forceLiteral = false;
			if (!call)
			{
				name = null;
				args = new List<string>();
			}

			RebuildSlots(call, name, args);
			suppress = true;
			assembled.SetText(text);
			assembled.Visible(call);
			suppress = false;
			if (notify && onChanged != null)
			{
				onChanged(text);
			}
		}

		private void RebuildSlots(bool call, string name, List<string> args)
		{
			slotRow.Clear();
			if (!call)
			{
				UiComboBox value = UiComboBox.Create(slotRow.ContentTransform, AbilityCatalogLookups.SnippetsFor(context, typeId, fieldKey, text), text);
				value.OnEndEdit(Commit);
				slotRow.Add(value.Grow());
				slotRow.Add(UiButton.Create(slotRow.ContentTransform, "Function", StartFunction, primary: false).FixedWidth(80f));
				return;
			}

			UiComboBox function = UiComboBox.Create(slotRow.ContentTransform, AbilityCatalogLookups.FunctionNameOptions(context), name);
			function.OnEndEdit(picked => OnFunctionPicked(picked, args));
			slotRow.Add(function.Grow());
			List<string> collapseArgs = args;

			ArgKind[] kinds = KindsFor(name, args.Count);
			int slots = Math.Max(kinds.Length, args.Count);
			if (name == "stringList")
			{
				slots = Math.Min(4, Math.Max(2, args.Count));
				kinds = RepeatKind(ArgKind.Tag, slots);
			}

			for (int i = 0; i < slots; i++)
			{
				int index = i;
				ArgKind kind = i < kinds.Length ? kinds[i] : ArgKind.Expression;
				string current = i < args.Count ? args[i] : DefaultFor(kind);
				bool optional = IsOptional(name, i);
				UiComboBox arg = UiComboBox.Create(slotRow.ContentTransform, OptionsFor(kind, context), current);
				arg.OnEndEdit(picked => OnArgEdited(name, args, index, picked, optional, slots));
				slotRow.Add(arg.Grow());
			}

			slotRow.Add(UiButton.Create(slotRow.ContentTransform, "Value", () => CollapseToValue(collapseArgs), primary: false).FixedWidth(56f));
		}

		private void OnFunctionPicked(string picked, List<string> previousArgs)
		{
			if (suppress)
			{
				return;
			}

			if (string.IsNullOrEmpty(picked) || picked == ValueModeLabel)
			{
				CollapseToValue(previousArgs);
				return;
			}

			Commit(Assemble(picked, PadArgs(picked, previousArgs)));
		}

		private void OnArgEdited(string name, List<string> previous, int index, string picked, bool optional, int slotCount)
		{
			if (suppress)
			{
				return;
			}

			List<string> next = new List<string>(PadArgs(name, previous, slotCount));
			while (next.Count <= index)
			{
				next.Add(string.Empty);
			}

			next[index] = picked ?? string.Empty;
			if (optional && string.IsNullOrEmpty(next[index]) && index == next.Count - 1)
			{
				next.RemoveAt(index);
			}

			Commit(Assemble(name, next));
		}

		private void StartFunction()
		{
			Commit(DefaultForContext(context));
		}

		private void CollapseToValue(List<string> args)
		{
			forceLiteral = true;
			Commit(LiteralFromArgs(args));
		}

		private string LiteralFromArgs(List<string> args)
		{
			if (args != null)
			{
				for (int i = 0; i < args.Count; i++)
				{
					string arg = args[i];
					if (IsSimpleLiteral(arg))
					{
						return arg;
					}
				}
			}

			return DefaultLiteral(context);
		}

		private static bool IsSimpleLiteral(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			if (value[0] == '%' || value[0] == '#')
			{
				return value.IndexOf('(') < 0;
			}

			return value.Length > 0 && value.IndexOf('(') < 0;
		}

		private static string DefaultLiteral(ExpressionContext context)
		{
			switch (context)
			{
				case ExpressionContext.Range:
				case ExpressionContext.Number:
					return "1";
				case ExpressionContext.Tags:
					return string.Empty;
				case ExpressionContext.Condition:
					return string.Empty;
				default:
					return DefaultUnit;
			}
		}

		private static List<string> PadArgs(string name, List<string> previous, int slotCount = -1)
		{
			ArgKind[] kinds = KindsFor(name, previous != null ? previous.Count : 0);
			int needed = slotCount >= 0 ? slotCount : kinds.Length;
			List<string> next = new List<string>();
			for (int i = 0; i < needed; i++)
			{
				if (previous != null && i < previous.Count && !string.IsNullOrEmpty(previous[i]))
				{
					next.Add(previous[i]);
				}
				else if (i < kinds.Length && !IsOptional(name, i))
				{
					next.Add(DefaultFor(kinds[i]));
				}
				else if (i < kinds.Length)
				{
					next.Add(string.Empty);
				}
			}

			return next;
		}

		private static string Assemble(string name, List<string> args)
		{
			List<string> kept = new List<string>();
			if (args != null)
			{
				for (int i = 0; i < args.Count; i++)
				{
					if (string.IsNullOrEmpty(args[i]) && IsOptional(name, i) && i == args.Count - 1)
					{
						continue;
					}

					kept.Add(args[i] ?? string.Empty);
				}
			}

			return kept.Count == 0 ? name + "()" : name + "(" + string.Join(", ", kept.ToArray()) + ")";
		}

		internal static bool TryParseCall(string value, out string name, out List<string> args)
		{
			name = null;
			args = new List<string>();
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			string trimmed = value.Trim();
			if (HasTopLevelLogic(trimmed))
			{
				return false;
			}

			int open = trimmed.IndexOf('(');
			if (open <= 0)
			{
				return false;
			}

			name = trimmed.Substring(0, open).Trim();
			if (!IsIdentifier(name))
			{
				return false;
			}

			int close = FindMatchingClose(trimmed, open);
			if (close < 0 || close != trimmed.Length - 1)
			{
				return false;
			}

			string inner = trimmed.Substring(open + 1, close - open - 1);
			args = SplitTopLevel(inner);
			return true;
		}

		private static bool IsIdentifier(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

			char first = name[0];
			if (!(first == '_' || (first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z')))
			{
				return false;
			}

			for (int i = 1; i < name.Length; i++)
			{
				char c = name[i];
				if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
				{
					return false;
				}
			}

			return true;
		}

		private static bool HasTopLevelLogic(string value)
		{
			int depth = 0;
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (c == '(')
				{
					depth++;
				}
				else if (c == ')')
				{
					depth--;
				}
				else if (depth == 0 && i + 1 < value.Length)
				{
					if ((c == '&' && value[i + 1] == '&') || (c == '|' && value[i + 1] == '|'))
					{
						return true;
					}
				}
			}

			return false;
		}

		private static int FindMatchingClose(string value, int open)
		{
			int depth = 0;
			for (int i = open; i < value.Length; i++)
			{
				if (value[i] == '(')
				{
					depth++;
				}
				else if (value[i] == ')')
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}

		private static List<string> SplitTopLevel(string inner)
		{
			List<string> parts = new List<string>();
			if (string.IsNullOrEmpty(inner) || string.IsNullOrEmpty(inner.Trim()))
			{
				return parts;
			}

			int depth = 0;
			int start = 0;
			for (int i = 0; i < inner.Length; i++)
			{
				char c = inner[i];
				if (c == '(')
				{
					depth++;
				}
				else if (c == ')')
				{
					depth--;
				}
				else if (c == ',' && depth == 0)
				{
					parts.Add(inner.Substring(start, i - start).Trim());
					start = i + 1;
				}
			}

			parts.Add(inner.Substring(start).Trim());
			return parts;
		}

		private enum ArgKind
		{
			Unit,
			Attach,
			Stat,
			Expression,
			Number,
			Tag,
			State
		}

		private static ArgKind[] KindsFor(string name, int parsedCount)
		{
			switch (name)
			{
				case "unitPosition":
					return new[] { ArgKind.Unit, ArgKind.Attach };
				case "unitHex":
				case "unitGroup":
				case "unitFacing":
				case "unitContext":
				case "unitHexSide":
				case "unitIsFlipped":
				case "getUnitId":
				case "getUnitCinematicId":
					return new[] { ArgKind.Unit };
				case "stat":
					return new[] { ArgKind.Unit, ArgKind.Stat };
				case "hexDistance":
					return new[] { ArgKind.Expression, ArgKind.Expression };
				case "hexNeighbour":
				case "hexNeighbourOrNextFree":
					return new[] { ArgKind.Expression, ArgKind.Expression };
				case "hasModifier":
				case "hasModifierByTag":
					return new[] { ArgKind.Unit, ArgKind.Tag };
				case "isOnState":
					return new[] { ArgKind.Unit, ArgKind.State };
				case "hasTags":
					return new[] { ArgKind.Unit, ArgKind.Tag };
				case "stringList":
					return RepeatKind(ArgKind.Tag, Math.Min(4, Math.Max(2, parsedCount)));
				case "expr":
					return new[] { ArgKind.Expression };
				case "not":
				case "ceil":
				case "floor":
				case "round":
					return new[] { ArgKind.Expression };
				case "min":
				case "max":
				case "randomBetween":
				case "randomI":
					return new[] { ArgKind.Number, ArgKind.Number };
				default:
					if (parsedCount <= 0)
					{
						return Array.Empty<ArgKind>();
					}

					return RepeatKind(ArgKind.Expression, parsedCount);
			}
		}

		private static bool IsOptional(string name, int index)
		{
			return (name == "unitPosition" && index == 1)
				|| ((name == "hexNeighbour" || name == "hexNeighbourOrNextFree") && index == 1);
		}

		private static string DefaultFor(ArgKind kind)
		{
			switch (kind)
			{
				case ArgKind.Unit:
					return DefaultUnit;
				case ArgKind.Stat:
					return "#baseDamage";
				case ArgKind.Number:
					return "1";
				case ArgKind.Attach:
					return string.Empty;
				default:
					return string.Empty;
			}
		}

		private string[] OptionsFor(ArgKind kind, ExpressionContext context)
		{
			switch (kind)
			{
				case ArgKind.Unit:
					return AbilityCatalogLookups.UnitArgOptions(typeId, fieldKey, null);
				case ArgKind.Attach:
					return AbilityPickerCatalog.AttachPoints;
				case ArgKind.Stat:
					return AbilityCatalogLookups.StatsFor(context);
				case ArgKind.Number:
					return AbilityCatalogLookups.SnippetsFor(ExpressionContext.Number);
				case ArgKind.Tag:
					return AbilityEnvelopeOptions.HitValidateTags;
				case ArgKind.State:
					return AbilityPickerCatalog.StateRefs;
				default:
					return AbilityCatalogLookups.SnippetsFor(context, typeId, fieldKey, null);
			}
		}

		private static string DefaultForContext(ExpressionContext context)
		{
			switch (context)
			{
				case ExpressionContext.Range:
					return "stat(%CASTER, #rangedAttackRange)";
				case ExpressionContext.Number:
					return "stat(%CASTER, #attackDamage)";
				case ExpressionContext.Position:
				case ExpressionContext.Unit:
					return "unitPosition(%TARGET)";
				case ExpressionContext.Condition:
					return "isOnState(%TARGET, #STUN)";
				case ExpressionContext.Tags:
					return "stringList(#MELEE, #TARGETED)";
				case ExpressionContext.Group:
					return "unitGroup(%CASTER)";
				default:
					return "stat(%CASTER, #baseDamage)";
			}
		}

		private static ArgKind[] RepeatKind(ArgKind kind, int count)
		{
			ArgKind[] kinds = new ArgKind[count];
			for (int i = 0; i < count; i++)
			{
				kinds[i] = kind;
			}

			return kinds;
		}
	}
}
