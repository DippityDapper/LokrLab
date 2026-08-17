# Encounter Lab editor design (Phase 2)

**Status:** Locked 2026-08-16. Phase 11 grow-board confirmed in-game.
Phase 13 (terrain catalog) confirmed in-game 2026-08-16. Phase 14
(scenario props) is in LokrLab 0.12.71. Phase 15 (Play camera bounds)
is in 0.12.76. Phase 12 floor-tile paint confirmed in-game 2026-08-16.

This is the Ability-overhaul equivalent of "pick nested cards before
writing the form." The product (visual map editor + exploration combat)
is researched in
[encounter-creator.md](../../../docs/roadmaps/started/encounter-creator.md)
Phases 1a–1c. v1 is the project-type scaffold on `fighttesterempty`.

---

## Decision

**Two workspaces — Setup + Sandbox — and a typed `encounter.json`.**

Setup is the authoring tab (map viewport from Phase 9; walkability
paint from Phase 10; grow-board from Phase 11; floor tiles from Phase 12;
terrain catalog from Phase 13; scenario props from Phase 14; Sandbox
camera bounds from Phase 15).
Sandbox is the additive fight embed (same workspace name as Character
and Ability; Play and Stage tabs were removed in 0.12.102). Combatants
are a Node Tree folder of Character refs and vanilla unit ids. Character
Sandbox can Load Encounter onto this same arm. Do not grow
`EmbeddedFightRequest`.

---

## Why not a single Sandbox tab

Character splits Properties / Animator / Sandbox. Ability splits
Library / Sandbox. A single Sandbox workspace would force the map
editor into the fight hole or a later third tab. Setup exists so
the map editor has a home that is not "the fight is running."

v1 Setup was a summary through Phase 8. Phase 9 puts the template
board in this tab. Phase 10 paints walkability. Phase 11 grows the
live hex rect. Phase 12 paints floor tiles. Phase 13 puts terrains in
the Node Tree. Phase 14 places scenario deco props.

---

## On disk

```
Mods/LokrLab/LokrEncounterLab/<slug_token>/
  project.json
  encounter.json
  aliases.json          (optional)
```

`ScanCategory` = `LokrEncounterLab`. Folder id uses the same
`LabSlugIds` mint as Character / Ability.

`project.json`:

```json
{
  "projectType": "encounter",
  "schemaVersion": 1,
  "displayName": "Bandit Ambush"
}
```

`encounter.json` (payload only; display name stays on the marker):

```json
{
  "schemaVersion": 9,
  "template": "fighttesterempty",
  "walkableDefault": false,
  "tilesDefault": false,
  "overrides": [
    { "col": 8, "row": 10, "walkable": false }
  ],
  "tiles": [
    { "col": 8, "row": 10, "terrainId": 1, "template": "combat_bridge" }
  ],
  "terrains": [
    { "terrainId": 1, "name": "Ice", "source": "import", "template": "combat_bridge" }
  ],
  "props": [
    { "id": "bush_1", "prefabName": "forest_deco_generic_bush_1x1_02", "snap": true, "col": 8, "row": 10, "flipped": false },
    { "id": "rock_1", "prefabName": "forest_deco_generic_rock_1x1_01", "snap": false, "x": 12.4, "y": 8.1, "flipped": false }
  ],
  "camera": {
    "minX": 1.5,
    "minY": -2,
    "maxX": 20,
    "maxY": 14,
    "lockZoom": true,
    "orthoSize": 6.25
  },
  "combatants": [
    {
      "id": "gerald_1",
      "side": "GoodSide",
      "source": "character",
      "projectId": "necromancer_ad8174",
      "level": 1,
      "col": 6,
      "row": 10,
      "flipped": false
    },
    {
      "id": "banditraider_1",
      "side": "BadSide",
      "source": "unit",
      "unitId": "BanditRaider",
      "flipped": true
    }
  ]
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | Required. v9 writes per-prop `snap` / `x` / `y`. v8 wrote optional `camera`. v1–v8 files still load. |
| `template` | Prefab name. Default `fighttesterempty`. Picker also offers `combat_bridge`. |
| `walkableDefault` | New files write false (blank blocked canvas). Missing key = true (template cells stand). |
| `tilesDefault` | New files write false (strip the template floor). Missing key = true (v1–v5 keep the host Tilemap). |
| `width` / `height` | Legacy 0.12.53 only. Expanded into walkable overrides; not rewritten. Size is derived from walkable hexes. |
| `overrides[]` | Sparse walkability deltas from the loaded template. `col`, `row`, `walkable`. Empty array is legal. |
| `tiles[]` | Sparse floor-tile stamps. `col`, `row`, `terrainId`, optional `template` when the art is not from the host. Empty plus `tilesDefault` true means the template Tilemap stands. |
| `terrains[]` | Node Tree catalog. `terrainId`, `name`, `source` (`template` / `import` / `custom`), `template` prefab. Empty means scan the host room. |
| `props[]` | Placed scenario deco instances. `id`, `prefabName`, `snap` (default true), optional `col` / `row` when snap, optional world `x` / `y` when free-move, `flipped`. Do not auto-block walk. Empty is legal. |
| `camera` | Optional world-space AABB for Play pan. `minX` / `minY` / `maxX` / `maxY`, `lockZoom` (default true), optional `orthoSize`. Missing object keeps the embed unclamped. Setup stays unclamped so the rect can be drawn. |
| `combatants[].id` | Unique legal slug. Mint from character slug / `unitId` + index. |
| `side` | `GoodSide` or `BadSide`. Reject anything else. |
| `source` | `character` or `unit`. |
| `projectId` | Required when `source` is `character`. Resolve lazily. |
| `unitId` | Required when `source` is `unit`. |
| `level` | Optional, default `1`. Character / hero ranks only. |
| `col` / `row` | Optional OffsetCoord. Omitted = Play center-offset. |
| `flipped` | Optional, default `false`. |

Empty `combatants` is legal to save, illegal to start in Sandbox. Sandbox also refuses
zero `GoodSide`. Missing Character folder: listed, inspector warning,
Play errors clearly.

Not in schema v9: triggers, `OwnSide`, `cinematicId`, `hasInitiative`.
Walkability paint is `overrides`. Floor tiles are `tiles`. Props are
`props`. Play camera is optional `camera`. Size is derived from
walkable hexes.

---

## Shell

| Piece | Lock |
|---|---|
| Type id | `encounter` (`LokrLabApi.EncounterTypeId`) |
| Code | `LokrLab/Encounter/`, namespace `LokrLab.Encounter` |
| `ReferenceableProjectTypes` | `character` |
| Node kinds | `Encounter` (root), `Combatants` (folder), `Combatant`, `Terrains` (folder), `EncounterTerrain`, `Props` (folder), `EncounterProp`, `Aliases` |
| Factory | `Combatant` on `Combatants`; `EncounterTerrain` on `Terrains` (import modal); `EncounterProp` on `Props` |
| Inspectors | Encounter (template, counts, Play camera); Combatants (visual catalogue + Stand preview); Combatant (source, side, level, hex, facing, remove, Jump); Terrains (import / custom); Terrain (name, Use for Paint, remove); Props (visual catalogue + tk2d deco preview); Prop (prefab, hex, facing, remove) |
| Workspaces | Setup (priority 0, default; auto-loads the board two frames after enter); Sandbox (priority 10) |
| Bottom panels | none |
| Create sheet | `LabSlugCreateFields` with `encounter.create` |
| File | Add Combatant; Import Terrains; Add Prop (visible when an Encounter session is open) |
| Save | `EncounterSession.IsDirty`; wire `LabSaveUx.TrySaveCurrent` |

Jump uses `PickProjectReference` / `JumpToProject` /
`ReturnToPreviousProject`. Encounter is the first type that *must* use
them for authoring.

---

## Spawn

Keep `EmbeddedFightRequest` as Character / Ability Sandbox 1v1. Encounter Sandbox spawns
through a suite-internal `EncounterRoster` (generalize `SandboxRoster`).
`StartEmbeddedFight` still needs `CasterUnitId`: pass the first
`GoodSide` resolved unit id and ignore the default BanditRaider when
the roster override is present.

Reuse `EmbeddedFightHost` and the existing embed patches. Gate new
Encounter behavior on embed-active plus an Encounter mode flag.

---

## Hover keys (copy in Phase 8)

`encounter.create.Name` / `.Slug` / `.SlugAuto` / `.Alias` /
`.AliasAuto` / `.IdPreview`; `encounter.template`;
`encounter.combatants.Add`; `encounter.catalogue.Search` / `.Add` (drag a card onto a Setup hex to place) /
`.Preview`; `encounter.combatant.Source` / `.Project` /
`.UnitId` / `.Side` / `.Level` / `.Col` / `.Row` / `.Flipped` /
`.Clear` / `.Remove`; `encounter.play.Start` / `.Stop`.

---

## Phase 3 checklist (first code)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.36 / LokrLabApi
1.5.2):

- `LokrLabApi.EncounterTypeId = "encounter"`.
- `EncounterProjectType.Register()` from suite `Awake` next to Character
  and Ability.
- Paths, marker, create / load / delete, `EncounterSession`.
- Empty `encounter.json` (`template` + `combatants: []`).
- Setup workspace summary; no Play, no combatant factory yet (Phase 4).
- `LabSaveUx.TrySaveCurrent` writes `encounter.json`.
- Project Browser lists the type; New Project wizard includes it.

## Phase 4 checklist (combatants)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.37):

- Add Combatant factory on the Combatants folder (Character vs unit).
- File → Add Combatant when an Encounter session is open.
- Combatant inspector: side, level (Character ranks only), remove, Jump.
- Missing Character folder: listed with a warning; Jump is disabled.
- Shared `ProjectReferencePickerModal` for Character picks (async; the
  LabApi `PickProjectReference` Func cannot wait on a click).

## Phase 5 checklist (placement)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.38):

- Combatant inspector: col, row, flipped, Clear placement.
- Live board size is authored + 8×4 (`fighttesterempty` = 24×24).
- Empty hex = Play center-offset. Partial col/row warns.
- Duplicate hex warns in the inspector; Play still rejects it.

## Phase 6 checklist (Play)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.39):

- Play workspace hole; Start / Stop.
- `EncounterPlay` arms the embed host; `EncounterRoster` spawns before
  `StartFight`. Stage / Sandbox stay 1v1.
- Refuse Play with zero GoodSide, duplicate hex, missing Character
  folder. Debug spawn panel stays off.

## Phase 7 checklist (template picker)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.41):

- Encounter inspector dropdown: `fighttesterempty` (open field) and
  `combat_bridge` (bridge). `combat_wip` is not offered (looks the same).
- Change dirties Save and clamps placement. Play uses the in-memory name.
- Unknown saved template stays listed until the author picks an empty host.

## Phase 8 checklist (hover copy)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.42):

- `LokrLab/Sidecars/encounter-hover.md`; optional `Mods/LokrLab` overlay.
- Create sheet, template, combatants, Play / Stop, Clear placement.

## Phase 9 checklist (Setup board + click-to-place)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.47):

- Setup hole; Show board / Hide board.
- `EncounterEdit` arms the embed; tap hex writes col/row; tap unit selects.
- `CheckFightEnd` fenced while edit is armed. Empty roster is legal.
- Turns / AI / fight HUD stay off; walkable hexes are painted as a static grid.

## Phase 10 checklist (walkability paint)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.52):

- Schema v2 sparse `overrides`. v1 files still load.
- Setup Place / Block / Unblock. Apply on Show board and Play.
- Play AI-to-player HUD handoff:
  [`play-ai-first-missing-walk-and-skills.md`](../../../docs/issues/resolved/play-ai-first-missing-walk-and-skills.md).

## Phase 11 checklist (grow-board)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.59):

- Live size is derived from walkable hexes (overrides + placements). Cap 64.
- Setup Unblock on the edge ring grows the board. No +W / +H buttons.
- Drag-paint Block / Unblock. Off-board click uses unclamped `PointToHex` and grows to that hex. Hover ghost marks the target when the cursor is off the live grid.
- `CreateBoard` + `SetBoard` before spawn. Embed camera is unclamped.

## Phase 12 checklist (floor-tile paint)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.63):

- Schema v4 sparse `tiles`. v1–v3 still load.
- Setup Tile / Erase. Left-drag paints both rectangular cells of each
  hex; Erase restores the template. `errorSprite` is replaced with an
  interior hex sprite while Encounter owns the embed. Apply on Show
  board, Play, and Sandbox Load Encounter.
- `ConstrainMap` skipped while Encounter owns the embed (Setup, Play,
  or Sandbox-loaded Encounter).

## Phase 13 checklist (terrain catalog)

Done and confirmed in-game 2026-08-16 (LokrLab 0.12.70):

- Schema v6 `tilesDefault` (new files false) and `terrains[]`. v1–v5 still load.
- Terrains folder in the Node Tree. Host hex-art terrains scan on
  open. Import from another `templates` prefab. Custom is a named stub.
- Select a terrain node to paint. Toolbar dropdown is gone.
- Setup Grid toggle hides the walk overlay. Empty blocked cells stay blank. The hover hex marker stays.
- Imported stamps store `tiles[].template` so art is not the host sheet.
- Lab embeds hide vanilla Paralax foregrounds (leaves, vines). The
  unclamped embed camera makes those layers sit wrong. Campaign fights
  keep them.

## Phase 14 checklist (scenario props)

Code in LokrLab 0.12.71 (in-game confirm not yet done):

- Schema v7 `props[]`. v1–v6 still load (missing key = empty).
- Props folder in the Node Tree. Add Prop picks a `scenario` deco name.
- Place writes col/row when snap is on, or world x/y when snap is off.
  Does not block walk. Does not grow the board.
- Show board and Play instantiate via `LoadAsset("scenario", name)`.

## Phase 15 checklist (Play camera bounds)

Code in LokrLab 0.12.77 (in-game confirm not yet done):

- Schema v8 optional `camera` (`minX` / `minY` / `maxX` / `maxY`,
  `lockZoom`, optional `orthoSize`). v1–v7 still load (missing key =
  unclamped embed).
- Setup Camera tool draws a world-space rectangle. Handles resize;
  interior moves. Encounter inspector: numbers, lock zoom, Use current
  view, Clear.
- Play applies that AABB as `CameraBase.cameraLimits` and locks wheel
  zoom when `lockZoom` is on. Embed drag clamps the camera center so
  the view stays inside the rect (vanilla clamp never sees that write).
  Authored ortho is clamped so the view still fits the rect. Setup and
  Sandbox stay unclamped.
- Do not grow `EmbeddedFightRequest`. Campaign fights stay vanilla.
