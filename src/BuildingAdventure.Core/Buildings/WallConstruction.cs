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

    private static bool OnRun(Point2 p, WallSegment wall) =>
        p.DistanceTo(PolygonGeometry.ClosestPointOnSegment(p, wall.A, wall.B)) < Epsilon;
}
