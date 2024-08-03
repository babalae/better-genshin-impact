using System;
using System.IO;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class LimitedFile(string rootPath)
{
    /// <summary>
    /// Normalize and validate a path.
    /// </summary>
    private string NormalizePath(string path)
    {
        // convert to full path relative to root
        path = path.Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, path));

        // if root is locked, make sure didn't attempt to exit it
        if (!fullPath.StartsWith(rootPath))
        {
            throw new ArgumentException($"Path '{path}' is not allowed, because its outside the caged root folder!");
        }

        // return full path
        return fullPath;
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>Text read from file.</returns>
    public string ReadTextSync(string path)
    {
        path = NormalizePath(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>Text read from file.</returns>
    public async Task<string> ReadText(string path)
    {
        try
        {
            path = NormalizePath(path);
            var ret = await File.ReadAllTextAsync(path);
            return ret;
        }
        catch (Exception ex)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Read all text from a file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="callbackFunc">Callback function.</param>
    /// <returns>Text read from file.</returns>
    public async Task<string> ReadText(string path, dynamic callbackFunc)
    {
        try
        {
            path = NormalizePath(path);
            var ret = await File.ReadAllTextAsync(path);
            callbackFunc(null, ret);
            return ret;
        }
        catch (Exception ex)
        {
            callbackFunc(ex.ToString(), null);
            return string.Empty;
        }
    }
}
