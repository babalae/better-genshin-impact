using System.Runtime.InteropServices;
using System.Security;

namespace MicaSetup.Natives;

public static class Shell32
{
    [DllImport(Lib.Shell32, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern void SHChangeNotify(SHCNE wEventId, SHCNF uFlags, nint dwItem1, nint dwItem2);

    [DllImport(Lib.Shell32, ExactSpelling = true, SetLastError = true)]
    [SecurityCritical, SuppressUnmanagedCodeSecurity]
    public static extern void SHAddToRecentDocs(SHARD uFlags, [MarshalAs(UnmanagedType.LPWStr)] string pv);
}
