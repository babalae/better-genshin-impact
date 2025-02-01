using System;
using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Helpers;

public class TempManager
{
    public static readonly string TempDirectory = Global.Absolute("User/Temp");
    static TempManager()
    {
        Directory.CreateDirectory(TempDirectory);
    }

    public static void CleanUp()
    {
        try
        {
            DirectoryHelper.DeleteDirectoryRecursively(TempDirectory);
        }
        catch
        {
            // Suppress any exceptions to avoid exposing errors
        }
    }
}