using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ironhide.Legends.Controller.Game;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Game.Units;
using LokrLab;
using LokrLab.Encounter;
using UnityEngine;
using UnityEngine.UI;

namespace LokrCharacterLab.Patches
{
	/// <summary>Keeps embedded <see cref="Stage.Update"/> from NRE-ing and skipping the hero turn.</summary>
	/// <remarks>
	/// Vanilla reads <c>MetagameManager.instance.EncounterManager.EncounterLogic</c> and
	/// <c>unitController</c> without null checks. An ephemeral main-menu embed can leave those
	/// unset, so Update throws every frame and the initiative / skills HUD never appears.
	/// </remarks>
	internal static class EmbeddedFightStagePatch
	{
		private static readonly AccessTools.FieldRef<Stage, List<Entity>> Entities =
			AccessTools.FieldRefAccess<Stage, List<Entity>>("entities");

		private static readonly AccessTools.FieldRef<Stage, bool> ProcessingEndTurn =
			AccessTools.FieldRefAccess<Stage, bool>("processingEndTurn");

		private static readonly MethodInfo CleanEntities =
			AccessTools.Method(typeof(Stage), "CleanEntities");

		private static readonly MethodInfo EndTurnNow =
			AccessTools.Method(typeof(Stage), "EndTurnNow");

		[HarmonyPatch(typeof(Stage), nameof(Stage.CheckForUnitsDoingStuff))]
		private static class CheckForUnitsDoingStuffPatch
		{
			private static bool Prefix(Stage __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null || __instance.units == null)
				{
					return true;
				}

				for (int i = 0; i < __instance.units.Count; i++)
				{
					Unit unit = __instance.units[i];
					if (unit != null && unit.IsStuffHappening && unit.unitView == null)
					{
						return false;
					}
				}

				return true;
			}
		}

		[HarmonyPatch(typeof(Stage), nameof(Stage.Update))]
		private static class UpdatePatch
		{
			private static bool Prefix(Stage __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null)
				{
					return true;
				}

				if (__instance.unitController == null)
				{
					__instance.unitController = Object.FindObjectOfType<UnitController>();
				}

				SafeUpdateWithoutEncounter(__instance);
				return false;
			}
		}

		private static void SafeUpdateWithoutEncounter(Stage stage)
		{
			stage.UpdateDelayedActions();
			List<Entity> entities = Entities(stage);
			if (entities != null)
			{
				for (int i = 0; i < entities.Count; i++)
				{
					Entity entity = entities[i];
					if (entity != null && !entity.remove)
					{
						entity.Update();
					}
				}
			}

			CleanEntities?.Invoke(stage, null);
			stage.CheckForUnitsDoingStuff();
			if (EncounterEdit.IsArmed)
			{
				stage.CanHandleInputOrAI = false;
				EncounterEdit.TickPointer();
				return;
			}

			EncounterExploration.Tick(stage);

			if (stage.turnActionController == null)
			{
				return;
			}

			if (stage.turnActionController.TurnMiniPauseDetected && stage.units != null)
			{
				for (int i = 0; i < stage.units.Count; i++)
				{
					Unit unit = stage.units[i];
					if (unit != null && unit.HandleQueuedAbilityDefinition())
					{
						return;
					}
				}
			}

			if (!stage.turnActionController.TurnActionFinished)
			{
				return;
			}

			if (stage.CheckFightEnd())
			{
				stage.CanHandleInputOrAI = false;
				stage.isFighting = false;
				return;
			}

			if (!stage.isFighting)
			{
				return;
			}

			if (ProcessingEndTurn(stage))
			{
				stage.CanHandleInputOrAI = false;
				return;
			}

			if (stage.unitController != null && stage.unitController.IsProcessingLogic)
			{
				return;
			}

			if (!stage.turnActionController.TurnActionFinished)
			{
				return;
			}

			if (stage.endTurnPetition)
			{
				stage.CanHandleInputOrAI = false;
				if (Time.time - stage.startTime > 0.033333335f)
				{
					stage.startTime = Time.time;
					stage.endTurnPetition = false;
					EndTurnNow?.Invoke(stage, null);
				}

				return;
			}

			Unit active = stage.initiative != null ? stage.initiative.ActiveUnit : null;
			if (active != null && (active.states == null || active.states.IsOn("DEAD")
				|| active.IsDisabled() || !active.CanDoStuff()))
			{
				stage.EndTurn();
				return;
			}

			stage.CanHandleInputOrAI = true;
		}

		[HarmonyPatch(typeof(InitiativeBar), nameof(InitiativeBar.HighlightActiveUnit))]
		private static class HighlightActiveUnitPatch
		{
			private static bool Prefix(InitiativeBar __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null || __instance.initiativePositions == null
					|| __instance.initiativePositions.Count == 0)
				{
					return !EmbeddedFightHost.IsActive;
				}

				return __instance.initiativePositions[0] != null
					&& __instance.initiativePositions[0].portrait != null;
			}
		}

		[HarmonyPatch(typeof(UnitViewComponent), nameof(UnitViewComponent.Update))]
		private static class UnitViewUpdatePatch
		{
			private static bool Prefix(UnitViewComponent __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null)
				{
					return true;
				}

				MasterAnimationController master = __instance.masterAnimationController;
				if (master != null && master.activeAnimationController != null)
				{
					return true;
				}

				Unit unit = __instance.GetUnit();
				if (unit == null)
				{
					return false;
				}

				__instance.UpdatePositionAndFlip();
				if (unit.states != null && !unit.states.IsOn("DEAD") && unit.isIdle && !unit.remove
					&& master != null)
				{
					__instance.PlayIdleAnimation();
				}

				return false;
			}
		}

		[HarmonyPatch(typeof(PortraitSkill), nameof(PortraitSkill.SetSkillCost))]
		private static class SetSkillCostPatch
		{
			private static bool Prefix(PortraitSkill __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null)
				{
					return true;
				}

				if (__instance.skillPointer == null || __instance.skillButtonAsset == null
					|| __instance.cost == null)
				{
					return false;
				}

				Traverse skill = Traverse.Create(__instance);
				return skill.Field<Image>("frame").Value != null
					&& skill.Field<Image>("highLight").Value != null;
			}
		}

		[HarmonyPatch(typeof(Stage), nameof(Stage.TurnStarted))]
		private static class TurnStartedPatch
		{
			private static void Postfix(Stage __instance, Unit activeUnit)
			{
				if (!EmbeddedFightHost.IsActive || EncounterEdit.IsArmed || __instance == null)
				{
					return;
				}

				SandboxFightControls.BeginPlayerTurnHandoff(__instance, activeUnit);
			}
		}

		[HarmonyPatch(typeof(PowerBarsVisibility), "Init")]
		private static class PowerBarsInitPatch
		{
			private static void Prefix()
			{
				if (EmbeddedFightHost.IsActive && MonoSingleton<LevelManager>.IsInstanceValid)
				{
					MonoSingleton<LevelManager>.Instance.runCinematics = false;
				}
			}

			private static void Postfix(PowerBarsVisibility __instance)
			{
				if (!EmbeddedFightHost.IsActive || __instance == null)
				{
					return;
				}

				if (EncounterEdit.IsArmed)
				{
					EncounterEdit.HideFightHud();
					return;
				}

				__instance.hidingHUD = false;
				__instance.ShowHud();
			}
		}
	}
}
