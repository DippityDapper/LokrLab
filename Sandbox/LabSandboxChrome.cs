using System;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab
{
	/// <summary>Shared Sandbox hole and Start / Stop / Level 1-3 toolbar for Character, Ability, and Encounter.</summary>
	/// <remarks>
	/// One workspace name and button copy everywhere. Each lab wires Start; this type only
	/// builds the chrome and the embed start/stop delegates.
	/// </remarks>
	internal sealed class LabSandboxChrome
	{
		/// <summary>Workspace tab name used by Character, Ability, and Encounter.</summary>
		internal const string WorkspaceName = "Sandbox";

		/// <summary>Highest rank the Sandbox Level dropdown offers (always 1 through this).</summary>
		internal const int MaxLevel = 3;

		/// <summary>The vertical stack that fills the viewport.</summary>
		internal UiStack Host { get; private set; }

		/// <summary>Fight hole <c>RectTransform</c> passed to <c>StartEmbeddedFight</c>.</summary>
		internal RectTransform Hole { get; private set; }

		/// <summary>Level dropdown when the lab shows one; otherwise null.</summary>
		internal UiDropdown LevelDropdown { get; private set; }

		/// <summary>1-based rank from the Level dropdown (1 through <see cref="MaxLevel"/>).</summary>
		internal int SelectedLevel
		{
			get
			{
				if (LevelDropdown == null || LevelDropdown.Dropdown == null)
				{
					return 1;
				}

				int level = LevelDropdown.Dropdown.value + 1;
				if (level < 1)
				{
					return 1;
				}

				if (level > MaxLevel)
				{
					return MaxLevel;
				}

				return level;
			}
		}

		/// <summary>Builds the Sandbox toolbar and hole. Optional Load Encounter sits after Stop.</summary>
		internal static LabSandboxChrome Build(
			Transform parent,
			Action onStart,
			Action onStop,
			bool showLevel,
			Action onLoadEncounter)
		{
			LabSandboxChrome chrome = new LabSandboxChrome();
			chrome.Host = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 8f);
			Stretch(chrome.Host);
			ClearImage(chrome.Host.GameObject);

			UiStack toolbar = UiStack.Horizontal(chrome.Host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			chrome.Host.Add(toolbar.FixedHeight(28f));
			if (showLevel)
			{
				chrome.LevelDropdown = UiDropdown.Create(
					toolbar.ContentTransform,
					new[] { "Level 1", "Level 2", "Level 3" },
					UiTheme.Default);
				toolbar.Add(chrome.LevelDropdown.FixedWidth(110f));
				LabHoverInfo.Bind(chrome.LevelDropdown.GameObject, "sandbox.Level");
			}

			UiButton start = UiButton.Create(toolbar.ContentTransform, "Start sandbox", () => onStart(), primary: true);
			toolbar.Add(start.FixedWidth(130f));
			LabHoverInfo.Bind(start.GameObject, "sandbox.Start");
			UiButton stop = UiButton.Create(toolbar.ContentTransform, "Stop sandbox", () => onStop(), primary: false);
			toolbar.Add(stop.FixedWidth(130f));
			LabHoverInfo.Bind(stop.GameObject, "sandbox.Stop");
			if (onLoadEncounter != null)
			{
				UiButton load = UiButton.Create(toolbar.ContentTransform, "Load Encounter", () => onLoadEncounter(), primary: false);
				toolbar.Add(load.FixedWidth(140f));
				LabHoverInfo.Bind(load.GameObject, "sandbox.LoadEncounter");
			}

			UiPanel hole = UiPanel.Create(chrome.Host.ContentTransform, UiTheme.Default);
			hole.Name("SandboxHole");
			ClearImage(hole.GameObject);
			LayoutElement holeLayout = hole.GameObject.GetComponent<LayoutElement>();
			if (holeLayout == null)
			{
				holeLayout = hole.GameObject.AddComponent<LayoutElement>();
			}

			holeLayout.minHeight = 64f;
			chrome.Host.Add(hole.Grow());
			chrome.Hole = hole.RectTransform;
			ReleaseFit(chrome.Host);
			return chrome;
		}

		/// <summary>Stops the current embed if one is running.</summary>
		internal static void StopFight()
		{
			Action stop = LokrLabApi.LokrLabApi.StopEmbeddedFight;
			LabHost hostApi = LokrLabApi.LokrLabApi.Host;
			if (stop == null && hostApi != null)
			{
				stop = hostApi.StopEmbeddedFight;
			}

			Func<bool> active = LokrLabApi.LokrLabApi.IsEmbeddedFightActive;
			if (active == null && hostApi != null)
			{
				active = hostApi.IsEmbeddedFightActive;
			}

			if (active != null && active())
			{
				stop?.Invoke();
			}
		}

		/// <summary>The host's StartEmbeddedFight delegate, or null when Character Lab has not bound it.</summary>
		internal static Func<EmbeddedFightRequest, string> ResolveStart()
		{
			Func<EmbeddedFightRequest, string> start = LokrLabApi.LokrLabApi.StartEmbeddedFight;
			LabHost hostApi = LokrLabApi.LokrLabApi.Host;
			if (start == null && hostApi != null)
			{
				start = hostApi.StartEmbeddedFight;
			}

			return start;
		}

		private static void ReleaseFit(UiStack stack)
		{
			if (stack == null || stack.GameObject == null)
			{
				return;
			}

			ContentSizeFitter fitter = stack.GameObject.GetComponent<ContentSizeFitter>();
			if (fitter != null)
			{
				fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
				fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
			}

			ContentSizeFitter contentFit = stack.ContentTransform != null
				? stack.ContentTransform.GetComponent<ContentSizeFitter>()
				: null;
			if (contentFit != null && contentFit != fitter)
			{
				contentFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
				contentFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
			}
		}

		private static void Stretch(UiStack stack)
		{
			RectTransform rect = stack.RectTransform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}

		private static void ClearImage(GameObject gameObject)
		{
			if (gameObject == null)
			{
				return;
			}

			Image image = gameObject.GetComponent<Image>();
			if (image != null)
			{
				image.color = Color.clear;
				image.raycastTarget = false;
			}
		}
	}
}
