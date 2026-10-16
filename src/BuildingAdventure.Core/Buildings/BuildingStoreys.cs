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

    public StoreyDefinition Copy() => new()
    {
        Name = Name,
        Perimeter = Perimeter.ToList(),
        Rooms = Rooms.Select(r => r.Copy()).ToList(),
        Doors = Doors.Select(d => d.Copy()).ToList()
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

    public StaircaseDefinition Copy() => new()
    {
        Id = Id,
        LowerFloorIndex = LowerFloorIndex,
        Centre = Centre,
        Length = Length,
        Width = Width,
        RotationDegrees = RotationDegrees,
        OpeningExtraWidth = OpeningExtraWidth,
        OpeningExtraLength = OpeningExtraLength
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
        1 + document.UpperFloors.Count;

    public static BuildingDocument Copy(BuildingDocument source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Name = source.Name,
        Perimeter = source.Perimeter.ToList(),
        Rooms = source.Rooms.Select(r => r.Copy()).ToList(),
        Doors = source.Doors.Select(d => d.Copy()).ToList(),
        UpperFloors = source.UpperFloors.Select(f => f.Copy()).ToList(),
        Staircases = source.Staircases.Select(s => s.Copy()).ToList()
    };

    /// <summary>
    /// An opening exists in the upper slab of each stair flight, not in the
    /// slab it starts from. The opening follows its stair's centre and angle.
    /// </summary>
    public static List<StairwellOpening> GetOpenings(
        IReadOnlyList<StaircaseDefinition> staircases, int floorIndex) =>
        floorIndex <= 0
            ? new List<StairwellOpening>()
            : staircases
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
        if (includeBelow && floorIndex > 0)
            adjacent.Add(new AdjacentFloorPlan(
                floorIndex - 1, false, GetFloor(building, floorIndex - 1)));
        if (includeAbove && floorIndex + 1 < FloorCount(building))
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
                Perimeter = building.Perimeter.ToList(),
                Rooms = building.Rooms.Select(r => r.Copy()).ToList(),
                Doors = building.Doors.Select(d => d.Copy()).ToList()
            };

        var floor = building.UpperFloors[floorIndex - 1];
        return new BuildingDocument
        {
            Name = building.Name,
            Perimeter = floor.Perimeter.ToList(),
            Rooms = floor.Rooms.Select(r => r.Copy()).ToList(),
            Doors = floor.Doors.Select(d => d.Copy()).ToList()
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
        }
        else
        {
            var floor = building.UpperFloors[index - 1];
            floor.Perimeter = editedFloor.Perimeter.ToList();
            floor.Rooms = editedFloor.Rooms.Select(r => r.Copy()).ToList();
            floor.Doors = editedFloor.Doors.Select(d => d.Copy()).ToList();
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
        var previous = GetFloor(result, FloorCount(result) - 1);
        if (!PolygonGeometry.IsValidOutline(previous.Perimeter, out reason))
        {
            reason = "Finish a valid perimeter before adding another floor: " + reason;
            return false;
        }
        int index = FloorCount(result);
        result.UpperFloors.Add(new StoreyDefinition
        {
            Name = FloorLabel(index),
            Perimeter = previous.Perimeter.ToList()
        });
        result.SchemaVersion = 2;
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
            reason = "The ground floor cannot be removed.";
            return false;
        }
        int topIndex = FloorCount(result) - 1;
        result.UpperFloors.RemoveAt(result.UpperFloors.Count - 1);
        result.Staircases.RemoveAll(stairs => stairs.LowerFloorIndex >= topIndex - 1);
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
        _ => $"Floor {index + 1}"
    };

    /// <summary>Validates all independent floor plans and each cross-floor stair reservation.</summary>
    public static bool Validate(BuildingDocument building, out string reason)
    {
        reason = "";
        if (building.UpperFloors is null || building.Staircases is null)
        {
            reason = "Floor or staircase collections are missing.";
            return false;
        }
        if (FloorCount(building) > 4)
        {
            reason = "Buildings cannot contain more than four floors.";
            return false;
        }
        if (building.SchemaVersion == 1 &&
            (building.UpperFloors.Count != 0 || building.Staircases.Count != 0))
        {
            reason = "Multi-floor buildings require JSON schema version 2.";
            return false;
        }
        if (building.SchemaVersion is not (1 or 2))
        {
            reason = $"Unsupported document schema version {building.SchemaVersion}.";
            return false;
        }

        for (int i = 0; i < FloorCount(building); i++)
        {
            if (i > 0)
            {
                var floor = building.UpperFloors[i - 1];
                if (floor is null || floor.Perimeter is null ||
                    floor.Rooms is null || floor.Doors is null ||
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
            if (i > 0 && singleFloor.Perimeter.Count == 0)
            {
                reason = $"{FloorLabel(i)} must have a perimeter.";
                return false;
            }
        }

        var ids = new HashSet<Guid>();
        foreach (var stair in building.Staircases)
        {
            if (stair is null || stair.Id == Guid.Empty || !ids.Add(stair.Id) ||
                stair.LowerFloorIndex < 0 || stair.LowerFloorIndex >= FloorCount(building) - 1 ||
                !double.IsFinite(stair.Centre.X) || !double.IsFinite(stair.Centre.Y) ||
                !double.IsFinite(stair.Width) || stair.Width < 0.7 || stair.Width > 5 ||
                !double.IsFinite(stair.Length) || stair.Length < 1.8 || stair.Length > 12 ||
                !double.IsFinite(stair.RotationDegrees) ||
                !double.IsFinite(stair.OpeningExtraWidth) ||
                stair.OpeningExtraWidth < 0 || stair.OpeningExtraWidth > 4 ||
                !double.IsFinite(stair.OpeningExtraLength) ||
                stair.OpeningExtraLength < 0 || stair.OpeningExtraLength > 6)
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
        for (int floorIndex = 1; floorIndex < FloorCount(building); floorIndex++)
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
        if (index < 0 || index >= FloorCount(building))
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
