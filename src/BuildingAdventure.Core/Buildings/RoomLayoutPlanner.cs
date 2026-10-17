using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Building purpose influences useful room roles; it never overrides
/// geometry or the user's existing room definitions.
/// </summary>
public enum BuildingUse
{
    Residence,
    Inn,
    Shop,
    Dungeon,
    Mixed
}

/// <summary>
/// Partition complete strips on either side of a straight hallway instead
/// of scattering small random rectangles. All candidates are individually
/// validated and only newly proposed rooms are changed.
/// </summary>
public static class RoomLayoutPlanner
{
    public static int FillHallFrontage(BuildingDocument source, int floorIndex,
        Guid hallId, BuildingUse use, Random random,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Id == hallId);
        if (hall is null || hall.Outline.Count != 4 ||
            floor.Perimeter.Count < 4) return 0;

        const double eps = 1e-5;
        var x = hall.Outline.Select(p => p.X).ToArray();
        var y = hall.Outline.Select(p => p.Y).ToArray();
        double hallLeft = x.Min(), hallRight = x.Max();
        double hallTop = y.Min(), hallBottom = y.Max();
        bool horizontal = hallRight - hallLeft > hallBottom - hallTop;

        // A corridor placed diagonally or inside a concave shape must not
        // force rectangular rooms through an exterior corner. Candidates
        // below are still validated, but only axis-aligned hallways qualify.
        bool axisAligned = hall.Outline.All(p =>
            (Math.Abs(p.X - hallLeft) < eps || Math.Abs(p.X - hallRight) < eps) &&
            (Math.Abs(p.Y - hallTop) < eps || Math.Abs(p.Y - hallBottom) < eps));
        if (!axisAligned) return 0;

        double minX = floor.Perimeter.Min(p => p.X);
        double maxX = floor.Perimeter.Max(p => p.X);
        double minY = floor.Perimeter.Min(p => p.Y);
        double maxY = floor.Perimeter.Max(p => p.Y);
        int lengthLow = GridCeil(horizontal ? hallLeft : hallTop);
        int lengthHigh = GridFloor(horizontal ? hallRight : hallBottom);
        if (lengthHigh - lengthLow < 4) return 0;

        int added = 0;
        var bands = horizontal
            ? new (int Near, int Far)[]
            {
                (GridFloor(hallTop), GridCeil(minY)),
                (GridCeil(hallBottom), GridFloor(maxY))
            }
            : new (int Near, int Far)[]
            {
                (GridFloor(hallLeft), GridCeil(minX)),
                (GridCeil(hallRight), GridFloor(maxX))
            };

        foreach (var (near, far) in bands)
        {
            int depth = Math.Abs(near - far);
            if (depth < 4) continue; // avoid rooms under 2m deep
            int length = lengthHigh - lengthLow;
            int target = Math.Clamp((int)Math.Round(length / 9.0) +
                random.Next(-1, 2), length >= 16 ? 2 : 1, Math.Max(1, length / 5));
            var widths = SplitFrontage(length, target, random);

            int position = lengthLow;
            foreach (int width in widths)
            {
                int from = Math.Min(near, far);
                int to = Math.Max(near, far);
                List<Point2> outline = horizontal
                    ? Rectangle(position, from, position + width, to)
                    : Rectangle(from, position, to, position + width);
                double area = PolygonGeometry.Area(outline);
                var current = BuildingStoreys.GetFloor(updated, floorIndex);
                string type = ChooseRoomType(use, floorIndex, area,
                    current.Rooms, random);
                var candidate = BuildingStoreys.Copy(updated);
                var edited = BuildingStoreys.GetFloor(candidate, floorIndex);
                edited.Rooms.Add(new RoomDefinition
                {
                    Name = $"{type} {edited.Rooms.Count + 1}",
                    Type = type,
                    Outline = outline
                });
                BuildingStoreys.SetFloor(candidate, floorIndex, edited);
                if (BuildingStoreys.Validate(candidate, out _))
                {
                    updated = candidate;
                    added++;
                }
                position += width;
            }
        }
        return added;
    }

    public static string ChooseRoomType(BuildingUse use, int floorIndex,
        double squareMetres, IReadOnlyList<RoomDefinition> rooms, Random random)
    {
        string[] choices;
        if (use == BuildingUse.Dungeon)
            choices = squareMetres < 9
                ? ["Cell", "Guardroom", "Storage"]
                : ["Chamber", "Barracks", "Guardroom", "Vault"];
        else if (use == BuildingUse.Shop)
            choices = squareMetres < 9
                ? ["Storeroom", "Office", "Washroom"]
                : ["Shop floor", "Workshop", "Stockroom", "Office"];
        else if (use == BuildingUse.Inn)
            choices = floorIndex > 0
                ? squareMetres < 9
                    ? ["Guest room", "Washroom"]
                    : ["Guest room", "Suite", "Linen store"]
                : squareMetres < 9
                    ? ["Pantry", "Washroom", "Storage"]
                    : ["Common room", "Kitchen", "Dining room", "Guest room"];
        else if (floorIndex < 0)
            choices = squareMetres < 9
                ? ["Pantry", "Storage", "Utility room"]
                : ["Cellar", "Workshop", "Storage"];
        else if (use == BuildingUse.Residence)
            choices = floorIndex > 0
                ? squareMetres < 9
                    ? ["Bathroom", "Closet", "Storage"]
                    : ["Bedroom", "Study", "Bedroom"]
                : squareMetres < 9
                    ? ["Bathroom", "Pantry", "Laundry"]
                    : squareMetres >= 23
                        ? ["Living room", "Kitchen", "Dining room"]
                        : ["Bedroom", "Kitchen", "Dining room", "Study"];
        else
            choices = squareMetres < 9
                ? ["Bathroom", "Storage", "Pantry"]
                : ["Office", "Living room", "Workshop", "Bedroom", "Kitchen"];

        // Prefer a missing role to repeating "Kitchen 2" or "Bathroom 3".
        int[] absent = Enumerable.Range(0, choices.Length)
            .Where(i => !rooms.Any(r =>
                r.Type.Equals(choices[i], StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var candidates = absent.Length > 0 ? absent : Enumerable.Range(0, choices.Length).ToArray();
        return choices[candidates[random.Next(candidates.Length)]];
    }

    public static double UnassignedArea(BuildingDocument floor)
    {
        double total = PolygonGeometry.Area(floor.Perimeter);
        double covered = floor.Rooms.Sum(r => PolygonGeometry.Area(r.Outline));
        return Math.Max(0, total - covered);
    }

    private static List<int> SplitFrontage(int cells, int requested, Random random)
    {
        int count = Math.Clamp(requested, 1, Math.Max(1, cells / 5));
        var segments = new List<int>(count);
        int remaining = cells;
        for (int i = 0; i < count; i++)
        {
            int left = count - i;
            int width = left == 1 ? remaining :
                Math.Clamp((int)Math.Round((double)remaining / left) +
                    random.Next(-2, 3), 5, remaining - 5 * (left - 1));
            segments.Add(width);
            remaining -= width;
        }
        return segments;
    }

    private static int GridFloor(double value) => (int)Math.Floor(value * 2 + 1e-7);
    private static int GridCeil(double value) => (int)Math.Ceiling(value * 2 - 1e-7);
    private static List<Point2> Rectangle(int x0, int y0, int x1, int y1) =>
    [
        new Point2(x0 / 2.0, y0 / 2.0),
        new Point2(x1 / 2.0, y0 / 2.0),
        new Point2(x1 / 2.0, y1 / 2.0),
        new Point2(x0 / 2.0, y1 / 2.0)
    ];
}
