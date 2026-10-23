using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Compact two-storey fantasy RPG residence. Inspired by readable RPG
/// battle maps, not a prescriptive historical reconstruction: a short
/// entrance lobby serves optional guest/study rooms; living and dining
/// connect naturally with the rear kitchen; a wall-side stair stays in
/// the living room. Upstairs, three bedrooms and a bathroom open from
/// a slim landing, while a sitting area contains the upper stair.
///
/// Plans both floors AND the stair reservation in one transaction.
/// Existing user rooms, footprints or staircases are never discarded.
/// Orthogonal nonrectangular footprints are left to other archetypes.
/// </summary>
public static class CompactRpgHousePlanner
{
    private const double Tolerance = 1e-5;

    public static bool TryGenerate(BuildingDocument source, int seed,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "A compact two-storey RPG house needs two empty matching " +
            "rectangular floors, enough space for bedrooms, and a clear stair.";
        if (BuildingStoreys.FloorCount(source) != 2 ||
            !BuildingStoreys.HasFloor(source, 0) ||
            !BuildingStoreys.HasFloor(source, 1) ||
            source.Staircases.Count > 1)
            return false;
        var lower = BuildingStoreys.GetFloor(source, 0);
        var upper = BuildingStoreys.GetFloor(source, 1);
        if (lower.Rooms.Count != 0 || upper.Rooms.Count != 0 ||
            lower.Doors.Count != 0 || upper.Doors.Count != 0 ||
            lower.Windows.Count != 0 || upper.Windows.Count != 0 ||
            lower.Perimeter.Count != 4 || upper.Perimeter.Count != 4)
            return false;

        static bool Rectangle(IReadOnlyList<Point2> outline)
        {
            double a = outline.Min(p => p.X), b = outline.Max(p => p.X);
            double c = outline.Min(p => p.Y), d = outline.Max(p => p.Y);
            return b - a > 0 && d - c > 0 &&
                outline.All(p =>
                    (Math.Abs(p.X - a) < Tolerance ||
                     Math.Abs(p.X - b) < Tolerance) &&
                    (Math.Abs(p.Y - c) < Tolerance ||
                     Math.Abs(p.Y - d) < Tolerance));
        }
        if (!Rectangle(lower.Perimeter) || !Rectangle(upper.Perimeter))
            return false;
        if (lower.Perimeter.Min(p => p.X) != upper.Perimeter.Min(p => p.X) ||
            lower.Perimeter.Max(p => p.X) != upper.Perimeter.Max(p => p.X) ||
            lower.Perimeter.Min(p => p.Y) != upper.Perimeter.Min(p => p.Y) ||
            lower.Perimeter.Max(p => p.Y) != upper.Perimeter.Max(p => p.Y))
            return false;

        double minX = lower.Perimeter.Min(p => p.X);
        double maxX = lower.Perimeter.Max(p => p.X);
        double minY = lower.Perimeter.Min(p => p.Y);
        double maxY = lower.Perimeter.Max(p => p.Y);
        bool deepAlongX = maxX - minX > maxY - minY;
        double width = deepAlongX ? maxY - minY : maxX - minX;
        double depth = deepAlongX ? maxX - minX : maxY - minY;
        if (width < 8.2 || depth < 8.0)
            return false;

        // All variants are seeded and geometrically distinct. The order
        // changes, but the same seed + footprint produces the same rooms.
        var random = new Random(seed);
        var variations = new List<(bool Mirror, bool Reverse,
            double Front, double Back, double Split, double Entry,
            double LobbyBias, bool KitchenAtRear,
            bool GuestLeft, bool BackDoor)>();
        foreach (bool mirror in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (double front in new[] { 0.29, 0.32, 0.34 })
        foreach (double back in new[] { 0.29, 0.32 })
        foreach (double split in new[] { 0.60, 0.64 })
        foreach (double entry in new[] { 1.35, 1.55 })
        foreach (double lobbyBias in new[] { -0.55, 0.0, 0.35 })
        foreach (bool kitchenAtRear in new[] { true, false })
        {
            variations.Add((mirror, reverse, front, back, split,
                entry, lobbyBias, kitchenAtRear,
                random.Next(2) == 0, random.Next(3) == 0));
        }
        for (int i = variations.Count - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            (variations[i], variations[other]) =
                (variations[other], variations[i]);
        }

        foreach (var variant in variations)
        {
            if (TryVariant(source, deepAlongX, minX, maxX, minY, maxY,
                width, depth, variant, out var layout, out var detail))
            {
                updated = layout;
                reason = "";
                return true;
            }
            reason = detail;
        }
        return false;
    }

    private static bool TryVariant(BuildingDocument source,
        bool deepAlongX, double minX, double maxX, double minY, double maxY,
        double width, double depth,
        (bool Mirror, bool Reverse, double Front, double Back,
            double Split, double Entry, double LobbyBias,
            bool KitchenAtRear, bool GuestLeft, bool BackDoor) variant,
        out BuildingDocument proposed, out string reason)
    {
        proposed = BuildingStoreys.Copy(source);
        reason = "No valid arrangement was found around the stair landings.";

        double frontEnd = Math.Clamp(depth * variant.Front, 2.6, 3.4);
        double rearStart = depth - Math.Clamp(depth * variant.Back, 2.3, 3.3);
        double splitX = Math.Clamp(width * variant.Split, 5.12, width - 2.7);
        // A central entrance is not the only viable configuration.
        // Shift the actual foyer partition, changing front-room sizes.
        double lobbyLeft = (width - variant.Entry) / 2 +
            variant.LobbyBias;
        double lobbyRight = lobbyLeft + variant.Entry;
        if (rearStart - frontEnd < 2.4 ||
            lobbyLeft < 2.6 || width - lobbyRight < 2.6 ||
            splitX < lobbyRight + 0.15)
            return false;

        Point2 World(double u, double v)
        {
            double across = variant.Mirror ? width - u : u;
            double along = variant.Reverse ? depth - v : v;
            return deepAlongX
                ? new Point2(minX + along, minY + across)
                : new Point2(minX + across, minY + along);
        }
        List<Point2> Rect(double x0,double y0,double x1,double y1) =>
            [World(x0,y0),World(x1,y0),World(x1,y1),World(x0,y1)];

        static RoomDefinition Room(string type,string name,List<Point2> outline) =>
            new() { Type=type, Name=name, Outline=outline };

        var ground = BuildingStoreys.GetFloor(proposed,0);
        ground.Rooms =
        [
            Room("Hallway","Entrance lobby",
                Rect(lobbyLeft,0,lobbyRight,frontEnd)),
            Room("Guest room",variant.GuestLeft?"Guest room":"Study",
                Rect(0,0,lobbyLeft,frontEnd)),
            Room("Study",variant.GuestLeft?"Study":"Guest room",
                Rect(lobbyRight,0,width,frontEnd)),
            Room("Living room","Living room",
                Rect(0,frontEnd,splitX,depth)),
            Room(variant.KitchenAtRear?"Dining room":"Kitchen",
                variant.KitchenAtRear?"Dining room":"Kitchen",
                Rect(splitX,frontEnd,width,rearStart)),
            Room(variant.KitchenAtRear?"Kitchen":"Dining room",
                variant.KitchenAtRear?"Kitchen":"Dining room",
                Rect(splitX,rearStart,width,depth))
        ];
        // If we swap the guest and study, also swap their actual roles.
        if (!variant.GuestLeft)
        {
            ground.Rooms[1].Type = "Study";
            ground.Rooms[2].Type = "Guest room";
        }
        BuildingStoreys.SetFloor(proposed,0,ground);

        var first = BuildingStoreys.GetFloor(proposed,1);
        first.Rooms =
        [
            Room("Hallway","Upper landing",
                Rect(lobbyLeft,0,lobbyRight,rearStart)),
            Room("Bedroom","Bedroom 2",Rect(0,0,lobbyLeft,frontEnd)),
            Room("Bedroom","Bedroom 3",Rect(lobbyRight,0,width,frontEnd)),
            Room("Bedroom","Master Bedroom",
                Rect(0,frontEnd,lobbyLeft,rearStart)),
            Room("Bathroom","Bathroom",
                Rect(lobbyRight,frontEnd,width,rearStart)),
            Room("Sitting room","Sitting room",
                Rect(0,rearStart,width,depth))
        ];
        BuildingStoreys.SetFloor(proposed,1,first);
        if (!BuildingStoreys.Validate(proposed,out reason)) return false;

        if (proposed.Staircases.Count == 0)
        {
            // Place a LONG EDGE flush against the rear exterior wall,
            // regardless of plan rotation/mirroring. The old formula
            // (rearStart + depth) / 2 left approximately one full stair
            // width of inaccessible floor between the flight and wall.
            // All landing envelopes remain on the room's long axis,
            // and both levels share the same stair geometry.
            const double stairWidth = 0.9;
            var stair = new StaircaseDefinition
            {
                LowerFloorIndex = 0,
                Centre=World(2.55, depth - stairWidth / 2),
                Length=2.8, Width=stairWidth, LandingLength=1,
                RotationDegrees=deepAlongX ? 0 : 90
            };
            if (!BuildingStoreys.TryAddStaircase(proposed,stair,
                out var stairsAdded,out reason))
                return false;
            proposed=stairsAdded;
        }
        else
        {
            // Existing staircase is a user constraint, not an instruction
            // to move it. Its flight, upper void AND both landings must
            // lie inside the intended social/sitting spaces.
            var stair=proposed.Staircases[0];
            var living=BuildingStoreys.GetFloor(proposed,0)
                .Rooms.Single(r=>r.Type=="Living room");
            var sitting=BuildingStoreys.GetFloor(proposed,1)
                .Rooms.Single(r=>r.Type=="Sitting room");
            var envelope=new[] {BuildingStoreys.StairFootprint(stair)}
                .Concat(BuildingStoreys.StairLandings(stair));
            if (stair.LowerFloorIndex != 0 ||
                !envelope.All(e=>FloorGeometry.IsWithinPolygon(e,living.Outline) &&
                    FloorGeometry.IsWithinPolygon(e,sitting.Outline)) ||
                !BuildingStoreys.Validate(proposed,out reason))
                return false;
        }

        BuildingConnectivity.AddDoors(proposed,0,out var withGroundDoors);
        BuildingConnectivity.AddDoors(withGroundDoors,1,out var withBothDoors);
        proposed=withBothDoors;
        if (!BuildingStoreys.Validate(proposed,out reason)) return false;
        var a=BuildingStoreys.GetFloor(proposed,0);
        var b=BuildingStoreys.GetFloor(proposed,1);
        RoomDefinition At(BuildingDocument f,string name) =>
            f.Rooms.Single(r=>r.Name==name);
        bool DoorsBetween(BuildingDocument f,RoomDefinition one,
            RoomDefinition two) =>
            f.Doors.Any(d=>OnWall(one.Outline,d.Centre) &&
                OnWall(two.Outline,d.Centre));

        var entry=At(a,"Entrance lobby");
        var livingRoom=At(a,"Living room");
        var dining=At(a,"Dining room");
        var kitchen=At(a,"Kitchen");
        if (!DoorsBetween(a,entry,livingRoom) ||
            !DoorsBetween(a,livingRoom,dining) ||
            !(DoorsBetween(a,kitchen,dining) ||
              DoorsBetween(a,kitchen,livingRoom)) ||
            !a.Rooms.Where(r=>r.Type is "Guest room" or "Study")
                .All(r=>DoorsBetween(a,r,entry)))
            return false;

        var landing=At(b,"Upper landing");
        var sittingRoom=At(b,"Sitting room");
        if (!DoorsBetween(b,landing,sittingRoom) ||
            !b.Rooms.Where(r=>r.Type is "Bedroom" or "Bathroom")
                .All(r=>DoorsBetween(b,r,landing)) ||
            b.Rooms.Count(r=>r.Type=="Bedroom")!=3)
            return false;
        if (!a.Doors.Any(d=>OnWall(a.Perimeter,d.Centre) &&
            OnWall(entry.Outline,d.Centre)))
            return false;

        // Optional second entrance on the Kitchen's outside wall:
        // the rear facade for the rear-kitchen archetype, or the side
        // facade when Kitchen and Dining have exchanged zones.
        if (variant.BackDoor)
        {
            var back=variant.KitchenAtRear
                ? World((splitX+width)/2,depth)
                : World(width,(frontEnd+rearStart)/2);
            var candidate=BuildingStoreys.Copy(proposed);
            var edited=BuildingStoreys.GetFloor(candidate,0);
            edited.Doors.Add(new DoorDefinition { Centre=back,Width=0.9 });
            BuildingStoreys.SetFloor(candidate,0,edited);
            if (BuildingStoreys.Validate(candidate,out _))
                proposed=candidate;
        }
        reason="";
        return true;
    }

    private static bool OnWall(IReadOnlyList<Point2> poly,Point2 centre) =>
        Enumerable.Range(0,poly.Count).Any(i=>
            PolygonGeometry.ClosestPointOnSegment(centre,
                poly[i],poly[(i+1)%poly.Count]).DistanceTo(centre)<Tolerance);
}
