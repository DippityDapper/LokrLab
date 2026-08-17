using LokrModMenu;

namespace LokrLab
{
	/// <summary>Registers Character Lab with the global mod menu popup.</summary>
	internal static class ModMenuRegistration
	{
		/// <summary>Registers Character Lab with the mod menu.</summary>
		internal static void Register()
		{
			ModMenuAPI.RegisterButton(
				"character-lab",
				"LokrLab",
				() => CharacterLabAccess.Open(),
				sortOrder: 0);

			ModMenuAPI.RegisterBlockingOverlay(
				() => CharacterLabAccess.IsOpen,
				() => CharacterLabAccess.Close());
		}
	}
}
