using System.Collections.Generic;

namespace LokrAbilityLab.Editor
{
	/// <summary>One KV action block: typed fields and nested stacks, or an opaque subtree.</summary>
	internal sealed class ActionCard
	{
		internal string TypeId = string.Empty;
		internal bool IsOpaque;
		internal bool Collapsed = true;
		internal string OpaqueText = string.Empty;
		internal readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
		internal readonly Dictionary<string, List<ActionCard>> ChildStacks = new Dictionary<string, List<ActionCard>>();
		internal string ExtraKv = string.Empty;

		/// <summary>Deep copy for Duplicate.</summary>
		internal ActionCard Clone()
		{
			ActionCard copy = new ActionCard
			{
				TypeId = TypeId,
				IsOpaque = IsOpaque,
				Collapsed = Collapsed,
				OpaqueText = OpaqueText,
				ExtraKv = ExtraKv,
			};
			foreach (KeyValuePair<string, string> pair in Fields)
			{
				copy.Fields[pair.Key] = pair.Value;
			}

			foreach (KeyValuePair<string, List<ActionCard>> pair in ChildStacks)
			{
				List<ActionCard> list = new List<ActionCard>(pair.Value.Count);
				foreach (ActionCard child in pair.Value)
				{
					list.Add(child.Clone());
				}

				copy.ChildStacks[pair.Key] = list;
			}

			return copy;
		}

		/// <summary>Returns the named child stack, creating it if missing.</summary>
		internal List<ActionCard> Stack(string name)
		{
			if (!ChildStacks.TryGetValue(name, out List<ActionCard> list))
			{
				list = new List<ActionCard>();
				ChildStacks[name] = list;
			}

			return list;
		}
	}

	/// <summary>An On* event hat and its action cards.</summary>
	internal sealed class EventNode
	{
		internal string Name = string.Empty;
		internal bool Collapsed = true;
		internal readonly List<ActionCard> Cards = new List<ActionCard>();

		internal EventNode Clone()
		{
			EventNode copy = new EventNode { Name = Name, Collapsed = Collapsed };
			foreach (ActionCard card in Cards)
			{
				copy.Cards.Add(card.Clone());
			}

			return copy;
		}
	}

	/// <summary>One AbilitySpecial variable row.</summary>
	internal sealed class SpecialVar
	{
		internal string Slot = string.Empty;
		internal string VarType = "FIELD_FLOAT";
		internal string Name = string.Empty;
		internal string Value = string.Empty;
	}

	/// <summary>One nested modifier definition.</summary>
	internal sealed class ModifierDef
	{
		internal string Id = string.Empty;
		internal bool Collapsed = true;
		internal bool Passive;
		internal string ModifierFXName = string.Empty;
		internal string IncompatibleStates = string.Empty;
		internal string AutoRemoveTags = string.Empty;
		internal string AutoRemoveModifierIds = string.Empty;
		internal string PropertiesAddKv = string.Empty;
		internal readonly List<EventNode> Events = new List<EventNode>();
		internal string ExtraKv = string.Empty;

		internal ModifierDef Clone()
		{
			ModifierDef copy = new ModifierDef
			{
				Id = Id,
				Collapsed = Collapsed,
				Passive = Passive,
				ModifierFXName = ModifierFXName,
				IncompatibleStates = IncompatibleStates,
				AutoRemoveTags = AutoRemoveTags,
				AutoRemoveModifierIds = AutoRemoveModifierIds,
				PropertiesAddKv = PropertiesAddKv,
				ExtraKv = ExtraKv,
			};
			foreach (EventNode ev in Events)
			{
				copy.Events.Add(ev.Clone());
			}

			return copy;
		}
	}

	/// <summary>A named AI block stored as raw inner KV (AIConfigB / AIBrain*).</summary>
	internal sealed class AiBlock
	{
		internal string Name = string.Empty;
		internal string InnerKv = string.Empty;
	}

	/// <summary>Structured ability body: events, modifiers, special, AI, opaque remainder.</summary>
	internal sealed class AbilityBody
	{
		internal readonly List<SpecialVar> Special = new List<SpecialVar>();
		internal readonly List<EventNode> Events = new List<EventNode>();
		internal readonly List<ModifierDef> Modifiers = new List<ModifierDef>();
		internal readonly List<AiBlock> Ai = new List<AiBlock>();
		internal readonly List<string> OpaqueTopLevel = new List<string>();

		/// <summary>Finds or creates an ability-level event hat.</summary>
		internal EventNode Event(string name)
		{
			foreach (EventNode ev in Events)
			{
				if (ev.Name == name)
				{
					return ev;
				}
			}

			EventNode created = new EventNode { Name = name };
			Events.Add(created);
			return created;
		}

		internal AbilityBody Clone()
		{
			AbilityBody copy = new AbilityBody();
			foreach (SpecialVar row in Special)
			{
				copy.Special.Add(new SpecialVar
				{
					Slot = row.Slot,
					VarType = row.VarType,
					Name = row.Name,
					Value = row.Value,
				});
			}

			foreach (EventNode ev in Events)
			{
				copy.Events.Add(ev.Clone());
			}

			foreach (ModifierDef mod in Modifiers)
			{
				copy.Modifiers.Add(mod.Clone());
			}

			foreach (AiBlock ai in Ai)
			{
				copy.Ai.Add(new AiBlock { Name = ai.Name, InnerKv = ai.InnerKv });
			}

			copy.OpaqueTopLevel.AddRange(OpaqueTopLevel);
			return copy;
		}
	}
}
