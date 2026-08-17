using System;
using LokrLabApi;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>Applies authored Play camera limits on the embed <see cref="CameraBase"/>.</summary>
	/// <remarks>
	/// Setup stays unclamped so the rect can be drawn. Sandbox 1v1 and campaign
	/// fights do not read this. A Sandbox Encounter fight is <see cref="EncounterSandbox"/>
	/// and does. Do not grow <c>EmbeddedFightRequest</c>.
	/// </remarks>
	internal static class EncounterCamera
	{
		/// <summary>True when Encounter Play should clamp pan to the authored rect.</summary>
		internal static bool ShouldClamp
		{
			get
			{
				return EncounterSandbox.IsArmed
					&& EncounterCameraRules.HasBounds(EncounterSandbox.File != null ? EncounterSandbox.File.Camera : null);
			}
		}

		/// <summary>True when Encounter Sandbox should skip vanilla fight-camera autofocus.</summary>
		/// <remarks>
		/// GameplayCamera LateUpdate, KeepTargetsOnCamera, and ZoomOnAction use a
		/// fullscreen CameraWindow, so pans miss the hole. Temporary until
		/// hole-space autofocus is rewritten. Drag and wheel zoom stay on
		/// EmbeddedFightCameraPatches. See
		/// docs/issues/unresolved/sandbox-encounter-camera-autofocus-wrong.md.
		/// </remarks>
		internal static bool SuppressFightAutofocus
		{
			get
			{
				return EncounterSandbox.IsArmed;
			}
		}

		/// <summary>True when Encounter Play should ignore the mouse wheel.</summary>
		internal static bool LockZoom
		{
			get
			{
				return ShouldClamp
					&& EncounterSandbox.File.Camera.LockZoom;
			}
		}

		/// <summary>Writes <c>cameraLimits</c> and ortho min/max from the authored rect.</summary>
		/// <remarks>
		/// Leaves GameplayCamera.CameraAdjust off. Encounter Sandbox autofocus is
		/// suppressed until hole-space targeting is rewritten.
		/// </remarks>
		/// <param name="frame">When true, centers the camera and applies Play ortho once. Later limit rewrites must not snap pan.</param>
		internal static void ApplyPlay(CameraBase cameraBase, bool frame = false)
		{
			if (cameraBase == null || !ShouldClamp)
			{
				return;
			}

			SandboxFightControls.CaptureCameraIfNeeded(cameraBase);
			EncounterCameraModel camera = EncounterSandbox.File.Camera;
			EncounterCameraRules.Normalize(camera);
			Camera hole = ResolveHoleCamera();
			if (hole == null)
			{
				hole = cameraBase.GetComponent<Camera>();
			}

			float aspect = hole != null && hole.aspect > 0.01f ? hole.aspect : 1f;
			float fit = EncounterCameraRules.FitOrtho(camera, aspect);
			float play = EncounterCameraRules.PlayOrtho(camera, aspect);
			float minOrtho = camera.LockZoom ? play : LokrLab.SandboxFightControls.EmbedMinOrthoSize;
			float maxOrtho = camera.LockZoom ? play : fit;
			Bounds limits = new Bounds();
			limits.SetMinMax(
				new Vector3(camera.MinX, camera.MinY, -1f),
				new Vector3(camera.MaxX, camera.MaxY, 1f));
			cameraBase.checkEncounterLimits = true;
			cameraBase.extendEncounterLimitsWidth = false;
			cameraBase.cameraLimits = limits;
			cameraBase.inGameMinOrthoSize = minOrtho;
			cameraBase.inGameMaxOrthoSize = maxOrtho;
			cameraBase.orthoSizeMin = minOrtho;
			cameraBase.orthoSizeMax = maxOrtho;
			if (hole != null)
			{
				if (frame || camera.LockZoom)
				{
					hole.orthographicSize = play;
				}
				else if (hole.orthographicSize > fit)
				{
					hole.orthographicSize = fit;
				}

				if (frame)
				{
					Vector3 center = new Vector3(
						(camera.MinX + camera.MaxX) * 0.5f,
						(camera.MinY + camera.MaxY) * 0.5f,
						hole.transform.position.z);
					hole.transform.position = center;
					cameraBase.transform.position = new Vector3(center.x, center.y, cameraBase.transform.position.z);
				}
			}

			if (cameraBase.gameplayCamera != null)
			{
				cameraBase.gameplayCamera.TakenOver = false;
				cameraBase.gameplayCamera.CameraAdjust = false;
			}

			if (cameraBase.cinematicCamera != null)
			{
				cameraBase.cinematicCamera.CameraAdjust = false;
			}
		}

		/// <summary>Keeps the embed camera center so the Play view stays inside the authored rect.</summary>
		/// <remarks>
		/// Embed drag writes <c>transform.position</c> and never calls
		/// <c>ClampTargetCameraPosition</c>. Zoom lock can succeed while pan stays free
		/// unless this runs after that write.
		/// </remarks>
		internal static void ClampPlayPosition(CameraBase cameraBase, Camera hole)
		{
			if (cameraBase == null || !ShouldClamp)
			{
				return;
			}

			if (hole == null)
			{
				hole = cameraBase.GetComponent<Camera>();
			}

			float aspect = hole != null && hole.aspect > 0.01f ? hole.aspect : 1f;
			float ortho = hole != null ? hole.orthographicSize : EncounterCameraRules.PlayOrtho(EncounterSandbox.File.Camera, aspect);
			float x;
			float y;
			EncounterCameraRules.ClampCenter(
				EncounterSandbox.File.Camera,
				cameraBase.transform.position.x,
				cameraBase.transform.position.y,
				ortho,
				aspect,
				out x,
				out y);
			Vector3 position = cameraBase.transform.position;
			cameraBase.transform.position = new Vector3(x, y, position.z);
			if (hole != null && hole.transform != cameraBase.transform)
			{
				Vector3 holePosition = hole.transform.position;
				hole.transform.position = new Vector3(x, y, holePosition.z);
			}
		}

		/// <summary>Copies the Setup hole frustum into the encounter camera rect.</summary>
		internal static bool CaptureCurrentView(EncounterFileModel file, Camera hole)
		{
			if (file == null || hole == null || !hole.orthographic)
			{
				return false;
			}

			float halfHeight = hole.orthographicSize;
			float halfWidth = halfHeight * hole.aspect;
			Vector3 center = hole.transform.position;
			file.Camera = EncounterCameraRules.FromCorners(
				center.x - halfWidth,
				center.y - halfHeight,
				center.x + halfWidth,
				center.y + halfHeight);
			file.Camera.LockZoom = true;
			file.Camera.OrthoSize = halfHeight;
			return EncounterCameraRules.HasBounds(file.Camera);
		}

		/// <summary>The embed hole camera, or null when Setup/Play is not showing a board.</summary>
		internal static Camera ResolveHoleCamera()
		{
			Func<Camera> getter = LokrLabApi.LokrLabApi.GetEmbeddedSceneCamera;
			LabHost host = LokrLabApi.LokrLabApi.Host;
			if (getter == null && host != null)
			{
				getter = host.GetEmbeddedSceneCamera;
			}

			return getter != null ? getter() : null;
		}
	}
}
