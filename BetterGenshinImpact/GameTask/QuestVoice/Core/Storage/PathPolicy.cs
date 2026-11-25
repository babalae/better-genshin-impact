using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

/// <summary>Lexical boundary plus existing ancestor checks; inputs use independent read-only paths.</summary>
public sealed class PathPolicy
{
    public string Root { get; }
    public PathPolicy(string root)
    {
        RejectAmbiguousWindowsComponents(root, nameof(root));
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string RequireOutput(string path)
    {
        RejectAmbiguousWindowsComponents(path, nameof(path));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, Root));
        if (!Within(full, Root)) throw new ArgumentException("Output is outside the experiment root", nameof(path));
        if (Within(full, Path.Combine(Root, "Source"))) throw new ArgumentException("Source is read-only", nameof(path));
        // A junction in the supplied root or any existing ancestor also bypasses a lexical boundary.
        for (string? current = full; current != null; current = Directory.GetParent(current)?.FullName)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Output ancestor is a reparse point: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        var relative = Path.GetRelativePath(Root, full);
        if (relative.Contains(':')) throw new ArgumentException("Alternate data streams are not allowed", nameof(path));
        return full;
    }

    private static void RejectAmbiguousWindowsComponents(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        if (!OperatingSystem.IsWindows()) return;
        foreach (var component in path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (component is "." or "..") continue;
            if (component.EndsWith(' ') || component.EndsWith('.'))
                throw new ArgumentException("Windows path components cannot end with spaces or dots", parameterName);
        }
    }

    private static bool Within(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
