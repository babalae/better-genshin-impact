using System;
using System.IO;
namespace BetterGenshinImpact.Helpers;

public class TempManager
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "BetterGenshinImpact", "Temp");

    public static string GetTempDirectory()
    {
        Directory.CreateDirectory(TempRoot);
        return TempRoot;
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
