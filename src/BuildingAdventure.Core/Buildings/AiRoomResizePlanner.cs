using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Deterministic shared-partition proposals between two rectangular rooms.
/// No wall or doorway is ever modified until a validated suggestion is accepted.
/// </summary>
public sealed record AiRoomResizeCandidate(
    BuildingSuggestion Suggestion, string GainingRoom, string LosingRoom,
    double AreaSquareMetres, double GainingAreaBefore, double GainingAreaAfter);

public static class AiRoomResizePlanner
{
    private const double Eps = 1e-6;
    private static readonly double[] Steps = [0.3, 0.6, 0.9];

    public static IReadOnlyList<AiRoomResizeCandidate> FindCandidates(
        BuildingDocument source, int floorIndex, int maxResults = 12)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) || maxResults <= 0)
            return [];
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var rooms = floor.Rooms.Where(r =>
            r.Type is not ("Hallway" or "Corridor") &&
            TryBounds(r.Outline, out _)).ToArray();
        var options = new List<AiRoomResizeCandidate>();
        for (int i = 0; i < rooms.Length; i++)
        for (int j = i + 1; j < rooms.Length; j++)
        {
            var first = rooms[i]; var second = rooms[j];
            if (!TryBounds(first.Outline, out var a) ||
                !TryBounds(second.Outline, out var b))
                continue;

            // Only a complete, straight, common wall: coincident y spans
            // for a vertical partition or coincident x spans for horizontal.
            // Partial wall contacts are delegated to future edit types.
            bool vertical = Same(a.Y0,b.Y0) && Same(a.Y1,b.Y1) &&
                (Same(a.X1,b.X0) || Same(b.X1,a.X0));
            bool horizontal = Same(a.X0,b.X0) && Same(a.X1,b.X1) &&
                (Same(a.Y1,b.Y0) || Same(b.Y1,a.Y0));
            if (!vertical && !horizontal) continue;

            double sharedCoordinate = vertical
                ? (Same(a.X1,b.X0) ? a.X1 : b.X1)
                : (Same(a.Y1,b.Y0) ? a.Y1 : b.Y1);
            Point2 wallA = vertical
                ? new(sharedCoordinate,a.Y0)
                : new(a.X0,sharedCoordinate);
            Point2 wallB = vertical
                ? new(sharedCoordinate,a.Y1)
                : new(a.X1,sharedCoordinate);

            // Moving a door on the partition silently would be surprising.
            // Defer door relocation to an explicit future edit type.
            if (floor.Doors.Any(d => OnSegment(d.Centre,wallA,wallB)))
                continue;

            foreach (double delta in Steps)
            foreach (int sign in new[] {-1,1})
            {
                double movement = sign * delta;
                var nextA = MoveSharedEdge(a,vertical,sharedCoordinate,movement);
                var nextB = MoveSharedEdge(b,vertical,sharedCoordinate,movement);
                if (nextA is null || nextB is null ||
                    MinSide(nextA.Value) < 2.4 ||
                    MinSide(nextB.Value) < 2.4)
                    continue;

                var candidate = BuildingStoreys.Copy(source);
                var level = BuildingStoreys.GetFloor(candidate,floorIndex);
                var aRoom = level.Rooms.Single(r => r.Id == first.Id);
                var bRoom = level.Rooms.Single(r => r.Id == second.Id);
                aRoom.Outline = Rect(nextA.Value);
                bRoom.Outline = Rect(nextB.Value);
                BuildingStoreys.SetFloor(candidate,floorIndex,level);
                if (!BuildingStoreys.Validate(candidate,out _))
                    continue;

                // The pair must change area by equal and opposite amounts,
                // with no room stolen from anywhere else on the floor.
                double oldArea = PolygonGeometry.Area(aRoom.Outline) +
                    PolygonGeometry.Area(bRoom.Outline);
                double expectedArea = (a.X1-a.X0)*(a.Y1-a.Y0) +
                    (b.X1-b.X0)*(b.Y1-b.Y0);
                if (Math.Abs(oldArea-expectedArea)>0.001 ||
                    Math.Abs(RoomLayoutPlanner.UnassignedArea(floor) -
                        RoomLayoutPlanner.UnassignedArea(level))>0.01)
                    continue;

                double aChange = PolygonGeometry.Area(aRoom.Outline) -
                    PolygonGeometry.Area(first.Outline);
                double bChange = PolygonGeometry.Area(bRoom.Outline) -
                    PolygonGeometry.Area(second.Outline);
                if (Math.Abs(aChange+bChange)>0.001 || Math.Abs(aChange)<0.25)
                    continue;
                var gained = aChange>0 ? first : second;
                var lost = aChange>0 ? second : first;
                double gainedBefore=PolygonGeometry.Area(gained.Outline);
                double gainedArea=Math.Abs(aChange);
                var draft = new BuildingSuggestion(candidate,0,[floorIndex],
                    $"AI IMPROVEMENT DRAFT — Resize {gained.Name} and {lost.Name} " +
                    $"by moving their shared wall {delta:0.#} m.");
                options.Add(new AiRoomResizeCandidate(draft,gained.Name,
                    lost.Name,gainedArea,gainedBefore,gainedBefore+gainedArea));
            }
        }
        // Offer both directions, but smaller, meaningful changes first.
        return options.OrderBy(c => c.AreaSquareMetres)
            .ThenBy(c => c.GainingRoom, StringComparer.Ordinal)
            .ThenBy(c => c.LosingRoom, StringComparer.Ordinal)
            .Take(maxResults).ToArray();
    }

    private readonly record struct Bounds(
        double X0,double Y0,double X1,double Y1);

    private static bool TryBounds(IReadOnlyList<Point2> outline,
        out Bounds bounds)
    {
        bounds = default;
        if (outline.Count != 4) return false;
        double x0=outline.Min(p=>p.X),x1=outline.Max(p=>p.X);
        double y0=outline.Min(p=>p.Y),y1=outline.Max(p=>p.Y);
        if (x1-x0<2.4 || y1-y0<2.4) return false;
        var corners = new[]
        {
            new Point2(x0,y0),new Point2(x1,y0),
            new Point2(x1,y1),new Point2(x0,y1)
        };
        if (corners.Any(p => !outline.Any(q=>p.DistanceTo(q)<Eps)))
            return false;
        bounds = new Bounds(x0,y0,x1,y1);
        return true;
    }

    private static Bounds? MoveSharedEdge(Bounds b,bool vertical,
        double sharedCoordinate,double delta)
    {
        if (vertical)
        {
            if (Same(b.X1,sharedCoordinate))
                return new Bounds(b.X0,b.Y0,b.X1+delta,b.Y1);
            if (Same(b.X0,sharedCoordinate))
                return new Bounds(b.X0+delta,b.Y0,b.X1,b.Y1);
        }
        else
        {
            if (Same(b.Y1,sharedCoordinate))
                return new Bounds(b.X0,b.Y0,b.X1,b.Y1+delta);
            if (Same(b.Y0,sharedCoordinate))
                return new Bounds(b.X0,b.Y0+delta,b.X1,b.Y1);
        }
        return null;
    }

    private static double MinSide(Bounds b)=>
        Math.Min(b.X1-b.X0,b.Y1-b.Y0);

    private static List<Point2> Rect(Bounds b) =>
        [new(b.X0,b.Y0),new(b.X1,b.Y0),
         new(b.X1,b.Y1),new(b.X0,b.Y1)];

    private static bool Same(double a,double b)=>Math.Abs(a-b)<Eps;
    private static bool OnSegment(Point2 point,Point2 a,Point2 b)=>
        PolygonGeometry.ClosestPointOnSegment(point,a,b)
            .DistanceTo(point)<Eps;
}
