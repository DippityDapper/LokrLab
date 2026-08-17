# LokrLab — `Editor/CharacterImporter.cs`

Reconstructs a `rig.json` + per-part PNGs from a real, already-shipped
`ExoSkeletonDataAsset` — the inverse of `RigEditorScene.OnSaveClicked`
(see [`rig-editor-scene.md`](rig-editor-scene.md)). Real characters never
ship their original authoring JSON (only final baked vertices/UVs/matrices
survive), so this derives a file that reproduces the same visual result
rather than recovering a literal original.

## `ImportInto(metaExoName, destFolder, reskinPngPath, out message)`

Same reconstruct as `Import`, but writes into an existing Lab character
folder (the leftover-id / `slug_token` folder) and optionally crops
parts from a pack `Exoskeletons/<name>.png` instead of the vanilla
atlas. Legacy pack import resolves the exo from that filename / Model
first (`TryResolveExoFromModel`) — Musketeer's `BanditArcher.png` is
the BanditArcher sheet (780x128), not a Ranger reskin (1044x252). A
reskin whose pixel size does not match the chosen exo still crops and
may need Animator work.

## `TryResolveExoFromModel(model, out exoName)`

Looks up the shipped exo on the `units` prefab named `model` (same
`FindPrefab` → `ExoSkeletonData.asset` path the game uses), then
`ExoSkeleton{model}_MetaDataAsset`. Returns the live asset — enemy
exos are not top-level bundle keys, so `LoadAsset` by `asset.name`
fails. Legacy import calls this with the pack PNG stem so
`BanditArcher.png` reconstructs BanditArcher, not the hero's `MetaExo`.

## `Import(metaExoName, out outputFolder, out message)`

Animator-only entry: writes under `Characters/<metaExoName>/` (no reskin)
and asks the user to Load.

1. Resolves the **richest** shipped exo: the Model prefab's
   `ExoSkeletonData.asset` when it has at least as many clips as the
   MetaDataAsset, otherwise `AssetBundleManager.LoadAsset` of the MetaExo
   id. Hero MetaDataAssets are map/UI poses (Ranger dump: Vanilla,
   Portrait, Stand, Victory, debug — five clips). Combat clips live on
   the combat prefab. Edit Vanilla Hero passes `CharacterProfile.Model`.
2. `MakeReadableAtlas` on `partSprites[0]` (GPU blit + `ReadPixels`,
   then `associatedAlphaSplitTexture` when Unity stored alpha on a
   second sheet). Without that alpha, packing-gap RGB is opaque and
   neighbor parts show up as edge lines.
3. Per part: offset = **centroid of `part.vertices`**. Crop uses the
   baked `part.uvs` quad — the same UVs `ExoSkeletonRenderer` samples
   (Ranger parts are 4-vertex quads; see
   `docs/reference/ExoSkeletonHumanRanger_MetaDataAsset.dump.txt`).
   `sprite.textureRect` is Unity packer space and does not match those
   UVs (it assigned the wrong cell: Head03 cropped a torso). Then
   **resize** to the vertex bounding box if the UV pixel size differs
   (scale compensation), so Load at scaleComp 1 matches the baked mesh.
4. Per frame: inverts `MatrixFlash` back to raw JSON values via the exact
   inverse of `NewMatrix`'s sign-flip/pixels-to-units conversion, writing
   one `"parts"` entry per `(renderOrder[i], matrices[i])` pair — including
   duplicate entries when the same part index appears more than once in a
   frame (this is exactly what feeds `RigEditorScene.LoadSavedRig`'s
   duplicate-instance handling — see
   [`rig-editor-scene.md`](rig-editor-scene.md)). Frame `"events"` and
   `"attachPoints"` are copied from the baked `AnimationFrame` (they are
   on the asset). Missing `AbilityAction` / `AbilityEnd` on
   Attack / SpecialAttack / SpellCast* / Death, and missing Head / Chest /
   Base sockets, are backfilled the same way Animator Save does — empty
   events used to play the clip and softlock `AbilityMeleeActivity`.

Output goes through the exact same Load path as any hand-authored rig
folder — nothing downstream needs to know an import happened.

File → **Edit Vanilla Hero…** copies unit defs, roster lock fields, loc,
and a reconstructed exo into a minted `slug_token` Lab folder
(`VanillaCharacterExtract`). UniqueId and block keys stay vanilla so
Loader last-wins replaces Gerald in place. Animator can edit the imported
clips. Use Import Character when you want to crop a pack reskin PNG onto
that exo instead of the vanilla atlas.
