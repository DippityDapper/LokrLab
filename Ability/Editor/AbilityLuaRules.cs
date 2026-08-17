namespace LokrAbilityLab.Editor
{
	/// <summary>Unity-free Lua-card helpers (default stub, KV flatten, quote check) for the editor and xUnit.</summary>
	/// <remarks>
	/// Vanilla Lua Action is a single quoted KV string. PenguinParser does not escape quotes, so a
	/// double quote inside the body cannot round-trip. Newlines are flattened on save to match the
	/// five vanilla files.
	/// </remarks>
	internal static class AbilityLuaRules
	{
		/// <summary>Seeded Action body for a newly added Lua card.</summary>
		internal const string DefaultAction = "return function(ctx) end";

		/// <summary>True when <paramref name="source"/> contains a double quote that KV1 cannot round-trip.</summary>
		internal static bool ContainsDoubleQuote(string source)
		{
			return !string.IsNullOrEmpty(source) && source.IndexOf('"') >= 0;
		}

		/// <summary>Collapses newlines so the Action field can be written as one quoted KV string.</summary>
		internal static string FlattenForKv(string source)
		{
			if (string.IsNullOrEmpty(source))
			{
				return source;
			}

			return source.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
		}
	}
}
