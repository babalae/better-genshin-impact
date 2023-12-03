using MicaSetup.Natives;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

internal static class PropVariantNativeMethods
{
    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromBooleanVector([In, MarshalAs(UnmanagedType.LPArray)] bool[] prgf, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromDoubleVector([In, Out] double[] prgn, uint cElems, [Out] PropVariant propvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromFileTime([In] ref System.Runtime.InteropServices.ComTypes.FILETIME pftIn, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromFileTimeVector([In, Out] System.Runtime.InteropServices.ComTypes.FILETIME[] prgft, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromInt16Vector([In, Out] short[] prgn, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromInt32Vector([In, Out] int[] prgn, uint cElems, [Out] PropVariant propVar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromInt64Vector([In, Out] long[] prgn, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromPropVariantVectorElem([In] PropVariant propvarIn, uint iElem, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromStringVector([In, Out] string[] prgsz, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromUInt16Vector([In, Out] ushort[] prgn, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromUInt32Vector([In, Out] uint[] prgn, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void InitPropVariantFromUInt64Vector([In, Out] ulong[] prgn, uint cElems, [Out] PropVariant ppropvar);

    [DllImport(Lib.Ole32, PreserveSig = false)]
    internal static extern void PropVariantClear([In, Out] PropVariant pvar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetBooleanElem([In] PropVariant propVar, [In] uint iElem, [Out, MarshalAs(UnmanagedType.Bool)] out bool pfVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetDoubleElem([In] PropVariant propVar, [In] uint iElem, [Out] out double pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I4)]
    internal static extern int PropVariantGetElementCount([In] PropVariant propVar);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetFileTimeElem([In] PropVariant propVar, [In] uint iElem, [Out, MarshalAs(UnmanagedType.Struct)] out System.Runtime.InteropServices.ComTypes.FILETIME pftVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetInt16Elem([In] PropVariant propVar, [In] uint iElem, [Out] out short pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetInt32Elem([In] PropVariant propVar, [In] uint iElem, [Out] out int pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetInt64Elem([In] PropVariant propVar, [In] uint iElem, [Out] out long pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetStringElem([In] PropVariant propVar, [In] uint iElem, [MarshalAs(UnmanagedType.LPWStr)] ref string ppszVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetUInt16Elem([In] PropVariant propVar, [In] uint iElem, [Out] out ushort pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetUInt32Elem([In] PropVariant propVar, [In] uint iElem, [Out] out uint pnVal);

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = false)]
    internal static extern void PropVariantGetUInt64Elem([In] PropVariant propVar, [In] uint iElem, [Out] out ulong pnVal);

    [DllImport(Lib.OleAut32, PreserveSig = false)]
    internal static extern nint SafeArrayAccessData(nint psa);

    [DllImport(Lib.OleAut32, PreserveSig = true)]
    internal static extern nint SafeArrayCreateVector(ushort vt, int lowerBound, uint cElems);

    [DllImport(Lib.OleAut32, PreserveSig = true)]
    internal static extern uint SafeArrayGetDim(nint psa);

    [DllImport(Lib.OleAut32, PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.IUnknown)]
    internal static extern object SafeArrayGetElement(nint psa, ref int rgIndices);

    [DllImport(Lib.OleAut32, PreserveSig = false)]
    internal static extern int SafeArrayGetLBound(nint psa, uint nDim);

    [DllImport(Lib.OleAut32, PreserveSig = false)]
    internal static extern int SafeArrayGetUBound(nint psa, uint nDim);

    [DllImport(Lib.OleAut32, PreserveSig = false)]
    internal static extern void SafeArrayUnaccessData(nint psa);
}
