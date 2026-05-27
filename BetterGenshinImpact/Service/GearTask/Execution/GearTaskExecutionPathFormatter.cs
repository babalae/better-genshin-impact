using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 执行记录中的任务路径格式化器。
/// 将运行期绝对路径转换为和任务定义列表一致的占位符路径。
/// </summary>
public static class GearTaskExecutionPathFormatter
{
    public const string PathingRepoFolderPlaceholder = "{pathingRepoFolder}";
    public const string JsUserFolderPlaceholder = "{jsUserFolder}";

    public static string FormatTaskPath(string? taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return string.Empty;
        }

        var normalizedInput = taskPath.Trim();
        if (!Path.IsPathRooted(normalizedInput))
        {
            return normalizedInput;
        }

        if (TryFormatUnderRoot(normalizedInput, MapPathingViewModel.PathJsonPath, PathingRepoFolderPlaceholder, out var pathingPath))
        {
            return pathingPath;
        }

        if (TryFormatUnderRoot(normalizedInput, Global.ScriptPath(), JsUserFolderPlaceholder, out var jsPath))
        {
            return jsPath;
        }

        return normalizedInput;
    }

    private static bool TryFormatUnderRoot(string sourcePath, string rootPath, string placeholder, out string formattedPath)
    {
        formattedPath = string.Empty;

        var normalizedSource = Path.GetFullPath(sourcePath);
        var normalizedRoot = Path.GetFullPath(rootPath);
        var sourceWithSeparator = EnsureTrailingSeparator(normalizedSource);
        var rootWithSeparator = EnsureTrailingSeparator(normalizedRoot);

        if (string.Equals(normalizedSource, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            formattedPath = sourcePath.EndsWith(Path.DirectorySeparatorChar) || sourcePath.EndsWith(Path.AltDirectorySeparatorChar)
                ? placeholder + Path.DirectorySeparatorChar
                : placeholder;
            return true;
        }

        if (!sourceWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(normalizedRoot, normalizedSource);
        formattedPath = string.IsNullOrEmpty(relativePath)
            ? placeholder
            : $@"{placeholder}\{relativePath}";

        if (sourcePath.EndsWith(Path.DirectorySeparatorChar) || sourcePath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            formattedPath += Path.DirectorySeparatorChar;
        }

        return true;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
