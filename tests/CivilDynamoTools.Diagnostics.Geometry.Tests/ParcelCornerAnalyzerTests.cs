using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class ParcelCornerAnalyzerTests
{
    private const int Precision = 9;

    // Shapes mirror the parcels validated in Dynamo: single clockwise loops, ordered and unreversed.
    private static List<LoopSegment> CwSquare(double size = 10, double offset = 0)
        => Loops.Polygon((offset, offset), (offset, offset + size), (offset + size, offset + size), (offset + size, offset));

    private static IReadOnlyList<LoopCornerResult> Analyze(params List<LoopSegment>[] loops)
        => ParcelCornerAnalyzer.Analyze(loops);

    [Fact]
    public void Four_line_parcel_is_valid_with_four_corners()
    {
        LoopCornerResult loop = Analyze(CwSquare()).Single();

        Assert.True(loop.IsValid);
        Assert.Empty(loop.Issues);
        Assert.Equal(LoopRole.Outer, loop.Role);
        Assert.All(loop.Vertices, v => Assert.Equal(90.0, v.InteriorAngleDegrees!.Value, Precision));
    }

    [Fact]
    public void Split_segment_parcel_keeps_the_collinear_junction()
    {
        LoopCornerResult loop = Analyze(Loops.Polygon((0, 0), (0, 10), (10, 10), (10, 4), (10, 0))).Single();

        Assert.True(loop.IsValid);
        Assert.Equal(5, loop.Vertices.Count);
        Assert.Equal(4, loop.Vertices.Count(v => v.IsCorner));
        Assert.Single(loop.Vertices, v => v.Classification == VertexClassification.CollinearJunction);
    }

    [Fact]
    public void Curve_parcel_reports_curve_adjacent_vertices_without_angles()
    {
        var segments = CwSquare();
        segments[2] = segments[2] with { Kind = SegmentKind.Curve };

        LoopCornerResult loop = Analyze(segments).Single();

        Assert.True(loop.IsValid);
        Assert.Equal(2, loop.Vertices.Count(v => v.Classification == VertexClassification.CurveAdjacent));
        Assert.Equal(2, loop.Vertices.Count(v => v.IsCorner));
    }

    [Fact]
    public void Unchainable_loop_is_invalid_and_has_no_vertices()
    {
        var segments = CwSquare();
        (segments[1], segments[2]) = (segments[2], segments[1]);

        LoopCornerResult loop = Analyze(segments).Single();

        Assert.False(loop.IsValid);
        Assert.Empty(loop.Vertices);
        Assert.Contains(loop.Issues, i => i.Contains("could not be chained"));
    }

    [Fact]
    public void Reversed_element_is_processed_but_flagged()
    {
        var segments = CwSquare();
        segments[1] = Loops.Reversed(segments[1]);

        LoopCornerResult loop = Analyze(segments).Single();

        Assert.True(loop.IsValid);
        Assert.Contains(loop.Issues, i => i.Contains("reversed"));
        Assert.All(loop.Vertices, v => Assert.Equal(90.0, v.InteriorAngleDegrees!.Value, Precision));
    }

    [Fact]
    public void Larger_loop_is_outer_and_hole_angles_are_measured_outside_it()
    {
        var results = Analyze(Loops.Polygon((4, 4), (6, 4), (6, 6), (4, 6)), CwSquare());

        Assert.Equal(LoopRole.Hole, results[0].Role);
        Assert.Equal(LoopRole.Outer, results[1].Role);
        Assert.All(results[0].Vertices, v => Assert.Equal(270.0, v.InteriorAngleDegrees!.Value, Precision));
        Assert.All(results[1].Vertices, v => Assert.Equal(90.0, v.InteriorAngleDegrees!.Value, Precision));
        Assert.All(results, r =>
        {
            Assert.True(r.IsValid);
            Assert.Contains(r.Issues, i => i.Contains("inferred"));
        });
    }

    [Fact]
    public void Failed_sibling_loop_leaves_the_other_loop_without_interior_angles()
    {
        var broken = CwSquare(2, 4);
        (broken[1], broken[2]) = (broken[2], broken[1]);

        var results = Analyze(CwSquare(), broken);

        LoopCornerResult intact = results[0];
        Assert.Equal(LoopRole.Unknown, intact.Role);
        Assert.False(intact.IsValid);
        Assert.Equal(4, intact.Vertices.Count);
        Assert.All(intact.Vertices, v =>
        {
            Assert.Null(v.InteriorAngleDegrees);
            Assert.NotNull(v.DeflectionDegrees);
            Assert.True(v.IsCorner);
        });
        Assert.False(results[1].IsValid);
    }

    [Fact]
    public void Zero_area_loop_is_invalid()
    {
        var lens = new List<LoopSegment>
        {
            new(0, new Point2(0, 0), new Point2(10, 0), SegmentKind.Curve),
            new(1, new Point2(10, 0), new Point2(0, 0), SegmentKind.Curve),
        };

        LoopCornerResult loop = Analyze(lens).Single();

        Assert.False(loop.IsValid);
        Assert.Contains(loop.Issues, i => i.Contains("orientation"));
    }

    [Fact]
    public void Parcel_with_no_loops_throws()
    {
        Assert.Throws<ArgumentException>(() => ParcelCornerAnalyzer.Analyze(Array.Empty<IReadOnlyList<LoopSegment>>()));
    }
}
