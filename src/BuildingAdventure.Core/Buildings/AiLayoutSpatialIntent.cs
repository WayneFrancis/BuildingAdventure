using System.Text.RegularExpressions;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Applies explicit natural-language location constraints BEFORE a model
/// chooses from already-validated geometry. An instruction to take space
/// ABOVE a bedroom must never be satisfied by a valid move to its LEFT.
/// </summary>
public static class AiLayoutSpatialIntent
{
    private const double Eps = 1e-5;

    public static IReadOnlyList<AiLayoutOption> Filter(
        BuildingDocument source, int floorIndex, string goal,
        IReadOnlyList<AiLayoutOption> options)
    {
        if (string.IsNullOrWhiteSpace(goal) || options.Count == 0)
            return options;
        var floor = BuildingStoreys.GetFloor(source, floorIndex);

        // First determine which named room is to GAIN area. For example,
        // "Make the bedroom larger" should not enlarge the living room.
        var targetMatch = Regex.Match(goal,
            @"\b(?:make|enlarge|expand|grow|widen)\s+(?:the\s+)?(?<name>.+?)\s+(?:larger|bigger|wider)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string? requestedTarget = targetMatch.Success
            ? floor.Rooms.OrderByDescending(r => r.Name.Length)
                .FirstOrDefault(r => string.Equals(r.Name,
                    targetMatch.Groups["name"].Value.Trim(),
                    StringComparison.OrdinalIgnoreCase))?.Name
            : null;

        // Directional words are hard constraints. The floor coordinate
        // system has Y increasing downwards, so ABOVE is smaller Y.
        var directionMatches = Regex.Matches(goal,
            @"\b(above|below|underneath|under|left|right|top|bottom)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Prefer the last spatial reference. "top band ... ABOVE the
        // bedroom" refers to the bedroom, not the earlier 'top' phrase.
        Match? directionMatch = directionMatches.Count == 0 ? null
            : directionMatches[directionMatches.Count - 1];
        if (directionMatch is null && requestedTarget is null)
            return options;
        string direction = directionMatch is not null
            ? directionMatch.Value.ToLowerInvariant() switch
            {
                "under" or "underneath" or "bottom" => "below",
                "top" => "above",
                var d => d
            }
            : "";

        // An explicitly named room AFTER "above" or "to the left of" is
        // the spatial anchor. Otherwise the room being enlarged is used.
        string following = directionMatch is not null
            ? goal[(directionMatch.Index + directionMatch.Length)..]
            : "";
        RoomDefinition? anchor = floor.Rooms
            .OrderByDescending(r => r.Name.Length)
            .FirstOrDefault(r => Regex.IsMatch(following,
                @"\b" + Regex.Escape(r.Name) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (anchor is null && requestedTarget is not null)
            anchor = floor.Rooms.FirstOrDefault(r =>
                string.Equals(r.Name, requestedTarget,
                    StringComparison.OrdinalIgnoreCase));

        var matched = new List<(AiLayoutOption Option, double Area)>();
        foreach (var option in options)
        {
            if (!TryGainedFootprint(source, floorIndex, option,
                    out var gainingRoom, out var gained))
                continue;
            if (requestedTarget is not null &&
                !string.Equals(gainingRoom.Name, requestedTarget,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (direction.Length > 0)
            {
                var reference = anchor ?? gainingRoom;
                if (!MatchesSide(gained, reference.Outline, direction))
                    continue;
            }
            matched.Add((option, option.AreaSquareMetres));
        }

        // "Take ALL the space from the band" means favour the largest
        // safe, matching patch, not the former smallest arbitrary nudge.
        bool entire = Regex.IsMatch(goal,
            @"\b(?:all\s+(?:of\s+)?(?:the\s+)?(?:space|band|strip|area)|entire\s+(?:thin\s+)?(?:band|strip|area|space)|whole\s+(?:band|strip|area|space))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (entire && matched.Count > 0)
        {
            double maximum = matched.Max(m => m.Area);
            matched = matched.Where(m => m.Area >= maximum - Eps).ToList();
        }
        return matched.Select(m => m.Option).ToArray();
    }

    /// <summary>
    /// Compute the bounding rectangle of the *newly gained* area without
    /// guessing it from the proposed room's name or bounding box.
    /// Handles L-shaped/orthogonal unions by using every grid cell midpoint.
    /// </summary>
    public static bool TryGainedFootprint(BuildingDocument source,
        int floorIndex, AiLayoutOption option,
        out RoomDefinition gainingRoom, out (double X0, double Y0,
            double X1, double Y1) gained)
    {
        gainingRoom = new RoomDefinition();
        gained = default;
        var before = BuildingStoreys.GetFloor(source, floorIndex);
        var after = BuildingStoreys.GetFloor(option.Suggestion.Building, floorIndex);
        foreach (var original in before.Rooms)
        {
            var modified = after.Rooms.FirstOrDefault(r => r.Id == original.Id);
            if (modified is null ||
                PolygonGeometry.Area(modified.Outline) -
                PolygonGeometry.Area(original.Outline) < 0.25)
                continue;

            var xs = original.Outline.Concat(modified.Outline)
                .Select(p => p.X).Distinct().Order().ToArray();
            var ys = original.Outline.Concat(modified.Outline)
                .Select(p => p.Y).Distinct().Order().ToArray();
            bool found = false;
            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            for (int x = 0; x < xs.Length - 1; x++)
            for (int y = 0; y < ys.Length - 1; y++)
            {
                var mid = new Point2(
                    (xs[x] + xs[x + 1]) / 2,
                    (ys[y] + ys[y + 1]) / 2);
                if (!PolygonGeometry.ContainsPoint(modified.Outline, mid) ||
                    PolygonGeometry.ContainsPoint(original.Outline, mid))
                    continue;
                found = true;
                x0 = Math.Min(x0, xs[x]);
                x1 = Math.Max(x1, xs[x + 1]);
                y0 = Math.Min(y0, ys[y]);
                y1 = Math.Max(y1, ys[y + 1]);
            }
            if (!found) continue;
            gainingRoom = original;
            gained = (x0, y0, x1, y1);
            return true;
        }
        return false;
    }

    private static bool MatchesSide(
        (double X0, double Y0, double X1, double Y1) patch,
        IReadOnlyList<Point2> reference, string direction)
    {
        double x0 = reference.Min(p => p.X);
        double x1 = reference.Max(p => p.X);
        double y0 = reference.Min(p => p.Y);
        double y1 = reference.Max(p => p.Y);
        return direction switch
        {
            "above" => patch.Y1 <= y0 + Eps,
            "below" => patch.Y0 >= y1 - Eps,
            "left" => patch.X1 <= x0 + Eps,
            "right" => patch.X0 >= x1 - Eps,
            _ => true
        };
    }
}
