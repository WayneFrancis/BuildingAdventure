using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Room-first fantasy RPG house for broad six-vertex L-shaped footprints.
/// Unlike the fallback corridor-driven layout, ground-floor stairs occupy
/// the common room rather than consuming a full parallel hallway; upstairs
/// stairs emerge into a sitting room and connect to the hall unobstructed.
/// Both levels use different room divisions, and bedrooms must be
/// reasonably similar in size and at least 3.0m wide.
/// </summary>
public static class BalancedSteppedRpgHousePlanner
{
    private const double Eps=1e-5;

    public static bool TryGenerate(BuildingDocument source,int seed,
        out BuildingDocument updated,out string reason)
    {
        updated=BuildingStoreys.Copy(source);
        reason="A balanced L-shaped RPG residence needs two empty, matching "+
            "broad stepped floors with enough space for separate kitchens, "+
            "bedrooms and an accessible shared staircase.";
        if(BuildingStoreys.FloorCount(source)!=2 ||
            source.Staircases.Count>1) return false;
        var lower=BuildingStoreys.GetFloor(source,0);
        var upper=BuildingStoreys.GetFloor(source,1);
        if(lower.Perimeter.Count!=6 || upper.Perimeter.Count!=6 ||
            lower.Rooms.Count!=0 || upper.Rooms.Count!=0 ||
            lower.Doors.Count!=0 || upper.Doors.Count!=0 ||
            lower.Windows.Count!=0 || upper.Windows.Count!=0 ||
            lower.Perimeter.Any(p=>!upper.Perimeter.Any(q=>
                p.DistanceTo(q)<Eps))) return false;

        var permutations=new List<(bool Swap,bool FlipU,bool FlipV,
            double SpineWidth,double KitchenDepth,double BedroomDepth)>();
        foreach(bool swap in new[]{false,true})
        foreach(bool flipU in new[]{false,true})
        foreach(bool flipV in new[]{false,true})
        foreach(double spine in new[]{1.4,1.55})
        foreach(double kitchen in new[]{0.59,0.63,0.67})
        foreach(double bedroom in new[]{0.60,0.64,0.68})
            permutations.Add((swap,flipU,flipV,spine,kitchen,bedroom));
        var rng=new Random(seed);
        for(int i=permutations.Count-1;i>0;i--)
        {
            int j=rng.Next(i+1);
            (permutations[i],permutations[j])=
                (permutations[j],permutations[i]);
        }
        var failures = new Dictionary<string,int>();
        foreach(var variation in permutations)
        {
            if(TryVariant(source,variation,out var candidate,out var message))
            {
                updated=candidate;
                reason="";
                return true;
            }
            failures.TryGetValue(message,out int matches);
            failures[message]=matches+1;
        }
        reason=string.Join(" | ",failures.OrderByDescending(p=>p.Value)
            .Select(p=>p.Value+" variants: "+p.Key));
        return false;
    }

    private static bool TryVariant(BuildingDocument source,
        (bool Swap,bool FlipU,bool FlipV,
            double SpineWidth,double KitchenDepth,double BedroomDepth) v,
        out BuildingDocument result,out string reason)
    {
        result=BuildingStoreys.Copy(source);
        reason="The step, room dimensions or stair reservation do not fit.";
        var ring=source.Perimeter;
        double rawUMin=ring.Min(p=>v.Swap?p.Y:p.X);
        double rawUMax=ring.Max(p=>v.Swap?p.Y:p.X);
        double rawVMin=ring.Min(p=>v.Swap?p.X:p.Y);
        double rawVMax=ring.Max(p=>v.Swap?p.X:p.Y);
        double width=rawUMax-rawUMin,depth=rawVMax-rawVMin;
        double U(Point2 p)=>v.FlipU ? rawUMax-(v.Swap?p.Y:p.X)
            : (v.Swap?p.Y:p.X)-rawUMin;
        double V(Point2 p)=>v.FlipV ? rawVMax-(v.Swap?p.X:p.Y)
            : (v.Swap?p.X:p.Y)-rawVMin;
        Point2 P(double u,double t)
        {
            double ru=v.FlipU?rawUMax-u:rawUMin+u;
            double rv=v.FlipV?rawVMax-t:rawVMin+t;
            return v.Swap ? new Point2(rv,ru) : new Point2(ru,rv);
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
        // Larger L-shaped homes can support a much better zoning than
        // the corridor-first fallback. Smaller footprints retain the
        // existing stepped planner instead of forcing skinny rooms.
        if(width<12.7 || stepX<10.8 || width-stepX<0.7 ||
            stepY<4.8 || stepY>6.0 || depth-stepY<3.1 ||
            depth-stepY>5.2)
            return false;
        var required=new[]
        {
            new Point2(0,0),new Point2(stepX,0),
            new Point2(stepX,stepY),new Point2(width,stepY),
            new Point2(width,depth),new Point2(0,depth)
        };
        if(required.Any(q=>!ring.Any(p=>
            Math.Abs(U(p)-q.X)<Eps &&
            Math.Abs(V(p)-q.Y)<Eps))) return false;

        reason="Reached dimensions but did not find a usable stair/room configuration.";
        const double stairRoomWidth=5.0;
        double hallRight=stairRoomWidth+v.SpineWidth;
        double kitchenDepth=stepY*v.KitchenDepth;
        // A narrow Kitchen return runs alongside the Pantry so that the
        // Kitchen can serve Dining directly, while keeping a preserved
        // front-wall hearth where it was before regeneration.
        const double servingReturnWidth=1.6;
        double pantryRight=stepX-servingReturnWidth;
        double bedroomDepth=stepY*v.BedroomDepth;
        double rearHeight=depth-stepY;
        double rightFrontWidth=stepX-hallRight;
        double rightRearWidth=width-hallRight;
        if(rightFrontWidth<3.5 || rightRearWidth<4.5 ||
            kitchenDepth<3 || stepY-kitchenDepth<1.5 ||
            pantryRight-hallRight<1.8 ||
            bedroomDepth<3.05 || stepY-bedroomDepth<1.5 ||
            rearHeight<3.1)
            return false;

        // Rectangular stair living-space on the lower floor,
        // sitting/landing room above. Neither needs a long corridor
        // running alongside its stair flight. Only the floor above
        // needs a free walkway BEHIND the upper flight.
        StaircaseDefinition stair;
        if(source.Staircases.Count==1)
            stair=source.Staircases.Single();
        else
            stair=new StaircaseDefinition
            {
                // Flush the full long edge with the exterior wall.
                // The old 0.85m centre offset left a 0.40m dead strip
                // beside the 0.90m-wide stair (unlike our other L plans).
                LowerFloorIndex=0,Centre=P(2.5,0.9/2),
                Width=0.9,Length=2.8,LandingLength=1,
                RotationDegrees=v.Swap
                    ? (v.FlipU?180:0) : (v.FlipU?90:270)
            };
        if(stair.LowerFloorIndex!=0) return false;
        // A manually placed stair is a hard constraint; never move it.
        // Automatically generated stairs, however, MUST be anchored.
        if(source.Staircases.Count==0 &&
            !BuildingConnectivity.StairLongEdgeOnExterior(result,stair))
        {
            reason="New stair long edge is not flush with the exterior wall.";
            return false;
        }
        reason="Room dimensions fit; staircase/landing reservation failed.";
        var landingRoom=Rect(0,0,stairRoomWidth,stepY);
        var reservations=new[]
        {
            BuildingStoreys.StairFootprint(stair),
            BuildingStoreys.StairOpeningFootprint(stair)
        }.Concat(BuildingStoreys.StairLandings(stair)).ToArray();
        if(!reservations.All(r=>FloorGeometry.IsWithinPolygon(
            r,landingRoom))) return false;

        // Confirm a full 1.1m walkable lane can run behind the
        // upper-flight opening from its far end to the hall door.
        // This is stronger than simply testing that the staircase
        // footprint and its two end landings fit in a room.
        var back=reservations.SelectMany(r=>r).Max(V);
        if(stepY-back<1.1-Eps) return false;
        if(!FloorGeometry.IsWithinPolygon(
            Rect(0,back,stairRoomWidth,back+1.1),landingRoom))
            return false;

        reason="Stair clearance passed; room allocation failed.";
        var hallway=Rect(stairRoomWidth,0,hallRight,depth);
        var groundRooms=new[]
        {
            ("Living room","Living room",Rect(0,0,stairRoomWidth,stepY)),
            // The old full-width rear pantry prevented a Kitchen-Dining
            // door. Keep the main Kitchen on the FRONT wall (including
            // accepted fireplace locations) and let a 1.6m Kitchen return
            // reach the shared Dining wall beside a compact Pantry.
            // This makes a useful L-shaped Kitchen without moving hearths.
            ("Kitchen","Kitchen",new List<Point2>
            {
                P(hallRight,0),P(stepX,0),P(stepX,stepY),
                P(pantryRight,stepY),P(pantryRight,kitchenDepth),
                P(hallRight,kitchenDepth)
            }),
            ("Pantry","Pantry",Rect(hallRight,kitchenDepth,
                pantryRight,stepY)),
            ("Study","Study",Rect(0,stepY,stairRoomWidth,depth)),
            ("Dining room","Dining room",Rect(hallRight,stepY,width,depth))
        };
        var firstRooms=new[]
        {
            ("Sitting room","Upper sitting room",
                Rect(0,0,stairRoomWidth,stepY)),
            ("Bedroom","Bedroom 2",
                Rect(hallRight,0,stepX,bedroomDepth)),
            ("Bathroom","Bathroom",
                Rect(hallRight,bedroomDepth,stepX,stepY)),
            ("Bedroom","Bedroom 3",
                Rect(0,stepY,stairRoomWidth,depth)),
            ("Bedroom","Master Bedroom",
                Rect(hallRight,stepY,width,depth))
        };
        var bedroomSizes=firstRooms.Where(r=>r.Item1=="Bedroom")
            .Select(r=>PolygonGeometry.Area(r.Item3)).ToArray();
        if(bedroomSizes.Min()<11 ||
            bedroomSizes.Max()/bedroomSizes.Min()>1.75)
            return false;
        if(new[]{stepY,stepX-hallRight,depth-stepY,
            stairRoomWidth}.Min()<3.05)
            return false;

        reason="Room size and balance passed; building geometry/doors failed.";
        for(int floorIndex=0;floorIndex<=1;floorIndex++)
        {
            var floor=BuildingStoreys.GetFloor(result,floorIndex);
            floor.Rooms.Add(new RoomDefinition
            {
                Type="Hallway",Name=floorIndex==0?"Entry passage":"Upper hall",
                Outline=hallway.ToList()
            });
            var roles=floorIndex==0?groundRooms:firstRooms;
            foreach(var role in roles)
                floor.Rooms.Add(new RoomDefinition
                {
                    Type=role.Item1,Name=role.Item2,
                    Outline=role.Item3.ToList()
                });
            BuildingStoreys.SetFloor(result,floorIndex,floor);
        }
        if(!BuildingStoreys.Validate(result,out reason)) return false;
        if(source.Staircases.Count==0)
        {
            if(!BuildingStoreys.TryAddStaircase(result,stair,
                out var connected,out reason)) return false;
            result=connected;
        }
        for(int index=0;index<=1;index++)
        {
            BuildingConnectivity.AddDoors(result,index,out var withDoors);
            result=withDoors;
        }

        // Regenerate clears the old room IDs associated with preserved
        // chimneys. The chimney itself is still fixed in place; reconnect
        // its hearth face to the NEW room enclosing the same masonry.
        // This prevents a formerly living-room hearth from looking
        // unassigned after the new layout makes that area the Kitchen.
        var completedFloors=result;
        foreach(var chimney in completedFloors.Chimneys.Where(ch=>
            ch.HearthRoomIds.Count==0 &&
            BuildingStoreys.HasFloor(completedFloors,ch.HearthFloorIndex)))
        {
            var level=BuildingStoreys.GetFloor(result,chimney.HearthFloorIndex);
            var footprint=BuildingStoreys.ChimneyFootprint(chimney);
            var room=level.Rooms.FirstOrDefault(r=>
                r.Type!="Hallway" &&
                FloorGeometry.IsWithinPolygon(footprint,r.Outline));
            if(room is null) continue;
            chimney.HearthRoomIds.Add(room.Id);
            if(!BuildingStoreys.Validate(result,out _))
                chimney.HearthRoomIds.Clear();
        }
        if(!BuildingStoreys.Validate(result,out reason)) return false;

        var serviced=BuildingStoreys.GetFloor(result,0);
        var kitchen=serviced.Rooms.Single(r=>r.Type=="Kitchen");
        var dining=serviced.Rooms.Single(r=>r.Type=="Dining room");
        var pantry=serviced.Rooms.Single(r=>r.Type=="Pantry");
        static bool OnShared(Point2 point,IReadOnlyList<Point2> outline)=>
            Enumerable.Range(0,outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(point,outline[i],
                    outline[(i+1)%outline.Count]).DistanceTo(point)<Eps);
        bool Linked(RoomDefinition first,RoomDefinition second)=>
            serviced.Doors.Any(d=>OnShared(d.Centre,first.Outline) &&
                OnShared(d.Centre,second.Outline));
        if(!Linked(kitchen,dining) || !Linked(kitchen,pantry))
        {
            reason="Kitchen needs direct doors to Dining and Pantry, " +
                "without routing service through the passage.";
            return false;
        }

        // A ground-floor living space can safely contain the stairs,
        // but each upstairs bedroom must have an independent door
        // from the hall (never via another bedroom or bathroom).
        reason="Building geometry passed; an independent hallway doorway is missing.";
        for(int index=0;index<=1;index++)
        {
            var floor=BuildingStoreys.GetFloor(result,index);
            var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
            static bool On(Point2 p,IReadOnlyList<Point2> poly)=>
                Enumerable.Range(0,poly.Count).Any(i=>
                    PolygonGeometry.ClosestPointOnSegment(p,
                        poly[i],poly[(i+1)%poly.Count])
                        .DistanceTo(p)<Eps);
            // The pantry should be a purposeful service room off
            // the kitchen; it need not consume a second door directly
            // into the hall. All other rooms (especially bedrooms)
            // require independent hall access, not through another
            // bedroom or a bathroom.
            foreach(var room in floor.Rooms.Where(r=>r.Type!="Hallway"))
            {
                bool fromHall=floor.Doors.Any(d=>
                    On(d.Centre,hall.Outline) &&
                    On(d.Centre,room.Outline));
                bool fromKitchen=room.Type=="Pantry" &&
                    floor.Doors.Any(d=>On(d.Centre,room.Outline) &&
                        floor.Rooms.Any(other=>other.Type=="Kitchen" &&
                            On(d.Centre,other.Outline)));
                if(!fromHall && !fromKitchen)
                {
                    reason=$"Floor {index} {room.Name} has no valid " +
                        "hallway or kitchen entrance.";
                    return false;
                }
            }
            if(index==0 && !floor.Rooms.Any(r=>r.Type=="Kitchen"))
                return false;
            double covered=floor.Rooms.Sum(r=>
                PolygonGeometry.Area(r.Outline));
            if(Math.Abs(covered-PolygonGeometry.Area(floor.Perimeter))>0.01)
                return false;
        }
        reason="";
        return true;
    }
}
