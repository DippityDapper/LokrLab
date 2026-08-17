using System.Collections.Generic;
using LokrLab.Editor.Animation;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>A Save-time rig check, returning warnings only, never blocking (matching how CustomRigLoader's own runtime version of this check works: log and let the rig load anyway).</summary>
	internal delegate IEnumerable<string> RigValidatorFn(IReadOnlyList<AnimationClip> clips);

	/// <summary>Extension point for Save-time rig checks.</summary>
	/// <remarks>Registered rather than hardcoded so a plugin adding its own required-animation-name convention can register an additional validator instead of RigEditorScene needing to know every downstream consumer's requirements in advance. Runs against the clips the user actually authored, before OnSaveClicked's own EnsureRequiredClip auto-generation fills in "Stand"/"StandStatic" -- after auto-generation, the required-names check would never fire, defeating its purpose.</remarks>
	internal static class AnimatorValidatorRegistry
	{
		private sealed class Entry
		{
			/// <summary>The registered validator's name, used as the lookup key when RegisterValidator replaces an existing entry.</summary>
			internal string Name;
			/// <summary>The validation function itself.</summary>
			internal RigValidatorFn Validate;
		}

		private static readonly List<Entry> validators = new List<Entry>();

		/// <summary>Registers a validator, replacing any existing validator of the same name.</summary>
		internal static void RegisterValidator(string name, RigValidatorFn validate)
		{
			validators.RemoveAll(v => v.Name == name);
			validators.Add(new Entry { Name = name, Validate = validate });
		}

		/// <summary>Runs every registered validator, collecting all their warnings.</summary>
		internal static List<string> RunAll(IReadOnlyList<AnimationClip> clips)
		{
			List<string> warnings = new List<string>();
			foreach (Entry entry in validators)
			{
				IEnumerable<string> result = entry.Validate(clips);
				if (result != null)
				{
					warnings.AddRange(result);
				}
			}
			return warnings;
		}

		/// <summary>Registers the required-animation-name validator (previously only a CustomRigLoader runtime warning), so the person building the rig sees it at author time instead of only after assigning it to a real hero. Safe to call more than once per process.</summary>
		internal static void RegisterDefaults()
		{
			RegisterValidator("Required animation names", RequiredAnimationNamesValidator.Validate);
		}
	}
}
