using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Two conservative post-planning repairs for six-corner stepped RPG homes:
/// (1) reserve an actual walkable bypass behind a wall-side stair, rather
/// than a 0.3m line of passage trapped between a bedroom and the stair;
/// (2) assign narrow notched exterior strips to the adjoining room and
/// cross-hall rather than leaving visually obvious holes in the floor.
/// Supports all eight rotations/reflections of the single-step footprint.
/// Only current generated rooms may be reshaped; other user-authored rooms,
/// windows, staircases and chimneys are never moved.
/// </summary>
public static class SteppedLayoutFinisher
{
    private const double Eps = 1e-5;
    private const double BypassWidth = 1.1;

    public static int Refine(BuildingDocument source, int floorIndex,
        IReadOnlyCollection<Guid> protectedRoomIds, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        if (!BuildingStoreys.HasFloor(source,floorIndex)) return 0;
        int changes=0;
        for(int pass=0;pass<2;pass++)
        {
            if (!TryImproveStairBypass(updated,floorIndex,protectedRoomIds,
                out var better)) break;
            updated=better;
            changes++;
        }
        for(int pass=0;pass<2;pass++)
        {
            if (!TryCloseExteriorStep(updated,floorIndex,protectedRoomIds,
                out var better)) break;
            updated=better;
            changes++;
        }
        return changes;
    }

    private sealed class Frame
    {
        public required Func<Point2,double> U { get; init; }
        public required Func<Point2,double> V { get; init; }
        public required Func<double,double,Point2> P { get; init; }
        public required double Width { get; init; }
        public required double Depth { get; init; }
        public required double StepU { get; init; }
        public required double StepV { get; init; }
        public List<Point2> Rect(double x0,double y0,double x1,double y1) =>
            [P(x0,y0),P(x1,y0),P(x1,y1),P(x0,y1)];
    }

    private static IEnumerable<Frame> Frames(IReadOnlyList<Point2> perimeter)
    {
        if(perimeter.Count!=6) yield break;
        foreach(bool swap in new[]{false,true})
        foreach(bool reflectU in new[]{false,true})
        foreach(bool reflectV in new[]{false,true})
        {
            double rawMinU=perimeter.Min(p=>swap?p.Y:p.X);
            double rawMaxU=perimeter.Max(p=>swap?p.Y:p.X);
            double rawMinV=perimeter.Min(p=>swap?p.X:p.Y);
            double rawMaxV=perimeter.Max(p=>swap?p.X:p.Y);
            double w=rawMaxU-rawMinU,d=rawMaxV-rawMinV;
            double U(Point2 p) => reflectU
                ? rawMaxU-(swap?p.Y:p.X) : (swap?p.Y:p.X)-rawMinU;
            double V(Point2 p) => reflectV
                ? rawMaxV-(swap?p.X:p.Y) : (swap?p.X:p.Y)-rawMinV;
            Point2 P(double u,double v)
            {
                double ru=reflectU ? rawMaxU-u : rawMinU+u;
                double rv=reflectV ? rawMaxV-v : rawMinV+v;
                return swap ? new Point2(rv,ru) : new Point2(ru,rv);
            }
            var us=perimeter.Select(U).Distinct().Order().ToArray();
            var vs=perimeter.Select(V).Distinct().Order().ToArray();
            if(us.Length!=3 || vs.Length!=3 ||
                Math.Abs(us[0])>Eps || Math.Abs(vs[0])>Eps ||
                w<8 || d<7) continue;
            double su=us[1],sv=vs[1];
            if(su<6 || w-su<0.5 || sv<4 || d-sv<2) continue;
            var necessary=new[]
            {
                new Point2(0,0),new Point2(su,0),new Point2(su,sv),
                new Point2(w,sv),new Point2(w,d),new Point2(0,d)
            };
            if(necessary.Any(c=>!perimeter.Any(p=>
                Math.Abs(U(p)-c.X)<Eps && Math.Abs(V(p)-c.Y)<Eps)))
                continue;
            yield return new Frame
            {
                U=U,V=V,P=P,Width=w,Depth=d,StepU=su,StepV=sv
            };
        }
    }

    private static bool IsRect(IReadOnlyList<Point2> ring,Frame f)
    {
        if(ring.Count!=4) return false;
        double minU=ring.Min(f.U),maxU=ring.Max(f.U);
        double minV=ring.Min(f.V),maxV=ring.Max(f.V);
        return ring.All(p=>
            (Math.Abs(f.U(p)-minU)<Eps ||
             Math.Abs(f.U(p)-maxU)<Eps) &&
            (Math.Abs(f.V(p)-minV)<Eps ||
             Math.Abs(f.V(p)-maxV)<Eps));
    }

    private static bool TryImproveStairBypass(BuildingDocument source,
        int index,IReadOnlyCollection<Guid> protectedIds,
        out BuildingDocument updated)
    {
        updated=BuildingStoreys.Copy(source);
        var f=BuildingStoreys.GetFloor(source,index);
        var hall=f.Rooms.SingleOrDefault(r=>
            r.Type=="Hallway" && !protectedIds.Contains(r.Id));
        if(hall is null) return false;
        var staircases=source.Staircases.Where(s=>
            s.LowerFloorIndex==index ||
            s.LowerFloorIndex+1==index).ToArray();
        if(staircases.Length!=1) return false;
        var stair=staircases.Single();
        var reserve=new[] {BuildingStoreys.StairFootprint(stair),
            BuildingStoreys.StairOpeningFootprint(stair)}
            .Concat(BuildingStoreys.StairLandings(stair)).ToArray();

        foreach(var frame in Frames(f.Perimeter))
        {
            // In this orientation the long axis of the flight is across
            // U, close to the v=0 outer wall. The hallway must already
            // wrap behind it on the two other sides.
            double minV=reserve.SelectMany(x=>x).Min(frame.V);
            double maxV=reserve.SelectMany(x=>x).Max(frame.V);
            double minU=reserve.SelectMany(x=>x).Min(frame.U);
            double maxU=reserve.SelectMany(x=>x).Max(frame.U);
            if(minV < -Eps || minV > 1.0 ||
                maxV>2.4 || minU< -Eps || maxU>frame.StepU-0.4 ||
                maxU-minU < 3.8 || maxV-minV > 1.6)
                continue;
            foreach(var room in f.Rooms.Where(r=>r.Id!=hall.Id &&
                !protectedIds.Contains(r.Id) && IsRect(r.Outline,frame)))
            {
                double left=room.Outline.Min(frame.U);
                double right=room.Outline.Max(frame.U);
                double top=room.Outline.Min(frame.V);
                double bottom=room.Outline.Max(frame.V);
                double newTop=maxV+BypassWidth;
                if(left>Eps || right<2.6 || right>frame.StepU-0.8 ||
                    top<maxV-Eps || top>=newTop-0.05 ||
                    bottom-newTop<2.5 || newTop>frame.StepV-1.0)
                    continue;
                // The old hallway has an edge exactly on the top of
                // this room, running from the outer wall to the inner
                // side corridor. Move BOTH of its endpoints down.
                var oldLeft=frame.P(0,top);
                var oldRight=frame.P(right,top);
                if(!hall.Outline.Any(p=>p.DistanceTo(oldLeft)<Eps) ||
                   !hall.Outline.Any(p=>p.DistanceTo(oldRight)<Eps))
                    continue;
                var trial=BuildingStoreys.Copy(source);
                var edit=BuildingStoreys.GetFloor(trial,index);
                var revisedHall=edit.Rooms.Single(r=>r.Id==hall.Id);
                revisedHall.Outline=revisedHall.Outline.Select(p=>
                    p.DistanceTo(oldLeft)<Eps ? frame.P(0,newTop) :
                    p.DistanceTo(oldRight)<Eps ? frame.P(right,newTop) : p)
                    .ToList();
                edit.Rooms.Single(r=>r.Id==room.Id).Outline=
                    frame.Rect(left,newTop,right,bottom);
                BuildingStoreys.SetFloor(trial,index,edit);
                if(!BuildingStoreys.Validate(trial,out _)) continue;
                if(!reserve.All(poly=>FloorGeometry.IsWithinPolygon(
                    poly,revisedHall.Outline))) continue;
                // The whole 1.1m strip immediately behind the upper
                // opening is now a contiguous hallway across the flight.
                var fullRoute=frame.Rect(0,maxV,right,newTop);
                if(!FloorGeometry.IsWithinPolygon(
                    fullRoute,revisedHall.Outline)) continue;
                updated=trial;
                return true;
            }
        }
        return false;
    }

    private static bool TryCloseExteriorStep(BuildingDocument source,
        int index,IReadOnlyCollection<Guid> protectedIds,
        out BuildingDocument updated)
    {
        updated=BuildingStoreys.Copy(source);
        var f=BuildingStoreys.GetFloor(source,index);
        var hall=f.Rooms.SingleOrDefault(r=>
            r.Type=="Hallway" && !protectedIds.Contains(r.Id));
        if(hall is null) return false;
        double missing=RoomLayoutPlanner.UnassignedArea(f);
        if(missing<0.05) return false;

        foreach(var frame in Frames(f.Perimeter))
        {
            foreach(var room in f.Rooms.Where(r=>r.Id!=hall.Id &&
                !protectedIds.Contains(r.Id) && IsRect(r.Outline,frame)))
            {
                double left=room.Outline.Min(frame.U);
                double right=room.Outline.Max(frame.U);
                double top=room.Outline.Min(frame.V);
                double bottom=room.Outline.Max(frame.V);
                if(left<2.0 || top>Eps ||
                   right>=frame.StepU-Eps ||
                   frame.StepU-right>1.25 ||
                   bottom<=frame.StepV+Eps ||
                   bottom-frame.StepV>0.8)
                    continue;

                Point2 a=frame.P(left,bottom);
                Point2 b=frame.P(frame.Width,bottom);
                // The hall's upper edge is one continuous segment
                // from the room's near corner across the stepped wing.
                // Extend it to the real outer step at v=StepV.
                int side=Enumerable.Range(0,hall.Outline.Count)
                    .FirstOrDefault(i=>
                        hall.Outline[i].DistanceTo(a)<Eps &&
                        hall.Outline[(i+1)%hall.Outline.Count]
                            .DistanceTo(b)<Eps,-1);
                if(side<0) continue;
                var newHall=new List<Point2>();
                for(int i=0;i<hall.Outline.Count;i++)
                {
                    newHall.Add(hall.Outline[i]);
                    if(i==side)
                    {
                        newHall.Add(frame.P(right,bottom));
                        newHall.Add(frame.P(right,frame.StepV));
                        newHall.Add(frame.P(frame.Width,frame.StepV));
                    }
                }
                var newRoom=new List<Point2>
                {
                    frame.P(left,top),frame.P(frame.StepU,top),
                    frame.P(frame.StepU,frame.StepV),
                    frame.P(right,frame.StepV),
                    frame.P(right,bottom),frame.P(left,bottom)
                };
                if(!PolygonGeometry.IsValidOutline(newHall,out _) ||
                   !PolygonGeometry.IsValidOutline(newRoom,out _))
                    continue;
                var trial=BuildingStoreys.Copy(source);
                var edit=BuildingStoreys.GetFloor(trial,index);
                edit.Rooms.Single(r=>r.Id==hall.Id).Outline=newHall;
                edit.Rooms.Single(r=>r.Id==room.Id).Outline=newRoom;
                BuildingStoreys.SetFloor(trial,index,edit);
                if(!BuildingStoreys.Validate(trial,out _)) continue;
                double after=RoomLayoutPlanner.UnassignedArea(
                    BuildingStoreys.GetFloor(trial,index));
                if(after>missing-0.05 || after>0.02) continue;
                updated=trial;
                return true;
            }
        }
        return false;
    }
}
