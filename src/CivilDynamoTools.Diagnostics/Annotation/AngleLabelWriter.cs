using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;
using CivilDynamoTools.Diagnostics.Geometry;
using CivilParcel = Autodesk.Civil.DatabaseServices.Parcel;

namespace CivilDynamoTools.Diagnostics.Annotation;

/// <summary>
/// Writes planned angle labels as annotative model-space MText, carrying the drawing's
/// current annotation scale (CANNOSCALE). Each label is tagged with XData
/// (app <see cref="RegAppName"/>: kind, parcel handle, loop index, vertex index), and every
/// write first erases the MText already tagged for that parcel, so re-running a graph
/// replaces labels instead of duplicating them. Untagged MText is never touched.
/// </summary>
internal sealed class AngleLabelWriter
{
    public const string RegAppName = "CDT_DIAGNOSTICS";
    private const string LabelKind = "ParcelAngleLabel";

    public sealed record WrittenLabel(PlannedAngleLabel Label, string Handle);

    public sealed record WriteResult(string ParcelName, string ParcelHandle, int DeletedCount, IReadOnlyList<WrittenLabel> Written);

    /// <summary>
    /// The drawing's current annotation scale. <c>Scale</c> is paper units per drawing unit, so a
    /// plotted text height divided by it is the model-space height.
    /// </summary>
    public static (string Name, double Scale) CurrentAnnotationScale()
    {
        Document activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");
        AnnotationScale scale = activeDocument.Database.Cannoscale
            ?? throw new InvalidOperationException("The drawing has no current annotation scale (CANNOSCALE).");
        if (!(scale.Scale > 0))
            throw new InvalidOperationException($"Annotation scale '{scale.Name}' has a non-positive scale factor ({scale.Scale}).");
        return (scale.Name, scale.Scale);
    }

    /// <param name="textHeight">Model-space text height at the current annotation scale.</param>
    public WriteResult Write(ObjectId parcelId, IReadOnlyList<PlannedAngleLabel> labels, double textHeight, string layer, string textStyle)
        => WriteAll(new[] { (parcelId, labels) }, textHeight, layer, textStyle).Single();

    /// <summary>
    /// Replaces the labels of several parcels in one transaction. A parcel with no labels still
    /// has its previous labels erased. <paramref name="textStyle"/> must name an existing text
    /// style; empty = the drawing's current text style.
    /// </summary>
    /// <param name="textHeight">Model-space text height at the current annotation scale.</param>
    public IReadOnlyList<WriteResult> WriteAll(
        IReadOnlyList<(ObjectId ParcelId, IReadOnlyList<PlannedAngleLabel> Labels)> parcels,
        double textHeight, string layer, string textStyle)
    {
        if (textHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(textHeight), textHeight, "Text height must be positive.");

        Document activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");
        Database db = activeDocument.Database;

        using DocumentLock _ = activeDocument.LockDocument();
        using Transaction tr = db.TransactionManager.StartTransaction();

        var targets = parcels.Select(p =>
        {
            var parcel = tr.GetObject(p.ParcelId, OpenMode.ForRead) as CivilParcel
                ?? throw new InvalidOperationException(
                    $"ObjectId {p.ParcelId} did not resolve to an Autodesk.Civil.DatabaseServices.Parcel.");
            return (Parcel: parcel, Handle: parcel.Handle.ToString(), p.Labels);
        }).ToList();

        EnsureRegApp(db, tr);
        ObjectId layerId = string.IsNullOrWhiteSpace(layer) ? db.Clayer : EnsureLayer(db, tr, layer.Trim());
        ObjectId textStyleId = string.IsNullOrWhiteSpace(textStyle) ? db.Textstyle : FindTextStyle(db, tr, textStyle.Trim());

        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        Dictionary<string, int> deleted = EraseExistingLabels(modelSpace, tr, targets.Select(t => t.Handle).ToHashSet());

        var results = new List<WriteResult>(targets.Count);
        foreach (var (parcel, parcelHandle, labels) in targets)
        {
            var written = WriteLabels(db, tr, modelSpace, parcelHandle, labels, textHeight, layerId, textStyleId);
            results.Add(new WriteResult(parcel.Name, parcelHandle, deleted.GetValueOrDefault(parcelHandle), written));
        }

        tr.Commit();
        return results;
    }

    private static List<WrittenLabel> WriteLabels(
        Database db, Transaction tr, BlockTableRecord modelSpace, string parcelHandle,
        IReadOnlyList<PlannedAngleLabel> labels, double textHeight, ObjectId layerId, ObjectId textStyleId)
    {
        var written = new List<WrittenLabel>(labels.Count);
        foreach (PlannedAngleLabel label in labels)
        {
            var mtext = new MText();
            mtext.SetDatabaseDefaults(db);
            mtext.Location = new Point3d(label.Position.X, label.Position.Y, 0);
            mtext.Contents = label.Text;
            mtext.TextHeight = textHeight;
            mtext.Attachment = label.Anchor == LabelAnchor.MiddleLeft ? AttachmentPoint.MiddleLeft : AttachmentPoint.MiddleRight;
            mtext.Rotation = label.RotationDegrees * Math.PI / 180.0;
            mtext.TextStyleId = textStyleId;
            mtext.LayerId = layerId;

            modelSpace.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);

            // Annotative needs a database-resident entity. The height is set again afterwards so it
            // is the model height in the current scale context, however the conversion treated it.
            mtext.Annotative = AnnotativeStates.True;
            if (!mtext.HasContext(db.Cannoscale))
                mtext.AddContext(db.Cannoscale);
            mtext.TextHeight = textHeight;

            // XData needs the entity to be database-resident, so it is attached after appending.
            mtext.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, LabelKind),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parcelHandle),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, label.LoopIndex),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, label.VertexIndex));

            written.Add(new WrittenLabel(label, mtext.Handle.ToString()));
        }
        return written;
    }

    /// <summary>Erases this node's labels for the given parcels; returns the erased count per parcel handle.</summary>
    private static Dictionary<string, int> EraseExistingLabels(BlockTableRecord modelSpace, Transaction tr, HashSet<string> parcelHandles)
    {
        string mtextDxfName = RXObject.GetClass(typeof(MText)).DxfName;
        var toErase = new List<Entity>();
        var counts = new Dictionary<string, int>();

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased || id.ObjectClass.DxfName != mtextDxfName)
                continue;

            var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
            using ResultBuffer? xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
                continue;

            TypedValue[] values = xdata.AsArray();
            if (values.Length >= 3
                && values[1].Value as string == LabelKind
                && values[2].Value is string handle
                && parcelHandles.Contains(handle))
            {
                toErase.Add(entity);
                counts[handle] = counts.GetValueOrDefault(handle) + 1;
            }
        }

        foreach (Entity entity in toErase)
        {
            entity.UpgradeOpen();
            entity.Erase();
        }
        return counts;
    }

    private static void EnsureRegApp(Database db, Transaction tr)
    {
        var regApps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (regApps.Has(RegAppName))
            return;

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord { Name = RegAppName };
        regApps.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
    }

    /// <summary>Text styles are not created: a missing name is most likely a typo, so it fails with the available names.</summary>
    private static ObjectId FindTextStyle(Database db, Transaction tr, string name)
    {
        var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (styles.Has(name))
            return styles[name];

        string available = string.Join(", ", styles.Cast<ObjectId>()
            .Select(id => (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead))
            .Where(s => !s.IsShapeFile && !string.IsNullOrEmpty(s.Name))
            .Select(s => $"'{s.Name}'"));
        throw new ArgumentException($"Text style '{name}' does not exist in this drawing. Available: {available}.", nameof(name));
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name)
    {
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
            return layers[name];

        try
        {
            SymbolUtilityServices.ValidateSymbolName(name, false);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"'{name}' is not a valid layer name: {ex.Message}", nameof(name), ex);
        }

        layers.UpgradeOpen();
        var record = new LayerTableRecord { Name = name };
        ObjectId id = layers.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
        return id;
    }
}
