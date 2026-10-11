using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BetterGenshinImpact.I18nSync;

internal sealed record I18nScanResult(IReadOnlyList<string> Keys, int XamlFileCount, int CodeFileCount);

internal static partial class I18nScanner
{
    [GeneratedRegex(@"\{i18n:T\s+(?<key>[^{}]+?)\}", RegexOptions.CultureInvariant)]
    private static partial Regex I18nKeyRegex();

    [GeneratedRegex(@"I18nService\.Instance\.Translate\(\s*""(?<key>[^""]+)""\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex CodeTranslationKeyRegex();

    [GeneratedRegex(@"new\s+(?:HotKeySettingModel|StatusItem)\(\s*""(?<key>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex CodeModelKeyRegex();

    public static I18nScanResult Scan(string projectDirectory)
    {
        var keys = new List<string>();
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        var xamlFileCount = 0;
        var codeFileCount = 0;

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
                AddMatches(I18nKeyRegex().Matches(attribute.Value), keys, knownKeys);
            }
        }

        foreach (var filePath in Directory
                     .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(projectDirectory, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            codeFileCount++;
            var source = RemoveComments(File.ReadAllText(filePath));
            AddMatches(CodeTranslationKeyRegex().Matches(source), keys, knownKeys, normalizeCSharpEscapes: true);
            AddMatches(CodeModelKeyRegex().Matches(source), keys, knownKeys, normalizeCSharpEscapes: true);
        }

        return new I18nScanResult(keys, xamlFileCount, codeFileCount);
    }

    private static void AddMatches(
        MatchCollection matches,
        ICollection<string> keys,
        ISet<string> knownKeys,
        bool normalizeCSharpEscapes = false)
    {
        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim();
            if (normalizeCSharpEscapes)
            {
                key = NormalizeCSharpEscapes(key);
            }

            if (key.Length > 0 && knownKeys.Add(key))
            {
                keys.Add(key);
            }
        }
    }

    private static string NormalizeCSharpEscapes(string key)
    {
        key = Regex.Replace(
            key,
            @"\\u(?<code>[0-9a-fA-F]{4})",
            unicodeMatch => ((char)Convert.ToInt32(unicodeMatch.Groups["code"].Value, 16)).ToString(),
            RegexOptions.CultureInvariant);

        return key
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string RemoveComments(string source)
    {
        return Regex.Replace(
            source,
            @"//.*?$|/\*.*?\*/",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
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
