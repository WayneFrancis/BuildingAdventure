using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Geometry-only interior editing. Operations produce a validated copy and
/// never mutate the building passed by the caller.
/// </summary>
public static class InteriorEditing
{
    private const double Epsilon = 1e-7;
    private const double MinimumRoomArea = 0.5;

    /// <summary>
    /// Cut an existing room by a straight partition joining two positions
    /// on its boundary. The original room keeps its ID; the second gets a
    /// new ID. Boundary points are inserted into any coincident room and
    /// perimeter wall edges, preserving shared corner coordinates.
    /// </summary>
    public static bool TrySplitRoom(
        BuildingDocument source,
        Guid roomId,
        Point2 from,
        Point2 to,
        out BuildingDocument result,
        out string reason)
    {
        result = Copy(source);
        reason = "";
        var sourceRoom = source.Rooms.FirstOrDefault(r => r.Id == roomId);
        if (sourceRoom is null)
        {
            reason = "Select a room to split.";
            return false;
        }

        if (!TryBoundaryPoint(sourceRoom.Outline, from, out var start) ||
            !TryBoundaryPoint(sourceRoom.Outline, to, out var end))
        {
            reason = "Both partition endpoints must lie on the selected room's walls.";
            return false;
        }
        if (start.DistanceTo(end) < 0.1)
        {
            reason = "Choose two different positions for the partition.";
            return false;
        }

        // Split any coincident wall edges at the same points, including the
        // perimeter. TrySplitWall already validates the intermediate result,
        // including door clearance at the newly inserted topology corners.
        foreach (var endpoint in new[] { start, end })
        {
            var workingRoom = result.Rooms.First(r => r.Id == roomId);
            if (workingRoom.Outline.Any(p => p.DistanceTo(endpoint) <= Epsilon))
                continue;

            int edge = FindEdge(workingRoom.Outline, endpoint);
            if (edge < 0 || !PerimeterEditing.TrySplitWall(result, false, roomId,
                    edge, endpoint, out var withCorner, out reason))
            {
                if (string.IsNullOrEmpty(reason))
                    reason = "Unable to insert a partition endpoint in the existing wall.";
                return false;
            }
            result = withCorner;
        }

        var original = result.Rooms.First(r => r.Id == roomId);
        int startIndex = original.Outline.FindIndex(p => p.DistanceTo(start) <= Epsilon);
        int endIndex = original.Outline.FindIndex(p => p.DistanceTo(end) <= Epsilon);
        if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
        {
            reason = "The partition endpoints must form two separate boundary positions.";
            return false;
        }

        var left = CollectPath(original.Outline, startIndex, endIndex);
        var right = CollectPath(original.Outline, endIndex, startIndex);

        if (!PolygonGeometry.IsValidOutline(left, out _) ||
            !PolygonGeometry.IsValidOutline(right, out _) ||
            PolygonGeometry.Area(left) < MinimumRoomArea ||
            PolygonGeometry.Area(right) < MinimumRoomArea)
        {
            reason = "The partition would create an invalid or excessively small room.";
            return false;
        }

        // A chord across a concave indentation can pass outside the room.
        // Check full polygon edges, not just their endpoints.
        if (!FloorGeometry.IsWithinPolygon(left, sourceRoom.Outline) ||
            !FloorGeometry.IsWithinPolygon(right, sourceRoom.Outline))
        {
            reason = "The partition crosses outside the existing room.";
            return false;
        }

        double originalArea = PolygonGeometry.Area(sourceRoom.Outline);
        double combinedArea = PolygonGeometry.Area(left) + PolygonGeometry.Area(right);
        if (Math.Abs(originalArea - combinedArea) >
            Math.Max(1e-5, originalArea * 1e-7))
        {
            reason = "The partition doesn't divide the original room cleanly.";
            return false;
        }

        var title = sourceRoom.Name.Length > 97
            ? sourceRoom.Name[..97]
            : sourceRoom.Name;
        original.Name = title + " A";
        original.Outline = left;
        result.Rooms.Add(new RoomDefinition
        {
            Name = title + " B",
            Type = original.Type,
            Outline = right
        });

        if (!FloorGeometry.Validate(result, out reason))
            return false;
        return true;
    }

    /// <summary>
    /// Move a complete wall fragment perpendicular to itself. Shared vertex
    /// coordinates are moved together in every polygon that contains them.
    /// Connected wall segments stretch or rotate as required; the candidate
    /// is accepted only if all rooms, exterior walls and doors remain valid.
    /// </summary>
    public static bool TryMoveWall(
        BuildingDocument source,
        WallSegment wall,
        double offsetMetres,
        out BuildingDocument result,
        out string reason)
    {
        result = Copy(source);
        reason = "";
        if (!double.IsFinite(offsetMetres))
        {
            reason = "The wall movement distance is invalid.";
            return false;
        }

        double length = wall.Length;
        if (length < 0.05)
        {
            reason = "The selected wall is too short to move.";
            return false;
        }
        if (Math.Abs(offsetMetres) < Epsilon)
            return true;

        var existingWalls = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms);
        if (!existingWalls.Any(w => SameEdge(w, wall)))
        {
            reason = "The selected wall no longer exists.";
            return false;
        }

        // Exterior walls can contain subsegments from several adjoining
        // rooms. Moving the whole collinear exterior run keeps every shared
        // room point attached, rather than moving one fragmented edge.
        if (wall.IsExterior)
            return SharedBoundaryEditing.TryMoveExteriorWall(
                source, wall, offsetMetres, out result, out reason);

        double offsetX = -(wall.B.Y - wall.A.Y) / length * offsetMetres;
        double offsetY = (wall.B.X - wall.A.X) / length * offsetMetres;
        var displaced = new Dictionary<Point2, Point2>();

        void CollectPoints(IReadOnlyList<Point2> polygon)
        {
            foreach (var point in polygon)
            {
                if (!OnSegment(point, wall.A, wall.B)) continue;
                displaced[point] = new Point2(point.X + offsetX, point.Y + offsetY);
            }
        }

        CollectPoints(source.Perimeter);
        foreach (var room in source.Rooms)
            CollectPoints(room.Outline);

        if (!displaced.ContainsKey(wall.A) || !displaced.ContainsKey(wall.B))
        {
            reason = "Split the wall at its endpoints before moving this section.";
            return false;
        }

        result.Perimeter = result.Perimeter.Select(p =>
            displaced.TryGetValue(p, out var moved) ? moved : p).ToList();
        foreach (var room in result.Rooms)
            room.Outline = room.Outline.Select(p =>
                displaced.TryGetValue(p, out var moved) ? moved : p).ToList();

        SharedBoundaryEditing.MoveOpenings(source, result);

        if (!FloorGeometry.Validate(result, out reason))
            return false;
        return true;
    }

    private static List<DoorDefinition> RepositionDoors(
        BuildingDocument source, WallSegment movedWall,
        IReadOnlyDictionary<Point2, Point2> displaced,
        double offsetX, double offsetY)
    {
        var originalEdges = EnumerateEdges(source).ToList();
        var result = source.Doors.Select(door => door.Copy()).ToList();

        foreach (var door in result)
        {
            // A door directly on the wall being translated moves with it.
            // For a door on an adjoining segment, retain its fractional
            // distance between that segment's old and new endpoints.
            if (OnSegment(door.Centre, movedWall.A, movedWall.B))
            {
                door.Centre = new Point2(
                    door.Centre.X + offsetX, door.Centre.Y + offsetY);
                continue;
            }

            (Point2 A, Point2 B)? attached = null;
            double shortest = double.PositiveInfinity;
            foreach (var edge in originalEdges)
            {
                if (!displaced.ContainsKey(edge.A) && !displaced.ContainsKey(edge.B))
                    continue;
                double distance = edge.A.DistanceTo(edge.B);
                if (distance <= Epsilon || distance >= shortest ||
                    !OnSegment(door.Centre, edge.A, edge.B))
                    continue;
                attached = edge;
                shortest = distance;
            }

            if (attached is not { } supporting) continue;
            double t = Parameter(door.Centre, supporting.A, supporting.B);
            var a = displaced.TryGetValue(supporting.A, out var nextA)
                ? nextA : supporting.A;
            var b = displaced.TryGetValue(supporting.B, out var nextB)
                ? nextB : supporting.B;
            door.Centre = new Point2(
                a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
        return result;
    }

    private static IEnumerable<(Point2 A, Point2 B)> EnumerateEdges(BuildingDocument doc)
    {
        foreach (var pair in Edges(doc.Perimeter))
            yield return pair;
        foreach (var room in doc.Rooms)
            foreach (var pair in Edges(room.Outline))
                yield return pair;
    }

    private static IEnumerable<(Point2 A, Point2 B)> Edges(IReadOnlyList<Point2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
            yield return (polygon[i], polygon[(i + 1) % polygon.Count]);
    }

    private static List<Point2> CollectPath(IReadOnlyList<Point2> vertices, int from, int to)
    {
        var path = new List<Point2> { vertices[from] };
        for (int i = (from + 1) % vertices.Count; ; i = (i + 1) % vertices.Count)
        {
            path.Add(vertices[i]);
            if (i == to) return path;
        }
    }

    private static bool TryBoundaryPoint(IReadOnlyList<Point2> outline,
        Point2 requested, out Point2 point)
    {
        point = default;
        double closest = 0.02;
        bool found = false;
        foreach (var (a, b) in Edges(outline))
        {
            Point2 candidate = PolygonGeometry.ClosestPointOnSegment(requested, a, b);
            double distance = candidate.DistanceTo(requested);
            if (distance > closest) continue;
            closest = distance;
            point = candidate;
            found = true;
        }
        return found;
    }

    private static int FindEdge(IReadOnlyList<Point2> outline, Point2 point)
    {
        for (int i = 0; i < outline.Count; i++)
            if (OnSegment(point, outline[i], outline[(i + 1) % outline.Count]))
                return i;
        return -1;
    }

    private static bool OnSegment(Point2 p, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(p, a, b).DistanceTo(p) <= Epsilon;

    private static double Parameter(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSq = dx * dx + dy * dy;
        return lengthSq <= Epsilon ? 0 :
            Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
    }

    private static bool SameEdge(WallSegment a, WallSegment b) =>
        (a.A.DistanceTo(b.A) <= Epsilon && a.B.DistanceTo(b.B) <= Epsilon) ||
        (a.A.DistanceTo(b.B) <= Epsilon && a.B.DistanceTo(b.A) <= Epsilon);

    private static BuildingDocument Copy(BuildingDocument source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Name = source.Name,
        Perimeter = source.Perimeter.ToList(),
        Rooms = source.Rooms.Select(r => r.Copy()).ToList(),
        Doors = source.Doors.Select(d => d.Copy()).ToList(),
        Windows = source.Windows.Select(w => w.Copy()).ToList(),
        WallThickness = source.WallThickness.Copy()
    };
}
