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
