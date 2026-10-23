using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class AngleLabelPlannerTests
{
    private const int Precision = 9;

    private static AngleLabelPlan Plan(List<LoopSegment> loop, AngleLabelOptions? options = null)
        => AngleLabelPlanner.Plan(ParcelCornerAnalyzer.Analyze(new[] { loop }), options);

    private static void AssertInsideConvexPolygon(Point2 p, List<LoopSegment> loop)
    {
        // Point is inside a convex CCW/CW polygon when it lies on the same side of every edge.
        var sides = loop.Select(s => Math.Sign((s.End.X - s.Start.X) * (p.Y - s.Start.Y) - (s.End.Y - s.Start.Y) * (p.X - s.Start.X))).ToList();
        Assert.True(sides.All(x => x == sides[0]) && sides[0] != 0, $"{p} is not strictly inside the polygon.");
    }

    [Fact]
    public void Right_angles_are_skipped_with_a_reason()
    {
        AngleLabelPlan plan = Plan(Loops.CcwSquare());

        Assert.Empty(plan.Labels);
        Assert.Equal(4, plan.Skipped.Count);
        Assert.All(plan.Skipped, s => Assert.StartsWith("Right angle", s.Reason));
    }

    [Fact]
    public void Right_angles_can_be_labeled()
    {
        AngleLabelPlan plan = Plan(Loops.CcwSquare(), new AngleLabelOptions { SkipRightAngles = false });

        Assert.Equal(4, plan.Labels.Count);
        Assert.All(plan.Labels, l => Assert.Equal("90°00'00\"", l.Text));
    }

    [Fact]
    public void Reflex_right_angle_is_also_skipped()
    {
        AngleLabelPlan plan = Plan(Loops.Polygon((0, 0), (10, 0), (10, 5), (5, 5), (5, 10), (0, 10)));

        Assert.Empty(plan.Labels);
        Assert.Contains(plan.Skipped, s => s.Vertex == new Point2(5, 5) && s.Reason.Contains("270"));
    }

    [Fact]
    public void Near_right_angle_that_displays_as_90_is_skipped()
    {
        // 0.3 arc-second off square: displays as 90°00'00".
        double tilt = 0.3 / 3600 * Math.PI / 180;
        var loop = Loops.Polygon((0, 0), (10, 0), (10 + 10 * Math.Sin(tilt), 10 * Math.Cos(tilt)), (0, 10));

        AngleLabelPlan plan = Plan(loop);

        Assert.DoesNotContain(plan.Labels, l => l.VertexIndex == 1);
    }

    [Fact]
    public void Triangle_labels_sit_inside_on_the_bisector()
    {
        var loop = Loops.Polygon((0, 0), (40, 0), (0, 30));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { Offset = 2.0, TextHeight = 0.1 });

        // The 90° corner is skipped; the two acute corners are labeled.
        Assert.Equal(2, plan.Labels.Count);
        PlannedAngleLabel atB = plan.Labels.Single(l => l.Vertex == new Point2(40, 0));
        Assert.Equal(AngleFormatter.ToDms(Math.Atan2(3, 4) * 180 / Math.PI), atB.Text);
        Assert.Equal(2.0, atB.Vertex.DistanceTo(atB.Position), Precision);

        // On the bisector: equal angle to both edges.
        double half = Math.Atan2(3, 4) / 2;
        Assert.Equal(40 - 2.0 * Math.Cos(half), atB.Position.X, Precision);
        Assert.Equal(2.0 * Math.Sin(half), atB.Position.Y, Precision);

        Assert.All(plan.Labels, l => AssertInsideConvexPolygon(l.Position, loop));
    }

    [Fact]
    public void Clockwise_loop_labels_are_also_inside()
    {
        var loop = Loops.Polygon((0, 30), (40, 0), (0, 0));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { Offset = 2.0 });

        Assert.Equal(2, plan.Labels.Count);
        Assert.All(plan.Labels, l => AssertInsideConvexPolygon(l.Position, loop));
    }

    [Fact]
    public void Reflex_corner_label_points_into_the_parcel()
    {
        // Arrowhead notch: the vertex at (10, 4) is a reflex corner.
        var loop = Loops.Polygon((0, 0), (20, 0), (20, 10), (10, 4), (0, 10));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { Offset = 1.0, TextHeight = 0.1 });

        PlannedAngleLabel notch = plan.Labels.Single(l => l.Vertex == new Point2(10, 4));
        Assert.True(notch.InteriorAngleDegrees > 180);
        Assert.Equal(10.0, notch.Position.X, Precision);
        Assert.Equal(3.0, notch.Position.Y, Precision); // straight down, into the parcel
    }

    private static PlannedAngleLabel LabelAtOrigin(List<LoopSegment> loop)
        => Plan(loop, new AngleLabelOptions { Offset = 1.0, TextHeight = 0.1 }).Labels.Single(l => l.Vertex == new Point2(0, 0));

    [Fact]
    public void Downward_bisector_text_starts_at_the_vertex_and_reads_away()
    {
        // Apex at the top, parcel below it (the screenshot case).
        PlannedAngleLabel label = LabelAtOrigin(Loops.Polygon((-10, -10), (0, 0), (20, -10), (20, -20), (-10, -20)));

        double expected = ((-135.0) + Math.Atan2(-10, 20) * 180 / Math.PI) / 2;
        Assert.Equal(LabelAnchor.MiddleLeft, label.Anchor);
        Assert.Equal(expected, label.RotationDegrees, Precision);
    }

    [Fact]
    public void Leftward_bisector_is_flipped_to_stay_readable()
    {
        // Apex at the bottom, opening up and to the left: reading away would be upside-down.
        PlannedAngleLabel label = LabelAtOrigin(Loops.Polygon((10, 10), (0, 0), (-20, 10), (-20, 20), (10, 20)));

        double bisectorHeading = (45.0 + Math.Atan2(10, -20) * 180 / Math.PI) / 2; // ~99.2°, between the two edges
        Assert.Equal(LabelAnchor.MiddleRight, label.Anchor);
        Assert.Equal(bisectorHeading - 180.0, label.RotationDegrees, Precision);
    }

    [Fact]
    public void Rotations_are_never_upside_down()
    {
        var loop = Loops.Polygon((0, 0), (12.3, -1.7), (20.1, 4.4), (15.6, 9.9), (8.2, 6.1), (1.5, 11.0));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { SkipRightAngles = false });

        Assert.Equal(6, plan.Labels.Count);
        Assert.All(plan.Labels, l => Assert.InRange(l.RotationDegrees, -90.0 + 1e-9, 90.0));
    }

    [Fact]
    public void Narrow_angle_widens_the_gap_so_text_clears_both_lines()
    {
        // 20° sliver at the origin.
        double tip = 20 * Math.PI / 180;
        var loop = Loops.Polygon((0, 0), (100, 0), (100 * Math.Cos(tip), 100 * Math.Sin(tip)));
        const double height = 2.0;

        PlannedAngleLabel label = Plan(loop, new AngleLabelOptions { Offset = 1.0, TextHeight = height })
            .Labels.Single(l => l.Vertex == new Point2(0, 0));

        double gap = label.Vertex.DistanceTo(label.Position);
        Assert.Equal(0.75 * height / Math.Sin(tip / 2), gap, Precision);
        double distanceToBaseLine = label.Position.Y;
        Assert.True(distanceToBaseLine >= height / 2, $"Text half-height overlaps the line ({distanceToBaseLine}).");
    }

    [Fact]
    public void Wide_angle_uses_the_requested_offset()
    {
        PlannedAngleLabel label = LabelAtOrigin(Loops.Polygon((-10, -10), (0, 0), (20, -10), (20, -20), (-10, -20)));

        Assert.Equal(1.0, label.Vertex.DistanceTo(label.Position), Precision);
    }

    [Fact]
    public void Hole_loop_labels_point_away_from_the_hole()
    {
        var outer = Loops.Polygon((0, 0), (100, 0), (100, 100), (0, 100));
        var hole = Loops.Polygon((40, 40), (60, 40), (50, 60));

        AngleLabelPlan plan = AngleLabelPlanner.Plan(
            ParcelCornerAnalyzer.Analyze(new[] { outer, hole }), new AngleLabelOptions { Offset = 1.0 });

        PlannedAngleLabel apex = plan.Labels.Single(l => l.Vertex == new Point2(50, 60));
        Assert.True(apex.Position.Y > 60, "Hole label should sit outside the hole (in the parcel).");
    }

    [Fact]
    public void Every_vertex_is_either_labeled_or_skipped()
    {
        var loop = Loops.Polygon((0, 0), (4, 0), (10, 0), (13, 8), (0, 10));
        loop[3] = loop[3] with { Kind = SegmentKind.Curve };

        AngleLabelPlan plan = Plan(loop);

        Assert.Equal(5, plan.Labels.Count + plan.Skipped.Count);
        Assert.Contains(plan.Skipped, s => s.Reason.Contains(nameof(VertexClassification.CollinearJunction)));
        Assert.Contains(plan.Skipped, s => s.Reason.Contains(nameof(VertexClassification.CurveAdjacent)));
    }

    [Fact]
    public void Corner_between_two_exterior_lines_is_skipped()
    {
        // Edges: 0 = (0,0)->(40,0), 1 = (40,0)->(0,30), 2 = (0,30)->(0,0).
        var loop = Loops.Polygon((0, 0), (40, 0), (0, 30));
        var exterior = new HashSet<LoopElementRef> { new(0, 0), new(0, 1) };

        AngleLabelPlan plan = AngleLabelPlanner.Plan(ParcelCornerAnalyzer.Analyze(new[] { loop }), null, exterior);

        Assert.Contains(plan.Skipped, s => s.Vertex == new Point2(40, 0) && s.Reason.Contains("exterior"));
        Assert.Single(plan.Labels, l => l.Vertex == new Point2(0, 30)); // only one of its lines is exterior
    }

    [Fact]
    public void Square_chamfer_ends_are_skipped_when_enabled()
    {
        // Square lot with its (20,20) corner cut by a 45° chamfer.
        var loop = Loops.Polygon((0, 0), (20, 0), (20, 15), (15, 20), (0, 20));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { SkipSquareChamfers = true });

        Assert.Empty(plan.Labels);
        Assert.Equal(2, plan.Skipped.Count(s => s.Reason.StartsWith("Square chamfer")));
    }

    [Fact]
    public void Square_chamfer_ends_are_labeled_by_default()
    {
        var loop = Loops.Polygon((0, 0), (20, 0), (20, 15), (15, 20), (0, 20));

        AngleLabelPlan plan = Plan(loop);

        Assert.Equal(2, plan.Labels.Count);
        Assert.All(plan.Labels, l => Assert.Equal("135°00'00\"", l.Text));
    }

    [Fact]
    public void Lone_135_degree_corner_is_still_labeled()
    {
        // (10,0) is exactly 135°; its neighbors are 90° and ~149°.
        var loop = Loops.Polygon((0, 0), (10, 0), (20, 10), (25, 30), (0, 30));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { SkipSquareChamfers = true });

        Assert.Contains(plan.Labels, l => l.Vertex == new Point2(10, 0) && l.Text == "135°00'00\"");
    }

    [Fact]
    public void Skewed_chamfer_is_labeled()
    {
        // The (20,20) corner is 80° (its far side heads at 190°); a chamfer across it has 130° ends.
        double far = 190 * Math.PI / 180;
        var loop = Loops.Polygon(
            (0, 0), (20, 0), (20, 15),
            (20 + 5 * Math.Cos(far), 20 + 5 * Math.Sin(far)),
            (20 + 20 * Math.Cos(far), 20 + 20 * Math.Sin(far)));

        AngleLabelPlan plan = Plan(loop, new AngleLabelOptions { SkipSquareChamfers = true });

        Assert.Equal(2, plan.Labels.Count(l => l.Text == "130°00'00\""));
        Assert.DoesNotContain(plan.Skipped, s => s.Reason.StartsWith("Square chamfer"));
    }

    [Fact]
    public void Exterior_elements_of_another_loop_do_not_match()
    {
        var loop = Loops.Polygon((0, 0), (40, 0), (0, 30));
        var exterior = new HashSet<LoopElementRef> { new(1, 0), new(1, 1) };

        AngleLabelPlan plan = AngleLabelPlanner.Plan(ParcelCornerAnalyzer.Analyze(new[] { loop }), null, exterior);

        Assert.Equal(2, plan.Labels.Count);
    }

    [Fact]
    public void Invalid_loop_vertices_are_skipped()
    {
        var broken = Loops.Polygon((0, 0), (10, 0), (10, 10), (0, 10));
        var loops = new[] { Loops.Polygon((0, 0), (40, 0), (0, 30)), broken };
        (broken[1], broken[2]) = (broken[2], broken[1]);

        AngleLabelPlan plan = AngleLabelPlanner.Plan(ParcelCornerAnalyzer.Analyze(loops));

        Assert.Empty(plan.Labels);
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("not valid"));
    }
}
