using HarmonyLib;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLab;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Stops a still-parked exploration pocket from causing a false instant win.</summary>
	/// <remarks>
	/// Vanilla <see cref="Stage.CheckFightEnd"/> excludes <c>NOT_IN_INITIATIVE_BAR</c> units from
	/// its living-BadSide count. During exploration that is correct for parked units that have
	/// simply not aggroed yet, but it means a fully-wiped active pocket reads as
	/// "BadSide == 0" and declares victory while other pockets are still parked-but-alive
	/// elsewhere on the board. This is stateless per-tick: it only overrides that one case, and
	/// only for units <see cref="EncounterExploration"/> actually parked — a unit that ships
	/// permanently <c>NOT_IN_INITIATIVE_BAR</c> from its own definition (dummy / hazard / trap
	/// kinds) is never tracked, so it never blocks victory here. A party wipe uses the same
	/// living-GoodSide filter as vanilla (DEAD, summons, NO_CHECK_END_FIGHT, and parked-unless-
	/// PREVENT_END_FIGHT do not count), so a dead hero still ends the fight. Once no
	/// exploration-tracked unit is alive-and-parked, this patch is a no-op.
	/// </remarks>
	[HarmonyPatch(typeof(Stage), nameof(Stage.CheckFightEnd))]
	internal static class EncounterExplorationFightEndPatch
	{
		private const string DeadState = "DEAD";
		private const string NoCheckEnd = "NO_CHECK_END_FIGHT";
		private const string Summoned = "SUMMONED";
		private const string NotInInitiative = "NOT_IN_INITIATIVE_BAR";
		private const string PreventEnd = "PREVENT_END_FIGHT";

		private static bool Prefix(Stage __instance, ref bool __result)
		{
			if (!EmbeddedFightHost.IsActive || __instance == null || __instance.units == null)
			{
				return true;
			}

			if (!EncounterExploration.HasLivingParkedMembers())
			{
				return true;
			}

			if (!HasCountedLivingGoodSide(__instance))
			{
				return true;
			}

			__result = false;
			return false;
		}

		/// <summary>True when vanilla <see cref="Stage.CheckFightEnd"/> would still count a living GoodSide unit.</summary>
		private static bool HasCountedLivingGoodSide(Stage stage)
		{
			for (int i = 0; i < stage.units.Count; i++)
			{
				Unit unit = stage.units[i];
				if (unit == null || unit.unitGroup != UnitGroup.GoodSide || unit.states == null)
				{
					continue;
				}

				if (unit.states.IsOn(DeadState) || unit.states.IsOn(NoCheckEnd) || unit.states.IsOn(Summoned))
				{
					continue;
				}

				if (unit.states.IsOn(NotInInitiative) && !unit.states.IsOn(PreventEnd))
				{
					continue;
				}

				return true;
			}

			return false;
		}
	}
}
