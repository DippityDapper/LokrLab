using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LokrAbilityLab.Editor
{
	/// <summary>Hover-info lookup: compiled fallbacks plus markdown sidecars that override them.</summary>
	/// <remarks>
	/// Copy is not compiled into field labels. Ship Sidecars/ability-hover.md,
	/// Sidecars/character-hover.md, and Sidecars/encounter-hover.md next to the DLL;
	/// Mods/LokrLab overlays of the same names win and can be edited without a rebuild.
	/// </remarks>
	internal static class AbilityHoverCopy
	{
		private static readonly Dictionary<string, HoverEntry> entries = new Dictionary<string, HoverEntry>(StringComparer.Ordinal);
		private static readonly List<OverlayWatch> overlays = new List<OverlayWatch>();

		/// <summary>One overlay markdown path plus the last write time that was applied.</summary>
		private struct OverlayWatch
		{
			/// <summary>Absolute path of the overlay file.</summary>
			internal string Path;

			/// <summary>UTC write time of the last successful apply.</summary>
			internal DateTime Stamp;
		}

		/// <summary>One hover-info row: short title plus body copy.</summary>
		internal struct HoverEntry
		{
			/// <summary>Line shown as the hover heading.</summary>
			internal string Title;

			/// <summary>One or two sentences of description.</summary>
			internal string Body;
		}

		/// <summary>Loads compiled defaults with no sidecar files (tests).</summary>
		internal static void Reload()
		{
			Reload((IList<string>)null, (IList<string>)null);
		}

		/// <summary>Loads compiled defaults, then every plugin sidecar, then every overlay (later files win per key).</summary>
		internal static void Reload(IList<string> pluginSidecarPaths, IList<string> overlayMarkdownPaths)
		{
			entries.Clear();
			LoadDefaults();
			if (pluginSidecarPaths != null)
			{
				for (int i = 0; i < pluginSidecarPaths.Count; i++)
				{
					ApplyFile(pluginSidecarPaths[i]);
				}
			}

			overlays.Clear();
			if (overlayMarkdownPaths != null)
			{
				for (int i = 0; i < overlayMarkdownPaths.Count; i++)
				{
					string path = overlayMarkdownPaths[i];
					if (!string.IsNullOrEmpty(path))
					{
						overlays.Add(new OverlayWatch { Path = path });
					}
				}
			}

			ApplyOverlayIfChanged(force: true);
		}

		/// <summary>Re-reads the overlay file when its write time changed.</summary>
		internal static void MaybeReloadOverlay()
		{
			ApplyOverlayIfChanged(force: false);
		}

		/// <summary>Parses markdown and overlays those keys onto the current table (for tests and Reload).</summary>
		internal static void ApplyMarkdown(string markdown)
		{
			if (string.IsNullOrEmpty(markdown))
			{
				return;
			}

			foreach (KeyValuePair<string, HoverEntry> pair in ParseMarkdown(markdown))
			{
				entries[pair.Key] = pair.Value;
			}
		}

		/// <summary>Looks up a hover key. Returns false when nothing is registered.</summary>
		internal static bool TryGet(string key, out HoverEntry entry)
		{
			if (!string.IsNullOrEmpty(key) && entries.TryGetValue(key, out entry))
			{
				return true;
			}

			entry = default(HoverEntry);
			return false;
		}

		/// <summary>Formats field copy plus an optional token / tag line for the current value.</summary>
		internal static string Format(string key, string currentValue, out string title)
		{
			MaybeReloadOverlay();
			HoverEntry field;
			if (TryGet(key, out field) && !string.IsNullOrEmpty(field.Title))
			{
				title = field.Title;
			}
			else
			{
				title = string.IsNullOrEmpty(key) ? string.Empty : key;
			}

			StringBuilder body = new StringBuilder();
			if (TryGet(key, out field) && !string.IsNullOrEmpty(field.Body))
			{
				body.Append(field.Body);
			}

			string tokenKey = TokenKey(currentValue);
			HoverEntry token;
			if (!string.IsNullOrEmpty(tokenKey) && TryGet(tokenKey, out token))
			{
				if (body.Length > 0)
				{
					body.Append("  ");
				}

				body.Append(token.Title);
				if (!string.IsNullOrEmpty(token.Body))
				{
					body.Append(" — ").Append(token.Body);
				}
			}

			if (body.Length == 0)
			{
				body.Append("No hover copy for this item. Add it to Sidecars/ability-hover.md, Sidecars/character-hover.md, or Sidecars/encounter-hover.md (or a Mods/LokrLab overlay of the same name).");
			}

			return body.ToString();
		}

		/// <summary>Splits markdown <c>## key</c> sections into title (first line) and body (rest).</summary>
		internal static Dictionary<string, HoverEntry> ParseMarkdown(string markdown)
		{
			Dictionary<string, HoverEntry> parsed = new Dictionary<string, HoverEntry>(StringComparer.Ordinal);
			if (string.IsNullOrEmpty(markdown))
			{
				return parsed;
			}

			string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
			string key = null;
			string title = null;
			StringBuilder body = new StringBuilder();
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.StartsWith("## ", StringComparison.Ordinal))
				{
					Flush(parsed, key, title, body);
					key = line.Substring(3).Trim();
					title = null;
					body.Length = 0;
					continue;
				}

				if (string.IsNullOrEmpty(key) || line.StartsWith("# ", StringComparison.Ordinal))
				{
					continue;
				}

				if (title == null)
				{
					if (string.IsNullOrWhiteSpace(line))
					{
						continue;
					}

					title = line.Trim();
					continue;
				}

				if (body.Length > 0)
				{
					body.Append(' ');
				}

				body.Append(line.Trim());
			}

			Flush(parsed, key, title, body);
			return parsed;
		}

		private static void Flush(Dictionary<string, HoverEntry> parsed, string key, string title, StringBuilder body)
		{
			if (string.IsNullOrEmpty(key))
			{
				return;
			}

			parsed[key] = new HoverEntry
			{
				Title = title ?? key,
				Body = body.ToString().Trim(),
			};
		}

		private static string TokenKey(string currentValue)
		{
			if (string.IsNullOrEmpty(currentValue))
			{
				return null;
			}

			string trimmed = currentValue.Trim();
			if (trimmed.Length < 2)
			{
				return null;
			}

			char first = trimmed[0];
			if (first == '%' || first == '#')
			{
				int end = trimmed.Length;
				for (int i = 1; i < trimmed.Length; i++)
				{
					char c = trimmed[i];
					if (!(char.IsLetterOrDigit(c) || c == '_'))
					{
						end = i;
						break;
					}
				}

				if (end > 1)
				{
					return "token." + trimmed.Substring(0, end);
				}
			}

			return null;
		}

		private static void ApplyFile(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return;
			}

			try
			{
				ApplyMarkdown(File.ReadAllText(path));
			}
			catch (IOException)
			{
			}
		}

		private static void ApplyOverlayIfChanged(bool force)
		{
			for (int i = 0; i < overlays.Count; i++)
			{
				OverlayWatch watch = overlays[i];
				if (string.IsNullOrEmpty(watch.Path) || !File.Exists(watch.Path))
				{
					continue;
				}

				DateTime stamp = File.GetLastWriteTimeUtc(watch.Path);
				if (!force && stamp == watch.Stamp)
				{
					continue;
				}

				watch.Stamp = stamp;
				overlays[i] = watch;
				ApplyFile(watch.Path);
			}
		}

		private static void LoadDefaults()
		{
			Add("card.Lua", "Lua", "MoonSharp chunk compiled at parse. Field Action is return function(ctx) … end. Rare in vanilla (five files); offered under Advanced.");
			Add("field.Lua.Action", "Lua Action", "The Lua body. Saved as one quoted KV string. Use single quotes inside; a double quote cannot round-trip.");
			Add("card.Hit", "Hit", "Applies a hit to Target. Nested InitActions / Actions / AlwaysActions run around the connect.");
			Add("field.Hit.Target", "Hit Target", "Unit that receives this hit. Common picks: %TARGET, %CASTER, %SOURCE, %HITTARGET.");
			Add("field.Hit.Tags", "Hit Tags", "Must be on HitAction.ValidateTags (#MELEE, #PROJECTILE, #TARGETED, …). #RANGED is not legal.");
			Add("field.TrackingProjectile.Target", "Projectile Target", "Unit the projectile tracks. Usually %TARGET.");
			Add("card.CallFunction", "Call Function", "Reflection into a shipped C# helper under Ironhide.Legends.Content.Abilities. Unknown Function names fail parse.");
			Add("envelope.CastRange", "Cast Range", "Hex range expression. Required on non-PASSIVE abilities. Empty strings drop the ability from the registry.");
			Add("envelope.AnimationId", "Animation Id", "Clip name on the caster rig. Needs AbilityAction / AbilityEnd frame events, or use NOANIMATION.");
			Add("envelope.CastFXId", "Cast FX", "FXMega name played on cast. Unknown names throw in combat unless a custom fx/ folder exists.");
			Add("token.%CASTER", "%CASTER", "The unit that cast this ability.");
			Add("token.%SOURCE", "%SOURCE", "Instigator / source unit for this action (often the caster).");
			Add("token.%TARGET", "%TARGET", "The ability's selected target unit.");
			Add("token.%HITTARGET", "%HITTARGET", "Unit that was hit (projectile or melee connect).");
			Add("token.%HITSOURCE", "%HITSOURCE", "Unit that dealt the hit.");
			Add("token.%ATTACKER", "%ATTACKER", "Attacker in an attack-event context.");
			Add("token.%ATTACKED", "%ATTACKED", "Defender in an attack-event context.");
			Add("token.%UNIT", "%UNIT", "Current iterator unit (ActOnTargets / similar).");
			Add("token.%unit", "%unit", "Same role as %UNIT; some vanilla files use the lowercase token.");
			Add("token.%newTarget", "%newTarget", "Retargeted unit from ActOnTargets / retarget helpers.");
			Add("token.%knockbackCenter", "%knockbackCenter", "Center unit or point for Knockback.");
			Add("token.#MELEE", "#MELEE", "Legal Hit tag: melee connect.");
			Add("token.#PROJECTILE", "#PROJECTILE", "Legal Hit tag: projectile connect. Use this instead of #RANGED.");
			Add("token.#TARGETED", "#TARGETED", "Legal Hit tag: a selected unit, not an environmental pulse.");
			Add("envelope.behavior.POINT_TARGET", "POINT_TARGET", "Hex / point pick. Parser forces Team Filter to TEAM_ALL.");
			Add("envelope.behavior.AOE", "AOE", "Requires AOE Range / Kind / CenterOnCaster / AffectsCaster or parse throws. Without this flag, AOE fields in KV are ignored.");
			Add("event.OnAttackStart", "OnAttackStart", "Parse-legal, but combat never fires this hat. Prefer OnAbilityStart or a modifier OnPreHit.");
			Add("character.roster.Tier", "Legend (roster)", "Which HeroRosterManager list. Distinct from the LEGEND state flag. Meaningless for Enemy/Summon.");
			Add("character.states.Flag", "State flag", "Present-and-true is written; absent = off. LEGEND here is combat/cinematic, not roster Tier.");
			Add("animator.frame.Title", "Rest Pose / Frame", "Rest Pose seeds new clips only. Later Rest Pose edits do not move Walk / Attack.");
			Add("animator.frame.RootMotionX", "Root X (px)", "Cumulative root-motion X for this frame. Empty clears the whole clip curve, not just this sample.");
			Add("encounter.create.Blank", "Blank floor", "New encounters strip the template Tilemap. Unblock hexes, then paint from the Node Tree. Old files keep the host floor.");
			Add("encounter.template", "Template", "templates prefab name loaded by the fight embed. Picker is empty-enough hosts only: fighttesterempty (open field) and combat_bridge (bridge, no enemy spawns).");
			Add("encounter.setup.Restart", "Restart board", "Stops and reloads the Setup preview. The hole shows Loading board until the preview is ready. Entering Setup already loads the board after two frames. Leaving Setup unloads it. Drag a catalogue card onto a hex to add and place.");
			Add("encounter.setup.Grid", "Grid", "Shows or hides the walk overlay. Empty blocked cells stay blank. The mouse hex marker stays.");
			Add("encounter.setup.EditUnit", "Edit Unit", "Select a combatant or a prop, then tap a hex. The selected hex uses the blue current-turn overlay. Combatants need a walkable cell and can grow the board. Snap props sit on a hex; free-move props follow the cursor. Props do not block walk.");
			Add("encounter.combatants.Add", "Add Combatant", "Opens the Combatants catalogue. Select a card for Stand preview, then Add or double-click. Empty list is legal to save. Sandbox refuses zero GoodSide.");
			Add("encounter.catalogue.Search", "Search", "Filters catalogue cards by display name and/or id. Case-insensitive substring.");
			Add("encounter.catalogue.Add", "Add selected", "Appends the highlighted catalogue card. Select a card first. Double-click a card does the same. Drag a card onto a Setup hex to add and place it. A ghost follows the cursor while the card is out of the list.");
			Add("encounter.catalogue.Preview", "Preview", "Characters and units play Stand (or the first clip). Props show the scenario prefab. One live preview, not one exo per card.");
			Add("encounter.props.Add", "Add Prop", "Opens the Props catalogue. Select a deco for preview, then Add or double-click. Place does not block walk.");
			Add("encounter.prop.Prefab", "Prefab", "Lowercase scenario bundle name. Loaded the same way cinematics load generic prefabs.");
			Add("encounter.prop.Snap", "Snap to grid", "On: tap a hex; the deco sits on that center. Off: tap or drag in the hole; the deco sits under the cursor. Missing key in older files is on.");
			Add("encounter.prop.Col", "Prop col", "Live OffsetCoord column when snap is on. Empty means the prop is listed but not on the board.");
			Add("encounter.prop.Row", "Prop row", "Live OffsetCoord row when snap is on. Empty means the prop is listed but not on the board.");
			Add("encounter.prop.X", "Prop X", "World X when snap is off. Tap or drag in the hole, or type a number.");
			Add("encounter.prop.Y", "Prop Y", "World Y when snap is off. Tap or drag in the hole, or type a number.");
			Add("encounter.prop.Flipped", "Prop flipped", "Mirrors the instantiated prefab on X.");
			Add("encounter.prop.Clear", "Clear prop placement", "Drops hex and world placement. The row stays in the Node Tree.");
			Add("encounter.prop.Remove", "Remove Prop", "Deletes this instance from the encounter. Does not delete the scenario prefab.");
			Add("encounter.setup.DrawHex", "Draw Hex", "Left-drag paints walkable hexes. Right-drag blocks. Click past the grid (left) to grow. Occupied hexes refuse paint.");
			Add("encounter.setup.PaintTerrain", "Paint Terrain", "Select a terrain in the Node Tree, then left-drag. Right-drag restores the template. Draw Hex first to grow.");
			Add("encounter.setup.Camera", "Camera", "Drag a world-space rectangle for Sandbox pan bounds. Drag a handle to resize, inside to move. Setup stays unclamped so you can draw the rect.");
			Add("encounter.camera.View", "Use current view", "Copies the Setup hole frustum into Sandbox camera bounds and locks zoom. Show the board first.");
			Add("encounter.camera.LockZoom", "Lock zoom", "When on, Sandbox ignores the mouse wheel and holds the authored ortho (clamped so the view still fits the rect).");
			Add("encounter.camera.Ortho", "Ortho", "Optional Sandbox orthographic size. Empty fits the rect to the Sandbox hole. Values larger than the rect are clamped down.");
			Add("encounter.setup.Terrain", "Terrain", "Pick a terrain in the Node Tree. Import from another stage to stamp that room's hex art.");
			Add("encounter.terrains.Import", "Import from stage", "Scan another templates prefab and add hex-art terrains. Same id from two stages is legal.");
			Add("encounter.terrains.Custom", "Add custom terrain", "Mints a named stub. Cannot paint until art exists. Import from a stage to stamp now.");
			Add("encounter.terrain.Name", "Terrain name", "Display label in the Node Tree. Does not change the vanilla terrainId.");
			Add("encounter.terrain.Source", "Terrain source", "template is the host room. import is another stage. custom is a stub with no floor art yet.");
			Add("encounter.terrain.Use", "Use for Paint", "Selects this terrain and switches Setup to Paint Terrain. Then left-drag on the board.");
			Add("encounter.terrain.Remove", "Remove Terrain", "Drops an import or custom row. Host template rows come back on the next scan.");
			Add("ability.vanilla.Browse", "Browse Vanilla Abilities", "Read-only reference over every shipped ability, parsed live from the game's own data. Does not open, copy, or edit anything.");
			Add("ability.vanilla.OverrideVsFork", "Override vs Fork", "Override would replace a shipped ability's id everywhere it's used -- one ability, global. Fork mints a new id so only wired Lab characters are affected; vanilla is untouched. Neither is available yet -- this browser is research only.");
			Add("ability.vanilla.CopyOverride", "Copy into Library (Override)", "Copies this ability into a Lab library folder, keeping its vanilla KV block key so last-wins replaces it everywhere at load. Not yet openable from an Edit Vanilla Ability button -- open it from the library like any other ability.");
			Add("ability.vanilla.CopyFork", "Copy into Library (Fork)", "Copies this ability into a Lab library folder under a newly minted id. Vanilla is untouched; only Lab characters you separately assign this new id to are affected.");
			Add("sandbox.Level", "Sandbox Level", "Always Level 1, 2, or 3. Sets the hero rank for this Sandbox run.");
			Add("sandbox.Start", "Start sandbox", "Plays the open encounter, or a 1v1 in Character / Ability. Same Sandbox hole everywhere.");
			Add("encounter.combatant.Side", "Side", "GoodSide (player) or BadSide (AI). Sandbox refuses zero GoodSide. OwnSide is not v1.");
		}

		private static void Add(string key, string title, string body)
		{
			entries[key] = new HoverEntry { Title = title, Body = body };
		}
	}
}
