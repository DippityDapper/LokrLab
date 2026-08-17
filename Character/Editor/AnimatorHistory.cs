using System.Collections.Generic;
using LokrLab;
using LokrLab.Shell;

namespace LokrLab.Editor
{
	/// <summary>One entry in AnimatorHistory's undo/redo stacks: a snapshot plus a human-readable description of the action this snapshot is the undo point for.</summary>
	/// <remarks>Wrapping RigSnapshot in this rather than tracking descriptions in a parallel list keeps the description physically attached to its snapshot through every push/pop, so the two can never drift out of sync.</remarks>
	internal sealed class HistoryEntry
	{
		/// <summary>The captured rig state this entry restores.</summary>
		internal RigSnapshot Snapshot;
		/// <summary>Human-readable label for the action this entry is the undo point for.</summary>
		internal string Description;
	}

	/// <summary>Snapshot-based undo/redo for the rig editor.</summary>
	/// <remarks>
	/// Chosen over a command pattern (paired do/undo operations per action) because RigEditorScene is a static
	/// class with direct field/dictionary mutation throughout and no existing indirection layer a command object
	/// could hook into. Correct by construction: a restored snapshot is exactly a prior real state, not a
	/// hand-maintained inverse operation that can drift from what its forward operation actually did.
	///
	/// Linear, not branching: making a new edit after an Undo (or after JumpTo-ing into the past) still discards
	/// everything in redoStack. The Edit History panel (EditHistoryPanel.cs) is a full visible list you can click
	/// into directly, not a tree of preserved alternate futures.
	/// </remarks>
	internal static class AnimatorHistory
	{
		private const int MaxDepth = 50;

		private static readonly List<HistoryEntry> undoStack = new List<HistoryEntry>();
		private static readonly List<HistoryEntry> redoStack = new List<HistoryEntry>();
		private static bool restoring;

		/// <summary>Called at the start of every user-initiated mutation, capturing state as it was immediately before that mutation, labeled with a description of what's about to happen.</summary>
		/// <remarks>Flushes any not-yet-committed live drag via CommitCurrentPoseToActiveContext first, so two drags in a row with no context switch in between still each get their own undo step, instead of collapsing into one (a drag's result only lands in restPoses/clips at the next commit point). Also pauses playback first, so it can't tick a frame change mid-edit.</remarks>
		internal static void CaptureBeforeChange(string description)
		{
			if (restoring)
			{
				return;
			}
			RigEditorScene.PausePlayback();
			RigEditorScene.CommitCurrentPoseToActiveContext();
			redoStack.Clear();
			undoStack.Add(new HistoryEntry { Snapshot = RigEditorScene.CaptureSnapshotForHistory(), Description = description });
			if (undoStack.Count > MaxDepth)
			{
				undoStack.RemoveAt(0);
			}

			LabSaveUx.MarkDirty();
			EditHistoryPanel.Refresh();
		}

		/// <summary>True when Undo has an entry to restore.</summary>
		internal static bool CanUndo => undoStack.Count > 0;

		/// <summary>True when Redo has an entry to restore.</summary>
		internal static bool CanRedo => redoStack.Count > 0;

		/// <summary>Called on Load/Import -- a freshly loaded rig has nothing in common with whatever was undoable before it, so carrying old history forward would let Undo silently jump back into an entirely different rig.</summary>
		internal static void Clear()
		{
			undoStack.Clear();
			redoStack.Clear();
			EditHistoryPanel.Refresh();
		}

		/// <summary>Restores the most recent undo entry, pushing the current state onto redoStack.</summary>
		/// <remarks>The pushed redo entry describes the same action as the undo entry it came from -- redoing reapplies it, it isn't a new/different action.</remarks>
		internal static void Undo()
		{
			if (undoStack.Count == 0)
			{
				return;
			}
			RigEditorScene.CommitCurrentPoseToActiveContext();
			RigSnapshot currentState = RigEditorScene.CaptureSnapshotForHistory();
			HistoryEntry popped = undoStack[undoStack.Count - 1];
			undoStack.RemoveAt(undoStack.Count - 1);
			redoStack.Add(new HistoryEntry { Snapshot = currentState, Description = popped.Description });
			Restore(popped.Snapshot);
			EditHistoryPanel.Refresh();
		}

		/// <summary>Restores the most recent redo entry, pushing the current state back onto undoStack.</summary>
		internal static void Redo()
		{
			if (redoStack.Count == 0)
			{
				return;
			}
			RigSnapshot currentState = RigEditorScene.CaptureSnapshotForHistory();
			HistoryEntry popped = redoStack[redoStack.Count - 1];
			redoStack.RemoveAt(redoStack.Count - 1);
			undoStack.Add(new HistoryEntry { Snapshot = currentState, Description = popped.Description });
			Restore(popped.Snapshot);
			EditHistoryPanel.Refresh();
		}

		/// <summary>Flattens undoStack (oldest to newest) + a synthetic "current" marker + redoStack (nearest-future to furthest-future) into one ordered list for EditHistoryPanel to render directly.</summary>
		/// <remarks>isCurrent is true for exactly the synthetic marker -- there's no stored snapshot for "right now," it's just wherever undoStack and redoStack currently meet.</remarks>
		internal static IReadOnlyList<(string description, bool isCurrent)> GetHistoryView()
		{
			List<(string description, bool isCurrent)> view = new List<(string, bool)>(undoStack.Count + redoStack.Count + 1);
			foreach (HistoryEntry entry in undoStack)
			{
				view.Add((entry.Description, false));
			}
			view.Add(("Current", true));
			for (int i = redoStack.Count - 1; i >= 0; i--)
			{
				view.Add((redoStack[i].Description, false));
			}
			return view;
		}

		/// <summary>Jumps directly to the state at flatIndex within GetHistoryView()'s ordering, by calling Undo()/Redo() the corresponding number of times.</summary>
		/// <remarks>Reuses the proven restore path entirely rather than restoring a snapshot directly, so JumpTo can never diverge from what a manual sequence of Undo/Redo clicks would have produced. MaxDepth (50) keeps the worst case trivially cheap.</remarks>
		internal static void JumpTo(int flatIndex)
		{
			int undoCount = undoStack.Count;
			if (flatIndex < undoCount)
			{
				int steps = undoCount - flatIndex;
				for (int i = 0; i < steps; i++)
				{
					Undo();
				}
			}
			else if (flatIndex > undoCount)
			{
				int steps = flatIndex - undoCount;
				for (int i = 0; i < steps; i++)
				{
					Redo();
				}
			}
		}

		/// <summary>Restores a snapshot, guarding against RigEditorScene.RestoreSnapshotForHistory's own refresh calls looping back into CaptureBeforeChange.</summary>
		private static void Restore(RigSnapshot snapshot)
		{
			restoring = true;
			RigEditorScene.RestoreSnapshotForHistory(snapshot);
			restoring = false;
		}
	}
}
