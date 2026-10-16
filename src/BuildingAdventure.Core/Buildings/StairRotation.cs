using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Rotation-handle geometry in building world coordinates. Screen Y increases
/// downwards, so 0 degrees points along the staircase's long axis towards
/// positive Y and positive angles rotate clockwise visually.
/// </summary>
public static class StairRotation
{
    /// <summary>
    /// World-coordinate location of the circular rotation grip, beyond
    /// the positive end of the staircase's long axis by offsetPixels.
    /// The pixel offset keeps it usable at different zoom levels.
    /// </summary>
    public static Point2 HandleCentre(
        StaircaseDefinition stair, double pixelsPerMetre, double offsetPixels = 24)
    {
        if (pixelsPerMetre <= 0 || !double.IsFinite(pixelsPerMetre))
            throw new ArgumentOutOfRangeException(nameof(pixelsPerMetre));
        double angle = stair.RotationDegrees * Math.PI / 180;
        double distance = stair.Length / 2 + offsetPixels / pixelsPerMetre;
        return new Point2(
            stair.Centre.X - Math.Sin(angle) * distance,
            stair.Centre.Y + Math.Cos(angle) * distance);
    }

    /// <summary>
    /// Angle of the cursor relative to the staircase's local positive long
    /// axis; returns [0, 360). Independent of zoom and floor orientation.
    /// </summary>
    public static double PointerAngle(Point2 centre, Point2 cursor)
    {
        double dx = cursor.X - centre.X;
        double dy = cursor.Y - centre.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy) ||
            Math.Sqrt(dx * dx + dy * dy) < 1e-6)
            throw new ArgumentException("Rotation pointer is too close to the staircase centre.");

        return Normalize(Math.Atan2(-dx, dy) * 180 / Math.PI);
    }

    /// <summary>
    /// A rotation drag preserves where within the handle the user grabbed:
    /// there is no angle jump on mouse-down. Shift can optionally constrain
    /// the angle to 15-degree increments.
    /// </summary>
    public static double FromDrag(
        double originalAngle, double initialPointerAngle, double currentPointerAngle,
        bool snapTo15Degrees)
    {
        double result = Normalize(originalAngle +
            NormalizeDelta(currentPointerAngle - initialPointerAngle));
        if (snapTo15Degrees)
            result = Normalize(Math.Round(result / 15, MidpointRounding.AwayFromZero) * 15);
        return result;
    }

    public static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees));
        double result = degrees % 360;
        return result < 0 ? result + 360 : result;
    }

    private static double NormalizeDelta(double delta)
    {
        delta %= 360;
        if (delta > 180) return delta - 360;
        if (delta < -180) return delta + 360;
        return delta;
    }
}
