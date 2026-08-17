# Ability Lab hover copy

Edit this file (or `Mods/LokrLab/ability-hover.md`) without a rebuild.
Each `## key` section: first line is the title, the rest is the body.
Overlay keys replace these. Token keys (`token.%CASTER`) are appended
when the hovered field's current value starts with `%` or `#`.

## envelope.LocalizationId
Localization Id
SKILL_* key stem. Blank uses the ability folder id.

## envelope.TeamFilter
Team Filter
TEAM_ALL / TEAM_FRIENDLY / TEAM_ENEMY. Ignored on POINT_TARGET (parser forces TEAM_ALL).

## envelope.CastRange
Cast Range
Hex range expression. Required on non-PASSIVE abilities. Empty strings drop the ability from the registry.

## envelope.CastMinRange
Cast Min Range
Optional minimum hex range. Forced to 0 on SELF_TARGET.

## envelope.Cooldown
Cooldown
Turns until the ability can be used again. Required on non-PASSIVE.

## envelope.PrewarmCooldown
Prewarm Cooldown
Optional. Starting cooldown when the fight begins. Empty omits the key.

## envelope.APCost
AP Cost
Action-point cost expression. Required on non-PASSIVE.

## envelope.CanExecute
Can Execute
Optional condition. If false, the ability cannot be chosen.

## envelope.HitChanceModifier
Hit Chance Modifier
Optional expression added to hit chance. Parses for passives too; only meaningful with HAS_CHANCE_TO_HIT.

## envelope.Icon
Icon
AbilityIcons stem. Hero-room traits need PASSIVE and a non-null Icon.

## envelope.AnimationId
Animation Id
Clip name on the caster rig. Needs AbilityAction / AbilityEnd frame events, or use NOANIMATION.

## envelope.CastFXId
Cast FX
FXMega name played on cast. Unknown names throw in combat unless a custom fx/ folder exists.

## envelope.AOEKind
AOE Shape
RANGE_CIRCLE or RANGE_TUNNEL for a new pick. RANGE_CONE parses but combat never fills it.

## envelope.AOETeamFilter
AOE Team Filter
Who the AOE hexes hit. Separate from Ability Team Filter.

## envelope.AOERange
AOE Range
Required when AOE is on. Empty required fields drop the ability.

## envelope.AOEMinRange
AOE Min Range
Optional inner hole of the AOE shape.

## envelope.AOEWidth
AOE Width
Required unless RANGE_CIRCLE. Tunnel/cone width in hexes.

## envelope.AOECenterOnCaster
Center On Caster
Required when AOE is on. Center the shape on the caster instead of the target hex.

## envelope.AOEAffectsCaster
Affects Caster
Required when AOE is on. Whether the caster is in the affected set.

## envelope.CastFX.Attach
Cast FX attach
Socket on the caster (`Chest`, `Base`, `Head`, `CastPoint`, `RayPoint`), not `#Chest`. Custom fx/ folders only.

## envelope.CastFX.Duration
Cast FX duration
Seconds the custom sprite FX plays. Custom fx/ folders only.

## envelope.CastFX.PixelsPerUnit
Cast FX pixels per unit
Higher values draw smaller. Custom fx/ folders only.

## envelope.CastFX.ChooseSprite
Choose Cast FX sprite
Browse a PNG into fx/<name>/. Changing Cast FX away from a custom name deletes that folder.

## envelope.CastFX.ClearSprite
Clear Cast FX sprite
Removes the custom fx/ folder and restores the previous Cast FX id.

## envelope.behavior.MELEE
MELEE
Melee targeting core. Almost always pairs with HAS_CHANCE_TO_HIT on basics. Distinct from Hit tag #MELEE.

## envelope.behavior.UNIT_TARGET
UNIT_TARGET
Must pick a unit. Most common vanilla flag.

## envelope.behavior.POINT_TARGET
POINT_TARGET
Hex / point pick. Parser forces Team Filter to TEAM_ALL.

## envelope.behavior.SELF_TARGET
SELF_TARGET
Never used in vanilla. Parser forces cast range and min to 0.

## envelope.behavior.AOE
AOE
Requires AOE Range / Kind / CenterOnCaster / AffectsCaster or parse throws. Without this flag, AOE fields in KV are ignored.

## envelope.behavior.POSITIVE_EFFECT
POSITIVE_EFFECT
Classification, not targeting.

## envelope.behavior.NEGATIVE_EFFECT
NEGATIVE_EFFECT
Classification, not targeting.

## envelope.behavior.PASSIVE
PASSIVE
Skips combat envelope (range, cooldown, AP, AnimationID, CastFX). Hero-room traits also need a non-null Icon.

## envelope.behavior.NEEDS_CLEAR_TERRAIN
NEEDS_CLEAR_TERRAIN
Targeting rejects occupied / blocked hexes.

## envelope.behavior.NEEDS_CLEAR_LINE_OF_SIGHT_EXCEPT_UNITS
NEEDS_CLEAR_LINE_OF_SIGHT_EXCEPT_UNITS
LoS check that ignores units. Rare (vanilla 2).

## envelope.behavior.NEEDS_VALID_TERRAIN
NEEDS_VALID_TERRAIN
Hex must be a legal board cell.

## envelope.behavior.CINEMATIC
CINEMATIC
Vanilla only as CINEMATIC | UNIT_TARGET (3 files).

## envelope.behavior.HAS_CHANCE_TO_HIT
HAS_CHANCE_TO_HIT
Rolls hit chance. Almost always on melee basics.

## envelope.behavior.DOESNT_CONSUME_MOVE
DOESNT_CONSUME_MOVE
Cast does not spend the move. Often with FAKE_ACTION.

## envelope.behavior.FAKE_ACTION
FAKE_ACTION
Not a real spend; usually pairs with DOESNT_CONSUME_MOVE.

## modifier.Card
Modifier
Nested modifier id is global. Passive 1 auto-applies in UnitAdded().

## modifier.Id
Modifier Id
Global modifier registry key. Collides across abilities.

## modifier.Passive
Passive
Auto-apply on unit added, duration 0, instigator = ability id.

## modifier.ModifierFXName
Modifier FX Name
Third FX path (duration FXMega). Same name catalog as Cast / Hit EffectName.

## modifier.IncompatibleStates
Incompatible States
AddModifier rejects if the target has any of these states. Vanilla usage is rare (one each of CANT_BE_*).

## modifier.AutoRemoveTags
Auto Remove Tags
On accept, remove modifiers with these tags first. Vanilla: wbf_phase, status_effect.

## modifier.AutoRemoveModifierIds
Auto Remove Modifier Ids
On accept, remove those ids first. Vanilla: none.

## modifier.PropertiesAdd
PropertiesAdd
Inner KV stat adds. Unknown keys warn.

## modifier.ExtraKv
Extra modifier KV
Remainder (PropertiesMult, events already extracted, unknown keys).

## special.Slot
Special slot
Tooltip / description data vars (SKILL_*_DESCRIPTION_DATA). Slot is 01, 02, …

## special.VarType
Special var_type
Type of the description-data variable.

## special.Name
Special Name
Variable name referenced from localization.

## special.Value
Special Value
Expression written into that slot.

## special.Add
Add variable
Appends a Special row (next 01/02 slot).

## ai.BlockName
AI block name
Scoring for custom abilities. Empty Considerations warn. PerAffectedAI is not an action.

## ai.InnerKv
AI inner KV
Raw AIConfigB / AIBrain* body.

## ai.Add
Add AIConfigB
Appends an AI scoring block.

## advanced.OpaqueTopLevel
Opaque top-level
Unrecognized root blocks, reindented on save.

## sandbox.Level
Sandbox Level
Always Level 1, 2, or 3. Rank of the first used-by hero, not this ability.

## sandbox.Start
Start sandbox
Embeds a real fight in the hole (StartEmbeddedFight). Not a mannequin preview. Same Sandbox as Character and Encounter.

## sandbox.Stop
Stop sandbox
Unloads the embedded fight.

## ability.create.Name
Ability name
Display name written to SKILL_*_NAME.

## ability.create.Slug
Ability slug
Human stem of the minted folder id. Must start with a letter.

## ability.create.SlugAuto
Slug Auto
Fills slug from Name while on.

## ability.create.Alias
Ability alias
$self $alias key written to aliases.json.

## ability.create.AliasAuto
Alias Auto
Copies the slug while on.

## ability.create.IdPreview
Ability id preview
Folder id is minted slug_token. Files write $alias for this id.

## ability.library.create.Name
Library name
Display name written to project.json.

## ability.library.create.Slug
Library slug
Human stem of the minted library folder id.

## ability.library.create.SlugAuto
Library slug Auto
Fills slug from the library name while on.

## ability.library.create.Alias
Library alias
Shown by the shared create strip; libraries do not write aliases.json.

## ability.library.create.AliasAuto
Library alias Auto
Copies the slug while on.

## ability.library.create.IdPreview
Library id preview
Library folder is minted slug_token.

## card.Hit
Hit
Applies a hit to Target. Nested InitActions / Actions / AlwaysActions run around the connect.

## field.Hit.Target
Hit Target
Unit that receives this hit. Common picks: %TARGET, %CASTER, %SOURCE, %HITTARGET. Boss-only tokens stay off the list unless the loaded file already uses them.

## field.Hit.EffectName
Hit EffectName
FXMega played on connect. Unknown names throw in combat unless a custom fx/ folder exists.

## field.Hit.Tags
Hit Tags
Must be on HitAction.ValidateTags (#MELEE, #PROJECTILE, #TARGETED, …). #RANGED is not legal.

## field.Hit.Enqueue
Hit Enqueue
Whether this hit is queued rather than applied immediately.

## field.Hit.Backstab
Hit Backstab
Backstab bonus expression. Empty omits the key.

## field.Hit.ExtraKv
Hit Extra KV
Target blocks and unknown keys, reindented on save.

## card.AddDamage
Add Damage
Adds Damage of Type to the current hit. Usually nested under Hit InitActions / Actions.

## field.AddDamage.Type
Damage Type
DAMAGE_* enum. Unknown types fail parse.

## field.AddDamage.Damage
Damage
Expression, often stat(%CASTER, #baseDamage).

## field.AddDamage.ExtraKv
AddDamage Extra KV
Unknown keys, reindented on save.

## card.ApplyModifier
Apply Modifier
Adds ModifierName to Target for Duration. Nested modifiers with Passive 1 auto-apply on UnitAdded.

## field.ApplyModifier.ModifierName
Apply Modifier Name
Must exist on this ability or in ability_modifiers.

## field.ApplyModifier.Target
Apply Modifier Target
Unit that receives the modifier. Common picks: %TARGET, %CASTER, %SOURCE.

## field.ApplyModifier.Duration
Apply Modifier Duration
Turns the modifier lasts. 0 with Passive is auto-apply.

## field.ApplyModifier.Source
Apply Modifier Source
Instigator unit. Usually %CASTER.

## field.ApplyModifier.Refresh
Apply Modifier Refresh
Whether re-applying refreshes duration.

## field.ApplyModifier.ExtraKv
ApplyModifier Extra KV
Unknown keys, reindented on save.

## card.RemoveModifier
Remove Modifier
Removes ModifierName from Target.

## field.RemoveModifier.ModifierName
Remove Modifier Name
Must exist on this ability or in ability_modifiers.

## field.RemoveModifier.Target
Remove Modifier Target
Unit that loses the modifier.

## field.RemoveModifier.ExtraKv
RemoveModifier Extra KV
Unknown keys, reindented on save.

## card.AttachEffect
Attach Effect
Attaches EffectName (FXMega) to Target.

## field.AttachEffect.EffectName
Attach Effect Name
FXMega played on the unit. Unknown names throw unless a custom fx/ folder exists.

## field.AttachEffect.Target
Attach Effect Target
Unit the FX attaches to.

## field.AttachEffect.ExtraKv
AttachEffect Extra KV
Unknown keys, reindented on save.

## card.TrackingProjectile
Tracking Projectile
Spawns a projectile prefab (Model) that tracks Target. OnProjectileHitUnit fires on connect.

## field.TrackingProjectile.Model
Projectile Model
Prefab name (not an FXMega). Vanilla catalog plus custom projectiles/ folders.

## field.TrackingProjectile.Target
Projectile Target
Unit the projectile tracks. Usually %TARGET.

## field.TrackingProjectile.SourcePos
Projectile SourcePos
Position expression. Use unitPosition(%CASTER, #Chest) sockets, not Cast FX attach names.

## field.TrackingProjectile.TargetPos
Projectile TargetPos
Position expression for the aim point.

## field.TrackingProjectile.TargetAttach
Projectile TargetAttach
Socket name on the tracked unit.

## field.TrackingProjectile.ExtraKv
TrackingProjectile Extra KV
Unknown keys, reindented on save.

## card.Heal
Heal
Heals Target by HealAmount.

## field.Heal.Target
Heal Target
Unit that receives healing.

## field.Heal.HealAmount
Heal Amount
Expression, often a number or stat lookup.

## field.Heal.ExtraKv
Heal Extra KV
Unknown keys, reindented on save.

## card.Knockback
Knockback
Pushes Target away from Center.

## field.Knockback.Target
Knockback Target
Unit that is pushed.

## field.Knockback.Center
Knockback Center
Origin of the push. Extra token %knockbackCenter is legal here.

## field.Knockback.Strength
Knockback Strength
Hexes / force expression.

## field.Knockback.Animation
Knockback Animation
Clip played on the pushed unit.

## field.Knockback.ExtraKv
Knockback Extra KV
Unknown keys, reindented on save.

## card.Conditional
Conditional
Runs Actions when Condition is true, ElseActions otherwise.

## field.Conditional.Condition
Conditional Condition
Boolean expression.

## field.Conditional.ExtraKv
Conditional Extra KV
Unknown keys, reindented on save.

## card.ActOnTargets
Act On Targets
Iterates matching units. Children see %UNIT / %index (or the IteratorName you set).

## field.ActOnTargets.IteratorName
Iterator Name
Token bound to each unit. Vanilla uses %UNIT.

## field.ActOnTargets.IteratorIndexName
Iterator Index Name
Token bound to the loop index. Vanilla uses %index.

## field.ActOnTargets.ExtraKv
ActOnTargets Extra KV
Unknown keys, reindented on save.

## card.Delay
Delay
Waits Time, then runs nested Actions.

## field.Delay.Time
Delay Time
Seconds (expression).

## field.Delay.ExtraKv
Delay Extra KV
Unknown keys, reindented on save.

## card.Times
Times
Repeats nested Actions this many times. Advanced.

## field.Times.Times
Times count
Repeat count expression.

## field.Times.ExtraKv
Times Extra KV
Unknown keys, reindented on save.

## card.PlaySound
Play Sound
Plays Sound on Unit via MasterAudio / unit.PlaySound. Not FXMega-internal audio and not the unit soundConfig group.

## field.PlaySound.Sound
Play Sound name
Graph catalog clip, not a unit soundConfig event.

## field.PlaySound.Unit
Play Sound Unit
Unit the voice attaches to. Usually %CASTER.

## field.PlaySound.ExtraKv
PlaySound Extra KV
Unknown keys, reindented on save.

## card.StopSound
Stop Sound
Stops Sound on Unit.

## field.StopSound.Sound
Stop Sound name
Graph catalog clip to stop.

## field.StopSound.Unit
Stop Sound Unit
Unit the voice is attached to.

## field.StopSound.ExtraKv
StopSound Extra KV
Unknown keys, reindented on save.

## card.SpawnUnit
Spawn Unit
Spawns UnitName at Position. OnSpawn runs on the new unit.

## field.SpawnUnit.UnitName
Spawn UnitName
Loaded unit definition id.

## field.SpawnUnit.Position
Spawn Position
Position expression.

## field.SpawnUnit.UnitGroup
Spawn UnitGroup
Group token for later ActOnTargets.

## field.SpawnUnit.IsAI
Spawn IsAI
Whether the spawn is AI-controlled.

## field.SpawnUnit.ExtraKv
SpawnUnit Extra KV
Unknown keys, reindented on save.

## card.SetStat
Set Stat
Sets Stat on Target to Value.

## field.SetStat.Target
Set Stat Target
Unit whose stat changes.

## field.SetStat.Stat
Set Stat name
Combat looks up stat(%CASTER, #name). Use the # token, not a bare word.

## field.SetStat.Value
Set Stat Value
Expression written into that stat.

## field.SetStat.ExtraKv
SetStat Extra KV
Unknown keys, reindented on save.

## card.CallFunction
Call Function
Reflection into a shipped C# helper under Ironhide.Legends.Content.Abilities. Unknown Function names fail parse.

## field.CallFunction.Function
CallFunction Function
One of the 16 shipped type names. Empty-filter helpers throw if they match nobody.

## field.CallFunction.ExtraKv
CallFunction Extra KV
Unknown keys, reindented on save.

## card.PlayActivityAnimation
Play Activity Animation
Clip on the caster activity (no Target field). Needs AbilityAction / AbilityEnd on that clip.

## field.PlayActivityAnimation.Animation
Activity Animation
Clip name on the caster rig.

## field.PlayActivityAnimation.ExtraKv
PlayActivityAnimation Extra KV
Unknown keys, reindented on save.

## card.PlayAnimation
Play Animation
Plays Animation on Target. Advanced.

## field.PlayAnimation.Animation
Play Animation clip
Clip on that unit's rig. Unknown names throw at StartAnimation.

## field.PlayAnimation.Target
Play Animation Target
Unit that plays the clip.

## field.PlayAnimation.ExtraKv
PlayAnimation Extra KV
Unknown keys, reindented on save.

## card.OverrideAnimation
Override Animation
Replaces Animation on Target until cleared. Advanced.

## field.OverrideAnimation.Animation
Override Animation clip
Clip on that unit's rig.

## field.OverrideAnimation.Target
Override Animation Target
Unit whose clip is overridden.

## field.OverrideAnimation.ExtraKv
OverrideAnimation Extra KV
Unknown keys, reindented on save.

## card.GiveArmor
Give Armor
Adds ArmorAmount to Target. Advanced.

## field.GiveArmor.Target
Give Armor Target
Unit that receives armor.

## field.GiveArmor.ArmorAmount
Armor Amount
Expression.

## field.GiveArmor.ExtraKv
GiveArmor Extra KV
Unknown keys, reindented on save.

## card.KillUnit
Kill Unit
Kills Target. Advanced.

## field.KillUnit.Target
Kill Unit Target
Unit that dies.

## field.KillUnit.ExtraKv
KillUnit Extra KV
Unknown keys, reindented on save.

## card.MoveUnit
Move Unit
Moves Target to Position. Advanced.

## field.MoveUnit.Target
Move Unit Target
Unit that is moved.

## field.MoveUnit.Position
Move Unit Position
Position expression.

## field.MoveUnit.ExtraKv
MoveUnit Extra KV
Unknown keys, reindented on save.

## card.TriggerSkill
Trigger Skill
Fires Skill on Target. Advanced.

## field.TriggerSkill.Skill
Trigger Skill id
Ability id to fire.

## field.TriggerSkill.Target
Trigger Skill Target
Unit that casts / receives the skill.

## field.TriggerSkill.ExtraKv
TriggerSkill Extra KV
Unknown keys, reindented on save.

## card.ResetCooldown
Reset Cooldown
Clears Skill cooldown on Target. Advanced.

## field.ResetCooldown.Target
Reset Cooldown Target
Unit whose cooldown is cleared.

## field.ResetCooldown.Skill
Reset Cooldown Skill
Ability id.

## field.ResetCooldown.ExtraKv
ResetCooldown Extra KV
Unknown keys, reindented on save.

## card.OffsetCooldown
Offset Cooldown
Adds Offset turns to Skill cooldown on Target. Advanced.

## field.OffsetCooldown.Target
Offset Cooldown Target
Unit whose cooldown changes.

## field.OffsetCooldown.Skill
Offset Cooldown Skill
Ability id.

## field.OffsetCooldown.Offset
Offset Cooldown Offset
Turns (expression). Negative can reduce remaining cooldown.

## field.OffsetCooldown.ExtraKv
OffsetCooldown Extra KV
Unknown keys, reindented on save.

## card.Lua
Lua
MoonSharp chunk compiled at parse. Field Action is `return function(ctx) … end`. Rare in vanilla (five files); offered under Advanced. Plugins register more cards with AbilityLabAPI.RegisterActionCard.

## field.Lua.Action
Lua Action
The Lua body. Saved as one quoted KV string. Use single quotes inside; a double quote cannot round-trip. Empty Action fails parse.

## field.Lua.ExtraKv
Lua Extra KV
Unknown keys, reindented on save.

## card.opaque
Opaque card
Unregistered type; raw KV is preserved on save.

## card.stack.Actions
Actions
Nested action list. Same add / Advanced menu as event hats.

## card.stack.InitActions
InitActions
Runs before the parent Hit connects.

## card.stack.AlwaysActions
AlwaysActions
Runs even if the Hit misses.

## card.stack.ElseActions
ElseActions
Runs when Conditional Condition is false.

## card.stack.OnSpawn
OnSpawn
Runs on the newly spawned unit.

## card.stack.ActionsIfFound
ActionsIfFound
ActOnTargets branch when at least one unit matched.

## card.stack.ActionsIfNotFound
ActionsIfNotFound
ActOnTargets branch when nobody matched.

## event.OnAbilityStart
OnAbilityStart
Fires when the cast begins / Cast FX plays.

## event.OnAbilityAction
OnAbilityAction
Fires on clip frame AbilityAction, or immediately when AnimationID is NOANIMATION. Most actives put Hit / projectile here.

## event.OnAbilityCustomEvent
OnAbilityCustomEvent
Fires when the clip raises a custom frame-event string.

## event.OnProjectileHitUnit
OnProjectileHitUnit
Fires when a tracking projectile hits a unit.

## event.OnProjectileMissedUnit
OnProjectileMissedUnit
Fires when a tracking projectile misses a unit.

## event.OnProjectileDestinationReached
OnProjectileDestinationReached
Fires when a tracking projectile arrives at its destination.

## event.OnCustomTargeting
OnCustomTargeting
Fires for custom targeting flows.

## event.OnThink
OnThink
AI think tick. Used by AIConfigB scoring, not player casts.

## event.OnAttackStart
OnAttackStart
Parse-legal, but combat never fires this hat. Prefer OnAbilityStart or a modifier OnPreHit.

## event.OnAttackAction
OnAttackAction
Parse-legal, but combat never fires this hat. Prefer OnAbilityAction.

## event.OnAttacked
OnAttacked
Parse-legal, but combat never fires this hat. Prefer a modifier OnPreHit / OnDamaged.

## event.OnAdded
OnAdded
Modifier: fires when the modifier is applied.

## event.OnRemoved
OnRemoved
Modifier: fires when the modifier is removed.

## event.OnTurnStarted
OnTurnStarted
Modifier: this unit's turn started.

## event.OnTurnFinished
OnTurnFinished
Modifier: this unit's turn finished.

## event.OnTurnStartedGlobal
OnTurnStartedGlobal
Modifier: any unit's turn started.

## event.OnTurnFinishedGlobal
OnTurnFinishedGlobal
Modifier: any unit's turn finished.

## event.OnPreHit
OnPreHit
Modifier: before a hit resolves. High-value combat hook.

## event.OnPostHit
OnPostHit
Modifier: after a hit resolves.

## event.OnHitEnd
OnHitEnd
Modifier: hit pipeline finished.

## event.OnHitStart
OnHitStart
Modifier: hit pipeline started.

## event.OnDamaged
OnDamaged
Modifier: this unit took damage.

## event.OnStartFight
OnStartFight
Modifier: fight began.

## event.OnVictory
OnVictory
Modifier: fight won.

## event.OnSpawn
OnSpawn
Modifier: this unit spawned. Default modifier hat.

## event.OnAbilityEnd
OnAbilityEnd
Parse-legal on modifiers, but combat never fires this hat.

## event.OnAttackEnd
OnAttackEnd
Parse-legal on modifiers, but combat never fires this hat.

## event.OnPreAttack
OnPreAttack
Parse-legal on modifiers, but combat never fires this hat. Prefer OnPreHit.

## event.OnPostAttack
OnPostAttack
Parse-legal on modifiers, but combat never fires this hat. Prefer OnPostHit.

## event.OnAttack
OnAttack
Parse-legal on modifiers, but combat never fires this hat.

## event.OnUnitMoved
OnUnitMoved
Parse-legal on modifiers, but combat never fires this hat. Prefer OnUnitLeavingNode / OnUnitEnteredNode.

## event.OnHitPreResultGlobal
OnHitPreResultGlobal
Parse-legal on modifiers, but combat never fires this hat.

## event.OnUnitDamaged
OnUnitDamaged
Modifier: a unit was damaged (broader than OnDamaged).

## event.OnEndTurnSkillCheck
OnEndTurnSkillCheck
Modifier: end-of-turn skill check.

## event.OnUnitLeavingNode
OnUnitLeavingNode
Modifier: a unit is leaving a hex.

## event.OnUnitEnteredNode
OnUnitEnteredNode
Modifier: a unit entered a hex.

## event.OnPreHitGlobal
OnPreHitGlobal
Modifier: before any hit resolves.

## event.OnHitPreProcessDamages
OnHitPreProcessDamages
Modifier: before this hit's damage is processed.

## event.OnHitPreProcessDamagesGlobal
OnHitPreProcessDamagesGlobal
Modifier: before any hit's damage is processed.

## event.OnHitPreResult
OnHitPreResult
Modifier: before this hit's result is applied.

## event.OnPostHitGlobal
OnPostHitGlobal
Modifier: after any hit resolves.

## event.OnUnitDiedGlobal
OnUnitDiedGlobal
Modifier: a unit died.

## event.OnUnitHealedGlobal
OnUnitHealedGlobal
Modifier: a unit was healed.

## event.OnDefeat
OnDefeat
Modifier: fight lost.

## event.OnFightFinished
OnFightFinished
Modifier: fight ended (win or lose).

## event.OnUnitSpawnedGlobal
OnUnitSpawnedGlobal
Modifier: any unit spawned.

## token.%CASTER
%CASTER
The unit that cast this ability.

## token.%SOURCE
%SOURCE
Instigator / source unit for this action (often the caster).

## token.%TARGET
%TARGET
The ability's selected target unit.

## token.%HITTARGET
%HITTARGET
Unit that was hit (projectile or melee connect).

## token.%HITSOURCE
%HITSOURCE
Unit that dealt the hit.

## token.%ATTACKER
%ATTACKER
Attacker in an attack-event context.

## token.%ATTACKED
%ATTACKED
Defender in an attack-event context.

## token.%UNIT
%UNIT
Current iterator unit (ActOnTargets / similar).

## token.%unit
%unit
Same role as %UNIT; some vanilla files use the lowercase token.

## token.%newTarget
%newTarget
Retargeted unit from ActOnTargets / retarget helpers.

## token.%knockbackCenter
%knockbackCenter
Center unit or point for Knockback.

## token.#MELEE
#MELEE
Legal Hit tag: melee connect.

## token.#PROJECTILE
#PROJECTILE
Legal Hit tag: projectile connect. Use this instead of #RANGED.

## token.#TARGETED
#TARGETED
Legal Hit tag: a selected unit, not an environmental pulse.

## token.#MAGICAL
#MAGICAL
Legal Hit tag: magical damage.

## token.#AOE
#AOE
Legal Hit tag: area pulse.

## token.#MODIFIER
#MODIFIER
Legal Hit tag: modifier-sourced hit.

## token.#ENVIRONMENTAL
#ENVIRONMENTAL
Legal Hit tag: terrain / environment, not a selected unit.

## token.#RAY
#RAY
Legal Hit tag: ray / beam.

## token.#REFLECTED
#REFLECTED
Legal Hit tag: reflected damage.

## token.#INTERNAL
#INTERNAL
Legal Hit tag: internal / scripted hit.

## token.#FREEZEDAMAGE
#FREEZEDAMAGE
Legal Hit tag: freeze damage.

## token.#BURNDAMAGE
#BURNDAMAGE
Legal Hit tag: burn damage.

## token.#CANTBEBLOCKED
#CANTBEBLOCKED
Legal Hit tag: cannot be blocked.

## token.#CANTBEDODGED
#CANTBEDODGED
Legal Hit tag: cannot be dodged.

## token.#CANTBESHIELDED
#CANTBESHIELDED
Legal Hit tag: cannot be shielded.

## token.#GLARE_SUPER_RAY
#GLARE_SUPER_RAY
Legal Hit tag: WBF glare super ray.

## token.#GLARE_TOWER_OR_CRYSTAL
#GLARE_TOWER_OR_CRYSTAL
Legal Hit tag: WBF glare tower or crystal.

## token.#HEX_BLAST_FIRST_TIME
#HEX_BLAST_FIRST_TIME
Legal Hit tag: first Hex Blast connect.

## token.#baseDamage
#baseDamage
Common stat: the unit's base damage.

## token.#health
#health
Common stat: current health.

## token.#rangedAttackRange
#rangedAttackRange
Common stat: ranged attack range.

## token.#Chest
#Chest
Attach literal for unitPosition / projectiles (chest socket). Distinct from Cast FX attach Chest (no #).

## token.#Head
#Head
Attach literal for speech / unitPosition (head socket).

## token.#Base
#Base
Attach literal for ground FX (base socket).

## token.#CastPoint
#CastPoint
Attach literal for cast-origin sockets.
