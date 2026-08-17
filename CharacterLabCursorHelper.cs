using UnityEngine;

namespace LokrLab
{
	/// <summary>Keeps the OS mouse cursor visible while Character Lab is open.</summary>
	/// <remarks>
	/// Defensive only: the lab's own scene is now the sole loaded scene while open (the scene the player was
	/// in is genuinely unloaded, not just hidden underneath — see CharacterLabScene's own remarks), so there's
	/// no foreign game logic left fighting over cursor visibility the way there was under the old overlay model.
	/// Kept as a cheap safeguard in case anything in the lab's own UI ever hides the OS cursor unexpectedly.
	/// </remarks>
	internal sealed class CharacterLabCursorHelper : MonoBehaviour
	{
		private void LateUpdate()
		{
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
		}
	}
}
