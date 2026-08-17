using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends;
using Ironhide.Legends.Controller.Game.Activities;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Map;
using Ironhide.Legends.Model.Metagame.Map.Quests;
using LokrLab;
using LokrLab.Encounter;
using LokrLabApi;
using UnityEngine;

namespace LokrCharacterLab
{
	/// <summary>
	/// Fight-specific embed: ephemeral quest, Sandbox roster spawn, and take-over-AI.
	/// Scene load, hole crop, and HUD fit live on LokrLab's <c>StartEmbeddedScene</c>.
	/// </summary>
	internal static class EmbeddedFightHost
	{
		private const string EncounterTemplate = "fighttesterempty";

		private static EmbeddedFightRequest request;
		private static bool listenersHooked;
		/// <summary>True after this embed has spawned the Sandbox roster (StartFight prefix or FightStarted fallback).</summary>
		private static bool rosterSpawned;

		/// <summary>True while Character Lab is hosting an embedded fight.</summary>
		internal static bool IsActive { get; private set; }

		/// <summary>True when Setup or a live Encounter fight (Play or Sandbox Load Encounter) owns the embed.</summary>
		/// <remarks>
		/// Tile crop/sprite patches and the exploration fence share this gate. Campaign
		/// fights stay vanilla because <see cref="IsActive"/> is false.
		/// </remarks>
		internal static bool EncounterOwnsEmbed
		{
			get
			{
				return EncounterEdit.IsArmed || EncounterSandbox.IsArmed;
			}
		}

		/// <summary>No-op. LokrLab's HUD fitter LateUpdates the hole every frame.</summary>
		internal static void RefitHole()
		{
		}

		/// <summary>Assigns the static fight facade so Ability Lab can start a fight even if Host is later replaced.</summary>
		internal static void BindStatic()
		{
			LokrLabApi.LokrLabApi.StartEmbeddedFight = Start;
			LokrLabApi.LokrLabApi.StopEmbeddedFight = Stop;
			LokrLabApi.LokrLabApi.IsEmbeddedFightActive = () => IsActive;
			BindHost(LokrLabApi.LokrLabApi.Host);
		}

		/// <summary>Copies fight-embed delegates onto a newly built Host.</summary>
		internal static void BindHost(LabHost host)
		{
			if (host == null)
			{
				return;
			}

			host.StartEmbeddedFight = Start;
			host.StopEmbeddedFight = Stop;
			host.IsEmbeddedFightActive = () => IsActive;
		}

		/// <summary>Clears fight delegates on the Host after Stop.</summary>
		internal static void UnbindHost(LabHost host)
		{
			Stop();
			if (host == null)
			{
				return;
			}

			host.StartEmbeddedFight = null;
			host.StopEmbeddedFight = null;
			host.IsEmbeddedFightActive = null;
		}

		/// <summary>True when Definitions or DefinitionsByUnique has this folder id, including a leftover c-prefixed block key.</summary>
		private static bool TryFindCasterDefinition(UnityDefinitionsParser parser, string id)
		{
			if (parser.Definitions != null)
			{
				if (parser.Definitions.ContainsKey(id))
				{
					return true;
				}
				if (id.Length > 0 && char.IsDigit(id[0]) && parser.Definitions.ContainsKey("c" + id))
				{
					return true;
				}
				if (id.Length > 1 && id[0] == 'c' && parser.Definitions.ContainsKey(id.Substring(1)))
				{
					return true;
				}
			}
			return parser.DefinitionsByUnique != null && parser.DefinitionsByUnique.ContainsKey(id);
		}

		private static string Start(EmbeddedFightRequest fightRequest)
		{
			if (IsActive)
			{
				return "An embedded fight is already running.";
			}

			if (fightRequest == null || string.IsNullOrEmpty(fightRequest.CasterUnitId))
			{
				return "No caster unit id.";
			}

			if (fightRequest.Hole == null)
			{
				return "No hole RectTransform.";
			}

			UnityDefinitionsParser parser;
			try
			{
				parser = UnityDefinitionsParser.instance;
			}
			catch (Exception ex)
			{
				return "Unit definitions failed to load: " + ex.Message;
			}

			if (parser == null || !TryFindCasterDefinition(parser, fightRequest.CasterUnitId))
			{
				return "No unit definition for '" + fightRequest.CasterUnitId + "'.";
			}

			MapQuestDefinition questDefinition = FindHostQuestDefinition();
			if (questDefinition == null)
			{
				return "Couldn't find a quest definition to host the fight.";
			}

			Func<EmbeddedSceneRequest, string> startScene = LokrLabApi.LokrLabApi.StartEmbeddedScene;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (startScene == null && host != null)
			{
				startScene = host.StartEmbeddedScene;
			}

			if (startScene == null)
			{
				return "LokrLab did not assign StartEmbeddedScene.";
			}

			request = fightRequest;
			IsActive = true;
			rosterSpawned = false;
			LokrCharacterLabPlugin.Log.LogInfo(
				"EmbeddedFightHost: additive fight with caster '" + fightRequest.CasterUnitId + "'.");
			try
			{
				CinematicMapHelper helper = new CinematicMapHelper();
				MapQuestStatus questStatus = helper.EnqueueEphemeralQuest(questDefinition.id);
				MetagameManager.instance.MapManager.ActivateEphermeral();
				string template = ResolveEncounterTemplate();
				questStatus.encounters = new List<string> { template };

				Stage.instance = null;
				string error = startScene(new EmbeddedSceneRequest
				{
					BundleId = "scenes",
					SceneName = SceneDB.GetScene("fight"),
					Hole = fightRequest.Hole,
					OnCamera = fightRequest.BindCamera,
					OnReady = HookFightEvents,
					OnFailed = message => Fail(message),
				});
				if (error != null)
				{
					IsActive = false;
					request = null;
					return error;
				}
			}
			catch (Exception ex)
			{
				IsActive = false;
				request = null;
				return "Additive fight load failed: " + ex.Message;
			}

			HookFightEvents();
			return null;
		}

		/// <summary>Stops the fight, unloads the embedded scene, and clears Stage.</summary>
		internal static void Stop()
		{
			bool wasActive = IsActive;
			IsActive = false;
			rosterSpawned = false;
			UnhookFightEvents();
			request = null;
			EncounterSandbox.Disarm();
			EncounterEdit.Disarm();
			LokrCharacterLab.Patches.EmbeddedFightCameraPatches.ResetDrag();
			if (InterfaceDataRepository.instance != null)
			{
				InterfaceDataRepository.instance.Reset();
			}

			ClearEphemeralQuests();

			SandboxFightControls.RestoreCapturedCamera();

			Action stopScene = LokrLabApi.LokrLabApi.StopEmbeddedScene;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (stopScene == null && host != null)
			{
				stopScene = host.StopEmbeddedScene;
			}

			stopScene?.Invoke();

			if (wasActive)
			{
				SandboxFightControls.End();
			}

			if (wasActive || Stage.IsInstanceValid)
			{
				Stage.instance = null;
			}
		}

		/// <summary>Spawns the Sandbox roster once, before <see cref="Stage.StartFight"/> sets fightStarted.</summary>
		/// <remarks>
		/// fighttesterempty has no encounter units. Spawning on FightStartedEvent is too late:
		/// FightStartedHandler already set fightStarted, so EntityAddedEventHandler Inserts and
		/// leaves ActiveUnit null. A higher-priority StartFight prefix calls this while
		/// fightStarted is still false so units are Add-ed. OnFightStarted keeps this as a
		/// fallback. See docs/issues/resolved/fight-started-empty-initiative-nre.md.
		/// </remarks>
		internal static void TrySpawnRosterBeforeFight(Stage stage)
		{
			if (!IsActive || rosterSpawned || request == null || stage == null)
			{
				return;
			}

			rosterSpawned = true;
			if (EncounterSandbox.IsArmed || EncounterEdit.IsArmed)
			{
				SandboxFightControls.Begin(enableDebugPanel: EncounterSandbox.IsArmed && EncounterSandbox.ShowDebugPanel);
				EncounterFileModel file = EncounterSandbox.IsArmed ? EncounterSandbox.File : EncounterEdit.File;
				Dictionary<string, Unit> spawned = new Dictionary<string, Unit>(StringComparer.Ordinal);
				if (EncounterSandbox.IsArmed)
				{
					EncounterRoster.Spawn(
						stage, file, spawned, EncounterSandbox.FillUnitId, EncounterSandbox.FillLevel,
						EncounterSandbox.FillLevel);
				}
				else
				{
					EncounterRoster.Spawn(stage, file, spawned);
				}

				if (EncounterEdit.IsArmed)
				{
					EncounterEdit.BindPreview(spawned);
					EncounterEdit.FreezeTurns(stage);
				}

				return;
			}

			SandboxFightControls.Begin();
			SandboxRoster.SpawnHeroAndEnemy(
				stage,
				request.CasterUnitId,
				request.EnemyUnitId,
				request.CasterLevel);
		}

		private static void OnFightStarted(FightStartedEvent theEvent)
		{
			if (!IsActive || request == null || theEvent == null || theEvent.stage == null)
			{
				return;
			}

			try
			{
				TrySpawnRosterBeforeFight(theEvent.stage);
				FinishFightControls(theEvent.stage);
				if (EncounterSandbox.IsArmed)
				{
					EncounterRoster.ApplyAuthoredFacing(EncounterSandbox.File);
				}

				Camera gameplay = ResolveGameplayCamera();
				if (gameplay != null)
				{
					request.BindCamera?.Invoke(gameplay);
				}

				request.OnReady?.Invoke();
			}
			catch (Exception ex)
			{
				Fail("Embedded fight spawn failed: " + ex.Message);
			}
		}

		private static void OnFightStartTurn(FightStartTurn theEvent)
		{
			if (!IsActive || theEvent == null)
			{
				return;
			}

			FinishFightControls(theEvent.stage);
		}

		/// <summary>Restores HUD after spawn and each FightStartTurn. A live Encounter fight keeps enemy AI; Sandbox Load Encounter may still show the debug panel.</summary>
		/// <remarks>
		/// Authored facing is applied from OnFightStarted only. This also runs on
		/// FightStartTurn for HUD / take-over-AI; re-applying Flipped here snaps
		/// units back after every action.
		/// </remarks>
		private static void FinishFightControls(Stage stage)
		{
			EncounterForeground.Hide();
			if (EncounterEdit.IsArmed)
			{
				EncounterEdit.ApplyPreviewPresentation(stage);
				return;
			}

			if (EncounterSandbox.IsArmed)
			{
				SandboxFightControls.Finish(
					stage, takeOverAll: false, showDebugPanel: EncounterSandbox.ShowDebugPanel);
				return;
			}

			SandboxFightControls.Finish(stage);
		}

		private static void OnFightEnded(FightEnded theEvent)
		{
			if (!IsActive)
			{
				return;
			}

			string result = theEvent != null && theEvent.group == UnitGroup.GoodSide
				? "Victory."
				: "Defeat.";
			LokrLab.Lab.SetStatus(result);
			Action onEnded = request != null ? request.OnEnded : null;
			Stop();
			if (onEnded != null)
			{
				onEnded();
			}
		}

		private static void Fail(string message)
		{
			Action<string> onFailed = request != null ? request.OnFailed : null;
			LokrCharacterLabPlugin.Log.LogWarning("EmbeddedFightHost: " + message);
			Stop();
			if (onFailed != null)
			{
				onFailed(message);
			}
		}

		private static void HookFightEvents()
		{
			if (listenersHooked || Events.instance == null)
			{
				return;
			}

			Events.instance.AddListener<FightStartedEvent>(OnFightStarted);
			Events.instance.AddListener<FightEnded>(OnFightEnded);
			Events.instance.AddListener<FightStartTurn>(OnFightStartTurn);
			listenersHooked = true;
		}

		private static void UnhookFightEvents()
		{
			if (!listenersHooked)
			{
				return;
			}

			listenersHooked = false;
			if (Events.instance == null)
			{
				return;
			}

			Events.instance.RemoveListener<FightStartedEvent>(OnFightStarted);
			Events.instance.RemoveListener<FightEnded>(OnFightEnded);
			Events.instance.RemoveListener<FightStartTurn>(OnFightStartTurn);
		}

		/// <summary>Encounter Play or Setup template, else the Sandbox empty field.</summary>
		private static string ResolveEncounterTemplate()
		{
			if (EncounterSandbox.IsArmed && !string.IsNullOrEmpty(EncounterSandbox.Template))
			{
				return EncounterSandbox.Template;
			}

			if (EncounterEdit.IsArmed && !string.IsNullOrEmpty(EncounterEdit.Template))
			{
				return EncounterEdit.Template;
			}

			return EncounterTemplate;
		}

		private static Camera ResolveGameplayCamera()
		{
			Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			return getter != null ? getter() : null;
		}

		/// <summary>Drops stacked Stage/Sandbox ephemeral quests so the next board is one encounter wide.</summary>
		private static void ClearEphemeralQuests()
		{
			try
			{
				if (!MetagameManager.IsInstanceValid || MetagameManager.instanceNoLoad == null
					|| MetagameManager.instanceNoLoad.MapManager == null)
				{
					return;
				}

				IMapManager map = MetagameManager.instanceNoLoad.MapManager;
				map.DisableEphemeral();
				IList quests = Traverse.Create(map).Field("ephemeralQuests").GetValue() as IList;
				if (quests != null)
				{
					quests.Clear();
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("EmbeddedFightHost: ephemeral quest clear failed: " + ex.Message);
			}
		}

		private static MapQuestDefinition FindHostQuestDefinition()
		{
			IMapManager map = null;
			if (MetagameManager.IsInstanceValid)
			{
				map = MetagameManager.instanceNoLoad.MapManager;
			}

			if (map != null)
			{
				foreach (MapQuestDefinition definition in map.GetAllQuestDefinitions())
				{
					return definition;
				}
			}

			try
			{
				foreach (MapQuestDefinition definition in MetagameManager.instance.MapManager.GetAllQuestDefinitions())
				{
					return definition;
				}
			}
			catch (Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("EmbeddedFightHost: quest lookup failed: " + ex.Message);
			}

			return null;
		}
	}
}
