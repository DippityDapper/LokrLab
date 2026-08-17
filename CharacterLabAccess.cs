using LokrModMenu;

namespace LokrLab
{
	/// <summary>Public entry points for opening the LokrLab shell from other plugins.</summary>
	/// <remarks>Opening/closing now drives a real scene transition (see CharacterLabScene's own remarks) rather than toggling an overlay -- "the lab" from a caller's perspective is still just Open/Close/Toggle/IsOpen, the scene-transition mechanics are entirely internal to CharacterLabScene.</remarks>
	public static class CharacterLabAccess
	{
		/// <summary>True while the lab is the active scene.</summary>
		public static bool IsOpen => CharacterLabScene.IsOpen;

		/// <summary>Shows or hides the Character Lab.</summary>
		public static void Toggle()
		{
			CharacterLabScene.Toggle();
		}

		/// <summary>Transitions into the Character Lab, remembering the current scene to return to on Close().</summary>
		public static void Open()
		{
			ModMenuAPI.Close();
			CharacterLabScene.Open();
		}

		/// <summary>Transitions back to whichever real scene the lab was opened from.</summary>
		public static void Close()
		{
			CharacterLabScene.Close();
		}

		/// <summary>Force-closes the lab and restores game input after a scene change.</summary>
		internal static void ForceCloseForSceneChange()
		{
			CharacterLabScene.ForceClose();
		}
	}
}
