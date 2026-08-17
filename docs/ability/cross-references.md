# LokrAbilityLab — Cross-references

## Base game / `KVLib`

- `KVLib.KVParser.KV1.ParseAll` / `KVLib.KeyValue` (`Ironhide.Legends.dll`,
  decompiled source under `../../../../lokr-modding/ih-original/
  Ironhide.Legends/KVLib/`) — the real KV1 parser/serializer this plugin
  reuses for `AbilityKvIO`, the same library the Character module's
  `RLHeroesParser` already trusts.
- `Ironhide.Legends.Model.Game.Units.Abilities.AbilityParser`/`Ability`/
  `AbilityBehavior`/`TeamFilter`/`AOEKind` (decompiled source, same repo)
  — the real schema `AbilityFileModel`/`AbilityEnvelopeOptions` mirror.
  `AbilityParser.ParseAbility`'s own `PASSIVE`/`SELF_TARGET`/
  `POINT_TARGET` branches are what `AbilityEditorForm.RefreshVisibility`
  mirrors. `AbilityCastMinRange` is optional: absent → constant 0; present
  with an empty value → `ParseGenericExpression` throws and the ability
  never registers (adventure-start `KeyNotFoundException` on the unit's
  default skill). Ability Lab therefore omits the key when the field is
  empty.
- `MonoSingleton<LevelManager>.IsInstanceValid` — the same combat guard
  `LokrLab/LabContentReloader.CanReloadInCurrentGameState` uses,
  duplicated here for the save-time reload check.
- Sandbox (0.8.20, renamed from Stage in 0.12.102): Character Lab's fight via
  `LabHost.StartEmbeddedFight` (which calls LokrLab
  `StartEmbeddedScene`) so `InitUnitView` / `SpawnUnit` run for real.
  Start sandbox passes the shared `SandboxHole` `RectTransform`. There is no dummy
  mannequin viewer and no Ability Lab isolation Harmony.

## `LokrCharacterLoader`

- `CharacterAPI.BuildingAbilities` / `AbilitiesBuilder.AddAbilityText` —
  the resolver-chain-style event `LokrCharacterLoader/CustomRigs/AbilityLabContentLoader.cs`
  subscribes to, alongside `AbilitiesDefinitionsPatches`'s existing
  `"NewAbilities"` scan (a second, independent subscriber to the same
  event). Nested `Abilities/<id>/ability.txt` folders are this plugin's
  own on-disk layout, but the code that reads them now lives in
  `LokrCharacterLoader`, not here — moved 2026-08-12 (see `layout.md` and
  `../../../docs/roadmaps/started/editor-redesign.md` §2.7) specifically
  so this layout loads for a player who only has `LokrCharacterLoader`
  installed. `CharacterAPI.ContributingLocalization` is also subscribed
  there so each ability folder's `localization_*.txt` (SKILL_* strings)
  loads. Nested icons are resolved by `PortraitPatches`' ability-icon
  resolver (`Abilities/<id>/icons/` then flat `AbilityIcons/`) — already
  in `LokrCharacterLoader`, unaffected by this move.
- `CharacterAPI.ReloadLabContent(Abilities | Visuals)` — called from
  `AbilityEditorPanel`'s save handler. The default `ReloadScope`
  (`LabCharacterDefaults`) excludes both, so they must be requested
  explicitly. Character Lab sandbox / legacy import request
  `ReloadScope.All` for the same reason. `RefreshCustomVisuals()` also
  runs when the Envelope tab creates a custom FX / projectile folder.
- `CharacterAPI.KnownCustomFxNames` / `KnownCustomProjectileNames` /
  `KnownCustomClipNames` — picker merge + validation. Clip names are
  scraped from Character Lab `rig/rig.json` by the Loader (strings
  only).
- Ability Sandbox does not load Character Lab rigs itself. The embedded
  fight uses the same spawn path as Character Lab Sandbox.

## `LokrModMenu`

- `ModMenuAPI.RegisterBlockingOverlay` — registered by
  `ModMenuRegistration.cs` so the fallback overlay does not stack under
  the mod menu. There is no `RegisterButton`. Required a fix to
  `ModMenuAPI.RegisterBlockingOverlay` (previously a single global
  `Func<bool>`/`Action` pair, silently overwritten by whichever plugin
  registered second) to become a list, since Character Lab already
  registers its own blocking overlay and both registering would
  otherwise clobber each other — see `LokrModMenu/ModMenuAPI.cs`'s own
  updated doc comment.

## Character module

Peer project type in this assembly. Ability content still flows through
`CharacterAPI` / file conventions — a character references an ability by
id in `skillProgression`. Jumps use `LokrLabApi.JumpToProject`. The
Loader stays independent so a player with only `LokrCharacterLoader`
still loads the same category folders.
