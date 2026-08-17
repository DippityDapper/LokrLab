using System.Collections.Generic;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Instantiates authored scenario props onto the live embed board.</summary>
	/// <remarks>
	/// Parents under <c>HexGridRoot</c> so Stop unloads them with the fight
	/// scene. Does not write <c>isPassable</c>. Does not call vanilla
	/// <c>EncounterEntitiesGenerator.InstantiateObject</c> (that path needs a
	/// PPtr). Clear tracking on Disarm; scene unload destroys the objects.
	/// </remarks>
	internal static class EncounterProps
	{
		private static readonly Dictionary<string, GameObject> spawnedById =
			new Dictionary<string, GameObject>();

		/// <summary>Destroys tracked instances and forgets them.</summary>
		internal static void Clear()
		{
			foreach (KeyValuePair<string, GameObject> pair in spawnedById)
			{
				if (pair.Value != null)
				{
					Object.Destroy(pair.Value);
				}
			}

			spawnedById.Clear();
		}

		/// <summary>Rebuilds every placed prop on the current Stage board.</summary>
		internal static void Apply(EncounterFileModel file)
		{
			Clear();
			if (file == null || file.Props == null)
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			Transform parent = ResolveParent();
			for (int i = 0; i < file.Props.Count; i++)
			{
				EncounterPropModel prop = file.Props[i];
				if (prop == null || !EncounterPropRules.HasPlacement(prop))
				{
					continue;
				}

				GameObject instance = Spawn(prop, parent);
				if (instance != null)
				{
					spawnedById[prop.Id] = instance;
				}
			}
		}

		/// <summary>Moves a tracked instance, or spawns it when it is not on the board yet.</summary>
		internal static void Ensure(EncounterPropModel prop)
		{
			if (prop == null || !EncounterPropRules.HasPlacement(prop))
			{
				Forget(prop);
				return;
			}

			if (TryMove(prop))
			{
				return;
			}

			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return;
			}

			GameObject instance = Spawn(prop, ResolveParent());
			if (instance != null)
			{
				spawnedById[prop.Id] = instance;
			}
		}

		/// <summary>Writes transform position when this id is already spawned.</summary>
		internal static bool TryMove(EncounterPropModel prop)
		{
			GameObject instance;
			if (prop == null
				|| !spawnedById.TryGetValue(prop.Id, out instance)
				|| instance == null)
			{
				return false;
			}

			Vector3 world;
			if (!TryResolveWorld(prop, out world))
			{
				return false;
			}

			instance.transform.position = world;
			ApplyFlip(instance, prop);
			return true;
		}

		/// <summary>Hex center for this OffsetCoord, or false when the board is missing.</summary>
		internal static bool TryHexWorld(int col, int row, out Vector3 world)
		{
			world = Vector3.zero;
			Stage stage = Stage.instance;
			if (stage == null || stage.board == null)
			{
				return false;
			}

			HexCoord cube = OffsetCoord.RoffsetToCube(OffsetCoord.ODD, new OffsetCoord(col, row));
			Point center = stage.board.HexToCenter(cube);
			world = new Vector3((float)center.x, (float)center.y, 0f);
			return true;
		}

		private static bool TryResolveWorld(EncounterPropModel prop, out Vector3 world)
		{
			world = Vector3.zero;
			if (prop == null)
			{
				return false;
			}

			if (!prop.Snap && prop.X.HasValue && prop.Y.HasValue)
			{
				world = new Vector3(prop.X.Value, prop.Y.Value, 0f);
				return true;
			}

			if (!prop.Col.HasValue || !prop.Row.HasValue)
			{
				return false;
			}

			return TryHexWorld(prop.Col.Value, prop.Row.Value, out world);
		}

		private static void Forget(EncounterPropModel prop)
		{
			GameObject instance;
			if (prop == null || !spawnedById.TryGetValue(prop.Id, out instance))
			{
				return;
			}

			if (instance != null)
			{
				Object.Destroy(instance);
			}

			spawnedById.Remove(prop.Id);
		}

		private static GameObject Spawn(EncounterPropModel prop, Transform parent)
		{
			GameObject prefab = EncounterPropCatalog.Load(prop.PrefabName);
			if (prefab == null)
			{
				LokrLab.Lab.SetStatus("Could not load prop '" + prop.PrefabName + "'.");
				return null;
			}

			Vector3 position;
			if (!TryResolveWorld(prop, out position))
			{
				return null;
			}

			GameObject instance = parent != null
				? Object.Instantiate(prefab, parent)
				: Object.Instantiate(prefab);
			instance.name = prop.PrefabName;
			instance.transform.position = position;
			ApplyFlip(instance, prop);
			return instance;
		}

		private static void ApplyFlip(GameObject instance, EncounterPropModel prop)
		{
			if (instance == null || prop == null)
			{
				return;
			}

			Vector3 scale = instance.transform.localScale;
			scale.x = prop.Flipped ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
			instance.transform.localScale = scale;
		}

		private static Transform ResolveParent()
		{
			GameObject root = GameObject.Find("HexGridRoot");
			return root != null ? root.transform : null;
		}
	}
}
