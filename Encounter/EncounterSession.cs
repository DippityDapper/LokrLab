using System;
using System.IO;
using LokrLab.Shell;
using LokrLabApi;

namespace LokrLab.Encounter
{
	/// <summary>One Encounter project session — folder plus the in-memory <c>encounter.json</c> model.</summary>
	public sealed class EncounterSession : ProjectSession
	{
		/// <summary>In-memory payload. Edits land here until File → Save.</summary>
		internal EncounterFileModel File { get; set; }

		/// <summary>Builds a session for the given encounter folder, creating files when missing.</summary>
		internal static EncounterSession Create(string folder)
		{
			if (string.IsNullOrEmpty(folder))
			{
				return null;
			}

			Directory.CreateDirectory(folder);
			string id = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string display = ProjectMarker.TryReadDisplayName(folder) ?? id;
			EncounterFileModel file = EncounterFileModel.LoadOrEmpty(folder);
			if (!System.IO.File.Exists(EncounterLabPaths.EncounterFile(folder)))
			{
				file.TryWrite(folder);
			}

			return new EncounterSession
			{
				ProjectTypeId = LokrLabApi.LokrLabApi.EncounterTypeId,
				Id = id,
				FolderPath = folder,
				DisplayName = display,
				IsDirty = false,
				File = file
			};
		}

		/// <summary>Writes <c>project.json</c> and <c>encounter.json</c>. Returns false when the payload write fails.</summary>
		internal bool TrySave()
		{
			if (string.IsNullOrEmpty(FolderPath) || File == null)
			{
				return false;
			}

			ProjectMarker.Write(FolderPath, LokrLabApi.LokrLabApi.EncounterTypeId, DisplayName);
			if (!File.TryWrite(FolderPath))
			{
				return false;
			}

			IsDirty = false;
			return true;
		}

		/// <summary>Appends a Character-sourced combatant. Returns null when the row is illegal.</summary>
		internal EncounterCombatantModel TryAddCharacter(string projectId, string side)
		{
			return TryAdd(new EncounterCombatantModel
			{
				Id = EncounterFileModel.MintCombatantId(projectId, File != null ? File.UsedIds() : null),
				Side = string.IsNullOrEmpty(side) ? EncounterFileModel.GoodSide : side,
				Source = EncounterFileModel.SourceCharacter,
				ProjectId = projectId ?? string.Empty,
				Level = 1
			});
		}

		/// <summary>Appends a unit-sourced combatant. Returns null when the row is illegal.</summary>
		internal EncounterCombatantModel TryAddUnit(string unitId, string side)
		{
			return TryAdd(new EncounterCombatantModel
			{
				Id = EncounterFileModel.MintCombatantId(unitId, File != null ? File.UsedIds() : null),
				Side = string.IsNullOrEmpty(side) ? EncounterFileModel.BadSide : side,
				Source = EncounterFileModel.SourceUnit,
				UnitId = unitId ?? string.Empty,
				Level = 1
			});
		}

		/// <summary>Appends a hero spawn point (GoodSide, no fixed hero — filled by whoever loads the encounter). Returns null when the row is illegal.</summary>
		internal EncounterCombatantModel TryAddSpawnPoint()
		{
			return TryAdd(new EncounterCombatantModel
			{
				Id = EncounterFileModel.MintCombatantId("hero_spawn", File != null ? File.UsedIds() : null),
				Side = EncounterFileModel.GoodSide,
				Source = EncounterFileModel.SourceSpawn,
				Level = 1
			});
		}

		/// <summary>Removes the combatant with this id. Returns false when it was not listed.</summary>
		internal bool TryRemove(string combatantId)
		{
			if (File == null || File.Combatants == null || string.IsNullOrEmpty(combatantId))
			{
				return false;
			}

			for (int i = 0; i < File.Combatants.Count; i++)
			{
				if (File.Combatants[i] != null
					&& string.Equals(File.Combatants[i].Id, combatantId, StringComparison.Ordinal))
				{
					File.Combatants.RemoveAt(i);
					return true;
				}
			}

			return false;
		}

		private EncounterCombatantModel TryAdd(EncounterCombatantModel combatant)
		{
			if (File == null || File.Combatants == null)
			{
				return null;
			}

			if (EncounterFileModel.ValidateCombatant(combatant, File.UsedIds()) != null)
			{
				return null;
			}

			File.Combatants.Add(combatant);
			return combatant;
		}
	}
}
