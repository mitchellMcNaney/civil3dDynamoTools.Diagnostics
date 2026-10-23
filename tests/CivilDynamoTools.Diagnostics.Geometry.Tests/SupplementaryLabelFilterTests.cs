using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class SupplementaryLabelFilterTests
{
    private const double Tolerance = 1.0 / 3600.0;

    private static AngleLabelPlan Plan(List<LoopSegment> loop)
        => AngleLabelPlanner.Plan(ParcelCornerAnalyzer.Analyze(new[] { loop }));

    private static IReadOnlyList<AngleLabelPlan> Apply(params (string Name, List<LoopSegment> Loop)[] parcels)
        => SupplementaryLabelFilter.Apply(parcels.Select(p => (p.Name, Plan(p.Loop))).ToList(), 1e-6, Tolerance);

    [Fact]
    public void T_junction_keeps_only_the_larger_angle()
    {
        // A slanted lot line from (5,0) to (4,-10) splits a strip between y=0 and y=-10.
        var lotA = Loops.Polygon((0, 0), (0, -10), (4, -10), (5, 0));
        var lotB = Loops.Polygon((5, 0), (4, -10), (10, -10), (10, 0));

        IReadOnlyList<AngleLabelPlan> plans = Apply(("A", lotA), ("B", lotB));

        // Top T: B is obtuse (~95.7), A acute. Bottom T: A is obtuse, B acute.
        PlannedAngleLabel top = Assert.Single(plans.SelectMany(p => p.Labels), l => l.Vertex == new Point2(5, 0));
        PlannedAngleLabel bottom = Assert.Single(plans.SelectMany(p => p.Labels), l => l.Vertex == new Point2(4, -10));
        Assert.True(top.InteriorAngleDegrees > 90);
        Assert.True(bottom.InteriorAngleDegrees > 90);
        Assert.Contains(plans[1].Labels, l => l == top);
        Assert.Contains(plans[0].Labels, l => l == bottom);

        SkippedVertex droppedTop = Assert.Single(plans[0].Skipped, s => s.Vertex == new Point2(5, 0));
        Assert.Equal($"Supplementary to {top.Text} labeled in parcel 'B' at this vertex.", droppedTop.Reason);
    }

    [Fact]
    public void Corners_at_a_vertex_that_do_not_sum_to_180_are_both_kept()
    {
        // The line through (5,0) bends there, so the two corners are independent.
        var lotA = Loops.Polygon((0, 1), (0, -10), (4, -10), (5, 0));
        var lotB = Loops.Polygon((5, 0), (4, -10), (10, -10), (10, 0));

        IReadOnlyList<AngleLabelPlan> plans = Apply(("A", lotA), ("B", lotB));

        Assert.Equal(2, plans.SelectMany(p => p.Labels).Count(l => l.Vertex == new Point2(5, 0)));
    }

    [Fact]
    public void Supplementary_angles_at_different_vertices_are_both_kept()
    {
        // A trapezoid's 80/100 pairs are on the same side, not at a shared vertex.
        var lot = Loops.Polygon((0, 0), (10, 0), (8, 10), (2, 10));

        IReadOnlyList<AngleLabelPlan> plans = Apply(("A", lot));

        Assert.Equal(4, plans[0].Labels.Count);
    }

    [Fact]
    public void Every_label_is_kept_or_skipped()
    {
        var lotA = Loops.Polygon((0, 0), (0, -10), (4, -10), (5, 0));
        var lotB = Loops.Polygon((5, 0), (4, -10), (10, -10), (10, 0));
        AngleLabelPlan a = Plan(lotA), b = Plan(lotB);

        IReadOnlyList<AngleLabelPlan> plans = SupplementaryLabelFilter.Apply(new[] { ("A", a), ("B", b) }, 1e-6, Tolerance);

        Assert.Equal(
            a.Labels.Count + a.Skipped.Count + b.Labels.Count + b.Skipped.Count,
            plans.Sum(p => p.Labels.Count + p.Skipped.Count));
    }
}
