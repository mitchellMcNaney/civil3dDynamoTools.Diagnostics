using System.Collections;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using CivilDynamoTools.Diagnostics.Models;
using CivilDynamoTools.Diagnostics.Topology;

namespace CivilDynamoTools.Diagnostics.Interop;

/// <summary>
/// Reads native Civil 3D parcel-loop topology through the legacy COM API
/// (Parcel.AcadObject -> AeccParcel.ParcelLoops) and counts, across every parcel in the
/// same site, how many parcels reference each unique segment element.
///
/// COM is isolated to this class: everything it returns is plain data
/// (ParcelSegmentElementData), never a COM reference.
///
/// NOT YET VERIFIED against the real Civil 3D 2026 API/COM surface:
///   - CivilDocument.GetSiteIds() / Site.GetParcelIds() member names
///   - dynamic AcadObject / ParcelLoops behavior under the .NET 8 host runtime
///   - exact COM member names on AeccParcel / AeccParcelSegmentElement / AeccParcelSegment
/// Failures here should be self-diagnosing (see the try/catch messages below) rather
/// than silently skipping data.
/// </summary>
internal sealed class ParcelTopologyReader
{
    /// <summary>One parcel of a site, with ReferenceCount (across the whole site) set on every element.</summary>
    public sealed record SiteParcel(
        ObjectId ParcelId, string Name, string Handle, string StyleName, IList<IList<ParcelSegmentElementData>> Loops);

    public sealed record SiteTopology(ObjectId SiteId, string SiteName, IReadOnlyList<SiteParcel> Parcels);

    public IList<ParcelSegmentElementData> GetSegmentReferenceCounts(ObjectId selectedParcelId)
    {
        Document? activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");

        Database db = activeDocument.Database;

        using Transaction tr = db.TransactionManager.StartTransaction();

        var selectedParcel = tr.GetObject(selectedParcelId, OpenMode.ForRead) as Parcel
            ?? throw new InvalidOperationException(
                $"ObjectId {selectedParcelId} did not resolve to an Autodesk.Civil.DatabaseServices.Parcel.");

        Site site = FindOwningSite(tr, db, selectedParcel, selectedParcelId);

        return ReadSiteParcels(tr, site)
            .Single(p => p.ParcelId == selectedParcelId)
            .Loops.SelectMany(loop => loop)
            .ToList();
    }

    /// <summary>
    /// Reads every parcel of a site. <paramref name="siteOrParcelId"/> may be the Site itself or
    /// any parcel in it.
    /// </summary>
    public SiteTopology GetSiteTopology(ObjectId siteOrParcelId)
    {
        Document? activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");

        Database db = activeDocument.Database;

        using Transaction tr = db.TransactionManager.StartTransaction();

        Site site = tr.GetObject(siteOrParcelId, OpenMode.ForRead) switch
        {
            Site s => s,
            Parcel p => FindOwningSite(tr, db, p, siteOrParcelId),
            var other => throw new InvalidOperationException(
                $"ObjectId {siteOrParcelId} resolved to {other?.GetType().FullName ?? "null"}, " +
                "not an Autodesk.Civil.DatabaseServices.Site or Parcel."),
        };

        return new SiteTopology(site.ObjectId, site.Name, ReadSiteParcels(tr, site));
    }

    /// <summary>
    /// Parcel exposes no SiteId in the managed API, so find the owning Site by asking
    /// each Site in the drawing whether its parcel list contains the parcel.
    /// </summary>
    private static Site FindOwningSite(Transaction tr, Database db, Parcel parcel, ObjectId parcelId)
    {
        CivilDocument civilDocument = CivilDocument.GetCivilDocument(db);

        Site? owner = null;
        foreach (ObjectId candidateSiteId in civilDocument.GetSiteIds())
        {
            var candidateSite = tr.GetObject(candidateSiteId, OpenMode.ForRead) as Site
                ?? throw new InvalidOperationException(
                    $"CivilDocument.GetSiteIds() returned ObjectId {candidateSiteId} that did not resolve to a Site.");

            if (!candidateSite.GetParcelIds().Contains(parcelId))
                continue;

            if (owner is not null)
            {
                throw new InvalidOperationException(
                    $"Parcel '{parcel.Name}' ({parcelId}) is listed by more than one Site " +
                    $"({owner.ObjectId} and {candidateSiteId}); cannot determine which sibling parcels to use.");
            }

            owner = candidateSite;
        }

        return owner ?? throw new InvalidOperationException(
            $"No Site in the drawing lists parcel '{parcel.Name}' ({parcelId}); " +
            "cannot enumerate sibling parcels to compute reference counts.");
    }

    /// <summary>Reads every parcel in the site and sets MatchingKey / ReferenceCount on all their elements.</summary>
    private static IReadOnlyList<SiteParcel> ReadSiteParcels(Transaction tr, Site site)
    {
        // key -> distinct parcels that reference it
        var referencingParcels = new Dictionary<SegmentElementKey, HashSet<ObjectId>>();
        var keyed = new List<(ParcelSegmentElementData Data, SegmentElementKey Key)>();
        var parcels = new List<SiteParcel>();

        foreach (ObjectId parcelId in site.GetParcelIds())
        {
            var parcel = tr.GetObject(parcelId, OpenMode.ForRead) as Parcel
                ?? throw new InvalidOperationException(
                    $"Site '{site.Name}' ({site.ObjectId}) contains ObjectId {parcelId} that did not resolve to a Parcel.");

            IList<IList<ParcelSegmentElementData>> loops = ReadLoops(parcel, parcelId);
            parcels.Add(new SiteParcel(parcelId, parcel.Name, parcel.Handle.ToString(), ReadStyleName(tr, parcel, parcelId), loops));

            foreach (ParcelSegmentElementData data in loops.SelectMany(loop => loop))
            {
                SegmentElementKey key = SegmentElementKey.Create(
                    data.ParentSegmentIdentity, data.StartX, data.StartY, data.EndX, data.EndY,
                    out bool reversed);

                data.MatchingKey = key.ToString();
                data.WasReversedRelativeToCanonical = reversed;

                if (!referencingParcels.TryGetValue(key, out HashSet<ObjectId>? parcelSet))
                {
                    parcelSet = new HashSet<ObjectId>();
                    referencingParcels[key] = parcelSet;
                }
                parcelSet.Add(parcelId);
                keyed.Add((data, key));
            }
        }

        foreach (var (data, key) in keyed)
            data.ReferenceCount = referencingParcels[key].Count;

        return parcels;
    }

    private static string ReadStyleName(Transaction tr, Parcel parcel, ObjectId parcelId)
    {
        try
        {
            return ((StyleBase)tr.GetObject(parcel.StyleId, OpenMode.ForRead)).Name;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read the style name of parcel '{parcel.Name}' ({parcelId}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads only the given parcel's loops, preserving COM loop order and element order
    /// exactly as ParcelLoops enumerates them. MatchingKey and ReferenceCount are left unset.
    /// </summary>
    public IList<IList<ParcelSegmentElementData>> GetParcelLoops(ObjectId parcelId)
    {
        Document? activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");

        using Transaction tr = activeDocument.Database.TransactionManager.StartTransaction();

        var parcel = tr.GetObject(parcelId, OpenMode.ForRead) as Parcel
            ?? throw new InvalidOperationException(
                $"ObjectId {parcelId} did not resolve to an Autodesk.Civil.DatabaseServices.Parcel.");

        return ReadLoops(parcel, parcelId);
    }

    private static IList<IList<ParcelSegmentElementData>> ReadLoops(Parcel parcel, ObjectId parcelId)
    {
        dynamic aeccParcel;
        try
        {
            aeccParcel = parcel.AcadObject;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to obtain COM AcadObject for parcel '{parcel.Name}' ({parcelId}) via " +
                $"`parcel.AcadObject`: {ex.Message}", ex);
        }

        if (aeccParcel is null)
        {
            throw new InvalidOperationException(
                $"Parcel '{parcel.Name}' ({parcelId}) returned a null COM AcadObject.");
        }

        IEnumerable loops;
        try
        {
            loops = (IEnumerable)aeccParcel.ParcelLoops;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read ParcelLoops for parcel '{parcel.Name}' ({parcelId}): {ex.Message}", ex);
        }

        var result = new List<IList<ParcelSegmentElementData>>();
        int loopIndex = 0;
        foreach (dynamic loop in loops)
        {
            IEnumerable elements;
            try
            {
                elements = (IEnumerable)loop;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Parcel '{parcel.Name}' ({parcelId}) loop {loopIndex} was not enumerable " +
                    $"as a collection of segment elements: {ex.Message}", ex);
            }

            var loopElements = new List<ParcelSegmentElementData>();
            int elementIndex = 0;
            foreach (dynamic element in elements)
            {
                loopElements.Add(ReadElement(parcel, loopIndex, elementIndex, element));
                elementIndex++;
            }

            result.Add(loopElements);
            loopIndex++;
        }

        return result;
    }

    private static ParcelSegmentElementData ReadElement(Parcel parcel, int loopIndex, int elementIndex, dynamic element)
    {
        var data = new ParcelSegmentElementData
        {
            ParcelName = parcel.Name,
            ParcelNumber = TryGetParcelNumber(parcel),
            LoopIndex = loopIndex,
            ElementIndex = elementIndex,
        };

        try
        {
            data.StartX = (double)element.StartX;
            data.StartY = (double)element.StartY;
            data.EndX = (double)element.EndX;
            data.EndY = (double)element.EndY;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read Start/End coordinates for parcel '{parcel.Name}' loop {loopIndex} " +
                $"element {elementIndex}: {ex.Message}", ex);
        }

        data.ElementTypeName = ComTypeInspector.GetComTypeName(element);
        data.IsCurve = data.ElementTypeName.Contains("Curve", StringComparison.OrdinalIgnoreCase);
        data.IsLine = data.ElementTypeName.Contains("Line", StringComparison.OrdinalIgnoreCase);
        if (!data.IsCurve && !data.IsLine)
        {
            data.Warnings.Add(
                $"Unrecognized COM element type '{data.ElementTypeName}'; could not classify as line or curve.");
        }

        try
        {
            data.Length = (double)element.Length;
        }
        catch
        {
            double dx = data.EndX - data.StartX;
            double dy = data.EndY - data.StartY;
            data.Length = Math.Sqrt(dx * dx + dy * dy);
            if (data.IsCurve)
            {
                data.Warnings.Add(
                    "COM element had no readable Length; reported length is the straight chord " +
                    "distance between endpoints, not arc length.");
            }
        }

        try
        {
            dynamic parentSegment = element.ParcelSegment;
            data.ParentSegmentIdentity = TryGetSegmentIdentity(parentSegment, out string? warning);
            if (warning is not null)
                data.Warnings.Add(warning);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read parent ParcelSegment for parcel '{parcel.Name}' loop {loopIndex} " +
                $"element {elementIndex}: {ex.Message}", ex);
        }

        return data;
    }

    private static string TryGetSegmentIdentity(dynamic parentSegment, out string? warning)
    {
        warning = null;

        try
        {
            string handle = (string)parentSegment.Handle;
            if (!string.IsNullOrEmpty(handle))
                return handle;
        }
        catch
        {
            // fall through to Name
        }

        try
        {
            string name = (string)parentSegment.Name;
            if (!string.IsNullOrEmpty(name))
            {
                warning = $"Parent segment had no usable COM Handle; falling back to Name '{name}' " +
                          "as identity, which may not be unique across the drawing.";
                return name;
            }
        }
        catch
        {
            // fall through to synthetic identity
        }

        warning = "Parent segment exposed neither a usable Handle nor Name; identity is not stable " +
                  "and this element will never match another parcel's element.";
        return $"unknown-{Guid.NewGuid():N}";
    }

    private static string? TryGetParcelNumber(Parcel parcel)
    {
        try
        {
            return parcel.Number.ToString();
        }
        catch
        {
            return null;
        }
    }
}
