using System.Text.RegularExpressions;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Route specific architectural verbs BEFORE listing geometry candidates.
/// An unsupported merge/split must fail closed, not become a wall nudge.
/// The model may rank numbered prevalidated plans, never invent geometry.
/// </summary>
public static class AiRoomOperationPlanner
{
    private const double Eps = 0.001;

    public static bool TryPlan(BuildingDocument building, int floorIndex,
        string? goal, int maxResults, out IReadOnlyList<AiLayoutOption> options)
    {
        options = [];
        if (string.IsNullOrWhiteSpace(goal) ||
            !BuildingStoreys.HasFloor(building, floorIndex)) return false;
        bool merge = Regex.IsMatch(goal, @"\b(?:combine|merge|join)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool split = Regex.IsMatch(goal,
            @"\b(?:split|divide|subdivide)\b|\bconvert\b.{0,100}?\b(?:into|to)\s+(?:2|two)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!merge && !split) return false;

        // A user can mention a room by its name, with or without "the".
        // Resolving identities in C# protects against model hallucinations.
        var rooms = BuildingStoreys.GetFloor(building, floorIndex).Rooms;
        var named = rooms.Where(room => Mentions(goal, room.Name)).ToArray();
        if (merge)
        {
            if (named.Length != 2) return true;
            options = Merge(building, floorIndex, named[0], named[1]);
        }
        else
        {
            if (named.Length != 1) return true;
            options = Split(building, floorIndex, named[0], maxResults);
        }
        return true;
    }

    /// <summary>
    /// A specific enlargement has a single named receiver. Select the FIRST
    /// room mentioned after the enlargement verb: "Make Pantry larger by
    /// taking space from Kitchen" must never enlarge Kitchen or Study.
    /// </summary>
    public static RoomDefinition? EnlargementTarget(
        BuildingDocument building, int floorIndex, string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal) ||
            !BuildingStoreys.HasFloor(building, floorIndex)) return null;
        var action = Regex.Match(goal, @"\b(?:increase|enlarge|expand|grow|widen|extend|make)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!action.Success) return null;
        string after = goal[(action.Index + action.Length)..];
        var rooms = BuildingStoreys.GetFloor(building, floorIndex).Rooms;
        return rooms.Select(r => (Room: r, At: MatchIndex(after, r.Name)))
            .Where(x => x.At >= 0)
            .OrderBy(x => x.At).ThenByDescending(x => x.Room.Name.Length)
            .Select(x => x.Room).FirstOrDefault();
    }

    private static bool Mentions(string text, string name) =>
        MatchIndex(text, name) >= 0;

    private static int MatchIndex(string text, string name) =>
        Regex.Match(text,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Index is var i &&
        Regex.IsMatch(text,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? i : -1;

    private static IReadOnlyList<AiLayoutOption> Merge(
        BuildingDocument source, int floorIndex,
        RoomDefinition a, RoomDefinition b)
    {
        // Keep the larger room's identity and purpose, preserving its doors
        // and label; a secondary room is completely absorbed.
        var first = PolygonGeometry.Area(a.Outline) >=
                    PolygonGeometry.Area(b.Outline) ? a : b;
        var second = first.Id == a.Id ? b : a;
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        if (!RoomMerging.TryMergeRooms(floor, first.Id, second.Id,
                out var changedFloor, out _))
            return [];
        var proposed = BuildingStoreys.Copy(source);
        BuildingStoreys.SetFloor(proposed, floorIndex, changedFloor);
        // When the absorbed room served a hearth, the merged room now owns it.
        foreach (var chimney in proposed.Chimneys.Where(c =>
            c.HearthFloorIndex == floorIndex))
        {
            chimney.HearthRoomIds = chimney.HearthRoomIds
                .Select(id => id == second.Id ? first.Id : id)
                .Distinct().ToList();
        }
        if (!BuildingStoreys.Validate(proposed, out _)) return [];
        var removedDoorCount = floor.Doors.Count - changedFloor.Doors.Count;
        var summary = $"Combine {a.Name} and {b.Name} into {first.Name}" +
            (removedDoorCount > 0
                ? $" (remove {removedDoorCount} redundant internal doorway(s))" : "");
        return [new AiLayoutOption(1, "Combine rooms", summary,
            PolygonGeometry.Area(second.Outline),
            new BuildingSuggestion(proposed, 0, [floorIndex],
                "AI IMPROVEMENT DRAFT — " + summary))];
    }

    /// <summary>
    /// Split an orthogonal rectangular room in two. Existing openings never
    /// move or disappear; if a new section lacks access, install a validated
    /// internal doorway (prefer a neighbouring room to a door between halves).
    /// </summary>
    private static IReadOnlyList<AiLayoutOption> Split(
        BuildingDocument source, int floorIndex,
        RoomDefinition original, int maxResults)
    {
        if (maxResults < 1 || original.Outline.Count != 4 ||
            source.Chimneys.Any(c => c.HearthFloorIndex == floorIndex &&
                c.HearthRoomIds.Contains(original.Id))) return [];
        double x0 = original.Outline.Min(p => p.X);
        double x1 = original.Outline.Max(p => p.X);
        double y0 = original.Outline.Min(p => p.Y);
        double y1 = original.Outline.Max(p => p.Y);
        double width = x1 - x0, height = y1 - y0;
        if (Math.Abs(PolygonGeometry.Area(original.Outline) - width * height) > Eps)
            return []; // Not a simple axis-aligned rectangle.
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        string newName = original.Name + " 2";
        for (int suffix = 3; floor.Rooms.Any(r =>
             r.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)); suffix++)
            newName = original.Name + " " + suffix;

        var options = new List<AiLayoutOption>();
        foreach (bool vertical in new[] { width >= height, width < height })
        {
            // Prefer dividing the long direction and preserving an existing
            // doorway's original side of the new partition.
            if ((vertical ? width : height) < 2.6) continue;
            foreach (double fraction in new[] { 0.38, 0.62, 0.5 })
            {
                double cut = vertical ? x0 + fraction * width :
                                        y0 + fraction * height;
                var left = vertical ? Rect(x0, y0, cut, y1) :
                                      Rect(x0, y0, x1, cut);
                var right = vertical ? Rect(cut, y0, x1, y1) :
                                       Rect(x0, cut, x1, y1);
                if (!Usable(left) || !Usable(right)) continue;

                // Existing doors must remain supported by an actual wall,
                // not end up at the new partition junction.
                bool firstHasDoor = floor.Doors.Any(d => OnOutline(d.Centre, left));
                bool secondHasDoor = floor.Doors.Any(d => OnOutline(d.Centre, right));
                bool keepFirst = firstHasDoor || !secondHasDoor;
                var keepShape = keepFirst ? left : right;
                var addShape = keepFirst ? right : left;

                var proposal = BuildingStoreys.Copy(source);
                var changed = BuildingStoreys.GetFloor(proposal, floorIndex);
                changed.Rooms.Single(r => r.Id == original.Id).Outline = keepShape;
                var addition = new RoomDefinition
                {
                    Name = newName, Type = original.Type,
                    Outline = addShape
                };
                changed.Rooms.Add(addition);
                BuildingStoreys.SetFloor(proposal, floorIndex, changed);
                if (!EnsureAccess(proposal, floorIndex, original.Id,
                        addition.Id)) continue;
                if (!BuildingStoreys.Validate(proposal, out _)) continue;

                string summary = $"Split {original.Name} into " +
                    $"{original.Name} and {newName}";
                int addedDoors = BuildingStoreys.GetFloor(proposal, floorIndex)
                    .Doors.Count - floor.Doors.Count;
                if (addedDoors > 0)
                    summary += $" (add {addedDoors} connecting doorway(s))";
                options.Add(new AiLayoutOption(options.Count + 1,
                    "Split room", summary, PolygonGeometry.Area(addShape),
                    new BuildingSuggestion(proposal, 0, [floorIndex],
                        "AI IMPROVEMENT DRAFT — " + summary)));
                if (options.Count >= maxResults) return options;
            }
        }
        return options;
    }

    private static bool EnsureAccess(BuildingDocument proposal, int floorIndex,
        Guid originalId, Guid additionId)
    {
        foreach (var id in new[] { originalId, additionId })
        {
            var floor = BuildingStoreys.GetFloor(proposal, floorIndex);
            var room = floor.Rooms.Single(r => r.Id == id);
            if (floor.Doors.Any(d => OnOutline(d.Centre, room.Outline)))
                continue;

            var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms);
            var candidates = walls.Where(w => !w.IsExterior && w.Length >= 1.0 &&
                OnOutline(Midpoint(w), room.Outline))
                .Select(w => (Wall: w, Neighbours: floor.Rooms
                    .Where(other => other.Id != id &&
                        OnOutline(Midpoint(w), other.Outline)).ToArray()))
                .Where(x => x.Neighbours.Length > 0)
                .OrderBy(x => x.Neighbours.Any(r =>
                    r.Id != originalId && r.Id != additionId) ? 0 : 1)
                .ThenByDescending(x => x.Wall.Length).ToArray();
            bool added = false;
            foreach (var candidate in candidates)
            {
                foreach (double ratio in new[] { 0.5, 0.3, 0.7 })
                {
                    var wall = candidate.Wall;
                    var centre = new Point2(
                        wall.A.X + (wall.B.X - wall.A.X) * ratio,
                        wall.A.Y + (wall.B.Y - wall.A.Y) * ratio);
                    if (!AiDoorPlacementPlanner.ClearOfStairs(
                            proposal, floorIndex, centre, 0.8)) continue;
                    var trial = BuildingStoreys.Copy(proposal);
                    var edited = BuildingStoreys.GetFloor(trial, floorIndex);
                    edited.Doors.Add(new DoorDefinition
                    {
                        Centre = centre, Width = 0.8
                    });
                    BuildingStoreys.SetFloor(trial, floorIndex, edited);
                    if (!BuildingStoreys.Validate(trial, out _)) continue;
                    // The candidate is detached, so update its floor only.
                    BuildingStoreys.SetFloor(proposal, floorIndex, edited);
                    added = true;
                    break;
                }
                if (added) break;
            }
            if (!added) return false;
        }
        return true;
    }

    private static bool Usable(IReadOnlyList<Point2> outline) =>
        PolygonGeometry.Area(outline) >= 2.0 &&
        outline.Max(p => p.X) - outline.Min(p => p.X) >= 1.2 &&
        outline.Max(p => p.Y) - outline.Min(p => p.Y) >= 1.2;

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    private static Point2 Midpoint(WallSegment segment) =>
        new((segment.A.X + segment.B.X) / 2,
            (segment.A.Y + segment.B.Y) / 2);

    private static bool OnOutline(Point2 p, IReadOnlyList<Point2> vertices) =>
        Enumerable.Range(0, vertices.Count).Any(i =>
            PolygonGeometry.ClosestPointOnSegment(p,
                vertices[i], vertices[(i + 1) % vertices.Count])
                .DistanceTo(p) <= Eps);
}
