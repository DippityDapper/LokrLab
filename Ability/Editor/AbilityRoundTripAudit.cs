using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LokrAbilityLab;

namespace LokrAbilityLab.Editor
{
	/// <summary>Phase 3 of the Vanilla Ability Edit track: measures how much AbilityKvIO.TryBuildText's round-trip diverges from vanilla source, across a stratified sample of shipped abilities.</summary>
	/// <remarks>
	/// docs/roadmaps/started/vanilla-ability-edit.md Phase 3: "No Edit Vanilla button until this is
	/// measured." AbilityKvIO depends on KVParser.KV1 (KVLib, bundled in the game's own assembly),
	/// which is not loaded in the LokrModding.Tests xUnit project (see
	/// docs/roadmaps/completed/test-suite.md Phase 4's note on this exact class) -- so this audit
	/// has to run inside the real game process against the real parser, not as an automated unit
	/// test. File -&gt; Run Vanilla Ability Fidelity Audit triggers it once; it writes a report to
	/// Mods/LokrLab/ability-fidelity-audit.txt for a human/AI pass to classify each difference as
	/// benign (reordering, omitted empty keys) vs semantic (a real behavior change).
	/// </remarks>
	internal static class AbilityRoundTripAudit
	{
		private const int SampleSizePerBucket = 6;

		/// <summary>Named in the roadmap as the hard case: opaque ActOnHexas/AddAsAffected, an ActOnTargets Target block in ExtraKv.</summary>
		private static readonly string[] ForcedIds = { "sasquatch_smash" };

		/// <summary>Runs the audit and writes the report file. Returns its path, or null if the report could not be written (summary explains why either way).</summary>
		internal static string Run(out string summary)
		{
			List<AbilityFileModel> sample = BuildSample();
			StringBuilder report = new StringBuilder();
			int identical = 0;
			int changed = 0;
			int failed = 0;

			report.Append("Vanilla ability round-trip fidelity audit\n");
			report.Append("Sample size: ").Append(sample.Count).Append('\n');
			report.Append("Pipeline: vanilla KV text -> parse -> AbilityKvIO.TryBuildText -> line-set diff\n\n");

			foreach (AbilityFileModel model in sample)
			{
				string sourceText = VanillaAbilityCatalog.FindSourceText(model.Id);
				if (string.IsNullOrEmpty(sourceText))
				{
					continue;
				}

				report.Append("==== ").Append(model.Id).Append(" ====\n");
				if (!AbilityKvIO.TryBuildText(model, out string rebuilt, out string error))
				{
					failed++;
					report.Append("FAIL (validation): ").Append(error).Append("\n\n");
					continue;
				}

				List<string> onlyInSource = LinesOnlyIn(sourceText, rebuilt);
				List<string> onlyInRebuilt = LinesOnlyIn(rebuilt, sourceText);
				if (onlyInSource.Count == 0 && onlyInRebuilt.Count == 0)
				{
					identical++;
					report.Append("IDENTICAL (line-set)\n\n");
					continue;
				}

				changed++;
				report.Append("DIFFERS -- lines only in vanilla source (").Append(onlyInSource.Count).Append("):\n");
				foreach (string line in onlyInSource)
				{
					report.Append("  - ").Append(line).Append('\n');
				}

				report.Append("DIFFERS -- lines only in rebuilt text (").Append(onlyInRebuilt.Count).Append("):\n");
				foreach (string line in onlyInRebuilt)
				{
					report.Append("  + ").Append(line).Append('\n');
				}

				report.Append('\n');
			}

			summary = string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"{0} sampled, {1} identical, {2} differ, {3} failed to round-trip.",
				sample.Count, identical, changed, failed);
			report.Insert(0, summary + "\n\n");

			string path = Path.Combine(AbilityLabPaths.SuiteRoot, "ability-fidelity-audit.txt");
			try
			{
				Directory.CreateDirectory(AbilityLabPaths.SuiteRoot);
				File.WriteAllText(path, report.ToString());
			}
			catch (Exception ex)
			{
				summary = "Audit ran (" + summary + ") but could not write report: " + ex.Message;
				return null;
			}

			return path;
		}

		private static List<AbilityFileModel> BuildSample()
		{
			IReadOnlyList<AbilityFileModel> all = VanillaAbilityCatalog.All();
			Dictionary<string, AbilityFileModel> byId = new Dictionary<string, AbilityFileModel>(StringComparer.Ordinal);
			foreach (AbilityFileModel model in all)
			{
				byId[model.Id] = model;
			}

			List<AbilityFileModel> sample = new List<AbilityFileModel>();
			HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);

			foreach (string id in ForcedIds)
			{
				if (byId.TryGetValue(id, out AbilityFileModel forced) && taken.Add(id))
				{
					sample.Add(forced);
				}
			}

			TakeFromBucket(all, taken, sample, HasLuaCard, SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, HasCallFunctionCard, SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, model => HasEvent(model, "OnCustomTargeting"), SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, model => model.IsPassive, SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, model => model.BehaviorFlags.Contains("AOE"), SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, AbilityUsage.HasProjectile, SampleSizePerBucket);
			TakeFromBucket(all, taken, sample, IsSimpleMelee, SampleSizePerBucket);

			sample.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
			return sample;
		}

		private static void TakeFromBucket(IReadOnlyList<AbilityFileModel> all, HashSet<string> taken, List<AbilityFileModel> sample, Func<AbilityFileModel, bool> predicate, int count)
		{
			int added = 0;
			foreach (AbilityFileModel model in all)
			{
				if (added >= count)
				{
					break;
				}

				if (taken.Contains(model.Id) || !predicate(model))
				{
					continue;
				}

				taken.Add(model.Id);
				sample.Add(model);
				added++;
			}
		}

		private static bool IsSimpleMelee(AbilityFileModel model)
		{
			return !model.IsPassive
				&& !model.BehaviorFlags.Contains("AOE")
				&& !AbilityUsage.HasProjectile(model)
				&& !HasLuaCard(model)
				&& !HasCallFunctionCard(model)
				&& !HasEvent(model, "OnCustomTargeting");
		}

		private static bool HasEvent(AbilityFileModel model, string name)
		{
			foreach (EventNode ev in model.Body.Events)
			{
				if (ev.Name == name)
				{
					return true;
				}
			}

			return false;
		}

		private static bool HasLuaCard(AbilityFileModel model)
		{
			return HasCardType(model, "Lua");
		}

		private static bool HasCallFunctionCard(AbilityFileModel model)
		{
			return HasCardType(model, "CallFunction");
		}

		private static bool HasCardType(AbilityFileModel model, string typeId)
		{
			bool found = false;
			AbilityUsage.WalkCards(model, (eventName, card) =>
			{
				if (card.TypeId == typeId)
				{
					found = true;
				}
			});
			return found;
		}

		private static List<string> LinesOnlyIn(string a, string b)
		{
			HashSet<string> bLines = new HashSet<string>(NormalizedLines(b), StringComparer.Ordinal);
			List<string> result = new List<string>();
			foreach (string line in NormalizedLines(a))
			{
				if (!bLines.Contains(line))
				{
					result.Add(line);
				}
			}

			return result;
		}

		private static IEnumerable<string> NormalizedLines(string text)
		{
			return (text ?? string.Empty)
				.Replace("\r\n", "\n")
				.Split('\n')
				.Select(line => line.Trim())
				.Where(line => line.Length > 0);
		}
	}
}
