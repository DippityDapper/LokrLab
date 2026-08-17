using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Drives the timeline's Play/Pause scrub playback, stepping RigEditorScene through the active clip's keyframes at their real durations. Separate from EditorInputController since that's specifically about input, not per-frame animation ticking.</summary>
	internal sealed class AnimationPlaybackController : MonoBehaviour
	{
		private void Update()
		{
			RigEditorScene.TickPlayback(Time.deltaTime);
		}
	}
}
