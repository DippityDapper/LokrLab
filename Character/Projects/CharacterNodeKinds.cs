namespace LokrLab.Projects
{
	/// <summary>LabNode.Kind strings the Character project type contributes.</summary>
	internal static class CharacterNodeKinds
	{
		/// <summary>The open character itself.</summary>
		internal const string Character = "Character";
		/// <summary>Rig folder / parts root.</summary>
		internal const string Rig = "Rig";
		/// <summary>One rig part (SceneTreePanel's old leaf).</summary>
		internal const string Part = "Part";
		/// <summary>Animator / clips root.</summary>
		internal const string Animator = "Animator";
		/// <summary>One animation clip name from rig.json.</summary>
		internal const string AnimationClip = "AnimationClip";
		/// <summary>One authored frame, or Rest Pose (InspectorPanel's Frame section).</summary>
		internal const string Frame = "Frame";
		/// <summary>A scale-reference overlay in the Animator viewport.</summary>
		internal const string Reference = "Reference";
		/// <summary>One Properties category (General, Portraits, Skills, …).</summary>
		internal const string PropertiesCategory = "PropertiesCategory";
		/// <summary>Folder of cross-project ability references from skillProgression / defaultSkill / skills.</summary>
		internal const string Abilities = "Abilities";
		/// <summary>One ability id referenced by this character. Jump opens the Ability Library.</summary>
		internal const string AbilityRef = "AbilityRef";
		/// <summary>This character folder's aliases.json.</summary>
		internal const string Aliases = "Aliases";
	}
}
