using BepInEx.Logging;

namespace LokrAbilityLab
{
	/// <summary>Facade for Ability Lab types that still read this type's Log and Guid.</summary>
	/// <remarks>
	/// Ability Lab is a module of LokrLab, not its own BepInEx plugin. Old GUID
	/// com.lokrmodding.abilitylab is retired; depend on <see cref="LokrLab.LokrLabPlugin.Guid"/>.
	/// </remarks>
	public static class LokrAbilityLabPlugin
	{
		/// <summary>Suite plugin GUID (com.lokrmodding.lab).</summary>
		public const string Guid = LokrLab.LokrLabPlugin.Guid;

		/// <summary>Display name for Ability Lab log lines.</summary>
		public const string Name = "LoKR Ability Lab";

		/// <summary>Suite version.</summary>
		public const string Version = LokrLab.LokrLabPlugin.Version;

		/// <summary>Shared suite logger.</summary>
		internal static ManualLogSource Log => LokrLab.LokrLabPlugin.Log;
	}
}
