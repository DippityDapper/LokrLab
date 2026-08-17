using LokrLab.Shell;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>World-space camera-rect overlay and drag handles on the Setup board.</summary>
	internal static class EncounterCameraGizmo
	{
		private const float HandleWorld = 0.45f;
		private const float LineWidth = 0.08f;

		private static LineRenderer outline;
		private static GameObject root;
		private static readonly GameObject[] handles = new GameObject[4];
		private static bool dragging;
		private static bool creating;
		private static EncounterCameraHandle activeHandle;
		private static EncounterCameraModel dragOrigin;
		private static float startX;
		private static float startY;

		/// <summary>Rebuilds the overlay from the armed Setup file. No-op when the board is hidden.</summary>
		internal static void Refresh()
		{
			if (!EncounterEdit.IsArmed)
			{
				Dispose();
				return;
			}

			EnsureRoot();
			EncounterCameraModel camera = PreviewCamera();
			bool show = EncounterCameraRules.HasBounds(camera);
			if (root != null)
			{
				root.SetActive(show || creating);
			}

			if (!show && !creating)
			{
				return;
			}

			DrawRect(camera);
		}

		/// <summary>Camera-tool drag. Returns true when this frame owns the pointer.</summary>
		internal static bool Tick(Vector3 world, bool left)
		{
			if (!EncounterEdit.IsArmed || EncounterEdit.File == null)
			{
				return false;
			}

			if (!left)
			{
				if (dragging || creating)
				{
					Commit();
				}

				Refresh();
				return false;
			}

			if (!dragging && !creating)
			{
				Begin(world.x, world.y);
			}
			else
			{
				Continue(world.x, world.y);
			}

			Refresh();
			return true;
		}

		/// <summary>Destroys the overlay when Setup hides.</summary>
		internal static void Dispose()
		{
			dragging = false;
			creating = false;
			dragOrigin = null;
			if (root != null)
			{
				Object.Destroy(root);
				root = null;
			}

			outline = null;
			for (int i = 0; i < handles.Length; i++)
			{
				handles[i] = null;
			}
		}

		private static void Begin(float x, float y)
		{
			EncounterFileModel file = EncounterEdit.File;
			EncounterCameraModel camera = file != null ? file.Camera : null;
			EncounterCameraHandle hit = EncounterCameraRules.Hit(camera, x, y, HandleWorld);
			if (hit != EncounterCameraHandle.None)
			{
				dragging = true;
				creating = false;
				activeHandle = hit;
				dragOrigin = Clone(camera);
				startX = x;
				startY = y;
				return;
			}

			dragging = true;
			creating = true;
			activeHandle = EncounterCameraHandle.None;
			startX = x;
			startY = y;
			if (file != null)
			{
				file.Camera = EncounterCameraRules.FromCorners(x, y, x + EncounterCameraRules.MinSpan, y + EncounterCameraRules.MinSpan);
			}
		}

		private static void Continue(float x, float y)
		{
			EncounterFileModel file = EncounterEdit.File;
			if (file == null)
			{
				return;
			}

			if (creating)
			{
				file.Camera = EncounterCameraRules.FromCorners(startX, startY, x, y);
				file.Camera.LockZoom = true;
				return;
			}

			if (dragOrigin == null)
			{
				return;
			}

			if (file.Camera == null)
			{
				file.Camera = new EncounterCameraModel();
			}

			EncounterCameraRules.ApplyHandle(file.Camera, dragOrigin, activeHandle, x, y, startX, startY);
		}

		private static void Commit()
		{
			EncounterFileModel file = EncounterEdit.File;
			if (file != null && EncounterCameraRules.HasBounds(file.Camera))
			{
				EncounterCameraRules.Normalize(file.Camera);
				LabSaveUx.MarkDirty();
				LokrLabApi.LokrLabApi.RequestRefresh();
			}
			else if (file != null && creating)
			{
				file.Camera = null;
			}

			dragging = false;
			creating = false;
			dragOrigin = null;
		}

		private static EncounterCameraModel PreviewCamera()
		{
			return EncounterEdit.File != null ? EncounterEdit.File.Camera : null;
		}

		private static EncounterCameraModel Clone(EncounterCameraModel source)
		{
			if (source == null)
			{
				return null;
			}

			return new EncounterCameraModel
			{
				MinX = source.MinX,
				MinY = source.MinY,
				MaxX = source.MaxX,
				MaxY = source.MaxY,
				LockZoom = source.LockZoom,
				OrthoSize = source.OrthoSize
			};
		}

		private static void EnsureRoot()
		{
			if (root != null)
			{
				return;
			}

			GameObject parent = GameObject.Find("HexGridRoot");
			root = new GameObject("EncounterCameraGizmo");
			if (parent != null)
			{
				root.transform.SetParent(parent.transform, false);
			}

			GameObject lineObject = new GameObject("Outline");
			lineObject.transform.SetParent(root.transform, false);
			outline = lineObject.AddComponent<LineRenderer>();
			outline.loop = true;
			outline.positionCount = 4;
			outline.useWorldSpace = true;
			outline.widthMultiplier = LineWidth;
			outline.numCapVertices = 0;
			outline.numCornerVertices = 0;
			outline.textureMode = LineTextureMode.Stretch;
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				outline.material = new Material(shader);
			}

			outline.startColor = new Color(0.35f, 0.75f, 1f, 0.95f);
			outline.endColor = outline.startColor;
			outline.sortingOrder = 80;
			for (int i = 0; i < handles.Length; i++)
			{
				GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Quad);
				handle.name = "Handle" + i;
				handle.transform.SetParent(root.transform, false);
				handle.transform.localScale = new Vector3(HandleWorld, HandleWorld, 1f);
				Object.Destroy(handle.GetComponent<Collider>());
				MeshRenderer renderer = handle.GetComponent<MeshRenderer>();
				if (shader != null)
				{
					renderer.sharedMaterial = new Material(shader);
				}

				renderer.sharedMaterial.color = new Color(0.95f, 0.95f, 1f, 1f);
				handles[i] = handle;
			}
		}

		private static void DrawRect(EncounterCameraModel camera)
		{
			if (outline == null || !EncounterCameraRules.HasBounds(camera))
			{
				return;
			}

			Vector3 nw = new Vector3(camera.MinX, camera.MaxY, -0.2f);
			Vector3 ne = new Vector3(camera.MaxX, camera.MaxY, -0.2f);
			Vector3 se = new Vector3(camera.MaxX, camera.MinY, -0.2f);
			Vector3 sw = new Vector3(camera.MinX, camera.MinY, -0.2f);
			outline.SetPosition(0, nw);
			outline.SetPosition(1, ne);
			outline.SetPosition(2, se);
			outline.SetPosition(3, sw);
			PlaceHandle(0, nw);
			PlaceHandle(1, ne);
			PlaceHandle(2, se);
			PlaceHandle(3, sw);
		}

		private static void PlaceHandle(int index, Vector3 position)
		{
			if (index < 0 || index >= handles.Length || handles[index] == null)
			{
				return;
			}

			handles[index].transform.position = position;
			handles[index].SetActive(EncounterEdit.Tool == EncounterEditTool.Camera);
		}
	}
}
