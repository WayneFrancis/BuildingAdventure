using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Keeps every room vertex and wall opening attached to an altered perimeter
/// boundary, including room vertices partway along a longer exterior edge.
/// Operations produce detached geometry and never modify the source.
/// </summary>
public static class SharedBoundaryEditing
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Move an exterior (or coincident room) vertex and interpolate all room
    /// points on its two incident perimeter edges. A room point at t=0.5
    /// follows the half-movement of the adjacent perimeter endpoint.
    /// </summary>
    public static BuildingDocument MoveVertex(
        BuildingDocument source, Point2 original, Point2 destination)
    {
        var result = BuildingStoreys.Copy(source);
        int index = source.Perimeter.FindIndex(p => Near(p, original));
        if (index < 0)
        {
            result.Perimeter = MoveMatching(source.Perimeter, original, destination);
            foreach (var room in result.Rooms)
                room.Outline = MoveMatching(room.Outline, original, destination);
        }
        else
        {
            int n = source.Perimeter.Count;
            Point2 previous = source.Perimeter[(index + n - 1) % n];
            Point2 next = source.Perimeter[(index + 1) % n];
            result.Perimeter[index] = destination;

            foreach (var room in result.Rooms)
            {
                room.Outline = room.Outline.Select(p =>
                {
                    if (Near(p, original)) return destination;
                    if (OnSegmentInterior(p, previous, original))
                    {
                        double t = Fraction(p, previous, original);
                        return Lerp(previous, destination, t);
                    }
                    if (OnSegmentInterior(p, original, next))
                    {
                        double t = Fraction(p, original, next);
                        return Lerp(destination, next, t);
                    }
                    return p;
                }).ToList();
            }
        }
        MoveOpenings(source, result);
        return result;
    }

    /// <summary>
    /// Move one continuous straight exterior wall run (even when split into
    /// several fragments by room endpoints) with all its coincident room
    /// points. Real bends stop the movement at the run ends.
    /// </summary>
    public static bool TryMoveExteriorWall(
        BuildingDocument source, WallSegment selected, double offset,
        out BuildingDocument result, out string reason)
    {
        result = BuildingStoreys.Copy(source);
        reason = "";
        if (!selected.IsExterior || !double.IsFinite(offset) || selected.Length < 0.05)
        {
            reason = "Select a valid exterior wall.";
            return false;
        }
        if (Math.Abs(offset) < 1e-7) return true;

        var perimeter = source.Perimeter;
        int n = perimeter.Count;
        var midpoint = Lerp(selected.A, selected.B, 0.5);
        int seed = -1;
        for (int i = 0; i < n; i++)
        {
            if (!OnSegment(midpoint, perimeter[i], perimeter[(i + 1) % n]))
                continue;
            seed = i;
            break;
        }
        if (seed < 0)
        {
            reason = "The exterior wall could not be located on the perimeter.";
            return false;
        }

        // Find the maximal connected collinear chain around the seed.
        var edges = new HashSet<int> { seed };
        int forward = seed;
        for (int step = 0; step < n - 1; step++)
        {
            int next = (forward + 1) % n;
            if (!Collinear(perimeter[next], perimeter[(next + 1) % n],
                selected.A, selected.B)) break;
            edges.Add(next);
            forward = next;
        }
        int backward = seed;
        for (int step = 0; step < n - 1; step++)
        {
            int prev = (backward + n - 1) % n;
            if (!Collinear(perimeter[prev], perimeter[(prev + 1) % n],
                selected.A, selected.B)) break;
            edges.Add(prev);
            backward = prev;
        }

        double nx = -(selected.B.Y - selected.A.Y) / selected.Length;
        double ny = (selected.B.X - selected.A.X) / selected.Length;
        double shiftX = nx * offset, shiftY = ny * offset;
        bool OnRun(Point2 point) => edges.Any(i => OnSegment(point,
            perimeter[i], perimeter[(i + 1) % n]));
        Point2 Shift(Point2 point) => OnRun(point)
            ? new Point2(point.X + shiftX, point.Y + shiftY) : point;

        result.Perimeter = source.Perimeter.Select(Shift).ToList();
        foreach (var room in result.Rooms)
            room.Outline = room.Outline.Select(Shift).ToList();

        MoveOpenings(source, result);
        return FloorGeometry.Validate(result, out reason);
    }

    /// <summary>
    /// Reattach doors and windows by mapping the original polygon edge
    /// to its updated edge. Shorter room wall subdivisions take precedence
    /// for doors, while windows always track exterior perimeter edges.
    /// </summary>
    public static void MoveOpenings(BuildingDocument source, BuildingDocument result)
    {
        result.Doors = source.Doors.Select(d =>
        {
            var next = d.Copy();
            next.Centre = MapAttachedPoint(source, result, d.Centre, exteriorOnly: false);
            return next;
        }).ToList();
        result.Windows = source.Windows.Select(w =>
        {
            var next = w.Copy();
            next.Centre = MapAttachedPoint(source, result, w.Centre, exteriorOnly: true);
            return next;
        }).ToList();
    }

    private static Point2 MapAttachedPoint(
        BuildingDocument source, BuildingDocument result, Point2 point,
        bool exteriorOnly)
    {
        double shortest = double.PositiveInfinity;
        Point2 mapped = point;
        void Visit(IReadOnlyList<Point2> before, IReadOnlyList<Point2> after)
        {
            if (before.Count != after.Count) return;
            for (int i = 0; i < before.Count; i++)
            {
                var a = before[i];
                var b = before[(i + 1) % before.Count];
                var movedA = after[i];
                var movedB = after[(i + 1) % after.Count];
                if (a.DistanceTo(movedA) < Epsilon && b.DistanceTo(movedB) < Epsilon)
                    continue;
                double length = a.DistanceTo(b);
                if (length >= shortest || length < Epsilon ||
                    !OnSegment(point, a, b)) continue;
                shortest = length;
                mapped = Lerp(movedA, movedB, Fraction(point, a, b));
            }
        }
        Visit(source.Perimeter, result.Perimeter);
        if (!exteriorOnly)
            for (int i = 0; i < source.Rooms.Count && i < result.Rooms.Count; i++)
                Visit(source.Rooms[i].Outline, result.Rooms[i].Outline);
        return mapped;
    }

    private static List<Point2> MoveMatching(
        IReadOnlyList<Point2> polygon, Point2 original, Point2 destination) =>
        polygon.Select(p => Near(p, original) ? destination : p).ToList();

    private static Point2 Lerp(Point2 a, Point2 b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Fraction(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < Epsilon * Epsilon) return 0;
        return Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) /
            lengthSquared, 0, 1);
    }

    private static bool Collinear(Point2 a, Point2 b, Point2 u, Point2 v)
    {
        double length = u.DistanceTo(v);
        return length > Epsilon &&
            Math.Abs((v.X - u.X) * (a.Y - u.Y) -
                     (v.Y - u.Y) * (a.X - u.X)) / length <= Epsilon &&
            Math.Abs((v.X - u.X) * (b.Y - u.Y) -
                     (v.Y - u.Y) * (b.X - u.X)) / length <= Epsilon;
    }

    private static bool Near(Point2 a, Point2 b) => a.DistanceTo(b) <= Epsilon;
    private static bool OnSegment(Point2 p, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(p, a, b).DistanceTo(p) <= Epsilon;

    private static bool OnSegmentInterior(Point2 p, Point2 a, Point2 b) =>
        OnSegment(p, a, b) && !Near(p, a) && !Near(p, b);
}
