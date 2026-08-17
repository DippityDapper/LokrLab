# LokrLab — `Editor/RigEditorScene.cs`

The largest file in the plugin; a `static class` that owns essentially all
editor state and orchestrates every other file. See
[`architecture.md`](architecture.md) for the state machine, viewport
layout, and commit/apply pose-context pattern this file implements. This
doc covers everything else: Load/Save (including the editor-only sidecar
files), selection, Mass Edit, the easing/baking pipeline, Undo/Redo's hook
into this file, Preview, and the matrix math underneath all of it.

## Load

`OnLoadClicked(folder)`: clears `AnimatorHistory` (a freshly loaded rig
shares nothing with whatever was undoable before it — see
`AnimatorHistory.Clear`'s own doc comment), destroys any previously loaded
parts, reads every `*.png` in the folder field, calls `LoadSavedRig` to
parse an existing `rig.json` (plus its sidecar files — see below) if
present, creates one `DraggablePart` GameObject per PNG (`SpriteRenderer` +
`BoxCollider` + `DraggablePart`), restores each part's `StaticLayer` from
the saved "parts" array order (or appends new parts after it), then spawns
extra duplicate-instance `DraggablePart`s for any part `LoadSavedRig` found
drawn more than once in a single frame (see "Duplicate part instances"
below), and finally auto-builds Preview (`RebuildPreview()`).
`CurrentFolder` defaults to `CharacterLabPaths.CharactersRoot`
(`Mods/LokrLab/LokrCharacterLab`). The Load workstation's recent list
(`CharacterListPanel` / `RecentFilesStore`) is a separate concern — which
character folders were opened, not which folder the Animator's file
browser last pointed at.

`LoadSavedRig(folder, outRestPoses, outClips, outPartOrder, outMaxOccurrenceByBaseName, out approximatePoseCount)`:
parses `rig.json` (plus `rig.pivots.json`/`rig.animsource.json`, see below)
into the in-memory model.

- The static `"parts"` array → `RestPose` per part name (world units,
  `offsetX/PixelsToUnits`, `-offsetY/PixelsToUnits`).
- Each animation → an `AnimationClip` of `PoseFrame`s, either reconstructed
  directly from `rig.animsource.json` (if that clip has a sidecar entry) or
  parsed from `rig.json`'s own flat frame array via `DecodeFrameMatrix` —
  see "Sidecar files" and "Easing and baked frames" below for why these are
  two different paths that must agree.
- **Duplicate part instances**: the real format allows the same named
  part to appear more than once in a single frame's array with different
  transforms (e.g. one arm sprite reused, mirrored, for both limbs) — a
  real, observed case in shipped rig data. Since everything else in this
  editor is keyed by a single "part name" string
  (`Dictionary<string, PartPose>`, one `DraggablePart` per name), extra
  occurrences beyond the first get a synthetic name —
  `"Asst_Arm01 (copy 2)"` — via `DuplicateName`/`BaseName` helpers, and
  are otherwise treated as fully independent parts sharing a texture (and
  initially a rest position) with their base. `outMaxOccurrenceByBaseName`
  reports, per base name, the highest occurrence count seen anywhere in
  the rig, so `OnLoadClicked` knows how many extra `DraggablePart`s to
  create. Both the sidecar-sourced path and the flat-JSON-decode path have
  to maintain this map identically, since a duplicate instance might only
  ever appear inside a sidecar-reconstructed clip.
- **Per-frame exclusion**: after every clip/frame is parsed, a second pass
  marks any known part-slot (base name or known duplicate occurrence) that
  a given frame's data never mentioned as `PartPose.Included = false`,
  deferred to a second pass specifically because a duplicate's existence —
  and thus the full set of slots every frame must be checked against —
  isn't known until every frame has been scanned once.
- **Rotation/shear/scale decomposition, and when a pose is `Approximate`**:
  `DecodeFrameMatrix` performs a full rotation→shear→scale decomposition
  of a frame's 2×2 linear matrix (the same model CSS/SVG use to decompose
  `matrix()`), via Gram-Schmidt on the matrix's two columns — see
  "Matrix encode/decode" below. This recovers `ScaleX`/`ScaleY` and
  `ShearDegrees` **exactly** for any invertible matrix, including
  non-uniform scale and real shear (a stretched cape, a mirrored limb via
  negative scale). `PartPose.Approximate` is now only set for a genuinely
  degenerate/near-singular source matrix (near-zero scale on the matrix's
  first column, where rotation itself is undefined) — a rare, likely
  malformed case — not the common "any shear at all" case it used to be.
  When it does happen, the exact raw matrix components are stored in
  `RawA..RawTranslateY` for lossless display/round-trip (see
  `DraggablePart.SetAffinePose` in
  [`supporting-classes.md`](supporting-classes.md)) and every drag tool
  except `PivotTool` refuses to touch that pose (see `IAnimatorTool.AllowsAffinePose`
  in [`architecture.md`](architecture.md)).
- `RenderOrderIndex`: a part's position within a *specific frame's* own
  `"parts"` array (its real per-frame draw order — see "Matrix
  encode/decode"), recorded per `PartPose` so playback/Save can reproduce
  it exactly rather than always falling back to the rig's one static
  order.

## Session reset

`ResetSession()` (added 2026-08-12, pre-redesign audit C-03) is a
distinct, narrower cousin of `OnLoadClicked`'s own destroy-and-clear
block — it clears every session field (loaded parts, selection, clips,
rest poses, active clip/frame/playback state, and scale-reference
overlays) back to empty, destroying any still-alive `DraggablePart`/
`ReferenceCharacter` GameObjects along the way, and clears
`AnimatorHistory`. Called from the very start of `Build()` and from
`CharacterLabScene.CloseTo`/`ForceClose` — all three represent the whole
lab scene being (or about to be) destroyed, which is why this also
discards reference overlays, unlike `OnLoadClicked`.

The bug this closes: `Build()`'s own last line is `RefreshTimeline()`
(see below), but before this fix nothing cleared `loadedParts`/`clips`/
etc. between a `CloseTo` and the next `Build()` — those C# static fields
survive a scene teardown even though the `DraggablePart` GameObjects they
reference don't, since this is a `static class`, not scene state. A
second `Build()` call (opening the Animator again after closing and
reopening the lab) would run `RefreshTimeline()` against those stale,
destroyed references before `OnLoadClicked` ever got a chance to clear
them — `SceneTreePanel.Refresh`/`InspectorPanel.Refresh` reading a
destroyed Unity Object's own members throws
`MissingReferenceException`. `ResetSession()` at `Build()`'s own start
closes that window entirely.

`OnLoadClicked` deliberately keeps its own separate clear block rather
than calling `ResetSession()` — same fields, minus reference overlays,
since switching to a different character folder within one still-live
session shouldn't discard those the way an actual scene teardown does.
It does now also clear `MultiSelection`/the actively-dragging group,
which it didn't before — the identical staleness bug, just triggered by
a same-session folder switch instead of a scene teardown.

## Sidecar files: custom pivots and authored animation source

`rig.json` itself is the real game's flat, baked schema — it has no
concept of a per-part pivot, and no concept of "these N frames were one
authored keyframe with an eased transition" versus "N separately
hand-placed frames." Two editor-only JSON files, written next to
`rig.json` and read back on the next Load, close those gaps without
touching the schema the game actually understands (see
[`conventions.md`](conventions.md)'s "no schema changes, ever").

- **`rig.pivots.json`** (`LoadPivotsSidecar`/`SavePivotsSidecar`) — one
  `{name, x, y}` entry per part with a non-default `RestPose.PivotOffset`.
  A part rotates/scales/shears around this point rather than its rendered
  center (see `ComputeCenterOffsetFromPivot`/`SetPartTransform` and the
  Matrix section below); `ComputeFrameMatrix` folds the chosen pivot
  straight into the exported matrix, so a rig using a custom pivot loads
  and renders correctly in-game with **zero** engine-side changes — this
  editor still needs the pivot value itself, though, to correctly decode a
  saved matrix back into `deltaPosition`/rotation/shear/scale on the next
  Load (`DecodeFrameMatrix` takes the pivot as an input). Only parts with
  an actual custom pivot are written; the file is deleted (not left behind
  empty) once every pivot is reset back to `(0, 0)`.
- **`rig.animsource.json`** (`LoadAnimationSourceSidecar`/
  `SaveAnimationSourceSidecar`) — the exact **authored** frame list per
  clip (`Duration`/`Easing`/`EasingSteps` plus every `PartPose`/
  `AttachPointPose`, keyed by clip name), written before baking expands one
  authored frame into however many generated sub-frames its transition
  needs. This exists to fix a real, previously-shipped data-loss bug:
  without it, `LoadSavedRig` has no way to tell "these 6 consecutive
  frames in `rig.json` were one authored frame eased over
  `EasingSteps=5`" apart from "these were 6 separately hand-placed
  frames" — every baked sub-frame would silently become its own new
  authored frame the moment the rig was reloaded, permanently flattening
  and losing the original easing grouping on every Save→Load round-trip.
  A clip name absent from the sidecar (an externally-authored rig, or one
  saved before this sidecar existed) falls back to the original flat-parse
  path for that clip only — every other clip's authored grouping is
  reconstructed exactly as saved. Optional `"rootMotion"` (per-authored-
  frame cumulative X) round-trips with the clip; if absent, Load
  downsamples `rig.json` `rootMotions` onto frame-start times. Only clips
  the user actually authored are written; `OnSaveClicked`'s auto-generated
  `Stand`/`StandStatic` fallback clips (see "Save" below) are always a
  single all-zero-delta frame with no easing, so they round-trip losslessly
  through the legacy path anyway and don't need an entry.

This sidecar is the Save/Load-time half of the fix; the complementary
in-memory half — never letting a *displayed* baked sub-frame get written
back into an authored frame's own data in the first place — is covered in
"Easing and baked frames" below.

## Selection, visibility, draw order

Click-to-select in the viewport is back, scoped to **Select mode only**
(`EditorInputController.TrySelectUnderCursor` — see
[`supporting-classes.md`](supporting-classes.md)): a raycast against every
part's `BoxCollider` (and any scale-reference overlay), picking whichever
hit part has the highest `SortingOrder` (i.e. whatever's actually drawn
on top), skipping parts that are hidden (`!Visible || !FrameVisible`). A
visible part under the cursor always wins over a `ReferenceCharacter`
overlay. It coexists with the Scene Tree's own row clicks —
`SceneTreePanel` rows call the same selection API — rather than replacing
them.

- **Multi-select.** `selectedPart` (`RigEditorScene.SelectedPart`) is
  still exactly "the one active part" — what the Inspector's Part section
  shows, what the pivot handle tracks, what a single-part drag tool acts
  on — but it's now always a member of a parallel `multiSelection` set
  (`RigEditorScene.MultiSelection`, `IsPartMultiSelected`). `SelectPart`
  resets the set to just the clicked part; `ToggleMultiSelect` (Ctrl+click,
  in both the viewport and the Scene Tree) adds/removes a part from the
  set without ever letting it drop to empty on a removal — removing the
  active part promotes whichever part remains instead. `SelectAllParts`
  (Ctrl+A) selects every loaded part regardless of whether it's included
  in the active frame, since the motivating use case (`CenterSelectedParts`,
  the Inspector's "Center Selected" button) should move frame-excluded
  parts' Rest Pose positions too. `ApplySelection` is the single place
  that reconciles `multiSelection`/`selectedPart` against each
  `DraggablePart.SetSelected` highlight, diffing old vs. new membership so
  only parts whose selected-ness actually changed get re-tinted.
- **Group dragging: Move, Rotate, Scale, Scale XY, and Pivot (temp
  group pivot).** `EditorInputController.TryBeginDrag` starts a *group*
  drag (tracked via `activelyDraggingGroup`) whenever the active tool's
  `IAnimatorTool.SupportsGroupDrag` is true and more than one part is
  multi-selected — calling the tool's `BeginGroupDrag`/`ContinueGroupDrag`
  with the whole `multiSelection` at once, **not** by looping the
  single-part `BeginDrag`/`ContinueDrag` over each member. That distinction
  matters: for Move, translating every part by the same delta already is
  a correct rigid-group move, so its group methods just apply the
  single-part logic per member. For Rotate/Scale/Scale XY, a real group
  operation needs one shared anchor for the *whole* selection —
  `RigEditorScene.GetGroupPivotWorld` (the session temp pivot when set,
  otherwise `AnimatorGroupMath.AveragePivotWorld`, frozen once at drag
  start) — so the group orbits/scales around that one shared point as a
  rigid formation (each part's own pivot moves relative to the shared
  center, its own rotation/scale also changes by the same delta/ratio)
  instead of each part spinning or resizing in place around its own
  individual pivot with no repositioning (which is what naively looping
  the single-part methods produces — an earlier version of this feature
  did exactly that, and it's why multi-part rotate/scale used to visibly
  ignore the other selected parts' positions). See `RotateTool`'s own
  doc comment in `AnimatorTools.cs` for the full math. `PivotTool`
  group-drags the session temp pivot only (`SetTemporaryGroupPivotWorld`);
  it does not rewrite each part's `RestPose.PivotOffset`. Membership
  change in `ApplySelection` resets that temp pivot to the selection
  centroid. `CenterSelectedParts` (Inspector's "Center Selected") is the
  multi-part analogue for repositioning: it moves the whole
  `multiSelection` so its centroid lands on world origin in one step (one
  `CaptureBeforeChange`/`CommitCurrentPoseToActiveContext` pair, not one
  per part), preserving every part's position relative to the others.
- `ToggleVisibility`: the persistent eye-toggle (`DraggablePart.Visible`),
  independent of any specific frame.
- `IncludePartInActiveFrame(part)`: gives a part excluded from the active
  frame (`Included = false`) a real rest-pose entry in that frame instead
  — makes it visible *and* editable, covering "add a part to an animation
  that didn't originally include it." The ordinary Visible eye-toggle
  can't do this on its own since `FrameVisible` always overrides it.
- `MovePartLayer`/`NormalizeLayers`: adjust `DraggablePart.StaticLayer`,
  the persistent single per-part draw order used by Save and the Scene
  Tree (distinct from the live per-frame `SortingOrder` — see
  [`supporting-classes.md`](supporting-classes.md)).
- `MoveFramePartOrder`: the per-*frame* analogue of `MovePartLayer` —
  reorders `PartPose.RenderOrderIndex` within just the active frame's
  included parts (via `NormalizeFrameRenderOrder`, which assigns every
  included part a concrete index before the swap so two parts both still
  saying "use the static layer" don't swap meaninglessly). Only meaningful
  for a real clip frame; Rest Pose has no `RenderOrderIndex` concept and
  reorders via `MovePartLayer` directly.

## Mass Edit and part-level rig surgery

See [`architecture.md`](architecture.md)'s Mass Edit section for how the
`MassEditEnabled` toggle (on `ToolbarPanel`, not the Inspector) changes
`CommitCurrentPoseToActiveContext`/`PausePlayback`'s behavior. While on,
a committed group Move/Rotate/Scale of `MultiSelection` propagates each
selected part's relative change to every other frame of the active clip
— not only `SelectedPart`. Two related, coarser operations live here too:

- `MassRemovePartFromClip(part)`: sets `Included = false` for `part` in
  **every** frame of the active clip in one step (one history entry),
  using the same convention `RemovePartFromActiveFrame` uses for a single
  frame. Scoped to the active clip only — not every clip, not Rest Pose —
  matching Mass Edit's own "this animation I'm working on" scope, not a
  rig-wide structural change.
- `MassReplacePart(oldPart, newPart)`: a rig-wide part swap (driven by
  `ReplacePartPickerPanel` — see
  [`supporting-classes.md`](supporting-classes.md)) — copies `oldPart`'s
  `RestPose` and `StaticLayer` onto `newPart`, then walks **every** clip
  (deliberately not scoped to the active clip, unlike Mass Edit proper —
  a replacement is a rig-wide structural correction) cloning `oldPart`'s
  pose into `newPart` wherever `oldPart` was actually included, and
  excluding `oldPart` from that same frame — a clean swap, not an
  add-alongside.

## Pose application: authored vs. baked

Two methods apply a `PoseFrame`'s data to the live `DraggablePart`
transforms, and keeping them distinct is what makes the baking
architecture below safe:

- **`ApplyContextPoseToParts()`** (internal entry point, called from every
  context switch) is the *authoritative, editing-anchored* path — always
  driven by the active context's own **authored** data (Rest Pose, or
  `activeClip.PoseFrames[activeFrameIndex]`), never by a baked sub-frame.
  It rebakes every clip first (`RebakeAllClips` — see "Easing and baked
  frames" below) so whatever it's about to apply (and whatever playback/
  Preview/Save read afterward) is guaranteed fresh, then delegates to
  `ApplyPoseFrameToParts`.
- **`ApplyPoseFrameToParts(frame)`** (private) does the actual per-part
  work — `FrameVisible`, `SortingOrder` (from `PartPose.RenderOrderIndex`
  when the frame recorded one, else `DraggablePart.StaticLayer`), and
  routes to `DraggablePart.SetAffinePose` instead of the normal
  `SetPartTransform` when `pose.Approximate` is true — for **whatever
  frame it's handed**, authored or baked. Playback (`TickPlayback`) and
  the frame strip's "ghost" sub-chip scrubbing (`ScrubToBakedFrame`) call
  this directly with a specific *baked* sub-frame, deliberately bypassing
  `ApplyContextPoseToParts` — see "Easing and baked frames" for why that
  split is the whole point.

Every part actively being dragged is skipped by both (see
[`architecture.md`](architecture.md)'s commit/apply section).

## Clip/frame editing, playback

`CreateNewClip`/`DeleteActiveClip`/`AddFrame` (duplicates the current
frame's poses, attach points, duration, and easing, but not Events — an
event is a one-shot trigger, and duplicating a frame that has one would
fire it twice)/`DeleteActiveFrame`/`SetActiveFrameDuration`.

**Frame clipboard and reorder.** `CopyActiveFrame` deep-clones the active
frame (poses, attach points, duration, easing, *and* events) onto a
session clipboard via `RigSnapshotCloner.Clone` — the same cloner undo
uses, so Included/Approximate/raw-matrix/draw-order come along, and
`BakedFrames` do not (they rebake on the next `RefreshTimeline`). The
clipboard is independent of which clip is active, so a frame copied from
Walk can be pasted into Run; it is cleared on Load because it belongs to
the previous character. `CopyActiveFrame` also works on Rest Pose: it
snapshots current rest as zero-delta poses. `PasteFrameAsNew` inserts a
fresh clone after the current frame (additive, same slot `AddFrame`
uses) and still no-ops on Rest Pose. `OverrideActiveFrame` replaces the
current clip frame in place, or — on Rest Pose — writes clipboard
transforms onto rest and compensates clip deltas so Walk/Attack keep
their world positions (`PivotOffset` is not rewritten).
`MoveActiveFrame(delta)` swaps the current frame one slot earlier or later
and keeps it selected; it clamps at either end rather than wrapping, so
mashing « cannot loop a frame from the start to the end. Undo/Redo covers
paste, override, and move (copy itself does not mutate the rig).

`TogglePlayback`/`TickPlayback(deltaTime)` — driven every `Update()` by
`AnimationPlaybackController` (see
[`supporting-classes.md`](supporting-classes.md)) — step through the
active clip. `TickPlayback` walks `PoseFrame.BakedFrames`, not
`PoseFrames` directly: each authored frame's `EasingSteps > 0` transition
is now several shorter baked sub-frames, each with its own (shorter)
`Duration`, and `activeBakedIndex` (not `activeFrameIndex` alone) tracks
which one is currently showing. Once `activeBakedIndex` runs past the
current frame's baked group, it rolls over to `activeFrameIndex + 1`'s own
group at index 0. This is what actually makes eased motion visible during
Play in the edit panel, not just in the saved file.

## Easing and baked frames

Baking is **not** something that happens only at Save time anymore — it's
a live, always-fresh derived cache maintained throughout editing and
playback, and getting this wrong once caused a real, previously-shipped
data-corruption bug. Read `PoseFrame.BakedFrames`'s own doc comment and
`CommitCurrentPoseToActiveContext`'s own guard comment in
`RigEditorScene.cs` for the full account; this section summarizes the
architecture those comments describe.

- **`PoseFrame.Easing`/`EasingSteps`** are authored, exactly as before:
  `CycleActiveFrameEasing`/`SetActiveFrameEasingSteps` edit them, and they
  describe the *outgoing* transition from this frame to the next
  (wrapping to frame 0 for the last frame — every clip loops in practice,
  per `ExoSkeletonAnimator.Start()` always calling `Play(name, true)`).
  `EasingSteps == 0` (the default) means "no baking" — a hard cut,
  byte-identical to what this tool produced before easing existed —
  regardless of which `Easing` curve is selected; `Easing` only matters
  once `EasingSteps > 0`.
- **`PoseFrame.BakedFrames`** is a *pure derived cache* — the actual
  baked, ready-to-play sub-frames for that frame's own outgoing
  transition. It is never part of `RigSnapshot`/`RigSnapshotCloner` (so
  Undo/Redo never restores it directly) and never hand-edited: it only
  ever gets replaced wholesale by a rebake, driven off the *same* frame's
  own `Poses`/`Easing`/`EasingSteps`/`Events`/`AttachPoints`, so there's no
  separate "keep it in sync" bookkeeping to get wrong. `EasingSteps <= 0`
  bakes to exactly one entry (a clone of the frame itself) — the real
  "no baking" case.
- **`RebakeFrame`/`RebakeClip`/`RebakeAllClips`** do the actual
  recomputation, deliberately non-incrementally: every call re-derives
  every clip's baked frames from scratch rather than tracking which edit
  invalidates which frame's bake (frame *i*'s bake depends on frame
  *i+1*'s pose too — the transition target — so almost any edit can
  invalidate a neighboring frame's bake, not just the one directly
  touched). `RebakeAllClips` is called unconditionally from
  `ApplyContextPoseToParts`, `RefreshTimeline`, `ScrubToBakedFrame`, and
  `TickPlayback` — a rig's clips total at most a few hundred `Lerp` calls
  combined, cheap enough not to bother gating.
- **`activeFrameIndex` vs. `activeBakedIndex`**: `activeFrameIndex`
  identifies the authored frame; `activeBakedIndex` identifies a position
  within *that frame's own* `BakedFrames` (`RigEditorScene.ActiveBakedIndex`).
  `ScrubToFrame` (clicking a frame chip) always resets `activeBakedIndex`
  to 0 — the frame's own exact authored pose — so jumping to a keyframe
  never lands mid-transition. `ScrubToBakedFrame(frameIndex, bakedIndex)`
  is the frame strip's ghost-row entry point for landing on a *specific*
  baked sub-frame directly. Only `TickPlayback` ever advances
  `activeBakedIndex` past 0, and only while actually playing.
- **Inspector field vs. context switch**: clicking a timeline chip or
  Node Tree clip deselects a focused pose field. Unity then fires
  `onEndEdit` against the *new* clip/frame. `PoseContextGeneration`
  increments on those switches; `InspectorPanel.BindPoseField` records
  it on focus and drops the late commit. Viewport drags are a second
  path: EventSystem runs before `EditorInputController.Update`, so
  releasing a Move/Rotate/Scale drag on a chip selects the new frame
  first. Those switch sites commit the old context, then
  `CancelActiveDrag` so mouse-up cannot write into the new one.
  `TickPlayback` does not cancel the drag (Mass Edit). See
  [`animator-pose-leaks-across-frames.md`](../../docs/issues/unresolved/animator-pose-leaks-across-frames.md).
- **The bug this architecture prevents**: `CommitCurrentPoseToActiveContext`
  is what turns the parts' live transforms back into stored pose data —
  called at every context switch and, critically, at the end of every
  drag. If it ever ran while the viewport was showing a **baked**
  sub-frame (`activeBakedIndex != 0` — mid-playback, or mid-ghost-row
  scrub) instead of an authored one, it would silently write that
  generated, interpolated pose back into the *authored parent frame's*
  `Poses` — corrupting it a little further on every subsequent
  scrub/commit, since the next bake would then derive from an already-
  corrupted authored value. `CommitCurrentPoseToActiveContext` closes this
  by resetting `activeBakedIndex` to 0 (and reapplying the authored pose)
  as the very first thing it does whenever it isn't already there, before
  reading any part's live transform — making it structurally impossible
  for a baked value to feed back into the data it was derived from. The
  companion half of this fix, `ApplyPoseFrameToParts` vs.
  `ApplyContextPoseToParts` (see "Pose application" above), is what lets
  playback/ghost-scrubbing display a baked frame at all without that
  display itself being mistaken for an edit.
- **`ExpandClipForSave(clip)`** — called only from `OnSaveClicked` — is
  now a **plain flatten**: it concatenates every authored frame's already-
  fresh `BakedFrames` into the list that gets written to `rig.json`. The
  actual interpolation math (`InterpolateFrame`, `Vector2.Lerp`/
  `Mathf.LerpAngle`/`Mathf.Lerp` per field) lives entirely in
  `RebakeFrame` now, not here — `ExpandClipForSave` doing its own
  from-scratch expansion at Save time is what this architecture replaced.
  `OnSaveClicked` also rebakes every clip it's about to write one more
  time, defensively, immediately before flattening (see "Save" below).

See [`animation-data-model.md`](animation-data-model.md) for the easing
curve formulas (`EasingFunctions.Evaluate`) themselves.

## Undo/Redo

`AnimatorHistory` (`Editor/AnimatorHistory.cs`) and `RigSnapshot`/
`RigSnapshotCloner` (`Editor/RigSnapshot.cs`) — see
[`animation-data-model.md`](animation-data-model.md) for their own detail
— hook into this file at two points:

- `RigEditorScene.CaptureSnapshotForHistory()` builds a deep-cloned
  `RigSnapshot` (`restPoses`, `clips`, each part's `StaticLayer`, and
  which clip/frame was active) for `AnimatorHistory` to push. It's called
  from `AnimatorHistory.CaptureBeforeChange`, itself called at the start
  of essentially every user-initiated mutation in this file (every
  `Set*`/`Mass*`/clip/frame editor, plus `EditorInputController.TryBeginDrag`
  for the drag tools) — see that method's own doc comment for why it also
  force-commits any not-yet-committed live drag first, so two drags in a
  row with no context switch between them still each get their own undo
  step.
- `RigEditorScene.RestoreSnapshotForHistory(snapshot)` is the inverse,
  called by `AnimatorHistory.Undo`/`Redo`/`JumpTo` — restores
  `restPoses`/`clips`/`StaticLayer`s and the active clip/frame, resets
  `activeBakedIndex` to 0 and stops playback, then calls
  `ApplyContextPoseToParts()` (which rebakes as its own first step, since
  `BakedFrames` is deliberately never part of a snapshot — see "Easing and
  baked frames" above).

`AnimatorHistory.CaptureBeforeChange` also calls `RigEditorScene.PausePlayback()`
first — see [`architecture.md`](architecture.md)'s Mass Edit section for
why that's a no-op while Mass Edit is on. `AnimatorHistory.Clear()` is
called only from `OnLoadClicked` — a freshly loaded rig has nothing in
common with prior history. `OnImportClicked` (`CharacterImporter.Import`
— see [`character-importer.md`](character-importer.md)) only points
`CurrentFolder` at the freshly written folder; history isn't cleared until
the user actually Loads it, same as opening any other folder.

## Save

`OnSaveClicked(folder)`: commits the current context, runs every
registered validator (`AnimatorValidatorRegistry.RunAll` — see
[`animation-data-model.md`](animation-data-model.md)) against what the
user actually authored (before the required-clip fallback below can mask
a real gap), sorts `loadedParts` by `StaticLayer` for the static
`"parts"` array (skipping duplicate-instance parts — there's no second
PNG for them, so they don't get their own static entry), auto-generates
`"Stand"` and (if neither exists) `"StandStatic"` via `EnsureRequiredClip`
(base-game systems throw if a hero's rig is missing these — see
`LokrCharacterLoader`'s `CustomRigLoader` validation), then every combat
sequence name `CombatSequenceNames.ForModel` returns for the character's
`CharacterProfile.Model` (the combat-view prefab; HumanArcher's angled
`Attack0` set is not universal — ObeliskLvl4 looks up un-angled
`SpecialAttack`), then missing `Head`/`Chest`/`Base` attach points and
missing `AbilityAction`/`AbilityEnd` events on Attack/SpecialAttack/SpellCast
clips and missing `AbilityEnd` on Death (see [`animation-data-model.md`](animation-data-model.md)), defensively
rebakes every clip about to be written (`RebakeClip` — belt-and-suspenders
alongside the rebakes that already happen throughout editing, and the
only bake `EnsureRequiredClip`'s own synthetic fallback clips ever get,
since they're never part of the in-memory `clips` list `RebakeAllClips`
walks), then for each frame of each clip:

- Builds a **per-frame** ordering (`RenderOrderIndex` if recorded, else
  `StaticLayer`) rather than always using the rig's static order — so
  round-tripping an imported rig doesn't flatten real per-frame
  z-variation back down to one static order.
- For `Approximate` poses, writes the stored raw matrix straight back out
  (inverting the same sign/scale convention `DecodeFrameMatrix` reads)
  instead of recomputing through `ComputeFrameMatrix` — since
  `DeltaPosition`/`RotationDegrees`/`ShearDegrees`/`Scale` for these are
  only ever a display fallback, never the source of truth.
- Duplicate-instance parts write their matrix entry under their **base**
  name (`BaseName(part.PartName)`), so a `"Asst_Arm01 (copy 2)"`
  `DraggablePart` appears as a second `{"name":"Asst_Arm01",...}` entry
  within that frame — reproducing the original "same name twice"
  structure.
- Frames actually written come from `ExpandClipForSave(clip)` — a plain
  flatten of every authored frame's own `BakedFrames` (see "Easing and
  baked frames" above); this is where an `EasingSteps > 0` transition
  turns into however many hard-cut frames the real game's flat schema
  needs.

Clips with authored `RootMotionPositions` also write a top-level
`rootMotions` array (`BuildRootMotionsJson` expands per-frame samples
to 30fps `positions` via `AnimatorFeelRules.ExpandRootMotionPositions`).
Empty lists omit the clip from that array.

After a successful write: `SavePivotsSidecar`/`SaveAnimationSourceSidecar`
(see "Sidecar files" above) write the two editor-only sidecars from the
same `orderedParts`/`clips` data, then `RebuildPreview()` runs immediately,
since the file on disk just changed.

**Atomic writes and escaping (2026-08-12, pre-redesign audit C-01/C-02).**
All three files (`rig.json` here, plus both sidecars) go through
`WriteAllTextAtomic(path, content)` — writes the full content to a
sibling `.tmp` file first, then deletes any existing file at `path` and
renames the temp file into place, rather than truncating and rewriting
`path` directly. A same-volume `File.Move` is a metadata-only rename, so
`path` itself is always either the complete old content or the complete
new content, never a partial write left by a crash, power loss, or full
disk mid-write. This does not make the three-file save a single
cross-file transaction — a crash between writing one file and the next
can still leave the trio inconsistent with each other — see
`WriteAllTextAtomic`'s own doc comment for why that fuller guarantee is
out of scope here. Every part/clip/event/attach-point name interpolated
into any of the three files' JSON also goes through
`LokrModAPI.Serialization.TextEscaping.JsonEscape` first — an unescaped
quote or backslash in a user-typed name used to be able to produce
unparseable JSON.

## Preview

`RebuildPreview()`: full rebuild from whatever's on disk — destroys any
existing preview object, calls `CustomRigLoader.BuildFromFolder`, builds
an `EditorPreviewRig` GameObject
(`ExoSkeletonData`/`MeshFilter`/`MeshRenderer`/`ExoSkeletonRenderer`/
`ExoSkeletonAnimator`). The preview animator **loops the active clip**
(`Play(index, looping: true)`). Editor Play/Pause and frame scrub stay on
the editable viewport; `RefreshPreviewFrame` no-ops while that loop is
running. Preview's own camera has no auto-fit either — see
[`architecture.md`](architecture.md)'s viewport section; it pans/zooms
manually, matching the Main Viewport's own bounds shape.

Auto-called after Load (if a `rig.json` already exists) and after every
Save — Preview stays live without the user ever pressing a button; the
"Refresh Preview" button (relabeled from "Preview") is now only a manual
"force reload from disk" escape hatch.

`SyncPreviewAnimIndexToActiveClip()`: lightweight — re-resolves which
animation index within the *already-built* `previewAsset` matches the
newly active clip, by name (the saved file's animation order isn't
guaranteed to match the session's clip list order). No rebuild needed;
called from `SelectClip`.

`RefreshPreviewFrame()`: no-ops while the preview animator is looping.
Fallback `SetPose` remains for Rest Pose / a never-posed
`ExoSkeletonData` (`ExoSkeletonRenderer.LateUpdate` null-refs otherwise).
Clip changes go through `SyncPreviewAnimIndexToActiveClip` →
`Play(index, looping: true)`, which restarts the loop on the new clip.

## Matrix encode/decode

The static `"parts"` offset is translation-only, baked once into each
part's vertices with no rotation component; a per-frame matrix is applied
*after* that, directly to the already-translated vertices — and matrix
rotation happens around the **world origin**, not the part's own position.
`ComputeFrameMatrix(restPosition, pivotOffset, deltaPosition, rotationDegrees, shearDegrees, scaleX, scaleY)`
solves for the translation component that keeps a part's **pivot**
(`restPosition + pivotOffset` — see `RestPose.PivotOffset` and the
`rig.pivots.json` sidecar above; defaults to the rest position itself)
mapped to its intended delta-shifted anchor under the composed
rotation→shear→scale matrix (`AffineMatrixMath.ComposeLinear`), then
converts through `ExoSkeletonDataAsset.NewMatrix`'s sign-flip/
pixels-to-units convention to the raw `a,b,c,d,tx,ty` the JSON format
uses. `SetPartTransform`/`ComputeCenterOffsetFromPivot` handle the
companion problem on the editing side: Unity's `Transform` always
rotates/scales a GameObject around its own `transform.position` (the
sprite's rendered center), not an arbitrary pivot, so whenever
`PivotOffset` is non-zero the part's transform position has to be offset
from `baseAnchor` by `(I - M) * PivotOffset` to make it *look* like the
part is rotating/scaling around the chosen pivot instead of its own
center.

`DecodeFrameMatrix` is `ComputeFrameMatrix`'s inverse, via Gram-Schmidt on
the matrix's two columns: `ScaleX`/rotation come from column 1 (exactly as
before shear existed — shear never touches column 1 in this convention);
column 2 splits into its component along column 1's own direction (the
shear) and its component perpendicular to that (`ScaleY`). This is exact
for **any** invertible 2×2 matrix, not an approximation — real shipped
data that's non-uniformly scaled or genuinely sheared now decodes exactly
instead of falling back to a lossy fit. `approximate` (see
`PartPose.Approximate` in the Load section above) is only set for a
genuinely degenerate matrix (near-zero scale on column 1, where rotation
is undefined). Decoding with the wrong pivot only corrupts the recovered
`deltaPosition` — rotation/shear/scale are pivot-independent, since
translation is the only matrix component a pivot choice actually affects,
which is why `LoadSavedRig` loads `rig.pivots.json` **before** decoding
any frame matrix.
