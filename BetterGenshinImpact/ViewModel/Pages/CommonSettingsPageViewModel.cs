using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform.Wine;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Converters;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Win32;
using Newtonsoft.Json;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class CommonSettingsPageViewModel : ViewModel
{
    private readonly INavigationService _navigationService;

    private readonly NotificationService _notificationService;
    private readonly CustomHtmlMaskService _customHtmlMaskService;
    private readonly TpConfig _tpConfig = TaskContext.Instance().Config.TpConfig;

    private string _selectedArea = string.Empty;


    private string _selectedCountry = string.Empty;
    [ObservableProperty] private List<string> _adventurersGuildCountry = ["无", "枫丹", "稻妻", "璃月", "蒙德"];
    
    [ObservableProperty] private List<Tuple<TimeSpan, string>> _serverTimeZones =
    [
        Tuple.Create(TimeSpan.FromHours(8), "其他 UTC+08"),
        Tuple.Create(TimeSpan.FromHours(1), "欧服 UTC+01"),
        Tuple.Create(TimeSpan.FromHours(-5), "美服 UTC-05")
    ];

    public CommonSettingsPageViewModel(IConfigService configService, INavigationService navigationService,
        NotificationService notificationService, CustomHtmlMaskService customHtmlMaskService)
    {
        Config = configService.Get();
        Config.MaskWindowConfig.EnsureOverlayMetricItems();
        Config.MaskWindowConfig.MigrateLegacyOverlayMetricsLayout();
        _navigationService = navigationService;
        _notificationService = notificationService;
        _customHtmlMaskService = customHtmlMaskService;
        // 设置页需要可绑定对象，避免把 Dictionary<string, bool> 直接暴露给 XAML 并丢失固定枚举顺序。
        OverlayMetricItems = new ObservableCollection<OverlayMetricSettingItem>(
            OverlayMetricItemDefaults.AllItems.Select(item => new OverlayMetricSettingItem(Config.MaskWindowConfig, item, OnRefreshMaskSettings)));
        OverlayStyleSettingGroups = OverlayStyleSettingGroup.Create(Config.MaskWindowConfig, OnOverlayStyleChanged);
        InitializeCountries();
        InitializeMiyousheCookie();
        // 初始化OCR模型选择
        SelectedPaddleOcrModelConfig = Config.OtherConfig.OcrConfig.PaddleOcrModelConfig;
    }

    public AllConfig Config { get; set; }
    public ObservableCollection<OverlayMetricSettingItem> OverlayMetricItems { get; }
    public ObservableCollection<OverlayStyleSettingGroup> OverlayStyleSettingGroups { get; }
    public ObservableCollection<string> CountryList { get; } = new();
    public ObservableCollection<string> Areas { get; } = new();

    public ObservableCollection<string> MapPathingTypes { get; } = ["SIFT", "TemplateMatch"];

    [ObservableProperty] private FrozenDictionary<string, string> _languageDict =
        new[] { "zh-Hans", "zh-Hant", "en", "ja" }
            .ToFrozenDictionary(c => c, c => CultureInfoNameToKVPConverter.GetDisplayName(c));

    [RelayCommand]
    private async Task OnUpdateUiLanguageAsync()
    {
        var cultureName = Config.OtherConfig.UiCultureInfoName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            throw new InvalidOperationException("当前UI语言为空，无法更新语言文件。");
        }

        if (cultureName == "zh-Hans")
        {
            await ThemedMessageBox.InformationAsync("zh-Hans 无语言文件，无需更新。");
            return;
        }

        var urls = new[]
        {
            $"https://raw.githubusercontent.com/babalae/bettergi-i18n/refs/heads/main/i18n/{cultureName}.json",
            $"https://cnb.cool/bettergi/bettergi-i18n/-/git/raw/main/i18n/{cultureName}.json"
        };

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        byte[]? bytes = null;
        Exception? lastError = null;
        var allNotFound = true;
        foreach (var url in urls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("BetterGenshinImpact");
                using var response = await httpClient.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    lastError = new HttpRequestException("Language file not found.", null, response.StatusCode);
                    continue;
                }

                allNotFound = false;
                response.EnsureSuccessStatusCode();
                bytes = await response.Content.ReadAsByteArrayAsync();

                var json = Encoding.UTF8.GetString(bytes);
                _ = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                    ?? throw new JsonException("翻译文件不是有效的 JSON 字典。");
                break;
            }
            catch (Exception e)
            {
                lastError = e;
                allNotFound = false;
            }
        }

        if (bytes == null)
        {
            if (allNotFound)
            {
                await ThemedMessageBox.WarningAsync($"语言文件不存在：{cultureName}.json");
                return;
            }

            throw new Exception($"下载语言文件失败：{cultureName}.json", lastError);
        }

        var dir = Global.Absolute(@"User\I18n");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{cultureName}.json");
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tmp, bytes);

        if (File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path);
        }

        var translator = App.GetService<ITranslationService>() ?? throw new NullReferenceException();
        translator.Reload();
    }

    public string SelectedCountry
    {
        get => _selectedCountry;
        set
        {
            if (SetProperty(ref _selectedCountry, value))
            {
                UpdateAreas(value);
                SelectedArea = Areas.FirstOrDefault() ?? string.Empty;
            }
        }
    }

    public string SelectedArea
    {
        get => _selectedArea;
        set
        {
            if (SetProperty(ref _selectedArea, value))
            {
                UpdateRevivePoint(SelectedCountry, SelectedArea);
            }
        }
    }

    public ObservableCollection<PaddleOcrModelConfig> PaddleOcrModelConfigs { get; } =
        new(Enum.GetValues(typeof(PaddleOcrModelConfig)).Cast<PaddleOcrModelConfig>());

    [ObservableProperty] private PaddleOcrModelConfig _selectedPaddleOcrModelConfig;

    [RelayCommand]
    public void OnQuestionButtonOnClick()
    {
        //            Owner = this,
        WebpageWindow cookieWin = new()
        {
            Title = "日志分析",
            Width = 800,
            Height = 600,

            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        cookieWin.NavigateToHtml(TravelsDiaryDetailManager.generHtmlMessage());
        cookieWin.Show();
    }

    private void InitializeMiyousheCookie()
    {
        OtherConfig.Miyoushe mcfg = TaskContext.Instance().Config.OtherConfig.MiyousheConfig;
        if (mcfg.Cookie == string.Empty &&
            mcfg.LogSyncCookie)
        {
            var config = LogParse.LoadConfig();
            mcfg.Cookie = config.Cookie;
        }
    }

    private void InitializeCountries()
    {
        var countries = MapLazyAssets.Instance.GoddessPositions.Values
            .OrderBy(g => int.TryParse(g.Id, out var id) ? id : int.MaxValue)
            .GroupBy(g => g.Country)
            .Select(grp => grp.Key);
        CountryList.Clear();
        foreach (var country in countries)
        {
            if (!string.IsNullOrEmpty(country))
            {
                CountryList.Add(country);
            }
        }

        _selectedCountry = _tpConfig.ReviveStatueOfTheSevenCountry;
        UpdateAreas(SelectedCountry);
        _selectedArea = _tpConfig.ReviveStatueOfTheSevenArea;
        UpdateRevivePoint(SelectedCountry, SelectedArea);
    }

    private void UpdateAreas(string country)
    {
        Areas.Clear();
        SelectedArea = string.Empty;
        if (string.IsNullOrEmpty(country)) return;

        var areas = MapLazyAssets.Instance.GoddessPositions.Values
            .Where(g => g.Country == country)
            .OrderBy(g => int.TryParse(g.Id, out var id) ? id : int.MaxValue)
            .GroupBy(g => g.Level1Area)
            .Select(grp => grp.Key);
        foreach (var area in areas)
        {
            if (!string.IsNullOrEmpty(area))
            {
                Areas.Add(area);
            }
        }
    }

    // 当国家或区域改变时更新坐标
    private void UpdateRevivePoint(string country, string area)
    {
        if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(area)) return;

        var goddess = MapLazyAssets.Instance.GoddessPositions.Values
            .FirstOrDefault(g => g.Country == country && g.Level1Area == area);
        if (goddess == null) return;
        _tpConfig.ReviveStatueOfTheSevenCountry = country;
        _tpConfig.ReviveStatueOfTheSevenArea = area;
        _tpConfig.ReviveStatueOfTheSevenPointX = goddess.X;
        _tpConfig.ReviveStatueOfTheSevenPointY = goddess.Y;
        _tpConfig.ReviveStatueOfTheSeven = goddess;
    }

    [RelayCommand]
    public void OnRefreshMaskSettings()
    {
        WeakReferenceMessenger.Default.Send(
            new PropertyChangedMessage<object>(this, "RefreshSettings", new object(), "重新计算控件位置"));
    }

    private void OnOverlayStyleChanged()
    {
        MaskWindow.InstanceNullable()?.Refresh();
        OnRefreshMaskSettings();
    }

    [RelayCommand]
    private void OnResetOverlayStyle()
    {
        Config.MaskWindowConfig.ResetOverlayStyle();
        foreach (var group in OverlayStyleSettingGroups)
        {
            group.RefreshFromConfig();
        }

        OnOverlayStyleChanged();
    }

    [RelayCommand]
    private void OnOpenCustomHtmlMaskEditor()
    {
        CustomHtmlMaskEditorWindow.ShowEditor(_customHtmlMaskService, Config.MaskWindowConfig);
    }

    [RelayCommand]
    private void OnOpenCustomHtmlMaskFolder()
    {
        Directory.CreateDirectory(_customHtmlMaskService.DirectoryPath);
        Process.Start("explorer.exe", _customHtmlMaskService.DirectoryPath);
    }

    [RelayCommand]
    private void OnResetMaskOverlayLayout()
    {
        var c = Config.MaskWindowConfig;
        c.StatusListLeftRatio = 20.0 / 1920;
        c.StatusListTopRatio = 807.0 / 1080;
        c.StatusListWidthRatio = 477.0 / 1920;
        c.StatusListHeightRatio = 24.0 / 1080;

        c.LogTextBoxLeftRatio = 20.0 / 1920;
        c.LogTextBoxTopRatio = 832.0 / 1080;
        c.LogTextBoxWidthRatio = 477.0 / 1920;
        c.LogTextBoxHeightRatio = 188.0 / 1080;

        c.ResetOverlayMetricsLayout();

        OnRefreshMaskSettings();
    }

    [RelayCommand]
    private void OnSwitchMaskEnabled()
    {
        // if (Config.MaskWindowConfig.MaskEnabled)
        // {
        //     MaskWindow.Instance().Show();
        // }
        // else
        // {
        //     MaskWindow.Instance().Hide();
        // }
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public void OnSwitchTakenScreenshotEnabled()
    {
    }

    [RelayCommand]
    public void OnGoToFolder()
    {
        var path = Global.Absolute(@"log\screenshot\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    public void OnGoToLogFolder()
    {
        var path = Global.Absolute(@"log");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task ImportLocalScriptsRepoZip()
    {
        Directory.CreateDirectory(ScriptRepoUpdater.ReposPath);

        var dialog = new OpenFileDialog
        {
            Title = "选择脚本仓库压缩包",
            Filter = "Zip Files (*.zip)|*.zip",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await ScriptRepoUpdater.Instance.ImportLocalRepoZip(dialog.FileName);
                ThemedMessageBox.Information("脚本仓库离线包导入成功！");
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Error($"脚本仓库离线包导入失败：{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void OpenAboutWindow()
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.Owner = Application.Current.MainWindow;
        aboutWindow.ShowDialog();
    }

    [RelayCommand]
    private void OpenKeyBindingsWindow()
    {
        var keyBindingsWindow = KeyBindingsWindow.Instance;
        keyBindingsWindow.Owner = Application.Current.MainWindow;
        keyBindingsWindow.ShowDialog();
    }


    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption
        {
            Trigger = UpdateTrigger.Manual,
            Channel = UpdateChannel.Stable
        });
    }

    [RelayCommand]
    private async Task CheckUpdateAlphaAsync()
    {
        var result = await ThemedMessageBox.ShowAsync("测试版本非常不稳定！\n测试版本非常不稳定！\n测试版本非常不稳定！\n\n是否继续检查更新？", "警告", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }
        
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption
        {
            Trigger = UpdateTrigger.Manual,
            Channel = UpdateChannel.Alpha,
        });
    }

    // [RelayCommand]
    // private async Task GotoGithubActionAsync()
    // {
    //     await Launcher.LaunchUriAsync(
    //         new Uri("https://github.com/babalae/better-genshin-impact/actions/workflows/publish.yml"));
    // }

    [RelayCommand]
    private async Task OnGameLangSelectionChanged(KeyValuePair<string, string> type)
    {
        await App.ServiceProvider.GetRequiredService<OcrFactory>().Unload();
    }

    [RelayCommand]
    private async Task OnPaddleOcrModelConfigChanged(PaddleOcrModelConfig value)
    {
        Config.OtherConfig.OcrConfig.PaddleOcrModelConfig = value;
        await App.ServiceProvider.GetRequiredService<OcrFactory>().Unload();
    }
}

// 只服务于设置页：把遮罩显示开关、样式配置和辅助入口组织成折叠分组。
public sealed class OverlayStyleSettingGroup : ObservableObject
{
    private readonly MaskWindowConfig? _config;
    private readonly System.Reflection.PropertyInfo? _switchProperty;
    private readonly Action? _onSwitchChanged;

    public OverlayStyleSettingGroup(string name, string description, OverlayStyleSettingGroupKind kind, IEnumerable<OverlayStyleSettingItem> items,
        MaskWindowConfig? config = null, string? switchPropertyName = null, Action? onSwitchChanged = null)
    {
        Name = name;
        Description = description;
        Kind = kind;
        Items = new ObservableCollection<OverlayStyleSettingItem>(items);
        _config = config;
        _switchProperty = switchPropertyName == null
            ? null
            : typeof(MaskWindowConfig).GetProperty(switchPropertyName)
              ?? throw new ArgumentException($"Unknown mask switch property: {switchPropertyName}", nameof(switchPropertyName));
        _onSwitchChanged = onSwitchChanged;
    }

    public string Name { get; }

    public string Description { get; }

    public OverlayStyleSettingGroupKind Kind { get; }

    public bool IsLogGroup => Kind == OverlayStyleSettingGroupKind.Log;

    public bool IsMetricsGroup => Kind == OverlayStyleSettingGroupKind.Metrics;

    public bool IsCustomHtmlGroup => Kind == OverlayStyleSettingGroupKind.CustomHtml;

    public bool HasSwitch => _switchProperty != null;

    public bool SwitchValue
    {
        get => _config != null && _switchProperty?.GetValue(_config) is true;
        set
        {
            if (_config == null || _switchProperty == null || SwitchValue == value)
            {
                return;
            }

            _switchProperty.SetValue(_config, value);
            OnPropertyChanged();
            _onSwitchChanged?.Invoke();
        }
    }

    public ObservableCollection<OverlayStyleSettingItem> Items { get; }

    public void RefreshFromConfig()
    {
        foreach (var item in Items)
        {
            item.RefreshFromConfig();
        }

        OnPropertyChanged(nameof(SwitchValue));
    }

    public static ObservableCollection<OverlayStyleSettingGroup> Create(MaskWindowConfig config, Action onChanged)
    {
        return
        [
            new OverlayStyleSettingGroup("日志遮罩", "显示运行日志，方便查看当前任务执行到哪一步。", OverlayStyleSettingGroupKind.Log, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogPanelBackgroundColor), "日志区域背景色", "日志窗口底色。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogPanelBorderColor), "日志区域边框颜色", "日志窗口边框颜色。边框粗细为 0 时不会显示边框。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogPanelBorderThickness), "日志区域边框粗细", "日志窗口边框线宽。填 0 表示不显示边框。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogTextColor), "日志文字颜色", "日志内容的文字颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogFontSize), "日志文字大小", "日志内容字号。", onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.LogShadowEnabled), "显示日志阴影", "开启后文字和区域更容易从游戏背景中区分出来。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogShadowColor), "日志阴影颜色", "日志阴影颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogShadowOpacity), "日志阴影透明度", "日志阴影强度，0 表示没有阴影，1 表示最明显。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogShadowBlurRadius), "日志阴影模糊半径", "日志阴影的扩散范围，数值越大阴影越柔和。", onChanged),
            ], config, nameof(MaskWindowConfig.ShowLogBox), onChanged),
            new OverlayStyleSettingGroup("状态遮罩", "显示实时任务的启用状态，用来快速确认当前有哪些功能正在工作。", OverlayStyleSettingGroupKind.Status, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusPanelBackgroundColor), "任务状态栏背景色", "状态栏底色。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusPanelBorderColor), "任务状态栏边框颜色", "状态栏边框颜色。边框粗细为 0 时不会显示边框。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusPanelBorderThickness), "任务状态栏边框粗细", "状态栏边框线宽。填 0 表示不显示边框。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusDisabledTextColor), "未启用状态文字颜色", "任务未启用时图标和文字的颜色。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusEnabledTextColor), "已启用状态文字颜色", "任务启用时图标和文字的颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusFontSize), "状态文字大小", "状态栏字号。", onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.StatusShadowEnabled), "显示状态栏阴影", "开启后状态栏在复杂背景上更容易看清。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusShadowColor), "状态栏阴影颜色", "状态栏阴影颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusShadowOpacity), "状态栏阴影透明度", "状态栏阴影强度，0 表示没有阴影，1 表示最明显。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusShadowBlurRadius), "状态栏阴影模糊半径", "状态栏阴影的扩散范围，数值越大阴影越柔和。", onChanged),
            ], config, nameof(MaskWindowConfig.ShowStatus), onChanged),
            new OverlayStyleSettingGroup("性能指标遮罩", "显示帧率、处理耗时和硬件占用等运行指标。", OverlayStyleSettingGroupKind.Metrics, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsPanelBackgroundColor), "指标栏背景色", "指标栏底色。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsPanelBorderColor), "指标栏边框颜色", "指标栏边框颜色。边框粗细为 0 时不会显示边框。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsPanelBorderThickness), "指标栏边框粗细", "指标栏边框线宽。填 0 表示不显示边框。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsTextColor), "指标文字颜色", "指标文字颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsFontSize), "指标文字大小", "指标栏字号。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsLineHeight), "指标单行高度", "每一行指标占用的高度。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsItemWidth), "单个指标项宽度", "每个指标项占用的宽度。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsNameColumnWidth), "指标名称列宽度", "指标名称这一列的宽度。", onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.MetricsShadowEnabled), "显示指标栏阴影", "开启后指标栏在复杂背景上更容易看清。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsShadowColor), "指标栏阴影颜色", "指标栏阴影颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsShadowOpacity), "指标栏阴影透明度", "指标栏阴影强度，0 表示没有阴影，1 表示最明显。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsShadowBlurRadius), "指标栏阴影模糊半径", "指标栏阴影的扩散范围，数值越大阴影越柔和。", onChanged),
            ], config, nameof(MaskWindowConfig.ShowOverlayMetrics), onChanged),
            new OverlayStyleSettingGroup("方位遮罩", "在小地图周围显示东、南、西、北文字，辅助判断朝向。", OverlayStyleSettingGroupKind.Direction, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.DirectionTextColor), "方位文字颜色", "小地图周围方位文字的颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionFontSize), "方位文字大小", "小地图方位文字字号。", onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.DirectionShadowEnabled), "显示方位文字阴影", "开启后方位文字在复杂背景上更容易看清。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.DirectionShadowColor), "方位文字阴影颜色", "方位文字阴影颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionShadowOpacity), "方位文字阴影透明度", "方位文字阴影强度，0 表示没有阴影，1 表示最明显。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionShadowBlurRadius), "方位文字阴影模糊半径", "方位文字阴影的扩散范围，数值越大阴影越柔和。", onChanged),
            ], config, nameof(MaskWindowConfig.DirectionsEnabled), onChanged),
            new OverlayStyleSettingGroup("全局遮罩", "控制整个遮罩窗口的通用显示效果。", OverlayStyleSettingGroupKind.Global, CreateGlobalItems(config, onChanged)),
            new OverlayStyleSettingGroup("识别结果遮罩", "显示图像识别的框线和文字。", OverlayStyleSettingGroupKind.Recognition, [
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.RecognitionUseDrawableStyle), "统一识别框线颜色", "关闭时保留任务自己指定的颜色；开启后使用下面设置的统一颜色。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionRectStrokeColor), "识别矩形边框颜色", "开启统一颜色后，识别矩形框使用的颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionRectStrokeThickness), "识别矩形线宽", "开启统一颜色后，识别矩形框的线宽。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionLineStrokeColor), "识别线条颜色", "开启统一颜色后，识别线条使用的颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionLineStrokeThickness), "识别线条线宽", "开启统一颜色后，识别线条的线宽。", onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionTextColor), "识别文字颜色", "普通识别结果文字的颜色。", onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionTextFontSize), "识别文字大小", "普通识别结果文字的基础字号。", onChanged),
            ], config, nameof(MaskWindowConfig.DisplayRecognitionResultsOnMask), onChanged),
            new OverlayStyleSettingGroup("自定义 HTML 遮罩", "用 HTML 制作遮罩。", OverlayStyleSettingGroupKind.CustomHtml, [
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.CustomHtmlMaskClickThrough), "HTML 遮罩鼠标穿透", "开启后鼠标可穿透遮罩；关闭后 HTML 内容可以接收点击和输入。", onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.CustomHtmlMaskAutoReloadOnSave), "保存后自动刷新 HTML", "开启后保存 HTML 会立即刷新已经打开的自定义遮罩。", onChanged),
            ], config, nameof(MaskWindowConfig.CustomHtmlMaskEnabled), onChanged),
        ];
    }

    private static IEnumerable<OverlayStyleSettingItem> CreateGlobalItems(MaskWindowConfig config, Action onChanged)
    {
        yield return OverlayStyleSettingItem.Slider(config, nameof(MaskWindowConfig.TextOpacity), "遮罩文字透明度", "调整遮罩上所有文字的透明度，1 表示完全不透明，0 表示完全透明。", 0, 1, 0.1, onChanged);
        yield return OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.OverlayWindowBackgroundColor), "主遮罩窗口背景色", "遮罩窗口本身的背景色。", onChanged);

        if (WinePlatformAddon.IsRunningOnWine)
        {
            yield return OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.WineOverlayBackgroundColor), "Wine 兼容背景色", "仅在 Wine 环境下使用的兼容背景色。", onChanged);
        }
    }
}

public sealed class OverlayStyleSettingItem : ObservableObject
{
    private static readonly IReadOnlyList<OverlayStyleColorOption> BaseColorOptions =
    [
        new("透明", MaskWindowConfig.DefaultTransparentColor),
        new("几乎透明黑色", MaskWindowConfig.DefaultOverlayWindowBackgroundColor),
        new("半透明黑色", MaskWindowConfig.DefaultPanelBorderColor),
        new("浅灰色", "LightGray"),
        new("白色", "White"),
        new("黑色", "Black"),
        new("纯黑色", MaskWindowConfig.DefaultShadowColor),
        new("浅绿色", "LightGreen"),
        new("绿色", "Green"),
        new("红色", "Red"),
        new("橙色", "Orange"),
        new("黄色", "Yellow"),
        new("天蓝色", "DeepSkyBlue"),
        new("蓝色", "DodgerBlue"),
        new("紫色", "Purple"),
        new("粉色", "HotPink")
    ];

    private static readonly OverlayStyleColorOption WineColorOption =
        new("Wine 兼容半透明黑色", MaskWindowConfig.DefaultWineOverlayBackgroundColor);

    private readonly MaskWindowConfig _config;
    private readonly System.Reflection.PropertyInfo _property;
    private readonly Action _onChanged;

    private OverlayStyleSettingItem(MaskWindowConfig config, string propertyName, string displayName, string description, OverlayStyleSettingKind kind, Action onChanged)
    {
        _config = config;
        _property = typeof(MaskWindowConfig).GetProperty(propertyName)
                    ?? throw new ArgumentException($"Unknown mask style property: {propertyName}", nameof(propertyName));
        _onChanged = onChanged;
        DisplayName = displayName;
        Description = description;
        Kind = kind;
    }

    public string DisplayName { get; }

    public string Description { get; }

    public OverlayStyleSettingKind Kind { get; }

    public bool IsText => Kind == OverlayStyleSettingKind.Text;

    public bool IsColor => Kind == OverlayStyleSettingKind.Color;

    public bool IsNumber => Kind == OverlayStyleSettingKind.Number;

    public bool IsSlider => Kind == OverlayStyleSettingKind.Slider;

    public bool IsBool => Kind == OverlayStyleSettingKind.Bool;

    public double SliderMinimum { get; private init; }

    public double SliderMaximum { get; private init; }

    public double SliderTickFrequency { get; private init; }

    public string TextValue
    {
        get => FormatValue(_property.GetValue(_config));
        set
        {
            if (Kind == OverlayStyleSettingKind.Bool || Kind == OverlayStyleSettingKind.Color || Kind == OverlayStyleSettingKind.Slider || !TrySetValue(value))
            {
                return;
            }

            OnPropertyChanged();
            _onChanged();
        }
    }

    public IReadOnlyList<OverlayStyleColorOption> ColorOptions
    {
        get
        {
            var commonColorOptions = GetCommonColorOptions();
            var currentValue = FormatValue(_property.GetValue(_config));
            if (string.IsNullOrWhiteSpace(currentValue)
                || commonColorOptions.Any(option => string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase)))
            {
                return commonColorOptions;
            }

            return [.. commonColorOptions, new OverlayStyleColorOption($"当前自定义颜色（{currentValue}）", currentValue)];
        }
    }

    public string ColorValue
    {
        get => FormatValue(_property.GetValue(_config));
        set
        {
            if (Kind != OverlayStyleSettingKind.Color
                || string.IsNullOrWhiteSpace(value)
                || string.Equals(ColorValue, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _property.SetValue(_config, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColorOptions));
            _onChanged();
        }
    }

    public bool BoolValue
    {
        get => _property.GetValue(_config) is true;
        set
        {
            if (Kind != OverlayStyleSettingKind.Bool || BoolValue == value)
            {
                return;
            }

            _property.SetValue(_config, value);
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double SliderValue
    {
        get => _property.GetValue(_config) is double value ? value : 0;
        set
        {
            if (Kind != OverlayStyleSettingKind.Slider || Math.Abs(SliderValue - value) < 0.0001)
            {
                return;
            }

            _property.SetValue(_config, value);
            OnPropertyChanged();
            _onChanged();
        }
    }

    public static OverlayStyleSettingItem Text(MaskWindowConfig config, string propertyName, string displayName, string description, Action onChanged)
    {
        return new OverlayStyleSettingItem(config, propertyName, displayName, description, OverlayStyleSettingKind.Text, onChanged);
    }

    public static OverlayStyleSettingItem Color(MaskWindowConfig config, string propertyName, string displayName, string description, Action onChanged)
    {
        return new OverlayStyleSettingItem(config, propertyName, displayName, description, OverlayStyleSettingKind.Color, onChanged);
    }

    public static OverlayStyleSettingItem Number(MaskWindowConfig config, string propertyName, string displayName, string description, Action onChanged)
    {
        return new OverlayStyleSettingItem(config, propertyName, displayName, description, OverlayStyleSettingKind.Number, onChanged);
    }

    public static OverlayStyleSettingItem Slider(MaskWindowConfig config, string propertyName, string displayName, string description, double minimum, double maximum, double tickFrequency, Action onChanged)
    {
        return new OverlayStyleSettingItem(config, propertyName, displayName, description, OverlayStyleSettingKind.Slider, onChanged)
        {
            SliderMinimum = minimum,
            SliderMaximum = maximum,
            SliderTickFrequency = tickFrequency
        };
    }

    public static OverlayStyleSettingItem Bool(MaskWindowConfig config, string propertyName, string displayName, string description, Action onChanged)
    {
        return new OverlayStyleSettingItem(config, propertyName, displayName, description, OverlayStyleSettingKind.Bool, onChanged);
    }

    public void RefreshFromConfig()
    {
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(ColorOptions));
        OnPropertyChanged(nameof(ColorValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(SliderValue));
    }

    private bool TrySetValue(string value)
    {
        if (_property.PropertyType == typeof(string))
        {
            _property.SetValue(_config, value ?? string.Empty);
            return true;
        }

        if (_property.PropertyType == typeof(double)
            && (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out invariantValue)))
        {
            _property.SetValue(_config, invariantValue);
            return true;
        }

        return false;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            string text => text,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IReadOnlyList<OverlayStyleColorOption> GetCommonColorOptions()
    {
        return WinePlatformAddon.IsRunningOnWine ? [.. BaseColorOptions, WineColorOption] : BaseColorOptions;
    }
}

public sealed class OverlayStyleColorOption
{
    private static readonly BrushConverter BrushConverter = new();

    public OverlayStyleColorOption(string displayName, string value)
    {
        DisplayName = displayName;
        Value = value;
        SwatchBrush = CreateSwatchBrush(value);
    }

    public string DisplayName { get; }

    public string Value { get; }

    public Brush SwatchBrush { get; }

    private static Brush CreateSwatchBrush(string value)
    {
        try
        {
            if (BrushConverter.ConvertFromString(value) is Brush brush)
            {
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // 历史配置可能存在手动填写的无效颜色，色块失败时不影响下拉项显示。
        }

        return Brushes.Transparent;
    }
}

public enum OverlayStyleSettingGroupKind
{
    Global,
    Log,
    Status,
    Metrics,
    Direction,
    Recognition,
    CustomHtml
}

public enum OverlayStyleSettingKind
{
    Text,
    Color,
    Number,
    Slider,
    Bool
}

// 只服务于设置页：把固定指标枚举、显示文案和配置字典中的开关包装成复选框可双向绑定的对象。
public sealed class OverlayMetricSettingItem : ObservableObject
{
    private readonly MaskWindowConfig _config;
    private readonly Action _onChanged;
    private bool _isEnabled;

    public OverlayMetricSettingItem(MaskWindowConfig config, OverlayMetricItem item, Action onChanged)
    {
        _config = config;
        _onChanged = onChanged;
        Item = item;
        DisplayName = OverlayMetricItemDefaults.GetDisplayName(item);
        ToolTipText = OverlayMetricItemDefaults.GetToolTipText(item);
        _isEnabled = config.IsOverlayMetricEnabled(item);
    }

    public OverlayMetricItem Item { get; }

    public string DisplayName { get; }

    public string ToolTipText { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
            {
                return;
            }

            _config.SetOverlayMetricEnabled(Item, value);
            _onChanged();
        }
    }
}
