using System;

namespace LokrLab.Encounter
{
	/// <summary>Which part of the authored camera rect a world point hits.</summary>
	internal enum EncounterCameraHandle
	{
		/// <summary>Outside the rect and its handles.</summary>
		None,

		/// <summary>Inside the rect, not on an edge or corner.</summary>
		Interior,

		/// <summary>Top edge.</summary>
		North,

		/// <summary>Bottom edge.</summary>
		South,

		/// <summary>Right edge.</summary>
		East,

		/// <summary>Left edge.</summary>
		West,

		/// <summary>Top-right corner.</summary>
		NorthEast,

		/// <summary>Top-left corner.</summary>
		NorthWest,

		/// <summary>Bottom-right corner.</summary>
		SouthEast,

		/// <summary>Bottom-left corner.</summary>
		SouthWest
	}

	/// <summary>Unity-free camera-rect math for Encounter Setup and Play.</summary>
	internal static class EncounterCameraRules
	{
		/// <summary>Smallest authored span on either axis, in world units.</summary>
		internal const float MinSpan = 1f;

		/// <summary>True when the four edges form a usable world AABB.</summary>
		internal static bool HasBounds(EncounterCameraModel camera)
		{
			if (camera == null)
			{
				return false;
			}

			return camera.MaxX - camera.MinX >= MinSpan
				&& camera.MaxY - camera.MinY >= MinSpan;
		}

		/// <summary>Swaps inverted edges and grows a degenerate axis to <see cref="MinSpan"/>.</summary>
		internal static void Normalize(EncounterCameraModel camera)
		{
			if (camera == null)
			{
				return;
			}

			if (camera.MaxX < camera.MinX)
			{
				float swap = camera.MinX;
				camera.MinX = camera.MaxX;
				camera.MaxX = swap;
			}

			if (camera.MaxY < camera.MinY)
			{
				float swap = camera.MinY;
				camera.MinY = camera.MaxY;
				camera.MaxY = swap;
			}

			if (camera.MaxX - camera.MinX < MinSpan)
			{
				float mid = (camera.MinX + camera.MaxX) * 0.5f;
				camera.MinX = mid - MinSpan * 0.5f;
				camera.MaxX = mid + MinSpan * 0.5f;
			}

			if (camera.MaxY - camera.MinY < MinSpan)
			{
				float mid = (camera.MinY + camera.MaxY) * 0.5f;
				camera.MinY = mid - MinSpan * 0.5f;
				camera.MaxY = mid + MinSpan * 0.5f;
			}

			if (camera.OrthoSize.HasValue && camera.OrthoSize.Value < 0.01f)
			{
				camera.OrthoSize = null;
			}
		}

		/// <summary>World AABB from two opposite corners.</summary>
		internal static EncounterCameraModel FromCorners(float x0, float y0, float x1, float y1)
		{
			EncounterCameraModel camera = new EncounterCameraModel
			{
				MinX = x0,
				MinY = y0,
				MaxX = x1,
				MaxY = y1,
				LockZoom = true
			};
			Normalize(camera);
			return camera;
		}

		/// <summary>Largest ortho that keeps the full view inside the rect at this aspect.</summary>
		internal static float FitOrtho(EncounterCameraModel camera, float aspect)
		{
			if (!HasBounds(camera))
			{
				return 1f;
			}

			if (aspect < 0.01f)
			{
				aspect = 1f;
			}

			float fromHeight = (camera.MaxY - camera.MinY) * 0.5f;
			float fromWidth = (camera.MaxX - camera.MinX) * 0.5f / aspect;
			return fromHeight < fromWidth ? fromHeight : fromWidth;
		}

		/// <summary>Play ortho: authored size clamped so the view still fits the rect.</summary>
		internal static float PlayOrtho(EncounterCameraModel camera, float aspect)
		{
			float fit = FitOrtho(camera, aspect);
			if (camera == null || !camera.OrthoSize.HasValue)
			{
				return fit;
			}

			float authored = camera.OrthoSize.Value;
			if (authored < 0.01f)
			{
				return fit;
			}

			return authored < fit ? authored : fit;
		}

		/// <summary>Clamps a camera center so the ortho view stays inside the authored rect.</summary>
		internal static void ClampCenter(
			EncounterCameraModel camera,
			float x,
			float y,
			float ortho,
			float aspect,
			out float clampedX,
			out float clampedY)
		{
			clampedX = x;
			clampedY = y;
			if (!HasBounds(camera))
			{
				return;
			}

			if (aspect < 0.01f)
			{
				aspect = 1f;
			}

			if (ortho < 0.01f)
			{
				ortho = 0.01f;
			}

			float halfHeight = ortho;
			float halfWidth = ortho * aspect;
			float minX = camera.MinX + halfWidth;
			float maxX = camera.MaxX - halfWidth;
			float minY = camera.MinY + halfHeight;
			float maxY = camera.MaxY - halfHeight;
			if (minX > maxX)
			{
				minX = (camera.MinX + camera.MaxX) * 0.5f;
				maxX = minX;
			}

			if (minY > maxY)
			{
				minY = (camera.MinY + camera.MaxY) * 0.5f;
				maxY = minY;
			}

			clampedX = x < minX ? minX : (x > maxX ? maxX : x);
			clampedY = y < minY ? minY : (y > maxY ? maxY : y);
		}

		/// <summary>Which handle <paramref name="x"/>/<paramref name="y"/> hits, or None.</summary>
		internal static EncounterCameraHandle Hit(
			EncounterCameraModel camera,
			float x,
			float y,
			float handle)
		{
			if (!HasBounds(camera) || handle < 0.01f)
			{
				return EncounterCameraHandle.None;
			}

			bool nearLeft = Near(x, camera.MinX, handle);
			bool nearRight = Near(x, camera.MaxX, handle);
			bool nearBottom = Near(y, camera.MinY, handle);
			bool nearTop = Near(y, camera.MaxY, handle);
			bool inX = x >= camera.MinX - handle && x <= camera.MaxX + handle;
			bool inY = y >= camera.MinY - handle && y <= camera.MaxY + handle;
			if (nearLeft && nearTop)
			{
				return EncounterCameraHandle.NorthWest;
			}

			if (nearRight && nearTop)
			{
				return EncounterCameraHandle.NorthEast;
			}

			if (nearLeft && nearBottom)
			{
				return EncounterCameraHandle.SouthWest;
			}

			if (nearRight && nearBottom)
			{
				return EncounterCameraHandle.SouthEast;
			}

			if (nearTop && inX)
			{
				return EncounterCameraHandle.North;
			}

			if (nearBottom && inX)
			{
				return EncounterCameraHandle.South;
			}

			if (nearLeft && inY)
			{
				return EncounterCameraHandle.West;
			}

			if (nearRight && inY)
			{
				return EncounterCameraHandle.East;
			}

			if (x >= camera.MinX && x <= camera.MaxX && y >= camera.MinY && y <= camera.MaxY)
			{
				return EncounterCameraHandle.Interior;
			}

			return EncounterCameraHandle.None;
		}

		/// <summary>Writes dest from origin plus a handle drag to worldX/worldY.</summary>
		internal static void ApplyHandle(
			EncounterCameraModel dest,
			EncounterCameraModel origin,
			EncounterCameraHandle handle,
			float worldX,
			float worldY,
			float startX,
			float startY)
		{
			if (dest == null || origin == null || handle == EncounterCameraHandle.None)
			{
				return;
			}

			dest.MinX = origin.MinX;
			dest.MinY = origin.MinY;
			dest.MaxX = origin.MaxX;
			dest.MaxY = origin.MaxY;
			dest.LockZoom = origin.LockZoom;
			dest.OrthoSize = origin.OrthoSize;
			if (handle == EncounterCameraHandle.Interior)
			{
				float dx = worldX - startX;
				float dy = worldY - startY;
				dest.MinX = origin.MinX + dx;
				dest.MaxX = origin.MaxX + dx;
				dest.MinY = origin.MinY + dy;
				dest.MaxY = origin.MaxY + dy;
				return;
			}

			if (handle == EncounterCameraHandle.West || handle == EncounterCameraHandle.NorthWest
				|| handle == EncounterCameraHandle.SouthWest)
			{
				dest.MinX = worldX;
			}

			if (handle == EncounterCameraHandle.East || handle == EncounterCameraHandle.NorthEast
				|| handle == EncounterCameraHandle.SouthEast)
			{
				dest.MaxX = worldX;
			}

			if (handle == EncounterCameraHandle.South || handle == EncounterCameraHandle.SouthWest
				|| handle == EncounterCameraHandle.SouthEast)
			{
				dest.MinY = worldY;
			}

			if (handle == EncounterCameraHandle.North || handle == EncounterCameraHandle.NorthWest
				|| handle == EncounterCameraHandle.NorthEast)
			{
				dest.MaxY = worldY;
			}

			Normalize(dest);
		}

		private static bool Near(float value, float target, float handle)
		{
			float delta = value - target;
			return delta <= handle && delta >= -handle;
		}
	}
}
