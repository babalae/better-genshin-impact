using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BetterGenshinImpact.I18nSync;

internal sealed record I18nScanResult(IReadOnlyList<string> Keys, int XamlFileCount);

internal static partial class I18nScanner
{
    [GeneratedRegex(@"\{i18n:T\s+(?<key>[^{}]+?)\}", RegexOptions.CultureInvariant)]
    private static partial Regex I18nKeyRegex();

    public static I18nScanResult Scan(string projectDirectory)
    {
        var keys = new List<string>();
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        var xamlFileCount = 0;

        foreach (var filePath in Directory
                     .EnumerateFiles(projectDirectory, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(projectDirectory, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            xamlFileCount++;
            XDocument document;
            try
            {
                document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"解析 XAML 失败：{filePath}", exception);
            }

            foreach (var attribute in document.Descendants().Attributes())
            {
                foreach (Match match in I18nKeyRegex().Matches(attribute.Value))
                {
                    var key = match.Groups["key"].Value.Trim();
                    if (key.Length > 0 && knownKeys.Add(key))
                    {
                        keys.Add(key);
                    }
                }
            }
        }

        return new I18nScanResult(keys, xamlFileCount);
    }

    private static bool IsBuildOutput(string projectDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, filePath);
        return relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
