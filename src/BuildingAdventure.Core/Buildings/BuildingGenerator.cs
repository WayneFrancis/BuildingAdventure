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
    /// Compare the actual room shapes/roles, not random object IDs, doors
    /// or windows. The editor uses this to prevent a "Regenerate" action
    /// from offering the same architectural arrangement again.
    /// </summary>
    public static bool SameRoomLayout(BuildingDocument first,
        BuildingDocument second, IEnumerable<int> floorIndexes)
    {
        static string Signature(RoomDefinition room) =>
            room.Type + "|" + string.Join(";", room.Outline
                .Select(p => FormattableString.Invariant(
                    $"{p.X:F4},{p.Y:F4}"))
                .OrderBy(p => p, StringComparer.Ordinal));
        foreach (int floorIndex in floorIndexes.Distinct())
        {
            if (!BuildingStoreys.HasFloor(first, floorIndex) ||
                !BuildingStoreys.HasFloor(second, floorIndex))
                return false;
            var before = BuildingStoreys.GetFloor(first, floorIndex).Rooms
                .Select(Signature).OrderBy(s => s, StringComparer.Ordinal);
            var after = BuildingStoreys.GetFloor(second, floorIndex).Rooms
                .Select(Signature).OrderBy(s => s, StringComparer.Ordinal);
            if (!before.SequenceEqual(after)) return false;
        }
        return true;
    }

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
        // Broad L-shaped homes can do substantially better than an
        // L-hall with thin dining/bedroom strips. Here the downstairs
        // common room houses the stair, upstairs has its own sitting
        // room and bedrooms, and every functional room has adequate width.
        if (floorCount==2 && options.CompactRpgHouseLayout &&
            options.Purpose==BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors && options.Stairs &&
            BuildingStoreys.TryAddFloor(blank,out var balancedBasis,out _) &&
            (SideLandingRpgHousePlanner.TryGenerate(balancedBasis,seed,
                out var balanced,out _) ||
             BalancedSteppedRpgHousePlanner.TryGenerate(balancedBasis,seed,
                out balanced,out _)))
        {
            int hearths=0;
            if(options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(balanced,
                    out var heated,options.Purpose))
            {
                balanced=heated;
                hearths=1;
            }
            if(options.Windows)
            {
                foreach(int level in new[]{0,1})
                {
                    BuildingConnectivity.AddWindows(balanced,level,out var glazed);
                    balanced=glazed;
                }
            }
            if(BuildingStoreys.Validate(balanced,out reason))
            {
                suggestion=new BuildingSuggestion(balanced,seed,[0,1],
                    $"{(balanced.Rooms.Any(r=>r.Name==\"Short entry passage\") ? "Side-landing" : "Balanced")} L-shaped RPG house. Seed {seed}: "+
                    "staircase in the ground living area, "+
                    $"independent upstairs room zoning, {hearths} hearth(s)");
                return true;
            }
        }

        // Simple six-corner stepped homes should be built as complete
        // RPG plans too. A rectangle-only template cannot fill their L
        // wings, and corridor-first planning could waste 77% of the floor.
        if (floorCount == 2 && options.CompactRpgHouseLayout &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors && options.Stairs &&
            BuildingStoreys.TryAddFloor(blank,out var steppedBasis,out _) &&
            SteppedRpgHousePlanner.TryGenerate(steppedBasis,seed,
                out var stepped,out _))
        {
            int chimneyCount=0;
            if(options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(stepped,
                    out var heated,options.Purpose))
            {
                stepped=heated;
                chimneyCount=1;
            }
            if(options.Windows)
            {
                foreach(int level in new[]{0,1})
                {
                    BuildingConnectivity.AddWindows(stepped,level,out var glazed);
                    stepped=glazed;
                }
            }
            if(BuildingStoreys.Validate(stepped,out reason))
            {
                suggestion=new BuildingSuggestion(stepped,seed,[0,1],
                    $"Stepped two-storey RPG house. Seed {seed}: " +
                    $"3 bedrooms upstairs, short stair passage, " +
                    $"{chimneyCount} chimney stack(s)");
                return true;
            }
        }

        // Prefer a genuine two-storey RPG plan over the old whole-width
        // corridor-first arrangement. The planner owns both sets of rooms
        // and the connecting stair in one transaction, so the door network
        // and landing geometry are verified before showing any preview.
        if (floorCount == 2 && options.CompactRpgHouseLayout &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors &&
            options.Stairs &&
            BuildingStoreys.TryAddFloor(blank, out var compactBasis, out _) &&
            CompactRpgHousePlanner.TryGenerate(compactBasis, seed,
                out var compact, out _))
        {
            int compactChimneys = 0;
            if (options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(compact,
                    out var withHearth, options.Purpose))
            {
                compact = withHearth;
                compactChimneys = 1;
            }
            if (options.Windows)
            {
                foreach (int level in new[] { 0, 1 })
                {
                    BuildingConnectivity.AddWindows(compact, level,
                        out var glazed);
                    compact = glazed;
                }
            }
            if (BuildingStoreys.Validate(compact, out reason))
            {
                suggestion = new BuildingSuggestion(compact, seed, [0, 1],
                    "Compact two-storey RPG house. " +
                    $"Seed {seed}: 3 upstairs bedrooms, " +
                    $"{compact.Rooms.Count + compact.UpperFloors[0].Rooms.Count} rooms, " +
                    $"1 staircase, {compactChimneys} chimney stack(s)");
                return true;
            }
        }

        // A single-storey hall house can be planned directly from its
        // exterior footprint. Building and then deleting a modern corridor
        // is unnecessary and may fail the temporary door-access checks
        // even though the historic topology itself fits perfectly.
        if (floorCount == 1 && options.HallHouseLayout &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors &&
            MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
                blank, seed, out var directHallHouse, out _))
        {
            int hearthCount = 0;
            if (options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(directHallHouse,
                    out var heated, options.Purpose))
            {
                directHallHouse = heated;
                hearthCount = 1;
            }
            if (options.Windows)
            {
                BuildingConnectivity.AddWindows(directHallHouse, 0,
                    out var withWindows);
                directHallHouse = withWindows;
            }
            if (BuildingStoreys.Validate(directHallHouse, out reason))
            {
                suggestion = new BuildingSuggestion(directHallHouse, seed, [0],
                    $"Medieval residence layout. Whole building (1 floor), seed {seed}: " +
                    $"{directHallHouse.Rooms.Count} rooms, {directHallHouse.Doors.Count} doors, " +
                    $"{hearthCount} chimney stack(s)");
                return true;
            }
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
        // The historical footprint planner works on the COMPLETE draft,
        // after stacked flights and stair bays are settled, not while
        // individual floors are being staged.
        stageOptions.HallHouseLayout = false;
        stageOptions.DeferCirculationValidation = true;
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
            fill.HallHouseLayout = false;
            fill.DeferCirculationValidation = true;
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
                // A wide entry wing should first become two storage rooms
                // while preserving a clear approach to the outside door.
                int entryStores = RoomLayoutPlanner.PartitionGeneratedEntranceWing(
                    result, level, Array.Empty<Guid>(), out var withStores);
                result = withStores;
                if (entryStores > 0 && options.Doors)
                {
                    BuildingConnectivity.AddDoors(result, level, out var withStoreDoors);
                    result = withStoreDoors;
                }
                // The whole-building path merges temporary stair bays only
                // after furnishing, so its hallway cleanup belongs here.
                int reclaimed = RoomLayoutPlanner.ReclaimOversizedGeneratedHallway(
                    result, level, Array.Empty<Guid>(), options.Purpose,
                    new Random(unchecked(seed + level + 11)), out var useful);
                result = useful;
                if (reclaimed > 0 && options.Doors)
                {
                    BuildingConnectivity.AddDoors(result, level, out var accessed);
                    result = accessed;
                }
                // Oversized L-shaped stair wings cannot be reduced to a
                // straight corridor, since their stairs lie in the wing.
                // Preserve a compact stair cap and approach; turn the
                // remaining pocket into useful accommodation.
                int reclaimedStairWing =
                    RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
                        result, level, Array.Empty<Guid>(), out var wingFixed);
                result = wingFixed;
                if (reclaimedStairWing > 0 && options.Doors)
                {
                    BuildingConnectivity.AddDoors(result, level, out var withAccess);
                    result = withAccess;
                }
                // Some stepped outlines need a much wider stair wing
                // initially. A compact stair cap + side approach +
                // short cross-passage can recover the two broad room
                // wings without moving the existing flight or entrance.
                int reclaimedIrregular = IrregularHallRepair.Reclaim(
                    result, level, Array.Empty<Guid>(), options.Purpose,
                    out var repartitioned);
                result = repartitioned;
                if (reclaimedIrregular > 0 && options.Doors)
                {
                    BuildingConnectivity.AddDoors(result, level, out var opened);
                    result = opened;
                }
                // Critical final pass: access spurs cannot be validated
                // until staircase enclosures have been removed and ALL
                // generated storage partitions are in their final place.
                // Old generated store doors are removed and rebuilt.
                if (options.Doors)
                {
                    RoomLayoutPlanner.RepairGeneratedStorageAccess(
                        result, level, Array.Empty<Guid>(), out var walkable);
                    result = walkable;
                    RoomLayoutPlanner.AbsorbMisplacedGeneratedStores(
                        result, level, Array.Empty<Guid>(), out var functional);
                    result = functional;
                    // An L-house may leave a narrow side aisle beside
                    // the full stair run. Put the service room behind a
                    // short cross passage instead, with direct access
                    // to the lower landing.
                    if (SideStairApproachPlanner.TryRebalance(
                        result, level, Array.Empty<Guid>(),
                        out var withUsableApproach))
                        result = withUsableApproach;
                }
                // This geometry correction is independent of the door
                // toggle: a room-only suggestion must not leave an
                // unassigned strip or inaccessible stairhead either.
                SteppedLayoutFinisher.Refine(result, level,
                    Array.Empty<Guid>(), out var withFinishedEdges);
                result = withFinishedEdges;
            }
        }

        // Replace the long modern central corridor with a screens
        // passage, great hall, kitchen/pantry service end and private
        // parlour where the COMPLETE perimeter and stacked stairs permit.
        // Only whole-building-generated rooms are replaced here.
        bool hallHouseBuilt = false;
        string hallHouseFitReason = "";
        if (options.HallHouseLayout && options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors)
        {
            if (MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
                result, seed, out var medieval, out hallHouseFitReason))
            {
                result = medieval;
                hallHouseBuilt = true;
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
        // A successfully validated polygon plan may still be unnavigable:
        // door-to-store-to-bedroom is not acceptable automatic circulation.
        if (options.Rooms && options.Doors && options.Corridors)
        {
            foreach (int floorIndex in BuildingStoreys.FloorIndexes(result))
            {
                // The established upper-floor layout mode may contain
                // interconnected private suites. This guard currently
                // targets the ground-level service-band defect.
                if (floorIndex != 0) continue;
                var isolated = BuildingConnectivity.RoomsIsolatedByServiceSpaces(
                    result, floorIndex);
                if (isolated.Count == 0) continue;
                reason = $"The draft has inaccessible rooms on " +
                    $"{BuildingStoreys.FloorLabel(floorIndex)}: " +
                    string.Join(", ", isolated.Select(r => r.Name)) +
                    ". Those rooms need a route that does not pass " +
                    "through a storeroom or another bedroom." +
                    (options.HallHouseLayout && !hallHouseBuilt
                        ? " Historic layout also could not fit: " + hallHouseFitReason
                        : "");
                return false;
            }
        }
        // A successful one-storey residential draft must actually have
        // sleeping accommodation. If the historic alternative cannot
        // accommodate it, do not quietly return a legacy room arrangement
        // consisting of nothing but a hall, kitchen and storage.
        if (floorCount == 1 && options.Purpose == BuildingUse.Residence &&
            options.HallHouseLayout && options.Rooms &&
            !result.Rooms.Any(room => room.Type == "Bedroom"))
        {
            reason = "No usable bedroom fitted this one-storey residence. " +
                "Try another seed, a larger footprint, or a different " +
                "generation style. No incomplete residence was proposed.";
            return false;
        }
        string label = (options.CompactRpgHouseLayout &&
            options.Purpose == BuildingUse.Residence && floorCount == 2
                ? "Compact RPG floor plan was not geometrically feasible; " +
                  "used the fallback residential planner. "
                : "") +
            (hallHouseBuilt
                ? "Medieval residence layout. "
                : options.HallHouseLayout && options.Purpose == BuildingUse.Residence
                    ? "Hall-house layout did not fit; using the traditional corridor layout. "
                    : "") +
            $"Whole building ({floorCount} floor(s)), seed {seed}: " +
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

        // A broad L-shaped residential footprint has its own room-first
        // archetype: do not allow the older fallback to duplicate rooms,
        // shrink the dining room to a 1.75m strip or create a 34m² bedroom.
        // The two floor layouts are independent except for stair geometry.
        if (options.CompactRpgHouseLayout &&
            options.Purpose==BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors && options.Stairs &&
            indices.Length==2 && indices[0]==0 && indices[1]==1 &&
            BuildingStoreys.FloorCount(source)==2 &&
            (SideLandingRpgHousePlanner.TryGenerate(source,seed,
                out var balanced,out _) ||
             BalancedSteppedRpgHousePlanner.TryGenerate(source,seed,
                out balanced,out _)))
        {
            int hearths=0;
            if(options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(balanced,
                    out var heated,options.Purpose))
            {
                balanced=heated;
                hearths=1;
            }
            if(options.Windows)
            {
                foreach(int level in indices)
                {
                    BuildingConnectivity.AddWindows(balanced,level,out var glazed);
                    balanced=glazed;
                }
            }
            if(BuildingStoreys.Validate(balanced,out reason))
            {
                suggestion=new BuildingSuggestion(balanced,seed,indices,
                    $"{(balanced.Rooms.Any(r=>r.Name==\"Short entry passage\") ? "Side-landing" : "Balanced")} L-shaped RPG house. Seed {seed}: "+
                    "kitchen, dining, independent upstairs partitions, "+
                    $"three proportionate bedrooms, {hearths} new hearth(s)");
                return true;
            }
        }

        // Select-floor and Regenerate should plan both halves of an
        // L-shaped residence together. The old corridor-first planner
        // routinely allocated 57–77% to an L-shaped hallway. If the
        // compact stepped archetype safely fits, use it before staging.
        if (options.CompactRpgHouseLayout &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors && options.Stairs &&
            indices.Length==2 && indices[0]==0 && indices[1]==1 &&
            BuildingStoreys.FloorCount(source)==2 &&
            SteppedRpgHousePlanner.TryGenerate(source,seed,
                out var stepped,out _))
        {
            int chimneyCount=0;
            if(options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(stepped,
                    out var heated,options.Purpose))
            {
                stepped=heated;
                chimneyCount=1;
            }
            if(options.Windows)
            {
                foreach(int index in indices)
                {
                    BuildingConnectivity.AddWindows(stepped,index,out var glazed);
                    stepped=glazed;
                }
            }
            if(BuildingStoreys.Validate(stepped,out reason))
            {
                suggestion=new BuildingSuggestion(stepped,seed,indices,
                    $"Stepped two-storey RPG house. Seed {seed}: " +
                    $"3 upper bedrooms, stair landing, {chimneyCount} chimney(s)");
                return true;
            }
        }

        // The user may be generating a blank matched pair of floors or
        // regenerating a previously accepted two-storey RPG house. Respect
        // any existing staircase (never move it) and other floors; only
        // use this archetype when BOTH complete floors are blank.
        if (options.CompactRpgHouseLayout &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors &&
            options.Stairs &&
            indices.Length == 2 && indices[0] == 0 && indices[1] == 1 &&
            BuildingStoreys.FloorCount(source) == 2 &&
            CompactRpgHousePlanner.TryGenerate(source, seed,
                out var compact, out _))
        {
            int compactChimneys = 0;
            if (options.Fireplaces &&
                BuildingConnectivity.TryAddChimney(compact,
                    out var heated, options.Purpose))
            {
                compact = heated;
                compactChimneys = 1;
            }
            if (options.Windows)
            {
                foreach (int index in indices)
                {
                    BuildingConnectivity.AddWindows(compact, index,
                        out var glazed);
                    compact = glazed;
                }
            }
            if (BuildingStoreys.Validate(compact, out reason))
            {
                suggestion = new BuildingSuggestion(compact, seed, indices,
                    "Compact two-storey RPG house. " +
                    $"Seed {seed}: 3 upstairs bedrooms, " +
                    $"1 staircase, {compactChimneys} new chimney stack(s)");
                return true;
            }
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
            if (options.Rooms && options.Corridors)
            {
                // Storage bands can otherwise sit between a bedroom or
                // dining room and the ONLY hallway: a generated door into
                // that store is not a satisfactory route through the house.
                // Carve an edge access passage before planning any doors.
                var preserved = BuildingStoreys.GetFloor(original, index)
                    .Rooms.Select(r => r.Id).ToArray();
                RoomLayoutPlanner.OpenDirectAccessPastGeneratedStores(
                    proposed, index, preserved, out var accessible);
                proposed = accessible;
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
                // Use shallow side pockets around a real exterior
                // doorway for accessible storage, not oversized hallway.
                int entryStores = RoomLayoutPlanner.PartitionGeneratedEntranceWing(
                    proposed, index, protectedRooms, out var withStores);
                proposed = withStores;
                addedRooms += entryStores;
                if (entryStores > 0 && options.Doors)
                {
                    addedDoors += BuildingConnectivity.AddDoors(
                        proposed, index, out var withStoreDoors);
                    proposed = withStoreDoors;
                }
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
                int reclaimedStairWing =
                    RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
                        proposed, index, protectedRooms, out var withCompactWing);
                proposed = withCompactWing;
                addedRooms += reclaimedStairWing;
                if (reclaimedStairWing > 0 && options.Doors)
                {
                    addedDoors += BuildingConnectivity.AddDoors(
                        proposed, index, out var withWingAccess);
                    proposed = withWingAccess;
                }
                int irregularRooms = IrregularHallRepair.Reclaim(
                    proposed, index, protectedRooms, options.Purpose,
                    out var withIrregularRooms);
                proposed = withIrregularRooms;
                addedRooms += irregularRooms;
                if (irregularRooms > 0 && options.Doors)
                {
                    addedDoors += BuildingConnectivity.AddDoors(
                        proposed, index, out var withIrregularDoors);
                    proposed = withIrregularDoors;
                }
                // Storage created by the final consolidation or recovery
                // pass needs a fresh accessibility check, after doors
                // were first generated earlier in this function.
                if (options.Doors)
                {
                    RoomLayoutPlanner.RepairGeneratedStorageAccess(
                        proposed, index, protectedRooms, out var walkable);
                    proposed = walkable;
                    RoomLayoutPlanner.AbsorbMisplacedGeneratedStores(
                        proposed, index, protectedRooms, out var functional);
                    proposed = functional;
                    if (SideStairApproachPlanner.TryRebalance(
                        proposed, index, protectedRooms,
                        out var withUsableApproach))
                        proposed = withUsableApproach;
                }
                SteppedLayoutFinisher.Refine(
                    proposed, index, protectedRooms,
                    out var withFinishedEdges);
                proposed = withFinishedEdges;
            }
        }

        // The selected-floor route may use the historical archetype too,
        // but only for a previously blank ground floor. Never overwrite
        // existing user rooms, windows or doors on an edited floor.
        bool hallHouseSuggested = false;
        if (options.HallHouseLayout && options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors && options.Doors &&
            indices.Contains(0))
        {
            var untouchedGround = BuildingStoreys.GetFloor(original, 0);
            if (untouchedGround.Rooms.Count == 0 &&
                untouchedGround.Doors.Count == 0 &&
                untouchedGround.Windows.Count == 0 &&
                !BuildingStoreys.GetFloor(proposed, 0).Rooms
                    .Any(r => r.Type == "Stairwell") &&
                MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
                    proposed, seed, out var medieval, out _))
            {
                proposed = medieval;
                hallHouseSuggested = true;
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
        // A mathematically valid plan may still be an unusable RPG
        // house. Stepped footprints previously produced two-storey
        // drafts with a single kitchen/bedroom and a 77%-area hallway.
        // Reject that result rather than reporting it as a success.
        // This only applies to freshly generated residential rooms:
        // hand-authored rooms and corridor-only staging are untouched.
        if (!options.DeferCirculationValidation &&
            options.Purpose == BuildingUse.Residence &&
            options.Rooms && options.Corridors)
        {
            foreach (int index in indices)
            {
                var sourceFloor=BuildingStoreys.GetFloor(original,index);
                var proposedFloor=BuildingStoreys.GetFloor(proposed,index);
                if (sourceFloor.Rooms.Count!=0 ||
                    proposedFloor.Perimeter.Count<6 ||
                    proposedFloor.Rooms.Count>2) continue;
                double total=PolygonGeometry.Area(proposedFloor.Perimeter);
                double hallway=proposedFloor.Rooms.Where(r=>r.Type=="Hallway")
                    .Sum(r=>PolygonGeometry.Area(r.Outline));
                if (total < 45 || hallway / total < 0.55) continue;
                reason=$"The generated {BuildingStoreys.FloorLabel(index)} " +
                    $"has only {proposedFloor.Rooms.Count-1} usable room(s) " +
                    $"and assigns {hallway/total:P0} of the floor to a " +
                    "hallway. Try regenerating with a different seed " +
                    "or adjust the outline; no poor-quality draft was proposed.";
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

        // Selected-floor suggestions must not quietly create rooms that
        // are reachable only by walking through generated storage.
        // Leave existing manually authored rooms alone.
        if (options.Rooms && options.Doors && options.Corridors &&
            !options.DeferCirculationValidation)
        {
            foreach (int index in indices)
            {
                if (index != 0) continue;
                var level = BuildingStoreys.GetFloor(proposed, index);
                if (level.Rooms.Any(r => r.Type == "Stairwell")) continue;
                var originalIds = BuildingStoreys.GetFloor(original, index)
                    .Rooms.Select(r => r.Id).ToHashSet();
                var isolated = BuildingConnectivity.RoomsIsolatedByServiceSpaces(
                    proposed, index).Where(r => !originalIds.Contains(r.Id))
                    .ToArray();
                if (isolated.Length == 0) continue;
                reason = $"The proposed layout isolates " +
                    string.Join(", ", isolated.Select(r => r.Name)) +
                    $" on {BuildingStoreys.FloorLabel(index)}. " +
                    "These rooms need a doorway to circulation without " +
                    "walking through a storeroom or private bedroom.";
                return false;
            }
        }

        // This guard also applies to regeneration of an existing
        // single-storey building. Do not replace an accepted house with a
        // kitchen/pantry/common-room-only draft lacking a bedroom.
        if (!options.DeferCirculationValidation &&
            options.Purpose == BuildingUse.Residence &&
            options.HallHouseLayout && options.Rooms &&
            BuildingStoreys.FloorCount(proposed) == 1 &&
            indices.Contains(0) &&
            BuildingStoreys.GetFloor(original, 0).Rooms.Count == 0 &&
            !BuildingStoreys.GetFloor(proposed, 0).Rooms
                .Any(r => r.Type == "Bedroom"))
        {
            reason = "The proposed one-storey residence has no bedroom. " +
                "Try another seed or a larger footprint.";
            return false;
        }

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
        string description = (hallHouseSuggested
                ? "Medieval residence with a common hall and screens passage. "
                : options.HallHouseLayout && options.Purpose == BuildingUse.Residence &&
                  indices.Contains(0) && BuildingStoreys.GetFloor(original, 0).Rooms.Count == 0
                    ? "Hall-house arrangement did not fit; corridor layout retained. "
                    : "") +
            $"Seed {seed}: {addedFootprints} new perimeter(s), " +
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
