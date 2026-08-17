# LokrCharacterLab — Architecture

## Suite module, contracts on LabApi

`LokrLab` owns the lab scene, Project Browser, dock chrome, and this
Character module. The module registers the `character` project type on
`LokrLabApi` and talks to the live scene through `LokrLabApi.Host` and
the `LabOpened` / `LabClosing` / `ShellShown` / `ScreenShown` events so
a third-party type can use the same contracts.

`Lab.cs` is an internal facade over `Host` so existing Editor code can keep
calling `Lab.ActivateWorkspace` / `Lab.CloseTo`.

## Lab-scene hooks

`CharacterProjectType.Register()` (suite `Awake`) registers Properties
categories so the Node Tree has General / Hero Roster / … as soon as a
new character is created, not only after `LabOpened`. It also assigns
`BuildCreateSheet` / `CommitCreateSheet` (name, slug, alias, description, role). A new
character is a blank slate: empty model, skills, tags, and sounds; the
wizard name is what the tree and game show. Folder scaffolding
(`character.json`, empty `rig.json`, `rlheroes.txt`, roster, portraits
folder) still runs in `HomeWorkstationScene.OnCreateCharacterConfirmed`.

`CharacterLabHooks.OnLabOpened` runs after the shell builds chrome:

- File Browser + menu-bar popups on the canvas
- Home / Load screen content (legacy hubs; Home is retired as a destination)
- Built-in `CharacterCreatorAPI` workstations (Properties / Animator / Sandbox)
- `PromptLegacyImport` (assigned at plugin load) / `RecentProjectFolders` for File → Import Legacy Pack
- `EmbeddedFightHost.BindHost` so Ability Lab can start a fight in the
  Sandbox hole without referencing this DLL. Scene load / hole crop / HUD
  fit are `Host.StartEmbeddedScene` (LokrLab); this plugin only validates
  the unit, sets up the ephemeral quest, and spawns via `SandboxRoster`.

`OnLabClosing` flushes focused Properties fields, stops a leftover sandbox
embed, and drops widget refs. Content reload is **not** in this handler —
`CharacterLabScene.CloseTo` runs `TryAutoReloadOnLabClose` after LabClosing
returns so a ResetSession throw cannot skip localization. See
[`override-description-needs-restart.md`](../../../docs/issues/resolved/override-description-needs-restart.md).
`OnShellShown` refreshes Home/checklist after leaving the Animator.
`OnScreenShown("Browser")` flushes fields then resets Properties hosts — Close Project
destroys InspectorDock persistent hosts without raising `LabClosing`, and
the next Load must not refresh the destroyed lists.

## Persistent inspectors

The shell Inspector gives each `PersistentInspectorRegistration` its own
Grow() scroll host and must not nest another ScrollRect. Character registers:

- `"properties"` — `PropertiesCategoryHost` for `PropertiesCategory` nodes
- `"animator-live"` — `InspectorPanel.BuildInto` while the Animator is live
  and a Part / Clip / Frame / Reference is selected; `Refresh` ticks playback

## Selection and activation

`OnSelectionChanged` syncs Node Tree Part/clip picks into `RigEditorScene`.
`OnNodeActivated` jumps AbilityRef rows into the Ability Library via
`LokrLabApi.JumpToProject`.

Animator cameras live on `AnimatorRuntimeRoot`, not under the shell UI.
`OnDeactivated` / Close Project call `SetRuntimeActive(false)` so those
cameras stop drawing before the Project Browser is shown.

## Menus

Character File / Edit / View items register `isVisible` so they only appear
in the matching workspace: Save / Import / Slice, frame edits, Timeline,
and Preview on Animator; Checklist on Properties; History and Sandbox on
any Character session. Ability Lab's New Ability is not shown here.

## Sandbox fight

Sandbox Start sandbox calls `LabHost.StartEmbeddedFight` with a
`SandboxHole` `RectTransform` — the same additive fight Ability Sandbox
uses. A Level dropdown picks the hero rank (`nextLevelArchetype` chain).
`GrantProgressionSkills` grants passives plus one interactive pick per
rank from 1 through that level (five interactive skills max). Stop
unloads the hole, resets `InterfaceDataRepository`, clears stacked
ephemeral quests, hides debug UI, and resets camera drag. Fight-end does
not call `ReopenAfterFight`. After Stop, the lab scene stays active, so
the next fight's HUD can spawn there; hex input must not treat skill /
confirm `Icon` or `EndTurn` hits as empty hole. Confirm canvases keep
`WorldSpace` and are rebound to the hole camera from
`TargetInteractionView.Awake` and `EnsureFightInput` (`BindConfirmCanvases`).
`EmbeddedFightStagePatch` keeps Stage / initiative HUD alive in the
ephemeral embed. `SkillsBarSlotCap` trims the bar to five hex slots.
