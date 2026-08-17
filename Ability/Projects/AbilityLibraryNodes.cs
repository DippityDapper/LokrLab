using System;
using System.Collections.Generic;
using System.IO;
using LokrAbilityLab.Editor;
using LokrLab;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Node kinds, tree contributor, Add Node factory, and inspector drawers for an Ability Library.</summary>
	internal static class AbilityLibraryNodes
	{
		/// <summary>Root node for the open library.</summary>
		internal const string LibraryKind = "AbilityLibrary";

		/// <summary>One ability folder / ability.txt.</summary>
		internal const string AbilityKind = "Ability";

		/// <summary>This ability folder's aliases.json.</summary>
		internal const string AliasesKind = "AbilityAliases";

		/// <summary>Root plus one child per ability folder that has ability.txt.</summary>
		internal static IEnumerable<LabNode> Contribute(ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				yield break;
			}

			LabNode root = new LabNode
			{
				Id = "ability-library",
				DisplayName = session.DisplayName ?? "Ability Library",
				Kind = LibraryKind,
				IconKey = "Lib",
				Payload = session
			};

			List<string> ids = new List<string>();
			foreach ((string _, string id) in AbilityLabPaths.EnumerateAbilitiesIn(session.FolderPath))
			{
				ids.Add(id);
			}

			ids.Sort(StringComparer.OrdinalIgnoreCase);
			foreach (string id in ids)
			{
				root.Children.Add(AbilityNode(id, session.FolderPath));
			}

			yield return root;
		}

		/// <summary>Opens the New Ability sheet. The node appears after confirm mints the id.</summary>
		internal static LabNode CreateAbility(LabNode parent, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				return null;
			}

			AbilityItemCreateModal.Show(session.FolderPath, id =>
			{
				AbilityLibraryViewport.Invalidate();
				LokrLabApi.LokrLabApi.RequestRefresh();
				LokrLabApi.LokrLabApi.JumpToProject(
					LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
					session.FolderPath,
					"ability:" + id);
			});
			return null;
		}

		/// <summary>Library root summary.</summary>
		internal static void DrawLibrary(LabNode node, ProjectSession session, Transform contentParent)
		{
			UiStack section = UiStack.Vertical(contentParent, UiTheme.Default, spacing: 6f, padding: 0f);
			string title = session != null ? session.DisplayName : "Ability Library";
			section.Add(UiLabel.Create(section.ContentTransform, title, UiTheme.Default, UiTheme.Default.TitleFontSize)
				.FixedHeight(26f));
			if (session != null)
			{
				LabClipboard.AddIdRow(section, session.Id);
			}

			int count = node != null && node.Children != null ? node.Children.Count : 0;
			section.Add(UiLabel.Create(section.ContentTransform, count + " abilit" + (count == 1 ? "y" : "ies") + " on disk.")
				.FixedHeight(22f));
			section.Add(UiLabel.Create(section.ContentTransform,
				"The viewport lists this library. Open an ability for the envelope here and action cards in the center. Stage previews combat without a fight.",
				UiTheme.Default, 11, TextAnchor.UpperLeft).FixedHeight(48f));
			DrawLibraryRename(section, session);
			DrawLeftoverAbilityIds(section, session);
		}

		/// <summary>Rename control when this library folder is not already a <c>slug_token</c> id.</summary>
		private static void DrawLibraryRename(UiStack section, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.Id))
			{
				return;
			}

			if (LabSlugIds.LooksLikeSlugTokenId(session.Id))
			{
				return;
			}

			section.Add(UiLabel.Create(section.ContentTransform,
				"This library folder still uses a leftover id.",
				UiTheme.Default, 11, TextAnchor.UpperLeft).FixedHeight(22f));
			string folder = session.FolderPath;
			string id = session.Id;
			string name = session.DisplayName;
			section.Add(UiButton.Create(section.ContentTransform, "Rename library to slug_token",
				() => AbilityLibraryRenameModal.Show(folder, id, name), primary: true).FixedHeight(28f));
		}

		/// <summary>Lists leftover ability folder ids on the library inspector with a Rename button each.</summary>
		private static void DrawLeftoverAbilityIds(UiStack section, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				return;
			}

			List<string> leftover = new List<string>();
			foreach ((string _, string id) in AbilityLabPaths.EnumerateAbilitiesIn(session.FolderPath))
			{
				if (!LabSlugIds.LooksLikeSlugTokenId(id))
				{
					leftover.Add(id);
				}
			}

			if (leftover.Count == 0)
			{
				return;
			}

			leftover.Sort(StringComparer.OrdinalIgnoreCase);
			section.Add(UiLabel.Create(section.ContentTransform,
				leftover.Count.ToString() + " leftover folder id(s). Rename ports the folder onto slug_token and updates character skill refs.",
				UiTheme.Default, 11, TextAnchor.UpperLeft).FixedHeight(36f));
			string library = session.FolderPath;
			for (int i = 0; i < leftover.Count; i++)
			{
				string id = leftover[i];
				UiStack row = UiStack.Horizontal(section.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f);
				section.Add(row.FixedHeight(28f));
				row.Add(UiLabel.Create(row.ContentTransform, id, UiTheme.Default, 12).Grow());
				row.Add(UiButton.Create(row.ContentTransform, "Rename", () => AbilityItemRenameModal.Show(library, id, null), primary: false)
					.FixedWidth(72f));
			}
		}

		/// <summary>Hosts AbilityEditorPanel in the shell inspector, plus leftover-id rename when needed.</summary>
		internal static void DrawAbility(LabNode node, ProjectSession session, Transform contentParent)
		{
			string abilityId = node != null ? (node.Payload as string ?? node.DisplayName) : null;
			DrawLeftoverIdRename(contentParent, session, abilityId);
			AbilityEditorPanel.BuildInto(contentParent);
			if (!string.IsNullOrEmpty(abilityId) && session != null)
			{
				AbilityEditorPanel.Load(AbilityLabPaths.AbilityDefinitionPath(session.FolderPath, abilityId));
			}

			AbilityLibraryViewport.OnAbilityLoaded();
		}

		/// <summary>Rename button when this ability folder is not already a <c>slug_token</c> id.</summary>
		private static void DrawLeftoverIdRename(Transform contentParent, ProjectSession session, string abilityId)
		{
			if (session == null || string.IsNullOrEmpty(abilityId) || LabSlugIds.LooksLikeSlugTokenId(abilityId))
			{
				return;
			}

			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform,
				"This folder still uses a leftover id.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(22f));
			string library = session.FolderPath;
			string oldId = abilityId;
			section.Add(UiButton.Create(section.ContentTransform, "Rename to slug_token",
				() => AbilityItemRenameModal.Show(library, oldId, null), theme, primary: true).FixedHeight(28f));
		}

		/// <summary>This ability folder's aliases.json list.</summary>
		internal static void DrawAliases(LabNode node, ProjectSession session, Transform contentParent)
		{
			string folder = node != null ? node.Payload as string : null;
			LokrLab.LabAliasesInspector.Draw(folder, contentParent);
		}

		private static LabNode AbilityNode(string id, string libraryFolder)
		{
			LabNode node = new LabNode
			{
				Id = "ability:" + id,
				DisplayName = id,
				Kind = AbilityKind,
				IconKey = "Abil",
				Payload = id
			};
			if (!string.IsNullOrEmpty(libraryFolder))
			{
				node.Children.Add(new LabNode
				{
					Id = "aliases:" + id,
					DisplayName = "Aliases",
					Kind = AliasesKind,
					IconKey = "Abil",
					Payload = AbilityLabPaths.AbilityFolder(libraryFolder, id)
				});
			}

			return node;
		}
	}
}
