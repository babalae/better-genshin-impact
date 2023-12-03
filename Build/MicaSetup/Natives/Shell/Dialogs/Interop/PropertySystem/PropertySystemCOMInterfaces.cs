using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS0108

[ComImport,
Guid(ShellIIDGuid.IPropertyDescription),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyDescription
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyKey(out PropertyKey pkey);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCanonicalName([MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetPropertyType(out VarEnum pvartype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime),
    PreserveSig]
    HResult GetDisplayName(out nint ppszName);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetEditInvitation(out nint ppszInvite);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetTypeFlags([In] PropertyTypeOptions mask, out PropertyTypeOptions ppdtFlags);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetViewFlags(out PropertyViewOptions ppdvFlags);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetDefaultColumnWidth(out uint pcxChars);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetDisplayType(out PropertyDisplayType pdisplaytype);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetColumnState(out PropertyColumnStateOptions pcsFlags);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetGroupingRange(out PropertyGroupingRange pgr);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRelativeDescriptionType(out PropertySystemNativeMethods.RelativeDescriptionType prdt);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRelativeDescription([In] PropVariant propvar1, [In] PropVariant propvar2, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDesc1, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDesc2);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetSortDescription(out PropertySortDescription psd);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetSortDescriptionLabel([In] bool fDescending, out nint ppszDescription);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetAggregationType(out PropertyAggregationType paggtype);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetConditionType(out PropertyConditionType pcontype, out PropertyConditionOperation popDefault);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetEnumTypeList([In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyEnumTypeList ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CoerceToCanonicalValue([In, Out] PropVariant propvar);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult FormatForDisplay([In] PropVariant propvar, [In] ref PropertyDescriptionFormatOptions pdfFlags, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplay);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult IsValueCanonical([In] PropVariant propvar);
}

[ComImport,
Guid(ShellIIDGuid.IPropertyDescription2),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyDescription2 : IPropertyDescription
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyKey(out PropertyKey pkey);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCanonicalName([MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyType(out VarEnum pvartype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetEditInvitation([MarshalAs(UnmanagedType.LPWStr)] out string ppszInvite);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetTypeFlags([In] PropertyTypeOptions mask, out PropertyTypeOptions ppdtFlags);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetViewFlags(out PropertyViewOptions ppdvFlags);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDefaultColumnWidth(out uint pcxChars);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayType(out PropertyDisplayType pdisplaytype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetColumnState(out uint pcsFlags);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetGroupingRange(out PropertyGroupingRange pgr);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRelativeDescriptionType(out PropertySystemNativeMethods.RelativeDescriptionType prdt);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRelativeDescription(
       [In] PropVariant propvar1,
       [In] PropVariant propvar2,
       [MarshalAs(UnmanagedType.LPWStr)] out string ppszDesc1,
       [MarshalAs(UnmanagedType.LPWStr)] out string ppszDesc2);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetSortDescription(out PropertySortDescription psd);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetSortDescriptionLabel([In] int fDescending, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDescription);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAggregationType(out PropertyAggregationType paggtype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetConditionType(
        out PropertyConditionType pcontype,
        out PropertyConditionOperation popDefault);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetEnumTypeList([In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CoerceToCanonicalValue([In, Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void FormatForDisplay([In] PropVariant propvar, [In] ref PropertyDescriptionFormatOptions pdfFlags, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplay);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult IsValueCanonical([In] PropVariant propvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetImageReferenceForValue(
        [In] PropVariant propvar,
        [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszImageRes);
}

[ComImport,
Guid(ShellIIDGuid.IPropertyDescriptionList),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyDescriptionList
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCount(out uint pcElem);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAt([In] uint iElem, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyDescription ppv);
}

[ComImport,
Guid(ShellIIDGuid.IPropertyEnumType),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyEnumType
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetEnumType([Out] out PropEnumType penumtype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRangeMinValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRangeSetValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayText([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplay);
}

[ComImport,
Guid(ShellIIDGuid.IPropertyEnumType2),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyEnumType2 : IPropertyEnumType
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetEnumType([Out] out PropEnumType penumtype);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRangeMinValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetRangeSetValue([Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayText([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplay);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetImageReference([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszImageRes);
}

[ComImport,
Guid(ShellIIDGuid.IPropertyEnumTypeList),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyEnumTypeList
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCount([Out] out uint pctypes);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAt(
    [In] uint itype,
    [In] ref Guid riid,
    [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyEnumType ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetConditionAt(
    [In] uint index,
    [In] ref Guid riid,
    out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void FindMatchingIndex(
    [In] PropVariant propvarCmp,
    [Out] out uint pnIndex);
}

[ComImport]
[Guid(ShellIIDGuid.IPropertyStore)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetCount([Out] out uint propertyCount);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetAt([In] uint propertyIndex, out PropertyKey key);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetValue([In] ref PropertyKey key, [Out] PropVariant pv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), PreserveSig]
    HResult SetValue([In] ref PropertyKey key, [In] PropVariant pv);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Commit();
}

[ComImport]
[Guid(ShellIIDGuid.IPropertyStoreCache)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStoreCache
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetState(ref PropertyKey key, [Out] out PropertyStoreCacheState state);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetValueAndState(ref PropertyKey propKey, [Out] PropVariant pv, [Out] out PropertyStoreCacheState state);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult SetState(ref PropertyKey propKey, PropertyStoreCacheState state);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult SetValueAndState(ref PropertyKey propKey, [In] PropVariant pv, PropertyStoreCacheState state);
}

[ComImport]
[Guid(ShellIIDGuid.IPropertyStoreCapabilities)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStoreCapabilities
{
    HResult IsPropertyWritable([In] ref PropertyKey propertyKey);
}

#pragma warning restore 108
