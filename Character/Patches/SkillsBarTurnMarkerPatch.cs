using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.View.Game.Units;
using LokrLab;

namespace LokrCharacterLab.Patches
{
	/// <summary>
	/// <see cref="SkillsBar.SetSelectedUnit(Unit)"/> indexes <c>skillPerUnits</c> without
	/// TryGetValue. The turn marker is never added to that map; TakeOverAI must not select it.
	/// </summary>
	[HarmonyPatch(typeof(SkillsBar), "SetSelectedUnit", new[] { typeof(Unit) })]
	internal static class SkillsBarTurnMarkerPatch
	{
		private static bool Prefix(SkillsBar __instance, Unit unit)
		{
			if (!EmbeddedFightHost.IsActive)
			{
				return true;
			}

			if (unit == null || SandboxFightControls.IsHudExempt(unit) || unit.unitView == null)
			{
				return false;
			}

			if (__instance.skillPerUnits == null || !__instance.skillPerUnits.ContainsKey(unit.unitView))
			{
				return false;
			}

			SkillsBarSlotCap.Trim(__instance, unit.unitView);
			return true;
		}
	}

	/// <summary>Keeps <see cref="SkillsBar"/> from indexing past its five hex slots.</summary>
	/// <remarks>
	/// Vanilla builds exactly five skill transforms, then walks every interactive skill on the
	/// unit. More than five (every skillProgression option at once) throws in
	/// <c>NotDefaultSkillSelected</c> / <c>SetSelectedSkill</c>.
	/// </remarks>
	internal static class SkillsBarSlotCap
	{
		/// <summary>Drops extra <see cref="MatchSkillsBarUnit"/> entries that have no hex slot.</summary>
		internal static void Trim(SkillsBar bar, UnitViewComponent unitView)
		{
			if (bar == null || bar.skillPerUnits == null || unitView == null || bar.skillsList == null)
			{
				return;
			}

			MatchSkillsBarUnit match;
			if (!bar.skillPerUnits.TryGetValue(unitView, out match) || match == null || match.skills == null)
			{
				return;
			}

			int cap = bar.skillsList.Count;
			if (cap > 0 && match.skills.Count > cap)
			{
				match.skills.RemoveRange(cap, match.skills.Count - cap);
			}
		}
	}

	/// <summary>Trims the bar after vanilla copies every interactive skill onto it.</summary>
	[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.AddSkillsBar))]
	internal static class SkillsBarSlotCapPatch
	{
		private static void Postfix(SkillsBar __instance, UnitViewComponent unitView)
		{
			if (EmbeddedFightHost.IsActive)
			{
				SkillsBarSlotCap.Trim(__instance, unitView);
			}
		}
	}

	/// <summary>Trims before vanilla indexes <c>skillsList</c> by skill count.</summary>
	[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.SetSelectedSkill))]
	internal static class SkillsBarSetSelectedSkillPatch
	{
		private static void Prefix(SkillsBar __instance, UnitViewComponent unitView)
		{
			if (EmbeddedFightHost.IsActive)
			{
				SkillsBarSlotCap.Trim(__instance, unitView);
			}
		}
	}

	/// <summary>Skips highlight when a skill index has no hex slot.</summary>
	[HarmonyPatch(typeof(SkillsBar), nameof(SkillsBar.NotDefaultSkillSelected))]
	internal static class SkillsBarNotDefaultSelectedPatch
	{
		private static bool Prefix(SkillsBar __instance, Unit unit, ref bool __result)
		{
			if (!EmbeddedFightHost.IsActive || __instance == null || unit == null
				|| unit.unitView == null || __instance.skillPerUnits == null)
			{
				return true;
			}

			MatchSkillsBarUnit match;
			if (!__instance.skillPerUnits.TryGetValue(unit.unitView, out match) || match == null
				|| match.skills == null || __instance.skillsList == null)
			{
				__result = false;
				return false;
			}

			int limit = match.skills.Count;
			if (limit > __instance.skillsList.Count)
			{
				limit = __instance.skillsList.Count;
			}

			for (int i = 0; i < limit; i++)
			{
				if (__instance.skillsList[i] != null
					&& __instance.skillsList[i].GetComponent<Portrait>().isSelected)
				{
					__result = i != 0;
					return false;
				}
			}

			__result = false;
			return false;
		}
	}
}
