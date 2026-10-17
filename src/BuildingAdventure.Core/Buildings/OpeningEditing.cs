using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Common editing mathematics for straight door and window openings.
/// Reprojects snapped pointer positions onto the structural wall centre line,
/// including sloped walls where a point cannot lie on the XY grid and wall
/// at the same time.
/// </summary>
public static class OpeningEditing
{
    public static Point2 OnWall(Point2 pointer, WallSegment wall,
        bool snapToGrid, double gridMetres = 0.5)
    {
        if (!double.IsFinite(gridMetres) || gridMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridMetres));
        if (snapToGrid)
            pointer = new Point2(
                Math.Round(pointer.X / gridMetres, MidpointRounding.AwayFromZero) * gridMetres,
                Math.Round(pointer.Y / gridMetres, MidpointRounding.AwayFromZero) * gridMetres);
        return PolygonGeometry.ClosestPointOnSegment(pointer, wall.A, wall.B);
    }

    /// <summary>
    /// Centre a door on its current physical wall segment, rather than
    /// on the entire connected collinear run used for door validation.
    /// A room partition is a physical wall segment too.
    /// </summary>
    public static bool TryCentreDoor(BuildingDocument source, Guid doorId,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        var door = source.Doors.FirstOrDefault(d => d.Id == doorId);
        if (door is null)
        {
            reason = "Select a door before centring.";
            return false;
        }

        var candidates = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)
            .Where(w => OnSegment(door.Centre, w.A, w.B))
            .ToList();
        string lastValidationError = "";
        foreach (var wall in candidates
            .OrderBy(w => door.Centre.DistanceTo(Midpoint(w.A, w.B)))
            .ThenBy(w => w.Length))
        {
            if (wall.Length < door.Width + 0.2 - 1e-7)
            {
                lastValidationError = "The wall segment is too short for this door width.";
                continue;
            }

            var proposedDoor = door.Copy();
            proposedDoor.Centre = Midpoint(wall.A, wall.B);
            if (TryUpdateDoor(source, proposedDoor, out var proposed, out var error))
            {
                updated = proposed;
                reason = "Door centred on its wall segment.";
                return true;
            }
            lastValidationError = error;
        }

        reason = candidates.Count == 0
            ? "No supporting wall segment found."
            : $"Cannot centre door: {lastValidationError}";
        return false;
    }

    /// <summary>
    /// Centre a window on the actual room wall bordering the exterior. A
    /// room's shorter exterior edge takes priority over a longer perimeter
    /// edge, even if both occupy exactly the same geometric line. Only
    /// where no room is defined against the window do we use perimeter.
    /// </summary>
    public static bool TryCentreWindow(BuildingDocument source, Guid windowId,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        var window = source.Windows.FirstOrDefault(w => w.Id == windowId);
        if (window is null)
        {
            reason = "Select a window before centring.";
            return false;
        }

        var exteriorPieces = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)
            .Where(w => w.IsExterior).ToList();
        var roomEdges = new List<WallSegment>();
        foreach (var room in source.Rooms)
        for (int i = 0; i < room.Outline.Count; i++)
        {
            var a = room.Outline[i];
            var b = room.Outline[(i + 1) % room.Outline.Count];
            var midpoint = Midpoint(a, b);
            if (OnSegment(window.Centre, a, b) &&
                OnExterior(a, exteriorPieces) &&
                OnExterior(b, exteriorPieces) &&
                OnExterior(midpoint, exteriorPieces))
                roomEdges.Add(new WallSegment(a, b, true));
        }

        // This fallback is intentional for unassigned interior areas:
        // there is no room wall there to centre against.
        var candidates = roomEdges.Count > 0
            ? roomEdges
            : FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)
                .Where(w => w.IsExterior &&
                    OnSegment(window.Centre, w.A, w.B)).ToList();

        string lastValidationError = "";
        foreach (var wall in candidates
            .OrderBy(w => window.Centre.DistanceTo(Midpoint(w.A, w.B)))
            .ThenBy(w => w.Length))
        {
            if (wall.Length < window.Width + 0.2 - 1e-7)
            {
                lastValidationError = "The room wall segment is too short for this window width.";
                continue;
            }

            var candidate = window.Copy();
            candidate.Centre = Midpoint(wall.A, wall.B);
            var proposed = BuildingStoreys.Copy(source);
            proposed.Windows[proposed.Windows.FindIndex(w => w.Id == windowId)] = candidate;
            if (FloorGeometry.Validate(proposed, out var validationError))
            {
                updated = proposed;
                reason = roomEdges.Count > 0
                    ? "Window centred on its room wall segment."
                    : "Window centred on its exterior wall segment (no room wall defined).";
                return true;
            }
            lastValidationError = validationError;
        }

        reason = candidates.Count == 0
            ? "No supporting exterior wall segment found."
            : $"Cannot centre window: {lastValidationError}";
        return false;
    }

    private static bool OnExterior(Point2 point,
        IReadOnlyList<WallSegment> exteriorPieces) =>
        exteriorPieces.Any(w => OnSegment(point, w.A, w.B));

    private static bool OnSegment(Point2 point, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(point, a, b)
            .DistanceTo(point) <= 0.01;

    private static Point2 Midpoint(Point2 a, Point2 b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    public static DoorDefinition ResizeDoor(DoorDefinition source,
        Point2 fixedEnd, Point2 movingEnd)
    {
        var result = source.Copy();
        result.Centre = new Point2(
            (fixedEnd.X + movingEnd.X) / 2,
            (fixedEnd.Y + movingEnd.Y) / 2);
        result.Width = fixedEnd.DistanceTo(movingEnd);
        return result;
    }

    public static bool TryUpdateDoor(BuildingDocument source,
        DoorDefinition changed, out BuildingDocument result, out string reason)
    {
        result = BuildingStoreys.Copy(source);
        int index = result.Doors.FindIndex(d => d.Id == changed.Id);
        if (index < 0)
        {
            reason = "The selected door no longer exists.";
            return false;
        }
        result.Doors[index] = changed.Copy();
        return FloorGeometry.Validate(result, out reason);
    }
}
