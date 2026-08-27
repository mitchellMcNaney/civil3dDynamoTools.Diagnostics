using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CivilDynamoTools.Diagnostics.Interop;

/// <summary>
/// Reads the real COM interface/coclass name off a late-bound (dynamic) COM object via
/// its ITypeInfo, e.g. "AeccParcelSegmentLine" vs "AeccParcelSegmentCurve". This needs
/// only BCL interop support (System.Runtime.InteropServices) - no Autodesk COM interop
/// assembly / PIA reference is required.
/// </summary>
internal static class ComTypeInspector
{
    public static string GetComTypeName(object comObject)
    {
        if (comObject is null)
            throw new ArgumentNullException(nameof(comObject));

        try
        {
            ITypeInfo typeInfo = Marshal.GetTypeInfoFromObject(comObject);
            typeInfo.GetDocumentation(-1, out string name, out _, out _, out _);
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }
        catch (Exception ex)
        {
            return $"Unknown (COM type inspection failed: {ex.Message})";
        }
    }
}
