using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Models;

/// <summary>Formats one loop's segment-order analysis as Dynamo/Watch-friendly dictionaries.</summary>
internal static class SegmentOrderOutput
{
    public static Dictionary<string, object> ToDictionary(
        IList<ParcelSegmentElementData> elements, SegmentOrderReport report, double linearTolerance)
    {
        OrientedLoop? chained = report.ChainedLoop;
        var rows = new List<Dictionary<string, object>>(elements.Count);
        for (int i = 0; i < elements.Count; i++)
        {
            ParcelSegmentElementData data = elements[i];
            JointReport joint = report.Joints[i];
            double dx = data.EndX - data.StartX, dy = data.EndY - data.StartY;

            rows.Add(new Dictionary<string, object>
            {
                ["ElementIndex"] = data.ElementIndex,
                ["ElementTypeName"] = data.ElementTypeName,
                ["IsLine"] = data.IsLine,
                ["IsCurve"] = data.IsCurve,
                ["StartX"] = data.StartX,
                ["StartY"] = data.StartY,
                ["EndX"] = data.EndX,
                ["EndY"] = data.EndY,
                ["Length"] = data.Length,
                ["ChordLength"] = Math.Sqrt(dx * dx + dy * dy),
                ["ParentSegmentIdentity"] = data.ParentSegmentIdentity,
                ["JointFromPrevious"] = joint.Status.ToString(),
                ["PreviousElementIndex"] = joint.PreviousSourceIndex,
                ["GapFromPreviousEnd"] = joint.GapAsGiven,
                ["ClosestEndpointDistanceToPrevious"] = joint.ClosestEndpointDistance,
                // Empty string rather than null when unchainable: Watch nodes render null poorly.
                ["ReversedWhenChained"] = chained is null ? string.Empty : chained.Segments[i].WasReversed,
                ["Warnings"] = data.Warnings.ToArray(),
            });
        }

        return new Dictionary<string, object>
        {
            ["ParcelName"] = elements.Count > 0 ? elements[0].ParcelName : string.Empty,
            ["LoopIndex"] = elements.Count > 0 ? elements[0].LoopIndex : -1,
            ["ElementCount"] = elements.Count,
            ["LinearTolerance"] = linearTolerance,
            ["AllJointsConnectedAsGiven"] = report.AllJointsConnectedAsGiven,
            ["IsChainable"] = report.IsChainable,
            ["ChainFailure"] = report.ChainFailure ?? string.Empty,
            ["ReversedSegmentCount"] = report.ReversedSegmentCount,
            ["Orientation"] = chained is null ? "Unknown"
                : chained.SignedVertexArea == 0 ? "Degenerate"
                : chained.IsCounterClockwise ? "CounterClockwise" : "Clockwise",
            ["SignedVertexArea"] = chained?.SignedVertexArea ?? 0.0,
            ["Elements"] = rows,
        };
    }
}
