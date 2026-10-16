namespace BuildingAdventure.Core.Geometry;

/// <summary>Geometry operations independent of the rendering technology.</summary>
public static class PolygonGeometry
{
    private const double Epsilon = 1e-9;

    public static double SignedArea(IReadOnlyList<Point2> vertices)
    {
        if (vertices.Count < 3) return 0;
        double sum = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            Point2 a = vertices[i];
            Point2 b = vertices[(i + 1) % vertices.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    public static double Area(IReadOnlyList<Point2> vertices) =>
        Math.Abs(SignedArea(vertices));

    public static double Perimeter(IReadOnlyList<Point2> vertices)
    {
        if (vertices.Count < 2) return 0;
        double length = 0;
        for (int i = 0; i < vertices.Count; i++)
            length += vertices[i].DistanceTo(vertices[(i + 1) % vertices.Count]);
        return length;
    }

    public static bool IsValidOutline(IReadOnlyList<Point2> vertices, out string reason)
    {
        reason = "";
        if (vertices.Count < 3)
        {
            reason = "The perimeter needs at least three corners.";
            return false;
        }
        if (vertices.Count > 10000)
        {
            reason = "The perimeter has too many corners.";
            return false;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            Point2 a = vertices[i];
            Point2 b = vertices[(i + 1) % vertices.Count];
            if (!double.IsFinite(a.X) || !double.IsFinite(a.Y))
            {
                reason = "All coordinates must be finite.";
                return false;
            }
            if (a.DistanceTo(b) < 0.001)
            {
                reason = "Two adjacent corners are too close together.";
                return false;
            }
        }

        if (Area(vertices) < 0.01)
        {
            reason = "The enclosed area is too small.";
            return false;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            Point2 previous = vertices[(i + vertices.Count - 1) % vertices.Count];
            Point2 current = vertices[i];
            Point2 next = vertices[(i + 1) % vertices.Count];
            double cross = Orientation(previous, current, next);
            double dot = (previous.X - current.X) * (next.X - current.X) +
                         (previous.Y - current.Y) * (next.Y - current.Y);
            if (Math.Abs(cross) < Epsilon && dot > 0)
            {
                reason = "Adjacent walls cannot overlap or reverse direction.";
                return false;
            }
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            Point2 a1 = vertices[i];
            Point2 a2 = vertices[(i + 1) % vertices.Count];
            for (int j = i + 1; j < vertices.Count; j++)
            {
                // Adjacent edges deliberately meet at their shared endpoint.
                if (j == i + 1 || (i == 0 && j == vertices.Count - 1))
                    continue;
                if (SegmentsIntersect(a1, a2, vertices[j], vertices[(j + 1) % vertices.Count]))
                {
                    reason = "Perimeter walls cannot cross or touch each other.";
                    return false;
                }
            }
        }

        return true;
    }

    public static Point2 ClosestPointOnSegment(Point2 point, Point2 a, Point2 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Epsilon) return a;
        double t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) /
                              lengthSquared, 0, 1);
        return new Point2(a.X + dx * t, a.Y + dy * t);
    }

    public static bool ContainsPoint(IReadOnlyList<Point2> polygon, Point2 point)
    {
        if (polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point2 a = polygon[j];
            Point2 b = polygon[i];
            if (point.DistanceTo(ClosestPointOnSegment(point, a, b)) < Epsilon)
                return true;

            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double Orientation(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(Point2 a, Point2 p, Point2 b) =>
        p.X >= Math.Min(a.X, b.X) - Epsilon &&
        p.X <= Math.Max(a.X, b.X) + Epsilon &&
        p.Y >= Math.Min(a.Y, b.Y) - Epsilon &&
        p.Y <= Math.Max(a.Y, b.Y) + Epsilon;

    private static bool SegmentsIntersect(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        double o1 = Orientation(a, b, c);
        double o2 = Orientation(a, b, d);
        double o3 = Orientation(c, d, a);
        double o4 = Orientation(c, d, b);
        if (((o1 > Epsilon && o2 < -Epsilon) || (o1 < -Epsilon && o2 > Epsilon)) &&
            ((o3 > Epsilon && o4 < -Epsilon) || (o3 < -Epsilon && o4 > Epsilon)))
            return true;

        return (Math.Abs(o1) <= Epsilon && OnSegment(a, c, b)) ||
               (Math.Abs(o2) <= Epsilon && OnSegment(a, d, b)) ||
               (Math.Abs(o3) <= Epsilon && OnSegment(c, a, d)) ||
               (Math.Abs(o4) <= Epsilon && OnSegment(c, b, d));
    }
}
