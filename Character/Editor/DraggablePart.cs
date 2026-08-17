using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Represents one rig part in the viewport -- selection, dragging, sorting order, and affine pose display.</summary>
	/// <remarks>
	/// Selection is list-only (SceneTreePanel rows call RigEditorScene.SelectPart), since imported rigs commonly
	/// stack many overlapping parts at the same screen position, making click-to-select unreliable. Dragging is
	/// correspondingly not driven by this object's own OnMouseDown/OnMouseDrag, but globally by
	/// EditorInputController against whatever RigEditorScene.SelectedPart currently is, so it works anywhere in
	/// the viewport regardless of what's under the cursor. See AnimatorToolRegistry/AnimatorTools.cs for the
	/// per-tool drag math.
	///
	/// Also owns draw order and selection/visibility state. Draw order lives on SpriteRenderer.sortingOrder, which
	/// drives both the editor's visual stacking and, via RigEditorScene.OnSaveClicked sorting parts by it, the
	/// actual in-game render order.
	/// </remarks>
	internal sealed class DraggablePart : MonoBehaviour
	{
		/// <summary>The name of the rig part this GameObject represents.</summary>
		internal string PartName;

		private SpriteRenderer spriteRenderer;
		private bool selected;

		private float rotationDegrees;
		private float scaleX = 1f;
		private float scaleY = 1f;
		private float shearDegrees;

		private static readonly Color NormalColor = Color.white;
		private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.3f);

		/// <summary>The actual current visual stacking, changing every time ApplyContextPoseToParts runs since per-frame draw order can differ frame to frame. Not what Save/the parts list use for the rig's persistent order -- that's StaticLayer.</summary>
		internal int SortingOrder
		{
			get => spriteRenderer.sortingOrder;
			set
			{
				spriteRenderer.sortingOrder = value;
				if (affineMeshRenderer != null)
				{
					affineMeshRenderer.sortingOrder = value;
				}
				if (liveShearMeshRenderer != null)
				{
					liveShearMeshRenderer.sortingOrder = value;
				}
			}
		}

		/// <summary>The persistent, single per-part draw order -- set at Load from rig.json's parts array order, editable via the parts list, and what OnSaveClicked sorts by when writing that array back out. ApplyContextPoseToParts applies it to SortingOrder as the baseline whenever a frame doesn't specify its own order.</summary>
		internal int StaticLayer { get; set; }

		private bool visible = true;
		private bool frameVisible = true;

		/// <summary>The persistent eye-toggle from the parts list -- the user's own choice, unaffected by which clip/frame is active.</summary>
		internal bool Visible
		{
			get => visible;
			set { visible = value; UpdateRendererEnabled(); }
		}

		/// <summary>Whether the active clip/frame actually includes this part, distinct from Visible so scrubbing frames never clobbers the user's own eye-toggle choice.</summary>
		internal bool FrameVisible
		{
			get => frameVisible;
			set { frameVisible = value; UpdateRendererEnabled(); }
		}

		private void UpdateRendererEnabled()
		{
			bool shouldShow = visible && frameVisible;
			spriteRenderer.enabled = shouldShow && !usingAffinePose && !usingLiveShearMesh;
			if (usingAffinePose && affineMeshObject != null)
			{
				affineMeshObject.SetActive(shouldShow);
			}
			if (usingLiveShearMesh && liveShearMeshObject != null)
			{
				liveShearMeshObject.SetActive(shouldShow);
			}
		}

		/// <summary>Current rotation in degrees; setting it immediately re-applies the live Transform (or shear mesh) via ApplyLiveTransform.</summary>
		internal float RotationDegrees
		{
			get => rotationDegrees;
			set { rotationDegrees = value; ApplyLiveTransform(); }
		}

		/// <summary>Independent per-axis scale -- ScaleTool sets both to the same value; ScaleXYTool sets them independently.</summary>
		/// <remarks>Clamped by magnitude only, sign preserved -- a negative axis scale is Unity's standard way to represent a mirrored part, and real shipped data legitimately uses it (e.g. a mirrored arm sprite reused for both limbs). Clamping the sign away previously collapsed a mirrored part to a barely-visible sliver instead of rendering it mirrored (a real reported bug).</remarks>
		internal float ScaleX
		{
			get => scaleX;
			set { scaleX = ClampMagnitude(value); ApplyLiveTransform(); }
		}

		/// <summary>The Y-axis counterpart to ScaleX, with the same magnitude-clamped, sign-preserving behavior on set.</summary>
		internal float ScaleY
		{
			get => scaleY;
			set { scaleY = ClampMagnitude(value); ApplyLiveTransform(); }
		}

		/// <summary>Clamps magnitude only, preserving sign -- Mathf.Sign(0f) returns 1 in Unity, so an exact-zero input still lands on a small positive fallback rather than a true (invisible) zero scale.</summary>
		private static float ClampMagnitude(float value)
		{
			return Mathf.Sign(value) * Mathf.Max(0.05f, Mathf.Abs(value));
		}

		/// <summary>Shear angle, in degrees. Authored only through InspectorPanel (a numeric field, no natural drag gesture); rendered live only when non-zero, so a part with ShearDegrees == 0 stays on the ordinary SpriteRenderer+Transform path unchanged.</summary>
		internal float ShearDegrees
		{
			get => shearDegrees;
			set { shearDegrees = value; ApplyLiveTransform(); }
		}

		private void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
		}

		/// <summary>Sets whether this part is shown as selected, updating its render color accordingly.</summary>
		internal void SetSelected(bool value)
		{
			selected = value;
			UpdateColor();
		}

		private void UpdateColor()
		{
			Color color = selected ? SelectedColor : NormalColor;
			spriteRenderer.color = color;
			if (affineMaterial != null)
			{
				affineMaterial.color = color;
			}
			if (liveShearMaterial != null)
			{
				liveShearMaterial.color = color;
			}
		}

		/// <summary>Routes RotationDegrees/ScaleX/ScaleY/ShearDegrees changes to either the plain Transform (no shear) or a baked live child mesh (with shear), since Unity's Transform has no shear component. Transform.position is left alone either way, so Move-tool dragging keeps working unchanged.</summary>
		private void ApplyLiveTransform()
		{
			if (usingAffinePose)
			{
				return;
			}
			if (Mathf.Abs(shearDegrees) < 0.01f)
			{
				if (usingLiveShearMesh)
				{
					ClearLiveShearMesh();
				}
				transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
				transform.localScale = new Vector3(scaleX, scaleY, 1f);
				return;
			}

			transform.rotation = Quaternion.identity;
			transform.localScale = Vector3.one;
			RebuildLiveShearMesh();
		}

		/// <summary>Affine (shear/non-uniform-scale) display for imported frames whose original matrix wasn't decomposable at all (PartPose.Approximate). Read-only, distinct from the live editable shear path below; builds a small child mesh with the exact source matrix applied to its vertices in world space, the same thing ExoSkeletonRenderer does for the real in-game mesh.</summary>
		private GameObject affineMeshObject;
		private Mesh affineMesh;
		private Material affineMaterial;
		private Renderer affineMeshRenderer;
		private bool usingAffinePose;

		/// <summary>Whether this part is showing the read-only affine display. EditorInputController checks this before dragging, since no rotation/shear/scale combination could represent an edit to an undecomposable raw matrix.</summary>
		internal bool IsAffinePose => usingAffinePose;

		/// <summary>Displays a raw source matrix directly (read-only), for a pose too degenerate to decompose into rotation/shear/scale.</summary>
		/// <remarks>Resets to identity first -- the vertices are computed directly in world space, so any leftover position/rotation/scale would double-apply.</remarks>
		internal void SetAffinePose(Vector2 restPosition, float mA, float mB, float mC, float mD, float translateX, float translateY)
		{
			Sprite sprite = spriteRenderer.sprite;
			if (sprite == null || sprite.vertices == null || sprite.vertices.Length == 0)
			{
				return;
			}
			ClearLiveShearMesh();
			EnsureAffineMeshObject();

			transform.position = Vector3.zero;
			transform.rotation = Quaternion.identity;
			transform.localScale = Vector3.one;

			Vector2[] localVertices = sprite.vertices;
			Vector3[] worldVertices = new Vector3[localVertices.Length];
			for (int i = 0; i < localVertices.Length; i++)
			{
				Vector2 partVertex = restPosition + localVertices[i];
				float worldX = mA * partVertex.x + mC * partVertex.y + translateX;
				float worldY = mB * partVertex.x + mD * partVertex.y + translateY;
				worldVertices[i] = new Vector3(worldX, worldY, 0f);
			}

			int[] triangles = new int[sprite.triangles.Length];
			for (int i = 0; i < triangles.Length; i++)
			{
				triangles[i] = sprite.triangles[i];
			}

			affineMesh.Clear();
			affineMesh.vertices = worldVertices;
			affineMesh.uv = sprite.uv;
			affineMesh.triangles = triangles;
			affineMesh.RecalculateBounds();

			if (affineMaterial.mainTexture != sprite.texture)
			{
				affineMaterial.mainTexture = sprite.texture;
			}

			usingAffinePose = true;
			spriteRenderer.enabled = false;
			affineMeshRenderer.sortingOrder = spriteRenderer.sortingOrder;
			affineMeshObject.SetActive(visible && frameVisible);
		}

		/// <summary>Reverts from the affine display back to normal Transform-driven rendering.</summary>
		internal void ClearAffinePose()
		{
			if (!usingAffinePose)
			{
				return;
			}
			usingAffinePose = false;
			if (affineMeshObject != null)
			{
				affineMeshObject.SetActive(false);
			}
			UpdateRendererEnabled();
		}

		private void EnsureAffineMeshObject()
		{
			if (affineMeshObject != null)
			{
				return;
			}
			affineMeshObject = new GameObject("AffineMesh", typeof(MeshFilter), typeof(MeshRenderer));
			affineMeshObject.transform.SetParent(transform, false);
			affineMesh = new Mesh();
			affineMeshObject.GetComponent<MeshFilter>().mesh = affineMesh;
			affineMaterial = new Material(Shader.Find("Sprites/Default")) { color = selected ? SelectedColor : NormalColor };
			affineMeshRenderer = affineMeshObject.GetComponent<MeshRenderer>();
			affineMeshRenderer.material = affineMaterial;
			affineMeshRenderer.sortingOrder = spriteRenderer.sortingOrder;
			CharacterLabLayers.ApplyToHierarchy(affineMeshObject);
		}

		/// <summary>Live editable shear mesh -- same idea as the read-only affine mesh (Transform can't express shear), but recomputed from this part's own live rotation/scale/shear fields on every edit, in local space (Transform.position still handles world placement normally).</summary>
		private GameObject liveShearMeshObject;
		private Mesh liveShearMesh;
		private Material liveShearMaterial;
		private Renderer liveShearMeshRenderer;
		private bool usingLiveShearMesh;

		private void RebuildLiveShearMesh()
		{
			Sprite sprite = spriteRenderer.sprite;
			if (sprite == null || sprite.vertices == null || sprite.vertices.Length == 0)
			{
				return;
			}
			EnsureLiveShearMeshObject();

			AffineMatrixMath.ComposeLinear(rotationDegrees, shearDegrees, scaleX, scaleY, out float mA, out float mB, out float mC, out float mD);

			Vector2[] localVertices = sprite.vertices;
			Vector3[] meshVertices = new Vector3[localVertices.Length];
			for (int i = 0; i < localVertices.Length; i++)
			{
				Vector2 v = localVertices[i];
				float x = mA * v.x + mC * v.y;
				float y = mB * v.x + mD * v.y;
				meshVertices[i] = new Vector3(x, y, 0f);
			}

			int[] triangles = new int[sprite.triangles.Length];
			for (int i = 0; i < triangles.Length; i++)
			{
				triangles[i] = sprite.triangles[i];
			}

			liveShearMesh.Clear();
			liveShearMesh.vertices = meshVertices;
			liveShearMesh.uv = sprite.uv;
			liveShearMesh.triangles = triangles;
			liveShearMesh.RecalculateBounds();

			if (liveShearMaterial.mainTexture != sprite.texture)
			{
				liveShearMaterial.mainTexture = sprite.texture;
			}

			usingLiveShearMesh = true;
			liveShearMeshRenderer.sortingOrder = spriteRenderer.sortingOrder;
			UpdateRendererEnabled();
		}

		private void ClearLiveShearMesh()
		{
			if (!usingLiveShearMesh)
			{
				return;
			}
			usingLiveShearMesh = false;
			if (liveShearMeshObject != null)
			{
				liveShearMeshObject.SetActive(false);
			}
			UpdateRendererEnabled();
		}

		private void EnsureLiveShearMeshObject()
		{
			if (liveShearMeshObject != null)
			{
				return;
			}
			liveShearMeshObject = new GameObject("LiveShearMesh", typeof(MeshFilter), typeof(MeshRenderer));
			liveShearMeshObject.transform.SetParent(transform, false);
			liveShearMesh = new Mesh();
			liveShearMeshObject.GetComponent<MeshFilter>().mesh = liveShearMesh;
			liveShearMaterial = new Material(Shader.Find("Sprites/Default")) { color = selected ? SelectedColor : NormalColor };
			liveShearMeshRenderer = liveShearMeshObject.GetComponent<MeshRenderer>();
			liveShearMeshRenderer.material = liveShearMaterial;
			liveShearMeshRenderer.sortingOrder = spriteRenderer.sortingOrder;
			CharacterLabLayers.ApplyToHierarchy(liveShearMeshObject);
		}

	}
}
