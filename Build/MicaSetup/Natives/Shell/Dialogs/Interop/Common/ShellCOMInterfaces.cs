using MicaSetup.Natives;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS0108

public enum SICHINTF
{
    SICHINT_DISPLAY = 0x00000000,
    SICHINT_CANONICAL = 0x10000000,
    SICHINT_TEST_FILESYSPATH_IF_NOT_EQUAL = 0x20000000,
    SICHINT_ALLFIELDS = unchecked((int)0x80000000)
}

[ComImport(),
Guid(ShellIIDGuid.ICondition),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICondition : IPersistStream
{
    [PreserveSig]
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    HResult IsDirty();

    [PreserveSig]
    HResult Load([In, MarshalAs(UnmanagedType.Interface)] IStream stm);

    [PreserveSig]
    HResult Save([In, MarshalAs(UnmanagedType.Interface)] IStream stm, bool fRemember);

    [PreserveSig]
    HResult GetSizeMax(out ulong cbSize);

    [PreserveSig]
    HResult GetConditionType([Out()] out SearchConditionType pNodeType);

    [PreserveSig]
    HResult GetSubConditions([In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);

    [PreserveSig]
    HResult GetComparisonInfo(
        [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszPropertyName,
        [Out] out SearchConditionOperation pcop,
        [Out] PropVariant ppropvar);

    [PreserveSig]
    HResult GetValueType([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszValueTypeName);

    [PreserveSig]
    HResult GetValueNormalization([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszNormalization);

    [PreserveSig]
    HResult GetInputTerms([Out] out IRichChunk ppPropertyTerm, [Out] out IRichChunk ppOperationTerm, [Out] out IRichChunk ppValueTerm);

    [PreserveSig]
    HResult Clone([Out()] out ICondition ppc);
};

[ComImport,
Guid(ShellIIDGuid.IConditionFactory),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IConditionFactory
{
    [PreserveSig]
    HResult MakeNot([In] ICondition pcSub, [In] bool fSimplify, [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult MakeAndOr([In] SearchConditionType ct, [In] IEnumUnknown peuSubs, [In] bool fSimplify, [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult MakeLeaf(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPropertyName,
        [In] SearchConditionOperation cop,
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszValueType,
        [In] PropVariant ppropvar,
        IRichChunk richChunk1,
        IRichChunk richChunk2,
        IRichChunk richChunk3,
        [In] bool fExpand,
        [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult Resolve();
};

[ComImport,
Guid("24264891-E80B-4fd3-B7CE-4FF2FAE8931F"),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEntity
{
}

[ComImport,
Guid(ShellIIDGuid.IEnumIDList),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumIDList
{
    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Next(uint celt, out nint rgelt, out uint pceltFetched);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Skip([In] uint celt);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Reset();

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Clone([MarshalAs(UnmanagedType.Interface)] out IEnumIDList ppenum);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ShellIIDGuid.IEnumUnknown)]
public interface IEnumUnknown
{
    [PreserveSig]
    HResult Next(uint requestedNumber, ref nint buffer, ref uint fetchedNumber);

    [PreserveSig]
    HResult Skip(uint number);

    [PreserveSig]
    HResult Reset();

    [PreserveSig]
    HResult Clone(out IEnumUnknown result);
}

[ComImport(),
                    Guid(ShellIIDGuid.IModalWindow),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IModalWindow
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime),
    PreserveSig]
    int Show([In] nint parent);
}

[ComImport,
Guid(ShellIIDGuid.IConditionFactory),
CoClass(typeof(ConditionFactoryCoClass))]
public interface INativeConditionFactory : IConditionFactory
{
}

[ComImport,
Guid(ShellIIDGuid.IQueryParserManager),
CoClass(typeof(QueryParserManagerCoClass))]
public interface INativeQueryParserManager : IQueryParserManager
{
}

[ComImport,
Guid(ShellIIDGuid.ISearchFolderItemFactory),
CoClass(typeof(SearchFolderItemFactoryCoClass))]
public interface INativeSearchFolderItemFactory : ISearchFolderItemFactory
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000109-0000-0000-C000-000000000046")]
public interface IPersistStream
{
    [PreserveSig]
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    HResult IsDirty();

    [PreserveSig]
    HResult Load([In, MarshalAs(UnmanagedType.Interface)] IStream stm);

    [PreserveSig]
    HResult Save([In, MarshalAs(UnmanagedType.Interface)] IStream stm, bool fRemember);

    [PreserveSig]
    HResult GetSizeMax(out ulong cbSize);
}

[ComImport,
Guid(ShellIIDGuid.IQueryParser),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IQueryParser
{
    [PreserveSig]
    HResult Parse([In, MarshalAs(UnmanagedType.LPWStr)] string pszInputString, [In] IEnumUnknown pCustomProperties, [Out] out IQuerySolution ppSolution);

    [PreserveSig]
    HResult SetOption([In] StructuredQuerySingleOption option, [In] PropVariant pOptionValue);

    [PreserveSig]
    HResult GetOption([In] StructuredQuerySingleOption option, [Out] PropVariant pOptionValue);

    [PreserveSig]
    HResult SetMultiOption([In] StructuredQueryMultipleOption option, [In, MarshalAs(UnmanagedType.LPWStr)] string pszOptionKey, [In] PropVariant pOptionValue);

    [PreserveSig]
    HResult GetSchemaProvider([Out] out nint ppSchemaProvider);

    [PreserveSig]
    HResult RestateToString([In] ICondition pCondition, [In] bool fUseEnglish, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszQueryString);

    [PreserveSig]
    HResult ParsePropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropertyName, [In, MarshalAs(UnmanagedType.LPWStr)] string pszInputString, [Out] out IQuerySolution ppSolution);

    [PreserveSig]
    HResult RestatePropertyValueToString([In] ICondition pCondition, [In] bool fUseEnglish, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszPropertyName, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszQueryString);
}

[ComImport,
Guid(ShellIIDGuid.IQueryParserManager),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IQueryParserManager
{
    [PreserveSig]
    HResult CreateLoadedParser([In, MarshalAs(UnmanagedType.LPWStr)] string pszCatalog, [In] ushort langidForKeywords, [In] ref Guid riid, [Out] out IQueryParser ppQueryParser);

    [PreserveSig]
    HResult InitializeOptions([In] bool fUnderstandNQS, [In] bool fAutoWildCard, [In] IQueryParser pQueryParser);

    [PreserveSig]
    HResult SetOption([In] QueryParserManagerOption option, [In] PropVariant pOptionValue);
};

[ComImport,
Guid(ShellIIDGuid.IQuerySolution),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IQuerySolution : IConditionFactory
{
    [PreserveSig]
    HResult MakeNot([In] ICondition pcSub, [In] bool fSimplify, [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult MakeAndOr([In] SearchConditionType ct, [In] IEnumUnknown peuSubs, [In] bool fSimplify, [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult MakeLeaf(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPropertyName,
        [In] SearchConditionOperation cop,
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszValueType,
        [In] PropVariant ppropvar,
        IRichChunk richChunk1,
        IRichChunk richChunk2,
        IRichChunk richChunk3,
        [In] bool fExpand,
        [Out] out ICondition ppcResult);

    [PreserveSig]
    HResult Resolve();

    [PreserveSig]
    HResult GetQuery([Out, MarshalAs(UnmanagedType.Interface)] out ICondition ppQueryNode, [Out, MarshalAs(UnmanagedType.Interface)] out IEntity ppMainType);

    [PreserveSig]
    HResult GetErrors([In] ref Guid riid, [Out] out nint ppParseErrors);

    [PreserveSig]
    HResult GetLexicalData([MarshalAs(UnmanagedType.LPWStr)] out string ppszInputString, [Out] out nint ppTokens, [Out] out uint plcid, [Out] /* IUnknown** */ out nint ppWordBreaker);
}

[ComImport,
Guid(ShellIIDGuid.IRichChunk),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRichChunk
{
    [PreserveSig]
    HResult GetData();
}

[ComImport,
Guid(ShellIIDGuid.ISearchFolderItemFactory),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISearchFolderItemFactory
{
    [PreserveSig]
    HResult SetScope([In, MarshalAs(UnmanagedType.Interface)] IShellItemArray ppv);

    [PreserveSig]
    HResult SetCondition([In] ICondition pCondition);

    [PreserveSig]
    int GetShellItem(ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
};

[ComImport,
Guid(ShellIIDGuid.ISharedBitmap),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISharedBitmap
{
    void GetSharedBitmap([Out] out nint phbm);

    void GetSize([Out] out SIZE pSize);

    void GetFormat([Out] out ThumbnailAlphaType pat);

    void InitializeBitmap([In] nint hbm, [In] ThumbnailAlphaType wtsAT);

    void Detach([Out] out nint phbm);
}

[ComImport,
Guid(ShellIIDGuid.IShellFolder),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
ComConversionLoss]
public interface IShellFolder
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void ParseDisplayName(nint hwnd, [In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc, [In, MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, [In, Out] ref uint pchEaten, [Out] nint ppidl, [In, Out] ref uint pdwAttributes);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult EnumObjects([In] nint hwnd, [In] ShellFolderEnumerationOptions grfFlags, [MarshalAs(UnmanagedType.Interface)] out IEnumIDList ppenumIDList);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult BindToObject([In] nint pidl, nint pbc, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void BindToStorage([In] ref nint pidl, [In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CompareIDs([In] nint lParam, [In] ref nint pidl1, [In] ref nint pidl2);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CreateViewObject([In] nint hwndOwner, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAttributesOf([In] uint cidl, [In] nint apidl, [In, Out] ref uint rgfInOut);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUIObjectOf([In] nint hwndOwner, [In] uint cidl, [In] nint apidl, [In] ref Guid riid, [In, Out] ref uint rgfReserved, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayNameOf([In] ref nint pidl, [In] uint uFlags, out nint pName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetNameOf([In] nint hwnd, [In] ref nint pidl, [In, MarshalAs(UnmanagedType.LPWStr)] string pszName, [In] uint uFlags, [Out] nint ppidlOut);
}

[ComImport,
Guid(ShellIIDGuid.IShellFolder2),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
ComConversionLoss]
public interface IShellFolder2 : IShellFolder
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void ParseDisplayName([In] nint hwnd, [In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc, [In, MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, [In, Out] ref uint pchEaten, [Out] nint ppidl, [In, Out] ref uint pdwAttributes);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void EnumObjects([In] nint hwnd, [In] ShellFolderEnumerationOptions grfFlags, [MarshalAs(UnmanagedType.Interface)] out IEnumIDList ppenumIDList);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void BindToObject([In] nint pidl, nint pbc, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void BindToStorage([In] ref nint pidl, [In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CompareIDs([In] nint lParam, [In] ref nint pidl1, [In] ref nint pidl2);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void CreateViewObject([In] nint hwndOwner, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAttributesOf([In] uint cidl, [In] nint apidl, [In, Out] ref uint rgfInOut);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUIObjectOf([In] nint hwndOwner, [In] uint cidl, [In] nint apidl, [In] ref Guid riid, [In, Out] ref uint rgfReserved, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDisplayNameOf([In] ref nint pidl, [In] uint uFlags, out nint pName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetNameOf([In] nint hwnd, [In] ref nint pidl, [In, MarshalAs(UnmanagedType.LPWStr)] string pszName, [In] uint uFlags, [Out] nint ppidlOut);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDefaultSearchGUID(out Guid pguid);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void EnumSearches([Out] out nint ppenum);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDefaultColumn([In] uint dwRes, out uint pSort, out uint pDisplay);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDefaultColumnState([In] uint iColumn, out uint pcsFlags);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDetailsEx([In] ref nint pidl, [In] ref PropertyKey pscid, [MarshalAs(UnmanagedType.Struct)] out object pv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDetailsOf([In] ref nint pidl, [In] uint iColumn, out nint psd);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void MapColumnToSCID([In] uint iColumn, out PropertyKey pscid);
}

[ComImport,
                                                Guid(ShellIIDGuid.IShellItem),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult BindToHandler(
        [In] nint pbc,
        [In] ref Guid bhid,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetDisplayName(
        [In] ShellItemDesignNameOptions sigdnName,
        out nint ppszName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAttributes([In] ShellFileGetAttributesOptions sfgaoMask, out ShellFileGetAttributesOptions psfgaoAttribs);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Compare(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem psi,
        [In] SICHINTF hint,
        out int piOrder);
}

[ComImport,
Guid(ShellIIDGuid.IShellItem2),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem2 : IShellItem
{
    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult BindToHandler(
        [In] nint pbc,
        [In] ref Guid bhid,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetDisplayName(
        [In] ShellItemDesignNameOptions sigdnName,
        [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetAttributes([In] ShellFileGetAttributesOptions sfgaoMask, out ShellFileGetAttributesOptions psfgaoAttribs);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void Compare(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem psi,
        [In] uint hint,
        out int piOrder);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), PreserveSig]
    int GetPropertyStore(
        [In] GetPropertyStoreOptions Flags,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyStoreWithCreateObject([In] GetPropertyStoreOptions Flags, [In, MarshalAs(UnmanagedType.IUnknown)] object punkCreateObject, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyStoreForKeys([In] ref PropertyKey rgKeys, [In] uint cKeys, [In] GetPropertyStoreOptions Flags, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out IPropertyStore ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyDescriptionList([In] ref PropertyKey keyType, [In] ref Guid riid, out nint ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult Update([In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetProperty([In] ref PropertyKey key, [Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCLSID([In] ref PropertyKey key, out Guid pclsid);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetFileTime([In] ref PropertyKey key, out System.Runtime.InteropServices.ComTypes.FILETIME pft);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetInt32([In] ref PropertyKey key, out int pi);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetString([In] ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUInt32([In] ref PropertyKey key, out uint pui);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUInt64([In] ref PropertyKey key, out ulong pull);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetBool([In] ref PropertyKey key, out int pf);
}

[ComImport,
Guid(ShellIIDGuid.IShellItemArray),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult BindToHandler(
        [In, MarshalAs(UnmanagedType.Interface)] nint pbc,
        [In] ref Guid rbhid,
        [In] ref Guid riid,
        out nint ppvOut);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetPropertyStore(
        [In] int Flags,
        [In] ref Guid riid,
        out nint ppv);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetPropertyDescriptionList(
        [In] ref PropertyKey keyType,
        [In] ref Guid riid,
        out nint ppv);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetAttributes(
        [In] ShellItemAttributeOptions dwAttribFlags,
        [In] ShellFileGetAttributesOptions sfgaoMask,
        out ShellFileGetAttributesOptions psfgaoAttribs);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetCount(out uint pdwNumItems);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetItemAt(
        [In] uint dwIndex,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult EnumItems([MarshalAs(UnmanagedType.Interface)] out nint ppenumShellItems);
}

[ComImportAttribute()]
[GuidAttribute("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory
{
    [PreserveSig]
    HResult GetImage(
    [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
    [In] SIIGBF flags,
    [Out] out nint phbm);
}

[ComImport,
    Guid(ShellIIDGuid.IShellLibrary),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellLibrary
{
    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult LoadLibraryFromItem(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem library,
        [In] AccessModes grfMode);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void LoadLibraryFromKnownFolder(
        [In] ref Guid knownfidLibrary,
        [In] AccessModes grfMode);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void AddFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem location);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void RemoveFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem location);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    HResult GetFolders(
        [In] LibraryFolderFilter lff,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void ResolveFolder(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem folderToResolve,
        [In] uint timeout,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetDefaultSaveFolder(
        [In] DefaultSaveFolderType dsft,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetDefaultSaveFolder(
        [In] DefaultSaveFolderType dsft,
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem si);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetOptions(
        out LibraryOptions lofOptions);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetOptions(
        [In] LibraryOptions lofMask,
        [In] LibraryOptions lofOptions);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetFolderType(out Guid ftid);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetFolderType([In] ref Guid ftid);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetIcon([MarshalAs(UnmanagedType.LPWStr)] out string icon);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetIcon([In, MarshalAs(UnmanagedType.LPWStr)] string icon);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void Commit();

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void Save(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem folderToSaveIn,
        [In, MarshalAs(UnmanagedType.LPWStr)] string libraryName,
        [In] LibrarySaveOptions lsf,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem2 savedTo);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SaveInKnownFolder(
        [In] ref Guid kfidToSaveIn,
        [In, MarshalAs(UnmanagedType.LPWStr)] string libraryName,
        [In] LibrarySaveOptions lsf,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem2 savedTo);
};

[ComImport,
Guid(ShellIIDGuid.IShellLinkW),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellLinkW
{
    void GetPath(
        [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
        int cchMaxPath,
        nint pfd,
        uint fFlags);

    void GetIDList(out nint ppidl);

    void SetIDList(nint pidl);

    void GetDescription(
        [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
        int cchMaxName);

    void SetDescription(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory(
        [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
        int cchMaxPath
        );

    void SetWorkingDirectory(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments(
        [Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
        int cchMaxPath);

    void SetArguments(
        [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotKey(out short wHotKey);

    void SetHotKey(short wHotKey);

    void GetShowCmd(out uint iShowCmd);

    void SetShowCmd(uint iShowCmd);

    void GetIconLocation(
        [Out(), MarshalAs(UnmanagedType.LPWStr)] out StringBuilder pszIconPath,
        int cchIconPath,
        out int iIcon);

    void SetIconLocation(
        [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
        int iIcon);

    void SetRelativePath(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
        uint dwReserved);

    void Resolve(nint hwnd, uint fFlags);

    void SetPath(
        [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport,
    Guid(ShellIIDGuid.IThumbnailCache),
InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailCache
{
    void GetThumbnail([In] IShellItem pShellItem,
    [In] uint cxyRequestedThumbSize,
    [In] ThumbnailOptions flags,
    [Out] out ISharedBitmap ppvThumb,
    [Out] out ThumbnailCacheOptions pOutFlags,
    [Out] ThumbnailId pThumbnailID);

    void GetThumbnailByID([In] ThumbnailId thumbnailID,
    [In] uint cxyRequestedThumbSize,
    [Out] out ISharedBitmap ppvThumb,
    [Out] out ThumbnailCacheOptions pOutFlags);
}

[ComImport,
ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.ConditionFactory)]
public class ConditionFactoryCoClass
{
}

[ComImport,
    Guid(ShellIIDGuid.CShellLink),
ClassInterface(ClassInterfaceType.None)]
public class CShellLink { }

[ComImport,
ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.QueryParserManager)]
public class QueryParserManagerCoClass
{
}

[ComImport,
    ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.SearchFolderItemFactory)]
public class SearchFolderItemFactoryCoClass
{
}
