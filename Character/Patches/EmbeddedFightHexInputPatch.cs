using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Battlechest.Common.Game.Gameboard;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Controller.Game;
using Ironhide.Legends.Controller.Game.Units;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.View.Game.Units;
using Lean.Touch;
using LokrLab.Encounter;
using LokrLabApi;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LokrCharacterLab.Patches
{
	/// <summary>Maps hole-camera screen taps to hexes. Vanilla LeanTouch uses <see cref="Camera.main"/> and a Physics2D ray that misses in the embed.</summary>
	/// <remarks>
	/// <c>GetWorldPosition(1f)</c> and <c>GetRay(null)</c> assume a fullscreen main camera plus a
	/// ground collider hit. The cropped hole camera plus additive load often yields point (0,0),
	/// so visible walk hexes never select. Setup edit uses unclamped <c>PointToHex</c> via
	/// <c>TryHandleWorld</c> so off-board taps grow to the cursor instead of the edge.
	/// After Stop then Start, fight HUD often lives in the lab scene;
	/// <c>IsPointerOverGameObject</c> then misses it and this prefix would steal skill
	/// / confirm taps. Icon hits skip the original method so EventSystem can deliver <c>OnTap</c>.
	/// </remarks>
	[HarmonyPatch(typeof(UnitController), "OnFingerTap")]
	internal static class EmbeddedFightHexInputPatch
	{
		private static bool Prefix(UnitController __instance)
		{
			if (!EmbeddedFightHost.IsActive || __instance == null)
			{
				return true;
			}

			if (IsNonLeftMouseTap())
			{
				// LeanTouch's mouse-simulated finger fires OnFingerTap for right/middle clicks
				// too, not just left. Those buttons drive camera pan / paint-erase in the embed
				// (EmbeddedFightCameraPatches, EncounterEdit's right-drag erase) — without this
				// guard, panning with the middle button also selects, places, or moves a unit
				// underneath the drag.
				return false;
			}

			if (EventSystem.current != null)
			{
				PointerEventData pointer = new PointerEventData(EventSystem.current)
				{
					position = Input.mousePosition,
				};
				List<RaycastResult> hits = new List<RaycastResult>();
				EventSystem.current.RaycastAll(pointer, hits);
				for (int i = 0; i < hits.Count; i++)
				{
					GameObject hit = hits[i].gameObject;
					if (hit != null && (hit.GetComponentInParent<Icon>() != null
						|| hit.GetComponentInParent<ConfirmButton>() != null
						|| hit.GetComponentInParent<EndTurn>() != null))
					{
						return false;
					}
				}

				if (EventSystem.current.IsPointerOverGameObject())
				{
					return true;
				}
			}

			bool editArmed = EncounterEdit.IsArmed;
			if (!editArmed
				&& Traverse.Create(__instance).Field<ActivityController>("activeActivityController").Value == null)
			{
				return false;
			}

			Camera camera = ResolveHoleCamera();
			Vector2 screen = Input.mousePosition;
			if (camera == null || !camera.pixelRect.Contains(screen))
			{
				return false;
			}

			float depth = Mathf.Abs(camera.transform.position.z);
			if (depth < 0.01f)
			{
				depth = 10f;
			}

			Vector3 world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
			world.z = 0f;

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				if (editArmed)
				{
					return false;
				}

				__instance.SelectPoint(world, false);
				return false;
			}

			if (editArmed)
			{
				EncounterEdit.TryHandleWorld(world);
				return false;
			}

			HexGridItem<GameHexGridItemData> hex = stage.board.PointToHexItem(world);

			if (hex != null && stage.units != null)
			{
				for (int i = 0; i < stage.units.Count; i++)
				{
					Unit unit = stage.units[i];
					if (unit == null || unit.unitView == null || unit.HexGridItem != hex)
					{
						continue;
					}

					if (unit.states != null && (unit.states.IsOn("DEAD") || unit.states.IsOn("NO_COLLISION")))
					{
						continue;
					}

					__instance.SelectUnit(unit.unitView, false);
					return false;
				}
			}

			__instance.SelectPoint(world, false);
			return false;
		}

		/// <summary>True when a mouse is involved and the tap wasn't caused by the left button.</summary>
		/// <remarks>
		/// Pure touch input (no mouse buttons down or just released) falls through unfiltered.
		/// </remarks>
		private static bool IsNonLeftMouseTap()
		{
			bool leftRelease = Input.GetMouseButtonUp(0);
			bool anyMouseButton = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)
				|| Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2);
			return anyMouseButton && !leftRelease;
		}

		private static Camera ResolveHoleCamera()
		{
			System.Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			return getter != null ? getter() : Camera.main;
		}
	}
}
