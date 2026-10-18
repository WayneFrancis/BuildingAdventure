using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A small English medieval hall-house archetype, deliberately distinct
/// from a modern full-length central corridor. A short cross/screens
/// passage serves kitchen and pantry at the low end, an expansive hall
/// occupies the middle, and (where possible) a parlour occupies the
/// private high end. Later upper floors can remain separate chambers.
///
/// This is a conservative, detached alternative to the legacy layout:
/// it accepts only orthogonal footprints, uses exact existing perimeter
/// vertices including 5/10ft coordinates, and checks existing stairs
/// and their landings before replacing any generated geometry.
/// </summary>
public static class MedievalHallHousePlanner
{
    private const double Epsilon = 1e-6;

    private readonly record struct HallVariant(
        bool AlongX, bool Reverse, bool PrivateEnd,
        double ServiceFraction, double ScreenWidth,
        double SideSplit, double PrivateFraction, string ServiceRoom);

    // Legacy callers keep the established deterministic arrangement.
    // Generated suggestions pass a seed to vary the building's actual
    // architecture, not merely its GUIDs, names or window positions.
    public static bool TryReplaceGeneratedGroundFloor(BuildingDocument building,
        int seed, out BuildingDocument updated, out string reason) =>
        TryReplaceGeneratedGroundFloorCore(building, seed, out updated, out reason);

    public static bool TryReplaceGeneratedGroundFloor(BuildingDocument building,
        out BuildingDocument updated, out string reason) =>
        TryReplaceGeneratedGroundFloorCore(building, null, out updated, out reason);

    private static bool TryReplaceGeneratedGroundFloorCore(BuildingDocument building,
        int? seed,
        out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(building);
        reason = "No hall-house arrangement fitted the perimeter and stair reservations.";
        if (!BuildingStoreys.HasFloor(building, 0)) return false;
        var ground = BuildingStoreys.GetFloor(building, 0);
        if (ground.Perimeter.Count < 4 || ground.Perimeter.Any((p) =>
            !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return false;

        // Only rectilinear vernacular footprints can be partitioned
        // exactly by the coordinate-cell approach. Leave angled and
        // hand-sculpted polygons to the normal generator.
        for (int i = 0; i < ground.Perimeter.Count; i++)
        {
            var a = ground.Perimeter[i];
            var b = ground.Perimeter[(i + 1) % ground.Perimeter.Count];
            if (Math.Abs(a.X - b.X) > Epsilon &&
                Math.Abs(a.Y - b.Y) > Epsilon) return false;
        }

        double minX = ground.Perimeter.Min(p => p.X);
        double maxX = ground.Perimeter.Max(p => p.X);
        double minY = ground.Perimeter.Min(p => p.Y);
        double maxY = ground.Perimeter.Max(p => p.Y);
        bool longX = maxX - minX >= maxY - minY;
        var variants = new List<HallVariant>();
        if (seed.HasValue)
        {
            // Major and minor room allocations genuinely vary between
            // rerolls. Orientation and which end contains service rooms
            // are included in the selection, rather than always choosing
            // the first valid variant from a fixed list.
            foreach (bool alongX in new[] { longX, !longX })
            foreach (bool reverse in new[] { false, true })
            foreach (bool privateEnd in new[] { true, false })
            foreach (double serviceFraction in new[] { 0.20, 0.26, 0.31 })
            foreach (double screenWidth in new[] { 1.35, 1.55 })
            foreach (double sideSplit in new[] { 0.40, 0.50, 0.60 })
            foreach (double privateFraction in new[] { 0.20, 0.26 })
            foreach (string serviceRoom in new[] { "Pantry", "Buttery" })
                variants.Add(new(alongX, reverse, privateEnd,
                    serviceFraction, screenWidth, sideSplit,
                    privateFraction, serviceRoom));

            var random = new Random(seed.Value);
            for (int i = variants.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (variants[i], variants[other]) = (variants[other], variants[i]);
            }
        }
        else
        {
            // Preserve the geometry expected by older direct callers,
            // project files and tests without a supplied generation seed.
            foreach (bool alongX in new[] { longX, !longX })
            foreach (bool reverse in new[] { false, true })
            foreach (bool privateEnd in new[] { true, false })
            foreach (double serviceFraction in new[] { 0.26, 0.20, 0.31 })
                variants.Add(new(alongX, reverse, privateEnd,
                    serviceFraction, 1.45, 0.5, 0.23, "Pantry"));
        }

        foreach (var variant in variants)
        {
            if (TryPlan(building, variant, out var candidate, out var detail))
            {
                updated = candidate;
                reason = "";
                return true;
            }
            reason = detail;
        }
        return false;
    }

    private static bool TryPlan(BuildingDocument building, HallVariant variant,
        out BuildingDocument result,
        out string reason)
    {
        result = BuildingStoreys.Copy(building);
        reason = "";
        var floor = BuildingStoreys.GetFloor(building, 0);
        bool alongX = variant.AlongX;
        bool reverse = variant.Reverse;
        // A one-storey house needs somewhere to sleep. The layout choice
        // controls whether a sufficiently large house gets a second
        // bedchamber, never whether it gets its first one.
        bool singleStorey = BuildingStoreys.FloorCount(building) == 1;
        bool privateEnd = variant.PrivateEnd || singleStorey;
        double serviceFraction = variant.ServiceFraction;
        double uLow = floor.Perimeter.Min(p => alongX ? p.X : p.Y);
        double uHigh = floor.Perimeter.Max(p => alongX ? p.X : p.Y);
        double vLow = floor.Perimeter.Min(p => alongX ? p.Y : p.X);
        double vHigh = floor.Perimeter.Max(p => alongX ? p.Y : p.X);
        double length = uHigh - uLow;
        double depth = vHigh - vLow;
        double footprintArea = PolygonGeometry.Area(floor.Perimeter);
        // A manor great hall is inappropriate in a normal cottage.
        // Use the familiar communal-room role for modest houses.
        string gatheringRoom = footprintArea >= 140 ? "Great hall" : "Common room";
        bool twoBedrooms = singleStorey && variant.PrivateEnd &&
            footprintArea >= 85 && depth >= 6.3;
        if (length < 8 || depth < 6)
        {
            reason = "A hall house needs an 8m long by 6m broad main footprint.";
            return false;
        }

        // Cuts along the social axis: service end / screens passage /
        // communal hall / optionally a parlour at the high end.
        // Try different historical bay proportions. A stair flight
        // requires its entire landing envelope in a single room;
        // a slightly shorter service bay can put that landing inside
        // the great hall instead of intersecting the screens wall.
        double service = Math.Max(2.2,
            Math.Min(length * serviceFraction, length - 5.6));
        double screens = variant.ScreenWidth;
        double parlour = privateEnd
            ? Math.Max(singleStorey ? 2.6 : 2.3,
                length * variant.PrivateFraction) : 0;
        if (length - service - screens - parlour <
            (singleStorey ? 1.7 : 3.3))
        {
            reason = "Insufficient depth for a useful great hall.";
            return false;
        }

        double uService = reverse ? uHigh - service : uLow + service;
        double uScreen = reverse ? uService - screens : uService + screens;
        double uPrivate = reverse
            ? uLow + parlour
            : uHigh - parlour;
        double vSplit = vLow + depth * variant.SideSplit;
        var us = floor.Perimeter.Select(p => alongX ? p.X : p.Y)
            .Append(uLow).Append(uHigh).Append(uService)
            .Append(uScreen).Append(uPrivate)
            .Distinct().OrderBy(v => v).ToArray();
        var vs = floor.Perimeter.Select(p => alongX ? p.Y : p.X)
            .Append(vLow).Append(vHigh).Append(vSplit)
            .Distinct().OrderBy(v => v).ToArray();

        var cells = new Dictionary<string, List<List<Point2>>>();
        double assignedArea = 0;
        for (int i = 1; i < us.Length; i++)
        for (int j = 1; j < vs.Length; j++)
        {
            double a = us[i - 1], b = us[i];
            double c = vs[j - 1], d = vs[j];
            if (b - a < Epsilon || d - c < Epsilon) continue;
            var outline = Rect(alongX, a, b, c, d);
            Point2 centre = World(alongX, (a + b) / 2, (c + d) / 2);
            if (!PolygonGeometry.ContainsPoint(floor.Perimeter, centre))
                continue;

            // Every cell is wholly inside or outside an orthogonal polygon
            // because all exterior corner coordinates are in the cuts.
            double naturalU = reverse ? uHigh - (a + b) / 2 :
                (a + b) / 2 - uLow;
            string type = naturalU < service - Epsilon
                ? (c + d) / 2 < vSplit ? "Kitchen" : variant.ServiceRoom
                : naturalU < service + screens - Epsilon
                    ? "Hallway"
                    : privateEnd && naturalU > length - parlour + Epsilon
                        ? singleStorey
                            ? twoBedrooms && (c + d) / 2 >= vSplit
                                ? "Bedroom 2" : "Bedroom"
                            : "Parlour"
                        : gatheringRoom;
            if (!cells.TryGetValue(type, out var group))
                cells[type] = group = new List<List<Point2>>();
            group.Add(outline);
            assignedArea += PolygonGeometry.Area(outline);
        }

        double buildingArea = PolygonGeometry.Area(floor.Perimeter);
        if (Math.Abs(assignedArea - buildingArea) > 0.001)
        {
            reason = "The historical zoning did not cover the entire footprint.";
            return false;
        }

        var rooms = new List<RoomDefinition>();
        foreach (string type in new[] { "Hallway", "Kitchen",
                     variant.ServiceRoom, gatheringRoom,
                     singleStorey ? "Bedroom" : "Parlour", "Bedroom 2" })
        {
            if (!cells.TryGetValue(type, out var region) ||
                !TryUnionCells(region, out var outline))
            {
                if ((type == "Parlour" && (singleStorey || !privateEnd)) ||
                    (type == "Bedroom 2" && !twoBedrooms)) continue;
                reason = "The footprint divides a required hall-house room.";
                return false;
            }
            double area = PolygonGeometry.Area(outline);
            double minimum = type switch
            {
                "Great hall" => 25,
                "Common room" => 10,
                "Hallway" => 5,
                "Kitchen" => 5,
                "Pantry" or "Buttery" => 4,
                "Bedroom" or "Bedroom 2" => 8,
                _ => 7
            };
            if (area < minimum)
            {
                reason = "One of the historical room bays is too small.";
                return false;
            }
            rooms.Add(new RoomDefinition
            {
                Type = type == "Bedroom 2" ? "Bedroom" : type,
                Name = type == "Hallway" ? "Screens passage" : type,
                Outline = outline
            });
        }

        var ground = BuildingStoreys.GetFloor(result, 0);
        ground.Rooms = rooms;
        ground.Doors.Clear();
        ground.Windows.Clear();
        BuildingStoreys.SetFloor(result, 0, ground);
        if (!BuildingStoreys.Validate(result, out reason)) return false;

        // Install doors from the screens passage to both service rooms
        // and the great hall, then from the hall into the private end.
        BuildingConnectivity.AddDoors(result, 0, out var withDoors);
        if (!BuildingStoreys.Validate(withDoors, out reason)) return false;
        var accessible = BuildingStoreys.GetFloor(withDoors, 0);
        var screen = accessible.Rooms.Single(r => r.Type == "Hallway");
        var hall = accessible.Rooms.Single(r => r.Type == gatheringRoom);
        var kitchen = accessible.Rooms.Single(r => r.Type == "Kitchen");
        var pantry = accessible.Rooms.Single(r =>
            r.Type == variant.ServiceRoom);
        if (!HasDoor(accessible, screen, hall) ||
            !HasDoor(accessible, screen, kitchen) ||
            !(HasDoor(accessible, screen, pantry) ||
              HasDoor(accessible, kitchen, pantry)))
        {
            reason = "The service rooms must be accessible from the screen.";
            return false;
        }
        if (singleStorey)
        {
            var bedrooms = accessible.Rooms.Where(r => r.Type == "Bedroom")
                .ToArray();
            if (bedrooms.Length < 1 || bedrooms.Length > 2 ||
                bedrooms.Any(room => !HasDoor(accessible, hall, room)) ||
                bedrooms.Any(room => PolygonGeometry.Area(room.Outline) < 8))
            {
                reason = "Every single-storey bedroom needs a usable " +
                    "private room with direct access from the common area.";
                return false;
            }
        }
        else if (privateEnd && !HasDoor(accessible, hall,
            accessible.Rooms.Single(r => r.Type == "Parlour")))
        {
            reason = "The private parlour has no entrance from the main hall.";
            return false;
        }
        if (!accessible.Doors.Any(d => Enumerable.Range(0, accessible.Perimeter.Count)
            .Any(i => OnSegment(d.Centre, accessible.Perimeter[i],
                accessible.Perimeter[(i + 1) % accessible.Perimeter.Count]) &&
                OnWall(d.Centre, screen.Outline))))
        {
            reason = "The screens passage is missing its exterior entrance.";
            return false;
        }
        // Conservatively keep historical storage to one internal door.
        if (accessible.Doors.Count(d => OnWall(d.Centre, pantry.Outline)) != 1)
        {
            reason = "The pantry has more than one doorway.";
            return false;
        }
        if (BuildingConnectivity.RoomsIsolatedByServiceSpaces(withDoors, 0)
            .Count > 0) return false;
        result = withDoors;
        reason = "";
        return true;
    }

    private static bool TryUnionCells(List<List<Point2>> cells,
        out List<Point2> merged)
    {
        merged = new List<Point2>();
        if (cells.Count == 0) return false;
        merged = cells[0].ToList();
        var pending = cells.Skip(1).ToList();
        for (int pass = 0; pass < cells.Count && pending.Count > 0; pass++)
        {
            bool advanced = false;
            for (int j = 0; j < pending.Count; j++)
            {
                var a = new RoomDefinition { Outline = merged };
                var b = new RoomDefinition { Outline = pending[j] };
                // Only adjacent cells are eligible, and a valid orthogonal
                // union is a simple, single-ring polygon.
                var scratch = new BuildingDocument
                {
                    Perimeter = [
                        new(-10000,-10000),new(10000,-10000),
                        new(10000,10000),new(-10000,10000)
                    ],
                    Rooms = [a,b]
                };
                if (!RoomMerging.TryMergeRooms(scratch,a.Id,b.Id,
                    out var joined,out _)) continue;
                merged = joined.Rooms[0].Outline.ToList();
                pending.RemoveAt(j);
                advanced = true;
                break;
            }
            if (!advanced) return false;
        }
        return pending.Count == 0;
    }

    private static Point2 World(bool alongX, double u, double v) =>
        alongX ? new Point2(u,v) : new Point2(v,u);
    private static List<Point2> Rect(bool alongX, double u0, double u1,
        double v0, double v1) =>
        [World(alongX,u0,v0),World(alongX,u1,v0),
         World(alongX,u1,v1),World(alongX,u0,v1)];
    private static bool OnSegment(Point2 p, Point2 a, Point2 b) =>
        PolygonGeometry.ClosestPointOnSegment(p,a,b).DistanceTo(p)<1e-5;
    private static bool OnWall(Point2 p,IReadOnlyList<Point2> outline) =>
        Enumerable.Range(0,outline.Count).Any(i =>
            OnSegment(p,outline[i],outline[(i+1)%outline.Count]));
    private static bool HasDoor(BuildingDocument floor, RoomDefinition a,
        RoomDefinition b) =>
        floor.Doors.Any(d => OnWall(d.Centre,a.Outline) &&
            OnWall(d.Centre,b.Outline));
}
