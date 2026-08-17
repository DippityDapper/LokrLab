using System;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free hex-line and clamp helpers for Setup walkability paint.</summary>
	/// <remarks>
	/// Vanilla <c>PointToHexItem</c> clamps off-board clicks to the edge. Setup paint
	/// uses the unclamped OffsetCoord and grows to that cell. Negative col/row cannot
	/// grow — the board only expands from (0,0). Cap is <see cref="EncounterGrowRules.MaxLive"/>.
	/// </remarks>
	internal static class EncounterPaintRules
	{
		/// <summary>Pins a paint cell to the live board, or rejects a hex that would grow backward.</summary>
		internal static bool TryClampCell(int col, int row, out int clampedCol, out int clampedRow)
		{
			clampedCol = 0;
			clampedRow = 0;
			if (col < 0 || row < 0)
			{
				return false;
			}

			int max = EncounterGrowRules.MaxLive - 1;
			clampedCol = col > max ? max : col;
			clampedRow = row > max ? max : row;
			return true;
		}

		/// <summary>Visits every odd-r hex on the shortest cube line, including both ends.</summary>
		/// <remarks>
		/// Fast mouse moves skip cells. Filling the hex line keeps a stroke solid.
		/// Each cell is clamped; negative samples are skipped.
		/// </remarks>
		internal static void ForEachOnLine(int col0, int row0, int col1, int row1, Action<int, int> visit)
		{
			if (visit == null)
			{
				return;
			}

			ToCube(col0, row0, out int q0, out int r0, out int s0);
			ToCube(col1, row1, out int q1, out int r1, out int s1);
			int distance = (Abs(q0 - q1) + Abs(r0 - r1) + Abs(s0 - s1)) / 2;
			if (distance == 0)
			{
				VisitClamped(col1, row1, visit);
				return;
			}

			int lastCol = int.MinValue;
			int lastRow = int.MinValue;
			for (int i = 0; i <= distance; i++)
			{
				float t = i / (float)distance;
				RoundCube(
					q0 + (q1 - q0) * t,
					r0 + (r1 - r0) * t,
					s0 + (s1 - s0) * t,
					out int q,
					out int r);
				FromCube(q, r, out int col, out int row);
				if (col == lastCol && row == lastRow)
				{
					continue;
				}

				lastCol = col;
				lastRow = row;
				VisitClamped(col, row, visit);
			}
		}

		private static void VisitClamped(int col, int row, Action<int, int> visit)
		{
			int clampedCol;
			int clampedRow;
			if (TryClampCell(col, row, out clampedCol, out clampedRow))
			{
				visit(clampedCol, clampedRow);
			}
		}

		private static void ToCube(int col, int row, out int q, out int r, out int s)
		{
			q = col - (row - (row & 1)) / 2;
			r = row;
			s = -q - r;
		}

		private static void FromCube(int q, int r, out int col, out int row)
		{
			row = r;
			col = q + (r - (r & 1)) / 2;
		}

		private static void RoundCube(float q, float r, float s, out int rq, out int rr)
		{
			int nq = (int)Math.Round(q);
			int nr = (int)Math.Round(r);
			int ns = (int)Math.Round(s);
			float dq = Abs(nq - q);
			float dr = Abs(nr - r);
			float ds = Abs(ns - s);
			if (dq > dr && dq > ds)
			{
				nq = -nr - ns;
			}
			else if (dr > ds)
			{
				nr = -nq - ns;
			}

			rq = nq;
			rr = nr;
		}

		private static int Abs(int value)
		{
			return value < 0 ? -value : value;
		}

		private static float Abs(float value)
		{
			return value < 0f ? -value : value;
		}
	}
}
