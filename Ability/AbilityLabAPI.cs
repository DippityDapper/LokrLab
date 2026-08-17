using System;
using System.Collections.Generic;
using LokrAbilityLab.Editor;

namespace LokrAbilityLab
{
	/// <summary>How a combobox field should be populated.</summary>
	public enum ActionCardCatalogKind
	{
		/// <summary>Plain text field.</summary>
		None = 0,
		/// <summary>FXMega names (Cast / hit / modifier FX).</summary>
		FxMega = 1,
		/// <summary>Projectile Model prefab names.</summary>
		Projectile = 2,
		/// <summary>PlaySound / StopSound names.</summary>
		Sound = 3,
		/// <summary>CallFunction type names.</summary>
		CallFunction = 4,
		/// <summary>SpawnUnit unit ids.</summary>
		Unit = 5,
		/// <summary>Animation clip names.</summary>
		Animation = 6,
		/// <summary>Expression snippets, context tokens, and parser function templates (stat, unitPosition, …).</summary>
		Expression = 7,
		/// <summary>Unit/target context values (%TARGET, unitPosition(%CASTER), …).</summary>
		UnitRef = 8,
		/// <summary>Stat refs (#baseDamage, #attackDamage, …).</summary>
		Stat = 9,
		/// <summary>AddDamage Type enum names.</summary>
		DamageType = 10,
	}

	/// <summary>Describes one action-card type the Ability Lab editor can show as a stacked card.</summary>
	public sealed class ActionCardDescriptor
	{
		/// <summary>KV block key, e.g. Hit or ApplyModifier.</summary>
		public string TypeId;

		/// <summary>Label on the card header.</summary>
		public string DisplayLabel;

		/// <summary>Scalar KV keys shown as fields, in display order.</summary>
		public string[] FieldKeys = Array.Empty<string>();

		/// <summary>Named child action-list keys (Actions, InitActions, …).</summary>
		public string[] ChildStackNames = Array.Empty<string>();

		/// <summary>Field key → catalog used by that field's combobox.</summary>
		public Dictionary<string, ActionCardCatalogKind> FieldCatalogs = new Dictionary<string, ActionCardCatalogKind>();

		/// <summary>True when the type is offered under Advanced rather than the default Add-action list.</summary>
		public bool Advanced;

		/// <summary>Higher priority wins when two plugins register the same TypeId.</summary>
		public int Priority;
	}

	/// <summary>Public Ability Lab extension surface. Built-in cards register the same way.</summary>
	public static class AbilityLabAPI
	{
		/// <summary>Registers or replaces an action-card type. Highest priority for a TypeId wins.</summary>
		public static void RegisterActionCard(string typeId, ActionCardDescriptor descriptor, int priority = 0)
		{
			if (descriptor == null)
			{
				throw new ArgumentNullException(nameof(descriptor));
			}

			if (string.IsNullOrEmpty(typeId))
			{
				throw new ArgumentException("typeId is required.", nameof(typeId));
			}

			descriptor.TypeId = typeId;
			descriptor.Priority = priority;
			if (string.IsNullOrEmpty(descriptor.DisplayLabel))
			{
				descriptor.DisplayLabel = typeId;
			}

			AbilityCardRegistry.Register(descriptor);
		}

		/// <summary>Every registered card type, highest priority first per TypeId.</summary>
		public static IReadOnlyList<ActionCardDescriptor> RegisteredActionCards => AbilityCardRegistry.All;
	}
}
