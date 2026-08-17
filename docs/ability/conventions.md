# Ability module — Conventions

- Suite module of `LokrLabPlugin` (`com.lokrmodding.lab`).
  `LokrAbilityLabPlugin` is a Log / Guid facade. Historical namespace
  `LokrAbilityLab` stays. Ability Lab no longer ships Harmony isolation;
  Sandbox is the embedded fight only.
- Public surface is `AbilityLabAccess` (open/close façade) and
  `AbilityLabAPI` / `ActionCardDescriptor` / `ActionCardCatalogKind`
  (card registry). Everything in `Editor/` is `internal`. Do not put the
  card registry on `LokrLabApi`. Third-party card plugins depend on
  `LokrLab.dll` (or keep using `AbilityLabAPI`).
- Character and Ability share this assembly; only the *patterns* were
  mirrored historically (two overlay screens; inspector hosts the same form).
- "Always regenerate known fields on save, never hand-preserve" applies
  to the envelope and to typed cards. Opaque cards / Advanced remainder
  keep `KeyValue.ToString` text (reindented, not reshaped). Never emit
  empty optional expressions.
- Both actives and passives live in this plugin. Character Lab does not
  write `ability.txt`. Custom FX folders are authored here; the Loader
  builds the prefabs.
