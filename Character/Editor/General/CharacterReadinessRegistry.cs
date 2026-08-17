using System.Collections.Generic;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Severity of a readiness item -- Warning flags an incomplete-but-functional character, Error flags one that genuinely won't work.</summary>
	internal enum ReadinessSeverity
	{
		Warning,
		Error
	}

	/// <summary>A single readiness checklist entry: a message paired with its severity.</summary>
	internal readonly struct ReadinessItem
	{
		/// <summary>The human-readable message shown in the readiness checklist.</summary>
		internal readonly string Message;
		/// <summary>Whether this item is a blocking Error or an informational Warning.</summary>
		internal readonly ReadinessSeverity Severity;

		/// <summary>Creates a readiness item with the given message and severity.</summary>
		internal ReadinessItem(string message, ReadinessSeverity severity)
		{
			Message = message;
			Severity = severity;
		}
	}

	/// <summary>Signature for a readiness check: inspects a character's folder/profile and yields any readiness items it finds.</summary>
	internal delegate IEnumerable<ReadinessItem> ReadinessCheckFn(string characterFolder, CharacterProfile profile);

	/// <summary>Registry of ReadinessCheckFn checks the General workstation's checklist runs.</summary>
	/// <remarks>Mirrors AnimatorValidatorRegistry.cs's shape exactly (register-by-name, remove-then-add so re-registering is idempotent, run-all-and-flatten), with one addition: Error vs. Warning severity, since unlike the Animator's warnings-only validator, this checklist needs to distinguish "the character genuinely won't work" from "incomplete but functional."</remarks>
	internal static class CharacterReadinessRegistry
	{
		private sealed class Entry
		{
			/// <summary>The registered check's name, used to replace it on re-registration.</summary>
			internal string Name;
			/// <summary>The check function itself.</summary>
			internal ReadinessCheckFn Check;
		}

		private static readonly List<Entry> checks = new List<Entry>();

		/// <summary>Registers a check, replacing any existing check of the same name.</summary>
		internal static void RegisterCheck(string name, ReadinessCheckFn check)
		{
			checks.RemoveAll(e => e.Name == name);
			checks.Add(new Entry { Name = name, Check = check });
		}

		/// <summary>Runs every registered check, collecting all their items.</summary>
		internal static List<ReadinessItem> RunAll(string characterFolder, CharacterProfile profile)
		{
			List<ReadinessItem> results = new List<ReadinessItem>();
			foreach (Entry entry in checks)
			{
				foreach (ReadinessItem item in entry.Check(characterFolder, profile))
				{
					results.Add(item);
				}
			}
			return results;
		}

		/// <summary>Registers every first-party readiness check. Idempotent -- calling this more than once per process is harmless.</summary>
		internal static void RegisterDefaults()
		{
			GeneralReadinessChecks.RegisterDefaults();
			AnimatorReadinessChecks.RegisterDefaults();
		}
	}
}
