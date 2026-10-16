using System.Globalization;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Merges two edge-adjacent rooms into one polygon by removing the internal
/// shared boundary. Handles sloped, partial and multi-segment shared walls.
/// Existing geometry is not modified unless the caller accepts the result.
/// </summary>
public static class RoomMerging
{
    private const double Epsilon = 1e-7;
    private const double MinimumLength = 1e-7;

    private sealed record DirectedEdge(Point2 Start, Point2 End, string From, string To);

    public static bool TryMergeRooms(BuildingDocument source,
        Guid primaryRoomId, Guid secondaryRoomId,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";

        if (primaryRoomId == secondaryRoomId)
        {
            reason = "Choose two different rooms.";
            return false;
        }

        var first = source.Rooms.FirstOrDefault(r => r.Id == primaryRoomId);
        var second = source.Rooms.FirstOrDefault(r => r.Id == secondaryRoomId);
        if (first is null || second is null)
        {
            reason = "One of the selected rooms no longer exists.";
            return false;
        }
        if (!FloorGeometry.Validate(source, out reason))
            return false;

        // Normalize both outlines counterclockwise so each room interior
        // stays on the left of its directed edges. Shared edges consequently
        // travel in opposite directions and cancel from the combined border.
        var a = Normalize(first.Outline);
        var b = Normalize(second.Outline);
        var points = a.Concat(b).ToList();
        var edges = new Dictionary<string, List<DirectedEdge>>(StringComparer.Ordinal);

        AddPieces(a);
        AddPieces(b);

        void AddPieces(IReadOnlyList<Point2> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 p = polygon[i];
                Point2 q = polygon[(i + 1) % polygon.Count];
                var cuts = new List<double> { 0, 1 };
                foreach (var candidate in points)
                {
                    if (PolygonGeometry.ClosestPointOnSegment(candidate, p, q)
                            .DistanceTo(candidate) > Epsilon)
                        continue;
                    double t = PositionOnEdge(candidate, p, q);
                    cuts.Add(t);
                }

                cuts.Sort();
                for (int j = 1; j < cuts.Count; j++)
                {
                    if (cuts[j] - cuts[j - 1] < Epsilon)
                        continue;
                    Point2 start = Lerp(p, q, cuts[j - 1]);
                    Point2 end = Lerp(p, q, cuts[j]);
                    if (start.DistanceTo(end) < MinimumLength) continue;
                    string from = VertexKey(start);
                    string to = VertexKey(end);
                    if (from == to) continue;

                    var segment = new DirectedEdge(start, end, from, to);
                    string key = UndirectedKey(from, to);
                    if (!edges.TryGetValue(key, out var matches))
                        edges.Add(key, matches = new List<DirectedEdge>());
                    matches.Add(segment);
                }
            }
        }

        var remaining = new List<DirectedEdge>();
        var removed = new List<DirectedEdge>();
        foreach (var pair in edges)
        {
            var matches = pair.Value;
            if (matches.Count == 1)
            {
                remaining.Add(matches[0]);
                continue;
            }
            if (matches.Count != 2 ||
                matches[0].From != matches[1].To ||
                matches[0].To != matches[1].From)
            {
                reason = "The rooms do not form a valid shared boundary.";
                return false;
            }
            removed.Add(matches[0]);
        }

        if (removed.Count == 0)
        {
            reason = "Rooms must share a wall, not just a corner or empty space.";
            return false;
        }

        // A connected, simple union has exactly one entering and one leaving
        // edge at each boundary vertex. Otherwise it might have a hole,
        // disconnected pieces, or a self-touching figure-eight boundary.
        var outgoing = new Dictionary<string, DirectedEdge>(StringComparer.Ordinal);
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in remaining)
        {
            if (!outgoing.TryAdd(edge.From, edge) || !incoming.Add(edge.To))
            {
                reason = "The selected rooms would create a branching or disconnected boundary.";
                return false;
            }
        }
        if (outgoing.Count != remaining.Count ||
            outgoing.Keys.Any(key => !incoming.Contains(key)))
        {
            reason = "The combined room does not have one continuous outer boundary.";
            return false;
        }

        var outline = new List<Point2>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = remaining[0].From;
        string starting = current;

        while (visited.Add(current))
        {
            if (!outgoing.TryGetValue(current, out var edge))
            {
                reason = "The outer boundary could not be traced.";
                return false;
            }
            outline.Add(edge.Start);
            current = edge.To;
            if (current == starting) break;
        }
        if (current != starting || visited.Count != remaining.Count)
        {
            reason = "The rooms would produce multiple separate perimeter loops.";
            return false;
        }

        // Strip unnecessary straight vertices but retain true bends and
        // points where a neighbouring third room joins the external border.
        outline = RemoveRedundantCollinearVertices(outline);
        if (!PolygonGeometry.IsValidOutline(outline, out var invalid))
        {
            reason = "The joined room has an invalid outline: " + invalid;
            return false;
        }
        double targetArea = PolygonGeometry.Area(first.Outline) +
                            PolygonGeometry.Area(second.Outline);
        if (Math.Abs(PolygonGeometry.Area(outline) - targetArea) >
            Math.Max(1e-5, targetArea * 1e-7))
        {
            reason = "The combined room area differs from the original room areas.";
            return false;
        }

        var keep = result.Rooms.First(r => r.Id == primaryRoomId);
        keep.Outline = outline;
        result.Rooms.RemoveAll(r => r.Id == secondaryRoomId);

        // A door solely on the removed partition no longer serves a wall.
        // Keep every other door exactly where it was.
        result.Doors.RemoveAll(door => removed.Any(edge =>
            PolygonGeometry.ClosestPointOnSegment(
                door.Centre, edge.Start, edge.End).DistanceTo(door.Centre) <= 0.01));

        if (!FloorGeometry.Validate(result, out reason))
            return false;

        return true;
    }

    private static List<Point2> Normalize(IReadOnlyList<Point2> points) =>
        PolygonGeometry.SignedArea(points) < 0
            ? points.Reverse().ToList()
            : points.ToList();

    private static List<Point2> RemoveRedundantCollinearVertices(List<Point2> vertices)
    {
        bool removed;
        do
        {
            removed = false;
            if (vertices.Count <= 3) break;
            for (int i = 0; i < vertices.Count; i++)
            {
                var p = vertices[(i + vertices.Count - 1) % vertices.Count];
                var q = vertices[i];
                var r = vertices[(i + 1) % vertices.Count];
                if (p.DistanceTo(r) < MinimumLength) continue;
                if (PolygonGeometry.ClosestPointOnSegment(q, p, r)
                        .DistanceTo(q) > Epsilon) continue;
                vertices.RemoveAt(i);
                removed = true;
                break;
            }
        } while (removed);
        return vertices;
    }

    private static string VertexKey(Point2 point) =>
        Math.Round(point.X, 6).ToString("F6", CultureInfo.InvariantCulture) +
        "," +
        Math.Round(point.Y, 6).ToString("F6", CultureInfo.InvariantCulture);

    private static string UndirectedKey(string first, string second) =>
        string.CompareOrdinal(first, second) < 0
            ? first + "|" + second : second + "|" + first;

    private static double PositionOnEdge(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSq = dx * dx + dy * dy;
        return lengthSq < Epsilon * Epsilon
            ? 0
            : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
    }

    private static Point2 Lerp(Point2 a, Point2 b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static BuildingDocument Copy(BuildingDocument source) => new()
    {
        Name = source.Name,
        SchemaVersion = source.SchemaVersion,
        Perimeter = source.Perimeter.ToList(),
        Rooms = source.Rooms.Select(r => r.Copy()).ToList(),
        Doors = source.Doors.Select(d => d.Copy()).ToList(),
        Windows = source.Windows.Select(w => w.Copy()).ToList()
    };
}
