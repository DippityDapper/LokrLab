using HarmonyLib;
using LokrModMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LokrLab.Patches
{
	/// <summary>Adds a "Character Lab" button to the title screen by cloning the Credits button.</summary>
	/// <remarks>Targets UIMainScreen (the actual title screen shown right after boot), not UIMainMenu (a different class for the deeper hub screen reached after save-slot selection). Clones the Credits button rather than building one from scratch, so it comes with the game's real sprites/animator/font already wired up.</remarks>
	[HarmonyPatch(typeof(UIMainScreen))]
	internal static class UIMainMenuPatches
	{
		private const string ButtonLabel = "Mods";

		/// <summary>Clones the Credits button into a new "Character Lab" button under the MainButtons container.</summary>
		/// <remarks>
		/// Button labels are TextMeshProUGUI + LocalizationComponent, not UnityEngine.UI.Text.
		/// LocalizationComponent.Start() runs a frame after Instantiate and unconditionally reapplies whatever
		/// localizationKey got copied from the source button, so a naive text edit made right after Instantiate
		/// gets silently stomped back to "Credits" one frame later -- clearing localizationKey and calling
		/// SetFinalText(...) avoids that. The real button container is a GameObject literally named
		/// "MainButtons", preferred over assuming Credits' immediate parent is the right place. Replaces
		/// button.onClick with a new UnityEvent object outright, rather than RemoveAllListeners(), since that
		/// only clears runtime listeners, not the persistent "ShowCredits" call wired in the Editor Inspector.
		/// </remarks>
		[HarmonyPatch("Start")]
		[HarmonyPostfix]
		private static void Start_Postfix(UIMainScreen __instance)
		{
			try
			{
				Button source = __instance.credits;
				if (source == null)
				{
					LokrLabPlugin.Log.LogWarning("UIMainMenuPatches: credits button not found, skipping Character Lab button.");
					return;
				}

				GameObject container = GameObject.Find("MainButtons");
				Transform parent = container != null ? container.transform : source.transform.parent;

				GameObject clone = Object.Instantiate(source.gameObject, parent);
				clone.name = "characterLabButton";
				clone.SetActive(true);
				clone.transform.SetAsLastSibling();

				RectTransform sourceRect = source.GetComponent<RectTransform>();
				RectTransform cloneRect = clone.GetComponent<RectTransform>();
				if (cloneRect != null && sourceRect != null)
				{
					cloneRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, -120f);
				}
				else
				{
					clone.transform.localPosition = source.transform.localPosition + new Vector3(0f, -120f, 0f);
				}

				LocalizationComponent localization = clone.GetComponentInChildren<LocalizationComponent>();
				if (localization != null)
				{
					localization.localizationKey = null;
					localization.SetFinalText(ButtonLabel);
				}
				else
				{
					TextMeshProUGUI tmpLabel = clone.GetComponentInChildren<TextMeshProUGUI>();
					if (tmpLabel != null)
					{
						tmpLabel.text = ButtonLabel;
					}
				}

				Button button = clone.GetComponent<Button>();
				button.onClick = new Button.ButtonClickedEvent();
				button.onClick.AddListener(() => ModMenuAPI.Toggle());

				LayoutGroup layoutGroup = parent != null ? parent.GetComponent<LayoutGroup>() : null;
				LokrLabPlugin.Log.LogInfo(string.Format(
					"CharacterLab button added under '{0}' (found by name: {1}, layoutGroup: {2}) at anchoredPos={3}",
					parent != null ? parent.name : "<null>",
					container != null,
					layoutGroup != null ? layoutGroup.GetType().Name : "<none>",
					cloneRect != null ? cloneRect.anchoredPosition.ToString() : "<no rect>"));
			}
			catch (System.Exception ex)
			{
				LokrLabPlugin.Log.LogError("UIMainMenuPatches.Start_Postfix threw: " + ex);
			}
		}
	}
}
