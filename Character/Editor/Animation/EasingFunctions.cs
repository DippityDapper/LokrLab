using UnityEngine;
using LokrLab;

namespace LokrLab.Editor.Animation
{
	/// <summary>Which curve shape a frame's outgoing transition bakes along.</summary>
	/// <remarks>The game's own ExoSkeletonAnimator doesn't interpolate between frames at all -- it holds one frame for its full duration, then jumps wholesale to the next. "Easing" here means baking an eased curve into a run of extra generated frames, the same technique flipbook/stop-motion animation uses to fake smooth motion with only discrete poses, not true continuous interpolation.</remarks>
	internal enum EasingType
	{
		Linear,
		EaseIn,
		EaseOut,
		EaseInOut
	}

	internal static class EasingFunctions
	{
		/// <summary>Evaluates an easing curve at t (0..1, clamped).</summary>
		internal static float Evaluate(EasingType type, float t)
		{
			t = Mathf.Clamp01(t);
			switch (type)
			{
				case EasingType.EaseIn:
					return t * t;
				case EasingType.EaseOut:
					return 1f - (1f - t) * (1f - t);
				case EasingType.EaseInOut:
					return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
				case EasingType.Linear:
				default:
					return t;
			}
		}
	}
}
