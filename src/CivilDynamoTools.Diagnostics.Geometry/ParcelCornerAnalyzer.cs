namespace CivilDynamoTools.Diagnostics.Geometry;

public enum LoopRole
{
    /// <summary>The parcel's outer boundary: interior angles are measured inside the loop.</summary>
    Outer,

    /// <summary>A hole in the parcel: interior angles are measured outside the loop.</summary>
    Hole,

    /// <summary>The role could not be determined (e.g. a sibling loop failed to chain); no interior angles.</summary>
    Unknown,
}

/// <summary>Corner analysis for one loop, plus everything that makes it suspect.</summary>
public sealed record LoopCornerResult(
    int LoopIndex,
    SegmentOrderReport Order,
    LoopRole Role,
    IReadOnlyList<LoopVertex> Vertices,
    IReadOnlyList<string> Issues)
{
    /// <summary>
    /// False when the loop's angles could not be computed (unchainable loop, unknown role,
    /// unreliable orientation). Issues can be present on a valid loop; they still need review.
    /// </summary>
    public bool IsValid { get; init; } = true;
}

/// <summary>
/// Runs segment-order validation and corner analysis over every loop of one parcel.
/// Unusual loops are flagged through <see cref="LoopCornerResult.Issues"/> and
/// <see cref="LoopCornerResult.IsValid"/> rather than repaired or skipped.
/// </summary>
public static class ParcelCornerAnalyzer
{
    public static IReadOnlyList<LoopCornerResult> Analyze(IReadOnlyList<IReadOnlyList<LoopSegment>> loops, CornerOptions? options = null)
    {
        options ??= new CornerOptions();
        if (loops.Count == 0)
            throw new ArgumentException("A parcel needs at least one loop.", nameof(loops));

        var reports = loops.Select(l => SegmentOrderAnalyzer.Analyze(l, options.LinearTolerance)).ToList();
        LoopRole[] roles = AssignRoles(reports);

        var results = new List<LoopCornerResult>(loops.Count);
        for (int i = 0; i < loops.Count; i++)
            results.Add(AnalyzeLoop(i, reports[i], roles[i], loops.Count, options));
        return results;
    }

    /// <summary>
    /// One loop is the outer boundary. With several loops, the one enclosing the largest area
    /// is assumed outer and the rest holes - an inference, so it is always reported as an issue.
    /// If any loop failed to chain, the outer loop cannot be identified safely.
    /// </summary>
    private static LoopRole[] AssignRoles(IReadOnlyList<SegmentOrderReport> reports)
    {
        var roles = new LoopRole[reports.Count];
        if (reports.Any(r => !r.IsChainable))
        {
            Array.Fill(roles, LoopRole.Unknown);
            if (reports.Count == 1)
                roles[0] = LoopRole.Outer;
            return roles;
        }

        int outer = 0;
        for (int i = 1; i < reports.Count; i++)
        {
            if (Math.Abs(reports[i].ChainedLoop!.SignedVertexArea) > Math.Abs(reports[outer].ChainedLoop!.SignedVertexArea))
                outer = i;
        }

        Array.Fill(roles, LoopRole.Hole);
        roles[outer] = LoopRole.Outer;
        return roles;
    }

    private static LoopCornerResult AnalyzeLoop(int index, SegmentOrderReport order, LoopRole role, int loopCount, CornerOptions options)
    {
        var issues = new List<string>();

        if (order.ChainedLoop is not OrientedLoop loop)
        {
            issues.Add($"Loop could not be chained in source order: {order.ChainFailure}");
            return new LoopCornerResult(index, order, role, Array.Empty<LoopVertex>(), issues) { IsValid = false };
        }

        if (order.ReversedSegmentCount > 0)
            issues.Add($"{order.ReversedSegmentCount} element(s) had to be reversed to chain the loop; source directions were inconsistent.");

        if (loopCount > 1 && role != LoopRole.Unknown)
            issues.Add($"Parcel has {loopCount} loops; this loop's role ({role}) was inferred from enclosed area and is unverified.");

        if (role == LoopRole.Unknown)
        {
            issues.Add("Another loop of this parcel could not be chained, so the outer boundary is unknown; interior angles not computed.");
            IReadOnlyList<LoopVertex> unsided = CornerAnalyzer.Analyze(loop, options)
                .Select(v => v with { InteriorAngleDegrees = null })
                .ToList();
            return new LoopCornerResult(index, order, role, unsided, issues) { IsValid = false };
        }

        IReadOnlyList<LoopVertex> vertices = CornerAnalyzer.Analyze(loop, options with { InteriorIsInsideLoop = role == LoopRole.Outer });

        bool orientationKnown = loop.HasReliableOrientation(options.LinearTolerance);
        if (!orientationKnown)
            issues.Add("Loop orientation is unreliable (near-zero vertex-polygon area); interior angles not computed.");

        return new LoopCornerResult(index, order, role, vertices, issues) { IsValid = orientationKnown };
    }
}
