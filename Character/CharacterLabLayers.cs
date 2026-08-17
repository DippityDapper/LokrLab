using UnityEngine;

namespace LokrLab
{
	/// <summary>Unity layer used to isolate Character Lab viewport content from the live game scene.</summary>
	/// <remarks>
	/// Viewport cameras use <see cref="ViewportMask"/> so they never draw foreign particles, sprites, or meshes
	/// that stay active under the overlay. Layer 31 works without TagManager entries — culling is by bit index.
	/// </remarks>
	internal static class CharacterLabLayers
	{
		internal const int Viewport = 31;

		internal static readonly int ViewportMask = 1 << Viewport;

		internal static void ApplyToHierarchy(GameObject root)
		{
			if (root == null)
			{
				return;
			}

			ApplyToHierarchy(root.transform);
		}

		internal static void ApplyToHierarchy(Transform root)
		{
			root.gameObject.layer = Viewport;
			for (int i = 0; i < root.childCount; i++)
			{
				ApplyToHierarchy(root.GetChild(i));
			}
		}
	}
}
