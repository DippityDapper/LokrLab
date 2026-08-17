# LokrCharacterLab — Cross-references

## `LokrLabApi`

- `RegisterProjectType("character")` plus workspaces, node contributors,
  inspector drawers, bottom panels, persistent inspectors, File/Edit/View
  menus.
- `Host` — live lab scene (font, canvas, CloseTo, StartEmbeddedFight, …).
- `LabOpened` / `LabClosing` / `ShellShown` / `ScreenShown`.
- `JumpToProject` for AbilityRef activation.
- `PromptLegacyImport` / `RecentProjectFolders` / `ImportLegacyFolder`
  for the Project Browser.

## `LokrLab`

This module lives in the suite assembly. The shell assigns `Host` and
raises lab-scene events. `CharacterLabAccess` (open/close the lab) is
in the same plugin.

## `LokrCharacterLoader`

- `CharacterAPI.ReloadLabContent` from `LabContentReloader`.
- `CustomRigLoader` consumes the same `rig/rig.json` + part PNGs the
  Animator writes.

## `LokrModAPI`

- `ModAPI.Files.EnumerateCategorySubfolders("LokrCharacterLab")` for
  migration and Project Browser `ScanCategory`.

## `SimpleUI`

- Dock content builders return `GameObject` / take `Transform`.
- File Browser and Properties forms use `UiStack` / `UiList` / comboboxes.

## Ability module

Peer project type in this assembly. Character AbilityRef nodes jump via
`LokrLabApi.JumpToProject(AbilityLibraryTypeId, …)`.
