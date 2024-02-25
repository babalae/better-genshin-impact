using System;
using System.IO;

namespace BetterGenshinImpact.Core.Config;

public class Global
{
    public static string Version { get; } = "0.25.0";

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

    /// <summary>
    /// 新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns></returns>
    public static bool IsNewVersion(string currentVersion)
    {
        return IsNewVersion(Version, currentVersion);
    }

    /// <summary>
    /// 新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="oldVersion">老版本</param>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns>是否需要更新</returns>
    public static bool IsNewVersion(string oldVersion, string currentVersion)
    {
        var currentVersionArr = oldVersion.Split('.');
        var newVersionArr = currentVersion.Split('.');
        if (currentVersionArr.Length != newVersionArr.Length)
        {
            return false;
        }

        for (var i = 0; i < currentVersionArr.Length; i++)
        {

            if (int.Parse(currentVersionArr[i]) > int.Parse(newVersionArr[i]))
            {
                // 不需要更新
                return false;
            }

            if (int.Parse(currentVersionArr[i]) < int.Parse(newVersionArr[i]))
            {
                // 需要更新
                return true;
            }
        }
        return false;
    }

    public static void WriteAllText(string relativePath, string blackListJson)
    {
        var path = Absolute(relativePath);
        File.WriteAllText(path, blackListJson);
    }
}