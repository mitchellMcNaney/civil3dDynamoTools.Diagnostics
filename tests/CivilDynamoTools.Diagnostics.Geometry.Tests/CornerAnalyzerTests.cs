using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class CornerAnalyzerTests
{
    private const int Precision = 9;

    [Fact]
    public void Ccw_square_has_four_right_angle_corners()
    {
        var vertices = Loops.Corners(Loops.CcwSquare());

        Assert.Equal(4, vertices.Count);
        Assert.All(vertices, v =>
        {
            Assert.Equal(VertexClassification.Corner, v.Classification);
            Assert.Equal(90.0, v.InteriorAngleDegrees!.Value, Precision);
            Assert.Equal(90.0, v.DeflectionDegrees!.Value, Precision);
            Assert.False(v.IsReflex);
        });
    }

    [Fact]
    public void Cw_square_still_reports_interior_angles()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (0, 10), (10, 10), (10, 0)));

        Assert.All(vertices, v =>
        {
            Assert.Equal(90.0, v.InteriorAngleDegrees!.Value, Precision);
            Assert.Equal(-90.0, v.DeflectionDegrees!.Value, Precision);
        });
    }

    [Fact]
    public void Vertex_i_is_the_start_of_segment_i()
    {
        var vertices = Loops.Corners(Loops.CcwSquare());

        Assert.Equal(new Point2(10, 0), vertices[1].Location);
        Assert.Equal(0, vertices[1].IncomingSourceIndex);
        Assert.Equal(1, vertices[1].OutgoingSourceIndex);
        Assert.Equal(3, vertices[0].IncomingSourceIndex);
    }

    [Fact]
    public void Right_triangle_angles_match_known_values()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (4, 0), (0, 3)));

        double atan34 = Math.Atan2(3, 4) * 180 / Math.PI;
        Assert.Equal(90.0, vertices[0].InteriorAngleDegrees!.Value, Precision);
        Assert.Equal(atan34, vertices[1].InteriorAngleDegrees!.Value, Precision);
        Assert.Equal(90.0 - atan34, vertices[2].InteriorAngleDegrees!.Value, Precision);
    }

    [Fact]
    public void Interior_angles_of_an_irregular_polygon_sum_to_n_minus_2_times_180()
    {
        var segments = Loops.Polygon((0, 0), (12.3, -1.7), (20.1, 4.4), (15.6, 9.9), (8.2, 6.1), (1.5, 11.0));

        var vertices = Loops.Corners(segments);

        Assert.Equal((vertices.Count - 2) * 180.0, vertices.Sum(v => v.InteriorAngleDegrees!.Value), Precision);
    }

    [Fact]
    public void Reflex_corner_of_an_l_shape_is_270()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (10, 0), (10, 5), (5, 5), (5, 10), (0, 10)));

        LoopVertex notch = vertices.Single(v => v.Location == new Point2(5, 5));
        Assert.Equal(270.0, notch.InteriorAngleDegrees!.Value, Precision);
        Assert.True(notch.IsReflex);
        Assert.Equal(-90.0, notch.DeflectionDegrees!.Value, Precision);
    }

    [Fact]
    public void Split_segment_produces_a_collinear_junction_that_is_kept()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (4, 0), (10, 0), (10, 10), (0, 10)));

        Assert.Equal(5, vertices.Count);
        LoopVertex junction = vertices.Single(v => v.Location == new Point2(4, 0));
        Assert.Equal(VertexClassification.CollinearJunction, junction.Classification);
        Assert.False(junction.IsCorner);
        Assert.Equal(180.0, junction.InteriorAngleDegrees!.Value, Precision);
        Assert.Equal(4, vertices.Count(v => v.IsCorner));
    }

    [Theory]
    [InlineData(0.5, VertexClassification.CollinearJunction)]
    [InlineData(2.0, VertexClassification.Corner)]
    public void Near_collinear_is_judged_against_the_angular_tolerance(double bendArcSeconds, VertexClassification expected)
    {
        double bend = bendArcSeconds / 3600.0 * Math.PI / 180.0;
        var bentPoint = (10 + 10 * Math.Cos(bend), 10 * Math.Sin(bend));
        var segments = Loops.Polygon((0, 0), (10, 0), bentPoint, (20, 10), (0, 10));

        var vertices = Loops.Corners(segments, new CornerOptions { CollinearToleranceDegrees = 1.0 / 3600.0 });

        Assert.Equal(expected, vertices[1].Classification);
        Assert.Equal(bendArcSeconds / 3600.0, vertices[1].DeflectionDegrees!.Value, Precision);
    }

    [Fact]
    public void Curve_adjacent_vertices_are_reported_without_angles()
    {
        var segments = Loops.CcwSquare();
        segments[1] = segments[1] with { Kind = SegmentKind.Curve };

        var vertices = Loops.Corners(segments);

        Assert.Equal(4, vertices.Count);
        foreach (int i in new[] { 1, 2 })
        {
            Assert.Equal(VertexClassification.CurveAdjacent, vertices[i].Classification);
            Assert.Null(vertices[i].InteriorAngleDegrees);
            Assert.Null(vertices[i].DeflectionDegrees);
            Assert.Null(vertices[i].IsReflex);
        }
        Assert.Equal(VertexClassification.Corner, vertices[0].Classification);
        Assert.Equal(VertexClassification.Corner, vertices[3].Classification);
    }

    [Fact]
    public void Unknown_segments_block_angles_like_curves()
    {
        var segments = Loops.CcwSquare();
        segments[1] = segments[1] with { Kind = SegmentKind.Unknown };

        var vertices = Loops.Corners(segments);

        Assert.Equal(VertexClassification.UnknownAdjacent, vertices[1].Classification);
        Assert.Null(vertices[1].InteriorAngleDegrees);
    }

    [Fact]
    public void Zero_length_line_is_degenerate_but_kept()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (10, 0), (10, 0), (10, 10), (0, 10)));

        Assert.Equal(5, vertices.Count);
        Assert.Equal(2, vertices.Count(v => v.Classification == VertexClassification.Degenerate));
        Assert.All(vertices.Where(v => v.Classification == VertexClassification.Degenerate), v =>
        {
            Assert.Null(v.InteriorAngleDegrees);
            Assert.NotEmpty(v.Warnings);
        });
    }

    [Fact]
    public void Very_short_line_still_gets_angles()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (10, 0), (10, 0.001), (10, 10), (0, 10)));

        Assert.Equal(VertexClassification.Corner, vertices[1].Classification);
        Assert.Equal(90.0, vertices[1].InteriorAngleDegrees!.Value, Precision);
        Assert.Equal(0.001, vertices[1].OutgoingChordLength, Precision);
        Assert.Equal(VertexClassification.CollinearJunction, vertices[2].Classification);
    }

    [Fact]
    public void Spike_that_doubles_back_is_degenerate()
    {
        var vertices = Loops.Corners(Loops.Polygon((0, 0), (10, 0), (15, 0), (10, 0), (10, 10), (0, 10)));

        LoopVertex tip = vertices.Single(v => v.Location == new Point2(15, 0));
        Assert.Equal(VertexClassification.Degenerate, tip.Classification);
        Assert.Contains(tip.Warnings, w => w.Contains("double back"));
    }

    [Fact]
    public void Hole_loop_reports_angles_on_the_outside()
    {
        var vertices = Loops.Corners(Loops.CcwSquare(), new CornerOptions { InteriorIsInsideLoop = false });

        Assert.All(vertices, v => Assert.Equal(270.0, v.InteriorAngleDegrees!.Value, Precision));
    }

    [Fact]
    public void Chained_reversed_segments_give_the_same_angles()
    {
        var segments = Loops.Polygon((0, 0), (4, 0), (10, 0), (10, 10), (0, 10));
        segments[1] = Loops.Reversed(segments[1]);
        segments[3] = Loops.Reversed(segments[3]);

        var vertices = Loops.Corners(segments);

        Assert.Equal(new double?[] { 90, 180, 90, 90, 90 }, vertices.Select(v => v.InteriorAngleDegrees is double a ? Math.Round(a, 9) : (double?)null));
    }

    [Fact]
    public void Two_arc_lens_has_unreliable_orientation_but_curve_vertices_are_still_reported()
    {
        var segments = new List<LoopSegment>
        {
            new(0, new Point2(0, 0), new Point2(10, 0), SegmentKind.Curve),
            new(1, new Point2(10, 0), new Point2(0, 0), SegmentKind.Curve),
        };

        var loop = Loops.Chain(segments);
        var vertices = CornerAnalyzer.Analyze(loop);

        Assert.False(loop.HasReliableOrientation(1e-6));
        Assert.Equal(2, vertices.Count);
        Assert.All(vertices, v => Assert.Equal(VertexClassification.CurveAdjacent, v.Classification));
    }
}
