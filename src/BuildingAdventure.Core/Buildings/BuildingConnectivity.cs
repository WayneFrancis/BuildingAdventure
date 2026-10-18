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
    // Opt-in historical archetype. Older API callers keep the legacy
    // corridor generator; the desktop editor enables this by default
    // for residence suggestions. Non-residences are unaffected.
    public bool HallHouseLayout { get; set; }
    public BuildingUse Purpose { get; set; } = BuildingUse.Residence;

    public GenerationOptions Copy() => new()
    {
        Rooms = Rooms, Corridors = Corridors, Doors = Doors,
        Windows = Windows, Stairs = Stairs, Fireplaces = Fireplaces,
        HallHouseLayout = HallHouseLayout, Purpose = Purpose
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

        // Use *exact* drawn exterior extents. Previously ceil/floor to
        // half-metre increments left a visible strip at both ends of the
        // hallway on imperial and custom-sized perimeters.
        double left = floor.Perimeter.Min(p => p.X);
        double right = floor.Perimeter.Max(p => p.X);
        double top = floor.Perimeter.Min(p => p.Y);
        double bottom = floor.Perimeter.Max(p => p.Y);
        double width = right - left, height = bottom - top;
        if (width < 6 || height < 6) return false;

        bool vertical = height >= width;
        double minorLow = vertical ? left : top;
        double minorHigh = vertical ? right : bottom;
        double centre = (minorLow + minorHigh - 2.0) / 2.0;
        double[] offsets = [0, -1, 1, -2, 2, -3, 3];
        foreach (double offset in offsets)
        {
            double a = centre + offset;
            double b = a + 2.0;
            if (a < minorLow + 1 || b > minorHigh - 1)
                continue;
            double fullLow = vertical ? top : left;
            double fullHigh = vertical ? bottom : right;
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

        // A floor with a stepped/notched perimeter may not contain a
        // corridor spanning its complete bounding box. Search shorter
        // axis-aligned interior sections between real perimeter vertices
        // rather than giving up and leaving the new floor unpartitioned.
        foreach (bool useVertical in new[] { vertical, !vertical })
        {
            double axisMin = useVertical ? top : left;
            double axisMax = useVertical ? bottom : right;
            double minorMin = useVertical ? left : top;
            double minorMax = useVertical ? right : bottom;
            var cuts = floor.Perimeter.Select(p => useVertical ? p.Y : p.X)
                .Append(axisMin).Append(axisMax).Distinct()
                .OrderBy(p => p).ToArray();
            var intervals = new List<(double Start, double End)>();
            for (int first = 0; first < cuts.Length; first++)
            for (int last = first + 1; last < cuts.Length; last++)
                if (cuts[last] - cuts[first] >= 5)
                    intervals.Add((cuts[first], cuts[last]));

            foreach (var (start, end) in intervals
                .OrderByDescending(s => s.End - s.Start))
            foreach (double offset in new double[] { 0, -1, 1, -2, 2, -3, 3 })
            {
                double near = (minorMin + minorMax - 2) / 2 + offset;
                double far = near + 2;
                if (near < minorMin || far > minorMax) continue;
                List<Point2> polygon = useVertical
                    ? [new(near,start),new(far,start),
                       new(far,end),new(near,end)]
                    : [new(start,near),new(end,near),
                       new(end,far),new(start,far)];
                if (!FloorGeometry.IsWithinPolygon(polygon, floor.Perimeter))
                    continue;
                var candidate = BuildingStoreys.Copy(building);
                var next = BuildingStoreys.GetFloor(candidate, floorIndex);
                var hallway = new RoomDefinition
                {
                    Name = $"Hallway {next.Rooms.Count + 1}",
                    Type = "Hallway", Outline = polygon
                };
                next.Rooms.Add(hallway);
                BuildingStoreys.SetFloor(candidate, floorIndex, next);
                if (!BuildingStoreys.Validate(candidate, out _)) continue;
                updated = candidate;
                corridorId = hallway.Id;
                return true;
            }
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
            bool serviceStore = IsServiceStore(room);
            var candidates = walls.Where(w => !w.IsExterior &&
                OnOutline(Midpoint(w), room.Outline) &&
                floor.Rooms.Any(other => other.Id != room.Id &&
                    OnOutline(Midpoint(w), other.Outline)))
                // A storage room usually has ONE purposeful entrance:
                // directly off the kitchen/workshop if it serves that
                // activity, or off circulation otherwise. Never make a
                // bedroom the preferred way into general household stores.
                .OrderBy(w =>
                {
                    var neighbours = floor.Rooms.Where(other =>
                        other.Id != room.Id &&
                        OnOutline(Midpoint(w), other.Outline)).ToArray();
                    if (serviceStore && neighbours.Any(other =>
                        other.Type is "Kitchen" or "Workshop" or "Bakehouse" or
                            "Brewery" or "Brewhouse" or "Scullery")) return 0;
                    if (neighbours.Any(other => other.Id == hall?.Id)) return 1;
                    if (neighbours.Any(other => other.Type is "Living room" or
                            "Dining room" or "Great hall" or "Common room")) return 2;
                    if (neighbours.Any(other => IsServiceStore(other))) return 4;
                    if (neighbours.Any(other => other.Type is "Bedroom" or
                            "Guest room" or "Suite")) return 6;
                    return 3;
                })
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
                // A storeroom with an existing internal entrance is
                // already accessible: do not punch a second door into it
                // just because the kitchen happens to share another wall.
                if (!linked && (!IsServiceStore(partner) ||
                    !HasConnectedInteriorDoor(partner)))
                    TryPlaceDoor(wallCandidates, 0.8);
            }
        }
        updated = working;
        return count;

        static bool IsServiceStore(RoomDefinition room) =>
            room.Type is "Storeroom" or "Storage" or "Stockroom" or
                "Pantry" or "Buttery" or "Linen store" or "Closet";

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
                // Stair reservations are placed before interior doors.
                // A doorway touching a flight or landing would otherwise
                // make the room nominally accessible but unusable.
                if (working.Staircases.Any(stair =>
                    (stair.LowerFloorIndex == floorIndex ||
                     stair.LowerFloorIndex + 1 == floorIndex) &&
                    new[] { BuildingStoreys.StairFootprint(stair) }
                        .Concat(BuildingStoreys.StairLandings(stair))
                        .Any(shape => PolygonGeometry.ContainsPoint(shape, centre) ||
                            Enumerable.Range(0, shape.Count).Any(i =>
                                PolygonGeometry.ClosestPointOnSegment(
                                    centre, shape[i],
                                    shape[(i + 1) % shape.Count])
                                    .DistanceTo(centre) < width / 2 + 0.1))))
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
                var proposedWindow = new WindowDefinition
                {
                    Centre = location, Width = Math.Min(1.2, wall.Length - 0.4)
                };
                var current = updated;
                if (chimneyStacks.Any(ch =>
                    BuildingStoreys.WindowConflictsWithChimney(
                        current, floorIndex, proposedWindow, ch)))
                    continue;

                var trial = BuildingStoreys.Copy(updated);
                var next = BuildingStoreys.GetFloor(trial, floorIndex);
                next.Windows.Add(proposedWindow);
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
        var working = updated;
        if (updated.Chimneys.Count > 0) return false;
        int ground = BuildingStoreys.HasFloor(building, 0)
            ? 0 : BuildingStoreys.LowestFloorIndex(building);
        var baseFloor = BuildingStoreys.GetFloor(building, ground);
        if (baseFloor.Perimeter.Count < 3) return false;

        double hearthWidth = use == BuildingUse.Inn ? 2.4 :
            use == BuildingUse.Mixed ? 1.8 : 1.4;
        double hearthDepth = use == BuildingUse.Inn ? 0.9 : 0.7;
        var allWalls = FloorGeometry.BuildWalls(baseFloor.Perimeter, baseFloor.Rooms)
            .Where(w => w.Length >= hearthWidth + 0.4).ToList();

        var kitchen = baseFloor.Rooms.FirstOrDefault(r =>
            r.Type.Equals("Kitchen", StringComparison.OrdinalIgnoreCase));
        var living = baseFloor.Rooms.FirstOrDefault(r =>
            r.Type.Equals("Living room", StringComparison.OrdinalIgnoreCase) ||
            r.Type.Equals("Common room", StringComparison.OrdinalIgnoreCase));

        // First seek an honest two-sided fireplace across a shared kitchen/
        // living wall. Both hearths are on the inhabited floor and use
        // one continuous chimney; no bedroom upstairs gets a fireplace.
        if (kitchen is not null && living is not null)
        {
            foreach (var wall in allWalls.Where(w => !w.IsExterior &&
                OnOutline(Midpoint(w), kitchen.Outline) &&
                OnOutline(Midpoint(w), living.Outline))
                .OrderByDescending(w => w.Length))
            foreach (double t in new[] { 0.25, 0.75, 0.5 })
            foreach (int sign in new[] { 1, -1 })
            {
                var pair = TryHearth(wall, t, sign, kitchen);
                if (pair is null) continue;
                double len = wall.Length;
                var secondary = new Point2(
                    pair.Centre.X + sign * 2 * (wall.B.Y - wall.A.Y) / len *
                        (hearthDepth / 2 + 0.02),
                    pair.Centre.Y - sign * 2 * (wall.B.X - wall.A.X) / len *
                        (hearthDepth / 2 + 0.02));
                // The secondary hearth faces into the other room, leaving
                // its masonry stack and vertical flue on the kitchen side.
                var other = pair.Copy();
                other.Centre = secondary;
                if (!FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.ChimneyFootprint(other), living.Outline))
                    continue;
                pair.OtherHearthCentre = secondary;
                pair.HearthRoomIds.Add(living.Id);
                if (TryCommit(pair)) { updated = working; return true; }
            }
        }

        string[] priority = [
            "Kitchen", "Common room", "Living room", "Dining room",
            "Parlour", "Great hall", "Shop floor", "Workshop",
            "Bedroom", "Study", "Guest room", "Chamber"
        ];
        var rooms = baseFloor.Rooms.Where(r => priority.Contains(r.Type,
            StringComparer.OrdinalIgnoreCase))
            .OrderBy(r => Array.FindIndex(priority, p =>
                p.Equals(r.Type, StringComparison.OrdinalIgnoreCase))).ToList();
        if (rooms.Count == 0 && baseFloor.Rooms.Count == 0)
            rooms.Add(new RoomDefinition
            {
                Name = "Unpartitioned", Type = "Unpartitioned",
                Outline = baseFloor.Perimeter.ToList()
            });

        // If no suitable aligned shared wall exists, place a single hearth
        // against an outer wall. Such a flue can rise against upper-storey
        // exterior walls instead of passing through a bedroom's centre.
        foreach (var room in rooms)
        foreach (var wall in allWalls
            .Where(w => OnOutline(Midpoint(w), room.Outline))
            .OrderByDescending(w => w.IsExterior)
            .ThenByDescending(w => w.Length))
        foreach (double t in new[] { 0.5, 0.25, 0.75 })
        foreach (int sign in new[] { 1, -1 })
        {
            var candidate = TryHearth(wall, t, sign, room);
            if (candidate is not null && TryCommit(candidate))
            { updated = working; return true; }
        }
        return false;

        ChimneyDefinition? TryHearth(WallSegment wall, double t, int sign,
            RoomDefinition room)
        {
            double dx = wall.B.X - wall.A.X, dy = wall.B.Y - wall.A.Y;
            double len = wall.Length;
            if (len < hearthWidth + 0.4) return null;
            double x = wall.A.X + dx * t;
            double y = wall.A.Y + dy * t;
            double away = hearthDepth / 2 + 0.02;
            var candidate = new ChimneyDefinition
            {
                Centre = new Point2(x - sign * dy / len * away,
                    y + sign * dx / len * away),
                Width = Math.Abs(dx) >= Math.Abs(dy) ? hearthWidth : hearthDepth,
                Depth = Math.Abs(dx) >= Math.Abs(dy) ? hearthDepth : hearthWidth,
                Exterior = wall.IsExterior,
                HearthFloorIndex = ground,
                HearthRoomIds = room.Type == "Unpartitioned" ? [] : [room.Id]
            };
            return FloorGeometry.IsWithinPolygon(
                BuildingStoreys.ChimneyFootprint(candidate), room.Outline)
                ? candidate : null;
        }

        bool TryCommit(ChimneyDefinition chimney)
        {
            // All inhabited upper levels must retain the stack adjacent to
            // an actual wall. Reject a new flue cutting through the middle
            // of an unrelated upstairs bedroom.
            foreach (int index in BuildingStoreys.FloorIndexes(building))
            {
                if (index == ground) continue;
                var floor = BuildingStoreys.GetFloor(building, index);
                if (!FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.ChimneyFootprint(chimney), floor.Perimeter))
                    continue;
                bool nearWall = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
                    .Any(w => PolygonGeometry.ClosestPointOnSegment(
                        chimney.Centre, w.A, w.B).DistanceTo(chimney.Centre) <=
                        Math.Max(chimney.Width, chimney.Depth) / 2 + 0.1);
                if (!nearWall) return false;
            }
            var trial = BuildingStoreys.Copy(building);
            trial.Chimneys.Add(chimney);
            if (!BuildingStoreys.Validate(trial, out _)) return false;
            working = trial;
            return true;
        }
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

    /// <summary>
    /// Plan two EMPTY storeys as one connected circulation problem.
    /// Independently centring a full-width corridor on each footprint
    /// can shift the bands apart when upstairs is inset or smaller.
    /// Both separately valid corridors can then have no common 0.9m-wide
    /// flight + 1m landings, even though the floor area is ample.
    ///
    /// Try aligned corridor bands, retaining each floor's own long-axis
    /// endpoints, and accept a trial only after a staircase validates on
    /// BOTH levels. No caller document is mutated on failure.
    /// </summary>
    public static bool TryAddAlignedHallwaysAndStairs(BuildingDocument source,
        int lower, out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "";
        if (!BuildingStoreys.HasFloor(source, lower) ||
            !BuildingStoreys.HasFloor(source, lower + 1))
        {
            reason = "The two adjoining floors do not exist.";
            return false;
        }

        var down = BuildingStoreys.GetFloor(source, lower);
        var up = BuildingStoreys.GetFloor(source, lower + 1);
        if (down.Rooms.Count > 0 || up.Rooms.Count > 0)
        {
            reason = "Aligned blank-floor planning is only available before either floor has rooms.";
            return false;
        }
        if (down.Perimeter.Count < 3 || up.Perimeter.Count < 3)
        {
            reason = "Both floors must have an exterior perimeter.";
            return false;
        }
        double xMin = Math.Max(down.Perimeter.Min(p => p.X),
            up.Perimeter.Min(p => p.X));
        double xMax = Math.Min(down.Perimeter.Max(p => p.X),
            up.Perimeter.Max(p => p.X));
        double yMin = Math.Max(down.Perimeter.Min(p => p.Y),
            up.Perimeter.Min(p => p.Y));
        double yMax = Math.Min(down.Perimeter.Max(p => p.Y),
            up.Perimeter.Max(p => p.Y));
        if (xMax - xMin < 2.0 || yMax - yMin < 2.0)
        {
            reason = "The two floor perimeters do not share enough interior area.";
            return false;
        }

        // The common corridor CROSS-band must coincide on both floors.
        // On a rectangular pair its long direction may use each
        // storey's full length, including the ground floor entrance.
        foreach (bool horizontal in (xMax - xMin >= yMax - yMin)
            ? new[] { true, false } : new[] { false, true })
        {
            double commonNear = horizontal ? yMin : xMin;
            double commonFar = horizontal ? yMax : xMax;
            double axisMin = horizontal ? xMin : yMin;
            double axisMax = horizontal ? xMax : yMax;
            if (commonFar - commonNear < 2.0 ||
                axisMax - axisMin < 5.0) continue;

            double center = (commonNear + commonFar) / 2.0;
            // Search centred and several off-centre placements. The
            // stair flight can then avoid future window/door positions.
            foreach (double offset in new[] { 0.0, -0.5, 0.5, -1.0, 1.0, -1.5, 1.5 })
            {
                double near = center - 1.0 + offset, far = near + 2.0;
                if (near < commonNear - 1e-6 || far > commonFar + 1e-6)
                    continue;

                foreach (bool fullLength in new[] { true, false })
                {
                    var trial = BuildingStoreys.Copy(source);
                    bool fitted = true;
                    foreach (int index in new[] { lower, lower + 1 })
                    {
                        var floor = BuildingStoreys.GetFloor(trial, index);
                        double from = fullLength
                            ? (horizontal ? floor.Perimeter.Min(p => p.X)
                                : floor.Perimeter.Min(p => p.Y)) : axisMin;
                        double to = fullLength
                            ? (horizontal ? floor.Perimeter.Max(p => p.X)
                                : floor.Perimeter.Max(p => p.Y)) : axisMax;
                        List<Point2> shape = horizontal
                            ? [new(from,near),new(to,near),
                                new(to,far),new(from,far)]
                            : [new(near,from),new(far,from),
                                new(far,to),new(near,to)];
                        if (!FloorGeometry.IsWithinPolygon(shape, floor.Perimeter))
                        {
                            fitted = false;
                            break;
                        }
                        floor.Rooms.Add(new RoomDefinition
                        {
                            Name = "Hallway 1", Type = "Hallway", Outline = shape
                        });
                        BuildingStoreys.SetFloor(trial, index, floor);
                        if (!BuildingStoreys.Validate(trial, out _))
                        {
                            fitted = false;
                            break;
                        }
                    }
                    if (!fitted) continue;

                    if (AddStairs(trial, [lower, lower + 1],
                            out var connected) != 1) continue;
                    updated = connected;
                    reason = "";
                    return true;
                }
            }
        }
        reason = "The two blank floors overlap, but no aligned hallway and " +
            "stair flight with clear landings could be fitted. " +
            "Try a wider shared area or simplify a sharp indentation.";
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
            // A two-level plan already furnished with a hallway still
            // needs a way upstairs. Try a slim flight along one side of
            // the existing hall, leaving a walking lane past the run.
            // Previously this case was unconditionally skipped.
            if (hallway is not null && stairwell is null)
            {
                var upstairsHall = upper.Rooms.FirstOrDefault(r => r.Type == "Hallway");
                if (upstairsHall is null) continue;
                // Upper storeys may have a smaller or offset perimeter.
                // In that case use the common rectangle of the two halls
                // as a second candidate bay rather than sampling only the
                // lower hall and repeatedly failing upper-floor clearance.
                double jointLeft = Math.Max(hallway.Outline.Min(p => p.X),
                    upstairsHall.Outline.Min(p => p.X));
                double jointRight = Math.Min(hallway.Outline.Max(p => p.X),
                    upstairsHall.Outline.Max(p => p.X));
                double jointTop = Math.Max(hallway.Outline.Min(p => p.Y),
                    upstairsHall.Outline.Min(p => p.Y));
                double jointBottom = Math.Min(hallway.Outline.Max(p => p.Y),
                    upstairsHall.Outline.Max(p => p.Y));
                IEnumerable<StaircaseDefinition> options =
                    HallwayStairCandidates(hallway, lower);
                if (jointRight - jointLeft >= 2 && jointBottom - jointTop >= 2)
                {
                    var common = new RoomDefinition
                    {
                        Name = "Common circulation area", Type = "Hallway",
                        Outline =
                        [
                            new(jointLeft, jointTop),
                            new(jointRight, jointTop),
                            new(jointRight, jointBottom),
                            new(jointLeft, jointBottom)
                        ]
                    };
                    options = HallwayStairCandidates(common, lower)
                        .Concat(options);
                }
                foreach (var stair in options)
                {
                    var reservations = new List<List<Point2>>
                    {
                        BuildingStoreys.StairFootprint(stair)
                    };
                    reservations.AddRange(BuildingStoreys.StairLandings(stair));
                    if (reservations.Any(shape =>
                        !FloorGeometry.IsWithinPolygon(shape, hallway.Outline) ||
                        !FloorGeometry.IsWithinPolygon(shape, upstairsHall.Outline)))
                        continue;
                    if (reservations.Any(shape =>
                        low.Doors.Concat(upper.Doors).Any(door =>
                            PolygonGeometry.ContainsPoint(shape, door.Centre) ||
                            Enumerable.Range(0, shape.Count).Any(i =>
                                PolygonGeometry.ClosestPointOnSegment(
                                    door.Centre, shape[i],
                                    shape[(i + 1) % shape.Count])
                                .DistanceTo(door.Centre) < 0.35))))
                        continue;
                    if (!BuildingStoreys.TryAddStaircase(updated, stair,
                            out var fitted, out _))
                        continue;
                    updated = fitted;
                    count++;
                    break;
                }
                continue;
            }
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

    private static IEnumerable<StaircaseDefinition> HallwayStairCandidates(
        RoomDefinition hall, int lower)
    {
        var outline = hall.Outline;
        double x0 = outline.Min(p => p.X), x1 = outline.Max(p => p.X);
        double y0 = outline.Min(p => p.Y), y1 = outline.Max(p => p.Y);
        bool horizontal = x1 - x0 >= y1 - y0;
        double length = horizontal ? x1 - x0 : y1 - y0;
        double width = horizontal ? y1 - y0 : x1 - x0;
        if (length < 5.0 || width < 1.85) yield break;
        const double run = 2.8;
        const double landing = 1.0;
        const double stairWidth = 0.9;
        const double margin = 0.0;
        // The flight's outside edge must meet the hallway wall rather than
        // floating 15 cm off it. A 2m corridor still leaves 1.1m alongside
        // the 0.9m run (provided its landings fit).
        if (width - stairWidth - margin < 0.8) yield break;
        double axisStart = horizontal ? x0 : y0;
        double axisEnd = horizontal ? x1 : y1;
        double crossStart = horizontal ? y0 : x0;
        double crossEnd = horizontal ? y1 : x1;
        double halfTotal = (run + 2 * landing) / 2;
        foreach (double axis in new[]
        {
            (axisStart + axisEnd) / 2,
            axisStart + halfTotal + 0.35,
            axisEnd - halfTotal - 0.35,
            axisStart + length * 0.33,
            axisStart + length * 0.67
        }.Distinct())
        {
            if (axis - halfTotal < axisStart + 0.10 ||
                axis + halfTotal > axisEnd - 0.10) continue;
            foreach (double cross in new[]
            {
                // Prefer the room-facing side, usually a continuous shared
                // wall on BOTH levels of a stepped residential footprint.
                crossEnd - stairWidth / 2 - margin,
                crossStart + stairWidth / 2 + margin
            })
                yield return new StaircaseDefinition
                {
                    LowerFloorIndex = lower,
                    Centre = horizontal
                        ? new Point2(axis, cross) : new Point2(cross, axis),
                    RotationDegrees = horizontal ? 90 : 0,
                    Width = stairWidth, Length = run,
                    LandingLength = landing
                };
        }
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

    /// <summary>
    /// Find ordinary rooms that cannot be reached from a hallway without
    /// walking through a storeroom or somebody else's private bedroom.
    /// This is a generator quality check, not a restriction on hand-drawn
    /// historical or fantasy structures.
    /// </summary>
    public static IReadOnlyList<RoomDefinition> RoomsIsolatedByServiceSpaces(
        BuildingDocument building, int floorIndex)
    {
        var floor = BuildingStoreys.GetFloor(building, floorIndex);
        var hallway = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
        if (hallway is null) return [];

        static bool IsStorage(RoomDefinition room) =>
            room.Type is "Storeroom" or "Storage" or "Pantry" or
                "Buttery" or "Stockroom" or "Closet" or "Linen store";
        bool HasDoor(RoomDefinition a, RoomDefinition b) =>
            floor.Doors.Any(d =>
                OnOutline(d.Centre, a.Outline) &&
                OnOutline(d.Centre, b.Outline));
        var seen = new HashSet<Guid> { hallway.Id };
        var queue = new Queue<RoomDefinition>();
        queue.Enqueue(hallway);
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            // Restrict this particular quality check to compulsory
            // walk-through STORAGE. Other room-to-room arrangements,
            // such as historic interconnected private chambers, have
            // their own architectural rules and must not accidentally
            // invalidate older generation modes.
            if (IsStorage(from)) continue;
            foreach (var to in floor.Rooms.Where(r =>
                !seen.Contains(r.Id) && HasDoor(from, r)))
            {
                seen.Add(to.Id);
                queue.Enqueue(to);
            }
        }
        return floor.Rooms.Where(r =>
            r.Id != hallway.Id && r.Type != "Stairwell" &&
            !IsStorage(r) && !seen.Contains(r.Id) &&
            // This regression catches a room whose only available route
            // passes through storage; do not turn unrelated pre-existing
            // door-placement limitations into generation failures.
            floor.Rooms.Any(store => IsStorage(store) && HasDoor(r, store)))
            .ToArray();
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
