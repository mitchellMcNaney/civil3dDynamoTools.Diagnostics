using CivilDynamoTools.Diagnostics.Interop;

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
}
