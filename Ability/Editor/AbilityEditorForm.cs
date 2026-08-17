using System;
using System.Collections.Generic;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Shared ability form: overlay (all tabs), shell inspector (envelope), Library viewport (body tabs).</summary>
	internal static class AbilityEditorForm
	{
		private static AbilityFileModel current;
		private static bool suppressFieldEvents;
		private static int activeTab;

		private static readonly Dictionary<string, UiToggle> behaviorToggles = new Dictionary<string, UiToggle>();
		private static UiTextField localizationIdField;
		private static UiDropdown teamFilterField;
		private static AbilityExpressionField castRangeField;
		private static AbilityExpressionField castMinRangeField;
		private static AbilityExpressionField cooldownField;
		private static AbilityExpressionField prewarmCooldownField;
		private static AbilityExpressionField apCostField;
		private static AbilityExpressionField canExecuteField;
		private static AbilityExpressionField hitChanceField;
		private static UiDropdown aoeKindField;
		private static UiDropdown aoeTeamFilterField;
		private static AbilityExpressionField aoeRangeField;
		private static AbilityExpressionField aoeMinRangeField;
		private static AbilityExpressionField aoeWidthField;
		private static UiToggle aoeCenterOnCasterField;
		private static UiToggle aoeAffectsCasterField;
		private static UiComboBox iconField;
		private static UiComboBox animationIdField;
		private static UiComboBox castFXIdField;

		private static UiElement teamFilterRow;
		private static UiElement castRangeRow;
		private static UiElement castMinRangeRow;
		private static UiElement cooldownRow;
		private static UiElement prewarmCooldownRow;
		private static UiElement apCostRow;
		private static UiElement canExecuteRow;
		private static UiElement hitChanceRow;
		private static UiElement animationRow;
		private static UiElement castFXRow;
		private static UiElement aoeSection;

		private static readonly string[] OverlayTabNames = { "Envelope", "Events", "Modifiers", "Special", "AI", "Advanced" };
		private static readonly string[] BodyTabNames = { "Events", "Modifiers", "Special", "AI", "Advanced" };
		private static readonly List<UiButton> tabButtons = new List<UiButton>();
		private static readonly List<UiStack> tabPages = new List<UiStack>();

		private static UiStack eventsHost;
		private static UiStack modifiersHost;
		private static UiStack specialHost;
		private static UiStack aiHost;
		private static UiStack advancedHost;
		private static UiStack customAssetsHost;
		private static UiContextMenu addMenu;

		/// <summary>Overlay path: envelope plus body tabs in one form.</summary>
		internal static void Build(UiStack section)
		{
			behaviorToggles.Clear();
			tabButtons.Clear();
			tabPages.Clear();
			activeTab = 0;

			AddTabStrip(section, OverlayTabNames);
			tabPages.Add(BuildEnvelopePage(section));
			AddBodyPages(section);
			foreach (UiStack page in tabPages)
			{
				section.Add(page);
			}

			addMenu = UiContextMenu.Create(section.ContentTransform);
			ShowTab(0);
		}

		/// <summary>Shell inspector: envelope only. Body tabs live in the Library viewport.</summary>
		internal static void BuildEnvelopeOnly(UiStack section)
		{
			behaviorToggles.Clear();
			section.Add(BuildEnvelopePage(section));
		}

		/// <summary>Library viewport: Events / Modifiers / Special / AI / Advanced.</summary>
		internal static void BuildBody(UiStack section)
		{
			tabButtons.Clear();
			tabPages.Clear();
			activeTab = 0;
			AddTabStrip(section, BodyTabNames);
			AddBodyPages(section);
			foreach (UiStack page in tabPages)
			{
				section.Add(page);
			}

			addMenu = UiContextMenu.Create(section.ContentTransform);
			ShowTab(0);
		}

		/// <summary>Pushes a loaded model into the form and rebuilds card stacks.</summary>
		internal static void Bind(AbilityFileModel model)
		{
			current = model;
			RefreshEnvelope();
			if (BodyHostsAlive())
			{
				RebuildBody();
			}
		}

		internal static AbilityFileModel Current => current;

		internal static void RefreshVisibility()
		{
			if (current == null || teamFilterRow == null)
			{
				return;
			}

			bool passive = current.BehaviorFlags.Contains("PASSIVE");
			bool selfTarget = current.BehaviorFlags.Contains("SELF_TARGET");
			bool pointTarget = current.BehaviorFlags.Contains("POINT_TARGET");
			bool aoe = current.BehaviorFlags.Contains("AOE");
			bool chance = current.BehaviorFlags.Contains("HAS_CHANCE_TO_HIT");

			teamFilterRow.GameObject.SetActive(!passive && !pointTarget);
			castRangeRow.GameObject.SetActive(!passive && !selfTarget);
			castMinRangeRow.GameObject.SetActive(!passive && !selfTarget);
			cooldownRow.GameObject.SetActive(!passive);
			prewarmCooldownRow.GameObject.SetActive(!passive);
			apCostRow.GameObject.SetActive(!passive);
			canExecuteRow.GameObject.SetActive(!passive);
			hitChanceRow.GameObject.SetActive(!passive && chance);
			animationRow.GameObject.SetActive(!passive);
			bool customCastFx = AbilityCustomAssets.Owns(current, "fx", current.CastFXId);
			castFXRow.GameObject.SetActive(!passive && !customCastFx);
			if (customAssetsHost != null)
			{
				customAssetsHost.GameObject.SetActive(!passive);
			}
			aoeSection.GameObject.SetActive(!passive && aoe);
		}

		internal static void ReleaseBodyHosts()
		{
			eventsHost = null;
			modifiersHost = null;
			specialHost = null;
			aiHost = null;
			advancedHost = null;
			addMenu = null;
		}

		private static bool BodyHostsAlive()
		{
			return eventsHost != null && eventsHost.GameObject != null;
		}

		internal static void RebuildBody()
		{
			if (current == null || !BodyHostsAlive())
			{
				return;
			}

			AbilityEditorCards.RebuildEvents(eventsHost, current, addMenu, DirtyRebuild);
			AbilityEditorCards.RebuildModifiers(modifiersHost, current, addMenu, DirtyRebuild);
			RebuildSpecial();
			RebuildAi();
			RebuildAdvanced();
		}

		private static void AddTabStrip(UiStack section, string[] names)
		{
			UiStack tabRow = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			section.Add(tabRow.FixedHeight(28f));
			for (int i = 0; i < names.Length; i++)
			{
				int index = i;
				UiButton button = UiButton.Create(tabRow.ContentTransform, names[i], () => ShowTab(index), primary: i == 0);
				tabRow.Add(button.Grow());
				tabButtons.Add(button);
			}
		}

		private static void AddBodyPages(UiStack section)
		{
			tabPages.Add(BuildHostPage(section, "Events — hats are On* lists. Add action opens the card registry.", out eventsHost));
			tabPages.Add(BuildHostPage(section, "Modifiers — nested definitions; Passive traits use the Passive toggle.", out modifiersHost));
			tabPages.Add(BuildHostPage(section, "AbilitySpecial — var_type + name + value. Empty values are omitted on save.", out specialHost));
			tabPages.Add(BuildHostPage(section, "AI — named AIConfigB / AIBrain* blocks. Empty uses generic scoring.", out aiHost));
			tabPages.Add(BuildHostPage(section, "Advanced — unrecognized top-level KV, preserved as written.", out advancedHost));
		}

		private static void ShowTab(int index)
		{
			activeTab = index;
			for (int i = 0; i < tabPages.Count; i++)
			{
				tabPages[i].Visible(i == index);
				if (i < tabButtons.Count)
				{
					tabButtons[i].Image.color = i == index ? UiTheme.Default.ButtonColor : UiTheme.Default.RowButtonColor;
				}
			}
		}

		private static UiStack BuildHostPage(UiStack section, string hint, out UiStack host)
		{
			UiStack page = UiStack.Vertical(section.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
			page.Add(UiLabel.Create(page.ContentTransform, hint, UiTheme.Default, 11).FixedHeight(36f));
			host = UiStack.Vertical(page.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
			page.Add(host);
			return page;
		}

		private static UiStack BuildEnvelopePage(UiStack section)
		{
			UiStack page = UiStack.Vertical(section.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
			page.Add(UiLabel.Create(page.ContentTransform, "Behavior:").FixedHeight(18f));
			UiStack behaviorStack = UiStack.Vertical(page.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f);
			page.Add(behaviorStack);
			foreach (string flag in AbilityEnvelopeOptions.BehaviorFlags)
			{
				UiToggle toggle = UiToggle.Create(behaviorStack.ContentTransform, flag, false);
				toggle.OnValueChanged(_ => OnBehaviorFlagChanged());
				behaviorStack.Add(toggle.FixedHeight(22f));
				behaviorToggles[flag] = toggle;
				LabHoverInfo.Bind(toggle.GameObject, "envelope.behavior." + flag);
			}

			localizationIdField = UiTextField.Create(page.ContentTransform);
			localizationIdField.OnEndEdit(value => Edit(() => current.LocalizationId = value));
			page.Add(LabeledRow(page.ContentTransform, "Localization Id (blank = same as ability id):", localizationIdField, hoverKey: "envelope.LocalizationId"));

			teamFilterField = UiDropdown.Create(page.ContentTransform, AbilityEnvelopeOptions.TeamFilters);
			teamFilterField.OnValueChanged(index => Edit(() => current.TeamFilter = AbilityEnvelopeOptions.TeamFilters[index]));
			teamFilterRow = LabeledRow(page.ContentTransform, "Team Filter:", teamFilterField, hoverKey: "envelope.TeamFilter");
			page.Add(teamFilterRow);

			castRangeRow = LabeledExpression(page.ContentTransform, "Cast Range (number or expression):", out castRangeField, value => Edit(() => current.CastRange = value), ExpressionContext.Range, hoverKey: "envelope.CastRange", typeId: "envelope", fieldKey: "CastRange");
			page.Add(castRangeRow);

			castMinRangeRow = LabeledExpression(page.ContentTransform, "Cast Min Range:", out castMinRangeField, value => Edit(() => current.CastMinRange = value), ExpressionContext.Range, hoverKey: "envelope.CastMinRange", typeId: "envelope", fieldKey: "CastMinRange");
			page.Add(castMinRangeRow);

			cooldownRow = LabeledExpression(page.ContentTransform, "Cooldown:", out cooldownField, value => Edit(() => current.Cooldown = value), ExpressionContext.Number, hoverKey: "envelope.Cooldown", typeId: "envelope", fieldKey: "Cooldown");
			page.Add(cooldownRow);

			prewarmCooldownRow = LabeledExpression(page.ContentTransform, "Prewarm Cooldown (optional):", out prewarmCooldownField, value => Edit(() => current.PrewarmCooldown = value), ExpressionContext.Number, hoverKey: "envelope.PrewarmCooldown", typeId: "envelope", fieldKey: "PrewarmCooldown");
			page.Add(prewarmCooldownRow);

			apCostRow = LabeledExpression(page.ContentTransform, "AP Cost:", out apCostField, value => Edit(() => current.APCost = value), ExpressionContext.Number, hoverKey: "envelope.APCost", typeId: "envelope", fieldKey: "APCost");
			page.Add(apCostRow);

			canExecuteRow = LabeledExpression(page.ContentTransform, "Can Execute (optional expression):", out canExecuteField, value => Edit(() => current.CanExecute = value), ExpressionContext.Condition, hoverKey: "envelope.CanExecute", typeId: "envelope", fieldKey: "CanExecute");
			page.Add(canExecuteRow);

			hitChanceRow = LabeledExpression(page.ContentTransform, "Hit Chance Modifier (optional):", out hitChanceField, value => Edit(() => current.HitChanceModifier = value), ExpressionContext.Number, hoverKey: "envelope.HitChanceModifier", typeId: "envelope", fieldKey: "HitChanceModifier");
			page.Add(hitChanceRow);

			aoeSection = BuildAOESection(page);
			page.Add(aoeSection);

			iconField = UiComboBox.Create(page.ContentTransform, AbilityPickerCatalog.IconStems);
			iconField.OnEndEdit(value => Edit(() => current.Icon = value));
			page.Add(LabeledRow(page.ContentTransform, "Icon (optional; required for hero-room traits):", iconField, hoverKey: "envelope.Icon"));

			animationIdField = UiComboBox.Create(page.ContentTransform, AbilityCatalogLookups.AnimationOptions());
			animationIdField.OnEndEdit(value => Edit(() => current.AnimationId = value));
			animationRow = LabeledRow(page.ContentTransform, "Animation Id:", animationIdField, hoverKey: "envelope.AnimationId");
			page.Add(animationRow);
			page.Add(UiLabel.Create(page.ContentTransform,
				"Clip name on the caster rig. Custom names play if that character's rig.json has the clip with AbilityAction / AbilityEnd (Character Lab Save backfills those on Attack / SpecialAttack / SpellCast*).",
				UiTheme.Default, 11).FixedHeight(36f));

			castFXIdField = UiComboBox.Create(page.ContentTransform, AbilityCatalogLookups.FxMegaOptions());
			castFXIdField.OnEndEdit(value =>
			{
				if (suppressFieldEvents || current == null)
				{
					return;
				}

				string previous = current.CastFXId;
				AbilityEditorSprites.OnCastFxIdEdited(current, previous, value, castFXIdField, RefreshCatalogCombos, RebuildCastFxPanel);
				LabSaveUx.MarkDirty();
			});
			castFXRow = LabeledRow(page.ContentTransform, "Cast FX:", castFXIdField, hoverKey: "envelope.CastFXId");
			page.Add(castFXRow);
			customAssetsHost = UiStack.Vertical(page.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			page.Add(customAssetsHost);
			return page;
		}

		private static UiElement BuildAOESection(UiStack parentSection)
		{
			UiStack section = UiStack.Vertical(parentSection.ContentTransform, UiTheme.Default, spacing: 4f, padding: 4f);
			section.Add(UiLabel.Create(section.ContentTransform, "AOE (only used while Behavior includes AOE):").FixedHeight(18f));

			aoeKindField = UiDropdown.Create(section.ContentTransform, AbilityEnvelopeOptions.SelectableAOEKinds);
			aoeKindField.OnValueChanged(index => Edit(() =>
			{
				string[] options = AbilityEnvelopeOptions.DropdownAOEKinds(current.AOEKind);
				if (index >= 0 && index < options.Length)
				{
					current.AOEKind = options[index];
				}
			}));
			section.Add(LabeledRow(section.ContentTransform, "Shape:", aoeKindField, hoverKey: "envelope.AOEKind"));

			aoeTeamFilterField = UiDropdown.Create(section.ContentTransform, AbilityEnvelopeOptions.TeamFilters);
			aoeTeamFilterField.OnValueChanged(index => Edit(() => current.AOETeamFilter = AbilityEnvelopeOptions.TeamFilters[index]));
			section.Add(LabeledRow(section.ContentTransform, "AOE Team Filter:", aoeTeamFilterField, hoverKey: "envelope.AOETeamFilter"));

			section.Add(LabeledExpression(section.ContentTransform, "AOE Range:", out aoeRangeField, value => Edit(() => current.AOERange = value), ExpressionContext.Range, hoverKey: "envelope.AOERange", typeId: "envelope", fieldKey: "AOERange"));
			section.Add(LabeledExpression(section.ContentTransform, "AOE Min Range (optional):", out aoeMinRangeField, value => Edit(() => current.AOEMinRange = value), ExpressionContext.Range, hoverKey: "envelope.AOEMinRange", typeId: "envelope", fieldKey: "AOEMinRange"));
			section.Add(LabeledExpression(section.ContentTransform, "AOE Width (tunnel/cone, optional):", out aoeWidthField, value => Edit(() => current.AOEWidth = value), ExpressionContext.Range, hoverKey: "envelope.AOEWidth", typeId: "envelope", fieldKey: "AOEWidth"));

			aoeCenterOnCasterField = UiToggle.Create(section.ContentTransform, "Center On Caster", false);
			aoeCenterOnCasterField.OnValueChanged(value => Edit(() => current.AOECenterOnCaster = value));
			section.Add(aoeCenterOnCasterField.FixedHeight(22f));
			LabHoverInfo.Bind(aoeCenterOnCasterField.GameObject, "envelope.AOECenterOnCaster");

			aoeAffectsCasterField = UiToggle.Create(section.ContentTransform, "Affects Caster", false);
			aoeAffectsCasterField.OnValueChanged(value => Edit(() => current.AOEAffectsCaster = value));
			section.Add(aoeAffectsCasterField.FixedHeight(22f));
			LabHoverInfo.Bind(aoeAffectsCasterField.GameObject, "envelope.AOEAffectsCaster");
			return section;
		}

		private static void RefreshEnvelope()
		{
			if (current == null || localizationIdField == null)
			{
				return;
			}

			suppressFieldEvents = true;
			foreach (KeyValuePair<string, UiToggle> entry in behaviorToggles)
			{
				entry.Value.SetValueSilently(current.BehaviorFlags.Contains(entry.Key));
			}

			localizationIdField.SetText(current.LocalizationId);
			teamFilterField.SetValueSilently(IndexOfOr0(AbilityEnvelopeOptions.TeamFilters, current.TeamFilter));
			castRangeField.SetText(current.CastRange);
			castMinRangeField.SetText(current.CastMinRange);
			cooldownField.SetText(current.Cooldown);
			prewarmCooldownField.SetText(current.PrewarmCooldown);
			apCostField.SetText(current.APCost);
			canExecuteField.SetText(current.CanExecute);
			hitChanceField.SetText(current.HitChanceModifier);
			string[] aoeKinds = AbilityEnvelopeOptions.DropdownAOEKinds(current.AOEKind);
			aoeKindField.SetOptions(aoeKinds);
			aoeKindField.SetValueSilently(IndexOfOr0(aoeKinds, current.AOEKind));
			aoeTeamFilterField.SetValueSilently(IndexOfOr0(AbilityEnvelopeOptions.TeamFilters, current.AOETeamFilter));
			aoeRangeField.SetText(current.AOERange);
			aoeMinRangeField.SetText(current.AOEMinRange);
			aoeWidthField.SetText(current.AOEWidth);
			aoeCenterOnCasterField.SetValueSilently(current.AOECenterOnCaster);
			aoeAffectsCasterField.SetValueSilently(current.AOEAffectsCaster);
			iconField.SetText(current.Icon);
			animationIdField.SetOptions(AbilityCatalogLookups.AnimationOptions());
			animationIdField.SetText(current.AnimationId);
			castFXIdField.SetOptions(AbilityCatalogLookups.FxMegaOptions());
			castFXIdField.SetText(current.CastFXId);
			suppressFieldEvents = false;
			RefreshVisibility();
			RebuildCastFxPanel();
		}

		private static void RebuildSpecial()
		{
			if (specialHost == null)
			{
				return;
			}

			specialHost.Clear();
			if (current.Body == null)
			{
				return;
			}

			for (int i = 0; i < current.Body.Special.Count; i++)
			{
				int index = i;
				SpecialVar row = current.Body.Special[i];
				UiStack block = UiStack.Vertical(specialHost.ContentTransform, UiTheme.Default, spacing: 4f, padding: 4f);
				specialHost.Add(block);
				UiStack header = UiStack.Horizontal(block.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
				block.Add(header.FixedHeight(28f));
				header.Add(UiLabel.Create(header.ContentTransform, "Variable").Grow());
				header.Add(UiButton.Create(header.ContentTransform, "x", () =>
				{
					current.Body.Special.RemoveAt(index);
					LabSaveUx.MarkDirty();
					RebuildSpecial();
				}, primary: false).FixedWidth(28f));

				UiTextField slot = UiTextField.Create(block.ContentTransform, row.Slot);
				slot.OnEndEdit(value => Edit(() => row.Slot = value));
				block.Add(LabeledRow(block.ContentTransform, "Slot (01, 02, …):", slot, hoverKey: "special.Slot"));
				UiTextField varType = UiTextField.Create(block.ContentTransform, row.VarType);
				varType.OnEndEdit(value => Edit(() => row.VarType = value));
				block.Add(LabeledRow(block.ContentTransform, "var_type:", varType, hoverKey: "special.VarType"));
				UiTextField name = UiTextField.Create(block.ContentTransform, row.Name);
				name.OnEndEdit(value => Edit(() => row.Name = value));
				block.Add(LabeledRow(block.ContentTransform, "Name:", name, hoverKey: "special.Name"));
				block.Add(LabeledExpression(block.ContentTransform, "Value:", out _, value => Edit(() => row.Value = value), ExpressionContext.General, initial: row.Value, hoverKey: "special.Value"));
			}

			UiButton addVariable = UiButton.Create(specialHost.ContentTransform, "Add variable", () =>
			{
				int n = current.Body.Special.Count + 1;
				current.Body.Special.Add(new SpecialVar { Slot = n.ToString("00") });
				LabSaveUx.MarkDirty();
				RebuildSpecial();
			}, primary: false);
			specialHost.Add(addVariable.FixedHeight(28f));
			LabHoverInfo.Bind(addVariable.GameObject, "special.Add");
		}

		private static void RebuildAi()
		{
			if (aiHost == null)
			{
				return;
			}

			aiHost.Clear();
			if (current.Body == null)
			{
				return;
			}

			for (int i = 0; i < current.Body.Ai.Count; i++)
			{
				int index = i;
				AiBlock ai = current.Body.Ai[i];
				UiStack block = UiStack.Vertical(aiHost.ContentTransform, UiTheme.Default, spacing: 4f, padding: 4f);
				aiHost.Add(block);
				UiTextField name = UiTextField.Create(block.ContentTransform, ai.Name);
				name.OnEndEdit(value => Edit(() => ai.Name = value));
				block.Add(LabeledRow(block.ContentTransform, "Block name (AIConfigB / AIBrain*):", name, hoverKey: "ai.BlockName"));
				UiTextField inner = UiTextField.Create(block.ContentTransform, ai.InnerKv, multiline: true);
				inner.OnEndEdit(value => Edit(() => ai.InnerKv = value));
				block.Add(inner.FixedHeight(140f));
				LabHoverInfo.Bind(inner.GameObject, "ai.InnerKv");
				block.Add(UiButton.Create(block.ContentTransform, "Remove AI block", () =>
				{
					current.Body.Ai.RemoveAt(index);
					LabSaveUx.MarkDirty();
					RebuildAi();
				}, primary: false).FixedHeight(28f));
			}

			UiButton addAi = UiButton.Create(aiHost.ContentTransform, "Add AIConfigB", () =>
			{
				current.Body.Ai.Add(new AiBlock { Name = "AIConfigB" });
				LabSaveUx.MarkDirty();
				RebuildAi();
			}, primary: false);
			aiHost.Add(addAi.FixedHeight(28f));
			LabHoverInfo.Bind(addAi.GameObject, "ai.Add");
		}

		private static void RebuildAdvanced()
		{
			if (advancedHost == null)
			{
				return;
			}

			advancedHost.Clear();
			if (current.Body == null)
			{
				return;
			}

			if (current.Body.OpaqueTopLevel.Count == 0)
			{
				advancedHost.Add(UiLabel.Create(advancedHost.ContentTransform, "No unrecognized top-level blocks.").FixedHeight(22f));
			}

			for (int i = 0; i < current.Body.OpaqueTopLevel.Count; i++)
			{
				int index = i;
				UiTextField field = UiTextField.Create(advancedHost.ContentTransform, current.Body.OpaqueTopLevel[i], multiline: true);
				field.OnEndEdit(value => Edit(() => current.Body.OpaqueTopLevel[index] = value));
				advancedHost.Add(field.FixedHeight(160f));
				LabHoverInfo.Bind(field.GameObject, "advanced.OpaqueTopLevel");
			}
		}

		private static void OnBehaviorFlagChanged()
		{
			if (suppressFieldEvents || current == null)
			{
				return;
			}

			List<string> flags = new List<string>();
			foreach (KeyValuePair<string, UiToggle> entry in behaviorToggles)
			{
				if (entry.Value.Toggle.isOn)
				{
					flags.Add(entry.Key);
				}
			}

			current.BehaviorFlags = flags;
			RefreshVisibility();
			LabSaveUx.MarkDirty();
		}

		/// <summary>Applies an envelope edit and marks the library session dirty, unless Bind is pushing values.</summary>
		private static void Edit(Action assign)
		{
			if (suppressFieldEvents || current == null)
			{
				return;
			}

			assign();
			LabSaveUx.MarkDirty();
		}

		/// <summary>Rebuilds body cards after a structural edit and marks the library session dirty.</summary>
		private static void DirtyRebuild()
		{
			LabSaveUx.MarkDirty();
			RebuildBody();
		}

		private static void RebuildCastFxPanel()
		{
			if (customAssetsHost == null)
			{
				return;
			}

			AbilityEditorSprites.BuildCastFxPanel(customAssetsHost, current, castFXIdField, RefreshCatalogCombos, RebuildCastFxPanel);
			RefreshVisibility();
		}


		private static void RefreshCatalogCombos()
		{
			if (animationIdField != null)
			{
				animationIdField.SetOptions(AbilityCatalogLookups.AnimationOptions());
			}

			if (castFXIdField != null)
			{
				castFXIdField.SetOptions(AbilityCatalogLookups.FxMegaOptions());
			}
		}

		/// <summary>Label plus expression composer, optionally bound to the hover-info strip.</summary>
		internal static UiElement LabeledExpression(Transform parent, string label, out AbilityExpressionField field, System.Action<string> onChanged, ExpressionContext context, string initial = "", string typeId = null, string fieldKey = null, string hoverKey = null)
		{
			UiStack row = UiStack.Vertical(parent, UiTheme.Default, spacing: 2f, padding: 0f);
			row.Add(UiLabel.Create(row.ContentTransform, label).FixedHeight(18f));
			field = AbilityExpressionField.Create(row.ContentTransform, initial, onChanged, context, typeId, fieldKey);
			row.Add(field.Root);
			AbilityExpressionField captured = field;
			LabHoverInfo.Bind(row.GameObject, hoverKey, () => captured.Text);
			return row;
		}

		/// <summary>Label plus a single widget row, optionally bound to the hover-info strip.</summary>
		internal static UiElement LabeledRow<T>(Transform parent, string label, T field, string hoverKey = null, System.Func<string> currentValue = null) where T : UiElement<T>
		{
			UiStack row = UiStack.Vertical(parent, UiTheme.Default, spacing: 2f, padding: 0f);
			row.Add(UiLabel.Create(row.ContentTransform, label).FixedHeight(18f));
			row.Add(field.FixedHeight(28f));
			LabHoverInfo.Bind(row.GameObject, hoverKey, currentValue);
			return row;
		}

		internal static int IndexOfOr0(string[] options, string value)
		{
			int index = System.Array.IndexOf(options, value);
			return index >= 0 ? index : 0;
		}
	}
}
