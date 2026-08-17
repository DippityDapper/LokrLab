# LokrAbilityLab — Architecture

## LokrLab project type (Phase 9)

`AbilityLibraryProjectType.Register()` (from suite `Awake`) adds an
`ability-library` type on `LokrLabApi`. Each library is its own project
under `Mods/LokrLab/LokrAbilityLab/<libraryId>/`. New Project asks
for a display name; the folder uses a generated id. Opening a library
shows the LokrLab shell: Node Tree (library root + one Ability node per
folder), Library workspace (browser or action-card canvas), Sandbox
workspace (preview / range / flow), and the envelope in the Inspector. File → New Ability (visible only while a library is
open) and right-click Add Ability create folders inside that library
from `AbilityTemplates.SelectedId`. Character projects jump to the
library that contains the referenced ability id. Libraries can be deleted
from the Project Browser; abilities are deleted inside a library.

Open a library from LokrLab (Project Browser or New Project). That path
uses LokrLab's own `FadeScreen` load graphic. The fallback scene below
runs only when `JumpToProject` is not assigned, and uses the same
`FadeScreen` + `UnloadSceneAsync` + `TransitionSceneComponent` pattern.
There is no standalone mod-menu button. A boot migration wraps the old
singleton `Abilities/<id>/` tree into one library.

## Fallback lab scene

`AbilityLabScene.cs` matches `LokrLab/CharacterLabScene.cs`'s transition
model: fade out with the game's load graphic, build a `CreateScene`
lab, unload the origin scene, fade in. Close fades out and
`TransitionSceneComponent.TransitionToNextScene` returns to the origin
(`LoadSceneMode.Single` destroys the lab scene; the next Open rebuilds
it). EventSystem isolation is gone — nothing foreign remains once the
origin is unloaded.

Two screens, `"List"` and `"Editor"`, switched via
`SimpleUI.UiScreenSwitcher`. The Extensibility surface is
`AbilityLabAPI.RegisterActionCard`, not a third hub screen.

## Envelope + body tree — how load and save work

`AbilityKvIO.cs` is the one place that reads/writes the real KV1 grammar,
reusing `KVLib` (`Ironhide.Legends.dll`).

**Load** (`TryLoad`): `KVParser.KV1.ParseAll(text)` parses the whole file
and requires exactly one top-level block. Recognized envelope keys are
pulled out via `KeyValue`'s indexer. Remaining children become an
`AbilityBody`:

- `AbilitySpecial` → `SpecialVar` rows
- legal ability `On*` keys → `EventNode` + nested `ActionCard`s
- `Modifiers` → `ModifierDef` (fields, modifier event hats, extra KV)
- `AIConfigB` / `AIBrain*` → named `AiBlock` (inner KV opaque)
- everything else → `OpaqueTopLevel` via `KeyValue.ToString(indent)`

A registered action type that is a leaf or unknown type becomes an
opaque card for that subtree. `Lua` is registered (Advanced) so its
`Action` field is typed, not opaque. The editor never drops the ability; the
game parser still can.

**Save** (`TrySave`): `AbilityValidation.TryValidate` runs first (empty
required expressions, illegal `On*` names, Hit tags outside
`ValidateTags`, and `PerAffectedAI` as an action block; catalog misses
and engine traps warn). Envelope scalars are regenerated. Combat fields are omitted for
`PASSIVE`. Optional expressions are omitted when empty
(`ParseGenericExpression` throws on `""`). Literal `"` in envelope
fields is rejected. Body order is AbilitySpecial → event hats with
cards → Modifiers → AI → opaque top-level. Opaque text is reindented,
not pretty-printed into a new shape.

## Shared form (overlay + inspector + viewport)

`AbilityEditorForm` + `AbilityEditorCards` are the only editor. Overlay
`AbilityEditorPanel.Build` still hosts every tab in one scrolling form
(fallback scene has no viewport). In the shell, `BuildInto` is envelope
only (the dock already scrolls). Events / Modifiers / Special / AI /
Advanced live in the Library viewport via `BuildBody`. Sandbox is a
second workspace — Start sandbox embeds a real fight in the camera
hole (`LabHost.StartEmbeddedFight` with the shared `SandboxHole`).
If Host, used-by Character, or load fails, Sandbox reports the error and
does not spawn standees. Isolation Harmony is gone. Tabs are a button row, not
`UiTabGroup.Create()`. Reorder is up/down buttons. Add action is
`UiContextMenu` filtered by `AbilityCardRegistry` (default vs Advanced).

Envelope Icon / AnimationID / CastFXId are `UiComboBox` catalogs
generated from the Phase 1 extract
(`AbilityPickerCatalog.generated.cs`, via
`docs/character-reference/generate_ability_picker_catalog.py`).
Expression and unit-ref fields use `AbilityExpressionField`: a one-level
composer (function + argument slots + assembled typable line) when the
value parses as `name(arg, …)`, otherwise a token/snippet combo plus a
Function button. Expanded calls have a matching Value button that
collapses back to a token or number. Each field has an `ExpressionContext` (Range, Number,
Position, Unit, Condition, Tags, Group, General) so Cast Range does not
offer `unitPosition` or `#turnsPlayed`. Name catalogs (FX, animation,
CallFunction type, damage type, SetStat) stay flat comboboxes. Combos
are catalogs, not whitelists; unknown names stay typable and warn
(`LoadFXMega` throws in combat unless a custom `fx/<name>/` folder
exists). Phase 5 merges `CharacterAPI.KnownCustomFxNames` /
`KnownCustomProjectileNames` / `KnownCustomClipNames` into those
pickers. The Envelope tab can create per-ability `fx/` and
`projectiles/` folders; the Loader builds the prefabs.

## Field visibility follows the real parser's own branches

`AbilityEditorForm.RefreshVisibility()` hides envelope fields
`AbilityParser.ParseAbility` never reads for the current
`AbilityBehavior` (`PASSIVE` skips combat fields; `SELF_TARGET`
hardcodes range to 0; `POINT_TARGET` hardcodes team filter to
`TEAM_ALL`).

## Reload wiring

`CharacterAPI.ReloadLabContent`'s default `ReloadScope`
(`LabCharacterDefaults`) excludes `Abilities`. `AbilityEditorPanel`'s
save handler requests `CharacterAPI.ReloadScope.Abilities |
ReloadScope.Visuals`, guarded by
`MonoSingleton<LevelManager>.IsInstanceValid`. Character Lab sandbox
and legacy import use `ReloadScope.All` so an imported `defaultSkill`
is in `AbilitiesDefinitions` without a game restart.
