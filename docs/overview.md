# LokrLab — Overview

The **first-party editor suite** — host shell plus Character, Ability,
and Encounter authoring. Project types still register on `LokrLabApi` so a
third-party editor can use the same contracts. This assembly owns the lab
scene, Project Browser, dock chrome, Character (Properties / Animator /
Sandbox), Ability Library (plus overlay fallback), and Encounter (Setup + Sandbox). Encounter design:
[`docs/encounter/`](encounter/overview.md); roadmap
[`docs/roadmaps/started/encounter-creator.md`](../../docs/roadmaps/started/encounter-creator.md)
(Phase 8 hover copy; **high with Harmony**).

**Screens today:**
- **Project Browser** — empty state; scan-and-open any `project.json` folder,
  or create a new project.   The list is grouped by project type, with a **Recent** section first
  (the five newest surviving folders; `-` unpins an entry from recents without deleting the
  project; `recent-projects.json` keeps the full history).
  **Show** toggles hide types (persisted in `HiddenProjectTypes`). **New Project**
  opens a wizard (every type with `CreateNew`, including Ability Library,
  plus that type's optional create sheet). Rows have a delete button (not
  singletons); File → Delete Project does the same for the open project.
  Extra folders come from each type's `ScanCategory`. This screen has the
  same File / Help menus as the shell. **File → Import Legacy Pack** (and
  the matching toolbar button) are always on this screen — you do not
  have to create or open a character first. The folder picker starts in
  `Mods/` (Musketeer, Official Pack, Onagro). **Close Lab** is on File
  and as a toolbar button.
- **Shell** — `UiDockSpace` chrome once a project is open. Workspace tabs,
  bottom panels, and inspector hosts come from the open project type.
  Left tabs: **Node Tree** and **File Tree**. Shell menus: File (Back /
  Close Project / Save / Import Legacy Pack / Close Lab), View (File Tree), Help
  (About). Project types add their own File / Edit / View items; each item
  is visible only in its own session / workspace.

Opened from the title-screen **Mods** button and from **`LokrModMenu`**
(**LokrLab**, BackQuote `` ` `` default; optional F3). Uses a **real scene
transition** — the origin scene is unloaded while the lab is open; closing
returns to where you came from.

## In this folder

- [`layout.md`](layout.md) — file structure
- [`architecture.md`](architecture.md) — scene transition lifecycle, Host binding
- [`conventions.md`](conventions.md) — structural patterns
- [`cross-references.md`](cross-references.md) — base-game dependencies

Character authoring: [`character/`](character/). Ability Library:
[`ability/`](ability/). Encounter (Phase 13 terrain catalog):
[`encounter/`](encounter/overview.md).

## Plugin metadata

`LokrLabPlugin.cs`: `Guid = "com.lokrmodding.lab"`,
`Name = "LoKR Lab"`, `Version = "0.12.107"`,
`[BepInDependency(LokrLabApiPlugin.Guid)]`,
`[BepInDependency(SimpleUIPlugin.Guid)]`,
`[BepInDependency(LokrModAPIPlugin.Guid)]`,
`[BepInDependency(LokrModMenuPlugin.Guid)]`,
`[BepInDependency(LokrCharacterLoaderPlugin.Guid)]`.

Bootstrap: `Awake()` → `Harmony.PatchAll()`,
`EmbeddedSceneHost.BindStatic()`,
`CharacterLabScene.BindLabApiNavigation()`, suite `ModMenuRegistration`,
Character, Ability, and Encounter project-type register, `EmbeddedFightHost.BindStatic()`,
Ability placeholders. The scene-load handler binds an additive embed, or
force-closes the lab / overlay on a Single load that destroyed it.
