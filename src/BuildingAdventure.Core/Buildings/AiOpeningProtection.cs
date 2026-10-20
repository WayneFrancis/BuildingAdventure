using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Safety guard for C#-proposed wall resizing/transfers. A door can still
/// appear to fit a long straight wall even when a NEW perpendicular partition
/// ends in the middle of its opening. Also preserve which existing rooms
/// each door connects; otherwise moving a wall past a doorway silently
/// changes a Kitchen door into a Pantry door.
/// </summary>
public static class AiOpeningProtection
{
    private const double Eps = 0.01;
    private const double CornerClearance = 0.1;

    public static bool PreservesExistingDoorAccess(
        BuildingDocument source, BuildingDocument proposed, int floorIndex)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) ||
            !BuildingStoreys.HasFloor(proposed, floorIndex)) return false;
        var before = BuildingStoreys.GetFloor(source, floorIndex);
        var after = BuildingStoreys.GetFloor(proposed, floorIndex);

        // This guard is for resizing, NOT intentional door deletion during
        // a merge or newly created rooms during a split. Keep the existing
        // IDs/adjacency relationships exactly as they were.
        var afterDoors = after.Doors.ToDictionary(d => d.Id);
        foreach (var original in before.Doors)
        {
            if (!afterDoors.TryGetValue(original.Id, out var moved) ||
                Math.Abs(original.Width - moved.Width) > Eps)
                return false;
            if (!DoorRoomIds(before, original).SetEquals(
                    DoorRoomIds(after, moved)))
                return false;
        }

        // Room corners that did not exist before must NOT arrive within an
        // opening's actual width plus corner clearance. This detects a new
        // T junction in the doorway, even if the door is otherwise supported
        // by a continuous wall according to FloorGeometry.TryFindDoorWall.
        var originalCorners = before.Rooms
            .SelectMany(r => r.Outline).ToArray();
        var newCorners = after.Rooms.SelectMany(r => r.Outline)
            .Where(point => !originalCorners.Any(old =>
                old.DistanceTo(point) < Eps)).ToArray();
        foreach (var door in after.Doors)
        foreach (var corner in newCorners)
        {
            if (door.Centre.DistanceTo(corner) <
                door.Width / 2 + CornerClearance - Eps)
                return false;
        }
        return true;
    }

    private static HashSet<Guid> DoorRoomIds(
        BuildingDocument floor, DoorDefinition door) =>
        floor.Rooms.Where(r => Enumerable.Range(0, r.Outline.Count)
            .Any(i => PolygonGeometry.ClosestPointOnSegment(
                door.Centre, r.Outline[i],
                r.Outline[(i + 1) % r.Outline.Count])
                .DistanceTo(door.Centre) < Eps))
            .Select(r => r.Id).ToHashSet();
}
