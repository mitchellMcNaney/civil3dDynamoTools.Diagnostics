namespace CivilDynamoTools.Diagnostics.Geometry;

/// <summary>Plain 2D point in drawing units (plan X/Y).</summary>
public readonly record struct Point2(double X, double Y)
{
    public double DistanceTo(Point2 other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"({X:F6}, {Y:F6})";
}
