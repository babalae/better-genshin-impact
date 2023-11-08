using System;
using System.IO;

namespace BetterGenshinImpact.Core.Config;

public class Global
{
    public static string Version = "0.13.2";

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
}