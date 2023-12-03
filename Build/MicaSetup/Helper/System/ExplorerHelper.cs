using MicaSetup.Natives;
using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Helper;

public static class ExplorerHelper
{
    public static void Refresh()
    {
        Shell32.SHChangeNotify(SHCNE.SHCNE_ASSOCCHANGED, SHCNF.SHCNF_FLUSH, 0, 0);
    }

    public static void RefreshDesktop()
    {
        Shell32.SHChangeNotify(SHCNE.SHCNE_ASSOCCHANGED, SHCNF.SHCNF_IDLIST, 0, 0);
    }

    public static void Refresh(string path = null!)
    {
        if (string.IsNullOrEmpty(path))
        {
            Refresh();
            return;
        }

        nint strPtr = 0;
        try
        {
            strPtr = Marshal.StringToHGlobalUni(path ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Shell32.SHChangeNotify(SHCNE.SHCNE_UPDATEDIR, SHCNF.SHCNF_PATHW, strPtr, 0);
        }
        finally
        {
            if (strPtr != 0)
            {
                Marshal.FreeHGlobal(strPtr);
            }
        }
    }
}
