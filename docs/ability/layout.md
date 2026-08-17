# Ability module — Layout

Source lives under `LokrLab/Ability/`. Historical namespace
`LokrAbilityLab` stays.

```
LokrLab/Ability/
├── LokrAbilityLabPlugin.cs      Facade (Log / Guid); suite Awake registers cards
├── AbilityLabAPI.cs             Public RegisterActionCard + ActionCardDescriptor
├── ModMenuRegistration.cs       Blocking overlay (no standalone button)
├── AbilityLabAccess.cs          Public façade (Open/Close/Toggle/IsOpen)
├── AbilityLabScene.cs           Fallback lab scene (FadeScreen + unload)
├── AbilityLabPaths.cs           Mods/LokrLab/LokrAbilityLab/<libraryId>/
├── AbilityPlaceholders.cs       Installs Placeholders/; delegates create to templates
├── Placeholders/                Deployed next to the DLL (library + new-ability.txt)
├── Sidecars/                    Deployed next to the DLL (ability-hover.md, character-hover.md)
├── Projects/
│   ├── AbilityLibraryProjectType.cs
│   ├── AbilityCreateSheet.cs         New Project library name / slug / Auto
│   ├── AbilityLibraryIdentityRekey.cs Leftover library-folder rename
│   ├── AbilityLibraryRenameModal.cs  Library slug_token Rename sheet
│   ├── AbilityItemCreateSheet.cs     New Ability name / slug / alias / Auto
│   ├── AbilityItemCreateModal.cs     New Ability confirm modal
│   ├── AbilityItemRenameModal.cs     Leftover-id Rename onto slug_token
│   ├── AbilityLibrarySession.cs
│   ├── AbilityLibraryNodes.cs        Node Tree + inspector drawers
│   ├── AbilityLibraryViewport.cs     Library workspace (browser / card canvas)
│   └── AbilitySandboxViewport.cs     Sandbox workspace (Start sandbox → embed)
├── Editor/
│   ├── AbilityFileModel.cs      Envelope + AbilityBody
│   ├── AbilityBodyModel.cs      ActionCard / EventNode / ModifierDef / AiBlock
│   ├── AbilityKvIO.cs           Load/Save — KVLib + typed body + opaque fallback
│   ├── AbilityValidation.cs     Block parse killers; warn on catalog misses and engine traps
│   ├── AbilityTemplates.cs      Five create-sheet seeds
│   ├── AbilityCardRegistry.cs   Highest priority per TypeId
│   ├── AbilityCardDescriptors.cs  Built-in v1 + Advanced cards
│   ├── AbilityCardFactory.cs    Default field values for a new card
│   ├── AbilityLuaRules.cs       Lua Action stub / KV flatten / quote check
│   ├── AbilityPickerRules.cs    UnitRef allow-list (editor + xUnit)
│   ├── AbilityHoverCopy.cs      Hover-info markdown lookup
│   ├── AbilityEventNames.cs     AbilityEvents / ModifierEvents allow-lists
│   ├── AbilityPickerCatalog.generated.cs  Vanilla name lists + expression catalogs (do not hand-edit)
│   ├── AbilityCatalogLookups.cs Catalog membership + merged unit / expression / custom visual lists
│   ├── AbilityCustomAssets.cs   Per-ability fx/ and projectiles/ folders
│   ├── AbilityEditorSprites.cs  One-browse Cast FX / projectile sprite UI
│   ├── AbilityFilePicker.cs     SimpleUI file browser for PNG pick / copy
│   ├── AbilityExpressionField.cs  One-level function composer (Expression / UnitRef)
│   ├── AbilityEnvelopeOptions.cs  Behavior / team / AOE enums
│   ├── AbilityEditorForm.cs     Overlay tabs / inspector envelope / viewport body
│   ├── AbilityEditorCards.cs    Event hats + nested cards
│   ├── AbilitySummonLink.cs     Copy target alias into ability aliases.json
│   ├── AbilityIdentityRekey.cs  Leftover-id rename onto slug_token
│   ├── AbilityEditorPanel.cs    Header / status chrome
│   ├── AbilityLibraryBrowser.cs Filterable library grid
│   ├── AbilityUsage.cs          Used-by + body walks
│   └── AbilityListPanel.cs      Overlay list + template dropdown
└── docs/
    └── editor-design.md         Phase 3 visual model (Phase 4 implemented)
```

Picker catalog generator (not in this module folder):
`../../../docs/character-reference/generate_ability_picker_catalog.py` —
rewrites `AbilityPickerCatalog.generated.cs` from the Phase 1 extract.

`Editor/` is authoring UI, not Unity Editor-time tooling.

**Loading abilities into the running game is not this module's job
anymore (2026-08-12).** That lives in
`LokrCharacterLoader/CustomRigs/AbilityLabContentLoader.cs`. See
`../../../docs/roadmaps/started/editor-redesign.md` §2.7 and
`cross-references.md`.
