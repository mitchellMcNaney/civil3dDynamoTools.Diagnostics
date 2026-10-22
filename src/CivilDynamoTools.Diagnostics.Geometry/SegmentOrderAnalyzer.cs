namespace CivilDynamoTools.Diagnostics.Geometry;

/// <summary>How the end of the previous segment meets the current segment, in source order.</summary>
public enum JointStatus
{
    /// <summary>previous.End == current.Start as given.</summary>
    Connected,
    ConnectedIfCurrentReversed,
    ConnectedIfPreviousReversed,
    ConnectedIfBothReversed,

    /// <summary>No endpoint pairing is within tolerance: the source order is not a chain here.</summary>
    Disconnected,
}

/// <summary>The joint between source segment i-1 and i (joint 0 is the closing joint last -> first).</summary>
public sealed record JointReport(
    int PreviousSourceIndex,
    int CurrentSourceIndex,
    double GapAsGiven,
    double ClosestEndpointDistance,
    JointStatus Status);

public sealed class SegmentOrderReport
{
    internal SegmentOrderReport(IReadOnlyList<JointReport> joints, OrientedLoop? chainedLoop, string? chainFailure)
    {
        Joints = joints;
        ChainedLoop = chainedLoop;
        ChainFailure = chainFailure;
    }

    /// <summary>One joint per segment, in source order.</summary>
    public IReadOnlyList<JointReport> Joints { get; }

    public bool AllJointsConnectedAsGiven => Joints.All(j => j.Status == JointStatus.Connected);

    /// <summary>
    /// The loop chained in source order, flipping individual segments where needed. Null when
    /// the source order cannot be chained without reordering (see <see cref="ChainFailure"/>).
    /// Segments are never reordered: if Civil 3D's order turns out not to be traversal order,
    /// that should be visible, not silently repaired.
    /// </summary>
    public OrientedLoop? ChainedLoop { get; }

    public string? ChainFailure { get; }

    public bool IsChainable => ChainedLoop is not null;

    public int ReversedSegmentCount => ChainedLoop?.Segments.Count(s => s.WasReversed) ?? 0;
}

public static class SegmentOrderAnalyzer
{
    /// <summary>Endpoint-match tolerance used when none is given; matches SegmentElementKey's 6-decimal rounding.</summary>
    public const double DefaultLinearTolerance = 1e-6;

    public static SegmentOrderReport Analyze(IReadOnlyList<LoopSegment> segments, double linearTolerance = DefaultLinearTolerance)
    {
        if (segments.Count == 0)
            throw new ArgumentException("A loop needs at least one segment.", nameof(segments));
        if (linearTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(linearTolerance), "Tolerance must be non-negative.");

        var joints = new List<JointReport>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            LoopSegment previous = segments[(i - 1 + segments.Count) % segments.Count];
            joints.Add(AnalyzeJoint(previous, segments[i], segments.Count == 1, linearTolerance));
        }

        OrientedLoop? loop = TryChain(segments, linearTolerance, out string? failure);
        return new SegmentOrderReport(joints, loop, failure);
    }

    private static JointReport AnalyzeJoint(LoopSegment previous, LoopSegment current, bool isSelfJoint, double tol)
    {
        double endToStart = previous.End.DistanceTo(current.Start);
        double endToEnd = previous.End.DistanceTo(current.End);
        double startToStart = previous.Start.DistanceTo(current.Start);
        double startToEnd = previous.Start.DistanceTo(current.End);

        JointStatus status;
        double closest;
        if (isSelfJoint)
        {
            // A single closed element (e.g. a full-circle curve) only joins itself end -> start.
            closest = endToStart;
            status = endToStart <= tol ? JointStatus.Connected : JointStatus.Disconnected;
        }
        else
        {
            closest = Math.Min(Math.Min(endToStart, endToEnd), Math.Min(startToStart, startToEnd));
            status = endToStart <= tol ? JointStatus.Connected
                : endToEnd <= tol ? JointStatus.ConnectedIfCurrentReversed
                : startToStart <= tol ? JointStatus.ConnectedIfPreviousReversed
                : startToEnd <= tol ? JointStatus.ConnectedIfBothReversed
                : JointStatus.Disconnected;
        }

        return new JointReport(previous.SourceIndex, current.SourceIndex, endToStart, closest, status);
    }

    private static OrientedLoop? TryChain(IReadOnlyList<LoopSegment> segments, double tol, out string? failure)
    {
        // Prefer keeping the first segment's source direction; only flip it if that cannot chain.
        string? firstFailure = null;
        foreach (bool flipFirst in new[] { false, true })
        {
            List<OrientedSegment>? chained = TryChainFrom(segments, flipFirst, tol, out string? attemptFailure);
            if (chained is not null)
            {
                failure = null;
                return new OrientedLoop(chained);
            }
            firstFailure ??= attemptFailure;
        }

        failure = firstFailure;
        return null;
    }

    private static List<OrientedSegment>? TryChainFrom(IReadOnlyList<LoopSegment> segments, bool flipFirst, double tol, out string? failure)
    {
        var chained = new List<OrientedSegment>(segments.Count) { Orient(segments[0], flipFirst) };

        for (int i = 1; i < segments.Count; i++)
        {
            Point2 tail = chained[^1].End;
            LoopSegment next = segments[i];

            if (tail.DistanceTo(next.Start) <= tol)
                chained.Add(Orient(next, reverse: false));
            else if (tail.DistanceTo(next.End) <= tol)
                chained.Add(Orient(next, reverse: true));
            else
            {
                failure = $"Segment {next.SourceIndex} does not touch the end of segment {chained[^1].SourceIndex} " +
                          $"{tail} in either direction (nearest endpoint " +
                          $"{Math.Min(tail.DistanceTo(next.Start), tail.DistanceTo(next.End)):G6} away).";
                return null;
            }
        }

        double closingGap = chained[^1].End.DistanceTo(chained[0].Start);
        if (closingGap > tol)
        {
            failure = $"Loop does not close: end of segment {chained[^1].SourceIndex} is {closingGap:G6} " +
                      $"from the start of segment {chained[0].SourceIndex}.";
            return null;
        }

        failure = null;
        return chained;
    }

    private static OrientedSegment Orient(LoopSegment s, bool reverse) => reverse
        ? new OrientedSegment(s.SourceIndex, s.End, s.Start, s.Kind, WasReversed: true)
        : new OrientedSegment(s.SourceIndex, s.Start, s.End, s.Kind, WasReversed: false);
}
