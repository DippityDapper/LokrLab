using System;
using System.Collections.Generic;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;

namespace LokrLab.Encounter
{
	/// <summary>
	/// Tracks parked BadSide pockets during Encounter Play or a Sandbox-loaded Encounter and wakes
	/// them when a GoodSide unit gets close enough, per pocket member's own aggro radius.
	/// </summary>
	/// <remarks>
	/// Pure grouping/distance math lives in <see cref="EncounterExplorationRules"/> so it stays
	/// Unity-free and testable; this class only knows how to read live <see cref="Unit"/> state.
	/// </remarks>
	internal static class EncounterExploration
	{
		private const string ParkedState = "NOT_IN_INITIATIVE_BAR";
		private const string DeadState = "DEAD";

		/// <summary>
		/// One tracked pocket member: the live spawned unit plus its resolved trigger condition —
		/// <see cref="Region"/> when the combatant authored a <c>triggerId</c> with painted cells,
		/// else plain <see cref="Radius"/>.
		/// </summary>
		internal sealed class Member
		{
			internal Unit Unit;
			internal int Radius;
			internal HashSet<EncounterExplorationRules.HexPos> Region;
		}

		private static Dictionary<string, List<Member>> pockets;
		private static Dictionary<string, List<HashSet<EncounterExplorationRules.HexPos>>> pocketTriggerRegions;

		/// <summary>Starts tracking pockets built right after roster spawn. BadSide combatants only.</summary>
		internal static void BeginTracking(Dictionary<string, List<Member>> parkedPockets)
		{
			pockets = parkedPockets;
		}

		/// <summary>
		/// Regions from triggers that name a pocket directly (<c>EncounterTriggerModel.PocketKey</c>) —
		/// wake that whole pocket on entry, independent of any member's own condition.
		/// </summary>
		internal static void BeginPocketTriggers(Dictionary<string, List<HashSet<EncounterExplorationRules.HexPos>>> regions)
		{
			pocketTriggerRegions = regions;
		}

		/// <summary>Builds one pocket's tracked member list (radius-based) and adds it to <paramref name="pockets"/>.</summary>
		internal static void AddMember(
			Dictionary<string, List<Member>> pockets,
			string pocketKey,
			Unit unit,
			int radius)
		{
			AddMember(pockets, pocketKey, unit, radius, null);
		}

		/// <summary>Builds one pocket's tracked member list and adds it to <paramref name="pockets"/>.</summary>
		/// <remarks><paramref name="region"/> non-null wins over <paramref name="radius"/> when the pocket is scanned.</remarks>
		internal static void AddMember(
			Dictionary<string, List<Member>> pockets,
			string pocketKey,
			Unit unit,
			int radius,
			HashSet<EncounterExplorationRules.HexPos> region)
		{
			if (pockets == null || unit == null || string.IsNullOrEmpty(pocketKey))
			{
				return;
			}

			List<Member> members;
			if (!pockets.TryGetValue(pocketKey, out members))
			{
				members = new List<Member>();
				pockets[pocketKey] = members;
			}

			members.Add(new Member { Unit = unit, Radius = radius, Region = region });
		}

		/// <summary>Fresh empty pocket map for a spawn pass to fill via <see cref="AddMember"/>.</summary>
		internal static Dictionary<string, List<Member>> NewPocketMap()
		{
			return new Dictionary<string, List<Member>>(StringComparer.Ordinal);
		}

		/// <summary>Clears tracked pockets between Play sessions.</summary>
		internal static void Reset()
		{
			pockets = null;
			pocketTriggerRegions = null;
		}

		/// <summary>
		/// True while at least one exploration-tracked pocket still has a living, still-parked
		/// member. Scoped to units this system actually parked — never a permanently-excluded
		/// unit (dummy / hazard / trap kinds), since those are never added to <see cref="pockets"/>.
		/// </summary>
		internal static bool HasLivingParkedMembers()
		{
			if (pockets == null)
			{
				return false;
			}

			foreach (KeyValuePair<string, List<Member>> pocket in pockets)
			{
				List<Member> members = pocket.Value;
				for (int i = 0; members != null && i < members.Count; i++)
				{
					Member member = members[i];
					if (member.Unit != null && member.Unit.states != null
						&& !member.Unit.states.IsOn(DeadState) && member.Unit.states.IsOn(ParkedState))
					{
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>Scans still-parked pockets against living GoodSide units and wakes any that are close enough.</summary>
		internal static void Tick(Stage stage)
		{
			if (pockets == null || pockets.Count == 0 || stage == null || stage.units == null)
			{
				return;
			}

			List<EncounterExplorationRules.HexPos> goodHexes = new List<EncounterExplorationRules.HexPos>();
			for (int i = 0; i < stage.units.Count; i++)
			{
				Unit unit = stage.units[i];
				if (unit != null && unit.unitGroup == UnitGroup.GoodSide && unit.states != null
					&& !unit.states.IsOn(DeadState) && unit.HexGridItem != null)
				{
					goodHexes.Add(ToHexPos(unit.HexGridItem.coord));
				}
			}

			if (goodHexes.Count == 0)
			{
				return;
			}

			Dictionary<string, List<EncounterExplorationRules.PocketMember>> parked =
				new Dictionary<string, List<EncounterExplorationRules.PocketMember>>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, List<Member>> pocket in pockets)
			{
				List<EncounterExplorationRules.PocketMember> members = null;
				List<Member> tracked = pocket.Value;
				for (int i = 0; tracked != null && i < tracked.Count; i++)
				{
					Member member = tracked[i];
					if (member.Unit == null || member.Unit.states == null
						|| member.Unit.states.IsOn(DeadState) || !member.Unit.states.IsOn(ParkedState)
						|| member.Unit.HexGridItem == null)
					{
						continue;
					}

					if (members == null)
					{
						members = new List<EncounterExplorationRules.PocketMember>();
					}

					EncounterExplorationRules.HexPos hex = ToHexPos(member.Unit.HexGridItem.coord);
					members.Add(member.Region != null
						? new EncounterExplorationRules.PocketMember(hex, member.Region)
						: new EncounterExplorationRules.PocketMember(hex, member.Radius));
				}

				if (members != null)
				{
					parked[pocket.Key] = members;
				}
			}

			if (parked.Count == 0)
			{
				pockets = null;
				return;
			}

			HashSet<string> aggro = EncounterExplorationRules.PocketsToAggro(goodHexes, parked, pocketTriggerRegions);
			if (aggro.Count == 0)
			{
				return;
			}

			foreach (string key in aggro)
			{
				List<Member> members = pockets[key];
				int woken = 0;
				for (int i = 0; members != null && i < members.Count; i++)
				{
					Unit unit = members[i].Unit;
					if (unit != null && unit.states != null && !unit.states.IsOn(DeadState))
					{
						unit.states.Disable(ParkedState);
						AddInitiativePortrait(unit);
						woken++;
					}
				}

				LokrLabPlugin.Log.LogInfo(
					"EncounterExploration: pocket '" + key + "' aggroed, " + woken + " unit(s) woken, "
					+ (pockets.Count - 1) + " pocket(s) still tracked.");
				pockets.Remove(key);
			}

			LokrLab.Lab.SetStatus("Enemies spotted!");
		}

		/// <summary>
		/// Adds this unit's portrait to the live initiative bar.
		/// </summary>
		/// <remarks>
		/// The bar builds <c>initiativePositions</c> as a one-time snapshot at
		/// <c>FightStartedEventHandler</c> (<c>InitiativeBar.InitializeInitiativeBarIconPositions</c>
		/// reading <c>UnitsWithVisibleInitiativeFromActive()</c> once) and otherwise only adds a
		/// portrait via <c>StageControllerComponent.AddUnitView</c>, which itself skips units that
		/// are <c>NOT_IN_INITIATIVE_BAR</c> at spawn time. Toggling the state later never revisits
		/// either path, so a woken pocket member needs its portrait added explicitly here — the
		/// same call vanilla makes for a unit spawned mid-fight.
		/// </remarks>
		private static void AddInitiativePortrait(Unit unit)
		{
			if (unit.unitView == null || InitiativeBar.initiativeInst == null || unit.states == null
				|| unit.states.IsOn("NOT_IN_INITIATIVE_BAR_VISUAL_ONLY"))
			{
				return;
			}

			// InitiativeBar.AddPortrait reads GetComponentInChildren<ExoSkeletonData>().asset
			// unconditionally and NREs on a unit with no rig (dummy / hazard kinds). Those are
			// filtered out of tracking upstream (EncounterRoster.BeginExploration), but guard here
			// too so a future edge case degrades to "no portrait" instead of aborting Tick().
			if (unit.unitView.GetComponentInChildren<ExoSkeletonData>() == null)
			{
				return;
			}

			InitiativeBar.initiativeInst.AddPortrait(unit.unitView);
		}

		private static EncounterExplorationRules.HexPos ToHexPos(HexCoord coord)
		{
			return new EncounterExplorationRules.HexPos(coord.q, coord.r, coord.s);
		}
	}
}
