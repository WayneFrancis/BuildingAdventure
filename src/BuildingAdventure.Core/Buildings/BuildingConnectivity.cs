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
    public bool Fireplaces { get; set; }
    public BuildingUse Purpose { get; set; } = BuildingUse.Residence;

    public GenerationOptions Copy() => new()
    {
        Rooms = Rooms, Corridors = Corridors, Doors = Doors,
        Windows = Windows, Stairs = Stairs, Fireplaces = Fireplaces, Purpose = Purpose
    };

    public bool Any => Rooms || Corridors || Doors || Windows || Stairs || Fireplaces;
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

    /// <summary>
    /// Build a navigable internal door network. An exterior entrance is
    /// permitted only on ground floor; an existing outside door never
    /// counts as a room's required connection to the interior.
    /// </summary>
    public static int AddDoors(BuildingDocument building, int floorIndex,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(building);
        var working = updated;
        int count = 0;
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
            .Where(w => w.Length >= 1.1).ToList();
        var hall = floor.Rooms.FirstOrDefault(r => r.Type.Equals(
            "Hallway", StringComparison.OrdinalIgnoreCase));
        bool hasEntrance = floor.Doors.Any(d => walls.Any(w => w.IsExterior &&
            OnOutline(d.Centre, [w.A, w.B])));

        // Ground-level street entrance: use hall if it reaches outside,
        // otherwise a suitable living/common/shop room.
        if (floorIndex == 0 && !hasEntrance)
        {
            var preferred = floor.Rooms.OrderBy(r =>
                r.Id == hall?.Id ? 0 :
                r.Type is "Living room" or "Common room" or "Shop floor" ? 1 : 2);
            foreach (var room in preferred)
            {
                var candidates = walls.Where(w => w.IsExterior &&
                    OnOutline(Midpoint(w), room.Outline))
                    .OrderByDescending(w => w.Length).ToList();
                if (TryPlaceDoor(candidates, 0.9))
                {
                    hasEntrance = true;
                    break;
                }
            }
        }

        // Connect ALL rooms to the internal plan. In particular, a ground
        // floor living room with a street door must still have a hall door.
        foreach (var room in floor.Rooms.Where(r => r.Id != hall?.Id))
        {
            if (HasConnectedInteriorDoor(room)) continue;
            var candidates = walls.Where(w => !w.IsExterior &&
                OnOutline(Midpoint(w), room.Outline) &&
                floor.Rooms.Any(other => other.Id != room.Id &&
                    OnOutline(Midpoint(w), other.Outline)))
                .OrderBy(w => hall is not null &&
                    OnOutline(Midpoint(w), hall.Outline) ? 0 : 1)
                .ThenByDescending(w => w.Length);
            TryPlaceDoor(candidates, 0.9);
        }

        // Secondary service routes: kitchen <-> dining and kitchen <->
        // storage/pantry, plus direct shared-wall access to the hearth room.
        var kitchen = floor.Rooms.FirstOrDefault(r => r.Type.Equals(
            "Kitchen", StringComparison.OrdinalIgnoreCase));
        if (kitchen is not null)
        {
            foreach (var partner in floor.Rooms.Where(r => r.Id != kitchen.Id &&
                new[] { "Dining room", "Pantry", "Storeroom", "Storage",
                        "Living room", "Common room" }
                    .Contains(r.Type, StringComparer.OrdinalIgnoreCase)))
            {
                var wallCandidates = walls.Where(w => !w.IsExterior &&
                    OnOutline(Midpoint(w), kitchen.Outline) &&
                    OnOutline(Midpoint(w), partner.Outline))
                    .OrderByDescending(w => w.Length);
                bool linked = floor.Doors.Any(d =>
                    OnOutline(d.Centre, kitchen.Outline) &&
                    OnOutline(d.Centre, partner.Outline));
                if (!linked) TryPlaceDoor(wallCandidates, 0.8);
            }
        }
        updated = working;
        return count;

        bool HasConnectedInteriorDoor(RoomDefinition room) =>
            floor.Doors.Any(d => walls.Any(w => !w.IsExterior &&
                OnOutline(d.Centre, [w.A, w.B]) &&
                OnOutline(d.Centre, room.Outline) &&
                floor.Rooms.Any(other => other.Id != room.Id &&
                    OnOutline(d.Centre, other.Outline))));

        bool TryPlaceDoor(IEnumerable<WallSegment> candidates, double width)
        {
            foreach (var wall in candidates)
            foreach (double t in new[] { 0.5, 0.32, 0.68, 0.2, 0.8 })
            {
                var centre = new Point2(wall.A.X + (wall.B.X - wall.A.X) * t,
                    wall.A.Y + (wall.B.Y - wall.A.Y) * t);
                if (floor.Doors.Any(d => d.Centre.DistanceTo(centre) < 0.5))
                    continue;
                var trial = BuildingStoreys.Copy(working);
                var edited = BuildingStoreys.GetFloor(trial, floorIndex);
                edited.Doors.Add(new DoorDefinition { Centre = centre, Width = width });
                BuildingStoreys.SetFloor(trial, floorIndex, edited);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                working = trial;
                floor = edited;
                count++;
                return true;
            }
            return false;
        }
    }

    public static bool RoomUsuallyNeedsWindow(RoomDefinition room) =>
        !new[] { "Hallway", "Corridor", "Stairwell", "Storeroom",
                 "Storage", "Cellar", "Closet", "Cupboard", "Pantry",
                 "Utility", "Boiler room", "Vault", "Dungeon", "Toilet" }
            .Contains(room.Type, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Give each eligible exterior-facing room its own window, subject to
    /// real wall geometry and opening clearance. Interior and storage rooms
    /// are not assigned fictional exterior windows.
    /// </summary>
    public static int AddWindows(BuildingDocument building, int floorIndex,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(building);
        int count = 0;
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var chimneyStacks = updated.Chimneys.ToList();
        var exteriorWalls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
            .Where(w => w.IsExterior && w.Length >= 1.7).ToList();
        foreach (var room in floor.Rooms.Where(RoomUsuallyNeedsWindow))
        {
            // Kitchens are normally glazed, except where a fireplace/hearth
            // occupies the room and supplies the main exterior-wall feature.
            if (room.Type.Equals("Kitchen", StringComparison.OrdinalIgnoreCase) &&
                chimneyStacks.Any(ch =>
                    PolygonGeometry.ContainsPoint(room.Outline, ch.Centre)))
                continue;
            if (floor.Windows.Any(w => OnOutline(w.Centre, room.Outline)))
                continue;
            // A kitchen with a fireplace/chimney may reasonably be
            // windowless; that exception can be modelled with the hearth.
            foreach (var wall in exteriorWalls
                .Where(w => OnOutline(Midpoint(w), room.Outline))
                .OrderByDescending(w => w.Length))
            {
                // Keep glazing away from the stairwell (including beside
                // its corner on the same facade), not just out of it.
                var location = Midpoint(wall);
                if (floor.Rooms.Where(r => r.Type == "Stairwell").Any(r =>
                    Enumerable.Range(0, r.Outline.Count).Any(i =>
                        PolygonGeometry.ClosestPointOnSegment(location,
                            r.Outline[i], r.Outline[(i + 1) % r.Outline.Count])
                            .DistanceTo(location) < 1.5)))
                    continue;
                if (updated.Staircases.Any(s =>
                    (s.LowerFloorIndex == floorIndex ||
                     s.LowerFloorIndex + 1 == floorIndex) &&
                    s.Centre.DistanceTo(location) < 2.3))
                    continue;
                if (chimneyStacks.Any(ch =>
                    FloorGeometry.IsWithinPolygon(
                        BuildingStoreys.ChimneyFootprint(ch), floor.Perimeter) &&
                    ch.Centre.DistanceTo(location) < 1.5))
                    continue;

                var trial = BuildingStoreys.Copy(updated);
                var next = BuildingStoreys.GetFloor(trial, floorIndex);
                next.Windows.Add(new WindowDefinition
                {
                    Centre = location, Width = Math.Min(1.2, wall.Length - 0.4)
                });
                BuildingStoreys.SetFloor(trial, floorIndex, next);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                updated = trial;
                floor = next;
                count++;
                break;
            }
        }
        return count;
    }

    /// <summary>
    /// Try a shared masonry fireplace stack at a ground-floor wall.
    /// Prefer an outside wall but also allow internal positions if geometry
    /// permits. Its fixed plan position carries up and down every floor
    /// whose perimeter covers the entire hearth footprint.
    /// </summary>
    public static bool TryAddChimney(BuildingDocument building,
        out BuildingDocument updated, BuildingUse use = BuildingUse.Residence)
    {
        updated = BuildingStoreys.Copy(building);
        if (updated.Chimneys.Count > 0) return false;
        int ground = BuildingStoreys.HasFloor(building, 0)
            ? 0 : BuildingStoreys.LowestFloorIndex(building);
        var baseFloor = BuildingStoreys.GetFloor(building, ground);
        if (baseFloor.Perimeter.Count < 3) return false;

        // Choose a ROOM before a wall. Heating a hallway or a stair alcove
        // is not a useful medieval domestic plan. A chimney in a partly
        // inhabited basement-only shell may be placed in open floor area.
        string[] roomPriority = [
            "Common room", "Living room", "Kitchen", "Dining room",
            "Parlour", "Great hall", "Shop floor", "Workshop",
            "Bedroom", "Study", "Guest room", "Chamber"
        ];
        var rooms = baseFloor.Rooms.Where(r => roomPriority.Contains(r.Type,
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(r => {
                int index = Array.FindIndex(roomPriority, t =>
                    t.Equals(r.Type, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }).ToList();
        if (rooms.Count == 0 && baseFloor.Rooms.Count == 0)
            rooms.Add(new RoomDefinition {
                Name = "Unpartitioned floor", Type = "Unpartitioned",
                Outline = baseFloor.Perimeter.ToList()
            });
        // Measured medieval great-house kitchen hearths can exceed 3m.
        // Larger buildings get wider schematic hearths; a cottage retains
        // a compact wall fireplace. These are configuration heuristics.
        double hearthWidth = use == BuildingUse.Inn ? 2.4 :
            use == BuildingUse.Mixed ? 1.8 : 1.4;
        double hearthDepth = use == BuildingUse.Inn ? 0.9 : 0.7;
        var allWalls = FloorGeometry.BuildWalls(baseFloor.Perimeter, baseFloor.Rooms)
            .Where(w => w.Length >= hearthWidth + 0.5).ToList();

        foreach (var room in rooms)
        foreach (var wall in allWalls
            .Where(w => OnOutline(Midpoint(w), room.Outline))
            .OrderByDescending(w => w.IsExterior)
            .ThenByDescending(w => w.Length))
        {
            var midpoint = Midpoint(wall);
            double dx = wall.B.X - wall.A.X, dy = wall.B.Y - wall.A.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < hearthWidth + 0.5) continue;
            // Conservative schematic masonry hearth: about 1.4 m along
            // the wall and 0.7 m projecting into the room. Medieval hearth
            // openings ranged from modest to monumental; this is a
            // domestic default, not a universal historic measurement.
            bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
            double w = horizontal ? hearthWidth : hearthDepth;
            double d = horizontal ? hearthDepth : hearthWidth;
            double normalDepth = hearthDepth;
            foreach (int sign in new[] { 1, -1 })
            {
                var centre = new Point2(
                    midpoint.X - sign * dy / len * (normalDepth / 2 + 0.02),
                    midpoint.Y + sign * dx / len * (normalDepth / 2 + 0.02));
                var hearth = new ChimneyDefinition {
                    Centre = centre, Width = w, Depth = d,
                    Exterior = wall.IsExterior
                };
                if (!FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.ChimneyFootprint(hearth), room.Outline))
                    continue;
                if (baseFloor.Rooms.Any(r =>
                    r.Type.Equals("Hallway", StringComparison.OrdinalIgnoreCase) &&
                    PolygonGeometry.ContainsPoint(r.Outline, centre)))
                    continue;
                var candidate = BuildingStoreys.Copy(updated);
                candidate.Chimneys.Add(hearth);
                if (!BuildingStoreys.Validate(candidate, out _)) continue;
                updated = candidate;
                return true;
            }
        }
        return false;
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
        string lastError = "No side bay with sufficient clear space.";
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
        // Compact domestic stairs: 2.8 m flight plus 1 m landing at both
        // ends. The full 4.8 m requirement fits inside a 5 m side bay.
        double pocketLength = 5.0;
        if (hallLength < pocketLength)
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
            // Reserve space between the flight and its doorway for access
            // to the hall, not just enough floor area for the stairs.
            if (sideEnd - sideStart < 2.2) continue;
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
            // The flight hugs the OUTER long wall of its stairwell
            // (0.2 m clearance), with its run toward the end wall.
            // The gap between its other side and the hallway is an
            // accessible landing instead of a staircase in open floor.
            const double wallClearance = 0.20;
            const double halfWidth = 1.0 / 2;
            double sideCentre = leftSide
                ? sideStart + halfWidth + wallClearance
                : sideEnd - halfWidth - wallClearance;
            double runCentre = (from + to) / 2;
            var centre = vertical
                ? new Point2(sideCentre, runCentre)
                : new Point2(runCentre, sideCentre);
            var candidate = BuildingStoreys.Copy(source);
            bool roomsFit = true;
            foreach (int index in levels)
            {
                var floor = BuildingStoreys.GetFloor(candidate, index);
                var matchingHall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
                if (matchingHall is null ||
                    !ShareWall(outline, matchingHall.Outline, 2.0))
                {
                    lastError = $"Hall/stair adjacency mismatch on floor {index}; " +
                        $"hall={matchingHall?.Outline.Count ?? 0} vertices; " +
                        $"stair bay={sideStart:0.#}..{sideEnd:0.#} / {from:0.#}..{to:0.#}.";
                    roomsFit = false;
                    break;
                }
                floor.Rooms.Add(new RoomDefinition
                {
                    Name = "Stairwell", Type = "Stairwell",
                    Outline = outline.ToList()
                });
                BuildingStoreys.SetFloor(candidate, index, floor);
                if (BuildingStoreys.Validate(candidate, out var invalid)) continue;
                lastError = invalid;
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
                    Length = 2.8, Width = 1.0,
                    LandingLength = 1.0,
                    RotationDegrees = vertical ? 0 : 90
                };
                if (!BuildingStoreys.TryAddStaircase(candidate, stair,
                        out var placed, out var invalid))
                {
                    lastError = invalid;
                    break;
                }
                candidate = placed;
                added++;
            }
            if (added != levels.Count - 1) continue;
            updated = candidate;
            flights = added;
            return true;
        }
        reason = "No valid stair bay could fit beside the corridor: " + lastError +
            $"; perimeter=({minX:0.##},{minY:0.##})-({maxX:0.##},{maxY:0.##}), " +
            $"hall=({hLeft:0.##},{hTop:0.##})-({hRight:0.##},{hBottom:0.##}), " +
            $"vertical={vertical}.";
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
            // Candidate locations follow perimeter / stairwell walls rather
            // than sampling the geometric centre of the building or a room.
            foreach (var (centre, angle) in WallAnchoredStairCandidates(low, stairwell))
            {
                var stair = new StaircaseDefinition
                {
                    LowerFloorIndex = lower,
                    Centre = centre,
                    Length = 3.6, Width = 1.4, LandingLength = 1.0,
                    RotationDegrees = angle
                };
                if (!BuildingStoreys.TryAddStaircase(updated, stair,
                        out var candidate, out _))
                    continue;
                updated = candidate;
                count++;
                break;
            }
        }
        return count;
    }

    private static IEnumerable<(Point2 Centre, double Angle)>
        WallAnchoredStairCandidates(BuildingDocument floor, RoomDefinition? stairwell)
    {
        var footprint = stairwell?.Outline ?? floor.Perimeter;
        if (footprint.Count < 3) yield break;
        double minX = footprint.Min(p => p.X), maxX = footprint.Max(p => p.X);
        double minY = footprint.Min(p => p.Y), maxY = footprint.Max(p => p.Y);
        // All candidates position the edge of a 1.4m-wide flight 0.2m
        // inside a wall. There must still be enough length for a 3.6m run.
        const double offset = 0.9; // 1.4/2 + 0.2m clearance
        const double endOffset = 2.0; // 3.6/2 + 0.2m clearance
        var hallway = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
        double hallX = hallway?.Outline.Average(p => p.X) ?? (minX + maxX) / 2;
        double hallY = hallway?.Outline.Average(p => p.Y) ?? (minY + maxY) / 2;
        if (maxY - minY >= 2 * endOffset && maxX - minX >= 2 * offset)
        {
            // When a dedicated stairwell exists, use the side furthest
            // from its hallway, keeping the hall-side floor as a landing.
            var sides = new[] { minX + offset, maxX - offset }
                .OrderByDescending(x => Math.Abs(x - hallX));
            foreach (double x in sides)
            foreach (double y in new[]
                { minY + endOffset, maxY - endOffset, (minY + maxY) / 2 })
                yield return (new Point2(x, y), 0);
        }
        if (maxX - minX >= 2 * endOffset && maxY - minY >= 2 * offset)
        {
            var sides = new[] { minY + offset, maxY - offset }
                .OrderByDescending(y => Math.Abs(y - hallY));
            foreach (double y in sides)
            foreach (double x in new[]
                { minX + endOffset, maxX - endOffset, (minX + maxX) / 2 })
                yield return (new Point2(x, y), 90);
        }
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
