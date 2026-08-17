using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>The rotation-shear-scale linear (2x2) composition shared by RigEditorScene.ComputeFrameMatrix (which adds pivot/translation) and DraggablePart's own live shear rendering (which only needs the local 2x2 part).</summary>
	/// <remarks>Kept in one place so the two call sites can't drift apart on the composition convention (M = R(rotation) * Shear(shear) * diag(scaleX, scaleY)) the way two independently-written copies of the same formula could.</remarks>
	internal static class AffineMatrixMath
	{
		/// <summary>Composes a rotation-shear-scale 2x2 matrix.</summary>
		internal static void ComposeLinear(float rotationDegrees, float shearDegrees, float scaleX, float scaleY,
			out float mA, out float mB, out float mC, out float mD)
		{
			float rad = rotationDegrees * Mathf.Deg2Rad;
			float cos = Mathf.Cos(rad);
			float sin = Mathf.Sin(rad);
			float shear = Mathf.Tan(shearDegrees * Mathf.Deg2Rad);

			mA = scaleX * cos;
			mB = scaleX * sin;
			mC = scaleY * (shear * cos - sin);
			mD = scaleY * (shear * sin + cos);
		}
	}
}
