# LokrLab — Architecture

This assembly is the **editor host**: scene transition, Project Browser,
dock chrome, `LokrLabApi.Host`, and generic scene-in-hole embed.
Character authoring (Animator, Properties, Sandbox fight) lives in
`LokrCharacterLab`. After chrome is built, `CharacterLabScene` assigns
`Host` (including `StartEmbeddedScene`) and raises `LabOpened`;
`CloseTo` / `ForceClose` call `EmbeddedSceneHost.Stop`, raise
`LabClosing`, and clear `Host`.

## Scene embed

`EmbeddedSceneHost` loads a bundle scene additively
(`AssetBundleManager.LoadScene(..., true)`), finds `Camera.main` (then
the first camera in that scene — do not wait on `CameraBase`), and crops
`Camera.rect` to the caller's hole via `EmbeddedSceneBinder`
(`DefaultExecutionOrder` 32767). `EmbeddedSceneHudFitter` (32000) remaps
non-lab Overlay canvases onto that camera with `ConstantPixelSize`
scaled to the last hole rect, writing only when values change. FadeScreen
is skipped (DDOL loading chrome); Stop snapshots and restores its Overlay
mode so sandbox zoom cannot scale the next `ShowFadeOut`.
`AspectUtility.SetCamera` is skipped while the embed is live. Over the
hole, `EmbeddedSceneInputPatch` drops lab Overlay hits only (empty fight
canvases stay so LeanTouch does not `First()` an empty RaycastAll).
`IsPointerOverGameObject` / `CheckIfTouchedOnUI` still treat empty hole
as not-UI so hex taps work. Fight HUD (`Icon`, `EndTurn`, `UIOptions`, `UISimpleModalDialog`,
`Selectable`, pointer handlers) is never treated as lab Overlay, even when it
instantiated into the lab scene after Stop left the lab active.
`EmbeddedSceneHudFitter.FitCanvas` saves and restores `Camera.rect` when
assigning `renderMode`; a visible `UISimpleModalDialog` is then stacked
above settings (`overrideSorting` 500, closer `planeDistance`, last
sibling). That remap is not enough when settings and confirm share a
canvas: `Apply` mutes every `UIOptions` `CanvasGroup` (`alpha` 0, no
raycasts) while the confirm is visible, restores those fields on
dismiss, and reparents the modal off a `UIOptions` parent first so
muting the sheet cannot hide Yes/No. A one-shot log dumps the confirm
hierarchy. Do not `CloseWindow` settings (that can drop the forfeit
callback). See
[`sandbox-forfeit-confirm-behind-settings.md`](../docs/issues/unresolved/sandbox-forfeit-confirm-behind-settings.md).
`EmbeddedSceneBinder` re-applies the hole crop
afterward. The gameplay camera is tagged `MainCamera`. Extra EventSystems
/ cameras / AudioListeners in the loaded scene are disabled on bind
(`FitScene`), then `LabEventSystem` is toggled so it stays
`EventSystem.current`. `EmbeddedSceneHexGridPatch` parents `HexGridRoot`
under the fight board at Awake; Stop still destroys strays. Stop freezes
the additive roots (including DOTween on every child), unbinds the hole,
clears AspectUtility's cached cameras, destroys leftover `HexGridRoot`
objects and leaked lab-scene fight HUD (`Icon`/`EndTurn` canvases,
`SkillsBar` / `SkillInfo` roots), clears fight MonoSingletons
(`HexBoardViewComponent`, `CameraBase`, `LevelManager`,
`StageControllerComponent`, `PowerBarsVisibility`), restores FadeScreen
to Overlay and the lab backdrop as `MainCamera` (untags the hole camera
first), then unloads asynchronously. A load
that finishes after Stop is unloaded instead of left behind. A new Start
waits for that unload so two fight scenes cannot overlap.
`CameraBase.mainCamera` is rebound to the hole camera in
`EmbeddedSceneCameraBasePatch` (Awake) and `BindCameraBaseMain` (FitScene).
Stop must not call `ReopenAfterFight`. Host also forwards
`StartEmbeddedFight` / `StopEmbeddedFight` (implemented in
`LokrCharacterLab`).

The hole must have a real height (`Grow()`, `minHeight`, no
`ContentSizeFitter`). A collapsed hole leaves the gameplay camera
fullscreen. `LokrLabPlugin.OnSceneLoaded` ignores `LoadSceneMode.Additive`
for ForceClose — that handler is only for Single loads that destroy the
lab. `EmbeddedSceneWatchdog` on the hole retries bind if `sceneLoaded`
was missed (otherwise the hole shows the black backdrop and the fight
stays fullscreen behind the lab).

`InspectorDock` loops `PersistentInspectors` (one Grow() host per
id; `Scrollable` false when the inner form owns the ScrollRect).
`LabShell` calls `OnDeactivated` / `OnSelectionChanged` /
`EnterViaSceneTransition` on the open project type. Close Project calls
`LabShell.UnloadProject` so Animator cameras and the center viewport are
torn down before the Project Browser is shown.

## `Patches/UIMainMenuPatches.cs` — adds the title-screen Mods button

Despite the file name, this patches **`UIMainScreen`**, not `UIMainMenu` —
`UIMainScreen` is the actual title screen (Start/Credits buttons) shown
right after boot; `UIMainMenu` is a different class for the deeper hub
screen reached only after Start → save-slot selection. (Do not confuse
this with `LokrEncyclopedia`'s own, identically-named
`Patches/UIMainMenuPatches.cs`, which really does target `UIMainMenu` —
see [`cross-references.md`](cross-references.md).)

`[HarmonyPatch(typeof(UIMainScreen), "Start")]`, postfix:

- Clones the existing `credits` button (rather than building one from
  scratch) so the new button inherits the game's real sprites/animator/
  font instead of looking like a placeholder.
- Two non-obvious fixes baked in as comments, found by reading decompiled
  source rather than guessing:
  1. Button labels are `TextMeshProUGUI` + a `LocalizationComponent`, and
     `LocalizationComponent.Start()` runs **one frame after** `Instantiate`
     and unconditionally reapplies whatever `localizationKey` got copied
     from the source — so a naive text edit right after `Instantiate` gets
     silently stomped back to "Credits" a frame later. Fix: clear
     `localizationKey` and call the component's own `SetFinalText(...)`.
  2. The real button container is a GameObject named `"MainButtons"` —
     found via `GameObject.Find("MainButtons")` rather than assuming the
     clone's parent is correct.
- Repositions the clone 120px below the source button (`RectTransform`
  when present, falls back to `transform.localPosition` otherwise).
- Replaces `button.onClick` with a **new** `Button.ButtonClickedEvent()`
  (rather than `RemoveAllListeners()`, which only clears runtime
  listeners, not the persistent "ShowCredits" call wired in the Editor
  Inspector) and adds a single listener: `ModMenuAPI.Toggle()`.
- Wrapped in try/catch, logging errors rather than throwing — a failure
  here shouldn't take down the title screen.

## `CharacterLabScene.cs` — real scene transition (2026-08-12, replaced the old overlay model)

Static class that opens/closes the lab via a **real scene transition** — the scene the player was in is genuinely unloaded (`SceneManager.UnloadSceneAsync`), the lab is built as its own scene (`SceneManager.CreateScene("LokrLab")`, rebuilt fresh each time it's entered, not kept alive for the whole session), and closing transitions back to whichever real scene it was opened from. Reuses the base game's own transition primitives: `FadeScreen` for the visual fade (there is no separate loading-screen/progress-bar asset in this game — every real scene transition is just this fade) and `TransitionSceneComponent.TransitionToNextScene` (the base game's real scene-loading mechanism) for the return leg, since that direction's destination is always a real, Build-Settings-registered scene name. The reverse direction (entering the lab) can't use `TransitionSceneComponent` as the engine — it only resolves scenes by name against Build Settings, and the lab's own `CreateScene`-built scene was never registered there — so entering is driven by hand (fade, then explicit `UnloadSceneAsync`).

- `Open()`: captures `SceneManager.GetActiveScene().name` as `originScene`, fades out, builds the lab scene *before* unloading the origin scene (Unity disallows unloading the only remaining loaded scene), unloads the origin scene, activates lab roots, fades back in, then shows the **Project Browser**. Close Lab clears `CurrentSession`; a pending fight reopen can still `SwitchToShell` when a session and `pendingWorkspace` remain. See [`../../docs/issues/resolved/lab-reopen-loading-screen-stuck.md`](../../docs/issues/resolved/lab-reopen-loading-screen-stuck.md).
- `Close()`: prompts via `LabSaveUx` when the session is dirty, then `EmbeddedSceneHost.Stop`, `LabClosing`, **content reload** (`LabContentReloader.TryAutoReloadOnLabClose`, try/catch so a teardown throw cannot skip it), clear `Host` / `CurrentSession` / shell widget refs, then transition back to `originScene`. `CloseTo(sceneName)`: same return path to an arbitrary real scene (also gated when dirty). Character widget cleanup (`RigEditorScene.ResetSession`) still runs from Character `LabClosing`; localization reload is owned by `CloseTo`. See [`../../docs/issues/resolved/override-description-needs-restart.md`](../../docs/issues/resolved/override-description-needs-restart.md).
- `ForceClose()`: `Stop`, optional `LabClosing`, `Host = null`, hide or reset lab roots — no `RigEditorScene` call here.
- The old "block/restore foreign EventSystems/cameras/canvases" isolation hack (and `CharacterLabLeanTouchIsolation`, now deleted) no longer exists — there's nothing foreign left to isolate once the origin scene is actually unloaded rather than just hidden underneath.
- `CharacterLabLayers` (layer 31): viewport cameras only draw lab rig content — prevents main-menu particles/UI bleeding into rig editor viewports. Unaffected by the above; still relevant since it's about the lab's *own* viewport cameras, not foreign-scene isolation.
- Screens: **Browser** (Project Browser empty state; projects grouped by type with Show-type filters; Ability Library is many named libraries), **Shell** (`UiDockSpace` with workspace tabs, Node Tree, File Tree, Inspector, hover-info strip, and the open type's bottom panels). Character workspaces: **Properties**, **Animator**, **Sandbox**. Ability Library workspaces: **Library**, **Sandbox**. Encounter workspaces: **Setup**, **Sandbox**. Left tabs: Node Tree / File Tree. Bottom tabs rebuild when `CurrentSession.ProjectTypeId` changes — Character hosts Timeline / Checklist / History (`isRelevant` auto-focuses among those, never hides them); Ability Library registers none and the zone collapses. Menus: File / Edit / View / Help on both the Project Browser and the shell, plus File → Save (Ctrl+S) / Back to Previous Project after `JumpToProject` and File → Import Legacy Pack (always; picker starts in `Mods/`). Dirty Animator or Ability edits set `session.IsDirty`; the LoKR Lab title and status bar show `*`; Close Lab, Close Project, and jump prompt save / discard / cancel. Items use `IsVisible` so they appear only in their session / workspace (Animator File/Edit/View items on Animator, Checklist on Properties, New Ability on the Ability Library, shell Close Project / File Tree when a project is open). Empty top-level menus are hidden. Character Node Tree includes an Abilities reference branch; jumps switch session (not split-view). **Home** is retired (`SwitchToHome` → shell). Sandbox and Ability Lab Stage fights are additive hole embeds (`StartEmbeddedFight`); fight-end does not call `ReopenAfterFight`. Close Lab still returns to the pre-lab origin. `SwitchToLoad()` aliases to the Project Browser.
- `CharacterCreatorAPI.RegisterWorkstation` still drives the old hub, and also forwards a `WorkspaceRegistration` onto the Character project type so the new API is dogfooded.
- `SwitchToWorkstation` (since 2026-08-12, pre-redesign audit C-04) enforces `WorkstationEntry.RequiresCharacterLoaded` server-side — redirects to Home with a logged warning instead of building/showing a gated workstation (e.g. Animator) with no character loaded. Previously this was only advisory, enforced by `HomeNavPanel.Refresh` hiding the nav button; `CharacterCreatorAPI.RegisterWorkstation` is a public extension point, and `SwitchToWorkstation` itself has no other caller-side guard, so a caller reaching a gated workstation some other way used to hit `OnLoadClicked(null)` (harmless — `Directory.Exists(null)` just returns `false`) but leave an empty, confusing editor rather than the "load a character first" experience this gating promises everywhere else.
- Entry: title-screen button (`UIMainMenuPatches`) and **`ModMenuAPI`** (`ModMenuRegistration`).

See also [`../../LokrModMenu/docs/overview.md`](../../LokrModMenu/docs/overview.md) and [`../../docs/roadmaps/started/live-reload.md`](../../docs/roadmaps/started/live-reload.md).

## UI construction — `SimpleUI`

The editor builds all of its UI on `SimpleUI` (referenced assembly
`SimpleUI.dll`): a fluent, chainable widget library (`UiPanel`, `UiStack`,
`UiSplit` for row/column layout, `UiLabel`, `UiButton`, `UiTextField`,
`UiModal`, `UiList<T>`, and others for controls). SimpleUI 1.2.4 also
ships the Phase 1 docking set (`UiDockSpace`, `UiSplitter`, `UiTabGroup`,
`UiTree`, …) plus `UiFileBrowser`. Phase 2 wired `UiDockSpace` into the real shell
(`Shell/LabShell.cs`). Phase 3 fills the Node Tree from the open project
type's `NodeTreeContributors` and writes
`LokrLabApi.Selection`. Phase 4's `InspectorDock` dispatches
`FindInspectorDrawer` / `FindInspectorSections` for the selected
`LabNode.Kind`. The Animator's `InspectorPanel` keeps its persistent
four sections (row reuse / playback-tick refresh / per-field focus-skip)
and maps `InspectorTarget` onto the same kind strings so extra
`RegisterInspectorSection` contributions can stack without rebuilding
those widgets every tick. Workspace tabs: Properties hosts category
panels in `PropertiesCategoryHost`; Animator binds viewport cameras to
the center dock (`ViewportCameraBinder`) — edit camera full-bleed, in-engine
preview as a toggleable bottom-right overlay — and shows the live
`InspectorPanel` while a Part/Clip is selected. Phase 6 bottom panels
(Timeline / Checklist / History) register through
`RegisterBottomPanel`; File / Edit / View / Help through
`LokrLabApi.RegisterMenu`. Phase 7's File Tree is a second left-dock
tab (`FileTreePanel`) — disk listing, not a merge with the Node Tree;
`UiTree.SetReorderable(false)` so drag does not reparent files.
The main canvas
(`Canvas` at the root of the scene), the top-level panel hierarchy
(Inspector, Timeline, Viewport regions), and every within-panel row
(`MenuBarPanel`, `ToolbarPanel`, `InspectorPanel`, and the other
hand-built sidebar panels) are all built with `SimpleUI` widgets. See
[`../../SimpleUI/docs/overview.md`](../../SimpleUI/docs/overview.md).
Every `SimpleUI` widget is created via `.Create()` factory methods and
composed via `.Add()` calls, so sizing is responsive and layout is
deterministic.

A handful of canvas-level chrome elements (`CharacterLabScene`'s own
title label/close button, `HomeWorkstationScene`'s status label,
`RigEditorScene`'s viewport-region labels, `PropertiesWorkstationScene`'s
Home button) need a single absolute anchor point on the canvas rather
than a layout-group-relative position. These are built the same way as
everything else — `UiLabel.Create`/`UiButton.Create` — and then have
their `RectTransform.anchorMin`/`anchorMax`/`sizeDelta` set directly
afterward (`UiElement.RectTransform` is public for exactly this reason).
There is no longer a separate hand-rolled helper class for this — the
pre-redesign audit's `EditorUiHelpers.cs` (2026-08-13) turned out to be
entirely dead code once every panel it was originally written for had
already migrated to real `SimpleUI` widgets, and `CharacterLabScene`'s
own `CreateLabel`/`CreateButton`/`CreateInputField` trio was down to a
handful of call sites; both were removed in favor of the pattern above.
See [`supporting-classes.md`](character/supporting-classes.md) for details.

## Character authoring (moved)

Animator viewport layout, Mass Edit, and `RigEditorScene` details live in
[`character/architecture.md`](character/architecture.md)
and [`character/rig-editor-scene.md`](character/rig-editor-scene.md).
The sections below are historical notes from when those types lived in this assembly.

## Viewport layout

Every top-level screen-fraction `Rect` this editor's UI and cameras lay
out against — `MenuBarRegion`, `ToolbarRegion`, `SceneTreeRegion`,
`MainViewportRegion`, `PreviewViewportRegion`, `InspectorRegion`,
`TimelineRegion`, and every modal popup region — is centralized in
`Editor/EditorLayout.cs`, not scattered across `RigEditorScene`.
`RectTransform.anchorMin/Max` and `Camera.rect` share the same 0–1,
bottom-left-origin convention, so one constant describes both a panel's
screen position and (for the two viewport cameras) its clip rect.
`EditorLayout`'s own doc comment explains why this centralization exists:
a real earlier bug shrank the viewport region and grew the toolbar region
by the same amount without noticing that the viewport's own label was
positioned in a gap *above* the region rather than inside its own top
edge — both moved together and landed on top of each other. Every
region-dependent position here (e.g. `ViewportLabelInset`) is now computed
from the same constant its own region uses, making that class of bug
structurally harder to reintroduce.

Three cameras exist, built in `RigEditorScene.Build`:

- The scene's original camera (from `CharacterLabScene.BuildCamera`) is
  repurposed as a **full-screen backdrop** — `cullingMask = 0` (renders
  nothing, just clears the whole screen to the background color) so
  nothing bleeds through the margins around the two real viewports.
- `mainViewportCamera` — a new camera clipped to
  `EditorLayout.MainViewportRegion`, renders `loadedParts` (the editable
  rig). Becomes the new `RigEditorScene.ActiveCamera`.
- `previewCamera` — a new camera clipped to
  `EditorLayout.PreviewViewportRegion`, positioned to look at
  `previewRoot`, renders the real in-engine `ExoSkeletonRenderer` preview.

This three-camera split exists because an earlier version repurposed the
*one* scene camera for the main viewport directly, which left the
margins/corners never cleared by any camera, letting the hidden main-menu
scene bleed through.

### Keeping the two viewports apart

Neither viewport camera has a culling mask separating it from the other's
content — both render the same scene. Two independent mechanisms keep
them from ever showing each other's parts, and **both** are load-bearing
now, not just the first one:

1. **A large, fixed world-space offset.** `previewRoot` sits at world
   position `(100, 0, 0)`; `partsRoot` sits at world identity. 100 units
   is comfortably larger than any realistic rig's extent.
2. **Independently-clamped camera bounds.** Both the Main Viewport and
   Preview cameras now support real middle-mouse pan and scroll-wheel
   zoom-to-cursor (`EditorInputController` — see
   [`supporting-classes.md`](character/supporting-classes.md)), not just the fixed
   framing this tool originally shipped with, so "the camera never moves
   far enough to see past the offset" is no longer true by default.
   `EditorInputController` clamps each camera's *center* to a
   `PanBoundsExtent` (30-unit) box around its own home position after
   every pan and zoom, and clamps `orthographicSize` to
   `[MinOrthoSize, MaxOrthoSize]` (0.2–30). The worst case — panned to the
   box's edge *and* zoomed all the way out — still lands with real margin
   short of the other viewport's content (see `PanBoundsExtent`'s own
   doc comment for the arithmetic). Neither mechanism alone would be
   sufficient once pan/zoom exists: the offset alone assumes a camera
   that never moves; the clamp alone (with no offset) would still let a
   camera's *own* frustum reach the other root at (0,0)-relative
   coordinates. Together, neither viewport can ever show the other's
   content regardless of how far the user pans or zooms.

Preview's own camera has no auto-fit or re-centering — that was tried and
removed (bounds computed off `ExoSkeletonRenderer`'s mesh weren't
reliable enough; manual pan/zoom alone works better). Its pan/zoom home
position is just wherever the camera started at scene build time, seeded
once on first use.

### Background reference grid

`ViewportGrid.Build` (`Editor/ViewportGrid.cs`) draws a faint scale
reference grid behind each viewport's own content: one call parented to
`partsRoot` with a 70-unit half-extent (covering the Main Viewport's full
pan+zoom reach), one parented to `previewRoot` with a 10-unit half-extent
(Preview's own reach is fixed and smaller). It's a hand-built
`MeshRenderer` quad with UV-tiled coordinates — not `SpriteRenderer`'s
`SpriteDrawMode.Tiled` and not `GL` immediate-mode drawing, both tried
first and rejected for reasons specific to this game's runtime (a
runtime-created `Sprite.Create` texture didn't actually tile at any zoom
level; `GL` drawing depended on camera render-callback timing the game
didn't reliably provide) — see `ViewportGrid.cs`'s own doc comment for
the full account, and [`supporting-classes.md`](character/supporting-classes.md)
for more. Rendered at `sortingOrder = -32000` (far below any real part's
own sorting order — see `DraggablePart.StaticLayer`), so it's always
background regardless of what's loaded.

### Scale-reference overlays

`Add Reference` (toolbar and Edit menu) spawns a `ReferenceCharacter` in
the Main Viewport -- a whole shipped character rendered through the same
`ExoSkeletonRenderer` path Preview uses, parented at world origin so the
edit camera sees it. Default is Gerald
(`ExoSkeletonHumanGeraldLightSeeker_MetaDataAsset`). The overlay is one
`BoxCollider` covering the posed mesh, so a click selects the character
as a unit; Move (W) and Rotate (E) act on that whole transform, Scale
and Pivot refuse with a status message. Overlays are editor-only (never
written to `rig.json`, not in undo history) and sit at
`sortingOrder = -1000`, above the grid and below real parts, so an
overlapping part still wins a click. Selecting one shows a Reference
section in the Inspector (character, pose, position, rotation, opacity).

## State machine

```csharp
internal const string SelectToolName = "Select";
internal static string CurrentToolName { get; private set; } = "Move";
```

The old hardcoded `EditMode` enum (`Select, Move, Rotate, Scale`) is
gone — replaced by a tool **registry**, `AnimatorToolRegistry`
(`Editor/AnimatorToolRegistry.cs`), so adding a new drag tool is a new
`IAnimatorTool` registration rather than a new `switch` case throughout
`EditorInputController`. `Select` is the one mode that isn't a registered
`IAnimatorTool` at all — it doesn't drive a drag, it drives click-to-select
(see "Selection, visibility, draw order" in
[`rig-editor-scene.md`](character/rig-editor-scene.md)), so there's nothing for a
tool implementation to do.

`AnimatorToolRegistry.RegisterDefaults()` registers five first-party
tools (`Editor/AnimatorTools.cs`), each with its own hotkey:
`MoveTool` (W), `RotateTool` (E), `ScaleTool` (R, uniform),
`ScaleXYTool` (Y, independent X/Y), `PivotTool` (T, edits
`RestPose.PivotOffset` on a single part, or the session temp group
pivot when more than one part is selected). `Q` switches to
`Select` directly; every other hotkey is matched by looping
`AnimatorToolRegistry.Tools` and comparing `IAnimatorTool.Hotkey` — see
`EditorInputController.Update` in
[`supporting-classes.md`](character/supporting-classes.md). Every tool rotates,
scales, and (for XY) shears around the part's own `RestPose.PivotOffset`,
not its rendered center — see the pivot math in
[`rig-editor-scene.md`](character/rig-editor-scene.md)'s Matrix section.
`IAnimatorTool.AllowsAffinePose` gates whether a tool can still act on a
part currently shown through `DraggablePart`'s read-only affine-mesh
display (a genuinely degenerate source matrix — see [`rig-editor-scene.md`](character/rig-editor-scene.md)'s
Matrix section); true only for `PivotTool`, since a part's pivot stays
meaningful regardless of whether the frame being viewed happens to be
undecomposable.

Dragging is still **not** driven by per-object mouse-hover events — see
`EditorInputController` in [`supporting-classes.md`](character/supporting-classes.md)
for why and how.

## Commit/apply pose-context pattern

`activeClip == null` means "editing Rest Pose" (the tool's original,
pre-animation behavior). `CommitCurrentPoseToActiveContext()` writes
whatever the parts' live transforms currently show into whichever context
was active *before* a switch; `ApplyContextPoseToParts()` then applies the
*new* context. Every place that changes `activeClip`/`activeFrameIndex`
(`SelectRestPose`, `SelectClip`, `ScrubToFrame`, `ScrubToBakedFrame`,
`TickPlayback`) calls commit-then-apply in that order, so dragging never
needs to sync anywhere except at these switch points and at Save.

A part actively being dragged (`ActivelyDraggingPart` for a single-part
drag, or the `activelyDraggingGroup` set for a Move-tool group drag) is
skipped by every `ApplyContextPoseToParts`/`ApplyPoseFrameToParts` write
for the whole duration of the drag — otherwise a context reapplication
mid-drag (most concretely: `TickPlayback` advancing the active frame
while Mass Edit, below, deliberately keeps playback running through an
edit) would stomp the drag's live, not-yet-committed transform.

Clip/frame switches (`SelectClip`, `SelectRestPose`, `ScrubToFrame` /
`ScrubToBakedFrame` when the index changes, and `[` / `]`
`MoveActiveFrame`) commit the old context first, then call
`EditorInputController.CancelActiveDrag` so the following apply writes
the new pose onto the dragged part. Mouse-up skips
`CommitCurrentPoseToActiveContext` when `PoseContextGeneration` no
longer matches the value recorded in `TryBeginDrag`, unless Mass Edit
is on (playback must still commit). `TickPlayback` does not cancel the
drag. See
[`animator-pose-leaks-across-frames.md`](../docs/issues/unresolved/animator-pose-leaks-across-frames.md).

`CommitCurrentPoseToActiveContext` additionally guards against reading a
**baked** (interpolated) sub-frame back in as if it were an authored
pose — see "Easing and baked frames" in
[`rig-editor-scene.md`](character/rig-editor-scene.md) for the data-corruption bug
this closes and the fuller `activeBakedIndex`/`PoseFrame.BakedFrames`
architecture it's part of. When the active context is Rest Pose, a
rest-position move also compensates existing clip deltas so Walk /
Attack keep their world positions
([`animator-feel.md`](../../docs/roadmaps/completed/animator-feel.md)
Phase 2).

### Mass Edit

A session-wide toggle (`RigEditorScene.MassEditEnabled`, flipped from
`ToolbarPanel` -- it is a global editing mode, not a per-part Inspector
property) that changes what a **commit** does, not what a **drag** does:
while on, committing an edit to every currently multi-selected part also
nudges every *other* frame of the active clip's stored pose for each of
those parts by the same relative amount (`PropagateMassEdit` -- a flat
delta for position/rotation/shear, a ratio for scale, so a frame with a
very different baseline scale can't get pushed to zero or negative). A
group Move/Rotate/Scale of a whole character with Mass Edit on therefore
applies across every frame of the clip, not only to `SelectedPart` (the
last-clicked member of the selection). It's one flag rather than a
per-part sticky setting.

Turning Mass Edit on also changes `PausePlayback`'s behavior: starting any
edit (`AnimatorHistory.CaptureBeforeChange`) normally pauses playback so
it can't fight the edit over the same part's transform; with Mass Edit
on, playback is deliberately left running instead, since
`ActivelyDraggingPart` / `activelyDraggingGroup` already protect the
part(s) actually being dragged, and the entire point of Mass Edit is to
watch the rest of the clip keep animating while nudging the selection
across every frame at once. Full detail — `MassRemovePartFromClip`,
`MassReplacePart` (a rig-wide part swap driven by `ReplacePartPickerPanel`)
— in [`rig-editor-scene.md`](character/rig-editor-scene.md).
