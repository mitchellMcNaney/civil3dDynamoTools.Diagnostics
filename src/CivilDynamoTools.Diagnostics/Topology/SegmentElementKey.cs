namespace CivilDynamoTools.Diagnostics.Topology;

/// <summary>
/// Identity of one parcel-loop segment element: the parent ParcelSegment's stable
/// identity, plus its direction-normalized, tolerance-rounded endpoints. Two elements
/// with the same parent segment identity whose endpoints match A-to-B or B-to-A both
/// produce the same key. Elements with different parent segment identities are never
/// merged, even if their endpoints happen to be numerically close.
/// </summary>
internal readonly record struct SegmentElementKey(string ParentSegmentIdentity, double X1, double Y1, double X2, double Y2)
{
    /// <summary>
    /// Coordinates are rounded to this many decimal places before comparison. This is
    /// the numeric tolerance for treating two endpoints as "the same point" - it does
    /// not merge elements across different parent segments.
    /// </summary>
    private const int CoordinateDecimalPlaces = 6;

    public static SegmentElementKey Create(
        string parentSegmentIdentity,
        double startX, double startY,
        double endX, double endY,
        out bool reversed)
    {
        double x1 = Round(startX), y1 = Round(startY);
        double x2 = Round(endX), y2 = Round(endY);

        reversed = IsAfter(x1, y1, x2, y2);
        if (reversed)
        {
            (x1, x2) = (x2, x1);
            (y1, y2) = (y2, y1);
        }

        return new SegmentElementKey(parentSegmentIdentity, x1, y1, x2, y2);
    }

    private static bool IsAfter(double x1, double y1, double x2, double y2)
        => x1 > x2 || (x1 == x2 && y1 > y2);

    private static double Round(double value)
        => Math.Round(value, CoordinateDecimalPlaces, MidpointRounding.AwayFromZero);

    public override string ToString()
        => $"{ParentSegmentIdentity}|{X1:F6},{Y1:F6}->{X2:F6},{Y2:F6}";
}
