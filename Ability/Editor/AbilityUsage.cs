using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLoader;
using LokrLab;
using LokrLab.Editor.General;
using LokrModAPI;

namespace LokrAbilityLab.Editor
{
	/// <summary>Who references an ability id, plus cheap walks of the body tree.</summary>
	internal static class AbilityUsage
	{
		/// <summary>Every unit/character id that references an ability, plus whether any of them is tutorial-only content. Phase 4's "Used by" blast radius (docs/roadmaps/started/vanilla-ability-edit.md).</summary>
		internal sealed class BlastRadiusResult
		{
			internal readonly List<string> UnitIds = new List<string>();
			internal bool UsedInTutorialContent;
		}

		/// <summary>Every unit/character id that references an ability, for the copy-confirm warning before an Override/Fork commit.</summary>
		/// <remarks>
		/// Unlike CharactersUsing (which AbilitySandboxViewport relies on to pick used[0] as the
		/// sandbox caster -- deliberately left alone so that selection doesn't change), this always
		/// unions every source instead of stopping once one source finds a hit: live
		/// CharacterAPI.KnownUnitDefinitions (currently-loaded content, including mods), the full
		/// live vanilla catalog (VanillaUnitCatalog -- so a not-yet-loaded vanilla hero/enemy still
		/// shows up), and Lab character.json folders (parsed structurally via
		/// CharacterProfileSidecar, not ScanDiskCharacters' raw substring match, which false-positives
		/// on any string field that happens to equal the ability id).
		/// </remarks>
		internal static BlastRadiusResult BlastRadius(string abilityId)
		{
			BlastRadiusResult result = new BlastRadiusResult();
			if (string.IsNullOrEmpty(abilityId))
			{
				return result;
			}

			EnsureDefinitionsLoaded();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, UnitDefinition> entry in CharacterAPI.KnownUnitDefinitions)
			{
				if (entry.Value != null && References(entry.Value, abilityId) && seen.Add(entry.Key))
				{
					result.UnitIds.Add(entry.Key);
				}
			}

			foreach (KeyValuePair<string, UnitDefinition> entry in VanillaUnitCatalog.All())
			{
				if (entry.Value == null || !References(entry.Value, abilityId) || !seen.Add(entry.Key))
				{
					continue;
				}

				result.UnitIds.Add(entry.Key);
				if (VanillaUnitCatalog.IsFromTutorialAsset(entry.Key))
				{
					result.UsedInTutorialContent = true;
				}
			}

			ScanLabCharactersStructured(abilityId, result.UnitIds, seen);

			result.UnitIds.Sort(StringComparer.OrdinalIgnoreCase);
			return result;
		}

		/// <summary>Structured Lab character.json scan (CharacterProfileSidecar), for BlastRadius -- unlike ScanDiskCharacters, does not false-positive on a name/description string that happens to equal the ability id.</summary>
		private static void ScanLabCharactersStructured(string abilityId, List<string> names, HashSet<string> seen)
		{
			if (ModAPI.Files == null)
			{
				return;
			}

			foreach (string category in new[] { "LokrCharacterLab", "Characters" })
			{
				foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(category))
				{
					CharacterProfile profile = CharacterProfileSidecar.Load(itemFolder);
					if (profile == null || !ReferencesProfile(profile, abilityId))
					{
						continue;
					}

					string id = !string.IsNullOrEmpty(profile.Id) ? profile.Id : System.IO.Path.GetFileName(itemFolder);
					if (seen.Add(id))
					{
						names.Add(id);
					}
				}
			}
		}

		private static bool ReferencesProfile(CharacterProfile profile, string abilityId)
		{
			if (string.Equals(profile.DefaultSkill, abilityId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (Contains(profile.Skills, abilityId))
			{
				return true;
			}

			if (profile.SkillProgression != null)
			{
				foreach (LevelSkillEntry entry in profile.SkillProgression)
				{
					if (entry != null && Contains(entry.SkillIds, abilityId))
					{
						return true;
					}
				}
			}

			return false;
		}

		internal static string DisplayName(string unitId)
		{
			if (string.IsNullOrEmpty(unitId))
			{
				return "(dummy)";
			}

			if (CharacterAPI.KnownUnitDefinitions.TryGetValue(unitId, out UnitDefinition definition)
				&& definition != null && !string.IsNullOrEmpty(definition.name)
				&& definition.name != unitId)
			{
				return definition.name;
			}

			return unitId;
		}

		internal static string IdFromDisplay(IList<string> unitIds, string display)
		{
			if (unitIds == null)
			{
				return display;
			}

			for (int i = 0; i < unitIds.Count; i++)
			{
				if (string.Equals(DisplayName(unitIds[i]), display, StringComparison.Ordinal)
					|| string.Equals(unitIds[i], display, StringComparison.Ordinal))
				{
					return unitIds[i];
				}
			}

			return display;
		}

		internal static List<string> CharactersUsing(string abilityId)
		{
			List<string> names = new List<string>();
			if (string.IsNullOrEmpty(abilityId))
			{
				return names;
			}

			EnsureDefinitionsLoaded();
			foreach (KeyValuePair<string, UnitDefinition> entry in CharacterAPI.KnownUnitDefinitions)
			{
				if (entry.Value != null && References(entry.Value, abilityId))
				{
					names.Add(entry.Key);
				}
			}

			if (names.Count == 0)
			{
				ScanDiskCharacters(abilityId, names);
			}

			names.Sort(StringComparer.OrdinalIgnoreCase);
			return names;
		}

		/// <summary>Main-menu lab never touches the parser, so KnownUnitDefinitions stays empty until something constructs it.</summary>
		internal static void EnsureDefinitionsLoaded()
		{
			try
			{
				UnityDefinitionsParser unused = UnityDefinitionsParser.instance;
			}
			catch (Exception)
			{
			}
		}

		private static void ScanDiskCharacters(string abilityId, List<string> names)
		{
			if (ModAPI.Files == null)
			{
				return;
			}

			foreach (string category in new[] { "LokrCharacterLab", "Characters" })
			{
				foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(category))
				{
					string jsonPath = System.IO.Path.Combine(itemFolder, "character.json");
					if (!System.IO.File.Exists(jsonPath))
					{
						continue;
					}

					string json = System.IO.File.ReadAllText(jsonPath);
					if (json.IndexOf("\"" + abilityId + "\"", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}

					string id = SpawnIdFromCharacterFolder(itemFolder, json);
					if (!names.Contains(id))
					{
						names.Add(id);
					}
				}
			}
		}

		/// <summary>Character project folder for a unit id. Lab characters live under LokrCharacterLab/; leftover Characters/ folders still count.</summary>
		internal static bool TryFindCharacterFolder(string unitId, out string folder)
		{
			folder = null;
			if (string.IsNullOrEmpty(unitId) || ModAPI.Files == null)
			{
				return false;
			}

			if (ModAPI.Files.TryFindFile("LokrCharacterLab", unitId + "/project.json", out string project)
				|| ModAPI.Files.TryFindFile("Characters", unitId + "/project.json", out project))
			{
				folder = System.IO.Path.GetDirectoryName(project);
				return true;
			}

			foreach (string category in new[] { "LokrCharacterLab", "Characters" })
			{
				foreach ((string _, string itemFolder) in ModAPI.Files.EnumerateCategorySubfolders(category))
				{
					if (string.Equals(System.IO.Path.GetFileName(itemFolder), unitId, StringComparison.OrdinalIgnoreCase))
					{
						folder = itemFolder;
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>Engine UniqueId for a vanilla override folder, otherwise the folder name.</summary>
		private static string SpawnIdFromCharacterFolder(string itemFolder, string json)
		{
			string folderId = System.IO.Path.GetFileName(itemFolder);
			Match match = Regex.Match(json ?? string.Empty, "\"vanillaSourceUniqueId\"\\s*:\\s*\"([^\"]*)\"");
			string vanilla = match.Success ? match.Groups[1].Value : null;
			return VanillaOverrideRules.EngineUniqueId(vanilla, folderId);
		}

		internal static bool HasProjectile(AbilityFileModel model)
		{
			return FirstCard(model, "TrackingProjectile") != null;
		}

		internal static ActionCard FirstCard(AbilityFileModel model, string typeId)
		{
			if (model == null || model.Body == null)
			{
				return null;
			}

			foreach (EventNode ev in model.Body.Events)
			{
				ActionCard found = FirstCard(ev.Cards, typeId);
				if (found != null)
				{
					return found;
				}
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					ActionCard found = FirstCard(ev.Cards, typeId);
					if (found != null)
					{
						return found;
					}
				}
			}

			return null;
		}

		internal static void WalkCards(AbilityFileModel model, Action<string, ActionCard> visit)
		{
			if (model == null || model.Body == null || visit == null)
			{
				return;
			}

			foreach (EventNode ev in model.Body.Events)
			{
				WalkCards(ev.Name, ev.Cards, visit);
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					WalkCards(mod.Id + "." + ev.Name, ev.Cards, visit);
				}
			}
		}

		internal static bool TryParseNumber(string expression, out float value)
		{
			return float.TryParse(
				(expression ?? string.Empty).Trim(),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out value);
		}

		private static bool References(UnitDefinition unit, string abilityId)
		{
			if (string.Equals(unit.defaultSkill, abilityId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (Contains(unit.skills, abilityId) || Contains(unit.skillPool, abilityId))
			{
				return true;
			}

			if (unit.skillProgression == null)
			{
				return false;
			}

			foreach (List<string> rank in unit.skillProgression.Values)
			{
				if (Contains(rank, abilityId))
				{
					return true;
				}
			}

			return false;
		}

		private static bool Contains(IEnumerable<string> list, string abilityId)
		{
			if (list == null)
			{
				return false;
			}

			foreach (string value in list)
			{
				if (string.Equals(value, abilityId, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static ActionCard FirstCard(List<ActionCard> cards, string typeId)
		{
			if (cards == null)
			{
				return null;
			}

			foreach (ActionCard card in cards)
			{
				if (card != null && card.TypeId == typeId)
				{
					return card;
				}

				if (card == null)
				{
					continue;
				}

				foreach (List<ActionCard> stack in card.ChildStacks.Values)
				{
					ActionCard found = FirstCard(stack, typeId);
					if (found != null)
					{
						return found;
					}
				}
			}

			return null;
		}

		private static void WalkCards(string hat, List<ActionCard> cards, Action<string, ActionCard> visit)
		{
			if (cards == null)
			{
				return;
			}

			foreach (ActionCard card in cards)
			{
				if (card == null)
				{
					continue;
				}

				visit(hat, card);
				foreach (List<ActionCard> stack in card.ChildStacks.Values)
				{
					WalkCards(hat, stack, visit);
				}
			}
		}
	}
}
