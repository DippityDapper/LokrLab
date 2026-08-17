using System;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Editor
{
	/// <summary>One-browse sprite assign/clear for Cast FX (envelope) and TrackingProjectile Model.</summary>
	internal static class AbilityEditorSprites
	{
		private static readonly string[] FxAttachPoints = { "Chest", "Base", "Head", "CastPoint", "RayPoint" };

		internal static void BuildCastFxPanel(UiStack host, AbilityFileModel model, UiComboBox castFxField, Action refreshCatalogs, Action rebuildPanel)
		{
			host.Clear();
			if (model == null)
			{
				return;
			}

			string name = model.CastFXId ?? string.Empty;
			bool custom = AbilityCustomAssets.Owns(model, "fx", name);
			host.Add(UiLabel.Create(host.ContentTransform, custom
				? "Cast FX sprite: " + name + " (custom PNG)"
				: (string.IsNullOrEmpty(name)
					? "Cast FX sprite: none"
					: "Cast FX: base-game " + name)).FixedHeight(18f));

			UiStack buttons = UiStack.Horizontal(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			host.Add(buttons.FixedHeight(28f));
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Choose sprite…", () =>
			{
				PickCastFx(model, castFxField, refreshCatalogs, rebuildPanel);
			}, primary: false).Grow());
			LabHoverInfo.Bind(buttons.GameObject, "envelope.CastFX.ChooseSprite");
			if (custom)
			{
				UiButton clear = UiButton.Create(buttons.ContentTransform, "Clear sprite", () =>
				{
					ClearCastFx(model, name, castFxField, refreshCatalogs, rebuildPanel);
				}, primary: false);
				buttons.Add(clear.FixedWidth(110f));
				LabHoverInfo.Bind(clear.GameObject, "envelope.CastFX.ClearSprite");
			}

			if (!custom)
			{
				return;
			}

			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			AbilityCustomAssets.SpriteFxEdit edit = AbilityCustomAssets.ReadFx(abilityFolder, name);
			UiComboBox attach = UiComboBox.Create(host.ContentTransform, FxAttachPoints, edit.attachPoint);
			attach.OnEndEdit(value =>
			{
				edit.attachPoint = AbilityCustomAssets.NormalizeAttachPoint(value);
				AbilityCustomAssets.WriteFx(abilityFolder, name, edit);
				AbilityCustomAssets.RefreshRuntime();
				AbilityEditorPanel.SetStatus("Cast FX attach point is now " + edit.attachPoint + ".");
			});
			host.Add(AbilityEditorForm.LabeledRow(host.ContentTransform, "Attach point:", attach, hoverKey: "envelope.CastFX.Attach"));

			AddFloatRow(host, "Duration (seconds):", edit.duration, parsed =>
			{
				edit.duration = parsed;
				AbilityCustomAssets.WriteFx(abilityFolder, name, edit);
				AbilityCustomAssets.RefreshRuntime();
				AbilityEditorPanel.SetStatus("Cast FX duration is now " + parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s.");
			}, "envelope.CastFX.Duration");
			AddFloatRow(host, "Pixels per unit (higher = smaller):", edit.pixelsPerUnit > 0f ? edit.pixelsPerUnit : 100f, parsed =>
			{
				edit.pixelsPerUnit = parsed;
				AbilityCustomAssets.WriteFx(abilityFolder, name, edit);
				AbilityCustomAssets.RefreshRuntime();
				AbilityEditorPanel.SetStatus("Cast FX pixels/unit is now " + parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
			}, "envelope.CastFX.PixelsPerUnit");
		}

		internal static void OnCastFxIdEdited(AbilityFileModel model, string previous, string next, UiComboBox castFxField, Action refreshCatalogs, Action rebuildPanel)
		{
			if (model == null || string.Equals(previous, next, StringComparison.Ordinal))
			{
				return;
			}

			model.CastFXId = next ?? string.Empty;
			if (AbilityCustomAssets.Owns(model, "fx", previous) && !string.Equals(previous, next, StringComparison.Ordinal))
			{
				if (AbilityCustomAssets.TryDelete(model, "fx", previous, out string error))
				{
					AbilityEditorPanel.SetStatus("Cast FX is now " + DescribeFx(next) + ". Deleted custom sprite folder \"" + previous + "\".");
				}
				else
				{
					AbilityEditorPanel.SetStatus(error);
				}

				refreshCatalogs();
			}
			else
			{
				AbilityEditorPanel.SetStatus("Cast FX is now " + DescribeFx(next) + ".");
			}

			rebuildPanel();
		}

		internal static void AddProjectileModelRow(UiStack box, ActionCard card, AbilityFileModel model, Action rebuild)
		{
			if (!card.Fields.ContainsKey("Model"))
			{
				card.Fields["Model"] = string.Empty;
			}

			string modelName = card.Fields["Model"];
			bool custom = AbilityCustomAssets.Owns(model, "projectiles", modelName);
			if (!custom)
			{
				UiComboBox combo = UiComboBox.Create(box.ContentTransform, AbilityCatalogLookups.ProjectileOptions(), modelName);
				combo.OnEndEdit(value =>
				{
					OnProjectileModelEdited(card, model, modelName, value, rebuild);
				});
				box.Add(AbilityEditorForm.LabeledRow(box.ContentTransform, "Model:", combo, hoverKey: "field.TrackingProjectile.Model", currentValue: () => combo.InputField.text));
			}
			box.Add(UiLabel.Create(box.ContentTransform, custom
				? "Projectile sprite: " + modelName + " (custom PNG)"
				: (string.IsNullOrEmpty(modelName)
					? "Projectile sprite: none"
					: "Projectile: base-game " + modelName)).FixedHeight(18f));

			UiStack buttons = UiStack.Horizontal(box.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			box.Add(buttons.FixedHeight(28f));
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Choose sprite…", () =>
			{
				PickProjectile(card, model, rebuild);
			}, primary: false).Grow());
			if (custom)
			{
				buttons.Add(UiButton.Create(buttons.ContentTransform, "Clear sprite", () =>
				{
					ClearProjectile(card, model, modelName, rebuild);
				}, primary: false).FixedWidth(110f));
			}

			if (!custom)
			{
				return;
			}

			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			AbilityCustomAssets.ProjectileEdit edit = AbilityCustomAssets.ReadProjectile(abilityFolder, modelName);
			AddFloatRow(box, "Pixels per unit (higher = smaller):", edit.pixelsPerUnit > 0f ? edit.pixelsPerUnit : 100f, parsed =>
			{
				edit.pixelsPerUnit = parsed;
				AbilityCustomAssets.WriteProjectile(abilityFolder, modelName, edit);
				AbilityCustomAssets.RefreshRuntime();
				AbilityEditorPanel.SetStatus("Projectile pixels/unit is now " + parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
			}, "field.TrackingProjectile.PixelsPerUnit");
		}

		private static void PickCastFx(AbilityFileModel model, UiComboBox castFxField, Action refreshCatalogs, Action rebuildPanel)
		{
			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			if (string.IsNullOrEmpty(abilityFolder))
			{
				AbilityEditorPanel.SetStatus("Save the ability once, then choose a Cast FX sprite.");
				return;
			}

			string previous = model.CastFXId ?? string.Empty;
			AbilityFilePicker.PickPng("Cast FX sprite", abilityFolder, path =>
			{
				if (!AbilityCustomAssets.TryCreateFxFromSprite(model, string.Empty, path, out string created, out string error))
				{
					AbilityEditorPanel.SetStatus(error);
					return;
				}

				RememberRestoreFx(abilityFolder, previous, created);

				if (!string.Equals(previous, created, StringComparison.Ordinal) && AbilityCustomAssets.Owns(model, "fx", previous))
				{
					AbilityCustomAssets.TryDelete(model, "fx", previous, out _);
				}

				model.CastFXId = created;
				if (castFxField != null)
				{
					refreshCatalogs();
					castFxField.SetText(created);
				}

				rebuildPanel();
				AbilityEditorPanel.SetStatus("Cast FX now uses your sprite \"" + created + "\""
					+ (AbilityCatalogLookups.IsVanillaFxMega(previous) ? " (was " + previous + ")." : ".")
					+ " Save to use it in combat.");
			});
		}

		private static void ClearCastFx(AbilityFileModel model, string name, UiComboBox castFxField, Action refreshCatalogs, Action rebuildPanel)
		{
			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			string restore = AbilityCustomAssets.ReadRestoreFxId(abilityFolder, name);
			if (!AbilityCustomAssets.TryDelete(model, "fx", name, out string error))
			{
				AbilityEditorPanel.SetStatus(error);
				return;
			}

			model.CastFXId = restore ?? string.Empty;
			if (castFxField != null)
			{
				refreshCatalogs();
				castFxField.SetText(model.CastFXId);
			}

			rebuildPanel();
			AbilityEditorPanel.SetStatus(string.IsNullOrEmpty(restore)
				? "Cleared Cast FX and deleted the custom sprite folder \"" + name + "\"."
				: "Cleared custom Cast FX sprite \"" + name + "\" and restored " + restore + ".");
		}

		private static void PickProjectile(ActionCard card, AbilityFileModel model, Action rebuild)
		{
			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			if (string.IsNullOrEmpty(abilityFolder))
			{
				AbilityEditorPanel.SetStatus("Save the ability once, then choose a projectile sprite.");
				return;
			}

			string previous = card.Fields.TryGetValue("Model", out string existing) ? existing : string.Empty;
			AbilityFilePicker.PickPng("Projectile sprite", abilityFolder, path =>
			{
				if (!AbilityCustomAssets.TryCreateProjectileFromSprite(model, string.Empty, path, out string created, out string error))
				{
					AbilityEditorPanel.SetStatus(error);
					return;
				}

				RememberRestoreModel(abilityFolder, previous, created);

				if (!string.Equals(previous, created, StringComparison.Ordinal) && AbilityCustomAssets.Owns(model, "projectiles", previous))
				{
					AbilityCustomAssets.TryDelete(model, "projectiles", previous, out _);
				}

				card.Fields["Model"] = created;
				rebuild();
				AbilityEditorPanel.SetStatus("This Tracking Projectile now uses your sprite \"" + created + "\""
					+ (AbilityCatalogLookups.IsVanillaProjectile(previous) ? " (was " + previous + ")." : ".")
					+ " Save to use it in combat.");
			});
		}

		private static void ClearProjectile(ActionCard card, AbilityFileModel model, string name, Action rebuild)
		{
			string abilityFolder = AbilityCustomAssets.AbilityFolder(model);
			string restore = AbilityCustomAssets.ReadRestoreModel(abilityFolder, name);
			if (string.IsNullOrEmpty(restore))
			{
				restore = "SimpleArrowProjectile";
			}

			if (!AbilityCustomAssets.TryDelete(model, "projectiles", name, out string error))
			{
				AbilityEditorPanel.SetStatus(error);
				return;
			}

			card.Fields["Model"] = restore;
			rebuild();
			AbilityEditorPanel.SetStatus("Cleared custom projectile sprite \"" + name + "\" and restored " + restore + ".");
		}

		private static void OnProjectileModelEdited(ActionCard card, AbilityFileModel model, string previous, string next, Action rebuild)
		{
			if (string.Equals(previous, next, StringComparison.Ordinal))
			{
				return;
			}

			card.Fields["Model"] = next ?? string.Empty;
			if (AbilityCustomAssets.Owns(model, "projectiles", previous) && !string.Equals(previous, next, StringComparison.Ordinal))
			{
				if (AbilityCustomAssets.TryDelete(model, "projectiles", previous, out string error))
				{
					AbilityEditorPanel.SetStatus("Projectile Model is now " + DescribeProjectile(next) + ". Deleted custom sprite folder \"" + previous + "\".");
				}
				else
				{
					AbilityEditorPanel.SetStatus(error);
				}
			}
			else
			{
				AbilityEditorPanel.SetStatus("Projectile Model is now " + DescribeProjectile(next) + ".");
			}

			rebuild();
		}

		private static void RememberRestoreFx(string abilityFolder, string previous, string created)
		{
			string restore = AbilityCatalogLookups.IsVanillaFxMega(previous)
				? previous
				: AbilityCustomAssets.ReadRestoreFxId(abilityFolder, previous);
			if (!string.IsNullOrEmpty(restore))
			{
				AbilityCustomAssets.WriteRestoreFxId(abilityFolder, created, restore);
			}
		}

		private static void RememberRestoreModel(string abilityFolder, string previous, string created)
		{
			string restore = AbilityCatalogLookups.IsVanillaProjectile(previous)
				? previous
				: AbilityCustomAssets.ReadRestoreModel(abilityFolder, previous);
			if (!string.IsNullOrEmpty(restore))
			{
				AbilityCustomAssets.WriteRestoreModel(abilityFolder, created, restore);
			}
		}

		private static void AddFloatRow(UiStack host, string label, float value, Action<float> onParsed, string hoverKey = null)
		{
			UiTextField field = UiTextField.Create(host.ContentTransform, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
			field.OnEndEdit(text =>
			{
				if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) && parsed > 0f)
				{
					onParsed(parsed);
				}
			});
			host.Add(AbilityEditorForm.LabeledRow(host.ContentTransform, label, field, hoverKey: hoverKey));
		}

		private static string DescribeFx(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "empty (no cast FX)";
			}

			return AbilityCatalogLookups.IsVanillaFxMega(name) ? "base-game " + name : name;
		}

		private static string DescribeProjectile(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "empty";
			}

			return AbilityCatalogLookups.IsVanillaProjectile(name) ? "base-game " + name : name;
		}
	}
}
