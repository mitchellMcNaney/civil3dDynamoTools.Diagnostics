using CivilDynamoTools.Diagnostics.Geometry;
using CivilDynamoTools.Diagnostics.Models;

namespace CivilDynamoTools.Diagnostics.Topology;

/// <summary>Maps COM-read element data onto the Autodesk-free geometry layer's input type.</summary>
internal static class LoopSegmentMapper
{
    public static LoopSegment ToLoopSegment(ParcelSegmentElementData data) => new(
        data.ElementIndex,
        new Point2(data.StartX, data.StartY),
        new Point2(data.EndX, data.EndY),
        ToKind(data));

    // A curve check wins over a line check: an unexpected type name matching both is not trusted as a line.
    private static SegmentKind ToKind(ParcelSegmentElementData data)
        => data.IsCurve ? SegmentKind.Curve
         : data.IsLine ? SegmentKind.Line
         : SegmentKind.Unknown;
}
