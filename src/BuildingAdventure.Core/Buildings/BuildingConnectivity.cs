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
    public BuildingUse Purpose { get; set; } = BuildingUse.Residence;

    public GenerationOptions Copy() => new()
    {
        Rooms = Rooms, Corridors = Corridors, Doors = Doors,
        Windows = Windows, Stairs = Stairs, Purpose = Purpose
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

    /// <summary>
    /// Reserve one aligned, hall-accessible stairwell on every floor of a new
    /// building before other rooms are laid out. Stairs run alongside the
    /// corridor at its far end, not across the corridor's walking route.
    /// All edits are validated on detached building copies.
    /// </summary>
    public static bool TryReserveStackedStairwell(BuildingDocument source,
        out BuildingDocument updated, out int flights, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        flights = 0;
        reason = "";
        var levels = BuildingStoreys.FloorIndexes(source);
        if (levels.Count < 2)
        {
            reason = "At least two floors are needed for connected stairs.";
            return false;
        }
        var ground = BuildingStoreys.GetFloor(source, levels[0]);
        var hall = ground.Rooms.FirstOrDefault(r => r.Type == "Hallway");
        if (hall is null || hall.Outline.Count != 4)
        {
            reason = "An open central hallway is required to reserve a stairwell.";
            return false;
        }
        double hLeft = hall.Outline.Min(p => p.X);
        double hRight = hall.Outline.Max(p => p.X);
        double hTop = hall.Outline.Min(p => p.Y);
        double hBottom = hall.Outline.Max(p => p.Y);
        bool vertical = hBottom - hTop >= hRight - hLeft;
        double minX = ground.Perimeter.Min(p => p.X);
        double maxX = ground.Perimeter.Max(p => p.X);
        double minY = ground.Perimeter.Min(p => p.Y);
        double maxY = ground.Perimeter.Max(p => p.Y);
        double hallLength = vertical ? hBottom - hTop : hRight - hLeft;
        // A 3.6 m run needs space at both ends for access / landings.
        double pocketLength = Math.Floor(Math.Min(5.0, hallLength) * 2) / 2;
        if (pocketLength < 4.5)
        {
            reason = "The hall is too short to reserve a landing beside the stairs.";
            return false;
        }

        // Prefer the far end of the hall, leaving its main entrance unobstructed;
        // try both sides, then the near end on irregular footprints.
        foreach (bool farEnd in new[] { true, false })
        foreach (bool leftSide in new[] { true, false })
        {
            double sideStart = vertical
                ? (leftSide ? minX : hRight)
                : (leftSide ? minY : hBottom);
            double sideEnd = vertical
                ? (leftSide ? hLeft : maxX)
                : (leftSide ? hTop : maxY);
            if (sideEnd - sideStart < 2.5) continue;
            double axisStart = vertical ? hTop : hLeft;
            double axisEnd = vertical ? hBottom : hRight;
            double from = farEnd ? axisEnd - pocketLength : axisStart;
            double to = from + pocketLength;
            var outline = vertical
                ? new List<Point2>
                {
                    new(sideStart, from), new(sideEnd, from),
                    new(sideEnd, to), new(sideStart, to)
                }
                : new List<Point2>
                {
                    new(from, sideStart), new(to, sideStart),
                    new(to, sideEnd), new(from, sideEnd)
                };
            var centre = vertical
                ? new Point2((sideStart + sideEnd) / 2, (from + to) / 2)
                : new Point2((from + to) / 2, (sideStart + sideEnd) / 2);
            var candidate = BuildingStoreys.Copy(source);
            bool roomsFit = true;
            foreach (int index in levels)
            {
                var floor = BuildingStoreys.GetFloor(candidate, index);
                var matchingHall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
                if (matchingHall is null ||
                    !ShareWall(outline, matchingHall.Outline, 2.0))
                {
                    roomsFit = false;
                    break;
                }
                floor.Rooms.Add(new RoomDefinition
                {
                    Name = "Stairwell", Type = "Stairwell",
                    Outline = outline.ToList()
                });
                BuildingStoreys.SetFloor(candidate, index, floor);
                if (BuildingStoreys.Validate(candidate, out _)) continue;
                roomsFit = false;
                break;
            }
            if (!roomsFit) continue;

            int added = 0;
            foreach (int lower in levels.Take(levels.Count - 1))
            {
                var stair = new StaircaseDefinition
                {
                    LowerFloorIndex = lower,
                    Centre = centre,
                    Length = 3.6, Width = 1.4,
                    RotationDegrees = vertical ? 0 : 90
                };
                if (!BuildingStoreys.TryAddStaircase(candidate, stair,
                        out var placed, out _))
                    break;
                candidate = placed;
                added++;
            }
            if (added != levels.Count - 1) continue;
            updated = candidate;
            flights = added;
            return true;
        }
        reason = "No valid stairwell could fit beside a corridor on every floor. " +
            "The generator did not put stairs in the main hallway.";
        return false;
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
            var stairwell = low.Rooms.FirstOrDefault(r =>
                r.Type == "Stairwell" &&
                upper.Rooms.Any(u => u.Type == "Stairwell" &&
                    u.Outline.SequenceEqual(r.Outline)));
            // Do not obstruct an existing hall. For fully empty plans retain
            // the original central location as a sensible starting point.
            if (hallway is not null && stairwell is null) continue;
            var preferred = stairwell is null ? new Point2(
                low.Perimeter.Average(p => p.X),
                low.Perimeter.Average(p => p.Y)) : new Point2(
                stairwell.Outline.Average(p => p.X),
                stairwell.Outline.Average(p => p.Y));
            bool placed = false;
            foreach (double offset in stairwell is null
                ? new double[] { 0, -2, 2, -3, 3 }
                : new double[] { 0 })
            {
                if (placed) break;
                foreach (double angle in stairwell is null
                    ? new double[] { 0, 90 }
                    : stairwell.Outline.Max(p => p.Y) - stairwell.Outline.Min(p => p.Y) >=
                      stairwell.Outline.Max(p => p.X) - stairwell.Outline.Min(p => p.X)
                        ? new double[] { 0 }
                        : new double[] { 90 })
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
