using System.Collections.Generic;
using UnityEngine;
using LokrLab;

namespace LokrLab.Editor
{
	/// <summary>A drag-driven editing tool (Move/Rotate/Scale/...).</summary>
	/// <remarks>Move/Rotate/Scale (the original hardcoded EditMode cases) and Scale XY/Pivot are all registered instances of this now, rather than special-cased switch statements in EditorInputController -- a plugin can register a genuinely new tool the same way. One shared instance per tool is enough, since EditorInputController never runs two drags at once.</remarks>
	internal interface IAnimatorTool
	{
		/// <summary>The tool's display name and registry key (e.g. "Move", "Rotate").</summary>
		string Name { get; }

		/// <summary>The hotkey that switches EditorInputController to this tool.</summary>
		KeyCode Hotkey { get; }

		/// <summary>Whether this tool can still act on a part currently shown via DraggablePart.SetAffinePose (a raw shear/non-uniform-scale matrix with no rotation/scale pair to edit). True only for tools that don't touch the pose's own position/rotation/scale -- PivotTool is the only one, since it edits RestPose.PivotOffset instead.</summary>
		bool AllowsAffinePose { get; }

		/// <summary>Whether EditorInputController may call BeginGroupDrag/ContinueGroupDrag instead of BeginDrag/ContinueDrag when more than one part is multi-selected. True for every tool with a well-defined "act on the whole selection as one rigid formation" behavior; false only for PivotTool, which edits RestPose.PivotOffset directly and has no obviously-correct group behavior.</summary>
		bool SupportsGroupDrag { get; }

		/// <summary>Starts a drag on exactly one part (RigEditorScene.SelectedPart). Called whenever SupportsGroupDrag is false, or the multi-selection has 1 or fewer members.</summary>
		void BeginDrag(DraggablePart part, Vector3 mouseWorldPosition);

		/// <summary>Continues an already-started single-part drag as the mouse moves.</summary>
		void ContinueDrag(DraggablePart part, Vector3 mouseWorldPosition);

		/// <summary>Starts a drag on the whole multi-selection at once, treated as one rigid formation -- not the same as calling BeginDrag/ContinueDrag once per member. Only called when SupportsGroupDrag is true and the multi-selection has more than one member.</summary>
		void BeginGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition);

		/// <summary>Continues an already-started group drag as the mouse moves.</summary>
		void ContinueGroupDrag(IReadOnlyList<DraggablePart> group, Vector3 mouseWorldPosition);
	}

	/// <summary>Registry of IAnimatorTool implementations (Move/Rotate/Scale/Scale XY/Pivot) keyed by hotkey.</summary>
	internal static class AnimatorToolRegistry
	{
		private static readonly List<IAnimatorTool> tools = new List<IAnimatorTool>();

		/// <summary>Every currently registered tool, in registration order.</summary>
		internal static IReadOnlyList<IAnimatorTool> Tools => tools;

		/// <summary>Registers a tool, replacing any existing tool of the same name.</summary>
		internal static void RegisterTool(IAnimatorTool tool)
		{
			tools.RemoveAll(t => t.Name == tool.Name);
			tools.Add(tool);
		}

		/// <summary>Looks up a registered tool by name, or returns null if none matches.</summary>
		internal static IAnimatorTool Find(string name)
		{
			return tools.Find(t => t.Name == name);
		}

		/// <summary>Registers the first-party tool set (Move/Rotate/Scale/Scale XY/Pivot). Safe to call more than once per process, since RegisterTool replaces rather than duplicates.</summary>
		internal static void RegisterDefaults()
		{
			RegisterTool(new MoveTool());
			RegisterTool(new RotateTool());
			RegisterTool(new ScaleTool());
			RegisterTool(new ScaleXYTool());
			RegisterTool(new PivotTool());
		}
	}
}
