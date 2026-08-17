using System.Collections.Generic;
using System.IO;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Confirms the target folder already contains one PNG per part, placed there by hand (or any external tool) -- nothing to convert. Registered mainly for symmetry with GridAtlasImporter, so "how did these PNGs get here" always has an explicit, listed answer.</summary>
	internal static class LoosePngImporter
	{
		/// <summary>Checks that targetFolder already has PNGs in it.</summary>
		internal static bool Run(string targetFolder, IReadOnlyDictionary<string, string> parameters, out string message)
		{
			if (!Directory.Exists(targetFolder))
			{
				message = "Folder does not exist: " + targetFolder;
				return false;
			}
			int count = Directory.GetFiles(targetFolder, "*.png").Length;
			message = count > 0
				? string.Format("{0} already contains {1} PNG(s) — nothing to import.", targetFolder, count)
				: string.Format("{0} has no PNGs yet — add part PNGs to it directly, or use the Grid Atlas importer below.", targetFolder);
			return count > 0;
		}
	}
}
