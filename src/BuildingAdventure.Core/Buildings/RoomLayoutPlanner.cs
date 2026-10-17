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
            // A domestic ground floor needs a real main living space, not
            // uniformly small frontage slices that cannot fit one.
            // Reserve that area before dividing the remaining frontage.
            var widths = ChooseFrontageWidths(length, depth, target,
                use, floorIndex,
                BuildingStoreys.GetFloor(updated, floorIndex).Rooms, random);

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
                    Name = NextRoomName(type, edited.Rooms),
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

    /// <summary>
    /// Choose useful rooms in architectural priority order, not in a random
    /// order which may omit the kitchen or create multiple living rooms.
    /// Repeatable accommodation roles (bedrooms, guest rooms and cells) may
    /// recur after the essential one-per-floor spaces have been allocated.
    /// This method never renames existing rooms.
    /// </summary>
    public static string ChooseRoomType(BuildingUse use, int floorIndex,
        double squareMetres, IReadOnlyList<RoomDefinition> rooms, Random random)
    {
        string[] priorities;
        string[] repeats;
        if (use == BuildingUse.Dungeon)
        {
            priorities = squareMetres < 9
                ? ["Cell", "Guardroom", "Storage"]
                : ["Guardroom", "Chamber", "Barracks", "Vault"];
            repeats = squareMetres < 9 ? ["Cell"] : ["Chamber", "Barracks"];
        }
        else if (use == BuildingUse.Inn)
        {
            if (floorIndex > 0)
            {
                priorities = squareMetres < 9
                    ? ["Washroom", "Guest room"]
                    : ["Guest room", "Linen store", "Suite"];
                repeats = squareMetres < 9 ? ["Guest room"] : ["Guest room", "Suite"];
            }
            else if (squareMetres < 9)
            {
                priorities = ["Pantry", "Washroom", "Storage"];
                repeats = ["Storage"];
            }
            else
            {
                priorities = squareMetres >= 23
                    ? ["Common room", "Kitchen", "Dining room", "Guest room"]
                    : ["Kitchen", "Dining room", "Guest room", "Storage"];
                repeats = ["Guest room"];
            }
        }
        else if (floorIndex < 0 && use != BuildingUse.Dungeon)
        {
            priorities = squareMetres < 9
                ? ["Pantry", "Storage", "Utility room"]
                : ["Cellar", "Workshop", "Storage"];
            repeats = ["Storage"];
        }
        else if (use == BuildingUse.Shop)
        {
            priorities = squareMetres < 9
                ? ["Washroom", "Storeroom", "Office"]
                : ["Shop floor", "Stockroom", "Workshop", "Office"];
            repeats = ["Stockroom"];
        }
        else if (use == BuildingUse.Residence)
        {
            if (floorIndex > 0)
            {
                priorities = squareMetres < 9
                    ? ["Bathroom", "Closet", "Storage"]
                    : ["Bedroom", "Study"];
                repeats = squareMetres < 9 ? ["Closet"] : ["Bedroom"];
            }
            else if (squareMetres < 9)
            {
                priorities = ["Bathroom", "Pantry", "Laundry"];
                repeats = ["Pantry"];
            }
            else if (squareMetres >= 23)
            {
                priorities = ["Living room", "Kitchen", "Dining room", "Study", "Bedroom"];
                repeats = ["Bedroom"];
            }
            else
            {
                priorities = ["Kitchen", "Dining room", "Bedroom", "Study"];
                repeats = ["Bedroom"];
            }
        }
        else
        {
            priorities = squareMetres < 9
                ? ["Bathroom", "Pantry", "Storage"]
                : ["Living room", "Kitchen", "Workshop", "Office", "Bedroom"];
            repeats = squareMetres < 9 ? ["Storage"] : ["Bedroom", "Office"];
        }

        foreach (string role in priorities)
            if (!rooms.Any(r => r.Type.Equals(role, StringComparison.OrdinalIgnoreCase)))
                return role;
        return repeats[random.Next(repeats.Length)];
    }

    /// <summary>Use the plain room name first and number only duplicates.</summary>
    public static string NextRoomName(string type, IReadOnlyList<RoomDefinition> rooms)
    {
        if (!rooms.Any(r => r.Name.Equals(type, StringComparison.OrdinalIgnoreCase)))
            return type;
        for (int number = 2; ; number++)
        {
            string candidate = $"{type} {number}";
            if (!rooms.Any(r => r.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    public static double UnassignedArea(BuildingDocument floor)
    {
        double total = PolygonGeometry.Area(floor.Perimeter);
        double covered = floor.Rooms.Sum(r => PolygonGeometry.Area(r.Outline));
        return Math.Max(0, total - covered);
    }

    private static List<int> ChooseFrontageWidths(int length, int depth,
        int target, BuildingUse use, int floorIndex,
        IReadOnlyList<RoomDefinition> existingRooms, Random random)
    {
        // Half-metre grid cells: a candidate with width W and depth D
        // covers W * D / 4 square metres. Reserve at least 23 m² for
        // one ground-floor residential living room, leaving >=2 m for
        // a small service room. Only do so on a sufficiently large
        // untouched frontage; irregular layouts keep the ordinary split.
        if (use == BuildingUse.Residence && floorIndex == 0 &&
            !existingRooms.Any(r => r.Type.Equals(
                "Living room", StringComparison.OrdinalIgnoreCase)) &&
            depth >= 5)
        {
            int minimumLivingWidth = (int)Math.Ceiling(92.0 / depth);
            if (length >= minimumLivingWidth + 4)
            {
                int livingWidth = Math.Clamp(
                    Math.Max(minimumLivingWidth, (int)Math.Round(length * 0.62)),
                    minimumLivingWidth, length - 4);
                int remainder = length - livingWidth;
                return [livingWidth, remainder];
            }
        }
        return SplitFrontage(length, target, random);
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
