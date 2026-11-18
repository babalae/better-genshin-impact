using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

namespace BetterGenshinImpact.I18nSync;

internal sealed record LanguageFilePlan(
    string FilePath,
    IReadOnlyList<string> MissingKeys,
    IReadOnlyList<string> ObsoleteKeys,
    string OriginalContent,
    string UpdatedContent)
{
    public bool RequiresWrite => !string.Equals(OriginalContent, UpdatedContent, StringComparison.Ordinal);
}

internal static class I18nJsonSynchronizer
{
    /// <summary>
    /// 为主程序的语言目录和额外的语言目录（如插件仓库中的语言插件）中的每个 JSON 文件生成同步计划。
    /// </summary>
    /// <param name="projectDirectory">BetterGenshinImpact 项目目录，语言文件在 User/I18n。</param>
    /// <param name="extraDirectories">额外的语言目录，按主程序扫描出的 Key 同步。</param>
    /// <param name="sourceKeys">主程序扫描出的 Key。</param>
    /// <param name="addOnly">是否只补充、不删除废弃 Key。</param>
    public static IReadOnlyList<LanguageFilePlan> CreatePlans(
        string projectDirectory,
        IReadOnlyList<string> extraDirectories,
        IReadOnlyList<string> sourceKeys,
        bool addOnly)
    {
        if (sourceKeys.Count == 0)
        {
            throw new InvalidOperationException("没有扫描到任何 i18n Key，为避免误删已中止同步。");
        }

        var directories = new[] { Path.Combine(projectDirectory, "User", "I18n") }
            .Concat(extraDirectories.Select(Path.GetFullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var languageFiles = directories.SelectMany(EnumerateLanguageFiles).ToArray();

        var sourceKeySet = sourceKeys.ToHashSet(StringComparer.Ordinal);
        return languageFiles
            .Select(path => CreatePlan(path, sourceKeys, sourceKeySet, addOnly))
            .ToArray();
    }

    /// <summary>
    /// 列出语言目录中可同步的 JSON 文件；以下划线开头的文件不同步。
    /// </summary>
    private static string[] EnumerateLanguageFiles(string i18nDirectory)
    {
        if (!Directory.Exists(i18nDirectory))
        {
            throw new DirectoryNotFoundException($"多语言目录不存在：{i18nDirectory}");
        }

        var languageFiles = Directory
            .EnumerateFiles(i18nDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languageFiles.Length == 0)
        {
            throw new InvalidOperationException($"多语言目录中没有可同步的 JSON 文件：{i18nDirectory}");
        }

        return languageFiles;
    }

    public static void Write(LanguageFilePlan plan)
    {
        if (!plan.RequiresWrite)
        {
            return;
        }

        var temporaryPath = $"{plan.FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, plan.UpdatedContent, new UTF8Encoding(false));
            File.Move(temporaryPath, plan.FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static LanguageFilePlan CreatePlan(
        string filePath,
        IReadOnlyList<string> sourceKeys,
        IReadOnlySet<string> sourceKeySet,
        bool addOnly)
    {
        var originalContent = File.ReadAllText(filePath, Encoding.UTF8);
        var root = ParseLanguageFile(filePath, originalContent);
        var existingKeys = root.Properties().Select(property => property.Name).ToArray();
        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        var missingKeys = sourceKeys.Where(key => !existingKeySet.Contains(key)).ToArray();
        var obsoleteKeys = existingKeys.Where(key => !sourceKeySet.Contains(key)).ToArray();

        var outputKeys = addOnly
            ? sourceKeys.Concat(obsoleteKeys)
            : sourceKeys;
        var updatedRoot = new JObject();
        foreach (var key in outputKeys.OrderBy(key => key, StringComparer.Ordinal))
        {
            updatedRoot.Add(key, root.TryGetValue(key, StringComparison.Ordinal, out var value)
                ? value.DeepClone()
                : string.Empty);
        }

        return new LanguageFilePlan(
            filePath,
            missingKeys,
            obsoleteKeys,
            originalContent,
            Serialize(updatedRoot));
    }

    private static JObject ParseLanguageFile(string filePath, string json)
    {
        try
        {
            using var stringReader = new StringReader(json);
            using var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
            };
            var token = JToken.Load(jsonReader, new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Load,
            });

            if (token is not JObject root)
            {
                throw new InvalidDataException("根节点必须是 JSON 对象。");
            }

            var nonStringProperty = root.Properties().FirstOrDefault(property => property.Value.Type != JTokenType.String);
            if (nonStringProperty != null)
            {
                throw new InvalidDataException($"Key“{nonStringProperty.Name}”的翻译值必须是字符串。");
            }

            return root;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"语言文件格式错误：{filePath}\n{exception.Message}", exception);
        }
    }

    private static string Serialize(JObject root)
    {
        var builder = new StringBuilder();
        using var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture)
        {
            NewLine = "\n",
        };
        using (var jsonWriter = new JsonTextWriter(stringWriter)
               {
                   Formatting = Formatting.Indented,
                   Indentation = 2,
                   IndentChar = ' ',
               })
        {
            root.WriteTo(jsonWriter);
        }

        builder.Append('\n');
        return builder.ToString();
    }
}
