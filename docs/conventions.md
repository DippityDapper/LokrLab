# LokrLab — Conventions

Character authoring conventions (RigEditorScene, InspectorPanel, one-scroll
viewport for Properties/Animator) live in
[`character/conventions.md`](character/conventions.md).

- **Suite plus contracts.** This plugin registers Character, Ability
  Library, and Encounter on `LokrLabApi`. Third-party editors depend on
  `LokrLabApi`, not `LokrLab.dll`.
- **Static-class-as-controller for chrome.** `CharacterLabScene`,
  `LabShell`, `InspectorDock`, and the other `Shell/` types are static
  classes that build UI once. Project types parent popups from `LabOpened`.
- **All UI construction goes through `SimpleUI`, one paradigm.** Every
  panel builds its widgets with `SimpleUI` factories (`UiPanel`,
  `UiStack`, `UiLabel`, `UiButton`, `UiList<T>`, etc.). The handful of
  canvas-level chrome elements that need an absolute anchor point rather
  than layout-group-relative placement (`CharacterLabScene`'s title/close,
  `HomeWorkstationScene`'s status label, `RigEditorScene`'s viewport
  labels, `PropertiesWorkstationScene`'s Home button) still use
  `UiLabel.Create`/`UiButton.Create`, then set `RectTransform.anchorMin`/
  `anchorMax`/`sizeDelta` directly afterward — `UiElement.RectTransform`
  is public for exactly this. `EditorUiHelpers.cs` (a second, unused
  helper set) and `CharacterLabScene`'s old hand-rolled
  `CreateLabel`/`CreateButton`/`CreateInputField` were removed 2026-08-13
  once every real caller had already migrated to `SimpleUI`. See
  [`supporting-classes.md`](character/supporting-classes.md).
- **Workspaces, not Home.** Properties and Animator are
  `WorkspaceRegistration` tabs. `SwitchToHome` aliases the shell.
  Checklist is the Checklist bottom panel (Phase 6). File Tree (Phase 7)
  is the on-disk folder listing; Node Tree stays the logical project.
- **Two inspectors, one kind string.** The shell `InspectorDock` rebuilds
  registered drawers when selection identity changes. The Animator's
  `InspectorPanel` keeps persistent Part / AnimationClip / Frame /
  Reference widgets and refreshes them in place (row reuse, per-field
  focus-skip) on every playback tick. Both use the same `LabNode.Kind`
  strings (`CharacterNodeKinds`). Extra `RegisterInspectorSection`
  widgets rebuild only when kind+id changes, never on a tick. See
  [`supporting-classes.md`](character/supporting-classes.md).
- **One scroll viewport in the Inspector.** A scrollable `UiStack` /
  `UiList<T>` reports no preferred height. Nesting one inside
  `InspectorDock`'s Grow() scroll host (or inside a category section that
  itself sits in that host) collapses the form to zero height — fields
  are built, the panel looks empty. `drawerHost`, `propertiesHost`, and
  `animatorHost` are the only `ScrollRect`s. `PropertiesCategoryHost`,
  `InspectorPanel.BuildInto`, `AbilityEditorPanel.BuildInto`, and every
  `Character*Panel` category list must stay `scrollable: false`.
  Horizontal timeline chip/clip rows may scroll because they also have
  `FixedHeight`. Same rule as SimpleUI
  [`conventions.md`](../../SimpleUI/docs/conventions.md) ("One scroll
  viewport"). Hit 2026-08-13 on Ability Library, Animator
  `InspectorPanel`, and Properties categories.
- **Global modes live on the toolbar, not in the Inspector.** Session-wide
  toggles and actions that are not a property of the currently inspected
  object (`MassEditEnabled`, `Add Reference`, tool mode, Undo/Redo) belong
  on `ToolbarPanel` (or the menu bar). `InspectorPanel` is for the
  selected part / clip / frame / reference only — putting a global toggle
  there hides it whenever nothing is selected, and implies it is a
  per-part setting. See [`supporting-classes.md`](character/supporting-classes.md).
- **Screen-fraction anchors shared between UI and cameras.** `Rect`s used
  for `RectTransform.anchorMin/Max` are the same values used for
  `Camera.rect` — both use a 0–1, bottom-left-origin convention, so a
  single set of layout constants describes both. These constants live in
  `EditorLayout.cs`, not scattered across the panels that consume them.
  See [`architecture.md`](architecture.md).
- **No schema changes, ever.** Every feature added to this editor
  (animation clips, easing, per-frame draw order, duplicate instances,
  shear display, custom pivots, authored-frame/easing round-tripping,
  scale-reference overlays) works within `ExoSkeletonDataAsset.ReloadData`'s
  existing JSON shape — nothing here has ever needed to extend `rig.json`'s
  own format. Editor-only data with nowhere to live in that schema (pivot
  offsets, authored pre-baking frame data) is persisted in small sidecar
  JSON files next to `rig.json` instead (`rig.pivots.json`,
  `rig.animsource.json`) — see [`rig-editor-scene.md`](character/rig-editor-scene.md).
  Scale-reference overlays are session-only viewport aids and are not
  persisted at all.
- **Decompiled-source-driven, not guessed.** Comments throughout cite
  specific base-game classes/methods (`ExoSkeletonRenderer.LateUpdate`,
  `ExoSkeletonAnimator.Start()`/`Update()`, `LocalizationComponent.Start()`)
  as the actual evidence for a given fix or design choice, not assumption
  — see [`cross-references.md`](cross-references.md).
