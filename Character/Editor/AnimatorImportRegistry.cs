using System.Collections.Generic;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Populates a rig folder's part PNGs before Load reads them. An importer's only job is to make targetFolder end up containing one named PNG per part; nothing downstream needs to know which importer produced them.</summary>
	internal delegate bool PartSourceImportFn(string targetFolder, IReadOnlyDictionary<string, string> parameters, out string message);

	/// <summary>Registry of PartSourceImportFn sources, keyed by name.</summary>
	/// <remarks>Registered rather than hardcoded so a plugin can add a third source without RigEditorScene needing a new special-cased branch per source. Same shape as CharacterAPI's ResolverChain&lt;T&gt; in LokrCharacterLoader: priority-ordered, and a re-registered name replaces its previous entry.</remarks>
	internal static class AnimatorImportRegistry
	{
		private sealed class Entry
		{
			/// <summary>The importer's registered name, used as the lookup key by TryImport and re-registration.</summary>
			internal string Name;
			/// <summary>Sort priority; entries run in descending order of this value.</summary>
			internal int Priority;
			/// <summary>The import function itself.</summary>
			internal PartSourceImportFn Run;
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>Registers an importer, replacing any existing importer of the same name, then re-sorts by descending priority.</summary>
		internal static void RegisterImporter(string name, int priority, PartSourceImportFn run)
		{
			entries.RemoveAll(e => e.Name == name);
			entries.Add(new Entry { Name = name, Priority = priority, Run = run });
			entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
		}

		/// <summary>Runs the named importer, or fails with a message if none is registered under that name.</summary>
		internal static bool TryImport(string name, string targetFolder, IReadOnlyDictionary<string, string> parameters, out string message)
		{
			foreach (Entry entry in entries)
			{
				if (entry.Name == name)
				{
					return entry.Run(targetFolder, parameters, out message);
				}
			}
			message = "No importer registered named '" + name + "'.";
			return false;
		}

		/// <summary>Registers the two first-party sources: "the folder already has PNGs in it" (as a real registered entry, not an unregistered default) and grid atlas slicing.</summary>
		internal static void RegisterDefaults()
		{
			RegisterImporter("Loose PNGs", 0, LoosePngImporter.Run);
			RegisterImporter("Grid Atlas", 0, GridAtlasImporter.Run);
		}
	}
}
