# Character Lab hover copy

Edit this file (or `Mods/LokrLab/character-hover.md`) without a rebuild.
Each `## key` section: first line is the title, the rest is the body.
Overlay keys replace these.

## character.general.Id
Character ID
Folder / engine id (`slug_token`). Not the player-facing Name. Files write `$alias` for this id.

## character.general.EntityType
Enemy/Summon
Off = Hero (`roster.json`, `AddHeroDefinition`). On = EnemySummon (no roster, `AddEnemyDefinition`). Same stats/skills/rig either way.

## character.general.Name
Name
English display (`UNIT_<id>_NAME_0001`). Blank Name fails readiness. Other locales are Localization.

## character.general.Description
Description
English flavor text (`UNIT_<NameStem>_LORE` in the hero room). Commits when the field loses focus — Enter inserts a newline. Close Lab and Reload in Game flush it so campaign lore updates without a restart.

## character.roster.Tier
Legend (roster)
Which `HeroRosterManager` list. Distinct from the `LEGEND` **state** flag. Meaningless for Enemy/Summon.

## character.roster.Locked
Locked
Default locked so a new hero is not free in a live save.

## character.roster.UnlockAchievement
Unlock Achievement
Achievement id ignored while Locked is off.

## character.file.EditVanillaHero
Edit Vanilla Hero
Copies a shipped hero into a minted slug_token folder. UniqueId and block keys stay vanilla so campaign and saves still see Gerald. Reconstructs the exo so Animator can edit clips. New Project stays a fork.

## character.create.Name
Create name
Display name written to the new profile.

## character.create.Slug
Create slug
Human stem of the minted folder id. Must start with a letter.

## character.create.SlugAuto
Slug Auto
Fills slug from Name while on.

## character.create.Alias
Create alias
`$alias` key written to this folder's aliases.json.

## character.create.AliasAuto
Alias Auto
Copies the slug while on.

## character.create.IdPreview
Create id preview
Folder id is minted `slug_token`.

## character.create.Role
Create role
Companion / Legend / Enemy-Summon. Same split as EntityType + roster Tier.

## character.aliases.Key
Alias key
This folder's aliases.json only. Key is `$alias`.

## character.aliases.Id
Alias id
The unique folder id that `$alias` resolves to.

## character.levels.Tab
Level tab
Level 1 is the full block. 2+ are rank-up diffs (`nextLevelArchetype`). Sandbox Level dropdown uses these ranks.

## character.levels.AddLevel
Add Level
Appends the next rank-up diff. Domain add, not list chrome.

## character.levels.StatName
Stat name
Arbitrary KV key. Combat looks up `stat(%CASTER, #name)`. Level 1 with zero stats is a readiness error.

## character.levels.StatValue
Stat value
Numeric override for this rank.

## character.states.Flag
State flag
Present-and-true is written; absent = off. `LEGEND` here is combat/cinematic, not roster Tier. Vanilla typos are real ids (`CANT_BE_ASLEPT`).

## character.appearance.Model
Combat prefab (Model)
`UnitDefinition.kind`. Sequence names (`Stand`, `Attack0`, …) come from **this** prefab. Unknown names throw at `StartAnimation`.

## character.appearance.AttackType
Attack Type
Suggestions RANGED / ARTILLERY / PHYSICAL / MAGIC. New heroes seed `MELEE` (legal, not in the combo).

## character.appearance.Icon
Icon
Unread by the base game. Roster banner is the **BANNER** portrait, not Icon. Round-trip only.

## character.appearance.Background
Background
Unread by the base game. Round-trip only.

## character.appearance.UnitOnMap
Unit On Map
Party-token exo part. Ignored when MAPMINI portrait is set (that slot wins).

## character.appearance.PortraitBackgroundColor
Portrait Background Color
6-digit hex tint for the map hero-bar portrait.

## character.skills.CinematicTag
Cinematic Tags
Ordered tags, **not** States (`Heroe` vanilla spelling, `Minion`, `LOCKED`, …).

## character.skills.SkillId
Skills (traits)
`skills` KV: hero-room trait circles. These ids must **not** also be Default Skill.

## character.skills.DefaultSkill
Default Skill
Separate `defaultSkill` KV. Must not appear in `skillProgression`. Empty is a readiness error for Heroes.

## character.skills.ProgressionLevel
Skill Progression rank
Hero room indexes ranks **1, 2, and 3** and throws if any is missing.

## character.skills.ProgressionSkill
Skill Progression skill
Sandbox grants the first interactive pick per rank (5-slot bar). Passives always grant.

## character.sound.AssetId
Sound Asset Id
Shared vanilla `DynamicSoundGroup*` name.

## character.sound.Event
Sound event
Combat-event key inside that group (`useSkill`, `death`, `walk`, …). Distinct from ability PlaySound cards and FXMega-internal audio.

## character.sound.Clip
Sound clip
Clip name played for that event.

## character.localization.Name
Locale name
Per-locale override. English is General, not a row here.

## character.localization.Description
Locale description
Per-locale override. English is General, not a row here.

## character.portraits.MINI
MINI portrait
Flat asset-bundle sprite. No animated fallback.

## character.portraits.BIG
BIG portrait
Flat asset-bundle sprite. No animated fallback.

## character.portraits.BANNER
BANNER portrait
Roster card art. Not Appearance Icon.

## character.portraits.MAPMINI
MAPMINI portrait
Exo part swap. Wins over Unit On Map. No animated fallback.

## character.portraits.MAP
MAP portrait
Defaults to animated exo. Custom image is a file replace, not a persisted flag.

## character.portraits.CHALLENGE
CHALLENGE portrait
Defaults to animated exo. Custom image is a file replace, not a persisted flag.

## character.readiness.Panel
Readiness checklist
Live Error/Warning list from CharacterReadinessRegistry. Row text is the check message.

## character.home.Reload
Reload in Game
Persist then `CharacterAPI.ReloadLabContent`. Re-open the hero room to verify. Does not restart the process.

## character.import.Sheet
Import Legacy Pack
Scan → check rows → write only checked items.

## character.import.AbilityLibrary
Import ability library
Required for imported abilities (existing library or typed new name).

## character.import.Hero
Import hero
Becomes a Character project.

## character.import.Ability
Import ability
Goes to the chosen ability library.

## character.import.Summon
Import summon
Summons/props become Enemy/Summon characters.

## animator.part.Name
Part name
Sprite id in `rig.json`.

## animator.part.Layer
Part layer
Rest-pose draw order (Scene Tree top=back; Frame list top=front).

## animator.part.Visible
Part visible
Editor-only. Does not write to the rig.

## animator.part.Pivot
Part pivot
Rest-pose pivot. Rotate/Scale orbit this. Group Pivot tool moves a **temp centroid**, not each rest pivot.

## animator.part.Replace
Replace part
Replaces this sprite in **all clips**.

## animator.part.RemoveFromClip
Remove from Clip
Removes the part from the **active clip only**, not Rest Pose.

## animator.part.CenterSelected
Center Selected
Needs multi-select. Centers the group.

## animator.clip.Name
Clip name
Exact `sequenceName` the Model prefab looks up. Missing Stand / Portrait-or-StandStatic throws in map/dialog.

## animator.clip.FrameCount
Frame count
Authored poses in this clip.

## animator.frame.Title
Rest Pose / Frame
Rest Pose seeds **new** clips only. Later Rest Pose edits do not move Walk / Attack.

## animator.frame.Copy
Copy frame
Copies the active pose (works on Rest Pose).

## animator.frame.PasteNew
Paste New
Disabled on Rest Pose until a real clip is copied.

## animator.frame.Override
Override frame
Skips excluded / approximate clipboard poses.

## animator.frame.Duration
Duration
Seconds this pose is **held**. The engine does not interpolate; it holds then jumps. Default 0.15.

## animator.frame.RootMotionX
Root X (px)
Cumulative root-motion X for this frame. Empty clears the **whole clip** curve, not just this sample.

## animator.frame.Easing
Easing
Bakes extra generated frames (`Linear` / EaseIn / Out / InOut). Not runtime interpolation.

## animator.frame.EasingSteps
Easing steps
Steps ≤ 0 = one block.

## animator.frame.Events
Frame events
`AbilityAction` fires OnAbilityAction; `AbilityEnd` ends the activity; `AbilityStart` comparisons are no-ops. Attack / SpecialAttack / SpellCast need Action+End; Death needs End or DeathActivity never finishes. Custom strings → OnAbilityCustomEvent.

## animator.frame.AddEvent
Add frame event
Adds a known or custom event string to this frame.

## animator.frame.AttachPoints
Attach points
Sockets on the **custom rig**, not the Model prefab. Vanilla combat: Head (speech), Chest (projectiles), Base (ground FX). Missing → origin.

## animator.frame.AttachPose
Attach pose
X / Y / Rot of one socket on this frame.

## animator.frame.PartPos
Part position
This frame only. Affine/shear poses cannot be edited by Move/Rotate/Scale; Convert to Editable bakes a TRS approximation.

## animator.frame.PartRot
Part rotation
This frame only. Degrees around the rest pivot.

## animator.frame.PartShear
Part shear
This frame only. Affine poses are read-only until Convert to Editable.

## animator.frame.PartScale
Part scale
This frame only.

## animator.frame.IncludePart
Include part
Adds the part to this frame only. Hidden on Rest Pose (every part exists).

## animator.frame.ExcludePart
Exclude part
Removes the part from this frame only, not Rest Pose.

## animator.toolbar.Select
Select (Q)
Click parts in the viewport. Does not move poses.

## animator.toolbar.Move
Move (W)
Drag position. Rotate/Scale orbit the rest pivot, not this tool.

## animator.toolbar.Rotate
Rotate (E)
Rotate around the rest pivot.

## animator.toolbar.Scale
Scale (R)
Uniform scale around the rest pivot.

## animator.toolbar.ScaleXY
Scale XY (Y)
Axis-aligned, not along the part's rotated axes.

## animator.toolbar.Pivot
Pivot (T)
The only tool that works on affine poses. Group Pivot moves a temp centroid, not each rest pivot.

## animator.toolbar.MassEdit
Mass Edit
Pose commits propagate to every multi-selected part across **every frame of the active clip**. Not a per-part flag. Unlike Replace, scoped to this clip.

## animator.toolbar.Preview
Preview
PIP of the same rig.

## animator.toolbar.AddReference
Add Reference
Reference overlay is a locked in-game size.

## animator.toolbar.Undo
Undo / Redo
Animator pose/clip history only, not Properties profile edits.

## animator.toolbar.History
History
Animator pose/clip history only, not Properties profile edits.

## animator.timeline.FrameChip
Frame chip
Authored pose. Click to scrub.

## animator.timeline.BakedChip
Baked chip
Easing ghost frame. Play needs more than one frame.

## animator.timeline.Play
Play
Plays the active clip. Needs more than one frame.

## animator.animations.Add
Add Animation
Opens presets for **this Model**, not the union of every prefab.

## animator.animations.Preset
Animation preset
Names this Model looks up. Offering HumanArcher `Attack0` on an Obelisk creates leftover angled clips.

## animator.animations.CustomName
Custom clip name
Exact `sequenceName` written to the rig.

## animator.reference.Pose
Reference pose
Overlay only; does not change the rig.

## animator.reference.Pos
Reference position
Overlay only.

## animator.reference.Rot
Reference rotation
Overlay only.

## animator.reference.Opacity
Reference opacity
Overlay only. Scale is locked on purpose.

## animator.file.AtlasGrid
Slice Atlas rows/cols
Uniform grid import into parts.

## animator.file.PickIslands
Pick Islands
Non-uniform island import into parts.

## sandbox.Level
Sandbox Level
Always Level 1, 2, or 3. Grants progression: first interactive pick per rank, passives always, cap 5 interactive.

## sandbox.Start
Start sandbox
Character: embeds `fighttesterempty` and spawns this hero + BanditRaider. Ability: same hole with the first used-by Character. Encounter: plays the open encounter (picks a Character if the file only has a Hero Spawn Point).

## sandbox.Stop
Stop sandbox
Unloads the fight. Leaving the workspace also stops.

## sandbox.LoadEncounter
Load Encounter
Picks an Encounter project and starts it in the Sandbox hole. This hero spawns at the encounter's first Hero Spawn Point (at the level selected above); every other authored combatant, terrain, prop, and trigger plays as authored. Refuses if the encounter has no Hero Spawn Point.
