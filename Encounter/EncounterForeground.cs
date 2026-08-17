using Ironhide.Legends.View.Paralax;
using LokrCharacterLab;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Hides vanilla fight Paralax foliage while the Lab embed camera is unclamped.</summary>
	/// <remarks>
	/// Campaign rooms keep the four-layer foreground (leaves, vines) because the
	/// fight camera stays clamped. The embed unclamps pan and zoom, so those
	/// layers sit in the wrong place. Vanilla debug already does
	/// <c>GameObject.Find("Paralax").SetActive(false)</c>. Do not destroy;
	/// scene unload tears the objects down. Do not touch EncounterBkgDefinition
	/// or the Tilemap.
	/// </remarks>
	internal static class EncounterForeground
	{
		/// <summary>Deactivates the Paralax root and each layer GameObject.</summary>
		internal static void Hide()
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return;
			}

			ParalaxEditorManager manager = Object.FindObjectOfType<ParalaxEditorManager>();
			if (manager != null)
			{
				if (manager.layersConfig != null)
				{
					for (int i = 0; i < manager.layersConfig.Count; i++)
					{
						ParalaxEditorManager.ParalaxLayerConfig config = manager.layersConfig[i];
						if (config != null && config.go != null)
						{
							config.go.SetActive(false);
						}
					}
				}

				manager.enabled = false;
				if (manager.gameObject != null)
				{
					manager.gameObject.SetActive(false);
				}
			}

			GameObject root = GameObject.Find("Paralax");
			if (root != null)
			{
				root.SetActive(false);
			}

			ParalaxLayer[] layers = Object.FindObjectsOfType<ParalaxLayer>();
			if (layers == null)
			{
				return;
			}

			for (int i = 0; i < layers.Length; i++)
			{
				ParalaxLayer layer = layers[i];
				if (layer == null)
				{
					continue;
				}

				layer.enabled = false;
				if (layer.gameObject != null)
				{
					layer.gameObject.SetActive(false);
				}
			}
		}
	}
}
