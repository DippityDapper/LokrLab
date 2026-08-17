using System;
using System.Collections.Generic;
using System.Globalization;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free OffsetCoord clamp and duplicate-hex checks for Encounter placement.</summary>
	/// <remarks>
	/// Authored template sizes come from Phase 1a <c>boardMetadata</c>. <see cref="LevelManager"/>
	/// pads a single room by <see cref="PadWidth"/> / <see cref="PadHeight"/> before
	/// <c>CreateBoard</c>, so Play's HexBoard is the live size. Store and clamp live coords.
	/// </remarks>
	internal static class EncounterPlacementRules
	{
		/// <summary>Vanilla <c>LevelManager</c> width pad on a single room.</summary>
		internal const int PadWidth = 8;

		/// <summary>Vanilla <c>LevelManager</c> height pad on a single room.</summary>
		internal const int PadHeight = 4;

		/// <summary>Dominant authored skirmish size (413 of 610 rooms).</summary>
		internal const int DefaultAuthoredWidth = 16;

		/// <summary>Dominant authored skirmish height.</summary>
		internal const int DefaultAuthoredHeight = 20;

		private static readonly Dictionary<string, (int Width, int Height)> RegisteredSizes =
			new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Records a template's real authored size, read off its actual boardMetadata (VanillaEncounterImporter) rather than guessed.</summary>
		/// <remarks>The dominant-size fallback below is wrong for ~30% of shipped rooms (182/610 are 20×24, not 16×20) -- this lets an actual import correct AuthoredSize for that one template without needing every caller to change.</remarks>
		internal static void RegisterAuthoredSize(string template, int width, int height)
		{
			if (string.IsNullOrEmpty(template) || width <= 0 || height <= 0)
			{
				return;
			}

			RegisteredSizes[template] = (width, height);
		}

		/// <summary>Authored <c>hexWidth</c> / <c>hexHeight</c> for a template with a registered real size, else a known empty template, else the dominant 16×20 guess.</summary>
		internal static void AuthoredSize(string template, out int width, out int height)
		{
			if (!string.IsNullOrEmpty(template) && RegisteredSizes.TryGetValue(template, out (int Width, int Height) registered))
			{
				width = registered.Width;
				height = registered.Height;
				return;
			}

			if (string.Equals(template, "combat_wip", StringComparison.OrdinalIgnoreCase))
			{
				width = 16;
				height = 20;
				return;
			}

			width = DefaultAuthoredWidth;
			height = DefaultAuthoredHeight;
		}

		/// <summary>Live HexBoard size after LevelManager padding.</summary>
		internal static void LiveSize(string template, out int width, out int height)
		{
			AuthoredSize(template, out int authoredWidth, out int authoredHeight);
			width = authoredWidth + PadWidth;
			height = authoredHeight + PadHeight;
		}

		/// <summary>Live HexBoard size for this payload, including an authored grow.</summary>
		internal static void LiveSize(EncounterFileModel file, out int width, out int height)
		{
			EncounterGrowRules.EffectiveLiveSize(file, out width, out height);
		}

		/// <summary>True when both col and row are authored.</summary>
		internal static bool HasPlacement(EncounterCombatantModel combatant)
		{
			return combatant != null && combatant.Col.HasValue && combatant.Row.HasValue;
		}

		/// <summary>True when exactly one of col / row is set.</summary>
		internal static bool HasPartialPlacement(EncounterCombatantModel combatant)
		{
			if (combatant == null)
			{
				return false;
			}

			return combatant.Col.HasValue != combatant.Row.HasValue;
		}

		/// <summary>Clamps a coord into <c>0 .. maxExclusive-1</c>.</summary>
		internal static int ClampCoord(int value, int maxExclusive)
		{
			if (maxExclusive <= 0)
			{
				return 0;
			}

			if (value < 0)
			{
				return 0;
			}

			if (value >= maxExclusive)
			{
				return maxExclusive - 1;
			}

			return value;
		}

		/// <summary>Clamps authored col/row to the live board. No-op when placement is unset.</summary>
		internal static void ClampToLiveBoard(EncounterCombatantModel combatant, string template)
		{
			if (!HasPlacement(combatant))
			{
				return;
			}

			LiveSize(template, out int width, out int height);
			combatant.Col = ClampCoord(combatant.Col.Value, width);
			combatant.Row = ClampCoord(combatant.Row.Value, height);
		}

		/// <summary>Clamps authored col/row to this payload's live board, including grow.</summary>
		internal static void ClampToLiveBoard(EncounterCombatantModel combatant, EncounterFileModel file)
		{
			if (!HasPlacement(combatant))
			{
				return;
			}

			LiveSize(file, out int width, out int height);
			combatant.Col = ClampCoord(combatant.Col.Value, width);
			combatant.Row = ClampCoord(combatant.Row.Value, height);
		}

		/// <summary>Id of another combatant on the same hex, or null.</summary>
		internal static string DuplicateHexId(EncounterFileModel file, EncounterCombatantModel combatant)
		{
			if (file == null || file.Combatants == null || !HasPlacement(combatant))
			{
				return null;
			}

			for (int i = 0; i < file.Combatants.Count; i++)
			{
				EncounterCombatantModel other = file.Combatants[i];
				if (other == null || other == combatant
					|| string.Equals(other.Id, combatant.Id, StringComparison.Ordinal)
					|| !HasPlacement(other))
				{
					continue;
				}

				if (other.Col.Value == combatant.Col.Value && other.Row.Value == combatant.Row.Value)
				{
					return other.Id;
				}
			}

			return null;
		}

		/// <summary>Inspector warning, or null when placement is legal or unset.</summary>
		internal static string Warning(EncounterFileModel file, EncounterCombatantModel combatant)
		{
			if (combatant == null)
			{
				return null;
			}

			if (HasPartialPlacement(combatant))
			{
				return "Col and row must both be set, or both empty (empty = Play center-offset).";
			}

			if (!HasPlacement(combatant))
			{
				return null;
			}

			LiveSize(file, out int width, out int height);
			if (combatant.Col.Value < 0 || combatant.Col.Value >= width
				|| combatant.Row.Value < 0 || combatant.Row.Value >= height)
			{
				return "Hex is outside the live " + width + "×" + height + " board.";
			}

			string other = DuplicateHexId(file, combatant);
			if (other != null)
			{
				return "Combatant '" + other + "' already uses hex (" + combatant.Col.Value
					+ ", " + combatant.Row.Value + "). Play will reject the duplicate.";
			}

			return null;
		}

		/// <summary>Parses a coord field. Empty clears. Returns false when the text is not a number.</summary>
		internal static bool TryParseCoord(string text, out int? value)
		{
			value = null;
			if (string.IsNullOrWhiteSpace(text))
			{
				return true;
			}

			int parsed;
			if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
			{
				return false;
			}

			value = parsed;
			return true;
		}
	}
}
