using System.Collections.Generic;
using LokrLab.Editor.Animation;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Checks for "Stand" and "Portrait"/"StandStatic" clips -- the same requirement CustomRigLoader.RequiredAnimationNames enforces at runtime (every base-game call site reading Hero.exoSkeletonDataAsset hardcodes them and throws deep in game code if missing), checked here too at Save time against what the user actually authored.</summary>
	internal static class RequiredAnimationNamesValidator
	{
		/// <summary>Warns if "Stand" or "Portrait"/"StandStatic" is missing.</summary>
		internal static IEnumerable<string> Validate(IReadOnlyList<AnimationClip> clips)
		{
			bool hasStand = false;
			bool hasPortraitOrStandStatic = false;
			foreach (AnimationClip clip in clips)
			{
				if (clip.Name == "Stand")
				{
					hasStand = true;
				}
				else if (clip.Name == "Portrait" || clip.Name == "StandStatic")
				{
					hasPortraitOrStandStatic = true;
				}
			}

			if (!hasStand)
			{
				yield return "No 'Stand' clip authored — Save will auto-generate one from Rest Pose (a single static frame, not a real animation). Author a clip named 'Stand' for an actual animated idle.";
			}
			if (!hasPortraitOrStandStatic)
			{
				yield return "No 'Portrait' or 'StandStatic' clip authored — Save will auto-generate 'StandStatic' from Rest Pose. Author one of those names directly for more control over the portrait pose.";
			}
		}
	}
}
