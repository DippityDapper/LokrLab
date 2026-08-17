using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ironhide.ExoSkeleton;
using LokrAbilityLab;
using LokrAbilityLab.Editor;
using LokrAbilityLab.Projects;
using LokrCharacterLab;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Editor;
using LokrLab.Shell;
using LokrLabApi;

namespace LokrLab.Editor.General
{
	/// <summary>Writes selected Official Pack / DNSpy rows into Lab character and Ability Library folders.</summary>
	/// <remarks>
	/// Scan is <see cref="LegacyPackScan"/>. Confirm only writes checked rows. Heroes reconstruct
	/// the exo the pack atlas is named after (Model / Exoskeletons/BanditArcher.png), not MetaExo
	/// when those differ, and crop that PNG into Animator parts. MetaExo on disk becomes the new
	/// folder id so CustomRigLoader uses that rig. Abilities and characters mint <c>slug_token</c>
	/// ids; leftover pack keys become per-folder <c>$alias</c> so authored skills stay readable.
	/// </remarks>
	internal static class LegacyModImporter
	{
		private static readonly string[] PortraitSlots = { "MINI", "BIG", "BANNER", "MAP", "MAPMINI", "CHALLENGE" };

		/// <summary>Outcome of an Import call: whether it succeeded, the first hero, and non-fatal warnings.</summary>
		internal sealed class ImportResult
		{
			/// <summary>Whether at least one selected row was written.</summary>
			internal bool Success;
			/// <summary>The first imported hero's id, if any.</summary>
			internal string CharacterId;
			/// <summary>The first imported hero's folder, if any.</summary>
			internal string CharacterFolder;
			/// <summary>A human-readable summary of the outcome.</summary>
			internal string Message;
			/// <summary>Non-fatal warnings (rig miss, size mismatch, skipped summon, overwrite).</summary>
			internal readonly List<string> Warnings = new List<string>();
		}

		/// <summary>Scans the folder and imports every parseable row (no picker). Prefer the selection sheet.</summary>
		internal static ImportResult Import(string legacyModFolder)
		{
			LegacyPackScanResult scan = LegacyPackScan.Scan(legacyModFolder);
			if (!scan.Success)
			{
				return new ImportResult { Success = false, Message = scan.Message };
			}

			return Import(scan);
		}

		/// <summary>Writes only the checked rows on the scan into Lab folders.</summary>
		internal static ImportResult Import(LegacyPackScanResult scan)
		{
			ImportResult result = new ImportResult();
			if (scan == null || !scan.Success)
			{
				result.Success = false;
				result.Message = scan != null ? scan.Message : "Nothing to import.";
				return result;
			}

			string library = EnsureAbilityLibrary(scan.AbilityLibraryFolder, scan.RootFolder);
			ImportIdMap map = new ImportIdMap();

			int abilities = 0;
			int summons = 0;
			int heroes = 0;
			foreach (LegacyPackItem ability in LegacyPackScan.Selected(scan, LegacyPackItemKind.Ability))
			{
				if (WriteAbility(ability, library, map, result.Warnings))
				{
					abilities++;
				}
			}

			foreach (LegacyPackItem summon in LegacyPackScan.Selected(scan, LegacyPackItemKind.Summon))
			{
				if (WriteSummon(summon, map, result.Warnings))
				{
					summons++;
				}
			}

			foreach (LegacyPackItem hero in LegacyPackScan.Selected(scan, LegacyPackItemKind.Hero))
			{
				if (WriteHero(hero, map, result))
				{
					heroes++;
				}
			}

			if (heroes + abilities + summons == 0)
			{
				result.Success = false;
				result.Message = "No rows were selected (or every selected row failed).";
				return result;
			}

			RewriteImportedAbilityBodyRefs(map);
			SeedImportedPackAliases(map);
			TryReloadImportedContent();

			result.Success = true;
			result.Message = "Imported " + heroes + " hero(s), " + abilities + " ability(ies), " + summons + " summon(s).";
			if (abilities > 0)
			{
				string libraryName = AbilityLibrarySession.ReadDisplayName(library) ?? Path.GetFileName(library);
				result.Message += " Abilities are in '" + libraryName + "' (" + Path.GetFileName(library) + ").";
			}

			return result;
		}

		/// <summary>Existing library path, or a new slug_token library named after the typed / pack name.</summary>
		/// <remarks>
		/// A typed name that matches no library used to be ignored, so abilities landed in the first
		/// existing folder (Onagro). Create instead — musketeer-abilities must not write into onagro_*.
		/// </remarks>
		internal static string EnsureAbilityLibrary(string requested, string packFolder)
		{
			string trimmed = (requested ?? string.Empty).Trim();
			if (Directory.Exists(trimmed) && File.Exists(Path.Combine(trimmed, "project.json")))
			{
				return trimmed;
			}

			foreach (string folder in AbilityLabPaths.EnumerateLibraryFolders())
			{
				string display = AbilityLibrarySession.ReadDisplayName(folder) ?? Path.GetFileName(folder);
				if (string.Equals(folder, trimmed, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(Path.GetFileName(folder), trimmed, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(display, trimmed, StringComparison.OrdinalIgnoreCase))
				{
					return folder;
				}
			}

			string displayName = string.IsNullOrEmpty(trimmed)
				? SuggestAbilityLibraryName(packFolder)
				: trimmed;
			string slug = LabSlugIds.LegalizeSlug(displayName, "library");
			string id = AbilityLabPaths.GenerateNewLibraryId(slug);
			string created = AbilityLabPaths.LibraryFolder(id);
			AbilityLibrarySession.WriteMarker(created, displayName);
			LokrCharacterLabPlugin.Log.LogInfo("Legacy import created ability library '" + displayName + "' at " + created);
			return created;
		}

		/// <summary>Default library display name from the pack folder (Musketeer → Musketeer Abilities).</summary>
		internal static string SuggestAbilityLibraryName(string packFolder)
		{
			string name = Path.GetFileName((packFolder ?? string.Empty).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(name) || string.Equals(name, "Mods", StringComparison.OrdinalIgnoreCase))
			{
				return "Imported Abilities";
			}

			if (name.EndsWith("Abilities", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("Ability", StringComparison.OrdinalIgnoreCase))
			{
				return name;
			}

			return name + " Abilities";
		}

		private static bool WriteAbility(LegacyPackItem item, string library, ImportIdMap map, List<string> warnings)
		{
			string alias = LabSlugIds.LegalizeSlug(item.BlockKey, "ability");
			string newId = AbilityLabPaths.GenerateNewAbilityId(alias);
			string destFolder = AbilityLabPaths.AbilityFolder(library, newId);
			string destPath = Path.Combine(destFolder, AbilityLabPaths.DefinitionFileName);
			Directory.CreateDirectory(Path.Combine(destFolder, "icons"));
			File.WriteAllText(destPath, item.SourceText);
			CopyAbilityIcons(item, destFolder);
			WriteAbilityLocalization(item, destFolder);
			if (!AbilityIdentityRekey.RewriteOnDisk(destFolder, item.BlockKey, newId, out string error))
			{
				warnings.Add("Ability '" + item.BlockKey + "' wrote but could not rekey to '" + newId + "': " + error);
				return false;
			}

			LabAliases.SeedSelf(destFolder, alias, newId);
			map.Abilities.Add(new MappedId
			{
				OldId = item.BlockKey,
				NewId = newId,
				Alias = alias,
				Folder = destFolder,
			});
			LokrCharacterLabPlugin.Log.LogInfo("Legacy import ability '" + item.BlockKey + "' → '" + newId
				+ "' ($" + alias + ")");
			return true;
		}

		private static bool WriteSummon(LegacyPackItem item, ImportIdMap map, List<string> warnings)
		{
			string existingNamed = Path.Combine(CharacterLabPaths.CharactersRoot, item.BlockKey);
			if (Directory.Exists(existingNamed))
			{
				warnings.Add("Skipped summon '" + item.BlockKey + "' — a character folder with that leftover name already exists.");
				return false;
			}

			string slug = LabSlugIds.LegalizeSlug(
				!string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.BlockKey,
				"character");
			string blockAlias = LabSlugIds.LegalizeSlug(item.BlockKey, "character");
			string id = CharacterLabPaths.GenerateNewCharacterId(slug);
			string folder = HomeWorkstationScene.ScaffoldCharacterFolder(id);
			CharacterProfile profile = new CharacterProfile
			{
				Id = id,
				Name = item.DisplayName,
				EntityType = CharacterEntityType.EnemySummon,
				ImportedFromLegacyMod = true,
			};
			profile.Skills.Clear();
			profile.SkillProgression.Clear();
			profile.CinematicTags.Clear();
			profile.SoundClips.Clear();
			profile.States.Clear();
			RLHeroesParser.ParseInto(item.SourceText, profile);
			profile.Id = id;
			profile.EntityType = CharacterEntityType.EnemySummon;
			if (string.IsNullOrEmpty(profile.Name))
			{
				profile.Name = item.DisplayName;
			}

			ApplyCharacterLocalization(item, profile);
			ApplyImportedSkillAliases(profile, map);
			CharacterProfileSidecar.Save(folder, profile);
			LabAliases.SeedSelf(folder, slug, id);
			if (!string.Equals(slug, blockAlias, StringComparison.OrdinalIgnoreCase))
			{
				LabAliases.SeedSelf(folder, blockAlias, id);
			}

			RLHeroesGenerator.Sync(folder, profile);
			ProjectMarker.Write(folder, LokrLabApi.LokrLabApi.CharacterTypeId, profile.Name);
			CharacterIdentityRekey.RewriteAbilityUnitNames(item.BlockKey, id, blockAlias, folder);
			CopySounds(item.SourceFolder, item.BlockKey, id);
			TryImportRig(item, folder, warnings);
			map.Summons.Add(new MappedId
			{
				OldId = item.BlockKey,
				NewId = id,
				Alias = blockAlias,
				Folder = folder,
			});
			return true;
		}

		private static bool WriteHero(LegacyPackItem item, ImportIdMap map, ImportResult result)
		{
			string slug = LabSlugIds.LegalizeSlug(
				!string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.BlockKey,
				"character");
			string blockAlias = LabSlugIds.LegalizeSlug(item.BlockKey, "character");
			string id = CharacterLabPaths.GenerateNewCharacterId(slug);
			string folder = HomeWorkstationScene.ScaffoldCharacterFolder(id);
			CharacterProfile profile = new CharacterProfile { Id = id };
			CharacterPlaceholders.ApplyToNewProfile(profile);
			RLHeroesParser.ParseInto(item.SourceText, profile);
			profile.Id = id;
			profile.ImportedFromLegacyMod = true;
			profile.Name = item.DisplayName;
			profile.Locked = item.Locked;
			profile.UnlockAchievement = item.UnlockAchievement ?? string.Empty;
			profile.Tier = item.Tier;
			profile.EntityType = CharacterEntityType.Hero;
			ApplyCharacterLocalization(item, profile);
			if (string.IsNullOrEmpty(profile.Model) && !string.IsNullOrEmpty(item.Model))
			{
				profile.Model = item.Model;
			}

			bool rigOk = TryImportRig(item, folder, result.Warnings);
			if (!rigOk)
			{
				CharacterPlaceholders.WritePlaceholderVisuals(folder, id);
			}

			CopyPortraits(item, id);
			CopySounds(item.SourceFolder, item.BlockKey, id);
			ApplyImportedSkillAliases(profile, map);
			CharacterProfileSidecar.Save(folder, profile);
			LabAliases.SeedSelf(folder, slug, id);
			if (!string.Equals(slug, blockAlias, StringComparison.OrdinalIgnoreCase))
			{
				LabAliases.SeedSelf(folder, blockAlias, id);
			}

			RLHeroesGenerator.Sync(folder, profile);
			ProjectMarker.Write(folder, LokrLabApi.LokrLabApi.CharacterTypeId, profile.Name);
			HomeWorkstationScene.AddRecentCharacter(folder);
			map.Heroes.Add(new MappedId
			{
				OldId = item.BlockKey,
				NewId = id,
				Alias = slug,
				Folder = folder,
			});
			if (string.IsNullOrEmpty(result.CharacterFolder))
			{
				result.CharacterId = id;
				result.CharacterFolder = folder;
				HomeWorkstationScene.OnLoadCharacterSelected(folder);
			}

			return true;
		}

		private static bool TryImportRig(LegacyPackItem item, string folder, List<string> warnings)
		{
			string reskin = FindReskinPng(item);
			ExoSkeletonDataAsset packExo;
			if (TryResolvePackExo(item, reskin, out packExo))
			{
				LokrCharacterLabPlugin.Log.LogInfo("Legacy import " + item.DisplayName + ": reconstruct prefab exo '"
					+ packExo.name + "' reskin=" + (reskin ?? "(none)"));
				if (FinishImportRig(item, folder,
					CharacterImporter.ImportInto(packExo, folder, reskin, out string packMessage),
					packMessage, warnings))
				{
					return true;
				}
			}

			if (string.IsNullOrEmpty(item.MetaExo))
			{
				warnings.Add(item.DisplayName + ": no MetaExo or Model exo — skipped rig reconstruct.");
				return false;
			}

			string metaReskin = packExo == null ? reskin : null;
			LokrCharacterLabPlugin.Log.LogInfo("Legacy import " + item.DisplayName + ": reconstruct MetaExo '"
				+ item.MetaExo + "' reskin=" + (metaReskin ?? "(none)"));
			return FinishImportRig(item, folder,
				CharacterImporter.ImportInto(item.MetaExo, folder, metaReskin, out string metaMessage),
				metaMessage, warnings);
		}

		/// <summary>Exo on the pack atlas / Model prefab when a reskin PNG is present.</summary>
		/// <remarks>
		/// Filename wins: Exoskeletons/BanditArcher.png means the BanditArcher prefab's live exo,
		/// not the hero's MetaExo. Reloading that exo by asset.name fails — it is not a bundle key.
		/// </remarks>
		private static bool TryResolvePackExo(LegacyPackItem item, string reskinPng, out ExoSkeletonDataAsset asset)
		{
			asset = null;
			if (string.IsNullOrEmpty(reskinPng))
			{
				return false;
			}

			string stem = Path.GetFileNameWithoutExtension(reskinPng);
			if (CharacterImporter.TryResolveExoFromModel(stem, out asset))
			{
				return true;
			}

			return !string.IsNullOrEmpty(item.Model)
				&& CharacterImporter.TryResolveExoFromModel(item.Model, out asset);
		}

		/// <summary>Records ImportInto's result and whether rig.json landed on disk.</summary>
		private static bool FinishImportRig(
			LegacyPackItem item,
			string folder,
			bool ok,
			string message,
			List<string> warnings)
		{
			if (!ok)
			{
				warnings.Add(item.DisplayName + ": " + message);
				return false;
			}

			if (!string.IsNullOrEmpty(message) && message.IndexOf("Reskin", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				warnings.Add(item.DisplayName + ": " + message);
			}

			return File.Exists(Path.Combine(folder, "rig", "rig.json"));
		}

		/// <summary>Official Pack Exoskeletons PNG whose name matches Model, UniqueId, or the vanilla atlas.</summary>
		internal static string FindReskinPng(LegacyPackItem item)
		{
			if (item == null || string.IsNullOrEmpty(item.SourceFolder))
			{
				return null;
			}

			string exoFolder = Path.Combine(item.SourceFolder, "Exoskeletons");
			if (!Directory.Exists(exoFolder))
			{
				return null;
			}

			string[] candidates = { item.Model, item.BlockKey };
			foreach (string file in Directory.GetFiles(exoFolder, "*.png"))
			{
				string stem = Path.GetFileNameWithoutExtension(file);
				for (int i = 0; i < candidates.Length; i++)
				{
					if (!string.IsNullOrEmpty(candidates[i])
						&& string.Equals(stem, candidates[i], StringComparison.OrdinalIgnoreCase))
					{
						return file;
					}
				}
			}

			if (!string.IsNullOrEmpty(item.MetaExo)
				&& CharacterImporter.TryVanillaAtlasTextureName(item.MetaExo, out string textureName))
			{
				foreach (string file in Directory.GetFiles(exoFolder, "*.png"))
				{
					if (string.Equals(Path.GetFileNameWithoutExtension(file), textureName, StringComparison.OrdinalIgnoreCase))
					{
						return file;
					}
				}
			}

			string[] pngs = Directory.GetFiles(exoFolder, "*.png");
			return pngs.Length == 1 ? pngs[0] : null;
		}

		private static void CopyPortraits(LegacyPackItem item, string newId)
		{
			string portraitsRoot = Path.Combine(item.SourceFolder, "Portraits");
			if (!Directory.Exists(portraitsRoot))
			{
				return;
			}

			string[] searchFolders =
			{
				Path.Combine(portraitsRoot, item.BlockKey),
				Path.Combine(portraitsRoot, Path.GetFileName(item.SourceFolder)),
				portraitsRoot
			};

			for (int s = 0; s < PortraitSlots.Length; s++)
			{
				string slot = PortraitSlots[s];
				string source = FindPortraitFile(searchFolders, item.BlockKey, slot);
				if (source == null)
				{
					continue;
				}

				string dest = CharacterPortraitsPanel.SlotPath(newId, slot);
				Directory.CreateDirectory(Path.GetDirectoryName(dest));
				File.Copy(source, dest, overwrite: true);
			}
		}

		private static string FindPortraitFile(string[] folders, string oldId, string slot)
		{
			string[] names =
			{
				oldId + "_" + slot + ".png",
				slot + ".png"
			};
			for (int f = 0; f < folders.Length; f++)
			{
				if (!Directory.Exists(folders[f]))
				{
					continue;
				}

				for (int n = 0; n < names.Length; n++)
				{
					string path = Path.Combine(folders[f], names[n]);
					if (File.Exists(path))
					{
						return path;
					}
				}

				foreach (string file in Directory.GetFiles(folders[f], "*.png"))
				{
					if (file.EndsWith("_" + slot + ".png", StringComparison.OrdinalIgnoreCase))
					{
						return file;
					}
				}
			}

			return null;
		}

		private static void CopyAbilityIcons(LegacyPackItem item, string destFolder)
		{
			string iconsRoot = Path.Combine(item.SourceFolder, "AbilityIcons");
			string destIcons = Path.Combine(destFolder, "icons");
			Directory.CreateDirectory(destIcons);
			if (!Directory.Exists(iconsRoot))
			{
				return;
			}

			string[] stems = { item.BlockKey, item.IconName };
			for (int i = 0; i < stems.Length; i++)
			{
				if (string.IsNullOrEmpty(stems[i]))
				{
					continue;
				}

				string flat = Path.Combine(iconsRoot, stems[i] + ".png");
				if (File.Exists(flat))
				{
					File.Copy(flat, Path.Combine(destIcons, Path.GetFileName(flat)), overwrite: true);
				}

				string nested = Path.Combine(iconsRoot, stems[i]);
				if (Directory.Exists(nested))
				{
					foreach (string file in Directory.GetFiles(nested, "*.png"))
					{
						File.Copy(file, Path.Combine(destIcons, Path.GetFileName(file)), overwrite: true);
					}
				}
			}
		}

		private static void WriteAbilityLocalization(LegacyPackItem item, string destFolder)
		{
			Dictionary<string, Dictionary<string, string>> loc = LegacyPackScan.LoadAllLocalization(item.SourceFolder);
			foreach (KeyValuePair<string, Dictionary<string, string>> locale in loc)
			{
				StringBuilder body = new StringBuilder();
				string nameKey = "SKILL_" + item.BlockKey + "_NAME";
				string descKey = "SKILL_" + item.BlockKey + "_DESCRIPTION";
				if (locale.Value.TryGetValue(nameKey, out string name))
				{
					body.Append('"').Append(nameKey).Append("\" = \"").Append(EscapeLoc(name)).Append("\"\n");
				}

				if (locale.Value.TryGetValue(descKey, out string desc))
				{
					body.Append('"').Append(descKey).Append("\" = \"").Append(EscapeLoc(desc)).Append("\"\n");
				}

				foreach (KeyValuePair<string, string> pair in locale.Value)
				{
					if (pair.Key.StartsWith("SKILL_" + item.BlockKey + "_", StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(pair.Key, nameKey, StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(pair.Key, descKey, StringComparison.OrdinalIgnoreCase))
					{
						body.Append('"').Append(pair.Key).Append("\" = \"").Append(EscapeLoc(pair.Value)).Append("\"\n");
					}

					if (pair.Key.StartsWith("COMBAT_MODIFIER_", StringComparison.OrdinalIgnoreCase)
						&& item.SourceText.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						body.Append('"').Append(pair.Key).Append("\" = \"").Append(EscapeLoc(pair.Value)).Append("\"\n");
					}
				}

				if (body.Length == 0)
				{
					continue;
				}

				string fileName = string.Equals(locale.Key, "en_US", StringComparison.OrdinalIgnoreCase)
					? "localization_en_US.txt"
					: "localization_" + locale.Key + ".txt";
				File.WriteAllText(Path.Combine(destFolder, fileName), body.ToString());
			}
		}

		private static void ApplyCharacterLocalization(LegacyPackItem item, CharacterProfile profile)
		{
			Dictionary<string, Dictionary<string, string>> loc = LegacyPackScan.LoadAllLocalization(item.SourceFolder);
			if (loc.TryGetValue("en_US", out Dictionary<string, string> english))
			{
				if (TryUnitLore(english, item.BlockKey, out string lore))
				{
					profile.Description = lore;
				}
			}

			for (int i = 0; i < LocaleCodes.AllNonEnglish.Count; i++)
			{
				string suffix = LocaleCodes.AllNonEnglish[i];
				if (!loc.TryGetValue(suffix, out Dictionary<string, string> map))
				{
					continue;
				}

				string name = LookupUnitName(map, item.BlockKey);
				TryUnitLore(map, item.BlockKey, out string lore);
				if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(lore))
				{
					continue;
				}

				profile.Localizations[suffix] = new CharacterLocalizedText
				{
					Name = name ?? string.Empty,
					Description = lore ?? string.Empty
				};
			}
		}

		private static bool TryUnitLore(Dictionary<string, string> map, string id, out string lore)
		{
			return map.TryGetValue("UNIT_" + id + "_LORE", out lore) && !string.IsNullOrEmpty(lore);
		}

		private static string LookupUnitName(Dictionary<string, string> map, string id)
		{
			if (map.TryGetValue("UNIT_" + id + "_NAME_0001", out string name) && !string.IsNullOrEmpty(name))
			{
				return name;
			}

			return map.TryGetValue("UNIT_" + id + "_NAME", out name) ? name : null;
		}

		private static void CopySounds(string sourceFolder, string oldId, string newId)
		{
			string soundsRoot = Path.Combine(sourceFolder, "Sounds");
			if (!Directory.Exists(soundsRoot))
			{
				return;
			}

			string dest = CharacterLabPaths.CharacterSoundsFolder(newId);
			string named = Path.Combine(soundsRoot, oldId);
			if (Directory.Exists(named))
			{
				CopyDirectoryContents(named, dest, recursive: true);
				return;
			}

			CopyDirectoryContents(soundsRoot, dest, recursive: true);
		}

		private static void CopyDirectoryContents(string sourceFolder, string destFolder, bool recursive)
		{
			if (!Directory.Exists(sourceFolder))
			{
				return;
			}

			Directory.CreateDirectory(destFolder);
			foreach (string file in Directory.GetFiles(sourceFolder))
			{
				File.Copy(file, Path.Combine(destFolder, Path.GetFileName(file)), overwrite: true);
			}

			if (recursive)
			{
				foreach (string sub in Directory.GetDirectories(sourceFolder))
				{
					CopyDirectoryContents(sub, Path.Combine(destFolder, Path.GetFileName(sub)), true);
				}
			}
		}

		/// <summary>Rewrites leftover quoted ability ids in imported ability.txt files to <c>$alias</c>.</summary>
		/// <remarks>Longer old ids first so <c>assassin</c> cannot eat <c>assassin_stealth</c>. <c>$$</c> is a literal dollar in Regex.Replace.</remarks>
		private static void RewriteImportedAbilityBodyRefs(ImportIdMap map)
		{
			List<MappedId> ordered = new List<MappedId>(map.Abilities);
			ordered.Sort((a, b) => b.OldId.Length.CompareTo(a.OldId.Length));
			foreach (MappedId ability in map.Abilities)
			{
				string path = Path.Combine(ability.Folder, AbilityLabPaths.DefinitionFileName);
				if (!File.Exists(path))
				{
					continue;
				}

				string text = File.ReadAllText(path);
				string updated = text;
				foreach (MappedId other in ordered)
				{
					updated = Regex.Replace(
						updated,
						"\"" + Regex.Escape(other.OldId) + "\"",
						"\"$$" + other.Alias + "\"",
						RegexOptions.IgnoreCase);
				}

				if (updated != text)
				{
					File.WriteAllText(path, updated);
				}
			}
		}

		/// <summary>Copies every imported ability, summon, and hero alias into each imported folder's aliases.json.</summary>
		/// <remarks>Expansion is per-folder only, so a character's <c>$assassin_lethal_strike</c> and a sibling ability's <c>$assassin_stealth</c> both need the map locally.</remarks>
		private static void SeedImportedPackAliases(ImportIdMap map)
		{
			List<MappedId> all = new List<MappedId>();
			all.AddRange(map.Abilities);
			all.AddRange(map.Summons);
			all.AddRange(map.Heroes);
			foreach (MappedId item in all)
			{
				Dictionary<string, string> aliases = LabAliases.Load(item.Folder);
				foreach (MappedId other in all)
				{
					aliases[other.Alias] = other.NewId;
				}

				LabAliases.Save(item.Folder, aliases);
			}
		}

		/// <summary>Turns leftover pack skill ids on the profile into <c>$alias</c> when this import minted that ability.</summary>
		private static void ApplyImportedSkillAliases(CharacterProfile profile, ImportIdMap map)
		{
			if (profile == null)
			{
				return;
			}

			profile.DefaultSkill = ToImportedSkillRef(profile.DefaultSkill, map);
			if (profile.Skills != null)
			{
				for (int i = 0; i < profile.Skills.Count; i++)
				{
					profile.Skills[i] = ToImportedSkillRef(profile.Skills[i], map);
				}
			}

			if (profile.SkillProgression == null)
			{
				return;
			}

			foreach (LevelSkillEntry entry in profile.SkillProgression)
			{
				if (entry == null || entry.SkillIds == null)
				{
					continue;
				}

				for (int i = 0; i < entry.SkillIds.Count; i++)
				{
					entry.SkillIds[i] = ToImportedSkillRef(entry.SkillIds[i], map);
				}
			}
		}

		/// <summary><c>$alias</c> when this import minted the leftover id; otherwise the original value (vanilla traits stay).</summary>
		private static string ToImportedSkillRef(string value, ImportIdMap map)
		{
			if (string.IsNullOrEmpty(value) || value[0] == '$')
			{
				return value;
			}

			return map.TryGetAbility(value, out MappedId mapped) ? "$" + mapped.Alias : value;
		}

		/// <summary>Rebuilds runtime caches so sandbox can resolve minted abilities without a game restart.</summary>
		private static void TryReloadImportedContent()
		{
			if (!LabContentReloader.CanReloadInCurrentGameState(out string skipReason))
			{
				LokrCharacterLabPlugin.Log.LogInfo("Legacy import: skipped live reload (" + skipReason + ").");
				return;
			}

			CharacterAPI.ReloadResult reload = CharacterAPI.ReloadLabContent(CharacterAPI.ReloadScope.All);
			if (!reload.Success)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Legacy import: live reload failed: " + reload.ErrorMessage);
				return;
			}

			LokrCharacterLabPlugin.Log.LogInfo(string.Format(
				CultureInfo.InvariantCulture,
				"Legacy import: reloaded All in {0:F0} ms ({1}).",
				reload.ElapsedMs,
				reload.Completed));
		}

		private static string EscapeLoc(string value)
		{
			return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		/// <summary>Leftover pack id plus the minted Lab id / alias / folder for one imported row.</summary>
		private struct MappedId
		{
			/// <summary>Original pack block key (assassin_lethal_strike, OnagroMine).</summary>
			internal string OldId;
			/// <summary>Minted slug_token folder id.</summary>
			internal string NewId;
			/// <summary>Legal slug used as the <c>$alias</c> key.</summary>
			internal string Alias;
			/// <summary>Lab folder that received this row.</summary>
			internal string Folder;
		}

		/// <summary>Ids minted during one Import call, used to rewrite skills and seed aliases.json.</summary>
		private sealed class ImportIdMap
		{
			/// <summary>Imported abilities in write order.</summary>
			internal readonly List<MappedId> Abilities = new List<MappedId>();
			/// <summary>Imported summons in write order.</summary>
			internal readonly List<MappedId> Summons = new List<MappedId>();
			/// <summary>Imported heroes in write order.</summary>
			internal readonly List<MappedId> Heroes = new List<MappedId>();

			/// <summary>Finds the minted ability whose leftover pack id matches.</summary>
			internal bool TryGetAbility(string oldId, out MappedId mapped)
			{
				for (int i = 0; i < Abilities.Count; i++)
				{
					if (string.Equals(Abilities[i].OldId, oldId, StringComparison.OrdinalIgnoreCase))
					{
						mapped = Abilities[i];
						return true;
					}
				}

				mapped = default(MappedId);
				return false;
			}
		}
	}
}
