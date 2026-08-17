using HarmonyLib;
using Ironhide.Legends.View.Paralax;
using LokrCharacterLab;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Hides Paralax foliage as soon as LevelManager generates it in a Lab embed.</summary>
	/// <remarks>
	/// Generate runs in <c>LevelManager.Start</c> before the spawn-ready callback.
	/// Hiding here avoids a one-frame flash. Campaign fights skip because
	/// <see cref="EmbeddedFightHost.IsActive"/> is false.
	/// </remarks>
	[HarmonyPatch(typeof(ParalaxPropConfigurerStatic), nameof(ParalaxPropConfigurerStatic.Generate))]
	internal static class EncounterParalaxPatch
	{
		/// <summary>Turns the layers off after they are instantiated.</summary>
		private static void Postfix()
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			EncounterForeground.Hide();
		}
	}
}
