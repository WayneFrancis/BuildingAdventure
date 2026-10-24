using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Room-first two-storey home for the shallow-front L footprints that
/// cannot fit the separate stair-cap/cross-passage stepped archetype.
/// The staircase is in the downstairs living room and upstairs sitting
/// room; a SHORT transverse hall serves the rooms at the rear. Kitchen
/// and Dining always share a serving wall. Floor layouts differ.
/// </summary>
public static class CompactSteppedRpgHousePlanner
{
    private const double Eps=1e-5;

    public static bool TryGenerate(BuildingDocument source,int seed,
        out BuildingDocument updated,out string reason)
    {
        updated=BuildingStoreys.Copy(source);
        reason="A compact L-shaped house needs matching empty stepped floors "+
            "with room for a stair, kitchen, dining and three bedrooms.";
        if(BuildingStoreys.FloorCount(source)!=2 ||
            source.Staircases.Count>1) return false;
        var ground=BuildingStoreys.GetFloor(source,0);
        var upper=BuildingStoreys.GetFloor(source,1);
        if(ground.Perimeter.Count!=6 || upper.Perimeter.Count!=6 ||
            ground.Rooms.Count!=0 || upper.Rooms.Count!=0 ||
            ground.Doors.Count!=0 || upper.Doors.Count!=0 ||
            ground.Windows.Count!=0 || upper.Windows.Count!=0 ||
            ground.Perimeter.Any(p=>!upper.Perimeter.Any(q=>
                p.DistanceTo(q)<Eps))) return false;

        var variants=new List<(bool Swap,bool FlipU,bool FlipV,
            double LivingWidth,double HallDepth,double BedroomDepth,
            bool RearKitchen)>();
        foreach(bool swap in new[]{false,true})
        foreach(bool flipU in new[]{false,true})
        foreach(bool flipV in new[]{false,true})
        foreach(double livingWidth in new[]{4.85,5.0,5.15})
        foreach(double hallDepth in new[]{1.25,1.4})
        foreach(double bedroomDepth in new[]{3.05,3.2})
        foreach(bool rearKitchen in new[]{true,false})
            variants.Add((swap,flipU,flipV,livingWidth,
                hallDepth,bedroomDepth,rearKitchen));
        var rng=new Random(seed);
        for(int i=variants.Count-1;i>0;i--)
        {
            int j=rng.Next(i+1);
            (variants[i],variants[j])=(variants[j],variants[i]);
        }
        var failures=new Dictionary<string,int>();
        foreach(var variant in variants)
        {
            if(TryVariant(source,variant,out var proposed,out var why))
            {
                updated=proposed;
                reason="";
                return true;
            }
            failures.TryGetValue(why,out int count);
            failures[why]=count+1;
        }
        reason="Compact stepped house rejected: "+
            string.Join(" | ",failures.OrderByDescending(p=>p.Value)
                .Take(8).Select(p=>$"{p.Value}: {p.Key}"));
        return false;
    }

    private static bool TryVariant(BuildingDocument source,
        (bool Swap,bool FlipU,bool FlipV,double LivingWidth,
            double HallDepth,double BedroomDepth,bool RearKitchen) v,
        out BuildingDocument result,out string reason)
    {
        result=BuildingStoreys.Copy(source);
        reason="The small stepped footprint cannot contain a complete "+
            "room-first house with protected stair landings.";
        var ring=source.Perimeter;
        double rawUMin=ring.Min(p=>v.Swap?p.Y:p.X);
        double rawUMax=ring.Max(p=>v.Swap?p.Y:p.X);
        double rawVMin=ring.Min(p=>v.Swap?p.X:p.Y);
        double rawVMax=ring.Max(p=>v.Swap?p.X:p.Y);
        double width=rawUMax-rawUMin,depth=rawVMax-rawVMin;
        double U(Point2 p)=>v.FlipU?rawUMax-(v.Swap?p.Y:p.X)
            :(v.Swap?p.Y:p.X)-rawUMin;
        double V(Point2 p)=>v.FlipV?rawVMax-(v.Swap?p.X:p.Y)
            :(v.Swap?p.X:p.Y)-rawVMin;
        Point2 P(double u,double t)
        {
            double ru=v.FlipU?rawUMax-u:rawUMin+u;
            double rv=v.FlipV?rawVMax-t:rawVMin+t;
            return v.Swap?new Point2(rv,ru):new Point2(ru,rv);
        }
        List<Point2> Rect(double x0,double y0,double x1,double y1)=>
            [P(x0,y0),P(x1,y0),P(x1,y1),P(x0,y1)];

        var xs=ring.Select(U).Distinct().Order().ToArray();
        var ys=ring.Select(V).Distinct().Order().ToArray();
        if(xs.Length!=3 || ys.Length!=3 ||
            Math.Abs(xs[0])>Eps || Math.Abs(ys[0])>Eps ||
            Math.Abs(xs[2]-width)>Eps || Math.Abs(ys[2]-depth)>Eps)
            return false;
        double stepX=xs[1],stepY=ys[1];
        var expected=new[]
        {
            new Point2(0,0),new Point2(stepX,0),
            new Point2(stepX,stepY),new Point2(width,stepY),
            new Point2(width,depth),new Point2(0,depth)
        };
        if(expected.Any(p=>!ring.Any(q=>
            Math.Abs(U(q)-p.X)<Eps && Math.Abs(V(q)-p.Y)<Eps)))
            return false;

        double livingWidth=v.LivingWidth;
        double hallEnd=stepY+v.HallDepth;
        double bedroomSplit=v.BedroomDepth;
        // Specifically serve small, shallow-front L shapes without
        // introducing thin rear rooms or very large corridors.
        if(width<9.5 || width>12.5 ||
            stepX<7.7 || stepX>10.0 ||
            width-stepX<0.8 || width-stepX>3.0 ||
            stepY<4.3 || stepY>5.6 ||
            depth-stepY<3.6 || depth-stepY>5.4 ||
            stepX-livingWidth<2.7 ||
            depth-hallEnd<2.5 ||
            bedroomSplit<3.0 ||
            stepY-bedroomSplit<1.2)
            return false;

        var stair=source.Staircases.Count==1
            ? source.Staircases.Single()
            : new StaircaseDefinition
            {
                LowerFloorIndex=0,
                Centre=P(2.5,0.45),
                Length=2.8,Width=0.9,LandingLength=1,
                RotationDegrees=v.Swap
                    ? (v.FlipU?180:0)
                    : (v.FlipU?90:270)
            };
        if(stair.LowerFloorIndex!=0) return false;
        var stairRoom=Rect(0,0,livingWidth,stepY);
        var reserved=new[] {BuildingStoreys.StairFootprint(stair),
            BuildingStoreys.StairOpeningFootprint(stair)}
            .Concat(BuildingStoreys.StairLandings(stair));
        if(!reserved.All(shape=>
            FloorGeometry.IsWithinPolygon(shape,stairRoom)))
        {
            reason="Stair flight or landing outside living room.";
            return false;
        }

        static RoomDefinition Room(string type,string name,List<Point2> outline)=>
            new(){Type=type,Name=name,Outline=outline};
        var lower=BuildingStoreys.GetFloor(result,0);
        lower.Rooms=
        [
            Room("Living room","Living room",stairRoom.ToList()),
            Room("Hallway","Rear passage",
                Rect(0,stepY,livingWidth,hallEnd)),
            Room(v.RearKitchen?"Dining room":"Kitchen",
                v.RearKitchen?"Dining room":"Kitchen",
                Rect(livingWidth,0,stepX,stepY)),
            Room(v.RearKitchen?"Kitchen":"Dining room",
                v.RearKitchen?"Kitchen":"Dining room",
                Rect(livingWidth,stepY,width,depth)),
            Room("Study","Study",Rect(0,hallEnd,livingWidth,depth))
        ];
        // The passage is really the FRONT entry hall. Its short exposed
        // exterior wall gives a safe direct entrance, with an interior
        // connection to the Living room and Study. Do not force visitors
        // to walk through the staircase room just to enter the home.
        lower.Rooms.Single(r=>r.Type=="Hallway").Name="Front passage";
        lower.Doors.Add(new DoorDefinition
        {
            Centre=P(0,stepY+v.HallDepth/2),Width=0.9
        });
        BuildingStoreys.SetFloor(result,0,lower);

        var first=BuildingStoreys.GetFloor(result,1);
        // The left end of the upper cross-passage serves no other room.
        // Give that unused strip to Bedroom 3 as a small L-shaped nook,
        // leaving a shorter, fully connected landing passage alongside
        // the upstairs sitting room and master-bedroom access.
        const double reclaimedWidth=1.25;
        if(livingWidth-reclaimedWidth<2.7) return false;
        var upperBedroom=new List<Point2>
        {
            P(0,stepY),P(reclaimedWidth,stepY),
            P(reclaimedWidth,hallEnd),P(livingWidth,hallEnd),
            P(livingWidth,depth),P(0,depth)
        };
        first.Rooms=
        [
            Room("Sitting room","Upper sitting room",stairRoom.ToList()),
            Room("Hallway","Upper passage",
                Rect(reclaimedWidth,stepY,livingWidth,hallEnd)),
            Room("Bedroom","Bedroom 2",
                Rect(livingWidth,0,stepX,bedroomSplit)),
            Room("Bathroom","Bathroom",
                Rect(livingWidth,bedroomSplit,stepX,stepY)),
            Room("Bedroom","Master Bedroom",
                Rect(livingWidth,stepY,width,depth)),
            Room("Bedroom","Bedroom 3",upperBedroom)
        ];
        BuildingStoreys.SetFloor(result,1,first);
        if(!BuildingStoreys.Validate(result,out reason)) return false;
        if(source.Staircases.Count==0)
        {
            if(!BuildingStoreys.TryAddStaircase(result,stair,
                out var connected,out reason)) return false;
            result=connected;
        }

        BuildingConnectivity.AddDoors(result,0,out var withGroundDoors);
        BuildingConnectivity.AddDoors(withGroundDoors,1,out var withUpperDoors);
        if(!BuildingStoreys.Validate(withUpperDoors,out reason))
            return false;
        result=withUpperDoors;

        static bool On(Point2 p,IReadOnlyList<Point2> outline)=>
            Enumerable.Range(0,outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(
                    p,outline[i],outline[(i+1)%outline.Count])
                    .DistanceTo(p)<Eps);
        bool Linked(BuildingDocument floor,string one,string two)
        {
            var first=floor.Rooms.Single(r=>r.Type==one);
            var second=floor.Rooms.Single(r=>r.Type==two);
            return floor.Doors.Any(d=>
                On(d.Centre,first.Outline) &&
                On(d.Centre,second.Outline));
        }
        var a=BuildingStoreys.GetFloor(result,0);
        var b=BuildingStoreys.GetFloor(result,1);
        if(!Linked(a,"Kitchen","Dining room") ||
            !Linked(a,"Living room","Hallway") ||
            !Linked(a,"Hallway","Study") ||
            !Linked(b,"Sitting room","Hallway") ||
            !b.Doors.Any(d=>On(d.Centre,b.Rooms.Single(r=>
                r.Name=="Master Bedroom").Outline) &&
                On(d.Centre,b.Rooms.Single(r=>
                    r.Name=="Upper passage").Outline)))
        {
            reason="The compact L-house is missing an essential "+
                "room-to-room doorway.";
            return false;
        }

        // Each upper bedroom and the bathroom needs direct public-space
        // access rather than making another bedroom a through-route.
        foreach(var room in b.Rooms.Where(r=>
            r.Type is "Bedroom" or "Bathroom"))
        {
            if(!b.Doors.Any(d=>On(d.Centre,room.Outline) &&
                b.Rooms.Any(shared=>
                    (shared.Type is "Hallway" or "Sitting room") &&
                    On(d.Centre,shared.Outline))))
            {
                reason=room.Name+" has no public sitting-room or hall doorway.";
                return false;
            }
        }
        reason="";
        return true;
    }
}
