using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Classifies usable interior points without assuming that unassigned
/// interior space is a hallway or automatically generating enclosing walls.
/// </summary>
public static class FloorSpaceAnalysis
{
    public enum SpaceKind { Outside, DefinedRoom, UnassignedInterior, FloorOpening }

    public static SpaceKind Classify(
        BuildingDocument floor,
        Point2 point,
        IReadOnlyList<StairwellOpening>? slabOpenings = null)
    {
        if (floor.Perimeter.Count < 3 ||
            !PolygonGeometry.ContainsPoint(floor.Perimeter, point))
            return SpaceKind.Outside;

        if (slabOpenings is not null &&
            slabOpenings.Any(voidArea =>
                PolygonGeometry.ContainsPoint(voidArea.Outline, point)))
            return SpaceKind.FloorOpening;

        if (floor.Rooms.Any(r => PolygonGeometry.ContainsPoint(r.Outline, point)))
            return SpaceKind.DefinedRoom;

        return SpaceKind.UnassignedInterior;
    }
}
