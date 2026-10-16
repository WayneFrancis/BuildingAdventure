namespace BuildingAdventure.Core.Geometry;

/// <summary>World-coordinate snapping independent of WPF pixels and mouse input.</summary>
public static class GeometrySnap
{
    /// <summary>
    /// Snap a moving vertex to an existing different vertex first, then an
    /// unrelated edge, then the grid. Edges incident on the original vertex
    /// are excluded to prevent dragging the point back onto its own wall.
    /// </summary>
    public static Point2 ForMovedVertex(Point2 target, Point2 original,
        IEnumerable<IReadOnlyList<Point2>> polygons,
        double proximityMetres, bool gridEnabled, double gridSize = 0.5,
        bool bypass = false)
    {
        if (bypass) return target;
        if (!double.IsFinite(proximityMetres) || proximityMetres <= 0)
            return gridEnabled ? Grid(target, gridSize) : target;

        var outlines = polygons.ToList();
        Point2 best = target;
        double bestDistance = proximityMetres;

        foreach (var outline in outlines)
        foreach (var corner in outline)
        {
            if (corner.DistanceTo(original) < 1e-7) continue;
            double distance = corner.DistanceTo(target);
            if (distance >= bestDistance) continue;
            best = corner;
            bestDistance = distance;
        }

        if (bestDistance < proximityMetres) return best;

        foreach (var outline in outlines)
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            if (a.DistanceTo(original) < 1e-7 ||
                b.DistanceTo(original) < 1e-7)
                continue;
            var onWall = PolygonGeometry.ClosestPointOnSegment(target, a, b);
            double distance = onWall.DistanceTo(target);
            if (distance >= bestDistance) continue;
            best = onWall;
            bestDistance = distance;
        }

        return bestDistance < proximityMetres
            ? best : gridEnabled ? Grid(target, gridSize) : target;
    }

    private static Point2 Grid(Point2 point, double size) =>
        new(Math.Round(point.X / size, MidpointRounding.AwayFromZero) * size,
            Math.Round(point.Y / size, MidpointRounding.AwayFromZero) * size);
}
