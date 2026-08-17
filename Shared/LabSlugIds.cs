using System;
using System.Text;

namespace LokrLab
{
	/// <summary>Legalizes display-name slugs and mints the 6-character token on a <c>slug_token</c> id.</summary>
	/// <remarks>
	/// Token alphabet is Crockford (no i, l, o, u) so the id stays a legal ability <c>#word</c>.
	/// Existing 18-digit folders stay valid; this only mints new ids. See
	/// docs/roadmaps/completed/human-readable-ids.md.
	/// </remarks>
	internal static class LabSlugIds
	{
		/// <summary>Placeholder shown on the create sheet before confirm mints the token.</summary>
		internal const string TokenPreview = "??????";

		/// <summary>Crockford characters, lowercase, excluding i, l, o, and u.</summary>
		private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

		/// <summary>Settled token length.</summary>
		internal const int TokenLength = 6;

		private static readonly Random Random = new Random();

		/// <summary>Lowercases text and keeps a legal stem: starts with a letter, then <c>[a-z0-9_]</c>.</summary>
		internal static string LegalizeSlug(string text, string emptyFallback)
		{
			if (string.IsNullOrEmpty(text))
			{
				return emptyFallback;
			}

			StringBuilder stem = new StringBuilder();
			foreach (char raw in text.Trim().ToLowerInvariant())
			{
				if ((raw >= 'a' && raw <= 'z') || (raw >= '0' && raw <= '9') || raw == '_')
				{
					stem.Append(raw);
				}
				else if (stem.Length > 0 && stem[stem.Length - 1] != '_')
				{
					stem.Append('_');
				}
			}

			while (stem.Length > 0 && stem[0] == '_')
			{
				stem.Remove(0, 1);
			}

			while (stem.Length > 0 && stem[stem.Length - 1] == '_')
			{
				stem.Length--;
			}

			if (stem.Length == 0)
			{
				return emptyFallback;
			}

			if (stem[0] < 'a' || stem[0] > 'z')
			{
				stem.Insert(0, 'c');
			}

			return stem.ToString();
		}

		/// <summary>True when slug is a non-empty legal stem.</summary>
		internal static bool IsLegalSlug(string slug)
		{
			if (string.IsNullOrEmpty(slug) || slug[0] < 'a' || slug[0] > 'z')
			{
				return false;
			}

			for (int i = 0; i < slug.Length; i++)
			{
				char c = slug[i];
				if ((c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '_')
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>Six Crockford characters.</summary>
		internal static string MintToken()
		{
			char[] token = new char[TokenLength];
			lock (Random)
			{
				for (int i = 0; i < TokenLength; i++)
				{
					token[i] = Alphabet[Random.Next(Alphabet.Length)];
				}
			}

			return new string(token);
		}

		/// <summary><c>slug_token</c>, retrying while <paramref name="exists"/> is true.</summary>
		internal static string MintUniqueId(string slug, Func<string, bool> exists)
		{
			string legal = IsLegalSlug(slug) ? slug : LegalizeSlug(slug, "unit");
			string candidate;
			do
			{
				candidate = legal + "_" + MintToken();
			}
			while (exists != null && exists(candidate));
			return candidate;
		}

		/// <summary>Read-only create-sheet preview (<c>slug_??????</c>).</summary>
		internal static string PreviewId(string slug)
		{
			string legal = IsLegalSlug(slug) ? slug : LegalizeSlug(slug, "unit");
			return legal + "_" + TokenPreview;
		}

		/// <summary>True for an 18-20 digit Lab folder id.</summary>
		internal static bool LooksLikeNumericId(string id)
		{
			if (string.IsNullOrEmpty(id) || id.Length < 18 || id.Length > 20)
			{
				return false;
			}

			for (int i = 0; i < id.Length; i++)
			{
				if (!char.IsDigit(id[i]))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>True for <c>slug_</c> plus exactly six Crockford / base36 characters.</summary>
		internal static bool LooksLikeSlugTokenId(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return false;
			}

			int split = id.LastIndexOf('_');
			if (split <= 0 || split + 1 + TokenLength != id.Length)
			{
				return false;
			}

			if (!IsLegalSlug(id.Substring(0, split)))
			{
				return false;
			}

			for (int i = split + 1; i < id.Length; i++)
			{
				char c = id[i];
				if (Alphabet.IndexOf(c) < 0 && (c < 'a' || c > 'z') && (c < '0' || c > '9'))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>True for a numeric Lab id or a <c>slug_token</c> id. Used so leftover named folders still rekey.</summary>
		internal static bool LooksLikeGeneratedId(string id)
		{
			return LooksLikeNumericId(id) || LooksLikeSlugTokenId(id);
		}

		/// <summary>Slug stem of a <c>slug_token</c> id, or a legalized fallback.</summary>
		internal static string SlugFromId(string id, string emptyFallback)
		{
			if (LooksLikeSlugTokenId(id))
			{
				return id.Substring(0, id.LastIndexOf('_'));
			}

			return LegalizeSlug(id, emptyFallback);
		}
	}
}
