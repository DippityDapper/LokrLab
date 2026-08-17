# Character module — Layout

Source lives under `LokrLab/Character/`. Historical namespaces
(`LokrLab`, `LokrCharacterLab`) stay.

```
LokrLab/Character/
├── LokrCharacterLabPlugin.cs           (facade: Log / Guid only)
├── Lab.cs                              (facade over LokrLabApi.Host)
├── CharacterLabHooks.cs                (LabOpened / LabClosing / ShellShown)
├── CharacterWorkstations.cs            (legacy CharacterCreatorAPI screens)
├── CharacterCreatorAPI.cs              (public; namespace LokrLab)
├── CharacterLabOptionsAPI.cs           (public; namespace LokrLab)
├── CharacterLabLayers.cs
├── CharacterLabUi.cs
├── LabContentReloader.cs
├── SandboxRoster.cs                 (shared hero + enemy spawn)
├── SandboxFightControls.cs          (TakeOverAI, HUD restore, BindConfirmCanvases)
├── EmbeddedFightHost.cs             (quest + spawn on top of StartEmbeddedScene)
├── Patches/
│   ├── EmbeddedFightCameraPatches.cs  (right/middle-drag pan in the hole; no edge scroll)
│   ├── EmbeddedFightHexInputPatch.cs  (hole-camera hex taps; skip Icon / EndTurn)
│   ├── EmbeddedFightConfirmButtonPatch.cs (OnTap retarget + Awake BindConfirmCanvases)
│   ├── EmbeddedFightStagePatch.cs     (Stage.Update / HUD guards in the embed)
│   ├── EmbeddedFightStartFightSpawnPatch.cs (spawn roster before StartFight)
│   └── SkillsBarTurnMarkerPatch.cs    (turn-marker skip + five-slot SkillsBar cap)
├── Projects/
│   ├── CharacterProjectType.cs
│   ├── CharacterCreateSheet.cs             (New Project name / slug / alias / role form)
│   ├── CharacterProjectSession.cs
│   ├── CharacterNodeKinds.cs
│   ├── CharacterNodeContributors.cs
│   └── CharacterInspectorDrawers.cs
├── Editor/                             (Properties, Animator, Sandbox UI)
│   └── General/
│       ├── CharacterPlaceholders.cs    (copies Placeholders/ onto new characters)
│       ├── LegacyPackScan.cs           (scan-only Official Pack / DNSpy rows)
│       ├── LegacyModImporter.cs        (writes selected rows + exo/reskin)
│       └── VanillaCharacterExtract.cs  (shipped hero → override Lab folder)
├── Placeholders/                       (deployed next to the DLL: rig, body, portrait)
└── docs/
```

Internal types keep historical namespaces (`LokrLab.Editor`,
`LokrLab.Projects`, `LokrLab.Editor.General`) inside this assembly.
`Editor/` naming matches Ability Lab: authoring UI, not Unity Editor tooling.

Runtime loading is not this module's job — `LokrCharacterLoader` reads the
same `LokrCharacterLab` category folders under any mod package.
