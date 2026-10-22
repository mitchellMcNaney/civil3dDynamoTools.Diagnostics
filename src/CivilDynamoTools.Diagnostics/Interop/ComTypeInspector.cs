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
            // Marshal.GetTypeInfoFromObject is .NET Framework-only; ask IDispatch directly.
            var dispatch = (IDispatchTypeInfo)comObject;
            Marshal.ThrowExceptionForHR(dispatch.GetTypeInfo(0, 0, out ITypeInfo typeInfo));
            typeInfo.GetDocumentation(-1, out string name, out _, out _, out _);
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }
        catch (Exception ex)
        {
            return $"Unknown (COM type inspection failed: {ex.Message})";
        }
    }

    // First two IDispatch vtable slots only (enough to reach GetTypeInfo); the slot order must match IDispatch.
    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchTypeInfo
    {
        [PreserveSig] int GetTypeInfoCount(out uint count);
        [PreserveSig] int GetTypeInfo(uint typeInfoIndex, uint lcid, out ITypeInfo typeInfo);
    }
}
