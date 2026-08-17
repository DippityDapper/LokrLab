using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SimpleUI;
using UnityEngine;
using UnityEngine.UI;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Non-uniform atlas import: auto-detects pixel islands in a loaded atlas, displays it as a clickable image, and lets the user merge islands into one part or discard islands that aren't parts at all.</summary>
	/// <remarks>
	/// Opened directly from MenuBarPanel's Atlas popup rather than going through AnimatorImportRegistry, since that
	/// registry's single-synchronous-call contract doesn't fit this panel's interactive, multi-step session. Built
	/// on SimpleUI: the atlas display is two stacked UiImages (base + overlay); Image.preserveAspect does the
	/// letterboxing and UiImage.OnClickAtUv does the click-to-UV conversion, both accounting for it automatically.
	/// The atlas/list split is plain anchor math (not UiSplit), since this row's size only resolves after a Unity
	/// layout pass runs, and UiSplit needs its parent's size already known. The split row is a raw GameObject
	/// with flexibleHeight, so Build must relax the parent stack's ContentSizeFitter the same way
	/// UiStack.Add(Grow()) would — otherwise the atlas and island list collapse to zero height.
	/// </remarks>
	internal static class IslandAtlasPickerPanel
	{
		private static readonly Color ClearColor = new Color(0f, 0f, 0f, 0f);
		private static readonly Color SelectedTint = new Color(1f, 0.92f, 0.2f, 0.55f);
		private static readonly Color MergedTint = new Color(0.25f, 0.85f, 0.35f, 0.4f);
		private static readonly Color DiscardedTint = new Color(0.85f, 0.2f, 0.2f, 0.45f);

		/// <summary>Same yellow family as SelectedTint, used to highlight a list row whenever any of its islands are part of the current selection, so clicking islands on the atlas shows which row(s) they belong to.</summary>
		private static readonly Color SelectedRowFieldColor = new Color(1f, 0.85f, 0.35f);
		private static readonly Color SelectedRowTextColor = new Color(1f, 0.85f, 0.35f);

		/// <summary>Every detected island starts as its own singleton group, auto-named like GridAtlasImporter's Part_01, Part_02. Merging/discarding/restoring only ever moves islands between groups or flips Excluded, so every island id always belongs to exactly one group.</summary>
		private sealed class PartGroup
		{
			/// <summary>This group's display/part name, editable inline; auto-named like GridAtlasImporter's Part_01, Part_02.</summary>
			internal string Name;
			/// <summary>The ids of every island currently merged into this group.</summary>
			internal readonly List<int> IslandIds = new List<int>();
			/// <summary>Whether this group is discarded and won't be imported as a part.</summary>
			internal bool Excluded;
		}

		private static UiModal modal;
		private static UiTextField characterFolderField;
		private static UiLabel targetFolderPreviewLabel;
		private static UiImage atlasImage;
		private static UiImage overlayImage;
		private static UiStack list;

		private static Texture2D atlasTexture;
		private static Texture2D overlayTexture;
		private static Sprite atlasSprite;
		private static Sprite overlaySprite;
		private static int atlasWidth;
		private static int atlasHeight;
		private static int[] labelMap;
		private static List<PixelIsland> islands = new List<PixelIsland>();
		private static string targetFolder;

		private static readonly List<PartGroup> groups = new List<PartGroup>();
		private static readonly HashSet<int> selectedIslandIds = new HashSet<int>();

		/// <summary>Drops modal, texture, and list refs after the lab scene is destroyed.</summary>
		internal static void ResetSession()
		{
			modal = null;
			characterFolderField = null;
			targetFolderPreviewLabel = null;
			atlasImage = null;
			overlayImage = null;
			list = null;
			if (atlasSprite != null)
			{
				UnityEngine.Object.Destroy(atlasSprite);
				atlasSprite = null;
			}
			if (overlaySprite != null)
			{
				UnityEngine.Object.Destroy(overlaySprite);
				overlaySprite = null;
			}
			if (atlasTexture != null)
			{
				UnityEngine.Object.Destroy(atlasTexture);
				atlasTexture = null;
			}
			if (overlayTexture != null)
			{
				UnityEngine.Object.Destroy(overlayTexture);
				overlayTexture = null;
			}
			labelMap = null;
			islands.Clear();
			groups.Clear();
			selectedIslandIds.Clear();
			targetFolder = null;
		}

		/// <summary>Builds the modal's UI, hidden until Open() is called. Skips when the current modal is still live.</summary>
		/// <remarks>The destination folder field is a plain name (not a path) resolved under CharacterLabPaths.CharactersRoot -- Import used to silently write into whatever RigEditorScene.CurrentFolder happened to be, easy to get wrong. The overlay image carries the click target since it's the topmost object over the same rect as the base atlas image, so clicks land regardless of the overlay's own per-pixel transparency.</remarks>
		internal static void Build(Transform canvas, Font labelFont)
		{
			if (canvas == null || IsLive(modal))
			{
				return;
			}

			modal = UiModal.Create(canvas, UiTheme.Default, null, 1728f, 907f);
			UiStack content = UiStack.Vertical(modal.ContentParent, UiTheme.Default, spacing: 8f, padding: 12f);
			modal.Add(content);

			content.Add(UiLabel.Create(content.ContentTransform,
				"Pick Islands — click island(s) on the atlas, then Merge or Discard; Import when ready",
				UiTheme.Default, UiTheme.Default.TitleFontSize).FixedHeight(28f));

			UiStack folderRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(folderRow.FixedHeight(28f));
			folderRow.Add(UiLabel.Create(folderRow.ContentTransform, "Character folder:").FixedWidth(140f));
			characterFolderField = UiTextField.Create(folderRow.ContentTransform);
			characterFolderField.InputField.onValueChanged.AddListener(_ => RefreshTargetFolderPreview());
			folderRow.Add(characterFolderField.FixedWidth(260f));
			targetFolderPreviewLabel = UiLabel.Create(folderRow.ContentTransform, "", UiTheme.Default, 12);
			targetFolderPreviewLabel.SetColor(new Color(0.7f, 0.75f, 0.85f));
			targetFolderPreviewLabel.Grow();
			folderRow.Add(targetFolderPreviewLabel);

			GameObject splitRow = new GameObject("AtlasSplit", typeof(RectTransform));
			splitRow.transform.SetParent(content.ContentTransform, false);
			splitRow.AddComponent<LayoutElement>().flexibleHeight = 1f;
			ContentSizeFitter contentFitter = content.GameObject.GetComponent<ContentSizeFitter>();
			if (contentFitter != null)
			{
				contentFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
			}

			RectTransform splitRect = splitRow.GetComponent<RectTransform>();
			splitRect.anchorMin = Vector2.zero;
			splitRect.anchorMax = Vector2.one;

			GameObject atlasArea = new GameObject("AtlasArea", typeof(RectTransform));
			atlasArea.transform.SetParent(splitRow.transform, false);
			RectTransform atlasAreaRect = atlasArea.GetComponent<RectTransform>();
			atlasAreaRect.anchorMin = new Vector2(0f, 0f);
			atlasAreaRect.anchorMax = new Vector2(0.6f, 1f);
			atlasAreaRect.offsetMin = Vector2.zero;
			atlasAreaRect.offsetMax = new Vector2(-6f, 0f);

			GameObject listArea = new GameObject("ListArea", typeof(RectTransform));
			listArea.transform.SetParent(splitRow.transform, false);
			RectTransform listAreaRect = listArea.GetComponent<RectTransform>();
			listAreaRect.anchorMin = new Vector2(0.6f, 0f);
			listAreaRect.anchorMax = new Vector2(1f, 1f);
			listAreaRect.offsetMin = new Vector2(6f, 0f);
			listAreaRect.offsetMax = Vector2.zero;

			atlasImage = UiImage.Create(atlasArea.transform);
			atlasImage.Image.raycastTarget = false;
			overlayImage = UiImage.Create(atlasArea.transform);
			overlayImage.OnClickAtUv(HandleAtlasClick);

			list = UiStack.Vertical(listArea.transform, UiTheme.Default, spacing: 2f, padding: 4f, scrollable: true);

			UiStack buttonsRow = UiStack.Horizontal(content.ContentTransform, UiTheme.Default, spacing: 8f, padding: 0f);
			content.Add(buttonsRow.FixedHeight(32f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Merge Selected", OnMergeSelectedClicked, primary: false).FixedWidth(160f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Discard Selected", OnDiscardSelectedClicked, primary: false).FixedWidth(160f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Discard Unselected", OnDiscardUnselectedClicked, primary: false).FixedWidth(190f));
			UiLabel spacer = UiLabel.Create(buttonsRow.ContentTransform, string.Empty);
			spacer.Grow();
			buttonsRow.Add(spacer);
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Import", OnImportClicked, primary: false).FixedWidth(100f));
			buttonsRow.Add(UiButton.Create(buttonsRow.ContentTransform, "Cancel", Close, primary: false).FixedWidth(100f));
		}

		/// <summary>Loads an atlas image, detects its pixel islands, and shows the modal.</summary>
		/// <remarks>Suggests the currently-loaded rig's folder name as a starting point, as plain editable text rather than a silent default.</remarks>
		internal static void Open(string atlasPath)
		{
			if (string.IsNullOrEmpty(atlasPath) || !File.Exists(atlasPath))
			{
				RigEditorScene.SetStatus("No atlas file specified, or it doesn't exist: " + atlasPath);
				return;
			}

			Build(Lab.Canvas, Lab.DefaultFont);
			if (!IsLive(modal))
			{
				RigEditorScene.SetStatus("Slice Atlas picker is not available (lab canvas missing).");
				return;
			}

			Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!loaded.LoadImage(File.ReadAllBytes(atlasPath)))
			{
				RigEditorScene.SetStatus("Could not decode '" + atlasPath + "' as an image.");
				UnityEngine.Object.Destroy(loaded);
				return;
			}

			if (atlasTexture != null)
			{
				UnityEngine.Object.Destroy(atlasTexture);
			}
			atlasTexture = loaded;
			atlasWidth = atlasTexture.width;
			atlasHeight = atlasTexture.height;

			characterFolderField.SetText(SuggestCharacterFolderName());
			RefreshTargetFolderPreview();

			islands = PixelIslandDetector.Detect(atlasTexture, out labelMap);

			groups.Clear();
			selectedIslandIds.Clear();
			foreach (PixelIsland island in islands)
			{
				PartGroup group = new PartGroup { Name = "Part_" + (island.Id + 1).ToString("00", CultureInfo.InvariantCulture) };
				group.IslandIds.Add(island.Id);
				groups.Add(group);
			}

			if (atlasSprite != null)
			{
				UnityEngine.Object.Destroy(atlasSprite);
			}
			atlasSprite = Sprite.Create(atlasTexture, new Rect(0f, 0f, atlasWidth, atlasHeight), new Vector2(0.5f, 0.5f), 100f);
			atlasImage.SetSprite(atlasSprite);

			if (overlayTexture != null)
			{
				UnityEngine.Object.Destroy(overlayTexture);
			}
			overlayTexture = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);
			if (overlaySprite != null)
			{
				UnityEngine.Object.Destroy(overlaySprite);
			}
			overlaySprite = Sprite.Create(overlayTexture, new Rect(0f, 0f, atlasWidth, atlasHeight), new Vector2(0.5f, 0.5f), 100f);
			overlayImage.SetSprite(overlaySprite);

			RefreshOverlay();
			RefreshList();
			RigEditorScene.SetStatus(string.Format("Detected {0} pixel island(s) in '{1}'.", islands.Count, Path.GetFileName(atlasPath)));

			modal.Show();
		}

		private static void Close()
		{
			modal.Hide();
		}

		private static string SuggestCharacterFolderName()
		{
			string current = RigEditorScene.CurrentFolder;
			if (string.IsNullOrEmpty(current))
			{
				return string.Empty;
			}
			return Path.GetFileName(current.TrimEnd('\\', '/'));
		}

		/// <summary>The plain folder name typed into characterFolderField, sanitized and resolved under CharacterLabPaths.CharactersRoot. Always computed fresh from the field, never cached.</summary>
		private static string GetCharacterFolder()
		{
			string name = SanitizeFileName((characterFolderField.InputField.text ?? string.Empty).Trim());
			return string.IsNullOrEmpty(name) ? null : Path.Combine(CharacterLabPaths.CharactersRoot, name);
		}

		/// <summary>The character folder's sprites/ subfolder, where island PNGs actually get written.</summary>
		private static string GetTargetFolder()
		{
			string characterFolder = GetCharacterFolder();
			return characterFolder == null ? null : Path.Combine(characterFolder, "sprites");
		}

		private static void RefreshTargetFolderPreview()
		{
			string folder = GetTargetFolder();
			targetFolderPreviewLabel.SetText(folder != null ? "-> " + folder : "Enter a character folder name above.");
		}

		/// <summary>Converts a click's normalized UV (0,0 = bottom-left) to atlas pixel coordinates and toggles whichever island occupies that pixel in/out of the current selection.</summary>
		private static void HandleAtlasClick(Vector2 uv)
		{
			int pixelX = Mathf.Clamp(Mathf.FloorToInt(uv.x * atlasWidth), 0, atlasWidth - 1);
			int pixelY = Mathf.Clamp(Mathf.FloorToInt(uv.y * atlasHeight), 0, atlasHeight - 1);
			int islandId = labelMap[pixelX + pixelY * atlasWidth];
			if (islandId < 0)
			{
				return;
			}
			if (!selectedIslandIds.Remove(islandId))
			{
				selectedIslandIds.Add(islandId);
			}
			RefreshOverlay();
			RefreshList();
		}

		/// <summary>Regenerates the overlay texture from current selection/group state -- yellow over the click-selection, green over merged islands, red over discarded ones, transparent elsewhere.</summary>
		private static void RefreshOverlay()
		{
			Dictionary<int, PartGroup> groupByIslandId = new Dictionary<int, PartGroup>();
			foreach (PartGroup group in groups)
			{
				foreach (int id in group.IslandIds)
				{
					groupByIslandId[id] = group;
				}
			}

			Color[] overlayPixels = new Color[atlasWidth * atlasHeight];
			for (int i = 0; i < overlayPixels.Length; i++)
			{
				int islandId = labelMap[i];
				if (islandId < 0)
				{
					overlayPixels[i] = ClearColor;
					continue;
				}
				if (selectedIslandIds.Contains(islandId))
				{
					overlayPixels[i] = SelectedTint;
					continue;
				}
				PartGroup group = groupByIslandId[islandId];
				if (group.Excluded)
				{
					overlayPixels[i] = DiscardedTint;
				}
				else if (group.IslandIds.Count > 1)
				{
					overlayPixels[i] = MergedTint;
				}
				else
				{
					overlayPixels[i] = ClearColor;
				}
			}
			overlayTexture.SetPixels(overlayPixels);
			overlayTexture.Apply();
		}

		private static void RefreshList()
		{
			list.Clear();

			list.Add(UiLabel.Create(list.ContentTransform, "Parts to import:", UiTheme.Default, 13).FixedHeight(22f));
			foreach (PartGroup group in groups)
			{
				if (group.Excluded)
				{
					continue;
				}
				list.Add(BuildIncludedGroupRow(group, GroupIntersectsSelection(group)));
			}

			list.Add(UiLabel.Create(list.ContentTransform, "Discarded:", UiTheme.Default, 13).FixedHeight(22f));
			bool anyDiscarded = false;
			foreach (PartGroup group in groups)
			{
				if (!group.Excluded)
				{
					continue;
				}
				anyDiscarded = true;
				list.Add(BuildDiscardedGroupRow(group, GroupIntersectsSelection(group)));
			}
			if (!anyDiscarded)
			{
				list.Add(UiLabel.Create(list.ContentTransform, "(none)", UiTheme.Default, 11).FixedHeight(26f));
			}
		}

		/// <summary>Builds a row for an included part group. Highlights the name field when the group intersects the current selection, so which row was just clicked on the atlas is obvious right where it'd be renamed.</summary>
		private static UiStack BuildIncludedGroupRow(PartGroup group, bool isSelected)
		{
			UiStack row = UiStack.Horizontal(list.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			row.FixedHeight(26f);

			UiTextField nameField = UiTextField.Create(row.ContentTransform, group.Name);
			nameField.OnEndEdit(text => group.Name = text);
			if (isSelected)
			{
				nameField.GameObject.GetComponent<Image>().color = SelectedRowFieldColor;
			}
			row.Add(nameField.FixedWidth(200f));

			row.Add(UiLabel.Create(row.ContentTransform, group.IslandIds.Count + " island(s)", UiTheme.Default, 11).FixedWidth(90f));
			row.Add(UiButton.Create(row.ContentTransform, "Select", () => OnSelectGroupClicked(group), primary: false).FixedWidth(70f));
			row.Add(UiButton.Create(row.ContentTransform, "Discard", () => OnDiscardGroupClicked(group), primary: false).FixedWidth(70f));

			return row;
		}

		/// <summary>Builds a row for a discarded part group, with a Restore button.</summary>
		private static UiStack BuildDiscardedGroupRow(PartGroup group, bool isSelected)
		{
			UiStack row = UiStack.Horizontal(list.ContentTransform, UiTheme.Default, spacing: 4f, padding: 0f);
			row.FixedHeight(26f);

			UiLabel name = UiLabel.Create(row.ContentTransform, group.Name + " (" + group.IslandIds.Count + " island(s))", UiTheme.Default, 11);
			if (isSelected)
			{
				name.SetColor(SelectedRowTextColor);
			}
			name.Grow();
			row.Add(name);
			row.Add(UiButton.Create(row.ContentTransform, "Restore", () => OnRestoreClicked(group), primary: false).FixedWidth(80f));

			return row;
		}

		/// <summary>Whether any of this group's islands are part of the current click-selection.</summary>
		private static bool GroupIntersectsSelection(PartGroup group)
		{
			foreach (int id in group.IslandIds)
			{
				if (selectedIslandIds.Contains(id))
				{
					return true;
				}
			}
			return false;
		}

		private static void OnSelectGroupClicked(PartGroup group)
		{
			selectedIslandIds.Clear();
			foreach (int id in group.IslandIds)
			{
				selectedIslandIds.Add(id);
			}
			RefreshOverlay();
			RefreshList();
		}

		private static void OnDiscardGroupClicked(PartGroup group)
		{
			group.Excluded = true;
			selectedIslandIds.Clear();
			RefreshOverlay();
			RefreshList();
		}

		private static void OnRestoreClicked(PartGroup group)
		{
			group.Excluded = false;
			RefreshOverlay();
			RefreshList();
		}

		/// <summary>Merges every group touched by the current selection into one. The surviving group is whichever contains the overall-lowest island id (a stable, deterministic choice) and keeps its own Name; the result is un-discarded.</summary>
		private static void OnMergeSelectedClicked()
		{
			if (selectedIslandIds.Count < 2)
			{
				RigEditorScene.SetStatus("Select two or more islands to merge (click them on the atlas first).");
				return;
			}

			List<PartGroup> touched = new List<PartGroup>();
			foreach (PartGroup group in groups)
			{
				foreach (int id in group.IslandIds)
				{
					if (selectedIslandIds.Contains(id))
					{
						touched.Add(group);
						break;
					}
				}
			}

			if (touched.Count < 2)
			{
				selectedIslandIds.Clear();
				RefreshOverlay();
				RefreshList();
				return;
			}

			PartGroup surviving = touched[0];
			int lowestId = MinIslandId(surviving);
			for (int i = 1; i < touched.Count; i++)
			{
				int candidateLowest = MinIslandId(touched[i]);
				if (candidateLowest < lowestId)
				{
					lowestId = candidateLowest;
					surviving = touched[i];
				}
			}

			surviving.Excluded = false;
			foreach (PartGroup group in touched)
			{
				if (group == surviving)
				{
					continue;
				}
				surviving.IslandIds.AddRange(group.IslandIds);
				groups.Remove(group);
			}

			selectedIslandIds.Clear();
			RefreshOverlay();
			RefreshList();
		}

		private static int MinIslandId(PartGroup group)
		{
			int min = int.MaxValue;
			foreach (int id in group.IslandIds)
			{
				if (id < min)
				{
					min = id;
				}
			}
			return min;
		}

		private static void OnDiscardSelectedClicked()
		{
			if (selectedIslandIds.Count == 0)
			{
				RigEditorScene.SetStatus("Select island(s) on the atlas first.");
				return;
			}
			foreach (PartGroup group in groups)
			{
				foreach (int id in group.IslandIds)
				{
					if (selectedIslandIds.Contains(id))
					{
						group.Excluded = true;
						break;
					}
				}
			}
			selectedIslandIds.Clear();
			RefreshOverlay();
			RefreshList();
		}

		/// <summary>Inverse of Discard Selected: click every island to KEEP, then this discards every group the selection doesn't touch in one shot -- the fast path for an atlas with scattered stray pixels.</summary>
		private static void OnDiscardUnselectedClicked()
		{
			if (selectedIslandIds.Count == 0)
			{
				RigEditorScene.SetStatus("Select the island(s) you want to KEEP first, then click Discard Unselected.");
				return;
			}
			foreach (PartGroup group in groups)
			{
				bool touchesSelection = false;
				foreach (int id in group.IslandIds)
				{
					if (selectedIslandIds.Contains(id))
					{
						touchesSelection = true;
						break;
					}
				}
				if (!touchesSelection)
				{
					group.Excluded = true;
				}
			}
			selectedIslandIds.Clear();
			RefreshOverlay();
			RefreshList();
		}

		private static void OnImportClicked()
		{
			string characterFolder = GetCharacterFolder();
			string resolvedFolder = GetTargetFolder();
			if (characterFolder == null || resolvedFolder == null)
			{
				RigEditorScene.SetStatus("Enter a character folder name before importing (created under Characters/).");
				return;
			}
			targetFolder = resolvedFolder;

			List<PartGroup> toWrite = new List<PartGroup>();
			HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PartGroup group in groups)
			{
				if (group.Excluded)
				{
					continue;
				}
				string name = SanitizeFileName((group.Name ?? string.Empty).Trim());
				if (string.IsNullOrEmpty(name))
				{
					RigEditorScene.SetStatus("Every part needs a name before importing (found a blank one).");
					return;
				}
				if (!usedNames.Add(name))
				{
					RigEditorScene.SetStatus("Part name '" + name + "' is used more than once — names must be unique.");
					return;
				}
				group.Name = name;
				toWrite.Add(group);
			}
			if (toWrite.Count == 0)
			{
				RigEditorScene.SetStatus("Nothing to import — every island is discarded.");
				return;
			}

			if (!WriteGroups(toWrite, out string message))
			{
				RigEditorScene.SetStatus("Island import failed: " + message);
				return;
			}
			Close();
			RigEditorScene.OnIslandAtlasImported(characterFolder, message);
		}

		private static string SanitizeFileName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return name;
			}
			char[] invalid = Path.GetInvalidFileNameChars();
			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
			{
				sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
			}
			return sb.ToString();
		}

		/// <summary>Writes one PNG per group: crops to the union bounding box of its islands, masking out any pixel not belonging to one of the group's own island ids -- keeps a merge correct even when a third, unrelated island's pixels fall inside the combined bounding box.</summary>
		private static bool WriteGroups(List<PartGroup> toWrite, out string message)
		{
			try
			{
				Directory.CreateDirectory(targetFolder);
				Color[] atlasPixels = atlasTexture.GetPixels();

				foreach (PartGroup group in toWrite)
				{
					int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
					foreach (int islandId in group.IslandIds)
					{
						PixelIsland island = islands[islandId];
						if (island.MinX < minX) minX = island.MinX;
						if (island.MinY < minY) minY = island.MinY;
						if (island.MaxX > maxX) maxX = island.MaxX;
						if (island.MaxY > maxY) maxY = island.MaxY;
					}

					int width = maxX - minX + 1;
					int height = maxY - minY + 1;
					HashSet<int> memberIds = new HashSet<int>(group.IslandIds);
					Color[] cropped = new Color[width * height];
					for (int y = 0; y < height; y++)
					{
						for (int x = 0; x < width; x++)
						{
							int srcIndex = (minX + x) + (minY + y) * atlasWidth;
							int label = labelMap[srcIndex];
							cropped[y * width + x] = (label >= 0 && memberIds.Contains(label)) ? atlasPixels[srcIndex] : ClearColor;
						}
					}

					Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, false);
					output.SetPixels(cropped);
					output.Apply();
					File.WriteAllBytes(Path.Combine(targetFolder, group.Name + ".png"), output.EncodeToPNG());
					UnityEngine.Object.Destroy(output);
				}

				message = string.Format("Imported {0} part(s) from pixel islands into {1}.", toWrite.Count, targetFolder);
				return true;
			}
			catch (Exception ex)
			{
				message = "Failed to write part PNGs: " + ex.Message;
				return false;
			}
		}

		/// <summary>True when the widget still exists in the current lab scene.</summary>
		private static bool IsLive(UiElement element)
		{
			return element != null && element.GameObject != null;
		}
	}
}
