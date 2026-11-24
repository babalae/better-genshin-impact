using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.I18n;

/// <summary>
/// 游戏内识别文本服务。
/// key 见 <see cref="GameTextKeys"/>，value 为对应游戏客户端语言的实际显示文本或匹配模式（可能是正则，由调用方决定如何使用）。
/// 与界面语言（I18nService）相互独立，不读写任何线程 Culture。
/// 缺少文本属于预期情况（静默回退），不记录日志；只有文本文件损坏时才记录错误。
/// </summary>
public sealed class GameTextService
{
    /// <summary>
    /// 基准语言。代码中用到的每个 key 都必须在该语言表中存在，其他语言缺项时回退到它。
    /// </summary>
    public const string BaseLanguage = "zh-Hans";

    /// <summary>
    /// 游戏文本表支持的语言，也是游戏语言下拉框的数据源。
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["zh-Hans", "zh-Hant", "en", "fr", "it"];

    private static readonly Lazy<GameTextService> LazyInstance =
        new(() => new GameTextService(Global.Absolute(Path.Combine("GameTask", "I18n"))));

    public static GameTextService Instance => LazyInstance.Value;

    private readonly string _directory;
    private readonly ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private ILogger? _logger;

    /// <param name="directory">存放 {language}.json 的目录</param>
    /// <param name="logger">为空时在首次需要记录日志时从容器中解析，解析失败则不记录</param>
    public GameTextService(string directory, ILogger? logger = null)
    {
        _directory = directory;
        _logger = logger;
    }

    public static bool IsSupported(string? language)
    {
        return language != null && SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 配置根中的游戏语言（OtherConfig.GameCultureInfoName）。
    /// 能拿到配置的调用方应优先使用 <see cref="Get(string, string?)"/> 显式传入语言；
    /// 配置尚未加载时（例如单元测试）返回基准语言。
    /// </summary>
    public static string CurrentLanguage => ConfigService.Config?.OtherConfig.GameCultureInfoName ?? BaseLanguage;

    /// <summary>
    /// 按配置根中的游戏语言取值，供无法拿到配置的静态调用方使用。
    /// </summary>
    public string Get(string key)
    {
        return Get(key, CurrentLanguage);
    }

    /// <summary>
    /// 按指定游戏语言取值。不支持的语言先按父区域（如 en-US → en）查找，仍不支持时按基准语言处理；
    /// 目标语言缺项时回退到基准语言；基准语言也没有时返回 key 本身。
    /// </summary>
    public string Get(string key, string? language)
    {
        language = NormalizeLanguage(language);

        if (TryGet(key, language, out var value) || TryGet(key, BaseLanguage, out value))
        {
            return value;
        }

        return key;
    }

    /// <summary>
    /// 只查指定语言的表，不做回退。
    /// </summary>
    public bool TryGet(string key, string language, [NotNullWhen(true)] out string? value)
    {
        return GetTable(language).TryGetValue(key, out value);
    }

    /// <summary>
    /// 把语言名规范为支持的语言：支持时原样返回，否则沿父区域查找，都不支持时返回基准语言。
    /// </summary>
    private static string NormalizeLanguage(string? language)
    {
        if (IsSupported(language))
        {
            return language!;
        }

        if (!string.IsNullOrEmpty(language))
        {
            try
            {
                for (var culture = CultureInfo.GetCultureInfo(language).Parent; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
                {
                    if (IsSupported(culture.Name))
                    {
                        return culture.Name;
                    }
                }
            }
            catch (CultureNotFoundException)
            {
                // 无法识别的语言名按基准语言处理
            }
        }

        return BaseLanguage;
    }

    private FrozenDictionary<string, string> GetTable(string language)
    {
        return _tables.GetOrAdd(language, l => new Lazy<FrozenDictionary<string, string>>(() => LoadTable(l))).Value;
    }

    private FrozenDictionary<string, string> LoadTable(string language)
    {
        var filePath = Path.Combine(_directory, $"{language}.json");
        if (!File.Exists(filePath))
        {
            return FrozenDictionary<string, string>.Empty;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            return dictionary == null
                ? FrozenDictionary<string, string>.Empty
                : dictionary
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToFrozenDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            // 每种语言只加载一次，因此这里最多记录一次
            Logger.LogError(e, "加载游戏文本文件失败：{FilePath}", filePath);
            return FrozenDictionary<string, string>.Empty;
        }
    }

    private ILogger Logger
    {
        get
        {
            if (_logger != null)
            {
                return _logger;
            }

            try
            {
                _logger = App.GetLogger<GameTextService>() ?? (ILogger)NullLogger.Instance;
            }
            catch
            {
                // 单元测试等没有 Host 的场景
                _logger = NullLogger.Instance;
            }

            return _logger;
        }
    }
}
