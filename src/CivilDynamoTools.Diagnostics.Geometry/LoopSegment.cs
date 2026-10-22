namespace CivilDynamoTools.Diagnostics.Geometry;

public enum SegmentKind
{
    Line,
    Curve,

    /// <summary>The source could not classify the element; no angles are computed next to it.</summary>
    Unknown,
}

/// <summary>
/// One parcel-loop element exactly as the source reported it, in source order and source
/// direction. <see cref="SourceIndex"/> is the element's index within its loop, so every
/// result can be traced back to the original COM element.
/// </summary>
public sealed record LoopSegment(int SourceIndex, Point2 Start, Point2 End, SegmentKind Kind)
{
    public double ChordLength => Start.DistanceTo(End);
}

/// <summary>
/// A loop segment after chaining: oriented so that its <see cref="Start"/> is the previous
/// segment's <see cref="End"/>. <see cref="WasReversed"/> records whether that required
/// flipping the source direction.
/// </summary>
public sealed record OrientedSegment(int SourceIndex, Point2 Start, Point2 End, SegmentKind Kind, bool WasReversed)
{
    public double ChordLength => Start.DistanceTo(End);
}
