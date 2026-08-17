using LokrLabApi;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LokrLab
{
	/// <summary>Forwards to <see cref="LokrLabApi.LokrLabApi.Host"/> so Character code never references LokrLab.dll.</summary>
	internal static class Lab
	{
		private static LabHost Host => LokrLabApi.LokrLabApi.Host;

		/// <summary>Built-in Arial used by lab chrome.</summary>
		internal static Font DefaultFont => Host != null ? Host.DefaultFont : null;

		/// <summary>The in-memory lab scene.</summary>
		internal static Scene LabScene => Host != null ? Host.LabScene : default(Scene);

		/// <summary>Full-screen backdrop camera.</summary>
		internal static Camera BackdropCamera => Host != null ? Host.BackdropCamera : null;

		/// <summary>Root canvas.</summary>
		internal static Transform Canvas => Host != null ? Host.Canvas : null;

		/// <summary>Real scene the lab was opened from.</summary>
		internal static string OriginScene => Host != null ? Host.OriginScene : null;

		/// <summary>Active workspace name, or empty.</summary>
		internal static string ActiveWorkspaceName
		{
			get
			{
				return Host != null && Host.GetActiveWorkspaceName != null ? Host.GetActiveWorkspaceName() : string.Empty;
			}
		}

		/// <summary>True while the lab scene is showing.</summary>
		internal static bool IsOpen => Host != null && Host.IsOpen != null && Host.IsOpen();

		/// <summary>Switches the in-shell workspace tab.</summary>
		internal static void ActivateWorkspace(string name)
		{
			Host?.ActivateWorkspace?.Invoke(name);
		}

		/// <summary>Focuses a bottom-dock tab by display name.</summary>
		internal static bool FocusBottomPanel(string name)
		{
			return Host != null && Host.FocusBottomPanel != null && Host.FocusBottomPanel(name);
		}

		/// <summary>Focuses a dock panel by id.</summary>
		internal static void FocusPanel(string id)
		{
			Host?.FocusPanel?.Invoke(id);
		}

		/// <summary>Sets the status-bar left text.</summary>
		internal static void SetStatus(string message)
		{
			Host?.SetStatus?.Invoke(message);
		}

		/// <summary>Selects a Node Tree row by id.</summary>
		internal static bool SelectNodeById(string id)
		{
			return Host != null && Host.SelectNodeById != null && Host.SelectNodeById(id);
		}

		/// <summary>Closes the lab into a real scene.</summary>
		internal static void CloseTo(string sceneName)
		{
			Host?.CloseTo?.Invoke(sceneName);
		}

		/// <summary>Rebuilds the lab after a sandbox fight.</summary>
		internal static void ReopenAfterFight(string preservedOrigin)
		{
			Host?.ReopenAfterFight?.Invoke(preservedOrigin, "Sandbox");
		}

		/// <summary>Closes the open project.</summary>
		internal static void CloseProject()
		{
			Host?.CloseProject?.Invoke();
		}

		/// <summary>Closes the lab.</summary>
		internal static void CloseLab()
		{
			Host?.CloseLab?.Invoke();
		}

		/// <summary>Shows the dockable shell.</summary>
		internal static void SwitchToHome()
		{
			Host?.SwitchToHome?.Invoke();
		}

		/// <summary>Shows the Project Browser.</summary>
		internal static void SwitchToLoad()
		{
			Host?.SwitchToLoad?.Invoke();
		}

		/// <summary>Shows the dockable shell for the current project.</summary>
		internal static void SwitchToShell()
		{
			Host?.SwitchToShell?.Invoke();
		}

		/// <summary>Shows a named lab screen.</summary>
		internal static void ShowScreen(string name)
		{
			Host?.ShowScreen?.Invoke(name);
		}

		/// <summary>Named screen root.</summary>
		internal static Transform GetScreenRoot(string name)
		{
			return Host != null && Host.GetScreenRoot != null ? Host.GetScreenRoot(name) : null;
		}

		/// <summary>Legacy workstation content frame.</summary>
		internal static Transform GetWorkstationContentRoot(Transform screenRoot)
		{
			return Host != null && Host.GetWorkstationContentRoot != null
				? Host.GetWorkstationContentRoot(screenRoot)
				: null;
		}

		/// <summary>Shows Help → About.</summary>
		internal static void ShowAbout()
		{
			Host?.ShowAbout?.Invoke();
		}

		/// <summary>Forces the inspector to rebuild on the next refresh.</summary>
		internal static void InvalidateInspector()
		{
			Host?.InvalidateInspector?.Invoke();
		}

		/// <summary>Returns from a cross-project jump.</summary>
		internal static void ReturnFromJump()
		{
			LokrLabApi.LokrLabApi.ReturnToPreviousProject?.Invoke();
		}
	}
}
