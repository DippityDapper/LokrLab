using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LokrModAPI.Serialization;

namespace LokrLab.Encounter
{
	/// <summary>One combatant row in <c>encounter.json</c>.</summary>
	internal sealed class EncounterCombatantModel
	{
		/// <summary>Unique legal slug inside this encounter.</summary>
		internal string Id = string.Empty;

		/// <summary><c>GoodSide</c> or <c>BadSide</c>.</summary>
		internal string Side = EncounterFileModel.GoodSide;

		/// <summary><c>character</c> or <c>unit</c>.</summary>
		internal string Source = EncounterFileModel.SourceUnit;

		/// <summary>Character folder id when <see cref="Source"/> is <c>character</c>.</summary>
		internal string ProjectId = string.Empty;

		/// <summary>Vanilla or loaded unit id when <see cref="Source"/> is <c>unit</c>.</summary>
		internal string UnitId = string.Empty;

		/// <summary>1-based hero rank. Default 1. Character / hero ranks only.</summary>
		internal int Level = 1;

		/// <summary>Optional board column. Null means Play uses the center-offset heuristic.</summary>
		internal int? Col;

		/// <summary>Optional board row. Null means Play uses the center-offset heuristic.</summary>
		internal int? Row;

		/// <summary>Facing. Default false.</summary>
		internal bool Flipped;

		/// <summary>Exploration wake-together group. BadSide only. Empty means solo (keyed by <see cref="Id"/>).</summary>
		internal string Pocket = string.Empty;

		/// <summary>Per-unit aggro radius override. BadSide only. Null falls back to the file default.</summary>
		internal int? AggroRadius;

		/// <summary>Painted trigger region id this unit wakes on. BadSide only. Wins over <see cref="AggroRadius"/> when set and the region has cells.</summary>
		internal string TriggerId = string.Empty;
	}

	/// <summary>One sparse painted trigger-region hex in <c>encounter.json</c>.</summary>
	internal sealed class EncounterHexTrigger
	{
		/// <summary>Board column.</summary>
		internal int Col;

		/// <summary>Board row.</summary>
		internal int Row;

		/// <summary>Trigger id this hex belongs to.</summary>
		internal string TriggerId = string.Empty;
	}

	/// <summary>One trigger in the Encounter Node Tree catalog.</summary>
	internal sealed class EncounterTriggerModel
	{
		/// <summary>Stable id. Referenced by painted cells and by combatants' <c>TriggerId</c>.</summary>
		internal string Id = string.Empty;

		/// <summary>Pocket this trigger wakes as a whole when entered. Empty means it wakes no pocket by itself — only combatants that individually set this trigger.</summary>
		internal string PocketKey = string.Empty;
	}

	/// <summary>One sparse walkability override in <c>encounter.json</c>.</summary>
	internal sealed class EncounterHexOverride
	{
		/// <summary>Board column.</summary>
		internal int Col;

		/// <summary>Board row.</summary>
		internal int Row;

		/// <summary>Authored passable flag. Overrides the template cell.</summary>
		internal bool Walkable;
	}

	/// <summary>One sparse floor-tile override in <c>encounter.json</c>.</summary>
	internal sealed class EncounterHexTile
	{
		/// <summary>Board column.</summary>
		internal int Col;

		/// <summary>Board row.</summary>
		internal int Row;

		/// <summary>Vanilla <c>HexaTile.terrainId</c> cloned onto the template Tilemap.</summary>
		internal int TerrainId;

		/// <summary>Source-stage prefab when this stamp is not from the host template.</summary>
		internal string Template = string.Empty;
	}

	/// <summary>One terrain in the Encounter Node Tree catalog.</summary>
	internal sealed class EncounterTerrainModel
	{
		/// <summary>Vanilla or minted <c>HexaTile.terrainId</c>.</summary>
		internal int TerrainId;

		/// <summary>Display name from TerrainData, or the custom label.</summary>
		internal string Name = string.Empty;

		/// <summary><c>template</c>, <c>import</c>, or <c>custom</c>.</summary>
		internal string Source = EncounterFileModel.TerrainSourceTemplate;

		/// <summary>templates prefab that owns the TerrainData. Empty for custom.</summary>
		internal string Template = string.Empty;
	}

	/// <summary>Authored Play camera AABB and zoom lock in <c>encounter.json</c>.</summary>
	internal sealed class EncounterCameraModel
	{
		/// <summary>World-space left edge.</summary>
		internal float MinX;

		/// <summary>World-space bottom edge.</summary>
		internal float MinY;

		/// <summary>World-space right edge.</summary>
		internal float MaxX;

		/// <summary>World-space top edge.</summary>
		internal float MaxY;

		/// <summary>When true, Play refuses wheel zoom.</summary>
		internal bool LockZoom = true;

		/// <summary>Optional fixed ortho. Null means fit the rect to the Play hole.</summary>
		internal float? OrthoSize;
	}

	/// <summary>One placed scenario deco in <c>encounter.json</c>.</summary>
	internal sealed class EncounterPropModel
	{
		/// <summary>Unique legal slug inside this encounter.</summary>
		internal string Id = string.Empty;

		/// <summary>Lowercase <c>scenario</c> prefab name passed to LoadAsset.</summary>
		internal string PrefabName = string.Empty;

		/// <summary>When true, place snaps to a hex center. Missing key loads as true.</summary>
		internal bool Snap = true;

		/// <summary>Optional board column. Null means the prop is listed but not on the board.</summary>
		internal int? Col;

		/// <summary>Optional board row. Null means the prop is listed but not on the board.</summary>
		internal int? Row;

		/// <summary>World X when <see cref="Snap"/> is false. Null until free-placed.</summary>
		internal float? X;

		/// <summary>World Y when <see cref="Snap"/> is false. Null until free-placed.</summary>
		internal float? Y;

		/// <summary>Facing. Default false.</summary>
		internal bool Flipped;
	}

	/// <summary>Typed <c>encounter.json</c> payload. Unity-free so xUnit can round-trip it.</summary>
	/// <remarks>
	/// Display name stays on <c>project.json</c>. Schema v2 adds sparse walkability overrides.
	/// Live board size is derived from walkable hexes (overrides + placements). Leftover
	/// v3 <c>width</c> / <c>height</c> expand into walkable overrides and are not rewritten.
	/// New files set <c>walkableDefault</c> false (blank blocked canvas). Missing key
	/// means true so v1/v2 files keep the template. Schema v4 adds sparse floor tiles.
	/// Schema v5 adds the terrain catalog (host / import / custom). Schema v6 adds
	/// <c>tilesDefault</c>: new files write false (strip the template floor). Missing
	/// key means true so v1–v5 files keep the template Tilemap. Schema v7 adds
	/// <c>props</c>. Schema v8 adds optional <c>camera</c> (Play bounds + lock
	/// zoom). Schema v9 adds per-prop <c>snap</c> (default true) and free-move
	/// <c>x</c> / <c>y</c>. Schema v10 adds <c>exploration</c> (default false)
	/// and <c>defaultAggroRadius</c>, plus per-combatant <c>pocket</c> and
	/// <c>aggroRadius</c> (BadSide only). Schema v12 adds the trigger
	/// catalog <c>triggers</c> (id + optional <c>pocketKey</c> — entering the
	/// region wakes that whole pocket) and sparse painted <c>triggerCells</c>
	/// (col/row/triggerId), plus per-combatant <c>triggerId</c> (BadSide
	/// only) so one unit can opt into a trigger even outside its target
	/// pocket. Missing keys on v1–v10 files mean no painted regions
	/// (radius-only, unchanged behavior). v11 (same-day, never confirmed
	/// in-game) used <c>triggers</c> for painted cells directly with no
	/// catalog or pocket targeting; that shape does not migrate. Schema
	/// v13 adds combatant source <c>spawn</c> — a GoodSide hex position
	/// with no fixed Character/unit, filled at load time by whoever
	/// spawns into the encounter (Sandbox, later Adventures). v1–v12
	/// files still load (no spawn rows existed before).
	/// </remarks>
	internal sealed class EncounterFileModel
	{
		/// <summary>Payload filename inside an encounter folder.</summary>
		internal const string FileName = "encounter.json";

		/// <summary>Current authored schema.</summary>
		internal const int CurrentSchemaVersion = 13;

		/// <summary>Fallback per-unit aggro radius (hexes) when neither the combatant nor the file overrides it.</summary>
		internal const int DefaultAggroRadiusValue = 4;

		/// <summary>v1 default arena prefab.</summary>
		internal const string DefaultTemplate = "fighttesterempty";

		/// <summary>Player / friendly side token.</summary>
		internal const string GoodSide = "GoodSide";

		/// <summary>Enemy side token.</summary>
		internal const string BadSide = "BadSide";

		/// <summary>Combatant sourced from a Character project.</summary>
		internal const string SourceCharacter = "character";

		/// <summary>Combatant sourced from a unit definition id.</summary>
		internal const string SourceUnit = "unit";

		/// <summary>Authored hex position with no fixed hero. GoodSide only. Filled by whoever loads the encounter (Sandbox, later Adventures).</summary>
		internal const string SourceSpawn = "spawn";

		/// <summary>Terrain that comes from this encounter's host template.</summary>
		internal const string TerrainSourceTemplate = "template";

		/// <summary>Terrain imported from another stage prefab.</summary>
		internal const string TerrainSourceImport = "import";

		/// <summary>Lab-authored terrain row. Floor art is later.</summary>
		internal const string TerrainSourceCustom = "custom";

		private static readonly Regex SchemaPattern = new Regex("\"schemaVersion\"\\s*:\\s*(\\d+)");
		private static readonly Regex TemplatePattern = new Regex("\"template\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
		private static readonly Regex WidthPattern = new Regex("\"width\"\\s*:\\s*(-?\\d+)");
		private static readonly Regex HeightPattern = new Regex("\"height\"\\s*:\\s*(-?\\d+)");
		private static readonly Regex WalkableDefaultPattern = new Regex("\"walkableDefault\"\\s*:\\s*(true|false)");
		private static readonly Regex TilesDefaultPattern = new Regex("\"tilesDefault\"\\s*:\\s*(true|false)");
		private static readonly Regex ExplorationPattern = new Regex("\"exploration\"\\s*:\\s*(true|false)");
		private static readonly Regex DefaultAggroRadiusPattern = new Regex("\"defaultAggroRadius\"\\s*:\\s*(-?\\d+)");
		private static readonly Regex CombatantsPattern = new Regex("\"combatants\"\\s*:\\s*\\[");
		private static readonly Regex OverridesPattern = new Regex("\"overrides\"\\s*:\\s*\\[");
		private static readonly Regex TilesPattern = new Regex("\"tiles\"\\s*:\\s*\\[");
		private static readonly Regex TerrainsPattern = new Regex("\"terrains\"\\s*:\\s*\\[");
		private static readonly Regex PropsPattern = new Regex("\"props\"\\s*:\\s*\\[");
		private static readonly Regex TriggersPattern = new Regex("\"triggers\"\\s*:\\s*\\[");
		private static readonly Regex TriggerCellsPattern = new Regex("\"triggerCells\"\\s*:\\s*\\[");
		private static readonly Regex CameraPattern = new Regex("\"camera\"\\s*:\\s*\\{");
		private static readonly Regex ObjectStringPattern = new Regex("\"([A-Za-z][A-Za-z0-9_]*)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
		private static readonly Regex ObjectIntPattern = new Regex("\"([A-Za-z][A-Za-z0-9_]*)\"\\s*:\\s*(-?\\d+)");
		private static readonly Regex ObjectFloatPattern = new Regex("\"([A-Za-z][A-Za-z0-9_]*)\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
		private static readonly Regex ObjectBoolPattern = new Regex("\"([A-Za-z][A-Za-z0-9_]*)\"\\s*:\\s*(true|false)");
		private static readonly Regex LegalIdPattern = new Regex("^[a-z][a-z0-9_]*$");

		/// <summary>File schema version. v13 adds combatant source <c>spawn</c> (on top of v12's trigger catalog / triggerCells / triggerId and v10's exploration / defaultAggroRadius / pocket / aggroRadius). v1–v12 still load; v11 does not migrate.</summary>
		internal int SchemaVersion = CurrentSchemaVersion;

		/// <summary>Arena prefab name loaded by the fight embed.</summary>
		internal string Template = DefaultTemplate;

		/// <summary>Legacy live width from 0.12.53. Normalized into walkable overrides.</summary>
		internal int? Width;

		/// <summary>Legacy live height from 0.12.53. Normalized into walkable overrides.</summary>
		internal int? Height;

		/// <summary>When false, cells with no override are impassable. New encounters start false.</summary>
		internal bool WalkableDefault;

		/// <summary>When false, the template floor is stripped. New encounters start false.</summary>
		internal bool TilesDefault;

		/// <summary>When true, Play parks BadSide combatants until a pocket aggroes. Default false (today's instant fight).</summary>
		internal bool Exploration;

		/// <summary>Hex-distance aggro radius used by BadSide combatants that don't set their own. Default 4.</summary>
		internal int DefaultAggroRadius = DefaultAggroRadiusValue;

		/// <summary>Authored combatants. Empty is legal to save.</summary>
		internal List<EncounterCombatantModel> Combatants = new List<EncounterCombatantModel>();

		/// <summary>Sparse walkability overrides. Empty means the template cells stand.</summary>
		internal List<EncounterHexOverride> Overrides = new List<EncounterHexOverride>();

		/// <summary>Sparse floor-tile overrides. Empty plus <see cref="TilesDefault"/> true means the template Tilemap stands.</summary>
		internal List<EncounterHexTile> Tiles = new List<EncounterHexTile>();

		/// <summary>Terrain catalog for the Node Tree. Empty means scan the host template.</summary>
		internal List<EncounterTerrainModel> Terrains = new List<EncounterTerrainModel>();

		/// <summary>Authored scenario deco instances. Empty is legal.</summary>
		internal List<EncounterPropModel> Props = new List<EncounterPropModel>();

		/// <summary>Trigger catalog for the Node Tree. Empty means no triggers.</summary>
		internal List<EncounterTriggerModel> Triggers = new List<EncounterTriggerModel>();

		/// <summary>Sparse painted trigger-region hexes. Empty means no painted regions.</summary>
		internal List<EncounterHexTrigger> TriggerCells = new List<EncounterHexTrigger>();

		/// <summary>Play camera AABB. Null means the embed stays unclamped.</summary>
		internal EncounterCameraModel Camera;

		/// <summary>Empty payload on <see cref="DefaultTemplate"/> with a blocked, tile-less canvas.</summary>
		internal static EncounterFileModel CreateEmpty()
		{
			return new EncounterFileModel
			{
				WalkableDefault = false,
				TilesDefault = false,
				Exploration = false,
				DefaultAggroRadius = DefaultAggroRadiusValue
			};
		}

		/// <summary>True when <paramref name="side"/> is a v1 legal side.</summary>
		internal static bool IsLegalSide(string side)
		{
			return string.Equals(side, GoodSide, StringComparison.Ordinal)
				|| string.Equals(side, BadSide, StringComparison.Ordinal);
		}

		/// <summary>True when <paramref name="source"/> is a legal combatant source.</summary>
		internal static bool IsLegalSource(string source)
		{
			return string.Equals(source, SourceCharacter, StringComparison.Ordinal)
				|| string.Equals(source, SourceUnit, StringComparison.Ordinal)
				|| string.Equals(source, SourceSpawn, StringComparison.Ordinal);
		}

		/// <summary>True when <paramref name="source"/> is a v5 legal terrain source.</summary>
		internal static bool IsLegalTerrainSource(string source)
		{
			return string.Equals(source, TerrainSourceTemplate, StringComparison.Ordinal)
				|| string.Equals(source, TerrainSourceImport, StringComparison.Ordinal)
				|| string.Equals(source, TerrainSourceCustom, StringComparison.Ordinal);
		}

		/// <summary>Validates one combatant. Returns an error, or null when the row is legal.</summary>
		internal static string ValidateCombatant(EncounterCombatantModel combatant, ICollection<string> usedIds)
		{
			if (combatant == null)
			{
				return "Combatant is missing.";
			}

			if (string.IsNullOrEmpty(combatant.Id) || !LegalIdPattern.IsMatch(combatant.Id))
			{
				return "Combatant id must be a legal slug.";
			}

			if (usedIds != null && usedIds.Contains(combatant.Id))
			{
				return "Combatant id '" + combatant.Id + "' is already used.";
			}

			if (!IsLegalSide(combatant.Side))
			{
				return "Combatant side must be GoodSide or BadSide.";
			}

			if (!IsLegalSource(combatant.Source))
			{
				return "Combatant source must be character, unit, or spawn.";
			}

			if (string.Equals(combatant.Source, SourceCharacter, StringComparison.Ordinal)
				&& string.IsNullOrEmpty(combatant.ProjectId))
			{
				return "Character combatant needs a projectId.";
			}

			if (string.Equals(combatant.Source, SourceUnit, StringComparison.Ordinal)
				&& string.IsNullOrEmpty(combatant.UnitId))
			{
				return "Unit combatant needs a unitId.";
			}

			if (string.Equals(combatant.Source, SourceSpawn, StringComparison.Ordinal)
				&& !string.Equals(combatant.Side, GoodSide, StringComparison.Ordinal))
			{
				return "Hero spawn points must be GoodSide.";
			}

			if (combatant.Level < 1)
			{
				return "Combatant level must be 1 or higher.";
			}

			return null;
		}

		/// <summary>Validates one prop. Returns an error, or null when the row is legal.</summary>
		internal static string ValidateProp(EncounterPropModel prop, ICollection<string> usedIds)
		{
			if (prop == null)
			{
				return "Prop is missing.";
			}

			if (string.IsNullOrEmpty(prop.Id) || !LegalIdPattern.IsMatch(prop.Id))
			{
				return "Prop id must be a legal slug.";
			}

			if (usedIds != null && usedIds.Contains(prop.Id))
			{
				return "Prop id '" + prop.Id + "' is already used.";
			}

			if (string.IsNullOrEmpty(prop.PrefabName))
			{
				return "Prop needs a prefabName.";
			}

			return null;
		}

		/// <summary>Parses <c>encounter.json</c> text. Invalid combatant rows are skipped.</summary>
		internal static bool TryParse(string json, out EncounterFileModel model, out string error)
		{
			model = CreateEmpty();
			error = null;
			if (string.IsNullOrWhiteSpace(json))
			{
				error = "Empty encounter.json.";
				return false;
			}

			Match schema = SchemaPattern.Match(json);
			if (schema.Success)
			{
				int version;
				if (int.TryParse(schema.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
				{
					model.SchemaVersion = version;
				}
			}

			Match template = TemplatePattern.Match(json);
			if (template.Success)
			{
				string name = Unescape(template.Groups[1].Value);
				if (!string.IsNullOrEmpty(name))
				{
					model.Template = name;
				}
			}

			TryParseOptionalSize(json, WidthPattern, out model.Width);
			TryParseOptionalSize(json, HeightPattern, out model.Height);
			Match walkableDefault = WalkableDefaultPattern.Match(json);
			model.WalkableDefault = !walkableDefault.Success
				|| string.Equals(walkableDefault.Groups[1].Value, "true", StringComparison.Ordinal);
			Match tilesDefault = TilesDefaultPattern.Match(json);
			model.TilesDefault = !tilesDefault.Success
				|| string.Equals(tilesDefault.Groups[1].Value, "true", StringComparison.Ordinal);
			Match exploration = ExplorationPattern.Match(json);
			model.Exploration = exploration.Success
				&& string.Equals(exploration.Groups[1].Value, "true", StringComparison.Ordinal);
			Match defaultAggroRadius = DefaultAggroRadiusPattern.Match(json);
			int parsedAggroRadius;
			model.DefaultAggroRadius = defaultAggroRadius.Success
				&& int.TryParse(defaultAggroRadius.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedAggroRadius)
				&& parsedAggroRadius > 0
				? parsedAggroRadius
				: DefaultAggroRadiusValue;
			EncounterGrowRules.Normalize(model);

			if (!TryParseObjectArray(json, OverridesPattern, ParseOverride, model.Overrides, "overrides", out error))
			{
				return false;
			}

			if (!TryParseObjectArray(json, TilesPattern, ParseTile, model.Tiles, "tiles", out error))
			{
				return false;
			}

			if (!TryParseObjectArray(json, TerrainsPattern, ParseTerrain, model.Terrains, "terrains", out error))
			{
				return false;
			}

			List<EncounterPropModel> props = new List<EncounterPropModel>();
			if (!TryParseObjectArray(json, PropsPattern, ParseProp, props, "props", out error))
			{
				return false;
			}

			if (!TryParseObjectArray(json, TriggersPattern, ParseTriggerModel, model.Triggers, "triggers", out error))
			{
				return false;
			}

			if (!TryParseObjectArray(json, TriggerCellsPattern, ParseTriggerCell, model.TriggerCells, "triggerCells", out error))
			{
				return false;
			}

			model.Camera = ParseCamera(json);

			List<EncounterCombatantModel> combatants = new List<EncounterCombatantModel>();
			if (!TryParseObjectArray(json, CombatantsPattern, ParseCombatant, combatants, "combatants", out error))
			{
				return false;
			}

			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < props.Count; i++)
			{
				EncounterPropModel prop = props[i];
				if (ValidateProp(prop, used) != null)
				{
					continue;
				}

				used.Add(prop.Id);
				model.Props.Add(prop);
			}

			for (int i = 0; i < combatants.Count; i++)
			{
				EncounterCombatantModel combatant = combatants[i];
				if (ValidateCombatant(combatant, used) != null)
				{
					continue;
				}

				used.Add(combatant.Id);
				model.Combatants.Add(combatant);
			}

			return true;
		}

		/// <summary>Writes schema v13 JSON (v1–v12 files still load; v11 does not migrate). Size is not stored.</summary>
		internal string ToJson()
		{
			EncounterGrowRules.Normalize(this);
			StringBuilder json = new StringBuilder();
			json.Append("{\n");
			json.Append("  \"schemaVersion\": ").Append(CurrentSchemaVersion).Append(",\n");
			json.Append("  \"template\": \"").Append(TextEscaping.JsonEscape(
				string.IsNullOrEmpty(Template) ? DefaultTemplate : Template)).Append("\",\n");
			json.Append("  \"walkableDefault\": ").Append(WalkableDefault ? "true" : "false").Append(",\n");
			json.Append("  \"tilesDefault\": ").Append(TilesDefault ? "true" : "false").Append(",\n");
			json.Append("  \"exploration\": ").Append(Exploration ? "true" : "false").Append(",\n");
			json.Append("  \"defaultAggroRadius\": ").Append(
				DefaultAggroRadius > 0 ? DefaultAggroRadius : DefaultAggroRadiusValue).Append(",\n");
			json.Append("  \"overrides\": [");
			if (Overrides != null && Overrides.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Overrides.Count; i++)
				{
					AppendOverride(json, Overrides[i]);
					if (i + 1 < Overrides.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			json.Append("  \"tiles\": [");
			if (Tiles != null && Tiles.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Tiles.Count; i++)
				{
					AppendTile(json, Tiles[i]);
					if (i + 1 < Tiles.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			json.Append("  \"terrains\": [");
			if (Terrains != null && Terrains.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Terrains.Count; i++)
				{
					AppendTerrain(json, Terrains[i]);
					if (i + 1 < Terrains.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			json.Append("  \"props\": [");
			if (Props != null && Props.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Props.Count; i++)
				{
					AppendProp(json, Props[i]);
					if (i + 1 < Props.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			json.Append("  \"triggers\": [");
			if (Triggers != null && Triggers.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Triggers.Count; i++)
				{
					AppendTriggerModel(json, Triggers[i]);
					if (i + 1 < Triggers.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			json.Append("  \"triggerCells\": [");
			if (TriggerCells != null && TriggerCells.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < TriggerCells.Count; i++)
				{
					AppendTriggerCell(json, TriggerCells[i]);
					if (i + 1 < TriggerCells.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("],\n");
			if (EncounterCameraRules.HasBounds(Camera))
			{
				EncounterCameraRules.Normalize(Camera);
				AppendCamera(json, Camera);
				json.Append(",\n");
			}

			json.Append("  \"combatants\": [");
			if (Combatants != null && Combatants.Count > 0)
			{
				json.Append('\n');
				for (int i = 0; i < Combatants.Count; i++)
				{
					AppendCombatant(json, Combatants[i]);
					if (i + 1 < Combatants.Count)
					{
						json.Append(',');
					}

					json.Append('\n');
				}

				json.Append("  ");
			}

			json.Append("]\n}\n");
			return json.ToString();
		}

		/// <summary>Reads <c>encounter.json</c> from a folder, or an empty model when missing or unreadable.</summary>
		internal static EncounterFileModel LoadOrEmpty(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return CreateEmpty();
			}

			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path))
			{
				return CreateEmpty();
			}

			try
			{
				EncounterFileModel parsed;
				string error;
				if (TryParse(File.ReadAllText(path), out parsed, out error))
				{
					return parsed;
				}
			}
			catch (IOException)
			{
			}

			return CreateEmpty();
		}

		/// <summary>Writes <c>encounter.json</c>. Returns false when the folder or write fails.</summary>
		internal bool TryWrite(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return false;
			}

			try
			{
				Directory.CreateDirectory(folder);
				File.WriteAllText(Path.Combine(folder, FileName), ToJson());
				return true;
			}
			catch (IOException)
			{
				return false;
			}
		}

		/// <summary>Ids already used in <see cref="Combatants"/> and <see cref="Props"/>.</summary>
		internal HashSet<string> UsedIds()
		{
			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			if (Combatants != null)
			{
				for (int i = 0; i < Combatants.Count; i++)
				{
					if (Combatants[i] != null && !string.IsNullOrEmpty(Combatants[i].Id))
					{
						used.Add(Combatants[i].Id);
					}
				}
			}

			if (Props != null)
			{
				for (int i = 0; i < Props.Count; i++)
				{
					if (Props[i] != null && !string.IsNullOrEmpty(Props[i].Id))
					{
						used.Add(Props[i].Id);
					}
				}
			}

			return used;
		}

		/// <summary>Mints <c>stem_1</c>, <c>stem_2</c>, … that is unused and a legal slug.</summary>
		internal static string MintCombatantId(string stem, ICollection<string> usedIds)
		{
			string legal = LegalizeStem(stem);
			int n = 1;
			string id;
			do
			{
				id = legal + "_" + n.ToString(CultureInfo.InvariantCulture);
				n++;
			}
			while (usedIds != null && usedIds.Contains(id));

			return id;
		}

		/// <summary>Living GoodSide count for Play validation and the Setup summary.</summary>
		internal int CountSide(string side)
		{
			int count = 0;
			if (Combatants == null)
			{
				return 0;
			}

			for (int i = 0; i < Combatants.Count; i++)
			{
				if (string.Equals(Combatants[i].Side, side, StringComparison.Ordinal))
				{
					count++;
				}
			}

			return count;
		}

		/// <summary>Reads an optional positive root size. Invalid or missing stays null.</summary>
		private static void TryParseOptionalSize(string json, Regex pattern, out int? value)
		{
			value = null;
			Match match = pattern.Match(json);
			if (!match.Success)
			{
				return;
			}

			int parsed;
			if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
				|| parsed < 1)
			{
				return;
			}

			value = parsed;
		}

		private static bool TryParseObjectArray<T>(
			string json,
			Regex pattern,
			Func<string, T> parse,
			List<T> dest,
			string name,
			out string error)
		{
			error = null;
			Match array = pattern.Match(json);
			if (!array.Success)
			{
				return true;
			}

			int start = json.IndexOf('[', array.Index);
			int end = FindMatchingBracket(json, start);
			if (start < 0 || end < 0)
			{
				error = "encounter.json " + name + " array is malformed.";
				return false;
			}

			foreach (string block in SplitTopObjects(json.Substring(start + 1, end - start - 1)))
			{
				T item = parse(block);
				if (item != null)
				{
					dest.Add(item);
				}
			}

			return true;
		}

		private static EncounterHexOverride ParseOverride(string block)
		{
			int? col = null;
			int? row = null;
			bool walkable = false;
			bool hasWalkable = false;
			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				int value;
				if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "col")
				{
					col = value;
				}
				else if (key == "row")
				{
					row = value;
				}
			}

			foreach (Match match in ObjectBoolPattern.Matches(block))
			{
				if (match.Groups[1].Value == "walkable")
				{
					walkable = match.Groups[2].Value == "true";
					hasWalkable = true;
				}
			}

			if (!col.HasValue || !row.HasValue || !hasWalkable)
			{
				return null;
			}

			return new EncounterHexOverride
			{
				Col = col.Value,
				Row = row.Value,
				Walkable = walkable
			};
		}

		private static void AppendOverride(StringBuilder json, EncounterHexOverride hex)
		{
			if (hex == null)
			{
				return;
			}

			json.Append("    { \"col\": ").Append(hex.Col)
				.Append(", \"row\": ").Append(hex.Row)
				.Append(", \"walkable\": ").Append(hex.Walkable ? "true" : "false")
				.Append(" }");
		}

		private static EncounterHexTrigger ParseTriggerCell(string block)
		{
			int? col = null;
			int? row = null;
			string triggerId = string.Empty;
			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				int value;
				if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "col")
				{
					col = value;
				}
				else if (key == "row")
				{
					row = value;
				}
			}

			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				if (match.Groups[1].Value == "triggerId")
				{
					triggerId = Unescape(match.Groups[2].Value);
				}
			}

			if (!col.HasValue || !row.HasValue || string.IsNullOrEmpty(triggerId))
			{
				return null;
			}

			return new EncounterHexTrigger
			{
				Col = col.Value,
				Row = row.Value,
				TriggerId = triggerId
			};
		}

		private static void AppendTriggerCell(StringBuilder json, EncounterHexTrigger trigger)
		{
			if (trigger == null)
			{
				return;
			}

			json.Append("    { \"col\": ").Append(trigger.Col)
				.Append(", \"row\": ").Append(trigger.Row)
				.Append(", \"triggerId\": \"").Append(TextEscaping.JsonEscape(trigger.TriggerId ?? string.Empty)).Append('"')
				.Append(" }");
		}

		private static EncounterTriggerModel ParseTriggerModel(string block)
		{
			string id = string.Empty;
			string pocketKey = string.Empty;
			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				string value = Unescape(match.Groups[2].Value);
				if (key == "id")
				{
					id = value;
				}
				else if (key == "pocketKey")
				{
					pocketKey = value;
				}
			}

			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			return new EncounterTriggerModel
			{
				Id = id,
				PocketKey = pocketKey ?? string.Empty
			};
		}

		private static void AppendTriggerModel(StringBuilder json, EncounterTriggerModel trigger)
		{
			if (trigger == null)
			{
				return;
			}

			json.Append("    { \"id\": \"").Append(TextEscaping.JsonEscape(trigger.Id ?? string.Empty)).Append('"');
			if (!string.IsNullOrEmpty(trigger.PocketKey))
			{
				json.Append(", \"pocketKey\": \"").Append(TextEscaping.JsonEscape(trigger.PocketKey)).Append('"');
			}

			json.Append(" }");
		}

		private static EncounterHexTile ParseTile(string block)
		{
			int? col = null;
			int? row = null;
			int? terrainId = null;
			string template = string.Empty;
			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				int value;
				if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "col")
				{
					col = value;
				}
				else if (key == "row")
				{
					row = value;
				}
				else if (key == "terrainId")
				{
					terrainId = value;
				}
			}

			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				if (match.Groups[1].Value == "template")
				{
					template = Unescape(match.Groups[2].Value);
				}
			}

			if (!col.HasValue || !row.HasValue || !terrainId.HasValue)
			{
				return null;
			}

			return new EncounterHexTile
			{
				Col = col.Value,
				Row = row.Value,
				TerrainId = terrainId.Value,
				Template = template ?? string.Empty
			};
		}

		private static void AppendTile(StringBuilder json, EncounterHexTile tile)
		{
			if (tile == null)
			{
				return;
			}

			json.Append("    { \"col\": ").Append(tile.Col)
				.Append(", \"row\": ").Append(tile.Row)
				.Append(", \"terrainId\": ").Append(tile.TerrainId);
			if (!string.IsNullOrEmpty(tile.Template))
			{
				json.Append(", \"template\": \"").Append(TextEscaping.JsonEscape(tile.Template)).Append('"');
			}

			json.Append(" }");
		}

		private static EncounterTerrainModel ParseTerrain(string block)
		{
			int? terrainId = null;
			string name = string.Empty;
			string source = TerrainSourceTemplate;
			string template = string.Empty;
			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				if (match.Groups[1].Value != "terrainId")
				{
					continue;
				}

				int value;
				if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					terrainId = value;
				}
			}

			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				string value = Unescape(match.Groups[2].Value);
				if (key == "name")
				{
					name = value;
				}
				else if (key == "source")
				{
					source = value;
				}
				else if (key == "template")
				{
					template = value;
				}
			}

			if (!terrainId.HasValue || !IsLegalTerrainSource(source))
			{
				return null;
			}

			return new EncounterTerrainModel
			{
				TerrainId = terrainId.Value,
				Name = name ?? string.Empty,
				Source = source,
				Template = template ?? string.Empty
			};
		}

		private static void AppendTerrain(StringBuilder json, EncounterTerrainModel terrain)
		{
			if (terrain == null)
			{
				return;
			}

			json.Append("    { \"terrainId\": ").Append(terrain.TerrainId)
				.Append(", \"name\": \"").Append(TextEscaping.JsonEscape(terrain.Name ?? string.Empty)).Append('"')
				.Append(", \"source\": \"").Append(TextEscaping.JsonEscape(
					string.IsNullOrEmpty(terrain.Source) ? TerrainSourceTemplate : terrain.Source)).Append('"')
				.Append(", \"template\": \"").Append(TextEscaping.JsonEscape(terrain.Template ?? string.Empty)).Append('"')
				.Append(" }");
		}

		private static EncounterPropModel ParseProp(string block)
		{
			EncounterPropModel prop = new EncounterPropModel();
			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				string value = Unescape(match.Groups[2].Value);
				if (key == "id")
				{
					prop.Id = value;
				}
				else if (key == "prefabName")
				{
					prop.PrefabName = value;
				}
			}

			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				int value;
				if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "col")
				{
					prop.Col = value;
				}
				else if (key == "row")
				{
					prop.Row = value;
				}
			}

			foreach (Match match in ObjectFloatPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				float value;
				if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "x")
				{
					prop.X = value;
				}
				else if (key == "y")
				{
					prop.Y = value;
				}
			}

			foreach (Match match in ObjectBoolPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				bool value = match.Groups[2].Value == "true";
				if (key == "flipped")
				{
					prop.Flipped = value;
				}
				else if (key == "snap")
				{
					prop.Snap = value;
				}
			}

			return prop;
		}

		private static void AppendProp(StringBuilder json, EncounterPropModel prop)
		{
			if (prop == null)
			{
				return;
			}

			json.Append("    { \"id\": \"").Append(TextEscaping.JsonEscape(prop.Id ?? string.Empty)).Append('"')
				.Append(", \"prefabName\": \"").Append(TextEscaping.JsonEscape(prop.PrefabName ?? string.Empty)).Append('"')
				.Append(", \"snap\": ").Append(prop.Snap ? "true" : "false");
			if (prop.Col.HasValue)
			{
				json.Append(", \"col\": ").Append(prop.Col.Value);
			}

			if (prop.Row.HasValue)
			{
				json.Append(", \"row\": ").Append(prop.Row.Value);
			}

			if (prop.X.HasValue)
			{
				json.Append(", \"x\": ").Append(FormatFloat(prop.X.Value));
			}

			if (prop.Y.HasValue)
			{
				json.Append(", \"y\": ").Append(FormatFloat(prop.Y.Value));
			}

			json.Append(", \"flipped\": ").Append(prop.Flipped ? "true" : "false")
				.Append(" }");
		}

		private static EncounterCombatantModel ParseCombatant(string block)
		{
			EncounterCombatantModel combatant = new EncounterCombatantModel();
			foreach (Match match in ObjectStringPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				string value = Unescape(match.Groups[2].Value);
				if (key == "id")
				{
					combatant.Id = value;
				}
				else if (key == "side")
				{
					combatant.Side = value;
				}
				else if (key == "source")
				{
					combatant.Source = value;
				}
				else if (key == "projectId")
				{
					combatant.ProjectId = value;
				}
				else if (key == "unitId")
				{
					combatant.UnitId = value;
				}
				else if (key == "pocket")
				{
					combatant.Pocket = value;
				}
				else if (key == "triggerId")
				{
					combatant.TriggerId = value;
				}
			}

			foreach (Match match in ObjectIntPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				int value;
				if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "level")
				{
					combatant.Level = value;
				}
				else if (key == "col")
				{
					combatant.Col = value;
				}
				else if (key == "row")
				{
					combatant.Row = value;
				}
				else if (key == "aggroRadius")
				{
					combatant.AggroRadius = value;
				}
			}

			foreach (Match match in ObjectBoolPattern.Matches(block))
			{
				if (match.Groups[1].Value == "flipped")
				{
					combatant.Flipped = match.Groups[2].Value == "true";
				}
			}

			return combatant;
		}

		private static void AppendCombatant(StringBuilder json, EncounterCombatantModel combatant)
		{
			if (combatant == null)
			{
				return;
			}

			json.Append("    {\n");
			json.Append("      \"id\": \"").Append(TextEscaping.JsonEscape(combatant.Id ?? string.Empty)).Append("\",\n");
			json.Append("      \"side\": \"").Append(TextEscaping.JsonEscape(combatant.Side ?? string.Empty)).Append("\",\n");
			json.Append("      \"source\": \"").Append(TextEscaping.JsonEscape(combatant.Source ?? string.Empty)).Append("\",\n");
			if (string.Equals(combatant.Source, SourceCharacter, StringComparison.Ordinal))
			{
				json.Append("      \"projectId\": \"").Append(TextEscaping.JsonEscape(combatant.ProjectId ?? string.Empty)).Append("\",\n");
			}
			else if (string.Equals(combatant.Source, SourceUnit, StringComparison.Ordinal))
			{
				json.Append("      \"unitId\": \"").Append(TextEscaping.JsonEscape(combatant.UnitId ?? string.Empty)).Append("\",\n");
			}

			json.Append("      \"level\": ").Append(combatant.Level < 1 ? 1 : combatant.Level);
			if (combatant.Col.HasValue)
			{
				json.Append(",\n      \"col\": ").Append(combatant.Col.Value);
			}

			if (combatant.Row.HasValue)
			{
				json.Append(",\n      \"row\": ").Append(combatant.Row.Value);
			}

			json.Append(",\n      \"flipped\": ").Append(combatant.Flipped ? "true" : "false");
			bool isBadSide = string.Equals(combatant.Side, BadSide, StringComparison.Ordinal);
			if (isBadSide && !string.IsNullOrEmpty(combatant.Pocket))
			{
				json.Append(",\n      \"pocket\": \"").Append(TextEscaping.JsonEscape(combatant.Pocket)).Append('"');
			}

			if (isBadSide && combatant.AggroRadius.HasValue)
			{
				json.Append(",\n      \"aggroRadius\": ").Append(combatant.AggroRadius.Value);
			}

			if (isBadSide && !string.IsNullOrEmpty(combatant.TriggerId))
			{
				json.Append(",\n      \"triggerId\": \"").Append(TextEscaping.JsonEscape(combatant.TriggerId)).Append('"');
			}

			json.Append("\n    }");
		}

		private static EncounterCameraModel ParseCamera(string json)
		{
			Match start = CameraPattern.Match(json);
			if (!start.Success)
			{
				return null;
			}

			int open = json.IndexOf('{', start.Index);
			int close = FindMatchingBracket(json, open);
			if (open < 0 || close < 0)
			{
				return null;
			}

			string block = json.Substring(open, close - open + 1);
			EncounterCameraModel camera = new EncounterCameraModel();
			bool hasMinX = false;
			bool hasMinY = false;
			bool hasMaxX = false;
			bool hasMaxY = false;
			foreach (Match match in ObjectFloatPattern.Matches(block))
			{
				string key = match.Groups[1].Value;
				float value;
				if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
				{
					continue;
				}

				if (key == "minX")
				{
					camera.MinX = value;
					hasMinX = true;
				}
				else if (key == "minY")
				{
					camera.MinY = value;
					hasMinY = true;
				}
				else if (key == "maxX")
				{
					camera.MaxX = value;
					hasMaxX = true;
				}
				else if (key == "maxY")
				{
					camera.MaxY = value;
					hasMaxY = true;
				}
				else if (key == "orthoSize")
				{
					camera.OrthoSize = value;
				}
			}

			foreach (Match match in ObjectBoolPattern.Matches(block))
			{
				if (match.Groups[1].Value == "lockZoom")
				{
					camera.LockZoom = match.Groups[2].Value == "true";
				}
			}

			if (!hasMinX || !hasMinY || !hasMaxX || !hasMaxY)
			{
				return null;
			}

			EncounterCameraRules.Normalize(camera);
			return EncounterCameraRules.HasBounds(camera) ? camera : null;
		}

		private static void AppendCamera(StringBuilder json, EncounterCameraModel camera)
		{
			json.Append("  \"camera\": {\n");
			json.Append("    \"minX\": ").Append(FormatFloat(camera.MinX)).Append(",\n");
			json.Append("    \"minY\": ").Append(FormatFloat(camera.MinY)).Append(",\n");
			json.Append("    \"maxX\": ").Append(FormatFloat(camera.MaxX)).Append(",\n");
			json.Append("    \"maxY\": ").Append(FormatFloat(camera.MaxY)).Append(",\n");
			json.Append("    \"lockZoom\": ").Append(camera.LockZoom ? "true" : "false");
			if (camera.OrthoSize.HasValue)
			{
				json.Append(",\n    \"orthoSize\": ").Append(FormatFloat(camera.OrthoSize.Value));
			}

			json.Append("\n  }");
		}

		private static string FormatFloat(float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		private static int FindMatchingBracket(string text, int openIndex)
		{
			if (openIndex < 0 || openIndex >= text.Length)
			{
				return -1;
			}

			char open = text[openIndex];
			char close;
			if (open == '[')
			{
				close = ']';
			}
			else if (open == '{')
			{
				close = '}';
			}
			else
			{
				return -1;
			}

			int depth = 0;
			bool inString = false;
			bool escape = false;
			for (int i = openIndex; i < text.Length; i++)
			{
				char c = text[i];
				if (inString)
				{
					if (escape)
					{
						escape = false;
					}
					else if (c == '\\')
					{
						escape = true;
					}
					else if (c == '"')
					{
						inString = false;
					}

					continue;
				}

				if (c == '"')
				{
					inString = true;
				}
				else if (c == open)
				{
					depth++;
				}
				else if (c == close)
				{
					depth--;
					if (depth == 0)
					{
						return i;
					}
				}
			}

			return -1;
		}

		private static List<string> SplitTopObjects(string arrayBody)
		{
			List<string> blocks = new List<string>();
			int depth = 0;
			int start = -1;
			bool inString = false;
			bool escape = false;
			for (int i = 0; i < arrayBody.Length; i++)
			{
				char c = arrayBody[i];
				if (inString)
				{
					if (escape)
					{
						escape = false;
					}
					else if (c == '\\')
					{
						escape = true;
					}
					else if (c == '"')
					{
						inString = false;
					}

					continue;
				}

				if (c == '"')
				{
					inString = true;
				}
				else if (c == '{')
				{
					if (depth == 0)
					{
						start = i;
					}

					depth++;
				}
				else if (c == '}')
				{
					depth--;
					if (depth == 0 && start >= 0)
					{
						blocks.Add(arrayBody.Substring(start, i - start + 1));
						start = -1;
					}
				}
			}

			return blocks;
		}

		private static string LegalizeStem(string stem)
		{
			if (string.IsNullOrEmpty(stem))
			{
				return "combatant";
			}

			StringBuilder text = new StringBuilder();
			foreach (char raw in stem.Trim().ToLowerInvariant())
			{
				if ((raw >= 'a' && raw <= 'z') || (raw >= '0' && raw <= '9') || raw == '_')
				{
					text.Append(raw);
				}
				else if (text.Length > 0 && text[text.Length - 1] != '_')
				{
					text.Append('_');
				}
			}

			while (text.Length > 0 && text[0] == '_')
			{
				text.Remove(0, 1);
			}

			while (text.Length > 0 && text[text.Length - 1] == '_')
			{
				text.Length--;
			}

			if (text.Length == 0)
			{
				return "combatant";
			}

			if (text[0] < 'a' || text[0] > 'z')
			{
				text.Insert(0, 'c');
			}

			return text.ToString();
		}

		private static string Unescape(string value)
		{
			if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
			{
				return value ?? string.Empty;
			}

			StringBuilder text = new StringBuilder(value.Length);
			bool escape = false;
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (!escape)
				{
					if (c == '\\')
					{
						escape = true;
					}
					else
					{
						text.Append(c);
					}

					continue;
				}

				escape = false;
				if (c == 'n')
				{
					text.Append('\n');
				}
				else if (c == 'r')
				{
					text.Append('\r');
				}
				else if (c == 't')
				{
					text.Append('\t');
				}
				else
				{
					text.Append(c);
				}
			}

			return text.ToString();
		}
	}
}
