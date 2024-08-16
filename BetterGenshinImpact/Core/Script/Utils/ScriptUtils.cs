using System;
using System.IO;

namespace BetterGenshinImpact.Core.Script.Utils;

public class ScriptUtils
{
    /// <summary>
    /// Normalize and validate a path.
    /// </summary>
    public static string NormalizePath(string root, string path)
    {
        // convert to full path relative to root
        path = path.Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(root, path));

        // if root is locked, make sure didn't attempt to exit it
        if (!fullPath.StartsWith(root))
        {
            throw new ArgumentException($"Path '{path}' is not allowed, because its outside the caged root folder!");
        }

        // return full path
        return fullPath;
    }
}
