using System.IO;
using LokrLab.Editor.General;
using LokrLabApi;
using LokrLab;

namespace LokrLab.Projects
{
	/// <summary>Character project type's session: wraps the already-loaded static CharacterSession.</summary>
	public sealed class CharacterProjectSession : ProjectSession
	{
		/// <summary>Builds a session from whatever HomeWorkstationScene / CharacterSession currently has loaded.</summary>
		public static CharacterProjectSession FromLoaded()
		{
			string folder = CharacterSession.Folder;
			string id = string.IsNullOrEmpty(folder) ? null : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string name = CharacterSession.Profile != null && !string.IsNullOrEmpty(CharacterSession.Profile.Name)
				? CharacterSession.Profile.Name
				: id;
			return new CharacterProjectSession
			{
				ProjectTypeId = CharacterProjectType.Id,
				Id = id,
				FolderPath = folder,
				DisplayName = name ?? "(unnamed)",
				IsDirty = false
			};
		}
	}
}
