using System;
using System.Collections.Generic;
using System.Text;
using LokrAbilityLab;
using LokrAbilityLab.Editor;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>File → Browse Vanilla Abilities... reference view over the shipped ability catalog, with Copy into Library (Phases 2 and 4).</summary>
	/// <remarks>
	/// Phases 1-4 of the Vanilla Ability Edit track (docs/roadmaps/started/vanilla-ability-edit.md).
	/// The detail view is read-only research (envelope fields, event list, which action types are
	/// opaque to the current card editor, icon/FX names); Copy into Library (VanillaAbilityImporter)
	/// actually writes a folder, in Override or Fork mode, behind a confirm step showing the "Used
	/// by" blast radius (AbilityUsage.BlastRadius) and any global modifier id collisions
	/// (VanillaAbilityImporter.FindModifierCollisions) -- Phase 4.
	///
	/// Registered with isVisible: IsLibraryOpen (AbilityLibraryProjectType.cs), not always-visible
	/// like File -> Edit Vanilla Hero... -- changed 2026-08-17. Copy into Library always needs an
	/// open library as its target (ResolveTargetLibrary requires one and no longer falls back to an
	/// arbitrary "first" library or auto-creates one), so showing this entry before a library is
	/// open led to a dead end: browse, then find every Copy button refuses to do anything. Gating
	/// the whole entry on IsLibraryOpen (the same guard "New Ability" already uses) means it only
	/// ever appears somewhere the copy can actually succeed.
	/// </remarks>
	internal static class VanillaAbilityBrowserModal
	{
		private static UiModal modal;
		private static UiStack listRoot;
		private static UiStack detailRoot;
		private static UiTextField searchField;
		private static UiStack listRows;
		private static UiLabel listEmptyLabel;
		private static UiLabel detailTitle;
		private static UiStack detailBody;
		private static UiLabel copyStatusLabel;
		private static AbilityFileModel detailModel;

		private static UiModal confirmModal;
		private static UiLabel confirmMessageLabel;
		private static string pendingCopyAbilityId;
		private static string pendingCopyLibrary;
		private static VanillaAbilityImportMode pendingCopyMode;

		/// <summary>Shows the searchable list of shipped abilities.</summary>
		internal static void Show()
		{
			if (!EnsureModal())
			{
				return;
			}

			ShowList();
			modal.Show();
		}

		private static bool EnsureModal()
		{
			if (modal != null && modal.GameObject != null)
			{
				return true;
			}

			Transform canvas = LokrLab.Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			modal = UiModal.Create(canvas, theme, "Browse Vanilla Abilities", 760f, 620f);
			UiStack content = UiStack.Vertical(modal.ContentParent, theme, spacing: 8f, padding: 12f);
			modal.Add(content);

			listRoot = BuildListRoot(content.ContentTransform, theme);
			content.Add(listRoot.Grow());

			detailRoot = BuildDetailRoot(content.ContentTransform, theme);
			content.Add(detailRoot.Grow());
			detailRoot.Visible(false);

			content.Add(UiButton.Create(content.ContentTransform, "Close", Hide, theme, primary: false).FixedHeight(32f));

			LabHoverInfo.Bind(modal.GameObject, "ability.vanilla.Browse");
			return true;
		}

		private static UiStack BuildListRoot(Transform parent, UiTheme theme)
		{
			UiStack root = UiStack.Vertical(parent, theme, spacing: 8f, padding: 0f);

			UiLabel explainer = UiLabel.Create(root.ContentTransform,
				"Pick an ability to see its structure and, if you want, copy it into a library as an Override (replaces the shipped ability everywhere) or a Fork (new id, vanilla untouched).",
				theme, 12, TextAnchor.UpperLeft);
			root.Add(explainer.FixedHeight(40f));
			LabHoverInfo.Bind(explainer.GameObject, "ability.vanilla.OverrideVsFork");

			searchField = UiTextField.Create(root.ContentTransform, string.Empty, theme);
			searchField.InputField.onValueChanged.AddListener(_ => RefreshList());
			root.Add(searchField.FixedHeight(28f));

			listEmptyLabel = UiLabel.Create(root.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			root.Add(listEmptyLabel.FixedHeight(24f));

			listRows = UiStack.Vertical(root.ContentTransform, theme, spacing: 2f, padding: 0f, scrollable: true);
			root.Add(listRows.Grow());

			return root;
		}

		private static UiStack BuildDetailRoot(Transform parent, UiTheme theme)
		{
			UiStack root = UiStack.Vertical(parent, theme, spacing: 8f, padding: 0f);

			detailTitle = UiLabel.Create(root.ContentTransform, string.Empty, theme, 16, TextAnchor.UpperLeft);
			root.Add(detailTitle.FixedHeight(24f));

			root.Add(UiButton.Create(root.ContentTransform, "< Back to list", ShowList, theme, primary: false)
				.FixedHeight(26f).FixedWidth(140f));

			detailBody = UiStack.Vertical(root.ContentTransform, theme, spacing: 6f, padding: 0f, scrollable: true);
			root.Add(detailBody.Grow());

			UiStack copyRow = UiStack.Horizontal(root.ContentTransform, theme, spacing: 8f, padding: 0f);
			root.Add(copyRow.FixedHeight(32f));
			UiButton copyOverride = UiButton.Create(copyRow.ContentTransform, "Copy into Library (Override)",
				() => CopyIntoLibrary(VanillaAbilityImportMode.Override), theme, primary: false);
			copyRow.Add(copyOverride.Grow());
			LabHoverInfo.Bind(copyOverride.GameObject, "ability.vanilla.CopyOverride");
			UiButton copyFork = UiButton.Create(copyRow.ContentTransform, "Copy into Library (Fork)",
				() => CopyIntoLibrary(VanillaAbilityImportMode.Fork), theme, primary: false);
			copyRow.Add(copyFork.Grow());
			LabHoverInfo.Bind(copyFork.GameObject, "ability.vanilla.CopyFork");

			copyStatusLabel = UiLabel.Create(root.ContentTransform, string.Empty, theme, 11, TextAnchor.UpperLeft);
			root.Add(copyStatusLabel.FixedHeight(20f));

			return root;
		}

		private static void ShowList()
		{
			listRoot.Visible(true);
			detailRoot.Visible(false);
			RefreshList();
		}

		private static void RefreshList()
		{
			if (listRows == null)
			{
				return;
			}

			listRows.Clear();
			string filter = searchField != null ? searchField.InputField.text.Trim() : string.Empty;
			int shown = 0;
			foreach (AbilityFileModel model in VanillaAbilityCatalog.All())
			{
				if (!string.IsNullOrEmpty(filter) && model.Id.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				AbilityFileModel captured = model;
				string label = model.Id + "  —  " + Summary(model);
				listRows.Add(UiButton.Create(listRows.ContentTransform, label, () => ShowDetail(captured), primary: false)
					.FixedHeight(26f));
				shown++;
			}

			listEmptyLabel.SetText(shown == 0 ? "No shipped abilities match." : string.Empty);
			listEmptyLabel.Visible(shown == 0);
		}

		private static string Summary(AbilityFileModel model)
		{
			List<string> bits = new List<string>();
			if (model.BehaviorFlags.Count > 0)
			{
				bits.Add(string.Join(" · ", model.BehaviorFlags.ToArray()));
			}

			if (!string.IsNullOrEmpty(model.CastRange))
			{
				bits.Add("range " + model.CastRange);
			}

			return bits.Count == 0 ? "ability" : string.Join("  ", bits.ToArray());
		}

		private static void ShowDetail(AbilityFileModel model)
		{
			listRoot.Visible(false);
			detailRoot.Visible(true);
			detailModel = model;
			detailTitle.SetText(model.Id);
			copyStatusLabel.SetText(string.Empty);
			BuildDetailBody(model);
		}

		private static void CopyIntoLibrary(VanillaAbilityImportMode mode)
		{
			if (detailModel == null)
			{
				return;
			}

			string library = ResolveTargetLibrary();
			if (string.IsNullOrEmpty(library))
			{
				copyStatusLabel.SetText("Open (or create) the Ability Library you want to copy into first.");
				return;
			}

			PromptCopyConfirm(detailModel, library, mode);
		}

		/// <summary>Phase 4: shows the "Used by" blast radius and any global modifier id collisions before committing the copy.</summary>
		private static void PromptCopyConfirm(AbilityFileModel model, string library, VanillaAbilityImportMode mode)
		{
			if (!EnsureConfirmModal())
			{
				return;
			}

			pendingCopyAbilityId = model.Id;
			pendingCopyLibrary = library;
			pendingCopyMode = mode;

			AbilityUsage.BlastRadiusResult blast = AbilityUsage.BlastRadius(model.Id);
			List<(string ModifierId, string OwnerAbilityId)> collisions = VanillaAbilityImporter.FindModifierCollisions(model);

			StringBuilder message = new StringBuilder();
			message.Append(mode == VanillaAbilityImportMode.Override
				? "Override replaces '" + model.Id + "' everywhere it's used, globally.\n\n"
				: "Fork mints a new id -- vanilla itself is untouched.\n\n");

			message.Append(blast.UnitIds.Count == 0
				? "No currently known units reference this ability.\n"
				: "Used by " + blast.UnitIds.Count + ": " + string.Join(", ", blast.UnitIds.ToArray()) + "\n");

			if (blast.UsedInTutorialContent)
			{
				message.Append("At least one user is tutorial content -- this may affect the tutorial.\n");
			}

			if (collisions.Count > 0)
			{
				string ids = string.Join(", ", collisions.ConvertAll(c => c.ModifierId).ToArray());
				message.Append(mode == VanillaAbilityImportMode.Override
					? "\nModifier id(s) already exist globally (expected for Override, will be replaced): " + ids
					: "\nWarning: Fork does not rename modifier ids. These already exist globally and this copy will replace them everywhere too, not just for this fork: " + ids);
			}

			confirmMessageLabel.SetText(message.ToString());
			confirmModal.Show();
		}

		private static bool EnsureConfirmModal()
		{
			if (confirmModal != null && confirmModal.GameObject != null)
			{
				return true;
			}

			Transform canvas = LokrLab.Lab.Canvas;
			if (canvas == null)
			{
				return false;
			}

			UiTheme theme = UiTheme.Default;
			confirmModal = UiModal.Create(canvas, theme, "Confirm Copy", 560f, 280f);
			UiStack content = UiStack.Vertical(confirmModal.ContentParent, theme, spacing: 8f, padding: 12f);
			confirmModal.Add(content);
			confirmMessageLabel = UiLabel.Create(content.ContentTransform, string.Empty, theme, 12, TextAnchor.UpperLeft);
			content.Add(confirmMessageLabel.Grow());
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, theme, spacing: 8f, padding: 0f);
			content.Add(buttons.FixedHeight(36f));
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Copy", OnCopyConfirmed, theme, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", confirmModal.Hide, theme, primary: false).FixedWidth(120f));
			LabHoverInfo.Bind(confirmModal.GameObject, "ability.vanilla.CopyConfirm");
			return true;
		}

		private static void OnCopyConfirmed()
		{
			if (confirmModal != null)
			{
				confirmModal.Hide();
			}

			string abilityId = pendingCopyAbilityId;
			string library = pendingCopyLibrary;
			VanillaAbilityImportMode mode = pendingCopyMode;
			pendingCopyAbilityId = null;
			pendingCopyLibrary = null;

			if (string.IsNullOrEmpty(abilityId) || string.IsNullOrEmpty(library))
			{
				return;
			}

			if (!VanillaAbilityImporter.TryImport(abilityId, library, mode, out string newFolder, out string error))
			{
				copyStatusLabel.SetText(error ?? "Copy failed.");
				return;
			}

			LokrLabApi.LokrLabApi.RequestRefresh();
			copyStatusLabel.SetText("Copied to " + newFolder + ".");
		}

		/// <summary>The currently open ability library, or null.</summary>
		/// <remarks>
		/// Deliberately does not fall back to "the first library that happens to exist" or silently
		/// mint one -- that picked an arbitrary, possibly unrelated library with no way to tell which
		/// one a copy landed in until after the fact. Requiring an open library makes the target
		/// explicit: open (or create) the library you want, then copy into it.
		/// </remarks>
		private static string ResolveTargetLibrary()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session != null && session.ProjectTypeId == LokrLabApi.LokrLabApi.AbilityLibraryTypeId
				&& !string.IsNullOrEmpty(session.FolderPath))
			{
				return session.FolderPath;
			}

			return null;
		}

		private static void BuildDetailBody(AbilityFileModel model)
		{
			detailBody.Clear();
			UiTheme theme = UiTheme.Default;

			AddSectionHeader("Envelope");
			AddRow("Behavior", model.BehaviorFlags.Count > 0 ? string.Join(" | ", model.BehaviorFlags.ToArray()) : "(none)");
			if (!string.IsNullOrEmpty(model.LocalizationId))
			{
				AddRow("Localization Id", model.LocalizationId);
			}

			if (!model.IsPassive)
			{
				AddRow("Team Filter", model.TeamFilter);
				AddRow("Cast Range", string.IsNullOrEmpty(model.CastRange) ? "(none)" : model.CastRange);
				if (!string.IsNullOrEmpty(model.CastMinRange))
				{
					AddRow("Cast Min Range", model.CastMinRange);
				}

				AddRow("Cooldown", model.Cooldown);
				if (!string.IsNullOrEmpty(model.PrewarmCooldown))
				{
					AddRow("Prewarm Cooldown", model.PrewarmCooldown);
				}

				AddRow("AP Cost", model.APCost);
				if (!string.IsNullOrEmpty(model.CanExecute))
				{
					AddRow("Can Execute", model.CanExecute);
				}

				if (model.BehaviorFlags.Contains("AOE"))
				{
					AddRow("AOE Kind", model.AOEKind);
					AddRow("AOE Team Filter", model.AOETeamFilter);
					AddRow("AOE Range", model.AOERange);
					if (!string.IsNullOrEmpty(model.AOEMinRange))
					{
						AddRow("AOE Min Range", model.AOEMinRange);
					}

					if (!string.IsNullOrEmpty(model.AOEWidth))
					{
						AddRow("AOE Width", model.AOEWidth);
					}

					AddRow("AOE Center On Caster", model.AOECenterOnCaster ? "yes" : "no");
					AddRow("AOE Affects Caster", model.AOEAffectsCaster ? "yes" : "no");
				}
			}

			if (!string.IsNullOrEmpty(model.HitChanceModifier))
			{
				AddRow("Hit Chance Modifier", model.HitChanceModifier);
			}

			AddSectionHeader("Icon / FX");
			AddRow("Icon", string.IsNullOrEmpty(model.Icon) ? "(none)" : model.Icon);
			AddRow("Animation Id", string.IsNullOrEmpty(model.AnimationId) ? "(none)" : model.AnimationId);
			AddRow("Cast FX Id", string.IsNullOrEmpty(model.CastFXId) ? "(none)" : model.CastFXId);

			List<string> opaqueTypes = new List<string>();
			AbilityUsage.WalkCards(model, (eventName, card) =>
			{
				if (card.IsOpaque && !opaqueTypes.Contains(card.TypeId))
				{
					opaqueTypes.Add(card.TypeId);
				}
			});

			AddSectionHeader("Events");
			if (model.Body.Events.Count == 0)
			{
				AddPlainLine("(no events)");
			}

			foreach (EventNode ev in model.Body.Events)
			{
				string cardList = ev.Cards.Count == 0
					? "(no cards)"
					: string.Join(", ", ev.Cards.ConvertAll(c => c.IsOpaque ? c.TypeId + " (opaque)" : c.TypeId).ToArray());
				AddRow(ev.Name, cardList);
			}

			AddSectionHeader("Opaque action types" + (opaqueTypes.Count > 0 ? " (" + opaqueTypes.Count + ")" : string.Empty));
			if (opaqueTypes.Count == 0)
			{
				AddPlainLine("None — every action card in this ability is fully editable in the current card editor.");
			}
			else
			{
				AddPlainLine(string.Join(", ", opaqueTypes.ToArray()));
				AddPlainLine("These render as raw, uneditable KV text in the card editor. Their content is shown below.");
				AbilityUsage.WalkCards(model, (eventName, card) =>
				{
					if (card.IsOpaque)
					{
						AddRawBlock(eventName + " → " + card.TypeId, card.OpaqueText);
					}
				});
			}

			if (model.Body.Modifiers.Count > 0)
			{
				AddSectionHeader("Modifiers (" + model.Body.Modifiers.Count + ")");
				foreach (ModifierDef mod in model.Body.Modifiers)
				{
					AddPlainLine(mod.Id + (mod.Passive ? "  (passive)" : string.Empty));
				}
			}

			if (model.Body.Ai.Count > 0)
			{
				AddSectionHeader("AI blocks");
				foreach (AiBlock ai in model.Body.Ai)
				{
					AddRawBlock(ai.Name, ai.InnerKv);
				}
			}

			if (model.Body.OpaqueTopLevel.Count > 0)
			{
				AddSectionHeader("Other unrecognized top-level blocks");
				for (int i = 0; i < model.Body.OpaqueTopLevel.Count; i++)
				{
					AddRawBlock("Block " + (i + 1), model.Body.OpaqueTopLevel[i]);
				}
			}

			void AddSectionHeader(string title)
			{
				UiLabel header = UiLabel.Create(detailBody.ContentTransform, title, theme, 13, TextAnchor.UpperLeft);
				detailBody.Add(header.FixedHeight(22f));
			}

			void AddRow(string label, string value)
			{
				UiLabel row = UiLabel.Create(detailBody.ContentTransform, label + ":  " + value, theme, 11, TextAnchor.UpperLeft);
				detailBody.Add(row.FixedHeight(RowHeight(label + ":  " + value)));
			}

			void AddPlainLine(string text)
			{
				UiLabel row = UiLabel.Create(detailBody.ContentTransform, text, theme, 11, TextAnchor.UpperLeft);
				detailBody.Add(row.FixedHeight(RowHeight(text)));
			}

			void AddRawBlock(string title, string kvText)
			{
				UiLabel header = UiLabel.Create(detailBody.ContentTransform, title, theme, 11, TextAnchor.UpperLeft);
				detailBody.Add(header.FixedHeight(18f));
				UiLabel body = UiLabel.Create(detailBody.ContentTransform, kvText, theme, 10, TextAnchor.UpperLeft);
				body.GameObject.name = "OpaqueRawKv";
				detailBody.Add(body.FixedHeight(RowHeight(kvText)));
				LabHoverInfo.Bind(body.GameObject, "card.opaque");
			}
		}

		private static float RowHeight(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return 18f;
			}

			int lines = 1;
			foreach (char c in text)
			{
				if (c == '\n')
				{
					lines++;
				}
			}

			return Mathf.Max(18f, lines * 15f + 4f);
		}

		private static void Hide()
		{
			if (modal != null)
			{
				modal.Hide();
			}
		}
	}
}
