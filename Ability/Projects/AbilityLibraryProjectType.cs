using System;
using System.IO;
using LokrAbilityLab.Editor;
using LokrLab;
using LokrLabApi;

namespace LokrAbilityLab.Projects
{
	/// <summary>Registers the Ability Library project type on LokrLabApi (many libraries, not a singleton).</summary>
	internal static class AbilityLibraryProjectType
	{
		/// <summary>Registers the type, node tree, inspector, Library + Sandbox workspaces, and File items.</summary>
		internal static void Register()
		{
			AbilityLabPaths.MigrateLegacySingleton();
			AbilityLabPaths.EnsureFoldersExist();
			ProjectTypeRegistration registration = LokrLabApi.LokrLabApi.RegisterProjectType(
				LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
				"Ability Library",
				"ability",
				AbilityLabPaths.LibrariesRoot);
			registration.IsSingleton = false;
			registration.ReferenceableProjectTypes = new[] { LokrLabApi.LokrLabApi.CharacterTypeId };
			registration.ScanCategory = AbilityLabPaths.LibrariesCategory;
			registration.CreateNew = CreateNew;
			registration.BuildCreateSheet = AbilityCreateSheet.Build;
			registration.CommitCreateSheet = AbilityCreateSheet.Commit;
			registration.Load = Load;
			registration.Delete = DeleteLibrary;
			registration.ResolveDisplayName = AbilityLibrarySession.ReadDisplayName;
			registration.RegisterNodeTreeContributor(AbilityLibraryNodes.Contribute, priority: 0);
			registration.RegisterNodeFactory(AbilityLibraryNodes.AbilityKind, new[] { AbilityLibraryNodes.LibraryKind }, AbilityLibraryNodes.CreateAbility);
			registration.RegisterInspectorDrawer(AbilityLibraryNodes.LibraryKind, AbilityLibraryNodes.DrawLibrary);
			registration.RegisterInspectorDrawer(AbilityLibraryNodes.AbilityKind, AbilityLibraryNodes.DrawAbility);
			registration.RegisterInspectorDrawer(AbilityLibraryNodes.AliasesKind, AbilityLibraryNodes.DrawAliases);
			registration.OnSelectionChanged = AbilityLibraryViewport.OnSelectionChanged;
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = AbilityLibraryViewport.WorkspaceName,
				IconKey = "Lib",
				Priority = 0,
				BuildViewport = AbilityLibraryViewport.Build,
				OnDeactivated = AbilityLibraryViewport.OnDeactivated
			});
			registration.RegisterWorkspace(new WorkspaceRegistration
			{
				Name = AbilitySandboxViewport.WorkspaceName,
				IconKey = "Sandbox",
				Priority = 10,
				BuildViewport = AbilitySandboxViewport.Build,
				OnDeactivated = AbilitySandboxViewport.OnDeactivated
			});

			LokrLabApi.LokrLabApi.RegisterMenu("File", priority: 0);
			LokrLabApi.LokrLabApi.RegisterMenuItem("File", "New Ability", PromptNewAbility, priority: 5,
				isVisible: IsLibraryOpen);
		}

		private static ProjectSession CreateNew()
		{
			AbilityLibraryCreateRequest request = AbilityCreateSheet.TakePending();
			string name = request != null && !string.IsNullOrEmpty(request.Name)
				? request.Name
				: "Ability Library";
			string slug = request != null && !string.IsNullOrEmpty(request.Slug)
				? request.Slug
				: LabSlugIds.LegalizeSlug(name, "library");
			string id = AbilityLabPaths.GenerateNewLibraryId(slug);
			string folder = AbilityLabPaths.LibraryFolder(id);
			AbilityLibrarySession.WriteMarker(folder, name);
			return AbilityLibrarySession.Create(folder);
		}

		private static ProjectSession Load(string folder)
		{
			string resolved = ResolveLibraryFolder(folder);
			if (string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved))
			{
				return null;
			}

			return AbilityLibrarySession.Create(resolved);
		}

		/// <summary>Maps FolderRoot / empty / a library folder to a concrete library path.</summary>
		internal static string ResolveLibraryFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder)
				|| string.Equals(folder, AbilityLabPaths.LibrariesRoot, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(folder, AbilityLabPaths.LegacyAbilitiesRoot, StringComparison.OrdinalIgnoreCase))
			{
				return AbilityLabPaths.FirstLibraryFolder();
			}

			return folder;
		}

		private static string DeleteLibrary(string folder)
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

		private static bool IsLibraryOpen()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			return session != null && session.ProjectTypeId == LokrLabApi.LokrLabApi.AbilityLibraryTypeId;
		}

		private static void PromptNewAbility()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			AbilityLibraryNodes.CreateAbility(null, session);
		}
	}
}
