using System.Runtime.InteropServices;
using System.Security;

namespace MicaSetup.Natives;

public static class NTdll
{
    [SecurityCritical]
    [DllImport(Lib.NTdll, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int RtlGetVersion(out OSVERSIONINFOEX versionInfo);
}
