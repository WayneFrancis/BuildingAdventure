using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Keeps windows attached to exterior walls during geometric edits by using
/// the same proportional attachment rules as doors. Window IDs are preserved.
/// </summary>
public static class WindowPlacement
{
    public static List<WindowDefinition> ForMovedVertex(
        IReadOnlyList<Point2> perimeter,
        IReadOnlyList<RoomDefinition> rooms,
        IReadOnlyList<WindowDefinition> windows,
        Point2 before, Point2 after)
    {
        var moved = DoorPlacement.ForMovedVertex(perimeter, rooms,
            windows.Select(ToDoor).ToList(), before, after);
        return moved.Select(ToWindow).ToList();
    }

    public static List<WindowDefinition> ForRemovedCorner(
        IReadOnlyList<WindowDefinition> windows,
        Point2 previous, Point2 corner, Point2 next)
    {
        var moved = DoorPlacement.ForRemovedCorner(
            windows.Select(ToDoor).ToList(), previous, corner, next);
        return moved.Select(ToWindow).ToList();
    }

    public static bool TryAddWindow(BuildingDocument source,
        Point2 centre, double width, out BuildingDocument result, out string reason)
    {
        result = BuildingStoreys.Copy(source);
        result.Windows.Add(new WindowDefinition { Centre = centre, Width = width });
        return FloorGeometry.Validate(result, out reason);
    }

    /// <summary>
    /// Move one endpoint of a window along its supporting exterior wall
    /// while keeping the opposite endpoint fixed. The caller projects the
    /// dragged point onto that wall and validates the proposed result.
    /// </summary>
    public static WindowDefinition ResizeFromEndpoint(
        WindowDefinition source, Point2 fixedEndpoint, Point2 draggedEndpoint)
    {
        var result = source.Copy();
        result.Centre = new Point2(
            (fixedEndpoint.X + draggedEndpoint.X) / 2,
            (fixedEndpoint.Y + draggedEndpoint.Y) / 2);
        result.Width = fixedEndpoint.DistanceTo(draggedEndpoint);
        return result;
    }

    /// <summary>Return a validated detached copy for a window update.</summary>
    public static bool TryUpdateWindow(BuildingDocument source,
        WindowDefinition proposed, out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        int index = updated.Windows.FindIndex(w => w.Id == proposed.Id);
        if (index < 0)
        {
            reason = "The selected window was not found.";
            return false;
        }
        updated.Windows[index] = proposed.Copy();
        return FloorGeometry.Validate(updated, out reason);
    }

    private static DoorDefinition ToDoor(WindowDefinition w) =>
        new() { Id = w.Id, Centre = w.Centre, Width = w.Width };

    private static WindowDefinition ToWindow(DoorDefinition d) =>
        new() { Id = d.Id, Centre = d.Centre, Width = d.Width };
}
