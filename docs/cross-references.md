# LokrLab — Cross-references

Base-game/Unity behavior this editor's design was built against, confirmed
by reading decompiled source rather than guessed.

- **`UIMainScreen` vs. `UIMainMenu`** — `LokrLab`'s own
  `Patches/UIMainMenuPatches.cs` targets `UIMainScreen` (the title screen),
  not `UIMainMenu` (the deeper hub screen). `LokrEncyclopedia` has an
  identically-named file that targets the actually-different class
  `UIMainMenu` — see `../../LokrEncyclopedia/docs/cross-references.md` for
  that side of the naming collision.
- **`LocalizationComponent.Start()` timing** — runs one frame *after*
  `Instantiate` and unconditionally reapplies whatever `localizationKey`
  got copied from a cloned source object, silently stomping a naive text
  edit made immediately after `Instantiate`. Worked around in
  `Patches/UIMainMenuPatches.cs` by clearing `localizationKey` and calling
  the component's own `SetFinalText(...)` instead.
- **`ExoSkeletonRenderer.LateUpdate` has no null guard on
  `renderOrder`/`matrices`** (only on `exoSkeletonData.parts`) — an
  `ExoSkeletonData` that's never had `SetPose` called on it null-refs
  there every single frame. This is why `RigEditorScene.RefreshPreviewFrame`
  is deliberately *not* gated on `activeClip != null` — see
  [`rig-editor-scene.md`](character/rig-editor-scene.md).
- **`ExoSkeletonRenderer` draw order and z-depth both come from a
  frame's own `renderOrder` array position**, not from the rig's static
  `"parts"` list order — the two can legitimately differ frame to frame in
  real animation data (e.g. an arm drawn behind the torso in one pose, in
  front in another). This is the entire reason `PartPose.RenderOrderIndex`
  exists — see [`rig-editor-scene.md`](character/rig-editor-scene.md).
- **`ExoSkeletonRenderer` only reads `partSprites[0].texture` for the
  whole mesh** — every part in a rig must share one packed texture atlas.
  Same constraint documented in `../../LokrModAPI/docs/cross-references.md`
  (`TextureAtlasPacker`) and
  `../../LokrCharacterLoader/docs/custom-rig-loader.md`.
- **`ExoSkeletonAnimator.Start()` always calls `Play(name, true)`** —
  every clip loops in practice regardless of `loopsByDefault`, which is
  why `ExpandClipForSave`'s eased-transition generation treats the
  last-frame-to-first-frame transition as a real, easeable transition (see
  [`character/rig-editor-scene.md`](character/rig-editor-scene.md)).
  The Animator preview uses that same looping `Play` so the PIP keeps
  cycling the active clip while the editable viewport stays paused.
- **`ExoSkeletonAnimator` does not interpolate between frames** — it
  holds a frame for its `duration`, then jumps wholesale to the next. This
  shapes both the animation-clip data model (discrete keyframes, not
  curves) and the whole design of baked easing — see
  [`animation-data-model.md`](character/animation-data-model.md).
- **Required animation names** (`Stand`, and `Portrait`/`StandStatic`) —
  every base-game call site reading `Hero.exoSkeletonDataAsset` hardcodes
  one of these and throws an uncaught exception deep in game code if
  missing. `RigEditorScene.OnSaveClicked` auto-generates whichever the
  user hasn't authored so a rig saved from this editor can never be
  missing them; `RequiredAnimationNamesValidator`, registered into
  `AnimatorValidatorRegistry`, separately warns at Save time about
  anything *else* wrong with the authored set — see
  [`rig-editor-scene.md`](character/rig-editor-scene.md),
  [`animation-data-model.md`](character/animation-data-model.md) for the validator
  registry, and `../../LokrCharacterLoader/docs/custom-rig-loader.md` for
  the loader that validates this at runtime.
- **Combat clip names are per-Model, and a miss throws.** Combat
  instantiates `CharacterProfile.Model` from the `units` bundle, then
  `UnitViewExoSkeletonPatches` swaps the custom rig onto that prefab.
  Each `ExoSkeletonUnitAnimationController` looks up its `sequenceName`
  (not the ability's `AnimationID`, which is the *controller* name) via
  `FindAnimationIndex`; `StartAnimation` throws `Can't play animation
  <controller.name>` on a miss. Those sequence names differ by prefab:
  HumanArcher uses angled `Attack0`/`SpecialAttack0`/…; ObeliskLvl4 uses
  un-angled `SpecialAttack` for both its Attack and SpecialAttack
  controllers. `CombatSequenceNames.ForModel` is the dumped source of
  truth — see [`animation-data-model.md`](character/animation-data-model.md).
- **The Model prefab is the combat view template, not the art.** Combat
  cannot instantiate a made-up kind (`FindPrefab(unit.kind)` only finds
  vanilla `units`-bundle prefabs). `CharacterProfile.Model` picks that
  template (animation controllers, `AttachPointContainerExoSkeleton`,
  dialog config). `MetaExo` / `CustomRigLoader` is the art: 
  `UnitViewExoSkeletonPatches` swaps the custom `ExoSkeletonDataAsset`
  onto that prefab. The GameObject name stays `UNIT-…-ObeliskLvl4` even
  when the mesh is Onagro's rig. Attach points and frame events after the
  swap come from the custom asset's current pose, not from Obelisk's
  original sockets.
- **Attach points are exo-skeleton sockets, not ability KV.** Vanilla
  frames carry `Head`/`Chest`/`Base`. `unitPosition(%SOURCE, #Head)` calls
  `Unit.GetAttachPoint`, which reads
  `AttachPointContainerExoSkeleton` → `exoSkeletonData.attachPoints`. A
  miss returns `(0,0)` (projectile at origin) and logs `Attach point name:
  Head not found` from `DialogViewConfigComponent`. There is no separate
  "cast point" field on the ability.
- **`OnAbilityAction` waits on the clip event `AbilityAction`.**
  `AbilityMeleeActivity.HandleViewEvent` only runs the projectile when the
  exo-skeleton animator raises `"AbilityAction"`, and only ends the
  activity on `"AbilityEnd"`. Those strings live in `rig.json` frame
  `events`, not in the ability file. A clip with empty events plays and
  spends AP, then never fires.
- **`DeathActivity` waits on the same `AbilityEnd` with no timeout.**
  It `PlayAnimation("Death")` then only calls `DeathFinished` from
  `HandleViewEvent("AbilityEnd")`. `Update` is empty, so a Death clip
  that exists but has empty `events` (a rest-pose Save stub) freezes the
  encounter. Save backfills `AbilityEnd` on Death's last authored frame.
- **The static `"parts"` offset is translation-only**, baked once into a
  part's vertices with no rotation component, and a per-frame matrix's
  rotation happens around the **world origin**, not the part's own
  position — the reason `ComputeFrameMatrix`/`DecodeFrameMatrix` need a
  compensating-translation term at all. See
  [`rig-editor-scene.md`](character/rig-editor-scene.md).
- **`IndexOutOfRangeException` from a dying rig's `LateUpdate`** — an
  intermittent crash previously caused by `CharacterLabScene.Open()`
  creating a new scene before a prior `Close()`'s async unload had
  actually finished. Fixed by only clearing `activeLabScene` in the
  unload's `.completed` callback — see [`architecture.md`](architecture.md).

- **`AssetBundleManager.LoadScene(bundle, name, additive)`** — the embed
  path. Additive must not trigger `ForceClose`. Vanilla
  `AspectUtility.SetCamera` writes a full-screen `Camera.rect` and is
  skipped while an embed is active.
- **`TileTestController.ConstrainMap`** — Awake crops the template
  Tilemap to a serialized width/height and can enable `TilemapHack`.
  Encounter Setup paints past that rect on a grown board, so
  `EncounterTileConstrainPatch` skips the crop while the embed is an
  Encounter Show board or Sandbox, or a Character Sandbox-loaded Encounter. Campaign fights stay vanilla. Do not enable
  `TilemapHack`. Floor stamps clone a `HexaTile` already on the map so
  neighbor-masked `terrainData` stays valid.

## Neighboring plugins

- **`LokrLabApi`** — project-type contracts this plugin implements
  (Character type + File menu + `CurrentSession`). See
  [`../../LokrLabApi/docs/overview.md`](../../LokrLabApi/docs/overview.md).
- **`SimpleUI`** — widgets including the Phase 1 docking set used by
  `Shell/LabShell.cs`.
- **`LokrCharacterLoader`** — runtime load of whatever the Character
  project type writes. Not a rename of this plugin.
