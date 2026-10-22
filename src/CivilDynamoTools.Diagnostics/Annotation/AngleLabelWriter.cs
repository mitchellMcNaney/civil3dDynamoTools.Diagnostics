using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;
using CivilDynamoTools.Diagnostics.Geometry;
using CivilParcel = Autodesk.Civil.DatabaseServices.Parcel;

namespace CivilDynamoTools.Diagnostics.Annotation;

/// <summary>
/// Writes planned angle labels as model-space MText. Each label is tagged with XData
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

    public WriteResult Write(ObjectId parcelId, IReadOnlyList<PlannedAngleLabel> labels, double textHeight, string layer)
    {
        if (textHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(textHeight), textHeight, "Text height must be positive.");

        Document activeDocument = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active Civil 3D document is available.");
        Database db = activeDocument.Database;

        using DocumentLock _ = activeDocument.LockDocument();
        using Transaction tr = db.TransactionManager.StartTransaction();

        var parcel = tr.GetObject(parcelId, OpenMode.ForRead) as CivilParcel
            ?? throw new InvalidOperationException(
                $"ObjectId {parcelId} did not resolve to an Autodesk.Civil.DatabaseServices.Parcel.");
        string parcelHandle = parcel.Handle.ToString();

        EnsureRegApp(db, tr);
        ObjectId layerId = string.IsNullOrWhiteSpace(layer) ? db.Clayer : EnsureLayer(db, tr, layer.Trim());

        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        int deleted = EraseExistingLabels(modelSpace, tr, parcelHandle);

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
            mtext.TextStyleId = db.Textstyle;
            mtext.LayerId = layerId;

            modelSpace.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);

            // XData needs the entity to be database-resident, so it is attached after appending.
            mtext.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, LabelKind),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parcelHandle),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, label.LoopIndex),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, label.VertexIndex));

            written.Add(new WrittenLabel(label, mtext.Handle.ToString()));
        }

        tr.Commit();
        return new WriteResult(parcel.Name, parcelHandle, deleted, written);
    }

    private static int EraseExistingLabels(BlockTableRecord modelSpace, Transaction tr, string parcelHandle)
    {
        string mtextDxfName = RXObject.GetClass(typeof(MText)).DxfName;
        var toErase = new List<Entity>();

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
                && values[2].Value as string == parcelHandle)
            {
                toErase.Add(entity);
            }
        }

        foreach (Entity entity in toErase)
        {
            entity.UpgradeOpen();
            entity.Erase();
        }
        return toErase.Count;
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
