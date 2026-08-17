using HarmonyLib;
using LokrCharacterLab;
using LokrLab.Encounter;
using UnityEngine;

namespace LokrLab.Patches
{
	/// <summary>Points <see cref="CameraBase"/> auto-pan at the hole camera, not the lab backdrop.</summary>
	/// <remarks>
	/// Vanilla Awake stores <see cref="Camera.main"/>. During embed that is still the lab
	/// backdrop, so hex-select pans through the wrong viewport and lands on a leftover offset.
	/// </remarks>
	[HarmonyPatch(typeof(CameraBase), "Awake")]
	internal static class EmbeddedSceneCameraBasePatch
	{
		private static void Postfix(CameraBase __instance)
		{
			if (!EmbeddedSceneHost.IsActive || __instance == null)
			{
				return;
			}

			Camera self = __instance.GetComponent<Camera>();
			if (self != null)
			{
				Traverse.Create(__instance).Field<Camera>("mainCamera").Value = self;
			}
		}
	}

	/// <summary>Skips vanilla fight pan clamps in the embed unless Encounter Play authored bounds.</summary>
	/// <remarks>
	/// Drag, auto-fit, and zoom-on-action all call this. A huge <c>cameraLimits</c> is not
	/// enough — <c>LevelManager</c> and cinematics rewrite it from <c>encounterLimits</c>.
	/// Encounter Play with a <c>camera</c> object lets vanilla clamp run against those limits.
	/// </remarks>
	[HarmonyPatch(typeof(CameraBase), nameof(CameraBase.ClampTargetCameraPosition))]
	internal static class EmbeddedFightCameraClampPatch
	{
		private static bool Prefix(Vector3 pos, ref Vector3 __result)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return true;
			}

			if (EncounterCamera.ShouldClamp)
			{
				return true;
			}

			__result = pos;
			return false;
		}
	}

	/// <summary>Re-unlocks embed camera bounds after vanilla writes <c>encounterLimits</c>.</summary>
	[HarmonyPatch(typeof(CameraBase), nameof(CameraBase.SetCameraLimits))]
	internal static class EmbeddedFightSetCameraLimitsPatch
	{
		private static void Postfix(CameraBase __instance)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			if (EncounterCamera.ShouldClamp)
			{
				EncounterCamera.ApplyPlay(__instance);
				return;
			}

			SandboxFightControls.UnlockCameraBounds(__instance);
		}
	}

	/// <summary>Re-unlocks embed camera bounds after vanilla grows <c>encounterLimits</c>.</summary>
	[HarmonyPatch(typeof(CameraBase), nameof(CameraBase.GrowCameraLimits))]
	internal static class EmbeddedFightGrowCameraLimitsPatch
	{
		private static void Postfix(CameraBase __instance)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			if (EncounterCamera.ShouldClamp)
			{
				EncounterCamera.ApplyPlay(__instance);
				return;
			}

			SandboxFightControls.UnlockCameraBounds(__instance);
		}
	}

	/// <summary>Keeps embed zoom-out from collapsing to a zero <c>encounterLimits</c> AABB.</summary>
	[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SetMaxOrthosizeCamera))]
	internal static class EmbeddedFightMaxOrthoPatch
	{
		private static void Postfix(ref float __result)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			if (EncounterCamera.ShouldClamp && EncounterSandbox.File != null)
			{
				Camera hole = EncounterCamera.ResolveHoleCamera();
				float aspect = hole != null && hole.aspect > 0.01f ? hole.aspect : 1f;
				__result = EncounterCameraRules.PlayOrtho(EncounterSandbox.File.Camera, aspect);
				return;
			}

			__result = SandboxFightControls.EmbedMaxOrthoSize;
		}
	}
}
