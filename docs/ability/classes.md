# LokrAbilityLab — Classes

## `LokrAbilityLabPlugin` (`LokrAbilityLabPlugin.cs`)

Standard plugin bootstrap (see `conventions.md`). `Awake()` registers
built-in action cards, the Ability Library project type, writes missing
placeholder abilities into Mods, and registers `ModMenuRegistration`.
Loading authored abilities is
`LokrCharacterLoader/CustomRigs/AbilityLabContentLoader.cs`. See
`layout.md`.

## `AbilityLabAPI` / `ActionCardDescriptor` (`AbilityLabAPI.cs`)

Public extension surface. `RegisterActionCard(typeId, descriptor,
priority)` — highest priority per TypeId wins. Built-in cards register
the same way at priority 0. Do not put this registry on `LokrLabApi`.

## `ModMenuRegistration` (`ModMenuRegistration.cs`)

Registers a blocking-overlay entry with `LokrModMenu.ModMenuAPI`. There
is no standalone `"Ability Lab"` button — open the library from LokrLab.

## `AbilityLabAccess` (`AbilityLabAccess.cs`)

Public façade — `Open`/`Close`/`Toggle`/`IsOpen` — for other plugins to
open the Ability Library in the LokrLab shell when `JumpToProject` is
assigned, otherwise the fallback lab scene.

## `AbilityLabScene` (`AbilityLabScene.cs`)

The fallback lab scene (FadeScreen + unload). See `architecture.md`.
Owns the `UiScreenSwitcher` with `"List"`/`"Editor"` screens.

## `AbilityPlaceholders` (`AbilityPlaceholders.cs`)

Copies `Placeholders/` into
`Mods/LokrLab/LokrAbilityLab/placeholders/` on first install, then
follows that library after a `slug_token` Rename (`placeholdersLibrary`
in `project.json`). Skips a shipped stem when that ability was already
rekeyed. `TryWriteNewAbility` delegates to
`AbilityTemplates` (`new-ability.txt` is the melee file fallback).
`ResolveAbilityId` returns the live folder id for a shipped stem.

## `AbilityLabPaths` (`AbilityLabPaths.cs`)

`ModRoot` / library folders
(`Mods/LokrLab/LokrAbilityLab/<libraryId>/<abilityId>/`).

## `AbilityFileModel` (`Editor/AbilityFileModel.cs`)

Envelope fields (behavior, team/range/AOE, AP, cooldown, CanExecute,
HitChanceModifier, Icon / AnimationId / CastFXId, LocalizationId) plus
`Body` (`AbilityBody`) and `SourceFilePath`.

## `AbilityBody` / `ActionCard` / `EventNode` / `ModifierDef` / `AiBlock` / `SpecialVar` (`Editor/AbilityBodyModel.cs`)

Typed body tree. An `ActionCard` is either typed fields + named child
stacks or an opaque KV subtree.

## `AbilityKvIO` (`Editor/AbilityKvIO.cs`)

`TryLoad`/`TrySave`/`TryBuildText`. `TryBuildText` is the same KV as
Save without writing disk. See `architecture.md`.

## `AbilityValidation` (`Editor/AbilityValidation.cs`)

Blocks empty required non-PASSIVE expressions, illegal `On*` names,
Hit `Tags` outside `HitAction.ValidateTags`, and `PerAffectedAI` as an
action. Warns on PASSIVE+no Icon, unknown catalog names, AOE fields
without the AOE flag, team filter on POINT_TARGET, RANGE_CONE, dead
event hats, empty AI Considerations, empty-filter CallFunction names,
MELEE+POINT_TARGET, GetCloseToUnitAI under POINT_TARGET, and unknown
PropertiesAdd / SetStat keys.

## `AbilityTemplates` (`Editor/AbilityTemplates.cs`)

Melee hit, Ranged projectile (`#PROJECTILE, #TARGETED` hit tags), Ally buff, Passive trait, Point AOE.
`SelectedId` is the overlay dropdown / File → New Ability choice.

## `AbilityCardRegistry` / `AbilityCardDescriptors` / `AbilityCardFactory`

Registry (highest priority wins), built-in v1 + Advanced cards (including
Lua), default field values for a newly added card. Lua seeds
`return function(ctx) end`.

## `AbilityLuaRules` (`Editor/AbilityLuaRules.cs`)

Unity-free Lua-card helpers: default stub, newline flatten for KV, double-quote check.

## `AbilityPickerRules` (`Editor/AbilityPickerRules.cs`)

Unity-free UnitRef allow-list (core tokens plus per-field extras). Loaded
off-list values stay visible.

## `AbilityHoverCopy` (`Editor/AbilityHoverCopy.cs`)

Hover-info lookup: compiled fallbacks plus markdown sidecars
(`Sidecars/ability-hover.md`, `Sidecars/character-hover.md`; overlays
`Mods/LokrLab/` of the same names). Later files win per key.

## `AbilityEventNames` (`Editor/AbilityEventNames.cs`)

AbilityEvents / ModifierEvents allow-lists copied from the decompiled
engine. Unknown `OnX` throws in-game. Add-event menus offer
`FiredAbilityEvents` / `FiredModifierEvents` (names with a combat fire
site). Dead names stay on `All*` so they are not save errors; a loaded
hat still renders and `AbilityValidation` warns.

## `AbilityPickerCatalog` / `AbilityCatalogLookups`

Generated vanilla name lists (FXMega, projectiles, clips, icons, sounds,
CallFunction types, SpawnUnit ids, expression functions, context tokens,
stat refs, dump snippets, unit refs, attach points, damage types).
Lookups merge `CharacterAPI.KnownUnitDefinitions` into the unit picker,
custom FX / projectile / clip names from `CharacterAPI`, and
concatenate expression tokens + functions + snippets. UnitRef / unit
snippet lists go through `AbilityPickerRules` so Hit Target does not
offer `%drainSource`.

## `AbilityCustomAssets` (`Editor/AbilityCustomAssets.cs`)

Creates `fx/<name>/` and `projectiles/<name>/` next to `ability.txt`
after the user picks a PNG in SimpleUI's file browser. Copies that
file to `sprite.png`. `TryDelete` removes the folder (and an empty
`fx/` / `projectiles/` parent). Restore fields remember the previous
base-game Cast FX Id / projectile Model. The Loader (`CustomFxLoader`)
turns those folders into prefabs. Ability Lab never builds a
`GameObject`.

## `AbilityEditorSprites` (`Editor/AbilityEditorSprites.cs`)

One file browse to assign a Cast FX or Tracking Projectile sprite.
Shows what is set (custom PNG vs base-game). Status announces every
assign, clear, delete, and attach/duration/pixels-per-unit change.

## `AbilityFilePicker` (`Editor/AbilityFilePicker.cs`)

`UiFileBrowser.PickFile` on `LokrLabApi.Host.Canvas` (shell) or
`AbilityLabScene.Canvas` (fallback). Starts in Linux Pictures/Home
under Proton. Extra places: this ability, Ability Lab libraries, Mods.

## `AbilityExpressionField` (`Editor/AbilityExpressionField.cs`)

One-level function composer for Expression / UnitRef fields. Parses
`name(arg, …)` into a function combo plus per-argument catalogs;
literals stay a single combo plus a Function button. `ExpressionContext`
limits functions and parameters per field role (Range vs Number vs
Position vs Condition, …). Nested / `&&` expressions stay on the
assembled line.

## `AbilityEnvelopeOptions` (`Editor/AbilityEnvelopeOptions.cs`)

Fixed option arrays (`BehaviorFlags`, `TeamFilters`, `AOEKinds`).
`SelectableAOEKinds` omits `RANGE_CONE` (combat never fills cone hexes);
a loaded cone value still binds so save does not rewrite it.
`HitValidateTags` is the `HitAction.ValidateTags` whitelist for the Hit
Tags picker (not dump-wide `HitTags`).

## `AbilityListPanel` (`Editor/AbilityListPanel.cs`)

Overlay list: search, template dropdown, create, delete, open.

## `AbilityEditorForm` / `AbilityEditorCards` / `AbilityEditorPanel`

Shared tabbed form. Overlay `Build` is the full tab strip. Shell
inspector `BuildInto` is envelope only. Library viewport `BuildBody`
hosts Events / Modifiers / Special / AI / Advanced. Panel owns
header/status/Save/Duplicate/Delete. Cast FX sprite stays on Envelope;
projectile sprite stays on Tracking Projectile. `BuildInto` is not
scrollable.

## `AbilityLibraryViewport` / `AbilityLibraryBrowser` (`Projects/` + `Editor/`)

Library workspace center. Library root → filterable grid (search,
Melee / Ranged / Passive / AOE chips, used-by). Ability node → card
canvas. Open uses `LabHost.SelectNodeById`.

## `AbilitySandboxViewport` (`Projects/AbilitySandboxViewport.cs`)

Sandbox workspace chrome (shared with Character and Encounter): Level
dropdown, Start sandbox / Stop sandbox, and a `SandboxHole` panel.
Start sandbox calls `LabHost.StartEmbeddedFight` when a used-by
Character exists. Failures log a warning. Stop sandbox unloads the
fight.

## `AbilityIdentityRekey` (`Editor/AbilityIdentityRekey.cs`)

Inspector-driven rename of leftover ability folders (`new_ability`,
18-digit ids) onto `slug_token`. Rewrites the KV block key, `SKILL_*`
loc stems, and character `defaultSkill` / `skills` / `skillProgression`
refs. `TryApplyToSlugToken` is that Rename button (does not run on
load). `RewriteOnDisk` is the legacy-import path: leftover pack text
is written into an already-minted folder, then the block key and loc
stems are retargeted in place (text rewrite, so nested pack modifiers
survive).

## `AbilityUsage` (`Editor/AbilityUsage.cs`)

Scans `CharacterAPI.KnownUnitDefinitions` (`defaultSkill`, `skills`,
`skillPool`, `skillProgression`) and walks the body tree.

## `AbilityLibraryProjectType` / `AbilityLibrarySession` / `AbilityLibraryNodes` / `AbilityCreateSheet` / `AbilityItemCreateSheet` (`Projects/`)

Many named libraries, session handle, Node Tree + inspector drawers.
New Project uses name / slug / Auto and mints a `slug_token` library
folder. Leftover numeric library folders get **Rename library to
slug_token** on the library inspector (characters reference ability
ids, so only the folder and recents move). New Ability uses name /
slug / alias / Auto. Leftover ability folders get **Rename** on the
Library grid card, the library inspector list, and the Ability
inspector. Each Ability node has an Aliases child.
