using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class SegmentOrderAnalyzerTests
{
    [Fact]
    public void Ordered_loop_is_connected_as_given()
    {
        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(Loops.CcwSquare());

        Assert.True(report.AllJointsConnectedAsGiven);
        Assert.True(report.IsChainable);
        Assert.Equal(0, report.ReversedSegmentCount);
        Assert.All(report.Joints, j => Assert.Equal(0, j.GapAsGiven, 12));
    }

    [Fact]
    public void Joint_zero_is_the_closing_joint()
    {
        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(Loops.CcwSquare());

        Assert.Equal(3, report.Joints[0].PreviousSourceIndex);
        Assert.Equal(0, report.Joints[0].CurrentSourceIndex);
    }

    [Fact]
    public void Reversed_middle_segment_is_reported_and_flipped_when_chaining()
    {
        var segments = Loops.CcwSquare();
        segments[2] = Loops.Reversed(segments[2]);

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);

        Assert.False(report.AllJointsConnectedAsGiven);
        Assert.Equal(JointStatus.ConnectedIfCurrentReversed, report.Joints[2].Status);
        Assert.Equal(JointStatus.ConnectedIfPreviousReversed, report.Joints[3].Status);
        Assert.True(report.IsChainable);
        Assert.Equal(1, report.ReversedSegmentCount);
        Assert.True(report.ChainedLoop!.Segments[2].WasReversed);
        AssertContiguous(report.ChainedLoop);
    }

    [Fact]
    public void Reversed_first_segment_is_flipped_rather_than_failing()
    {
        var segments = Loops.CcwSquare();
        segments[0] = Loops.Reversed(segments[0]);

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);

        Assert.True(report.IsChainable);
        Assert.Equal(1, report.ReversedSegmentCount);
        Assert.True(report.ChainedLoop!.Segments[0].WasReversed);
        AssertContiguous(report.ChainedLoop);
    }

    [Fact]
    public void Every_segment_reversed_chains_without_flipping_in_the_opposite_direction()
    {
        var segments = Loops.CcwSquare().Select(Loops.Reversed).Reverse().ToList();

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);

        Assert.True(report.AllJointsConnectedAsGiven);
        Assert.Equal(0, report.ReversedSegmentCount);
        Assert.False(report.ChainedLoop!.IsCounterClockwise);
    }

    [Fact]
    public void Out_of_order_segments_are_not_reordered()
    {
        var segments = Loops.CcwSquare();
        (segments[1], segments[2]) = (segments[2], segments[1]);

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);

        Assert.False(report.IsChainable);
        Assert.Null(report.ChainedLoop);
        Assert.NotNull(report.ChainFailure);
        Assert.Contains(report.Joints, j => j.Status == JointStatus.Disconnected);
    }

    [Fact]
    public void Open_loop_is_not_chainable()
    {
        var segments = Loops.Polygon((0, 0), (10, 0), (10, 10), (0, 10));
        segments[3] = segments[3] with { End = new Point2(0, 0.5) };

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments);

        Assert.False(report.IsChainable);
        Assert.Contains("does not close", report.ChainFailure);
        Assert.Equal(0.5, report.Joints[0].GapAsGiven, 12);
        Assert.Equal(JointStatus.Disconnected, report.Joints[0].Status);
    }

    [Theory]
    [InlineData(1e-7, true)]
    [InlineData(1e-3, false)]
    public void Gaps_are_judged_against_the_linear_tolerance(double gap, bool expectConnected)
    {
        var segments = Loops.CcwSquare();
        segments[1] = segments[1] with { Start = new Point2(10 + gap, 0) };

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(segments, linearTolerance: 1e-6);

        Assert.Equal(expectConnected, report.Joints[1].Status == JointStatus.Connected);
        Assert.Equal(gap, report.Joints[1].GapAsGiven, 12);
    }

    [Fact]
    public void Single_closed_curve_is_a_valid_loop()
    {
        var circle = new LoopSegment(0, new Point2(5, 0), new Point2(5, 0), SegmentKind.Curve);

        SegmentOrderReport report = SegmentOrderAnalyzer.Analyze(new[] { circle });

        Assert.Equal(JointStatus.Connected, report.Joints.Single().Status);
        Assert.True(report.IsChainable);
    }

    [Fact]
    public void Empty_loop_throws()
    {
        Assert.Throws<ArgumentException>(() => SegmentOrderAnalyzer.Analyze(Array.Empty<LoopSegment>()));
    }

    private static void AssertContiguous(OrientedLoop loop)
    {
        for (int i = 0; i < loop.Segments.Count; i++)
        {
            var next = loop.Segments[(i + 1) % loop.Segments.Count];
            Assert.Equal(0, loop.Segments[i].End.DistanceTo(next.Start), 12);
        }
    }
}
