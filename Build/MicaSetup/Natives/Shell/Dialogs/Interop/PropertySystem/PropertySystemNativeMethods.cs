using MicaSetup.Natives;
using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

public static class PropertySystemNativeMethods
{
    public enum RelativeDescriptionType
    {
        General,
        Date,
        Size,
        Count,
        Revision,
        Length,
        Duration,
        Speed,
        Rate,
        Rating,
        Priority
    }

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int PSGetNameFromPropertyKey(
        ref PropertyKey propkey,
        [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszCanonicalName
    );

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern HResult PSGetPropertyDescription(
        ref PropertyKey propkey,
        ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyDescription ppv
    );

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int PSGetPropertyDescriptionListFromString(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPropList,
        [In] ref Guid riid,
        out IPropertyDescriptionList ppv
    );

    [DllImport(Lib.PropSys, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int PSGetPropertyKeyFromName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszCanonicalName,
        out PropertyKey propkey
    );
}
