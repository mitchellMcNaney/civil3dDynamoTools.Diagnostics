using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

/// <summary>Builders for test loops.</summary>
internal static class Loops
{
    /// <summary>Closed loop of lines through the given points, in order, with source direction = traversal direction.</summary>
    public static List<LoopSegment> Polygon(params (double X, double Y)[] points)
    {
        var segments = new List<LoopSegment>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            var (x1, y1) = points[i];
            var (x2, y2) = points[(i + 1) % points.Length];
            segments.Add(new LoopSegment(i, new Point2(x1, y1), new Point2(x2, y2), SegmentKind.Line));
        }
        return segments;
    }

    public static LoopSegment Reversed(LoopSegment s) => s with { Start = s.End, End = s.Start };

    public static OrientedLoop Chain(List<LoopSegment> segments)
    {
        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);
        Assert.True(report.IsChainable, report.ChainFailure);
        return report.ChainedLoop!;
    }

    public static IReadOnlyList<LoopVertex> Corners(List<LoopSegment> segments, CornerOptions? options = null)
        => CornerAnalyzer.Analyze(Chain(segments), options);

    /// <summary>Unit square, counter-clockwise from the origin.</summary>
    public static List<LoopSegment> CcwSquare() => Polygon((0, 0), (10, 0), (10, 10), (0, 10));
}
