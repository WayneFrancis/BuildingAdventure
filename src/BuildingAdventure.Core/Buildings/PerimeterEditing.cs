using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Topology-safe editing operations on saved building geometry.
/// Splitting a wall inserts the same geometric corner into all coincident
/// room and exterior edges, including shared perimeter/room boundaries.
/// No other coordinates are moved by a split.
/// </summary>
public static class PerimeterEditing
{
    private const double VertexTolerance = 1e-7;
    private const double MinimumPieceLength = 0.05;

    public static bool TrySplitWall(BuildingDocument source,
        bool exterior, Guid? roomId, int edgeIndex, Point2 point,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        List<Point2>? sourceOutline = exterior
            ? result.Perimeter
            : result.Rooms.FirstOrDefault(r => r.Id == roomId)?.Outline;

        if (sourceOutline is null ||
            edgeIndex < 0 || edgeIndex >= sourceOutline.Count)
        {
            reason = "Select a valid wall to split.";
            return false;
        }

        Point2 start = sourceOutline[edgeIndex];
        Point2 end = sourceOutline[(edgeIndex + 1) % sourceOutline.Count];
        Point2 onWall = PolygonGeometry.ClosestPointOnSegment(point, start, end);

        if (point.DistanceTo(onWall) > 0.02)
        {
            reason = "The new corner must be placed on the existing wall.";
            return false;
        }
        if (onWall.DistanceTo(start) < MinimumPieceLength ||
            onWall.DistanceTo(end) < MinimumPieceLength)
        {
            reason = "Choose a point at least 5 cm away from each end of the wall.";
            return false;
        }

        // The perimeter and rooms are still distinct polygons, but each
        // gets a matching topological vertex where edges are coincident.
        // This prevents editing a shared wall from leaving the two faces
        // with different connectivity.
        InsertPoint(result.Perimeter, onWall);
        foreach (var room in result.Rooms)
            InsertPoint(room.Outline, onWall);

        if (!FloorGeometry.Validate(result, out reason))
            return false;

        return true;
    }

    /// <summary>
    /// Remove a polygon corner and join its two neighboring wall segments.
    /// Coincident corners in other polygons are removed only when both of
    /// their incident edges lie on the two original wall segments. This
    /// prevents blindly deleting unrelated interior junctions.
    ///
    /// Doors on the affected walls are repositioned along the replacement
    /// segment, and the entire candidate must pass floor validation.
    /// </summary>
    public static bool TryRemoveCorner(BuildingDocument source,
        bool exterior, Guid? roomId, int vertexIndex,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";

        List<Point2>? target = exterior
            ? result.Perimeter
            : result.Rooms.FirstOrDefault(r => r.Id == roomId)?.Outline;

        if (target is null || vertexIndex < 0 || vertexIndex >= target.Count)
        {
            reason = "Select a valid corner to remove.";
            return false;
        }
        if (target.Count <= 3)
        {
            reason = "A closed perimeter or room must have at least three corners.";
            return false;
        }

        Point2 previous = target[(vertexIndex + target.Count - 1) % target.Count];
        Point2 removed = target[vertexIndex];
        Point2 next = target[(vertexIndex + 1) % target.Count];

        // Capture which corners belong to the same connected old wall before
        // changing any geometry. Rooms may traverse shared edges in reverse.
        var affected = new List<(List<Point2> Polygon, int Index)> { (target, vertexIndex) };
        IEnumerable<List<Point2>> otherPolygons =
            new[] { result.Perimeter }.Concat(result.Rooms.Select(r => r.Outline));
        foreach (var polygon in otherPolygons)
        {
            if (ReferenceEquals(polygon, target)) continue;
            for (int i = 0; i < polygon.Count; i++)
            {
                if (polygon[i].DistanceTo(removed) > VertexTolerance) continue;

                Point2 before = polygon[(i + polygon.Count - 1) % polygon.Count];
                Point2 after = polygon[(i + 1) % polygon.Count];
                bool oneOnPrevious = OnOldWall(before, previous, removed) ||
                                     OnOldWall(after, previous, removed);
                bool oneOnNext = OnOldWall(before, removed, next) ||
                                 OnOldWall(after, removed, next);
                if (!oneOnPrevious || !oneOnNext) continue;

                if (polygon.Count <= 3)
                {
                    reason = "A connected room would have fewer than three corners.";
                    return false;
                }

                affected.Add((polygon, i));
            }
        }

        foreach (var (polygon, index) in affected)
            polygon.RemoveAt(index);

        result.Doors = DoorPlacement.ForRemovedCorner(
            source.Doors, previous, removed, next);
        result.Windows = WindowPlacement.ForRemovedCorner(
            source.Windows, previous, removed, next);

        if (!FloorGeometry.Validate(result, out reason))
            return false;

        return true;
    }

    private static bool OnOldWall(Point2 point, Point2 first, Point2 second) =>
        PolygonGeometry.ClosestPointOnSegment(point, first, second)
            .DistanceTo(point) <= VertexTolerance;

    private static void InsertPoint(List<Point2> polygon, Point2 point)
    {
        // Iterate backwards so insertions cannot invalidate earlier indices.
        for (int i = polygon.Count - 1; i >= 0; i--)
        {
            Point2 a = polygon[i];
            Point2 b = polygon[(i + 1) % polygon.Count];
            if (point.DistanceTo(a) <= VertexTolerance ||
                point.DistanceTo(b) <= VertexTolerance)
                continue;
            if (PolygonGeometry.ClosestPointOnSegment(point, a, b)
                    .DistanceTo(point) > VertexTolerance)
                continue;
            if (point.DistanceTo(a) < MinimumPieceLength ||
                point.DistanceTo(b) < MinimumPieceLength)
                continue;
            polygon.Insert(i + 1, point);
        }
    }

    private static BuildingDocument Copy(BuildingDocument source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Name = source.Name,
        Perimeter = source.Perimeter.ToList(),
        Rooms = source.Rooms.Select(r => r.Copy()).ToList(),
        Doors = source.Doors.Select(d => d.Copy()).ToList(),
        Windows = source.Windows.Select(w => w.Copy()).ToList()
    };
}
