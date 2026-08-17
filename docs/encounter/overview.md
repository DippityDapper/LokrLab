# Encounter Lab — Overview

A **many-projects** type in the LokrLab suite: author a combat encounter
in Setup and play-test it in Sandbox (the same workspace name as
Character and Ability). Not Custom Adventures, not a Unity prefab editor.

**Status:** Setup auto-loads the board on tab enter (deferred two
frames) and shows a Loading board notice in the hole until the
preview is ready. Restart board reloads the preview. Combatants / Props cards
drag out of the catalogue onto a hex (LokrLab 0.12.85). Props snap to a hex or free-move in
the hole (schema v9, LokrLab 0.12.88). Play camera bounds (schema v8, Setup Camera tool)
confirmed in-game 2026-08-16 (LokrLab 0.12.77). Exploration pockets
(schema v10 `exploration` / `pocket` / `aggroRadius`) confirmed in-game
2026-08-16 (LokrLab 0.12.90). Painted trigger regions (schema v12
trigger catalog + `pocketKey` targeting + overlapping per-trigger hex
sets, Setup Trigger tool) confirmed in-game 2026-08-16 (LokrLab 0.12.96).
Hero spawn points (schema v13 combatant `source="spawn"`) plus a
Sandbox "Load Encounter" button are in LokrLab 0.12.101 — spawn points
now live in their own Node Tree folder (not the Combatants catalogue),
Setup preview never restarts to add or place one, and no placeholder
unit spawns at one. Encounter Lab Play is gone — Sandbox is the only
live-fight workspace (0.12.102). The Sandbox Level dropdown is always 1-3
(0.12.104). Hero-death wipe confirmed in-game 2026-08-17. Visual catalogues
(Combatants / Props) plus Stand-pose exo preview are in 0.12.75
(enemy kind-prefab exo; spritesheet bundle prop thumbs).
Phase 14 (scenario props) confirmed in-game 2026-08-16 (LokrLab 0.12.71). Phase 13
terrain catalog confirmed in-game 2026-08-16. Phase 12 floor-tile paint
confirmed in-game 2026-08-16.
Phase 11 grow-board confirmed in-game 2026-08-16. Phase 10
walkability paint confirmed in-game 2026-08-16. Implement remaining
phases from [`editor-design.md`](editor-design.md). Roadmap:
[`docs/roadmaps/started/encounter-creator.md`](../../../docs/roadmaps/started/encounter-creator.md).

v1 is create / save / combatants / Sandbox on a stock template, plus
click-to-place, walkability paint, grow/shrink of the live hex board,
floor-tile paint, a terrain catalog, scenario deco props, and optional
camera bounds. Exploration-then-combat (Phase 16) is done. Phase 17
(hero spawn points + one Sandbox workspace) is coded: an authored
spawn point has no fixed hero — Character Sandbox Load Encounter or
Encounter Sandbox (character pick) fills the first one. Hero-death
wipe confirmed in-game 2026-08-17.

## In this folder

- [`editor-design.md`](editor-design.md) — locked schema, node tree,
  Setup + Sandbox workspaces
