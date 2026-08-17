using System;
using LokrCharacterLab;
using LokrLab.Encounter;
using LokrLabApi;
using LokrLab.Shell;
using SimpleUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LokrLab.Editor
{
	/// <summary>Character Sandbox: Start sandbox is a 1v1; Load Encounter plays an authored fight in the same hole.</summary>
	/// <remarks>
	/// Uses <see cref="EmbeddedFightHost"/> (the same additive <c>fighttesterempty</c> path as
	/// Ability and Encounter Sandbox). The lab stays open. Fight-end unloads the hole and does not
	/// call <c>ReopenAfterFight</c>.
	/// </remarks>
	internal static class SandboxWorkstationScene
	{
		private static LabSandboxChrome chrome;

		/// <summary>Builds the Sandbox workstation's content (legacy full-screen path).</summary>
		internal static void Build(Scene scene, Transform screenRoot)
		{
			Transform contentRoot = Lab.GetWorkstationContentRoot(screenRoot);
			UiPanel panel = UiPanel.Create(contentRoot, UiTheme.Default, LabSandboxChrome.WorkspaceName);
			panel.Add(BuildContent(panel.ContentParent));
		}

		/// <summary>Builds the Sandbox viewport: Start / Stop / Load Encounter and a fight hole.</summary>
		internal static GameObject BuildViewport(Transform parent)
		{
			return BuildContent(parent).GameObject;
		}

		/// <summary>Unloads an embedded fight when the Sandbox workspace is left.</summary>
		internal static void OnDeactivated()
		{
			StopFight();
			chrome = null;
		}

		private static UiStack BuildContent(Transform parent)
		{
			chrome = LabSandboxChrome.Build(parent, StartFight, StopFight, showLevel: true, PromptLoadEncounter);
			return chrome.Host;
		}

		private static void StartFight()
		{
			if (HomeWorkstationScene.CurrentProfile == null || HomeWorkstationScene.CurrentCharacterFolder == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: no character loaded.");
				return;
			}

			StopFight();
			HomeWorkstationScene.PersistCurrentCharacter();
			LabContentReloader.ReloadCurrentCharacter(persistFirst: false);
			string heroUnitId = HomeWorkstationScene.CurrentProfile.SpawnUnitId;
			int level = chrome != null ? chrome.SelectedLevel : 1;
			Func<EmbeddedFightRequest, string> start = LabSandboxChrome.ResolveStart();
			if (start == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: StartEmbeddedFight is not assigned.");
				return;
			}

			LokrCharacterLabPlugin.Log.LogInfo(
				"Sandbox: starting embedded fight with '" + heroUnitId + "' at level " + level + ".");
			string error = start(new EmbeddedFightRequest
			{
				CasterUnitId = heroUnitId,
				CasterLevel = level,
				Hole = chrome != null ? chrome.Hole : null,
				OnFailed = message =>
				{
					LokrCharacterLabPlugin.Log.LogWarning("Sandbox: embed failed — " + message);
				},
			});
			if (error != null)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: embed start error — " + error);
			}
		}

		/// <summary>Opens a picker for which Encounter project to load into the Sandbox hole.</summary>
		private static void PromptLoadEncounter()
		{
			if (HomeWorkstationScene.CurrentProfile == null || HomeWorkstationScene.CurrentCharacterFolder == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: no character loaded.");
				return;
			}

			ProjectReferencePickerModal.Show(LokrLabApi.LokrLabApi.EncounterTypeId, LoadEncounter);
		}

		/// <summary>Loads the picked Encounter and starts it with the current character filling its first hero spawn point.</summary>
		private static void LoadEncounter(ProjectReference picked)
		{
			if (picked == null || string.IsNullOrEmpty(picked.FolderPath))
			{
				return;
			}

			EncounterFileModel file = EncounterFileModel.LoadOrEmpty(picked.FolderPath);
			if (EncounterPlayRules.FirstSpawnPoint(file) == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning(
					"Sandbox: '" + picked.DisplayName + "' has no Hero Spawn Point to fill.");
				Lab.SetStatus("This encounter has no Hero Spawn Point.");
				return;
			}

			StopFight();
			HomeWorkstationScene.PersistCurrentCharacter();
			LabContentReloader.ReloadCurrentCharacter(persistFirst: false);
			string heroUnitId = HomeWorkstationScene.CurrentProfile.SpawnUnitId;
			int level = chrome != null ? chrome.SelectedLevel : 1;
			Func<EmbeddedFightRequest, string> start = LabSandboxChrome.ResolveStart();
			if (start == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: StartEmbeddedFight is not assigned.");
				return;
			}

			EncounterSandbox.Arm(file, heroUnitId, level, showDebugPanel: true);
			LokrCharacterLabPlugin.Log.LogInfo(
				"Sandbox: loading Encounter '" + picked.DisplayName + "' with '" + heroUnitId
				+ "' at level " + level + ".");
			string error = start(new EmbeddedFightRequest
			{
				CasterUnitId = heroUnitId,
				CasterLevel = level,
				Hole = chrome != null ? chrome.Hole : null,
				OnFailed = message =>
				{
					EncounterSandbox.Disarm();
					LokrCharacterLabPlugin.Log.LogWarning("Sandbox: embed failed — " + message);
				},
			});
			if (error != null)
			{
				EncounterSandbox.Disarm();
				LokrCharacterLabPlugin.Log.LogWarning("Sandbox: embed start error — " + error);
			}
		}

		private static void StopFight()
		{
			LabSandboxChrome.StopFight();
		}
	}
}
