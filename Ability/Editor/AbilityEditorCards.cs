using System;
using System.Collections.Generic;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>Event hats and nested action cards for the shared ability form.</summary>
	internal static class AbilityEditorCards
	{
		internal static void RebuildEvents(UiStack host, AbilityFileModel model, UiContextMenu addMenu, Action rebuild)
		{
			host.Clear();
			if (model.Body == null)
			{
				model.Body = new AbilityBody();
			}

			List<string> names = new List<string>(AbilityEventNames.DefaultAbilityEvents);
			foreach (EventNode ev in model.Body.Events)
			{
				if (!names.Contains(ev.Name))
				{
					names.Add(ev.Name);
				}
			}

			foreach (string name in names)
			{
				EventNode ev = model.Body.Event(name);
				host.Add(BuildEventHat(host.ContentTransform, ev, model, addMenu, rebuild, () =>
				{
					if (Array.IndexOf(AbilityEventNames.DefaultAbilityEvents, ev.Name) < 0)
					{
						model.Body.Events.Remove(ev);
						rebuild();
					}
				}));
			}

			host.Add(UiButton.Create(host.ContentTransform, "Add event hat", () =>
			{
				ShowAddNames(addMenu, Remaining(AbilityEventNames.FiredAbilityEvents, names), picked =>
				{
					model.Body.Event(picked);
					rebuild();
				});
			}, primary: false).FixedHeight(28f));
		}

		internal static void RebuildModifiers(UiStack host, AbilityFileModel model, UiContextMenu addMenu, Action rebuild)
		{
			host.Clear();
			if (model.Body == null)
			{
				model.Body = new AbilityBody();
			}

			for (int i = 0; i < model.Body.Modifiers.Count; i++)
			{
				int index = i;
				ModifierDef mod = model.Body.Modifiers[i];
				UiStack card = UiStack.Vertical(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 6f);
				host.Add(card);

				UiStack header = UiStack.Horizontal(card.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
				card.Add(header.FixedHeight(28f));
				header.Add(UiButton.Create(header.ContentTransform, mod.Collapsed ? "+" : "-", () =>
				{
					mod.Collapsed = !mod.Collapsed;
					rebuild();
				}, primary: false).FixedWidth(28f));
				header.Add(UiLabel.Create(header.ContentTransform, "Modifier").Grow());
				LabHoverInfo.Bind(header.GameObject, "modifier.Card");
				header.Add(UiButton.Create(header.ContentTransform, "x", () =>
				{
					model.Body.Modifiers.RemoveAt(index);
					rebuild();
				}, primary: false).FixedWidth(28f));

				if (mod.Collapsed)
				{
					continue;
				}

				UiTextField idField = UiTextField.Create(card.ContentTransform, mod.Id);
				idField.OnEndEdit(value => Edit(() => mod.Id = value));
				card.Add(AbilityEditorForm.LabeledRow(card.ContentTransform, "Id:", idField, hoverKey: "modifier.Id"));

				UiToggle passive = UiToggle.Create(card.ContentTransform, "Passive", mod.Passive);
				passive.OnValueChanged(value => Edit(() => mod.Passive = value));
				card.Add(passive.FixedHeight(22f));
				LabHoverInfo.Bind(passive.GameObject, "modifier.Passive");

				UiComboBox fx = UiComboBox.Create(card.ContentTransform, AbilityPickerCatalog.FxMegaNames, mod.ModifierFXName);
				fx.OnEndEdit(value => Edit(() => mod.ModifierFXName = value));
				card.Add(AbilityEditorForm.LabeledRow(card.ContentTransform, "Modifier FX Name:", fx, hoverKey: "modifier.ModifierFXName"));

				UiTextField incompatible = UiTextField.Create(card.ContentTransform, mod.IncompatibleStates);
				incompatible.OnEndEdit(value => Edit(() => mod.IncompatibleStates = value));
				card.Add(AbilityEditorForm.LabeledRow(card.ContentTransform, "Incompatible States:", incompatible, hoverKey: "modifier.IncompatibleStates"));

				UiTextField autoTags = UiTextField.Create(card.ContentTransform, mod.AutoRemoveTags);
				autoTags.OnEndEdit(value => Edit(() => mod.AutoRemoveTags = value));
				card.Add(AbilityEditorForm.LabeledRow(card.ContentTransform, "Auto Remove Tags:", autoTags, hoverKey: "modifier.AutoRemoveTags"));

				UiTextField autoIds = UiTextField.Create(card.ContentTransform, mod.AutoRemoveModifierIds);
				autoIds.OnEndEdit(value => Edit(() => mod.AutoRemoveModifierIds = value));
				card.Add(AbilityEditorForm.LabeledRow(card.ContentTransform, "Auto Remove Modifier Ids:", autoIds, hoverKey: "modifier.AutoRemoveModifierIds"));

				UiTextField props = UiTextField.Create(card.ContentTransform, mod.PropertiesAddKv, multiline: true);
				props.OnEndEdit(value => Edit(() => mod.PropertiesAddKv = value));
				card.Add(UiLabel.Create(card.ContentTransform, "PropertiesAdd (inner KV):").FixedHeight(18f));
				card.Add(props.FixedHeight(80f));
				LabHoverInfo.Bind(props.GameObject, "modifier.PropertiesAdd");

				UiTextField extra = UiTextField.Create(card.ContentTransform, mod.ExtraKv, multiline: true);
				extra.OnEndEdit(value => Edit(() => mod.ExtraKv = value));
				card.Add(UiLabel.Create(card.ContentTransform, "Extra modifier KV:").FixedHeight(18f));
				card.Add(extra.FixedHeight(80f));
				LabHoverInfo.Bind(extra.GameObject, "modifier.ExtraKv");

				List<string> eventNames = new List<string>(AbilityEventNames.DefaultModifierEvents);
				foreach (EventNode ev in mod.Events)
				{
					if (!eventNames.Contains(ev.Name))
					{
						eventNames.Add(ev.Name);
					}
				}

				foreach (string name in eventNames)
				{
					EventNode ev = FindOrAdd(mod.Events, name);
					card.Add(BuildEventHat(card.ContentTransform, ev, model, addMenu, rebuild, () =>
					{
						if (Array.IndexOf(AbilityEventNames.DefaultModifierEvents, ev.Name) < 0)
						{
							mod.Events.Remove(ev);
							rebuild();
						}
					}));
				}

				card.Add(UiButton.Create(card.ContentTransform, "Add modifier event", () =>
				{
					ShowAddNames(addMenu, Remaining(AbilityEventNames.FiredModifierEvents, eventNames), picked =>
					{
						FindOrAdd(mod.Events, picked);
						rebuild();
					});
				}, primary: false).FixedHeight(28f));
			}

			host.Add(UiButton.Create(host.ContentTransform, "Add modifier", () =>
			{
				model.Body.Modifiers.Add(new ModifierDef { Id = NextModifierId(model.Body), Collapsed = false });
				rebuild();
			}, primary: false).FixedHeight(28f));
		}

		private static UiElement BuildEventHat(Transform parent, EventNode ev, AbilityFileModel model, UiContextMenu addMenu, Action rebuild, Action onRemove)
		{
			UiStack hat = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 4f);
			UiStack header = UiStack.Horizontal(hat.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			hat.Add(header.FixedHeight(28f));
			header.Add(UiButton.Create(header.ContentTransform, ev.Collapsed ? "+" : "-", () =>
			{
				ev.Collapsed = !ev.Collapsed;
				rebuild();
			}, primary: false).FixedWidth(28f));
			header.Add(UiLabel.Create(header.ContentTransform, ev.Name).Grow());
			LabHoverInfo.Bind(header.GameObject, "event." + ev.Name);
			header.Add(UiButton.Create(header.ContentTransform, "Add action", () =>
			{
				ShowAddCards(addMenu, advanced: false, typeId =>
				{
					AddNewCard(ev.Cards, typeId);
					ev.Collapsed = false;
					rebuild();
				});
			}, primary: false).FixedWidth(110f));
			header.Add(UiButton.Create(header.ContentTransform, "Advanced", () =>
			{
				ShowAddCards(addMenu, advanced: true, typeId =>
				{
					AddNewCard(ev.Cards, typeId);
					ev.Collapsed = false;
					rebuild();
				});
			}, primary: false).FixedWidth(90f));
			header.Add(UiButton.Create(header.ContentTransform, "x", () => onRemove(), primary: false).FixedWidth(28f));

			if (!ev.Collapsed)
			{
				hat.Add(BuildCardStack(hat.ContentTransform, ev.Cards, model, addMenu, rebuild));
			}

			return hat;
		}

		private static UiStack BuildCardStack(Transform parent, List<ActionCard> cards, AbilityFileModel model, UiContextMenu addMenu, Action rebuild)
		{
			UiStack stack = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 2f);
			for (int i = 0; i < cards.Count; i++)
			{
				int index = i;
				stack.Add(BuildCard(stack.ContentTransform, cards[index], model, () =>
				{
					if (index > 0)
					{
						ActionCard swap = cards[index - 1];
						cards[index - 1] = cards[index];
						cards[index] = swap;
						rebuild();
					}
				}, () =>
				{
					if (index < cards.Count - 1)
					{
						ActionCard swap = cards[index + 1];
						cards[index + 1] = cards[index];
						cards[index] = swap;
						rebuild();
					}
				}, () =>
				{
					cards.Insert(index + 1, cards[index].Clone());
					rebuild();
				}, () =>
				{
					cards.RemoveAt(index);
					rebuild();
				}, addMenu, rebuild));
			}

			return stack;
		}

		private static UiElement BuildCard(Transform parent, ActionCard card, AbilityFileModel model, Action moveUp, Action moveDown, Action duplicate, Action delete, UiContextMenu addMenu, Action rebuild)
		{
			UiStack box = UiStack.Vertical(parent, UiTheme.Default, spacing: 4f, padding: 6f);
			ActionCardDescriptor descriptor = AbilityCardRegistry.Find(card.TypeId);
			string label = card.IsOpaque
				? card.TypeId + " (opaque)"
				: (descriptor != null ? descriptor.DisplayLabel : card.TypeId);

			UiStack header = UiStack.Horizontal(box.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			box.Add(header.FixedHeight(28f));
			header.Add(UiButton.Create(header.ContentTransform, card.Collapsed ? "+" : "-", () =>
			{
				card.Collapsed = !card.Collapsed;
				rebuild();
			}, primary: false).FixedWidth(28f));
			header.Add(UiLabel.Create(header.ContentTransform, label).Grow());
			LabHoverInfo.Bind(header.GameObject, card.IsOpaque ? "card.opaque" : "card." + card.TypeId);
			header.Add(UiButton.Create(header.ContentTransform, "^", () => moveUp(), primary: false).FixedWidth(28f));
			header.Add(UiButton.Create(header.ContentTransform, "v", () => moveDown(), primary: false).FixedWidth(28f));
			header.Add(UiButton.Create(header.ContentTransform, "Dup", () => duplicate(), primary: false).FixedWidth(44f));
			header.Add(UiButton.Create(header.ContentTransform, "x", () => delete(), primary: false).FixedWidth(28f));

			if (card.Collapsed)
			{
				return box;
			}

			if (card.IsOpaque)
			{
				UiTextField opaque = UiTextField.Create(box.ContentTransform, card.OpaqueText, multiline: true);
				opaque.OnEndEdit(value => Edit(() => card.OpaqueText = value));
				box.Add(opaque.FixedHeight(140f));
				LabHoverInfo.Bind(opaque.GameObject, "card.opaque");
				return box;
			}

			if (descriptor != null)
			{
				foreach (string key in descriptor.FieldKeys)
				{
					if (!card.Fields.ContainsKey(key))
					{
						card.Fields[key] = string.Empty;
					}

					string current = card.Fields[key];
					if (card.TypeId == "Lua" && key == "Action")
					{
						UiTextField lua = UiTextField.Create(box.ContentTransform, current, multiline: true);
						string fieldKey = key;
						lua.OnEndEdit(value => Edit(() => card.Fields[fieldKey] = value));
						box.Add(UiLabel.Create(box.ContentTransform, "Action (Lua):").FixedHeight(18f));
						box.Add(lua.FixedHeight(120f));
						LabHoverInfo.Bind(lua.GameObject, "field.Lua.Action", () => lua.InputField.text);
						continue;
					}

					if (card.TypeId == "TrackingProjectile" && key == "Model")
					{
						AbilityEditorSprites.AddProjectileModelRow(box, card, model, rebuild);
						continue;
					}

					if (descriptor.FieldCatalogs.TryGetValue(key, out ActionCardCatalogKind kind)
						&& kind != ActionCardCatalogKind.None)
					{
						if (kind == ActionCardCatalogKind.Expression || kind == ActionCardCatalogKind.UnitRef)
						{
							string fieldKey = key;
							box.Add(AbilityEditorForm.LabeledExpression(
								box.ContentTransform,
								key + ":",
								out _,
								value => Edit(() => card.Fields[fieldKey] = value),
								ContextForKey(key, kind),
								initial: current,
								typeId: card.TypeId,
								fieldKey: fieldKey,
								hoverKey: "field." + card.TypeId + "." + fieldKey));
						}
						else
						{
							UiComboBox combo = UiComboBox.Create(box.ContentTransform, AbilityCatalogLookups.OptionsFor(kind, card.TypeId, key, current), current);
							string fieldKey = key;
							ActionCardCatalogKind fieldKind = kind;
							combo.OnEndEdit(value =>
							{
								Edit(() => card.Fields[fieldKey] = fieldKind == ActionCardCatalogKind.Unit
									? AbilitySummonLink.CommitUnitName(model, value)
									: value);
							});
							box.Add(AbilityEditorForm.LabeledRow(
								box.ContentTransform,
								key + ":",
								combo,
								hoverKey: "field." + card.TypeId + "." + fieldKey,
								currentValue: () => combo.InputField.text));
						}
					}
					else
					{
						UiTextField field = UiTextField.Create(box.ContentTransform, current);
						field.OnEndEdit(value => Edit(() => card.Fields[key] = value));
						box.Add(AbilityEditorForm.LabeledRow(box.ContentTransform, key + ":", field, hoverKey: "field." + card.TypeId + "." + key));
					}
				}

				foreach (string stackName in descriptor.ChildStackNames)
				{
					List<ActionCard> children = card.Stack(stackName);
					UiLabel stackLabel = UiLabel.Create(box.ContentTransform, stackName);
					box.Add(stackLabel.FixedHeight(18f));
					LabHoverInfo.Bind(stackLabel.GameObject, "card.stack." + stackName);
					box.Add(BuildCardStack(box.ContentTransform, children, model, addMenu, rebuild));
					UiStack addRow = UiStack.Horizontal(box.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
					box.Add(addRow.FixedHeight(28f));
					addRow.Add(UiButton.Create(addRow.ContentTransform, "Add in " + stackName, () =>
					{
						ShowAddCards(addMenu, advanced: false, typeId =>
						{
							AddNewCard(children, typeId);
							rebuild();
						});
					}, primary: false).Grow());
					addRow.Add(UiButton.Create(addRow.ContentTransform, "Advanced", () =>
					{
						ShowAddCards(addMenu, advanced: true, typeId =>
						{
							AddNewCard(children, typeId);
							rebuild();
						});
					}, primary: false).FixedWidth(90f));
				}
			}

			UiTextField extra = UiTextField.Create(box.ContentTransform, card.ExtraKv, multiline: true);
			extra.OnEndEdit(value => Edit(() => card.ExtraKv = value));
			box.Add(UiLabel.Create(box.ContentTransform, "Extra KV (Target blocks, unknown fields):").FixedHeight(18f));
			box.Add(extra.FixedHeight(80f));
			LabHoverInfo.Bind(extra.GameObject, "field." + card.TypeId + ".ExtraKv");
			return box;
		}

		private static void AddNewCard(List<ActionCard> cards, string typeId)
		{
			ActionCard card = AbilityCardFactory.Create(typeId);
			card.Collapsed = false;
			cards.Add(card);
		}

		private static void ShowAddCards(UiContextMenu menu, bool advanced, Action<string> onPick)
		{
			if (menu == null)
			{
				return;
			}

			menu.ClearItems();
			List<ActionCardDescriptor> list = advanced
				? AbilityCardRegistry.AdvancedAddList()
				: AbilityCardRegistry.DefaultAddList();
			foreach (ActionCardDescriptor descriptor in list)
			{
				string typeId = descriptor.TypeId;
				menu.AddItem(descriptor.DisplayLabel, () => onPick(typeId));
			}

			menu.Show(Input.mousePosition);
		}

		private static void ShowAddNames(UiContextMenu menu, List<string> names, Action<string> onPick)
		{
			if (menu == null)
			{
				return;
			}

			menu.ClearItems();
			if (names.Count == 0)
			{
				menu.AddItem("(all hats already shown)", null, enabled: false);
			}

			foreach (string name in names)
			{
				string picked = name;
				menu.AddItem(picked, () => onPick(picked));
			}

			menu.Show(Input.mousePosition);
		}

		private static List<string> Remaining(string[] all, List<string> present)
		{
			List<string> leftover = new List<string>();
			foreach (string name in all)
			{
				if (!present.Contains(name))
				{
					leftover.Add(name);
				}
			}

			return leftover;
		}

		private static EventNode FindOrAdd(List<EventNode> events, string name)
		{
			foreach (EventNode ev in events)
			{
				if (ev.Name == name)
				{
					return ev;
				}
			}

			EventNode created = new EventNode { Name = name };
			events.Add(created);
			return created;
		}

		private static ExpressionContext ContextForKey(string key, ActionCardCatalogKind kind)
		{
			if (kind == ActionCardCatalogKind.UnitRef)
			{
				return ExpressionContext.Unit;
			}

			switch (key)
			{
				case "Damage":
				case "HealAmount":
				case "Duration":
				case "Time":
				case "Strength":
				case "ArmorAmount":
				case "Offset":
				case "Times":
				case "Refresh":
				case "Enqueue":
				case "Backstab":
					return ExpressionContext.Number;
				case "SourcePos":
				case "TargetPos":
				case "Position":
					return ExpressionContext.Position;
				case "Condition":
					return ExpressionContext.Condition;
				case "Tags":
					return ExpressionContext.Tags;
				case "UnitGroup":
					return ExpressionContext.Group;
				default:
					return ExpressionContext.General;
			}
		}

		private static string NextModifierId(AbilityBody body)
		{
			string prefix = "new_modifier";
			string id = prefix;
			int n = 2;
			while (true)
			{
				bool taken = false;
				foreach (ModifierDef mod in body.Modifiers)
				{
					if (mod.Id == id)
					{
						taken = true;
						break;
					}
				}

				if (!taken)
				{
					return id;
				}

				id = prefix + "_" + n;
				n++;
			}
		}

		/// <summary>Applies a card-field edit and marks the library session dirty.</summary>
		private static void Edit(Action assign)
		{
			assign();
			LabSaveUx.MarkDirty();
		}
	}
}
