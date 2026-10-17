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
        "Bathroom", "Workshop", "Living room", "Dining room"
    ];

    /// <summary>
    /// Suggest a completely new draft building with one to four floors.
    /// This never overwrites the active building until explicitly accepted.
    /// </summary>
    public static bool TrySuggestWholeBuilding(int floorCount, int seed,
        out BuildingSuggestion suggestion, out string reason) =>
        TrySuggestWholeBuilding(floorCount, seed,
            new GenerationOptions { Corridors = false, Doors = false },
            out suggestion, out reason);

    public static bool TrySuggestWholeBuilding(int floorCount, int seed,
        GenerationOptions options, out BuildingSuggestion suggestion, out string reason)
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
        if (options is null)
        {
            reason = "Generation options are required.";
            return false;
        }
        if (!options.Rooms && !options.Corridors)
        {
            reason = "A new whole-building draft needs Rooms or Corridors enabled " +
                "to establish its initial floor layout.";
            return false;
        }
        var stageOptions = options.Copy();
        stageOptions.Stairs = false;
        if (!TrySuggest(blank, [0], seed, stageOptions, out var first, out reason))
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
                    unchecked(seed + 1), stageOptions, out var upper, out reason))
                return false;
            result = upper.Building;
        }
        int stairs = 0;
        if (options.Stairs && floorCount > 1)
        {
            stairs = BuildingConnectivity.AddStairs(result,
                Enumerable.Range(0, floorCount).ToArray(), out var withStairs);
            result = withStairs;
        }
        string label = $"Whole building ({floorCount} floor(s)), seed {seed}: " +
            $"{Enumerable.Range(0, floorCount).Sum(i => BuildingStoreys.GetFloor(result, i).Rooms.Count)} rooms, " +
            $"{result.UpperFloors.Sum(f => f.Doors.Count) + result.Doors.Count} doors, " +
            $"{stairs} new stair flight(s)";
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(result), seed,
            Enumerable.Range(0, floorCount).ToArray(), label);
        reason = "";
        return true;
    }

    /// <summary>
    /// Accept precisely one room from a detached suggestion on one floor.
    /// Other suggested rooms and all other floors remain untouched.
    /// If the target floor has no perimeter, carry over its suggested
    /// footprint as a prerequisite to placing the room.
    /// </summary>
    public static bool TryAcceptRoom(BuildingDocument source,
        BuildingSuggestion suggestion, int floorIndex, Guid roomId,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "";
        if (!BuildingStoreys.HasFloor(source, floorIndex) ||
            !BuildingStoreys.HasFloor(suggestion.Building, floorIndex) ||
            !suggestion.FloorIndexes.Contains(floorIndex))
        {
            reason = "The selected floor is not part of this suggestion.";
            return false;
        }
        var proposedFloor = BuildingStoreys.GetFloor(suggestion.Building, floorIndex);
        var room = proposedFloor.Rooms.FirstOrDefault(r => r.Id == roomId);
        if (room is null)
        {
            reason = "The selected proposed room was not found.";
            return false;
        }
        var current = BuildingStoreys.GetFloor(updated, floorIndex);
        if (current.Rooms.Any(r => r.Id == room.Id))
        {
            reason = "This proposed room has already been accepted.";
            return false;
        }
        if (current.Perimeter.Count == 0)
            current.Perimeter = proposedFloor.Perimeter.ToList();
        current.Rooms.Add(room.Copy());
        BuildingStoreys.SetFloor(updated, floorIndex, current);
        return BuildingStoreys.Validate(updated, out reason);
    }

    public static bool TrySuggest(BuildingDocument source,
        IReadOnlyCollection<int> selectedFloors, int seed,
        out BuildingSuggestion suggestion, out string reason) =>
        TrySuggest(source, selectedFloors, seed,
            new GenerationOptions { Corridors = false, Doors = false },
            out suggestion, out reason);

    public static bool TrySuggest(BuildingDocument source,
        IReadOnlyCollection<int> selectedFloors, int seed, GenerationOptions options,
        out BuildingSuggestion suggestion, out string reason)
    {
        var original = BuildingStoreys.Copy(source);
        var proposed = BuildingStoreys.Copy(source);
        suggestion = new BuildingSuggestion(original, seed, [], "");
        reason = "";
        if (options is null || !options.Any)
        {
            reason = "Select at least one feature: rooms, corridors, doors, windows or stairs.";
            return false;
        }

        int floors = BuildingStoreys.FloorCount(source);
        int[] indices = selectedFloors.Distinct().OrderBy(i => i).ToArray();
        if (indices.Length == 0)
        {
            reason = "Choose at least one floor for generation.";
            return false;
        }
        if (indices.Any(i => !BuildingStoreys.HasFloor(source, i)))
        {
            reason = "A selected floor no longer exists.";
            return false;
        }

        var random = new Random(seed);
        int addedRooms = 0;
        int addedCorridors = 0;
        int addedDoors = 0;
        int addedWindows = 0;
        int addedFootprints = 0;
        int addedStairs = 0;
        var skippedFloors = new List<int>();
        var roomsPerFloor = new List<string>();
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

            Guid corridorId = Guid.Empty;
            if (options.Corridors &&
                BuildingConnectivity.TryAddCorridor(proposed, index,
                    out var withCorridor, out corridorId))
            {
                proposed = withCorridor;
                floor = BuildingStoreys.GetFloor(proposed, index);
                addedCorridors++;
            }
            var corridor = corridorId != Guid.Empty
                ? floor.Rooms.FirstOrDefault(room => room.Id == corridorId)
                : options.Corridors
                    ? floor.Rooms.FirstOrDefault(r =>
                        r.Type.Equals("Hallway", StringComparison.OrdinalIgnoreCase))
                    : null;

            if (options.Rooms)
            {
            double minX = floor.Perimeter.Min(p => p.X);
            double maxX = floor.Perimeter.Max(p => p.X);
            double minY = floor.Perimeter.Min(p => p.Y);
            double maxY = floor.Perimeter.Max(p => p.Y);
            int left = (int)Math.Ceiling(minX * 2);
            int right = (int)Math.Floor(maxX * 2);
            int top = (int)Math.Ceiling(minY * 2);
            int bottom = (int)Math.Floor(maxY * 2);

            // First try varied room proportions; if random placement misses
            // remaining space, enumerate smaller candidate rectangles on the
            // same half-metre lattice. This avoids "zero rooms" where a
            // geometrically usable area still exists.
            int thisFloorRooms = 0;
            if (corridor is not null)
            {
                int planned = RoomLayoutPlanner.FillHallFrontage(
                    proposed, index, corridor.Id, options.Purpose,
                    random, out var arranged);
                if (planned > 0)
                {
                    proposed = arranged;
                    floor = BuildingStoreys.GetFloor(proposed, index);
                    addedRooms += planned;
                    thisFloorRooms += planned;
                }
            }
            int targetRooms = thisFloorRooms > 0 ? 0 : 1 + random.Next(3);
            bool TryPlace(int x, int y, int width, int height)
            {
                List<Point2> shape =
                [
                    new(x / 2.0, y / 2.0),
                    new((x + width) / 2.0, y / 2.0),
                    new((x + width) / 2.0, (y + height) / 2.0),
                    new(x / 2.0, (y + height) / 2.0)
                ];

                bool reservesStair = proposed.Staircases.Any(stair =>
                {
                    bool lower = stair.LowerFloorIndex == index;
                    bool upper = stair.LowerFloorIndex + 1 == index;
                    if (!lower && !upper) return false;
                    var reservation = upper
                        ? BuildingStoreys.StairOpeningFootprint(stair)
                        : BuildingStoreys.StairFootprint(stair);
                    // The room and stair reservation may overlap with no
                    // vertices within the other; test centre and crossings.
                    return FloorGeometry.IsWithinPolygon(reservation, shape) ||
                        FloorGeometry.IsWithinPolygon(shape, reservation) ||
                        shape.Any(p => PolygonGeometry.ContainsPoint(reservation, p)) ||
                        reservation.Any(p => PolygonGeometry.ContainsPoint(shape, p)) ||
                        PolygonGeometry.ContainsPoint(shape, stair.Centre);
                });
                if (reservesStair) return false;
                if (corridor is not null &&
                    !BuildingConnectivity.ShareWall(shape, corridor.Outline, 1.1))
                    return false;

                var nextFloor = BuildingStoreys.GetFloor(proposed, index);
                string type = RoomLayoutPlanner.ChooseRoomType(
                    options.Purpose, index, width * height / 4.0,
                    nextFloor.Rooms, random);
                nextFloor.Rooms.Add(new RoomDefinition
                {
                    Name = $"{type} {nextFloor.Rooms.Count + 1}",
                    Type = type,
                    Outline = shape
                });
                var next = BuildingStoreys.Copy(proposed);
                BuildingStoreys.SetFloor(next, index, nextFloor);
                if (!BuildingStoreys.Validate(next, out _)) return false;
                proposed = next;
                floor = nextFloor;
                addedRooms++;
                thisFloorRooms++;
                return true;
            }

            for (int roomNumber = 0; roomNumber < targetRooms; roomNumber++)
            {
                if (right - left < 2 || bottom - top < 2) break;
                bool placed = false;
                if (right - left >= 4 && bottom - top >= 4)
                {
                    for (int attempt = 0; attempt < 220; attempt++)
                    {
                        int width = random.Next(4, Math.Min(15, right - left) + 1);
                        int height = random.Next(4, Math.Min(13, bottom - top) + 1);
                        int x = random.Next(left, right - width + 1);
                        int y = random.Next(top, bottom - height + 1);
                        if (!TryPlace(x, y, width, height)) continue;
                        placed = true;
                        break;
                    }
                }
                if (!placed && thisFloorRooms == 0)
                {
                    // Deterministic fallback: large first, then down to
                    // a 1m x 1m minimum. More than one rectangle shape is
                    // considered so a narrow strip can still be filled.
                    int[] sizes = [8, 6, 4, 3, 2];
                    foreach (int width in sizes.Where(w => w <= right - left))
                    {
                        foreach (int height in sizes.Where(h => h <= bottom - top))
                        {
                            for (int y = top; y + height <= bottom && !placed; y++)
                            for (int x = left; x + width <= right; x++)
                            {
                                if (!TryPlace(x, y, width, height)) continue;
                                placed = true;
                                break;
                            }
                            if (placed) break;
                        }
                        if (placed) break;
                    }
                }
                if (!placed) break;
            }

            if (thisFloorRooms == 0) skippedFloors.Add(index);
            roomsPerFloor.Add($"{BuildingStoreys.FloorLabel(index)}: {thisFloorRooms} room(s)");
            }
            if (options.Doors)
            {
                addedDoors += BuildingConnectivity.AddDoors(
                    proposed, index, out var withDoors);
                proposed = withDoors;
            }
            if (options.Windows)
            {
                addedWindows += BuildingConnectivity.AddWindows(
                    proposed, index, out var withWindows);
                proposed = withWindows;
            }
        }
        if (options.Stairs)
        {
            addedStairs = BuildingConnectivity.AddStairs(
                proposed, indices, out var withStairs);
            proposed = withStairs;
        }

        if (addedRooms == 0 && addedCorridors == 0 &&
            addedDoors == 0 && addedWindows == 0 && addedStairs == 0)
        {
            reason = "No valid new features fit the selected floors and settings. " +
                "Try a different seed, smaller rooms, additional floor space, " +
                "or enable different features.";
            return false;
        }
        if (!BuildingStoreys.Validate(proposed, out reason))
            return false;

        int roomsWithoutDoors = options.Doors ? indices.Sum(i =>
            BuildingConnectivity.CountRoomsWithoutDoors(proposed, i)) : 0;
        var areaSummary = string.Join("; ", indices.Select(i =>
        {
            var f = BuildingStoreys.GetFloor(proposed, i);
            double total = PolygonGeometry.Area(f.Perimeter);
            double remaining = RoomLayoutPlanner.UnassignedArea(f);
            double coverage = total > 0 ? (total - remaining) / total * 100 : 0;
            return $"{BuildingStoreys.FloorLabel(i)} {coverage:0.#}% assigned, " +
                $"{remaining:0.#} m² unused";
        }));
        string floorSummary = string.Join(", ", indices.Select(BuildingStoreys.FloorLabel));
        string description = $"Seed {seed}: {addedFootprints} new perimeter(s), " +
            $"{addedRooms} room(s), {addedCorridors} corridor(s), " +
            $"{addedDoors} door(s), {addedWindows} window(s), " +
            $"{addedStairs} stair flight(s) across {floorSummary}. " +
            (options.Doors && roomsWithoutDoors > 0 ?
                $"{roomsWithoutDoors} room(s) still without door access. " : "") +
            string.Join("; ", roomsPerFloor) + ". " + areaSummary +
            (skippedFloors.Count > 0 ? " | No rooms fit: " +
                string.Join(", ", skippedFloors.Select(BuildingStoreys.FloorLabel)) : "");
        suggestion = new BuildingSuggestion(proposed, seed, indices, description);
        reason = "";
        return true;
    }
}
