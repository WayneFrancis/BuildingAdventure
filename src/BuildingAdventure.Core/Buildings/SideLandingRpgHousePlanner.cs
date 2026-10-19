using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Room-first, two-storey residence for deeper L-shaped footprints.
/// Ground: stairs in the living room, with only a short central passage.
/// Upper: stairs rise into a sitting/landing room and feed a CROSS hall,
/// with independently partitioned, similarly sized bedrooms. It must never
/// clone the ground floor hallway/room outlines upstairs.
/// </summary>
public static class SideLandingRpgHousePlanner
{
    private const double Epsilon = 1e-5;

    public static bool TryGenerate(BuildingDocument source, int seed,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "The independent side-landing residence requires two empty, " +
            "matching six-corner L floors and room for its stair approach.";
        if (BuildingStoreys.FloorCount(source) != 2 || source.Staircases.Count > 1)
            return false;
        var ground = BuildingStoreys.GetFloor(source, 0);
        var first = BuildingStoreys.GetFloor(source, 1);
        if (ground.Perimeter.Count != 6 || first.Perimeter.Count != 6 ||
            ground.Rooms.Count != 0 || first.Rooms.Count != 0 ||
            ground.Doors.Count != 0 || first.Doors.Count != 0 ||
            ground.Windows.Count != 0 || first.Windows.Count != 0 ||
            ground.Perimeter.Any(p => !first.Perimeter.Any(q =>
                p.DistanceTo(q) < Epsilon)))
            return false;

        // Independent upstairs cuts vary by seed without affecting the
        // ground-floor living/kitchen layout or existing staircase.
        var variants = new List<(bool Swap, bool FlipU, bool FlipV,
            double CorridorStart, double CorridorEnd, double RearSplit)>();
        foreach (bool swap in new[] { false, true })
        foreach (bool flipU in new[] { false, true })
        foreach (bool flipV in new[] { false, true })
        foreach (double crossStart in new[] { 4.9, 5.05, 5.2 })
        foreach (double crossEnd in new[] { 6.35, 6.5, 6.65 })
        foreach (double split in new[] { 0.47, 0.5, 0.53 })
            variants.Add((swap, flipU, flipV, crossStart, crossEnd, split));

        var random = new Random(seed);
        for (int i = variants.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (variants[i], variants[j]) = (variants[j], variants[i]);
        }
        var failures = new Dictionary<string, int>();
        foreach (var variant in variants)
        {
            if (TryVariant(source, variant, out var draft, out var why))
            {
                updated = draft;
                reason = "";
                return true;
            }
            failures.TryGetValue(why, out int count);
            failures[why] = count + 1;
        }
        reason = string.Join(" | ", failures.OrderByDescending(x => x.Value)
            .Select(x => $"{x.Value} variants: {x.Key}"));
        return false;
    }

    private static bool TryVariant(BuildingDocument source,
        (bool Swap, bool FlipU, bool FlipV, double CorridorStart,
            double CorridorEnd, double RearSplit) variant,
        out BuildingDocument result, out string reason)
    {
        result = BuildingStoreys.Copy(source);
        reason = "Footprint proportions do not fit the side-landing archetype.";
        var perimeter = source.Perimeter;
        double rawUMin = perimeter.Min(p => variant.Swap ? p.Y : p.X);
        double rawUMax = perimeter.Max(p => variant.Swap ? p.Y : p.X);
        double rawVMin = perimeter.Min(p => variant.Swap ? p.X : p.Y);
        double rawVMax = perimeter.Max(p => variant.Swap ? p.X : p.Y);
        double U(Point2 p) => variant.FlipU
            ? rawUMax - (variant.Swap ? p.Y : p.X)
            : (variant.Swap ? p.Y : p.X) - rawUMin;
        double V(Point2 p) => variant.FlipV
            ? rawVMax - (variant.Swap ? p.X : p.Y)
            : (variant.Swap ? p.X : p.Y) - rawVMin;
        Point2 P(double u, double v)
        {
            double realU = variant.FlipU ? rawUMax - u : rawUMin + u;
            double realV = variant.FlipV ? rawVMax - v : rawVMin + v;
            return variant.Swap ? new Point2(realV, realU) :
                new Point2(realU, realV);
        }
        List<Point2> Rect(double x0, double y0, double x1, double y1) =>
            [P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1)];

        double width = rawUMax - rawUMin;
        double depth = rawVMax - rawVMin;
        var xs = perimeter.Select(U).Distinct().Order().ToArray();
        var ys = perimeter.Select(V).Distinct().Order().ToArray();
        if (xs.Length != 3 || ys.Length != 3) return false;
        double stepX = xs[1], stepY = ys[1];
        // This is intentionally the deeper, shorter front wing (example12),
        // not the shallower example10 archetype. Reject marginal footprints
        // rather than returning skinny bedrooms or inaccessible stairs.
        if (width < 12.1 || width > 15.0 ||
            stepX < 10.2 || stepX > 11.8 ||
            width - stepX < 1 || width - stepX > 3 ||
            stepY < 5.7 || stepY > 6.3 ||
            depth - stepY < 3.7 || depth - stepY > 4.8)
            return false;
        var corners = new[]
        {
            new Point2(0,0), new Point2(stepX,0),
            new Point2(stepX,stepY), new Point2(width,stepY),
            new Point2(width,depth), new Point2(0,depth)
        };
        if (corners.Any(p => !perimeter.Any(actual =>
            Math.Abs(U(actual) - p.X) < Epsilon &&
            Math.Abs(V(actual) - p.Y) < Epsilon)))
            return false;

        const double bayWidth = 5.6;
        const double hallLeft = 4.2;
        const double groundEntryDepth = 3.1;
        const double upperSittingDepth = 2.8;
        // Put the pantry against the far/front kitchen wall, rather than
        // between the kitchen and dining room. The entire kitchen-dining
        // partition is then available for a direct serving doorway.
        double pantryEnd = 1.4;
        double crossStart = variant.CorridorStart;
        double crossEnd = variant.CorridorEnd;
        double rearSplit = width * variant.RearSplit;
        if (stepX - bayWidth < 4.3 ||
            stepY - pantryEnd < 3.05 ||
            crossStart - upperSittingDepth < 1.9 ||
            crossStart >= stepY - 0.45 ||
            crossEnd <= stepY + 0.3 ||
            depth - crossEnd < 3.05 ||
            rearSplit < 4.5 || width - rearSplit < 4.5)
            return false;

        // The previous automatically generated stair centred at V=1.1m,
        // leaving 0.65m of unusable floor between its edge and the wall.
        // New flights abut the perimeter. On regeneration move ONLY the
        // recognisable old generated flight; preserve any custom placement.
        // Updating the copied document retains the original staircase ID
        // and keeps the source building untouched until Accept.
        var stair = result.Staircases.Count == 1
            ? result.Staircases.Single()
            : new StaircaseDefinition
            {
                LowerFloorIndex = 0, Centre = P(2.8, 0.45),
                Length = 2.8, Width = 0.9, LandingLength = 1,
                RotationDegrees = variant.Swap
                    ? (variant.FlipU ? 180 : 0)
                    : (variant.FlipU ? 90 : 270)
            };
        if (stair.LowerFloorIndex != 0) return false;
        if (source.Staircases.Count == 1 &&
            Math.Abs(U(stair.Centre) - 2.8) < Epsilon &&
            Math.Abs(V(stair.Centre) - 1.1) < Epsilon &&
            Math.Abs(stair.Length - 2.8) < Epsilon &&
            Math.Abs(stair.Width - 0.9) < Epsilon &&
            Math.Abs(stair.LandingLength - 1) < Epsilon &&
            stair.OpeningExtraWidth == 0 && stair.OpeningExtraLength == 0)
        {
            // The perimeter can be mirrored or rotated, so snap along the
            // canonical short axis, not in absolute screen coordinates.
            // The stair is horizontal in the canonical local coordinates:
            // its narrow footprint edge must sit on the exterior wall.
            if (Math.Abs(stair.RotationDegrees -
                    (variant.Swap ? (variant.FlipU ? 180 : 0)
                                  : (variant.FlipU ? 90 : 270))) < Epsilon)
                stair.Centre = P(2.8, stair.Width / 2);
        }
        var reservations = new[]
        {
            BuildingStoreys.StairFootprint(stair),
            BuildingStoreys.StairOpeningFootprint(stair)
        }.Concat(BuildingStoreys.StairLandings(stair)).ToArray();
        var groundStairRoom = Rect(0, 0, bayWidth, groundEntryDepth);
        var upperStairRoom = Rect(0, 0, bayWidth, upperSittingDepth);
        reason = "Stair flight or landing does not fit the independent sitting room.";
        if (!reservations.All(r =>
            FloorGeometry.IsWithinPolygon(r, groundStairRoom) &&
            FloorGeometry.IsWithinPolygon(r, upperStairRoom)))
            return false;
        // The upper flight is not a walkable corridor. Its far landing
        // must have an actual 1.1m clear lane behind the whole opening.
        double rearEdge = reservations.SelectMany(r => r).Max(V);
        if (upperSittingDepth - rearEdge < 1.1 - Epsilon)
            return false;

        var lowerHall = Rect(hallLeft, groundEntryDepth, bayWidth, depth);
        var lowerLiving = new List<Point2>
        {
            P(0,0), P(bayWidth,0), P(bayWidth,groundEntryDepth),
            P(hallLeft,groundEntryDepth), P(hallLeft,stepY), P(0,stepY)
        };
        var groundRoles = new[]
        {
            ("Living room", "Living room", lowerLiving),
            ("Kitchen", "Kitchen", Rect(bayWidth,pantryEnd,stepX,stepY)),
            ("Pantry", "Pantry", Rect(bayWidth,0,stepX,pantryEnd)),
            ("Study", "Study", Rect(0,stepY,hallLeft,depth)),
            ("Dining room", "Dining room", Rect(bayWidth,stepY,width,depth))
        };

        // The upstairs hall runs ACROSS the house to both rear bedrooms,
        // but must stop where it has already served their doors. The
        // outer L wing (stepX..width, stepY..crossEnd) is a dead-end
        // corridor patch; annex it to Bedroom 3 instead of wasting it.
        // The resulting L-shaped bedroom has a useful nook and still
        // shares a wide doorway-capable wall with the remaining hall.
        var upperHall = new List<Point2>
        {
            P(hallLeft,upperSittingDepth),
            P(bayWidth,upperSittingDepth),
            P(bayWidth,crossStart),
            P(stepX,crossStart),
            P(stepX,crossEnd),
            P(0,crossEnd),
            P(0,crossStart),
            P(hallLeft,crossStart)
        };
        var rearBedroomWithNook = new List<Point2>
        {
            P(rearSplit,crossEnd),
            P(stepX,crossEnd),
            P(stepX,stepY),
            P(width,stepY),
            P(width,depth),
            P(rearSplit,depth)
        };
        var upperRoles = new[]
        {
            ("Sitting room", "Upper sitting room", upperStairRoom),
            ("Bedroom", "Bedroom 2", Rect(bayWidth,0,stepX,crossStart)),
            ("Bathroom", "Bathroom", Rect(0,upperSittingDepth,hallLeft,crossStart)),
            ("Bedroom", "Master Bedroom", Rect(0,crossEnd,rearSplit,depth)),
            ("Bedroom", "Bedroom 3", rearBedroomWithNook)
        };
        double ShortSide(IReadOnlyList<Point2> p) =>
            Math.Min(p.Max(t=>U(t)) - p.Min(t=>U(t)),
                     p.Max(t=>V(t)) - p.Min(t=>V(t)));
        var bedrooms = upperRoles.Where(r => r.Item1 == "Bedroom").ToArray();
        var areas = bedrooms.Select(r => PolygonGeometry.Area(r.Item3)).ToArray();
        if (areas.Min() < 11 ||
            areas.Max() / areas.Min() > 1.75 ||
            bedrooms.Any(r => ShortSide(r.Item3) < 3.05) ||
            groundRoles.Where(r => r.Item1 is "Dining room" or "Study")
                .Any(r => ShortSide(r.Item3) < 3.05))
            return false;

        reason = "New independent partitions do not validate.";
        for (int index = 0; index <= 1; index++)
        {
            var floor = BuildingStoreys.GetFloor(result, index);
            floor.Rooms.Add(new RoomDefinition
            {
                Type = "Hallway", Name = index == 0 ? "Short entry passage" : "Upper cross hall",
                Outline = (index == 0 ? lowerHall : upperHall).ToList()
            });
            foreach (var role in index == 0 ? groundRoles : upperRoles)
                floor.Rooms.Add(new RoomDefinition
                {
                    Type = role.Item1, Name = role.Item2, Outline = role.Item3.ToList()
                });
            BuildingStoreys.SetFloor(result, index, floor);
        }
        if (!BuildingStoreys.Validate(result, out reason)) return false;
        if (source.Staircases.Count == 0)
        {
            if (!BuildingStoreys.TryAddStaircase(result, stair,
                out var connected, out reason)) return false;
            result = connected;
        }

        // An entrance directly into the living room avoids the old
        // front-door-to-stairflight squeeze and an absurd rear hall entry.
        var ground = BuildingStoreys.GetFloor(result, 0);
        ground.Doors.Add(new DoorDefinition
        {
            Centre = P(0, (groundEntryDepth + stepY) / 2),
            Width = 0.9
        });
        BuildingStoreys.SetFloor(result, 0, ground);
        if (!BuildingStoreys.Validate(result, out reason)) return false;

        for (int index = 0; index <= 1; index++)
        {
            BuildingConnectivity.AddDoors(result, index, out var withDoors);
            result = withDoors;
        }

        // The pantry may never block the kitchen/dining serving route.
        // This direct doorway is required in addition to doors leading
        // through the hall and from the kitchen into the pantry.
        var groundAfterDoors = BuildingStoreys.GetFloor(result, 0);
        var kitchen = groundAfterDoors.Rooms.Single(r => r.Type == "Kitchen");
        var dining = groundAfterDoors.Rooms.Single(r => r.Type == "Dining room");
        static bool OnSharedWall(Point2 p, IReadOnlyList<Point2> outline) =>
            Enumerable.Range(0, outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(p, outline[i],
                    outline[(i + 1) % outline.Count]).DistanceTo(p) < Epsilon);
        if (!groundAfterDoors.Doors.Any(d =>
                OnSharedWall(d.Centre, kitchen.Outline) &&
                OnSharedWall(d.Centre, dining.Outline)))
        {
            reason = "Kitchen and dining room have no direct serving doorway.";
            return false;
        }

        // Existing chimney masonry is immutable, but its old room GUID
        // was removed by regeneration. Attach the hearth to the enclosing
        // room on the new ground plan.
        var completedFloors = result;
        foreach (var chimney in completedFloors.Chimneys.Where(ch =>
            ch.HearthRoomIds.Count == 0 &&
            BuildingStoreys.HasFloor(completedFloors, ch.HearthFloorIndex)))
        {
            var level = BuildingStoreys.GetFloor(result, chimney.HearthFloorIndex);
            var footprint = BuildingStoreys.ChimneyFootprint(chimney);
            var room = level.Rooms.FirstOrDefault(r =>
                r.Type != "Hallway" &&
                FloorGeometry.IsWithinPolygon(footprint, r.Outline));
            if (room is null) continue;
            chimney.HearthRoomIds.Add(room.Id);
            if (!BuildingStoreys.Validate(result, out _))
                chimney.HearthRoomIds.Clear();
        }
        if (!BuildingStoreys.Validate(result, out reason)) return false;

        // Prevent an apparent valid layout from hiding disconnected rooms.
        // Both upstairs rear bedrooms and the bathroom need hallway doors,
        // not access through another private room.
        static bool On(Point2 p, IReadOnlyList<Point2> outline) =>
            Enumerable.Range(0, outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(p, outline[i],
                    outline[(i + 1) % outline.Count]).DistanceTo(p) < Epsilon);
        for (int index = 0; index <= 1; index++)
        {
            var floor = BuildingStoreys.GetFloor(result, index);
            var hall = floor.Rooms.Single(r => r.Type == "Hallway");
            foreach (var room in floor.Rooms.Where(r => r.Id != hall.Id))
            {
                bool fromHall = floor.Doors.Any(d =>
                    On(d.Centre, hall.Outline) &&
                    On(d.Centre, room.Outline));
                bool pantryViaKitchen = room.Type == "Pantry" &&
                    floor.Doors.Any(d => On(d.Centre, room.Outline) &&
                        floor.Rooms.Any(k => k.Type == "Kitchen" &&
                            On(d.Centre, k.Outline)));
                if (!fromHall && !pantryViaKitchen)
                {
                    reason = $"{BuildingStoreys.FloorLabel(index)} {room.Name} " +
                        "was generated without its own safe hallway access.";
                    return false;
                }
            }
            double covered = floor.Rooms.Sum(r => PolygonGeometry.Area(r.Outline));
            if (Math.Abs(covered - PolygonGeometry.Area(floor.Perimeter)) > 0.01)
            {
                reason = "A side-landing layout left unallocated floor space.";
                return false;
            }
        }

        if (PolygonGeometry.Area(lowerHall) >= 14 ||
            PolygonGeometry.Area(upperHall) >= 25 ||
            groundRoles.Any(r => upperRoles.Any(u =>
                r.Item3.SequenceEqual(u.Item3))))
        {
            reason = "Side-landing hall proportions or upstairs independence failed.";
            return false;
        }

        reason = "";
        return true;
    }
}
