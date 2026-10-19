using System.Text.RegularExpressions;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Generate safe, non-destructive doorway proposals on actual shared walls.
/// The language model chooses a numbered proposal; it never supplies a door
/// position or changes an existing opening.
/// </summary>
public static class AiDoorPlacementPlanner
{
    private const double Eps = 0.001;
    private const double Width = 0.9;
    private static readonly double[] Positions = [0.5, 0.32, 0.68];

    public static bool IsDoorwayGoal(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal)) return false;
        // Never interpret a negative constraint as authorization to insert
        // an opening, even if the sentence contains "add a door".
        if (Regex.IsMatch(goal,
            @"\b(?:do\s+not|don't|never)\s+(?:\w+\s+){0,4}(?:add|create|place|install|insert|make)\b.{0,40}\b(?:door|doors|doorway|doorways)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(goal,
                @"\bwithout\s+(?:adding\s+)?(?:(?:a|any)\s+)?(?:new\s+)?(?:door|doors|doorway|doorways)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        // "Preserve doors" in the default resize goal is NOT an instruction
        // to create an opening. Only explicit creation/connection intent
        // should switch the planner away from room improvements.
        return Regex.IsMatch(goal,
            @"\b(?:add|create|install|place|insert|put|build|open|propose|suggest)\b.{0,35}\b(?:door|doors|doorway|doorways|entrance|entryway)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(goal, @"\bconnect\s+(?:the\s+)?\w",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static IReadOnlyList<AiLayoutOption> FindOptions(
        BuildingDocument source, int floorIndex, string goal,
        int maxResults = 20)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) || maxResults < 1)
            return [];
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var rooms = floor.Rooms.ToArray();

        // A request naming TWO rooms must match both actual rooms. Do not
        // silently connect one known room to a different neighbour because
        // the other name was misspelled or is not present on this floor.
        var pair = Regex.Match(goal,
            @"\bbetween\s+(?:the\s+)?(?<from>.+?)\s+and\s+(?:the\s+)?(?<to>.+?)(?:[.!?;]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!pair.Success)
            pair = Regex.Match(goal,
                @"\bconnect\s+(?:the\s+)?(?<from>.+?)\s+to\s+(?:the\s+)?(?<to>.+?)(?:[.!?;]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (pair.Success)
        {
            var from = pair.Groups["from"].Value.Trim();
            var to = pair.Groups["to"].Value.Trim();
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ||
                !rooms.Any(r => string.Equals(r.Name, from,
                    StringComparison.OrdinalIgnoreCase)) ||
                !rooms.Any(r => string.Equals(r.Name, to,
                    StringComparison.OrdinalIgnoreCase)))
                return [];
        }

        // Explicitly named rooms are hard constraints. Never place a door
        // between some other pair because the requested pair has no shared wall.
        var named = rooms.Where(r => Regex.IsMatch(goal,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(r.Name) +
            @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (named.Length > 2) return [];

        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
            .Where(w => !w.IsExterior && w.Length >= Width + 0.2)
            .OrderByDescending(w => w.Length).ToArray();
        var options = new List<AiLayoutOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wall in walls)
        {
            var centre = Midpoint(wall);
            var owners = rooms.Where(r => OnOutline(centre, r.Outline)).ToArray();
            if (owners.Length != 2) continue;
            if (named.Any(r => owners.All(owner => owner.Id != r.Id)))
                continue;
            if (named.Length == 2 &&
                (owners.All(r => r.Id != named[0].Id) ||
                 owners.All(r => r.Id != named[1].Id)))
                continue;

            // Once two rooms have a direct doorway, don't add another
            // unrequested opening between them.
            if (floor.Doors.Any(d =>
                OnOutline(d.Centre, owners[0].Outline) &&
                OnOutline(d.Centre, owners[1].Outline)))
                continue;

            foreach (double t in Positions)
            {
                var location = new Point2(
                    wall.A.X + (wall.B.X - wall.A.X) * t,
                    wall.A.Y + (wall.B.Y - wall.A.Y) * t);
                string key = FormattableString.Invariant(
                    $"{owners[0].Id}:{owners[1].Id}:{location.X:F4}:{location.Y:F4}");
                if (!seen.Add(key) || !ClearOfStairs(source, floorIndex, location))
                    continue;

                var proposal = BuildingStoreys.Copy(source);
                var changed = BuildingStoreys.GetFloor(proposal, floorIndex);
                changed.Doors.Add(new DoorDefinition
                {
                    Centre = location, Width = Width
                });
                BuildingStoreys.SetFloor(proposal, floorIndex, changed);
                if (!BuildingStoreys.Validate(proposal, out _))
                    continue;

                string summary = $"Add doorway between {owners[0].Name} and " +
                    $"{owners[1].Name} at ({location.X:0.##}, {location.Y:0.##}) m";
                var draft = new BuildingSuggestion(proposal, 0, [floorIndex],
                    "AI IMPROVEMENT DRAFT — " + summary +
                    ". Existing rooms and openings are unchanged.");
                options.Add(new AiLayoutOption(options.Count + 1,
                    "Add doorway", summary, 0, draft));
                if (options.Count >= maxResults) return options;
            }
        }
        return options;
    }

    private static bool ClearOfStairs(BuildingDocument source,
        int floorIndex, Point2 centre)
    {
        // Check the entire potentially affected landing/flight area,
        // not just whether the door centre falls inside a staircase.
        foreach (var stair in source.Staircases.Where(s =>
            s.LowerFloorIndex == floorIndex ||
            s.LowerFloorIndex + 1 == floorIndex))
        foreach (var shape in new[] { BuildingStoreys.StairFootprint(stair) }
            .Concat(BuildingStoreys.StairLandings(stair)))
        {
            if (PolygonGeometry.ContainsPoint(shape, centre)) return false;
            for (int i = 0; i < shape.Count; i++)
            {
                if (PolygonGeometry.ClosestPointOnSegment(centre, shape[i],
                        shape[(i + 1) % shape.Count])
                    .DistanceTo(centre) < Width / 2 + 0.1)
                    return false;
            }
        }
        return true;
    }

    private static Point2 Midpoint(WallSegment wall) =>
        new((wall.A.X + wall.B.X) / 2, (wall.A.Y + wall.B.Y) / 2);

    private static bool OnOutline(Point2 point, IReadOnlyList<Point2> ring) =>
        Enumerable.Range(0, ring.Count).Any(i =>
            PolygonGeometry.ClosestPointOnSegment(
                point, ring[i], ring[(i + 1) % ring.Count])
                .DistanceTo(point) <= Eps);
}
