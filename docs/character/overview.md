# LokrCharacterLab — Overview

The **Character project type** inside the LokrLab suite — Properties,
Animator, and Sandbox. Ability Library is a peer project type in the same
assembly. Opposite of `LokrCharacterLoader`, which loads finished mod
content at runtime.

Public APIs stay in namespace `LokrLab` (`CharacterCreatorAPI`,
`CharacterLabOptionsAPI`).

**Workspaces:**
- **Properties** — roster, localization, states, skills; fields live in the
  shell Inspector via a persistent category host
- **Animator** — rig editor, timeline, preview PIP
- **Sandbox** — in-shell fight hole; Start sandbox embeds the same
  `fighttesterempty` fight Ability Sandbox uses. Stop unloads it.

Opened through the LokrLab Project Browser (or File once a project is open).
The shell itself is still opened from the title-screen **Mods** button and
`LokrModMenu` (**LokrLab**).

## In this folder

- [`architecture.md`](architecture.md) — registration, lab-scene hooks, persistent inspectors
- [`layout.md`](layout.md) — file structure
- [`supporting-classes.md`](supporting-classes.md) — panels, input, grid, undo/redo
- [`rig-editor-scene.md`](rig-editor-scene.md) — `RigEditorScene.cs` orchestrator
- [`character-importer.md`](character-importer.md) — import from shipped characters
- [`animation-data-model.md`](animation-data-model.md) — clips, easing, timeline
- [`conventions.md`](conventions.md) — structural patterns
- [`cross-references.md`](cross-references.md) — neighboring plugins

## Plugin metadata

Character Lab is a module of `LokrLabPlugin` (`com.lokrmodding.lab`,
0.12.107). `LokrCharacterLabPlugin` is a facade for Log / Guid only.

Config: `LiveReload.AutoReloadOnLabClose` (default true) — persist + reload
on lab close (skipped during combat).

Bootstrap: `Awake()` → subscribe `LabOpened` / `LabClosing` / `ShellShown` /
`ScreenShown`, `CharacterProjectType.Register()`, migrate `project.json`
onto existing LokrCharacterLab folders, `EmbeddedFightHost.BindStatic()`.

New characters copy `Placeholders/rig.json`, `body.png`, and `portrait.png`
from this plugin folder, and reference Ability Lab's current placeholder
ability ids (the leftover stems, or the `slug_token` folders after Rename)
so the hero room has every `skillProgression` key it indexes.
