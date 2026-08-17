# LokrCharacterLab — Supporting Classes

Everything in `Editor/` besides `RigEditorScene.cs` itself (the
orchestrator, [`rig-editor-scene.md`](rig-editor-scene.md)) and the
animation-data-model files (`AnimationClip.cs`, easing, timeline —
[`animation-data-model.md`](animation-data-model.md)). All state still
lives on `RigEditorScene`; every class here either (a) is a
`MonoBehaviour` that exists only because Unity needs a live component for
`Update()`, or (b) is a `static class` UI panel that renders
`RigEditorScene` state and calls back into it — see
[`conventions.md`](conventions.md)'s "static-class-as-controller" rule.

## `Editor/DraggablePart.cs`

One `MonoBehaviour` per rig part (or duplicate instance — see
[`rig-editor-scene.md`](rig-editor-scene.md)'s "Duplicate part instances").

- `SortingOrder` (live, `SpriteRenderer.sortingOrder`, changes every
  `ApplyContextPoseToParts` call per `PartPose.RenderOrderIndex`) vs.
  `StaticLayer` (persistent single order, what Save/Scene Tree use) — two
  genuinely separate concepts kept as separate properties. Setting
  `SortingOrder` also pushes the same value onto whichever of the two
  mesh renderers below is currently active, so draw order stays correct
  regardless of which rendering path a part is on.
- `Visible` (persistent eye-toggle) vs. `FrameVisible` (per-frame
  inclusion) — both AND together in `UpdateRendererEnabled()`; kept
  separate so scrubbing frames never clobbers the user's own show/hide
  choice.
- `RotationDegrees`, `ScaleX`/`ScaleY`, `ShearDegrees`: four independent
  fields, all routed through `ApplyLiveTransform()`.
  - `ScaleX`/`ScaleY` are **independent per-axis** scale, not a single
    uniform `Scale` — `ScaleTool` sets both to the same value (the
    original uniform behavior), `ScaleXYTool` sets them independently.
    `RigEditorScene.ComputeFrameMatrix`/`DecodeFrameMatrix` treat
    `ScaleX == ScaleY` as just the degenerate uniform case, not a
    separately-tracked mode — no schema change needed (see
    `conventions.md`).
  - Clamped by **magnitude only** via `ClampMagnitude`, sign preserved —
    a negative axis scale is Unity's own standard mirroring convention
    (`Transform.localScale`, `SpriteRenderer` both handle it natively),
    and real shipped data legitimately mirrors one sprite for both limbs.
    Clamping the sign away instead of just the magnitude previously
    collapsed a mirrored part to a barely-visible 0.05-scale sliver
    rather than rendering it mirrored — traced from a real bug report
    ("part stretched very thin, arm missing compared to Preview").
  - `ShearDegrees`: authored only through `InspectorPanel`'s numeric
    field (no drag tool — shear has no natural drag gesture the way
    move/rotate/scale do). A part with `ShearDegrees == 0` stays on the
    plain `SpriteRenderer` + `Transform` path unchanged — zero behavior
    change for every part that isn't actually sheared.
- **Live shear rendering**: Unity's `Transform` has no shear component,
  so once `ShearDegrees` is non-zero, `ApplyLiveTransform` zeroes the
  Transform's own rotation/scale and instead rebuilds a child `Mesh`
  (`RebuildLiveShearMesh`) from the part's sprite quad with
  `AffineMatrixMath.ComposeLinear(rotation, shear, scaleX, scaleY, ...)`
  baked into the vertices directly, in local space (`Transform.position`
  still handles world placement normally). `Mathf.Abs(shearDegrees) <
  0.01f` reverting clears the mesh and falls back to the ordinary path.
- **Affine (raw-matrix) display**: `SetAffinePose(restPosition, mA, mB,
  mC, mD, translateX, translateY)` — for `Approximate` poses (see
  [`rig-editor-scene.md`](rig-editor-scene.md)'s "Approximate/shear
  detection"), the normal
  rotation+scale(+shear) path can't represent a genuinely degenerate
  source matrix, so this instead applies the exact matrix to the sprite
  quad's vertices directly in **world** space (this object's own
  transform reset to identity) — the same thing `ExoSkeletonRenderer`
  does for the real in-game mesh, scoped to one part. Read-only:
  `IsAffinePose` is checked by `EditorInputController`/each
  `IAnimatorTool` before allowing a drag, since there's no
  rotation/shear/scale combination that could represent an edit to a raw
  matrix `DecodeFrameMatrix` couldn't decompose. `ClearAffinePose()`
  reverts to whichever of the other two paths applies.
- `SetSelected(bool)`: drives `UpdateColor()` — `SelectedColor` (a warm
  yellow) vs. `NormalColor` (white), applied to whichever
  renderer/material is currently active (`SpriteRenderer` or one of the
  two mesh materials above). Called by `RigEditorScene` for both the
  single active selection and every member of a multi-selection (see
  below) — this class itself has no notion of "active" vs.
  "multi-selected", it only draws highlighted-or-not.
- No Unity mouse-event handlers remain on this class at all — no
  `OnMouseDown`/`OnMouseDrag`/`OnMouseOver`. A `BoxCollider` is still
  attached (added at spawn in `RigEditorScene.OnLoadClicked`), but it's
  read by `EditorInputController`'s own `Physics.RaycastAll`, not by
  Unity's per-object mouse callbacks — see "Selection model" below for
  why. (An earlier version reordered `StaticLayer` on scroll-wheel-while-
  hovering via `OnMouseOver`; removed per explicit request and not
  replaced — layer reorder today is exclusively the Scene
  Tree/Inspector's `+1`/`-1` buttons, i.e. `RigEditorScene.MovePartLayer`.)

## Selection model (`DraggablePart` + `SceneTreePanel` + `EditorInputController`)

Click-to-select in the viewport exists again, but it is **not** a
`DraggablePart.OnMouseDown` handler — imported rigs routinely stack many
overlapping parts at the same screen position, and Unity's per-collider
mouse events have no way to control which one "wins" when several
overlap. Instead:

- `EditorInputController.TrySelectUnderCursor()` (called from
  `TryBeginDrag()` only while `RigEditorScene.CurrentToolName ==
  SelectToolName`) does one `Physics.RaycastAll` against every part's
  `BoxCollider` and picks by **highest `SortingOrder`**, not
  closest-to-camera — every part sits at world `Z = 0`
  (`DraggablePart`'s transform never sets Z), so a plain nearest-hit
  raycast ties arbitrarily whenever parts overlap on screen. Picking by
  `SortingOrder` instead matches what's actually *visible* on top, which
  is what a click is supposed to mean. Hidden parts (`Visible`/
  `FrameVisible` false) keep their collider enabled but are skipped.
- Gated to Select-tool mode specifically: clicking with Move/Rotate/Scale/
  Scale XY/Pivot active starts a drag on the already-selected part
  instead (see `EditorInputController` below) — a click shouldn't
  reselect out from under an in-progress editing tool.
- **Multi-select**: Ctrl+click (either viewport click or a Scene Tree
  row) toggles that part's membership in `RigEditorScene.MultiSelection`
  via `ToggleMultiSelect`; Ctrl+A calls `SelectAllParts()`. A plain click
  (no Ctrl) replaces the whole selection with just that one part via the
  ordinary `SelectPart`.
- `SceneTreePanel` renders two distinct highlight tiers per row (see
  `UpdateRow`): the single **active** part (`RigEditorScene.SelectedPart`
  — the one Inspector shows / drag tools act on) gets the brightest
  color; every *other* member of `MultiSelection` gets a dimmer version
  of the same hue; a plain included part gets a faint neutral tint;
  excluded-from-frame gets a faint red tint — all computed once per row
  in `UpdateRow`, independent of each other.

## `Editor/ViewportGrid.cs`

`static class`, not a `MonoBehaviour` — builds the faint background
reference grid for a viewport once (`Build(parent, extent)`) rather than
redrawing it per frame, since it never needs to move: it's sized to
comfortably cover the full pan/zoom range `EditorInputController` allows
(`PanBoundsExtent` for Main, the equivalent re-centered box for Preview —
see below), so nothing needs to reposition or resize it as the user pans/
zooms.

Final approach: a hand-built `MeshRenderer` quad (`BuildTiledQuadMesh`)
whose UVs span `0..repeats` instead of the usual `0..1`, sampled against
a small tiling texture (`Sprites/Default` shader, `wrapMode = Repeat`) —
tiling happens by construction from the UV math, not from any Unity
tiling feature. This is the third approach tried, and the doc comment in
the file itself records why the first two failed:

1. **GL immediate-mode drawing** — dropped because it depends on camera
   render-callback timing/state (correct matrices, correct viewport at
   the point `OnPreRender` fires) that this game's runtime didn't
   reliably provide.
2. **`SpriteRenderer` with `SpriteDrawMode.Tiled`** — the same rendering
   path `DraggablePart` already uses, tried next for consistency. Failed
   because a runtime-created `Sprite` (via `Sprite.Create`, no packing/
   border metadata) doesn't actually tile in this game's build — it
   rendered as one stretched tile regardless of zoom, so only the
   separate axis bars (built with `Simple`-equivalent sizing, never
   tiled) were ever visible.
3. The final mesh approach sidesteps `SpriteDrawMode` entirely.
   `LineSpacing = 1f` world unit matches `RigEditorScene.PixelsToUnits`
   (1 world unit = 100px), so each grid square is a fixed, known
   sprite-pixel size to gauge a character against. The tile texture is
   generated at `TileTexturePixels = 64` with `mipChain = true` +
   `FilterMode.Bilinear` specifically because it gets tiled up to ~140
   times across the Main Viewport's own extent — zooming out shrinks
   each tile below a screen pixel, and a 1-texel line sampled with Point
   filtering and no mip chain aliases away to nothing between samples
   instead of fading out gracefully.

Two axis bars (`BuildAxisBar`, `SpriteDrawMode.Tiled` on a *solid* 1×1
sprite — solid colors tile trivially, so this particular failure mode
doesn't apply to them) are drawn separately at `sortingOrder = -31999`,
just above the grid's own `-32000` — both are far below any real part
(`DraggablePart.StaticLayer` sits at small near-zero values), guaranteeing
the grid is always background.

## `Editor/ReferenceCharacter.cs`

A whole-character scale overlay in the Main Viewport -- the answer to
"the grid tells me units, but not how big a real hero is." Loaded from a
shipped `ExoSkeletonDataAsset` via `AssetBundleManager.LoadAsset` (the
same lookup `CharacterImporter` uses), rendered with the same
`ExoSkeletonRenderer` path Preview uses, but parented at world origin so
the Main Viewport camera sees it. Default character is Gerald
(`ExoSkeletonHumanGeraldLightSeeker_MetaDataAsset`).

- One `BoxCollider` covers the posed mesh, so a click selects the
  character as a unit rather than an individual bone/sprite.
  `EditorInputController` prefers a visible part under the cursor over
  an overlay, since the overlay is a comparison guide sitting behind the
  work (`sortingOrder = -1000`, above the grid at `-32000` and below
  real parts).
- Move (W) and Rotate (E) act on the wrapper transform; Scale, Scale XY,
  and Pivot refuse with a status message -- resizing a known-size
  reference would defeat the point. `LateUpdate` also forces
  `localScale = 1` as a structural lock.
- Editor-only: never written to `rig.json`, not part of undo history.
  Position, rotation, character, pose (first frame of a chosen clip),
  visibility, and opacity are Inspector fields; `Add Reference` on the
  toolbar / Edit menu spawns one (staggered left of origin so it does
  not sit on top of the work) and selects it.
- `SceneTreePanel` lists overlays in a separate "References" section
  above the parts, hidden entirely when none are present.

## `Editor/EditorInputController.cs`

`MonoBehaviour` (needs a live `Update()`, unlike the static
`RigEditorScene`) — owns all raw input plumbing: mode-switch hotkeys,
click-to-select, drag dispatch, viewport pan/zoom, undo/redo/select-all,
and frame copy/paste/override/reorder shortcuts. Per-tool drag math itself lives in each
`IAnimatorTool` (`AnimatorToolRegistry`/`AnimatorTools.cs` — see
[`animation-data-model.md`](animation-data-model.md)); this class only owns *which* tool is active
and hands off `BeginDrag`/`ContinueDrag` calls to it.

- **Mode-switch hotkeys**: `Q` selects `RigEditorScene.SelectToolName`;
  every other registered `IAnimatorTool.Hotkey` (from
  `AnimatorToolRegistry.Tools` — today Move/Rotate/Scale/Scale XY/Pivot,
  see `ToolbarPanel` below) switches to that tool. Both guarded by
  `typingInField` (an `InputField` has `EventSystem.current.
  currentSelectedGameObject`), so typing a numeric value into e.g. the
  Inspector's Rotation field doesn't also fire a mode switch on any
  letter that happens to match a hotkey.
- **Escape**: `RigEditorScene.DeselectAll()`.
- **Ctrl+Z / Ctrl+Y / Ctrl+A / Ctrl+C / Ctrl+V / Ctrl+Shift+V**:
  `AnimatorHistory.Undo()`/`Redo()`, `RigEditorScene.SelectAllParts()`,
  and frame `CopyActiveFrame` / `PasteFrameAsNew` / `OverrideActiveFrame`
  — all live here now (not on `MenuBarPanel`/`ToolbarPanel`, which only
  expose the same actions as clickable buttons), same `typingInField`
  guard as the mode hotkeys so they cannot steal text-field copy.
- **`[` / `]`**: `MoveActiveFrame(-1)` / `MoveActiveFrame(1)`, also
  `typingInField`-guarded and skipped while Ctrl is held.
- **Click-to-select**: see "Selection model" above.
- **Drag** (`TryBeginDrag`/`ContinueDrag`, on left-mouse state via
  `Input.GetMouseButtonDown/Button/ButtonUp(0)`): driven globally against
  `RigEditorScene.SelectedPart`, not per-collider — works anywhere in the
  viewport regardless of what's under the cursor once a part is already
  selected, unlike the old `DraggablePart.OnMouseDown`/`OnMouseDrag` this
  replaced (which only fired while hovering that exact object).
  - Refuses to drag (with a status message, not a silent no-op) when the
    selected part `IsAffinePose` and the active tool doesn't set
    `AllowsAffinePose` — only `PivotTool` does, since it edits
    `RestPose.PivotOffset` rather than the pose itself.
  - **Group-drag**: when the active tool's `SupportsGroupDrag` is true
    (Move, Rotate, Scale, Scale XY, and Pivot) and
    `RigEditorScene.MultiSelection.Count > 1`, `TryBeginDrag` populates
    `dragGroup` and calls `tool.BeginGroupDrag`/`ContinueGroupDrag` **once,
    with the whole group**, instead of only dragging `SelectedPart` or
    looping the single-part `BeginDrag`/`ContinueDrag` per member. That
    distinction is exactly the fix for a real bug: an earlier version of
    this feature *did* just loop the single-part methods per member, which
    for Rotate/Scale span meant each part spun/resized in place around its
    own individual pivot with no regard for the other selected parts —
    multi-part rotate/scale visibly ignored the rest of the selection.
    `BeginGroupDrag`/`ContinueGroupDrag` fix this by computing one shared
    anchor for the whole selection (`RigEditorScene.GetGroupPivotWorld` —
    the session temp pivot when set, otherwise
    `AnimatorGroupMath.AveragePivotWorld`, frozen once at drag start) and
    treating the group as a single rigid formation around it: Move
    translates every part by the same delta (already correct without any
    shared-center math, since pure translation commutes); Rotate orbits
    every part's pivot around that shared center by the same angle delta
    while also spinning each part in place by that same delta; Scale/Scale
    XY move every part's pivot toward/away from that shared center by the
    same ratio while also resizing each part by that ratio — which is what
    keeps the group's relative proportions/spacing intact as a whole
    character grows or shrinks. See `RotateTool`'s own doc comment in
    `AnimatorTools.cs` for the full math. `PivotTool` group-drags the
    session temp pivot only; it does not rewrite each part's
    `RestPose.PivotOffset`.
  - One `AnimatorHistory.CaptureBeforeChange` per drag (group or single),
    not per-frame while held, described via `DragVerbForTool` (maps each
    tool's `Name` to the same past-tense verbs the Inspector's own direct
    edits use, e.g. `"Scale XY"` → `"scaled"`, `"Pivot"` → `"pivot
    moved"`) so history entries read consistently regardless of whether
    an edit came from a drag or a typed field.
  - `MouseButtonUp` commits immediately via
    `RigEditorScene.CommitCurrentPoseToActiveContext()` rather than
    waiting for the next lazy commit point — necessary because Mass Edit
    can leave playback running through a drag (see
    `RigEditorScene.PausePlayback`), and without an immediate commit the
    very next `TickPlayback` tick would reapply the frame's pre-drag pose
    right over the drag's result before it was ever saved. This is also
    what triggers Mass Edit's own propagation. If
    `PoseContextGeneration` moved since `TryBeginDrag` (timeline chip /
    clip click / `[` `]`), mouse-up skips that commit unless Mass Edit
    is on. `CancelActiveDrag` clears `isDragging` without committing;
    clip/frame switches call it after the old-context commit.
- **Main Viewport pan/zoom** (`HandleViewportPan`/`HandleViewportZoom`):
  middle-mouse-drag pans, scroll wheel zooms centered on the cursor (the
  world point under the mouse stays under the mouse — computed by
  comparing `ScreenToWorldPoint` before/after changing
  `orthographicSize`). Both gated to starting while the pointer is over
  `RigEditorScene.ActiveCamera.pixelRect` and not over UI, so they can't
  fight with any UI panel's own `ScrollRect` or be triggered by a
  middle-click elsewhere. `orthographicSize` is clamped to
  `[MinOrthoSize (0.2), MaxOrthoSize (30)]`; camera position is clamped
  by `ClampCameraPosition` to `±PanBoundsExtent (30)` on each axis,
  centered on **world origin** (`partsRoot` sits at world identity).
  `PanBoundsExtent`'s value is chosen with margin on both sides:
  comfortably larger than any realistic rig's extent, while its worst
  case combined with `MaxOrthoSize`'s own reach (`30 * ~0.73` aspect ≈ 22
  units past the camera center) still lands well short of `previewRoot`
  (100 units away — see [`architecture.md`](architecture.md)'s three-camera
  layout) — this bound is what actually guarantees the two viewports can
  never bleed into each other via pan, not just user restraint.
- **Preview viewport pan/zoom** (`HandlePreviewPan`/`HandlePreviewZoom`):
  mirrors the Main Viewport pair exactly (same `MinOrthoSize`/
  `MaxOrthoSize`, same `PanBoundsExtent` box shape), gated to
  `RigEditorScene.PreviewCamera.pixelRect` instead. Cannot reuse the Main
  Viewport's world-origin-relative bounds unchanged, since Preview's
  camera looks at `previewRoot`, itself offset 100 world units from
  origin — so `ClampPreviewCamera` instead re-centers the same box on
  `previewHomeCenter`, lazily captured the first time pan/zoom is used
  (`EnsurePreviewHomeInitialized`, seeded from wherever the camera
  started). Preview has no auto-fit/re-centering of its own — tried
  against `ExoSkeletonRenderer`'s mesh bounds and removed, per the code
  comment, because the computed bounds weren't reliable enough; manual
  pan/zoom alone worked better and is what ships.
- `IsPointerOverMainViewport`/`IsPointerOverPreviewViewport`/
  `IsPointerOverUI`: shared gating predicates used by every one of the
  above (click-to-select, both drags, all four pan/zoom handlers).

## `Editor/AnimationPlaybackController.cs`

Trivial `MonoBehaviour`: `Update()` calls
`RigEditorScene.TickPlayback(Time.deltaTime)`. Separate from
`EditorInputController` since it's about animation ticking, not input —
unchanged from before this rewrite.

## `Editor/SceneTreePanel.cs`

Renamed from `PartsListPanel` as part of the UI revamp (see
[`layout.md`](layout.md)), and moved to a narrower left-dock layout
(`EditorLayout.SceneTreeRegion` — see [`architecture.md`](architecture.md)
for the overall viewport/panel region model). Scope narrowed to match: this is now a
pure "what's here, what's its status, click (or Ctrl+click) to select"
surface — a Godot-Scene-Tree-style read+select list, not a property
editor. Per-property editing (layer reorder, visibility, transform) moved
to `InspectorPanel`, which is why each row today is just a name plus
status suffixes instead of the `+1`/`-1`/`H`/`S`/`+` button row it used to
carry.

- A real `ScrollRect` (imported rigs routinely have 30+ parts) with a
  thin always-visible scrollbar; `Content`'s height is set to
  `rowCount * RowHeight` in `Refresh()` so drag/wheel scrolling clamps
  correctly.
- **Rows are reused across `Refresh()` calls**, keyed by part name
  (`rowsByPartName`, updated in place — text/color/position, reordered
  via `SetSiblingIndex`) rather than destroyed and rebuilt every call, as
  an earlier version did unconditionally. `Refresh` runs on every
  playback frame tick (via `RigEditorScene.RefreshTimeline`/
  `TickPlayback`), and Unity's `Button` click needs the *same*
  `GameObject` alive across both pointer-down and pointer-up — with
  rebuild-from-scratch, a row could be destroyed out from under an
  in-progress click by an unrelated refresh landing between press and
  release, making "click a part while a clip is playing" unreliable.
  Reusing rows removes that race entirely.
- Row click: plain click → `RigEditorScene.SelectPart(part)`; Ctrl+held →
  `RigEditorScene.ToggleMultiSelect(part)` — same modifier convention as
  viewport click (see "Selection model" above).
- `UpdateRow`'s highlight tiers and status suffixes (`" (hidden)"`,
  `" (not in this frame)"`, `" (read-only pose)"`) are described under
  "Selection model" above and sourced respectively from
  `DraggablePart.Visible`, `RigEditorScene.IsPartIncludedInActiveFrame`,
  and `RigEditorScene.IsPartApproximateInActiveFrame` — each is ground
  truth from the actual animation data, so "is this genuinely excluded/
  read-only, or is something else wrong" never has to be guessed from the
  render alone.

## `Shell/LabShell.cs` (Phase 8 fight return + File Tree / workspaces / bottom panels)

Workspace tab strip under File / Edit / View / Help. `ActivateWorkspace`
fills the center dock + toolbar (`BuildViewport` / `BuildToolbar`) and
auto-focuses a bottom panel whose `isRelevant` matches. Sandbox Start
sandbox embeds a fight in the workspace hole. Home is not a tab.
`FocusPanel` selects any dock panel by id (`file-tree`, `timeline`, …);
`FocusBottomPanel` is the name-based wrapper used by View → Timeline /
Checklist / History. `Build` clears the active workspace so a post-fight
rebuild does not keep a stale tab with an empty viewport. Workspace tabs
rebuild when `CurrentSession.ProjectTypeId` changes (`UiToolbar.Clear`)
so Ability Library does not keep Character's Properties/Animator/Sandbox
strip. The same type-change pass rebuilds the bottom dock from
`type.BottomPanels` (`RemovePanel` + destroy, then add). Character keeps
Timeline / Checklist / History; a type with an empty list (Ability
Library) collapses the zone. `FirstWorkspaceName` is the fallback when
opening a project that is not Character (Project Browser → Ability
Library must land on Library, not Properties).

## `SandboxRoster.cs` / `SandboxFightControls.cs`

`SandboxRoster` is the shared hero + `BanditRaider` spawn used by
Character, Ability, and Encounter Sandbox embeds. `ResolveDefinitionAtLevel` walks
`nextLevelArchetype`. Each rank grants its passives plus the first
interactive option (vanilla picks one at random; the five-slot skills
bar cannot hold every variant). `SandboxFightControls` turns on
`Stage.TakeOverAICheat` before spawn (and clears `isAI` on units already
on the board) so the camera and hex input are not locked on an AI turn,
enables the debug panel, and forces the gameplay camera off cinematics.
Embed fights also drop vanilla `encounterLimits` pan clamps and raise
`inGameMinOrthoSize` / `inGameMaxOrthoSize` so the hole can wheel-zoom
(the fight camera has no zoom of its own). Stop captures those values
before unlock, restores hole ortho, puts FadeScreen back on Overlay, and
retags the lab backdrop as MainCamera so FadeScreen / transitionscene do
not inherit the zoomed size
([`sandbox-zoom-leaks-into-loading-ui.md`](../../../docs/issues/resolved/sandbox-zoom-leaks-into-loading-ui.md)).
Campaign fights stay bounded.

## `EmbeddedFightHost.cs`

Implements `LabHost.StartEmbeddedFight` / `StopEmbeddedFight`. Validates
the caster, enqueues `fighttesterempty`, then calls
`Host.StartEmbeddedScene` (LokrLab owns load, `Camera.rect`, HUD fit).
Spawns via `SandboxRoster` from a Harmony prefix on `Stage.StartFight`
(priority 600, before a LokrPatch StartFight replacement) while
`fightStarted` is still false, so empty `fighttesterempty` boards get
units `Add`-ed to initiative. `FightStartedEvent` still binds camera /
`OnReady` / `Finish`, and calls the same spawn helper if the once-flag
is clear. Stop unloads through
`StopEmbeddedScene`, resets `InterfaceDataRepository`, clears stacked
ephemeral quests, restores captured camera ortho / bounds, hides debug UI, resets camera drag, and sets
`Stage.instance = null`.
`SandboxFightControls.RecoverTurnChrome`, `EnsureFightHud`, and
`EnsureFightInput` run on each `FightStartTurn` (cinematic HideHud,
missed initiative portraits, walkable hex `Calculate`, and turn start).
`FightStartTurn` is raised at the end of the *current* unit's
`StartUserInteraction`, so after an AI turn it does not run for the
player. `EmbeddedFightStagePatch` postfixes `Stage.TurnStarted` and
`BeginPlayerTurnHandoff` retries HUD + `Calculate` + player
`StartUserInteraction` once end-turn tweens finish. See
[`play-ai-first-missing-walk-and-skills.md`](../../docs/issues/resolved/play-ai-first-missing-walk-and-skills.md).
Fight-end does **not** call `ReopenAfterFight`. `EmbeddedFightCameraPatches`
pans by right/middle-button drag inside the hole (unclamped) and
scroll-wheel zooms toward the cursor — no screen- or hole-edge scroll. `EmbeddedFightHexInputPatch` maps hole-camera taps to
hexes and skips the original `OnFingerTap` when the pointer is over a
skill / confirm `Icon` or `EndTurn`. `EmbeddedFightConfirmButtonPatch`
retargets `OnTap` to the live `UnitController`.
`EmbeddedFightConfirmCanvasPatch` (`TargetInteractionView.Awake`) and
`EnsureFightInput` both call `BindConfirmCanvases` so WorldSpace confirm
canvases use the hole camera. Forfeit Yes/No is vanilla
`UISimpleModalDialog`; the HUD fitter mutes `UIOptions` while that
dialog is visible (and still remaps its canvas into the hole). See
[`sandbox-forfeit-confirm-behind-settings.md`](../../docs/issues/unresolved/sandbox-forfeit-confirm-behind-settings.md). `EmbeddedFightStagePatch` guards
`Stage.Update` and related HUD during the embed. `SkillsBarSlotCap`
Harmony patches trim overflow past five hex slots. Every Sandbox
workspace uses this path.

## `Shell/NodeTreePanel.cs`

Logical project tree from the open type's contributors. Writes
`EditorSelection` on click. `SelectById` / `FindIdByDisplayName` are the
hooks File Tree uses to jump to a matching row.

## `Shell/FileTreePanel.cs`

Left-dock listing of `CurrentSession.FolderPath`. Root is the project
folder name; directories then files; hidden names (`.` prefix, `Thumbs.db`)
are skipped. Double-click a folder toggles expand; double-click a file
guesses a Node Tree id (`project.json`/`character.json` → Character,
`rig.json` → Rig, `sprites/<name>.png` → Part, portrait paths → Portraits,
else display-name match) and calls `NodeTreePanel.SelectById`. Drag-reparent
is off. `FileBrowserPanel` stays the modal picker for Save/Import/Atlas.

## `Editor/PropertiesCategoryHost.cs`

Persistent Properties category sections for the shell inspector. Built
once into InspectorDock's Grow() scroll host (no inner scroll — a nested
ScrollRect collapsed the form to zero height). `Show(name)` accepts a
registry `Name` or `DisplayLabel` and toggles visibility so
`PersistAndSync` refresh keeps working. Category nodes live under the
Character node (`PropertiesCategory` kind).

## `Editor/AnimatorWorkspace.cs` / `ViewportCameraBinder.cs` (RenderTexture slots)

Animator workspace: Main | Preview cameras in the center dock, tool
strip in the workspace toolbar. Reloads the rig only when that folder
is not already in the runtime. Timeline lives in the bottom dock
(`TimelineBottomPanel`). The center Viewport host is a stretch `RectTransform` (not a `UiStack`).
The edit camera fills that host via `Camera.rect`. LabBackdrop, the
shell root `UiPanel`, the dock space, and the Viewport panel Images
are cleared (`MakeSeeThrough`) so those cameras are not seen through
a 94% dark veil — child UI does not punch a hole in a parent Image. The in-engine preview
is a small bottom-right overlay (280×210, accent border only — the
interior is a transparent hole). Toolbar **Preview** and View → Preview
show/hide it. Hovering the preview overlay excludes the edit viewport
so pan/zoom do not drive both cameras. Node Tree multi-select of Part
nodes calls `SelectParts` so every selected part gets the yellow tint.
Timeline clip picks call `NodeTreePanel.SelectById("clip:" + name)`.
The live `InspectorPanel` is built into the shell inspector with
`BuildInto` (no nested titled panel or inner scroll).

## `Editor/TimelineBottomPanel.cs`

Composes `AnimationsPanel.BuildInto` + `AnimationTimelinePanel.BuildInto`
for the Timeline bottom tab. Clip buttons and the frame strip refresh
through the existing `RefreshTimeline` path once built.

## `Shell/InspectorDock.cs`

Shell Inspector (right dock). Rebuilds only when `EditorSelection`
identity changes (primary id/kind plus every selected id), then calls
the open project type's `FindInspectorDrawer` and
`FindInspectorSections` for that kind. Properties categories use a
Grow() scroll host plus `PropertiesCategoryHost` (same layout as the
live Animator `BuildInto` path). A missing drawer or unknown category
shows kind + name + id rather than a blank panel. Character's built-in drawers live
in `Projects/CharacterInspectorDrawers.cs` — identity summaries for
Character / Rig / Animator, and the Part / AnimationClip / Frame /
Reference port (name, `rig.json` offsets/frame count, Open
Properties/Animator). Live pose, pivot, events, and attach points stay
on `InspectorPanel` below so playback-tick row reuse is not rebuilt
from a drawer callback.

## `Editor/InspectorPanel.cs`

Godot-Inspector-style "select something, see and edit every property by
value" panel — right dock column (`EditorLayout.InspectorRegion`).
Dispatches on `LabNode.Kind` strings mapped from
`RigEditorScene.CurrentInspectorTarget` (`InspectorPanel.KindFromCurrentTarget`)
between four mutually-exclusive **built-in** sections that stay as
persistent widgets, only one of which is visible/laid out at a time.
`RegisterInspectorSection` contributions stack under the built-in
section and rebuild only when the inspected kind+id changes — never on
a playback-tick `Refresh`. The shell's `InspectorDock` uses the same
kind strings via `RegisterInspectorDrawer`. The four built-in sections:

- **Part**: general, frame-independent info for
  `RigEditorScene.SelectedPart` — name, `StaticLayer` (+1/-1), Visible
  toggle, Pivot (`RestPose.PivotOffset` — shared by every frame/clip of
  this part, which is why it lives here and not in the Frame section
  below), plus three action buttons:
  - **Replace...** opens `ReplacePartPickerPanel.Open(part)` (below).
  - **Remove from Clip** (`RigEditorScene.MassRemovePartFromClip`):
    clip-scoped, removes this part from every frame of the active clip;
    no-ops with a status message on Rest Pose.
  - **Center Selected** (`RigEditorScene.CenterSelectedParts`): only
    meaningful with more than one part multi-selected — greyed out via
    `Button.interactable` (not hidden) when
    `RigEditorScene.MultiSelection.Count <= 1`, so it's discoverable
    before the user already knows Ctrl+click/Ctrl+A exist, and its own
    label grows a live `"(N)"` count when active.
  Session-wide Mass Edit is not here — it is a global editing mode on
  `ToolbarPanel`, not a property of the inspected part.
- **Animation**: a real clip's name + frame count, a hint that root
  motion X is per frame, plus Delete Animation.
  Shown only when `CurrentInspectorTarget == Animation` **and**
  `RigEditorScene.ActiveClip != null`.
- **Frame**: shown for a real frame, or — reusing this same section,
  trimmed — for Rest Pose (`ActiveClip == null` while target is still
  `Animation`), since Rest Pose isn't a real clip and has no Duration/
  Easing/Events/Attach Points/Paste New/reorder/Delete/add-remove
  to show. Copy and Override stay available on Rest Pose (Paste New
  greys out). Rest Pose shows a hint: it is the default for new clips;
  later Rest Pose edits do not move Walk / Attack / other clips.
  `frameOnlyControlsRoot` (Duration/Easing/Events/Attach Points/reorder/
  Delete Frame) is hidden for Rest Pose.
  - **Root X (px)** (`RigEditorScene.GetActiveFrameRootMotionText` /
    `SetActiveFrameRootMotion`): cumulative unit-origin X in pixels for
    this frame. Blank clears the whole clip curve. Hidden with the rest
    of `frameOnlyControlsRoot` on Rest Pose.
  - **Events** is a `UiComboBox` of `CombatPlaybackRequirements.KnownEventNames`
    (`AbilityStart` / `AbilityAction` / `AbilityEnd`) plus Add, and a
    `UiList<string>` of the frame's current tags keyed by name so a
    Refresh during playback does not rebuild the row mid-click. The
    combobox stays typeable for custom `OnAbilityCustomEvent` strings.
    `AddActiveFrameEvent` / `RemoveActiveFrameEvent` replace the old
    comma-separated text field so picking one name cannot wipe extras
    already on the frame.
  - **Attach points** is the same pattern: a `UiComboBox` of
    `CombatPlaybackRequirements.AttachPointNames` (`Head` / `Chest` /
    `Base`, still typeable for custom socket names) plus Add, and a
    `UiList<AttachPointPose>` keyed by name. Each row shows the socket
    name, remove, and editable X/Y/Rot; playback Refresh updates field
    text in place (skipped while focused) so an in-progress edit is not
    overwritten. `AddActiveFrameAttachPoint` places Head/Chest/Base from
    the rest-pose bounding box; `SetActiveFrameAttachPoint` writes the
    row's fields.
  - **Copy / Paste New / Override** (`CopyActiveFrame` /
    `PasteFrameAsNew` / `OverrideActiveFrame`) sit above the clip-only
    **« / »** (`MoveActiveFrame`) / Delete Frame row. Copy and Override
    work on Rest Pose (copy snapshots rest; override writes poses onto
    rest and compensates clip deltas). Paste New still needs a real clip.
    Paste/Override grey out until a frame has been copied; « / » grey
    out at either end of the clip. Same actions live on
    `AnimationTimelinePanel`'s transport row and the Edit menu — see
    [`animation-data-model.md`](animation-data-model.md).
  - The **parts container** (both real-frame and Rest-Pose cases) lists
    every loaded part, expandable per row (`OnTogglePartExpanded`,
    state persisted in `expandedPartNames` across an in-place `Refresh`,
    cleared when the active clip/frame identity actually changes),
    reorderable (Up/Down arrows — `MovePartLayer` for Rest Pose since it
    has no per-frame order, `RigEditorScene.MoveFramePartOrder` for a
    real frame), with add/remove for a real frame only
    (`RigEditorScene.IncludePartInActiveFrame`/
    `RemovePartFromActiveFrame`). Sorted **descending** by effective
    draw order, front-most first (top of the list) — deliberately the
    opposite of `SceneTreePanel`'s ascending/back-to-front convention,
    per an explicit "top of container is the frontmost" request.
  - Rows are reused across `Refresh()` the same way `SceneTreePanel`'s
    are (`partRowStates`, keyed by part name) and for the identical
    reason — `RefreshTimeline` calls this on every playback tick via
    `InspectorPanel.Refresh`, and rebuilding from scratch made this
    container's own Up/Down reorder arrows unreliable to click for the
    same "row torn out mid-click" race. A row's subtree is only actually
    rebuilt when its *shape* changes (included vs. excluded, expanded vs.
    collapsed, approximate vs. not while expanded); an expanded row's
    live Pos/Rot/Shear/Scale field **text** is refreshed in place instead
    (`UpdateIncludedPartRowValues`), skipped per-field while that field
    `.isFocused` so an in-progress edit is never overwritten mid-type.
  - Field edits (`BuildPartFieldRows`) write through the same explicit-
    part `RigEditorScene.SetPartPosition`/`SetPartRotation`/
    `SetPartScale`/`SetPartShear` the drag tools use — so Undo/Redo and
    Save both keep working unchanged regardless of whether an edit came
    from a drag or a typed field. Fields are `Button.interactable =
    false` (not hidden) for an `Approximate` pose, alongside a "Read-only
    pose (degenerate matrix)" notice and a **Convert to Editable** button
    (`RigEditorScene.ConvertPartPoseToEditable`).
- **Reference**: a scale-reference overlay (`ReferenceCharacter`) --
  character (Choose... via `MetaExoPickerPanel`), pose dropdown, position,
  rotation, visible, opacity, and Remove. Scale is not a field; the
  overlay exists as a known in-game size. Shown when
  `CurrentInspectorTarget == Reference` and `SelectedReference != null`.

Selecting a part in the Scene Tree is the only thing that changes
`RigEditorScene.SelectedPart` (the viewport drag-tool target); clicking
an animation or a frame elsewhere only changes what *this panel* shows,
without touching that selection.

## `Editor/MenuBarPanel.cs`

Static dropdown/modal refs are cleared on lab close (`ResetSession`).
`EnsurePopups` rebuilds Save/Import/Slice Atlas modals when the C# wrapper
is still set but Unity already destroyed the GameObject (same fake-null
as EditHistory). See
[`lab-static-panels-not-reset-on-close.md`](../../../docs/issues/resolved/lab-static-panels-not-reset-on-close.md).

Very top strip: File / Edit / Help, each toggling a dropdown panel
directly below (`SetOnly` — only one open at a time) — a hand-built
"menu" (show/hide a panel), not a real dropdown widget, consistent with
the rest of this UI.

- **File**: Save..., Import..., Slice Atlas...,
  Refresh Preview. Every dropdown here is buttons-only — no inline
  fields mixed in — because an earlier version *did* embed the rig-
  folder/import/atlas fields directly in the dropdown and had to
  continually re-tune horizontal-space fractions to keep them from
  fighting each other. Save/Import instead open a small shared
  **single-field popup** (`OpenSingleFieldPopup` — one field, an
  optional side button, Confirm/Cancel; reused across both since
  only one can ever be open at once):
  - Save's side button is "Browse..." → `FileBrowserPanel.
    OpenForFolder`.
  - Import's side button is "Choose..." → `MetaExoPickerPanel.Open`
    (sourced from `CharacterAPI.KnownUnitDefinitions` — see below); the
    field itself still accepts a freely-typed metaExo id either way.
  - Slice Atlas gets its own dedicated popup (path + rows + cols — three
    fields don't fit the single-field shape), plus a "Pick Islands..."
    button that hands its same path field to `IslandAtlasPickerPanel`
    (a non-uniform atlas picker — see
    [`animation-data-model.md`](animation-data-model.md)) instead
    of the row/column grid path.
  - Refresh Preview needs no input, so it's a direct call to
    `RigEditorScene.RebuildPreview()`.
- **Edit**: Add Reference (`RigEditorScene.AddReference`, default Gerald),
  Copy Frame / Paste Frame as New / Override Frame / Move Frame Left /
  Move Frame Right (`CopyActiveFrame` / `PasteFrameAsNew` /
  `OverrideActiveFrame` / `MoveActiveFrame`), Undo, Redo
  (`AnimatorHistory.Undo`/`Redo`), History...
  (`EditHistoryPanel.Open()`).
- **Help**: static text pointing at this docs folder.

## `Editor/CombatSequenceNames.cs`

The clip names combat actually `FindAnimationIndex`s, keyed by
`CharacterProfile.Model` (the vanilla `units`-bundle prefab combat
instantiates as the view). Not a global HumanArcher list: ObeliskLvl4
looks up un-angled `SpecialAttack`; HumanGeraldLightSeeker looks up
`Attack` plus `SpecialAttackA/B/C`. `ForModel` is what Save backfills
and what `AnimatorReadinessChecks` warns for; `PresetsForModel` (that
list plus map-only `Portrait`) is what the Add Animation modal offers.
Save/Load drop leftover `Attack0`/`SpecialAttack45` clips that
`ForModel` does not ask for. Full account, including the dumped
per-prefab tables, is in
[`animation-data-model.md`](animation-data-model.md).

## `Editor/CombatPlaybackRequirements.cs`

`Head`/`Chest`/`Base` socket names (`AttachPointNames` — the Frame
inspector Attach points combobox) and `KnownEventNames`
(`AbilityStart`/`AbilityAction`/`AbilityEnd`). Combat needs those after
the custom rig is swapped onto the Model prefab. Save backfills missing
sockets from the rest-pose bounding box, missing Action/End events on
Attack/SpecialAttack/SpellCast clips, and missing `AbilityEnd` on
`Death` (`DeathActivity.Update` is empty, so without that event the
encounter freezes). Full account in
[`animation-data-model.md`](animation-data-model.md).

## `Editor/ToolbarPanel.cs`

The thin strip directly below `MenuBarPanel` — tool-mode buttons, the
**Mass Edit** toggle, **Add Reference**, the status label, and
History/Undo/Redo. Extracted out of `RigEditorScene.Build` for the same
reason every other panel already is: `RigEditorScene` owns the state
(`CurrentToolName`, `MassEditEnabled`, the status text itself), this
only renders it. Global editing modes belong here, not in
`InspectorPanel` (which is for the currently selected object).

- Mode buttons are **not** a hardcoded Select/Move/Rotate/Scale list —
  `Build()` calls `AnimatorToolRegistry.RegisterDefaults()` then iterates
  `AnimatorToolRegistry.Tools` to lay out one button per registered
  `IAnimatorTool` (today Move, Rotate, Scale, Scale XY, Pivot — see
  `AnimatorToolRegistry.RegisterDefaults`), alongside one fixed "Select
  (Q)" button that has no registered tool of its own. This is what lets
  `EditorInputController`'s own hotkey loop and `RigEditorScene.SetTool`
  stay generic instead of switch-statement-per-tool, and is the
  extensibility point the roadmap docs describe (a plugin can register a
  genuinely new tool the same way).
- **Mass Edit** (`UiToggle` after the tool-mode buttons) is the
  session-wide `RigEditorScene.MassEditEnabled` flag: committing a
  Move/Rotate/Scale of the current multi-selection also applies that
  relative change to every other frame of the active clip. `Refresh()`
  and `RefreshMassEditToggle()` keep the toggle in sync when Load
  resets the flag.
- `RefreshModeButtons()` recolors every button (`ModeActiveColor` vs.
  `ModeInactiveColor`) by comparing its tool name against
  `RigEditorScene.CurrentToolName`, via a `Dictionary<string, Image>`
  (`modeButtonImages`) captured at build time.
- History/Undo/Redo buttons call `EditHistoryPanel.Open`/
  `AnimatorHistory.Undo`/`Redo` directly — the same actions
  `EditorInputController`'s Ctrl+Z/Ctrl+Y and `MenuBarPanel`'s Edit menu
  expose, just as toolbar buttons too.
- Returns the status `Text` component to the caller — `RigEditorScene`
  keeps the reference itself (same as every other piece of editor state)
  so `SetStatus` can write to it directly; this class only builds it.

## `Editor/EditHistoryPanel.cs`

"History..." popup (opened from both `ToolbarPanel` and `MenuBarPanel`'s
Edit dropdown) — a thin UI view over `AnimatorHistory.GetHistoryView()`
(oldest-first, current entry flagged and highlighted in
`ToolbarPanel.ModeActiveColor`'s same warm accent). Same modal/row-list
shape `MetaExoPickerPanel`/`ReplacePartPickerPanel`
all use (`UiModal.Create` + a scrollable `UiStack`). Clicking a
row calls `AnimatorHistory.JumpTo(index)` and closes — this panel holds
no history logic of its own; see `AnimatorHistory` in
[`animation-data-model.md`](animation-data-model.md) for why `JumpTo`
can never diverge from a manual sequence of Undo/Redo calls.
`ResetSession` (0.12.30) nulls modal and dock refs from `OnLabClosing`.
`Fill` skips Unity fake-null widgets so Close Lab then Project Browser
open cannot `Visible()` a destroyed empty label.

## `Editor/CharacterListPanel.cs`

Load workstation: Create / Load Existing / Import, plus the recent-
characters list. Each row is the display name and opaque folder id
(`Onagro (1842…)`) with an **x** that calls
`HomeWorkstationScene.RemoveRecentCharacter` — it drops the path from
`recent.json` without loading or deleting the character. Labels come from
`CharacterProfileSidecar.Load` (`Name`, falling back to folder name).

## `Editor/General/LegacyPackScan.cs` / `LegacyModImporter.cs` / `LegacyModImportPanel.cs`

Official Pack / DNSpy import (0.12.13; File menu on the Project Browser
in 0.12.15). Scan lists heroes (one row per rank-up chain or extra
`RLHeroes` file), abilities, and summons (sibling blocks in one file are
separate rows). The selection sheet imports only checked rows. Heroes
reconstruct the exo the pack atlas is named after (Model /
`Exoskeletons/BanditArcher.png`), not `MetaExo` when those differ, into
`rig/` + `sprites/`, cropping each part from the baked `part.uvs` quad
(plus split alpha) and that PNG when present. Portraits, ability icons, and
localization copy across. Abilities mint `slug_token` folders (same as
create) and leftover pack keys become per-folder `$alias` on the
character and each ability (`defaultSkill` / skills write
`$assassin_lethal_strike`). The ability-library combo accepts an existing
library or a typed name (`musketeer-abilities`); a new name creates a
`slug_token` library instead of writing into the first existing one.
`Resources` / `new_heroes_lib` are skipped on a pack-root pick.
**File → Import Legacy Pack** is always on the Project Browser (no
character required). The folder picker starts in `Mods/`. Closing a
successful result opens the first imported hero as a Character project
(not the empty shell).

## `Editor/General/CharacterPlaceholders.cs`

Copies `BepInEx/plugins/LokrLab/Placeholders/` (`rig.json`,
`body.png`, `portrait.png`) onto a new character folder and assigns the
current Ability Lab placeholder ability ids (`AbilityPlaceholders.ResolveAbilityId`
for attack / skill / passives). Ability KV is not written here —
that lives in the suite `Placeholders/` ability templates. Called from
`HomeWorkstationScene.CreateCharacter`.

## `Editor/General/CharacterIdentityRekey.cs`

On Load, leftover `importedFromLegacyMod` characters whose folder is
still a display name (not a generated id) are rewritten onto
`GenerateNewCharacterId`: folder, UniqueId/MetaExo/roster, `UNIT_*`
keys, `portraits/`/`sounds/` files prefixed with the old id, and Ability
Lab `SpawnUnit` `UnitName` lines that pointed at the old block key
(rewritten to `$alias` when an alias is supplied).
Leftover 18-digit folders stay until the Character inspector **Rename**
button (`TryApplyToSlugToken`); a load does not silently move them.
New creates already mint `slug_token`.

## `Editor/RecentFilesStore.cs`

Pure persistence — reads/writes
`CharacterLabPaths.EditorDataRoot/recent.json`, a flat JSON array of
folder path strings, most-recent-first. Hand-built JSON string on
write, `SimpleJSON`'s `JSONNode` on read — the same low-ceremony style
`RigEditorScene`'s pivot-sidecar load/save already uses for one small
file that doesn't warrant its own schema/versioning. The Load
workstation (`CharacterListPanel`) is the only UI; there is no separate
`RecentFilesPanel` and the Animator's File menu does not list recents.

## `Editor/FileBrowserPanel.cs`

Thin Character Lab wrapper around SimpleUI's `UiFileBrowser` (1.2.0).
Save / Import / Atlas / portraits still call `OpenForFolder` /
`OpenForFile`; those open the shared Dolphin-style modal (Places,
breadcrumbs, details, preview, file operations). Extra Places include
this plugin's Characters folder and mod root. `EnsureBuilt` /
`ResetSession` map to `UiFileBrowser.EnsureModal` / `ReleaseModal`.

## `Editor/MetaExoPickerPanel.cs`

"Choose..." popup for the Import field's convenience picker. Sourced
**dynamically** from `CharacterAPI.KnownUnitDefinitions` — every
`UnitDefinition` the game has parsed so far this session, not a static
list — deduped by `metaExo` string (`seenMetaExo`) so a rig shared by
more than one unit id only appears once, labeled by the readable unit id
(`"Ranger"`) rather than the raw metaExo string
(`"ExoSkeletonHumanRanger_MetaDataAsset"`). Because it depends on
whatever's been parsed by boot time, an empty list is expected to be rare
in practice (the empty-state message says as much) rather than a bug —
and the underlying field stays freely typed regardless, so a metaExo id
missing from the roster can still be entered by hand.

## `Editor/ReplacePartPickerPanel.cs`

"Replace..." popup for `InspectorPanel`'s Part section
(`InspectorPanel.OnReplaceClicked` → `ReplacePartPickerPanel.
Open(SelectedPart)`). Lists every other currently-loaded part
(`RigEditorScene.LoadedParts`, excluding whichever part is being
replaced), same modal/row-list shape as the other pickers. Choosing one
calls `RigEditorScene.MassReplacePart(oldPart, newPart)` directly — this
panel holds no swap logic itself, purely a part-name chooser.

## UI construction (historical: `EditorUiHelpers.cs`)

Every panel in this editor — `MenuBarPanel`'s dropdown/popup internals,
`InspectorPanel`, `EditHistoryPanel`, `FileBrowserPanel`,
`MetaExoPickerPanel`, `ReplacePartPickerPanel`, and all the rest — builds
its rows with real `SimpleUI` widgets (`UiPanel`, `UiStack`, `UiModal`,
`UiLabel`, `UiButton`, `UiList<T>`). `Editor/EditorUiHelpers.cs` was a
second, parallel helper set (`AnchorTopLeft`, `CreateTopLeftText`/
`Field`/`Button`, `CreateScrollList`/`CreateHorizontalScrollList`,
`CreateModal`/`ShowModal`/`HideModal`) originally written for these same
panels; by the time of the pre-redesign audit (2026-08-13) every one of
them had already migrated onto `SimpleUI` instead, leaving
`EditorUiHelpers.cs` with zero real callers anywhere in the solution.
It, and `CharacterLabScene`'s equally-unused
`CreateLabel`/`CreateButton`/`CreateInputField` trio, were deleted at
that point (pre-redesign audit C-UI-01). See
[`architecture.md`](architecture.md)'s "UI construction — `SimpleUI`"
section for the pattern that replaced the pixel-anchored-controls half of
what `EditorUiHelpers.cs` did: a plain `UiLabel.Create`/`UiButton.Create`
with its `RectTransform` anchor set directly afterward, for the small
number of canvas-level chrome elements that genuinely need an absolute
anchor point rather than a panel's layout-group placement.
