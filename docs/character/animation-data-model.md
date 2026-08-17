# LokrLab — Animation Data Model & Editing Subsystem

Scope has grown well past the original clip/frame data model — this doc
now covers the in-memory pose/clip types, the live baked-frame cache that
sits on top of them, the timeline UI, snapshot-based undo/redo, and the
three plugin-style registries (tools/importers/validators) an editing
session runs through. See [`layout.md`](layout.md) for the full file list
this doc is responsible for.

## `Editor/Animation/AnimationClip.cs`

### `PartPose`

One part's pose within a single keyframe, relative to that part's
`RestPose` — the schema's static `"parts"` offset is one fixed value
shared by every animation, so it can't itself vary per keyframe.

- `DeltaPosition` (`Vector2`) — offset from rest position.
- `RotationDegrees`.
- `ScaleX`/`ScaleY` — independent per-axis scale, generalizing an earlier
  single uniform `Scale` field. `ScaleX == ScaleY` reproduces every pose
  authored before non-uniform scale existed. Added for the **Scale XY**
  tool (`AnimatorTools.ScaleXYTool`, below) — displaying non-uniform scale
  read-only wasn't the point, authoring it was.
- `ShearDegrees` — shear angle in degrees. Together with
  `RotationDegrees`/`ScaleX`/`ScaleY` this is a full
  rotation→shear→scale decomposition of the pose matrix's 2x2 linear part
  (`RigEditorScene.ComputeFrameMatrix`/`DecodeFrameMatrix`, via
  `AffineMatrixMath.ComposeLinear` — see below). `0` reproduces every pose
  authored before shear existed.
- `Included` — `false` means this part is absent from this frame's source
  data entirely (e.g. an imported rig where an accessory only appears in
  some animations). Defaults `true`; only `RigEditorScene.LoadSavedRig`
  ever sets it `false`.
- `Approximate` + `RawA`/`RawB`/`RawC`/`RawD`/`RawTranslateX`/
  `RawTranslateY` — `Approximate` is `true` only when
  `DecodeFrameMatrix` couldn't recover a rotation at all (near-zero scale
  on the matrix's first column — a genuinely degenerate/near-singular
  source matrix). **Before `ShearDegrees` existed, any shear at all forced
  this path** (the old rotation+uniform-scale decomposition couldn't
  represent it); now `DecodeFrameMatrix` recovers rotation/shear/scale
  exactly for any invertible 2x2 matrix via Gram-Schmidt on its two
  columns, so real shipped poses that are merely non-uniformly scaled or
  sheared — not actually singular — decode exactly instead of falling
  back here. When `Approximate` is `true`, the `Raw*` fields hold the
  exact source matrix (already in `DecodeFrameMatrix`'s Unity-units/
  sign convention) so the part still displays and re-saves losslessly
  without going through `DeltaPosition`/rotation/shear/scale at all — see
  `DraggablePart.SetAffinePose` in
  [`supporting-classes.md`](supporting-classes.md).
- `RenderOrderIndex` — this frame's actual draw-order position for this
  part (`ExoSkeletonRenderer.LateUpdate`: draw order *and* z-depth come
  from position within the frame's own `renderOrder` array, not the rig's
  static `"parts"` list order — see
  [`cross-references.md`](cross-references.md)). `-1` means "not
  recorded," falling back to the part's persistent
  `DraggablePart.StaticLayer`.

### `AttachPointPose`

A named socket at a specific point in a specific frame — where a held
weapon or VFX should spawn from. Unlike a part, the schema has no static
rest offset for an attach point at all: each frame independently declares
its own full list, so `Position`/`RotationDegrees`/`ShearDegrees`/
`ScaleX`/`ScaleY` here are plain absolutes (world units), not deltas.
`RigEditorScene.ComputeFrameMatrix`/`DecodeFrameMatrix` are reused
unchanged for the matrix math by passing `restPosition = pivotOffset =
Vector2.zero` — with no rest offset, "pivot at rest position" and "pivot
at the origin" are the same thing. `Index` preserves the schema's
`AttachPointDef.index` (frames are re-sorted by it on load) from whatever
was loaded, so re-saving imported data doesn't reorder it.

### `PoseFrame`

- `Duration` (default `0.15f`), `Poses` (`Dictionary<string, PartPose>`).
- `Events` (`List<string>`) — tag strings fired when this frame plays.
  Combat actually names three: `AbilityStart`, `AbilityAction`, and
  `AbilityEnd` (`CombatPlaybackRequirements.KnownEventNames`). The Frame
  inspector Events field is a combobox of those plus Add, and a keyed
  list of the frame's current tags (a frame can carry more than one —
  a 1-frame SpellCast often has both `AbilityAction` and `AbilityEnd`).
  The combobox is still typeable: any other string is forwarded as
  `OnAbilityCustomEvent`. This is not the Properties-workstation sound
  event list (`CharacterLabKnownOptions.SoundEvents`).
- `AttachPoints` (`Dictionary<string, AttachPointPose>`) — keyed like
  `Poses`, same "absent means doesn't exist in this frame" semantics.
  Vanilla combat looks up `Head`/`Chest`/`Base`
  (`CombatPlaybackRequirements.AttachPointNames`). The Frame inspector
  lists each socket on the active frame with editable X/Y/Rot, plus a
  combobox to add a known or custom name.
- `Easing` (`EasingType`) / `EasingSteps` (`int`) — describe the
  **outgoing** transition from this frame to the next one in the clip
  (wrapping to frame 0 for the last frame — every clip loops in practice,
  see `ExoSkeletonAnimator.Start()` in
  [`cross-references.md`](cross-references.md)). `EasingSteps <= 0` is
  the default and means "no baking," byte-for-byte the same hard cut this
  tool produced before easing existed. `Easing` is independent of that —
  it's *which curve shape* to bake along whenever `EasingSteps > 0`, not
  a synonym for "no baking"; `Linear` with `EasingSteps > 0` still bakes
  real sub-frames, just along a straight ramp.
- `BakedFrames` (`List<PoseFrame>`) — see below. The single biggest
  addition to this file since it was last documented.

### `AnimationClip` / `RestPose`

`AnimationClip`: `Name` + `List<PoseFrame>` + `RootMotionPositions`
(per-authored-frame cumulative X in pixels; empty means the clip has
no `rootMotions` entry). See “Root motion” below.

`RestPose`: `Position`, `RotationDegrees`, `ScaleX`/`ScaleY`,
`ShearDegrees` (same non-uniform-scale/shear generalization as
`PartPose`, used when editing "Rest Pose" directly and for
auto-generating the required `Stand`/`Portrait`/`StandStatic` clips when
the user hasn't authored them), plus `PivotOffset` — the point (relative
to `Position`) rotation/scale pivot around, for every clip/frame of this
part. The schema has no pivot concept at all: `ComputeFrameMatrix`/
`DecodeFrameMatrix` fold it into the exported matrix, so a rig using a
custom pivot loads and renders correctly with zero engine-side changes.
This editor still needs the value for its own round-tripping (decoding a
saved matrix back into `DeltaPosition`/rotation/shear/scale requires
knowing what pivot produced it), so it's persisted in an editor-only
sidecar, `rig.pivots.json`, next to `rig.json` — see
`RigEditorScene.LoadPivotsSidecar`/`SavePivotsSidecar` in
[`rig-editor-scene.md`](rig-editor-scene.md).

Rest Pose is the **default for new clips**, not a live bind that Walk /
Attack follow. `CreateNewClip` snapshots current rest into frame 0
(`DefaultPoseFor` for every loaded part). Later rest-position edits
compensate existing clip deltas (`CompensateClipDeltasForPart` /
`AnimatorFeelRules.CompensateClipDelta`) so included, non-approximate
poses keep their world positions. Rotation/scale on `PartPose` were
already absolute. Save-time `EnsureRequiredClip` stubs still use an
empty frame so Stand / combat fallbacks follow live rest. See
[`animator-feel.md`](../../../docs/roadmaps/completed/animator-feel.md).

## Root motion — `AnimationClip.RootMotionPositions`

Vanilla `ExoSkeletonDataAsset.ReloadData` reads top-level
`rootMotions[]` `{ name, positions[] }` (pixel cumulative X) and turns
consecutive samples into `Animation.moveCurve` speeds at 1/30s. Combat
uses `moveCurve`; `ExoSkeletonAnimator` does not apply it. This is a
curve that moves the **unit origin**, not parts wiggling in place.

The Lab authors one sample per `PoseFrame` (`RootMotionPositions`).
Empty list = no `rootMotions` entry. Frame inspector **Root X (px)**;
blank clears the whole clip curve. Save expands to dense 30fps
`positions` via `AnimatorFeelRules.ExpandRootMotionPositions`. Sidecar
`rig.animsource.json` stores optional `"rootMotion"` per clip; load
prefers that, else downsamples `rig.json` with
`SampleRootMotionAtFrameStarts`. Frame add/paste/delete/move keep the
list in sync. The editor viewport does not offset the grid live —
preview after Save uses vanilla `moveCurve`.

Unity-free helpers (also used by xUnit) live in
`LokrLab/AnimatorFeelRules.cs`.

## `PoseFrame.BakedFrames` — live baked-frame cache

`BakedFrames` is a **pure derived cache**, not authored state: the actual
baked-and-ready-to-play sub-frames for this frame's outgoing transition,
rebuilt wholesale from this frame's own `Poses`/`Easing`/`EasingSteps`/
`Events`/`AttachPoints` by `RigEditorScene.RebakeFrame`/`RebakeClip`/
`RebakeAllClips` (all `private` — every external caller goes through
`RebakeAllClips`). It is:

- **Never hand-edited** — only ever replaced wholesale by a rebake, so
  there's no "keep it in sync" bookkeeping to get wrong.
- **Never part of `RigSnapshot`/`RigSnapshotCloner`** (undo/redo, below)
  — since it's always rebuildable from source data, snapshotting it would
  just be wasted memory on every undo step.
- **Always rebaked before it's read.** `RigEditorScene.RefreshTimeline()`
  — the chokepoint nearly every mutator in `RigEditorScene` funnels
  through at the end of an edit — unconditionally calls
  `RebakeAllClips()` first, every call, before refreshing
  `AnimationsPanel`/`AnimationTimelinePanel`. `TickPlayback` and
  `ScrubToBakedFrame` also rebake explicitly (they read `BakedFrames`
  outside `RefreshTimeline`'s own call). Rebaking is deliberately
  **non-incremental** — every call re-derives every clip's baked frames
  from scratch rather than tracking which edit invalidates which frame's
  bake, because frame *i*'s bake depends on frame *i+1*'s pose too (the
  transition target), so almost any edit can invalidate a neighboring
  frame's bake, not just the one directly touched. A rig's clips total at
  most a few hundred `Lerp` calls combined — recomputing all of them on
  every call, including from code paths that run on essentially every
  context switch and playback tick, isn't measurable against Unity's
  frame budget. Simplicity and guaranteed freshness win over an
  optimization nothing here needs.

**Baking semantics** (`RebakeFrame(clip, i)`):

- `EasingSteps <= 0`, or fewer than 2 frames in the clip: the real "no
  baking" case, regardless of `Easing`. Bakes to exactly **one** entry —
  a full clone (`RigSnapshotCloner.Clone`) of the frame itself, including
  its `Events`/`AttachPoints`.
- Otherwise: `segments = EasingSteps + 1`, `segmentDuration =
  Duration / segments`. Entry 0 is a shortened clone of the frame itself
  (`Duration = segmentDuration`, keeps its own `Events`/`AttachPoints`).
  Entries 1..`segments-1` are generated via `InterpolateFrame(current,
  next, t, segmentDuration)`, `t = EasingFunctions.Evaluate(Easing, s /
  segments)`, lerping toward `next` — the *next* `PoseFrame` in the clip,
  wrapping to index 0 for the last frame. `InterpolateFrame` lerps
  `DeltaPosition` (`Vector2.Lerp`), `RotationDegrees`
  (`Mathf.LerpAngle` — wraps correctly), `ShearDegrees` and `ScaleX`/
  `ScaleY` (plain `Mathf.Lerp` — `ShearDegrees` isn't a wrapping angle,
  `DecodeFrameMatrix` derives it via `Atan`, always within `(-90,90)`).
  Attach points lerp the same way over the union of both sides' names,
  holding-then-popping for a socket present on only one side (no
  rest-pose equivalent to fall back to). **Generated in-between entries
  never carry `Events`** — a one-shot trigger firing once per generated
  sub-frame would fire it several times over instead of once; only the
  "self" clone (entry 0) carries the frame's real `Events`.
  `next` itself is never appended a second time — it's already present
  as frame `i+1`'s own `BakedFrames[0]` (or, for the wrap-around case,
  `PoseFrames[0]`'s), and the game's own frame-array looping closes the
  transition without duplicating it.

**Why a live cache instead of "bake only at Save."** An earlier version
of this tool only ever expanded easing at Save time — the editor's frame
list stayed exactly what the user placed, and nothing else read a baked
sub-frame. That shipped a real bug: nothing distinguished an authored
parent frame's own data from a baked/interpolated sub-frame's data during
editing, so scrubbing or playing back through an eased transition risked
committing a baked, interpolated pose *as if it were* the authored
frame's own pose, corrupting the easing setup a little more on every
scrub. The fix has two parts:

1. `BakedFrames` is now populated continuously, not just at Save, so
   scrubbing/playback have real baked data to show at any time (see
   `ScrubToBakedFrame`/`TickPlayback` below).
2. `activeBakedIndex` (`RigEditorScene`) — which entry of the *active*
   frame's own `BakedFrames` is currently being displayed — is reset to
   `0` (the frame's own exact authored pose, `BakedFrames[0]`) every time
   `activeFrameIndex` changes, and only `TickPlayback` ever advances it
   past `0`. Anything that reads the live pose back into `Poses` for
   editing (`CommitCurrentPoseToActiveContext`) only ever does so while
   `activeBakedIndex == 0` — a baked/interpolated sub-frame's values can
   be *previewed* (via `ApplyPoseFrameToParts`, not
   `ApplyContextPoseToParts`) but can never be written back over the
   authored frame's own `Poses`. See `RigEditorScene.ScrubToFrame`
   (always resets to `BakedFrames[0]`) vs. `ScrubToBakedFrame` (jumps to
   a specific sub-frame, preview-only) for the two entry points, and
   `AnimationTimelinePanel`'s "ghost row" (below) for the UI this feeds.

**Save still flattens separately.** `RigEditorScene.ExpandClipForSave`
is unchanged in spirit — it is still the one place that produces the flat
frame list actually written to `rig.json` — but it's now a thin flatten
(`expanded.AddRange(frame.BakedFrames)` per frame) rather than where the
interpolation math itself lives; baking already happened by the time it
runs. `OnSaveClicked` calls `RebakeClip` defensively on every clip about
to be written (including `EnsureRequiredClip`'s auto-generated
`Stand`/`StandStatic` and per-Model combat stubs, which were never part of `clips`
and so were never touched by `RebakeAllClips`) immediately before calling
`ExpandClipForSave`, so Save's correctness never depends on some earlier
mutator having already triggered a rebake.

**Surviving a save/reload round-trip.** The flat `rig.json` format has no
way to represent "these N consecutive frames were one authored parent
frame baked with 5 steps" — reloading a flattened file the naive way
would treat every baked sub-frame as if it had been separately
hand-authored, silently exploding e.g. 2 authored eased frames into 30
flat ones and destroying the original `Easing`/`EasingSteps` grouping.
`RigEditorScene` avoids this with a second editor-only sidecar,
`rig.animsource.json` (`SaveAnimationSourceSidecar`/
`LoadAnimationSourceSidecar`), alongside the existing pivots sidecar —
it persists each clip's authored (pre-baking) `PoseFrame` list, keyed by
clip name, plus optional `"rootMotion"` (per-frame cumulative X) when
the clip has authored samples, and `LoadSavedRig` prefers it over
re-deriving frames from the flat file whenever a clip's name is present
in it. See
[`rig-editor-scene.md`](rig-editor-scene.md)'s Save/Load section for the
sidecar's own read/write format and fallback behavior for clips/rigs
saved before it existed.

## `Editor/Animation/EasingFunctions.cs`

`EasingType` enum (`Linear`/`EaseIn`/`EaseOut`/`EaseInOut`) +
`Evaluate(type, t)` (quadratic formulas: `t*t`, `1-(1-t)^2`, the
piecewise `t<0.5` split for in-out). Unchanged. Explicitly *not* runtime
interpolation — the base game's `ExoSkeletonAnimator` holds each frame
for its full duration then jumps wholesale to the next, with no tween
parameter to plug a curve into (see
[`cross-references.md`](cross-references.md)). "Easing" here means
baking extra generated frames along an eased curve into `BakedFrames`
above, the same technique flipbook/stop-motion animation uses to fake
smooth motion from discrete poses.

## Timeline UI — `Editor/AnimationsPanel.cs` + `Editor/Animation/AnimationTimelinePanel.cs`

Clip selection and frame navigation are now two separate panels, not one
four-row panel with an inline clip selector. Per-frame property editing
(`Duration`/`Easing`/`Events`/`AttachPoints`, per-part transforms) no
longer lives inline in either — it moved to `InspectorPanel`'s Frame
section, dispatched off `LabNode.Kind` strings mapped from
`RigEditorScene.CurrentInspectorTarget`
(`enum InspectorTarget { None, Part, Animation, Frame, Reference }`) — see
[`supporting-classes.md`](supporting-classes.md).

**`AnimationsPanel`** (`EditorLayout.AnimationsRowRegion`, above the
frame strip): a static "+ Add Animation" button (opens a name-only modal
whose quick-add presets are `CombatSequenceNames.PresetsForModel` for
the open character's Model; `RigEditorScene.CreateNewClip` snapshots
current Rest Pose into frame 0), then a
horizontally-scrolling row of "Rest Pose" + one button per clip
(`RigEditorScene.SelectRestPose`/`SelectClip`) — unbounded, so it lives
in its own `UiStack.Horizontal(..., scrollable: true)` clamped to the
panel's actual resolved width rather than running off-screen.

**`AnimationTimelinePanel`** (`EditorLayout.FrameStripRegion`, below
`AnimationsPanel`) is now a **frame-chip strip**, not a fixed-width
`< Frame N/Total >` scrubber: one individually-clickable chip per frame
(`RigEditorScene.ScrubToFrame(i)`), highlighted for the active frame,
plus a trailing "+" chip (`RigEditorScene.AddFrame`). Below the chip
strip, a transport row holds **Copy Frame** / **Paste as New** /
**Override** / **«** / **»** (`CopyActiveFrame` / `PasteFrameAsNew` /
`OverrideActiveFrame` / `MoveActiveFrame`) and Play/Pause. Copy stores a
full deep clone of the active frame (every part pose, attach points,
duration, easing, events) on a session clipboard, or a zero-delta
snapshot when Rest Pose is selected; Paste as New inserts
that clone as a new frame after the current one (including into a
different clip) and still requires a real clip; Override replaces the
current clip frame in place, or Rest Pose (clip deltas compensated so
Walk/Attack keep their world positions); « / »
shift the current frame one slot on the timeline without wrapping. Paste
and Override stay greyed out until something has been copied; « / » grey
out at either end of the clip. Copy and Override also work while Rest
Pose is selected (Paste as New does not). The same actions are on the Frame
inspector, the Edit menu, and hotkeys (Ctrl+C / Ctrl+V / Ctrl+Shift+V /
`[` / `]`), skipped while a text field is focused. This replaced an
earlier scrubber specifically because a frame-per-button row with no
scroll bound made any clip past ~9 frames wide enough to collide with
other controls; the current version lives in its own horizontal scroll
viewport (`UiStack.Horizontal(..., scrollable: true)`) clamped to the
panel's real resolved pixel width, correct regardless of frame count.
`RigEditorScene.ScrubToAdjacentFrame(direction)` (wraps, doesn't clamp)
still exists but has no remaining caller in `Editor/` — the chip strip
calls `ScrubToFrame` directly per chip instead; likely a vestige of the
pre-chip scrubber UI.

A second **"ghost" row** sits directly below the parent chip row,
visualizing each parent frame's own `PoseFrame.BakedFrames`: a frame with
`EasingSteps <= 0` shows one sub-chip spanning its whole slot (matching
"bakes to exactly one entry" above); a frame with `EasingSteps > 0` shows
that many narrower sub-chips sharing the slot, one highlighted at a time
as playback advances through it (`RigEditorScene.TickPlayback`) — direct
visual confirmation easing is actually happening. Clicking a ghost
sub-chip calls `RigEditorScene.ScrubToBakedFrame(frameIndex, bakedIndex)`
to preview that specific baked sub-frame without disturbing the authored
frame's own data (see the BakedFrames section above). The ghost row is
built as more chips in the *same* scrolling `Content` as the parent row,
not a second independent `ScrollRect` — two independent `ScrollRect`s can
only be kept in sync by manually mirroring one's scroll position onto the
other every frame, which drifts under fast drags/inertia; sharing one
`Content` makes "the ghost row stays aligned under the parent row" true
by construction.

`AnimationTimelinePanel` keeps Play/Pause (`RigEditorScene.
TogglePlayback`) since it's a whole-clip transport control that sits
right next to what it plays through, not a property of any one frame.
Frame copy/paste/override/reorder sit on that same transport row because
shifting frames is a timeline operation; they are duplicated on
`InspectorPanel`'s Frame section (the current frame's own actions) and
the Edit menu.

## Undo/Redo — `Editor/AnimatorHistory.cs` + `Editor/RigSnapshot.cs`

Not documented anywhere previously; new since this doc was last accurate.

**Snapshot-based, not command-pattern.** `RigSnapshot` is a deep copy of
everything undo/redo needs to restore: `RestPoses`
(`Dictionary<string, RestPose>`), `Clips` (`List<AnimationClip>`),
`StaticLayers` (`Dictionary<string, int>` — each part's persistent draw
order, not stored on `RestPose`/`PartPose` itself), plus
`ActiveClipName`/`ActiveFrameIndex` so undoing also restores the editor's
viewing context. It deliberately does **not** include
`DraggablePart.Visible` (the eye-toggle — a display convenience, never
persisted to disk either) or `PoseFrame.BakedFrames` (a pure derived
cache that gets rebuilt from the restored `Poses`/`Easing`/`EasingSteps`
anyway — snapshotting it would be pure wasted memory on every step).
Chosen over a command-pattern (paired do/undo operations per action)
because `RigEditorScene` is a `static class` with direct field/dictionary
mutation throughout and no existing indirection layer a command object
could hook into — a full command-pattern rewrite would touch most of
that file for comparatively little benefit over "just keep whole prior
copies." Correct by construction: a restored snapshot is exactly a prior
real state, not a hand-maintained inverse operation that can drift from
what its forward operation actually did.

`RigSnapshotCloner` holds the deep-clone helpers for every type
`RigSnapshot` references (`RestPose`, `AnimationClip`, `PoseFrame`,
`PartPose`, `AttachPointPose`) — deliberately kept separate from those
types' own definitions in `AnimationClip.cs` since cloning is purely an
undo/redo concern, not part of the animation data model itself. (Reused
elsewhere too: `RebakeFrame`'s "no baking" case clones a frame via the
same helper — see above.)

**`AnimatorHistory`**: two stacks of `HistoryEntry` (`Snapshot` +
`Description`, wrapped together so a description can never drift out of
sync with its snapshot through a push/pop), capped at `MaxDepth = 50`.

- `CaptureBeforeChange(description)` — called at the start of every
  user-initiated mutation throughout `RigEditorScene` and
  `EditorInputController.TryBeginDrag`. Pauses playback first
  (`PausePlayback` — stops it from ticking a frame change mid-edit) and
  flushes any not-yet-committed live drag
  (`CommitCurrentPoseToActiveContext`) before capturing, so two drags in
  a row with no context switch between them still each get their own
  undo step — a drag's result only lands in `restPoses`/`clips` at the
  next commit point otherwise, and a snapshot only ever reads from those.
  Clears `redoStack` (see "linear, not branching" below).
- `Undo()`/`Redo()` — pop one stack, capture current state onto the
  other, restore. `Restore` sets a `restoring` guard so
  `RestoreSnapshotForHistory`'s own refresh calls can't loop back into
  `CaptureBeforeChange`.
- `Clear()` — called on Load/Import; a freshly loaded rig has nothing in
  common with prior history, so carrying it forward would let Undo jump
  into a different rig entirely.
- `GetHistoryView()` — flattens `undoStack` (oldest→newest) + a synthetic
  "Current" marker + `redoStack` (reversed) into one ordered list for
  `EditHistoryPanel` (see [`supporting-classes.md`](supporting-classes.md))
  to render directly, oldest at the top.
- `JumpTo(flatIndex)` — jumps to any point in that flattened view by
  calling `Undo()`/`Redo()` the right number of times, reusing the proven
  restore path rather than restoring a snapshot directly, so it can never
  diverge from what a manual sequence of clicks would produce.
- **Linear, not branching**: making a new edit after an Undo (or after
  `JumpTo`-ing into the past) discards everything in `redoStack`, exactly
  like ordinary Undo/Redo always have — `EditHistoryPanel` is a visible
  list you can click into directly, not a tree of preserved alternate
  futures.

## Editing-tool registry — `Editor/AnimatorToolRegistry.cs` + `Editor/AnimatorTools.cs`

`IAnimatorTool` — a drag-driven editing tool (`Name`, `Hotkey`
(`KeyCode`), `AllowsAffinePose`, `BeginDrag`/`ContinueDrag`). Move/
Rotate/Scale (the original hardcoded `EditMode` switch-statement cases in
`EditorInputController`) plus the newer Scale XY/Pivot are all just
registered instances now, rather than special-cased switch arms — a
plugin can register a genuinely new tool the same way. One shared
instance per tool is enough, since only one drag can be active at a time
(`EditorInputController.isDragging`).

`AnimatorToolRegistry`: `RegisterTool`/`Find(name)`/`Tools` — a flat list,
`RegisterTool`'s remove-then-add means `RegisterDefaults()` is safe to
call more than once per process. **Not priority-ordered** (unlike the
import/validator registries below) — tools are looked up by exact name
(hotkey/toolbar button), never resolved through a first-match chain.
`RegisterDefaults()` registers, in order: `MoveTool` (`W`), `RotateTool`
(`E`), `ScaleTool` (`R`), `ScaleXYTool` (`Y`), `PivotTool` (`T`) — the
current first-party tool set (`ToolbarPanel`'s Select/Move/Rotate/Scale/
Scale XY/Pivot buttons — see [`layout.md`](layout.md)).

`AnimatorTools.cs` implementations, all operating on the **currently
active frame's freshly-refetched stored pose** each tick
(`RigEditorScene.GetStoredPose`/`GetStoredCenter`), not values frozen at
drag-start — necessary so a part keeps visibly animating through its own
frames if Mass Edit leaves playback running through a drag, instead of
freezing at wherever the drag started:

- **`MoveTool`** — accumulated mouse delta since drag start, applied on
  top of `GetStoredCenter`.
- **`RotateTool`**/**`ScaleTool`** — pivot around `RestPose.PivotOffset`
  (via `RigEditorScene.GetPivotWorldPosition`), not the part's rendered
  center; `ScaleTool` drives `ScaleX`/`ScaleY` together (uniform scale —
  the original Scale tool's behavior, now expressed through the same
  non-uniform-capable fields `ScaleXYTool` uses).
- **`ScaleXYTool`** — independent X/Y scale: horizontal mouse distance
  from the pivot drives `ScaleX`, vertical drives `ScaleY`, independently.
  v1 interaction — simpler to implement correctly than true draggable
  corner/edge handles (which need their own positioned, individually-
  clickable gizmo objects kept in sync with the part's rotated bounds)
  while still delivering the actual capability the tool exists for:
  authoring non-uniform scale at all, not just displaying it read-only.
  Distances are measured axis-aligned from the pivot, not along the
  part's own rotated axes — a known, acceptable v1 tradeoff. Corner-handle
  dragging remains a possible future refinement of this same tool.
- **`PivotTool`** — `AllowsAffinePose == true`. Single-part drag edits
  `RestPose.PivotOffset` directly (via
  `RigEditorScene.SetPivotWorldPosition`). With a multi-selection,
  `SupportsGroupDrag` is true and group drag moves the session temp
  group pivot (`SetTemporaryGroupPivotWorld`) instead of rewriting each
  part's rest pivot. See
  [`animator-feel.md`](../../../docs/roadmaps/completed/animator-feel.md)
  Phase 3.

## Part-source importer registry — `Editor/AnimatorImportRegistry.cs` + `Editor/LoosePngImporter.cs` + `Editor/GridAtlasImporter.cs`

Extension point for how a rig folder's part PNGs get populated before
`RigEditorScene.OnLoadClicked` reads them. Registered rather than
hardcoded so a plugin can add a third source without `RigEditorScene`
needing a new special-cased branch. Same shape as `CharacterAPI`'s
`ResolverChain<T>` in `LokrCharacterLoader`: priority-ordered
(`entries.Sort` descending by `Priority`), and re-registering a name
replaces its previous entry (`RegisterDefaults` is safe to call more than
once). An importer's whole job is making `targetFolder` end up containing
one named PNG per part — nothing downstream (`Load`, the rest of this
editor) needs to know which importer produced them.

`PartSourceImportFn(targetFolder, parameters, out message) -> bool`.
`AnimatorImportRegistry.TryImport(name, ...)` looks up by exact `name`
(not priority-resolved across names — priority only matters if two
importers ever register under the *same* name, which `RegisterDefaults`
never does today).

`RegisterDefaults()` registers two, both `Priority = 0`:

- **`"Loose PNGs"`** (`LoosePngImporter.Run`) — the original, only-ever
  behavior before this registry existed: the target folder already
  contains PNGs, placed there by hand or any external tool. Registered
  mainly for symmetry with the grid importer, so "how did these PNGs get
  here" always has an explicit, listed answer instead of an implicit
  default. `Run` only checks the folder is non-empty; it converts nothing.
- **`"Grid Atlas"`** (`GridAtlasImporter.Run`) — v1 atlas import: given
  `parameters["atlasPath"]` + `"rows"`/`"cols"`, slices a clean uniform
  grid, trims each non-empty cell to its own alpha bounding box
  (`AlphaThreshold = 0.01f`, same threshold `PixelIslandDetector` uses),
  and writes one PNG per non-empty cell, auto-named `Part_01`, `Part_02`,
  ... in raster order (row 0 = image top, flipped from Unity's
  bottom-up texture row convention). Cells are auto-named rather than
  prompted per-cell because `Load` matches a part's name to its PNG's
  filename — renaming a generated file in Explorer before clicking Load
  is equivalent to, and far simpler to build than, a dedicated per-cell
  rename dialog.

## Non-uniform atlas import — `Editor/PixelIslandDetector.cs` + `Editor/IslandAtlasPickerPanel.cs`

For atlases that aren't a clean grid — a second import path, not routed
through `AnimatorImportRegistry` (opened directly from `MenuBarPanel`'s
Atlas popup instead — the registry's single-synchronous-call contract
doesn't fit this panel's interactive, multi-step session).

**`PixelIslandDetector.Detect(atlas, out labelMap)`** — flood-fill
connected-component labeling. Pure algorithm, no UI/editor dependency
beyond `Texture2D`. 8-connectivity (diagonal neighbors count as
connected) — hand-drawn/anti-aliased sprite art routinely has
diagonal-only pixel connections at its edges; 4-connectivity would
fragment one visual piece into multiple islands. Returns `List<PixelIsland>`
(`Id`, inclusive `MinX`/`MinY`/`MaxX`/`MaxY` bounds in native
bottom-up texture pixel space) sorted in reading order (top-to-bottom,
then left-to-right), with `labelMap` (`width*height`, `-1` = background)
remapped so `island.Id` matches its position in the sorted list.

**`IslandAtlasPickerPanel`** — the interactive UI built on top of it.
Static modal/texture refs are cleared on lab close — see
[`lab-static-panels-not-reset-on-close.md`](../../../docs/issues/resolved/lab-static-panels-not-reset-on-close.md).
Every detected island starts as its own singleton `PartGroup` (`Name` +
`IslandIds` + `Excluded`), auto-named `Part_01`, `Part_02`, ... . The
user:

- Clicks islands on a clickable atlas overlay (`AtlasClickTarget`,
  normalized-then-pixel-mapped click → `labelMap` lookup) to select them.
- **Merges** selected islands into one group/part — for art whose visual
  part isn't a single contiguous blob (a cape split by an overlapping
  strap, say). The surviving group is whichever touched group contains
  the overall-lowest island id (deterministic), keeping that group's own
  (possibly user-renamed) `Name`.
- **Discards**/**Restores** groups — `Discard Selected`/
  `Discard Unselected` (bulk-discard everything the current selection
  *doesn't* touch — the fast path for cleaning up stray opaque pixels)/
  per-row `Discard`/`Restore`.
- Renames each kept group inline, and chooses a target character-folder
  **name** (not a path — sanitized and resolved under
  `CharacterLabPaths.CharactersRoot`), shown as an explicit, editable
  preview rather than silently reusing `RigEditorScene.CurrentFolder`
  (an earlier, easy-to-get-wrong default).

**Import** (`WriteGroups`) writes one PNG per surviving group: crops to
the union bounding box of all the group's islands, and masks out any
pixel whose `labelMap` entry isn't one of the group's own island ids —
necessary because two merged-but-spatially-distant islands can easily
have a third, unrelated island's pixels fall inside their combined
bounding box. Same crop/encode/write shape `GridAtlasImporter` uses.
`RigEditorScene.OnIslandAtlasImported(targetFolder, message)` is the
completion callback.

**Both atlas import paths converge on the same representation**
`LoosePngImporter`/the original Load path already consumes: one named
PNG per part in a folder. Nothing downstream of import — the pose model,
Save, Preview — needs to know whether a rig's PNGs came from hand-placed
files, a grid slice, or a merged pixel-island selection.

## Rig validator registry — `Editor/AnimatorValidatorRegistry.cs` + `Editor/RequiredAnimationNamesValidator.cs`

Extension point for Save-time rig checks — warnings only, never blocking,
matching how `CustomRigLoader`'s own runtime version of this same check
works (logs and lets the rig load anyway). Registered so a plugin adding
its own convention (e.g. a required animation name for a new combat
feature) can add a validator instead of `RigEditorScene` needing to know
about every downstream consumer's requirements in advance.

`RigValidatorFn(clips) -> IEnumerable<string>`.
`AnimatorValidatorRegistry.RunAll(clips)` runs every registered validator
(no priority — order doesn't matter, results are just concatenated) and
flattens their warnings. Crucially, `RunAll` is called against the clips
the user **actually authored**, before `OnSaveClicked`'s own
`EnsureRequiredClip` auto-generation fills in `Stand`/`StandStatic` — by
the time auto-generation has run, a required-names check would never
fire, defeating the point of it.

`RegisterDefaults()` registers one first-party validator, `"Required
animation names"` → `RequiredAnimationNamesValidator.Validate`: warns if
no clip is named `"Stand"` (Save will auto-generate a single static frame
from Rest Pose instead of a real animated idle), and warns if neither
`"Portrait"` nor `"StandStatic"` is authored (Save auto-generates
`"StandStatic"`). Mirrors the exact requirement
`CustomRigLoader.RequiredAnimationNames` enforces at runtime — every
base-game call site reading `Hero.exoSkeletonDataAsset` hardcodes
`"Stand"` and either `"Portrait"` or `"StandStatic"`, and throws an
uncaught exception deep in game code if missing (see
[`cross-references.md`](cross-references.md)) — surfaced here too, at
author time, so the rig builder sees it immediately instead of only after
assigning the rig to a real hero.

Combat clip names are a separate, **per-Model** list — see
`CombatSequenceNames` below — not part of this validator. Save backfills
them from Rest Pose via `EnsureRequiredClip` after validation runs.

## Combat sequence names — `Editor/CombatSequenceNames.cs`

The clip names combat actually `FindAnimationIndex`s are **not** a single
global list. Combat instantiates `CharacterProfile.Model` (written as the
`Model` KV, becoming `UnitDefinition.kind`) from the `units` bundle, then
`UnitViewExoSkeletonPatches` swaps the custom rig onto that prefab. Each
`ExoSkeletonUnitAnimationController` on the prefab has a `sequenceName`
(and optional `angledAnimations`) that is looked up by name in the swapped
asset; a miss throws from `StartAnimation` (`Can't play animation <controller.name>`).

Dumped 2026-08-12 from those prefab components:

- **HumanArcher** (Lab default): angled `Attack0/45/90/270/315` and
  `SpecialAttack0/45/90/270/315`.
- **ObeliskLvl4** (Onagro's Model): both the `Attack` and `SpecialAttack`
  *controllers* look up sequenceName **`SpecialAttack`** (no angle suffix).
  There is no `Attack` clip name on this prefab at all.
- **HumanGeraldLightSeeker**: un-angled `Attack` plus
  `SpecialAttackA/B/C`.

`ForModel(model)` returns the matching list, or the union of every dumped
template if the Model hasn't been dumped yet. `OnSaveClicked` backfills
that list (not the HumanArcher-only set) and **drops** leftover
`Attack0`/`SpecialAttack45`/… clips that `ForModel` does not ask for —
those were never required for ObeliskLvl4, but Save used to keep writing
whatever was already in memory. The Add Animation modal's presets are
`PresetsForModel` (that same list plus map-only `Portrait`), rebuilt
when the modal opens, not the union of every dumped prefab.
`AnimatorReadinessChecks` warns for any `ForModel` name missing from
`rig.json`.

## Combat sockets and clip events — `Editor/CombatPlaybackRequirements.cs`

Swapping the custom rig onto the Model prefab also means **attach points
and animation events come from the custom `rig.json`**, not from Obelisk's
(or HumanArcher's) original art. Vanilla frames always carry sockets
named `Head`, `Chest`, and `Base`. `onagro_missile` uses
`unitPosition(%SOURCE, #Head)` / `unitPosition(%TARGET, #Chest)`; dialog
bubbles look up `Head` via `DialogViewConfigComponent`. A miss does not
throw — `Unit.GetAttachPoint` returns `(0,0)` — but the cinematic logs
`Attach point name: Head not found` and projectiles spawn at the unit
origin.

Separately, `AbilityMeleeActivity` only runs `OnAbilityAction` (the
projectile) when the playing clip raises the exo-skeleton event
**`AbilityAction`**, and only ends the activity on **`AbilityEnd`**.
`ExoSkeletonUnitAnimationController` also names **`AbilityStart`** (the
comparisons there are no-ops). Those three are
`CombatPlaybackRequirements.KnownEventNames` — the Frame inspector
Events combobox. Any other string is forwarded as `OnAbilityCustomEvent`.
They are frame `events` strings, not ability KV fields. A clip that
plays but has empty `events` will spend AP and show the animation, then
hang until something else cancels the activity.

**Death is the same hang, with no timeout.** `DeathActivity` plays the
`Death` clip and only calls `DeathFinished` when the exo-skeleton raises
`AbilityEnd`. Its `Update` is empty — unlike a missing attack event,
there is nothing else to cancel the activity — so a Death clip with
empty `events` freezes the encounter. `NeedsAbilityEndEvent` covers
Death as well as the attack/spell set; Save backfills `AbilityEnd` on
the last authored frame. Death does not need `AbilityAction`.

The Frame inspector authors them as a combobox + Add + removable list
(not a comma-separated text field), so picking `AbilityAction` does not
wipe a second tag already on the frame. `OnSaveClicked` backfills missing
`Head`/`Chest`/`Base` from the rest-pose bounding box, and adds
`AbilityAction` (first authored frame) / `AbilityEnd` (last) on any clip
whose name starts with `Attack`, `SpecialAttack`, or `SpellCast`, plus
`AbilityEnd` on `Death`. The Frame inspector can still author real
positions and event timing; Save will not overwrite sockets or events
the user already placed.

## `Editor/AffineMatrixMath.cs`

`ComposeLinear(rotationDegrees, shearDegrees, scaleX, scaleY, out mA, out
mB, out mC, out mD)` — the rotation→shear→scale linear (2x2) composition
`M = R(rotation) * Shear(shear) * diag(scaleX, scaleY)`. Shared by two
call sites that would otherwise be two independent copies of the same
formula, free to drift apart: `RigEditorScene.ComputeFrameMatrix` (which
adds pivot/translation on top, to build the exported `rig.json` matrix)
and `DraggablePart`'s own live shear rendering (which only needs the
local 2x2 part — translation is handled by the GameObject's ordinary
`Transform.position`). `DecodeFrameMatrix`, the inverse (Gram-Schmidt on
the matrix's two columns), lives directly in `RigEditorScene` rather than
here — see [`rig-editor-scene.md`](rig-editor-scene.md)'s matrix
encode/decode section, and the `PartPose.Approximate` discussion above
for what it recovers exactly vs. approximately.
