using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.System;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform.Wine;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.ExternalAccess;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Converters;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Plugins;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Win32;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class CommonSettingsPageViewModel : ViewModel
{
    private readonly IConfigService _configService;
    private readonly INavigationService _navigationService;

    private readonly NotificationService _notificationService;
    private readonly CustomHtmlMaskService _customHtmlMaskService;
    private readonly RecognitionTemplateEditorService _recognitionTemplateEditorService;
    private readonly I18nService _i18nService;

    /// <summary>
    /// 外部访问令牌生成与校验服务。
    /// </summary>
    private readonly ExternalAccessTokenService _externalAccessTokenService;

    /// <summary>
    /// 存储区域的统计，用于设置页的存储空间摘要。
    /// </summary>
    private readonly IStorageService _storageService;

    /// <summary>
    /// 按需创建存储空间窗口。
    /// </summary>
    private readonly Func<StorageManagementWindow> _storageManagementWindowFactory;

    /// <summary>
    /// 计入摘要的分组：程序管理的缓存、下载、网页数据和临时文件。
    /// </summary>
    private static readonly StorageGroup[] StorageSummaryGroups =
        [StorageGroup.Cache, StorageGroup.Download, StorageGroup.WebData, StorageGroup.Temp];

    /// <summary>
    /// 摘要上次统计的时间，短时间内反复进入页面时不重复统计。
    /// </summary>
    private DateTime _storageSummaryUpdatedAt = DateTime.MinValue;

    /// <summary>
    /// 是否正在统计摘要。
    /// </summary>
    private bool _isStorageSummaryRefreshing;

    /// <summary>
    /// “网络与代理”卡片的状态。
    /// </summary>
    public NetworkSettingsViewModel NetworkSettings { get; }

    /// <summary>
    /// 设置页“存储空间”卡片的说明文字。
    /// </summary>
    [ObservableProperty]
    private string _storageSummaryText = I18nService.Instance.Translate("正在计算占用…");

    private readonly TpConfig _tpConfig;

    private string _selectedArea = string.Empty;


    private string _selectedCountry = string.Empty;
    // 冒险家协会国家名是保存值（OtherConfig.AutoFetchDispatchAdventurersGuildCountry），仅展示时翻译
    [ObservableProperty] private List<string> _adventurersGuildCountry = [I18nService.Key("无"), I18nService.Key("枫丹"), I18nService.Key("稻妻"), I18nService.Key("璃月"), I18nService.Key("蒙德")];
    
    [ObservableProperty] private List<Tuple<TimeSpan, string>> _serverTimeZones =
    [
        // 只保存 Item1（时差），Item2 为显示名，界面通过 TBinding 翻译
        Tuple.Create(TimeSpan.FromHours(8), I18nService.Key("其他 UTC+08")),
        Tuple.Create(TimeSpan.FromHours(1), I18nService.Key("欧服 UTC+01")),
        Tuple.Create(TimeSpan.FromHours(-5), I18nService.Key("美服 UTC-05"))
    ];

    public IReadOnlyDictionary<ItemIconRecognitionMode, string> ItemIconRecognitionModes { get; } =
        new Dictionary<ItemIconRecognitionMode, string>
        {
            [ItemIconRecognitionMode.Item] = "ItemV2",
            [ItemIconRecognitionMode.GridIcon] = "GridIcon"
        };

    public CommonSettingsPageViewModel(IConfigService configService, INavigationService navigationService,
        NotificationService notificationService, CustomHtmlMaskService customHtmlMaskService,
        RecognitionTemplateEditorService recognitionTemplateEditorService, I18nService i18nService,
        ExternalAccessState externalAccessState, ExternalAccessTokenService externalAccessTokenService,
        IStorageService storageService, Func<StorageManagementWindow> storageManagementWindowFactory,
        NetworkSettingsViewModel networkSettings, PluggableRegistry pluggables)
    {
        NetworkSettings = networkSettings;
        // OCR 模型是可插拔能力：可选项为内置的 PP-OCRv6 和已启用的 OCR 插件，与插件页“功能选择”保持同步
        OcrModel = new PluggableItemViewModel(pluggables, pluggables.GetDescriptor<IOcrService>());
        _storageService = storageService;
        _storageManagementWindowFactory = storageManagementWindowFactory;
        Config = configService.Get();
        _configService = configService;
        _tpConfig = Config.TpConfig;
        Config.MaskWindowConfig.EnsureOverlayMetricItems();
        Config.MaskWindowConfig.MigrateLegacyOverlayMetricsLayout();
        _navigationService = navigationService;
        _notificationService = notificationService;
        _customHtmlMaskService = customHtmlMaskService;
        _recognitionTemplateEditorService = recognitionTemplateEditorService;
        _i18nService = i18nService;
        ExternalAccessState = externalAccessState;
        _externalAccessTokenService = externalAccessTokenService;
        Config.ExternalAccessConfig.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExternalAccessConfig.AccessToken))
            {
                OnPropertyChanged(nameof(MaskedExternalAccessToken));
            }
        };
        // 设置页需要可绑定对象，避免把 Dictionary<string, bool> 直接暴露给 XAML 并丢失固定枚举顺序。
        OverlayMetricItems = new ObservableCollection<OverlayMetricSettingItem>(
            OverlayMetricItemDefaults.AllItems.Select(item => new OverlayMetricSettingItem(Config.MaskWindowConfig, item, OnRefreshMaskSettings)));
        OverlayStyleSettingGroups = OverlayStyleSettingGroup.Create(Config.MaskWindowConfig, OnOverlayStyleChanged);
        InitializeCountries();
        InitializeMiyousheCookie();
        // 界面语言列表随插件启用、停用变化
        RebuildUiLanguageOptions();
        _i18nService.LanguagesChanged += (_, _) => RebuildUiLanguageOptions();
    }

    public AllConfig Config { get; set; }

    /// <summary>
    /// “OCR 配置”中的模型选择。选择保存在插件配置中，插件页的“功能选择”显示同一个选择。
    /// </summary>
    public PluggableItemViewModel OcrModel { get; }
    public ObservableCollection<OverlayMetricSettingItem> OverlayMetricItems { get; }
    public ObservableCollection<OverlayStyleSettingGroup> OverlayStyleSettingGroups { get; }
    public ObservableCollection<string> CountryList { get; } = new();
    public ObservableCollection<string> Areas { get; } = new();

    /// <summary>
    /// 外部访问服务的只读运行状态。
    /// </summary>
    public ExternalAccessState ExternalAccessState { get; }

    /// <summary>
    /// 设置页展示的脱敏访问令牌。
    /// </summary>
    public string MaskedExternalAccessToken
    {
        get
        {
            var token = Config.ExternalAccessConfig.AccessToken;
            if (string.IsNullOrEmpty(token))
            {
                return I18nService.Instance.Translate("尚未生成");
            }

            return token.Length <= 8
                ? "********"
                : $"{token[..4]}…{token[^4..]}";
        }
    }

    public ObservableCollection<string> MapPathingTypes { get; } = ["SIFT", "TemplateMatch"];

    /// <summary>
    /// 原神游戏语言的可选项。与界面语言无关，意大利语迁移为插件后游戏语言仍保留 it。
    /// 游戏内文字识别依赖对应语言的资源（GameTask 下各任务的 .resx）。
    /// </summary>
    [ObservableProperty] private FrozenDictionary<string, string> _gameLanguageDict =
        new[] { "zh-Hans", "zh-Hant", "en", "ja", "ru", "it", "pt-BR" }
            .ToFrozenDictionary(c => c, c => CultureInfoNameToKVPConverter.GetDisplayName(c));

    /// <summary>
    /// 界面语言的可选项：内置语言和已启用插件提供的语言；保存的语言不可用时末尾多一项“（不可用）”。
    /// </summary>
    public ObservableCollection<UiLanguageOption> UiLanguageOptions { get; } = [];

    /// <summary>
    /// 当前选中的界面语言。用户选择时保存配置并切换语言。
    /// </summary>
    [ObservableProperty] private UiLanguageOption? _selectedUiLanguage;

    /// <summary>
    /// 正在重建界面语言列表，期间的选中变化不写配置。
    /// </summary>
    private bool _rebuildingUiLanguages;

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

    [RelayCommand]
    public void OnQuestionButtonOnClick()
    {
        //            Owner = this,
        WebpageWindow cookieWin = new()
        {
            Title = I18nService.Instance.Translate("日志分析"),
            Width = 800,
            Height = 600,

            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        cookieWin.NavigateToHtml(TravelsDiaryDetailManager.generHtmlMessage());
        cookieWin.Show();
    }

    /// <summary>
    /// 将完整外部访问令牌复制到系统剪贴板。
    /// </summary>
    [RelayCommand]
    private void CopyExternalAccessToken()
    {
        var token = Config.ExternalAccessConfig.AccessToken;
        if (string.IsNullOrEmpty(token))
        {
            ThemedMessageBox.Warning(I18nService.Instance.Translate("访问令牌尚未生成。请先启用 HTTP API 或 MCP 后重启，或者点击“重新生成”。"));
            return;
        }

        Clipboard.SetDataObject(token);
        ThemedMessageBox.Information(I18nService.Instance.Translate("访问令牌已复制到剪贴板。"));
    }

    /// <summary>
    /// 重新生成访问令牌并立即使 HTTP API 与 MCP 使用新令牌。
    /// </summary>
    [RelayCommand]
    private void RegenerateExternalAccessToken()
    {
        _externalAccessTokenService.RegenerateToken();
        OnPropertyChanged(nameof(MaskedExternalAccessToken));
        ThemedMessageBox.Information(I18nService.Instance.Translate("访问令牌已重新生成，旧令牌立即失效。"));
    }

    private void InitializeMiyousheCookie()
    {
        OtherConfig.Miyoushe mcfg = Config.OtherConfig.MiyousheConfig;
        if (mcfg.Cookie == string.Empty &&
            mcfg.LogSyncCookie)
        {
            var config = LogParse.LoadConfig();
            mcfg.Cookie = config.Cookie;
        }
    }

    private void InitializeCountries()
    {
        var countries = MapLazyAssets.Get().GoddessPositions.Values
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

        // 区域名是保存值，界面展示时由 {i18n:TBinding} 翻译，Key 登记见 StatueOfTheSevenI18nKeys
        var areas = MapLazyAssets.Get().GoddessPositions.Values
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

        var goddess = MapLazyAssets.Get().GoddessPositions.Values
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
        // 绘制层样式由 MaskWindowViewModel 监听配置变化后自动重绘，这里只需刷新布局相关设置
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

    /// <summary>
    /// 打开插件页，安装或启用其他 OCR 模型插件。
    /// </summary>
    [RelayCommand]
    private void GoToPluginsPage()
    {
        _navigationService.Navigate(typeof(PluginsPage));
    }

    [RelayCommand]
    private async Task OpenRecognitionTemplateEditorFromImageAsync()
    {
        await _recognitionTemplateEditorService.OpenFromImageAsync();
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
    public void OnGoToRewardRecognitionFolder()
    {
        var path = Global.Absolute(@"log\RewardRecognition\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    /// <summary>
    /// 进入设置页时在后台刷新存储空间摘要
    /// </summary>
    public override void OnNavigatedTo()
    {
        _ = RefreshStorageSummaryAsync(force: false);
        // 插件可能刚在插件页启用或停用，按注册表的当前状态刷新 OCR 模型列表
        OcrModel.Refresh();
        // 切换语言后去掉之前的“（不可用）”项
        RebuildUiLanguageOptions();
    }

    /// <summary>
    /// 打开存储空间窗口，关闭后刷新摘要
    /// </summary>
    [RelayCommand]
    private async Task OpenStorageManagement()
    {
        var window = _storageManagementWindowFactory();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        await RefreshStorageSummaryAsync(force: true);
    }

    /// <summary>
    /// 统计缓存、下载、网页数据和临时文件的总占用；一分钟内重复进入页面时沿用上次结果
    /// </summary>
    private async Task RefreshStorageSummaryAsync(bool force)
    {
        if (_isStorageSummaryRefreshing || (!force && DateTime.UtcNow - _storageSummaryUpdatedAt < TimeSpan.FromMinutes(1)))
        {
            return;
        }

        _isStorageSummaryRefreshing = true;
        try
        {
            var areas = _storageService.GetAreas().Where(area => StorageSummaryGroups.Contains(area.Group)).ToList();
            var sizes = await Task.WhenAll(areas.Select(area => _storageService.MeasureAsync(area, CancellationToken.None)));
            StorageSummaryText = string.Format(I18nService.Instance.Translate("缓存、下载、网页数据和临时文件共占用 {0}"), ByteSizeFormatter.Format(sizes.Sum()));
            _storageSummaryUpdatedAt = DateTime.UtcNow;
        }
        catch (Exception)
        {
            StorageSummaryText = I18nService.Instance.Translate("查看和清理缓存、下载、网页数据与临时文件");
        }
        finally
        {
            _isStorageSummaryRefreshing = false;
        }
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
            Title = I18nService.Instance.Translate("选择脚本仓库压缩包"),
            Filter = "Zip Files (*.zip)|*.zip",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await ScriptRepoUpdater.Instance.ImportLocalRepoZip(dialog.FileName);
                ThemedMessageBox.Information(I18nService.Instance.Translate("脚本仓库离线包导入成功！"));
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Error(string.Format(I18nService.Instance.Translate("脚本仓库离线包导入失败：{0}"), ex.Message));
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
        var result = await ThemedMessageBox.ShowAsync(I18nService.Instance.Translate("测试版本非常不稳定！\n测试版本非常不稳定！\n测试版本非常不稳定！\n\n是否继续检查更新？"), I18nService.Instance.Translate("警告"), MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning);
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

    /// <summary>
    /// 用户选择界面语言时保存配置并切换。重建列表时的选中变化、清空列表时的 null 不处理。
    /// </summary>
    partial void OnSelectedUiLanguageChanged(UiLanguageOption? value)
    {
        if (_rebuildingUiLanguages || value is null
            || string.Equals(value.Code, Config.OtherConfig.UiCultureInfoName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Config.OtherConfig.UiCultureInfoName = value.Code;
        _i18nService.ChangeLanguage(value.Code);
        _configService.Save();
        _ = AskRestartAfterUiLanguageChangedAsync();
    }

    /// <summary>
    /// 切换界面语言后询问是否立即重启。
    /// </summary>
    private async Task AskRestartAfterUiLanguageChangedAsync()
    {
        // 大部分文字已即时切换；启动时生成的文字（日志、托盘、已创建的窗口等）需重启才会更新，询问是否立即重启
        var result = await ThemedMessageBox.QuestionAsync(
            I18nService.Instance.Translate("部分界面文字需要重启 BetterGI 后才会切换为新语言。\n是否立即重启？正在运行的任务会被中断。"),
            I18nService.Instance.Translate("切换语言"),
            MessageBoxButton.YesNo,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            SystemControl.RestartApplication([], graceful: true);
        }
    }

    /// <summary>
    /// 按多语言服务的当前语言列表重建界面语言下拉框，并重新选中保存的语言。
    /// 保存的语言不可用（提供它的插件未安装或未启用）时追加一项“（不可用）”，否则下拉框会显示空白。
    /// </summary>
    private void RebuildUiLanguageOptions()
    {
        _rebuildingUiLanguages = true;
        try
        {
            var saved = Config.OtherConfig.UiCultureInfoName;
            // 先清空选中项：选项是值相等的记录，重建后赋相同的值不会触发变化通知，下拉框会显示空白
            SelectedUiLanguage = null;
            UiLanguageOptions.Clear();
            foreach (var language in _i18nService.Languages)
            {
                UiLanguageOptions.Add(language);
            }

            var selected = UiLanguageOptions.FirstOrDefault(l => string.Equals(l.Code, saved, StringComparison.OrdinalIgnoreCase));
            if (selected is null && !string.IsNullOrWhiteSpace(saved))
            {
                selected = new UiLanguageOption(saved,
                    string.Format(I18nService.Instance.Translate("{0}（不可用）"), GetNativeLanguageName(saved)));
                UiLanguageOptions.Add(selected);
            }

            SelectedUiLanguage = selected;
        }
        finally
        {
            _rebuildingUiLanguages = false;
        }
    }

    /// <summary>
    /// 语言的自称，如 it → italiano；代码不是 .NET 认识的文化名时返回代码本身。
    /// </summary>
    private static string GetNativeLanguageName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    // 切换游戏语言不再重建 OCR：OCR 模型与游戏语言无关，由“OCR 配置”中的模型选择决定

    [RelayCommand]
    private void SelectCrosshairImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = I18nService.Instance.Translate("图片文件") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = I18nService.Instance.Translate("选择准星图片")
        };
        if (dialog.ShowDialog() == true)
        {
            Config.MaskWindowConfig.CrosshairImagePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void SelectMainBackgroundImage()
    {
        var dialog = new OpenFileDialog
        {
            // 注意：不声明 *.webp —— WPF BitmapImage 原生不支持 WebP 解码，声明了会导致用户选中后加载失败
            Filter = I18nService.Instance.Translate("图片文件") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = I18nService.Instance.Translate("选择主窗口背景图片")
        };
        if (dialog.ShowDialog() == true)
        {
            // 选图后弹出编辑器：可旋转/裁剪后保存副本使用，也可直接使用原图
            var editor = new ImageEditWindow(dialog.FileName)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (editor.ShowDialog() == true && !string.IsNullOrEmpty(editor.ResultImagePath))
            {
                // 主窗口 ViewModel 通过订阅配置变更事件实时刷新背景
                Config.CommonConfig.MainBackgroundImagePath = editor.ResultImagePath;
                Config.CommonConfig.MainBackgroundEnabled = true;
            }
        }
    }

    [RelayCommand]
    private void ClearMainBackgroundImage()
    {
        Config.CommonConfig.MainBackgroundImagePath = string.Empty;
        Config.CommonConfig.MainBackgroundEnabled = false;
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
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("全局遮罩配置"), I18nService.Instance.Translate("控制整个遮罩窗口的通用显示效果。"), OverlayStyleSettingGroupKind.Global, CreateGlobalItems(config, onChanged)),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("日志遮罩"), I18nService.Instance.Translate("显示运行日志，方便查看当前任务执行到哪一步。"), OverlayStyleSettingGroupKind.Log, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogPanelBackgroundColor), I18nService.Instance.Translate("日志区域背景色"), I18nService.Instance.Translate("日志窗口底色。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogPanelBorderColor), I18nService.Instance.Translate("日志区域边框颜色"), I18nService.Instance.Translate("日志窗口边框颜色。边框粗细为 0 时不会显示边框。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogPanelBorderThickness), I18nService.Instance.Translate("日志区域边框粗细"), I18nService.Instance.Translate("日志窗口边框线宽。填 0 表示不显示边框。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogTextColor), I18nService.Instance.Translate("日志文字颜色"), I18nService.Instance.Translate("日志内容的文字颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogFontSize), I18nService.Instance.Translate("日志文字大小"), I18nService.Instance.Translate("日志内容字号。"), onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.LogShadowEnabled), I18nService.Instance.Translate("显示日志阴影"), I18nService.Instance.Translate("开启后文字和区域更容易从游戏背景中区分出来。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.LogShadowColor), I18nService.Instance.Translate("日志阴影颜色"), I18nService.Instance.Translate("日志阴影颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogShadowOpacity), I18nService.Instance.Translate("日志阴影透明度"), I18nService.Instance.Translate("日志阴影强度，0 表示没有阴影，1 表示最明显。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.LogShadowBlurRadius), I18nService.Instance.Translate("日志阴影模糊半径"), I18nService.Instance.Translate("日志阴影的扩散范围，数值越大阴影越柔和。"), onChanged),
            ], config, nameof(MaskWindowConfig.ShowLogBox), onChanged),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("状态遮罩"), I18nService.Instance.Translate("显示实时任务的启用状态，用来快速确认当前有哪些功能正在工作。"), OverlayStyleSettingGroupKind.Status, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusPanelBackgroundColor), I18nService.Instance.Translate("任务状态栏背景色"), I18nService.Instance.Translate("状态栏底色。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusPanelBorderColor), I18nService.Instance.Translate("任务状态栏边框颜色"), I18nService.Instance.Translate("状态栏边框颜色。边框粗细为 0 时不会显示边框。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusPanelBorderThickness), I18nService.Instance.Translate("任务状态栏边框粗细"), I18nService.Instance.Translate("状态栏边框线宽。填 0 表示不显示边框。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusDisabledTextColor), I18nService.Instance.Translate("未启用状态文字颜色"), I18nService.Instance.Translate("任务未启用时图标和文字的颜色。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusEnabledTextColor), I18nService.Instance.Translate("已启用状态文字颜色"), I18nService.Instance.Translate("任务启用时图标和文字的颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusFontSize), I18nService.Instance.Translate("状态文字大小"), I18nService.Instance.Translate("状态栏字号。"), onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.StatusShadowEnabled), I18nService.Instance.Translate("显示状态栏阴影"), I18nService.Instance.Translate("开启后状态栏在复杂背景上更容易看清。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.StatusShadowColor), I18nService.Instance.Translate("状态栏阴影颜色"), I18nService.Instance.Translate("状态栏阴影颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusShadowOpacity), I18nService.Instance.Translate("状态栏阴影透明度"), I18nService.Instance.Translate("状态栏阴影强度，0 表示没有阴影，1 表示最明显。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.StatusShadowBlurRadius), I18nService.Instance.Translate("状态栏阴影模糊半径"), I18nService.Instance.Translate("状态栏阴影的扩散范围，数值越大阴影越柔和。"), onChanged),
            ], config, nameof(MaskWindowConfig.ShowStatus), onChanged),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("性能指标遮罩"), I18nService.Instance.Translate("显示帧率、处理耗时和硬件占用等运行指标。"), OverlayStyleSettingGroupKind.Metrics, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsPanelBackgroundColor), I18nService.Instance.Translate("指标栏背景色"), I18nService.Instance.Translate("指标栏底色。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsPanelBorderColor), I18nService.Instance.Translate("指标栏边框颜色"), I18nService.Instance.Translate("指标栏边框颜色。边框粗细为 0 时不会显示边框。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsPanelBorderThickness), I18nService.Instance.Translate("指标栏边框粗细"), I18nService.Instance.Translate("指标栏边框线宽。填 0 表示不显示边框。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsTextColor), I18nService.Instance.Translate("指标文字颜色"), I18nService.Instance.Translate("指标文字颜色。"), onChanged),
                OverlayStyleSettingItem.Slider(config, nameof(MaskWindowConfig.MetricsFontScale), I18nService.Instance.Translate("指标栏缩放率"), I18nService.Instance.Translate("启用遮罩缩放后，在指标字号和布局尺寸的基础上叠加缩放，范围为 0.5-3.0 倍。"), MaskWindowConfig.MinLogFontScale, MaskWindowConfig.MaxLogFontScale, 0.1, onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsFontSize), I18nService.Instance.Translate("指标文字大小"), I18nService.Instance.Translate("指标栏字号。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsLineHeight), I18nService.Instance.Translate("指标单行高度"), I18nService.Instance.Translate("每一行指标占用的高度。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsItemWidth), I18nService.Instance.Translate("单个指标项宽度"), I18nService.Instance.Translate("每个指标项占用的宽度。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsNameColumnWidth), I18nService.Instance.Translate("指标名称列宽度"), I18nService.Instance.Translate("指标名称这一列的宽度。"), onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.MetricsShadowEnabled), I18nService.Instance.Translate("显示指标栏阴影"), I18nService.Instance.Translate("开启后指标栏在复杂背景上更容易看清。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.MetricsShadowColor), I18nService.Instance.Translate("指标栏阴影颜色"), I18nService.Instance.Translate("指标栏阴影颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsShadowOpacity), I18nService.Instance.Translate("指标栏阴影透明度"), I18nService.Instance.Translate("指标栏阴影强度，0 表示没有阴影，1 表示最明显。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.MetricsShadowBlurRadius), I18nService.Instance.Translate("指标栏阴影模糊半径"), I18nService.Instance.Translate("指标栏阴影的扩散范围，数值越大阴影越柔和。"), onChanged),
            ], config, nameof(MaskWindowConfig.ShowOverlayMetrics), onChanged),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("方位遮罩"), I18nService.Instance.Translate("在小地图周围显示东、南、西、北文字，辅助判断朝向。"), OverlayStyleSettingGroupKind.Direction, [
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.DirectionTextColor), I18nService.Instance.Translate("方位文字颜色"), I18nService.Instance.Translate("小地图周围方位文字的颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionFontSize), I18nService.Instance.Translate("方位文字大小"), I18nService.Instance.Translate("小地图方位文字字号。"), onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.DirectionShadowEnabled), I18nService.Instance.Translate("显示方位文字阴影"), I18nService.Instance.Translate("开启后方位文字在复杂背景上更容易看清。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.DirectionShadowColor), I18nService.Instance.Translate("方位文字阴影颜色"), I18nService.Instance.Translate("方位文字阴影颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionShadowOpacity), I18nService.Instance.Translate("方位文字阴影透明度"), I18nService.Instance.Translate("方位文字阴影强度，0 表示没有阴影，1 表示最明显。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.DirectionShadowBlurRadius), I18nService.Instance.Translate("方位文字阴影模糊半径"), I18nService.Instance.Translate("方位文字阴影的扩散范围，数值越大阴影越柔和。"), onChanged),
            ], config, nameof(MaskWindowConfig.DirectionsEnabled), onChanged),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("识别结果遮罩"), I18nService.Instance.Translate("显示图像识别的框线和文字。"), OverlayStyleSettingGroupKind.Recognition, [
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.RecognitionUseDrawableStyle), I18nService.Instance.Translate("统一识别框线颜色"), I18nService.Instance.Translate("关闭时保留任务自己指定的颜色；开启后使用下面设置的统一颜色。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionRectStrokeColor), I18nService.Instance.Translate("识别矩形边框颜色"), I18nService.Instance.Translate("开启统一颜色后，识别矩形框使用的颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionRectStrokeThickness), I18nService.Instance.Translate("识别矩形线宽"), I18nService.Instance.Translate("开启统一颜色后，识别矩形框的线宽。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionLineStrokeColor), I18nService.Instance.Translate("识别线条颜色"), I18nService.Instance.Translate("开启统一颜色后，识别线条使用的颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionLineStrokeThickness), I18nService.Instance.Translate("识别线条线宽"), I18nService.Instance.Translate("开启统一颜色后，识别线条的线宽。"), onChanged),
                OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.RecognitionTextColor), I18nService.Instance.Translate("识别文字颜色"), I18nService.Instance.Translate("普通识别结果文字的颜色。"), onChanged),
                OverlayStyleSettingItem.Number(config, nameof(MaskWindowConfig.RecognitionTextFontSize), I18nService.Instance.Translate("识别文字大小"), I18nService.Instance.Translate("普通识别结果文字的基础字号。"), onChanged),
            ], config, nameof(MaskWindowConfig.DisplayRecognitionResultsOnMask), onChanged),
            new OverlayStyleSettingGroup(I18nService.Instance.Translate("自定义 HTML 遮罩"), I18nService.Instance.Translate("用 HTML 制作遮罩。"), OverlayStyleSettingGroupKind.CustomHtml, [
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.CustomHtmlMaskClickThrough), I18nService.Instance.Translate("HTML 遮罩鼠标穿透"), I18nService.Instance.Translate("开启后鼠标可穿透遮罩；关闭后 HTML 内容可以接收点击和输入。"), onChanged),
                OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.CustomHtmlMaskAutoReloadOnSave), I18nService.Instance.Translate("保存后自动刷新 HTML"), I18nService.Instance.Translate("开启后保存 HTML 会立即刷新已经打开的自定义遮罩。"), onChanged),
            ], config, nameof(MaskWindowConfig.CustomHtmlMaskEnabled), onChanged),
        ];
    }

    private static IEnumerable<OverlayStyleSettingItem> CreateGlobalItems(MaskWindowConfig config, Action onChanged)
    {
        yield return OverlayStyleSettingItem.Bool(config, nameof(MaskWindowConfig.OverlayScalingEnabled), I18nService.Instance.Translate("启用遮罩字体缩放"), I18nService.Instance.Translate("开启后根据游戏分辨率和系统 DPI 缩放日志、状态、FPS 与指标栏的字体大小；关闭后直接使用各项基础尺寸。"), onChanged);
        yield return OverlayStyleSettingItem.Slider(config, nameof(MaskWindowConfig.LogFontScale), I18nService.Instance.Translate("遮罩字体缩放率"), I18nService.Instance.Translate("启用遮罩字体缩放后，在日志、状态和 FPS 基础字号上叠加缩放，范围为 0.5-3.0 倍。"), MaskWindowConfig.MinLogFontScale, MaskWindowConfig.MaxLogFontScale, 0.1, onChanged);
        yield return OverlayStyleSettingItem.Slider(config, nameof(MaskWindowConfig.TextOpacity), I18nService.Instance.Translate("遮罩文字透明度"), I18nService.Instance.Translate("调整遮罩上所有文字的透明度，1 表示完全不透明，0 表示完全透明。"), 0, 1, 0.1, onChanged);
        yield return OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.OverlayWindowBackgroundColor), I18nService.Instance.Translate("主遮罩窗口背景色"), I18nService.Instance.Translate("遮罩窗口本身的背景色。"), onChanged);

        if (WinePlatformAddon.IsRunningOnWine)
        {
            yield return OverlayStyleSettingItem.Color(config, nameof(MaskWindowConfig.WineOverlayBackgroundColor), I18nService.Instance.Translate("Wine 兼容背景色"), I18nService.Instance.Translate("仅在 Wine 环境下使用的兼容背景色。"), onChanged);
        }
    }
}

public sealed class OverlayStyleSettingItem : ObservableObject
{
    private static readonly IReadOnlyList<OverlayStyleColorOption> BaseColorOptions =
    [
        new(I18nService.Key("透明"), MaskWindowConfig.DefaultTransparentColor),
        new(I18nService.Key("几乎透明黑色"), MaskWindowConfig.DefaultOverlayWindowBackgroundColor),
        new(I18nService.Key("半透明黑色"), MaskWindowConfig.DefaultPanelBorderColor),
        new(I18nService.Key("浅灰色"), "LightGray"),
        new(I18nService.Key("白色"), "White"),
        new(I18nService.Key("黑色"), "Black"),
        new(I18nService.Key("纯黑色"), MaskWindowConfig.DefaultShadowColor),
        new(I18nService.Key("浅绿色"), "LightGreen"),
        new(I18nService.Key("绿色"), "Green"),
        new(I18nService.Key("红色"), "Red"),
        new(I18nService.Key("橙色"), "Orange"),
        new(I18nService.Key("黄色"), "Yellow"),
        new(I18nService.Key("天蓝色"), "DeepSkyBlue"),
        new(I18nService.Key("蓝色"), "DodgerBlue"),
        new(I18nService.Key("紫色"), "Purple"),
        new(I18nService.Key("粉色"), "HotPink")
    ];

    private static readonly OverlayStyleColorOption WineColorOption =
        new(I18nService.Key("Wine 兼容半透明黑色"), MaskWindowConfig.DefaultWineOverlayBackgroundColor);

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

            return [.. commonColorOptions, new OverlayStyleColorOption(string.Format(I18nService.Instance.Translate("当前自定义颜色（{0}）"), currentValue), currentValue)];
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
        _displayName = displayName;
        Value = value;
        SwatchBrush = CreateSwatchBrush(value);
    }

    /// <summary>
    /// 颜色名称（i18n Key 或已翻译的文字）。
    /// </summary>
    private readonly string _displayName;

    /// <summary>
    /// 按界面语言显示的颜色名称；预设颜色列表是静态的，因此在取值时翻译。
    /// </summary>
    public string DisplayName => I18nService.Instance.Translate(_displayName);

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
        DisplayName = I18nService.Instance.Translate(OverlayMetricItemDefaults.GetDisplayName(item));
        ToolTipText = I18nService.Instance.Translate(OverlayMetricItemDefaults.GetToolTipText(item));
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
