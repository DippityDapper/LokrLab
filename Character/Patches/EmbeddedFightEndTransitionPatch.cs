using HarmonyLib;
using Ironhide.Legends;
using Ironhide.Legends.View.Screens.Transition;
using LokrLab;

namespace LokrCharacterLab.Patches
{
	/// <summary>Keeps an embedded wipe inside the Lab. Vanilla would Single-load the victory or defeat scene.</summary>
	/// <remarks>
	/// <see cref="LevelManager.FightEndedHandler"/> starts HideHud / FadeScreen /
	/// <see cref="TransitionSceneComponent.TransitionToNextScene"/>. That loads
	/// <c>transitionscene</c> as Single, destroys the Lab, then
	/// <c>VictoryWindow.Awake</c> throws (no LEGEND hero on an ephemeral quest).
	/// <see cref="EmbeddedFightHost.OnFightEnded"/> already <c>Stop()</c>s the hole.
	/// Gate on Lab open, not only embed-active: Stop clears the embed flag before
	/// other <c>FightEnded</c> listeners run. Close Lab sets <c>IsOpen</c> false
	/// before its own transition, so that path still leaves.
	/// </remarks>
	[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.FightEndedHandler))]
	internal static class EmbeddedFightEndHandlerPatch
	{
		/// <summary>Skips victory / defeat coroutines while the Lab is open.</summary>
		private static bool Prefix()
		{
			return !CharacterLabScene.IsOpen && !EmbeddedFightHost.IsActive;
		}
	}

	/// <summary>Blocks <c>transitionscene</c> Single loads while the Lab is still open.</summary>
	[HarmonyPatch(typeof(TransitionSceneComponent), nameof(TransitionSceneComponent.TransitionToNextScene))]
	internal static class EmbeddedFightEndTransitionPatch
	{
		/// <summary>Skips the Single load so Close Lab (IsOpen already false) still works.</summary>
		private static bool Prefix()
		{
			return !CharacterLabScene.IsOpen;
		}
	}
}
