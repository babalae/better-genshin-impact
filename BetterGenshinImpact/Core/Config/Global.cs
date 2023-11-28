using System;
using System.IO;

namespace BetterGenshinImpact.Core.Config;

public class Global
{
    public static string Version = "0.16.3";

    public static string StartUpPath { get; private set; } = AppContext.BaseDirectory;

    public static string AppPath { get; private set; } = Absolute("BetterGI.exe");

    public static string Absolute(string relativePath)
    {
        return Path.Combine(StartUpPath, relativePath);
    }

    public static string? ReadAllTextIfExist(string relativePath)
    {
        var path = Absolute(relativePath);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return null;
    }

    public static bool IsNewVersion(string currentVersion)
    {
        return IsNewVersion(Version, currentVersion);
    }

    public static bool IsNewVersion(string oldVersion, string currentVersion)
    {
        var currentVersionArr = oldVersion.Split('.');
        var newVersionArr = currentVersion.Split('.');
        if (currentVersionArr.Length != newVersionArr.Length)
        {
            return false;
        }

        for (int i = 0; i < currentVersionArr.Length; i++)
        {
            if (int.Parse(currentVersionArr[i]) < int.Parse(newVersionArr[i]))
            {
                return true;
            }
        }
        return false;
    }
}