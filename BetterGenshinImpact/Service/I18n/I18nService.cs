using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;
using OpenccNetLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Wpf.Ui.Violeta.Appearance;

namespace BetterGenshinImpact.Service.I18n;

/// <summary>
/// 界面多语言服务。简体中文原文同时作为资源键和缺省文案，因此不需要 zh-Hans.json；
/// 繁体中文在运行时通过 OpenCC 动态转换，因此也不需要 zh-Hant.json。
/// 可选语言由内置语言和插件登记的语言组成，插件语言可以覆盖同代码的内置语言，见 Docs/design/ui-language-plugin.md。
/// </summary>
public sealed class I18nService : INotifyPropertyChanged
{
    public const string DefaultLanguage = "zh-Hans";
    private const string TraditionalLanguage = "zh-Hant";

    /// <summary>
    /// 内置语言，按下拉框中的顺序排列。zh-Hans、zh-Hant 没有语言文件，其余读取 User/I18n/&lt;代码&gt;.json。
    /// </summary>
    private static readonly UiLanguageOption[] BuiltinLanguages =
    [
        new(DefaultLanguage, "简体中文"),
        new(TraditionalLanguage, "繁體中文"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ru", "Русский"),
        new("pt-BR", "Português (Brasil)"),
    ];

    /// <summary>
    /// 空的翻译表。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyTranslations = new Dictionary<string, string>();

    private static readonly Opencc TraditionalChineseConverter = new(OpenccConfig.S2Twp);
    private readonly string _i18nDirectory = Global.Absolute(Path.Combine("User", "I18n"));

    /// <summary>
    /// 保护插件语言列表和当前语言状态的锁。<see cref="Translate"/> 只读取不可变快照，不加锁。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 插件登记的语言，按登记顺序排列；同一语言代码以最后登记的为准。
    /// </summary>
    private readonly List<(string SourceId, UiLanguagePack Pack)> _pluginLanguages = [];

    /// <summary>
    /// 当前使用的翻译表，语言或插件语言变化时整体替换。
    /// </summary>
    private volatile TranslationTable _table = new(null, EmptyTranslations, false);

    /// <summary>
    /// 当前可选的语言列表快照。
    /// </summary>
    private volatile IReadOnlyList<UiLanguageOption> _languages = BuiltinLanguages;

    private string _currentLanguage = DefaultLanguage;
    private string _effectiveLanguage = DefaultLanguage;
    private long _revision;

    private I18nService()
    {
    }

    public static I18nService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 可选语言列表变化时触发（插件语言登记或移除）。在调用登记、移除方法的线程上触发，即界面线程。
    /// </summary>
    public event EventHandler? LanguagesChanged;

    /// <summary>
    /// 供 TExtension 创建的 Binding 监听。语言改变时递增，触发转换器重新取值。
    /// </summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>
    /// 当前可选的语言：内置语言在前，插件语言按显示名称排序。被插件覆盖的内置语言保持原位置和名称。
    /// </summary>
    public IReadOnlyList<UiLanguageOption> Languages => _languages;

    /// <summary>
    /// 保存的语言选择，不因回退而改变。
    /// </summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// 实际使用的语言；保存的选择不可用（例如提供它的插件未启用）时为 zh-Hans。
    /// </summary>
    public string EffectiveLanguage => _effectiveLanguage;

    /// <summary>
    /// 保存的语言选择是否不可用，已回退为简体中文。
    /// </summary>
    public bool IsFallback => !string.Equals(_currentLanguage, _effectiveLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 切换界面语言。语言不可用时回退为简体中文，保存的选择不变，插件登记该语言后自动生效。
    /// </summary>
    /// <param name="language">语言代码，为空时为简体中文。</param>
    public void ChangeLanguage(string? language)
    {
        lock (_gate)
        {
            _currentLanguage = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;
            ApplyCurrentLanguage();
        }

        RaiseRevisionChanged();
    }

    /// <summary>
    /// 登记某个插件提供的语言。同一来源重复登记时先移除旧的。
    /// 影响当前语言时立即重新应用，界面随之刷新。
    /// </summary>
    /// <param name="sourceId">来源标识，即插件标识。</param>
    /// <param name="packs">插件提供的语言。</param>
    public void AddPluginLanguages(string sourceId, IReadOnlyList<UiLanguagePack> packs)
    {
        // 大多数插件不提供语言，此时不触发列表变化
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
    /// 移除某个插件提供的全部语言。影响当前语言时立即重新应用（回退到内置翻译或简体中文）。
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
    /// 某个来源是否登记过语言。
    /// </summary>
    private bool HasPluginLanguages(string sourceId)
    {
        lock (_gate)
        {
            return _pluginLanguages.Any(x => x.SourceId == sourceId);
        }
    }

    /// <summary>
    /// 标记数据值（如保存在配置中、参与逻辑比较的中文选项）为 i18n Key，原样返回，不做翻译。
    /// 同步工具会扫描 <c>I18nService.Key("...")</c> 收集 Key；界面展示时再通过 <see cref="TBindingExtension"/> 翻译。
    /// </summary>
    public static string Key(string key) => key;

    public string Translate(string key)
    {
        var table = _table;
        // 1. 生效插件的翻译；2. 内置语言文件的翻译；3. 繁体中文用 OpenCC 转换，其他返回中文 Key
        if (table.Plugin is not null && table.Plugin.TryGetValue(key, out var translation) && !string.IsNullOrWhiteSpace(translation))
        {
            return translation;
        }

        if (table.Builtin.TryGetValue(key, out translation) && !string.IsNullOrWhiteSpace(translation))
        {
            return translation;
        }

        return table.UseTraditionalChinese ? TraditionalChineseConverter.Convert(key) : key;
    }

    /// <summary>
    /// 读取语言文件：Key 为简体中文原文，值为译文。
    /// </summary>
    /// <param name="filePath">语言文件的绝对路径。</param>
    /// <returns>按序数比较 Key 的翻译表。</returns>
    /// <exception cref="Exception">文件不存在、无法读取或不是字符串字典。</exception>
    public static IReadOnlyDictionary<string, string> ReadTranslationFile(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        return dictionary == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(dictionary, StringComparer.Ordinal);
    }

    /// <summary>
    /// 修改插件语言列表，重建可选语言；当前语言的翻译来源因此变化时重新应用。
    /// </summary>
    private void UpdatePluginLanguages(Action update)
    {
        bool currentChanged;
        lock (_gate)
        {
            var before = FindPluginPack(_currentLanguage);
            update();
            _languages = BuildLanguages();
            currentChanged = !ReferenceEquals(before, FindPluginPack(_currentLanguage));
            if (currentChanged)
            {
                ApplyCurrentLanguage();
            }
        }

        LanguagesChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
        {
            RaiseRevisionChanged();
        }
    }

    /// <summary>
    /// 按保存的选择确定实际语言，生成新的翻译表并设置 .NET 的 CurrentUICulture。调用方持有 <see cref="_gate"/>。
    /// </summary>
    private void ApplyCurrentLanguage()
    {
        var builtin = BuiltinLanguages.FirstOrDefault(l => string.Equals(l.Code, _currentLanguage, StringComparison.OrdinalIgnoreCase));
        var plugin = FindPluginPack(_currentLanguage);
        // 内置和插件都没有时回退为简体中文
        var effective = builtin?.Code ?? plugin?.Code ?? DefaultLanguage;
        _effectiveLanguage = effective;
        _table = new TranslationTable(
            plugin?.Translations,
            builtin is null ? EmptyTranslations : LoadBuiltinTranslations(builtin.Code),
            string.Equals(effective, TraditionalLanguage, StringComparison.OrdinalIgnoreCase));

        CultureInfo? culture = null;
        try
        {
            culture = CultureInfo.GetCultureInfo(effective);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // 插件语言代码不一定是 .NET 认识的文化名；此时只是不修改 .NET 的 CurrentUICulture。
        }

        // 同步 Wpf.Ui.Violeta 控件内置文案（如级联选择框的“请选择...”）的语言；
        // 插件语言无对应区域性时回退简体中文（#1846 曾固定为简体中文）。
        I18N.Culture = culture ?? CultureInfo.GetCultureInfo(DefaultLanguage);
    }

    /// <summary>
    /// 某个语言代码当前生效的插件语言（最后登记的），没有时为 null。调用方持有 <see cref="_gate"/>。
    /// </summary>
    private UiLanguagePack? FindPluginPack(string code)
    {
        for (var i = _pluginLanguages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_pluginLanguages[i].Pack.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return _pluginLanguages[i].Pack;
            }
        }

        return null;
    }

    /// <summary>
    /// 生成可选语言列表：内置语言在前，其后是不与内置语言重复的插件语言，按显示名称排序。调用方持有 <see cref="_gate"/>。
    /// </summary>
    private List<UiLanguageOption> BuildLanguages()
    {
        var languages = new List<UiLanguageOption>(BuiltinLanguages);
        var pluginOnly = _pluginLanguages
            .Select(x => x.Pack.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => BuiltinLanguages.All(l => !string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            .Select(code => FindPluginPack(code)!)
            .Select(pack => new UiLanguageOption(pack.Code, pack.DisplayName))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCulture);
        languages.AddRange(pluginOnly);
        return languages;
    }

    /// <summary>
    /// 读取内置语言的翻译。简体中文、繁体中文没有语言文件；读取失败时返回空表，界面显示中文。
    /// </summary>
    private IReadOnlyDictionary<string, string> LoadBuiltinTranslations(string language)
    {
        if (string.Equals(language, DefaultLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, TraditionalLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EmptyTranslations;
        }

        try
        {
            var filePath = Path.Combine(_i18nDirectory, $"{language}.json");
            return File.Exists(filePath) ? ReadTranslationFile(filePath) : EmptyTranslations;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"加载语言文件 {language}.json 失败：{exception}");
            return EmptyTranslations;
        }
    }

    /// <summary>
    /// 递增 <see cref="Revision"/>，让界面上的翻译绑定重新取值。
    /// </summary>
    private void RaiseRevisionChanged()
    {
        Interlocked.Increment(ref _revision);
        OnPropertyChanged(nameof(Revision));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 当前语言的翻译表，不可变。
    /// </summary>
    /// <param name="Plugin">生效插件的翻译，没有插件提供当前语言时为 null。</param>
    /// <param name="Builtin">内置语言文件的翻译。</param>
    /// <param name="UseTraditionalChinese">未翻译的条目是否转换为繁体中文。</param>
    private sealed record TranslationTable(
        IReadOnlyDictionary<string, string>? Plugin,
        IReadOnlyDictionary<string, string> Builtin,
        bool UseTraditionalChinese);
}
