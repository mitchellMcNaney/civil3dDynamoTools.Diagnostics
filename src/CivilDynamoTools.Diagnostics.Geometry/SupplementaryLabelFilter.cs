namespace CivilDynamoTools.Diagnostics.Geometry;

/// <summary>
/// Drops redundant labels across parcels: where a lot line meets a straight line (a T), the two
/// corners on either side of the lot line share a vertex and sum to 180°, so one can be derived
/// from the other. The larger angle keeps its label; the smaller one is moved to Skipped with
/// a reason naming the angle it is derivable from.
/// </summary>
public static class SupplementaryLabelFilter
{
    /// <param name="parcels">Each parcel's name and plan; returned plans are in the same order.</param>
    /// <param name="linearTolerance">Maximum distance between two vertices treated as the same point.</param>
    /// <param name="angleToleranceDegrees">Maximum difference from 180° for a pair to count as supplementary.</param>
    public static IReadOnlyList<AngleLabelPlan> Apply(
        IReadOnlyList<(string ParcelName, AngleLabelPlan Plan)> parcels, double linearTolerance, double angleToleranceDegrees)
    {
        // Largest angle first, so in each supplementary pair the larger one is kept.
        var candidates = parcels
            .SelectMany((p, parcelIndex) => p.Plan.Labels.Select((label, labelIndex) => (parcelIndex, labelIndex, label)))
            .OrderByDescending(c => c.label.InteriorAngleDegrees)
            .ThenBy(c => c.parcelIndex)
            .ThenBy(c => c.labelIndex)
            .ToList();

        var kept = new List<(int ParcelIndex, PlannedAngleLabel Label)>();
        var dropped = new Dictionary<(int ParcelIndex, int LabelIndex), string>();

        foreach (var (parcelIndex, labelIndex, label) in candidates)
        {
            var partner = kept.FirstOrDefault(k =>
                k.Label.Vertex.DistanceTo(label.Vertex) <= linearTolerance
                && Math.Abs(k.Label.InteriorAngleDegrees + label.InteriorAngleDegrees - 180.0) <= angleToleranceDegrees);

            if (partner.Label is null)
                kept.Add((parcelIndex, label));
            else
                dropped[(parcelIndex, labelIndex)] =
                    $"Supplementary to {partner.Label.Text} labeled in parcel '{parcels[partner.ParcelIndex].ParcelName}' at this vertex.";
        }

        return parcels.Select((p, parcelIndex) =>
        {
            var labels = new List<PlannedAngleLabel>();
            var skipped = new List<SkippedVertex>(p.Plan.Skipped);
            for (int i = 0; i < p.Plan.Labels.Count; i++)
            {
                PlannedAngleLabel label = p.Plan.Labels[i];
                if (dropped.TryGetValue((parcelIndex, i), out string? reason))
                    skipped.Add(new SkippedVertex(label.LoopIndex, label.VertexIndex, label.Vertex, reason));
                else
                    labels.Add(label);
            }
            return new AngleLabelPlan(labels, skipped);
        }).ToList();
    }
}
