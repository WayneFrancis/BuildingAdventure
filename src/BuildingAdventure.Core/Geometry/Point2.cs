namespace BuildingAdventure.Core.Geometry;

/// <summary>A point in world coordinates, measured in metres.</summary>
public readonly record struct Point2(double X, double Y)
{
    public double DistanceTo(Point2 other) =>
        Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}
