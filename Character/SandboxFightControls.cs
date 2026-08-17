using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends;
using Ironhide.Legends.Controller.Game;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Battlechest.Client.View;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Hud;
using Lean.Touch;
using LokrCharacterLab;
using LokrLab.Encounter;
using LokrLabApi;
using LokrModAPI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LokrLab
{
	/// <summary>Take-over-AI, debug panel, and gameplay-camera unlock for embedded Sandbox and Stage fights.</summary>
	internal static class SandboxFightControls
	{
		/// <summary>Reads Stage.processingEndTurn so the handoff waits until EndTurnNow finishes.</summary>
		private static readonly AccessTools.FieldRef<Stage, bool> ProcessingEndTurn =
			AccessTools.FieldRefAccess<Stage, bool>("processingEndTurn");

		/// <summary>Increments so a newer AI-to-player handoff cancels an older retry loop.</summary>
		private static int handoffSerial;
		/// <summary>Call before <see cref="Stage.AddUnit"/> so new units are player-controlled.</summary>
		/// <remarks>
		/// Encounter Play keeps the debug spawn panel off. Sandbox still wants it.
		/// </remarks>
		internal static void Begin(bool enableDebugPanel = true)
		{
			Stage.TakeOverAICheat = true;
			CheatDebugController.DEBUG_PANEL_ENABLED = enableDebugPanel;
			UnlockCamera();
		}

		/// <summary>Call after spawn so already-placed combat units are player-controlled. Skips the turn marker (it has no skills bar).</summary>
		/// <remarks>
		/// Encounter Play leaves BadSide as AI and does not show the debug panel.
		/// </remarks>
		internal static void Finish(Stage stage, bool takeOverAll = true, bool showDebugPanel = true)
		{
			if (takeOverAll && stage != null && stage.units != null)
			{
				foreach (Unit unit in stage.units)
				{
					if (unit == null || IsHudExempt(unit))
					{
						continue;
					}

					unit.isAI = false;
				}
			}

			UnlockCamera();
			if (showDebugPanel)
			{
				DebugPanelController debugPanel = DebugPanelController.GetDebugPanel();
				if (debugPanel != null && debugPanel.rootContent != null)
				{
					debugPanel.rootContent.SetActive(true);
				}
			}

			RecoverTurnChrome();
			EnsureFightHud(stage);
			EnsureFightInput(stage);
			EmbeddedFightHost.RefitHole();
			if (MonoSingleton<StageControllerComponent>.IsInstanceValid)
			{
				MonoSingleton<StageControllerComponent>.Instance.StartCoroutine(EnsureFightHudLater(stage));
			}
		}

		/// <summary>Slides the initiative / skills bars back on after the fight scene hid them for cinematics.</summary>
		/// <remarks>
		/// The fight scene serializes <c>runCinematics</c> true, so <c>PowerBarsVisibility.Init</c>
		/// can HideHud before UnlockCamera runs. If Start already added a skills bar,
		/// only a missing initiative portrait is added — full AddUnitView would
		/// duplicate the SkillsBar dictionary key.
		/// </remarks>
		internal static void EnsureFightHud(Stage stage)
		{
			if (MonoSingleton<LevelManager>.IsInstanceValid)
			{
				MonoSingleton<LevelManager>.Instance.runCinematics = false;
			}

			RestoreHudObjects();
			if (MonoSingleton<PowerBarsVisibility>.IsInstanceValid)
			{
				PowerBarsVisibility hud = MonoSingleton<PowerBarsVisibility>.Instance;
				hud.hidingHUD = false;
				hud.ShowHud();
				Unit active = stage != null && stage.initiative != null ? stage.initiative.ActiveUnit : null;
				if (active != null && !IsHudExempt(active) && !active.isAI)
				{
					hud.ShowPowerBarButtons();
				}
			}

			if (stage == null || stage.units == null || InitiativeBar.initiativeInst == null
				|| !MonoSingleton<StageControllerComponent>.IsInstanceValid)
			{
				return;
			}

			StageControllerComponent controller = MonoSingleton<StageControllerComponent>.Instance;
			List<PortraitInitiative> portraits = InitiativeBar.initiativeInst.portraits;
			foreach (Unit unit in stage.units)
			{
				if (unit == null || unit.unitView == null || IsHudExempt(unit))
				{
					continue;
				}

				bool exists = false;
				if (portraits != null)
				{
					for (int i = 0; i < portraits.Count; i++)
					{
						if (portraits[i] != null && portraits[i].unit == unit.unitView)
						{
							exists = true;
							break;
						}
					}
				}

				if (!exists)
				{
					if (SkillsBar.InstSkill != null && SkillsBar.InstSkill.skillPerUnits != null
						&& SkillsBar.InstSkill.skillPerUnits.ContainsKey(unit.unitView))
					{
						InitiativeBar.initiativeInst.AddPortrait(unit.unitView);
					}
				}
			}
		}

		/// <summary>Retries HUD restore for a few frames until InitiativeBar and unit views exist.</summary>
		private static IEnumerator EnsureFightHudLater(Stage stage)
		{
			for (int i = 0; i < 8; i++)
			{
				yield return null;
				EnsureFightHud(stage);
			}

			EnsureFightInput(stage);
		}

		/// <summary>Retries HUD and walk hexes after initiative advances to a player unit.</summary>
		/// <remarks>
		/// <c>FightStartTurn</c> fires at the end of the current unit's
		/// <c>StartUserInteraction</c>, so after an AI turn it never runs for
		/// the player. Vanilla then depends on <c>LateUpdate</c>
		/// <c>CheckUserInteraction</c>, which no-ops while a cinematic flag is
		/// set, while <c>processingLogic</c> is stuck, or after it consumes
		/// <c>CanHandleInputOrAI</c> on a frame <c>TurnActionFinished</c> is
		/// still false. That leaves no walk overlay and no skills bar. Do not
		/// start interaction for AI from here — that re-opens the petition loop.
		/// </remarks>
		internal static void BeginPlayerTurnHandoff(Stage stage, Unit expected)
		{
			if (stage == null || expected == null || expected.isAI || IsHudExempt(expected))
			{
				return;
			}

			if (!EmbeddedFightHost.IsActive || EncounterEdit.IsArmed)
			{
				return;
			}

			if (!MonoSingleton<StageControllerComponent>.IsInstanceValid)
			{
				return;
			}

			MonoSingleton<StageControllerComponent>.Instance.StartCoroutine(HandoffPlayerTurn(stage, expected));
		}

		/// <summary>Waits for end-turn tweens to finish, then restores player chrome.</summary>
		private static IEnumerator HandoffPlayerTurn(Stage stage, Unit expected)
		{
			int serial = ++handoffSerial;
			const int maxFrames = 45;
			for (int i = 0; i < maxFrames; i++)
			{
				yield return null;
				if (serial != handoffSerial)
				{
					yield break;
				}

				if (!CanContinueHandoff(stage, expected))
				{
					yield break;
				}

				if (IsHandoffBlocked(stage))
				{
					continue;
				}

				RecoverPlayerTurn(stage, forceStart: false);
				yield break;
			}

			if (serial == handoffSerial && CanContinueHandoff(stage, expected))
			{
				RecoverPlayerTurn(stage, forceStart: true);
			}
		}

		/// <summary>True while the embed is still on the expected living player unit.</summary>
		private static bool CanContinueHandoff(Stage stage, Unit expected)
		{
			if (stage == null || expected == null || !EmbeddedFightHost.IsActive || EncounterEdit.IsArmed
				|| !stage.isFighting)
			{
				return false;
			}

			if (expected.isAI || IsHudExempt(expected)
				|| (expected.states != null && expected.states.IsOn("DEAD")))
			{
				return false;
			}

			Unit active = stage.initiative != null ? stage.initiative.ActiveUnit : null;
			return active == expected;
		}

		/// <summary>True while end-turn, AI logic, or the skills-bar hide tween is still running.</summary>
		private static bool IsHandoffBlocked(Stage stage)
		{
			if (stage.endTurnPetition || ProcessingEndTurn(stage))
			{
				return true;
			}

			if (stage.unitController != null && stage.unitController.IsProcessingLogic)
			{
				return true;
			}

			if (stage.turnActionController != null && !stage.turnActionController.TurnActionFinished)
			{
				return true;
			}

			return MonoSingleton<PowerBarsVisibility>.IsInstanceValid
				&& MonoSingleton<PowerBarsVisibility>.Instance.barTweening;
		}

		/// <summary>Restores HUD and starts player interaction if vanilla never did.</summary>
		/// <remarks>
		/// <paramref name="forceStart"/> is the last-chance path after the
		/// retry window: clear a stuck hide tween and a stuck
		/// <c>processingLogic</c> only when the player still has no activity
		/// and cannot accept input. Do not force while a real activity is up.
		/// </remarks>
		private static void RecoverPlayerTurn(Stage stage, bool forceStart)
		{
			if (forceStart && MonoSingleton<PowerBarsVisibility>.IsInstanceValid)
			{
				PowerBarsVisibility hud = MonoSingleton<PowerBarsVisibility>.Instance;
				hud.hidingHUD = false;
				hud.barTweening = false;
			}

			if (forceStart && stage != null && stage.unitController != null
				&& stage.unitController.CurrentActivity == null && !stage.unitController.CanAcceptInput)
			{
				Traverse.Create(stage.unitController).Field<bool>("processingLogic").Value = false;
			}

			RecoverTurnChrome();
			EnsureFightHud(stage);
			EnsureFightInput(stage);
		}

		/// <summary>Enables hex / skill / End Turn input after an embed that skipped the normal turn-start coroutine.</summary>
		/// <remarks>
		/// Walkable hex sprites stay hidden until <c>moveController.Calculate</c> fills
		/// <c>reachableHexes.walkables</c>. The cast-range line renderer does not need that,
		/// so an embed can show only the outline. Do not call <c>SetSelectedSkill</c> here —
		/// it toggles the default move off if it already ran. Do not start
		/// <c>StartUserInteraction</c> for AI here: that coroutine raises
		/// <c>FightStartTurn</c>, which calls this again, and a second
		/// <c>EndTurn</c> resets the petition timer so <c>EndTurnNow</c> never runs.
		/// </remarks>
		internal static void EnsureFightInput(Stage stage)
		{
			UnitController controller = stage != null ? stage.unitController : null;
			if (controller == null)
			{
				controller = Object.FindObjectOfType<UnitController>();
				if (stage != null)
				{
					stage.unitController = controller;
				}
			}

			Camera gameplay = ResolveGameplayCamera();
			if (controller != null)
			{
				Traverse input = Traverse.Create(controller);
				input.Field<bool>("startCheckingUserInteraction").Value = true;
				if (gameplay != null)
				{
					input.Field<Camera>("mainCamera").Value = gameplay;
				}
			}

			BindConfirmCanvases(gameplay);
			AdoptHexGridRoot();

			if (controller == null)
			{
				return;
			}

			Traverse traverse = Traverse.Create(controller);

			Unit active = stage != null && stage.initiative != null ? stage.initiative.ActiveUnit : null;
			if (active != null && !IsHudExempt(active) && !active.isAI && active.CanDoStuff())
			{
				if (active.moveController != null && active.HexGridItem != null)
				{
					active.moveController.Calculate();
				}

				if (Events.instance != null)
				{
					Events.instance.Raise(new UnitTargettingChanged());
				}
			}

			if (controller.CurrentActivity != null || traverse.Field<bool>("processingLogic").Value)
			{
				return;
			}

			if (active == null || IsHudExempt(active) || active.isAI || !active.CanDoStuff()
				|| (stage != null && stage.endTurnPetition))
			{
				return;
			}

			if (stage != null)
			{
				stage.CanHandleInputOrAI = true;
			}

			System.Collections.IEnumerator start = AccessTools.Method(typeof(UnitController), "StartUserInteraction")
				.Invoke(controller, null) as System.Collections.IEnumerator;
			if (start != null)
			{
				traverse.Field<bool>("processingLogic").Value = true;
				controller.StartCoroutine(start);
			}
		}

		/// <summary>Aims confirm / cast canvases at the hole camera so EventSystem can hit the boot.</summary>
		/// <remarks>
		/// <see cref="TargetInteractionView.Awake"/> sets <c>worldCamera = Camera.main</c>. After
		/// Stop that is the lab camera, and FitScene may disable the camera Awake bound. The hole
		/// camera still draws the WorldSpace boot; GraphicRaycaster misses unless we rebind.
		/// Do not change <see cref="Canvas.renderMode"/> — FitCanvas would flatten the boot off the hex.
		/// </remarks>
		internal static void BindConfirmCanvases(Camera gameplay)
		{
			if (gameplay == null)
			{
				return;
			}

			Scene fight = ResolveFightScene();
			ConfirmButton[] buttons = Object.FindObjectsOfType<ConfirmButton>(true);
			for (int i = 0; i < buttons.Length; i++)
			{
				ConfirmButton button = buttons[i];
				if (button == null)
				{
					continue;
				}

				Canvas canvas = button.GetComponentInParent<Canvas>();
				if (canvas == null)
				{
					continue;
				}

				if (canvas.worldCamera != gameplay)
				{
					canvas.worldCamera = gameplay;
				}

				if (fight.IsValid() && canvas.transform.parent == null && canvas.gameObject.scene != fight)
				{
					SceneManager.MoveGameObjectToScene(canvas.gameObject, fight);
				}
			}
		}

		/// <summary>Moves <c>HexGridRoot</c> into the fight scene when Awake created it in the lab.</summary>
		private static void AdoptHexGridRoot()
		{
			GameObject root = GameObject.Find("HexGridRoot");
			if (root == null || Stage.instance == null || !Stage.IsInstanceValid)
			{
				return;
			}

			Scene fight = ResolveFightScene();
			if (fight.IsValid() && root.scene != fight)
			{
				SceneManager.MoveGameObjectToScene(root, fight);
			}
		}

		/// <summary>The loaded fight scene, or default if the board is not up yet.</summary>
		private static Scene ResolveFightScene()
		{
			if (MonoSingleton<HexBoardViewComponent>.IsInstanceValid)
			{
				return MonoSingleton<HexBoardViewComponent>.Instance.gameObject.scene;
			}

			return default(Scene);
		}

		private static Camera ResolveGameplayCamera()
		{
			System.Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			return getter != null ? getter() : null;
		}

		/// <summary>
		/// Re-enables skills / End Turn / LeanTouch if a canvas rebuild dropped
		/// <c>UnitEndedActivity</c> and left the HUD grayed out.
		/// </summary>
		internal static void RecoverTurnChrome()
		{
			Portrait[] portraits = Object.FindObjectsOfType<Portrait>(true);
			for (int i = 0; i < portraits.Length; i++)
			{
				Portrait portrait = portraits[i];
				if (portrait == null)
				{
					continue;
				}

				portrait.showDisabled = false;
				Graphic graphic = portrait.GetComponent<Graphic>();
				if (graphic != null)
				{
					graphic.color = Color.white;
				}
			}

			EndTurn endTurn = Object.FindObjectOfType<EndTurn>();
			if (endTurn != null)
			{
				Traverse.Create(endTurn).Field<bool>("showDisabled").Value = false;
			}

			LeanTouch leanTouch = Object.FindObjectOfType<LeanTouch>();
			if (leanTouch != null)
			{
				leanTouch.enabled = true;
			}
		}

		/// <summary>Turns fight HUD objects back on after Setup <c>SetActive(false)</c>.</summary>
		private static void RestoreHudObjects()
		{
			if (MonoSingleton<PowerBarsVisibility>.IsInstanceValid)
			{
				GameObject hud = MonoSingleton<PowerBarsVisibility>.Instance.gameObject;
				if (hud != null && !hud.activeSelf)
				{
					hud.SetActive(true);
				}
			}

			if (InitiativeBar.initiativeInst != null && !InitiativeBar.initiativeInst.gameObject.activeSelf)
			{
				InitiativeBar.initiativeInst.gameObject.SetActive(true);
			}

			if (SkillsBar.InstSkill != null && !SkillsBar.InstSkill.gameObject.activeSelf)
			{
				SkillsBar.InstSkill.gameObject.SetActive(true);
			}

			EndTurn endTurn = Object.FindObjectOfType<EndTurn>(true);
			if (endTurn != null && !endTurn.gameObject.activeSelf)
			{
				endTurn.gameObject.SetActive(true);
			}
		}

		/// <summary>Hides the debug panel and restores the ModAPI TakeOverAI config.</summary>
		internal static void End()
		{
			RestoreCapturedCamera();
			HideDebugUi();
			Stage.TakeOverAICheat = ModAPI.Config != null && ModAPI.Config.TakeOverAI != null
				&& ModAPI.Config.TakeOverAI.Value;
		}

		internal static void HideDebugUi()
		{
			CheatDebugController.DEBUG_PANEL_ENABLED = false;
			DebugPanelController debugPanel = DebugPanelController.GetDebugPanel();
			if (debugPanel == null)
			{
				return;
			}

			if (debugPanel.rootContent != null)
			{
				debugPanel.rootContent.SetActive(false);
			}

			if (debugPanel.visibilityButton != null)
			{
				debugPanel.visibilityButton.gameObject.SetActive(false);
			}
		}

		internal static bool IsHudExempt(Unit unit)
		{
			if (unit.states != null
				&& (unit.states.IsOn("NOT_IN_INITIATIVE_BAR")
					|| unit.states.IsOn("NOT_IN_INITIATIVE_BAR_VISUAL_ONLY")))
			{
				return true;
			}

			string name = unit.ToString();
			return name != null && name.IndexOf("TurnMarker", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>Closest the embed camera may zoom in (wheel and auto-fit).</summary>
		internal const float EmbedMinOrthoSize = 0.4f;

		/// <summary>Farthest the embed camera may zoom out (wheel and auto-fit).</summary>
		internal const float EmbedMaxOrthoSize = 50f;

		private static bool cameraSnapshotTaken;
		private static float savedInGameMinOrtho;
		private static float savedInGameMaxOrtho;
		private static float savedOrthoMin;
		private static float savedOrthoMax;
		private static float savedOrthographicSize;
		private static bool savedCheckEncounterLimits;
		private static bool savedExtendEncounterLimitsWidth;
		private static Bounds savedCameraLimits;

		/// <summary>Stores vanilla CameraBase / hole ortho once, before embed zoom writes.</summary>
		/// <remarks>
		/// Wheel zoom and UnlockCameraBounds rewrite the hole camera. That camera is tagged
		/// MainCamera for the embed, so FadeScreen / transitionscene inherit the zoomed ortho
		/// and the loading chrome scales. Capture must run before the first unlock.
		/// See docs/issues/resolved/sandbox-zoom-leaks-into-loading-ui.md.
		/// </remarks>
		internal static void CaptureCameraIfNeeded(CameraBase cameraBase)
		{
			if (cameraSnapshotTaken || cameraBase == null)
			{
				return;
			}

			cameraSnapshotTaken = true;
			savedInGameMinOrtho = cameraBase.inGameMinOrthoSize;
			savedInGameMaxOrtho = cameraBase.inGameMaxOrthoSize;
			savedOrthoMin = cameraBase.orthoSizeMin;
			savedOrthoMax = cameraBase.orthoSizeMax;
			savedCheckEncounterLimits = cameraBase.checkEncounterLimits;
			savedExtendEncounterLimitsWidth = cameraBase.extendEncounterLimitsWidth;
			savedCameraLimits = cameraBase.cameraLimits;
			Camera camera = cameraBase.GetComponent<Camera>();
			savedOrthographicSize = camera != null ? camera.orthographicSize : 5f;
		}

		/// <summary>Puts CameraBase min/max and the hole camera ortho back, then forgets the snapshot.</summary>
		internal static void RestoreCapturedCamera()
		{
			if (!cameraSnapshotTaken)
			{
				return;
			}

			if (MonoSingleton<CameraBase>.IsInstanceValid)
			{
				CameraBase cameraBase = MonoSingleton<CameraBase>.Instance;
				if (cameraBase != null)
				{
					cameraBase.inGameMinOrthoSize = savedInGameMinOrtho;
					cameraBase.inGameMaxOrthoSize = savedInGameMaxOrtho;
					cameraBase.orthoSizeMin = savedOrthoMin;
					cameraBase.orthoSizeMax = savedOrthoMax;
					cameraBase.checkEncounterLimits = savedCheckEncounterLimits;
					cameraBase.extendEncounterLimitsWidth = savedExtendEncounterLimitsWidth;
					cameraBase.cameraLimits = savedCameraLimits;
					Camera self = cameraBase.GetComponent<Camera>();
					if (self != null)
					{
						self.orthographicSize = savedOrthographicSize;
					}
				}
			}

			Camera hole = EmbeddedSceneHost.GameplayCamera;
			if (hole != null)
			{
				hole.orthographicSize = savedOrthographicSize;
			}

			cameraSnapshotTaken = false;
		}

		private static void UnlockCamera()
		{
			if (!MonoSingleton<LevelManager>.IsInstanceValid)
			{
				return;
			}

			MonoSingleton<LevelManager>.Instance.runCinematics = false;
			if (!MonoSingleton<CameraBase>.IsInstanceValid)
			{
				return;
			}

			CameraBase cameraBase = MonoSingleton<CameraBase>.Instance;
			if (EncounterCamera.ShouldClamp)
			{
				EncounterCamera.ApplyPlay(cameraBase, true);
			}
			else
			{
				UnlockCameraBounds(cameraBase);
				if (cameraBase.gameplayCamera != null)
				{
					cameraBase.gameplayCamera.TakenOver = false;
					cameraBase.gameplayCamera.CameraAdjust = !EncounterEdit.IsArmed
						&& !EncounterCamera.SuppressFightAutofocus;
				}
			}

			if (cameraBase.cinematicCamera != null)
			{
				cameraBase.cinematicCamera.CameraAdjust = false;
			}
		}

		/// <summary>Drops vanilla fight pan clamps and raises the ortho min/max so the hole can zoom.</summary>
		/// <remarks>
		/// <see cref="LevelManager"/> and <c>CinematicHelper</c> keep writing
		/// <c>encounterLimits</c> (often an AABB of 0 on <c>fighttesterempty</c>)
		/// and <c>inGameMaxOrthoSize</c>. The fight camera has no wheel zoom.
		/// Embed-only — campaign fights must stay bounded.
		/// </remarks>
		internal static void UnlockCameraBounds(CameraBase cameraBase)
		{
			if (cameraBase == null)
			{
				return;
			}

			CaptureCameraIfNeeded(cameraBase);

			cameraBase.checkEncounterLimits = false;
			cameraBase.extendEncounterLimitsWidth = true;
			cameraBase.cameraLimits = new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 2f));
			cameraBase.inGameMinOrthoSize = EmbedMinOrthoSize;
			cameraBase.inGameMaxOrthoSize = EmbedMaxOrthoSize;
			cameraBase.orthoSizeMin = EmbedMinOrthoSize;
			cameraBase.orthoSizeMax = EmbedMaxOrthoSize;
		}
	}
}
