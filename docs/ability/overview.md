# LokrAbilityLab — Overview

An in-game visual editor over the existing `AbilitiesDefinitions` KV-text
ability system — a **peer project type** in the LokrLab suite, not a
Character workspace. A fallback lab scene still exists when the shell
jump hook is missing. Abilities stay a shared library referenced by id
from any character. See
[`../../../docs/roadmaps/completed/ability-lab.md`](../../../docs/roadmaps/completed/ability-lab.md).

## Scope: envelope + nested action cards

v1 shipped a structured envelope form plus a raw KV body box. Phase 4 of
the [overhaul](../../../docs/roadmaps/completed/ability-lab-overhaul.md)
replaced that box as the primary editor with **nested action cards**
(events as hats, actions as stacked SimpleUI cards). The on-disk format
is still `ability.txt`. Unknown blocks stay opaque cards / Advanced
remainder — not one whole-file textarea.

See [`editor-design.md`](editor-design.md) for the Phase 3 pick and the
v1 card set. Custom sprite FX / clips shipped in Phase 5 (0.5.0).
Phase 6 (0.6.0) uses the shell viewport: library browser, action-card
canvas, and a Sandbox host. Phase 8 (0.8.20) loads an embedded real
fight in the Sandbox hole. Used-by is scanned from Character folders even
before a save. If embed cannot start, Sandbox shows an error — there is
no mannequin fallback. Lua is a real Advanced card (Phase 7). UnitRef
pickers are filtered (Phase 9). Hover info lives above the status bar
(Phase 10).

## In this folder

- [`architecture.md`](architecture.md) — shell + fallback scene, body-tree
  load/save, shared form
- [`editor-design.md`](editor-design.md) — visual model (implemented)
- [`layout.md`](layout.md) — file structure
- [`classes.md`](classes.md) — every class and what it owns
- [`conventions.md`](conventions.md) — plugin shape and public surface
- [`cross-references.md`](cross-references.md) — base-game / KVLib /
  CharacterAPI / ModMenu

## Plugin metadata

Ability Lab is a module of `LokrLabPlugin` (`com.lokrmodding.lab`,
0.12.0). `LokrAbilityLabPlugin` is a facade for Log / Guid only.
Authoring in the LokrLab shell is the primary path; the fallback lab
scene (same FadeScreen load graphic) runs if the shell has not assigned
`JumpToProject`. Runtime loading
still does not need this plugin.

On boot the suite registers built-in action cards, copies
`Placeholders/` into `Mods/LokrLab/LokrAbilityLab/placeholders/`
when no placeholders library exists yet, and creates new abilities from one of
five templates (Melee hit, Ranged projectile, Ally buff, Passive trait,
Point AOE). After a library-folder Rename, `project.json`
`placeholdersLibrary` keeps that folder as the install target.
`Placeholders/new-ability.txt` remains the melee file
fallback. Character Lab resolves the current placeholder ability ids; it does not write
ability files.
