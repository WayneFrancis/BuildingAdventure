using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Independently selectable passes for non-destructive building suggestions.
/// Defaults are conservative; stairs/windows require explicit opt-in.
/// </summary>
public sealed class GenerationOptions
{
    public bool Rooms { get; set; } = true;
    public bool Corridors { get; set; } = true;
    public bool Doors { get; set; } = true;
    public bool Windows { get; set; }
    public bool Stairs { get; set; }

    public GenerationOptions Copy() => new()
    {
        Rooms = Rooms, Corridors = Corridors, Doors = Doors,
        Windows = Windows, Stairs = Stairs
    };

    public bool Any => Rooms || Corridors || Doors || Windows || Stairs;
}

/// <summary>
/// Candidate geometry helpers. Every proposed edit is validated on a
/// detached document; invalid candidates never damage a working building.
/// </summary>
public static class BuildingConnectivity
{
    private const double Tolerance = 1e-5;

    public static bool TryAddCorridor(BuildingDocument building, int floorIndex,
        out BuildingDocument updated, out Guid corridorId)
    {
        updated = BuildingStoreys.Copy(building);
        corridorId = Guid.Empty;
        var floor = BuildingStoreys.GetFloor(building, floorIndex);
        if (floor.Perimeter.Count < 4 ||
            floor.Rooms.Any(r => r.Type.Equals("Hallway",
                StringComparison.OrdinalIgnoreCase)))
            return false;

        int left = (int)Math.Ceiling(floor.Perimeter.Min(p => p.X) * 2);
        int right = (int)Math.Floor(floor.Perimeter.Max(p => p.X) * 2);
        int top = (int)Math.Ceiling(floor.Perimeter.Min(p => p.Y) * 2);
        int bottom = (int)Math.Floor(floor.Perimeter.Max(p => p.Y) * 2);
        int width = right - left, height = bottom - top;
        if (width < 12 || height < 12) return false;

        bool vertical = height >= width;
        int minorLow = vertical ? left : top;
        int minorHigh = vertical ? right : bottom;
        int centre = (minorLow + minorHigh - 4) / 2;
        int[] offsets = [0, -2, 2, -4, 4, -6, 6];
        foreach (int offset in offsets)
        {
            int corridorStart = centre + offset;
            if (corridorStart < minorLow + 2 || corridorStart + 4 > minorHigh - 2)
                continue;
            double a = corridorStart / 2.0;
            double b = (corridorStart + 4) / 2.0;
            double fullLow = (vertical ? top : left) / 2.0;
            double fullHigh = (vertical ? bottom : right) / 2.0;
            List<Point2> shape = vertical
                ? [new(a, fullLow), new(b, fullLow),
                   new(b, fullHigh), new(a, fullHigh)]
                : [new(fullLow, a), new(fullHigh, a),
                   new(fullHigh, b), new(fullLow, b)];
            var candidate = BuildingStoreys.Copy(building);
            var next = BuildingStoreys.GetFloor(candidate, floorIndex);
            var hallway = new RoomDefinition
            {
                Name = $"Hallway {next.Rooms.Count + 1}",
                Type = "Hallway", Outline = shape
            };
            next.Rooms.Add(hallway);
            BuildingStoreys.SetFloor(candidate, floorIndex, next);
            if (!BuildingStoreys.Validate(candidate, out _)) continue;
            updated = candidate;
            corridorId = hallway.Id;
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when polygons actually share a nontrivial straight wall.
    /// Corner-only touching is not enough to install a door.
    /// </summary>
    public static bool ShareWall(IReadOnlyList<Point2> first,
        IReadOnlyList<Point2> second, double minimumLength = 1.2)
    {
        for (int i = 0; i < first.Count; i++)
        for (int j = 0; j < second.Count; j++)
        {
            var a = first[i];
            var b = first[(i + 1) % first.Count];
            var c = second[j];
            var d = second[(j + 1) % second.Count];
            double length = a.DistanceTo(b);
            if (length < minimumLength) continue;
            double ux = (b.X - a.X) / length, uy = (b.Y - a.Y) / length;
            double crossC = Math.Abs((c.X - a.X) * uy - (c.Y - a.Y) * ux);
            double crossD = Math.Abs((d.X - a.X) * uy - (d.Y - a.Y) * ux);
            if (crossC > Tolerance || crossD > Tolerance) continue;
            double cDist = (c.X - a.X) * ux + (c.Y - a.Y) * uy;
            double dDist = (d.X - a.X) * ux + (d.Y - a.Y) * uy;
            double overlap = Math.Min(length, Math.Max(cDist, dDist)) -
                Math.Max(0, Math.Min(cDist, dDist));
            if (overlap >= minimumLength - Tolerance) return true;
        }
        return false;
    }

    public static int AddDoors(BuildingDocument building, int floorIndex,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(building);
        int count = 0;
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
            .Where(w => w.Length >= 1.25).ToList();
        var hallway = floor.Rooms.FirstOrDefault(r =>
            r.Type.Equals("Hallway", StringComparison.OrdinalIgnoreCase));
        bool hasExteriorEntrance = floor.Doors.Any(d =>
            walls.Any(w => w.IsExterior && OnOutline(d.Centre, [w.A, w.B])));
        // Hallways first: connecting them to the exterior establishes an
        // entrance without turning every room into an exterior doorway.
        var ordered = floor.Rooms.OrderBy(r => hallway is not null &&
            r.Id == hallway.Id ? 0 : 1).ToList();
        foreach (var room in ordered)
        {
            if (HasDoorOnRoom(room, floor.Doors)) continue;
            var candidates = walls.Where(w =>
                    OnOutline(Midpoint(w), room.Outline))
                .OrderBy(w => w.IsExterior && !hasExteriorEntrance ? 0 :
                    hallway is not null && room.Id != hallway.Id &&
                    OnOutline(Midpoint(w), hallway.Outline) ? 1 :
                    !w.IsExterior ? 2 : 3)
                .ThenByDescending(w => w.Length)
                .ToList();
            foreach (var wall in candidates)
            {
                if (room.Id == hallway?.Id && !wall.IsExterior) continue;
                var trial = BuildingStoreys.Copy(updated);
                var edited = BuildingStoreys.GetFloor(trial, floorIndex);
                edited.Doors.Add(new DoorDefinition
                {
                    Centre = Midpoint(wall), Width = 0.9
                });
                BuildingStoreys.SetFloor(trial, floorIndex, edited);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                updated = trial;
                floor = edited;
                if (wall.IsExterior) hasExteriorEntrance = true;
                count++;
                break;
            }
        }
        // An existing plan may already have internal doorways but no entry
        // from outdoors. Do not mistake internal connectivity for access.
        if (!hasExteriorEntrance && floor.Rooms.Count > 0)
        {
            foreach (var wall in walls.Where(w => w.IsExterior)
                .OrderByDescending(w => w.Length))
            {
                Point2 centre = Midpoint(wall);
                if (!floor.Rooms.Any(r => OnOutline(centre, r.Outline)))
                    continue;
                var candidate = BuildingStoreys.Copy(updated);
                var candidateFloor = BuildingStoreys.GetFloor(candidate, floorIndex);
                candidateFloor.Doors.Add(new DoorDefinition
                {
                    Centre = centre, Width = 0.9
                });
                BuildingStoreys.SetFloor(candidate, floorIndex, candidateFloor);
                if (!BuildingStoreys.Validate(candidate, out _)) continue;
                updated = candidate;
                count++;
                break;
            }
        }
        return count;
    }

    public static int AddWindows(BuildingDocument building, int floorIndex,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(building);
        int count = 0;
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
            .Where(w => w.IsExterior && w.Length >= 2).OrderByDescending(w => w.Length);
        foreach (var wall in walls)
        {
            if (count >= 2) break;
            var midpoint = Midpoint(wall);
            if (!floor.Rooms.Any(r =>
                !r.Type.Equals("Hallway", StringComparison.OrdinalIgnoreCase) &&
                OnOutline(midpoint, r.Outline)))
                continue;
            var trial = BuildingStoreys.Copy(updated);
            var candidate = BuildingStoreys.GetFloor(trial, floorIndex);
            candidate.Windows.Add(new WindowDefinition
            {
                Centre = midpoint, Width = 1.2
            });
            BuildingStoreys.SetFloor(trial, floorIndex, candidate);
            if (!BuildingStoreys.Validate(trial, out _)) continue;
            updated = trial;
            floor = candidate;
            count++;
        }
        return count;
    }

    public static int AddStairs(BuildingDocument building,
        IReadOnlyCollection<int> selectedFloors, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(building);
        int count = 0;
        var set = selectedFloors.ToHashSet();
        foreach (int lower in BuildingStoreys.FloorIndexes(updated))
        {
            if (!set.Contains(lower) || !set.Contains(lower + 1) ||
                !BuildingStoreys.HasFloor(updated, lower + 1) ||
                updated.Staircases.Any(s => s.LowerFloorIndex == lower))
                continue;
            var low = BuildingStoreys.GetFloor(updated, lower);
            var upper = BuildingStoreys.GetFloor(updated, lower + 1);
            if (low.Perimeter.Count < 3 || upper.Perimeter.Count < 3)
                continue;
            var hallway = low.Rooms.FirstOrDefault(r => r.Type == "Hallway");
            var preferred = hallway is null ? new Point2(
                low.Perimeter.Average(p => p.X),
                low.Perimeter.Average(p => p.Y)) : new Point2(
                hallway.Outline.Average(p => p.X),
                hallway.Outline.Average(p => p.Y));
            bool placed = false;
            foreach (double offset in new double[] { 0, -2, 2, -3, 3 })
            {
                if (placed) break;
                foreach (double angle in new double[] { 0, 90 })
                {
                    var stair = new StaircaseDefinition
                    {
                        LowerFloorIndex = lower,
                        Centre = new Point2(preferred.X,
                            preferred.Y + offset),
                        Length = 3.6, Width = 1.4,
                        RotationDegrees = angle
                    };
                    if (!BuildingStoreys.TryAddStaircase(updated, stair,
                        out var candidate, out _))
                        continue;
                    updated = candidate;
                    count++;
                    placed = true;
                    break;
                }
            }
        }
        return count;
    }

    public static int CountRoomsWithoutDoors(BuildingDocument building,
        int floorIndex)
    {
        var floor = BuildingStoreys.GetFloor(building, floorIndex);
        return floor.Rooms.Count(r => !HasDoorOnRoom(r, floor.Doors));
    }

    private static bool HasDoorOnRoom(RoomDefinition room, List<DoorDefinition> doors) =>
        doors.Any(door => OnOutline(door.Centre, room.Outline));

    private static Point2 Midpoint(WallSegment wall) =>
        new((wall.A.X + wall.B.X) / 2, (wall.A.Y + wall.B.Y) / 2);

    private static bool OnOutline(Point2 point, IReadOnlyList<Point2> outline)
    {
        for (int i = 0; i < outline.Count; i++)
            if (PolygonGeometry.ClosestPointOnSegment(point,
                    outline[i], outline[(i + 1) % outline.Count])
                .DistanceTo(point) < Tolerance)
                return true;
        return false;
    }
}
