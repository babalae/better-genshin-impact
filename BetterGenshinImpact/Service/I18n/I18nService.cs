using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace BetterGenshinImpact.Service.I18n;

/// <summary>
/// 界面多语言服务。中文原文同时作为资源键和缺省文案，因此不需要 zh-Hans.json。
/// </summary>
public sealed class I18nService : INotifyPropertyChanged
{
    public const string DefaultLanguage = "zh-Hans";

    private readonly string _i18nDirectory = Global.Absolute(Path.Combine("User", "I18n"));
    private IReadOnlyDictionary<string, string> _translations = new Dictionary<string, string>();
    private long _revision;

    private I18nService()
    {
    }

    public static I18nService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 供 TExtension 创建的 Binding 监听。语言改变时递增，触发转换器重新取值。
    /// </summary>
    public long Revision => _revision;

    public void ChangeLanguage(string? language)
    {
        language = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;
        _translations = LoadTranslations(language);

        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // 自定义语言文件名也允许使用；只是不修改 .NET 的 CurrentUICulture。
        }

        _revision++;
        OnPropertyChanged(nameof(Revision));
    }

    public string Translate(string key)
    {
        return _translations.TryGetValue(key, out var translation) && !string.IsNullOrWhiteSpace(translation)
            ? translation
            : key;
    }

    private IReadOnlyDictionary<string, string> LoadTranslations(string language)
    {
        if (string.Equals(language, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var filePath = Path.Combine(_i18nDirectory, $"{language}.json");
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            return dictionary == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(dictionary, StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"加载语言文件 {language}.json 失败：{exception}");
            return new Dictionary<string, string>();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
