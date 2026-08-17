using UnityEngine;

namespace LokrLab
{
	/// <summary>Retries hole bind if <c>sceneLoaded</c> missed, and starts a request queued behind unload.</summary>
	/// <remarks>
	/// The fight can finish booting (HUD, <c>OnReady</c>) even when the shell never saw
	/// <c>sceneLoaded</c>. Without a bind, <c>WorldCamera</c> stays fullscreen behind the lab
	/// and the hole shows only the black backdrop.
	/// </remarks>
	internal sealed class EmbeddedSceneWatchdog : MonoBehaviour
	{
		private void LateUpdate()
		{
			EmbeddedSceneHost.Tick();
		}
	}
}
