using System.Collections.Generic;
using LokrLab;
using LokrLab.Editor.General;
using UnityEngine;

namespace LokrCharacterLab
{
	/// <summary>Legacy workstation screen switch for Home nav and CharacterCreatorAPI tabs.</summary>
	internal static class CharacterWorkstations
	{
		private static readonly HashSet<string> built = new HashSet<string>();
		private static string currentCustom;

		/// <summary>Shows a registered workstation, or the in-shell tab for Properties/Animator/Sandbox.</summary>
		internal static void Show(string name)
		{
			if (!Lab.IsOpen)
			{
				return;
			}

			CharacterCreatorAPI.WorkstationEntry entry = CharacterCreatorAPI.Find(name);
			if (entry == null)
			{
				return;
			}

			if (entry.RequiresCharacterLoaded && CharacterSession.Folder == null)
			{
				LokrCharacterLabPlugin.Log.LogWarning(
					"CharacterWorkstations: refused to open workstation '" + name + "' with no character loaded -- redirecting to the shell.");
				Lab.SwitchToShell();
				return;
			}

			if (name == "Properties" || name == "Animator" || name == "Sandbox")
			{
				Lab.SwitchToShell();
				Lab.ActivateWorkspace(name);
				return;
			}

			if (!built.Contains(name))
			{
				Transform root = Lab.GetScreenRoot(name);
				if (root == null)
				{
					return;
				}

				entry.Build(Lab.LabScene, root);
				built.Add(name);
			}

			HideCurrentCustom();
			Lab.ShowScreen(name);
			currentCustom = name;
			entry.OnShow?.Invoke();
		}

		/// <summary>Hides a custom CharacterCreatorAPI screen when the shell shows Browser or Shell.</summary>
		internal static void OnScreenShown(string name)
		{
			if (name == currentCustom)
			{
				return;
			}

			HideCurrentCustom();
		}

		/// <summary>Clears lazy-build tracking when the lab scene is torn down.</summary>
		internal static void Reset()
		{
			HideCurrentCustom();
			built.Clear();
		}

		private static void HideCurrentCustom()
		{
			if (string.IsNullOrEmpty(currentCustom))
			{
				return;
			}

			CharacterCreatorAPI.Find(currentCustom)?.OnHide?.Invoke();
			currentCustom = null;
		}
	}
}
