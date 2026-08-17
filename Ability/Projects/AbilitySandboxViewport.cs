using System;
using System.Collections.Generic;
using LokrAbilityLab.Editor;
using LokrLab;
using LokrLabApi;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Ability Sandbox: Start sandbox loads an embedded real fight into the dock hole.</summary>
	internal static class AbilitySandboxViewport
	{
		/// <summary>Workspace tab name. Same as Character and Encounter.</summary>
		internal const string WorkspaceName = LabSandboxChrome.WorkspaceName;

		private static LabSandboxChrome chrome;

		/// <summary>Builds the Sandbox toolbar and hole.</summary>
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

		/// <summary>No-op. Level choices are fixed 1-3 on the shared chrome.</summary>
		internal static void Refresh()
		{
		}

		private static void Start()
		{
			TryStartEmbeddedFight(AbilityEditorForm.Current);
		}

		private static void Stop()
		{
			LabSandboxChrome.StopFight();
		}

		/// <summary>Starts Character Lab's additive fight in the hole.</summary>
		private static void TryStartEmbeddedFight(AbilityFileModel model)
		{
			Func<EmbeddedFightRequest, string> start = LabSandboxChrome.ResolveStart();
			if (start == null)
			{
				LokrAbilityLabPlugin.Log.LogWarning("Sandbox: Character Lab did not assign StartEmbeddedFight.");
				return;
			}

			List<string> used = model != null ? AbilityUsage.CharactersUsing(model.Id) : new List<string>();
			if (used.Count == 0)
			{
				LokrAbilityLabPlugin.Log.LogWarning("Sandbox: no used-by Character for '" + (model != null ? model.Id : "") + "'.");
				return;
			}

			int level = chrome != null ? chrome.SelectedLevel : 1;
			Stop();
			LokrAbilityLabPlugin.Log.LogInfo(
				"Sandbox: starting embedded fight with '" + used[0] + "' at level " + level + ".");
			string error = start(new EmbeddedFightRequest
			{
				CasterUnitId = used[0],
				CasterLevel = level,
				Hole = chrome != null ? chrome.Hole : null,
				OnFailed = message =>
				{
					LokrAbilityLabPlugin.Log.LogWarning("Sandbox: embed failed — " + message);
				},
			});
			if (error != null)
			{
				LokrAbilityLabPlugin.Log.LogWarning("Sandbox: embed start error — " + error);
			}
		}
	}
}
