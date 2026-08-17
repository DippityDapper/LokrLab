using LokrModMenu;

namespace LokrAbilityLab
{
	/// <summary>Registers Ability Lab's overlay with the global mod menu so the hotkey closes it instead of stacking.</summary>
	internal static class ModMenuRegistration
	{
		/// <summary>Registers the overlay as a blocking overlay. There is no standalone Ability Lab button — open the library from LokrLab.</summary>
		internal static void Register()
		{
			ModMenuAPI.RegisterBlockingOverlay(
				() => AbilityLabAccess.IsOpen,
				() => AbilityLabAccess.Close());
		}
	}
}
