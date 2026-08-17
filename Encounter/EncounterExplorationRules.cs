using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free pocket-aggro math: which parked pockets should wake given live hex positions.</summary>
	internal static class EncounterExplorationRules
	{
		/// <summary>Cube hex coordinate, matching the engine's <c>q</c>/<c>r</c>/<c>s</c> layout without a Unity dependency.</summary>
		internal readonly struct HexPos : IEquatable<HexPos>
		{
			internal readonly int Q;
			internal readonly int R;
			internal readonly int S;

			internal HexPos(int q, int r, int s)
			{
				Q = q;
				R = r;
				S = s;
			}

			/// <summary>Cube hex distance (same formula as the engine's <c>HexCoord.Distance</c>).</summary>
			internal static int Distance(HexPos a, HexPos b)
			{
				return (Math.Abs(a.Q - b.Q) + Math.Abs(a.R - b.R) + Math.Abs(a.S - b.S)) / 2;
			}

			public bool Equals(HexPos other)
			{
				return Q == other.Q && R == other.R && S == other.S;
			}

			public override bool Equals(object obj)
			{
				return obj is HexPos other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (Q * 397 ^ R) * 397 ^ S;
			}
		}

		/// <summary>
		/// One still-parked pocket member's own trigger condition: a radius from its current hex,
		/// or a painted region (independent of its own position) when <see cref="Region"/> is set.
		/// </summary>
		internal readonly struct PocketMember
		{
			internal readonly HexPos Hex;
			internal readonly int Radius;
			internal readonly HashSet<HexPos> Region;

			/// <summary>Radius-based member: wakes when a GoodSide hex is within <paramref name="radius"/> of <paramref name="hex"/>.</summary>
			internal PocketMember(HexPos hex, int radius)
			{
				Hex = hex;
				Radius = radius;
				Region = null;
			}

			/// <summary>Region-based member: wakes when a GoodSide hex is inside <paramref name="region"/>, regardless of <paramref name="hex"/>.</summary>
			internal PocketMember(HexPos hex, HashSet<HexPos> region)
			{
				Hex = hex;
				Radius = 0;
				Region = region;
			}
		}

		/// <summary>
		/// Pocket keys that should wake now: any member whose own radius or region reaches a living
		/// GoodSide hex, OR the pocket's own <paramref name="pocketRegions"/> region is entered
		/// (a trigger that names this pocket directly, independent of any member's own condition).
		/// </summary>
		internal static HashSet<string> PocketsToAggro(
			IReadOnlyList<HexPos> goodHexes,
			IReadOnlyDictionary<string, List<PocketMember>> parkedPockets,
			IReadOnlyDictionary<string, List<HashSet<HexPos>>> pocketRegions = null)
		{
			HashSet<string> aggro = new HashSet<string>(StringComparer.Ordinal);
			if (goodHexes == null || goodHexes.Count == 0 || parkedPockets == null)
			{
				return aggro;
			}

			foreach (KeyValuePair<string, List<PocketMember>> pocket in parkedPockets)
			{
				List<PocketMember> members = pocket.Value;
				if (members == null)
				{
					continue;
				}

				List<HashSet<HexPos>> directRegions;
				if (pocketRegions != null && pocketRegions.TryGetValue(pocket.Key, out directRegions) && directRegions != null)
				{
					for (int r = 0; r < directRegions.Count; r++)
					{
						HashSet<HexPos> region = directRegions[r];
						if (region == null)
						{
							continue;
						}

						for (int g = 0; g < goodHexes.Count; g++)
						{
							if (region.Contains(goodHexes[g]))
							{
								aggro.Add(pocket.Key);
								break;
							}
						}

						if (aggro.Contains(pocket.Key))
						{
							break;
						}
					}
				}

				for (int m = 0; m < members.Count && !aggro.Contains(pocket.Key); m++)
				{
					PocketMember member = members[m];
					if (member.Region != null)
					{
						for (int g = 0; g < goodHexes.Count; g++)
						{
							if (member.Region.Contains(goodHexes[g]))
							{
								aggro.Add(pocket.Key);
								break;
							}
						}

						continue;
					}

					for (int g = 0; g < goodHexes.Count; g++)
					{
						if (HexPos.Distance(member.Hex, goodHexes[g]) <= member.Radius)
						{
							aggro.Add(pocket.Key);
							break;
						}
					}
				}
			}

			return aggro;
		}
	}
}
