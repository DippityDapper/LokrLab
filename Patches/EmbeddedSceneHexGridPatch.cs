using HarmonyLib;
using Ironhide.Battlechest.Client.View;
using UnityEngine;

namespace LokrLab.Patches
{
	/// <summary>Keeps <c>HexGridRoot</c> in the fight scene so Stop can unload it.</summary>
	/// <remarks>
	/// Vanilla <see cref="HexBoardViewComponent.Awake"/> does <c>new GameObject("HexGridRoot")</c>
	/// in the active scene. Additive load often still has the lab active, so the grid lives in
	/// the lab and survives unload. The next Stage then SetBoards a leftover grid and hexes drift.
	/// </remarks>
	[HarmonyPatch(typeof(HexBoardViewComponent), "Awake")]
	internal static class EmbeddedSceneHexGridPatch
	{
		private static void Postfix(HexBoardViewComponent __instance)
		{
			if (__instance == null)
			{
				return;
			}

			GameObject root = Traverse.Create(__instance).Field<GameObject>("gridRoot").Value;
			if (root == null || __instance.transform == null)
			{
				return;
			}

			root.transform.SetParent(__instance.transform, false);
		}
	}
}
