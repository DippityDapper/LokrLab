using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>The rig-side checklist rows, read directly from &lt;folder&gt;/rig/rig.json on disk -- deliberately not through RigEditorScene's live in-memory state, since General can show a character that isn't the one currently open in the Animator.</summary>
	/// <remarks>Mirrors CustomRigLoader's required-animation-name check and RequiredAnimationNamesValidator's Stand/Portrait wording, plus CombatSequenceNames.ForModel for the combat clips the character's Model prefab actually looks up, plus Head/Chest/Base attach points and AbilityAction/AbilityEnd events combat needs after the custom rig is swapped onto that prefab (Death needs AbilityEnd or DeathActivity never finishes), all sourced from raw JSON instead of a live List&lt;AnimationClip&gt;.</remarks>
	internal static class AnimatorReadinessChecks
	{
		/// <summary>Registers the rig-side readiness checks.</summary>
		internal static void RegisterDefaults()
		{
			CharacterReadinessRegistry.RegisterCheck("At least one part", CheckAtLeastOnePart);
			CharacterReadinessRegistry.RegisterCheck("Stand/Portrait animations", CheckStandAndPortrait);
			CharacterReadinessRegistry.RegisterCheck("Combat animation names", CheckCombatAnimationNames);
			CharacterReadinessRegistry.RegisterCheck("Combat attach points", CheckCombatAttachPoints);
			CharacterReadinessRegistry.RegisterCheck("Combat clip events", CheckCombatClipEvents);
		}

		/// <summary>Checks that a rig has been authored and has at least one part.</summary>
		private static IEnumerable<ReadinessItem> CheckAtLeastOnePart(string folder, CharacterProfile profile)
		{
			if (!TryParseRigJson(folder, out JSONNode root))
			{
				yield return new ReadinessItem("No rig has been authored yet — open the Rig Editor and import some parts.", ReadinessSeverity.Error);
				yield break;
			}
			if (root["parts"].Count == 0)
			{
				yield return new ReadinessItem("No parts loaded — nothing to render yet.", ReadinessSeverity.Error);
			}
		}

		/// <summary>Checks Stand and Portrait/StandStatic presence. Yields nothing if the rig couldn't be parsed at all -- already reported by CheckAtLeastOnePart.</summary>
		private static IEnumerable<ReadinessItem> CheckStandAndPortrait(string folder, CharacterProfile profile)
		{
			if (!TryParseRigJson(folder, out JSONNode root))
			{
				yield break;
			}
			bool hasStand = false;
			bool hasPortraitOrStandStatic = false;
			foreach (JSONNode animNode in root["animations"].Children)
			{
				string name = animNode["name"].Value;
				if (name == "Stand")
				{
					hasStand = true;
				}
				else if (name == "Portrait" || name == "StandStatic")
				{
					hasPortraitOrStandStatic = true;
				}
			}
			if (!hasStand)
			{
				yield return new ReadinessItem("Missing \"Stand\" animation — every base-game read of Hero.exoSkeletonDataAsset (map hero bar, party visual, buff store, reward screen, dialog views) throws without it.", ReadinessSeverity.Error);
			}
			if (!hasPortraitOrStandStatic)
			{
				yield return new ReadinessItem("Missing \"Portrait\" or \"StandStatic\" animation — same crash class as a missing \"Stand\".", ReadinessSeverity.Error);
			}
		}

		/// <summary>Warns for each combat sequenceName the character's Model prefab looks up that is missing from rig.json. Save backfills these from Rest Pose; authoring the real clip is what makes the attack/walk/etc. actually animate.</summary>
		private static IEnumerable<ReadinessItem> CheckCombatAnimationNames(string folder, CharacterProfile profile)
		{
			if (!TryParseRigJson(folder, out JSONNode root))
			{
				yield break;
			}
			HashSet<string> present = new HashSet<string>();
			foreach (JSONNode animNode in root["animations"].Children)
			{
				present.Add(animNode["name"].Value);
			}
			string model = (profile == null || string.IsNullOrEmpty(profile.Model)) ? "HumanArcher" : profile.Model;
			List<string> missing = new List<string>();
			foreach (string name in CombatSequenceNames.ForModel(model))
			{
				if (!present.Contains(name))
				{
					missing.Add(name);
				}
			}
			if (missing.Count > 0)
			{
				yield return new ReadinessItem(
					"Model \"" + model + "\" looks up combat clip(s) this rig does not have: " + string.Join(", ", missing.ToArray())
					+ " — Save will backfill a rest-pose stub; author a real clip under that exact name or attacks/walks that need it will throw at runtime.",
					ReadinessSeverity.Warning);
			}
		}

		/// <summary>Warns when no authored frame carries Head/Chest/Base -- combat SourcePos/dialog look those up on the swapped custom rig.</summary>
		private static IEnumerable<ReadinessItem> CheckCombatAttachPoints(string folder, CharacterProfile profile)
		{
			if (!TryParseRigJson(folder, out JSONNode root))
			{
				yield break;
			}
			HashSet<string> present = new HashSet<string>();
			foreach (JSONNode animNode in root["animations"].Children)
			{
				foreach (JSONNode frameNode in animNode["frames"].Children)
				{
					foreach (JSONNode attachNode in frameNode["attachPoints"].Children)
					{
						present.Add(attachNode["name"].Value);
					}
				}
			}
			List<string> missing = new List<string>();
			foreach (string name in CombatPlaybackRequirements.AttachPointNames)
			{
				if (!present.Contains(name))
				{
					missing.Add(name);
				}
			}
			if (missing.Count > 0)
			{
				yield return new ReadinessItem(
					"No frame defines attach point(s) " + string.Join(", ", missing.ToArray())
					+ " — combat looks these up on the custom rig (not the Model prefab). Save will place stubs from the rest pose; author real Head/Chest/Base sockets or projectiles/dialog spawn at the origin.",
					ReadinessSeverity.Warning);
			}
		}

		/// <summary>Warns when Attack/SpecialAttack/SpellCast clips have no AbilityAction, and when those clips plus Death have no AbilityEnd -- DeathActivity never finishes without it, which freezes the encounter.</summary>
		private static IEnumerable<ReadinessItem> CheckCombatClipEvents(string folder, CharacterProfile profile)
		{
			if (!TryParseRigJson(folder, out JSONNode root))
			{
				yield break;
			}
			List<string> missingAction = new List<string>();
			List<string> missingEnd = new List<string>();
			foreach (JSONNode animNode in root["animations"].Children)
			{
				string clipName = animNode["name"].Value;
				bool needsAction = CombatPlaybackRequirements.NeedsCombatEvents(clipName);
				bool needsEnd = CombatPlaybackRequirements.NeedsAbilityEndEvent(clipName);
				if (!needsAction && !needsEnd)
				{
					continue;
				}
				bool hasAction = false;
				bool hasEnd = false;
				foreach (JSONNode frameNode in animNode["frames"].Children)
				{
					foreach (JSONNode eventNode in frameNode["events"].Children)
					{
						if (eventNode.Value == CombatPlaybackRequirements.AbilityActionEvent)
						{
							hasAction = true;
						}
						if (eventNode.Value == CombatPlaybackRequirements.AbilityEndEvent)
						{
							hasEnd = true;
						}
					}
				}
				if (needsAction && !hasAction)
				{
					missingAction.Add(clipName);
				}
				if (needsEnd && !hasEnd)
				{
					missingEnd.Add(clipName);
				}
			}
			if (missingAction.Count > 0)
			{
				yield return new ReadinessItem(
					"Clip(s) " + string.Join(", ", missingAction.ToArray())
					+ " have no AbilityAction event — the attack animation will play but OnAbilityAction (the projectile/hit) never runs. Save will add one on the first frame.",
					ReadinessSeverity.Warning);
			}
			if (missingEnd.Count > 0)
			{
				yield return new ReadinessItem(
					"Clip(s) " + string.Join(", ", missingEnd.ToArray())
					+ " have no AbilityEnd event — the activity never finishes after the animation (DeathActivity.Update is empty, so a missing Death AbilityEnd freezes the encounter). Save will add one on the last frame.",
					ReadinessSeverity.Warning);
			}
		}

		/// <summary>Parses &lt;folder&gt;/rig/rig.json, returning false if it's missing or malformed.</summary>
		private static bool TryParseRigJson(string folder, out JSONNode root)
		{
			root = null;
			string path = Path.Combine(folder, "rig", "rig.json");
			if (!File.Exists(path))
			{
				return false;
			}
			try
			{
				root = JSON.Parse(File.ReadAllText(path));
				return true;
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("AnimatorReadinessChecks: failed to parse " + path + ": " + ex.Message);
				return false;
			}
		}
	}
}
