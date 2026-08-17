# Ability Lab editor design (Phase 3)

**Status:** Implemented 2026-08-13 (Phase 4 visual editor, LokrAbilityLab
0.4.0; Phase 5 custom VFX / clips, 0.5.0). This file is the design
record, not UI code.

The on-disk contract is still `ability.txt`. The editor is now the
nested-card form described here. This document picked the visual model
against the Phase 1 catalog and Phase 2 rules, then mapped the
completeness inventory to pickers / cards / advanced / opaque.

See [ability-lab-overhaul.md](../../../docs/roadmaps/completed/ability-lab-overhaul.md),
[ability-rules.html](../../../docs/api/character-reference/ability-rules.html),
[ability-vfx-animation.html](../../../docs/api/character-reference/ability-vfx-animation.html).

---

## Decision

**Nested action cards** — structured forms and pickers, with events as
section hats and actions as stacked cards that can contain child stacks.

This is the SimpleUI translation of “blocks,” not a web Blockly host and
not a node graph. It is candidate 3 from the overhaul doc (forms +
pickers for the 80% the dump shows) using candidate 1’s mental model
(events as hats, actions as stacks). Candidate 2 (node graph) is
rejected.

The on-disk format stays the same `ability.txt` KV the loader already
consumes. The in-memory model grows from “envelope + opaque string” to
“envelope + typed body tree + opaque remainder.”

---

## Why the other two lose

| Candidate | Fit to KV | Fit to SimpleUI | Round-trip | Verdict |
|-----------|-----------|-----------------|------------|---------|
| Scratch / Blockly | Best mental match (hats + stacks) | No block canvas, no web view. Would invent a second UI toolkit. | Fine if we built it | Reject as a *host*. Keep the mental model. |
| Node graph | Good for `Conditional` / `ActOnTargets` branches | No wires, no free-move canvas. `UiTree` is a hierarchy, not a graph. | Invents X/Y layout that is not in the file | Reject. Spatial graphs fight a list-shaped file. |
| Forms + pickers + nested cards | KV *is* nested named lists | `UiStack`, `UiList`, `UiComboBox`, `UiTabGroup`, `UiContextMenu`, `UiModal` already exist | 1:1 with `KeyValue` children | **Pick.** |

In-game SimpleUI is a hard constraint. There is no Blockly host unless we
add one; adding one is out of scope for this track. A node graph would
spend Phase 4 on chrome the dump does not need: 278 of 431 abilities are
an `OnAbilityAction` list, not a wide branch network.

---

## What a card is

A **card** is a `UiPanel` (or titled `UiStack`) for one KV block:

- Header: action type name, move up/down, duplicate, delete, collapse.
- Body: typed fields for that type (`UiComboBox` / `UiTextField` /
  `UiToggle`).
- Optional nested **stack** for child action lists (`Actions`,
  `InitActions`, `ActionsIfFound`, …).

An **event hat** is a collapsible section whose children are cards:
`OnAbilityAction`, `OnProjectileHitUnit`, and so on. Adding a card is
`UiContextMenu` / “Add action” filtered by the registry (see
[Extensibility](#extensibility)).

This is not drag-and-drop onto a canvas. Reorder is buttons (and later
`UiList` reorder if we add it). Nesting is “Add inside this card,” not
wires.

Unknown or unregistered blocks become an **opaque card**: a raw KV
textarea for that subtree only, preserved on save. That is how the tail
stays reachable without a leftover whole-file textarea as the *only*
editor.

---

## Data model and round-trip

Keep `AbilityFileModel` envelope fields. Replace `RawBodyText` as the
*editing* surface with a body tree. Keep a remainder string only for
top-level children the tree does not own.

```
AbilityFileModel
  envelope          (existing ~16 fields; visibility already follows the parser)
  body.nodes[]      BodyNode
    EventNode       name in AbilityEvents / ModifierEvents, cards[]
    SpecialNode     AbilitySpecial variables
    ModifiersNode   modifier definitions (id, Passive, tags, events, PropertiesAdd, …)
    AiNode          AIConfigB / AIBrain*
    OpaqueNode      original KeyValue.ToString() for that child
  remainder         unused if every top-level child is a node
```

`ActionCard`:

- `TypeId` — KV block key (`Hit`, `ApplyModifier`, …)
- typed fields the card owns
- named child stacks (`Actions`, `InitActions`, …) of `ActionCard`
- or `Opaque` payload if the type is unknown / failed to parse

**Load** (`AbilityKvIO`): same as today for the envelope. Body children
become nodes. A registered type that fails field parse falls back to
`OpaqueNode` for that subtree — never drop the ability in the editor
(the *game* parser still can; the Lab must show the text).

**Save:** regenerate envelope (existing rules: omit empty optional
expressions; reject raw `"` in envelope fields). Serialize the body tree
in a stable order:

1. `AbilitySpecial` if present
2. Ability event hats that have any cards
3. `Modifiers`
4. AI blocks
5. Opaque top-level nodes in original relative order

Do not pretty-print an `OpaqueNode` into a different shape. Do not emit
empty expression strings (`ParseGenericExpression` throws and the
ability disappears from the registry).

Both the LokrLab inspector drawer and the overlay `AbilityEditorPanel`
must call one shared form builder. Do not fork two editors.

---

## Inspector layout

`UiTabGroup` on the ability inspector (overlay Editor screen uses the
same tabs):

| Tab | Contents |
|-----|----------|
| **Envelope** | Today’s form: behavior flags, team/range/AOE, AP, cooldown, `AbilityCanExecute`, icon / loc / `AnimationID` / `CastFXId`. Hide fields the parser skips (`RefreshVisibility` already does this). |
| **Events** | Event hats + action cards. Default open hat: `OnAbilityAction` (278 vanilla). |
| **Modifiers** | Nested modifier definitions; `"Passive" "1"` toggle; incompatible states / auto-remove tags; modifier event hats; `ModifierFXName` picker. |
| **Special** | `AbilitySpecial` rows (`var_type` + name + value). |
| **AI** | `AIConfigB` / considerations. Empty = generic scoring. |
| **Advanced** | Opaque remainder, rare events, “show raw file” (read-only or last-resort edit of remainder only). |

Envelope stays the first tab. Phase 4 does not delete it.

New-ability templates (picker on create, not a fourth plugin):

| Template | Behavior | Body seed |
|----------|----------|-----------|
| Melee hit | `MELEE \| HAS_CHANCE_TO_HIT` | `OnAbilityAction` → `Hit` → `AddDamage` |
| Ranged projectile | `UNIT_TARGET` | `CastFXId` + `TrackingProjectile` + `OnProjectileHitUnit` → `Hit` with `#PROJECTILE, #TARGETED` |
| Ally buff | `UNIT_TARGET \| POSITIVE_EFFECT` | `OnAbilityAction` → `ApplyModifier` |
| Passive trait | `PASSIVE \| POSITIVE_EFFECT` | `Modifiers` + `"Passive" "1"`; warn if `Icon` empty |
| Point AOE | `POINT_TARGET \| AOE` | Required AOE fields + `OnAbilityAction` → `ActOnTargets` / `Hit` |

These match the dump’s high-count flag combinations. `SELF_TARGET` is
legal but unused in vanilla; do not put it on the create sheet.

---

## Coverage checklist

Phase 2 inventory → what visual-editor v1 (Phase 4) actually builds.
“Not leftover textareas” means each row has a home. Opaque cards are
allowed for the tail; a single whole-body box is not the only path.

| Area | Phase 4 home | Notes |
|------|----------------|-------|
| Behavior / targeting flags | Envelope tab + presets from the 35 vanilla combos | Parser overrides stay: `POINT_TARGET` hides team filter; `PASSIVE` hides combat fields. |
| Expressions / AbilitySpecial | Special tab + one-level function composer on cards and envelope | Never emit `""`. Calls like `unitPosition(%TARGET, #Chest)` are function + arg slots. Each field’s `ExpressionContext` limits functions and parameters (range vs number vs position vs condition). Nested / `&&` stay typable on the assembled line. |
| Icon / loc | Envelope pickers | Icon: catalog stems + `icons/` in the ability folder. Loc: default `LocalizationId` = ability id; do not invent a Character Lab loc editor here. Trait warning if `PASSIVE` and `Icon` empty. |
| Cast clip | Envelope `AnimationID` combo | Vanilla catalog names + free type. Frame-event checklist is a short note, not a rig editor. |
| Cast FX | Envelope `CastFXId` combo | FXMega names (460 + dump). Separate from hit / modifier FX. |
| Hit / damage | `Hit` + `AddDamage` cards | First-class. `EffectName` is the **impact** FX picker, not CastFX. |
| Projectiles | `TrackingProjectile` card + `OnProjectileHitUnit` hat | `Model` is a **projectile** prefab picker, not FXMega. |
| Modifier FX | Modifier `ModifierFXName` picker | Third FX path. |
| Sounds (graph) | `PlaySound` / `StopSound` cards | Catalog names. Do not fold unit `soundConfig` or FXMega-internal audio into this picker. |
| Targeting extras | Envelope + `OnCustomTargeting` hat | `CallFunction` card: picker of the 16 shipped type names. No KV rewrite of those helpers. |
| Summons | `SpawnUnit` card | Unit-id picker from loaded definitions (`CharacterAPI` / registry). Does not create creatures. |
| Knockback | `Knockback` card | Common (71). In v1. |
| Conditionals / AoE iterate | `Conditional`, `ActOnTargets` cards | Nested stacks. This is why cards nest. |
| Delay / Times | Cards | `Delay` is common (97). |
| SetStat / Heal / AttachEffect / RemoveModifier | Cards | High dump counts. |
| AI | AI tab | Optional. Default empty. 14 consideration types as add-row, not a novel scorer UI. |
| Extra anim actions | Advanced cards | `PlayAnimation` (8), `OverrideAnimation` (33), `PlayActivityAnimation` (71). `PlayActivityAnimation` is common enough to promote if Phase 4 has room; otherwise Advanced. |
| ActOnHexas / MoveUnit / KillUnit / GiveArmor | Advanced cards | Present, not default “Add action” top hits. |
| Chaining | Advanced | `TriggerSkill` (2), `QueueAttackUnit` (4), cooldown offsets. |
| Battlefield extras | Advanced | `AreaControl` (11), `CameraControl` (7), `ModifyHexPassable` (1). |
| Cinematics | Opaque / Advanced | `QueueCinematic` (18), `CINEMATIC` flag. Not a create-sheet template. |
| Metagame | Opaque / Advanced | `AchievementIncrement` (33) is mostly hidden traits. Map/darkness containers stay opaque. |
| In-ability Lua | Advanced card | 5 vanilla files. Phase 7 authors `Action` as a multiline field. |
| Hero-room contract | Validation copy, not a Character Lab kit editor | Ability Lab warns: traits need `PASSIVE`+`Icon`; it does not write `skillProgression`. |
| Official Pack | Out of picker source | Vanilla catalogs only. Mods may still load; unknown names stay typed + warn. |

### Visual-editor v1 card set (must ship)

`Hit`, `AddDamage`, `ApplyModifier`, `RemoveModifier`, `AttachEffect`,
`TrackingProjectile`, `Heal`, `Knockback`, `Conditional`, `ActOnTargets`,
`Delay`, `PlaySound`, `StopSound`, `SpawnUnit`, `SetStat`, `CallFunction`
(picker of known types).

Event hats in v1: `OnAbilityStart`, `OnAbilityAction`,
`OnAbilityCustomEvent`, `OnProjectileHitUnit`,
`OnProjectileDestinationReached`, `OnProjectileMissedUnit`,
`OnCustomTargeting`, `OnThink`. Other ability events: addable, empty by
default. Modifier events: on the Modifiers tab as hats.

Everything else registered in `AbilityParser.genericClassConfigs` is
either an Advanced card (typed fields if cheap) or an opaque card.

---

## Picker data

Picker catalogs stay in this module; do not pull Character types for
name lists. Ability Lab may depend on `LokrCharacterLoader` / `CharacterAPI`
(already does)
and on **shipped name lists** generated from the same extract as the
HTML catalogs.

Phase 4 adds a generator (next to `generate_skills_catalog.py`) that
writes a JSON (or generated C#) catalog into this plugin:

- FXMega names from `FXMegaList.txt` (460) union dump `CastFXId` /
  `EffectName` / `ModifierFXName`
- Projectile `Model` names from the dump
- `AnimationID` / `PlayAnimation` clip names from the dump
- Icon stems from the dump
- `PlaySound` names from the dump
- `CallFunction` type strings from the dump
- `SpawnUnit` unit ids from the dump (runtime list can replace/extend
  this from loaded definitions)
- Expression functions (parser registry), context tokens, dump snippets,
  `SetStat` `#stat` refs, unit-ref values, `unitPosition` attach points,
  and `AddDamage` types
- Expression / UnitRef fields use `AbilityExpressionField` (function +
  args), not a single flat combo

Unknown typed values stay allowed; the combo is a catalog, not a
whitelist. Warn when a name is not in the vanilla catalog or the
Loader's custom-name lists (`LoadFXMega` throws in combat if neither
has it).

---

## Extensibility

Public surface lives on this plugin (not `LokrLabApi`), same idea as
[ability-lab.md](../../../docs/roadmaps/completed/ability-lab.md)
“form pickers should not be a closed hardcoded list”:

```
AbilityLabAPI.RegisterActionCard(typeId, descriptor)
```

`descriptor` supplies: display label, which event hats it is offered
under, field schema (or a `Transform` builder), and KV read/write. First
registration for a `typeId` wins, or last-at-higher-priority — match
`CharacterAPI` resolver style (priority int, first non-null / highest
priority).

Built-in v1 cards register the same way at ordinary priority. Lua
registers as an Advanced card (Phase 7). Custom gameplay plugins
register new action types the engine already accepts via `CallFunction`
or future parser hooks.

Do not put this registry on `LokrLabApi`. Inspector drawers stay
Ability-Library-specific.

`AbilityLabAccess` remains the open/close façade. The new API is additive.

---

## Validation (save-time, non-fatal except parse killers)

Block save (same class of error as today’s quote reject):

- Empty required expressions on a non-`PASSIVE` (`AbilityCastRange`,
  `AbilityCooldown`, `AbilityAPCost`, AOE required fields when `AOE` is
  set)
- Event hat names that look like `OnX` but are not in
  `AbilityEvents` / `ModifierEvents` (game throws
  `Unrecogniced AbilityEventName`)

Warn, do not block:

- `PASSIVE` with empty `Icon` (trait will not show in the hero room)
- `CastFXId` / `EffectName` / `ModifierFXName` / projectile `Model` not
  in the shipped catalog
- `AnimationID` other than `NOANIMATION` with no note that the caster
  rig needs `AbilityAction` / `AbilityEnd`
- `AOE` fields present without the `AOE` flag (parser ignores them)
- Team filter present on `POINT_TARGET` (parser overwrites to
  `TEAM_ALL`)

Ability Lab does not write `skillProgression` and does not fix Greg’s
hero-room indexing. That stays Character Lab.

---

## Constraints (unchanged)

- In-game SimpleUI only
- Round-trip the loader’s `ability.txt`
- Do not put the card registry on `LokrLabApi`
- Actives and passives in this one plugin
- No Encounter Creator work from this track
  ([encounter-creator.md](../../docs/roadmaps/started/encounter-creator.md))
- Custom FX/clip *assets* are Phase 5 (sprite folders + Loader inject;
  pickers over vanilla names shipped in Phase 4)

---

## Phase 4 slice (what “done” means)

1. Shared form builder used by overlay + Ability Library inspector — done
2. Body tree in `AbilityFileModel` / `AbilityKvIO` with opaque fallback — done
3. Tabs above — done
4. v1 card set + event hats — done
5. Shipped picker catalogs + warn-on-unknown — done
6. `AbilityLabAPI.RegisterActionCard` — done
7. Create-sheet templates — done
8. Raw whole-file box gone as the primary editor; Advanced remainder
   remains for the tail — done

Out of Phase 4: custom FXMega inject, custom clip authoring, Lua card,
node graph, Blockly, Character Lab kit UI, Official Pack as a second
catalog source.

---

## Phase 5 — custom visuals (done)

Ability Lab authors folders; LokrCharacterLoader instantiates prefabs.
Character Lab does not own FX prefabs.

- Cast FX lives on Envelope (every ability can have one). Projectile
  sprite lives on the **Tracking Projectile** card (only abilities
  that fire one). One **Choose sprite…** browse copies the PNG to
  `fx/<name>/` or `projectiles/<name>/` (name from the file stem) and
  assigns Cast FX Id / Model. The status line says what changed and
  what it replaced. **Clear sprite** deletes that folder and restores
  the previous base-game name (stored in `fx.json` / `projectile.json`).
  Switching away from a custom name also deletes its folder. Attach
  point is a socket (`Chest`, not `#Chest`). Duration and pixels/unit
  edit `fx.json` / `projectile.json` and call
  `CharacterAPI.RefreshCustomVisuals()`. The Loader rebuilds a missing
  prefab on `LoadFXMega` if the folder exists.
- Pickers merge `CharacterAPI.KnownCustomFxNames` /
  `KnownCustomProjectileNames` / `KnownCustomClipNames` with the
  vanilla catalogs. Unknown names still warn.
- Animation Id documents the `AbilityAction` / `AbilityEnd` contract.
  Custom clip names come from Character Lab `rig/rig.json` (string
  scrape in the Loader).
- Full particle FXMega remains an external Unity AssetBundle.

---

## Phase 6 — viewport (done)

Implemented 2026-08-13 (LokrAbilityLab **0.6.0**). See the overhaul
[Phase 6](../../../docs/roadmaps/completed/ability-lab-overhaul.md).

- Library root → filterable grid in the center (not a help label).
- Ability selected → action cards in the Library viewport; Inspector
  keeps envelope + Save.
- Sandbox workspace: embed-only live fight in `SandboxHole` via
  `StartEmbeddedFight` (0.8.20+; tab was named Stage until 0.12.102). Overlay fallback stays if the shell
  jump hook is unset. See
  overhaul [Phase 8](../../../docs/roadmaps/completed/ability-lab-overhaul.md).
- Overlay fallback keeps the full tabbed form (no viewport there).
- Phase 3 card model unchanged.
