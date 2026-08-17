using HarmonyLib;

namespace LokrLab.Patches
{
	/// <summary>Skips vanilla <see cref="AspectUtility.SetCamera"/> while a scene is embedded in a hole.</summary>
	/// <remarks>Vanilla writes a full-screen (or 16:9) <c>Camera.rect</c> that undoes the hole crop.</remarks>
	[HarmonyPatch(typeof(AspectUtility), nameof(AspectUtility.SetCamera))]
	internal static class EmbeddedSceneAspectPatch
	{
		private static bool Prefix()
		{
			return !EmbeddedSceneHost.IsActive;
		}
	}
}
