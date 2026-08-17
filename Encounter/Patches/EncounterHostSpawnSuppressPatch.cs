using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLab;
using System.Collections.Generic;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Stops the host <c>template</c> room from spawning its own baked-in units and props during an Encounter embed.</summary>
	/// <remarks>
	/// <c>EncounterRoster.Spawn</c> already places every <c>EncounterFileModel.Combatants</c>/<c>Props</c>
	/// entry -- for a room imported by <c>VanillaEncounterImporter</c>, that list is a copy of the same
	/// room's own <c>EncounterDefinition.encounterData</c>. Left unpatched, vanilla's own
	/// <c>LevelManager</c> load path still calls <c>InitializeEncounter</c> (enemies/cinematic/hero
	/// units) and <c>Initialize</c>'s private <c>InitializeObjects</c> (props) on that same
	/// <c>EncounterDefinition</c>, so both the vanilla and the Lab copy spawn on the same hexes --
	/// the doubled-unit bug reported after the first Vanilla Encounter Import test. Campaign fights
	/// are unaffected: <see cref="EmbeddedFightHost.EncounterOwnsEmbed"/> is false outside Setup/Sandbox.
	/// </remarks>
	[HarmonyPatch(typeof(EncounterDefinition), nameof(EncounterDefinition.InitializeEncounter))]
	internal static class EncounterHostSpawnSuppressPatch
	{
		private static bool Prefix(List<Unit> heroes, bool isFirstEncounter)
		{
			return !EmbeddedFightHost.IsActive || !EmbeddedFightHost.EncounterOwnsEmbed;
		}
	}

	/// <summary>Stops the host <c>template</c> room from instantiating its own baked-in <c>encounterObjs</c> (props) during an Encounter embed.</summary>
	/// <remarks>
	/// Companion to <see cref="EncounterHostSpawnSuppressPatch"/> -- see its remarks. Patches the
	/// private <c>InitializeObjects</c> rather than the public <c>Initialize(Vector2)</c> so the
	/// room's <c>gridPos</c>/<c>transformWithData</c> setup (still needed for terrain) is untouched.
	/// </remarks>
	[HarmonyPatch(typeof(EncounterDefinition), "InitializeObjects")]
	internal static class EncounterHostObjectSpawnSuppressPatch
	{
		private static bool Prefix()
		{
			return !EmbeddedFightHost.IsActive || !EmbeddedFightHost.EncounterOwnsEmbed;
		}
	}
}
