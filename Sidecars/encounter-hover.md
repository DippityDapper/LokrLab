# Encounter Lab hover copy

Edit this file (or `Mods/LokrLab/encounter-hover.md`) without a rebuild.
Each `## key` section: first line is the title, the rest is the body.
Overlay keys replace these.

## encounter.create.Name
Create name
Display name on `project.json`. Not the folder id. Sandbox and the Project Browser show this.

## encounter.create.Slug
Create slug
Human stem of the minted folder id under `Mods/LokrLab/LokrEncounterLab/`. Must start with a letter.

## encounter.create.SlugAuto
Slug Auto
Fills slug from Name while on.

## encounter.create.Alias
Create alias
`$alias` key written to this folder's aliases.json. Same rules as Character.

## encounter.create.AliasAuto
Alias Auto
Copies the slug while on.

## encounter.create.IdPreview
Create id preview
Folder id is minted `slug_token`. That id is the Encounter project id, not a unit id.

## encounter.create.Blank
Blank floor
New encounters write `tilesDefault` false. Show board strips the template Tilemap. Unblock hexes, then paint from the Node Tree. Old files without the key keep the host floor.

## encounter.template
Template
`templates` prefab name loaded by the fight embed. Picker is empty-enough hosts only: `fighttesterempty` (open field) and `combat_bridge` (bridge, no enemy spawns). `combat_wip` looks like the default. `combat_blank` is not empty. Sandbox uses the in-memory name (no auto-save).

## encounter.exploration.Enabled
Exploration
When on, Sandbox parks every BadSide combatant (excluded from the initiative bar and turn order) until a pocket aggroes. Off is today's instant fight — every combatant joins initiative immediately. Default off; existing files are unaffected.

## encounter.exploration.DefaultAggroRadius
Default aggro radius (hexes; a combatant's own radius wins if set)
Hex-distance trigger used by any BadSide combatant that doesn't set its own Aggro radius. Default 4. Only matters while Exploration is on.

## encounter.combatants.Add
Add Combatant
Opens the Combatants catalogue. Select a card for Stand preview, then Add or double-click. Empty list is legal to save. Sandbox refuses zero GoodSide.

## encounter.catalogue.Search
Search
Filters catalogue cards by display name and/or id. Case-insensitive substring.

## encounter.catalogue.Add
Add selected
Appends the highlighted catalogue card. Select a card first. Double-click a card does the same. Drag a card onto a Setup hex to add and place it. A ghost follows the cursor while the card is out of the list. Release off the hole does not add.

## encounter.catalogue.Preview
Preview
Characters and units play the Stand clip (or the first animation). Props show the scenario prefab. One live preview for the selected card, not one exo per row.

## encounter.terrains.Import
Import from stage
Scan another `templates` prefab and add terrains that have hex floor art. Same terrain id from two stages is legal — the tile stamp stores the source template.

## encounter.terrains.Custom
Add custom terrain
Mints a named stub (`custom_N`) for later art. Cannot paint until sprites exist. Import from a stage to stamp floor tiles now.

## encounter.terrain.Name
Terrain name
Display label in the Node Tree. Does not change the vanilla `terrainId`.

## encounter.terrain.Source
Terrain source
`template` is this encounter's host room. `import` is another stage prefab. `custom` is a Lab stub with no floor art yet.

## encounter.terrain.Use
Use for Paint
Selects this terrain and switches Setup to Paint Terrain. Then left-drag on the board.

## encounter.terrain.Remove
Remove Terrain
Drops an import or custom row. Host template rows stay — the next scan puts them back.

## encounter.triggers.Add
Add Trigger
Mints a new trigger id, adds it to the catalog, arms the Trigger tool with it, and selects the new row. Left-drag on the board to paint it.

## encounter.trigger.Rename
Id (rename)
Changes the trigger's id and updates every painted hex and every combatant's Trigger field that referenced the old id. Must be a legal, unused slug.

## encounter.trigger.Pocket
Pocket this trigger wakes
Optional. When set, entering the painted region wakes every BadSide combatant in this pocket at once, regardless of their own Trigger field. Blank means this trigger only wakes combatants that individually set it in their own Trigger field.

## encounter.trigger.Paint
Paint
Selects this trigger and switches Setup to the Trigger tool. Left-drag paints, right-drag erases. Shows immediately as the red overlay, even before hovering.

## encounter.trigger.Remove
Remove Trigger
Drops the catalog row, clears every painted hex with this id, and clears it from any combatant's Trigger field (those combatants fall back to their Aggro radius).

## encounter.combatant.Source
Source
`character` (folder id → definition) or `unit` (vanilla / loaded unit id). Character defaults GoodSide; unit defaults BadSide. Not a live edit of the source project. Hero Spawn Point is a third option — no card to pick, just a GoodSide hex position with no fixed hero; whoever loads the encounter (Sandbox, later Adventures) spawns their character there. Encounter Sandbox Start on a spawn-only file opens a Character pick to fill the first one.

## encounter.combatant.Project
Character project
Read-only `projectId` (folder id = Definitions key). Jump switches session; no split-view. Missing folder: listed, Sandbox errors.

## encounter.combatant.UnitId
Unit id
Vanilla or loaded definition id (`BanditRaider`, …). Not a Character folder id. Unknown ids fail Sandbox spawn for that row.

## encounter.combatant.Side
Side
`GoodSide` (player) or `BadSide` (AI). Wipe: living GoodSide 0 is a loss; else living BadSide 0 is a win. `OwnSide` is not v1. Sandbox refuses zero GoodSide.

## encounter.combatant.Level
Level
1-based hero rank. Walks `nextLevelArchetype` and grants progression skills. Character / hero ranks only; unit rows ignore this in the inspector.

## encounter.combatant.Col
Col
OffsetCoord column on the **live** board (authored + LevelManager pad 8×4, plus Setup grow). Default hosts are 24×24; +W / +H can raise that to 64. Empty with row empty = Sandbox center-offset. Partial col/row warns; Sandbox rejects it. Duplicate hex: inspector warns, Sandbox rejects. `GetHexItem` clamps off-board to the edge — Sandbox rejects instead.

## encounter.combatant.Row
Row
OffsetCoord row on the live board. Same rules as Col. Do not store vanilla world `spawnPos`.

## encounter.combatant.Flipped
Flipped
Facing on the live unit (`isFlipped`). Setup preview updates immediately. Sandbox re-applies after StartFight. Default false. BadSide often wants on so they face the party.

## encounter.combatant.Pocket
Pocket (blank = wakes alone)
BadSide only, shown while Exploration is on. Combatants sharing a non-empty pocket tag wake together the instant any one of them aggroes. Blank means this unit is its own solo pocket, keyed by its combatant id. Ignored on GoodSide rows.

## encounter.combatant.AggroRadius
Aggro radius (blank = file default)
BadSide only, shown while Exploration is on. Hex distance at which this unit's pocket wakes when a GoodSide unit gets close. Blank falls back to the encounter's Default aggro radius. A pocket wakes on whichever member's own radius triggers first. Ignored when a Trigger is set.

## encounter.combatant.TriggerId
Trigger (wins over radius when set)
BadSide only, shown while Exploration is on. Individual opt-in: when set, this unit's own pocket wakes when a GoodSide unit steps onto the trigger's painted region, instead of using its radius — independent of the trigger's own Pocket field, so a unit can use a trigger even if that trigger targets a different pocket. "None" uses the radius fields above.

## encounter.combatant.Clear
Clear placement
Clears col and row and despawns the Setup preview unit. Sandbox then uses the center-offset heuristic (GoodSide dir 3, BadSide dir 0, distance 2+slot).

## encounter.combatant.Remove
Remove Combatant
Drops this row from `encounter.json` and despawns the Setup preview unit. Does not delete the Character project or unit definition.

## encounter.setup.Restart
Restart board
Stops and reloads the Setup preview. The hole shows Loading board until the preview is ready. Entering Setup already starts the board after two frames so the tab chrome paints first. Leaving Setup unloads it. Does not call `ReopenAfterFight`. Turns, AI, and fight HUD stay off. Drag a catalogue card onto a hex to add and place. Uses in-memory payload (unsaved edits included).

## encounter.setup.Grid
Grid
Shows or hides the walk overlay. Empty blocked cells stay blank; a unit on a hex still shows the overlay. The mouse hex marker stays so you can still paint and place.

## encounter.setup.EditUnit
Edit Unit
Select a combatant or a prop, then tap a hex. The selected hex uses the blue current-turn overlay. Combatants need a walkable cell and can grow the board. Snap props sit on a hex center. Free-move props follow the cursor in the hole. Props do not block walk — use Draw Hex (right-drag) if the hex should be impassable.

## encounter.props.Add
Add Prop
Opens the Props catalogue. Select a deco for preview, then Add or double-click. Place does not block walk. Empty list is legal.

## encounter.prop.Prefab
Prefab
Lowercase `scenario` bundle name. Loaded the same way cinematics load generic prefabs.

## encounter.prop.Snap
Snap to grid
On: tap a hex; the deco sits on that center. Off: tap or drag in the hole; the deco sits under the cursor. Missing key in older files is on.

## encounter.prop.Col
Prop col
Live OffsetCoord column when snap is on. Empty means the prop is listed but not on the board.

## encounter.prop.Row
Prop row
Live OffsetCoord row when snap is on. Empty means the prop is listed but not on the board.

## encounter.prop.X
Prop X
World X when snap is off. Tap or drag in the hole, or type a number.

## encounter.prop.Y
Prop Y
World Y when snap is off. Tap or drag in the hole, or type a number.

## encounter.prop.Flipped
Prop flipped
Mirrors the instantiated prefab on X.

## encounter.prop.Clear
Clear prop placement
Drops hex and world placement. The row stays in the Node Tree.

## encounter.prop.Remove
Remove Prop
Deletes this instance from the encounter. Does not delete the scenario prefab.

## encounter.setup.DrawHex
Draw Hex
Left-drag paints walkable hexes. Right-drag blocks. Click or drag past the current grid (left) — the hover ghost shows the target hex, and the board grows to that cell in one stroke (cap 64). Occupied hexes refuse paint. Size is derived from walkable hexes; it is not stored.

## encounter.setup.PaintTerrain
Paint Terrain
Select a terrain in the Node Tree, then left-drag on the live board. Right-drag restores the template (or blank floor). Does not grow the board — Draw Hex first.

## encounter.setup.Trigger
Trigger
Select or add a trigger in the Node Tree, then left-drag on the live board to paint the region. Right-drag erases. Shown as a red overlay while this trigger is selected. Does not grow the board — Draw Hex first.

## encounter.setup.Camera
Camera
Drag a world-space rectangle for Sandbox pan bounds. Drag a corner or edge to resize, inside to move. Setup stays unclamped so you can frame the shot. Missing `camera` keeps Sandbox unclamped.

## encounter.camera.View
Use current view
Copies the current Setup hole frustum into Sandbox camera bounds and turns lock zoom on. Show the board first. Clear drops the object so Sandbox stays unclamped.

## encounter.camera.LockZoom
Lock zoom
When on, Sandbox ignores the mouse wheel and holds the authored ortho (clamped so the view still fits the rect).

## encounter.camera.Ortho
Ortho
Optional Sandbox orthographic size. Empty fits the rect to the Sandbox hole aspect. A value larger than the rect is clamped down.

## encounter.setup.Terrain
Terrain
Pick a terrain in the Node Tree Terrains folder. Host rooms scan on open. Import from another stage to stamp that room's hex art.

## sandbox.Level
Sandbox Level
Always Level 1, 2, or 3. Sets the first GoodSide hero (or spawn-fill character) rank for this run. Does not change the combatant's saved Level.

## sandbox.Start
Start sandbox
Plays this encounter in the hole (unsaved edits included). Needs a GoodSide combatant, or a placed Hero Spawn Point plus a Character pick. Same Sandbox as Character and Ability. The Level dropdown sets that hero's rank for this run.

## sandbox.Stop
Stop sandbox
Unloads the fight hole. Leaving the workspace also stops.

## encounter.vanilla.Import
Import Vanilla Encounter
Read-only import spike. Reconstructs a shipped combat room's combatants and impassable hexes into a brand-new Lab Encounter project -- vanilla is never touched. Lua, cinematics, and quest-gated variants are not imported; see the log for the full loss-list report.
