using SimpleUI;
using UnityEngine;

namespace LokrLab
{
	/// <summary>Character Lab UI theme tweaks (nested panels inside the content shell).</summary>
	internal static class CharacterLabUi
	{
		/// <summary>Theme for column panels inside the main content shell — lighter than the shell itself.</summary>
		internal static readonly UiTheme NestedPanelTheme;

		static CharacterLabUi()
		{
			NestedPanelTheme = new UiTheme();
			NestedPanelTheme.PanelBackground = new Color(0.1f, 0.11f, 0.14f, 0.55f);
		}
	}
}
