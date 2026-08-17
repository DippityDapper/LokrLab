using System.Collections.Generic;
using LokrLab;

namespace LokrLab.Editor.General
{
	/// <summary>The base game's LocalizationManager.LanguageCode values, minus EN, as their own localization_&lt;suffix&gt;.txt filename suffixes.</summary>
	/// <remarks>Mirrors LocalizationManager.fileNamesMapping (Ironhide.Localization, decompiled source) -- duplicated here rather than reflected into, since it's a small, stable, publicly-documented file-naming convention, the same pragmatic choice CharacterLabContentLoader's own ReadTier makes for a single-field read. EN itself isn't included: it's CharacterProfile.Name/Description directly, not a CharacterProfile.Localizations entry.</remarks>
	internal static class LocaleCodes
	{
		internal static readonly IReadOnlyList<string> AllNonEnglish = new List<string>
		{
			"es", "de", "ru", "fr", "it", "tr", "zh-Hans", "zh-Hant", "ar", "pt", "ja", "nl", "ko", "en-gb", "fr-ca",
		};
	}
}
