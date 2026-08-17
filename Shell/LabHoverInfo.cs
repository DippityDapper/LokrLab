using System;
using LokrAbilityLab;
using LokrAbilityLab.Editor;
using SimpleUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LokrLab.Shell
{
	/// <summary>Shared Lab hover-info strip above the status bar. Ability, Character, and Encounter fields bind the same chrome.</summary>
	internal static class LabHoverInfo
	{
		private static UiLabel titleLabel;
		private static UiLabel bodyLabel;
		private static string activeKey;

		/// <summary>Builds the hover strip into the shell column. Reloads sidecar copy from disk.</summary>
		internal static UiPanel Build(Transform parent, UiTheme theme)
		{
			theme = theme ?? UiTheme.Default;
			ReloadCopy();

			UiPanel panel = UiPanel.Create(parent, theme);
			UiStack column = UiStack.Vertical(panel.ContentParent, theme, spacing: 2f, padding: 6f);
			panel.Add(column);
			titleLabel = UiLabel.Create(column.ContentTransform, "Hover a field for a short description.", theme, theme.BodyFontSize, TextAnchor.MiddleLeft);
			column.Add(titleLabel.FixedHeight(18f));
			bodyLabel = UiLabel.Create(column.ContentTransform, "Sidecars: ability-hover.md, character-hover.md, encounter-hover.md (plugin or Mods/LokrLab overlay).", theme, Math.Max(12, theme.BodyFontSize - 2), TextAnchor.UpperLeft);
			column.Add(bodyLabel.Grow());
			activeKey = null;
			return panel;
		}

		/// <summary>Drops widget refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			titleLabel = null;
			bodyLabel = null;
			activeKey = null;
		}

		/// <summary>Attaches pointer enter/exit so hovering <paramref name="go"/> fills the info strip.</summary>
		internal static void Bind(GameObject go, string key, Func<string> currentValue = null)
		{
			if (go == null || string.IsNullOrEmpty(key))
			{
				return;
			}

			Graphic graphic = go.GetComponent<Graphic>();
			if (graphic == null)
			{
				Image image = go.AddComponent<Image>();
				image.color = new Color(0f, 0f, 0f, 0f);
				image.raycastTarget = true;
			}
			else
			{
				graphic.raycastTarget = true;
			}

			LabHoverTarget target = go.GetComponent<LabHoverTarget>();
			if (target == null)
			{
				target = go.AddComponent<LabHoverTarget>();
			}

			target.Configure(key, currentValue);
		}

		/// <summary>Shows copy for <paramref name="key"/>, plus token copy when <paramref name="currentValue"/> is a % or # token.</summary>
		internal static void Show(string key, string currentValue)
		{
			if (titleLabel == null || bodyLabel == null)
			{
				return;
			}

			activeKey = key;
			string title;
			string body = AbilityHoverCopy.Format(key, currentValue, out title);
			titleLabel.SetText(title ?? string.Empty);
			bodyLabel.SetText(body ?? string.Empty);
		}

		/// <summary>Clears the strip when the pointer leaves the same key that is currently shown.</summary>
		internal static void ClearIf(string key)
		{
			if (activeKey != key || titleLabel == null || bodyLabel == null)
			{
				return;
			}

			activeKey = null;
			titleLabel.SetText("Hover a field for a short description.");
			bodyLabel.SetText("Sidecars: ability-hover.md, character-hover.md, encounter-hover.md (plugin or Mods/LokrLab overlay).");
		}

		private static void ReloadCopy()
		{
			string pluginDir = PathDir(typeof(LokrLabPlugin).Assembly.Location);
			string pluginAbility = string.IsNullOrEmpty(pluginDir)
				? null
				: System.IO.Path.Combine(pluginDir, "Sidecars", "ability-hover.md");
			string pluginCharacter = string.IsNullOrEmpty(pluginDir)
				? null
				: System.IO.Path.Combine(pluginDir, "Sidecars", "character-hover.md");
			string pluginEncounter = string.IsNullOrEmpty(pluginDir)
				? null
				: System.IO.Path.Combine(pluginDir, "Sidecars", "encounter-hover.md");
			string overlayRoot = AbilityLabPaths.SuiteRoot;
			AbilityHoverCopy.Reload(
				new[] { pluginAbility, pluginCharacter, pluginEncounter },
				new[]
				{
					System.IO.Path.Combine(overlayRoot, "ability-hover.md"),
					System.IO.Path.Combine(overlayRoot, "character-hover.md"),
					System.IO.Path.Combine(overlayRoot, "encounter-hover.md"),
				});
		}

		private static string PathDir(string location)
		{
			return string.IsNullOrEmpty(location) ? null : System.IO.Path.GetDirectoryName(location);
		}

		/// <summary>Pointer-enter binder used by <see cref="Bind"/>.</summary>
		internal sealed class LabHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			private string key;
			private Func<string> currentValue;

			/// <summary>Stores the hover key and optional current-value getter.</summary>
			internal void Configure(string hoverKey, Func<string> value)
			{
				key = hoverKey;
				currentValue = value;
			}

			/// <summary>Fills the hover strip for this key.</summary>
			public void OnPointerEnter(PointerEventData eventData)
			{
				Show(key, currentValue != null ? currentValue() : null);
			}

			/// <summary>Clears the hover strip when this key is still the active one.</summary>
			public void OnPointerExit(PointerEventData eventData)
			{
				ClearIf(key);
			}
		}
	}
}
