using System.Collections.Generic;
using Ironhide.Battlechest.Common.Game.Gameboard;
using Ironhide.Battlechest.Common.Hexes;
using Ironhide.Legends.Model.Game;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Game.Units.Abilities;

namespace LokrLab
{
	/// <summary>Shared hero + dummy-enemy spawn used by Sandbox and Ability Lab embedded fights.</summary>
	internal static class SandboxRoster
	{
		/// <summary>A simple melee enemy used as the default opponent.</summary>
		internal const string DefaultEnemyUnitId = "BanditRaider";

		/// <summary>How many hex steps the hero and enemy are placed on either side of the board's center hex.</summary>
		internal const int SpawnDistanceFromCenter = 2;

		/// <summary>Hex direction index the enemy is offset toward from center.</summary>
		internal const int EnemyDirection = 0;

		/// <summary>Opposite of <see cref="EnemyDirection"/> so the two face each other.</summary>
		internal const int HeroDirection = 3;

		/// <summary>Spawns the hero at <paramref name="heroLevel"/> plus one enemy a couple of hex steps apart.</summary>
		internal static void SpawnHeroAndEnemy(Stage stage, string heroUnitId, string enemyUnitId, int heroLevel = 1)
		{
			if (stage == null || string.IsNullOrEmpty(heroUnitId))
			{
				return;
			}

			UnitDefinition heroDefinition = ResolveDefinitionAtLevel(heroUnitId, heroLevel);
			if (heroDefinition == null)
			{
				LokrCharacterLab.LokrCharacterLabPlugin.Log.LogWarning(
					"SandboxRoster: no unit definition for '" + heroUnitId + "' -- nothing to spawn.");
				return;
			}

			HexBoard board = stage.board;
			HexGridItem<GameHexGridItemData> heroHex = OffsetFromCenter(board, HeroDirection, SpawnDistanceFromCenter);
			SpawnAt(stage, heroDefinition, heroHex, UnitGroup.GoodSide, false, true, heroLevel);

			string enemyId = string.IsNullOrEmpty(enemyUnitId) ? DefaultEnemyUnitId : enemyUnitId;
			UnitDefinition enemyDefinition = UnityDefinitionsParser.instance.GetDefinition(enemyId);
			if (enemyDefinition != null)
			{
				HexGridItem<GameHexGridItemData> enemyHex = OffsetFromCenter(board, EnemyDirection, SpawnDistanceFromCenter);
				SpawnAt(stage, enemyDefinition, enemyHex, UnitGroup.BadSide, true, false, 1);
				return;
			}

			LokrCharacterLab.LokrCharacterLabPlugin.Log.LogWarning(
				"SandboxRoster: no unit definition for '" + enemyId + "' -- hero spawned alone.");
		}

		/// <summary>Adds one unit at <paramref name="hex"/> and grants rank skills.</summary>
		internal static Unit SpawnAt(
			Stage stage,
			UnitDefinition definition,
			HexGridItem<GameHexGridItemData> hex,
			UnitGroup group,
			bool flipped,
			bool isHero,
			int level)
		{
			if (stage == null || definition == null || hex == null)
			{
				return null;
			}

			Unit unit = new Unit(hex.data.position, flipped, string.Empty, UnitClass.Generic, group, definition)
			{
				isHero = isHero,
				isAI = group != UnitGroup.GoodSide,
				HexGridItem = hex,
			};
			GrantProgressionSkills(unit, level);
			stage.AddUnit(unit);
			return unit;
		}

		/// <summary>Center hex of the live board, or null when the board is missing.</summary>
		internal static HexGridItem<GameHexGridItemData> CenterHex(HexBoard board)
		{
			if (board == null)
			{
				return null;
			}

			return board.GetHexItem(new OffsetCoord(board.width / 2, board.height / 2));
		}

		/// <summary>Hex a few steps from center in <paramref name="direction"/>, or center when off-board.</summary>
		internal static HexGridItem<GameHexGridItemData> OffsetFromCenter(HexBoard board, int direction, int distance)
		{
			HexGridItem<GameHexGridItemData> center = CenterHex(board);
			if (center == null)
			{
				return null;
			}

			if (distance < 1)
			{
				return center;
			}

			HexCoord coord = HexCoord.GetInDirection(center.coord, direction, distance);
			return board.IsCoordInBounds(coord) ? board.GetHexItem(coord) : center;
		}

		/// <summary>Hex a few steps from center, or null when that step is off-board.</summary>
		internal static HexGridItem<GameHexGridItemData> TryOffsetFromCenter(HexBoard board, int direction, int distance)
		{
			HexGridItem<GameHexGridItemData> center = CenterHex(board);
			if (center == null)
			{
				return null;
			}

			if (distance < 1)
			{
				return center;
			}

			HexCoord coord = HexCoord.GetInDirection(center.coord, direction, distance);
			return board.IsCoordInBounds(coord) ? board.GetHexItem(coord) : null;
		}

		/// <summary>Walks <c>nextLevelArchetype</c> from the base id to the requested 1-based rank.</summary>
		internal static UnitDefinition ResolveDefinitionAtLevel(string unitId, int level)
		{
			UnityDefinitionsParser parser = UnityDefinitionsParser.instance;
			if (parser == null || string.IsNullOrEmpty(unitId))
			{
				return null;
			}

			if (parser.Definitions == null || !parser.Definitions.ContainsKey(unitId))
			{
				return null;
			}

			UnitDefinition current = parser.Definitions[unitId];
			int target = level < 1 ? 1 : level;
			int seen = 1;
			while (seen < target && !string.IsNullOrEmpty(current.nextLevelArchetype))
			{
				string nextId = current.nextLevelArchetype;
				if (!parser.Definitions.ContainsKey(nextId))
				{
					break;
				}

				current = parser.Definitions[nextId];
				seen++;
			}

			return current;
		}

		/// <summary>1-based ranks reachable from this unit's <c>nextLevelArchetype</c> chain.</summary>
		internal static List<int> ListAvailableLevels(string unitId)
		{
			List<int> levels = new List<int>();
			UnityDefinitionsParser parser = UnityDefinitionsParser.instance;
			if (parser == null || string.IsNullOrEmpty(unitId) || !parser.Definitions.ContainsKey(unitId))
			{
				levels.Add(1);
				return levels;
			}

			UnitDefinition current = parser.Definitions[unitId];
			int rank = 1;
			levels.Add(1);
			HashSet<string> seen = new HashSet<string> { current.id };
			while (!string.IsNullOrEmpty(current.nextLevelArchetype)
				&& parser.Definitions.ContainsKey(current.nextLevelArchetype)
				&& seen.Add(current.nextLevelArchetype))
			{
				current = parser.Definitions[current.nextLevelArchetype];
				rank++;
				levels.Add(rank);
			}

			return levels;
		}

		/// <summary>Grants one interactive pick per rank (vanilla level-up) plus passives. The skills bar has five slots.</summary>
		/// <remarks>
		/// Vanilla <c>Hero.UpdateSkills</c> picks one unlocked option per rank at random. The lab
		/// takes the first option so Stage/Sandbox stays deterministic. Granting every option
		/// overflowed the five-slot bar and threw in <c>SkillsBar.NotDefaultSkillSelected</c>.
		/// </remarks>
		private static void GrantProgressionSkills(Unit unit, int upToLevel)
		{
			if (unit == null || unit.unitDefinition == null || unit.unitDefinition.skillProgression == null)
			{
				return;
			}

			int last = upToLevel < 1 ? 1 : upToLevel;
			int interactive = CountInteractiveSkills(unit);
			for (int rank = 1; rank <= last; rank++)
			{
				List<string> skills;
				if (!unit.unitDefinition.skillProgression.TryGetValue(rank, out skills) || skills == null)
				{
					continue;
				}

				bool grantedInteractive = false;
				for (int i = 0; i < skills.Count; i++)
				{
					string skillId = skills[i];
					if (string.IsNullOrEmpty(skillId) || unit.HasSkill(skillId))
					{
						continue;
					}

					Ability ability = null;
					if (AbilitiesDefinitions.instance != null && AbilitiesDefinitions.instance.abilities != null)
					{
						AbilitiesDefinitions.instance.abilities.TryGetValue(skillId, out ability);
					}

					bool passive = ability != null && ability.AbilityBehavior.HasFlag(AbilityBehavior.PASSIVE);
					if (passive)
					{
						unit.AddSkill(skillId);
						continue;
					}

					if (!grantedInteractive && interactive < 5)
					{
						unit.AddSkill(skillId);
						grantedInteractive = true;
						interactive++;
					}
				}
			}
		}

		/// <summary>How many skills the bar would draw for this unit.</summary>
		private static int CountInteractiveSkills(Unit unit)
		{
			int count = 0;
			if (unit == null || unit.skills == null)
			{
				return 0;
			}

			foreach (KeyValuePair<string, SkillActivityPointer> pair in unit.skills)
			{
				if (pair.Value != null && pair.Value.IsInteractive)
				{
					count++;
				}
			}

			return count;
		}
	}
}
