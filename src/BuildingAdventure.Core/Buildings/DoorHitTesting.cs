using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Screen-sized, geometry-aware hit tests for the door glyph used by the
/// floor editor. A door is drawn as a gap in the wall PLUS a swinging leaf.
/// Clicking either visible part must target the door instead of the room
/// behind the swing. All measurements are normalized to screen pixels,
/// so selection remains usable while zooming.
/// </summary>
public static class DoorHitTesting
{
    private const double CentreRadiusPixels = 12;
    private const double OpeningRadiusPixels = 9;
    private const double LeafRadiusPixels = 11;

    /// <summary>
    /// Return the nearest visible door symbol under a cursor. A selected
    /// door wins an exact tie, but an unrelated farther door does not.
    /// Invalid/stale doors without a supporting wall are not selectable.
    /// </summary>
    public static DoorDefinition? FindDoorAt(
        IEnumerable<DoorDefinition> doors,
        IReadOnlyList<WallSegment> walls,
        Point2 pointer, double pixelsPerMetre,
        Guid? selectedDoorId = null)
    {
        if (!double.IsFinite(pixelsPerMetre) || pixelsPerMetre <= 0)
            return null;

        DoorDefinition? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var door in doors)
        {
            if (!FloorGeometry.TryFindDoorWall(door, walls, out var wall) ||
                wall is null || wall.Length <= 0)
                continue;

            double directionX = (wall.B.X - wall.A.X) / wall.Length;
            double directionY = (wall.B.Y - wall.A.Y) / wall.Length;
            var hinge = new Point2(
                door.Centre.X - directionX * door.Width / 2,
                door.Centre.Y - directionY * door.Width / 2);
            var latch = new Point2(
                door.Centre.X + directionX * door.Width / 2,
                door.Centre.Y + directionY * door.Width / 2);
            var openEnd = new Point2(
                hinge.X - directionY * door.Width,
                hinge.Y + directionX * door.Width);

            double centreDistance = pointer.DistanceTo(door.Centre)
                * pixelsPerMetre;
            double openingDistance = PolygonGeometry.ClosestPointOnSegment(
                pointer, hinge, latch).DistanceTo(pointer) * pixelsPerMetre;
            double leafDistance = PolygonGeometry.ClosestPointOnSegment(
                pointer, hinge, openEnd).DistanceTo(pointer) * pixelsPerMetre;

            double score = double.PositiveInfinity;
            if (centreDistance <= CentreRadiusPixels)
                score = Math.Min(score, centreDistance / CentreRadiusPixels);
            if (openingDistance <= OpeningRadiusPixels)
                score = Math.Min(score, openingDistance / OpeningRadiusPixels);
            if (leafDistance <= LeafRadiusPixels)
                score = Math.Min(score, leafDistance / LeafRadiusPixels);
            if (!double.IsFinite(score)) continue;

            if (score < bestScore - 1e-9 ||
                Math.Abs(score - bestScore) < 1e-9 &&
                    door.Id == selectedDoorId && best?.Id != selectedDoorId)
            {
                best = door;
                bestScore = score;
            }
        }
        return best;
    }
}
