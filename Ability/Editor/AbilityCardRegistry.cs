using System;
using System.Collections.Generic;

namespace LokrAbilityLab.Editor
{
	/// <summary>Stores RegisterActionCard entries. Highest priority per TypeId wins.</summary>
	internal static class AbilityCardRegistry
	{
		private static readonly Dictionary<string, ActionCardDescriptor> byId =
			new Dictionary<string, ActionCardDescriptor>(StringComparer.Ordinal);
		private static readonly List<ActionCardDescriptor> all = new List<ActionCardDescriptor>();

		/// <summary>Every winning descriptor, insertion order among distinct TypeIds.</summary>
		internal static IReadOnlyList<ActionCardDescriptor> All => all;

		/// <summary>Adds or replaces a descriptor when the new priority is greater or equal.</summary>
		internal static void Register(ActionCardDescriptor descriptor)
		{
			if (byId.TryGetValue(descriptor.TypeId, out ActionCardDescriptor existing)
				&& existing.Priority > descriptor.Priority)
			{
				return;
			}

			if (existing != null)
			{
				all.Remove(existing);
			}

			byId[descriptor.TypeId] = descriptor;
			all.Add(descriptor);
		}

		/// <summary>Winning descriptor for a KV block key, or null.</summary>
		internal static ActionCardDescriptor Find(string typeId)
		{
			if (string.IsNullOrEmpty(typeId))
			{
				return null;
			}

			byId.TryGetValue(typeId, out ActionCardDescriptor descriptor);
			return descriptor;
		}

		/// <summary>Types offered on the default Add-action menu (not Advanced).</summary>
		internal static List<ActionCardDescriptor> DefaultAddList()
		{
			List<ActionCardDescriptor> list = new List<ActionCardDescriptor>();
			foreach (ActionCardDescriptor descriptor in all)
			{
				if (!descriptor.Advanced)
				{
					list.Add(descriptor);
				}
			}

			return list;
		}

		/// <summary>Advanced types for the Advanced add menu.</summary>
		internal static List<ActionCardDescriptor> AdvancedAddList()
		{
			List<ActionCardDescriptor> list = new List<ActionCardDescriptor>();
			foreach (ActionCardDescriptor descriptor in all)
			{
				if (descriptor.Advanced)
				{
					list.Add(descriptor);
				}
			}

			return list;
		}
	}
}
