namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A document-wide undo/redo journal. Each entry is a detached snapshot of
/// the entire building plus the floor the user was editing. Switching the
/// active floor is a view action, not an undoable document mutation.
/// </summary>
public sealed record BuildingHistoryState(BuildingDocument Building, int FloorIndex)
{
    public BuildingHistoryState Copy() =>
        new(BuildingStoreys.Copy(Building), FloorIndex);
}

public sealed class BuildingEditHistory
{
    private const int MaximumEntries = 200;
    private readonly List<BuildingHistoryState> _undo = new();
    private readonly List<BuildingHistoryState> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>
    /// Record the full state before a successful change. Call only once the
    /// operation is committed: drafts, camera movements, validation failures
    /// and floor-selection changes do not belong in the history.
    /// </summary>
    public void RecordBefore(BuildingDocument building, int floorIndex)
    {
        Add(_undo, new BuildingHistoryState(BuildingStoreys.Copy(building), floorIndex));
        _redo.Clear();
    }

    public bool TryUndo(BuildingDocument current, int currentFloor,
        out BuildingHistoryState restored)
    {
        if (_undo.Count == 0)
        {
            restored = new BuildingHistoryState(BuildingStoreys.Copy(current), currentFloor);
            return false;
        }

        Add(_redo, new BuildingHistoryState(BuildingStoreys.Copy(current), currentFloor));
        restored = TakeLast(_undo).Copy();
        return true;
    }

    public bool TryRedo(BuildingDocument current, int currentFloor,
        out BuildingHistoryState restored)
    {
        if (_redo.Count == 0)
        {
            restored = new BuildingHistoryState(BuildingStoreys.Copy(current), currentFloor);
            return false;
        }

        Add(_undo, new BuildingHistoryState(BuildingStoreys.Copy(current), currentFloor));
        restored = TakeLast(_redo).Copy();
        return true;
    }

    private static void Add(List<BuildingHistoryState> entries, BuildingHistoryState state)
    {
        if (entries.Count == MaximumEntries)
            entries.RemoveAt(0);
        entries.Add(state);
    }

    private static BuildingHistoryState TakeLast(List<BuildingHistoryState> entries)
    {
        int index = entries.Count - 1;
        BuildingHistoryState state = entries[index];
        entries.RemoveAt(index);
        return state;
    }
}
