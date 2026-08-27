using System.Collections;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
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
///   - Parcel.SiteId / Site.GetParcelIds() member names
///   - dynamic AcadObject / ParcelLoops behavior under the .NET 8 host runtime
///   - exact COM member names on AeccParcel / AeccParcelSegmentElement / AeccParcelSegment
/// Failures here should be self-diagnosing (see the try/catch messages below) rather
/// than silently skipping data.
/// </summary>
internal sealed class ParcelTopologyReader
{
    public IList<ParcelSegmentElementData> GetSegmentReferenceCounts(ObjectId selectedParcelId)
    {
        Document? activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");

        Database db = activeDocument.Database;

        using Transaction tr = db.TransactionManager.StartTransaction();

        var selectedParcel = tr.GetObject(selectedParcelId, OpenMode.ForRead) as Parcel
            ?? throw new InvalidOperationException(
                $"ObjectId {selectedParcelId} did not resolve to an Autodesk.Civil.DatabaseServices.Parcel.");

        ObjectId siteId = selectedParcel.SiteId;
        if (siteId.IsNull)
        {
            throw new InvalidOperationException(
                $"Parcel '{selectedParcel.Name}' ({selectedParcelId}) is not assigned to a Site; " +
                "cannot enumerate sibling parcels to compute reference counts.");
        }

        var site = tr.GetObject(siteId, OpenMode.ForRead) as Site
            ?? throw new InvalidOperationException(
                $"Could not open Site {siteId} referenced by parcel '{selectedParcel.Name}'.");

        ObjectIdCollection siteParcelIds = site.GetParcelIds();

        // key -> distinct parcels that reference it
        var referencingParcels = new Dictionary<SegmentElementKey, HashSet<ObjectId>>();
        var selectedParcelElements = new List<ParcelSegmentElementData>();
        var selectedParcelKeys = new List<SegmentElementKey>();

        foreach (ObjectId parcelId in siteParcelIds)
        {
            var parcel = tr.GetObject(parcelId, OpenMode.ForRead) as Parcel
                ?? throw new InvalidOperationException(
                    $"Site {siteId} contains ObjectId {parcelId} that did not resolve to a Parcel.");

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

                int elementIndex = 0;
                foreach (dynamic element in elements)
                {
                    ParcelSegmentElementData data = ReadElement(parcel, loopIndex, elementIndex, element);

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

                    if (parcelId == selectedParcelId)
                    {
                        selectedParcelElements.Add(data);
                        selectedParcelKeys.Add(key);
                    }

                    elementIndex++;
                }

                loopIndex++;
            }
        }

        for (int i = 0; i < selectedParcelElements.Count; i++)
        {
            selectedParcelElements[i].ReferenceCount = referencingParcels[selectedParcelKeys[i]].Count;
        }

        return selectedParcelElements;
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
