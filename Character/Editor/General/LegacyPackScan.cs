using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KVLib;
using LokrAbilityLab;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>Kind of a pickable row on the legacy pack import sheet.</summary>
	internal enum LegacyPackItemKind
	{
		/// <summary>One hero or companion (a whole rank-up chain).</summary>
		Hero,
		/// <summary>One top-level ability block.</summary>
		Ability,
		/// <summary>One enemy or summon / prop block.</summary>
		Summon
	}

	/// <summary>One pickable hero, ability, or summon discovered by a scan. No disk writes.</summary>
	internal sealed class LegacyPackItem
	{
		/// <summary>Stable key for the selection sheet (kind + source file + block).</summary>
		internal string Key;
		/// <summary>Hero, ability, or summon.</summary>
		internal LegacyPackItemKind Kind;
		/// <summary>UniqueId, ability id, or enemy block key.</summary>
		internal string BlockKey = string.Empty;
		/// <summary>Localization name when present, otherwise BlockKey.</summary>
		internal string DisplayName = string.Empty;
		/// <summary>The old-mod folder this item came from.</summary>
		internal string SourceFolder = string.Empty;
		/// <summary>The txt file this item was parsed from.</summary>
		internal string SourceFile = string.Empty;
		/// <summary>KV text ParseInto / ability write should consume (one chain or one block).</summary>
		internal string SourceText = string.Empty;
		/// <summary>Vanilla MetaExo asset name from the hero block, or empty.</summary>
		internal string MetaExo = string.Empty;
		/// <summary>Vanilla Model prefab name from the hero or summon block, or empty.</summary>
		internal string Model = string.Empty;
		/// <summary>Ability Icon field, used to find a matching AbilityIcons PNG.</summary>
		internal string IconName = string.Empty;
		/// <summary>Roster locked flag when a HeroRoster fragment was found.</summary>
		internal bool Locked;
		/// <summary>Roster unlockAchievement when present.</summary>
		internal string UnlockAchievement = string.Empty;
		/// <summary>Legend vs companion from the HeroRoster filename.</summary>
		internal CharacterTier Tier = CharacterTier.Companion;
		/// <summary>Why this row may overwrite or skip, or empty.</summary>
		internal string CollisionNote = string.Empty;
		/// <summary>Parse failure for this file, or empty. Failed rows stay unselected.</summary>
		internal string ParseError = string.Empty;
		/// <summary>UnitName values this ability SpawnUnit's, for the unchecked-summon warning.</summary>
		internal readonly List<string> SpawnedUnitKeys = new List<string>();
		/// <summary>Whether the selection sheet has this row checked. Defaults true except parse errors.</summary>
		internal bool Selected = true;
	}

	/// <summary>Scan-only result of pointing the importer at one old-mod folder or a pack root.</summary>
	internal sealed class LegacyPackScanResult
	{
		/// <summary>True when the folder looked like a legacy mod or a pack of them.</summary>
		internal bool Success;
		/// <summary>Human-readable outcome of the scan.</summary>
		internal string Message;
		/// <summary>Folder the user picked.</summary>
		internal string RootFolder;
		/// <summary>True when children were scanned as separate old-mod folders.</summary>
		internal bool IsPackRoot;
		/// <summary>Existing library path, or a display name Confirm should create (pack name + Abilities).</summary>
		internal string AbilityLibraryFolder;
		/// <summary>Heroes / companions (one row per rank chain or extra RLHeroes file).</summary>
		internal readonly List<LegacyPackItem> Heroes = new List<LegacyPackItem>();
		/// <summary>Top-level ability blocks.</summary>
		internal readonly List<LegacyPackItem> Abilities = new List<LegacyPackItem>();
		/// <summary>Enemy / summon / prop blocks.</summary>
		internal readonly List<LegacyPackItem> Summons = new List<LegacyPackItem>();
		/// <summary>Pack children that are not character mods (Resources, new_heroes_lib).</summary>
		internal readonly List<string> SkippedNotes = new List<string>();
	}

	/// <summary>Reads an Official Pack / DNSpy Mods folder into pickable rows without writing Lab files.</summary>
	/// <remarks>
	/// A rank-up chain in one RLHeroes file is one hero. Sibling keys in one EnemiesDefinitions
	/// file (OnagroMine + SulfurBomb) are separate summons. Extra RLHeroes files in one folder
	/// (Empty Units) are separate heroes. Parse failures become rows with an error, not silent skips.
	/// </remarks>
	internal static class LegacyPackScan
	{
		private static readonly string[] SkippedPackFolderNames =
		{
			"Resources", "new_heroes_lib"
		};

		private static readonly Regex UnitNamePattern = new Regex(
			"\"UnitName\"\\s+\"#?([^\"]+)\"", RegexOptions.IgnoreCase);

		/// <summary>True when this folder has an old-system content subfolder.</summary>
		internal static bool LooksLikeLegacyModFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return false;
			}

			return Directory.Exists(Path.Combine(folder, "RLHeroes"))
				|| Directory.Exists(Path.Combine(folder, "Localization"))
				|| Directory.Exists(Path.Combine(folder, "NewAbilities"))
				|| Directory.Exists(Path.Combine(folder, "EnemiesDefinitions"))
				|| Directory.Exists(Path.Combine(folder, "HeroRoster"));
		}

		/// <summary>Scans a single old-mod folder or a pack root whose children are old-mod folders.</summary>
		internal static LegacyPackScanResult Scan(string folder)
		{
			LegacyPackScanResult result = new LegacyPackScanResult
			{
				RootFolder = folder,
				AbilityLibraryFolder = LegacyModImporter.SuggestAbilityLibraryName(folder)
			};

			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				result.Success = false;
				result.Message = "Folder does not exist.";
				return result;
			}

			if (LooksLikeLegacyModFolder(folder))
			{
				ScanMod(folder, result);
				result.Success = result.Heroes.Count + result.Abilities.Count + result.Summons.Count > 0;
				result.Message = result.Success
					? "Found " + DescribeCounts(result) + " in '" + Path.GetFileName(folder) + "'."
					: "'" + folder + "' looks like a legacy mod but has no parseable RLHeroes, NewAbilities, or EnemiesDefinitions blocks.";
				return result;
			}

			int childMods = 0;
			foreach (string child in Directory.GetDirectories(folder))
			{
				string name = Path.GetFileName(child);
				if (IsSkippedPackFolder(name))
				{
					result.SkippedNotes.Add(name + " is not a character folder (shared resources or map scripts).");
					continue;
				}

				if (!LooksLikeLegacyModFolder(child))
				{
					continue;
				}

				childMods++;
				ScanMod(child, result);
			}

			if (childMods == 0)
			{
				result.Success = false;
				result.Message = "'" + folder + "' doesn't look like a legacy mod or Official Pack Mods folder.";
				return result;
			}

			result.IsPackRoot = true;
			result.Success = result.Heroes.Count + result.Abilities.Count + result.Summons.Count > 0;
			result.Message = result.Success
				? "Pack: " + DescribeCounts(result) + " across " + childMods + " folder(s)."
				: "Pack folders were found but none had parseable content.";
			return result;
		}

		/// <summary>Every selected item of the given kind.</summary>
		internal static IEnumerable<LegacyPackItem> Selected(LegacyPackScanResult scan, LegacyPackItemKind kind)
		{
			if (scan == null)
			{
				yield break;
			}

			List<LegacyPackItem> list = kind == LegacyPackItemKind.Hero ? scan.Heroes
				: kind == LegacyPackItemKind.Ability ? scan.Abilities
				: scan.Summons;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Selected && string.IsNullOrEmpty(list[i].ParseError))
				{
					yield return list[i];
				}
			}
		}

		/// <summary>Warning when a selected ability SpawnUnit's a summon that is unchecked.</summary>
		internal static string UncheckedSummonWarning(LegacyPackScanResult scan)
		{
			if (scan == null)
			{
				return null;
			}

			HashSet<string> selectedSummons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (LegacyPackItem summon in Selected(scan, LegacyPackItemKind.Summon))
			{
				selectedSummons.Add(summon.BlockKey);
			}

			List<string> notes = new List<string>();
			foreach (LegacyPackItem ability in Selected(scan, LegacyPackItemKind.Ability))
			{
				for (int i = 0; i < ability.SpawnedUnitKeys.Count; i++)
				{
					string unit = ability.SpawnedUnitKeys[i];
					if (ReferencesUncheckedSummon(scan, unit, selectedSummons))
					{
						notes.Add(ability.BlockKey + " SpawnUnit '" + unit + "' is not selected.");
					}
				}
			}

			return notes.Count == 0 ? null : string.Join(" ", notes) + " The ability will keep the old UnitName until you import or retarget it.";
		}

		private static bool ReferencesUncheckedSummon(
			LegacyPackScanResult scan,
			string unit,
			HashSet<string> selectedSummons)
		{
			bool known = false;
			for (int i = 0; i < scan.Summons.Count; i++)
			{
				if (string.Equals(scan.Summons[i].BlockKey, unit, StringComparison.OrdinalIgnoreCase))
				{
					known = true;
					break;
				}
			}

			return known && !selectedSummons.Contains(unit);
		}

		private static void ScanMod(string folder, LegacyPackScanResult result)
		{
			Dictionary<string, Dictionary<string, string>> locByLocale = LoadLocalization(folder);
			Dictionary<string, string> english = locByLocale.TryGetValue("en_US", out Dictionary<string, string> en)
				? en
				: new Dictionary<string, string>();

			ScanHeroes(folder, english, result);
			ScanAbilities(folder, english, result);
			ScanSummons(folder, english, result);
			ApplyRoster(folder, result.Heroes);
		}

		private static void ScanHeroes(string folder, Dictionary<string, string> english, LegacyPackScanResult result)
		{
			string heroesFolder = Path.Combine(folder, "RLHeroes");
			if (!Directory.Exists(heroesFolder))
			{
				return;
			}

			foreach (string file in Directory.GetFiles(heroesFolder, "*.txt"))
			{
				KeyValue[] roots;
				if (!TryParseFile(file, out roots, out string error))
				{
					result.Heroes.Add(FailedItem(LegacyPackItemKind.Hero, folder, file, error));
					continue;
				}

				foreach (List<KeyValue> chain in GroupChains(roots))
				{
					KeyValue baseBlock = FindBaseBlock(chain);
					string blockKey = ReadField(baseBlock, "UniqueId") ?? ReadField(baseBlock, "Name") ?? baseBlock.Key;
					LegacyPackItem item = new LegacyPackItem
					{
						Kind = LegacyPackItemKind.Hero,
						BlockKey = blockKey,
						DisplayName = LookupUnitName(english, blockKey) ?? blockKey,
						SourceFolder = folder,
						SourceFile = file,
						SourceText = JoinBlocks(chain),
						MetaExo = ReadField(baseBlock, "MetaExo") ?? string.Empty,
						Model = ReadField(baseBlock, "Model") ?? string.Empty,
						Key = ItemKey(LegacyPackItemKind.Hero, folder, file, blockKey)
					};
					string named = Path.Combine(CharacterLabPaths.CharactersRoot, blockKey);
					if (Directory.Exists(named))
					{
						item.CollisionNote = "A character folder named '" + blockKey + "' already exists (leftover import).";
					}

					result.Heroes.Add(item);
				}
			}
		}

		private static void ScanAbilities(string folder, Dictionary<string, string> english, LegacyPackScanResult result)
		{
			string abilitiesFolder = Path.Combine(folder, "NewAbilities");
			if (!Directory.Exists(abilitiesFolder))
			{
				return;
			}

			foreach (string file in Directory.GetFiles(abilitiesFolder, "*.txt"))
			{
				KeyValue[] roots;
				if (!TryParseFile(file, out roots, out string error))
				{
					result.Abilities.Add(FailedItem(LegacyPackItemKind.Ability, folder, file, error));
					continue;
				}

				foreach (KeyValue root in roots)
				{
					if (string.IsNullOrEmpty(root.Key))
					{
						continue;
					}

					LegacyPackItem item = new LegacyPackItem
					{
						Kind = LegacyPackItemKind.Ability,
						BlockKey = root.Key,
						DisplayName = LookupSkillName(english, root.Key) ?? root.Key,
						SourceFolder = folder,
						SourceFile = file,
						SourceText = root.ToString(),
						IconName = ReadField(root, "Icon") ?? string.Empty,
						Key = ItemKey(LegacyPackItemKind.Ability, folder, file, root.Key)
					};
					CollectSpawnedUnits(root.ToString(), item.SpawnedUnitKeys);
					if (AbilityLabPaths.AbilityIdExists(root.Key))
					{
						item.CollisionNote = "Ability id already exists in Ability Lab and will be overwritten.";
					}

					result.Abilities.Add(item);
				}
			}
		}

		private static void ScanSummons(string folder, Dictionary<string, string> english, LegacyPackScanResult result)
		{
			string enemiesFolder = Path.Combine(folder, "EnemiesDefinitions");
			if (!Directory.Exists(enemiesFolder))
			{
				return;
			}

			foreach (string file in Directory.GetFiles(enemiesFolder, "*.txt"))
			{
				KeyValue[] roots;
				if (!TryParseFile(file, out roots, out string error))
				{
					result.Summons.Add(FailedItem(LegacyPackItemKind.Summon, folder, file, error));
					continue;
				}

				foreach (List<KeyValue> chain in GroupChains(roots))
				{
					KeyValue baseBlock = chain[0];
					string blockKey = ReadField(baseBlock, "Name") ?? baseBlock.Key;
					LegacyPackItem item = new LegacyPackItem
					{
						Kind = LegacyPackItemKind.Summon,
						BlockKey = baseBlock.Key,
						DisplayName = LookupUnitName(english, blockKey) ?? LookupUnitName(english, baseBlock.Key) ?? blockKey,
						SourceFolder = folder,
						SourceFile = file,
						SourceText = JoinBlocks(chain),
						Model = ReadField(baseBlock, "Model") ?? string.Empty,
						MetaExo = ReadField(baseBlock, "MetaExo") ?? string.Empty,
						Key = ItemKey(LegacyPackItemKind.Summon, folder, file, baseBlock.Key)
					};
					string named = Path.Combine(CharacterLabPaths.CharactersRoot, baseBlock.Key);
					if (Directory.Exists(named))
					{
						item.CollisionNote = "A character folder named '" + baseBlock.Key + "' already exists; this summon will be skipped.";
					}

					result.Summons.Add(item);
				}
			}
		}

		private static void ApplyRoster(string folder, List<LegacyPackItem> heroes)
		{
			string rosterFolder = Path.Combine(folder, "HeroRoster");
			if (!Directory.Exists(rosterFolder) || heroes.Count == 0)
			{
				return;
			}

			bool locked = false;
			string unlock = string.Empty;
			CharacterTier tier = CharacterTier.Companion;
			bool found = false;
			foreach (string rosterPath in Directory.GetFiles(rosterFolder))
			{
				string fileName = Path.GetFileName(rosterPath);
				if (fileName.IndexOf("legend_", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					tier = CharacterTier.Legend;
				}
				else if (fileName.IndexOf("companion_", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				string text = File.ReadAllText(rosterPath);
				Match lockedMatch = Regex.Match(text, "\"locked\"\\s*:\\s*(true|false)");
				if (lockedMatch.Success)
				{
					locked = lockedMatch.Groups[1].Value == "true";
				}

				Match unlockMatch = Regex.Match(text, "\"unlockAchievement\"\\s*:\\s*\"([^\"]*)\"");
				if (unlockMatch.Success)
				{
					unlock = unlockMatch.Groups[1].Value;
				}

				found = true;
				break;
			}

			if (!found)
			{
				return;
			}

			for (int i = 0; i < heroes.Count; i++)
			{
				if (string.Equals(heroes[i].SourceFolder, folder, StringComparison.OrdinalIgnoreCase))
				{
					heroes[i].Locked = locked;
					heroes[i].UnlockAchievement = unlock;
					heroes[i].Tier = tier;
				}
			}
		}

		/// <summary>Groups a file's top-level blocks into rank-up chains. Unrelated InheritsFrom Base/Hero siblings stay separate.</summary>
		private static List<List<KeyValue>> GroupChains(KeyValue[] roots)
		{
			Dictionary<string, KeyValue> byKey = new Dictionary<string, KeyValue>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValue root in roots)
			{
				if (!string.IsNullOrEmpty(root.Key))
				{
					byKey[root.Key] = root;
				}
			}

			HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<List<KeyValue>> chains = new List<List<KeyValue>>();
			foreach (KeyValue root in roots)
			{
				if (string.IsNullOrEmpty(root.Key) || claimed.Contains(root.Key) || !IsChainRoot(root, byKey))
				{
					continue;
				}

				List<KeyValue> chain = new List<KeyValue>();
				KeyValue current = root;
				while (current != null && claimed.Add(current.Key))
				{
					chain.Add(current);
					string next = ReadField(current, "nextLevelArchetype");
					current = !string.IsNullOrEmpty(next) && byKey.TryGetValue(next, out KeyValue nextBlock)
						? nextBlock
						: null;
				}

				if (chain.Count > 0)
				{
					chains.Add(chain);
				}
			}

			foreach (KeyValue root in roots)
			{
				if (!string.IsNullOrEmpty(root.Key) && claimed.Add(root.Key))
				{
					chains.Add(new List<KeyValue> { root });
				}
			}

			return chains;
		}

		private static bool IsChainRoot(KeyValue block, Dictionary<string, KeyValue> byKey)
		{
			if (block["UniqueId"] != null)
			{
				return true;
			}

			string inherits = ReadField(block, "InheritsFrom");
			if (string.IsNullOrEmpty(inherits)
				|| string.Equals(inherits, "Hero", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(inherits, "Base", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return !byKey.ContainsKey(inherits);
		}

		private static KeyValue FindBaseBlock(List<KeyValue> chain)
		{
			for (int i = 0; i < chain.Count; i++)
			{
				if (chain[i]["UniqueId"] != null)
				{
					return chain[i];
				}
			}

			return chain[0];
		}

		private static bool TryParseFile(string file, out KeyValue[] roots, out string error)
		{
			roots = Array.Empty<KeyValue>();
			error = null;
			try
			{
				roots = KVParser.KV1.ParseAll(File.ReadAllText(file));
				if (roots == null || roots.Length == 0)
				{
					error = "No top-level KV blocks.";
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private static LegacyPackItem FailedItem(LegacyPackItemKind kind, string folder, string file, string error)
		{
			return new LegacyPackItem
			{
				Kind = kind,
				BlockKey = Path.GetFileNameWithoutExtension(file),
				DisplayName = Path.GetFileName(file),
				SourceFolder = folder,
				SourceFile = file,
				ParseError = error,
				Selected = false,
				Key = ItemKey(kind, folder, file, "error")
			};
		}

		private static string JoinBlocks(List<KeyValue> chain)
		{
			StringBuilder text = new StringBuilder();
			for (int i = 0; i < chain.Count; i++)
			{
				if (i > 0)
				{
					text.Append('\n');
				}

				text.Append(chain[i].ToString());
			}

			return text.ToString();
		}

		private static string ReadField(KeyValue block, string key)
		{
			if (block == null)
			{
				return null;
			}

			KeyValue field = block[key];
			return field != null ? field.GetString() : null;
		}

		private static Dictionary<string, Dictionary<string, string>> LoadLocalization(string folder)
		{
			Dictionary<string, Dictionary<string, string>> byLocale =
				new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			string locFolder = Path.Combine(folder, "Localization");
			if (!Directory.Exists(locFolder))
			{
				return byLocale;
			}

			foreach (string path in Directory.GetFiles(locFolder, "*.txt"))
			{
				string locale = LocaleSuffixFromFileName(Path.GetFileName(path));
				if (string.IsNullOrEmpty(locale))
				{
					continue;
				}

				if (!byLocale.TryGetValue(locale, out Dictionary<string, string> map))
				{
					map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					byLocale[locale] = map;
				}

				foreach (KeyValuePair<string, string> pair in ParseLocalizationLines(File.ReadAllText(path)))
				{
					map[pair.Key] = pair.Value;
				}
			}

			return byLocale;
		}

		/// <summary>Reads "KEY" = "VALUE" lines from a localization file.</summary>
		internal static Dictionary<string, string> ParseLocalizationLines(string text)
		{
			Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(text))
			{
				return result;
			}

			foreach (string rawLine in text.Split('\n'))
			{
				Match match = Regex.Match(rawLine, "^\\s*\"(.*)\"\\s*=\\s*\"(.*)\"\\s*$");
				if (match.Success)
				{
					result[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"");
				}
			}

			return result;
		}

		/// <summary>Locale suffix from a localization filename (onagro_en_US.txt → en_US).</summary>
		internal static string LocaleSuffixFromFileName(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
			{
				return null;
			}

			string stem = Path.GetFileNameWithoutExtension(fileName);
			if (stem.EndsWith("_en_US", StringComparison.OrdinalIgnoreCase))
			{
				return "en_US";
			}

			for (int i = 0; i < LocaleCodes.AllNonEnglish.Count; i++)
			{
				string suffix = LocaleCodes.AllNonEnglish[i];
				if (stem.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase))
				{
					return suffix;
				}
			}

			return null;
		}

		/// <summary>Loads every localization map for a legacy mod folder, keyed by locale suffix.</summary>
		internal static Dictionary<string, Dictionary<string, string>> LoadAllLocalization(string folder)
		{
			return LoadLocalization(folder);
		}

		private static string LookupUnitName(Dictionary<string, string> english, string id)
		{
			if (english == null || string.IsNullOrEmpty(id))
			{
				return null;
			}

			if (english.TryGetValue("UNIT_" + id + "_NAME_0001", out string name) && !string.IsNullOrEmpty(name))
			{
				return name;
			}

			if (english.TryGetValue("UNIT_" + id + "_NAME", out name) && !string.IsNullOrEmpty(name))
			{
				return name;
			}

			return null;
		}

		private static string LookupSkillName(Dictionary<string, string> english, string id)
		{
			if (english == null || string.IsNullOrEmpty(id))
			{
				return null;
			}

			return english.TryGetValue("SKILL_" + id + "_NAME", out string name) && !string.IsNullOrEmpty(name)
				? name
				: null;
		}

		private static void CollectSpawnedUnits(string abilityText, List<string> dest)
		{
			if (string.IsNullOrEmpty(abilityText))
			{
				return;
			}

			foreach (Match match in UnitNamePattern.Matches(abilityText))
			{
				string unit = match.Groups[1].Value;
				if (!string.IsNullOrEmpty(unit) && !dest.Contains(unit))
				{
					dest.Add(unit);
				}
			}
		}

		private static bool IsSkippedPackFolder(string name)
		{
			for (int i = 0; i < SkippedPackFolderNames.Length; i++)
			{
				if (string.Equals(name, SkippedPackFolderNames[i], StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static string ItemKey(LegacyPackItemKind kind, string folder, string file, string blockKey)
		{
			return kind + "|" + folder + "|" + file + "|" + blockKey;
		}

		private static string DescribeCounts(LegacyPackScanResult result)
		{
			return result.Heroes.Count + " hero(s), " + result.Abilities.Count + " ability(ies), "
				+ result.Summons.Count + " summon(s)";
		}
	}
}
