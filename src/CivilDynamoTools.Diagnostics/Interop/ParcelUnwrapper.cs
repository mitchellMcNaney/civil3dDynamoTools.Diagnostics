using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace CivilDynamoTools.Diagnostics.Interop;

/// <summary>
/// Unwraps whatever object a Dynamo graph hands us into an AutoCAD ObjectId pointing at
/// a Civil 3D Parcel. We deliberately accept `object` at the node boundary rather than a
/// specific wrapper type, because the exact type a Civil 3D 2026 Dynamo graph passes for
/// a selected parcel has not been confirmed yet.
///
/// Known shapes are handled directly; anything else throws with the runtime type name
/// and its public property list, so the exception itself tells us what shape to add
/// support for once this is run in an actual Civil 3D 2026 + Dynamo session.
/// </summary>
internal static class ParcelUnwrapper
{
    public static ObjectId ToParcelObjectId(object? input) => ToObjectId(input, "parcel");

    /// <summary>
    /// Same unwrapping rules for any Civil 3D object (e.g. a Site). <paramref name="kind"/> only
    /// names the expected input in error messages; the caller checks what the id resolves to.
    /// </summary>
    public static ObjectId ToObjectId(object? input, string kind)
    {
        switch (input)
        {
            case null:
                throw new ArgumentNullException(nameof(input), $"{char.ToUpperInvariant(kind[0])}{kind[1..]} input was null.");
            case ObjectId id:
                return id;
            case DBObject dbObject:
                return dbObject.ObjectId;
        }

        Type type = input.GetType();

        if (TryGetPropertyValue<ObjectId>(input, type, "ObjectId", out var idFromProperty) ||
            TryGetPropertyValue<ObjectId>(input, type, "InternalObjectId", out idFromProperty))
        {
            return idFromProperty;
        }

        if (TryGetPropertyValue<DBObject>(input, type, "InternalDBObject", out var dbObjectFromProperty))
        {
            return dbObjectFromProperty.ObjectId;
        }

        string members = string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.PropertyType.Name} {p.Name}"));

        throw new InvalidOperationException(
            $"Could not unwrap {kind} input of runtime type '{type.FullName}' into an " +
            $"Autodesk.AutoCAD.DatabaseServices.ObjectId. Public properties found: [{members}]. " +
            "Update ParcelUnwrapper.ToObjectId to handle this wrapper shape.");
    }

    private static bool TryGetPropertyValue<T>(object instance, Type type, string propertyName, out T value)
    {
        value = default!;
        PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
            return false;

        object? raw = property.GetValue(instance);
        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        return false;
    }
}
