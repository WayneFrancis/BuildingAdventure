using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A small one-storey home needs actual living, cooking and sleeping
/// accommodation, not a corridor whose only purpose is to serve one room.
/// Divide a rectangular footprint into a through living room and two
/// independently accessed domestic rooms. All variants are detached,
/// seeded, reversible and checked by whole-building validation.
/// </summary>
public static class SmallCottagePlanner
{
    private const double Eps = 1e-5;

    public static bool TryGenerate(BuildingDocument source, int seed,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(source);
        reason = "A compact cottage needs a rectangular, empty ground floor " +
            "with room for living, kitchen, bedroom and an outside door.";
        if (BuildingStoreys.FloorCount(source) != 1 ||
            source.Staircases.Count != 0 || source.Chimneys.Count != 0)
            return false;
        var floor = BuildingStoreys.GetFloor(source, 0);
        if (floor.Perimeter.Count != 4 || floor.Rooms.Count != 0 ||
            floor.Doors.Count != 0 || floor.Windows.Count != 0)
            return false;

        double x0 = floor.Perimeter.Min(p=>p.X);
        double x1 = floor.Perimeter.Max(p=>p.X);
        double y0 = floor.Perimeter.Min(p=>p.Y);
        double y1 = floor.Perimeter.Max(p=>p.Y);
        if (floor.Perimeter.Any(p=>
                (Math.Abs(p.X-x0)>Eps && Math.Abs(p.X-x1)>Eps) ||
                (Math.Abs(p.Y-y0)>Eps && Math.Abs(p.Y-y1)>Eps)))
            return false;

        bool lengthAlongX = x1-x0 >= y1-y0;
        double length = lengthAlongX ? x1-x0 : y1-y0;
        double breadth = lengthAlongX ? y1-y0 : x1-x0;
        if (length < 6.5 || breadth < 5.6 ||
            length > 11.0 || breadth > 9.2) return false;

        var random = new Random(seed);
        for(int attempt=0;attempt<30;attempt++)
        {
            bool mirror=random.Next(2)==0;
            bool reverse=random.Next(2)==0;
            bool kitchenFirst=random.Next(2)==0;
            double splitU = Math.Clamp(
                breadth * (0.47 + random.NextDouble()*0.09),
                2.7,breadth-2.5);
            double splitV = Math.Clamp(
                length * (0.43 + random.NextDouble()*0.14),
                2.7,length-2.7);

            Point2 At(double u,double v)
            {
                double a=mirror ? breadth-u : u;
                double b=reverse ? length-v : v;
                return lengthAlongX
                    ? new Point2(x0+b,y0+a)
                    : new Point2(x0+a,y0+b);
            }
            List<Point2> Rect(double u0,double v0,double u1,double v1) =>
                [At(u0,v0),At(u1,v0),At(u1,v1),At(u0,v1)];

            var candidate=BuildingStoreys.Copy(source);
            var level=BuildingStoreys.GetFloor(candidate,0);
            level.Rooms=
            [
                new RoomDefinition
                {
                    Name="Living room",Type="Living room",
                    Outline=Rect(0,0,splitU,length)
                },
                new RoomDefinition
                {
                    Name=kitchenFirst?"Kitchen":"Bedroom",
                    Type=kitchenFirst?"Kitchen":"Bedroom",
                    Outline=Rect(splitU,0,breadth,splitV)
                },
                new RoomDefinition
                {
                    Name=kitchenFirst?"Bedroom":"Kitchen",
                    Type=kitchenFirst?"Bedroom":"Kitchen",
                    Outline=Rect(splitU,splitV,breadth,length)
                }
            ];
            level.Doors=
            [
                new DoorDefinition
                {
                    Centre=At(0,length/2),Width=0.9
                },
                new DoorDefinition
                {
                    Centre=At(splitU,splitV/2),Width=0.9
                },
                new DoorDefinition
                {
                    Centre=At(splitU,(splitV+length)/2),Width=0.9
                }
            ];
            BuildingStoreys.SetFloor(candidate,0,level);
            if (!BuildingStoreys.Validate(candidate,out reason))
                continue;
            updated=candidate;
            reason="";
            return true;
        }
        return false;
    }
}
