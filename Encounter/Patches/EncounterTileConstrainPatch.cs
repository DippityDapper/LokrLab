using HarmonyLib;
using LokrCharacterLab;

namespace LokrLab.Encounter.Patches
{
	/// <summary>Stops vanilla Tilemap crop from deleting Lab-painted floor tiles outside the template rect.</summary>
	/// <remarks>
	/// <c>TileTestController.ConstrainMap</c> nulls cells past its serialized width/height.
	/// A grown board paints past that. Skip the crop while Setup, Play, or a
	/// Sandbox-loaded Encounter owns the embed; campaign fights keep vanilla.
	/// </remarks>
	[HarmonyPatch(typeof(TileTestController), nameof(TileTestController.ConstrainMap), typeof(bool))]
	internal static class EncounterTileConstrainPatch
	{
		private static bool Prefix()
		{
			return !EmbeddedFightHost.IsActive || !EmbeddedFightHost.EncounterOwnsEmbed;
		}
	}
}
