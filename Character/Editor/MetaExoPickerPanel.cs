using System;
using System.Collections.Generic;
using Ironhide.Legends.Model.Game.Units;
using LokrCharacterLoader;
using SimpleUI;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>"Choose..." popup for MenuBarPanel's Import metaExo id field, listing every distinct metaExo id from CharacterAPI.KnownUnitDefinitions, labeled by the unit's own readable id rather than the raw metaExo string.</summary>
	/// <remarks>A convenience on top of the free-typed metaExo field, not a replacement -- typing an id that isn't in the currently-loaded roster still works exactly as before.</remarks>
	internal static class MetaExoPickerPanel
	{
		private static UiModal modal;
		private static UiStack list;
		private static UiLabel emptyLabel;
		private static Action<string> onSelected;

		/// <summary>Builds the picker modal.</summary>
		internal static void Build(Transform canvas, Font labelFont)
		{
			modal = UiModal.Create(canvas, UiTheme.Default, "Choose a character (from currently-loaded UnitDefinitions)", 1152f, 756f);
			UiStack content = UiStack.Vertical(modal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			modal.Add(content);

			emptyLabel = UiLabel.Create(content.ContentTransform,
				"No UnitDefinitions with a metaExo have been seen yet this session — the game usually parses these at boot, so this should populate before you ever reach the main menu. Type an id directly instead if this stays empty.",
				UiTheme.Default, alignment: TextAnchor.UpperLeft);
			content.Add(emptyLabel);

			list = UiStack.Vertical(content.ContentTransform, UiTheme.Default, spacing: 2f, padding: 0f, scrollable: true);
			list.Grow();
			content.Add(list);

			content.Add(UiButton.Create(content.ContentTransform, "Cancel", Close, primary: false).FixedHeight(32f));
		}

		/// <summary>Opens the picker.</summary>
		internal static void Open(Action<string> onMetaExoSelected)
		{
			onSelected = onMetaExoSelected;
			RefreshList();
			modal.Show();
		}

		private static void Close()
		{
			modal.Hide();
			onSelected = null;
		}

		/// <summary>Rebuilds the list, deduped by metaExo (a rig can in principle be shared by more than one unit id) -- the first unit id seen for a given metaExo wins the label, since they'd render identically either way.</summary>
		private static void RefreshList()
		{
			list.Clear();

			List<(string unitId, string metaExo)> options = new List<(string, string)>();
			HashSet<string> seenMetaExo = new HashSet<string>();
			foreach (KeyValuePair<string, UnitDefinition> entry in CharacterAPI.KnownUnitDefinitions)
			{
				string metaExo = entry.Value?.metaExo;
				if (string.IsNullOrEmpty(metaExo) || !seenMetaExo.Add(metaExo))
				{
					continue;
				}
				options.Add((entry.Key, metaExo));
			}
			options.Sort((a, b) => string.Compare(a.unitId, b.unitId, StringComparison.OrdinalIgnoreCase));

			emptyLabel.Visible(options.Count == 0);
			foreach ((string unitId, string metaExo) in options)
			{
				string capturedMetaExo = metaExo;
				UiButton row = UiButton.Create(list.ContentTransform, string.Format("{0}  ({1})", unitId, metaExo),
					() => Choose(capturedMetaExo), primary: false).FixedHeight(24f);
				list.Add(row);
			}
		}

		private static void Choose(string metaExo)
		{
			Action<string> callback = onSelected;
			Close();
			callback?.Invoke(metaExo);
		}
	}
}
