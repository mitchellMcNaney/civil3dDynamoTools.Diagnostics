using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Models;

/// <summary>Formats one loop's corner analysis as Dynamo/Watch-friendly dictionaries.</summary>
internal static class CornersOutput
{
    public static Dictionary<string, object> ToDictionary(
        IList<ParcelSegmentElementData> elements, LoopCornerResult result, CornerOptions options)
    {
        OrientedLoop? chained = result.Order.ChainedLoop;

        return new Dictionary<string, object>
        {
            ["ParcelName"] = elements.Count > 0 ? elements[0].ParcelName : string.Empty,
            ["LoopIndex"] = result.LoopIndex,
            ["Role"] = result.Role.ToString(),
            ["IsValid"] = result.IsValid,
            ["Issues"] = result.Issues.ToArray(),
            ["ElementCount"] = elements.Count,
            ["VertexCount"] = result.Vertices.Count,
            ["CornerCount"] = result.Vertices.Count(v => v.IsCorner),
            ["Orientation"] = chained is null ? "Unknown"
                : chained.SignedVertexArea == 0 ? "Degenerate"
                : chained.IsCounterClockwise ? "CounterClockwise" : "Clockwise",
            ["SignedVertexArea"] = chained?.SignedVertexArea ?? 0.0,
            ["CollinearToleranceDegrees"] = options.CollinearToleranceDegrees,
            ["LinearTolerance"] = options.LinearTolerance,
            ["Vertices"] = result.Vertices.Select(v => VertexRow(elements, result.LoopIndex, v)).ToList(),
        };
    }

    // Angle fields are null (not empty string) when not computed, so downstream math and
    // filtering nodes see "no value" rather than a string.
    private static Dictionary<string, object?> VertexRow(IList<ParcelSegmentElementData> elements, int loopIndex, LoopVertex v)
    {
        ParcelSegmentElementData incoming = elements[v.IncomingSourceIndex];
        ParcelSegmentElementData outgoing = elements[v.OutgoingSourceIndex];

        return new Dictionary<string, object?>
        {
            ["LoopIndex"] = loopIndex,
            ["VertexIndex"] = v.VertexIndex,
            ["X"] = v.Location.X,
            ["Y"] = v.Location.Y,
            ["Classification"] = v.Classification.ToString(),
            ["IsCorner"] = v.IsCorner,
            ["InteriorAngleDegrees"] = v.InteriorAngleDegrees,
            ["DeflectionDegrees"] = v.DeflectionDegrees,
            ["IsReflex"] = v.IsReflex,
            ["BisectorX"] = v.InteriorBisector?.X,
            ["BisectorY"] = v.InteriorBisector?.Y,
            ["IncomingElementIndex"] = v.IncomingSourceIndex,
            ["OutgoingElementIndex"] = v.OutgoingSourceIndex,
            ["IncomingKind"] = v.IncomingKind.ToString(),
            ["OutgoingKind"] = v.OutgoingKind.ToString(),
            ["IncomingParentSegmentIdentity"] = incoming.ParentSegmentIdentity,
            ["OutgoingParentSegmentIdentity"] = outgoing.ParentSegmentIdentity,
            ["SameParentSegment"] = incoming.ParentSegmentIdentity == outgoing.ParentSegmentIdentity,
            ["IncomingLength"] = incoming.Length,
            ["OutgoingLength"] = outgoing.Length,
            ["IncomingChordLength"] = v.IncomingChordLength,
            ["OutgoingChordLength"] = v.OutgoingChordLength,
            ["Warnings"] = v.Warnings.ToArray(),
        };
    }
}
