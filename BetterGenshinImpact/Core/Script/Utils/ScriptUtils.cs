using System;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Core.Script.Utils;

public class ScriptUtils
{
    /// <summary>
    /// Normalize and validate a path.
    /// </summary>
    public static string NormalizePath(string root, string path)
    {
        // 校验空字符串
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("文件路径不能为空");

        // 检查是否含有非法文件名字符
        var invalidChars = Path.GetInvalidFileNameChars();
        string fileName = Path.GetFileName(path);
        if (fileName.Any(c => invalidChars.Contains(c)))
        {
            throw new ArgumentException($"文件路径 '{path}' 包含非法字符");
        }

        // 替换分隔符
        path = path.Replace('\\', '/');

        // 组合并获取绝对路径
        var fullPath = Path.GetFullPath(Path.Combine(root, path));

        // 防止越界访问。目录边界必须带分隔符，避免同名前缀的兄弟目录被误判为目录内路径。
        var rootBoundary = Path.GetFullPath(root);
        if (!rootBoundary.EndsWith(Path.DirectorySeparatorChar)) rootBoundary += Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootBoundary, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.Equals(rootBoundary.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"文件路径 '{path}' 越界访问!");
        }

        return fullPath;
    }
}
