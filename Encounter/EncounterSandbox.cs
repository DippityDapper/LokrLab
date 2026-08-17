namespace LokrLab.Encounter
{
	/// <summary>Armed Encounter roster for the next Sandbox embed. Cleared on Stop.</summary>
	/// <remarks>
	/// One live-fight arm for Character / Ability / Encounter Sandbox when the hole is
	/// playing an authored Encounter. A fill (character + level) occupies the first
	/// hero spawn point; null leaves spawn points empty. Setup stays
	/// <see cref="EncounterEdit"/>. Do not grow <c>EmbeddedFightRequest</c>.
	/// </remarks>
	internal static class EncounterSandbox
	{
		/// <summary>True while this live Encounter fight owns the next or current embed spawn.</summary>
		internal static bool IsArmed { get; private set; }

		/// <summary>Arena prefab name used when the host enqueues the ephemeral quest.</summary>
		internal static string Template { get; private set; }

		/// <summary>In-memory payload spawned before <c>StartFight</c>.</summary>
		internal static EncounterFileModel File { get; private set; }

		/// <summary>True while this session started with parked BadSide pockets still to aggro.</summary>
		internal static bool ExplorationActive { get; private set; }

		/// <summary>Unit id to spawn at the first hero spawn point, or null to leave spawn points empty.</summary>
		internal static string FillUnitId { get; private set; }

		/// <summary>Sandbox Level dropdown: fill rank, and override for the first GoodSide hero.</summary>
		internal static int FillLevel { get; private set; }

		/// <summary>True when the vanilla debug panel should show (always on for Sandbox).</summary>
		internal static bool ShowDebugPanel { get; private set; }

		/// <summary>Marks the next embed as this Encounter with no spawn fill.</summary>
		internal static void Arm(EncounterFileModel file)
		{
			Arm(file, null, 1, true);
		}

		/// <summary>Marks the next embed as a live Encounter fight. Disarms Setup.</summary>
		/// <param name="file">Authored payload to spawn.</param>
		/// <param name="fillUnitId">Character or vanilla unit id for the first hero spawn point; null leaves every spawn empty.</param>
		/// <param name="fillLevel">Rank to spawn the fill at.</param>
		/// <param name="showDebugPanel">Sandbox always passes true.</param>
		internal static void Arm(EncounterFileModel file, string fillUnitId, int fillLevel, bool showDebugPanel)
		{
			EncounterEdit.Disarm();
			File = file;
			Template = file != null && !string.IsNullOrEmpty(file.Template)
				? file.Template
				: EncounterFileModel.DefaultTemplate;
			FillUnitId = fillUnitId;
			FillLevel = fillLevel < 1 ? 1 : fillLevel;
			ShowDebugPanel = showDebugPanel;
			ExplorationActive = file != null && file.Exploration;
			IsArmed = true;
		}

		/// <summary>Clears the live Encounter so a 1v1 Sandbox stays unaffected.</summary>
		internal static void Disarm()
		{
			IsArmed = false;
			File = null;
			Template = null;
			FillUnitId = null;
			FillLevel = 1;
			ShowDebugPanel = false;
			ExplorationActive = false;
			EncounterExploration.Reset();
		}
	}
}
