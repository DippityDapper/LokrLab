using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LokrCharacterLoader;
using LokrLab;

namespace LokrAbilityLab.Editor
{
	/// <summary>Save-time checks: block parse killers, warn on catalog misses and hero-room traps.</summary>
	internal static class AbilityValidation
	{
		/// <summary>CallFunction type names whose Execute throws when the named filter matches nobody.</summary>
		private static readonly string[] EmptyFilterCallFunctions =
		{
			"ClosestTargetPreferNoFlip",
			"KrumSelectTargets",
			"SBFAspectPhysicalTeleportTarget",
			"SBFAspectSummonerTeleportTarget",
			"WBFIrizaTeleportTarget",
			"WBFOverseerSelectTentacleSpawn",
		};

		/// <summary>Returns false when the file would fail AbilityParser or use an illegal On* name.</summary>
		internal static bool TryValidate(AbilityFileModel model, out string error, out string warnings)
		{
			error = null;
			warnings = CollectWarnings(model);
			if (model == null)
			{
				error = "Nothing to save.";
				return false;
			}

			if (!model.IsPassive)
			{
				if (!model.BehaviorFlags.Contains("SELF_TARGET") && string.IsNullOrEmpty(model.CastRange))
				{
					error = "Cast Range is required (the parser throws on an empty expression).";
					return false;
				}

				if (string.IsNullOrEmpty(model.Cooldown))
				{
					error = "Cooldown is required (the parser throws on an empty expression).";
					return false;
				}

				if (string.IsNullOrEmpty(model.APCost))
				{
					error = "AP Cost is required (the parser throws on an empty expression).";
					return false;
				}

				if (model.BehaviorFlags.Contains("AOE") && string.IsNullOrEmpty(model.AOERange))
				{
					error = "AOE Range is required when Behavior includes AOE.";
					return false;
				}
			}

			if (TryFindIllegalEvent(model, out string illegal))
			{
				error = "Unknown event name \"" + illegal + "\" — the game throws Unrecogniced AbilityEventName. Use a name from AbilityEvents / ModifierEvents.";
				return false;
			}

			if (TryFindCard(model, card => card.TypeId == "PerAffectedAI"))
			{
				error = "PerAffectedAI is not an AbilityAction; the loader skips it. Use a real OnThink action (GetInRangeAI, KeepDistanceAI2, RetreatIfWeekAI, …).";
				return false;
			}

			if (TryFindIllegalHitTag(model, out string badTag))
			{
				error = "Hit Tags token \"" + badTag + "\" is not in HitAction.ValidateTags. Use #PROJECTILE, #MELEE, #TARGETED, …";
				return false;
			}

			return true;
		}

		/// <summary>Non-blocking notes shown after load/save.</summary>
		internal static string CollectWarnings(AbilityFileModel model)
		{
			if (model == null)
			{
				return string.Empty;
			}

			List<string> notes = new List<string>();
			if (model.IsPassive && string.IsNullOrEmpty(model.Icon))
			{
				notes.Add("PASSIVE with no Icon — the hero-room trait slot stays hidden.");
			}

			if (model.BehaviorFlags.Contains("POINT_TARGET"))
			{
				notes.Add("POINT_TARGET: the parser overwrites AbilityTeamFilter to TEAM_ALL.");
			}

			if (model.BehaviorFlags.Contains("MELEE") && model.BehaviorFlags.Contains("POINT_TARGET"))
			{
				notes.Add("MELEE + POINT_TARGET: melee select NullRefs because POINT_TARGET never creates targetFilter.");
			}

			if (LabCatalogRules.ShouldWarnRangeCone(model.AOEKind))
			{
				notes.Add("RANGE_CONE: combat never fills cone hexes (CalculateAOE / ActOnHexas / PassesFilter). Use RANGE_CIRCLE or RANGE_TUNNEL.");
			}

			if (!model.BehaviorFlags.Contains("AOE")
				&& (!string.IsNullOrEmpty(model.AOERange) || !string.IsNullOrEmpty(model.AOEMinRange) || !string.IsNullOrEmpty(model.AOEWidth)))
			{
				notes.Add("AOE fields are set but Behavior does not include AOE — the parser ignores them.");
			}

			if (!string.IsNullOrEmpty(model.CastFXId) && !AbilityCatalogLookups.IsKnownFxMega(model.CastFXId))
			{
				notes.Add("CastFXId \"" + model.CastFXId + "\" is not a vanilla or custom FXMega (LoadFXMega throws in combat).");
			}

			if (!string.IsNullOrEmpty(model.AnimationId) && model.AnimationId != "NOANIMATION")
			{
				if (!AbilityCatalogLookups.IsKnownClip(model.AnimationId))
				{
					notes.Add("AnimationID \"" + model.AnimationId + "\" is not a vanilla or Character Lab clip name — the caster rig still needs that clip with AbilityAction / AbilityEnd.");
				}
				else
				{
					notes.Add("Cast clip \"" + model.AnimationId + "\" must exist on the caster rig with AbilityAction / AbilityEnd.");
				}
			}

			WarnDeadEvents(model, notes);
			WarnAiBlocks(model, notes);
			WarnCards(model, notes, CollectLocalModifierIds(model.Body));
			return notes.Count == 0 ? string.Empty : string.Join("  ", notes);
		}

		/// <summary>Modifier ids defined in this ability's own Modifiers block.</summary>
		private static HashSet<string> CollectLocalModifierIds(AbilityBody body)
		{
			HashSet<string> ids = new HashSet<string>();
			if (body == null || body.Modifiers == null)
			{
				return ids;
			}

			foreach (ModifierDef mod in body.Modifiers)
			{
				if (mod != null && !string.IsNullOrEmpty(mod.Id))
				{
					ids.Add(mod.Id);
				}
			}

			return ids;
		}

		private static void WarnDeadEvents(AbilityFileModel model, List<string> notes)
		{
			if (model.Body == null)
			{
				return;
			}

			foreach (EventNode ev in model.Body.Events)
			{
				if (AbilityEventNames.IsDeadAbilityEvent(ev.Name))
				{
					notes.Add(ev.Name + " is parse-legal but the engine never dispatches it.");
				}
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					if (AbilityEventNames.IsDeadModifierEvent(ev.Name))
					{
						notes.Add("Modifier " + ev.Name + " is parse-legal but the engine never dispatches it.");
					}
				}
			}
		}

		private static void WarnAiBlocks(AbilityFileModel model, List<string> notes)
		{
			if (model.Body == null || model.Body.Ai == null)
			{
				return;
			}

			foreach (AiBlock block in model.Body.Ai)
			{
				if (block == null)
				{
					continue;
				}

				if (HasEmptyConsiderations(block.InnerKv))
				{
					notes.Add((string.IsNullOrEmpty(block.Name) ? "AIConfigB" : block.Name)
						+ " Considerations is empty — AIDecisionScoreEvaluator.Eval divides by zero on think.");
				}
			}
		}

		private static void WarnCards(AbilityFileModel model, List<string> notes, HashSet<string> localModifiers)
		{
			AbilityBody body = model != null ? model.Body : null;
			if (body == null)
			{
				return;
			}

			bool pointTarget = model.BehaviorFlags != null && model.BehaviorFlags.Contains("POINT_TARGET");
			ForEachCard(model, card =>
			{
				if (pointTarget && card.TypeId == "GetCloseToUnitAI")
				{
					notes.Add("GetCloseToUnitAI under POINT_TARGET NullRefs (targetFilter is never created).");
				}
			});

			foreach (ModifierDef mod in body.Modifiers)
			{
				if (!string.IsNullOrEmpty(mod.ModifierFXName)
					&& !AbilityCatalogLookups.IsKnownFxMega(mod.ModifierFXName))
				{
					notes.Add("ModifierFXName \"" + mod.ModifierFXName + "\" is not a vanilla or custom FXMega.");
				}

				WarnStatKeysFromKv("PropertiesAdd", mod.PropertiesAddKv, notes);
				WarnStatKeysFromKv("PropertiesMult", ExtractNamedBlock(mod.ExtraKv, "PropertiesMult"), notes);
			}

			foreach (EventNode ev in body.Events)
			{
				foreach (ActionCard card in ev.Cards)
				{
					WarnCard(model, card, notes, localModifiers);
				}
			}

			foreach (ModifierDef mod in body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					foreach (ActionCard card in ev.Cards)
					{
						WarnCard(model, card, notes, localModifiers);
					}
				}
			}
		}

		private static void WarnCard(AbilityFileModel model, ActionCard card, List<string> notes, HashSet<string> localModifiers)
		{
			if (card == null || card.IsOpaque)
			{
				return;
			}

			if (card.TypeId == "Lua")
			{
				if (card.Fields == null || !card.Fields.TryGetValue("Action", out string luaAction)
					|| string.IsNullOrEmpty(luaAction) || string.IsNullOrEmpty(luaAction.Trim()))
				{
					notes.Add("Lua Action is empty — AbilityParser drops the ability if LUA_ACTION fails.");
				}
				else if (AbilityLuaRules.ContainsDoubleQuote(luaAction))
				{
					notes.Add("Lua Action contains a double quote; KV1 cannot round-trip that. Use single quotes.");
				}
			}

			if ((card.TypeId == "ApplyModifier" || card.TypeId == "RemoveModifier")
				&& card.Fields != null && card.Fields.TryGetValue("ModifierName", out string modifierName)
				&& !string.IsNullOrEmpty(modifierName)
				&& !AbilityCatalogLookups.IsKnownModifier(modifierName, localModifiers))
			{
				notes.Add(card.TypeId + " \"" + AbilityCatalogLookups.StripHash(modifierName)
					+ "\" is not defined on this ability or loaded in ability_modifiers.");
			}

			if (card.TypeId == "CallFunction"
				&& card.Fields != null && card.Fields.TryGetValue("Function", out string function)
				&& IsEmptyFilterCallFunction(function))
			{
				notes.Add("CallFunction \"" + function + "\" throws if its filter matches nobody (empty board / no heroes / no tentacle markers).");
			}

			if (card.TypeId == "SetStat"
				&& card.Fields != null && card.Fields.TryGetValue("Stat", out string stat)
				&& !string.IsNullOrEmpty(stat)
				&& !AbilityCatalogLookups.IsKnownStatRef(stat))
			{
				notes.Add("SetStat Stat \"" + stat + "\" is not a known stat key.");
			}

			ActionCardDescriptor descriptor = AbilityCardRegistry.Find(card.TypeId);
			if (descriptor != null)
			{
				foreach (KeyValuePair<string, ActionCardCatalogKind> pair in descriptor.FieldCatalogs)
				{
					if (!card.Fields.TryGetValue(pair.Key, out string value) || string.IsNullOrEmpty(value))
					{
						continue;
					}

					if (pair.Value == ActionCardCatalogKind.FxMega
						&& !AbilityCatalogLookups.IsKnownFxMega(value))
					{
						notes.Add(card.TypeId + "." + pair.Key + " \"" + value + "\" is not a vanilla or custom FXMega.");
					}
					else if (pair.Value == ActionCardCatalogKind.Projectile
						&& !AbilityCatalogLookups.IsKnownProjectile(value))
					{
						notes.Add(card.TypeId + ".Model \"" + value + "\" is not a known projectile prefab.");
					}
					else if (pair.Value == ActionCardCatalogKind.Unit && !IsKnownUnitOrAlias(model, value))
					{
						notes.Add(card.TypeId + ".UnitName \"" + value + "\" is not a known unit id.");
					}
					else if (pair.Value == ActionCardCatalogKind.Animation
						&& value != "NOANIMATION"
						&& !AbilityCatalogLookups.IsKnownClip(value))
					{
						notes.Add(card.TypeId + ".Animation \"" + value + "\" is not a vanilla or Character Lab clip name.");
					}
					else if (pair.Value == ActionCardCatalogKind.Sound
						&& !AbilityCatalogLookups.Contains(AbilityPickerCatalog.SoundNames, value))
					{
						notes.Add(card.TypeId + ".Sound \"" + value + "\" is not in the dumped sound catalog.");
					}
					else if (pair.Value == ActionCardCatalogKind.CallFunction
						&& !AbilityCatalogLookups.Contains(AbilityPickerCatalog.CallFunctions, value))
					{
						notes.Add(card.TypeId + ".Function \"" + value + "\" is not a shipped CallFunction type.");
					}
					else if (pair.Value == ActionCardCatalogKind.DamageType
						&& !AbilityCatalogLookups.Contains(AbilityPickerCatalog.DamageTypes, value))
					{
						notes.Add(card.TypeId + ".Type \"" + value + "\" is not a known damage type.");
					}
				}
			}

			foreach (List<ActionCard> stack in card.ChildStacks.Values)
			{
				foreach (ActionCard child in stack)
				{
					WarnCard(model, child, notes, localModifiers);
				}
			}
		}

		private static bool TryFindIllegalEvent(AbilityFileModel model, out string name)
		{
			name = null;
			if (model.Body == null)
			{
				return false;
			}

			foreach (EventNode ev in model.Body.Events)
			{
				if (AbilityEventNames.LooksLikeEventName(ev.Name) && !AbilityEventNames.IsAbilityEvent(ev.Name))
				{
					name = ev.Name;
					return true;
				}
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					if (AbilityEventNames.LooksLikeEventName(ev.Name) && !AbilityEventNames.IsModifierEvent(ev.Name))
					{
						name = ev.Name;
						return true;
					}
				}
			}

			return false;
		}

		private static bool TryFindIllegalHitTag(AbilityFileModel model, out string token)
		{
			token = null;
			string found = null;
			TryFindCard(model, card =>
			{
				if (card.TypeId != "Hit" || found != null)
				{
					return false;
				}

				string tags = HitTagsValue(card);
				foreach (string part in ParseHitTagTokens(tags))
				{
					if (!AbilityEnvelopeOptions.IsLegalHitTag(part))
					{
						found = part;
						return true;
					}
				}

				return false;
			});
			token = found;
			return found != null;
		}

		private static bool TryFindCard(AbilityFileModel model, Func<ActionCard, bool> match)
		{
			bool found = false;
			ForEachCard(model, card =>
			{
				if (!found && match(card))
				{
					found = true;
				}
			});
			return found;
		}

		private static void ForEachCard(AbilityFileModel model, Action<ActionCard> visit)
		{
			if (model == null || model.Body == null || visit == null)
			{
				return;
			}

			foreach (EventNode ev in model.Body.Events)
			{
				ForEachCard(ev.Cards, visit);
			}

			foreach (ModifierDef mod in model.Body.Modifiers)
			{
				foreach (EventNode ev in mod.Events)
				{
					ForEachCard(ev.Cards, visit);
				}
			}
		}

		private static void ForEachCard(List<ActionCard> cards, Action<ActionCard> visit)
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

				visit(card);
				foreach (List<ActionCard> stack in card.ChildStacks.Values)
				{
					ForEachCard(stack, visit);
				}
			}
		}

		private static string HitTagsValue(ActionCard card)
		{
			if (card.Fields != null && card.Fields.TryGetValue("Tags", out string tags) && !string.IsNullOrEmpty(tags))
			{
				return tags;
			}

			return string.Empty;
		}

		private static List<string> ParseHitTagTokens(string value)
		{
			List<string> tokens = new List<string>();
			if (string.IsNullOrEmpty(value))
			{
				return tokens;
			}

			string text = value.Trim();
			const string prefix = "stringList(";
			if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
			{
				text = text.Substring(prefix.Length, text.Length - prefix.Length - 1);
			}

			foreach (string part in text.Split(','))
			{
				string token = part.Trim().Trim('"');
				if (token.Length == 0)
				{
					continue;
				}

				tokens.Add(token);
			}

			return tokens;
		}

		private static bool IsEmptyFilterCallFunction(string name)
		{
			return Array.IndexOf(EmptyFilterCallFunctions, name) >= 0;
		}

		private static bool HasEmptyConsiderations(string innerKv)
		{
			if (string.IsNullOrWhiteSpace(innerKv))
			{
				return true;
			}

			int key = IndexOfBareKey(innerKv, "Considerations");
			if (key < 0)
			{
				return false;
			}

			int brace = innerKv.IndexOf('{', key);
			if (brace < 0)
			{
				return true;
			}

			int close = MatchingBrace(innerKv, brace);
			if (close < 0)
			{
				return true;
			}

			return !HasNonCommentChild(innerKv.Substring(brace + 1, close - brace - 1));
		}

		private static void WarnStatKeysFromKv(string label, string kv, List<string> notes)
		{
			if (string.IsNullOrWhiteSpace(kv))
			{
				return;
			}

			string[] lines = kv.Replace("\r\n", "\n").Split('\n');
			foreach (string line in lines)
			{
				string key = FirstKvKey(line);
				if (string.IsNullOrEmpty(key) || key.StartsWith("#", StringComparison.Ordinal) && key.Length == 1)
				{
					continue;
				}

				if (key == "PropertiesAdd" || key == "PropertiesMult" || key == "{" || key == "}")
				{
					continue;
				}

				if (!AbilityCatalogLookups.IsKnownStatRef(key))
				{
					notes.Add(label + " key \"" + key + "\" is not a known stat — Stats.ApplyModifier throws on a missing key.");
				}
			}
		}

		private static string ExtractNamedBlock(string extraKv, string blockName)
		{
			if (string.IsNullOrEmpty(extraKv) || string.IsNullOrEmpty(blockName))
			{
				return string.Empty;
			}

			int key = IndexOfBareKey(extraKv, blockName);
			if (key < 0)
			{
				return string.Empty;
			}

			int brace = extraKv.IndexOf('{', key);
			if (brace < 0)
			{
				return string.Empty;
			}

			int close = MatchingBrace(extraKv, brace);
			if (close < 0)
			{
				return string.Empty;
			}

			return extraKv.Substring(brace + 1, close - brace - 1);
		}

		private static int IndexOfBareKey(string text, string key)
		{
			int start = 0;
			while (start < text.Length)
			{
				int found = text.IndexOf(key, start, StringComparison.Ordinal);
				if (found < 0)
				{
					return -1;
				}

				bool leftOk = found == 0 || !char.IsLetterOrDigit(text[found - 1]);
				int after = found + key.Length;
				bool rightOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
				if (leftOk && rightOk)
				{
					return found;
				}

				start = found + 1;
			}

			return -1;
		}

		private static int MatchingBrace(string text, int open)
		{
			int depth = 0;
			for (int i = open; i < text.Length; i++)
			{
				if (text[i] == '{')
				{
					depth++;
				}
				else if (text[i] == '}')
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}

		private static bool HasNonCommentChild(string inner)
		{
			string[] lines = inner.Replace("\r\n", "\n").Split('\n');
			foreach (string line in lines)
			{
				string key = FirstKvKey(line);
				if (string.IsNullOrEmpty(key) || key == "{" || key == "}")
				{
					continue;
				}

				if (key[0] == '#')
				{
					continue;
				}

				return true;
			}

			return false;
		}

		private static string FirstKvKey(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return string.Empty;
			}

			string trimmed = line.Trim();
			if (trimmed.Length == 0)
			{
				return string.Empty;
			}

			if (trimmed[0] == '"')
			{
				int end = trimmed.IndexOf('"', 1);
				return end > 1 ? trimmed.Substring(1, end - 1) : string.Empty;
			}

			int i = 0;
			while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]) && trimmed[i] != '{')
			{
				i++;
			}

			return i > 0 ? trimmed.Substring(0, i) : string.Empty;
		}

		private static bool IsKnownUnitOrAlias(AbilityFileModel model, string value)
		{
			if (AbilityCatalogLookups.IsKnownUnit(value))
			{
				return true;
			}

			string folder = model != null && !string.IsNullOrEmpty(model.SourceFilePath)
				? Path.GetDirectoryName(model.SourceFilePath)
				: null;
			string resolved = LabAliases.ResolveRef(folder, AbilityCatalogLookups.StripHash(value));
			return AbilityCatalogLookups.IsKnownUnit(resolved);
		}

		internal static string FormatStatus(string prefix, string warnings)
		{
			if (string.IsNullOrEmpty(warnings))
			{
				return prefix;
			}

			StringBuilder text = new StringBuilder();
			if (!string.IsNullOrEmpty(prefix))
			{
				text.Append(prefix).Append(' ');
			}

			text.Append("Warning: ").Append(warnings);
			return text.ToString();
		}
	}
}
