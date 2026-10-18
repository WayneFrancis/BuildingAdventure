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
        GenerationOptions options, out BuildingSuggestion suggestion, out string reason,
        int gridFeet = 0)
    {
        var blank = new BuildingDocument { GridFeet = gridFeet };
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
        // Choose dimensions for the REQUESTED total storeys now. The
        // staging generator initially sees only ground floor and would
        // otherwise size a three-storey home as a single-room cottage.
        blank.Perimeter = MedievalFootprint(options.Purpose, floorCount,
            gridFeet, new Random(seed));
        if (!options.Rooms && !options.Corridors)
        {
            reason = "A new whole-building draft needs Rooms or Corridors enabled " +
                "to establish its initial floor layout.";
            return false;
        }
        // On a new multi-floor building, place a hallway on every level
        // first, reserve aligned stairwell rooms off that hallway, and only
        // then fill the remaining frontage. Placing stairs last had forced
        // them into the middle of the only unassigned corridor.
        bool reserveStairwells = options.Stairs && options.Corridors && floorCount > 1;
        var stageOptions = options.Copy();
        stageOptions.Stairs = false;
        stageOptions.Fireplaces = false;
        // Add windows only AFTER chimney placement, using actual full
        // window spans rather than testing only fireplace-centre distances.
        stageOptions.Windows = false;
        if (reserveStairwells)
        {
            stageOptions.Rooms = false;
            stageOptions.Doors = false;
            stageOptions.Windows = false;
        }
        if (!TrySuggest(blank, [0], seed, stageOptions, out var first, out reason))
            return false;
        var basis = BuildingStoreys.Copy(first.Building);
        for (int i = 1; i < floorCount; i++)
        {
            if (!BuildingStoreys.TryAddFloor(basis, out var next, out reason))
                return false;
            basis = next;
        }
        BuildingDocument result = basis;
        if (floorCount > 1)
        {
            if (!TrySuggest(basis, Enumerable.Range(1, floorCount - 1).ToArray(),
                    unchecked(seed + 1), stageOptions, out var upper, out reason))
                return false;
            result = upper.Building;
        }
        int stairs = 0;
        if (reserveStairwells)
        {
            if (!BuildingConnectivity.TryReserveStackedStairwell(result,
                    out var staged, out stairs, out reason))
                return false;
            result = staged;
            var fill = options.Copy();
            fill.Stairs = false;
            fill.Fireplaces = false;
            fill.Windows = false;
            if (fill.Rooms || fill.Doors || fill.Windows)
            {
                if (!TrySuggest(result, Enumerable.Range(0, floorCount).ToArray(),
                        unchecked(seed + 2), fill, out var furnished, out reason))
                    return false;
                result = furnished.Building;
            }
        }
        else if (options.Stairs && floorCount > 1)
        {
            stairs = BuildingConnectivity.AddStairs(result,
                Enumerable.Range(0, floorCount).ToArray(), out var withStairs);
            result = withStairs;
        }
        if (options.Stairs && floorCount > 1 &&
            result.Staircases.Count < floorCount - 1)
        {
            reason = "The generator could not connect every requested floor with a staircase. " +
                "No disconnected building was proposed.";
            return false;
        }
        // Reserve the stair geometry through room subdivision, then
        // remove the temporary enclosure BEFORE merging narrow passages
        // into circulation. Doing this in the opposite order can turn
        // the reserved rectangular hallway into an L before its bay
        // is merged, breaking whole-building generation.
        if (reserveStairwells)
        {
            // The temporary stair-bay enclosure is not a separate room.
            // Merge it into an open alcove of the hallway, so this space
            // is assigned and no spurious dividing wall / door remains.
            foreach (int index in BuildingStoreys.FloorIndexes(result))
            {
                if (!RoomLayoutPlanner.TryMergeStairBayIntoHall(result,
                        index, out var merged, out reason))
                    return false;
                result = merged;
            }
        }
        if (options.Rooms)
        {
            foreach (int level in BuildingStoreys.FloorIndexes(result))
            {
                RoomLayoutPlanner.ConsolidateGeneratedRooms(result, level,
                    Array.Empty<Guid>(), out var shaped);
                result = shaped;
            }
        }

        int chimneys = 0;
        if (options.Fireplaces &&
            BuildingConnectivity.TryAddChimney(result, out var withChimney, options.Purpose))
        {
            result = withChimney;
            chimneys = 1;
        }
        if (options.Windows)
        {
            foreach (int level in BuildingStoreys.FloorIndexes(result))
            {
                BuildingConnectivity.AddWindows(result, level, out var glazed);
                result = glazed;
            }
        }
        string label = $"Whole building ({floorCount} floor(s)), seed {seed}: " +
            $"{Enumerable.Range(0, floorCount).Sum(i => BuildingStoreys.GetFloor(result, i).Rooms.Count)} rooms, " +
            $"{result.UpperFloors.Sum(f => f.Doors.Count) + result.Doors.Count} doors, " +
            $"{stairs} new stair flight(s), {chimneys} chimney stack(s)";
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(result), seed,
            Enumerable.Range(0, floorCount).ToArray(), label);
        reason = "";
        return true;
    }

    /// <summary>
    /// English/Welsh vernacular-inspired size bands, NOT modern house
    /// dimensions. A single-storey cottage differs substantially from a
    /// prosperous multi-storey residence, an inn, or a trading workshop.
    /// Some footprints include a modest wing instead of a rectangle.
    /// Internal layouts and fireplace placement are handled separately.
    /// </summary>
    public static List<Point2> MedievalFootprint(BuildingUse use,
        int floors, int gridFeet, Random random)
    {
        (double lowW, double highW, double lowD, double highD) = use switch
        {
            BuildingUse.Residence when floors == 1 => (7, 10, 6, 9),
            BuildingUse.Residence => (8.5, 11.5, 8, 11),
            BuildingUse.Inn => (12, 18, 9, 13),
            BuildingUse.Shop => (6.5, 10, 8, 12),
            BuildingUse.Dungeon => (11, 19, 9, 15),
            _ => (9, 15, 8, 12)
        };

        // The wall generator uses metres internally. For imperial maps
        // *all* external vertices snap to exact 5/10-foot squares.
        double cell = gridFeet is 5 or 10 ? gridFeet * 0.3048 : 0.5;
        double width = Math.Max(cell * 2,
            Math.Round((lowW + random.NextDouble() * (highW - lowW)) / cell) * cell);
        double depth = Math.Max(cell * 2,
            Math.Round((lowD + random.NextDouble() * (highD - lowD)) / cell) * cell);

        // Optional rear-side wing; its low end does not overlap the
        // central corridor. Modest residences mostly remain rectangular.
        bool wing = (use != BuildingUse.Residence || floors > 1) &&
            random.Next(4) == 0 && width >= 3 * cell && depth >= 3 * cell;
        if (!wing)
            return [new(0, 0), new(width, 0), new(width, depth), new(0, depth)];

        double wingWidth = gridFeet == 10 ? cell :
            Math.Max(cell, Math.Round(2.0 / cell) * cell);
        double wingStart = Math.Round(depth * 0.6 / cell) * cell;
        wingStart = Math.Clamp(wingStart, cell, depth - cell);
        return
        [
            new(0, 0), new(width, 0),
            new(width, wingStart), new(width + wingWidth, wingStart),
            new(width + wingWidth, depth), new(0, depth)
        ];
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

    /// <summary>
    /// Explicit regeneration: keep the hand-drawn perimeter and unchanged
    /// floors, but replace the selected floors' room/opening drafts. The
    /// original building is never mutated or committed automatically.
    /// Stairs/chimneys are retained and remain geometry constraints.
    /// </summary>
    public static bool TryRegenerate(BuildingDocument source,
        IReadOnlyCollection<int> selectedFloors, int seed, GenerationOptions options,
        out BuildingSuggestion suggestion, out string reason)
    {
        var original = BuildingStoreys.Copy(source);
        suggestion = new BuildingSuggestion(original, seed, [], "");
        var indices = selectedFloors.Distinct().OrderBy(i => i).ToArray();
        if (indices.Length == 0 || indices.Any(i => !BuildingStoreys.HasFloor(source, i)))
        {
            reason = "Select one or more existing floors to regenerate.";
            return false;
        }
        var cleared = BuildingStoreys.Copy(source);
        foreach (int index in indices)
        {
            var floor = BuildingStoreys.GetFloor(cleared, index);
            floor.Rooms.Clear();
            floor.Doors.Clear();
            floor.Windows.Clear();
            BuildingStoreys.SetFloor(cleared, index, floor);
        }
        // Existing flues remain, but deleted/renamed room IDs cannot
        // continue representing hearth faces after room regeneration.
        foreach (var chimney in cleared.Chimneys.Where(ch =>
            indices.Contains(ch.HearthFloorIndex)))
        {
            chimney.HearthRoomIds.Clear();
            chimney.OtherHearthCentre = null;
        }
        if (!BuildingStoreys.Validate(cleared, out reason))
        {
            reason = "Cannot regenerate while keeping existing stairs/chimneys: " + reason;
            return false;
        }
        if (!TrySuggest(cleared, indices, seed, options, out var generated, out reason))
            return false;
        suggestion = new BuildingSuggestion(generated.Building, seed, indices,
            "REPLACEMENT DRAFT — selected rooms, doors and windows will be replaced. " +
            generated.Description);
        return true;
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
        int absorbedHallStrips = 0;
        int addedDoors = 0;
        int addedWindows = 0;
        int addedFootprints = 0;
        int addedStairs = 0;
        int addedChimneys = 0;
        var skippedFloors = new List<int>();
        var roomsPerFloor = new List<string>();

        // When two existing levels are being suggested together, reserve
        // their vertical connection BEFORE subdividing the unallocated
        // space. The previous rooms-first order could fill every usable
        // landing bay and then fail to add stairs, discarding both proposed
        // floor layouts. Reuse any existing hallways and retain the normal
        // room pass below for all other features.
        bool hasAdjacentFloors = indices.Any(i => indices.Contains(i + 1));
        string stairPlanningReason = "";
        if (options.Stairs && options.Rooms && options.Corridors &&
            hasAdjacentFloors &&
            indices.All(i => BuildingStoreys.GetFloor(proposed, i).Perimeter.Count >= 3))
        {
            // Two empty levels MUST be laid out together, not given
            // independently centred hallways. On a smaller upper floor,
            // separately valid hallways often miss one another and cannot
            // support a single common staircase and both landings.
            foreach (int lower in indices.Where(i => indices.Contains(i + 1)))
            {
                if (BuildingStoreys.GetFloor(proposed, lower).Rooms.Count != 0 ||
                    BuildingStoreys.GetFloor(proposed, lower + 1).Rooms.Count != 0 ||
                    proposed.Staircases.Any(s => s.LowerFloorIndex == lower))
                    continue;
                if (BuildingConnectivity.TryAddAlignedHallwaysAndStairs(
                    proposed, lower, out var aligned, out var detail))
                {
                    proposed = aligned;
                    addedCorridors += 2;
                    addedStairs++;
                }
                else
                    stairPlanningReason = detail;
            }

            foreach (int index in indices)
            {
                var floor = BuildingStoreys.GetFloor(proposed, index);
                if (floor.Rooms.Any(r => r.Type == "Hallway")) continue;
                if (BuildingConnectivity.TryAddCorridor(proposed, index,
                        out var withHall, out _))
                {
                    proposed = withHall;
                    addedCorridors++;
                }
            }

            addedStairs += BuildingConnectivity.AddStairs(
                proposed, indices, out var reservedStairs);
            proposed = reservedStairs;
        }
        foreach (int index in indices)
        {
            var floor = BuildingStoreys.GetFloor(proposed, index);
            if (floor.Perimeter.Count == 0)
            {
                // First-draft building generation: start with a usable
                // rectangular outline, then subdivide it into rooms.
                floor.Perimeter = MedievalFootprint(options.Purpose, floors,
                    proposed.GridFeet, random);
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
            // Deal with a long shallow band alongside the hallway BEFORE
            // the residual-filling pass can turn it into a 6ft x 25ft
            // "bedroom". It belongs to circulation, not accommodation.
            // Temporary stairwell bays used by whole-building generation
            // must stay rectangular until their own merge is complete.
            if (corridor is not null &&
                !BuildingStoreys.GetFloor(proposed, index).Rooms
                    .Any(r => r.Type == "Stairwell"))
            {
                absorbedHallStrips += RoomLayoutPlanner.AbsorbNarrowPerimeterGapsIntoHall(
                    proposed, index, out var withShallowPassage);
                proposed = withShallowPassage;
                floor = BuildingStoreys.GetFloor(proposed, index);
                corridor = floor.Rooms.FirstOrDefault(r => r.Id == corridor.Id);
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
                string type = RoomLayoutPlanner.ChooseRoomTypeForOutline(
                    options.Purpose, index, width * height / 4.0,
                    nextFloor.Rooms, shape, random);
                nextFloor.Rooms.Add(new RoomDefinition
                {
                    Name = RoomLayoutPlanner.NextRoomName(type, nextFloor.Rooms),
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

            // Fill any accessible leftover orthogonal pockets (including
            // L-shaped wings and custom-drawn non-metric wall offsets).
            // The residual pass preserves all existing room geometry.
            int filled = RoomLayoutPlanner.FillUnassignedRegions(
                proposed, index, options.Purpose, random, out var completed);
            if (filled > 0)
            {
                proposed = completed;
                addedRooms += filled;
                thisFloorRooms += filled;
            }
            // A separate narrow-strip pass stretches only NEW rooms to
            // nearby external walls, without modifying hand-drawn rooms.
            RoomLayoutPlanner.ExtendSuggestedRoomsToExterior(
                original, proposed, index, out var seamFree);
            proposed = seamFree;
            if (thisFloorRooms == 0) skippedFloors.Add(index);
            roomsPerFloor.Add($"{BuildingStoreys.FloorLabel(index)}: {thisFloorRooms} room(s)");
            }
            // Absorb the 0.762m L-wing gap only during the final room
            // layout. The earlier corridors-only staging phase of a new
            // whole building must keep rectangular halls intact: the
            // reserved-stair-bay planner requires those rectangles before
            // it can locate and stack flights.
            if (options.Rooms && !BuildingStoreys.GetFloor(proposed, index)
                .Rooms.Any(r => r.Type == "Stairwell"))
            {
                // Reserved stairwell rooms are temporary until the
                // whole-building finisher merges them into the hallway.
                // Widening that hall first makes its outline concave and
                // invalidates the rectangular stair-bay merge contract.
                absorbedHallStrips += RoomLayoutPlanner.AbsorbNarrowPerimeterGapsIntoHall(
                    proposed, index, out var withConnectedHall);
                proposed = withConnectedHall;
            }
            if (options.Doors)
            {
                addedDoors += BuildingConnectivity.AddDoors(
                    proposed, index, out var withDoors);
                proposed = withDoors;
            }
        }
        if (options.Stairs)
        {
            addedStairs += BuildingConnectivity.AddStairs(
                proposed, indices, out var withStairs);
            proposed = withStairs;
            // A requested multi-floor staircase is a connectivity
            // requirement, not a best-effort decoration. Do not return
            // a seemingly successful building with inaccessible levels.
            var checkedSet = indices.ToHashSet();
            int[] inaccessible = indices.Where(i => checkedSet.Contains(i + 1) &&
                BuildingStoreys.HasFloor(proposed, i + 1) &&
                !proposed.Staircases.Any(s => s.LowerFloorIndex == i)).ToArray();
            if (inaccessible.Length > 0)
            {
                reason = "No unobstructed staircase with landings fits between " +
                    string.Join(", ", inaccessible.Select(i =>
                        BuildingStoreys.FloorLabel(i) + " and " +
                        BuildingStoreys.FloorLabel(i + 1))) + ". " +
                    (string.IsNullOrEmpty(stairPlanningReason)
                        ? "Try a wider common area or adjust an existing hallway."
                        : stairPlanningReason);
                return false;
            }
        }
        // Last, consolidate newly generated sliver rooms. Multiple
        // rectangles labelled Passage beside one hall are not useful,
        // and small offset service wings make better L-shaped rooms than
        // tiny isolated rectangles. Never merge away pre-existing rooms.
        if (options.Rooms)
        {
            foreach (int index in indices)
            {
                // A temporary stairwell bay is still required by the
                // whole-building merger: preserve its rectangular hall.
                if (BuildingStoreys.GetFloor(proposed, index).Rooms
                    .Any(r => r.Type == "Stairwell")) continue;
                var protectedRooms = BuildingStoreys.GetFloor(original, index)
                    .Rooms.Select(r => r.Id).ToArray();
                RoomLayoutPlanner.ConsolidateGeneratedRooms(
                    proposed, index, protectedRooms, out var withMergedRooms);
                proposed = withMergedRooms;
                // The merged passages may otherwise inflate an L-shaped
                // footprint's hallway to more than half its usable area.
                // Only newly generated hallways are eligible for reclamation.
                int reclaimed = RoomLayoutPlanner.ReclaimOversizedGeneratedHallway(
                    proposed, index, protectedRooms, options.Purpose, random,
                    out var withUsefulRooms);
                proposed = withUsefulRooms;
                addedRooms += reclaimed;
                // Rooms reclaimed from an overlarge hall need access doors;
                // earlier door planning could not see their new partitions.
                if (reclaimed > 0 && options.Doors)
                {
                    addedDoors += BuildingConnectivity.AddDoors(
                        proposed, index, out var withAccess);
                    proposed = withAccess;
                }
            }
        }

        // Chimneys before glazing. Otherwise a 1.2m window can overlap
        // the edge of a fireplace even when its centre is far away.
        if (options.Fireplaces &&
            BuildingConnectivity.TryAddChimney(proposed, out var withFireplace, options.Purpose))
        {
            proposed = withFireplace;
            addedChimneys++;
        }

        if (options.Windows)
        {
            foreach (int index in indices)
            {
                addedWindows += BuildingConnectivity.AddWindows(
                    proposed, index, out var withWindows);
                proposed = withWindows;
            }
        }

        if (options.Rooms)
        {
            var floorsWithoutRooms = indices.Where(i =>
            {
                var level = BuildingStoreys.GetFloor(proposed, i);
                return level.Perimeter.Count >= 3 &&
                    level.Rooms.All(r => r.Type is "Hallway" or "Stairwell") &&
                    RoomLayoutPlanner.UnassignedArea(level) >= 4.0;
            }).ToArray();
            if (floorsWithoutRooms.Length > 0)
            {
                reason = "No usable room partitions could be generated on " +
                    string.Join(", ", floorsWithoutRooms.Select(BuildingStoreys.FloorLabel)) +
                    ". The exterior shape or existing walls prevent a valid layout. " +
                    "Try a different seed or adjust the perimeter; no rooms were changed.";
                return false;
            }
        }
        if (addedRooms == 0 && addedCorridors == 0 &&
            addedDoors == 0 && addedWindows == 0 && addedStairs == 0 &&
            addedChimneys == 0 && absorbedHallStrips == 0)
        {
            bool roomOnly = options.Rooms && !options.Corridors &&
                !options.Doors && !options.Windows && !options.Stairs && !options.Fireplaces;
            reason = (roomOnly
                ? "No valid room fits the selected floors. "
                : "No valid new features fit the selected floors and settings. ") +
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
            $"{absorbedHallStrips} connected hallway gap(s), " +
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
