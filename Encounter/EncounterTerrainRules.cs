using System;
using System.Collections.Generic;

namespace LokrLab.Encounter
{
	/// <summary>Unity-free terrain catalog upserts for the Encounter Node Tree.</summary>
	/// <remarks>
	/// Host rows are merged by scan without a dirty flag. Import and custom
	/// rows dirty immediately. Same <c>terrainId</c> from two stages is legal —
	/// the tile stamp stores the source template so imported art survives apply.
	/// </remarks>
	internal static class EncounterTerrainRules
	{
		/// <summary>First unused id at or above 1024 so custom stubs stay off vanilla bit flags.</summary>
		internal const int CustomIdStart = 1024;

		/// <summary>Catalog row for this id and source template, or the first id match.</summary>
		internal static EncounterTerrainModel Find(EncounterFileModel file, int terrainId, string template)
		{
			if (file == null || file.Terrains == null)
			{
				return null;
			}

			EncounterTerrainModel fallback = null;
			string key = template ?? string.Empty;
			for (int i = 0; i < file.Terrains.Count; i++)
			{
				EncounterTerrainModel terrain = file.Terrains[i];
				if (terrain == null || terrain.TerrainId != terrainId)
				{
					continue;
				}

				if (string.Equals(terrain.Template ?? string.Empty, key, StringComparison.Ordinal))
				{
					return terrain;
				}

				if (fallback == null)
				{
					fallback = terrain;
				}
			}

			return fallback;
		}

		/// <summary>True when this id and source template are already listed.</summary>
		internal static bool Contains(EncounterFileModel file, int terrainId, string template)
		{
			if (file == null || file.Terrains == null)
			{
				return false;
			}

			string key = template ?? string.Empty;
			for (int i = 0; i < file.Terrains.Count; i++)
			{
				EncounterTerrainModel terrain = file.Terrains[i];
				if (terrain != null && terrain.TerrainId == terrainId
					&& string.Equals(terrain.Template ?? string.Empty, key, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Adds the row when that id and template are not listed. False when skipped.</summary>
		internal static bool Add(EncounterFileModel file, EncounterTerrainModel terrain)
		{
			if (file == null || terrain == null)
			{
				return false;
			}

			if (file.Terrains == null)
			{
				file.Terrains = new List<EncounterTerrainModel>();
			}

			if (Contains(file, terrain.TerrainId, terrain.Template))
			{
				return false;
			}

			file.Terrains.Add(terrain);
			return true;
		}

		/// <summary>Drops an import or custom row. Host template rows stay — the next scan puts them back.</summary>
		internal static bool Remove(EncounterFileModel file, int terrainId, string template)
		{
			EncounterTerrainModel existing = FindExact(file, terrainId, template);
			if (existing == null || file.Terrains == null)
			{
				return false;
			}

			if (string.Equals(existing.Source, EncounterFileModel.TerrainSourceTemplate, StringComparison.Ordinal))
			{
				return false;
			}

			return file.Terrains.Remove(existing);
		}

		/// <summary>Drops host-template rows that no longer match <paramref name="hostTemplate"/>.</summary>
		internal static void DropStaleHost(EncounterFileModel file, string hostTemplate)
		{
			if (file == null || file.Terrains == null)
			{
				return;
			}

			string host = hostTemplate ?? string.Empty;
			for (int i = file.Terrains.Count - 1; i >= 0; i--)
			{
				EncounterTerrainModel terrain = file.Terrains[i];
				if (terrain == null
					|| !string.Equals(terrain.Source, EncounterFileModel.TerrainSourceTemplate, StringComparison.Ordinal))
				{
					continue;
				}

				if (!string.Equals(terrain.Template ?? string.Empty, host, StringComparison.Ordinal))
				{
					file.Terrains.RemoveAt(i);
				}
			}
		}

		/// <summary>Mints a custom row named <c>custom_N</c>.</summary>
		internal static EncounterTerrainModel AddCustom(EncounterFileModel file)
		{
			if (file == null)
			{
				return null;
			}

			if (file.Terrains == null)
			{
				file.Terrains = new List<EncounterTerrainModel>();
			}

			EncounterTerrainModel terrain = new EncounterTerrainModel
			{
				TerrainId = MintTerrainId(file),
				Name = MintCustomName(file),
				Source = EncounterFileModel.TerrainSourceCustom,
				Template = string.Empty
			};
			file.Terrains.Add(terrain);
			return terrain;
		}

		/// <summary>Next unused integer at or above <see cref="CustomIdStart"/>.</summary>
		internal static int MintTerrainId(EncounterFileModel file)
		{
			HashSet<int> used = new HashSet<int>();
			if (file != null && file.Terrains != null)
			{
				for (int i = 0; i < file.Terrains.Count; i++)
				{
					if (file.Terrains[i] != null)
					{
						used.Add(file.Terrains[i].TerrainId);
					}
				}
			}

			int id = CustomIdStart;
			while (used.Contains(id))
			{
				id++;
			}

			return id;
		}

		/// <summary>Display label for a source value.</summary>
		internal static string SourceLabel(string source)
		{
			if (string.Equals(source, EncounterFileModel.TerrainSourceImport, StringComparison.Ordinal))
			{
				return "import";
			}

			if (string.Equals(source, EncounterFileModel.TerrainSourceCustom, StringComparison.Ordinal))
			{
				return "custom";
			}

			return "template";
		}

		private static EncounterTerrainModel FindExact(EncounterFileModel file, int terrainId, string template)
		{
			if (file == null || file.Terrains == null)
			{
				return null;
			}

			string key = template ?? string.Empty;
			for (int i = 0; i < file.Terrains.Count; i++)
			{
				EncounterTerrainModel terrain = file.Terrains[i];
				if (terrain != null && terrain.TerrainId == terrainId
					&& string.Equals(terrain.Template ?? string.Empty, key, StringComparison.Ordinal))
				{
					return terrain;
				}
			}

			return null;
		}

		private static string MintCustomName(EncounterFileModel file)
		{
			int n = 1;
			while (NameTaken(file, "custom_" + n))
			{
				n++;
			}

			return "custom_" + n;
		}

		private static bool NameTaken(EncounterFileModel file, string name)
		{
			if (file == null || file.Terrains == null)
			{
				return false;
			}

			for (int i = 0; i < file.Terrains.Count; i++)
			{
				EncounterTerrainModel terrain = file.Terrains[i];
				if (terrain != null && string.Equals(terrain.Name, name, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}
	}
}
