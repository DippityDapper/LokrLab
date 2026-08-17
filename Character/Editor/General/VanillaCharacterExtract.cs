using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Path = System.IO.Path;
using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.View.Metagame.Screens.Logic;
using Ironhide.Localization;
using LokrCharacterLoader;
using LokrCharacterLab;
using LokrLab.Editor;
using LokrModAPI;
using UnityEngine;

namespace LokrLab.Editor.General
{
	/// <summary>One shipped hero that can be opened as a Lab override project.</summary>
	internal sealed class VanillaHeroRow
	{
		/// <summary>UnitDefinition.uniqueId (Gerald).</summary>
		internal string UniqueId = string.Empty;

		/// <summary>Localized or KV display name.</summary>
		internal string DisplayName = string.Empty;

		/// <summary>Existing Lab folder that already claims this UniqueId, or null.</summary>
		internal string ExistingFolder;
	}

	/// <summary>Copies a shipped hero into a minted slug_token Lab folder so Loader last-wins replaces them in place.</summary>
	/// <remarks>
	/// Reconstructs the vanilla exo into rig/ + sprites/ so Animator can edit clips. UniqueId and
	/// block keys stay vanilla. File → Import Character remains the pack-reskin crop path. See
	/// docs/roadmaps/started/vanilla-character-edit.md Phase 3.
	/// </remarks>
	internal static class VanillaCharacterExtract
	{
		/// <summary>Level-1 Hero UniqueIds from the live unit-definition table, sorted by display name.</summary>
		internal static List<VanillaHeroRow> ListVanillaHeroes()
		{
			List<VanillaHeroRow> rows = new List<VanillaHeroRow>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, UnitDefinition> pair in CharacterAPI.KnownUnitDefinitions)
			{
				UnitDefinition definition = pair.Value;
				if (definition == null
					|| string.IsNullOrEmpty(definition.uniqueId)
					|| !string.Equals(definition.inheritsFrom, "Hero", StringComparison.Ordinal)
					|| Mathf.RoundToInt(StatOr(definition, "level", -1f)) != 1
					|| !seen.Add(definition.uniqueId))
				{
					continue;
				}

				VanillaHeroRow row = new VanillaHeroRow
				{
					UniqueId = definition.uniqueId,
					DisplayName = ReadDisplayName(definition),
					ExistingFolder = FindExistingOverrideFolder(definition.uniqueId)
				};
				rows.Add(row);
			}

			rows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
			return rows;
		}

		/// <summary>Opens an existing override folder, or extracts a new one. True on success.</summary>
		internal static bool TryOpenOrCreate(string uniqueId, out string folder, out string status)
		{
			folder = null;
			status = null;
			if (string.IsNullOrEmpty(uniqueId))
			{
				status = "No hero id.";
				return false;
			}

			string existing = FindExistingOverrideFolder(uniqueId);
			if (!string.IsNullOrEmpty(existing))
			{
				CharacterProfile existingProfile = CharacterProfileSidecar.Load(existing);
				folder = HealLoadedOverride(existing, existingProfile);
				status = "Opened existing override for '" + uniqueId + "'.";
				return true;
			}

			if (!TryBuildProfile(uniqueId, out CharacterProfile profile, out status))
			{
				return false;
			}

			CharacterLabPaths.EnsureFoldersExist();
			string slug = LabSlugIds.LegalizeSlug(uniqueId, "hero");
			string folderId = CharacterLabPaths.GenerateNewCharacterId(slug);
			folder = HomeWorkstationScene.ScaffoldCharacterFolder(folderId);
			profile.Id = folderId;
			CharacterProfileSidecar.Save(folder, profile);
			WriteProjectMarker(folder, profile);
			TryImportVanillaRig(folder, profile);
			RLHeroesGenerator.Sync(folder, profile);
			status = "Extracted '" + profile.Name + "' as a vanilla override.";
			return true;
		}

		/// <summary>First Lab character folder that already claims this UniqueId.</summary>
		internal static string FindExistingOverrideFolder(string uniqueId)
		{
			if (string.IsNullOrEmpty(uniqueId))
			{
				return null;
			}

			foreach ((string _, string characterFolder) in ModAPI.Files.EnumerateCategorySubfolders(
				CharacterLabPaths.CharactersCategory))
			{
				CharacterProfile profile = CharacterProfileSidecar.Load(characterFolder);
				string characterJsonId = ReadJsonStringField(Path.Combine(characterFolder, "character.json"), "id");
				string rosterId = ReadJsonStringField(Path.Combine(characterFolder, "roster.json"), "id");
				if (VanillaOverrideRules.FolderClaimsUniqueId(
					profile.VanillaSourceUniqueId,
					characterJsonId,
					rosterId,
					uniqueId))
				{
					return characterFolder;
				}
			}

			return null;
		}

		private static bool TryBuildProfile(string uniqueId, out CharacterProfile profile, out string status)
		{
			profile = null;
			status = null;
			if (!TryFindLevel1(uniqueId, out UnitDefinition level1))
			{
				status = "No level-1 Hero definition for '" + uniqueId + "'.";
				return false;
			}

			profile = new CharacterProfile
			{
				VanillaSourceUniqueId = uniqueId,
				VanillaNameStem = level1.name ?? string.Empty,
				VanillaMetaExo = level1.metaExo ?? string.Empty,
				EntityType = CharacterEntityType.Hero,
				ImportedFromLegacyMod = false
			};
			ApplyDefinition(profile, level1);
			profile.VanillaBlockKeys.Clear();
			UnitDefinition current = level1;
			HashSet<string> visited = new HashSet<string>();
			while (current != null && visited.Add(current.id))
			{
				profile.VanillaBlockKeys.Add(current.id);
				if (string.IsNullOrEmpty(current.nextLevelArchetype)
					|| !TryGetDefinition(current.nextLevelArchetype, out UnitDefinition next))
				{
					break;
				}

				CharacterLevel extra = ReadLevel(next);
				if (extra != null)
				{
					profile.Levels.Add(extra);
				}

				current = next;
			}

			ReadRosterFields(uniqueId, profile);
			ReadLocalization(profile);
			if (string.IsNullOrEmpty(profile.Name))
			{
				profile.Name = uniqueId;
			}

			return true;
		}

		private static void ApplyDefinition(CharacterProfile profile, UnitDefinition definition)
		{
			profile.Model = definition.kind ?? profile.Model;
			profile.AttackType = definition.attackType ?? profile.AttackType;
			profile.Icon = definition.icon ?? profile.Icon;
			profile.Background = definition.background ?? profile.Background;
			profile.UnitOnMap = definition.unitOnMap ?? profile.UnitOnMap;
			profile.PortraitBackgroundColor = definition.portraitBackgroundColor ?? profile.PortraitBackgroundColor;
			profile.DefaultSkill = definition.defaultSkill ?? profile.DefaultSkill;
			if (definition.soundConfig != null)
			{
				profile.SoundAssetId = definition.soundConfig.assetId ?? profile.SoundAssetId;
				if (definition.soundConfig.soundClips != null)
				{
					profile.SoundClips.Clear();
					foreach (KeyValuePair<string, string> clip in definition.soundConfig.soundClips)
					{
						profile.SoundClips[clip.Key] = clip.Value;
					}
				}
			}

			if (definition.cinematicTags != null)
			{
				profile.CinematicTags.Clear();
				profile.CinematicTags.AddRange(definition.cinematicTags);
			}

			if (definition.skills != null)
			{
				profile.Skills.Clear();
				profile.Skills.AddRange(definition.skills);
			}

			if (definition.skillProgression != null)
			{
				profile.SkillProgression.Clear();
				foreach (KeyValuePair<int, List<string>> entry in definition.skillProgression)
				{
					LevelSkillEntry row = new LevelSkillEntry { Level = entry.Key };
					if (entry.Value != null)
					{
						row.SkillIds.AddRange(entry.Value);
					}

					profile.SkillProgression.Add(row);
				}
			}

			profile.States.Clear();
			if (definition.states != null)
			{
				foreach (KeyValuePair<string, int> state in definition.states)
				{
					profile.States[state.Key] = state.Value != 0;
				}
			}

			profile.Levels.Clear();
			CharacterLevel level = ReadLevel(definition);
			if (level != null)
			{
				profile.Levels.Add(level);
			}
		}

		private static CharacterLevel ReadLevel(UnitDefinition definition)
		{
			if (definition == null || definition.stats == null)
			{
				return null;
			}

			CharacterLevel level = new CharacterLevel
			{
				Level = Mathf.RoundToInt(StatOr(definition, "level", 1f))
			};
			foreach (KeyValuePair<string, float> stat in definition.stats)
			{
				level.Stats.Add(new StatEntry { Name = stat.Key, Value = stat.Value });
			}

			return level;
		}

		private static bool TryFindLevel1(string uniqueId, out UnitDefinition definition)
		{
			definition = null;
			foreach (KeyValuePair<string, UnitDefinition> pair in CharacterAPI.KnownUnitDefinitions)
			{
				UnitDefinition candidate = pair.Value;
				if (candidate == null
					|| !string.Equals(candidate.uniqueId, uniqueId, StringComparison.Ordinal)
					|| Mathf.RoundToInt(StatOr(candidate, "level", -1f)) != 1)
				{
					continue;
				}

				definition = candidate;
				return true;
			}

			return false;
		}

		private static bool TryGetDefinition(string id, out UnitDefinition definition)
		{
			if (CharacterAPI.KnownUnitDefinitions.TryGetValue(id, out definition) && definition != null)
			{
				return true;
			}

			definition = null;
			return false;
		}

		private static void ReadRosterFields(string uniqueId, CharacterProfile profile)
		{
			profile.Locked = false;
			profile.UnlockAchievement = string.Empty;
			profile.Tier = CharacterTier.Companion;
			if (!MetagameManager.IsInstanceValid)
			{
				return;
			}

			HeroRosterManager rosterManager =
				Traverse.Create(MetagameManager.instanceNoLoad).Field<HeroRosterManager>("heroRosterManager").Value;
			if (rosterManager == null)
			{
				return;
			}

			HeroRosterConfig config =
				Traverse.Create(rosterManager).Field<HeroRosterConfig>("heroRosterConfig").Value;
			if (config == null || config.all == null
				|| !config.all.TryGetValue(uniqueId, out HeroRosterConfig.HeroConfig hero)
				|| hero == null)
			{
				return;
			}

			profile.Locked = hero.locked;
			profile.UnlockAchievement = hero.unlockAchievement ?? string.Empty;
			if (config.legends != null)
			{
				for (int i = 0; i < config.legends.Count; i++)
				{
					if (config.legends[i] != null
						&& string.Equals(config.legends[i].id, uniqueId, StringComparison.Ordinal))
					{
						profile.Tier = CharacterTier.Legend;
						return;
					}
				}
			}
		}

		private static void ReadLocalization(CharacterProfile profile)
		{
			string stem = VanillaOverrideRules.LocStem(profile.VanillaNameStem, profile.VanillaSourceUniqueId);
			if (string.IsNullOrEmpty(stem) || LocalizationManager.instance == null)
			{
				return;
			}

			string name = LocalizationManager.instance.TryLocalizeString(LabCatalogRules.UnitNameLocKey(stem));
			string lore = LocalizationManager.instance.TryLocalizeString(LabCatalogRules.UnitLoreLocKey(stem));
			if (!string.IsNullOrEmpty(name) && name != LabCatalogRules.UnitNameLocKey(stem))
			{
				profile.Name = name;
			}

			if (!string.IsNullOrEmpty(lore) && lore != LabCatalogRules.UnitLoreLocKey(stem))
			{
				profile.Description = lore;
			}
		}

		private static string ReadDisplayName(UnitDefinition definition)
		{
			string stem = VanillaOverrideRules.LocStem(definition.name, definition.uniqueId);
			if (LocalizationManager.instance != null && !string.IsNullOrEmpty(stem))
			{
				string localized = LocalizationManager.instance.TryLocalizeString(LabCatalogRules.UnitNameLocKey(stem));
				if (!string.IsNullOrEmpty(localized) && localized != LabCatalogRules.UnitNameLocKey(stem))
				{
					return localized;
				}
			}

			return !string.IsNullOrEmpty(definition.uniqueId) ? definition.uniqueId : definition.id;
		}

		/// <summary>Renames a leftover override folder onto slug_token and reconstructs a missing rig.</summary>
		internal static string HealLoadedOverride(string folder, CharacterProfile profile)
		{
			if (string.IsNullOrEmpty(folder) || profile == null || !profile.IsVanillaOverride)
			{
				return folder;
			}

			string healed = EnsureSlugTokenFolder(folder, profile.VanillaSourceUniqueId);
			profile.Id = Path.GetFileName(
				healed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			bool imported = TryImportVanillaRig(healed, profile);
			bool renamed = !string.Equals(healed, folder, StringComparison.Ordinal);
			if (imported || renamed)
			{
				CharacterProfileSidecar.Save(healed, profile);
				WriteProjectMarker(healed, profile);
				RLHeroesGenerator.Sync(healed, profile);
			}

			return healed;
		}

		/// <summary>Moves a leftover named override folder onto a minted slug_token under CharactersRoot.</summary>
		private static string EnsureSlugTokenFolder(string folder, string uniqueId)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return folder;
			}

			string name = Path.GetFileName(
				folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (LabSlugIds.LooksLikeSlugTokenId(name))
			{
				return folder;
			}

			string root = CharacterLabPaths.CharactersRoot;
			string parent = Path.GetDirectoryName(Path.GetFullPath(folder));
			if (!string.Equals(parent, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
			{
				return folder;
			}

			string folderId = CharacterLabPaths.GenerateNewCharacterId(
				LabSlugIds.LegalizeSlug(uniqueId, "hero"));
			string dest = Path.Combine(root, folderId);
			try
			{
				Directory.Move(folder, dest);
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning(
					"Vanilla extract: could not rename '" + name + "' to '" + folderId + "' — " + ex.Message);
				return folder;
			}

			LokrCharacterLabPlugin.Log.LogInfo(
				"Vanilla extract: renamed leftover folder '" + name + "' to '" + folderId + "'.");
			return dest;
		}

		/// <summary>Reconstructs the shipped combat exo into this folder when rig.json is missing or map-only.</summary>
		private static bool TryImportVanillaRig(string folder, CharacterProfile profile)
		{
			if (profile == null || string.IsNullOrEmpty(folder))
			{
				return false;
			}

			if (string.IsNullOrEmpty(profile.VanillaMetaExo) && string.IsNullOrEmpty(profile.Model))
			{
				return false;
			}

			string rigPath = Path.Combine(folder, "rig", "rig.json");
			if (File.Exists(rigPath) && ExoImportRules.JsonHasCombatClip(File.ReadAllText(rigPath)))
			{
				return false;
			}

			if (!CharacterImporter.ImportInto(
				profile.VanillaMetaExo,
				profile.Model,
				folder,
				null,
				out string message))
			{
				LokrCharacterLabPlugin.Log.LogWarning("Vanilla extract: could not reconstruct rig — " + message);
				return false;
			}

			LokrCharacterLabPlugin.Log.LogInfo(
				"Vanilla extract: reconstructed rig from Model '" + profile.Model
				+ "' / MetaExo '" + profile.VanillaMetaExo + "'.");
			return true;
		}

		private static void WriteProjectMarker(string folder, CharacterProfile profile)
		{
			string display = TextEscapingSafe(profile.Name);
			string vanilla = TextEscapingSafe(profile.VanillaSourceUniqueId);
			File.WriteAllText(
				Path.Combine(folder, "project.json"),
				"{\"projectType\":\"character\",\"schemaVersion\":1,\"displayName\":\""
				+ display + "\",\"vanillaSourceUniqueId\":\"" + vanilla + "\"}");
		}

		private static string TextEscapingSafe(string value)
		{
			return LokrModAPI.Serialization.TextEscaping.JsonEscape(value ?? string.Empty);
		}

		private static float StatOr(UnitDefinition definition, string name, float fallback)
		{
			if (definition == null || definition.stats == null || string.IsNullOrEmpty(name))
			{
				return fallback;
			}

			float value;
			return definition.stats.TryGetValue(name, out value) ? value : fallback;
		}

		private static string ReadJsonStringField(string path, string field)
		{
			if (!File.Exists(path))
			{
				return null;
			}

			Match match = Regex.Match(File.ReadAllText(path), "\"" + field + "\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : null;
		}
	}
}
