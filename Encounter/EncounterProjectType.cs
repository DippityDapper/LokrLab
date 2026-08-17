using System;
using System.IO;
using LokrLab.Shell;
using LokrLabApi;

namespace LokrLab.Encounter
{
	/// <summary>Registers the Encounter project type on LokrLabApi (many projects, Setup, Sandbox, catalogues, Import Terrains).</summary>
	internal static class EncounterProjectType
	{
		/// <summary>Registers the type, create/load/delete, Setup workspace, and inspectors.</summary>
		internal static void Register()
		{
			EncounterLabPaths.EnsureFoldersExist();
			ProjectTypeRegistration registration = LokrLabApi.LokrLabApi.RegisterProjectType(
				LokrLabApi.LokrLabApi.EncounterTypeId,
				"Encounter",
				"encounter",
				EncounterLabPaths.Root);
			registration.IsSingleton = false;
			registration.ReferenceableProjectTypes = new[] { LokrLabApi.LokrLabApi.CharacterTypeId };
			registration.ScanCategory = EncounterLabPaths.Category;
			registration.CreateNew = CreateNew;
			registration.BuildCreateSheet = EncounterCreateSheet.Build;
			registration.CommitCreateSheet = EncounterCreateSheet.Commit;
			registration.Load = Load;
			registration.Delete = DeleteEncounter;
			registration.ResolveDisplayName = ResolveDisplayName;
			registration.RegisterNodeTreeContributor(EncounterNodes.Contribute, priority: 0);
			registration.RegisterNodeFactory(EncounterNodes.CombatantKind, new[] { EncounterNodes.CombatantsKind },
				EncounterNodes.CreateCombatant);
			registration.RegisterNodeFactory(EncounterNodes.TerrainKind, new[] { EncounterNodes.TerrainsKind },
				EncounterNodes.CreateTerrain);
			registration.RegisterNodeFactory(EncounterNodes.PropKind, new[] { EncounterNodes.PropsKind },
				EncounterNodes.CreateProp);
			registration.RegisterNodeFactory(EncounterNodes.TriggerKind, new[] { EncounterNodes.TriggersKind },
				EncounterNodes.CreateTrigger);
			registration.RegisterNodeFactory(EncounterNodes.SpawnPointKind, new[] { EncounterNodes.SpawnPointsKind },
				EncounterNodes.CreateSpawnPoint);
			registration.RegisterInspectorDrawer(EncounterNodes.EncounterKind, EncounterNodes.DrawEncounter);
			registration.RegisterInspectorDrawer(EncounterNodes.CombatantsKind, EncounterNodes.DrawCombatants);
			registration.RegisterInspectorDrawer(EncounterNodes.CombatantKind, EncounterNodes.DrawCombatant);
			registration.RegisterInspectorDrawer(EncounterNodes.TerrainsKind, EncounterNodes.DrawTerrains);
			registration.RegisterInspectorDrawer(EncounterNodes.TerrainKind, EncounterNodes.DrawTerrain);
			registration.RegisterInspectorDrawer(EncounterNodes.PropsKind, EncounterNodes.DrawProps);
			registration.RegisterInspectorDrawer(EncounterNodes.PropKind, EncounterNodes.DrawProp);
			registration.RegisterInspectorDrawer(EncounterNodes.TriggersKind, EncounterNodes.DrawTriggers);
			registration.RegisterInspectorDrawer(EncounterNodes.TriggerKind, EncounterNodes.DrawTrigger);
			registration.RegisterInspectorDrawer(EncounterNodes.SpawnPointsKind, EncounterNodes.DrawSpawnPoints);
			registration.RegisterInspectorDrawer(EncounterNodes.SpawnPointKind, EncounterNodes.DrawSpawnPoint);
			registration.RegisterInspectorDrawer(EncounterNodes.AliasesKind, EncounterNodes.DrawAliases);
			registration.OnSelectionChanged = EncounterNodes.OnSelectionChanged;
			registration.OnNodeActivated = EncounterNodes.OnNodeActivated;
			registration.RegisterPersistentInspector(new PersistentInspectorRegistration
			{
				Id = "encounter-combatants-catalogue",
				Matches = EncounterCombatantsCatalogue.Matches,
				EnsureBuilt = EncounterCombatantsCatalogue.Build,
				Show = EncounterCombatantsCatalogue.Show,
				Hide = EncounterCombatantsCatalogue.Hide,
				Scrollable = false
			});
			registration.RegisterPersistentInspector(new PersistentInspectorRegistration
			{
				Id = "encounter-props-catalogue",
				Matches = EncounterPropsCatalogue.Matches,
				EnsureBuilt = EncounterPropsCatalogue.Build,
				Show = EncounterPropsCatalogue.Show,
				Hide = EncounterPropsCatalogue.Hide,
				Scrollable = false
			});
			LokrLabApi.LokrLabApi.RegisterMenu("File", priority: 0);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Add Combatant", PromptAddCombatant, priority: 5,
				isVisible: IsEncounterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Import Terrains", PromptImportTerrains, priority: 6,
				isVisible: IsEncounterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Add Prop", PromptAddProp, priority: 7,
				isVisible: IsEncounterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Add Trigger", PromptAddTrigger, priority: 8,
				isVisible: IsEncounterSession);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "Add Spawn Point", PromptAddSpawnPoint, priority: 9,
				isVisible: IsEncounterSession);
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = EncounterSetupViewport.WorkspaceName,
				IconKey = "Setup",
				Priority = 0,
				BuildViewport = EncounterSetupViewport.Build,
				OnActivated = EncounterSetupViewport.OnActivated,
				OnDeactivated = EncounterSetupViewport.OnDeactivated
			});
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = EncounterSandboxViewport.WorkspaceName,
				IconKey = "Sandbox",
				Priority = 10,
				BuildViewport = EncounterSandboxViewport.Build,
				OnDeactivated = EncounterSandboxViewport.OnDeactivated
			});
		}

		/// <summary>True when the open session is an Encounter project.</summary>
		internal static bool IsEncounterSession()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			return session != null && session.ProjectTypeId == LokrLabApi.LokrLabApi.EncounterTypeId;
		}

		private static ProjectSession CreateNew()
		{
			EncounterCreateRequest request = EncounterCreateSheet.TakePending();
			string name = request != null && !string.IsNullOrEmpty(request.Name)
				? request.Name
				: "Encounter";
			string slug = request != null && !string.IsNullOrEmpty(request.Slug)
				? request.Slug
				: LokrLab.LabSlugIds.LegalizeSlug(name, "encounter");
			string id = EncounterLabPaths.GenerateNewId(slug);
			string folder = EncounterLabPaths.EncounterFolder(id);
			ProjectMarker.Write(folder, LokrLabApi.LokrLabApi.EncounterTypeId, name);
			EncounterFileModel.CreateEmpty().TryWrite(folder);
			if (request != null)
			{
				EncounterCreateSheet.WriteAlias(folder, id, request.Alias);
			}

			return EncounterSession.Create(folder);
		}

		private static ProjectSession Load(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return null;
			}

			return EncounterSession.Create(folder);
		}

		private static string DeleteEncounter(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return "Folder does not exist.";
			}

			try
			{
				Directory.Delete(folder, true);
			}
			catch (Exception ex)
			{
				return ex.Message;
			}

			return null;
		}

		private static string ResolveDisplayName(string folder)
		{
			return ProjectMarker.TryReadDisplayName(folder);
		}

		private static void PromptAddCombatant()
		{
			EncounterNodes.FocusCombatantsFolder(LokrLabApi.LokrLabApi.CurrentSession as EncounterSession);
		}

		private static void PromptImportTerrains()
		{
			EncounterNodes.CreateTerrain(null, LokrLabApi.LokrLabApi.CurrentSession);
		}

		private static void PromptAddProp()
		{
			EncounterNodes.FocusPropsFolder(LokrLabApi.LokrLabApi.CurrentSession as EncounterSession);
		}

		private static void PromptAddTrigger()
		{
			EncounterNodes.CreateTrigger(null, LokrLabApi.LokrLabApi.CurrentSession);
		}

		private static void PromptAddSpawnPoint()
		{
			EncounterNodes.CreateSpawnPoint(null, LokrLabApi.LokrLabApi.CurrentSession);
		}
	}
}
