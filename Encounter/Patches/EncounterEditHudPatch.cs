using HarmonyLib;
using Ironhide.Legends.Controller.Game;
using LokrCharacterLab;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Skips fight HUD (initiative + skills) while Encounter Setup is showing a preview.</summary>
	/// <remarks>
	/// <c>UnitViewComponent.Start</c> calls <see cref="StageControllerComponent.AddUnitView"/>,
	/// which instantiates <c>PortraitInitiative</c> and NREs in <c>SetInitiativeType</c> on a
	/// custom-rig unit. Setup does not need that chrome; the unit view still spawns on the board.
	/// </remarks>
	[HarmonyPatch(typeof(StageControllerComponent), nameof(StageControllerComponent.AddUnitView))]
	internal static class EncounterEditHudPatch
	{
		/// <summary>Skips the original while the Setup board is armed.</summary>
		private static bool Prefix()
		{
			return !EmbeddedFightHost.IsActive || !EncounterEdit.IsArmed;
		}
	}
}
