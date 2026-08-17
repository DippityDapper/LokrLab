using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>A reference-frame outline shown in the viewport only while editing the "Portrait" (or "StandStatic") clip -- the size/position a character's Portrait pose needs to roughly fill to look correctly scaled in the base game's adventure-map hero bar.</summary>
	/// <remarks>
	/// The base game's ExoSkeletonUIGraphic builds its portrait mesh straight from the Portrait frame's part
	/// matrices via `matrixFlash.Transform(v) * 100f`, with no Y-flip (unlike this project's own
	/// ComputeFrameMatrix/DecodeFrameMatrix, which negate ty/translateY). That mesh is clipped by a UGUI mask
	/// ("IconMask") inside the real "PortraitTeamPrefab" (found via AssetStudioModCLI, since RectTransform data
	/// isn't in decompiled C#): IconMask is sized 83.3x89.55 against its 100x100 parent, and its child
	/// ExoSkeletonPortrait sits at anchoredPosition=(3.76,-5.82) with localScale=2. Solving "meshLocal*scale +
	/// anchoredPosition stays within IconMask's rect" for the mesh's own pre-scale position gives the allowed
	/// matrixValue range directly (X needs no further conversion; Y needs its sign flipped to match this project's
	/// -ty convention). This is exact, not an approximation like the original version of this class, which
	/// targeted an empirically-measured silhouette as a stand-in before this real prefab data was found.
	/// </remarks>
	internal static class PortraitFrameGuide
	{
		private static readonly Vector2 ReferenceCenter = new Vector2(-0.0188f, -0.0291f);
		private static readonly Vector2 ReferenceSize = new Vector2(0.4165f, 0.4478f);

		private const float LineThickness = 0.02f;
		private static readonly Color LineColor = new Color(0.2f, 0.95f, 1f, 0.85f);

		private static GameObject root;
		private static Sprite solidSprite;

		/// <summary>Builds the guide as four thin bars (not a filled rect, so it's a comparison guide rather than an overlay that hides the pose), hidden by default.</summary>
		internal static void Build(Transform parent)
		{
			EnsureResources();

			root = new GameObject("PortraitFrameGuide");
			root.transform.SetParent(parent, false);
			root.transform.localPosition = new Vector3(ReferenceCenter.x, ReferenceCenter.y, 0f);

			float halfWidth = ReferenceSize.x * 0.5f;
			float halfHeight = ReferenceSize.y * 0.5f;
			BuildBar(new Vector2(0f, halfHeight), new Vector2(ReferenceSize.x + LineThickness, LineThickness));
			BuildBar(new Vector2(0f, -halfHeight), new Vector2(ReferenceSize.x + LineThickness, LineThickness));
			BuildBar(new Vector2(-halfWidth, 0f), new Vector2(LineThickness, ReferenceSize.y + LineThickness));
			BuildBar(new Vector2(halfWidth, 0f), new Vector2(LineThickness, ReferenceSize.y + LineThickness));

			root.SetActive(false);
		}

		/// <summary>Shows/hides the guide. Toggled from RigEditorScene.RefreshTimeline, the one chokepoint nearly every mutator that can change activeClip funnels through.</summary>
		internal static void SetVisible(bool visible)
		{
			if (root != null)
			{
				root.SetActive(visible);
			}
		}

		/// <summary>Builds one guide bar. sortingOrder (32000) is above every real part and the background grid, so the guide stays visible regardless of what's posed under it.</summary>
		private static void BuildBar(Vector2 localPosition, Vector2 size)
		{
			GameObject barObject = new GameObject("Bar", typeof(SpriteRenderer));
			barObject.transform.SetParent(root.transform, false);
			barObject.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
			SpriteRenderer renderer = barObject.GetComponent<SpriteRenderer>();
			renderer.sprite = solidSprite;
			renderer.drawMode = SpriteDrawMode.Tiled;
			renderer.size = size;
			renderer.color = LineColor;
			renderer.sortingOrder = 32000;
		}

		private static void EnsureResources()
		{
			if (solidSprite != null)
			{
				return;
			}
			Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();
			solidSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
		}
	}
}
