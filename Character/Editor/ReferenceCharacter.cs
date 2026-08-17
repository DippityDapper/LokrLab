using System.Collections.Generic;
using Ironhide.AssetBundles;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLoader;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>A whole-character scale overlay in the Main Viewport, loaded from a shipped ExoSkeletonDataAsset and selectable as one piece.</summary>
	/// <remarks>
	/// Not a rig part: never written to rig.json, never part of undo history, and never scaled -- the whole point is
	/// a known in-game size to pose against. Rendered with the same ExoSkeletonRenderer path Preview uses, parented
	/// at world origin (not previewRoot) so the Main Viewport camera sees it. One BoxCollider covers the posed mesh
	/// so a click selects the character as a unit rather than an individual bone/sprite. sortingOrder sits above the
	/// background grid and below real parts, so overlapping editable parts still win a click.
	/// </remarks>
	internal sealed class ReferenceCharacter : MonoBehaviour
	{
		/// <summary>Draws above ViewportGrid (-32000) and below any real part's typical StaticLayer (near zero), so overlapping parts stay clickable.</summary>
		internal const int OverlaySortingOrder = -1000;

		/// <summary>Gerald's shipped metaExo id -- the default Add Reference character, as a known human-scale hero.</summary>
		internal const string DefaultMetaExo = "ExoSkeletonHumanGeraldLightSeeker_MetaDataAsset";

		private static int nextInstanceId = 1;
		private static readonly Color NormalColor = Color.white;
		private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.3f);

		private BoxCollider boxCollider;
		private GameObject exoObject;
		private MeshRenderer meshRenderer;
		private ExoSkeletonData exoData;
		private ExoSkeletonDataAsset exoAsset;
		private Material exoMaterial;
		private bool selected;
		private bool visible = true;
		private float opacity = 1f;
		private int animationIndex;
		private readonly List<string> animationNames = new List<string>();

		/// <summary>Stable per-instance key for SceneTreePanel's UiList, independent of which character is currently loaded.</summary>
		internal int InstanceId { get; private set; }

		/// <summary>The metaExo id currently loaded into this overlay.</summary>
		internal string MetaExoName { get; private set; } = string.Empty;

		/// <summary>Readable unit id for this metaExo when one is known, otherwise a shortened metaExo string.</summary>
		internal string DisplayName { get; private set; } = string.Empty;

		/// <summary>Animation names available on the loaded asset, for the Inspector dropdown.</summary>
		internal IReadOnlyList<string> AnimationNames => animationNames;

		/// <summary>The currently shown animation's name, or empty when nothing is loaded.</summary>
		internal string AnimationName =>
			animationIndex >= 0 && animationIndex < animationNames.Count ? animationNames[animationIndex] : string.Empty;

		/// <summary>Index into AnimationNames of the currently shown pose.</summary>
		internal int AnimationIndex => animationIndex;

		/// <summary>World-space draw order used by click-to-select when a part and a reference overlap -- always OverlaySortingOrder.</summary>
		internal int SortingOrder => OverlaySortingOrder;

		/// <summary>Whether this overlay is shown in the viewport. Hidden references keep their collider disabled so they are not pickable.</summary>
		internal bool Visible
		{
			get => visible;
			set
			{
				visible = value;
				ApplyVisibility();
			}
		}

		/// <summary>Mesh tint alpha in 0..1, independent of Visible. Lets the overlay sit over the work without fully hiding it.</summary>
		internal float Opacity
		{
			get => opacity;
			set
			{
				opacity = Mathf.Clamp01(value);
				ApplyColor();
			}
		}

		/// <summary>Whole-character rotation in degrees around world Z. Scale is not exposed -- LateUpdate forces localScale to one.</summary>
		internal float RotationDegrees
		{
			get => transform.eulerAngles.z;
			set => transform.rotation = Quaternion.Euler(0f, 0f, value);
		}

		/// <summary>Assigns a unique InstanceId. Load() is what actually builds the exo child -- Awake only prepares the collider.</summary>
		private void Awake()
		{
			InstanceId = nextInstanceId++;
			boxCollider = GetComponent<BoxCollider>();
			if (boxCollider == null)
			{
				boxCollider = gameObject.AddComponent<BoxCollider>();
			}
			boxCollider.size = new Vector3(1.5f, 2f, 0.1f);
		}

		/// <summary>Keeps scale locked and the pick collider matched to the posed mesh, which ExoSkeletonRenderer rebuilds in its own LateUpdate.</summary>
		/// <remarks>Forcing localScale every frame is the structural guarantee that Scale-tool drags (and anything else that writes Transform.localScale) cannot resize a reference. Collider sync is one frame stale if this LateUpdate runs before ExoSkeletonRenderer's; a 1-frame lag on a pick volume is invisible in practice.</remarks>
		private void LateUpdate()
		{
			transform.localScale = Vector3.one;
			SyncColliderToMesh();
			if (meshRenderer != null)
			{
				meshRenderer.sortingOrder = OverlaySortingOrder;
			}
		}

		/// <summary>Loads (or reloads) a shipped ExoSkeletonDataAsset by metaExo id, keeping this overlay's current position and rotation.</summary>
		internal bool TryLoad(string metaExoName, out string error)
		{
			metaExoName = (metaExoName ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(metaExoName))
			{
				error = "Enter a metaExo id first (e.g. ExoSkeletonHumanGeraldLightSeeker_MetaDataAsset).";
				return false;
			}

			ExoSkeletonDataAsset asset = AssetBundleManager.LoadAsset<ExoSkeletonDataAsset>("units", metaExoName);
			if (asset == null)
			{
				error = "Could not find an ExoSkeletonDataAsset named '" + metaExoName + "' in the 'units' bundle.";
				return false;
			}
			if (asset.animations == null || asset.animations.Length == 0)
			{
				error = "'" + metaExoName + "' has no animations — nothing to pose.";
				return false;
			}

			DestroyExoChild();

			GameObject go = new GameObject("ReferenceExo",
				typeof(ExoSkeletonData), typeof(MeshFilter), typeof(MeshRenderer),
				typeof(ExoSkeletonRenderer), typeof(ExoSkeletonAnimator));
			go.transform.SetParent(transform, false);

			MeshRenderer renderer = go.GetComponent<MeshRenderer>();
			exoMaterial = new Material(Shader.Find("Sprites/Default"));
			renderer.material = exoMaterial;
			renderer.sortingOrder = OverlaySortingOrder;

			ExoSkeletonData data = go.GetComponent<ExoSkeletonData>();
			data.UpdateAsset(asset);

			ExoSkeletonRenderer exoRenderer = go.GetComponent<ExoSkeletonRenderer>();
			exoRenderer.exoSkeletonData = data;

			ExoSkeletonAnimator animator = go.GetComponent<ExoSkeletonAnimator>();
			animator.data = data;
			animator.enabled = false;

			CharacterLabLayers.ApplyToHierarchy(go);

			exoObject = go;
			meshRenderer = renderer;
			exoData = data;
			exoAsset = asset;
			MetaExoName = metaExoName;
			DisplayName = DisplayNameFor(metaExoName);

			animationNames.Clear();
			for (int i = 0; i < asset.animations.Length; i++)
			{
				string name = asset.animations[i].name;
				animationNames.Add(string.IsNullOrEmpty(name) ? "Animation " + i.ToString() : name);
			}

			int standIndex = IndexOfAnimation("Stand");
			ApplyAnimation(standIndex >= 0 ? standIndex : 0);
			ApplyColor();
			ApplyVisibility();
			SeedColliderFromRestVertices(asset);

			error = null;
			return true;
		}

		/// <summary>Shows the first frame of the named animation. No-ops if the name is not on this asset.</summary>
		internal void SetAnimation(string animationName)
		{
			int index = IndexOfAnimation(animationName);
			if (index >= 0)
			{
				ApplyAnimation(index);
			}
		}

		/// <summary>Shows the first frame of the animation at the given index.</summary>
		internal void SetAnimationIndex(int index)
		{
			if (index >= 0 && index < animationNames.Count)
			{
				ApplyAnimation(index);
			}
		}

		/// <summary>Sets the selection highlight tint. Does not change Visible.</summary>
		internal void SetSelected(bool value)
		{
			selected = value;
			ApplyColor();
		}

		/// <summary>Readable label for a metaExo: the first KnownUnitDefinitions id that points at it, otherwise a stripped asset name.</summary>
		internal static string DisplayNameFor(string metaExo)
		{
			if (string.IsNullOrEmpty(metaExo))
			{
				return string.Empty;
			}
			foreach (KeyValuePair<string, UnitDefinition> entry in CharacterAPI.KnownUnitDefinitions)
			{
				if (entry.Value != null && entry.Value.metaExo == metaExo)
				{
					return entry.Key;
				}
			}
			const string prefix = "ExoSkeleton";
			const string suffix = "_MetaDataAsset";
			string name = metaExo;
			if (name.StartsWith(prefix))
			{
				name = name.Substring(prefix.Length);
			}
			if (name.EndsWith(suffix))
			{
				name = name.Substring(0, name.Length - suffix.Length);
			}
			return string.IsNullOrEmpty(name) ? metaExo : name;
		}

		private int IndexOfAnimation(string animationName)
		{
			for (int i = 0; i < animationNames.Count; i++)
			{
				if (animationNames[i] == animationName)
				{
					return i;
				}
			}
			return -1;
		}

		private void ApplyAnimation(int index)
		{
			animationIndex = index;
			if (exoData == null || exoAsset == null || index < 0 || index >= exoAsset.animations.Length)
			{
				return;
			}
			Ironhide.ExoSkeleton.Animation animation = exoAsset.animations[index];
			if (animation.frames == null || animation.frames.Length == 0)
			{
				return;
			}
			AnimationFrame frame = animation.frames[0];
			exoData.SetPose(frame.renderOrder, frame.matrices, frame.attachPoints, frame.alphas);
		}

		private void ApplyColor()
		{
			if (exoMaterial == null)
			{
				return;
			}
			Color color = selected ? SelectedColor : NormalColor;
			color.a = opacity;
			exoMaterial.color = color;
		}

		private void ApplyVisibility()
		{
			if (exoObject != null)
			{
				exoObject.SetActive(visible);
			}
			if (boxCollider != null)
			{
				boxCollider.enabled = visible;
			}
		}

		private void SyncColliderToMesh()
		{
			if (boxCollider == null || meshRenderer == null || !meshRenderer.enabled)
			{
				return;
			}
			Bounds world = meshRenderer.bounds;
			if (world.size.sqrMagnitude < 0.0001f)
			{
				return;
			}
			Vector3 localCenter = transform.InverseTransformPoint(world.center);
			Vector3 localSize = transform.InverseTransformVector(world.size);
			boxCollider.center = new Vector3(localCenter.x, localCenter.y, 0f);
			boxCollider.size = new Vector3(Mathf.Max(0.1f, Mathf.Abs(localSize.x)), Mathf.Max(0.1f, Mathf.Abs(localSize.y)), 0.1f);
		}

		private void SeedColliderFromRestVertices(ExoSkeletonDataAsset asset)
		{
			if (boxCollider == null || asset.parts == null || asset.parts.Count == 0)
			{
				return;
			}
			bool started = false;
			Bounds bounds = default;
			foreach (Part part in asset.parts)
			{
				if (part.vertices == null)
				{
					continue;
				}
				foreach (Vector2 vertex in part.vertices)
				{
					if (!started)
					{
						bounds = new Bounds(vertex, Vector3.zero);
						started = true;
					}
					else
					{
						bounds.Encapsulate(vertex);
					}
				}
			}
			if (!started)
			{
				return;
			}
			boxCollider.center = new Vector3(bounds.center.x, bounds.center.y, 0f);
			boxCollider.size = new Vector3(Mathf.Max(0.1f, bounds.size.x), Mathf.Max(0.1f, bounds.size.y), 0.1f);
		}

		private void DestroyExoChild()
		{
			if (exoObject != null)
			{
				Destroy(exoObject);
				exoObject = null;
			}
			meshRenderer = null;
			exoData = null;
			exoAsset = null;
			exoMaterial = null;
			animationNames.Clear();
			animationIndex = 0;
		}

		private void OnDestroy()
		{
			DestroyExoChild();
		}
	}
}
