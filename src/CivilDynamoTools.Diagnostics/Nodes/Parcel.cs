using CivilDynamoTools.Diagnostics.Geometry;
using CivilDynamoTools.Diagnostics.Interop;
using CivilDynamoTools.Diagnostics.Models;
using CivilDynamoTools.Diagnostics.Topology;

namespace CivilDynamoTools.Diagnostics.Nodes;

/// <summary>
/// Zero-touch Dynamo node library. Public static methods on this class are picked up
/// by Dynamo as nodes named "Parcel.&lt;MethodName&gt;".
/// </summary>
public static class Parcel
{
    /// <summary>
    /// Diagnostic node. For the given Civil 3D parcel, enumerates every parcel in the
    /// same site, reads native parcel-loop topology via COM, and reports - for every
    /// segment element belonging to the input parcel - how many parcels in the site
    /// reference that same element.
    ///
    /// This is exploratory: it does not clean, merge, or filter geometry. Every
    /// element is reported as-is, including short segments, curves, and elements that
    /// could not be matched with full confidence (flagged via Warnings).
    /// </summary>
    /// <param name="parcel">
    /// A Civil 3D parcel. Accepts a native Autodesk.Civil.DatabaseServices.Parcel, an
    /// Autodesk.AutoCAD.DatabaseServices.ObjectId/DBObject, or any object exposing an
    /// ObjectId/InternalObjectId/InternalDBObject property. See ParcelUnwrapper for the
    /// exact matching rules; an unrecognized shape throws with the runtime type's
    /// public members so the exact wrapper can be added explicitly.
    /// </param>
    public static IList<Dictionary<string, object>> SegmentReferenceCounts(object parcel)
    {
        var objectId = ParcelUnwrapper.ToParcelObjectId(parcel);
        var reader = new ParcelTopologyReader();
        return reader.GetSegmentReferenceCounts(objectId)
            .Select(data => data.ToDictionary())
            .ToList();
    }

    /// <summary>
    /// Diagnostic node. Reports, for each loop of the given parcel, whether Civil 3D's
    /// ParcelLoops element order is a continuous end-to-start chain: per element, how its
    /// start meets the previous element's end (gap, and whether flipping either element
    /// would connect them), plus per loop whether it can be chained by flipping elements
    /// in place, how many flips that needs, and the resulting orientation.
    ///
    /// Elements are never reordered or dropped. A loop that only chains after reordering
    /// is reported as not chainable, so a non-traversal source order is visible.
    /// </summary>
    /// <param name="parcel">A Civil 3D parcel; same accepted shapes as SegmentReferenceCounts.</param>
    /// <param name="linearTolerance">Maximum endpoint distance treated as touching, in drawing units.</param>
    public static IList<Dictionary<string, object>> SegmentOrder(object parcel, double linearTolerance = 1e-6)
    {
        var objectId = ParcelUnwrapper.ToParcelObjectId(parcel);
        var reader = new ParcelTopologyReader();
        return reader.GetParcelLoops(objectId)
            .Select(elements => SegmentOrderOutput.ToDictionary(
                elements,
                SegmentOrderAnalyzer.Analyze(elements.Select(LoopSegmentMapper.ToLoopSegment).ToList(), linearTolerance),
                linearTolerance))
            .ToList();
    }

    /// <summary>
    /// Geometry node. For each loop of the given parcel, returns every vertex with its
    /// classification (Corner, CollinearJunction, CurveAdjacent, UnknownAdjacent,
    /// Degenerate) and, for line-line vertices, the deflection and interior angle on the
    /// parcel side. Curve-adjacent vertices are included with null angles.
    ///
    /// Each loop is validated first (see SegmentOrder). Loops that cannot be chained,
    /// whose outer/hole role is unknown, or whose orientation is unreliable are returned
    /// with IsValid = false and their Issues listed, never silently processed.
    /// </summary>
    /// <param name="parcel">A Civil 3D parcel; same accepted shapes as SegmentReferenceCounts.</param>
    /// <param name="collinearToleranceDegrees">Line-line direction changes up to this are collinear junctions, not corners. Default 1 arc-second.</param>
    /// <param name="linearTolerance">Maximum endpoint distance treated as touching, and minimum line length, in drawing units.</param>
    public static IList<Dictionary<string, object>> Corners(
        object parcel, double collinearToleranceDegrees = 1.0 / 3600.0, double linearTolerance = 1e-6)
    {
        var objectId = ParcelUnwrapper.ToParcelObjectId(parcel);
        var reader = new ParcelTopologyReader();
        IList<IList<ParcelSegmentElementData>> loops = reader.GetParcelLoops(objectId);

        var options = new CornerOptions
        {
            CollinearToleranceDegrees = collinearToleranceDegrees,
            LinearTolerance = linearTolerance,
        };
        IReadOnlyList<LoopCornerResult> results = ParcelCornerAnalyzer.Analyze(
            loops.Select(elements => elements.Select(LoopSegmentMapper.ToLoopSegment).ToList()).ToList(),
            options);

        return results
            .Select(result => CornersOutput.ToDictionary(loops[result.LoopIndex], result, options))
            .ToList();
    }
}
