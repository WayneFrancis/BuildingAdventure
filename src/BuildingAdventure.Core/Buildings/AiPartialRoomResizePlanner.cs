using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Conservative partial-wall and L-shaped room improvements. Creates a
/// rectangular strip wholly in one existing room and adjoining another;
/// the shared-edge Boolean operation is owned and validated by C#.
/// </summary>
public static class AiPartialRoomResizePlanner
{
    private const double Eps = 1e-6;
    private static readonly double[] Depths = [0.5, 0.75, 1.0];
    private const double MaximumTransferArea = 12;

    public static IReadOnlyList<AiRoomResizeCandidate> FindCandidates(
        BuildingDocument source, int floorIndex, int maxResults = 8)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) || maxResults < 1)
            return [];
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var rooms = floor.Rooms
            .Where(r => r.Type is not ("Hallway" or "Corridor") &&
                r.Outline.Count is >= 4 and <= 16 && Orthogonal(r.Outline))
            .ToArray();
        var choices = new List<AiRoomResizeCandidate>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        int checks = 0;

        foreach (var donor in rooms)
        foreach (var receiver in rooms)
        {
            if (donor.Id == receiver.Id) continue;
            if (PolygonGeometry.Area(donor.Outline) < 8) continue;
            foreach (var from in Edges(donor.Outline))
            foreach (var to in Edges(receiver.Outline))
            {
                bool horizontal = Near(from.A.Y, from.B.Y) &&
                    Near(to.A.Y, to.B.Y) && Near(from.A.Y, to.A.Y);
                bool vertical = Near(from.A.X, from.B.X) &&
                    Near(to.A.X, to.B.X) && Near(from.A.X, to.A.X);
                if (!horizontal && !vertical) continue;
                double lo = Math.Max(
                    Math.Min(horizontal ? from.A.X : from.A.Y,
                             horizontal ? from.B.X : from.B.Y),
                    Math.Min(horizontal ? to.A.X : to.A.Y,
                             horizontal ? to.B.X : to.B.Y));
                double hi = Math.Min(
                    Math.Max(horizontal ? from.A.X : from.A.Y,
                             horizontal ? from.B.X : from.B.Y),
                    Math.Max(horizontal ? to.A.X : to.A.Y,
                             horizontal ? to.B.X : to.B.Y));
                if (hi - lo < 1.8) continue;

                // Whole-edge transfers between two perfect rectangles are
                // handled by AiRoomResizePlanner. This pass is reserved for
                // partial contacts or truly irregular room outlines.
                bool donorFull = SameRange(from, lo, hi, horizontal);
                bool receiverFull = SameRange(to, lo, hi, horizontal);
                if (donor.Outline.Count == 4 && receiver.Outline.Count == 4 &&
                    donorFull && receiverFull)
                    continue;

                var wallA = horizontal
                    ? new Point2(lo, from.A.Y)
                    : new Point2(from.A.X, lo);
                var wallB = horizontal
                    ? new Point2(hi, from.A.Y)
                    : new Point2(from.A.X, hi);

                // No implicit door removal, movement or ownership changes.
                if (floor.Doors.Any(d => OnSegment(d.Centre, wallA, wallB)))
                    continue;

                foreach (int sign in new[] { -1, 1 })
                {
                    var centre = (lo + hi) / 2;
                    var inwardProbe = horizontal
                        ? new Point2(centre, from.A.Y + sign * 0.02)
                        : new Point2(from.A.X + sign * 0.02, centre);
                    var outwardProbe = horizontal
                        ? new Point2(centre, from.A.Y - sign * 0.02)
                        : new Point2(from.A.X - sign * 0.02, centre);
                    if (!PolygonGeometry.ContainsPoint(donor.Outline, inwardProbe) ||
                        PolygonGeometry.ContainsPoint(receiver.Outline, inwardProbe) ||
                        !PolygonGeometry.ContainsPoint(receiver.Outline, outwardProbe))
                        continue;

                    // Candidate depths include the ACTUAL opposite edges
                    // of the donor. A user can request an entire thin band
                    // rather than being limited to the former 1m nudge.
                    // For the demo L-shaped living room this adds 2m,
                    // allowing all 6 x 2m above the bedroom to transfer.
                    var depths = Depths.Concat(donor.Outline.Select(p =>
                        Math.Abs((horizontal ? p.Y : p.X) -
                                 (horizontal ? from.A.Y : from.A.X))))
                        .Where(d => d >= 0.2 && d <= 4)
                        .Distinct().OrderBy(d => d).ToArray();
                    foreach (double depth in depths)
                    {
                        if (checks >= 180) break;
                        double x0 = horizontal ? lo :
                            Math.Min(from.A.X, from.A.X + sign * depth);
                        double x1 = horizontal ? hi :
                            Math.Max(from.A.X, from.A.X + sign * depth);
                        double y0 = horizontal ?
                            Math.Min(from.A.Y, from.A.Y + sign * depth) : lo;
                        double y1 = horizontal ?
                            Math.Max(from.A.Y, from.A.Y + sign * depth) : hi;
                        double area = (x1 - x0) * (y1 - y0);
                        if (area < 0.5 || area > MaximumTransferArea) continue;
                        string key = FormattableString.Invariant(
                            $"{donor.Id}:{receiver.Id}:{x0:F5}:{y0:F5}:{x1:F5}:{y1:F5}");
                        if (!unique.Add(key)) continue;
                        checks++;

                        var edit = new AiRoomTransfer
                        {
                            FloorIndex = floorIndex,
                            FromRoomId = donor.Id,
                            ToRoomId = receiver.Id,
                            XMin = x0, YMin = y0,
                            XMax = x1, YMax = y1,
                            Reason = "Resize rooms along a partial or irregular shared wall"
                        };
                        if (!AiLayoutImprover.TryApplyRoomTransfer(
                            source, floorIndex, edit, out var suggestion, out _))
                            continue;
                        var proposedFloor = BuildingStoreys.GetFloor(
                            suggestion.Building, floorIndex);
                        var afterDonor = proposedFloor.Rooms
                            .Single(r => r.Id == donor.Id);
                        var afterReceiver = proposedFloor.Rooms
                            .Single(r => r.Id == receiver.Id);
                        // Reject minuscule donor slivers. A narrow receiver is
                        // allowed, as improving it may be precisely the goal.
                        double shortSide = Math.Min(
                            afterDonor.Outline.Max(p => p.X) -
                                afterDonor.Outline.Min(p => p.X),
                            afterDonor.Outline.Max(p => p.Y) -
                                afterDonor.Outline.Min(p => p.Y));
                        if (shortSide < 2.1 ||
                            PolygonGeometry.Area(afterDonor.Outline) < 5)
                            continue;
                        if (Math.Abs(PolygonGeometry.Area(afterReceiver.Outline) -
                            PolygonGeometry.Area(receiver.Outline) - area) > 0.01)
                            continue;
                        choices.Add(new AiRoomResizeCandidate(
                            suggestion, receiver.Name, donor.Name, area,
                            PolygonGeometry.Area(receiver.Outline),
                            PolygonGeometry.Area(afterReceiver.Outline)));
                    }
                }
            }
        }

        // A modest strip preserves practical proportions, and the model can
        // decide which receiving room best meets the user's goal.
        return choices.OrderBy(c => c.AreaSquareMetres)
            .ThenBy(c => c.GainingRoom, StringComparer.Ordinal)
            .ThenBy(c => c.LosingRoom, StringComparer.Ordinal)
            .Take(maxResults).ToArray();
    }

    private static IEnumerable<(Point2 A, Point2 B)> Edges(
        IReadOnlyList<Point2> outline) =>
        Enumerable.Range(0, outline.Count).Select(i =>
            (outline[i], outline[(i + 1) % outline.Count]));

    private static bool Orthogonal(IReadOnlyList<Point2> outline) =>
        Edges(outline).All(e =>
            Near(e.A.X, e.B.X) || Near(e.A.Y, e.B.Y));

    private static bool SameRange((Point2 A, Point2 B) edge,
        double lo, double hi, bool horizontal)
    {
        double a = horizontal ? edge.A.X : edge.A.Y;
        double b = horizontal ? edge.B.X : edge.B.Y;
        return Near(Math.Min(a, b), lo) && Near(Math.Max(a, b), hi);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < Eps;

    private static bool OnSegment(Point2 point, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(point, a, b)
            .DistanceTo(point) <= Eps;
}
