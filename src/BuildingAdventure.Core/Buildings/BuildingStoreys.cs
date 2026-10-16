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

    public StaircaseDefinition Copy() => new()
    {
        Id = Id,
        LowerFloorIndex = LowerFloorIndex,
        Centre = Centre,
        Length = Length,
        Width = Width,
        RotationDegrees = RotationDegrees
    };
}

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
                !double.IsFinite(stair.RotationDegrees))
            {
                reason = "Invalid staircase location, floor connection, or dimensions.";
                return false;
            }

            var footprint = StairFootprint(stair);
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
