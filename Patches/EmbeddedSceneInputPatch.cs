using System.Collections.Generic;
using HarmonyLib;
using Ironhide.Legends.Controller.Game.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LokrLab.Patches
{
	/// <summary>Shared hole-pointer tests for embed input patches.</summary>
	internal static class EmbeddedScenePointer
	{
		/// <summary>True when the pointer is inside the gameplay camera hole.</summary>
		internal static bool OverHole(Vector2 screen)
		{
			if (!EmbeddedSceneHost.IsActive || !EmbeddedSceneHost.EmbeddedScene.IsValid())
			{
				return false;
			}

			Camera camera = EmbeddedSceneHost.GameplayCamera;
			return camera != null && camera.pixelRect.Contains(screen);
		}

		/// <summary>True when this hit is lab Overlay chrome or the Ability Lab hole catcher.</summary>
		/// <remarks>
		/// Fight HUD that leaked into the lab scene must not be stripped — otherwise hex input
		/// steals skill and confirm clicks after the first Start.
		/// </remarks>
		internal static bool IsLabOverlayHit(GameObject gameObject)
		{
			if (gameObject == null)
			{
				return true;
			}

			if (IsFightHudControl(gameObject))
			{
				return false;
			}

			if (EmbeddedSceneHost.IsLabCanvas(gameObject.GetComponentInParent<Canvas>(), EmbeddedSceneHost.LabScene))
			{
				return true;
			}

			return EmbeddedSceneHost.LabScene.IsValid() && gameObject.scene == EmbeddedSceneHost.LabScene;
		}

		/// <summary>True when the hit is a real fight control (skill, End Turn, settings, forfeit confirm, debug), not an empty canvas.</summary>
		internal static bool IsFightHudControl(GameObject gameObject)
		{
			Transform transform = gameObject != null ? gameObject.transform : null;
			while (transform != null)
			{
				if (transform.GetComponent<Icon>() != null || transform.GetComponent<ConfirmButton>() != null
					|| transform.GetComponent<EndTurn>() != null
					|| transform.GetComponent<UISimpleModalDialog>() != null
					|| transform.GetComponent<UIOptions>() != null)
				{
					return true;
				}

				if (HasComponentNamed(transform, "UIFightNavProxy"))
				{
					return false;
				}

				if (transform.GetComponent<Selectable>() != null)
				{
					return true;
				}

				MonoBehaviour[] behaviours = transform.GetComponents<MonoBehaviour>();
				for (int i = 0; i < behaviours.Length; i++)
				{
					if (behaviours[i] is IPointerClickHandler || behaviours[i] is IPointerDownHandler
						|| behaviours[i] is IDragHandler)
					{
						return true;
					}
				}

				transform = transform.parent;
			}

			return false;
		}

		private static bool HasComponentNamed(Transform transform, string typeName)
		{
			return transform != null && transform.GetComponent(typeName) != null;
		}
	}

	/// <summary>Drops lab Overlay hits over the hole so fight HUD can be the EventSystem target.</summary>
	/// <remarks>
	/// Must not strip empty fight-canvas hits. LeanTouch.Update does <c>First()</c> on RaycastAll
	/// results; an empty list throws and hex movement dies.
	/// </remarks>
	[HarmonyPatch(typeof(EventSystem), nameof(EventSystem.RaycastAll))]
	internal static class EmbeddedSceneInputPatch
	{
		private static void Postfix(PointerEventData eventData, List<RaycastResult> raycastResults)
		{
			if (raycastResults == null || raycastResults.Count == 0)
			{
				return;
			}

			Vector2 position = eventData != null ? eventData.position : (Vector2)Input.mousePosition;
			if (!EmbeddedScenePointer.OverHole(position))
			{
				return;
			}

			for (int i = raycastResults.Count - 1; i >= 0; i--)
			{
				if (EmbeddedScenePointer.IsLabOverlayHit(raycastResults[i].gameObject))
				{
					raycastResults.RemoveAt(i);
				}
			}
		}
	}

	/// <summary>Treats an empty hole as not-UI so LeanTouch hex taps are not discarded.</summary>
	[HarmonyPatch(typeof(EventSystem), "IsPointerOverGameObject", new System.Type[0])]
	internal static class EmbeddedScenePointerOverPatch
	{
		private static void Postfix(EventSystem __instance, ref bool __result)
		{
			if (!__result || __instance == null || !EmbeddedScenePointer.OverHole(Input.mousePosition))
			{
				return;
			}

			PointerEventData pointer = new PointerEventData(__instance) { position = Input.mousePosition };
			List<RaycastResult> hits = new List<RaycastResult>();
			__instance.RaycastAll(pointer, hits);
			__result = HasFightHudHit(hits);
		}

		internal static bool HasFightHudHit(List<RaycastResult> hits)
		{
			if (hits == null)
			{
				return false;
			}

			for (int i = 0; i < hits.Count; i++)
			{
				if (EmbeddedScenePointer.IsFightHudControl(hits[i].gameObject))
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>Same empty-hole rule as pointer-over, for <c>CheckIfTouchedOnUI</c> hex rejection.</summary>
	[HarmonyPatch(typeof(ActivityController), nameof(ActivityController.CheckIfTouchedOnUI))]
	internal static class EmbeddedSceneTouchedOnUiPatch
	{
		private static void Postfix(ref bool __result)
		{
			if (!__result || !EmbeddedScenePointer.OverHole(Input.mousePosition))
			{
				return;
			}

			EventSystem eventSystem = EventSystem.current;
			if (eventSystem == null)
			{
				__result = false;
				return;
			}

			PointerEventData pointer = new PointerEventData(eventSystem) { position = Input.mousePosition };
			List<RaycastResult> hits = new List<RaycastResult>();
			eventSystem.RaycastAll(pointer, hits);
			__result = EmbeddedScenePointerOverPatch.HasFightHudHit(hits);
		}
	}
}
