using System.Globalization;
using System.IO;
using Ironhide.Legends.Utils;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrCharacterLoader;
using LokrCharacterLab;
using UnityEngine.EventSystems;

namespace LokrLab
{
	/// <summary>Persists the open Lab character and pushes it into the running game via <see cref="CharacterAPI"/>.</summary>
	internal static class LabContentReloader
	{
		/// <summary>True when reload is allowed outside active combat (v1 scope).</summary>
		internal static bool CanReloadInCurrentGameState(out string skipReason)
		{
			if (MonoSingleton<LevelManager>.IsInstanceValid)
			{
				skipReason = "active combat";
				return false;
			}

			skipReason = null;
			return true;
		}

		/// <summary>Persists disk files (when requested) and reloads Lab character and ability content into runtime caches.</summary>
		/// <remarks>
		/// Uses <see cref="CharacterAPI.ReloadScope.All"/> so sandbox can resolve an imported
		/// defaultSkill. <see cref="CharacterAPI.ReloadScope.LabCharacterDefaults"/> omits
		/// Abilities, which left Assassin's lethal strike missing until a full restart.
		/// </remarks>
		internal static CharacterAPI.ReloadResult ReloadCurrentCharacter(bool persistFirst)
		{
			CommitPendingFieldEdits();
			if (HomeWorkstationScene.CurrentProfile == null
				|| string.IsNullOrEmpty(HomeWorkstationScene.CurrentCharacterFolder))
			{
				return new CharacterAPI.ReloadResult
				{
					Success = false,
					ErrorMessage = "No character loaded.",
				};
			}

			if (!CanReloadInCurrentGameState(out string skipReason))
			{
				return new CharacterAPI.ReloadResult
				{
					Success = false,
					ErrorMessage = "Reload skipped — " + skipReason + ".",
				};
			}

			if (persistFirst)
			{
				HomeWorkstationScene.PersistCurrentCharacter();
			}

			return CharacterAPI.ReloadLabContent(CharacterAPI.ReloadScope.All);
		}

		/// <summary>Called from <see cref="CharacterLabScene.Close"/> when auto-reload is enabled.</summary>
		/// <remarks>
		/// Always reloads Lab content even with no character open (Close Project then Close Lab
		/// still has to push localization). Stops a leftover sandbox first so a stale
		/// <c>LevelManager</c> is not treated as campaign combat. Flushes focused Properties
		/// fields before persist — Description is multiline and does not commit on Enter.
		/// </remarks>
		internal static void TryAutoReloadOnLabClose()
		{
			LokrCharacterLabPlugin.Log.LogInfo("Auto-reload on Lab close: starting.");
			CommitPendingFieldEdits();
			EmbeddedFightHost.Stop();

			if (HomeWorkstationScene.CurrentProfile != null
				&& !string.IsNullOrEmpty(HomeWorkstationScene.CurrentCharacterFolder)
				&& Directory.Exists(HomeWorkstationScene.CurrentCharacterFolder))
			{
				CharacterProfileService.PersistToDisk();
			}

			CharacterAPI.ReloadResult result = CharacterAPI.ReloadLabContent(CharacterAPI.ReloadScope.All);
			if (!result.Success)
			{
				if (!string.IsNullOrEmpty(result.ErrorMessage))
				{
					LokrCharacterLabPlugin.Log.LogWarning("Auto-reload on Lab close: " + result.ErrorMessage);
				}
				return;
			}

			LokrCharacterLabPlugin.Log.LogInfo(string.Format(
				CultureInfo.InvariantCulture,
				"Auto-reload on Lab close completed in {0:F0} ms ({1}).",
				result.ElapsedMs,
				result.Completed));
		}

		/// <summary>Ends InputField edit so OnEndEdit writes Name/Description before persist.</summary>
		internal static void FlushPendingEdits()
		{
			CommitPendingFieldEdits();
		}

		/// <summary>Unfocuses the EventSystem selection and commits General Name/Description.</summary>
		private static void CommitPendingFieldEdits()
		{
			try
			{
				if (EventSystem.current != null)
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
			}
			catch (System.Exception ex)
			{
				LokrCharacterLabPlugin.Log.LogWarning("FlushPendingEdits: EventSystem unfocus failed: " + ex.Message);
			}

			CharacterGeneralPanel.CommitPending();
		}
	}
}
