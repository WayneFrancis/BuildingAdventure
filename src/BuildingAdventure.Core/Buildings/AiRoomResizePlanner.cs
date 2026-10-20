using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Validated shared-partition proposals between adjoining four-sided rooms.
/// The common wall may be slightly off-axis (as when manually drawn).
/// No wall or doorway is modified until the user accepts the suggestion.
/// </summary>
public sealed record AiRoomResizeCandidate(
    BuildingSuggestion Suggestion, string GainingRoom, string LosingRoom,
    double AreaSquareMetres, double GainingAreaBefore, double GainingAreaAfter);

public static class AiRoomResizePlanner
{
    private const double Eps = 1e-5;
    private const double MaxLean = 0.15; // metres over the entire shared edge
    private const double MinResultWidth = 2.1; // never make a room less usable
    private static readonly double[] Steps = [0.3, 0.6, 0.9, 1.2];

    public static IReadOnlyList<AiRoomResizeCandidate> FindCandidates(
        BuildingDocument source, int floorIndex, int maxResults = 12,
        Guid? gainingRoomId = null, bool moveSharedDoors = false)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) || maxResults <= 0)
            return [];
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        // Crucially, do NOT reject rooms by their current width: an
        // undersized room is often precisely what needs improvement.
        var rooms = floor.Rooms.Where(r =>
            r.Type is not ("Hallway" or "Corridor") &&
            r.Outline.Count == 4).ToArray();
        var options = new List<AiRoomResizeCandidate>();

        for (int i = 0; i < rooms.Length; i++)
        for (int j = i + 1; j < rooms.Length; j++)
        {
            var first = rooms[i];
            var second = rooms[j];
            for (int a = 0; a < first.Outline.Count; a++)
            for (int b = 0; b < second.Outline.Count; b++)
            {
                var start = first.Outline[a];
                var end = first.Outline[(a + 1) % 4];
                if (!SameEdge(start, end,
                    second.Outline[b], second.Outline[(b + 1) % 4]))
                    continue;

                double dx = Math.Abs(end.X - start.X);
                double dy = Math.Abs(end.Y - start.Y);
                bool nearVertical = dy >= 2.4 &&
                    dx <= Math.Min(MaxLean, dy * 0.05);
                bool nearHorizontal = dx >= 2.4 &&
                    dy <= Math.Min(MaxLean, dx * 0.05);
                if (!nearVertical && !nearHorizontal) continue;

                // Only one entire shared side, with the other three sides
                // horizontal/vertical. This also matches near-rectangular
                // trapezoids without inventing arbitrary polygon edits.
                if (!OtherEdgesAxisAligned(first.Outline, a) ||
                    !OtherEdgesAxisAligned(second.Outline, b))
                    continue;
                var partitionDoors = floor.Doors.Where(d =>
                    OnSegment(d.Centre, start, end)).ToArray();
                if (partitionDoors.Length > 0 && !moveSharedDoors)
                    continue; // generic improvements never move existing doors

                foreach (double delta in Steps)
                foreach (int sign in new[] { -1, 1 })
                {
                    // Shift horizontally for a near-vertical partition and
                    // vertically for a near-horizontal one. Both endpoints
                    // move identically; the tiny hand-drawn lean is retained.
                    var movement = nearVertical
                        ? new Point2(sign * delta, 0)
                        : new Point2(0, sign * delta);
                    var candidate = BuildingStoreys.Copy(source);
                    var level = BuildingStoreys.GetFloor(candidate, floorIndex);
                    var aRoom = level.Rooms.Single(r => r.Id == first.Id);
                    var bRoom = level.Rooms.Single(r => r.Id == second.Id);
                    aRoom.Outline = MoveEndpoints(first.Outline, start, end,
                        movement);
                    bRoom.Outline = MoveEndpoints(second.Outline, start, end,
                        movement);
                    // For an explicitly requested enlargement, the door
                    // remains in the same relative position on its shifted
                    // partition, retaining its ID, width and visibility.
                    bool relocatedDoorsClear = true;
                    foreach (var old in partitionDoors)
                    {
                        var relocated = level.Doors.Single(d => d.Id == old.Id);
                        relocated.Centre = new Point2(old.Centre.X + movement.X,
                            old.Centre.Y + movement.Y);
                        if (!AiDoorPlacementPlanner.ClearOfStairs(
                                candidate, floorIndex, relocated.Centre,
                                relocated.Width))
                        {
                            relocatedDoorsClear = false;
                            break;
                        }
                    }
                    if (!relocatedDoorsClear) continue;

                    // Judge dimensions AFTER the proposed improvement.
                    // The existing 1.5m room in example13 can now gain
                    // width instead of being excluded at the outset.
                    if (BoundingShortSide(aRoom.Outline) < MinResultWidth ||
                        BoundingShortSide(bRoom.Outline) < MinResultWidth)
                        continue;
                    BuildingStoreys.SetFloor(candidate, floorIndex, level);
                    if (!BuildingStoreys.Validate(candidate, out _) ||
                        !AiOpeningProtection.PreservesExistingDoorAccess(
                            source, candidate, floorIndex))
                        continue;

                    double areaFirstBefore = PolygonGeometry.Area(first.Outline);
                    double areaSecondBefore = PolygonGeometry.Area(second.Outline);
                    double changeFirst = PolygonGeometry.Area(aRoom.Outline) -
                        areaFirstBefore;
                    double changeSecond = PolygonGeometry.Area(bRoom.Outline) -
                        areaSecondBefore;
                    if (Math.Abs(changeFirst + changeSecond) > 0.001 ||
                        Math.Abs(changeFirst) < 0.25)
                        continue;
                    var gainId = changeFirst > 0 ? first.Id : second.Id;
                    if (gainingRoomId.HasValue && gainId != gainingRoomId.Value)
                        continue;
                    if (Math.Abs(RoomLayoutPlanner.UnassignedArea(floor) -
                        RoomLayoutPlanner.UnassignedArea(level)) > 0.01)
                        continue;

                    var gained = changeFirst > 0 ? first : second;
                    var lost = changeFirst > 0 ? second : first;
                    double beforeArea = PolygonGeometry.Area(gained.Outline);
                    double areaGained = Math.Abs(changeFirst);
                    var draft = new BuildingSuggestion(candidate, 0, [floorIndex],
                        $"AI IMPROVEMENT DRAFT — Resize {gained.Name} and " +
                        $"{lost.Name} by moving their shared wall {delta:0.#} m.");
                    options.Add(new AiRoomResizeCandidate(draft,
                        gained.Name, lost.Name, areaGained, beforeArea,
                        beforeArea + areaGained));
                }
            }
        }

        return options.OrderBy(c => c.AreaSquareMetres)
            .ThenBy(c => c.GainingRoom, StringComparer.Ordinal)
            .ThenBy(c => c.LosingRoom, StringComparer.Ordinal)
            .Take(maxResults).ToArray();
    }

    private static List<Point2> MoveEndpoints(
        IReadOnlyList<Point2> outline, Point2 start, Point2 end, Point2 offset)
    {
        return outline.Select(p =>
            Near(p, start) || Near(p, end)
                ? new Point2(p.X + offset.X, p.Y + offset.Y)
                : p).ToList();
    }

    private static bool OtherEdgesAxisAligned(
        IReadOnlyList<Point2> outline, int sharedEdge)
    {
        for (int i = 0; i < outline.Count; i++)
        {
            if (i == sharedEdge) continue;
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            if (Math.Abs(a.X - b.X) > Eps &&
                Math.Abs(a.Y - b.Y) > Eps)
                return false;
        }
        return true;
    }

    private static double BoundingShortSide(IReadOnlyList<Point2> outline) =>
        Math.Min(outline.Max(p => p.X) - outline.Min(p => p.X),
                 outline.Max(p => p.Y) - outline.Min(p => p.Y));

    private static bool SameEdge(Point2 a, Point2 b, Point2 c, Point2 d) =>
        (Near(a, c) && Near(b, d)) || (Near(a, d) && Near(b, c));

    private static bool Near(Point2 a, Point2 b) => a.DistanceTo(b) <= Eps;

    private static bool OnSegment(Point2 point, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(point, a, b)
            .DistanceTo(point) <= Eps;
}
