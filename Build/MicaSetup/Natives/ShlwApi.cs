using System.Runtime.InteropServices;

namespace MicaSetup.Natives;

public static class ShlwApi
{
    [DllImport(Lib.ShlwApi, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int PathParseIconLocation([MarshalAs(UnmanagedType.LPWStr)] ref string pszIconFile);
}
