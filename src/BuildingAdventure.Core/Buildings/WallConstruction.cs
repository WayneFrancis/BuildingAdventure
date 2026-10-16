using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Wall material sizes in metres. Wall centre lines continue to use the
/// original editable polygon edges; thickness is centred on those lines.
/// </summary>
public sealed class WallThicknessSettings
{
    public double ExteriorMetres { get; set; } = 0.24;
    public double InteriorMetres { get; set; } = 0.12;

    public WallThicknessSettings Copy() => new()
    {
        ExteriorMetres = ExteriorMetres,
        InteriorMetres = InteriorMetres
    };

    public bool Validate(out string reason)
    {
        if (!double.IsFinite(ExteriorMetres) ||
            ExteriorMetres < 0.08 || ExteriorMetres > 1.0)
        {
            reason = "Exterior wall thickness must be between 0.08 and 1 metre.";
            return false;
        }
        if (!double.IsFinite(InteriorMetres) ||
            InteriorMetres < 0.05 || InteriorMetres > 0.6)
        {
            reason = "Interior wall thickness must be between 0.05 and 0.6 metres.";
            return false;
        }
        reason = "";
        return true;
    }
}

/// <summary>Solid section between openings in an existing structural wall.</summary>
public sealed record WallDrawingPiece(Point2 A, Point2 B, bool IsExterior)
{
    public double Length => A.DistanceTo(B);
}

/// <summary>
/// Filled miter patch closing the wedge between structural wall strokes at
/// a real corner or a T/X junction. A separate exterior patch colour is
/// used when any connecting wall is an exterior wall.
/// </summary>
public sealed record WallJunctionPatch(
    Point2 Centre, IReadOnlyList<Point2> Outline, bool IsExterior);

/// <summary>
/// Produce physical wall runs with door and window openings subtracted.
/// This is a renderer-neutral model: SVG, 3D, and WPF can all use the same
/// gaps, even when an opening straddles a collinear split in the wall.
/// </summary>
public static class WallConstruction
{
    private const double Epsilon = 1e-6;

    public static List<WallDrawingPiece> SolidPieces(BuildingDocument floor)
    {
        var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms);
        var exteriorWalls = walls.Where(w => w.IsExterior).ToList();
        var openings = new List<(WallSegment Run, Point2 Centre, double Width)>();

        foreach (var door in floor.Doors)
            if (FloorGeometry.TryFindDoorWall(door, walls, out var run) &&
                run is not null)
                openings.Add((run, door.Centre, door.Width));

        foreach (var window in floor.Windows)
        {
            var proxy = new DoorDefinition
            {
                Id = window.Id, Centre = window.Centre, Width = window.Width
            };
            if (FloorGeometry.TryFindDoorWall(proxy, exteriorWalls, out var run) &&
                run is not null)
                openings.Add((run, window.Centre, window.Width));
        }

        var result = new List<WallDrawingPiece>();
        foreach (var wall in walls)
        {
            double length = wall.Length;
            if (length < Epsilon) continue;
            double ux = (wall.B.X - wall.A.X) / length;
            double uy = (wall.B.Y - wall.A.Y) / length;

            var gaps = new List<(double Start, double End)>();
            foreach (var opening in openings)
            {
                // Opening and wall must lie on the same supported run. This
                // allows an opening spanning two short collinear pieces.
                if (!OnRun(wall.A, opening.Run) ||
                    !OnRun(wall.B, opening.Run))
                    continue;
                double centreAlong =
                    (opening.Centre.X - wall.A.X) * ux +
                    (opening.Centre.Y - wall.A.Y) * uy;
                double start = Math.Max(0, centreAlong - opening.Width / 2);
                double end = Math.Min(length, centreAlong + opening.Width / 2);
                if (end > start + Epsilon)
                    gaps.Add((start, end));
            }

            double cursor = 0;
            foreach (var (start, end) in gaps.OrderBy(g => g.Start))
            {
                if (start > cursor + Epsilon)
                    Add(cursor, start);
                cursor = Math.Max(cursor, end);
            }
            if (length > cursor + Epsilon)
                Add(cursor, length);

            void Add(double start, double end)
            {
                Point2 a = new(wall.A.X + ux * start, wall.A.Y + uy * start);
                Point2 b = new(wall.A.X + ux * end, wall.A.Y + uy * end);
                result.Add(new WallDrawingPiece(a, b, wall.IsExterior));
            }
        }
        return result;
    }

    /// <summary>
    /// Return solid, mitred patches at non-collinear junctions only.
    /// Purely collinear split points don't receive caps or bulges, and
    /// window/door ends never receive a cap unless another solid piece
    /// genuinely joins at that exact endpoint.
    /// </summary>
    public static List<WallJunctionPatch> JunctionPatches(
        BuildingDocument floor, double outlinePaddingMetres = 0)
    {
        if (!double.IsFinite(outlinePaddingMetres) || outlinePaddingMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(outlinePaddingMetres));

        var pieces = SolidPieces(floor);
        var vertices = new List<(Point2 Position, List<(Point2 End, bool Exterior)> Arms)>();
        foreach (var piece in pieces)
        {
            Register(piece.A, piece.B, piece.IsExterior);
            Register(piece.B, piece.A, piece.IsExterior);
        }

        void Register(Point2 centre, Point2 far, bool exterior)
        {
            int i = vertices.FindIndex(v => v.Position.DistanceTo(centre) < Epsilon);
            if (i < 0)
            {
                vertices.Add((centre, new List<(Point2 End, bool Exterior)>()));
                i = vertices.Count - 1;
            }
            vertices[i].Arms.Add((far, exterior));
        }

        var patches = new List<WallJunctionPatch>();
        foreach (var (centre, arms) in vertices)
        {
            if (arms.Count < 2) continue;

            var directions = arms.Select(arm =>
            {
                double length = centre.DistanceTo(arm.End);
                double ux = (arm.End.X - centre.X) / length;
                double uy = (arm.End.Y - centre.Y) / length;
                double radius = (arm.Exterior
                    ? floor.WallThickness.ExteriorMetres
                    : floor.WallThickness.InteriorMetres) / 2 +
                    outlinePaddingMetres;
                return (ux, uy, radius);
            }).ToList();

            // There is no corner on a straight continuous wall, even if
            // room vertices split the wall into multiple shorter pieces.
            bool bent = false;
            for (int i = 0; i < directions.Count; i++)
            for (int j = i + 1; j < directions.Count; j++)
                bent |= Math.Abs(Cross(directions[i].ux, directions[i].uy,
                    directions[j].ux, directions[j].uy)) > 1e-5;
            if (!bent) continue;

            var polygonPoints = new List<Point2>();
            foreach (var (ux, uy, radius) in directions)
            {
                polygonPoints.Add(new Point2(
                    centre.X - uy * radius, centre.Y + ux * radius));
                polygonPoints.Add(new Point2(
                    centre.X + uy * radius, centre.Y - ux * radius));
            }

            // Intersections of both sides of each connected wall form sharp
            // miter corners. Cap very acute miters to avoid long spikes.
            for (int i = 0; i < directions.Count; i++)
            for (int j = i + 1; j < directions.Count; j++)
            {
                var a = directions[i];
                var b = directions[j];
                double denominator = Cross(a.ux, a.uy, b.ux, b.uy);
                if (Math.Abs(denominator) <= 1e-5) continue;
                foreach (int signA in new[] { -1, 1 })
                foreach (int signB in new[] { -1, 1 })
                {
                    Point2 offsetA = new(
                        centre.X - a.uy * signA * a.radius,
                        centre.Y + a.ux * signA * a.radius);
                    Point2 offsetB = new(
                        centre.X - b.uy * signB * b.radius,
                        centre.Y + b.ux * signB * b.radius);
                    double t = Cross(
                        offsetB.X - offsetA.X, offsetB.Y - offsetA.Y,
                        b.ux, b.uy) / denominator;
                    Point2 intersection = new(
                        offsetA.X + t * a.ux,
                        offsetA.Y + t * a.uy);
                    double maximum = Math.Max(a.radius, b.radius) * 3;
                    if (centre.DistanceTo(intersection) <= maximum)
                        polygonPoints.Add(intersection);
                }
            }

            var outline = ConvexHull(polygonPoints);
            if (outline.Count >= 3)
                patches.Add(new WallJunctionPatch(centre, outline,
                    arms.Any(arm => arm.Exterior)));
        }
        return patches;
    }

    private static double Cross(double ax, double ay, double bx, double by) =>
        ax * by - ay * bx;

    private static List<Point2> ConvexHull(IReadOnlyList<Point2> vertices)
    {
        var sorted = vertices
            .OrderBy(p => p.X).ThenBy(p => p.Y)
            .ToList();
        var hull = new List<Point2>();
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 &&
                Cross(hull[^1].X - hull[^2].X,
                      hull[^1].Y - hull[^2].Y,
                      p.X - hull[^1].X,
                      p.Y - hull[^1].Y) <= Epsilon)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        int lowerCount = hull.Count;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            Point2 p = sorted[i];
            while (hull.Count > lowerCount &&
                Cross(hull[^1].X - hull[^2].X,
                      hull[^1].Y - hull[^2].Y,
                      p.X - hull[^1].X,
                      p.Y - hull[^1].Y) <= Epsilon)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static bool OnRun(Point2 p, WallSegment wall) =>
        p.DistanceTo(PolygonGeometry.ClosestPointOnSegment(p, wall.A, wall.B)) < Epsilon;
}
