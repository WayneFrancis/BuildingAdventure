using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Two-storey fantasy RPG house on a six-corner L/stepped exterior.
/// Room zoning starts with the actual notched perimeter and a shared
/// stair, instead of giving the whole inside corner to a giant hall.
/// Supports each rotation/reflection of the single-step shape. More
/// complex nonrectangular footprints use existing generators.
/// </summary>
public static class SteppedRpgHousePlanner
{
    private const double Epsilon = 1e-5;

    public static bool TryGenerate(BuildingDocument source, int seed,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "This two-floor RPG house needs two empty matching " +
            "six-corner stepped footprints with space for a safe stair.";
        if (BuildingStoreys.FloorCount(source) != 2 ||
            source.Staircases.Count > 1 ||
            !BuildingStoreys.HasFloor(source,0) ||
            !BuildingStoreys.HasFloor(source,1))
            return false;
        var ground = BuildingStoreys.GetFloor(source,0);
        var first = BuildingStoreys.GetFloor(source,1);
        if (ground.Rooms.Count!=0 || first.Rooms.Count!=0 ||
            ground.Doors.Count!=0 || first.Doors.Count!=0 ||
            ground.Windows.Count!=0 || first.Windows.Count!=0 ||
            ground.Perimeter.Count!=6 || first.Perimeter.Count!=6)
            return false;
        if (ground.Perimeter.Count!=first.Perimeter.Count ||
            ground.Perimeter.Any(p=>!first.Perimeter.Any(q=>
                p.DistanceTo(q)<Epsilon)))
            return false;

        // Canonical L: (0,0)->(stepX,0)->(stepX,stepY)
        // ->(width,stepY)->(width,depth)->(0,depth).
        // Test each transpose/mirror transform, allowing the staircase
        // to identify the end of the house to use as its private bay.
        var variants=new List<(bool Swap,bool FlipU,bool FlipV,
            double StairWidth,double CrossWidth,double SplitShift)>();
        foreach(bool swap in new[]{false,true})
        foreach(bool flipU in new[]{false,true})
        foreach(bool flipV in new[]{false,true})
        // Allow at least 0.4m past both end landings and a
        // continuous transverse route behind the stair flight.
        foreach(double stairWidth in new[]{5.6,5.75,5.8})
        foreach(double crossWidth in new[]{1.4,1.6})
        foreach(double splitShift in new[]{-0.35,0.0,0.35})
            variants.Add((swap,flipU,flipV,stairWidth,
                crossWidth,splitShift));
        var rng=new Random(seed);
        for(int i=variants.Count-1;i>0;i--)
        {
            int j=rng.Next(i+1);
            (variants[i],variants[j])=(variants[j],variants[i]);
        }
        foreach(var variation in variants)
        {
            if (!TryVariant(source,variation,out var candidate,out reason))
                continue;
            updated=candidate;
            reason="";
            return true;
        }
        return false;
    }

    private static bool TryVariant(BuildingDocument source,
        (bool Swap,bool FlipU,bool FlipV,
            double StairWidth,double CrossWidth,double SplitShift) v,
        out BuildingDocument result,out string reason)
    {
        result=BuildingStoreys.Copy(source);
        reason="No safe stair and bedroom arrangement fitted the step.";
        var footprint=source.Perimeter;
        double rawUMin=footprint.Min(p=>v.Swap?p.Y:p.X);
        double rawUMax=footprint.Max(p=>v.Swap?p.Y:p.X);
        double rawVMin=footprint.Min(p=>v.Swap?p.X:p.Y);
        double rawVMax=footprint.Max(p=>v.Swap?p.X:p.Y);
        double width=rawUMax-rawUMin,depth=rawVMax-rawVMin;
        double U(Point2 p)
        {
            double raw=v.Swap?p.Y:p.X;
            return v.FlipU ? rawUMax-raw : raw-rawUMin;
        }
        double V(Point2 p)
        {
            double raw=v.Swap?p.X:p.Y;
            return v.FlipV ? rawVMax-raw : raw-rawVMin;
        }
        Point2 P(double u,double t)
        {
            double ru=v.FlipU?rawUMax-u:rawUMin+u;
            double rv=v.FlipV?rawVMax-t:rawVMin+t;
            return v.Swap?new Point2(rv,ru):new Point2(ru,rv);
        }
        List<Point2> Rect(double x0,double y0,double x1,double y1) =>
            [P(x0,y0),P(x1,y0),P(x1,y1),P(x0,y1)];

        var xs=footprint.Select(U).Distinct().OrderBy(x=>x).ToArray();
        var ys=footprint.Select(V).Distinct().OrderBy(y=>y).ToArray();
        if(xs.Length!=3 || ys.Length!=3 || width<9 ||
            depth<8 || Math.Abs(xs[0])>Epsilon ||
            Math.Abs(xs[2]-width)>Epsilon ||
            Math.Abs(ys[0])>Epsilon ||
            Math.Abs(ys[2]-depth)>Epsilon)
            return false;
        double cutX=xs[1], cutY=ys[1];

        // Ensure this is a single missing corner, not an arbitrary
        // six-vertex orthogonal polygon or a thin decoration at the edge.
        if(cutX<7.2 || width-cutX<0.8 || cutY<4.5 ||
           depth-cutY<3.3) return false;
        var required=new[]
        {
            new Point2(0,0),new Point2(cutX,0),
            new Point2(cutX,cutY),new Point2(width,cutY),
            new Point2(width,depth),new Point2(0,depth)
        };
        if(required.Any(p=>!footprint.Any(actual=>
            Math.Abs(U(actual)-p.X)<Epsilon &&
            Math.Abs(V(actual)-p.Y)<Epsilon))) return false;

        double coreWidth=v.StairWidth;
        double crossDepth=v.CrossWidth;
        double split=coreWidth+v.SplitShift;
        const double approachWidth=1.4;
        if(coreWidth<4.8 || cutX-coreWidth<2.6 ||
            coreWidth-approachWidth<2.8 ||
            depth-cutY-crossDepth<2.0 ||
            split<3.3 || width-split<3.3)
            return false;

        StaircaseDefinition stair;
        if (source.Staircases.Count==1)
            stair=source.Staircases.Single();
        else
            stair=new StaircaseDefinition
            {
                LowerFloorIndex=0,
                // The long side of the flight sits against the outside
                // wall. The clear 1.1m bypass belongs BEHIND its opposite
                // edge, not in a narrow unused strip next to the facade.
                // The ascent still points into the cross-passage.
                Centre=P(coreWidth/2,0.45),
                Length=2.8,Width=0.9,LandingLength=1,
                RotationDegrees=v.Swap
                    ? (v.FlipU ? 180 : 0)
                    : (v.FlipU ? 90 : 270)
            };
        if(stair.LowerFloorIndex!=0) return false;
        var stairEnvelope = new[]
        {
            BuildingStoreys.StairFootprint(stair),
            BuildingStoreys.StairOpeningFootprint(stair)
        }.Concat(BuildingStoreys.StairLandings(stair)).ToArray();
        // Being inside the stair bay is not enough. In the old
        // example6 layout there was only 0.4m between the flight
        // and the side room: the far landing was effectively stranded.
        // A connected 1.1m-wide bypass behind the entire reservation
        // lets both landings reach the approach on BOTH floors without
        // stepping through the flight or falling into the upper void.
        const double minimumBypassWidth=1.1;
        double capDepth=Math.Max(2.25,
            stairEnvelope.SelectMany(r=>r).Max(V)+minimumBypassWidth);
        if (capDepth - stairEnvelope.SelectMany(r=>r).Max(V)
                < minimumBypassWidth-Epsilon)
            return false;
        if(capDepth>cutY-2.3) return false;
        var cap=Rect(0,0,coreWidth,capDepth);
        var approach=Rect(coreWidth-approachWidth,capDepth,
            coreWidth,cutY);
        var cross=Rect(0,cutY,width,cutY+crossDepth);
        if(!stairEnvelope.All(poly=>
            FloorGeometry.IsWithinPolygon(poly,cap))) return false;

        // Assemble one canonical 10-vertex passage outline from the
        // three NONOVERLAPPING rectangles. Mapping the canonical
        // winding back through P supports all reflections and rotations
        // without relying on polygon-union ordering heuristics.
        var hall=new List<Point2>
        {
            P(0,0),P(coreWidth,0),
            P(coreWidth,cutY),P(width,cutY),
            P(width,cutY+crossDepth),P(0,cutY+crossDepth),
            P(0,cutY),P(coreWidth-approachWidth,cutY),
            P(coreWidth-approachWidth,capDepth),P(0,capDepth)
        };
        if(!PolygonGeometry.IsValidOutline(hall,out _) ||
           !FloorGeometry.IsWithinPolygon(hall,footprint))
            return false;
        var pockets=new[]
        {
            Rect(0,capDepth,coreWidth-approachWidth,cutY),
            Rect(coreWidth,0,cutX,cutY),
            Rect(0,cutY+crossDepth,split,depth),
            Rect(split,cutY+crossDepth,width,depth)
        };
        if(pockets.Any(p=>PolygonGeometry.Area(p)<8 ||
            !FloorGeometry.IsWithinPolygon(p,footprint)))
            return false;
        double accounted=PolygonGeometry.Area(hall)+
            pockets.Sum(PolygonGeometry.Area);
        if(Math.Abs(accounted-PolygonGeometry.Area(footprint))>0.01)
            return false;

        for(int index=0;index<=1;index++)
        {
            var roomRoles = index==0
                ? new[] {"Living room","Kitchen","Dining room","Study"}
                : new[] {"Bedroom","Bedroom","Bedroom","Bathroom"};
            var floor=BuildingStoreys.GetFloor(result,index);
            floor.Rooms.Add(new RoomDefinition
            {
                Type="Hallway",Name=index==0?"Stair passage":"Upper landing",
                Outline=hall.ToList()
            });
            for(int i=0;i<pockets.Length;i++)
            {
                string type=roomRoles[i];
                floor.Rooms.Add(new RoomDefinition
                {
                    Type=type,Name=index==1 && i==0
                        ? "Master Bedroom"
                        : RoomLayoutPlanner.NextRoomName(type,floor.Rooms),
                    Outline=pockets[i].ToList()
                });
            }
            BuildingStoreys.SetFloor(result,index,floor);
        }
        if(!BuildingStoreys.Validate(result,out reason)) return false;
        if(source.Staircases.Count==0)
        {
            if(!BuildingStoreys.TryAddStaircase(result,stair,
                out var placed,out reason)) return false;
            result=placed;
        }
        // The hall's front facade is directly beside the staircase. Do
        // NOT put an entrance in that wall (the previous midpoint door
        // opened directly into the side of the flight). Enter through the
        // living room's exterior wall instead, with a full room-width
        // approach before encountering any stairs or landing.
        var entryFloor = BuildingStoreys.GetFloor(result, 0);
        entryFloor.Doors.Add(new DoorDefinition
        {
            Centre = P(0, (capDepth + cutY) / 2), Width = 0.9
        });
        BuildingStoreys.SetFloor(result, 0, entryFloor);
        if (!BuildingStoreys.Validate(result, out reason)) return false;

        BuildingConnectivity.AddDoors(result,0,out var doorGround);
        BuildingConnectivity.AddDoors(doorGround,1,out var doorUpper);
        if(!BuildingStoreys.Validate(doorUpper,out reason)) return false;
        result=doorUpper;

        // Every private room must have direct hall access; a bedroom
        // may never act as the only route to another bedroom.
        for(int index=0;index<=1;index++)
        {
            var floor=BuildingStoreys.GetFloor(result,index);
            var corridor=floor.Rooms.Single(r=>r.Type=="Hallway");
            static bool On(Point2 p,IReadOnlyList<Point2> poly)=>
                Enumerable.Range(0,poly.Count).Any(i=>
                    PolygonGeometry.ClosestPointOnSegment(
                        p,poly[i],poly[(i+1)%poly.Count]).DistanceTo(p)<Epsilon);
            if(floor.Rooms.Where(r=>r.Type!="Hallway").Any(r=>
                !floor.Doors.Any(d=>
                    On(d.Centre,r.Outline) &&
                    On(d.Centre,corridor.Outline))))
                return false;
            if(PolygonGeometry.Area(corridor.Outline) >
                0.45*PolygonGeometry.Area(floor.Perimeter))
                return false;
        }
        reason="";
        return true;
    }
}
