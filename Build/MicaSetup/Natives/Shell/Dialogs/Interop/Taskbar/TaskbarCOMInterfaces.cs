using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[ComImportAttribute()]
[GuidAttribute("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICustomDestinationList
{
    void SetAppID(
        [MarshalAs(UnmanagedType.LPWStr)] string pszAppID);

    [PreserveSig]
    HResult BeginList(
        out uint cMaxSlots,
        ref Guid riid,
        [Out(), MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    [PreserveSig]
    HResult AppendCategory(
        [MarshalAs(UnmanagedType.LPWStr)] string pszCategory,
        [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);

    void AppendKnownCategory(
        [MarshalAs(UnmanagedType.I4)] KnownDestinationCategory category);

    [PreserveSig]
    HResult AddUserTasks(
        [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);

    void CommitList();

    void GetRemovedDestinations(
        ref Guid riid,
        [Out(), MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    void DeleteList(
        [MarshalAs(UnmanagedType.LPWStr)] string pszAppID);

    void AbortList();
}

[ComImportAttribute()]
[GuidAttribute("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    void GetCount(out uint cObjects);

    void GetAt(
        uint iIndex,
        ref Guid riid,
        [Out(), MarshalAs(UnmanagedType.Interface)] out object ppvObject);
}

[ComImportAttribute()]
[GuidAttribute("5632B1A4-E38A-400A-928A-D4CD63230295")]
[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectCollection
{
    [PreserveSig]
    void GetCount(out uint cObjects);

    [PreserveSig]
    void GetAt(
        uint iIndex,
        ref Guid riid,
        [Out(), MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    void AddObject(
        [MarshalAs(UnmanagedType.Interface)] object pvObject);

    void AddFromArray(
        [MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);

    void RemoveObject(uint uiIndex);

    void Clear();
}

[ComImportAttribute()]
[GuidAttribute("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList4
{
    [PreserveSig]
    void HrInit();

    [PreserveSig]
    void AddTab(nint hwnd);

    [PreserveSig]
    void DeleteTab(nint hwnd);

    [PreserveSig]
    void ActivateTab(nint hwnd);

    [PreserveSig]
    void SetActiveAlt(nint hwnd);

    [PreserveSig]
    void MarkFullscreenWindow(
        nint hwnd,
        [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    [PreserveSig]
    void SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);

    [PreserveSig]
    void SetProgressState(nint hwnd, TaskbarProgressBarStatus tbpFlags);

    [PreserveSig]
    void RegisterTab(nint hwndTab, nint hwndMDI);

    [PreserveSig]
    void UnregisterTab(nint hwndTab);

    [PreserveSig]
    void SetTabOrder(nint hwndTab, nint hwndInsertBefore);

    [PreserveSig]
    void SetTabActive(nint hwndTab, nint hwndInsertBefore, uint dwReserved);

    [PreserveSig]
    HResult ThumbBarAddButtons(
        nint hwnd,
        uint cButtons,
        [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);

    [PreserveSig]
    HResult ThumbBarUpdateButtons(
        nint hwnd,
        uint cButtons,
        [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);

    [PreserveSig]
    void ThumbBarSetImageList(nint hwnd, nint himl);

    [PreserveSig]
    void SetOverlayIcon(
      nint hwnd,
      nint hIcon,
      [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);

    [PreserveSig]
    void SetThumbnailTooltip(
        nint hwnd,
        [MarshalAs(UnmanagedType.LPWStr)] string pszTip);

    [PreserveSig]
    void SetThumbnailClip(
        nint hwnd,
        nint prcClip);

    void SetTabProperties(nint hwndTab, SetTabPropertiesOption stpFlags);
}

[GuidAttribute("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
[ClassInterfaceAttribute(ClassInterfaceType.None)]
[ComImportAttribute()]
internal class CDestinationList { }

[GuidAttribute("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
[ClassInterfaceAttribute(ClassInterfaceType.None)]
[ComImportAttribute()]
internal class CEnumerableObjectCollection { }

[GuidAttribute("56FDF344-FD6D-11d0-958A-006097C9A090")]
[ClassInterfaceAttribute(ClassInterfaceType.None)]
[ComImportAttribute()]
internal class CTaskbarList { }
