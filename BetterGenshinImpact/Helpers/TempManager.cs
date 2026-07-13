using System;
using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Helpers;

public class TempManager
{
    public static string GetTempDirectory()
    {
        var tmp = Global.Absolute("User/Temp");
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    public static void CleanUp()
    {
        try
        {
            DirectoryHelper.DeleteDirectoryRecursively(GetTempDirectory());
        }
        catch
        {
            // Suppress any exceptions to avoid exposing errors
        }
    }
}