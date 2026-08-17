using System;
using System.IO;
using LokrCharacterLab;
using LokrLab.Editor.General;
using LokrLab.Shell;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Encounter Sandbox: Start sandbox plays the open encounter in the dock hole.</summary>
	internal static class EncounterSandboxViewport
	{
		/// <summary>Workspace tab name. Same as Character and Ability.</summary>
		internal const string WorkspaceName = LabSandboxChrome.WorkspaceName;

		private static LabSandboxChrome chrome;

		/// <summary>Builds the Sandbox toolbar and hole into the shell viewport.</summary>
		internal static GameObject Build(Transform parent)
		{
			chrome = LabSandboxChrome.Build(parent, Start, Stop, showLevel: true, onLoadEncounter: null);
			return chrome.Host.GameObject;
		}

		/// <summary>Stops the embed when leaving Sandbox.</summary>
		internal static void OnDeactivated()
		{
			Stop();
			chrome = null;
		}

		private static void Start()
		{
			EncounterSession session = LokrLabApi.LokrLabApi.CurrentSession as EncounterSession;
			if (session == null || session.File == null)
			{
				LokrLab.Lab.SetStatus("No encounter payload.");
				return;
			}

			EncounterFileModel file = session.File;
			int level = chrome != null ? chrome.SelectedLevel : 1;
			if (EncounterPlayRules.FirstSpawnPoint(file) != null
				&& EncounterPlayRules.FirstGoodSide(file) == null)
			{
				ProjectReferencePickerModal.Show(LokrLabApi.LokrLabApi.CharacterTypeId, picked =>
				{
					StartArmed(session, file, UnitIdFromCharacter(picked), level);
				});
				return;
			}

			string ready = EncounterPlayRules.CanStart(file, hasFill: false);
			if (ready != null)
			{
				LokrLab.Lab.SetStatus(ready);
				return;
			}

			StartArmed(session, file, null, level);
		}

		private static void StartArmed(
			EncounterSession session,
			EncounterFileModel file,
			string fillUnitId,
			int fillLevel)
		{
			string ready = EncounterPlayRules.CanStart(file, !string.IsNullOrEmpty(fillUnitId));
			if (ready != null)
			{
				LokrLab.Lab.SetStatus(ready);
				return;
			}

			Func<EmbeddedFightRequest, string> start = LabSandboxChrome.ResolveStart();
			if (start == null)
			{
				LokrLab.Lab.SetStatus("StartEmbeddedFight is not assigned.");
				return;
			}

			string casterId = fillUnitId;
			if (string.IsNullOrEmpty(casterId))
			{
				EncounterCombatantModel firstGood = EncounterPlayRules.FirstGoodSide(file);
				casterId = EncounterPlayRules.UnitId(firstGood);
			}

			if (string.IsNullOrEmpty(casterId))
			{
				LokrLab.Lab.SetStatus("Sandbox needs a GoodSide combatant or a character to fill a spawn point.");
				return;
			}

			Stop();
			EncounterSandbox.Arm(file, fillUnitId, fillLevel, showDebugPanel: true);
			LokrLabPlugin.Log.LogInfo(
				"Sandbox: starting encounter '" + session.DisplayName + "' on " + EncounterSandbox.Template
				+ " with caster '" + casterId + "' at level " + fillLevel + ".");
			string error = start(new EmbeddedFightRequest
			{
				CasterUnitId = casterId,
				CasterLevel = fillLevel,
				Hole = chrome != null ? chrome.Hole : null,
				OnFailed = message =>
				{
					EncounterSandbox.Disarm();
					LokrLab.Lab.SetStatus(message);
				},
				OnEnded = EncounterSandbox.Disarm,
			});
			if (error != null)
			{
				EncounterSandbox.Disarm();
				LokrLab.Lab.SetStatus(error);
			}
		}

		private static void Stop()
		{
			LabSandboxChrome.StopFight();
			EncounterSandbox.Disarm();
		}

		private static string UnitIdFromCharacter(ProjectReference picked)
		{
			if (picked == null || string.IsNullOrEmpty(picked.FolderPath))
			{
				return null;
			}

			CharacterProfile profile = CharacterProfileSidecar.Load(picked.FolderPath);
			if (profile != null && !string.IsNullOrEmpty(profile.SpawnUnitId))
			{
				return profile.SpawnUnitId;
			}

			return Path.GetFileName(picked.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}
	}
}
