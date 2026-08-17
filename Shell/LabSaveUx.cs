using System;
using LokrAbilityLab.Editor;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrLab.Encounter;
using LokrLab.Projects;
using LokrLabApi;
using SimpleUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LokrLab.Shell
{
	/// <summary>Manual save chrome: dirty flag, Ctrl+S, title <c>*</c>, and save/discard/cancel on leave.</summary>
	/// <remarks>
	/// Animator pose/clip/rig edits, Ability form edits, Encounter payload edits, and (since
	/// 2026-08-17) Character Properties field edits all set <see cref="ProjectSession.IsDirty"/>
	/// instead of writing to disk immediately -- see
	/// <c>CharacterProfileService.MarkDirtyAndRefresh</c>. <c>LabAliasesInspector</c> still writes
	/// aliases.json through immediately and does not use this flag.
	/// </remarks>
	internal static class LabSaveUx
	{
		private static UiModal prompt;
		private static UiLabel promptBody;
		private static Action pendingLeave;

		/// <summary>True when the open session has unsaved Animator, Ability, or Encounter edits.</summary>
		internal static bool IsDirty
		{
			get
			{
				ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
				return session != null && session.IsDirty;
			}
		}

		/// <summary>Attaches the Ctrl+S driver to the lab canvas.</summary>
		internal static void Bind(GameObject canvas)
		{
			if (canvas != null && canvas.GetComponent<Driver>() == null)
			{
				canvas.AddComponent<Driver>();
			}
		}

		/// <summary>Drops the unsaved-changes modal after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			prompt = null;
			promptBody = null;
			pendingLeave = null;
		}

		/// <summary>Marks the open session dirty and refreshes the title and status <c>*</c>.</summary>
		internal static void MarkDirty()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return;
			}

			session.IsDirty = true;
			RefreshChrome();
		}

		/// <summary>Clears dirty after a successful save or load and refreshes chrome.</summary>
		internal static void ClearDirty()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session != null)
			{
				session.IsDirty = false;
			}

			RefreshChrome();
		}

		/// <summary>Updates the LoKR Lab title and status-bar dirty marker without rebuilding docks.</summary>
		internal static void RefreshChrome()
		{
			CharacterLabScene.RefreshTitle();
			LabShell.RefreshDirtyIndicator();
		}

		/// <summary>Saves the open project the same way File → Save does. Returns false when the write fails.</summary>
		internal static bool TrySaveCurrent()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session == null)
			{
				return true;
			}

			if (session.ProjectTypeId == CharacterProjectType.Id)
			{
				CharacterProfileService.PersistAndSync();
				if (!RigEditorScene.IsRuntimeLive || string.IsNullOrEmpty(RigEditorScene.CurrentFolder))
				{
					return true;
				}

				bool rigSaved = RigEditorScene.TrySaveRig(RigEditorScene.CurrentFolder);
				if (!rigSaved)
				{
					MarkDirty();
				}

				return rigSaved;
			}

			if (session.ProjectTypeId == LokrLabApi.LokrLabApi.AbilityLibraryTypeId)
			{
				return AbilityEditorPanel.TrySave();
			}

			EncounterSession encounter = session as EncounterSession;
			if (encounter != null)
			{
				if (!encounter.TrySave())
				{
					return false;
				}

				ClearDirty();
				return true;
			}

			ClearDirty();
			return true;
		}

		/// <summary>File → Save and Ctrl+S. No-ops when nothing is open.</summary>
		internal static void SaveCurrent()
		{
			TrySaveCurrent();
		}

		/// <summary>Close Lab with an unsaved-changes prompt when dirty.</summary>
		internal static void RequestCloseLab()
		{
			GuardLeave(() => CharacterLabScene.CloseTo(CharacterLabScene.OriginScene));
		}

		/// <summary>Close Project with an unsaved-changes prompt when dirty.</summary>
		internal static void RequestCloseProject()
		{
			GuardLeave(CharacterLabScene.CloseProject);
		}

		/// <summary>Close Lab into a real scene with an unsaved-changes prompt when dirty.</summary>
		internal static void RequestCloseTo(string sceneName)
		{
			GuardLeave(() => CharacterLabScene.CloseTo(sceneName));
		}

		/// <summary>Runs proceed immediately when clean; otherwise shows save / discard / cancel.</summary>
		internal static void GuardLeave(Action proceed)
		{
			if (proceed == null)
			{
				return;
			}

			if (!IsDirty)
			{
				proceed();
				return;
			}

			pendingLeave = proceed;
			ShowPrompt();
		}

		private static void Tick()
		{
			if (!CharacterLabScene.IsOpen)
			{
				return;
			}

			GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			bool typingInField = focused != null && focused.GetComponent<InputField>() != null;
			bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
			if (!typingInField && ctrlHeld && Input.GetKeyDown(KeyCode.S))
			{
				SaveCurrent();
			}
		}

		private static void ShowPrompt()
		{
			EnsurePrompt();
			if (prompt == null)
			{
				return;
			}

			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			string name = session != null ? session.DisplayName : "this project";
			promptBody.SetText("'" + name + "' has unsaved changes. Save, discard, or cancel and stay in the project.");
			prompt.Show();
		}

		private static void EnsurePrompt()
		{
			if (prompt != null)
			{
				return;
			}

			Transform canvas = LabShell.Canvas ?? CharacterLabScene.Canvas;
			if (canvas == null)
			{
				return;
			}

			prompt = UiModal.Create(canvas, UiTheme.Default, "Unsaved changes", 560f, 220f);
			UiStack content = UiStack.Vertical(prompt.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			prompt.Add(content);
			promptBody = UiLabel.Create(content.ContentTransform, string.Empty, UiTheme.Default, 14, TextAnchor.UpperLeft);
			content.Add(promptBody.Grow());
			UiStack buttons = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(buttons.FixedHeight(36f));
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Save", OnPromptSave, UiTheme.Default, primary: true).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Discard", OnPromptDiscard, UiTheme.Default, primary: false).Grow());
			buttons.Add(UiButton.Create(buttons.ContentTransform, "Cancel", OnPromptCancel, UiTheme.Default, primary: false).Grow());
		}

		private static void OnPromptSave()
		{
			if (!TrySaveCurrent())
			{
				return;
			}

			LeaveAfterPrompt();
		}

		private static void OnPromptDiscard()
		{
			ProjectSession session = LokrLabApi.LokrLabApi.CurrentSession;
			if (session != null && session.ProjectTypeId == CharacterProjectType.Id)
			{
				HomeWorkstationScene.DiscardUnsavedProfileEdits();
			}

			ClearDirty();
			LeaveAfterPrompt();
		}

		private static void OnPromptCancel()
		{
			pendingLeave = null;
			if (prompt != null)
			{
				prompt.Hide();
			}
		}

		private static void LeaveAfterPrompt()
		{
			Action proceed = pendingLeave;
			pendingLeave = null;
			if (prompt != null)
			{
				prompt.Hide();
			}

			proceed?.Invoke();
		}

		/// <summary>Ctrl+S while the lab canvas is alive.</summary>
		private sealed class Driver : MonoBehaviour
		{
			private void Update()
			{
				Tick();
			}
		}
	}
}
