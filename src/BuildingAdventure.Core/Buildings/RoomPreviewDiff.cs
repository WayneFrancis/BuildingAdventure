using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Detect geometry edits to rooms whose identities are unchanged.
/// Suggested AI resizes keep the room IDs, so preview renderers must
/// compare outlines rather than considering only newly created IDs.
/// </summary>
public sealed record RoomGeometryChange(
    Guid RoomId, string Name, double BeforeSquareMetres,
    double AfterSquareMetres);

public static class RoomPreviewDiff
{
    private const double Tolerance = 1e-6;

    public static IReadOnlyList<RoomGeometryChange> ChangedExistingRooms(
        IReadOnlyList<RoomDefinition> current,
        IReadOnlyList<RoomDefinition> proposed)
    {
        var before = current.ToDictionary(r => r.Id);
        var changes = new List<RoomGeometryChange>();
        foreach (var next in proposed)
        {
            if (!before.TryGetValue(next.Id, out var old) ||
                SameOutline(old.Outline, next.Outline))
                continue;

            changes.Add(new RoomGeometryChange(next.Id, next.Name,
                PolygonGeometry.Area(old.Outline),
                PolygonGeometry.Area(next.Outline)));
        }
        return changes;
    }

    // Ignore a harmless rotation or reversal of a polygon's vertex order.
    // The visual diff should light up only when the geometry really changed.
    public static bool SameOutline(
        IReadOnlyList<Point2> first, IReadOnlyList<Point2> second)
    {
        if (first.Count != second.Count) return false;
        if (first.Count == 0) return true;

        int n = first.Count;
        for (int offset = 0; offset < n; offset++)
        {
            if (first[0].DistanceTo(second[offset]) > Tolerance)
                continue;
            bool forward = true, backward = true;
            for (int j = 1; j < n; j++)
            {
                forward &= first[j].DistanceTo(second[(offset + j) % n])
                    <= Tolerance;
                backward &= first[j].DistanceTo(
                    second[(offset - j + n) % n]) <= Tolerance;
                if (!forward && !backward) break;
            }
            if (forward || backward) return true;
        }
        return false;
    }
}
