using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Keeps door openings attached when a wall endpoint moves or rotates.
/// Door centres remain ordinary world coordinates in the JSON file; this
/// operation updates them together with the geometry in a single edit.
/// </summary>
public static class DoorPlacement
{
    private const double Tolerance = 1e-7;
    private const double DoorOnWallTolerance = 0.01;

    /// <summary>
    /// Reposition door centres after replacing all occurrences of an old
    /// vertex with a new point. A door on an incident wall retains its
    /// fractional position between the original wall endpoints, even when
    /// the wall changes angle. Doors on unrelated walls do not move.
    ///
    /// This method is intentionally pure: mouse-move previews recompute
    /// from the same starting snapshot, avoiding accumulated drift.
    /// The caller must validate the resulting floor before committing.
    /// </summary>
    public static List<DoorDefinition> ForMovedVertex(
        IReadOnlyList<Point2> originalPerimeter,
        IReadOnlyList<RoomDefinition> originalRooms,
        IReadOnlyList<DoorDefinition> originalDoors,
        Point2 previousVertex,
        Point2 newVertex)
    {
        var result = originalDoors.Select(door => door.Copy()).ToList();

        if (previousVertex.DistanceTo(newVertex) <= Tolerance)
            return result;

        // The same geometric edge can occur in an exterior outline and in
        // one or more rooms. Prefer the shortest original supporting edge:
        // this avoids using an unrelated long perimeter edge when the door
        // is actually attached to a shorter room partition.
        var edges = EnumerateEdges(originalPerimeter, originalRooms)
            .Where(edge => IsSame(edge.A, previousVertex) ||
                           IsSame(edge.B, previousVertex))
            .ToList();

        foreach (var door in result)
        {
            (Point2 A, Point2 B)? attached = null;
            double shortestLength = double.PositiveInfinity;

            foreach (var edge in edges)
            {
                double length = edge.A.DistanceTo(edge.B);
                if (length <= Tolerance || length >= shortestLength)
                    continue;

                var onEdge = PolygonGeometry.ClosestPointOnSegment(
                    door.Centre, edge.A, edge.B);
                if (door.Centre.DistanceTo(onEdge) > DoorOnWallTolerance)
                    continue;

                attached = edge;
                shortestLength = length;
            }

            if (attached is not { } wall)
                continue;

            double t = ProjectionParameter(door.Centre, wall.A, wall.B);
            Point2 nextA = IsSame(wall.A, previousVertex) ? newVertex : wall.A;
            Point2 nextB = IsSame(wall.B, previousVertex) ? newVertex : wall.B;

            door.Centre = new Point2(
                nextA.X + (nextB.X - nextA.X) * t,
                nextA.Y + (nextB.Y - nextA.Y) * t);
        }

        return result;
    }

    /// <summary>
    /// Join two consecutive wall segments into one straight segment.
    /// Doors on either old segment keep their fraction of the original
    /// combined wall length. Unrelated doors are left untouched.
    ///
    /// If the new wall would be too short or would cause invalid door
    /// clearance, the caller's final FloorGeometry.Validate rejects it.
    /// </summary>
    public static List<DoorDefinition> ForRemovedCorner(
        IReadOnlyList<DoorDefinition> originalDoors,
        Point2 previous, Point2 corner, Point2 next)
    {
        var result = originalDoors.Select(d => d.Copy()).ToList();
        double firstLength = previous.DistanceTo(corner);
        double secondLength = corner.DistanceTo(next);
        double combinedLength = firstLength + secondLength;
        if (combinedLength <= Tolerance) return result;

        foreach (var door in result)
        {
            Point2 nearestFirst = PolygonGeometry.ClosestPointOnSegment(
                door.Centre, previous, corner);
            Point2 nearestSecond = PolygonGeometry.ClosestPointOnSegment(
                door.Centre, corner, next);

            double distanceFirst = door.Centre.DistanceTo(nearestFirst);
            double distanceSecond = door.Centre.DistanceTo(nearestSecond);
            if (Math.Min(distanceFirst, distanceSecond) > DoorOnWallTolerance)
                continue;

            double alongWall;
            if (distanceFirst <= distanceSecond)
            {
                alongWall = ProjectionParameter(door.Centre, previous, corner) *
                            firstLength;
            }
            else
            {
                alongWall = firstLength +
                            ProjectionParameter(door.Centre, corner, next) *
                            secondLength;
            }

            double t = alongWall / combinedLength;
            door.Centre = new Point2(
                previous.X + (next.X - previous.X) * t,
                previous.Y + (next.Y - previous.Y) * t);
        }
        return result;
    }

    private static IEnumerable<(Point2 A, Point2 B)> EnumerateEdges(
        IReadOnlyList<Point2> perimeter, IReadOnlyList<RoomDefinition> rooms)
    {
        foreach (var edge in Edges(perimeter))
            yield return edge;

        foreach (var room in rooms)
            foreach (var edge in Edges(room.Outline))
                yield return edge;
    }

    private static IEnumerable<(Point2 A, Point2 B)> Edges(IReadOnlyList<Point2> outline)
    {
        for (int i = 0; i < outline.Count; i++)
            yield return (outline[i], outline[(i + 1) % outline.Count]);
    }

    private static double ProjectionParameter(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double squaredLength = dx * dx + dy * dy;
        return squaredLength <= Tolerance ? 0 :
            Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) /
                       squaredLength, 0, 1);
    }

    private static bool IsSame(Point2 a, Point2 b) =>
        a.DistanceTo(b) <= Tolerance;
}
