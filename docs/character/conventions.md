# Character module — Conventions

- Suite module of `LokrLabPlugin` (`com.lokrmodding.lab`).
  `LokrCharacterLabPlugin` is a Log / Guid facade. Historical namespaces
  (`LokrLab`, `LokrCharacterLab`) stay.
- Host calls go through `LokrLabApi.Host` (`Lab.cs`) or events.
- Public APIs that third parties already compile against stay in
  namespace `LokrLab` (`CharacterCreatorAPI`, `CharacterLabOptionsAPI`).
  Depend on `LokrLabPlugin.Guid`.
- Animator / Properties state lives on static orchestrators
  (`RigEditorScene`, `PropertiesWorkstationScene`). Panels read and
  write that state; they are not `MonoBehaviour`s.
- One scroll viewport: persistent inspector hosts are the only
  `ScrollRect`. `PropertiesCategoryHost` and `InspectorPanel.BuildInto`
  must not nest another (zero-height collapse).
- `/// <summary>` on every `public` / `internal` member.
