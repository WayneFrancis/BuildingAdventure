using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Converts a generated, oversized, six-corner L-shaped stair hallway
/// into a short transverse passage, an approach to its existing stair,
/// and useful rooms in the wings. Geometry is derived from the actual
/// existing polygon; the exterior and every user-authored room remain
/// unchanged. Never carve away a stair opening or its two landings.
/// </summary>
public static class IrregularHallRepair
{
    private const double Epsilon = 1e-5;

    public static int Reclaim(BuildingDocument source, int floorIndex,
        IReadOnlyCollection<Guid> protectedRoomIds,
        BuildingUse use, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        if (!BuildingStoreys.HasFloor(source, floorIndex)) return 0;
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway" &&
            !protectedRoomIds.Contains(r.Id));
        if (hall is null || hall.Outline.Count != 6 ||
            PolygonGeometry.Area(floor.Perimeter) < 45) return 0;
        double hallArea = PolygonGeometry.Area(hall.Outline);
        if (hallArea < 30 ||
            hallArea < 0.52 * PolygonGeometry.Area(floor.Perimeter))
            return 0;
        if (!Orthogonal(hall.Outline)) return 0;
        var stairs = source.Staircases.Where(s =>
            s.LowerFloorIndex == floorIndex ||
            s.LowerFloorIndex + 1 == floorIndex).ToArray();
        if (stairs.Length != 1) return 0;
        var stair = stairs[0];
        var clearance = new[] { BuildingStoreys.StairFootprint(stair),
                BuildingStoreys.StairOpeningFootprint(stair) }
            .Concat(BuildingStoreys.StairLandings(stair)).ToArray();

        // u: lateral direction across the spine and cross passage.
        // v: longitudinal direction from the stair toward the cross passage.
        // Consider both transposed orientations and all four rotations.
        foreach (bool transpose in new[] { false, true })
        {
            double U(Point2 p) => transpose ? p.Y : p.X;
            double V(Point2 p) => transpose ? p.X : p.Y;
            Point2 Point(double u, double v) =>
                transpose ? new Point2(v, u) : new Point2(u, v);
            List<Point2> Rect(double ua, double va, double ub, double vb) =>
                [Point(ua, va), Point(ub, va),
                 Point(ub, vb), Point(ua, vb)];

            var us = hall.Outline.Select(U).Distinct().OrderBy(n => n).ToArray();
            var vs = hall.Outline.Select(V).Distinct().OrderBy(n => n).ToArray();
            if (us.Length != 3 || vs.Length != 3) continue;

            double uMin = us[0], uMax = us[2];
            double vMin = vs[0], vMax = vs[2];
            foreach (bool spineLeft in new[] { true, false })
            foreach (bool barAtHighEnd in new[] { true, false })
            {
                double spineStart = spineLeft ? uMin : us[1];
                double spineEnd = spineLeft ? us[1] : uMax;
                double barStart = barAtHighEnd ? vs[1] : vMin;
                double barEnd = barAtHighEnd ? vMax : vs[1];
                var spine = Rect(spineStart, vMin, spineEnd, vMax);
                var bar = Rect(uMin, barStart, uMax, barEnd);
                double spineWidth = spineEnd - spineStart;
                double barDepth = barEnd - barStart;
                double sideDepth = barAtHighEnd ? barStart-vMin : vMax-barEnd;
                if (spineWidth < 3.9 || barDepth < 3.1 || sideDepth < 3.6 ||
                    uMax-uMin < 7.5) continue;
                if (!FloorGeometry.IsWithinPolygon(spine, hall.Outline) ||
                    !FloorGeometry.IsWithinPolygon(bar, hall.Outline))
                    continue;
                double unionArea = PolygonGeometry.Area(spine) +
                    PolygonGeometry.Area(bar) - spineWidth * barDepth;
                if (Math.Abs(unionArea-hallArea) > 0.01) continue;

                // This is the stair wing only if ALL of the flight,
                // opening and both landings lie in the long spine.
                if (!clearance.All(c =>
                    FloorGeometry.IsWithinPolygon(c, spine)))
                    continue;
                double stairEdge = barAtHighEnd
                    ? clearance.SelectMany(p => p).Max(V)
                    : clearance.SelectMany(p => p).Min(V);
                double stairCapEnd = barAtHighEnd
                    ? Math.Max(vMin + 1.5, stairEdge + 0.25)
                    : Math.Min(vMax - 1.5, stairEdge - 0.25);
                double sideStart = barAtHighEnd ? stairCapEnd : barEnd;
                double sideEnd = barAtHighEnd ? barStart : stairCapEnd;
                if (sideEnd - sideStart < 2.3) continue;

                var stairCap = barAtHighEnd
                    ? Rect(spineStart, vMin, spineEnd, stairCapEnd)
                    : Rect(spineStart, stairCapEnd, spineEnd, vMax);
                if (!clearance.All(c =>
                    FloorGeometry.IsWithinPolygon(c, stairCap))) continue;

                // Keep a side approach so the staircase is reachable
                // from the entire cross passage; carve the opposite side.
                double passageDepth = 1.5;
                double crossStart = barAtHighEnd ? barStart : barEnd-passageDepth;
                double crossEnd = barAtHighEnd ? barStart+passageDepth : barEnd;
                double pocketV0 = barAtHighEnd ? crossEnd : barStart;
                double pocketV1 = barAtHighEnd ? barEnd : crossStart;
                if (pocketV1-pocketV0 < 1.6) continue;

                foreach (bool passageRight in new[] { true, false })
                {
                    const double passageWidth = 1.4;
                    double approachStart = passageRight
                        ? spineEnd-passageWidth : spineStart;
                    double approachEnd = approachStart+passageWidth;
                    double roomU0 = passageRight ? spineStart : approachEnd;
                    double roomU1 = passageRight ? approachStart : spineEnd;
                    var approach=Rect(approachStart,sideStart,
                        approachEnd,sideEnd);
                    var cross=Rect(uMin,crossStart,uMax,crossEnd);
                    var sideRoom=Rect(roomU0,sideStart,roomU1,sideEnd);
                    if (PolygonGeometry.Area(sideRoom) < 8.0 ||
                        roomU1-roomU0 < 2.3) continue;

                    // Two rooms facing the cross passage. Deliberately
                    // use the stair-spine boundary as one candidate split;
                    // another split at the centre improves smaller wings.
                    foreach (double split in new[]
                    {
                        spineLeft ? spineEnd : spineStart,
                        (uMin+uMax)/2
                    }.Distinct())
                    {
                        if (split-uMin < 2.7 || uMax-split < 2.7) continue;
                        var farOne=Rect(uMin,pocketV0,split,pocketV1);
                        var farTwo=Rect(split,pocketV0,uMax,pocketV1);
                        if (PolygonGeometry.Area(farOne) < 7.5 ||
                            PolygonGeometry.Area(farTwo) < 7.5) continue;

                        // These three circulation pieces touch exactly,
                        // with no overlap. Use the established polygon
                        // union rather than manually guessing corner order.
                        var capRoom = new RoomDefinition
                            { Name="Stair landing", Type="Hallway",
                              Outline=stairCap };
                        var sideHall = new RoomDefinition
                            { Name="Stair approach", Type="Hallway",
                              Outline=approach };
                        var crossHall = new RoomDefinition
                            { Name="Cross passage", Type="Hallway",
                              Outline=cross };
                        var scratch = new BuildingDocument
                        {
                            Perimeter=floor.Perimeter.ToList(),
                            Rooms=[capRoom, sideHall, crossHall]
                        };
                        if (!RoomMerging.TryMergeRooms(scratch,
                            capRoom.Id,sideHall.Id,out var joined,out _))
                            continue;
                        if (!RoomMerging.TryMergeRooms(joined,
                            capRoom.Id,crossHall.Id,out var merged,out _))
                            continue;
                        var newHall=merged.Rooms.Single().Outline;
                        double removedArea=hallArea-PolygonGeometry.Area(newHall);
                        double addedArea=PolygonGeometry.Area(sideRoom)+
                            PolygonGeometry.Area(farOne)+
                            PolygonGeometry.Area(farTwo);
                        if (Math.Abs(removedArea-addedArea)>0.02 ||
                            PolygonGeometry.Area(newHall) >
                                0.48*PolygonGeometry.Area(floor.Perimeter) ||
                            !clearance.All(c =>
                                FloorGeometry.IsWithinPolygon(c,newHall)))
                            continue;

                        // Existing outside entrances should still serve
                        // circulation rather than suddenly becoming a
                        // private bedroom's exterior entrance.
                        bool ExteriorDoor(DoorDefinition door) =>
                            Enumerable.Range(0,floor.Perimeter.Count).Any(i=>
                                PolygonGeometry.ClosestPointOnSegment(
                                    door.Centre,floor.Perimeter[i],
                                    floor.Perimeter[(i+1)%floor.Perimeter.Count])
                                    .DistanceTo(door.Centre)<Epsilon);
                        bool OnRoom(Point2 p,IReadOnlyList<Point2> ring) =>
                            Enumerable.Range(0,ring.Count).Any(i=>
                                PolygonGeometry.ClosestPointOnSegment(
                                    p,ring[i],ring[(i+1)%ring.Count])
                                    .DistanceTo(p)<Epsilon);
                        if (floor.Doors.Any(d => ExteriorDoor(d) &&
                            OnRoom(d.Centre,hall.Outline) &&
                            !OnRoom(d.Centre,newHall))) continue;

                        var candidate=BuildingStoreys.Copy(source);
                        var edited=BuildingStoreys.GetFloor(candidate,floorIndex);
                        edited.Rooms.Single(r=>r.Id==hall.Id).Outline=
                            newHall.ToList();
                        var roomSpecs = floorIndex == 0
                            ? new[] { "Living room", "Dining room", "Study" }
                            : new[] { "Bedroom", "Bedroom", "Bathroom" };
                        var outlines=new[] {sideRoom,farOne,farTwo};
                        for(int n=0;n<outlines.Length;n++)
                        {
                            string type=roomSpecs[n];
                            edited.Rooms.Add(new RoomDefinition
                            {
                                Name=RoomLayoutPlanner.NextRoomName(type,edited.Rooms),
                                Type=type,Outline=outlines[n]
                            });
                        }
                        BuildingStoreys.SetFloor(candidate,floorIndex,edited);
                        if (!BuildingStoreys.Validate(candidate,out _))
                            continue;
                        updated=candidate;
                        return 3;
                    }
                }
            }
        }
        return 0;
    }

    private static bool Orthogonal(IReadOnlyList<Point2> ring)
    {
        for(int i=0;i<ring.Count;i++)
        {
            var a=ring[i];
            var b=ring[(i+1)%ring.Count];
            if(Math.Abs(a.X-b.X)>Epsilon &&
                Math.Abs(a.Y-b.Y)>Epsilon) return false;
        }
        return true;
    }
}
