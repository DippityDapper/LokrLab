using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Builds the faint background reference grid for a viewport.</summary>
	/// <remarks>
	/// A hand-built MeshRenderer quad with UV-tiled coordinates, not SpriteRenderer's SpriteDrawMode.Tiled --
	/// runtime-created Sprites (via Sprite.Create, no packing/border metadata) didn't actually tile in this game's
	/// build, rendering as a single stretched tile. Building the mesh and UVs directly (repeating the UV rect
	/// `repeats` times instead of 0..1) sidesteps SpriteDrawMode and tiles by construction. GL immediate-mode
	/// drawing was tried even earlier and dropped since it depends on camera render-callback timing/state this
	/// game's runtime didn't reliably provide.
	///
	/// Static/one-time rather than per-frame: sized once to comfortably cover wherever a viewport's camera can
	/// ever show, so nothing needs to reposition or resize it as the user pans/zooms.
	/// </remarks>
	internal static class ViewportGrid
	{
		/// <summary>World units per grid line, matching RigEditorScene.PixelsToUnits (1 world unit = 100px) so a grid square is a known real sprite-pixel size to gauge a character against.</summary>
		private const float LineSpacing = 1f;
		private const int TileTexturePixels = 64;
		private const float AxisThickness = 0.02f;

		private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.12f);
		private static readonly Color AxisColor = new Color(1f, 1f, 1f, 0.35f);

		private static Material gridMaterial;
		private static Sprite solidSprite;

		/// <summary>Builds the grid and axis bars, centered on parent's local origin. extent is the half-width/height to cover, in world units.</summary>
		/// <remarks>The grid's sortingOrder (-32000) is far below any real part's, guaranteeing it's always background.</remarks>
		internal static void Build(Transform parent, float extent)
		{
			EnsureResources();

			GameObject gridObject = new GameObject("Grid", typeof(MeshFilter), typeof(MeshRenderer));
			gridObject.transform.SetParent(parent, false);
			gridObject.GetComponent<MeshFilter>().mesh = BuildTiledQuadMesh(extent);
			MeshRenderer gridRenderer = gridObject.GetComponent<MeshRenderer>();
			gridRenderer.material = gridMaterial;
			gridRenderer.sortingOrder = -32000;

			BuildAxisBar(parent, new Vector2(extent * 2f, AxisThickness));
			BuildAxisBar(parent, new Vector2(AxisThickness, extent * 2f));
		}

		/// <summary>A flat quad from -extent to +extent on both axes, with UV coordinates spanning 0..repeats instead of 0..1 -- gridMaterial's Repeat-wrapped texture tiles by sampling past 1.0, landing a grid line every LineSpacing world units.</summary>
		private static Mesh BuildTiledQuadMesh(float extent)
		{
			float repeats = (extent * 2f) / LineSpacing;
			Mesh mesh = new Mesh
			{
				vertices = new[]
				{
					new Vector3(-extent, -extent, 0f),
					new Vector3(extent, -extent, 0f),
					new Vector3(extent, extent, 0f),
					new Vector3(-extent, extent, 0f)
				},
				uv = new[]
				{
					new Vector2(0f, 0f),
					new Vector2(repeats, 0f),
					new Vector2(repeats, repeats),
					new Vector2(0f, repeats)
				},
				colors = new[] { Color.white, Color.white, Color.white, Color.white },
				triangles = new[] { 0, 1, 2, 0, 2, 3 }
			};
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>Builds one axis bar (a solid-color tiled sprite), sortingOrder just above the grid but still far below any real part.</summary>
		private static void BuildAxisBar(Transform parent, Vector2 size)
		{
			GameObject barObject = new GameObject("GridAxis", typeof(SpriteRenderer));
			barObject.transform.SetParent(parent, false);
			SpriteRenderer renderer = barObject.GetComponent<SpriteRenderer>();
			renderer.sprite = solidSprite;
			renderer.drawMode = SpriteDrawMode.Tiled;
			renderer.size = size;
			renderer.color = AxisColor;
			renderer.sortingOrder = -31999;
		}

		/// <summary>Lazily builds the grid material/texture and the solid-color axis sprite, once.</summary>
		/// <remarks>mipChain=true + Bilinear filtering matter here: this texture tiles up to ~140 times across the Main Viewport's grid, and zooming out shrinks each tile to a fraction of a screen pixel -- a single 1-texel-wide line sampled with Point filtering and no mip chain would alias away to nothing between pixel samples instead of fading out gracefully.</remarks>
		private static void EnsureResources()
		{
			if (gridMaterial != null)
			{
				return;
			}

			Texture2D tileTexture = new Texture2D(TileTexturePixels, TileTexturePixels, TextureFormat.RGBA32, true)
			{
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Bilinear
			};
			Color clear = new Color(0f, 0f, 0f, 0f);
			Color[] pixels = new Color[TileTexturePixels * TileTexturePixels];
			for (int y = 0; y < TileTexturePixels; y++)
			{
				for (int x = 0; x < TileTexturePixels; x++)
				{
					pixels[y * TileTexturePixels + x] = (x == 0 || y == 0) ? LineColor : clear;
				}
			}
			tileTexture.SetPixels(pixels);
			tileTexture.Apply(true);

			gridMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tileTexture };

			Texture2D solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
			solidTexture.SetPixel(0, 0, Color.white);
			solidTexture.Apply();
			solidSprite = Sprite.Create(solidTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
		}
	}
}
