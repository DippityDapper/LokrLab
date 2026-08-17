using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ironhide.AssetBundles;
using Ironhide.ExoSkeleton;
using LokrModAPI.Serialization;
using UnityEngine;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>Reconstructs an editable rig from a real, shipped base-game character.</summary>
	/// <remarks>Real characters never ship the original authoring JSON -- only the final baked Part vertices/uvs and Animation matrices survive -- so this derives a rig.json that reproduces the same visual result rather than recovering a literal original file. Output goes through the exact same Load path as any other rig folder, so nothing downstream needs to know an import happened.</remarks>
	internal static class CharacterImporter
	{
		private const float PixelsToUnits = 100f;

		/// <summary>Reconstructs a rig.json + part PNGs from a shipped ExoSkeletonDataAsset, the inverse of RigEditorScene.OnSaveClicked.</summary>
		/// <remarks>
		/// Writes into &lt;folder&gt;/rig/ + &lt;folder&gt;/sprites/, the same character-folder layout
		/// RigEditorScene.OnLoadClicked/OnSaveClicked and CustomRigLoader use. ExoSkeletonRenderer only ever reads
		/// partSprites[0].texture for the whole mesh, so every part's working rig necessarily shares that one atlas.
		/// ReloadData bakes part.vertices[i] = offset + k*sprite.vertices[i] (k = 1/partScaleCompensation); since
		/// this importer's own PackSprites always creates centered-pivot (0.5,0.5) sprites, centroid(part.vertices)
		/// is exactly right for offset regardless of the original asset's pivot. Crops use the baked part.uvs quad
		/// (what ExoSkeletonRenderer samples). sprite.textureRect is a different packed-cell space and does not
		/// match those UVs — using it assigned the wrong atlas cell (Head03 came out as a torso). Vanilla atlases
		/// often store alpha on associatedAlphaSplitTexture; without it, packing-gap RGB is opaque.
		/// The PNG is then resized to part.vertices' bounding box so Load (scaleComp 1) matches the baked mesh.
		/// Each baked matrix is already the final MatrixFlash NewMatrix would have produced, so its conversion
		/// is inverted (raw(a,b,c,d,tx,ty) -&gt; MatrixFlash(a,-b,-c,d,tx/P,-ty/P)) to recover raw JSON values.
		/// Frame events and attach points come from the baked AnimationFrame (they are on the asset — empty
		/// events used to softlock AbilityMeleeActivity). Missing AbilityAction / AbilityEnd and Head / Chest
		/// / Base are backfilled the same way Animator Save does.
		/// </remarks>
		internal static bool Import(string metaExoName, out string outputFolder, out string message)
		{
			outputFolder = null;
			metaExoName = (metaExoName ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(metaExoName))
			{
				message = "Enter a metaExo id first (e.g. ExoSkeletonHumanRanger_MetaDataAsset).";
				return false;
			}

			string folder = Path.Combine(CharacterLabPaths.CharactersRoot, metaExoName);
			Directory.CreateDirectory(CharacterLabPaths.CharacterSoundsFolder(metaExoName));
			Directory.CreateDirectory(CharacterLabPaths.CharacterPortraitsFolder(metaExoName));
			if (!ImportInto(metaExoName, folder, null, out message))
			{
				return false;
			}

			outputFolder = folder;
			message += " Click Load to open it.";
			return true;
		}

		/// <summary>Reconstructs a vanilla MetaExo into destFolder/rig + sprites, optionally cropping parts from a pack reskin PNG.</summary>
		/// <remarks>
		/// destFolder is the Lab character folder (slug_token), not a folder named after the vanilla asset.
		/// reskinPngPath must be a paint-over of this exo's atlas (same layout). Pack files are often
		/// named after Model (Musketeer: BanditArcher.png, 780x128), not MetaExo (Ranger, 1044x252) —
		/// callers resolve the exo from that filename via TryResolveExoFromModel before calling here.
		/// Crops use each part's baked UV quad (ExoSkeletonRenderer) plus the atlas split-alpha sheet.
		/// </remarks>
		internal static bool ImportInto(string metaExoName, string destFolder, string reskinPngPath, out string message)
		{
			return ImportInto(metaExoName, model: null, destFolder, reskinPngPath, out message);
		}

		/// <summary>Reconstructs the richest shipped exo (combat prefab when it has more clips than MetaExo) into destFolder.</summary>
		/// <remarks>
		/// MetaExo assets are map/UI poses. Combat clips are on the Model prefab. model may be empty;
		/// the MetaExo name is then inferred to ExoSkeletonX_MetaDataAsset → X.
		/// </remarks>
		internal static bool ImportInto(
			string metaExoName,
			string model,
			string destFolder,
			string reskinPngPath,
			out string message)
		{
			message = null;
			metaExoName = (metaExoName ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(metaExoName) && string.IsNullOrEmpty(model))
			{
				message = "Enter a metaExo id first (e.g. ExoSkeletonHumanRanger_MetaDataAsset).";
				return false;
			}

			ExoSkeletonDataAsset asset = ResolveRichestExo(metaExoName, model);
			if (asset == null)
			{
				message = "Could not find an importable exo for '"
					+ (string.IsNullOrEmpty(metaExoName) ? model : metaExoName) + "'.";
				return false;
			}

			return ImportInto(asset, destFolder, reskinPngPath, out message);
		}

		/// <summary>Reconstructs a live ExoSkeletonDataAsset into destFolder/rig + sprites, optionally cropping a pack reskin PNG.</summary>
		/// <remarks>
		/// Enemy Model prefabs (BanditArcher) keep the exo on the prefab; asset.name is not a
		/// units-bundle key, so LoadAsset by that name fails and import used to write the gray
		/// placeholder body. This overload uses the prefab reference directly.
		/// </remarks>
		internal static bool ImportInto(ExoSkeletonDataAsset asset, string destFolder, string reskinPngPath, out string message)
		{
			message = null;
			if (asset == null)
			{
				message = "ExoSkeletonDataAsset is null.";
				return false;
			}

			if (string.IsNullOrEmpty(destFolder))
			{
				message = "Destination character folder is empty.";
				return false;
			}

			string exoLabel = string.IsNullOrEmpty(asset.name) ? "(unnamed exo)" : asset.name;
			if (asset.partSprites == null || asset.partSprites.Count == 0 || asset.partSprites[0] == null)
			{
				message = "'" + exoLabel + "' has no partSprites — nothing to export.";
				return false;
			}

			if (asset.parts == null || asset.parts.Count == 0)
			{
				message = "'" + exoLabel + "' has no parts — nothing to export.";
				return false;
			}

			Texture2D readableAtlas = null;
			Texture2D reskinAtlas = null;
			try
			{
				readableAtlas = MakeReadableAtlas(asset.partSprites[0]);
				int vanillaWidth = readableAtlas.width;
				int vanillaHeight = readableAtlas.height;
				if (!string.IsNullOrEmpty(reskinPngPath) && File.Exists(reskinPngPath))
				{
					reskinAtlas = LoadPng(reskinPngPath);
					if (reskinAtlas != null)
					{
						if (reskinAtlas.width != vanillaWidth || reskinAtlas.height != vanillaHeight)
						{
							message = "Reskin '" + Path.GetFileName(reskinPngPath) + "' is "
								+ reskinAtlas.width + "x" + reskinAtlas.height + " (vanilla atlas is "
								+ vanillaWidth + "x" + vanillaHeight
								+ "). Parts were cropped anyway and may need Animator work.";
						}

						UnityEngine.Object.Destroy(readableAtlas);
						readableAtlas = reskinAtlas;
						reskinAtlas = null;
					}
				}

				string folder = destFolder;
				string rigSubfolder = Path.Combine(folder, "rig");
				string spritesSubfolder = Path.Combine(folder, "sprites");
				Directory.CreateDirectory(rigSubfolder);
				Directory.CreateDirectory(spritesSubfolder);
				ClearReconstructedOutputs(rigSubfolder, spritesSubfolder);

				StringBuilder partsJson = new StringBuilder();
				bool firstPart = true;
				foreach (Part part in asset.parts)
				{
					Vector2 centroid = Centroid(part.vertices);
					float offsetX = centroid.x * PixelsToUnits;
					float offsetY = -1f * centroid.y * PixelsToUnits;

					(int targetWidthPx, int targetHeightPx) = ComputeTargetPixelSize(part.vertices);
					ExportPartTexture(readableAtlas, part, spritesSubfolder, targetWidthPx, targetHeightPx);

					if (!firstPart)
					{
						partsJson.Append(",");
					}
					firstPart = false;
					partsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(part.name)).Append("\",\"offsetX\":")
						.Append(F(offsetX)).Append(",\"offsetY\":").Append(F(offsetY)).Append("}");
				}

				ComputeDefaultAttachRaw(asset, out Vector2 headRaw, out Vector2 chestRaw, out Vector2 baseRaw);
				StringBuilder animationsJson = new StringBuilder();
				int animationCount = asset.animations == null ? 0 : asset.animations.Length;
				for (int a = 0; a < animationCount; a++)
				{
					Ironhide.ExoSkeleton.Animation animation = asset.animations[a];
					if (a > 0)
					{
						animationsJson.Append(",");
					}
					animationsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(animation.name)).Append("\",\"frames\":[");

					int frameCount = animation.frames == null ? 0 : animation.frames.Length;
					bool clipHasAction = ClipHasEvent(animation, CombatPlaybackRequirements.AbilityActionEvent);
					bool clipHasEnd = ClipHasEvent(animation, CombatPlaybackRequirements.AbilityEndEvent);
					for (int f = 0; f < frameCount; f++)
					{
						AnimationFrame frame = animation.frames[f];
						if (f > 0)
						{
							animationsJson.Append(",");
						}
						animationsJson.Append("{\"duration\":").Append(F(frame.time)).Append(",\"parts\":[");

						bool firstFramePart = true;
						int orderLength = frame.renderOrder == null ? 0 : frame.renderOrder.Length;
						for (int i = 0; i < orderLength; i++)
						{
							int partIndex = frame.renderOrder[i];
							if (partIndex < 0 || partIndex >= asset.parts.Count || frame.matrices == null
								|| i >= frame.matrices.Length)
							{
								continue;
							}
							string partName = asset.parts[partIndex].name;
							InvertBakedMatrix(frame.matrices[i], out float rawA, out float rawB, out float rawC,
								out float rawD, out float rawTx, out float rawTy);

							if (!firstFramePart)
							{
								animationsJson.Append(",");
							}
							firstFramePart = false;
							animationsJson.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(partName)).Append("\",\"matrix\":[")
								.Append(F(rawA)).Append(",").Append(F(rawB)).Append(",").Append(F(rawC)).Append(",")
								.Append(F(rawD)).Append(",").Append(F(rawTx)).Append(",").Append(F(rawTy)).Append("]}");
						}

						AppendFrameEventsAndAttachPoints(animationsJson, frame, animation.name, f, frameCount,
							clipHasAction, clipHasEnd, headRaw, chestRaw, baseRaw);
					}
					animationsJson.Append("]}");
				}

				string json = "{\"partsPadding\":0,\"parts\":[" + partsJson + "],\"animations\":[" + animationsJson + "]}";
				File.WriteAllText(Path.Combine(rigSubfolder, "rig.json"), json);

				string summary = string.Format("Imported '{0}': {1} parts, {2} animation(s) into {3}.",
					exoLabel, asset.parts.Count, animationCount, folder);
				message = string.IsNullOrEmpty(message) ? summary : summary + " " + message;
				return true;
			}
			catch (Exception ex)
			{
				message = "Import failed: " + ex.Message;
				LokrCharacterLabPlugin.Log.LogError("CharacterImporter: " + ex);
				return false;
			}
			finally
			{
				if (readableAtlas != null)
				{
					UnityEngine.Object.Destroy(readableAtlas);
				}

				if (reskinAtlas != null)
				{
					UnityEngine.Object.Destroy(reskinAtlas);
				}
			}
		}

		/// <summary>Combat prefab exo when it has at least as many clips as MetaExo, otherwise the MetaDataAsset.</summary>
		/// <remarks>
		/// Ranger's MetaDataAsset dump has five clips (Vanilla, Portrait, Stand, Victory, debug). Walk / Attack /
		/// Death live on the Model prefab. Edit Vanilla Hero used to import MetaExo and never saw combat clips.
		/// </remarks>
		private static ExoSkeletonDataAsset ResolveRichestExo(string metaExoName, string model)
		{
			ExoSkeletonDataAsset meta = null;
			if (!string.IsNullOrEmpty(metaExoName))
			{
				meta = AssetBundleManager.LoadAsset<ExoSkeletonDataAsset>("units", metaExoName);
			}

			if (string.IsNullOrEmpty(model))
			{
				model = ExoImportRules.InferModelFromMetaExo(metaExoName);
			}

			ExoSkeletonDataAsset prefab = null;
			if (!string.IsNullOrEmpty(model))
			{
				TryResolveExoFromModel(model, out prefab);
			}

			int metaCount = AnimationCount(meta);
			int prefabCount = AnimationCount(prefab);
			if (ExoImportRules.PreferPrefabExo(prefabCount, metaCount) && IsImportableExo(prefab))
			{
				if (prefabCount != metaCount)
				{
					LokrCharacterLabPlugin.Log.LogInfo(
						"CharacterImporter: using Model prefab exo '" + model + "' ("
						+ prefabCount + " animations) instead of MetaExo (" + metaCount + ").");
				}

				return prefab;
			}

			if (IsImportableExo(meta))
			{
				return meta;
			}

			return IsImportableExo(prefab) ? prefab : null;
		}

		/// <summary>Clip count on a shipped exo, or 0 when the asset or array is missing.</summary>
		private static int AnimationCount(ExoSkeletonDataAsset asset)
		{
			return asset != null && asset.animations != null ? asset.animations.Length : 0;
		}

		/// <summary>Drops previous reconstruct output so a map-only import cannot hide combat clips on reload.</summary>
		private static void ClearReconstructedOutputs(string rigSubfolder, string spritesSubfolder)
		{
			DeleteIfExists(Path.Combine(rigSubfolder, "rig.animsource.json"));
			DeleteIfExists(Path.Combine(rigSubfolder, "rig.pivots.json"));
			if (!Directory.Exists(spritesSubfolder))
			{
				return;
			}

			foreach (string png in Directory.GetFiles(spritesSubfolder, "*.png"))
			{
				File.Delete(png);
			}
		}

		/// <summary>Deletes a file when it exists.</summary>
		private static void DeleteIfExists(string path)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		/// <summary>Shipped exo on the combat prefab named <paramref name="model"/>, or an exo asset named after it.</summary>
		/// <remarks>
		/// Pack Exoskeletons PNGs are named after Model (Musketeer: BanditArcher.png), not MetaExo
		/// (HumanRanger). Ranger UVs on that 780x128 sheet crop the wrong cells. Returns the prefab's
		/// live ExoSkeletonData.asset — picks the child exo with the most clips when a prefab has more
		/// than one. Enemy exos are not top-level units-bundle keys, so reloading by asset.name failed
		/// and WritePlaceholderVisuals wrote the gray body sprite.
		/// </remarks>
		internal static bool TryResolveExoFromModel(string model, out ExoSkeletonDataAsset asset)
		{
			asset = null;
			if (string.IsNullOrEmpty(model))
			{
				return false;
			}

			AssetBundle bundle = AssetBundleManager.GetBundle("units");
			if (bundle == null)
			{
				bundle = AssetBundleManager.LoadAssetBundle("units");
			}

			if (bundle == null)
			{
				return false;
			}

			string key = model.ToLowerInvariant();
			GameObject prefab = bundle.LoadAsset<GameObject>(key);
			if (prefab != null)
			{
				ExoSkeletonDataAsset richest = null;
				int richestCount = -1;
				ExoSkeletonData[] data = prefab.GetComponentsInChildren<ExoSkeletonData>(true);
				if (data != null)
				{
					for (int i = 0; i < data.Length; i++)
					{
						ExoSkeletonDataAsset candidate = data[i] != null ? data[i].asset : null;
						if (!IsImportableExo(candidate))
						{
							continue;
						}

						int count = AnimationCount(candidate);
						if (count > richestCount)
						{
							richest = candidate;
							richestCount = count;
						}
					}
				}

				if (richest != null)
				{
					asset = richest;
					return true;
				}
			}

			string[] exoKeys =
			{
				"ExoSkeleton" + model + "_MetaDataAsset",
				"ExoSkeleton" + model
			};
			for (int i = 0; i < exoKeys.Length; i++)
			{
				ExoSkeletonDataAsset named = bundle.LoadAsset<ExoSkeletonDataAsset>(exoKeys[i].ToLowerInvariant());
				if (IsImportableExo(named))
				{
					asset = named;
					return true;
				}
			}

			return false;
		}

		/// <summary>True when an exo has parts and an atlas sprite to crop from.</summary>
		private static bool IsImportableExo(ExoSkeletonDataAsset asset)
		{
			return asset != null
				&& asset.parts != null && asset.parts.Count > 0
				&& asset.partSprites != null && asset.partSprites.Count > 0 && asset.partSprites[0] != null;
		}

		/// <summary>Vanilla atlas texture name for a shipped MetaExo, or false when the asset is missing.</summary>
		internal static bool TryVanillaAtlasTextureName(string metaExoName, out string textureName)
		{
			textureName = null;
			if (string.IsNullOrEmpty(metaExoName))
			{
				return false;
			}

			ExoSkeletonDataAsset asset = AssetBundleManager.LoadAsset<ExoSkeletonDataAsset>("units", metaExoName);
			if (asset == null || asset.partSprites == null || asset.partSprites.Count == 0 || asset.partSprites[0] == null
				|| asset.partSprites[0].texture == null)
			{
				return false;
			}

			textureName = asset.partSprites[0].texture.name;
			return !string.IsNullOrEmpty(textureName);
		}

		/// <summary>Loads a PNG from disk into a readable Texture2D.</summary>
		private static Texture2D LoadPng(string path)
		{
			byte[] data = File.ReadAllBytes(path);
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			return texture.LoadImage(data) ? texture : null;
		}

		/// <summary>Makes a possibly-non-CPU-readable texture readable by copying it through a RenderTexture (GPU blit + ReadPixels), which works regardless of the source's own readable flag.</summary>
		private static Texture2D MakeReadable(Texture2D source)
		{
			RenderTexture renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			Graphics.Blit(source, renderTexture);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = renderTexture;

			Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
			readable.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
			readable.Apply();

			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(renderTexture);
			return readable;
		}

		/// <summary>Copies the shared atlas and, when Unity split alpha off the RGB sheet, writes that alpha onto the copy.</summary>
		/// <remarks>Shipped unit atlases are often ETC/DXT RGB plus associatedAlphaSplitTexture. Blitting only the RGB sheet leaves packing-gap pixels opaque, so neighbor parts show up as edge lines.</remarks>
		private static Texture2D MakeReadableAtlas(Sprite atlasSprite)
		{
			Texture2D readable = MakeReadable(atlasSprite.texture);
			if (atlasSprite.associatedAlphaSplitTexture == null)
			{
				return readable;
			}

			Texture2D alpha = MakeReadable(atlasSprite.associatedAlphaSplitTexture);
			ApplyAlphaSplit(readable, alpha);
			UnityEngine.Object.Destroy(alpha);
			return readable;
		}

		/// <summary>Sets each color pixel's alpha from the split texture and clears RGB where alpha is 0.</summary>
		private static void ApplyAlphaSplit(Texture2D color, Texture2D alpha)
		{
			if (color == null || alpha == null)
			{
				return;
			}

			Texture2D alphaSource = alpha;
			Texture2D scaled = null;
			if (color.width != alpha.width || color.height != alpha.height)
			{
				scaled = ResizeTexture(alpha, color.width, color.height);
				alphaSource = scaled;
			}

			Color[] colors = color.GetPixels();
			Color[] alphas = alphaSource.GetPixels();
			int count = colors.Length < alphas.Length ? colors.Length : alphas.Length;
			for (int i = 0; i < count; i++)
			{
				float a = alphas[i].r;
				if (a <= 0.01f)
				{
					colors[i] = new Color(0f, 0f, 0f, 0f);
				}
				else
				{
					colors[i].a = a;
				}
			}

			color.SetPixels(colors);
			color.Apply();
			if (scaled != null)
			{
				UnityEngine.Object.Destroy(scaled);
			}
		}

		/// <summary>Crops one part using the baked UV quad ExoSkeletonRenderer samples, then resizes to the vertex box if needed.</summary>
		/// <remarks>
		/// Ranger parts are 4-vertex quads (see ExoSkeletonHumanRanger_MetaDataAsset.dump). The mesh UVs are the
		/// atlas region, not FindSprite(part.name).textureRect — that rect is Unity packer space and picked the
		/// wrong cell (Asst_Head03 cropped a torso). Edges of the UV box are rounded independently so the crop
		/// does not expand by a pixel into the next cell.
		/// </remarks>
		private static void ExportPartTexture(Texture2D atlas, Part part, string folder, int targetWidthPx, int targetHeightPx)
		{
			if (!TryUvCropRect(atlas, part, out int x, out int y, out int width, out int height))
			{
				return;
			}

			Color[] pixels = atlas.GetPixels(x, y, width, height);
			ClearFullyTransparentRgb(pixels);
			Texture2D cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
			cropped.SetPixels(pixels);
			cropped.Apply();

			Texture2D final = cropped;
			if (targetWidthPx != width || targetHeightPx != height)
			{
				final = ResizeTexture(cropped, targetWidthPx, targetHeightPx);
			}

			File.WriteAllBytes(Path.Combine(folder, part.name + ".png"), final.EncodeToPNG());
			UnityEngine.Object.Destroy(cropped);
			if (final != cropped)
			{
				UnityEngine.Object.Destroy(final);
			}
		}

		/// <summary>Pixel rect of part.uvs on the atlas (axis-aligned quad, same space GetPixels uses).</summary>
		private static bool TryUvCropRect(Texture2D atlas, Part part, out int x, out int y, out int width, out int height)
		{
			x = 0;
			y = 0;
			width = 0;
			height = 0;
			if (atlas == null || part.uvs == null || part.uvs.Length == 0)
			{
				return false;
			}

			float minU = float.MaxValue;
			float minV = float.MaxValue;
			float maxU = float.MinValue;
			float maxV = float.MinValue;
			foreach (Vector2 uv in part.uvs)
			{
				minU = Mathf.Min(minU, uv.x);
				maxU = Mathf.Max(maxU, uv.x);
				minV = Mathf.Min(minV, uv.y);
				maxV = Mathf.Max(maxV, uv.y);
			}

			x = Mathf.Clamp(Mathf.RoundToInt(minU * atlas.width), 0, atlas.width - 1);
			y = Mathf.Clamp(Mathf.RoundToInt(minV * atlas.height), 0, atlas.height - 1);
			int x1 = Mathf.Clamp(Mathf.RoundToInt(maxU * atlas.width), x + 1, atlas.width);
			int y1 = Mathf.Clamp(Mathf.RoundToInt(maxV * atlas.height), y + 1, atlas.height);
			width = x1 - x;
			height = y1 - y;
			return width > 0 && height > 0;
		}

		/// <summary>Drops leftover RGB on pixels that are already transparent so a later resize cannot smear neighbor colors.</summary>
		private static void ClearFullyTransparentRgb(Color[] pixels)
		{
			if (pixels == null)
			{
				return;
			}

			for (int i = 0; i < pixels.Length; i++)
			{
				if (pixels[i].a <= 0.01f)
				{
					pixels[i] = new Color(0f, 0f, 0f, 0f);
				}
			}
		}

		/// <summary>Resizes a texture via a RenderTexture blit.</summary>
		private static Texture2D ResizeTexture(Texture2D source, int width, int height)
		{
			width = Mathf.Max(1, width);
			height = Mathf.Max(1, height);

			RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			RenderTexture previous = RenderTexture.active;
			Graphics.Blit(source, renderTexture);
			RenderTexture.active = renderTexture;

			Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
			resized.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
			resized.Apply();

			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(renderTexture);
			return resized;
		}

		/// <summary>The pixel size implied by a part's vertex bounding box.</summary>
		private static (int width, int height) ComputeTargetPixelSize(Vector2[] vertices)
		{
			if (vertices == null || vertices.Length == 0)
			{
				return (1, 1);
			}
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			foreach (Vector2 v in vertices)
			{
				minX = Mathf.Min(minX, v.x);
				maxX = Mathf.Max(maxX, v.x);
				minY = Mathf.Min(minY, v.y);
				maxY = Mathf.Max(maxY, v.y);
			}
			int width = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * PixelsToUnits));
			int height = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * PixelsToUnits));
			return (width, height);
		}

		/// <summary>The average of a set of vertices.</summary>
		private static Vector2 Centroid(Vector2[] vertices)
		{
			Vector2 sum = Vector2.zero;
			foreach (Vector2 v in vertices)
			{
				sum += v;
			}
			return vertices.Length > 0 ? sum / vertices.Length : Vector2.zero;
		}

		/// <summary>Inverts a baked MatrixFlash back to the raw JSON six-tuple ReloadData's NewMatrix expects.</summary>
		private static void InvertBakedMatrix(
			MatrixFlash matrix,
			out float rawA,
			out float rawB,
			out float rawC,
			out float rawD,
			out float rawTx,
			out float rawTy)
		{
			rawA = matrix.a;
			rawB = -1f * matrix.b;
			rawC = -1f * matrix.c;
			rawD = matrix.d;
			rawTx = matrix.tx * PixelsToUnits;
			rawTy = -1f * matrix.ty * PixelsToUnits;
		}

		/// <summary>True when any frame of the clip already lists this exo event name.</summary>
		private static bool ClipHasEvent(Ironhide.ExoSkeleton.Animation animation, string eventName)
		{
			if (animation == null || animation.frames == null || string.IsNullOrEmpty(eventName))
			{
				return false;
			}

			for (int f = 0; f < animation.frames.Length; f++)
			{
				List<string> events = animation.frames[f].events;
				if (events == null)
				{
					continue;
				}

				for (int e = 0; e < events.Count; e++)
				{
					if (events[e] == eventName)
					{
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>Writes frame events and attach points, copying the baked lists then backfilling combat sockets.</summary>
		/// <remarks>
		/// AbilityMeleeActivity only fires OnAbilityAction / ends the activity on AbilityAction / AbilityEnd.
		/// Empty imported events play the clip and softlock the fight. Vanilla frames usually already have
		/// both; BanditArcher-style enemy exos sometimes do not, so Attack / SpecialAttack / SpellCast* and
		/// Death get the same first/last-frame backfill Animator Save uses. Head / Chest / Base are added
		/// when a frame has none of those names.
		/// </remarks>
		private static void AppendFrameEventsAndAttachPoints(
			StringBuilder json,
			AnimationFrame frame,
			string clipName,
			int frameIndex,
			int frameCount,
			bool clipHasAction,
			bool clipHasEnd,
			Vector2 headRaw,
			Vector2 chestRaw,
			Vector2 baseRaw)
		{
			List<string> events = new List<string>();
			if (frame != null && frame.events != null)
			{
				for (int e = 0; e < frame.events.Count; e++)
				{
					if (!string.IsNullOrEmpty(frame.events[e]) && !events.Contains(frame.events[e]))
					{
						events.Add(frame.events[e]);
					}
				}
			}

			if (frameIndex == 0
				&& CombatPlaybackRequirements.NeedsCombatEvents(clipName)
				&& !clipHasAction
				&& !events.Contains(CombatPlaybackRequirements.AbilityActionEvent))
			{
				events.Add(CombatPlaybackRequirements.AbilityActionEvent);
			}

			if (frameIndex == frameCount - 1
				&& CombatPlaybackRequirements.NeedsAbilityEndEvent(clipName)
				&& !clipHasEnd
				&& !events.Contains(CombatPlaybackRequirements.AbilityEndEvent))
			{
				events.Add(CombatPlaybackRequirements.AbilityEndEvent);
			}

			json.Append("],\"events\":[");
			for (int e = 0; e < events.Count; e++)
			{
				if (e > 0)
				{
					json.Append(",");
				}

				json.Append("\"").Append(TextEscaping.JsonEscape(events[e])).Append("\"");
			}

			json.Append("],\"attachPoints\":[");
			bool first = true;
			bool hasHead = false;
			bool hasChest = false;
			bool hasBase = false;
			int nextIndex = 0;
			if (frame != null && frame.attachPoints != null)
			{
				for (int i = 0; i < frame.attachPoints.Count; i++)
				{
					AttachPointDef point = frame.attachPoints[i];
					if (point == null || string.IsNullOrEmpty(point.name))
					{
						continue;
					}

					if (point.name == CombatPlaybackRequirements.AttachPointNames[0])
					{
						hasHead = true;
					}
					else if (point.name == CombatPlaybackRequirements.AttachPointNames[1])
					{
						hasChest = true;
					}
					else if (point.name == CombatPlaybackRequirements.AttachPointNames[2])
					{
						hasBase = true;
					}

					if (point.index >= nextIndex)
					{
						nextIndex = point.index + 1;
					}

					InvertBakedMatrix(point.matrix, out float rawA, out float rawB, out float rawC,
						out float rawD, out float rawTx, out float rawTy);
					if (!first)
					{
						json.Append(",");
					}

					first = false;
					AppendAttachPointJson(json, point.name, rawA, rawB, rawC, rawD, rawTx, rawTy, point.index);
				}
			}

			if (!hasHead)
			{
				if (!first)
				{
					json.Append(",");
				}

				first = false;
				AppendAttachPointJson(json, CombatPlaybackRequirements.AttachPointNames[0],
					1f, 0f, 0f, 1f, headRaw.x, headRaw.y, nextIndex++);
			}

			if (!hasChest)
			{
				if (!first)
				{
					json.Append(",");
				}

				first = false;
				AppendAttachPointJson(json, CombatPlaybackRequirements.AttachPointNames[1],
					1f, 0f, 0f, 1f, chestRaw.x, chestRaw.y, nextIndex++);
			}

			if (!hasBase)
			{
				if (!first)
				{
					json.Append(",");
				}

				AppendAttachPointJson(json, CombatPlaybackRequirements.AttachPointNames[2],
					1f, 0f, 0f, 1f, baseRaw.x, baseRaw.y, nextIndex);
			}

			json.Append("]}");
		}

		/// <summary>One ReloadData attach-point object.</summary>
		private static void AppendAttachPointJson(
			StringBuilder json,
			string name,
			float rawA,
			float rawB,
			float rawC,
			float rawD,
			float rawTx,
			float rawTy,
			int index)
		{
			json.Append("{\"name\":\"").Append(TextEscaping.JsonEscape(name)).Append("\",\"matrix\":[")
				.Append(F(rawA)).Append(",").Append(F(rawB)).Append(",").Append(F(rawC)).Append(",")
				.Append(F(rawD)).Append(",").Append(F(rawTx)).Append(",").Append(F(rawTy))
				.Append("],\"index\":").Append(index).Append("}");
		}

		/// <summary>Default Head / Chest / Base in raw JSON pixels, from the exo's rest vertices.</summary>
		private static void ComputeDefaultAttachRaw(
			ExoSkeletonDataAsset asset,
			out Vector2 headRaw,
			out Vector2 chestRaw,
			out Vector2 baseRaw)
		{
			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float minY = float.MaxValue;
			float maxY = float.MinValue;
			bool any = false;
			if (asset != null && asset.parts != null)
			{
				foreach (Part part in asset.parts)
				{
					if (part == null || part.vertices == null)
					{
						continue;
					}

					foreach (Vector2 vertex in part.vertices)
					{
						any = true;
						minX = Mathf.Min(minX, vertex.x);
						maxX = Mathf.Max(maxX, vertex.x);
						minY = Mathf.Min(minY, vertex.y);
						maxY = Mathf.Max(maxY, vertex.y);
					}
				}
			}

			if (!any)
			{
				headRaw = new Vector2(0f, -24f);
				chestRaw = new Vector2(0f, 0f);
				baseRaw = new Vector2(0f, 24f);
				return;
			}

			float midX = (minX + maxX) * 0.5f;
			headRaw = ToRawAttach(midX, Mathf.Lerp(minY, maxY, 0.9f));
			chestRaw = ToRawAttach(midX, Mathf.Lerp(minY, maxY, 0.45f));
			baseRaw = ToRawAttach(midX, minY);
		}

		/// <summary>World-space attach position to the raw JSON translation NewMatrix will invert.</summary>
		private static Vector2 ToRawAttach(float worldX, float worldY)
		{
			return new Vector2(worldX * PixelsToUnits, -1f * worldY * PixelsToUnits);
		}

		/// <summary>Formats a float for JSON output, trimming trailing zeros.</summary>
		private static string F(float value)
		{
			return value.ToString("0.######", CultureInfo.InvariantCulture);
		}
	}
}
