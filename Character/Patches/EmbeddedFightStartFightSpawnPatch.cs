using HarmonyLib;
using Ironhide.Legends.Model.Game;

namespace LokrCharacterLab.Patches
{
	/// <summary>Spawns the Sandbox roster before vanilla <see cref="Stage.StartFight"/> raises FightStartedEvent.</summary>
	/// <remarks>
	/// Priority 600 runs before a default-priority LokrPatch replacement of StartFight. Returning
	/// true lets that patch (or vanilla) continue. fighttesterempty has no encounter units;
	/// spawning here while fightStarted is still false Add-s them to initiative. Do not skip
	/// StartFight. See docs/issues/resolved/fight-started-empty-initiative-nre.md.
	/// </remarks>
	[HarmonyPatch(typeof(Stage), nameof(Stage.StartFight))]
	[HarmonyPriority(600)]
	internal static class EmbeddedFightStartFightSpawnPatch
	{
		/// <summary>Spawns the Lab roster when the embed is active, then lets StartFight continue.</summary>
		private static bool Prefix(Stage __instance)
		{
			if (EmbeddedFightHost.IsActive)
			{
				EmbeddedFightHost.TrySpawnRosterBeforeFight(__instance);
			}

			return true;
		}
	}
}
