using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Canonical visible wall fragment. Room boundaries contribute the same
/// fragment to one wall record; they never render double-thickness lines.
/// </summary>
public sealed record WallSegment(Point2 A, Point2 B, bool IsExterior)
{
    public double Length => A.DistanceTo(B);
}

public static class FloorGeometry
{
    private const double Tolerance = 1e-7;

    public static bool Validate(BuildingDocument document, out string reason)
    {
        reason = "";
        if (document.Perimeter.Count == 0)
        {
            if (document.Rooms.Count > 0 || document.Doors.Count > 0)
            {
                reason = "A building needs an exterior perimeter before rooms or doors can be added.";
                return false;
            }
            return true;
        }

        if (!PolygonGeometry.IsValidOutline(document.Perimeter, out reason))
            return false;
        if (document.Rooms.Count > 200)
        {
            reason = "A floor cannot contain more than 200 rooms.";
            return false;
        }

        var ids = new HashSet<Guid>();
        foreach (RoomDefinition room in document.Rooms)
        {
            if (room is null || room.Outline is null ||
                !ids.Add(room.Id) || room.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > 100 ||
                string.IsNullOrWhiteSpace(room.Type) || room.Type.Length > 80)
            {
                reason = "A room has invalid identifying information.";
                return false;
            }
            if (!PolygonGeometry.IsValidOutline(room.Outline, out string problem))
            {
                reason = $"Room '{room.Name}': {problem}";
                return false;
            }

            if (PolygonGeometry.Area(room.Outline) < 0.5)
            {
                reason = $"Room '{room.Name}' is smaller than 0.5 square metres.";
                return false;
            }

            if (!IsWithinPolygon(room.Outline, document.Perimeter))
            {
                reason = $"Room '{room.Name}' extends outside the building.";
                return false;
            }
        }

        for (int i = 0; i < document.Rooms.Count; i++)
        for (int j = i + 1; j < document.Rooms.Count; j++)
        {
            if (PolygonsOverlap(document.Rooms[i].Outline, document.Rooms[j].Outline))
            {
                reason = $"Rooms '{document.Rooms[i].Name}' and '{document.Rooms[j].Name}' overlap.";
                return false;
            }
        }

        var walls = BuildWalls(document.Perimeter, document.Rooms);
        if (document.Doors.Count > 500)
        {
            reason = "This floor has too many doors.";
            return false;
        }

        var doorIds = new HashSet<Guid>();
        foreach (DoorDefinition door in document.Doors)
        {
            if (door is null || door.Id == Guid.Empty || !doorIds.Add(door.Id) ||
                !double.IsFinite(door.Centre.X) || !double.IsFinite(door.Centre.Y) ||
                !double.IsFinite(door.Width) || door.Width < 0.4 || door.Width > 3)
            {
                reason = "A door has invalid values.";
                return false;
            }
            if (!TryFindDoorWall(door, walls, out _))
            {
                reason = "A door is not properly positioned on a sufficiently long wall.";
                return false;
            }
        }

        for (int i = 0; i < document.Doors.Count; i++)
        for (int j = i + 1; j < document.Doors.Count; j++)
        {
            var a = document.Doors[i];
            var b = document.Doors[j];
            if (a.Centre.DistanceTo(b.Centre) < (a.Width + b.Width) / 2 + 0.1)
            {
                reason = "Two door openings are too close together.";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Derive shared physical wall pieces by splitting all contributing edges at
    /// every collinear endpoint, then de-duplicating the subsegments.
    /// </summary>
    public static IReadOnlyList<WallSegment> BuildWalls(
        IReadOnlyList<Point2> exterior, IReadOnlyList<RoomDefinition> rooms)
    {
        var originalEdges = new List<(Point2 A, Point2 B, bool Exterior)>();
        AddEdges(exterior, true);
        foreach (var room in rooms)
            AddEdges(room.Outline, false);

        void AddEdges(IReadOnlyList<Point2> points, bool outside)
        {
            for (int i = 0; i < points.Count; i++)
                originalEdges.Add((points[i], points[(i + 1) % points.Count], outside));
        }

        var pieces = new Dictionary<string, WallSegment>();
        var endpoints = originalEdges.SelectMany(e => new[] { e.A, e.B }).ToList();
        foreach (var edge in originalEdges)
        {
            if (edge.A.DistanceTo(edge.B) < Tolerance) continue;
            var positions = new List<double> { 0, 1 };
            foreach (var point in endpoints)
            {
                if (OnSegment(point, edge.A, edge.B))
                    positions.Add(Parameter(point, edge.A, edge.B));
            }
            positions.Sort();
            for (int i = 1; i < positions.Count; i++)
            {
                double start = positions[i - 1];
                double end = positions[i];
                if (end - start < Tolerance) continue;
                Point2 a = Interpolate(edge.A, edge.B, start);
                Point2 b = Interpolate(edge.A, edge.B, end);
                var key = EdgeKey(a, b);
                if (pieces.TryGetValue(key, out var existing))
                    pieces[key] = existing with { IsExterior = existing.IsExterior || edge.Exterior };
                else
                    pieces[key] = new WallSegment(a, b, edge.Exterior);
            }
        }
        return pieces.Values.ToList();
    }

    public static bool TryFindDoorWall(DoorDefinition door, IReadOnlyList<WallSegment> walls,
        out WallSegment? wall)
    {
        wall = null;
        foreach (var candidate in walls)
        {
            double length = candidate.Length;
            if (length < door.Width + 0.2) continue;
            Point2 closest = PolygonGeometry.ClosestPointOnSegment(
                door.Centre, candidate.A, candidate.B);
            if (closest.DistanceTo(door.Centre) > 0.01) continue;
            if (door.Centre.DistanceTo(candidate.A) < door.Width / 2 + 0.1 ||
                door.Centre.DistanceTo(candidate.B) < door.Width / 2 + 0.1)
                continue;
            wall = candidate;
            return true;
        }
        return false;
    }

    private static bool IsWithinPolygon(IReadOnlyList<Point2> inner, IReadOnlyList<Point2> outer)
    {
        foreach (Point2 point in inner)
            if (!PolygonGeometry.ContainsPoint(outer, point)) return false;

        // A concave exterior may have two inside corners joined by an edge
        // that crosses an exterior indentation. Split every edge at all
        // intersections and check every open interval.
        foreach (var (a, b) in Edges(inner))
        {
            var parameters = SplitParameters(a, b, outer);
            for (int i = 1; i < parameters.Count; i++)
            {
                if (parameters[i] - parameters[i - 1] < Tolerance) continue;
                var point = Interpolate(a, b, (parameters[i] + parameters[i - 1]) / 2);
                if (!PolygonGeometry.ContainsPoint(outer, point))
                    return false;
            }
        }
        return true;
    }

    private static bool PolygonsOverlap(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b) =>
        HasInteriorEdgeInside(a, b) || HasInteriorEdgeInside(b, a);

    private static bool HasInteriorEdgeInside(
        IReadOnlyList<Point2> source, IReadOnlyList<Point2> other)
    {
        // Offset an edge midpoint slightly into its room. Shared walls between
        // adjacent rooms then test on opposite sides, while coincident rooms
        // and genuinely intersecting rooms are detected as overlaps.
        double inward = PolygonGeometry.SignedArea(source) > 0 ? 1 : -1;
        foreach (var (a, b) in Edges(source))
        {
            double length = a.DistanceTo(b);
            if (length <= Tolerance) continue;
            var split = SplitParameters(a, b, other);
            for (int i = 1; i < split.Count; i++)
            {
                if (split[i] - split[i - 1] < Tolerance) continue;
                Point2 middle = Interpolate(a, b, (split[i] + split[i - 1]) / 2);
                double offset = Math.Min(0.0001, length * (split[i] - split[i - 1]) * 0.1);
                var insideSource = new Point2(middle.X - inward * (b.Y - a.Y) / length * offset,
                                              middle.Y + inward * (b.X - a.X) / length * offset);
                if (PointStrictlyInside(other, insideSource))
                    return true;
            }
        }
        return false;
    }

    private static bool PointStrictlyInside(IReadOnlyList<Point2> polygon, Point2 point) =>
        PolygonGeometry.ContainsPoint(polygon, point) &&
        Edges(polygon).All(edge =>
            PolygonGeometry.ClosestPointOnSegment(point, edge.A, edge.B)
                .DistanceTo(point) > 1e-7);

    private static List<double> SplitParameters(
        Point2 a, Point2 b, IReadOnlyList<Point2> polygon)
    {
        var results = new List<double> { 0, 1 };
        foreach (var (c, d) in Edges(polygon))
        {
            if (OnSegment(c, a, b)) results.Add(Parameter(c, a, b));
            if (OnSegment(d, a, b)) results.Add(Parameter(d, a, b));
            double rx = b.X - a.X, ry = b.Y - a.Y;
            double sx = d.X - c.X, sy = d.Y - c.Y;
            double denominator = Cross(rx, ry, sx, sy);
            if (Math.Abs(denominator) < Tolerance) continue;
            double t = Cross(c.X - a.X, c.Y - a.Y, sx, sy) / denominator;
            double u = Cross(c.X - a.X, c.Y - a.Y, rx, ry) / denominator;
            if (t >= -Tolerance && t <= 1 + Tolerance &&
                u >= -Tolerance && u <= 1 + Tolerance)
                results.Add(Math.Clamp(t, 0, 1));
        }
        results.Sort();
        return results;
    }

    private static IEnumerable<(Point2 A, Point2 B)> Edges(IReadOnlyList<Point2> points)
    {
        for (int i = 0; i < points.Count; i++)
            yield return (points[i], points[(i + 1) % points.Count]);
    }

    private static double Cross(double ax, double ay, double bx, double by) =>
        ax * by - ay * bx;

    private static double Parameter(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        return lengthSquared < Tolerance ? 0 :
            Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
    }

    private static Point2 Interpolate(Point2 a, Point2 b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static bool OnSegment(Point2 p, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(p, a, b).DistanceTo(p) <= 1e-7;

    private static string EdgeKey(Point2 a, Point2 b)
    {
        static string PointKey(Point2 p) =>
            $"{Math.Round(p.X, 6):F6},{Math.Round(p.Y, 6):F6}";
        string first = PointKey(a);
        string last = PointKey(b);
        return string.CompareOrdinal(first, last) < 0
            ? first + "|" + last
            : last + "|" + first;
    }
}
