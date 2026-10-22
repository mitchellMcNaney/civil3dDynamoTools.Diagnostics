namespace CivilDynamoTools.Diagnostics.Geometry;

/// <summary>
/// A closed, end-to-start chained loop. Vertex i is the start of segment i (and the end of
/// segment i-1, wrapping around).
///
/// Orientation comes from the shoelace area of the vertex polygon. Curve bulges are not
/// included, so for loops whose shape depends mostly on arcs (e.g. two arcs forming a lens)
/// the area can be near zero and the orientation unreliable; <see cref="HasReliableOrientation"/>
/// reports that case instead of guessing.
/// </summary>
public sealed class OrientedLoop
{
    public OrientedLoop(IReadOnlyList<OrientedSegment> segments)
    {
        if (segments.Count == 0)
            throw new ArgumentException("A loop needs at least one segment.", nameof(segments));

        Segments = segments;
        SignedVertexArea = ComputeSignedArea(segments);
    }

    public IReadOnlyList<OrientedSegment> Segments { get; }

    /// <summary>Shoelace area of the vertex polygon; positive = counter-clockwise.</summary>
    public double SignedVertexArea { get; }

    public bool IsCounterClockwise => SignedVertexArea > 0;

    /// <summary>
    /// True when the vertex-polygon area clearly exceeds a strip of width
    /// <paramref name="linearTolerance"/> around the perimeter, i.e. the sign is meaningful.
    /// </summary>
    public bool HasReliableOrientation(double linearTolerance)
        => Math.Abs(SignedVertexArea) > linearTolerance * Segments.Sum(s => s.ChordLength);

    private static double ComputeSignedArea(IReadOnlyList<OrientedSegment> segments)
    {
        double twiceArea = 0;
        foreach (OrientedSegment s in segments)
            twiceArea += s.Start.X * s.End.Y - s.End.X * s.Start.Y;
        return twiceArea / 2;
    }
}
