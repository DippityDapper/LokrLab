using System;
using HarmonyLib;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Utils;
using LokrLab;
using LokrLab.Encounter;
using LokrLabApi;
using UnityEngine;

namespace LokrCharacterLab.Patches
{
	/// <summary>Replaces vanilla edge-scroll with hole drag and wheel zoom while a fight is embedded.</summary>
	/// <remarks>
	/// Vanilla <see cref="CameraBaseMousePan"/> edge-scrolls using the full screen. In the Stage hole
	/// that pans from the monitor edges. Embed pans only by drag inside the hole; pointer outside
	/// the hole does nothing (must not fall through to vanilla). Sandbox and Setup stay
	/// unclamped. Encounter Play with an authored <c>camera</c> skips the wheel when
	/// <c>lockZoom</c> is on and clamps this drag write — vanilla
	/// <c>ClampTargetCameraPosition</c> never sees embed pan because this prefix
	/// replaces LateUpdate.
	/// </remarks>
	[HarmonyPatch(typeof(CameraBaseMousePan), "LateUpdate")]
	internal static class EmbeddedFightCameraPatches
	{
		private static Vector3 lastDrag;

		/// <summary>Clears drag state so a new Stage does not inherit the last pan delta.</summary>
		internal static void ResetDrag()
		{
			lastDrag = Vector3.zero;
		}

		private static bool Prefix(CameraBaseMousePan __instance)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return true;
			}

			Traverse traverse = Traverse.Create(__instance);
			if (traverse.Field<bool>("fightEnded").Value)
			{
				return false;
			}

			CameraBase cameraBase = traverse.Field<CameraBase>("cameraBase").Value;
			if (cameraBase == null || !MonoSingleton<CameraBase>.IsInstanceValid)
			{
				return false;
			}

			Camera camera = ResolveHoleCamera(cameraBase);
			if (camera == null)
			{
				return false;
			}

			Rect hole = camera.pixelRect;
			Vector3 mouse = Input.mousePosition;
			if (hole.Contains((Vector2)mouse) && !EncounterCamera.LockZoom)
			{
				ApplyWheelZoom(camera, cameraBase);
			}

			bool paintOwnsRight = EncounterEdit.IsArmed
				&& (EncounterEdit.Tool == EncounterEditTool.DrawHex
					|| EncounterEdit.Tool == EncounterEditTool.PaintTerrain
					|| EncounterEdit.Tool == EncounterEditTool.Trigger);
			if ((!Input.GetMouseButton(1) || paintOwnsRight) && !Input.GetMouseButton(2))
			{
				EncounterCamera.ClampPlayPosition(cameraBase, camera);
				return false;
			}

			if (!hole.Contains((Vector2)mouse))
			{
				return false;
			}

			if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
			{
				lastDrag = mouse;
			}

			Vector3 from = camera.ScreenToWorldPoint(new Vector3(lastDrag.x, lastDrag.y, 10f));
			Vector3 to = camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 10f));
			cameraBase.transform.position = cameraBase.transform.position - (to - from);
			cameraBase.loseControlToDragging = true;
			lastDrag = mouse;
			EncounterCamera.ClampPlayPosition(cameraBase, camera);
			return false;
		}

		/// <summary>Scroll-wheel zoom toward the cursor, then holds auto-fit so it does not undo the size.</summary>
		private static void ApplyWheelZoom(Camera camera, CameraBase cameraBase)
		{
			float scroll = Input.mouseScrollDelta.y;
			if (Mathf.Abs(scroll) < 0.01f)
			{
				return;
			}

			Vector3 mouse = Input.mousePosition;
			Vector3 worldBefore = camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 10f));
			float min = SandboxFightControls.EmbedMinOrthoSize;
			float max = SandboxFightControls.EmbedMaxOrthoSize;
			if (EncounterCamera.ShouldClamp && EncounterSandbox.File != null)
			{
				float aspect = camera.aspect > 0.01f ? camera.aspect : 1f;
				max = EncounterCameraRules.FitOrtho(EncounterSandbox.File.Camera, aspect);
			}

			float next = Mathf.Clamp(
				camera.orthographicSize * Mathf.Pow(0.9f, scroll),
				min,
				max);
			camera.orthographicSize = next;
			Vector3 worldAfter = camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 10f));
			cameraBase.transform.position += worldBefore - worldAfter;
			cameraBase.loseControlToDragging = true;
			EncounterCamera.ClampPlayPosition(cameraBase, camera);
		}

		private static Camera ResolveHoleCamera(CameraBase cameraBase)
		{
			Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			Camera fromHost = getter != null ? getter() : null;
			if (fromHost != null)
			{
				return fromHost;
			}

			return cameraBase != null ? cameraBase.GetComponent<Camera>() : null;
		}
	}

	/// <summary>Skips vanilla camera follow when Stage or the hex view is already gone.</summary>
	/// <remarks>
	/// Stop nulls <see cref="Stage.instance"/> while <see cref="UnloadSceneAsync"/> is still
	/// finishing. Vanilla LateUpdate then NREs on initiative / target hex.
	/// </remarks>
	[HarmonyPatch(typeof(GameplayCamera), nameof(GameplayCamera.LateUpdate))]
	internal static class EmbeddedFightGameplayCameraPatch
	{
		private static bool Prefix(GameplayCamera __instance)
		{
			if (__instance == null || __instance.cameraBase == null)
			{
				return false;
			}

			if (!Stage.IsInstanceValid || Stage.instance == null
				|| Stage.instance.initiative == null
				|| Stage.instance.initiative.ActiveUnit == null)
			{
				return false;
			}

			return true;
		}

		private static void Postfix(GameplayCamera __instance)
		{
			if (!EncounterCamera.ShouldClamp || __instance == null)
			{
				return;
			}

			EncounterCamera.ClampPlayPosition(__instance.cameraBase, EncounterCamera.ResolveHoleCamera());
		}
	}

	/// <summary>Skips fight-nav Update when Stage was cleared for embed unload.</summary>
	[HarmonyPatch(typeof(UIFightNavProxy), "Update")]
	internal static class EmbeddedFightNavProxyPatch
	{
		private static bool Prefix()
		{
			return Stage.IsInstanceValid && Stage.instance != null && Stage.instance.board != null;
		}
	}
}
