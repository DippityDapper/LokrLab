using System.IO;
using UnityEngine;

namespace LokrLab.Encounter
{
	/// <summary>On-disk roots for Encounter Lab projects under <c>Mods/LokrLab/LokrEncounterLab/</c>.</summary>
	internal static class EncounterLabPaths
	{
		/// <summary>Payload filename inside an encounter folder.</summary>
		internal const string FileName = EncounterFileModel.FileName;

		/// <summary>ModAPI category name and on-disk folder for encounter projects.</summary>
		internal const string Category = "LokrEncounterLab";

		/// <summary>Suite mod package root (<c>Mods/LokrLab</c>).</summary>
		internal static string SuiteRoot => Path.Combine(Application.dataPath, "Mods", "LokrLab");

		/// <summary>Folder holding every authored encounter project.</summary>
		internal static string Root => Path.Combine(SuiteRoot, Category);

		/// <summary>One encounter's folder, <see cref="Root"/>/<paramref name="encounterId"/>/.</summary>
		internal static string EncounterFolder(string encounterId) => Path.Combine(Root, encounterId);

		/// <summary>The <c>encounter.json</c> path inside an encounter folder.</summary>
		internal static string EncounterFile(string folder) => Path.Combine(folder, FileName);

		/// <summary>Creates <see cref="Root"/> if missing.</summary>
		internal static void EnsureFoldersExist()
		{
			Directory.CreateDirectory(Root);
		}

		/// <summary>Mints a <c>slug_token</c> encounter folder id and retries while that folder already exists.</summary>
		internal static string GenerateNewId(string slug = null)
		{
			string stem = string.IsNullOrEmpty(slug) ? "encounter" : slug;
			return LokrLab.LabSlugIds.MintUniqueId(stem, id => Directory.Exists(EncounterFolder(id)));
		}
	}
}
