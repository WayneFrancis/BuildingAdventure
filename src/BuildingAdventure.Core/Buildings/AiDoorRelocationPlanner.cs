using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// For an explicitly requested room resize, reposition an obstructed interior
/// door on a nearby partition between the SAME two rooms. Exterior entrances
/// never move. This only edits a detached preview, not the source building.
/// </summary>
public static class AiDoorRelocationPlanner
{
    private const double Eps = 0.01;
    private const double CornerClearance = 0.1;

    public static bool TryRelocateObstructedDoors(
        BuildingDocument source, BuildingDocument proposal, int floorIndex)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) ||
            !BuildingStoreys.HasFloor(proposal, floorIndex)) return false;
        var before = BuildingStoreys.GetFloor(source, floorIndex);
        var floor = BuildingStoreys.GetFloor(proposal, floorIndex);
        var doors = floor.Doors.ToDictionary(d => d.Id);
        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms);
        var originalCorners = before.Rooms.SelectMany(r => r.Outline).ToArray();
        var newCorners = floor.Rooms.SelectMany(r => r.Outline)
            .Where(p => !originalCorners.Any(old =>
                old.DistanceTo(p) < Eps)).ToArray();

        foreach (var original in before.Doors)
        {
            if (!doors.TryGetValue(original.Id, out var door) ||
                Math.Abs(original.Width - door.Width) > Eps) return false;
            var owners = RoomIds(before, original.Centre);
            if (Safe(door, owners)) continue;
            if (owners.Count != 2) return false; // Do not move exterior doors.

            var possible = new List<Point2>();
            foreach (var wall in walls.Where(w =>
                         !w.IsExterior && w.Length >= original.Width + 0.2))
            {
                int samples = Math.Max(2, (int)Math.Ceiling(wall.Length / 0.05));
                for (int i = 0; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    var p = new Point2(
                        wall.A.X + (wall.B.X - wall.A.X) * t,
                        wall.A.Y + (wall.B.Y - wall.A.Y) * t);
                    if (RoomIds(floor, p).SetEquals(owners))
                        possible.Add(p);
                }
            }
            var previous = door.Centre;
            bool found = false;
            foreach (var centre in possible
                .OrderBy(p => p.DistanceTo(original.Centre))
                .ThenBy(p => p.X).ThenBy(p => p.Y))
            {
                door.Centre = centre;
                if (!Safe(door, owners) ||
                    !AiDoorPlacementPlanner.ClearOfStairs(
                        proposal, floorIndex, centre, door.Width))
                    continue;
                found = true;
                break;
            }
            if (!found)
            {
                door.Centre = previous;
                return false;
            }
        }
        BuildingStoreys.SetFloor(proposal, floorIndex, floor);
        return true;

        bool Safe(DoorDefinition door, HashSet<Guid> owners) =>
            RoomIds(floor, door.Centre).SetEquals(owners) &&
            FloorGeometry.TryFindDoorWall(door, walls, out _) &&
            newCorners.All(p => p.DistanceTo(door.Centre) >=
                door.Width / 2 + CornerClearance - Eps) &&
            floor.Doors.Where(other => other.Id != door.Id)
                .All(other => other.Centre.DistanceTo(door.Centre) >=
                    (other.Width + door.Width) / 2 + 0.1) &&
            floor.Windows.All(w => w.Centre.DistanceTo(door.Centre) >=
                (w.Width + door.Width) / 2 + 0.1);
    }

    private static HashSet<Guid> RoomIds(BuildingDocument floor, Point2 point) =>
        floor.Rooms.Where(room => Enumerable.Range(0, room.Outline.Count)
            .Any(i => PolygonGeometry.ClosestPointOnSegment(
                point, room.Outline[i],
                room.Outline[(i + 1) % room.Outline.Count])
                .DistanceTo(point) < Eps))
            .Select(r => r.Id).ToHashSet();
}
