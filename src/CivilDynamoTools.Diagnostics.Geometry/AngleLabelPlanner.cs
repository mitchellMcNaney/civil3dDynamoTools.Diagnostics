namespace CivilDynamoTools.Diagnostics.Geometry;

public sealed record AngleLabelOptions
{
    /// <summary>Text height in drawing units; used to keep the text clear of the corner's lines.</summary>
    public double TextHeight { get; init; } = 2.0;

    /// <summary>
    /// Minimum distance from the vertex to the near end of the text, along the interior bisector,
    /// in drawing units. Increased automatically for narrow angles so the text clears both lines.
    /// </summary>
    public double Offset { get; init; } = 1.0;

    /// <summary>Skip corners whose label text would read exactly 90°00'00" or 270°00'00".</summary>
    public bool SkipRightAngles { get; init; } = true;
}

/// <summary>Which end of the text sits at <see cref="PlannedAngleLabel.Position"/> (vertically centered).</summary>
public enum LabelAnchor
{
    /// <summary>Text starts at the position and reads away from the vertex.</summary>
    MiddleLeft,

    /// <summary>Text ends at the position and reads toward the vertex (used where reading away would be upside-down).</summary>
    MiddleRight,
}

/// <summary>
/// One label to create: DMS text laid along the interior bisector, rotated by
/// <see cref="RotationDegrees"/> (always in (-90, 90], so never upside-down), with its
/// vertex-side end at <see cref="Position"/>.
/// </summary>
public sealed record PlannedAngleLabel(
    int LoopIndex,
    int VertexIndex,
    Point2 Vertex,
    Point2 Position,
    double RotationDegrees,
    LabelAnchor Anchor,
    double InteriorAngleDegrees,
    string Text);

/// <summary>A vertex that gets no label, and why.</summary>
public sealed record SkippedVertex(int LoopIndex, int VertexIndex, Point2 Vertex, string Reason);

public sealed record AngleLabelPlan(IReadOnlyList<PlannedAngleLabel> Labels, IReadOnlyList<SkippedVertex> Skipped);

/// <summary>
/// Decides which vertices of a parcel get an angle label, the label text, and where it goes.
/// Every vertex ends up in exactly one of <see cref="AngleLabelPlan.Labels"/> or
/// <see cref="AngleLabelPlan.Skipped"/>; nothing is dropped silently.
/// </summary>
public static class AngleLabelPlanner
{
    /// <summary>Half the text height plus a quarter-height margin, kept between the text and each line.</summary>
    private const double ClearanceInTextHeights = 0.75;

    private static readonly string[] RightAngleTexts = { AngleFormatter.ToDms(90), AngleFormatter.ToDms(270) };

    public static AngleLabelPlan Plan(IReadOnlyList<LoopCornerResult> loops, AngleLabelOptions? options = null)
    {
        options ??= new AngleLabelOptions();
        if (options.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Offset, "Offset must be non-negative.");
        if (options.TextHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.TextHeight, "Text height must be positive.");

        var labels = new List<PlannedAngleLabel>();
        var skipped = new List<SkippedVertex>();

        foreach (LoopCornerResult loop in loops)
        {
            foreach (LoopVertex v in loop.Vertices)
            {
                string? reason = SkipReason(loop, v, options, out string? text);
                if (reason is not null)
                {
                    skipped.Add(new SkippedVertex(loop.LoopIndex, v.VertexIndex, v.Location, reason));
                    continue;
                }

                labels.Add(PlaceLabel(loop.LoopIndex, v, text!, options));
            }
        }

        return new AngleLabelPlan(labels, skipped);
    }

    private static PlannedAngleLabel PlaceLabel(int loopIndex, LoopVertex v, string text, AngleLabelOptions options)
    {
        double angle = v.InteriorAngleDegrees!.Value;
        Point2 b = v.InteriorBisector!.Value;

        // A point on the bisector at distance d is d*sin(angle/2) from each line of an angle
        // under 180; past 180 the nearest point of either line is the vertex itself.
        double halfAngleSine = angle < 180.0 ? Math.Sin(angle / 2.0 * Math.PI / 180.0) : 1.0;
        double gap = Math.Max(options.Offset, ClearanceInTextHeights * options.TextHeight / halfAngleSine);
        var position = new Point2(v.Location.X + b.X * gap, v.Location.Y + b.Y * gap);

        double heading = Math.Atan2(b.Y, b.X) * 180.0 / Math.PI;
        bool readsAway = heading > -90.0 && heading <= 90.0;
        double rotation = readsAway ? heading : heading > 0 ? heading - 180.0 : heading + 180.0;

        return new PlannedAngleLabel(
            loopIndex, v.VertexIndex, v.Location, position, rotation,
            readsAway ? LabelAnchor.MiddleLeft : LabelAnchor.MiddleRight,
            angle, text);
    }

    private static string? SkipReason(LoopCornerResult loop, LoopVertex v, AngleLabelOptions options, out string? text)
    {
        text = null;
        if (!loop.IsValid)
            return "Loop is not valid; see the loop's Issues.";
        if (!v.IsCorner)
            return $"Not a corner ({v.Classification}).";
        if (v.InteriorAngleDegrees is not double angle || v.InteriorBisector is null)
            return "No interior angle was computed.";

        text = AngleFormatter.ToDms(angle);
        if (options.SkipRightAngles && RightAngleTexts.Contains(text))
            return $"Right angle ({text}).";

        return null;
    }
}
