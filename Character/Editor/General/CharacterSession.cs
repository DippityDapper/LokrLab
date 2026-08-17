namespace LokrLab.Editor.General
{
	/// <summary>The single owner of "which character is currently active" state -- folder, profile, and which archetype rank Properties is showing.</summary>
	/// <remarks>
	/// Extracted from HomeWorkstationScene (pre-redesign audit P2 "SRP / god classes" target
	/// split -- "CharacterSession: profile, folder, dirty flags, replaces scattered statics"),
	/// so this state has a home that isn't itself a workstation's own UI-orchestration class.
	/// HomeWorkstationScene is really "build the Home screen and own the ~40 CharacterProfile
	/// field mutators plus PersistAndSync" (the separate, larger CharacterProfileService target
	/// from the same audit split, not attempted here) -- not "be the place every other file
	/// reaches into for session state." Other code (SandboxWorkstationScene,
	/// CharacterLabScene's Animator onShow hookup and its C-04 RequiresCharacterLoaded guard, a
	/// future node tree) can read "is a character loaded, and which one" from here directly
	/// without depending on HomeWorkstationScene at all.
	///
	/// HomeWorkstationScene's own CurrentCharacterFolder/CurrentProfile/CurrentEditingLevel
	/// properties still exist, now as thin forwards onto this class's Folder/Profile/EditingLevel
	/// -- deliberately, so every existing internal assignment inside HomeWorkstationScene and
	/// every other file's read of those properties keeps compiling and behaving identically. This
	/// pass only relocates where the state actually lives, not who's allowed to read or write it.
	/// </remarks>
	internal static class CharacterSession
	{
		/// <summary>The folder of the currently active character, or null if none is loaded.</summary>
		internal static string Folder { get; private set; }

		/// <summary>The currently active character's profile, or null if none is loaded.</summary>
		internal static CharacterProfile Profile { get; private set; }

		/// <summary>Which archetype rank Properties is currently showing/editing. Reset to 1 whenever a different character loads.</summary>
		internal static int EditingLevel { get; private set; } = 1;

		/// <summary>Sets the active character's folder. By convention, called only from HomeWorkstationScene's own CurrentCharacterFolder setter -- not enforced by the language (this is `internal`, assembly-wide, not namespace-scoped), just the same "one sanctioned writer" discipline every other session-mutating method in this plugin already follows.</summary>
		internal static void SetFolder(string folder)
		{
			Folder = folder;
		}

		/// <summary>Sets the active character's profile. By convention, called only from HomeWorkstationScene's own CurrentProfile setter -- see SetFolder's own remarks.</summary>
		internal static void SetProfile(CharacterProfile profile)
		{
			Profile = profile;
		}

		/// <summary>Sets which archetype rank is being edited. By convention, called only from HomeWorkstationScene's own CurrentEditingLevel setter -- see SetFolder's own remarks.</summary>
		internal static void SetEditingLevel(int level)
		{
			EditingLevel = level;
		}

		/// <summary>Resets to "no character loaded". Call when the Character project closes so no stale folder/profile survives past the close.</summary>
		/// <remarks>
		/// Before this existed, <c>CharacterLabScene.CloseProject</c> cleared the generic
		/// <c>LokrLabApi.CurrentSession</c> but never this Character-specific state (the shell is
		/// project-type-agnostic and has no reason to know about it). Deleting the open project's
		/// folder right after Close left <see cref="Folder"/> pointing at a directory that no longer
		/// existed; closing the Lab then tried to persist a profile into it and crashed
		/// (docs/issues/unresolved/character-close-lab-crash-after-deleting-open-project.md).
		/// </remarks>
		internal static void Clear()
		{
			Folder = null;
			Profile = null;
			EditingLevel = 1;
		}
	}
}
