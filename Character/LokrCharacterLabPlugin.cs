using BepInEx.Configuration;
using BepInEx.Logging;

namespace LokrCharacterLab
{
	/// <summary>Facade for Character Lab types that still read this type's Log, Guid, and live-reload config.</summary>
	/// <remarks>
	/// Character Lab is a module of LokrLab, not its own BepInEx plugin. Old GUID
	/// com.lokrmodding.characterlab is retired; depend on <see cref="LokrLab.LokrLabPlugin.Guid"/>.
	/// </remarks>
	public static class LokrCharacterLabPlugin
	{
		/// <summary>Suite plugin GUID (com.lokrmodding.lab).</summary>
		public const string Guid = LokrLab.LokrLabPlugin.Guid;

		/// <summary>Display name for Character Lab log lines.</summary>
		public const string Name = "LoKR Character Lab";

		/// <summary>Suite version.</summary>
		public const string Version = LokrLab.LokrLabPlugin.Version;

		/// <summary>Shared suite logger.</summary>
		internal static ManualLogSource Log => LokrLab.LokrLabPlugin.Log;

		/// <summary>When true, closing the lab persists and reloads game content.</summary>
		internal static ConfigEntry<bool> AutoReloadOnLabClose => LokrLab.LokrLabPlugin.AutoReloadOnLabClose;
	}
}
