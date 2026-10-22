namespace CivilDynamoTools.Diagnostics.Geometry;

public enum VertexClassification
{
    /// <summary>Two lines meet with a real change of direction; angles are computed.</summary>
    Corner,

    /// <summary>Two lines meet with no change of direction (within tolerance), e.g. a split segment.</summary>
    CollinearJunction,

    /// <summary>At least one adjacent segment is a curve; angles are not computed.</summary>
    CurveAdjacent,

    /// <summary>At least one adjacent segment is unclassified (and none is a curve); angles are not computed.</summary>
    UnknownAdjacent,

    /// <summary>An adjacent line has zero length, or the two lines double back on each other.</summary>
    Degenerate,
}

public sealed record CornerOptions
{
    /// <summary>Lines whose direction changes by no more than this are a collinear junction, not a corner. Default: 1 arc-second.</summary>
    public double CollinearToleranceDegrees { get; init; } = 1.0 / 3600.0;

    /// <summary>Lines no longer than this are treated as zero-length.</summary>
    public double LinearTolerance { get; init; } = SegmentOrderAnalyzer.DefaultLinearTolerance;

    /// <summary>
    /// True when the region of interest is enclosed by the loop (a parcel's outer boundary).
    /// False for a hole loop, where the parcel lies outside it and interior angles are the
    /// complement (360 - angle) of the loop-enclosed angle.
    /// </summary>
    public bool InteriorIsInsideLoop { get; init; } = true;
}

/// <summary>
/// One vertex of a chained loop: the point where <c>Incoming</c> ends and <c>Outgoing</c> starts.
/// Every vertex is reported, whatever its classification.
/// </summary>
public sealed record LoopVertex(
    int VertexIndex,
    Point2 Location,
    int IncomingSourceIndex,
    int OutgoingSourceIndex,
    SegmentKind IncomingKind,
    SegmentKind OutgoingKind,
    double IncomingChordLength,
    double OutgoingChordLength,
    VertexClassification Classification,
    double? DeflectionDegrees,
    double? InteriorAngleDegrees,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Unit vector from the vertex into the region of interest, splitting the interior angle in
    /// half. Present exactly when <see cref="InteriorAngleDegrees"/> is.
    /// </summary>
    public Point2? InteriorBisector { get; init; }

    public bool IsCorner => Classification == VertexClassification.Corner;

    public bool? IsReflex => InteriorAngleDegrees is double angle ? angle > 180.0 : null;
}

public static class CornerAnalyzer
{
    public static IReadOnlyList<LoopVertex> Analyze(OrientedLoop loop, CornerOptions? options = null)
    {
        options ??= new CornerOptions();

        bool orientationKnown = loop.HasReliableOrientation(options.LinearTolerance);

        // Sign that turns a signed deflection into the angle on the region-of-interest side.
        int interiorSign = (loop.IsCounterClockwise ? 1 : -1) * (options.InteriorIsInsideLoop ? 1 : -1);

        var vertices = new List<LoopVertex>(loop.Segments.Count);
        for (int i = 0; i < loop.Segments.Count; i++)
        {
            OrientedSegment incoming = loop.Segments[(i - 1 + loop.Segments.Count) % loop.Segments.Count];
            OrientedSegment outgoing = loop.Segments[i];
            vertices.Add(AnalyzeVertex(i, incoming, outgoing, orientationKnown, interiorSign, options));
        }
        return vertices;
    }

    private static LoopVertex AnalyzeVertex(
        int index, OrientedSegment incoming, OrientedSegment outgoing,
        bool orientationKnown, int interiorSign, CornerOptions options)
    {
        var warnings = new List<string>();
        double? deflection = null;
        double? interior = null;
        Point2? bisector = null;
        VertexClassification classification;

        if (incoming.Kind == SegmentKind.Curve || outgoing.Kind == SegmentKind.Curve)
        {
            classification = VertexClassification.CurveAdjacent;
        }
        else if (incoming.Kind == SegmentKind.Unknown || outgoing.Kind == SegmentKind.Unknown)
        {
            classification = VertexClassification.UnknownAdjacent;
        }
        else if (incoming.ChordLength <= options.LinearTolerance || outgoing.ChordLength <= options.LinearTolerance)
        {
            classification = VertexClassification.Degenerate;
            warnings.Add("An adjacent line is zero-length (within tolerance); its direction is undefined.");
        }
        else
        {
            double turn = SignedDeflectionDegrees(incoming, outgoing);
            deflection = turn;

            if (Math.Abs(turn) <= options.CollinearToleranceDegrees)
            {
                classification = VertexClassification.CollinearJunction;
            }
            else if (180.0 - Math.Abs(turn) <= options.CollinearToleranceDegrees)
            {
                classification = VertexClassification.Degenerate;
                warnings.Add("Adjacent lines double back on each other (deflection ~180 degrees).");
            }
            else
            {
                classification = VertexClassification.Corner;
            }

            if (orientationKnown)
            {
                interior = 180.0 - interiorSign * turn;
                bisector = InteriorBisector(outgoing, interior.Value, interiorSign);
            }
            else
                warnings.Add("Loop orientation is unreliable (near-zero vertex-polygon area); interior angle not computed.");
        }

        return new LoopVertex(
            index, outgoing.Start,
            incoming.SourceIndex, outgoing.SourceIndex,
            incoming.Kind, outgoing.Kind,
            incoming.ChordLength, outgoing.ChordLength,
            classification, deflection, interior, warnings)
        {
            InteriorBisector = bisector,
        };
    }

    /// <summary>
    /// The region of interest lies on the <paramref name="interiorSign"/> side of the outgoing
    /// line (+1 = left), and sweeping the outgoing direction that way by the interior angle
    /// reaches the incoming line; half that sweep is the bisector.
    /// </summary>
    private static Point2 InteriorBisector(OrientedSegment outgoing, double interiorDegrees, int interiorSign)
    {
        double heading = Math.Atan2(outgoing.End.Y - outgoing.Start.Y, outgoing.End.X - outgoing.Start.X);
        double angle = heading + interiorSign * interiorDegrees / 2.0 * Math.PI / 180.0;
        return new Point2(Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>Signed change of direction from incoming to outgoing, in (-180, 180]; positive = left turn.</summary>
    private static double SignedDeflectionDegrees(OrientedSegment incoming, OrientedSegment outgoing)
    {
        double ax = incoming.End.X - incoming.Start.X, ay = incoming.End.Y - incoming.Start.Y;
        double bx = outgoing.End.X - outgoing.Start.X, by = outgoing.End.Y - outgoing.Start.Y;
        double cross = ax * by - ay * bx;
        double dot = ax * bx + ay * by;
        return Math.Atan2(cross, dot) * 180.0 / Math.PI;
    }
}
