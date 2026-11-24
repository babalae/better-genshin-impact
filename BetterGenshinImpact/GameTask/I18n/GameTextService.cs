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
/// 可选语言由内置语言和插件登记的语言组成，插件语言可以覆盖同代码的内置语言；本服务只认“来源标识 + 文本表”，不依赖插件系统。
/// 缺少文本属于预期情况（静默回退），不记录日志；只有内置文本文件损坏时才记录错误。
/// </summary>
public sealed class GameTextService
{
    /// <summary>
    /// 基准语言。代码中用到的每个 key 都必须在该语言表中存在，其他语言缺项时回退到它。
    /// </summary>
    public const string BaseLanguage = "zh-Hans";

    /// <summary>
    /// 内置游戏语言，按下拉框中的顺序排列，文本读取 GameTask/I18n/&lt;代码&gt;.json。其他语言由插件提供（如意大利语插件 lang_it）。
    /// </summary>
    public static IReadOnlyList<GameLanguageOption> BuiltinLanguages { get; } =
    [
        new(BaseLanguage, "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("en", "English"),
        new("fr", "Français"),
    ];

    private static readonly Lazy<GameTextService> LazyInstance =
        new(() => new GameTextService(Global.Absolute(Path.Combine("GameTask", "I18n"))));

    public static GameTextService Instance => LazyInstance.Value;

    /// <summary>
    /// 内置文本文件所在目录。
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// 内置语言的文本表，首次使用时懒加载，加载后不变。
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>>> _builtinTables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保护插件语言列表的锁。取值只读取不可变的 <see cref="_catalog"/> 快照，不加锁。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 插件登记的语言，按登记顺序排列；同一语言代码以最后登记的为准。
    /// </summary>
    private readonly List<(string SourceId, GameTextPack Pack)> _pluginLanguages = [];

    /// <summary>
    /// 当前的语言目录快照，插件语言变化时整体替换。
    /// </summary>
    private volatile Catalog _catalog = new(BuiltinLanguages, FrozenDictionary<string, GameTextPack>.Empty);

    private ILogger? _logger;

    /// <param name="directory">存放内置语言 {language}.json 的目录</param>
    /// <param name="logger">为空时在首次需要记录日志时从容器中解析，解析失败则不记录</param>
    public GameTextService(string directory, ILogger? logger = null)
    {
        _directory = directory;
        _logger = logger;
    }

    /// <summary>
    /// 可选语言列表变化时触发（插件语言登记或移除）。在调用登记、移除方法的线程上触发，即界面线程。
    /// </summary>
    public event EventHandler? LanguagesChanged;

    /// <summary>
    /// 当前可选的游戏语言：内置语言在前，插件语言按显示名称排序。被插件覆盖的内置语言保持原位置和名称。
    /// </summary>
    public IReadOnlyList<GameLanguageOption> Languages => _catalog.Languages;

    /// <summary>
    /// 配置根中的游戏语言（OtherConfig.GameCultureInfoName）。
    /// 能拿到配置的调用方应优先使用 <see cref="Get(string, string?)"/> 显式传入语言；
    /// 配置尚未加载时（例如单元测试）返回基准语言。
    /// </summary>
    public static string CurrentLanguage => ConfigService.Config?.OtherConfig.GameCultureInfoName ?? BaseLanguage;

    /// <summary>
    /// 某种语言当前是否可选（内置，或由已启用的插件提供）。
    /// </summary>
    public bool IsAvailable(string? language)
    {
        return IsAvailable(language, _catalog);
    }

    /// <summary>
    /// 实际使用的语言：可选时原样返回，否则沿父区域（如 en-US → en）查找，都不可选时返回基准语言。
    /// </summary>
    public string GetEffectiveLanguage(string? language)
    {
        return NormalizeLanguage(language, _catalog);
    }

    /// <summary>
    /// 按配置根中的游戏语言取值，供无法拿到配置的静态调用方使用。
    /// </summary>
    public string Get(string key)
    {
        return Get(key, CurrentLanguage);
    }

    /// <summary>
    /// 按指定游戏语言取值。不可选的语言先按父区域（如 en-US → en）查找，仍不可选时按基准语言处理；
    /// 目标语言缺项时回退到基准语言；基准语言也没有时返回 key 本身。
    /// </summary>
    public string Get(string key, string? language)
    {
        // 同一次取值只使用一个目录快照，避免插件启用、停用时前后不一致
        var catalog = _catalog;
        language = NormalizeLanguage(language, catalog);

        if (TryGet(key, language, catalog, out var value) || TryGet(key, BaseLanguage, catalog, out value))
        {
            return value;
        }

        return key;
    }

    /// <summary>
    /// 只查指定语言，不回退到其他语言：先查生效插件的文本，再查内置文本。
    /// </summary>
    public bool TryGet(string key, string language, [NotNullWhen(true)] out string? value)
    {
        return TryGet(key, language, _catalog, out value);
    }

    /// <summary>
    /// 登记某个插件提供的游戏语言。同一来源重复登记时先移除旧的。
    /// 已经开始的任务在构造时取过文本，下次启动任务才使用新文本。
    /// </summary>
    /// <param name="sourceId">来源标识，即插件标识。</param>
    /// <param name="packs">插件提供的游戏语言。</param>
    public void AddPluginLanguages(string sourceId, IReadOnlyList<GameTextPack> packs)
    {
        // 大多数插件不提供游戏语言，此时不触发列表变化
        if (packs.Count == 0 && !HasPluginLanguages(sourceId))
        {
            return;
        }

        UpdatePluginLanguages(() =>
        {
            _pluginLanguages.RemoveAll(x => x.SourceId == sourceId);
            _pluginLanguages.AddRange(packs.Select(pack => (sourceId, pack)));
        });
    }

    /// <summary>
    /// 移除某个插件提供的全部游戏语言，同代码的语言回退到内置文本或基准语言。
    /// </summary>
    /// <param name="sourceId">来源标识，即插件标识。</param>
    public void RemovePluginLanguages(string sourceId)
    {
        if (!HasPluginLanguages(sourceId))
        {
            return;
        }

        UpdatePluginLanguages(() => _pluginLanguages.RemoveAll(x => x.SourceId == sourceId));
    }

    /// <summary>
    /// 读取游戏文本文件：Key 为语义标识，值为文本或匹配模式，空值视为缺项丢弃。
    /// </summary>
    /// <param name="filePath">文本文件的绝对路径。</param>
    /// <returns>按序数比较 Key 的文本表。</returns>
    /// <exception cref="Exception">文件不存在、无法读取或不是字符串字典。</exception>
    public static FrozenDictionary<string, string> ReadTableFile(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        return dictionary == null
            ? FrozenDictionary<string, string>.Empty
            : dictionary
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToFrozenDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// 在指定目录快照中只查一种语言：生效插件的文本优先，其次是内置文本。
    /// </summary>
    private bool TryGet(string key, string language, Catalog catalog, [NotNullWhen(true)] out string? value)
    {
        if (catalog.Plugins.TryGetValue(language, out var pack) && pack.Table.TryGetValue(key, out value))
        {
            return true;
        }

        if (IsBuiltin(language))
        {
            return GetBuiltinTable(language).TryGetValue(key, out value);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 是否是内置语言。
    /// </summary>
    private static bool IsBuiltin(string language)
    {
        return BuiltinLanguages.Any(l => string.Equals(l.Code, language, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 在指定目录快照中某种语言是否可选。
    /// </summary>
    private static bool IsAvailable(string? language, Catalog catalog)
    {
        return !string.IsNullOrEmpty(language) && (IsBuiltin(language) || catalog.Plugins.ContainsKey(language));
    }

    /// <summary>
    /// 把语言名规范为可选的语言：可选时原样返回，否则沿父区域查找，都不可选时返回基准语言。
    /// </summary>
    private static string NormalizeLanguage(string? language, Catalog catalog)
    {
        if (IsAvailable(language, catalog))
        {
            return language!;
        }

        if (!string.IsNullOrEmpty(language))
        {
            try
            {
                for (var culture = CultureInfo.GetCultureInfo(language).Parent; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
                {
                    if (IsAvailable(culture.Name, catalog))
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

    /// <summary>
    /// 某个来源是否登记过游戏语言。
    /// </summary>
    private bool HasPluginLanguages(string sourceId)
    {
        lock (_gate)
        {
            return _pluginLanguages.Any(x => x.SourceId == sourceId);
        }
    }

    /// <summary>
    /// 修改插件语言列表并重建目录快照，然后通知列表变化。
    /// </summary>
    private void UpdatePluginLanguages(Action update)
    {
        lock (_gate)
        {
            update();
            _catalog = BuildCatalog();
        }

        LanguagesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 生成目录快照：每个语言代码取最后登记的插件语言；可选语言为内置语言加上不与内置语言重复的插件语言（按显示名称排序）。
    /// 调用方持有 <see cref="_gate"/>。
    /// </summary>
    private Catalog BuildCatalog()
    {
        var plugins = new Dictionary<string, GameTextPack>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, pack) in _pluginLanguages)
        {
            // 按登记顺序覆盖，最后登记的生效
            plugins[pack.Code] = pack;
        }

        var languages = new List<GameLanguageOption>(BuiltinLanguages);
        languages.AddRange(plugins.Values
            .Where(pack => !IsBuiltin(pack.Code))
            .Select(pack => new GameLanguageOption(pack.Code, pack.DisplayName))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCulture));
        return new Catalog(languages, plugins.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private FrozenDictionary<string, string> GetBuiltinTable(string language)
    {
        return _builtinTables.GetOrAdd(language, l => new Lazy<FrozenDictionary<string, string>>(() => LoadBuiltinTable(l))).Value;
    }

    /// <summary>
    /// 读取内置语言的文本表。文件不存在时视为空表；文件损坏时记录错误并视为空表，不抛异常。
    /// </summary>
    private FrozenDictionary<string, string> LoadBuiltinTable(string language)
    {
        var filePath = Path.Combine(_directory, $"{language}.json");
        if (!File.Exists(filePath))
        {
            return FrozenDictionary<string, string>.Empty;
        }

        try
        {
            return ReadTableFile(filePath);
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

    /// <summary>
    /// 语言目录快照，不可变。
    /// </summary>
    /// <param name="Languages">可选语言。</param>
    /// <param name="Plugins">每个语言代码当前生效的插件语言，不区分大小写。</param>
    private sealed record Catalog(IReadOnlyList<GameLanguageOption> Languages, FrozenDictionary<string, GameTextPack> Plugins);
}
