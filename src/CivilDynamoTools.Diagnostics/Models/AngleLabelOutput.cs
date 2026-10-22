using CivilDynamoTools.Diagnostics.Annotation;
using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Models;

/// <summary>Formats one parcel's label run as a Dynamo/Watch-friendly dictionary.</summary>
internal static class AngleLabelOutput
{
    public static Dictionary<string, object> ToDictionary(
        AngleLabelWriter.WriteResult written, AngleLabelPlan plan, IReadOnlyList<LoopCornerResult> loops) => new()
    {
        ["ParcelName"] = written.ParcelName,
        ["ParcelHandle"] = written.ParcelHandle,
        ["LabelsCreated"] = written.Written.Count,
        ["PreviousLabelsDeleted"] = written.DeletedCount,
        ["Labels"] = written.Written.Select(w => new Dictionary<string, object>
        {
            ["LoopIndex"] = w.Label.LoopIndex,
            ["VertexIndex"] = w.Label.VertexIndex,
            ["Text"] = w.Label.Text,
            ["InteriorAngleDegrees"] = w.Label.InteriorAngleDegrees,
            ["VertexX"] = w.Label.Vertex.X,
            ["VertexY"] = w.Label.Vertex.Y,
            ["LabelX"] = w.Label.Position.X,
            ["LabelY"] = w.Label.Position.Y,
            ["RotationDegrees"] = w.Label.RotationDegrees,
            ["Anchor"] = w.Label.Anchor.ToString(),
            ["MTextHandle"] = w.Handle,
        }).ToList(),
        ["Skipped"] = plan.Skipped.Select(s => new Dictionary<string, object>
        {
            ["LoopIndex"] = s.LoopIndex,
            ["VertexIndex"] = s.VertexIndex,
            ["X"] = s.Vertex.X,
            ["Y"] = s.Vertex.Y,
            ["Reason"] = s.Reason,
        }).ToList(),
        ["Loops"] = loops.Select(l => new Dictionary<string, object>
        {
            ["LoopIndex"] = l.LoopIndex,
            ["Role"] = l.Role.ToString(),
            ["IsValid"] = l.IsValid,
            ["Issues"] = l.Issues.ToArray(),
        }).ToList(),
    };
}
