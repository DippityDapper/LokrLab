using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Phase 1 empty-enough arena names for the Encounter template picker.</summary>
	internal static class EncounterTemplateRules
	{
		/// <summary>Empty 16×20 field. Looks like the default; kept for old saves, not the picker.</summary>
		internal const string CombatWip = "combat_wip";

		/// <summary>Bridge corridor on the same 24×24 live board. No enemy spawns; 27 props.</summary>
		internal const string CombatBridge = "combat_bridge";

		/// <summary>v1 picker hosts. Default first. <c>combat_blank</c> is not empty.</summary>
		internal static readonly string[] EmptyEnough =
		{
			EncounterFileModel.DefaultTemplate,
			CombatBridge
		};

		/// <summary>Trimmed template, or <see cref="EncounterFileModel.DefaultTemplate"/> when empty.</summary>
		internal static string Normalize(string template)
		{
			if (string.IsNullOrWhiteSpace(template))
			{
				return EncounterFileModel.DefaultTemplate;
			}

			return template.Trim();
		}

		/// <summary>Canonical empty-enough spelling, or the trimmed unknown name.</summary>
		internal static string Canonical(string template)
		{
			string normalized = Normalize(template);
			if (string.Equals(normalized, CombatWip, StringComparison.OrdinalIgnoreCase))
			{
				return CombatWip;
			}

			for (int i = 0; i < EmptyEnough.Length; i++)
			{
				if (string.Equals(EmptyEnough[i], normalized, StringComparison.OrdinalIgnoreCase))
				{
					return EmptyEnough[i];
				}
			}

			return normalized;
		}

		/// <summary>True when <paramref name="template"/> is an empty-enough host, including old <c>combat_wip</c> saves.</summary>
		internal static bool IsEmptyEnough(string template)
		{
			string canonical = Canonical(template);
			if (string.Equals(canonical, CombatWip, StringComparison.Ordinal))
			{
				return true;
			}

			for (int i = 0; i < EmptyEnough.Length; i++)
			{
				if (string.Equals(EmptyEnough[i], canonical, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Picker caption for a template id.</summary>
		internal static string Label(string template)
		{
			string id = Canonical(template);
			if (string.Equals(id, EncounterFileModel.DefaultTemplate, StringComparison.Ordinal))
			{
				return "fighttesterempty — open field";
			}

			if (string.Equals(id, CombatBridge, StringComparison.Ordinal))
			{
				return "combat_bridge — bridge";
			}

			if (string.Equals(id, CombatWip, StringComparison.Ordinal))
			{
				return "combat_wip — open field";
			}

			return id;
		}

		/// <summary>Picker entries: empty-enough names, plus the current value when it is unknown.</summary>
		internal static string[] Options(string current)
		{
			List<string> options = new List<string>(EmptyEnough.Length + 1);
			for (int i = 0; i < EmptyEnough.Length; i++)
			{
				options.Add(EmptyEnough[i]);
			}

			string extra = Canonical(current);
			if (!Contains(options, extra))
			{
				options.Add(extra);
			}

			return options.ToArray();
		}

		/// <summary>Index of <paramref name="template"/> in <paramref name="options"/>, or 0.</summary>
		internal static int IndexOf(IList<string> options, string template)
		{
			if (options == null || options.Count == 0)
			{
				return 0;
			}

			string canonical = Canonical(template);
			for (int i = 0; i < options.Count; i++)
			{
				if (string.Equals(options[i], canonical, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			return 0;
		}

		private static bool Contains(List<string> options, string value)
		{
			for (int i = 0; i < options.Count; i++)
			{
				if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}
	}
}
