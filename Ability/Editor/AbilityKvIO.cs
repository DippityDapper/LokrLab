using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KVLib;
using LokrCharacterLoader;

namespace LokrAbilityLab.Editor
{
	/// <summary>Reads/writes ability.txt as envelope fields plus a typed body tree with opaque fallback.</summary>
	internal static class AbilityKvIO
	{
		private static readonly HashSet<string> EnvelopeKeys = new HashSet<string>
		{
			"AbilityBehavior", "LocalizationId", "AbilityTeamFilter",
			"AbilityCastRange", "AbilityCastMinRange", "AbilityCooldown", "AbilityPrewarmCooldown", "AbilityAPCost",
			"AbilityCanExecute", "HitChanceModifier",
			"AbilityAOEKind", "AbilityAOETeamFilter", "AbilityAOERange", "AbilityAOEMinRange", "AbilityAOEWidth",
			"AbilityAOECenterOnCaster", "AbilityAOEAffectsCaster",
			"Icon", "AnimationID", "CastFXId",
		};

		/// <summary>Loads one top-level ability block into a file model.</summary>
		internal static bool TryLoad(string filePath, out AbilityFileModel model, out string error)
		{
			model = null;
			string text;
			try
			{
				text = File.ReadAllText(filePath);
			}
			catch (Exception ex)
			{
				error = "Could not read file: " + ex.Message;
				return false;
			}

			KeyValue[] roots;
			try
			{
				roots = KVParser.KV1.ParseAll(text);
			}
			catch (Exception ex)
			{
				error = "Could not parse KV text: " + ex.Message;
				return false;
			}

			if (roots.Length != 1)
			{
				error = "Expected exactly one top-level ability block, found " + roots.Length + " — not editable here.";
				return false;
			}

			model = LoadFromKeyValue(roots[0], filePath);
			error = null;
			return true;
		}

		/// <summary>Builds a file model from one already-parsed top-level ability block, without touching disk.</summary>
		/// <remarks>Factored out of TryLoad so a source that isn't "exactly one block in one file" can still build the same model -- see LoadAllFromText, used by VanillaAbilityCatalog for vanilla bundle TextAssets, several of which hold more than one top-level block (e.g. _basicAbilities.txt).</remarks>
		internal static AbilityFileModel LoadFromKeyValue(KeyValue root, string sourceLabel)
		{
			return new AbilityFileModel
			{
				Id = root.Key,
				SourceFilePath = sourceLabel,
				BehaviorFlags = ParseFlags(GetString(root, "AbilityBehavior", string.Empty)),
				LocalizationId = GetString(root, "LocalizationId", string.Empty),
				TeamFilter = GetString(root, "AbilityTeamFilter", "TEAM_ENEMY"),
				CastRange = GetString(root, "AbilityCastRange", string.Empty),
				CastMinRange = GetString(root, "AbilityCastMinRange", string.Empty),
				Cooldown = GetString(root, "AbilityCooldown", string.Empty),
				PrewarmCooldown = GetString(root, "AbilityPrewarmCooldown", string.Empty),
				APCost = GetString(root, "AbilityAPCost", string.Empty),
				CanExecute = GetString(root, "AbilityCanExecute", string.Empty),
				HitChanceModifier = GetString(root, "HitChanceModifier", string.Empty),
				AOEKind = GetString(root, "AbilityAOEKind", "RANGE_CIRCLE"),
				AOETeamFilter = GetString(root, "AbilityAOETeamFilter", "TEAM_ENEMY"),
				AOERange = GetString(root, "AbilityAOERange", string.Empty),
				AOEMinRange = GetString(root, "AbilityAOEMinRange", string.Empty),
				AOEWidth = GetString(root, "AbilityAOEWidth", string.Empty),
				AOECenterOnCaster = GetBool(root, "AbilityAOECenterOnCaster"),
				AOEAffectsCaster = GetBool(root, "AbilityAOEAffectsCaster"),
				Icon = GetString(root, "Icon", string.Empty),
				AnimationId = GetString(root, "AnimationID", string.Empty),
				CastFXId = GetString(root, "CastFXId", string.Empty),
				Body = ParseBody(root),
			};
		}

		/// <summary>Parses every top-level ability block in raw KV text, also returning each block's own source text keyed by id. Blocks that fail to parse are skipped and reported in errors rather than thrown, so one bad block doesn't drop the rest of a multi-block asset.</summary>
		/// <remarks>rawTextById exists so a caller can write a block back out close to verbatim (KeyValue.ToString(0)) instead of round-tripping through TryBuildText, which is lossy -- see VanillaAbilityImporter, which needs this for copying a vanilla ability into a Lab library folder.</remarks>
		internal static List<AbilityFileModel> LoadAllFromText(string text, string sourceLabel, out Dictionary<string, string> rawTextById, out List<string> errors)
		{
			errors = new List<string>();
			rawTextById = new Dictionary<string, string>();
			List<AbilityFileModel> models = new List<AbilityFileModel>();
			KeyValue[] roots;
			try
			{
				roots = KVParser.KV1.ParseAll(text);
			}
			catch (Exception ex)
			{
				errors.Add(sourceLabel + ": could not parse KV text: " + ex.Message);
				return models;
			}

			foreach (KeyValue root in roots)
			{
				try
				{
					AbilityFileModel model = LoadFromKeyValue(root, sourceLabel);
					models.Add(model);
					rawTextById[model.Id] = root.ToString(0);
				}
				catch (Exception ex)
				{
					errors.Add(sourceLabel + "/" + root.Key + ": " + ex.Message);
				}
			}

			return models;
		}

		/// <summary>Writes envelope + serialized body. Rejects literal quotes in envelope fields.</summary>
		internal static bool TrySave(AbilityFileModel model, string filePath, out string error)
		{
			if (!TryBuildText(model, out string text, out error))
			{
				return false;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(filePath));
			string folder = Path.GetDirectoryName(filePath);
			text = RewriteUnitFieldsToAliases(text, folder);
			File.WriteAllText(filePath, text);
			model.SourceFilePath = filePath;
			return true;
		}

		/// <summary>Serializes the current editor model to KV text without writing disk (Stage preview).</summary>
		internal static bool TryBuildText(AbilityFileModel model, out string text, out string error)
		{
			text = null;
			if (!AbilityValidation.TryValidate(model, out error, out _))
			{
				return false;
			}

			if (TryFindQuote(model, out string offendingField))
			{
				error = "\"" + offendingField + "\" contains a literal \" character, which would corrupt the ability file. Remove it and try again.";
				return false;
			}

			StringBuilder content = new StringBuilder();
			content.Append('"').Append(model.Id).Append("\"\n{\n");
			AppendField(content, "AbilityBehavior", string.Join(" | ", model.BehaviorFlags));
			if (!string.IsNullOrEmpty(model.LocalizationId))
			{
				AppendField(content, "LocalizationId", model.LocalizationId);
			}

			bool passive = model.IsPassive;
			if (!passive)
			{
				if (!model.BehaviorFlags.Contains("POINT_TARGET"))
				{
					AppendField(content, "AbilityTeamFilter", model.TeamFilter);
				}

				if (!model.BehaviorFlags.Contains("SELF_TARGET"))
				{
					AppendField(content, "AbilityCastRange", model.CastRange);
					if (!string.IsNullOrEmpty(model.CastMinRange))
					{
						AppendField(content, "AbilityCastMinRange", model.CastMinRange);
					}
				}

				AppendField(content, "AbilityCooldown", model.Cooldown);
				if (!string.IsNullOrEmpty(model.PrewarmCooldown))
				{
					AppendField(content, "AbilityPrewarmCooldown", model.PrewarmCooldown);
				}

				AppendField(content, "AbilityAPCost", model.APCost);
				if (!string.IsNullOrEmpty(model.CanExecute))
				{
					AppendField(content, "AbilityCanExecute", model.CanExecute);
				}

				if (model.BehaviorFlags.Contains("AOE"))
				{
					AppendField(content, "AbilityAOEKind", model.AOEKind);
					AppendField(content, "AbilityAOETeamFilter", model.AOETeamFilter);
					AppendField(content, "AbilityAOERange", model.AOERange);
					if (!string.IsNullOrEmpty(model.AOEMinRange))
					{
						AppendField(content, "AbilityAOEMinRange", model.AOEMinRange);
					}

					if (!string.IsNullOrEmpty(model.AOEWidth))
					{
						AppendField(content, "AbilityAOEWidth", model.AOEWidth);
					}

					AppendField(content, "AbilityAOECenterOnCaster", model.AOECenterOnCaster ? "1" : "0");
					AppendField(content, "AbilityAOEAffectsCaster", model.AOEAffectsCaster ? "1" : "0");
				}

				if (!string.IsNullOrEmpty(model.AnimationId))
				{
					AppendField(content, "AnimationID", model.AnimationId);
				}

				if (!string.IsNullOrEmpty(model.CastFXId))
				{
					AppendField(content, "CastFXId", model.CastFXId);
				}
			}

			if (!string.IsNullOrEmpty(model.HitChanceModifier))
			{
				AppendField(content, "HitChanceModifier", model.HitChanceModifier);
			}

			if (!string.IsNullOrEmpty(model.Icon))
			{
				AppendField(content, "Icon", model.Icon);
			}

			content.Append('\n');
			WriteBody(content, model.Body);
			content.Append("}\n");
			text = content.ToString();
			error = null;
			return true;
		}

		private static AbilityBody ParseBody(KeyValue root)
		{
			AbilityBody body = new AbilityBody();
			foreach (KeyValue child in root.Children)
			{
				if (EnvelopeKeys.Contains(child.Key))
				{
					continue;
				}

				if (child.Key == "AbilitySpecial")
				{
					ParseSpecial(child, body);
					continue;
				}

				if (child.Key == "Modifiers")
				{
					ParseModifiers(child, body);
					continue;
				}

				if (child.Key == "AIConfigB" || child.Key.StartsWith("AIBrain", StringComparison.Ordinal))
				{
					body.Ai.Add(new AiBlock { Name = child.Key, InnerKv = InnerChildrenText(child, 2) });
					continue;
				}

				if (AbilityEventNames.IsAbilityEvent(child.Key))
				{
					EventNode ev = body.Event(child.Key);
					foreach (KeyValue actionKv in child.Children)
					{
						ev.Cards.Add(ParseAction(actionKv));
					}

					continue;
				}

				body.OpaqueTopLevel.Add(child.ToString(1).TrimEnd() + "\n");
			}

			return body;
		}

		private static void ParseSpecial(KeyValue special, AbilityBody body)
		{
			foreach (KeyValue slot in special.Children)
			{
				SpecialVar row = new SpecialVar { Slot = slot.Key };
				foreach (KeyValue field in slot.Children)
				{
					if (field.Key == "var_type")
					{
						row.VarType = field.GetString() ?? row.VarType;
					}
					else if (!field.HasChildren)
					{
						row.Name = field.Key;
						row.Value = field.GetString() ?? string.Empty;
					}
				}

				body.Special.Add(row);
			}
		}

		private static void ParseModifiers(KeyValue modifiers, AbilityBody body)
		{
			foreach (KeyValue modKv in modifiers.Children)
			{
				ModifierDef mod = new ModifierDef { Id = modKv.Key };
				StringBuilder extra = new StringBuilder();
				foreach (KeyValue child in modKv.Children)
				{
					if (!child.HasChildren && child.Key == "Passive")
					{
						mod.Passive = child.GetString() == "1" || child.GetBool();
					}
					else if (!child.HasChildren && child.Key == "ModifierFXName")
					{
						mod.ModifierFXName = child.GetString() ?? string.Empty;
					}
					else if (!child.HasChildren && child.Key == "IncompatibleStates")
					{
						mod.IncompatibleStates = child.GetString() ?? string.Empty;
					}
					else if (!child.HasChildren && child.Key == "AutoRemoveTags")
					{
						mod.AutoRemoveTags = child.GetString() ?? string.Empty;
					}
					else if (!child.HasChildren && child.Key == "AutoRemoveModifierIds")
					{
						mod.AutoRemoveModifierIds = child.GetString() ?? string.Empty;
					}
					else if (child.Key == "PropertiesAdd")
					{
						mod.PropertiesAddKv = InnerChildrenText(child, 3);
					}
					else if (AbilityEventNames.IsModifierEvent(child.Key) || AbilityEventNames.LooksLikeEventName(child.Key))
					{
						EventNode ev = new EventNode { Name = child.Key };
						foreach (KeyValue actionKv in child.Children)
						{
							ev.Cards.Add(ParseAction(actionKv));
						}

						mod.Events.Add(ev);
					}
					else
					{
						extra.Append(child.ToString(3).TrimEnd()).Append('\n');
					}
				}

				mod.ExtraKv = extra.ToString();
				body.Modifiers.Add(mod);
			}
		}

		/// <summary>Parses one KV action block into a typed card, or an opaque subtree when the type is unknown.</summary>
		internal static ActionCard ParseAction(KeyValue kv)
		{
			ActionCardDescriptor descriptor = AbilityCardRegistry.Find(kv.Key);
			if (descriptor == null || !kv.HasChildren)
			{
				return new ActionCard
				{
					TypeId = kv.Key,
					IsOpaque = true,
					OpaqueText = kv.ToString(1).TrimEnd() + "\n",
				};
			}

			ActionCard card = new ActionCard { TypeId = kv.Key };
			HashSet<string> stacks = new HashSet<string>(descriptor.ChildStackNames);
			HashSet<string> fields = new HashSet<string>(descriptor.FieldKeys);
			StringBuilder extra = new StringBuilder();
			foreach (KeyValue child in kv.Children)
			{
				if (stacks.Contains(child.Key) && child.HasChildren)
				{
					List<ActionCard> list = card.Stack(child.Key);
					foreach (KeyValue nested in child.Children)
					{
						list.Add(ParseAction(nested));
					}
				}
				else if (fields.Contains(child.Key) && !child.HasChildren)
				{
					card.Fields[child.Key] = child.GetString() ?? string.Empty;
				}
				else
				{
					extra.Append(child.ToString(2).TrimEnd()).Append('\n');
				}
			}

			card.ExtraKv = extra.ToString();
			RewriteRangedHitTags(card);
			return card;
		}

		/// <summary>Rewrites leftover New-ranged <c>#RANGED</c> to <c>#PROJECTILE</c> so ValidateTags can parse the file.</summary>
		/// <remarks>Do not open the vanilla whitelist. Vanilla ranged hits already test PROJECTILE, not RANGED.</remarks>
		private static void RewriteRangedHitTags(ActionCard card)
		{
			if (card == null || card.TypeId != "Hit" || card.Fields == null)
			{
				return;
			}

			if (!card.Fields.TryGetValue("Tags", out string tags) || string.IsNullOrEmpty(tags)
				|| tags.IndexOf("#RANGED", StringComparison.Ordinal) < 0)
			{
				return;
			}

			card.Fields["Tags"] = tags.Replace("#RANGED", "#PROJECTILE");
		}

		private static void WriteBody(StringBuilder content, AbilityBody body)
		{
			if (body == null)
			{
				return;
			}

			if (body.Special.Count > 0)
			{
				content.Append("\t\"AbilitySpecial\"\n\t{\n");
				foreach (SpecialVar row in body.Special)
				{
					if (string.IsNullOrEmpty(row.Slot) || string.IsNullOrEmpty(row.Name))
					{
						continue;
					}

					content.Append("\t\t\"").Append(row.Slot).Append("\"\n\t\t{\n");
					AppendField(content, "var_type", row.VarType, indent: 3);
					AppendField(content, row.Name, row.Value, indent: 3);
					content.Append("\t\t}\n");
				}

				content.Append("\t}\n");
			}

			foreach (EventNode ev in body.Events)
			{
				if (ev.Cards.Count == 0)
				{
					continue;
				}

				content.Append('\t').Append('"').Append(ev.Name).Append("\"\n\t{\n");
				foreach (ActionCard card in ev.Cards)
				{
					WriteAction(content, card, 2);
				}

				content.Append("\t}\n");
			}

			if (body.Modifiers.Count > 0)
			{
				content.Append("\t\"Modifiers\"\n\t{\n");
				foreach (ModifierDef mod in body.Modifiers)
				{
					if (string.IsNullOrEmpty(mod.Id))
					{
						continue;
					}

					content.Append("\t\t\"").Append(mod.Id).Append("\"\n\t\t{\n");
					if (mod.Passive)
					{
						AppendField(content, "Passive", "1", indent: 3);
					}

					if (!string.IsNullOrEmpty(mod.ModifierFXName))
					{
						AppendField(content, "ModifierFXName", mod.ModifierFXName, indent: 3);
					}

					if (!string.IsNullOrEmpty(mod.IncompatibleStates))
					{
						AppendField(content, "IncompatibleStates", mod.IncompatibleStates, indent: 3);
					}

					if (!string.IsNullOrEmpty(mod.AutoRemoveTags))
					{
						AppendField(content, "AutoRemoveTags", mod.AutoRemoveTags, indent: 3);
					}

					if (!string.IsNullOrEmpty(mod.AutoRemoveModifierIds))
					{
						AppendField(content, "AutoRemoveModifierIds", mod.AutoRemoveModifierIds, indent: 3);
					}

					if (!string.IsNullOrEmpty(mod.PropertiesAddKv))
					{
						content.Append("\t\t\t\"PropertiesAdd\"\n\t\t\t{\n");
						content.Append(Reindent(mod.PropertiesAddKv, 4));
						content.Append("\t\t\t}\n");
					}

					foreach (EventNode ev in mod.Events)
					{
						if (ev.Cards.Count == 0)
						{
							continue;
						}

						content.Append("\t\t\t\"").Append(ev.Name).Append("\"\n\t\t\t{\n");
						foreach (ActionCard card in ev.Cards)
						{
							WriteAction(content, card, 4);
						}

						content.Append("\t\t\t}\n");
					}

					if (!string.IsNullOrEmpty(mod.ExtraKv))
					{
						content.Append(mod.ExtraKv);
					}

					content.Append("\t\t}\n");
				}

				content.Append("\t}\n");
			}

			foreach (AiBlock ai in body.Ai)
			{
				if (string.IsNullOrEmpty(ai.Name))
				{
					continue;
				}

				content.Append('\t').Append('"').Append(ai.Name).Append("\"\n\t{\n");
				content.Append(ai.InnerKv);
				if (!string.IsNullOrEmpty(ai.InnerKv) && !ai.InnerKv.EndsWith("\n"))
				{
					content.Append('\n');
				}

				content.Append("\t}\n");
			}

			foreach (string opaque in body.OpaqueTopLevel)
			{
				content.Append(opaque);
				if (!opaque.EndsWith("\n"))
				{
					content.Append('\n');
				}
			}
		}

		internal static void WriteAction(StringBuilder content, ActionCard card, int indent)
		{
			if (card.IsOpaque)
			{
				content.Append(Reindent(card.OpaqueText, indent));
				return;
			}

			string pad = new string('\t', indent);
			content.Append(pad).Append('"').Append(card.TypeId).Append("\"\n").Append(pad).Append("{\n");
			ActionCardDescriptor descriptor = AbilityCardRegistry.Find(card.TypeId);
			if (descriptor != null)
			{
				foreach (string key in descriptor.FieldKeys)
				{
					if (card.Fields.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
					{
						if (card.TypeId == "Lua" && key == "Action")
						{
							value = AbilityLuaRules.FlattenForKv(value);
						}

						AppendField(content, key, value, indent + 1);
					}
				}

				foreach (string stackName in descriptor.ChildStackNames)
				{
					if (!card.ChildStacks.TryGetValue(stackName, out List<ActionCard> list) || list.Count == 0)
					{
						continue;
					}

					string innerPad = new string('\t', indent + 1);
					content.Append(innerPad).Append('"').Append(stackName).Append("\"\n").Append(innerPad).Append("{\n");
					foreach (ActionCard child in list)
					{
						WriteAction(content, child, indent + 2);
					}

					content.Append(innerPad).Append("}\n");
				}
			}
			else
			{
				foreach (KeyValuePair<string, string> pair in card.Fields)
				{
					if (!string.IsNullOrEmpty(pair.Value))
					{
						AppendField(content, pair.Key, pair.Value, indent + 1);
					}
				}
			}

			if (!string.IsNullOrEmpty(card.ExtraKv))
			{
				content.Append(Reindent(card.ExtraKv, indent + 1));
			}

			content.Append(pad).Append("}\n");
		}

		private static string InnerChildrenText(KeyValue parent, int childIndent)
		{
			StringBuilder text = new StringBuilder();
			foreach (KeyValue child in parent.Children)
			{
				text.Append(child.ToString(childIndent).TrimEnd()).Append('\n');
			}

			return text.ToString();
		}

		private static string Reindent(string block, int indent)
		{
			if (string.IsNullOrEmpty(block))
			{
				return string.Empty;
			}

			string[] lines = block.Replace("\r\n", "\n").TrimEnd().Split('\n');
			int minTabs = int.MaxValue;
			foreach (string line in lines)
			{
				if (line.Trim().Length == 0)
				{
					continue;
				}

				int tabs = 0;
				while (tabs < line.Length && line[tabs] == '\t')
				{
					tabs++;
				}

				if (tabs < minTabs)
				{
					minTabs = tabs;
				}
			}

			if (minTabs == int.MaxValue)
			{
				minTabs = 0;
			}

			StringBuilder result = new StringBuilder();
			string pad = new string('\t', indent);
			foreach (string line in lines)
			{
				string stripped = minTabs <= line.Length ? line.Substring(minTabs) : line.TrimStart('\t');
				result.Append(pad).Append(stripped).Append('\n');
			}

			return result.ToString();
		}

		/// <summary>Rewrites unique-id UnitName values to <c>$alias</c> when this folder has a mapping.</summary>
		private static string RewriteUnitFieldsToAliases(string text, string folder)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(folder))
			{
				return text;
			}

			Dictionary<string, string> map = LabAliases.Load(folder);
			if (map.Count == 0)
			{
				return text;
			}

			return System.Text.RegularExpressions.Regex.Replace(text,
				"(\"UnitName\"\\s+\")#?([^\"]+)(\")",
				match =>
				{
					string uniqueId = match.Groups[2].Value;
					if (uniqueId.Length > 0 && uniqueId[0] == '$')
					{
						return match.Value;
					}

					string key = LabAliases.FindKeyForId(map, uniqueId);
					return key != null
						? match.Groups[1].Value + "$" + key + match.Groups[3].Value
						: match.Value;
				});
		}

		private static void AppendField(StringBuilder content, string key, string value, int indent = 1)
		{
			content.Append(new string('\t', indent)).Append('"').Append(key).Append("\"\t\"").Append(value ?? string.Empty).Append("\"\n");
		}

		private static List<string> ParseFlags(string raw)
		{
			List<string> flags = new List<string>();
			if (string.IsNullOrEmpty(raw))
			{
				return flags;
			}

			foreach (string part in raw.Split('|'))
			{
				string trimmed = part.Trim();
				if (trimmed.Length > 0)
				{
					flags.Add(trimmed);
				}
			}

			return flags;
		}

		private static string GetString(KeyValue root, string key, string fallback)
		{
			KeyValue node = root[key];
			return node != null ? (node.GetString() ?? fallback) : fallback;
		}

		private static bool GetBool(KeyValue root, string key)
		{
			KeyValue node = root[key];
			return node != null && node.GetBool();
		}

		private static bool TryFindQuote(AbilityFileModel model, out string fieldLabel)
		{
			(string label, string value)[] fields =
			{
				("Id", model.Id),
				("Localization Id", model.LocalizationId),
				("Team Filter", model.TeamFilter),
				("Cast Range", model.CastRange),
				("Cast Min Range", model.CastMinRange),
				("Cooldown", model.Cooldown),
				("Prewarm Cooldown", model.PrewarmCooldown),
				("AP Cost", model.APCost),
				("Can Execute", model.CanExecute),
				("Hit Chance Modifier", model.HitChanceModifier),
				("AOE Kind", model.AOEKind),
				("AOE Team Filter", model.AOETeamFilter),
				("AOE Range", model.AOERange),
				("AOE Min Range", model.AOEMinRange),
				("AOE Width", model.AOEWidth),
				("Icon", model.Icon),
				("Animation Id", model.AnimationId),
				("Cast FX Id", model.CastFXId),
			};
			foreach ((string label, string value) in fields)
			{
				if (!string.IsNullOrEmpty(value) && value.Contains("\""))
				{
					fieldLabel = label;
					return true;
				}
			}

			fieldLabel = null;
			return false;
		}
	}
}
