using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A non-destructive generator: a suggestion is a detached, validated
/// building document. No caller's floor, stairs or existing rooms are
/// modified until an explicit acceptance.
/// </summary>
public sealed record BuildingSuggestion(
    BuildingDocument Building, int Seed, IReadOnlyList<int> FloorIndexes,
    string Description)
{
    public BuildingSuggestion Copy() => new(
        BuildingStoreys.Copy(Building), Seed, FloorIndexes.ToArray(), Description);
}

public static class BuildingGenerator
{
    private static readonly string[] RoomTypes =
    [
        "Bedroom", "Office", "Storeroom", "Kitchen",
        "Bathroom", "Workshop", "Living room", "Hallway"
    ];

    /// <summary>
    /// Suggest a completely new draft building with one to four floors.
    /// This never overwrites the active building until explicitly accepted.
    /// </summary>
    public static bool TrySuggestWholeBuilding(int floorCount, int seed,
        out BuildingSuggestion suggestion, out string reason)
    {
        var blank = new BuildingDocument();
        suggestion = new BuildingSuggestion(blank, seed, [], "");
        if (floorCount is < 1 or > 4)
        {
            reason = "Choose between one and four floors.";
            return false;
        }

        // First establish a perimeter; subsequent floors inherit the exact
        // footprint but get separately proposed rooms.
        if (!TrySuggest(blank, [0], seed, out var first, out reason))
            return false;
        var basis = BuildingStoreys.Copy(first.Building);
        for (int i = 1; i < floorCount; i++)
        {
            if (!BuildingStoreys.TryAddFloor(basis, out var next, out reason))
                return false;
            basis = next;
        }
        // Keep the generated ground-floor rooms and fill upper floors.
        BuildingDocument result = basis;
        if (floorCount > 1)
        {
            if (!TrySuggest(basis, Enumerable.Range(1, floorCount - 1).ToArray(),
                    unchecked(seed + 1), out var upper, out reason))
                return false;
            result = upper.Building;
        }
        string label = $"Whole building ({floorCount} floor(s)), seed {seed}: " +
            $"{Enumerable.Range(0, floorCount).Sum(i => BuildingStoreys.GetFloor(result, i).Rooms.Count)} rooms";
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(result), seed,
            Enumerable.Range(0, floorCount).ToArray(), label);
        reason = "";
        return true;
    }

    public static bool TrySuggest(BuildingDocument source,
        IReadOnlyCollection<int> selectedFloors, int seed,
        out BuildingSuggestion suggestion, out string reason)
    {
        var original = BuildingStoreys.Copy(source);
        var proposed = BuildingStoreys.Copy(source);
        suggestion = new BuildingSuggestion(original, seed, [], "");
        reason = "";

        int floors = BuildingStoreys.FloorCount(source);
        int[] indices = selectedFloors.Distinct().OrderBy(i => i).ToArray();
        if (indices.Length == 0)
        {
            reason = "Choose at least one floor for generation.";
            return false;
        }
        if (indices.Any(i => i < 0 || i >= floors))
        {
            reason = "A selected floor no longer exists.";
            return false;
        }

        var random = new Random(seed);
        int addedRooms = 0;
        int addedFootprints = 0;
        foreach (int index in indices)
        {
            var floor = BuildingStoreys.GetFloor(proposed, index);
            if (floor.Perimeter.Count == 0)
            {
                // First-draft building generation: start with a usable
                // rectangular outline, then subdivide it into rooms.
                double width = random.Next(22, 37) * 0.5;
                double depth = random.Next(16, 29) * 0.5;
                floor.Perimeter =
                [
                    new Point2(0, 0), new Point2(width, 0),
                    new Point2(width, depth), new Point2(0, depth)
                ];
                BuildingStoreys.SetFloor(proposed, index, floor);
                if (!BuildingStoreys.Validate(proposed, out reason))
                {
                    reason = "The generated outline could not fit this building: " + reason;
                    return false;
                }
                addedFootprints++;
            }

            double minX = floor.Perimeter.Min(p => p.X);
            double maxX = floor.Perimeter.Max(p => p.X);
            double minY = floor.Perimeter.Min(p => p.Y);
            double maxY = floor.Perimeter.Max(p => p.Y);
            int left = (int)Math.Ceiling(minX * 2);
            int right = (int)Math.Floor(maxX * 2);
            int top = (int)Math.Ceiling(minY * 2);
            int bottom = (int)Math.Floor(maxY * 2);

            // A preview adds up to three useful rectangular rooms per
            // selected floor. Future grammar/routing passes will provide
            // hallway connectivity and furnishing; this pass does not
            // pretend that rooms are fully architecturally connected.
            int targetRooms = 1 + random.Next(3);
            for (int roomNumber = 0; roomNumber < targetRooms; roomNumber++)
            {
                bool placed = false;
                if (right - left < 4 || bottom - top < 4) break;
                for (int attempt = 0; attempt < 180; attempt++)
                {
                    int width = random.Next(4, Math.Min(15, right - left) + 1);
                    int height = random.Next(4, Math.Min(13, bottom - top) + 1);
                    int x = random.Next(left, right - width + 1);
                    int y = random.Next(top, bottom - height + 1);
                    List<Point2> shape =
                    [
                        new(x / 2.0, y / 2.0),
                        new((x + width) / 2.0, y / 2.0),
                        new((x + width) / 2.0, (y + height) / 2.0),
                        new(x / 2.0, (y + height) / 2.0)
                    ];
                    // Keep generated functional rooms clear of physical
                    // staircases and upper-floor slab openings; stairwells
                    // can be explicitly created by later circulation passes.
                    bool reservesStair = proposed.Staircases.Any(stair =>
                    {
                        bool lower = stair.LowerFloorIndex == index;
                        bool upper = stair.LowerFloorIndex + 1 == index;
                        if (!lower && !upper) return false;
                        var reservation = upper
                            ? BuildingStoreys.StairOpeningFootprint(stair)
                            : BuildingStoreys.StairFootprint(stair);
                        return PolygonGeometry.ContainsPoint(shape, stair.Centre) ||
                            shape.Any(p => PolygonGeometry.ContainsPoint(reservation, p));
                    });
                    if (reservesStair) continue;

                    string type = RoomTypes[random.Next(RoomTypes.Length)];
                    var room = new RoomDefinition
                    {
                        Name = $"{type} {floor.Rooms.Count + 1}",
                        Type = type,
                        Outline = shape
                    };
                    var nextFloor = BuildingStoreys.GetFloor(proposed, index);
                    nextFloor.Rooms.Add(room);
                    var next = BuildingStoreys.Copy(proposed);
                    BuildingStoreys.SetFloor(next, index, nextFloor);
                    if (!BuildingStoreys.Validate(next, out _)) continue;

                    proposed = next;
                    floor = nextFloor;
                    addedRooms++;
                    placed = true;
                    break;
                }
                if (!placed) break;
            }
        }

        if (addedFootprints == 0 && addedRooms == 0)
        {
            reason = "No valid additional rooms fit the selected floors. Try a different floor, enlarge the perimeter, or remove an existing room.";
            return false;
        }
        if (!BuildingStoreys.Validate(proposed, out reason))
            return false;

        string floorSummary = string.Join(", ", indices.Select(BuildingStoreys.FloorLabel));
        string description = $"Seed {seed}: {addedFootprints} new perimeter(s), " +
            $"{addedRooms} room(s) on {floorSummary}";
        suggestion = new BuildingSuggestion(proposed, seed, indices, description);
        reason = "";
        return true;
    }
}
