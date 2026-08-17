using UnityEngine;
using UnityEngine.SceneManagement;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The Load workstation's own orchestrator, the screen the Lab opens on. Only job: create a new character, load an existing one, or import a legacy mod, each handing off to HomeWorkstationScene (the state owner) and switching straight to Home.</summary>
	internal static class LoadWorkstationScene
	{
		/// <summary>Builds the character list, folder scan, and legacy-import panels.</summary>
		internal static void Build(Scene scene, Transform screenRoot)
		{
			Transform contentRoot = Lab.GetWorkstationContentRoot(screenRoot);
			CharacterListPanel.Build(contentRoot, Lab.DefaultFont);
			CharacterFolderScanPanel.Build(contentRoot, Lab.DefaultFont);
			LegacyModImportPanel.Build(screenRoot, Lab.DefaultFont);

			Refresh();
		}

		/// <summary>Refreshes both Load panels (recents and on-disk folder scan).</summary>
		internal static void Refresh()
		{
			CharacterListPanel.Refresh();
			CharacterFolderScanPanel.Refresh();
		}
	}
}
