using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Semver;

namespace BetterGenshinImpact.Core.Config;

public class Global
{
    public static string Version { get; } = Assembly.GetEntryAssembly()?.
        GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.
        InformationalVersion!;

    public static string StartUpPath { get; set; } = AppContext.BaseDirectory;
    public static string UserDataRoot => Path.Combine(StartUpPath, "User");

    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Absolute(string relativePath)
    {
        if (IsUserPath(relativePath))
        {
            // 检查是否是脚本文件路径，如果是则直接返回 User 目录路径
            if (IsScriptPath(relativePath))
            {
                return Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
            }

            return UserCache.Absolute(relativePath);
        }

        return Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.Combine(StartUpPath, relativePath);
    }

    private static bool IsScriptPath(string path)
    {
        var scriptDirs = new[]
        {
            "JsScript",
            "KeyMouseScript",
            "AutoFight",
            "AutoGeniusInvokation",
            "AutoPathing",
            "ScriptGroup",
            "OneDragon",
            "Images"
        };

        var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.StartsWith("User" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("User/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(5);
        }

        foreach (var dir in scriptDirs)
        {
            if (trimmed.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetRelativeUserPath(string path)
    {
        var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.StartsWith("User" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("User/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(5);
        }
        return trimmed;
    }

    public static string ScriptPath()
    {
        return Absolute("User\\JsScript");
    }

    public static string? ReadAllTextIfExist(string relativePath)
    {
        if (IsUserPath(relativePath))
        {
            // 脚本文件直接从磁盘读取
            if (IsScriptPath(relativePath))
            {
                var fullPath = Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
                return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            }

            return UserCache.ReadTextIfExist(relativePath);
        }

        var fullPath2 = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.Combine(StartUpPath, relativePath);
        return File.Exists(fullPath2) ? File.ReadAllText(fullPath2) : null;
    }

    /// <summary>
    ///     新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns></returns>
    public static bool IsNewVersion(string currentVersion)
    {
        return IsNewVersion(Version, currentVersion);
    }

    /// <summary>
    ///     新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="oldVersion">老版本</param>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns>是否需要更新</returns>
    public static bool IsNewVersion(string oldVersion, string currentVersion)
    {
        try
        {
            var oldVersionX = SemVersion.Parse(oldVersion);
            var currentVersionX = SemVersion.Parse(currentVersion);

            if (currentVersionX.CompareSortOrderTo(oldVersionX) > 0)
                // 需要更新
                return true;
        }
        catch
        {
            ///
        }

        // 不需要更新
        return false;
    }

    public static void WriteAllText(string relativePath, string content)
    {
        if (IsUserPath(relativePath))
        {
            // 脚本文件直接写入磁盘
            if (IsScriptPath(relativePath))
            {
                var fullPath = Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(fullPath, content);
                return;
            }

            UserCache.WriteText(relativePath, content);
            return;
        }

        var fullPath2 = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.Combine(StartUpPath, relativePath);
        var directory2 = Path.GetDirectoryName(fullPath2);
        if (!string.IsNullOrEmpty(directory2) && !Directory.Exists(directory2))
        {
            Directory.CreateDirectory(directory2);
        }

        File.WriteAllText(fullPath2, content);
    }

    public static byte[]? ReadAllBytesIfExist(string relativePath)
    {
        if (IsUserPath(relativePath))
        {
            // 脚本文件直接从磁盘读取
            if (IsScriptPath(relativePath))
            {
                var fullPath = Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
                return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            }

            return UserCache.ReadBytesIfExist(relativePath);
        }

        var fullPath2 = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.Combine(StartUpPath, relativePath);
        return File.Exists(fullPath2) ? File.ReadAllBytes(fullPath2) : null;
    }

    public static void WriteAllBytes(string relativePath, byte[] content, bool isText = false)
    {
        if (IsUserPath(relativePath))
        {
            // 脚本文件直接写入磁盘
            if (IsScriptPath(relativePath))
            {
                var fullPath = Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllBytes(fullPath, content);
                return;
            }

            UserCache.WriteBytes(relativePath, content, isText);
            return;
        }

        var fullPath2 = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.Combine(StartUpPath, relativePath);
        var directory2 = Path.GetDirectoryName(fullPath2);
        if (!string.IsNullOrEmpty(directory2) && !Directory.Exists(directory2))
        {
            Directory.CreateDirectory(directory2);
        }

        File.WriteAllBytes(fullPath2, content);
    }

    public static bool DeleteUserPath(string relativePath)
    {
        if (!IsUserPath(relativePath))
        {
            return false;
        }

        // 脚本文件直接从磁盘删除
        if (IsScriptPath(relativePath))
        {
            try
            {
                var fullPath = Path.Combine(UserDataRoot, GetRelativeUserPath(relativePath));
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        return UserCache.Delete(relativePath);
    }

    public static string AbsoluteUserData(string relativePath)
    {
        return Path.Combine(UserDataRoot, relativePath);
    }

    private static bool IsUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return UserStorage.TryNormalizeUserPath(path, out _);
        }

        var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!trimmed.StartsWith($"User{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("User/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (UserStorage.TryNormalizeUserPath(trimmed, out _))
        {
            return true;
        }

        return false;
    }
}
