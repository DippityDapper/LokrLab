using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LokrAbilityLab;
using LokrAbilityLab.Projects;
using LokrCharacterLab;
using LokrCharacterLoader;
using LokrLab.Encounter;
using LokrLab.Projects;
using LokrLabApi;
using LokrModAPI;
using LokrModMenu;
using SimpleUI;
using UnityEngine.SceneManagement;

namespace LokrLab
{
	/// <summary>Entry point for LokrLab, the first-party editor suite (host + Character + Ability + Encounter).</summary>
	/// <remarks>
	/// Hosts the lab scene, Project Browser, and dock chrome. Character, Ability, and Encounter
	/// project types register on LokrLabApi from this assembly. LokrLabApi stays the contracts plugin.
	/// </remarks>
	[BepInPlugin(Guid, Name, Version)]
	[BepInDependency(LokrLabApiPlugin.Guid)]
	[BepInDependency(SimpleUIPlugin.Guid)]
	[BepInDependency(LokrModAPIPlugin.Guid)]
	[BepInDependency(LokrModMenuPlugin.Guid)]
	[BepInDependency(LokrCharacterLoaderPlugin.Guid)]
	public class LokrLabPlugin : BaseUnityPlugin
	{
		/// <summary>This plugin's BepInEx GUID.</summary>
		public const string Guid = "com.lokrmodding.lab";
		/// <summary>This plugin's display name, shown in BepInEx logs/UI.</summary>
		public const string Name = "LoKR Lab";
		/// <summary>This plugin's version, reported to BepInEx.</summary>
		public const string Version = "0.12.112";

		/// <summary>This plugin's shared logger.</summary>
		internal static ManualLogSource Log;

		/// <summary>Comma-separated project type ids hidden in the Project Browser list.</summary>
		internal static ConfigEntry<string> HiddenProjectTypes;

		/// <summary>When true, closing the lab persists and reloads game content.</summary>
		internal static ConfigEntry<bool> AutoReloadOnLabClose;

		private Harmony harmony;

		/// <summary>Applies every Harmony patch in this assembly and registers suite project types.</summary>
		private void Awake()
		{
			Log = base.Logger;

			HiddenProjectTypes = Config.Bind(
				"ProjectBrowser",
				"HiddenProjectTypes",
				"",
				"Comma-separated project type ids hidden in the Project Browser list.");

			AutoReloadOnLabClose = Config.Bind(
				"LiveReload",
				"AutoReloadOnLabClose",
				true,
				"When true, closing LokrLab persists the current character and calls CharacterAPI.ReloadLabContent(). Skipped during active combat.");

			harmony = new Harmony(Guid);
			harmony.PatchAll();

			EmbeddedSceneHost.BindStatic();
			CharacterLabScene.BindLabApiNavigation();
			ModMenuRegistration.Register();
			LokrAbilityLab.ModMenuRegistration.Register();

			CharacterLabHooks.Register();
			LokrLabApi.LokrLabApi.LabOpened += CharacterLabHooks.OnLabOpened;
			LokrLabApi.LokrLabApi.LabClosing += CharacterLabHooks.OnLabClosing;
			LokrLabApi.LokrLabApi.ShellShown += CharacterLabHooks.OnShellShown;
			LokrLabApi.LokrLabApi.ScreenShown += CharacterLabHooks.OnScreenShown;

			CharacterProjectType.Register();
			CharacterProjectType.MigrateExistingCharacterFolders();
			EmbeddedFightHost.BindStatic();

			LokrAbilityLab.Editor.AbilityCardDescriptors.RegisterBuiltins();
			AbilityLibraryProjectType.Register();
			AbilityPlaceholders.InstallIntoMods();
			EncounterProjectType.Register();

			SceneManager.sceneLoaded += OnSceneLoaded;

			Log.LogInfo(string.Format(
				"{0} v{1} loaded — {2} method(s) patched. Suite (host + Character + Ability + Encounter) ready.",
				Name, Version, harmony.GetPatchedMethods().Count()));
		}

		/// <summary>Unsubscribes scene and lab-scene handlers.</summary>
		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			LokrLabApi.LokrLabApi.LabOpened -= CharacterLabHooks.OnLabOpened;
			LokrLabApi.LokrLabApi.LabClosing -= CharacterLabHooks.OnLabClosing;
			LokrLabApi.LokrLabApi.ShellShown -= CharacterLabHooks.OnShellShown;
			LokrLabApi.LokrLabApi.ScreenShown -= CharacterLabHooks.OnScreenShown;
		}

		/// <summary>Binds an additive embed, or force-closes the lab / overlay if a Single load destroyed it.</summary>
		/// <remarks>Additive loads are the embed path and must not tear down the lab.</remarks>
		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			EmbeddedSceneHost.OnSceneLoaded(scene, mode);
			if (mode == LoadSceneMode.Additive)
			{
				return;
			}

			CharacterLabScene.OnExternalSceneLoaded(scene.name);

			if (AbilityLabAccess.IsOpen)
			{
				AbilityLabAccess.ForceCloseForSceneChange();
			}
		}
	}
}
