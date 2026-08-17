using HarmonyLib;
using Ironhide.Legends.Model.Game;
using LokrCharacterLab;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Stops an empty or preview-only Setup board from auto-winning or auto-losing.</summary>
	/// <remarks>
	/// Vanilla <see cref="Stage.CheckFightEnd"/> treats living GoodSide == 0 as a loss. Setup
	/// may show zero combatants. Gate on embed-active plus <see cref="EncounterEdit.IsArmed"/>
	/// so Play, Sandbox, and campaign fights stay vanilla.
	/// </remarks>
	[HarmonyPatch(typeof(Stage), nameof(Stage.CheckFightEnd))]
	internal static class EncounterEditFightEndPatch
	{
		/// <summary>Returns false and skips the original while Encounter Setup owns the embed.</summary>
		private static bool Prefix(ref bool __result)
		{
			if (!EmbeddedFightHost.IsActive || !EncounterEdit.IsArmed)
			{
				return true;
			}

			__result = false;
			return false;
		}
	}
}
