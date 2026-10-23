using CivilDynamoTools.Diagnostics.Annotation;
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
        var (loops, results, options) = AnalyzeCorners(objectId, collinearToleranceDegrees, linearTolerance);

        return results
            .Select(result => CornersOutput.ToDictionary(loops[result.LoopIndex], result, options))
            .ToList();
    }

    /// <summary>
    /// Creates DMS angle labels (MText) at the parcel's line-line corners, laid along each
    /// corner's interior bisector starting just inside the vertex (flipped where needed so
    /// text is never upside-down). Collinear junctions, curve-adjacent
    /// vertices, vertices of invalid loops, and (optionally) right angles are skipped, each
    /// with a reason in the output.
    ///
    /// Re-running replaces this parcel's labels: MText previously created by this node for
    /// the same parcel (identified by XData) is erased first. Other MText is never touched.
    /// </summary>
    /// <param name="parcel">A Civil 3D parcel; same accepted shapes as SegmentReferenceCounts.</param>
    /// <param name="textHeight">Plotted (paper) text height. Labels are annotative MText at the drawing's current annotation scale, so the model height is this divided by that scale.</param>
    /// <param name="offset">Minimum gap from the vertex to the near end of the text, in drawing units; widened automatically for narrow angles so the text clears both lines.</param>
    /// <param name="layer">Layer for the labels (created if missing). Empty = current layer.</param>
    /// <param name="textStyle">Name of an existing text style for the labels. Empty = current text style. An unknown name fails, listing the available styles.</param>
    /// <param name="skipRightAngles">Skip corners whose label would read 90°00'00" or 270°00'00".</param>
    /// <param name="collinearToleranceDegrees">As for Corners.</param>
    /// <param name="linearTolerance">As for Corners.</param>
    public static Dictionary<string, object> LabelAngles(
        object parcel,
        double textHeight = 0.08,
        double offset = 1.0,
        string layer = "",
        string textStyle = "",
        bool skipRightAngles = true,
        double collinearToleranceDegrees = 1.0 / 3600.0,
        double linearTolerance = 1e-6)
    {
        var objectId = ParcelUnwrapper.ToParcelObjectId(parcel);
        var (_, results, _) = AnalyzeCorners(objectId, collinearToleranceDegrees, linearTolerance);
        var (scaleName, modelTextHeight) = ModelTextHeight(textHeight);

        AngleLabelPlan plan = AngleLabelPlanner.Plan(results, new AngleLabelOptions
        {
            TextHeight = modelTextHeight,
            Offset = offset,
            SkipRightAngles = skipRightAngles,
        });

        var written = new AngleLabelWriter().Write(objectId, plan.Labels, modelTextHeight, layer, textStyle);
        Dictionary<string, object> output = AngleLabelOutput.ToDictionary(written, plan, results);
        output["AnnotationScale"] = scaleName;
        output["ModelTextHeight"] = modelTextHeight;
        return output;
    }

    /// <summary>
    /// LabelAngles for every parcel in a site, in one run. On top of LabelAngles' rules:
    /// ROW parcels (matched by name or style name) get no labels, and corners whose two lines
    /// are both on the site's exterior boundary are skipped, since the boundary's bearings
    /// and distances already define them. An element is exterior when only one parcel in
    /// the site references it (see SegmentReferenceCounts). Where a lot line meets a straight
    /// line, the two corners at that vertex sum to 180°, so only the larger is labeled.
    ///
    /// Re-running replaces labels for every parcel in the site, including erasing any old
    /// labels on parcels that now get none (e.g. ROW parcels labeled by an earlier run).
    /// </summary>
    /// <param name="siteOrParcel">A Civil 3D site, or any parcel in it.</param>
    /// <param name="textHeight">As for LabelAngles.</param>
    /// <param name="offset">As for LabelAngles.</param>
    /// <param name="layer">As for LabelAngles.</param>
    /// <param name="textStyle">As for LabelAngles.</param>
    /// <param name="skipRightAngles">As for LabelAngles.</param>
    /// <param name="skipExteriorCorners">Skip corners where both lines are on the site's exterior boundary.</param>
    /// <param name="skipSupplementaryCorners">Where two parcels' corners at the same vertex sum to 180° (within collinearToleranceDegrees), label only the larger.</param>
    /// <param name="skipSquareChamfers">Skip both ends of a chamfer across a square corner (two neighboring corners that both read 135°00'00").</param>
    /// <param name="rowTerms">';'-separated words that mark a parcel as ROW when found (whole word, any case) in its name or style name. Empty = no ROW parcels.</param>
    /// <param name="collinearToleranceDegrees">As for Corners.</param>
    /// <param name="linearTolerance">As for Corners.</param>
    public static Dictionary<string, object> LabelSiteAngles(
        object siteOrParcel,
        double textHeight = 0.08,
        double offset = 1.0,
        string layer = "",
        string textStyle = "",
        bool skipRightAngles = true,
        bool skipExteriorCorners = true,
        bool skipSupplementaryCorners = true,
        bool skipSquareChamfers = true,
        string rowTerms = RowParcelMatcher.DefaultTerms,
        double collinearToleranceDegrees = 1.0 / 3600.0,
        double linearTolerance = 1e-6)
    {
        var objectId = ParcelUnwrapper.ToObjectId(siteOrParcel, "site or parcel");
        ParcelTopologyReader.SiteTopology site = new ParcelTopologyReader().GetSiteTopology(objectId);

        IReadOnlyList<string> terms = RowParcelMatcher.ParseTerms(rowTerms);
        var (scaleName, modelTextHeight) = ModelTextHeight(textHeight);
        var labelOptions = new AngleLabelOptions
        {
            TextHeight = modelTextHeight,
            Offset = offset,
            SkipRightAngles = skipRightAngles,
            SkipSquareChamfers = skipSquareChamfers,
        };

        var runs = site.Parcels.Select(p =>
        {
            string? rowMatch = RowParcelMatcher.Match(p.Name, p.StyleName, terms);
            var (_, results, _) = AnalyzeCorners(p.Loops, collinearToleranceDegrees, linearTolerance);

            AngleLabelPlan plan = rowMatch is not null
                ? new AngleLabelPlan(
                    Array.Empty<PlannedAngleLabel>(),
                    results.SelectMany(l => l.Vertices.Select(v =>
                        new SkippedVertex(l.LoopIndex, v.VertexIndex, v.Location, $"ROW parcel: {rowMatch}"))).ToList())
                : AngleLabelPlanner.Plan(results, labelOptions, skipExteriorCorners ? ExteriorElements(p.Loops) : null);

            return (Parcel: p, RowMatch: rowMatch, Results: results, Plan: plan);
        }).ToList();

        if (skipSupplementaryCorners)
        {
            IReadOnlyList<AngleLabelPlan> filtered = SupplementaryLabelFilter.Apply(
                runs.Select(r => (r.Parcel.Name, r.Plan)).ToList(), linearTolerance, collinearToleranceDegrees);
            runs = runs.Select((r, i) => r with { Plan = filtered[i] }).ToList();
        }

        IReadOnlyList<AngleLabelWriter.WriteResult> written = new AngleLabelWriter().WriteAll(
            runs.Select(r => (r.Parcel.ParcelId, r.Plan.Labels)).ToList(), modelTextHeight, layer, textStyle);

        Dictionary<string, object> output = AngleLabelOutput.SiteToDictionary(
            site.SiteName,
            runs.Select((r, i) => (written[i], r.Plan, r.Results, r.Parcel.StyleName, r.RowMatch)).ToList());
        output["AnnotationScale"] = scaleName;
        output["ModelTextHeight"] = modelTextHeight;
        return output;
    }

    /// <summary>Plotted text height -> model-space height at the drawing's current annotation scale.</summary>
    private static (string ScaleName, double ModelHeight) ModelTextHeight(double plottedHeight)
    {
        if (plottedHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(plottedHeight), plottedHeight, "Text height must be positive.");
        var (name, scale) = AngleLabelWriter.CurrentAnnotationScale();
        return (name, plottedHeight / scale);
    }

    /// <summary>Elements referenced by only one parcel in the site: the site's exterior boundary.</summary>
    private static HashSet<LoopElementRef> ExteriorElements(IList<IList<ParcelSegmentElementData>> loops)
        => loops.SelectMany(loop => loop)
            .Where(e => e.ReferenceCount == 1)
            .Select(e => new LoopElementRef(e.LoopIndex, e.ElementIndex))
            .ToHashSet();

    private static (IList<IList<ParcelSegmentElementData>> Loops, IReadOnlyList<LoopCornerResult> Results, CornerOptions Options)
        AnalyzeCorners(Autodesk.AutoCAD.DatabaseServices.ObjectId objectId, double collinearToleranceDegrees, double linearTolerance)
        => AnalyzeCorners(new ParcelTopologyReader().GetParcelLoops(objectId), collinearToleranceDegrees, linearTolerance);

    private static (IList<IList<ParcelSegmentElementData>> Loops, IReadOnlyList<LoopCornerResult> Results, CornerOptions Options)
        AnalyzeCorners(IList<IList<ParcelSegmentElementData>> loops, double collinearToleranceDegrees, double linearTolerance)
    {
        var options = new CornerOptions
        {
            CollinearToleranceDegrees = collinearToleranceDegrees,
            LinearTolerance = linearTolerance,
        };
        IReadOnlyList<LoopCornerResult> results = ParcelCornerAnalyzer.Analyze(
            loops.Select(elements => elements.Select(LoopSegmentMapper.ToLoopSegment).ToList()).ToList(),
            options);

        return (loops, results, options);
    }
}
