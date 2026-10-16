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

    private static DoorDefinition ToDoor(WindowDefinition w) =>
        new() { Id = w.Id, Centre = w.Centre, Width = w.Width };

    private static WindowDefinition ToWindow(DoorDefinition d) =>
        new() { Id = d.Id, Centre = d.Centre, Width = d.Width };
}
