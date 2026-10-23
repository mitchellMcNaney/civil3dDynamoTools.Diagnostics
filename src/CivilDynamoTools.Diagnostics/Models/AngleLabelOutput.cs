using CivilDynamoTools.Diagnostics.Annotation;
using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Models;

/// <summary>Formats label runs as Dynamo/Watch-friendly dictionaries.</summary>
internal static class AngleLabelOutput
{
    /// <summary>Site totals, the parcels treated as ROW, then each parcel's run (as <see cref="ToDictionary"/>, plus style and ROW match).</summary>
    public static Dictionary<string, object> SiteToDictionary(
        string siteName,
        IReadOnlyList<(AngleLabelWriter.WriteResult Written, AngleLabelPlan Plan, IReadOnlyList<LoopCornerResult> Loops, string StyleName, string? RowMatch)> parcels) => new()
    {
        ["SiteName"] = siteName,
        ["ParcelCount"] = parcels.Count,
        ["LabelsCreated"] = parcels.Sum(p => p.Written.Written.Count),
        ["PreviousLabelsDeleted"] = parcels.Sum(p => p.Written.DeletedCount),
        ["RowParcels"] = parcels.Where(p => p.RowMatch is not null).Select(p => new Dictionary<string, object>
        {
            ["ParcelName"] = p.Written.ParcelName,
            ["Reason"] = p.RowMatch!,
        }).ToList(),
        ["ParcelsWithInvalidLoops"] = parcels.Where(p => p.Loops.Any(l => !l.IsValid)).Select(p => p.Written.ParcelName).ToList(),
        ["Parcels"] = parcels.Select(p =>
        {
            Dictionary<string, object> run = ToDictionary(p.Written, p.Plan, p.Loops);
            run["StyleName"] = p.StyleName;
            run["IsRow"] = p.RowMatch is not null;
            return run;
        }).ToList(),
    };

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
