using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Safety guard for C#-proposed wall resizing/transfers. A door or window can
/// still appear to fit a continuous wall even when a NEW perpendicular
/// partition ends in the middle of its opening. Preserve existing doorway
/// connections and exterior-window room assignments as well.
/// </summary>
public static class AiOpeningProtection
{
    private const double Eps = 0.01;
    private const double CornerClearance = 0.1;
    private const double UnchangedOpeningTolerance = 1e-6;

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
            if (!OpeningRoomIds(before, original.Centre).SetEquals(
                    OpeningRoomIds(after, moved.Centre)))
                return false;
        }

        // Windows are fixed to the exterior facade. General building
        // validation checks the exterior wall, but cannot tell when a
        // shifted interior partition has reassigned a window to another
        // room. Reject silent movement, resizing and reassignment.
        var afterWindows = after.Windows.ToDictionary(w => w.Id);
        foreach (var original in before.Windows)
        {
            if (!afterWindows.TryGetValue(original.Id, out var moved) ||
                original.Centre.DistanceTo(moved.Centre) >
                    UnchangedOpeningTolerance ||
                Math.Abs(original.Width - moved.Width) >
                    UnchangedOpeningTolerance)
                return false;
            if (!OpeningRoomIds(before, original.Centre).SetEquals(
                    OpeningRoomIds(after, moved.Centre)))
                return false;
        }

        // Room corners that did not exist before must NOT arrive within an
        // opening's actual width plus corner clearance. This detects a new
        // T junction in a doorway or window even if the opening is still
        // supported by a continuous wall according to FloorGeometry.
        var originalCorners = before.Rooms
            .SelectMany(r => r.Outline).ToArray();
        var newCorners = after.Rooms.SelectMany(r => r.Outline)
            .Where(point => !originalCorners.Any(old =>
                old.DistanceTo(point) < Eps)).ToArray();
        foreach (var corner in newCorners)
        {
            if (after.Doors.Any(door => door.Centre.DistanceTo(corner) <
                    door.Width / 2 + CornerClearance - Eps) ||
                after.Windows.Any(window => window.Centre.DistanceTo(corner) <
                    window.Width / 2 + CornerClearance - Eps))
                return false;
        }
        return true;
    }

    private static HashSet<Guid> OpeningRoomIds(
        BuildingDocument floor, Point2 centre) =>
        floor.Rooms.Where(r => Enumerable.Range(0, r.Outline.Count)
            .Any(i => PolygonGeometry.ClosestPointOnSegment(
                centre, r.Outline[i],
                r.Outline[(i + 1) % r.Outline.Count])
                .DistanceTo(centre) < Eps))
            .Select(r => r.Id).ToHashSet();
}
