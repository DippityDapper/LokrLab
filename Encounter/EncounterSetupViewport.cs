using System;
using Ironhide.Legends.Model.Game;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab.Encounter
{
	/// <summary>Setup workspace: auto-loads the template board with an in-hole loading notice, Restart, Place, Draw Hex, Paint Terrain, and Camera bounds.</summary>
	internal static class EncounterSetupViewport
	{
		/// <summary>Workspace tab name.</summary>
		internal const string WorkspaceName = "Setup";

		private static UiStack host;
		private static RectTransform setupSlot;
		private static UiLabel hintLabel;
		private static UiLabel templateLabel;
		private static UiButton placeButton;
		private static UiButton drawHexButton;
		private static UiButton paintTerrainButton;
		private static UiButton triggerButton;
		private static UiButton cameraButton;
		private static UiButton gridButton;
		private static UiPanel loadingOverlay;
		private static UiLabel loadingLabel;
		private static bool boardWanted;
		private static bool loading;
		private static int showSerial;

		/// <summary>Builds the Setup toolbar and hole into the shell viewport.</summary>
		internal static GameObject Build(Transform parent)
		{
			host = UiStack.Vertical(parent, UiTheme.Default, spacing: 6f, padding: 8f);
			Stretch(host);
			ClearImage(host.GameObject);

			UiStack toolbar = UiStack.Horizontal(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			host.Add(toolbar.FixedHeight(28f));
			UiButton restart = UiButton.Create(toolbar.ContentTransform, "Restart board", RestartBoard, primary: true);
			toolbar.Add(restart.FixedWidth(120f));
			LabHoverInfo.Bind(restart.GameObject, "encounter.setup.Restart");
			gridButton = UiButton.Create(toolbar.ContentTransform, "Grid", ToggleGrid, primary: false);
			toolbar.Add(gridButton.FixedWidth(70f));
			LabHoverInfo.Bind(gridButton.GameObject, "encounter.setup.Grid");

			UiStack tools = UiStack.Horizontal(host.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			host.Add(tools.FixedHeight(28f));
			placeButton = UiButton.Create(tools.ContentTransform, "Edit Unit", () => SetTool(EncounterEditTool.Place), primary: true);
			tools.Add(placeButton.FixedWidth(90f));
			LabHoverInfo.Bind(placeButton.GameObject, "encounter.setup.EditUnit");
			drawHexButton = UiButton.Create(tools.ContentTransform, "Draw Hex", () => SetTool(EncounterEditTool.DrawHex), primary: false);
			tools.Add(drawHexButton.FixedWidth(90f));
			LabHoverInfo.Bind(drawHexButton.GameObject, "encounter.setup.DrawHex");
			paintTerrainButton = UiButton.Create(tools.ContentTransform, "Paint Terrain", () => SetTool(EncounterEditTool.PaintTerrain), primary: false);
			tools.Add(paintTerrainButton.FixedWidth(120f));
			LabHoverInfo.Bind(paintTerrainButton.GameObject, "encounter.setup.PaintTerrain");
			triggerButton = UiButton.Create(tools.ContentTransform, "Trigger", () => SetTool(EncounterEditTool.Trigger), primary: false);
			tools.Add(triggerButton.FixedWidth(90f));
			LabHoverInfo.Bind(triggerButton.GameObject, "encounter.setup.Trigger");
			cameraButton = UiButton.Create(tools.ContentTransform, "Camera", () => SetTool(EncounterEditTool.Camera), primary: false);
			tools.Add(cameraButton.FixedWidth(80f));
			LabHoverInfo.Bind(cameraButton.GameObject, "encounter.setup.Camera");
			RefreshToolButtons();

			hintLabel = UiLabel.Create(host.ContentTransform, HintText(), UiTheme.Default, 12);
			host.Add(hintLabel.FixedHeight(20f));
			templateLabel = UiLabel.Create(host.ContentTransform, TemplateText(), UiTheme.Default, 12);
			host.Add(templateLabel.FixedHeight(20f));

			UiPanel hole = UiPanel.Create(host.ContentTransform, UiTheme.Default);
			hole.Name("EncounterSetupHole");
			ClearImage(hole.GameObject);
			LayoutElement holeLayout = hole.GameObject.GetComponent<LayoutElement>();
			if (holeLayout == null)
			{
				holeLayout = hole.GameObject.AddComponent<LayoutElement>();
			}

			holeLayout.minHeight = 64f;
			host.Add(hole.Grow());
			setupSlot = hole.RectTransform;
			BuildLoadingOverlay(hole.RectTransform);
			ReleaseFit(host);
			return host.GameObject;
		}

		/// <summary>Starts a deferred Show board so the Setup chrome paints before the embed hitch.</summary>
		/// <remarks>
		/// <c>StartEmbeddedFight</c> still runs on the main thread. Waiting two
		/// frames lets the tab switch complete first. Hide / leaving Setup
		/// cancels a pending load.
		/// </remarks>
		internal static void OnActivated()
		{
			ScheduleShowBoard();
		}

		/// <summary>Stops the edit embed when leaving Setup so Play, Sandbox, and Stage stay 1v1.</summary>
		internal static void OnDeactivated()
		{
			CancelScheduledShow();
			HideBoard();
			EncounterEdit.Tool = EncounterEditTool.Place;
			host = null;
			setupSlot = null;
			hintLabel = null;
			templateLabel = null;
			placeButton = null;
			drawHexButton = null;
			paintTerrainButton = null;
			cameraButton = null;
			gridButton = null;
			loadingOverlay = null;
			loadingLabel = null;
			loading = false;
			EncounterEdit.GridVisible = true;
		}

		/// <summary>True when this screen point is inside the Setup hole RectTransform.</summary>
		/// <remarks>
		/// Used while the fight camera is not up yet so a catalogue drop during
		/// auto-load still counts as over the hole.
		/// </remarks>
		internal static bool HoleContainsScreen(Vector2 screen)
		{
			if (setupSlot == null)
			{
				return false;
			}

			Canvas canvas = setupSlot.GetComponentInParent<Canvas>();
			Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
				? canvas.worldCamera
				: null;
			return RectTransformUtility.RectangleContainsScreenPoint(setupSlot, screen, uiCamera);
		}

		/// <summary>Hides the in-hole loading notice once the preview roster and overlays exist.</summary>
		internal static void NotifyBoardReady()
		{
			HideViewportNotice();
			Refresh();
		}

		/// <summary>Updates the hint and template lines without destroying a live hole.</summary>
		internal static void Refresh()
		{
			RefreshToolButtons();
			if (hintLabel != null)
			{
				hintLabel.SetText(loading ? "Loading the Setup board…" : HintText());
			}

			if (templateLabel != null)
			{
				templateLabel.SetText(TemplateText());
			}
		}

		/// <summary>Reloads the preview roster when combatants change while the board is showing.</summary>
		internal static void RestartPreviewIfShowing()
		{
			if (!boardWanted)
			{
				return;
			}

			ShowBoard(forceReload: true);
		}

		/// <summary>Unloads and reloads the preview after a template change.</summary>
		internal static void NotifyTemplateChanged()
		{
			if (!boardWanted && !EncounterEdit.IsArmed)
			{
				Refresh();
				return;
			}

			HideBoard();
			Refresh();
			ScheduleShowBoard();
			LokrLab.Lab.SetStatus("Template changed — reloading board.");
		}

		private static void RestartBoard()
		{
			CancelScheduledShow();
			ShowBoard(forceReload: true);
		}

		private static void ScheduleShowBoard()
		{
			if (host == null || host.GameObject == null)
			{
				return;
			}

			showSerial++;
			int mine = showSerial;
			LoadPump pump = host.GameObject.GetComponent<LoadPump>();
			if (pump == null)
			{
				pump = host.GameObject.AddComponent<LoadPump>();
			}

			pump.Arm(mine);
			ShowViewportNotice("Loading board…", pulsing: true);
			LokrLab.Lab.SetStatus("Loading board…");
		}

		private static void CancelScheduledShow()
		{
			showSerial++;
		}

		/// <summary>Runs the deferred Show when this serial is still current.</summary>
		internal static void TryRunScheduledShow(int serial)
		{
			if (serial != showSerial)
			{
				return;
			}

			ShowBoard(forceReload: false);
		}

		private static void ShowBoard(bool forceReload)
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			if (session == null || session.File == null)
			{
				ShowViewportNotice("Open an Encounter project.", pulsing: false);
				LokrLab.Lab.SetStatus("Open an Encounter project.");
				return;
			}

			if (setupSlot == null)
			{
				ShowViewportNotice("Setup hole is not ready.", pulsing: false);
				LokrLab.Lab.SetStatus("Setup hole is not ready.");
				return;
			}

			LabHost hostApi = LokrLabApi.LokrLabApi.Host;
			Func<EmbeddedFightRequest, string> start = LokrLabApi.LokrLabApi.StartEmbeddedFight;
			if (start == null && hostApi != null)
			{
				start = hostApi.StartEmbeddedFight;
			}

			if (start == null)
			{
				ShowViewportNotice("StartEmbeddedFight is not assigned.", pulsing: false);
				LokrLab.Lab.SetStatus("StartEmbeddedFight is not assigned.");
				return;
			}

			if (!forceReload && boardWanted && EncounterEdit.IsArmed && Stage.instance != null)
			{
				HideViewportNotice();
				Refresh();
				return;
			}

			EncounterCombatantModel firstGood = EncounterPlayRules.FirstGoodSide(session.File);
			string casterId = firstGood != null ? EncounterPlayRules.UnitId(firstGood) : null;
			if (string.IsNullOrEmpty(casterId))
			{
				casterId = SandboxRoster.DefaultEnemyUnitId;
			}

			StopEmbed();
			boardWanted = true;
			EncounterEdit.Arm(session.File);
			ShowViewportNotice("Loading board…", pulsing: true);
			LokrLabPlugin.Log.LogInfo(
				"Encounter Setup: showing '" + session.DisplayName + "' on " + EncounterEdit.Template
				+ " with caster '" + casterId + "'.");
			string error = start(new EmbeddedFightRequest
			{
				CasterUnitId = casterId,
				CasterLevel = firstGood != null ? firstGood.Level : 1,
				Hole = setupSlot,
				OnFailed = message =>
				{
					boardWanted = false;
					EncounterEdit.Disarm();
					ShowViewportNotice(message, pulsing: false);
					LokrLab.Lab.SetStatus(message);
				},
				OnEnded = () =>
				{
					boardWanted = false;
					EncounterEdit.Disarm();
				},
			});
			if (error != null)
			{
				boardWanted = false;
				EncounterEdit.Disarm();
				ShowViewportNotice(error, pulsing: false);
				LokrLab.Lab.SetStatus(error);
			}
		}

		private static void HideBoard()
		{
			CancelScheduledShow();
			boardWanted = false;
			StopEmbed();
			EncounterEdit.Disarm();
			HideViewportNotice();
		}

		private static void StopEmbed()
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

			EncounterEdit.Disarm();
		}

		private static void SetTool(EncounterEditTool tool)
		{
			EncounterEdit.Tool = tool;
			RefreshToolButtons();
			Refresh();
			EncounterCameraGizmo.Refresh();
			EncounterEdit.ShowFullGrid();
		}

		private static void RefreshToolButtons()
		{
			PaintToolButton(placeButton, EncounterEdit.Tool == EncounterEditTool.Place);
			PaintToolButton(drawHexButton, EncounterEdit.Tool == EncounterEditTool.DrawHex);
			PaintToolButton(paintTerrainButton, EncounterEdit.Tool == EncounterEditTool.PaintTerrain);
			PaintToolButton(triggerButton, EncounterEdit.Tool == EncounterEditTool.Trigger);
			PaintToolButton(cameraButton, EncounterEdit.Tool == EncounterEditTool.Camera);
			PaintToolButton(gridButton, EncounterEdit.GridVisible);
		}

		private static void ToggleGrid()
		{
			EncounterEdit.ToggleGrid();
			Refresh();
		}

		private static void PaintToolButton(UiButton button, bool selected)
		{
			if (button == null)
			{
				return;
			}

			button.SetColor(selected ? UiTheme.Default.ButtonColor : UiTheme.Default.RowButtonColor);
		}

		private static string HintText()
		{
			if (EncounterEdit.Tool == EncounterEditTool.DrawHex)
			{
				return "Left-drag to draw walkable hexes. Right-drag to block. Click past the edge (left) to grow toward the cursor.";
			}

			if (EncounterEdit.Tool == EncounterEditTool.PaintTerrain)
			{
				return BlankFloor()
					? "Select a terrain in the Node Tree, then left-drag. Right-drag restores the blank floor."
					: "Select a terrain in the Node Tree, then left-drag. Right-drag restores the template.";
			}

			if (EncounterEdit.Tool == EncounterEditTool.Trigger)
			{
				return string.IsNullOrEmpty(EncounterEdit.SelectedTriggerId)
					? "Select or add a trigger in the Node Tree, then left-drag to paint it. Right-drag erases."
					: "Left-drag to paint '" + EncounterEdit.SelectedTriggerId + "'. Right-drag erases. A pocket member using this trigger wakes when a GoodSide unit steps onto a painted hex.";
			}

			if (EncounterEdit.Tool == EncounterEditTool.Camera)
			{
				return "Drag a rectangle for Play camera bounds. Drag a handle to resize, inside to move. Play locks zoom to this frame.";
			}

			return "Drag a Combatants or Props card onto a hex, or select one and tap. The selected unit or prop uses the blue current-turn hex.";
		}

		private static bool BlankFloor()
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			return session != null && session.File != null && !session.File.TilesDefault;
		}

		private static string TemplateText()
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			if (session == null || session.File == null)
			{
				return "Open an Encounter project.";
			}

			EncounterGrowRules.EffectiveLiveSize(session.File, out int width, out int height);
			return "Template: " + EncounterTemplateRules.Label(session.File.Template)
				+ " · " + width + "×" + height;
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

		private static void BuildLoadingOverlay(Transform hole)
		{
			loadingOverlay = UiPanel.Create(hole, UiTheme.Default);
			loadingOverlay.Name("EncounterSetupLoading");
			Image background = loadingOverlay.GameObject.GetComponent<Image>();
			if (background != null)
			{
				background.color = new Color(0.07f, 0.08f, 0.10f, 0.92f);
				background.raycastTarget = false;
			}

			loadingLabel = UiLabel.Create(
				loadingOverlay.ContentParent,
				"Loading board…",
				UiTheme.Default,
				16,
				TextAnchor.MiddleCenter);
			RectTransform labelRect = loadingLabel.RectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = Vector2.zero;
			labelRect.offsetMax = Vector2.zero;
			loadingOverlay.Add(loadingLabel);
			LoadingPulse pulse = loadingOverlay.GameObject.GetComponent<LoadingPulse>();
			if (pulse == null)
			{
				pulse = loadingOverlay.GameObject.AddComponent<LoadingPulse>();
			}

			pulse.Configure(loadingLabel);
			ShowViewportNotice("Loading board…", pulsing: true);
		}

		private static void ShowViewportNotice(string message, bool pulsing)
		{
			loading = pulsing;
			if (loadingLabel != null)
			{
				loadingLabel.SetText(message ?? string.Empty);
			}

			if (loadingOverlay != null)
			{
				loadingOverlay.Visible(true);
				LoadingPulse pulse = loadingOverlay.GameObject.GetComponent<LoadingPulse>();
				if (pulse != null)
				{
					pulse.SetPulsing(pulsing);
				}
			}

			if (hintLabel != null)
			{
				hintLabel.SetText(pulsing ? "Loading the Setup board…" : HintText());
			}
		}

		private static void HideViewportNotice()
		{
			loading = false;
			if (loadingOverlay != null)
			{
				loadingOverlay.Visible(false);
				LoadingPulse pulse = loadingOverlay.GameObject.GetComponent<LoadingPulse>();
				if (pulse != null)
				{
					pulse.SetPulsing(false);
				}
			}

			if (hintLabel != null)
			{
				hintLabel.SetText(HintText());
			}
		}

		/// <summary>Cycles trailing dots on the in-hole loading label.</summary>
		private sealed class LoadingPulse : MonoBehaviour
		{
			private UiLabel label;
			private bool pulsing;
			private string stem = "Loading board";

			/// <summary>Binds the label this pulse writes.</summary>
			internal void Configure(UiLabel target)
			{
				label = target;
			}

			/// <summary>Starts or stops the trailing-dot animation.</summary>
			internal void SetPulsing(bool on)
			{
				pulsing = on;
				enabled = on;
				if (on && label != null && !string.IsNullOrEmpty(label.Text.text))
				{
					string text = label.Text.text.TrimEnd('.');
					stem = string.IsNullOrEmpty(text) ? "Loading board" : text;
				}
			}

			private void Update()
			{
				if (!pulsing || label == null)
				{
					return;
				}

				int dots = 1 + ((int)(Time.unscaledTime * 3f) % 3);
				label.SetText(stem + new string('.', dots));
			}
		}

		/// <summary>Waits two frames after Setup activates, then starts Show board.</summary>
		private sealed class LoadPump : MonoBehaviour
		{
			private int serial;
			private int wait;

			/// <summary>Arms this pump for the given Show serial.</summary>
			internal void Arm(int next)
			{
				serial = next;
				wait = 2;
				enabled = true;
			}

			private void Update()
			{
				if (wait > 0)
				{
					wait--;
					return;
				}

				enabled = false;
				TryRunScheduledShow(serial);
			}
		}
	}
}
