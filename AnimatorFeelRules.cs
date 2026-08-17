using System;
using System.Collections.Generic;

namespace LokrLab
{
	/// <summary>Unity-free Animator feel helpers (rest-delta compensation, temp group pivot, root-motion sampling) for the editor and xUnit.</summary>
	internal static class AnimatorFeelRules
	{
		/// <summary>Vanilla ReloadData converts consecutive root-motion positions into speeds at this timestep (1/30s).</summary>
		internal const float RootMotionSampleDt = 1f / 30f;

		/// <summary>Subtracts a rest-position move from a clip pose delta so the part's world position stays put.</summary>
		internal static void CompensateClipDelta(ref float deltaX, ref float deltaY, float restMoveX, float restMoveY)
		{
			deltaX -= restMoveX;
			deltaY -= restMoveY;
		}

		/// <summary>True when group rotate/scale should use a session temp pivot instead of the selection centroid.</summary>
		internal static bool UseTemporaryGroupPivot(int multiSelectCount, bool tempPivotSet)
		{
			return multiSelectCount > 1 && tempPivotSet;
		}

		/// <summary>Grows an already-authored root-motion list to frameCount with zeros; no-op when the list is empty (clip has no root motion).</summary>
		internal static void EnsureRootMotionLength(List<float> samples, int frameCount)
		{
			if (samples == null || samples.Count == 0 || frameCount < 0)
			{
				return;
			}

			while (samples.Count < frameCount)
			{
				samples.Add(0f);
			}

			while (samples.Count > frameCount)
			{
				samples.RemoveAt(samples.Count - 1);
			}
		}

		/// <summary>Inserts a sample at index, copying the previous value when the list is already authored.</summary>
		internal static void InsertRootMotionSample(List<float> samples, int index)
		{
			if (samples == null || samples.Count == 0)
			{
				return;
			}

			int clamped = index < 0 ? 0 : (index > samples.Count ? samples.Count : index);
			float copied = clamped > 0 ? samples[clamped - 1] : samples[0];
			samples.Insert(clamped, copied);
		}

		/// <summary>Removes the sample at index when the list is already authored.</summary>
		internal static void RemoveRootMotionSample(List<float> samples, int index)
		{
			if (samples == null || samples.Count == 0 || index < 0 || index >= samples.Count)
			{
				return;
			}

			samples.RemoveAt(index);
		}

		/// <summary>Moves a sample from one index to another when the list is already authored.</summary>
		internal static void MoveRootMotionSample(List<float> samples, int fromIndex, int toIndex)
		{
			if (samples == null || samples.Count == 0
				|| fromIndex < 0 || fromIndex >= samples.Count
				|| toIndex < 0 || toIndex >= samples.Count
				|| fromIndex == toIndex)
			{
				return;
			}

			float value = samples[fromIndex];
			samples.RemoveAt(fromIndex);
			samples.Insert(toIndex, value);
		}

		/// <summary>Expands per-authored-frame cumulative positions (pixels) into a 30fps positions array ReloadData can turn into moveCurve.</summary>
		internal static float[] ExpandRootMotionPositions(float[] perFramePositions, float[] frameDurations)
		{
			if (perFramePositions == null || perFramePositions.Length == 0)
			{
				return Array.Empty<float>();
			}

			if (frameDurations == null || frameDurations.Length == 0)
			{
				if (perFramePositions.Length >= 2)
				{
					return (float[])perFramePositions.Clone();
				}

				return new[] { perFramePositions[0], perFramePositions[0] };
			}

			float total = 0f;
			int frameCount = frameDurations.Length;
			for (int i = 0; i < frameCount; i++)
			{
				total += DurationAt(frameDurations, i);
			}

			if (total <= 0f)
			{
				return new[] { perFramePositions[0], perFramePositions[0] };
			}

			int sampleCount = Math.Max(2, (int)Math.Ceiling(total / RootMotionSampleDt) + 1);
			float[] samples = new float[sampleCount];
			for (int s = 0; s < sampleCount; s++)
			{
				float t = s * RootMotionSampleDt;
				if (t > total)
				{
					t = total;
				}

				samples[s] = EvaluateRootMotionAtTime(perFramePositions, frameDurations, t);
			}

			return samples;
		}

		/// <summary>Interpolates authored per-frame cumulative positions at time t seconds from clip start.</summary>
		internal static float EvaluateRootMotionAtTime(float[] perFramePositions, float[] frameDurations, float t)
		{
			if (perFramePositions == null || perFramePositions.Length == 0)
			{
				return 0f;
			}

			if (frameDurations == null || frameDurations.Length == 0)
			{
				return perFramePositions[0];
			}

			if (t <= 0f)
			{
				return perFramePositions[0];
			}

			float acc = 0f;
			int frameCount = frameDurations.Length;
			for (int i = 0; i < frameCount; i++)
			{
				float duration = DurationAt(frameDurations, i);
				float next = acc + duration;
				float a = PositionAt(perFramePositions, i);
				float b = PositionAt(perFramePositions, i + 1);
				if (t <= next || i == frameCount - 1)
				{
					float u = duration > 0f ? (t - acc) / duration : 0f;
					if (u < 0f)
					{
						u = 0f;
					}

					if (u > 1f)
					{
						u = 1f;
					}

					return a + (b - a) * u;
				}

				acc = next;
			}

			return PositionAt(perFramePositions, perFramePositions.Length - 1);
		}

		/// <summary>Downsamples a dense ReloadData positions array onto authored frame-start times.</summary>
		internal static float[] SampleRootMotionAtFrameStarts(float[] densePositions, float[] frameDurations)
		{
			if (densePositions == null || densePositions.Length == 0 || frameDurations == null || frameDurations.Length == 0)
			{
				return Array.Empty<float>();
			}

			float total = 0f;
			for (int i = 0; i < frameDurations.Length; i++)
			{
				total += DurationAt(frameDurations, i);
			}

			float[] perFrame = new float[frameDurations.Length];
			float acc = 0f;
			int last = densePositions.Length - 1;
			for (int i = 0; i < frameDurations.Length; i++)
			{
				float u = total > 0f ? acc / total : 0f;
				float index = u * last;
				int lo = (int)index;
				if (lo < 0)
				{
					lo = 0;
				}

				if (lo > last)
				{
					lo = last;
				}

				int hi = lo < last ? lo + 1 : last;
				float frac = index - lo;
				perFrame[i] = densePositions[lo] + (densePositions[hi] - densePositions[lo]) * frac;
				acc += DurationAt(frameDurations, i);
			}

			return perFrame;
		}

		private static float DurationAt(float[] durations, int index)
		{
			float value = durations[index];
			return value > 0.02f ? value : 0.02f;
		}

		private static float PositionAt(float[] positions, int index)
		{
			if (index < 0)
			{
				return positions[0];
			}

			if (index >= positions.Length)
			{
				return positions[positions.Length - 1];
			}

			return positions[index];
		}
	}
}
