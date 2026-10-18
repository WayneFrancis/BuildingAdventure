using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>An upper floor. Ground-floor geometry remains at the document root
/// for backward compatibility with all perimeter-only and v0.4 JSON files.</summary>
public sealed class StoreyDefinition
{
    public string Name { get; set; } = "Upper Floor";
    public List<Point2> Perimeter { get; set; } = new();
    public List<RoomDefinition> Rooms { get; set; } = new();
    public List<DoorDefinition> Doors { get; set; } = new();
    public List<WindowDefinition> Windows { get; set; } = new();
    public WallThicknessSettings WallThickness { get; set; } = new();

    public StoreyDefinition Copy() => new()
    {
        Name = Name,
        Perimeter = Perimeter.ToList(),
        Rooms = Rooms.Select(r => r.Copy()).ToList(),
        Doors = Doors.Select(d => d.Copy()).ToList(),
        Windows = Windows.Select(w => w.Copy()).ToList(),
        WallThickness = WallThickness.Copy()
    };
}

/// <summary>
/// One rectangular stair footprint connecting two consecutive floors.
/// An index of 0 joins ground floor to first floor; 1 joins first to second.
/// Centre, length, width and rotation are expressed in world metres/degrees.
/// </summary>
public sealed class StaircaseDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LowerFloorIndex { get; set; }
    public Point2 Centre { get; set; }
    public double Length { get; set; } = 3.6;
    public double Width { get; set; } = 1.4;
    public double RotationDegrees { get; set; }

    // Additional void clearance around the stairs on the upper floor.
    // Defaults to zero to preserve all existing v2 staircase placements.
    public double OpeningExtraWidth { get; set; }
    public double OpeningExtraLength { get; set; }

    // Clear platform at BOTH ends of the flight, on both connected levels.
    // Zero maintains compatibility with previously saved stairs.
    public double LandingLength { get; set; }

    public StaircaseDefinition Copy() => new()
    {
        Id = Id,
        LowerFloorIndex = LowerFloorIndex,
        Centre = Centre,
        Length = Length,
        Width = Width,
        RotationDegrees = RotationDegrees,
        OpeningExtraWidth = OpeningExtraWidth,
        OpeningExtraLength = OpeningExtraLength,
        LandingLength = LandingLength
    };
}

/// <summary>A floor slab void computed from a linked staircase.</summary>
public sealed record StairwellOpening(Guid StaircaseId, int FloorIndex,
    List<Point2> Outline);

/// <summary>A reference floor displayed only as a translucent guide.</summary>
public sealed record AdjacentFloorPlan(int FloorIndex, bool IsAbove,
    BuildingDocument Floor);

public static class BuildingStoreys
{
    public static int FloorCount(BuildingDocument document) =>
        (document.HasGroundFloor ? 1 : 0) +
        document.UpperFloors.Count + document.BasementFloors.Count;

    public static int LowestFloorIndex(BuildingDocument document) =>
        document.BasementFloors.Count > 0 ? -document.BasementFloors.Count :
        document.HasGroundFloor ? 0 : 1;

    public static int HighestFloorIndex(BuildingDocument document) =>
        document.UpperFloors.Count > 0 ? document.UpperFloors.Count :
        document.HasGroundFloor ? 0 : -1;

    public static IReadOnlyList<int> FloorIndexes(BuildingDocument document) =>
        Enumerable.Range(LowestFloorIndex(document), FloorCount(document)).ToArray();

    public static bool HasFloor(BuildingDocument document, int index) =>
        index < 0 ? index >= -document.BasementFloors.Count :
        index == 0 ? document.HasGroundFloor :
        index <= document.UpperFloors.Count;

    public static BuildingDocument Copy(BuildingDocument source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Name = source.Name,
        GridFeet = source.GridFeet,
        HasGroundFloor = source.HasGroundFloor,
        Perimeter = source.Perimeter.ToList(),
        Rooms = source.Rooms.Select(r => r.Copy()).ToList(),
        Doors = source.Doors.Select(d => d.Copy()).ToList(),
        Windows = source.Windows.Select(w => w.Copy()).ToList(),
        WallThickness = source.WallThickness.Copy(),
        UpperFloors = source.UpperFloors.Select(f => f.Copy()).ToList(),
        BasementFloors = source.BasementFloors.Select(f => f.Copy()).ToList(),
        Staircases = source.Staircases.Select(s => s.Copy()).ToList(),
        Chimneys = source.Chimneys.Select(ch => ch.Copy()).ToList()
    };

    /// <summary>
    /// An opening exists in the upper slab of each stair flight, not in the
    /// slab it starts from. The opening follows its stair's centre and angle.
    /// </summary>
    public static List<StairwellOpening> GetOpenings(
        IReadOnlyList<StaircaseDefinition> staircases, int floorIndex) =>
        staircases
                .Where(s => s.LowerFloorIndex == floorIndex - 1)
                .Select(s => new StairwellOpening(s.Id, floorIndex,
                    StairOpeningFootprint(s)))
                .ToList();

    public static List<StairwellOpening> GetOpenings(
        BuildingDocument building, int floorIndex)
    {
        CheckFloorIndex(building, floorIndex);
        return GetOpenings(building.Staircases, floorIndex);
    }

    public static double SlabArea(BuildingDocument building, int floorIndex)
    {
        var floor = GetFloor(building, floorIndex);
        return PolygonGeometry.Area(floor.Perimeter) -
            GetOpenings(building, floorIndex)
                .Sum(opening => PolygonGeometry.Area(opening.Outline));
    }

    public static List<AdjacentFloorPlan> AdjacentFloors(BuildingDocument building,
        int floorIndex, bool includeBelow, bool includeAbove)
    {
        CheckFloorIndex(building, floorIndex);
        var adjacent = new List<AdjacentFloorPlan>();
        if (includeBelow && floorIndex > LowestFloorIndex(building))
            adjacent.Add(new AdjacentFloorPlan(
                floorIndex - 1, false, GetFloor(building, floorIndex - 1)));
        if (includeAbove && floorIndex < HighestFloorIndex(building))
            adjacent.Add(new AdjacentFloorPlan(
                floorIndex + 1, true, GetFloor(building, floorIndex + 1)));
        return adjacent;
    }

    public static List<Point2> StairOpeningFootprint(StaircaseDefinition stair)
    {
        var enlarged = stair.Copy();
        enlarged.Width += stair.OpeningExtraWidth;
        enlarged.Length += stair.OpeningExtraLength;
        return StairFootprint(enlarged);
    }

    /// <summary>Returns a separate single-floor document suitable for the existing editor.</summary>
    public static BuildingDocument GetFloor(BuildingDocument building, int floorIndex)
    {
        CheckFloorIndex(building, floorIndex);
        if (floorIndex == 0)
            return new BuildingDocument
            {
                Name = building.Name,
                GridFeet = building.GridFeet,
                Perimeter = building.Perimeter.ToList(),
                Rooms = building.Rooms.Select(r => r.Copy()).ToList(),
                Doors = building.Doors.Select(d => d.Copy()).ToList(),
                Windows = building.Windows.Select(w => w.Copy()).ToList(),
                WallThickness = building.WallThickness.Copy()
            };

        var floor = floorIndex < 0
            ? building.BasementFloors[-floorIndex - 1]
            : building.UpperFloors[floorIndex - 1];
        return new BuildingDocument
        {
            Name = building.Name,
            GridFeet = building.GridFeet,
            Perimeter = floor.Perimeter.ToList(),
            Rooms = floor.Rooms.Select(r => r.Copy()).ToList(),
            Doors = floor.Doors.Select(d => d.Copy()).ToList(),
            Windows = floor.Windows.Select(w => w.Copy()).ToList(),
            WallThickness = floor.WallThickness.Copy()
        };
    }

    /// <summary>Replace only one floor, preserving other floors and stairs.</summary>
    public static void SetFloor(BuildingDocument building, int index, BuildingDocument editedFloor)
    {
        CheckFloorIndex(building, index);
        if (index == 0)
        {
            building.Perimeter = editedFloor.Perimeter.ToList();
            building.Rooms = editedFloor.Rooms.Select(r => r.Copy()).ToList();
            building.Doors = editedFloor.Doors.Select(d => d.Copy()).ToList();
            building.Windows = editedFloor.Windows.Select(w => w.Copy()).ToList();
            building.WallThickness = editedFloor.WallThickness.Copy();
        }
        else
        {
            var floor = index < 0
                ? building.BasementFloors[-index - 1]
                : building.UpperFloors[index - 1];
            floor.Perimeter = editedFloor.Perimeter.ToList();
            floor.Rooms = editedFloor.Rooms.Select(r => r.Copy()).ToList();
            floor.Doors = editedFloor.Doors.Select(d => d.Copy()).ToList();
            floor.Windows = editedFloor.Windows.Select(w => w.Copy()).ToList();
            floor.WallThickness = editedFloor.WallThickness.Copy();
        }
    }

    /// <summary>Append an empty upper floor copying only the previous floor's perimeter.</summary>
    public static bool TryAddFloor(BuildingDocument source, out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (FloorCount(result) >= 4)
        {
            reason = "Buildings are currently limited to four floors.";
            return false;
        }
        if (!result.HasGroundFloor)
        {
            // A basement-only structure gets a new ground level before
            // any positive upper-floor levels can be created.
            var basement = GetFloor(result, -1);
            if (!PolygonGeometry.IsValidOutline(basement.Perimeter, out reason))
                return false;
            result.HasGroundFloor = true;
            SetFloor(result, 0, basement);
            result.SchemaVersion = 2;
            return true;
        }
        var previous = GetFloor(result, HighestFloorIndex(result));
        if (!PolygonGeometry.IsValidOutline(previous.Perimeter, out reason))
        {
            reason = "Finish a valid perimeter before adding another floor: " + reason;
            return false;
        }
        int index = HighestFloorIndex(result) + 1;
        result.UpperFloors.Add(new StoreyDefinition
        {
            Name = FloorLabel(index),
            Perimeter = previous.Perimeter.ToList(),
            WallThickness = previous.WallThickness.Copy()
        });
        result.SchemaVersion = 2;
        return true;
    }

    /// <summary>
    /// Add a new lowest underground level, maintaining ground as floor 0
    /// and addressing basement levels by -1, -2, etc.
    /// </summary>
    public static bool TryAddBasement(BuildingDocument source,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (FloorCount(result) >= 4)
        {
            reason = "Buildings are currently limited to four levels total.";
            return false;
        }
        int newIndex = LowestFloorIndex(result) - 1;
        var above = GetFloor(result, newIndex + 1);
        if (!PolygonGeometry.IsValidOutline(above.Perimeter, out reason))
        {
            reason = "Finish the perimeter above the basement first: " + reason;
            return false;
        }
        result.BasementFloors.Add(new StoreyDefinition
        {
            Name = FloorLabel(newIndex),
            Perimeter = above.Perimeter.ToList(),
            WallThickness = above.WallThickness.Copy()
        });
        result.SchemaVersion = 2;
        return true;
    }

    /// <summary>Remove only the deepest underground floor and its access stairs.</summary>
    public static bool TryRemoveBottomBasement(BuildingDocument source,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (result.BasementFloors.Count == 0)
        {
            reason = "There is no basement to remove.";
            return false;
        }
        if (FloorCount(result) <= 1)
        {
            reason = "A building must contain at least one floor.";
            return false;
        }
        int bottom = LowestFloorIndex(result);
        result.BasementFloors.RemoveAt(result.BasementFloors.Count - 1);
        result.Staircases.RemoveAll(s => s.LowerFloorIndex == bottom);
        return true;
    }

    /// <summary>Only the highest floor can be removed; linked stair flights are removed too.</summary>
    public static bool TryRemoveTopFloor(BuildingDocument source,
        out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (result.UpperFloors.Count == 0)
        {
            reason = "There is no upper floor to remove.";
            return false;
        }
        int topIndex = HighestFloorIndex(result);
        result.UpperFloors.RemoveAt(result.UpperFloors.Count - 1);
        result.Staircases.RemoveAll(stairs => stairs.LowerFloorIndex >= topIndex - 1);
        return true;
    }

    /// <summary>
    /// Remove any selected level. Levels strictly above it shift down by
    /// one elevation, and stairs touching the deleted level are removed.
    /// Other stairs are renumbered and revalidated against their new floors.
    /// Removing the last ground floor can leave a basement-only building.
    /// </summary>
    public static bool TryRemoveFloor(BuildingDocument source, int removedIndex,
        out BuildingDocument result, out int removedStairCount, out string reason)
    {
        result = Copy(source);
        removedStairCount = 0;
        reason = "";
        if (!HasFloor(source, removedIndex))
        {
            reason = "The selected level no longer exists.";
            return false;
        }
        if (FloorCount(source) <= 1)
        {
            reason = "A building must contain at least one floor.";
            return false;
        }

        var surviving = FloorIndexes(source)
            .Where(i => i != removedIndex).ToArray();
        var shifted = surviving
            .Select(old => (Old: old, New: old > removedIndex ? old - 1 : old))
            .ToList();
        if (!shifted.Any(x => x.New >= 0))
        {
            // Basement-only documents have no physical ground floor.
            // Keep negative indices contiguous even when removing the
            // topmost basement, otherwise an unrepresentable gap appears.
            shifted = surviving.Select((old, position) =>
                (Old: old, New: position - surviving.Length)).ToList();
        }
        result.HasGroundFloor = shifted.Any(x => x.New == 0);
        result.UpperFloors = new List<StoreyDefinition>();
        result.BasementFloors = new List<StoreyDefinition>();

        // Only the selected level disappears. Preserve rooms, windows and
        // door identities on each remaining floor, even across index 0.
        // Basement storage runs -1, -2, ... rather than ascending floor
        // indices. Assemble all levels in top-to-bottom storage order.
        foreach (var (oldIndex, newIndex) in shifted.OrderByDescending(x => x.New))
        {
            var floor = GetFloor(source, oldIndex);
            if (newIndex == 0)
            {
                SetFloor(result, 0, floor);
                continue;
            }
            var originalName = oldIndex < 0
                ? source.BasementFloors[-oldIndex - 1].Name
                : oldIndex > 0
                    ? source.UpperFloors[oldIndex - 1].Name
                    : FloorLabel(0); // The source ground is stored at root, not UpperFloors[-1].
            var name = originalName == FloorLabel(oldIndex)
                ? FloorLabel(newIndex) : originalName;
            var storey = new StoreyDefinition
            {
                Name = name,
                Perimeter = floor.Perimeter.ToList(),
                Rooms = floor.Rooms.Select(r => r.Copy()).ToList(),
                Doors = floor.Doors.Select(d => d.Copy()).ToList(),
                Windows = floor.Windows.Select(w => w.Copy()).ToList(),
                WallThickness = floor.WallThickness.Copy()
            };
            if (newIndex < 0)
                result.BasementFloors.Add(storey);
            else
                result.UpperFloors.Insert(0, storey);
        }

        if (!result.HasGroundFloor)
        {
            // The root floor is inactive in a basement-only structure.
            // Clear obsolete geometry rather than persisting a ghost floor.
            result.Perimeter = new List<Point2>();
            result.Rooms = new List<RoomDefinition>();
            result.Doors = new List<DoorDefinition>();
            result.Windows = new List<WindowDefinition>();
        }

        var stairs = new List<StaircaseDefinition>();
        foreach (var original in source.Staircases)
        {
            if (original.LowerFloorIndex == removedIndex ||
                original.LowerFloorIndex + 1 == removedIndex)
            {
                removedStairCount++;
                continue;
            }
            var oldLower = shifted.FirstOrDefault(s => s.Old == original.LowerFloorIndex);
            var oldUpper = shifted.FirstOrDefault(s => s.Old == original.LowerFloorIndex + 1);
            // Floor deletion or basement-only rebasing must not leave
            // staircases pointing at nonexistent or non-adjacent levels.
            if (!shifted.Any(s => s.Old == original.LowerFloorIndex) ||
                !shifted.Any(s => s.Old == original.LowerFloorIndex + 1) ||
                oldUpper.New != oldLower.New + 1)
            {
                removedStairCount++;
                continue;
            }
            var copied = original.Copy();
            copied.LowerFloorIndex = oldLower.New;
            stairs.Add(copied);
        }
        result.Staircases = stairs;
        result.SchemaVersion = 2;
        if (!Validate(result, out reason))
        {
            result = Copy(source);
            return false;
        }
        return true;
    }

    /// <summary>Place a new staircase using whole-building validation.</summary>
    public static bool TryAddStaircase(BuildingDocument source,
        StaircaseDefinition proposed, out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (proposed is null || proposed.Id == Guid.Empty ||
            result.Staircases.Any(s => s.Id == proposed.Id))
        {
            reason = "The new staircase needs a unique identifier.";
            return false;
        }
        result.Staircases.Add(proposed.Copy());
        return Validate(result, out reason);
    }

    /// <summary>
    /// Update an existing staircase's size, rotation or centre without
    /// changing its floor connection or identity. No caller-owned object
    /// is mutated if validation fails.
    /// </summary>
    public static bool TryUpdateStaircase(BuildingDocument source,
        StaircaseDefinition proposed, out BuildingDocument result, out string reason)
    {
        result = Copy(source);
        reason = "";
        if (proposed is null)
        {
            reason = "No staircase was provided.";
            return false;
        }

        int index = result.Staircases.FindIndex(s => s.Id == proposed.Id);
        if (index < 0)
        {
            reason = "The selected staircase was not found.";
            return false;
        }
        if (result.Staircases[index].LowerFloorIndex != proposed.LowerFloorIndex)
        {
            reason = "A staircase cannot be reassigned to another pair of floors.";
            return false;
        }

        result.Staircases[index] = proposed.Copy();
        return Validate(result, out reason);
    }

    public static string FloorLabel(int index) => index switch
    {
        0 => "Ground Floor",
        1 => "First Floor",
        2 => "Second Floor",
        3 => "Third Floor",
        -1 => "Basement 1",
        -2 => "Basement 2",
        -3 => "Basement 3",
        _ => index < 0 ? $"Basement {-index}" : $"Floor {index + 1}"
    };

    /// <summary>Validates all independent floor plans and each cross-floor stair reservation.</summary>
    public static bool Validate(BuildingDocument building, out string reason)
    {
        reason = "";
        if (building.GridFeet is not (0 or 5 or 10))
        {
            reason = "Grid scale must be metric, 5 feet or 10 feet.";
            return false;
        }
        if (building.WallThickness is null)
        {
            reason = "Ground Floor: Wall thickness settings are missing.";
            return false;
        }
        if (building.UpperFloors is null || building.BasementFloors is null ||
            building.Staircases is null || building.Chimneys is null)
        {
            reason = "Floor or staircase collections are missing.";
            return false;
        }
        if (FloorCount(building) < 1 || FloorCount(building) > 4 ||
            (!building.HasGroundFloor && building.UpperFloors.Count > 0))
        {
            reason = "A building needs 1–4 contiguous levels; above-ground floors require ground level.";
            return false;
        }
        if (building.SchemaVersion == 1 &&
            (building.UpperFloors.Count != 0 || building.BasementFloors.Count != 0 ||
             building.Staircases.Count != 0 || !building.HasGroundFloor))
        {
            reason = "Multi-floor buildings require JSON schema version 2.";
            return false;
        }
        if (building.SchemaVersion is not (1 or 2))
        {
            reason = $"Unsupported document schema version {building.SchemaVersion}.";
            return false;
        }

        foreach (int i in FloorIndexes(building))
        {
            if (i != 0)
            {
                var floor = i < 0
                    ? building.BasementFloors[-i - 1]
                    : building.UpperFloors[i - 1];
                if (floor is null || floor.Perimeter is null ||
                    floor.Rooms is null || floor.Doors is null || floor.Windows is null ||
                    floor.WallThickness is null ||
                    string.IsNullOrWhiteSpace(floor.Name) || floor.Name.Length > 100)
                {
                    reason = $"Floor {i + 1} has missing or invalid information.";
                    return false;
                }
            }

            var singleFloor = GetFloor(building, i);
            if (!FloorGeometry.Validate(singleFloor, out var error))
            {
                reason = $"{FloorLabel(i)}: {error}";
                return false;
            }
            if (i != 0 && singleFloor.Perimeter.Count == 0)
            {
                reason = $"{FloorLabel(i)} must have a perimeter.";
                return false;
            }
        }

        var ids = new HashSet<Guid>();
        foreach (var stair in building.Staircases)
        {
            if (stair is null || stair.Id == Guid.Empty || !ids.Add(stair.Id) ||
                stair.LowerFloorIndex < LowestFloorIndex(building) ||
                stair.LowerFloorIndex >= HighestFloorIndex(building) ||
                !double.IsFinite(stair.Centre.X) || !double.IsFinite(stair.Centre.Y) ||
                !double.IsFinite(stair.Width) || stair.Width < 0.7 || stair.Width > 5 ||
                !double.IsFinite(stair.Length) || stair.Length < 1.8 || stair.Length > 12 ||
                !double.IsFinite(stair.RotationDegrees) ||
                !double.IsFinite(stair.OpeningExtraWidth) ||
                stair.OpeningExtraWidth < 0 || stair.OpeningExtraWidth > 4 ||
                !double.IsFinite(stair.OpeningExtraLength) ||
                stair.OpeningExtraLength < 0 || stair.OpeningExtraLength > 6 ||
                !double.IsFinite(stair.LandingLength) ||
                stair.LandingLength < 0 || stair.LandingLength > 3)
            {
                reason = "Invalid staircase location, floor connection, or dimensions.";
                return false;
            }

            var footprint = StairFootprint(stair);
            var opening = StairOpeningFootprint(stair);
            if (!PolygonGeometry.IsValidOutline(footprint, out var error))
            {
                reason = "Invalid staircase footprint: " + error;
                return false;
            }
            for (int i = stair.LowerFloorIndex; i <= stair.LowerFloorIndex + 1; i++)
            {
                var floor = GetFloor(building, i);
                if (!FloorGeometry.IsWithinPolygon(footprint, floor.Perimeter))
                {
                    reason = $"The staircase does not fit inside {FloorLabel(i)}.";
                    return false;
                }

                // A stair opening cannot pass through an internal wall.
                // Users can lay rooms around its reservation, including a
                // dedicated stairwell room that encloses the rectangle.
                foreach (var wall in FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms))
                {
                    if (wall.IsExterior) continue;
                    if (WallCrossesFootprintInterior(wall, footprint))
                    {
                        reason = $"The staircase crosses an interior wall on {FloorLabel(i)}.";
                        return false;
                    }
                }
            }

            // Generated flights require a clear, usable landing at each
            // end on BOTH levels; older manually positioned flights retain
            // their prior behaviour with LandingLength == 0.
            foreach (var landing in StairLandings(stair))
            for (int i = stair.LowerFloorIndex; i <= stair.LowerFloorIndex + 1; i++)
            {
                var level = GetFloor(building, i);
                if (!FloorGeometry.IsWithinPolygon(landing, level.Perimeter) ||
                    FloorGeometry.BuildWalls(level.Perimeter, level.Rooms)
                        .Any(w => !w.IsExterior &&
                            WallCrossesFootprintInterior(w, landing)))
                {
                    reason = $"A stair landing has insufficient unobstructed space on {FloorLabel(i)}.";
                    return false;
                }
            }

            // The upstairs slab has the opening, which may be bigger than
            // the staircase itself. Validate the complete void clearance
            // against that floor's exterior and interior walls.
            var upperFloor = GetFloor(building, stair.LowerFloorIndex + 1);
            if (!FloorGeometry.IsWithinPolygon(opening, upperFloor.Perimeter))
            {
                reason = $"The stairwell opening does not fit inside {FloorLabel(stair.LowerFloorIndex + 1)}.";
                return false;
            }
            foreach (var wall in FloorGeometry.BuildWalls(upperFloor.Perimeter, upperFloor.Rooms))
            {
                if (wall.IsExterior) continue;
                if (WallCrossesFootprintInterior(wall, opening))
                {
                    reason = $"The stairwell opening crosses an interior wall on {FloorLabel(stair.LowerFloorIndex + 1)}.";
                    return false;
                }
            }
        }

        // Voids must not overlap: unioning several different holes would
        // otherwise misstate usable slab area and create ambiguous openings.
        foreach (int floorIndex in FloorIndexes(building).Where(i => i > LowestFloorIndex(building)))
        {
            var openings = GetOpenings(building, floorIndex);
            for (int i = 0; i < openings.Count; i++)
            for (int j = i + 1; j < openings.Count; j++)
            {
                // Legacy v2 files could contain intersecting stair footprints.
                // Keep those files loadable when neither flight asks for
                // additional clearance; new expanded openings must not clash.
                var first = building.Staircases.First(s =>
                    s.Id == openings[i].StaircaseId);
                var second = building.Staircases.First(s =>
                    s.Id == openings[j].StaircaseId);
                bool hasExtraClearance =
                    first.OpeningExtraWidth > 0 || first.OpeningExtraLength > 0 ||
                    second.OpeningExtraWidth > 0 || second.OpeningExtraLength > 0;
                if (!hasExtraClearance ||
                    !ConvexPolygonsOverlap(openings[i].Outline, openings[j].Outline))
                    continue;
                reason = $"Stairwell openings overlap on {FloorLabel(floorIndex)}.";
                return false;
            }
        }
        var chimneyIds = new HashSet<Guid>();
        foreach (var chimney in building.Chimneys)
        {
            if (chimney is null || chimney.Id == Guid.Empty ||
                !chimneyIds.Add(chimney.Id) ||
                !double.IsFinite(chimney.Centre.X) ||
                !double.IsFinite(chimney.Centre.Y) ||
                !double.IsFinite(chimney.Width) ||
                !double.IsFinite(chimney.Depth) ||
                chimney.Width < 0.4 || chimney.Width > 2 ||
                chimney.Depth < 0.4 || chimney.Depth > 2)
            {
                reason = "A chimney has invalid dimensions or identifier.";
                return false;
            }
            var footprint = ChimneyFootprint(chimney);
            var served = ChimneyFloors(building, chimney);
            if (served.Count == 0)
            {
                reason = "The chimney is outside every floor of the building.";
                return false;
            }
            foreach (int index in served)
            {
                var level = GetFloor(building, index);
                if (FloorGeometry.BuildWalls(level.Perimeter, level.Rooms)
                    .Any(w => !w.IsExterior &&
                        WallCrossesFootprintInterior(w, footprint)))
                {
                    reason = $"The chimney crosses an interior wall on {FloorLabel(index)}.";
                    return false;
                }
                if (building.Staircases.Any(s =>
                    (s.LowerFloorIndex == index ||
                     s.LowerFloorIndex + 1 == index) &&
                    (ConvexPolygonsOverlap(footprint, StairFootprint(s)) ||
                     StairLandings(s).Any(l => ConvexPolygonsOverlap(footprint, l)))))
                {
                    reason = $"The chimney blocks a stair or landing on {FloorLabel(index)}.";
                    return false;
                }
                if (level.Doors.Any(d => d.Centre.DistanceTo(chimney.Centre) < 1.1) ||
                    level.Windows.Any(w => w.Centre.DistanceTo(chimney.Centre) < 1.2))
                {
                    reason = $"A door or window is too close to the fireplace on {FloorLabel(index)}.";
                    return false;
                }
            }
        }
        return true;
    }

    // Separating-axis theorem for rotated rectangular holes.
    private static bool ConvexPolygonsOverlap(
        IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        foreach (var polygon in new[] { first, second })
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            double axisX = -(b.Y - a.Y), axisY = b.X - a.X;
            var f = first.Select(p => p.X * axisX + p.Y * axisY).ToList();
            var s = second.Select(p => p.X * axisX + p.Y * axisY).ToList();
            if (f.Max() <= s.Min() + 1e-7 ||
                s.Max() <= f.Min() + 1e-7)
                return false;
        }
        return true;
    }

    /// <summary>Rectangular clear floor areas beyond both ends of a flight.</summary>
    public static List<Point2> ChimneyFootprint(ChimneyDefinition chimney)
    {
        double a = chimney.Centre.X - chimney.Width / 2;
        double b = chimney.Centre.Y - chimney.Depth / 2;
        return
        [
            new Point2(a, b),
            new Point2(a + chimney.Width, b),
            new Point2(a + chimney.Width, b + chimney.Depth),
            new Point2(a, b + chimney.Depth)
        ];
    }

    public static IReadOnlyList<int> ChimneyFloors(
        BuildingDocument building, ChimneyDefinition chimney) =>
        FloorIndexes(building).Where(i =>
            FloorGeometry.IsWithinPolygon(ChimneyFootprint(chimney),
                GetFloor(building, i).Perimeter)).ToArray();

    public static IReadOnlyList<List<Point2>> StairLandings(StaircaseDefinition stair)
    {
        if (stair.LandingLength <= 0) return [];
        double angle = stair.RotationDegrees * Math.PI / 180;
        double dx = -Math.Sin(angle), dy = Math.Cos(angle);
        double distance = (stair.Length + stair.LandingLength) / 2;
        var result = new List<List<Point2>>(2);
        foreach (int sign in new[] { -1, 1 })
        {
            var landing = stair.Copy();
            landing.Centre = new Point2(stair.Centre.X + sign * dx * distance,
                stair.Centre.Y + sign * dy * distance);
            landing.Length = stair.LandingLength;
            result.Add(StairFootprint(landing));
        }
        return result;
    }

    public static List<Point2> StairFootprint(StaircaseDefinition stair)
    {
        double radians = stair.RotationDegrees * Math.PI / 180;
        double c = Math.Cos(radians), s = Math.Sin(radians);
        double halfWidth = stair.Width / 2;
        double halfLength = stair.Length / 2;
        return
        [
            Rotate(-halfWidth, -halfLength),
            Rotate(halfWidth, -halfLength),
            Rotate(halfWidth, halfLength),
            Rotate(-halfWidth, halfLength)
        ];

        Point2 Rotate(double x, double y) => new(
            stair.Centre.X + x * c - y * s,
            stair.Centre.Y + x * s + y * c);
    }

    private static bool WallCrossesFootprintInterior(
        WallSegment wall, IReadOnlyList<Point2> footprint)
    {
        // A wall may coincide with the boundary of the staircase, but not
        // run through its interior. Check endpoints and crossing intervals.
        var midpoint = new Point2((wall.A.X + wall.B.X) / 2,
                                  (wall.A.Y + wall.B.Y) / 2);
        if (StrictlyInside(footprint, wall.A) ||
            StrictlyInside(footprint, wall.B) ||
            StrictlyInside(footprint, midpoint))
            return true;

        // For a convex rectangle a segment with two boundary crossings
        // must pass through its interior even if its midpoint is outside.
        int crossings = 0;
        for (int i = 0; i < footprint.Count; i++)
        {
            var a = footprint[i];
            var b = footprint[(i + 1) % footprint.Count];
            double rx = wall.B.X - wall.A.X;
            double ry = wall.B.Y - wall.A.Y;
            double sx = b.X - a.X;
            double sy = b.Y - a.Y;
            double denominator = rx * sy - ry * sx;
            if (Math.Abs(denominator) < 1e-9) continue;
            double t = ((a.X - wall.A.X) * sy - (a.Y - wall.A.Y) * sx) / denominator;
            double u = ((a.X - wall.A.X) * ry - (a.Y - wall.A.Y) * rx) / denominator;
            if (t > 1e-7 && t < 1 - 1e-7 &&
                u > 1e-7 && u < 1 - 1e-7) crossings++;
        }
        return crossings >= 2;
    }

    private static bool StrictlyInside(IReadOnlyList<Point2> polygon, Point2 p) =>
        PolygonGeometry.ContainsPoint(polygon, p) &&
        Enumerable.Range(0, polygon.Count).All(i =>
            PolygonGeometry.ClosestPointOnSegment(
                p, polygon[i], polygon[(i + 1) % polygon.Count]).DistanceTo(p) > 1e-7);

    private static void CheckFloorIndex(BuildingDocument building, int index)
    {
        if (!HasFloor(building, index))
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
