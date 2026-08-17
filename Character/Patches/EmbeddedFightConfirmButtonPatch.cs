using HarmonyLib;
using Ironhide.Legends.Controller.Game;
using Ironhide.Legends.View.Hud;
using LokrLabApi;
using UnityEngine;

namespace LokrCharacterLab.Patches
{
	/// <summary>Points the walk / confirm icon at the live fight <see cref="UnitController"/>.</summary>
	/// <remarks>
	/// <see cref="ConfirmButton.Start"/> caches <c>FindObjectOfType&lt;UnitController&gt;()</c>.
	/// A leftover button after Stop still talks to the destroyed controller, so the icon looks
	/// fine but does nothing. Hex clicks use the new controller and still work.
	/// </remarks>
	[HarmonyPatch(typeof(ConfirmButton), nameof(ConfirmButton.OnTap))]
	internal static class EmbeddedFightConfirmButtonPatch
	{
		private static void Prefix(ConfirmButton __instance)
		{
			if (!EmbeddedFightHost.IsActive || __instance == null)
			{
				return;
			}

			UnitController live = Object.FindObjectOfType<UnitController>();
			if (live != null)
			{
				Traverse.Create(__instance).Field<UnitController>("unitController").Value = live;
			}

			TargetInteractionView view = Object.FindObjectOfType<TargetInteractionView>();
			if (view != null)
			{
				Traverse.Create(__instance).Field<TargetInteractionView>("targetView").Value = view;
			}
		}
	}

	/// <summary>Rebinds confirm canvases after vanilla points them at <see cref="Camera.main"/>.</summary>
	/// <remarks>
	/// After Stop, <c>Camera.main</c> is the lab camera. GraphicRaycaster then misses the
	/// WorldSpace boot even though the hole camera still draws it.
	/// </remarks>
	[HarmonyPatch(typeof(TargetInteractionView), "Awake")]
	internal static class EmbeddedFightConfirmCanvasPatch
	{
		private static void Postfix()
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			System.Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			LokrLab.SandboxFightControls.BindConfirmCanvases(getter != null ? getter() : null);
		}
	}

	/// <summary>Rebinds confirm canvases when the boot is shown, not only on Awake.</summary>
	[HarmonyPatch(typeof(TargetInteractionView), "ShowConfirmButtons")]
	internal static class EmbeddedFightConfirmShowPatch
	{
		private static void Postfix()
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			System.Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			LokrLab.SandboxFightControls.BindConfirmCanvases(getter != null ? getter() : null);
		}
	}
}
