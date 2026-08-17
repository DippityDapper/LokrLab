using System.Collections.Generic;
using LokrCharacterLoader;
using LokrLab.Shell;
using LokrLabApi;
using SimpleUI;
using UnityEngine;

namespace LokrLab
{
	/// <summary>Inspector list that edits one folder's aliases.json (short name → unique id).</summary>
	internal static class LabAliasesInspector
	{
		/// <summary>Draws add / remove / edit rows for <paramref name="folder"/>/aliases.json.</summary>
		internal static void Draw(string folder, Transform contentParent)
		{
			UiTheme theme = UiTheme.Default;
			UiStack section = UiStack.Vertical(contentParent, theme, spacing: 6f, padding: 0f);
			section.Add(UiLabel.Create(section.ContentTransform, "Aliases", theme, theme.TitleFontSize)
				.FixedHeight(26f));
			section.Add(UiLabel.Create(section.ContentTransform,
				"Short names used as $alias in this folder only. Saving this list writes aliases.json.",
				theme, 11, TextAnchor.UpperLeft).FixedHeight(36f));

			if (string.IsNullOrEmpty(folder))
			{
				section.Add(UiLabel.Create(section.ContentTransform, "No folder is open.").FixedHeight(22f));
				return;
			}

			Dictionary<string, string> map = LabAliases.Load(folder);
			List<string> keys = new List<string>(map.Keys);
			keys.Sort(System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < keys.Count; i++)
			{
				string key = keys[i];
				section.Add(BuildRow(section.ContentTransform, folder, map, key, theme));
			}

			section.Add(UiButton.Create(section.ContentTransform, "Add alias", () =>
			{
				string fresh = NextKey(map);
				map[fresh] = string.Empty;
				LabAliases.Save(folder, map);
				LokrLabApi.LokrLabApi.RequestRefresh();
			}, primary: false).FixedHeight(28f));
		}

		private static UiElement BuildRow(
			Transform parent,
			string folder,
			Dictionary<string, string> map,
			string key,
			UiTheme theme)
		{
			UiStack row = UiStack.Horizontal(parent, theme, spacing: 4f, padding: 0f);
			UiTextField nameField = UiTextField.Create(row.ContentTransform, key, theme);
			row.Add(nameField.FixedWidth(140f));
			LabHoverInfo.Bind(nameField.GameObject, "character.aliases.Key");
			UiTextField idField = UiTextField.Create(row.ContentTransform, map[key], theme);
			row.Add(idField.Grow());
			LabHoverInfo.Bind(idField.GameObject, "character.aliases.Id");
			string currentKey = key;
			nameField.OnEndEdit(value => Rename(folder, map, currentKey, value));
			idField.OnEndEdit(value =>
			{
				map[currentKey] = value != null ? value.Trim() : string.Empty;
				LabAliases.Save(folder, map);
			});
			row.Add(LabClipboard.CreateCopyButton(row.ContentTransform, () => idField.InputField.text));
			row.Add(UiButton.Create(row.ContentTransform, "x", () =>
			{
				map.Remove(currentKey);
				LabAliases.Save(folder, map);
				LokrLabApi.LokrLabApi.RequestRefresh();
			}, primary: false).FixedWidth(28f));
			return row.FixedHeight(28f);
		}

		private static void Rename(string folder, Dictionary<string, string> map, string oldKey, string rawNew)
		{
			string next = rawNew != null ? rawNew.Trim().ToLowerInvariant() : string.Empty;
			if (string.IsNullOrEmpty(next) || string.Equals(next, oldKey, System.StringComparison.Ordinal))
			{
				return;
			}

			if (!LabSlugIds.IsLegalSlug(next))
			{
				return;
			}

			if (map.ContainsKey(next))
			{
				return;
			}

			string uniqueId = map[oldKey];
			map.Remove(oldKey);
			map[next] = uniqueId;
			LabAliases.Save(folder, map);
			LokrLabApi.LokrLabApi.RequestRefresh();
		}

		private static string NextKey(Dictionary<string, string> map)
		{
			string prefix = "alias";
			string key = prefix;
			int n = 2;
			while (map.ContainsKey(key))
			{
				key = prefix + n;
				n++;
			}

			return key;
		}
	}
}
