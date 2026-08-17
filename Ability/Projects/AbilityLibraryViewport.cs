using System.Collections.Generic;
using LokrAbilityLab.Editor;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrAbilityLab.Projects
{
	/// <summary>Library workspace center: browser on the root, action-card canvas on an ability.</summary>
	internal static class AbilityLibraryViewport
	{
		internal const string WorkspaceName = "Library";

		private static UiStack host;
		private static string lastKind;
		private static string lastAbilityId;

		internal static GameObject Build(Transform parent)
		{
			host = UiStack.Vertical(parent, UiTheme.Default, spacing: 8f, padding: 12f);
			Stretch(host);
			lastKind = null;
			lastAbilityId = null;
			Sync();
			return host.GameObject;
		}

		internal static void Invalidate()
		{
			lastKind = null;
			lastAbilityId = null;
		}

		internal static void OnDeactivated()
		{
			host = null;
			lastKind = null;
			lastAbilityId = null;
			AbilityEditorForm.ReleaseBodyHosts();
		}

		internal static void OnSelectionChanged(IReadOnlyList<LabNode> nodes)
		{
			if (IsSandboxActive())
			{
				AbilitySandboxViewport.Refresh();
				return;
			}

			Sync();
		}

		internal static void OnAbilityLoaded()
		{
			if (IsSandboxActive())
			{
				AbilitySandboxViewport.Refresh();
				return;
			}

			lastAbilityId = null;
			Sync();
		}

		private static void Sync()
		{
			if (host == null || host.GameObject == null)
			{
				return;
			}

			LabNode primary = LokrLabApi.LokrLabApi.Selection.Primary;
			string kind = primary != null ? primary.Kind : AbilityLibraryNodes.LibraryKind;
			if (kind == AbilityLibraryNodes.AbilityKind)
			{
				string id = AbilityId(primary);
				if (kind == lastKind && id == lastAbilityId && AbilityEditorForm.Current != null)
				{
					AbilityEditorForm.RebuildBody();
					return;
				}

				lastKind = kind;
				lastAbilityId = id;
				ShowCards();
				return;
			}

			if (kind == lastKind && lastKind == AbilityLibraryNodes.LibraryKind)
			{
				return;
			}

			lastKind = AbilityLibraryNodes.LibraryKind;
			lastAbilityId = null;
			ShowBrowser();
		}

		private static void ShowBrowser()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			AbilityLibraryBrowser.Build(host, session != null ? session.FolderPath : null);
		}

		private static void ShowCards()
		{
			host.Clear();
			AbilityFileModel model = AbilityEditorForm.Current;
			if (model == null)
			{
				host.Add(UiLabel.Create(host.ContentTransform,
					"Select an ability — the Inspector loads the envelope, then the action cards appear here.",
					UiTheme.Default, 14, TextAnchor.UpperLeft).Grow());
				return;
			}

			host.Add(UiLabel.Create(host.ContentTransform, model.Id + " — action cards").FixedHeight(22f));
			host.Add(UiLabel.Create(host.ContentTransform,
				"Envelope stays in the Inspector. Stage tab previews combat without opening a fight.",
				UiTheme.Default, 11).FixedHeight(28f));
			UiStack body = UiStack.Vertical(host.ContentTransform, UiTheme.Default, spacing: 6f, padding: 0f, scrollable: true);
			host.Add(body.Grow());
			AbilityEditorForm.BuildBody(body);
			AbilityEditorForm.RebuildBody();
		}

		private static string AbilityId(LabNode node)
		{
			if (node == null)
			{
				return null;
			}

			return node.Payload as string ?? node.DisplayName;
		}

		private static bool IsSandboxActive()
		{
			LabHost lab = LokrLabApi.LokrLabApi.Host;
			return lab != null && lab.GetActiveWorkspaceName != null
				&& lab.GetActiveWorkspaceName() == AbilitySandboxViewport.WorkspaceName;
		}

		private static void Stretch(UiStack stack)
		{
			RectTransform rect = stack.RectTransform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}
	}
}
