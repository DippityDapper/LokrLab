using System;
using System.Collections.Generic;
using LokrLab.Editor;

namespace LokrLab
{
	/// <summary>Extension point for adding known-value suggestions to Character Lab Properties comboboxes.</summary>
	/// <remarks>
	/// Built-in defaults come from the vanilla/base-game character-reference survey (see
	/// <c>CharacterLabKnownOptions</c>). Other plugins merge their own entries on top via
	/// <see cref="AddOptions"/> / <see cref="AddOption"/> — typically from <c>Awake()</c>, with
	/// <c>[BepInDependency(LokrLabPlugin.Guid)]</c> so the suite loads first.
	/// Comboboxes still accept manual text for values not in any list.
	/// </remarks>
	public static class CharacterLabOptionsAPI
	{
		/// <summary>Which Properties workstation combobox a value belongs to.</summary>
		public enum PropertyOptionList
		{
			/// <summary><c>AttackType</c> on the Appearance category.</summary>
			AttackTypes,
			/// <summary>Stat names on the Level Properties category.</summary>
			StatNames,
			/// <summary>State flags on the States category.</summary>
			StateFlags,
			/// <summary><c>Model</c> on the Appearance category.</summary>
			ModelValues,
			/// <summary><c>soundConfig.assetId</c> on the Sound category.</summary>
			SoundAssetIds,
			/// <summary>Sound event keys in <c>soundConfig.sounds</c> on the Sound category.</summary>
			SoundEvents,
			/// <summary><c>cinematicTags</c> on the Skills category.</summary>
			CinematicTags,
			/// <summary><c>defaultSkill</c> and related skill ids on the Skills category.</summary>
			SkillIds,
		}

		private static readonly Dictionary<PropertyOptionList, List<string>> pluginOptions = new Dictionary<PropertyOptionList, List<string>>();

		static CharacterLabOptionsAPI()
		{
			foreach (PropertyOptionList list in Enum.GetValues(typeof(PropertyOptionList)))
			{
				pluginOptions[list] = new List<string>();
			}
		}

		/// <summary>Adds one or more suggested values to the named combobox list. Duplicates are ignored.</summary>
		/// <remarks>Safe to call multiple times; new unique values append after the built-in defaults.</remarks>
		public static void AddOptions(PropertyOptionList list, IEnumerable<string> values)
		{
			if (values == null)
			{
				return;
			}
			List<string> target = pluginOptions[list];
			HashSet<string> existing = new HashSet<string>(GetOptions(list));
			foreach (string value in values)
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				string trimmed = value.Trim();
				if (existing.Add(trimmed))
				{
					target.Add(trimmed);
				}
			}
		}

		/// <summary>Adds a single suggested value to the named combobox list. A no-op if empty or already present.</summary>
		public static void AddOption(PropertyOptionList list, string value)
		{
			AddOptions(list, new[] { value });
		}

		/// <summary>Returns the merged built-in plus plugin suggestion list for a combobox.</summary>
		public static IReadOnlyList<string> GetOptions(PropertyOptionList list)
		{
			List<string> merged = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			AppendUnique(merged, seen, GetBaseOptions(list));
			AppendUnique(merged, seen, pluginOptions[list]);
			return merged;
		}

		private static void AppendUnique(List<string> merged, HashSet<string> seen, IEnumerable<string> values)
		{
			foreach (string value in values)
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				string trimmed = value.Trim();
				if (seen.Add(trimmed))
				{
					merged.Add(trimmed);
				}
			}
		}

		private static string[] GetBaseOptions(PropertyOptionList list)
		{
			switch (list)
			{
				case PropertyOptionList.AttackTypes:
					return CharacterLabKnownOptions.AttackTypes;
				case PropertyOptionList.StatNames:
					return CharacterLabKnownOptions.StatNames;
				case PropertyOptionList.StateFlags:
					return CharacterLabKnownOptions.StateFlags;
				case PropertyOptionList.ModelValues:
					return CharacterLabKnownOptions.ModelValues;
				case PropertyOptionList.SoundAssetIds:
					return CharacterLabKnownOptions.SoundAssetIds;
				case PropertyOptionList.SoundEvents:
					return CharacterLabKnownOptions.SoundEvents;
				case PropertyOptionList.CinematicTags:
					return CharacterLabKnownOptions.CinematicTags;
				case PropertyOptionList.SkillIds:
					return CharacterLabKnownOptions.SkillIds;
				default:
					return Array.Empty<string>();
			}
		}
	}
}
