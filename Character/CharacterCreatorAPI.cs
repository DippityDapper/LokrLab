using System;
using System.Collections.Generic;
using LokrLabApi;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LokrLab
{
	/// <summary>Character-scoped facade: workstation tabs plus forwarding RegisterWorkspace / RegisterInspectorDrawer / RegisterInspectorSection onto the Character project type.</summary>
	/// <remarks>
	/// Mirrors CharacterAPI's own resolver-chain/event shape (in LokrCharacterLoader) one level up:
	/// a workstation is registered once, by name, and CharacterLabScene builds/shows/hides it
	/// without needing to know anything about what's inside. Properties and the Animator register
	/// through this exact same surface at startup -- no special-cased fast path for "built-in"
	/// content, the same standard CharacterAPI's own default resolvers already hold themselves to
	/// (see LokrCharacterLoader/DefaultContentSources.cs). Load and Home aren't workstations in
	/// this sense -- they're the hub's own mandatory shell (the entry point and the return point
	/// every workstation comes back to), not a tab a plugin adds. RegisterWorkspace /
	/// RegisterInspectorDrawer / RegisterInspectorSection / RegisterBottomPanel forward onto the
	/// Character ProjectTypeRegistration so a Character-only plugin does not have to look the type
	/// up itself.
	/// </remarks>
	public static class CharacterCreatorAPI
	{
		/// <summary>Builds a workstation's UI once, the first time it's shown. Never called more than once per Character Lab session (per screenRoot's own lifetime).</summary>
		public delegate void WorkstationBuildFn(Scene scene, Transform screenRoot);

		/// <summary>Called every time this workstation is shown, including right after Build -- the hook for refreshing from whatever the current character's on-disk state now is. Pass null if there's nothing to refresh on re-entry.</summary>
		public delegate void WorkstationShowFn();

		/// <summary>Called every time this workstation is hidden because the player switched to a different screen. Pass null if there's nothing to pause/deactivate on exit.</summary>
		public delegate void WorkstationHideFn();

		/// <summary>One registered workstation's own callbacks and display metadata.</summary>
		internal sealed class WorkstationEntry
		{
			/// <summary>The screen id this workstation is registered and shown under.</summary>
			internal string Name;
			/// <summary>The label shown on this workstation's Home nav button.</summary>
			internal string DisplayLabel;
			/// <summary>Builds this workstation's UI, once, the first time it's shown.</summary>
			internal WorkstationBuildFn Build;
			/// <summary>Fires every time this workstation is shown, or null if it has nothing to refresh on re-entry.</summary>
			internal WorkstationShowFn OnShow;
			/// <summary>Fires every time this workstation is hidden, or null if it has nothing to pause/deactivate on exit.</summary>
			internal WorkstationHideFn OnHide;
			/// <summary>Whether this workstation's Home nav button stays hidden until a character is created or loaded.</summary>
			internal bool RequiresCharacterLoaded;
		}

		private static readonly List<WorkstationEntry> workstations = new List<WorkstationEntry>();

		/// <summary>Registers a workstation tab, replacing any existing one of the same name.</summary>
		/// <remarks>name doubles as the internal screen id -- must be unique across every registered workstation, including the built-in ones ("Properties", "Animator"). displayLabel is what shows on its Home nav button. onShow/onHide are both optional. requiresCharacterLoaded (default true) hides this workstation's nav button until a character is created or loaded, matching every first-party workstation but Load/Home/General itself.</remarks>
		public static void RegisterWorkstation(string name, string displayLabel, WorkstationBuildFn build,
			WorkstationShowFn onShow = null, WorkstationHideFn onHide = null, bool requiresCharacterLoaded = true)
		{
			workstations.RemoveAll(w => w.Name == name);
			workstations.Add(new WorkstationEntry
			{
				Name = name,
				DisplayLabel = displayLabel,
				Build = build,
				OnShow = onShow,
				OnHide = onHide,
				RequiresCharacterLoaded = requiresCharacterLoaded,
			});

			ProjectTypeRegistration character = LokrLabApi.LokrLabApi.GetProjectType("character");
			if (character != null)
			{
				WorkspaceRegistration existing = character.FindWorkspace(name);
				if (existing == null || (existing.BuildViewport == null && existing.BuildToolbar == null
					&& existing.RequiresSceneTransition == null))
				{
					character.RegisterWorkspace(new WorkspaceRegistration
					{
						Name = name,
						Priority = workstations.Count,
						RequiresProjectOpen = requiresCharacterLoaded,
						RequiresSceneTransition = null
					});
				}
			}
		}

		/// <summary>Forwards a workspace onto the Character project type. Prefer this for new workspaces; RegisterWorkstation remains for the pre-redesign hub.</summary>
		public static void RegisterWorkspace(WorkspaceRegistration workspace)
		{
			RequireCharacterType().RegisterWorkspace(workspace);
		}

		/// <summary>Forwards a primary inspector drawer onto the Character project type. Highest priority wins.</summary>
		public static void RegisterInspectorDrawer(string kind, InspectorDrawer drawer, int priority = 0)
		{
			RequireCharacterType().RegisterInspectorDrawer(kind, drawer, priority);
		}

		/// <summary>Forwards an extra inspector section onto the Character project type. Many sections stack under the primary drawer.</summary>
		public static void RegisterInspectorSection(string kind, InspectorDrawer section, int priority = 0)
		{
			RequireCharacterType().RegisterInspectorSection(kind, section, priority);
		}

		/// <summary>Forwards a bottom panel onto the Character project type. <paramref name="isRelevant"/> only auto-focuses; it never hides the tab.</summary>
		public static void RegisterBottomPanel(string name, string iconKey, Func<Transform, GameObject> builder, BottomPanelIsRelevant isRelevant = null, System.Action refresh = null, System.Action unbind = null)
		{
			RequireCharacterType().RegisterBottomPanel(name, iconKey, builder, isRelevant, refresh, unbind);
		}

		/// <summary>The registered Character project type, or throws if LokrLab has not booted yet.</summary>
		private static ProjectTypeRegistration RequireCharacterType()
		{
			ProjectTypeRegistration character = LokrLabApi.LokrLabApi.GetProjectType("character");
			if (character == null)
			{
				throw new System.InvalidOperationException("Character project type is not registered yet. Depend on LokrCharacterLab.");
			}

			return character;
		}

		/// <summary>Every currently registered workstation, in registration order.</summary>
		internal static IReadOnlyList<WorkstationEntry> Workstations => workstations;

		/// <summary>Looks up a registered workstation by name, or returns null if none matches.</summary>
		internal static WorkstationEntry Find(string name)
		{
			return workstations.Find(w => w.Name == name);
		}
	}
}
