using LokrLabApi;
using LokrModMenu;

namespace LokrAbilityLab
{
	/// <summary>Public entry points for opening Ability Lab from other plugins.</summary>
	/// <remarks>Prefers the LokrLab shell (that host already fades). The fallback scene uses the same FadeScreen + unload pattern when JumpToProject is not assigned.</remarks>
	public static class AbilityLabAccess
	{
		/// <summary>True while the fallback Ability Lab scene is open (not the LokrLab shell path).</summary>
		public static bool IsOpen => AbilityLabScene.IsOpen;

		/// <summary>Opens or closes Ability Lab.</summary>
		public static void Toggle()
		{
			AbilityLabScene.Toggle();
		}

		/// <summary>Opens the Ability Library in the LokrLab shell when JumpToProject is assigned; otherwise the fallback lab scene (FadeScreen + unload).</summary>
		public static void Open()
		{
			ModMenuAPI.Close();
			if (LokrLabApi.LokrLabApi.OpenProject != null)
			{
				LokrLabApi.LokrLabApi.JumpToProject(
					LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
					AbilityLabPaths.FirstLibraryFolder(),
					null);
				return;
			}

			AbilityLabScene.Open();
		}

		/// <summary>Closes the fallback lab scene (no-op if the shell owns the session).</summary>
		public static void Close()
		{
			AbilityLabScene.Close();
		}

		/// <summary>Force-closes the lab and restores game input after a scene change.</summary>
		internal static void ForceCloseForSceneChange()
		{
			AbilityLabScene.ForceClose();
		}
	}
}
