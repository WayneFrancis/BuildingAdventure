using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Rebalances a generated side-stair wing into a cross-passage that reaches
/// the lower stair landing directly and a larger service room beneath it.
/// In a side bay, a long skinny corridor alongside the entire flight is
/// not a useful way to approach the foot of the stairs. This pass replaces
/// only GENERATED hallway/service outlines, never an accepted user layout.
/// Both storeys retain their flight, landings and all unrelated rooms.
/// </summary>
public static class SideStairApproachPlanner
{
    private const double Epsilon = 1e-5;

    public static bool TryRebalance(BuildingDocument source, int floorIndex,
        IReadOnlyCollection<Guid> protectedRoomIds,
        out BuildingDocument updated)
    {
        return TryRebalance(source,floorIndex,protectedRoomIds,out updated,out _);
    }

    public static bool TryRebalance(BuildingDocument source, int floorIndex,
        IReadOnlyCollection<Guid> protectedRoomIds,
        out BuildingDocument updated, out string stage)
    {
        updated = BuildingStoreys.Copy(source);
        stage="Initial geometry or pattern not matched";
        if (!BuildingStoreys.HasFloor(source, floorIndex)) return false;
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var hall = floor.Rooms.SingleOrDefault(r =>
            r.Type == "Hallway" && !protectedRoomIds.Contains(r.Id));
        if (hall is null || hall.Outline.Count != 8 ||
            !Orthogonal(hall.Outline)) return false;

        string serviceType = floorIndex == 0 ? "Utility" : "Bathroom";
        var service = floor.Rooms.SingleOrDefault(r =>
            r.Type == serviceType && !protectedRoomIds.Contains(r.Id) &&
            r.Outline.Count == 4 && Orthogonal(r.Outline));
        if (service is null) return false;

        stage="Recognized generated hallway and service room";
        var flights = source.Staircases.Where(s =>
            s.LowerFloorIndex == floorIndex ||
            s.LowerFloorIndex + 1 == floorIndex).ToArray();
        if (flights.Length != 1) return false;
        var flight = flights.Single();
        var reserved = new[] { BuildingStoreys.StairFootprint(flight),
            BuildingStoreys.StairOpeningFootprint(flight) }
            .Concat(BuildingStoreys.StairLandings(flight)).ToArray();

        // Normalize orientation into u=across the passage,
        // v=toward the side-stair wing. This recognises the opposite
        // mirrored wing and 90-degree rotations of the same pattern.
        foreach (bool swapped in new[] { false, true })
        foreach (bool flipU in new[] { false, true })
        foreach (bool flipV in new[] { false, true })
        {
            double uLow = floor.Perimeter.Min(p => swapped ? p.Y : p.X);
            double uHigh = floor.Perimeter.Max(p => swapped ? p.Y : p.X);
            double vLow = floor.Perimeter.Min(p => swapped ? p.X : p.Y);
            double vHigh = floor.Perimeter.Max(p => swapped ? p.X : p.Y);
            double width = uHigh-uLow, depth=vHigh-vLow;
            double U(Point2 p)
            {
                double raw = swapped ? p.Y : p.X;
                return flipU ? uHigh - raw : raw - uLow;
            }
            double V(Point2 p)
            {
                double raw = swapped ? p.X : p.Y;
                return flipV ? vHigh - raw : raw - vLow;
            }
            Point2 P(double u,double v)
            {
                double rawU=flipU ? uHigh-u : uLow+u;
                double rawV=flipV ? vHigh-v : vLow+v;
                return swapped ? new Point2(rawV,rawU) :
                    new Point2(rawU,rawV);
            }
            List<Point2> Rectangle(double left,double top,
                double right,double bottom) =>
                [P(left,top),P(right,top),P(right,bottom),P(left,bottom)];

            // The long main corridor begins at v=0, with two wall
            // coordinates across it. The side wing starts further in
            // (v=wingStart) and the stair is against the u=0 wall.
            var frontWall = hall.Outline.Where(p =>
                Math.Abs(V(p)) < Epsilon).ToArray();
            if (frontWall.Length != 2) continue;
            double spineLeft=frontWall.Min(U);
            double spineRight=frontWall.Max(U);
            if (spineLeft<3.4 || spineRight-spineLeft<1.6 ||
                spineRight-spineLeft>2.8 ||
                width-spineRight<2.0 || depth<8.0)
                continue;

            double roomLeft=service.Outline.Min(U);
            double roomRight=service.Outline.Max(U);
            double roomTop=service.Outline.Min(V);
            double roomBottom=service.Outline.Max(V);
            if (Math.Abs(roomRight-spineLeft)>Epsilon ||
                roomLeft<1.1 || roomLeft>2.6 ||
                roomTop<3 || roomBottom>depth-0.3 ||
                roomBottom<roomTop+2)
                continue;
            if (Math.Abs(hall.Outline.Min(U))>Epsilon ||
                Math.Abs(hall.Outline.Max(U)-spineRight)>Epsilon ||
                Math.Abs(hall.Outline.Max(V)-depth)>Epsilon)
                continue;

            stage="Detected main corridor and utility bay";
            // Require the original service room and the side corridor
            // to form ONE rectangular bay from x=0 to spineRight,
            // y=roomTop to the rear perimeter.
            var wing=Rectangle(0,roomTop,spineRight,depth);
            if (!FloorGeometry.IsWithinPolygon(wing,floor.Perimeter))
                continue;

            stage="Verified side wing inside exterior";
            double minFlightU=reserved.SelectMany(p=>p).Min(U);
            double maxFlightU=reserved.SelectMany(p=>p).Max(U);
            double minFlightV=reserved.SelectMany(p=>p).Min(V);
            double maxFlightV=reserved.SelectMany(p=>p).Max(V);
            // LandingLength adds a full landing beyond each end of
            // the treads. The existing upper landing in example7 ends
            // at v=10.9 of an 11m exterior. That is VALID: the
            // platform itself must fit, but it need not leave another
            // 15cm after the landing against the exterior wall.
            if (minFlightU < -Epsilon ||
                maxFlightU > roomLeft+0.1 ||
                minFlightV < roomTop-Epsilon ||
                maxFlightV > depth+Epsilon)
                continue;

            // Put the service room BEHIND the cross passage (as in
            // example8), not beside the length of the staircase (as
            // in example7). Keep a 1.35m stair-width aisle along the
            // outer wall, containing the entire flight and landings.
            stage="Flight geometry is eligible for side access";
            double aisleWidth=Math.Max(1.35, Math.Ceiling(
                (maxFlightU+0.15)*20)/20);
            if (aisleWidth>=spineLeft-2.3 ||
                maxFlightU>aisleWidth-0.1)
                continue;

            // At least 1.1m of shared corridor frontage to any
            // neighbouring room at the opposite side of the spine.
            // Example7's Study begins at v=6.5; a passage ending at
            // 7.5m would leave a short shared wall. Allow 1.8m here.
            double crossEnd=roomTop+1.8;
            // The lower stair landing intentionally overlaps the
            // cross-passage: that is how visitors approach the foot
            // directly, without squeezing alongside the treads.
            if (crossEnd>depth-2.5)
                continue;
            // The arrival landing needs to be reached from the bar.
            // The bar and the narrow stair aisle must overlap along
            // a complete line, not join at a single point.
            stage="Computed cross passage and stair aisle";
            var newHall=new List<Point2>
            {
                P(spineLeft,0),P(spineRight,0),
                P(spineRight,crossEnd),P(aisleWidth,crossEnd),
                P(aisleWidth,depth),P(0,depth),
                P(0,roomTop),P(spineLeft,roomTop)
            };
            var newService=Rectangle(aisleWidth,crossEnd,
                spineRight,depth);
            if (!PolygonGeometry.IsValidOutline(newHall,out _) ||
                !FloorGeometry.IsWithinPolygon(newHall,floor.Perimeter) ||
                !FloorGeometry.IsWithinPolygon(newService,floor.Perimeter) ||
                PolygonGeometry.Area(newService)<12)
                continue;
            if (!reserved.All(r=>
                FloorGeometry.IsWithinPolygon(r,newHall)))
                continue;

            // The union of old hallway + service room must equal the
            // union of proposed hallway + proposed service room, so
            // existing living/kitchen/bedrooms are not impacted.
            double oldArea=PolygonGeometry.Area(hall.Outline)+
                PolygonGeometry.Area(service.Outline);
            double newArea=PolygonGeometry.Area(newHall)+
                PolygonGeometry.Area(newService);
            if (Math.Abs(oldArea-newArea)>0.02) continue;
            // The point-in-polygon checks above cannot alone prevent
            // exchanging a portion of some unrelated existing room.
            // Whole-building geometry validation follows.

            stage="Validated proposed room and stair geometry";
            var draft=BuildingStoreys.Copy(source);
            var edited=BuildingStoreys.GetFloor(draft,floorIndex);
            edited.Rooms.Single(r=>r.Id==hall.Id).Outline=newHall;
            edited.Rooms.Single(r=>r.Id==service.Id).Outline=newService;

            // Old doors on dissolved partitions would now float inside
            // the replacement service room. Keep every valid door (in
            // particular the outside entrance) and regenerate ONLY
            // those whose old supporting wall has disappeared.
            var walls=FloorGeometry.BuildWalls(edited.Perimeter,edited.Rooms);
            edited.Doors.RemoveAll(d=>
                !FloorGeometry.TryFindDoorWall(d,walls,out _));
            BuildingStoreys.SetFloor(draft,floorIndex,edited);
            if (!BuildingStoreys.Validate(draft,out var draftFailure))
            {
                stage="Draft validation: "+draftFailure;
                continue;
            }
            BuildingConnectivity.AddDoors(draft,floorIndex,out var connected);
            if (!BuildingStoreys.Validate(connected,out var connectedFailure))
            {
                stage="Door validation: "+connectedFailure;
                continue;
            }
            stage="Door network generated";

            // Every room except the hall should have a direct internal
            // door into circulation; especially the upstairs study on
            // the outside of the bend and the new utility/bathroom.
            var final=BuildingStoreys.GetFloor(connected,floorIndex);
            var corridor=final.Rooms.Single(r=>r.Id==hall.Id);
            static bool OnWall(Point2 p,IReadOnlyList<Point2> outline)=>
                Enumerable.Range(0,outline.Count).Any(i=>
                    PolygonGeometry.ClosestPointOnSegment(p,
                        outline[i],outline[(i+1)%outline.Count])
                        .DistanceTo(p)<Epsilon);
            var disconnected=final.Rooms.Where(r=>r.Id!=hall.Id)
                .FirstOrDefault(r=>!final.Doors.Any(d=>
                    OnWall(d.Centre,r.Outline) &&
                    OnWall(d.Centre,corridor.Outline)));
            if (disconnected is not null)
            {
                stage="No direct hallway door into "+disconnected.Name;
                continue;
            }

            updated=connected;
            stage="";
            return true;
        }
        return false;
    }

    private static bool Orthogonal(IReadOnlyList<Point2> poly)
    {
        for (int i=0;i<poly.Count;i++)
        {
            var a=poly[i];
            var b=poly[(i+1)%poly.Count];
            if (Math.Abs(a.X-b.X)>Epsilon &&
                Math.Abs(a.Y-b.Y)>Epsilon)
                return false;
        }
        return true;
    }
}
