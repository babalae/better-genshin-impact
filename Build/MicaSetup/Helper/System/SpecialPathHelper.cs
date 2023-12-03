using System;
using System.IO;
using System.Reflection;

namespace MicaSetup.Helper;

public static class SpecialPathHelper
{
    private static string _defaultApplicationDataFolder = Option.Current.KeyName ?? Assembly.GetExecutingAssembly().GetName().Name;
    private static readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string TempPath { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public static void SetApplicationDataFolder(string applicationDataFolder)
    {
        _defaultApplicationDataFolder = applicationDataFolder;
    }

    public static string GetFolder(string optionFolder = null!)
    {
        return Path.Combine(_localApplicationData, optionFolder ?? _defaultApplicationDataFolder);
    }

    public static string GetPath(string baseName)
    {
        string configPath = Path.Combine(GetFolder(), baseName);

        if (!Directory.Exists(new FileInfo(configPath).DirectoryName))
        {
            _ = Directory.CreateDirectory(new FileInfo(configPath).DirectoryName!);
        }
        return configPath;
    }
}

public static class SpecialPathExtension
{
    public static string SureDirectoryExists(this string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}
