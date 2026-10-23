using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.System;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TaskSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    /// <summary>
    /// 自动首领讨伐的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoBossTargetItems { get; }

    /// <summary>
    /// 自动秘境的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoDomainTargetItems { get; }

    /// <summary>
    /// 自动地脉花的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoLeyLineTargetItems { get; }

    private readonly INavigationService _navigationService;
    /// <summary>
    /// 把停止按钮产生的外部停止信号转发给当前运行。
    /// </summary>
    private readonly TaskStopService _taskStopService;

    /// <summary>
    /// 配置读写服务，用于在远程实例启动前把当前页面修改落盘。
    /// </summary>
    private readonly IConfigService _configService;

    /// <summary>
    /// 独立任务注册表，提供可单独运行的任务清单、显示名和参数序列化。
    /// </summary>
    private readonly SoloTaskRegistry _soloTaskRegistry;

    /// <summary>
    /// 独立任务按钮的状态刷新与启动、停止、多实例管理流程。
    /// </summary>
    private readonly SoloTaskButtonController _soloTaskButtonController;

    /// <summary>
    /// 独立任务按钮共用的展示状态，键为任务标识（如 auto_wood）。
    /// </summary>
    public IReadOnlyDictionary<string, SoloTaskButtonState> SoloTaskStates { get; }

    private CancellationTokenSource? _cts;
    private static readonly object _locker = new();

    // [ObservableProperty]
    // private string[] _strategyList;

    [ObservableProperty]
    private int _autoWoodRoundNum;

    [ObservableProperty]
    private int _autoWoodDailyMaxCount = 2000;

    //[ObservableProperty]
    //private string[] _combatStrategyList;

    [ObservableProperty]
    private int _autoDomainRoundNum;

    [ObservableProperty]
    private int _autoStygianOnslaughtRoundNum;

    [ObservableProperty]
    private string _switchAutoTrackButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackPathButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoComboRunButtonText = "测试运行";

    private bool _autoComboRunRunning;

    private bool _autoComboRunPaused;

    /// <summary>“拉取模型列表”得到的候选模型名；不写入配置，重启后需要重新拉取</summary>
    [ObservableProperty]
    private ObservableCollection<string> _autoComboModelNameList = [];

    /// <summary>正在拉取模型列表，用于禁用按钮避免重复请求</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchAutoComboModelListCommand))]
    private bool _fetchingAutoComboModelList;

    [ObservableProperty]
    private List<string> _domainNameList;

    public static List<string> ArtifactSalvageStarList = ["4", "3", "2", "1"];

    public static List<int> BossNumList = [1, 2, 3];

    public static List<string> AutoBossNameList = [.. AutoBossData.SupportedBossNames];

    public static List<string> AvatarIndexList = ["", "1", "2", "3", "4"];
    public static List<string> CombatAvatarNameList = [.. AvatarProfiles.GetProfileNames()];
    public static List<string> LeyLineOutcropTypeList = ["启示之花", "藏金之花"];
    public static List<string> LeyLineOutcropCountryList = ["蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱", "至冬"];
    public static List<string> LeyLineOutcropTypeListWithEmpty = ["", .. LeyLineOutcropTypeList];
    public static List<string> LeyLineOutcropCountryListWithEmpty = ["", .. LeyLineOutcropCountryList];

    [ObservableProperty]
    private List<string> _autoMusicLevelList = ["传说", "大师", "困难", "普通", "所有"];

    [ObservableProperty]
    private AutoFightViewModel? _autoFightViewModel;

    [ObservableProperty]
    private OneDragonFlowViewModel? _oneDragonFlowViewModel;

    [ObservableProperty]
    private bool _scanDropsAfterRewardEnabledUi;

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _fishingTimePolicyDict = Enum.GetValues(typeof(FishingTimePolicy))
        .Cast<FishingTimePolicy>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    private bool saveScreenshotOnKeyTick;
    private bool _suppressScanDropsAfterRewardPrompt;
    private int _scanDropsAfterRewardPromptVersion;
    public bool SaveScreenshotOnKeyTick
    {
        get => Config.CommonConfig.ScreenshotEnabled && saveScreenshotOnKeyTick;
        set => SetProperty(ref saveScreenshotOnKeyTick, value);
    }

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _recognitionFailurePolicyDict = Enum.GetValues(typeof(RecognitionFailurePolicy))
        .Cast<RecognitionFailurePolicy>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _gridNameDict = Enum.GetValues(typeof(GridScreenName))
        .Cast<GridScreenName>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    /// <summary>数量 OCR 测试当前选择的分类或当前一页模式。</summary>
    [ObservableProperty]
    private InventoryCountComparisonTarget _inventoryCountComparisonTarget = InventoryCountComparisonTarget.CharacterDevelopmentItems;

    /// <summary>数量 OCR 测试下拉框显示的四个目标选项。</summary>
    public FrozenDictionary<Enum, string> InventoryCountComparisonTargetDict { get; } = Enum
        .GetValues<InventoryCountComparisonTarget>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    /// <summary>
    /// 任务设置页面 ViewModel
    /// </summary>
    /// <param name="configService">配置服务</param>
    /// <param name="navigationService">导航服务</param>
    /// <param name="taskStopService">任务停止服务</param>
    /// <param name="oneDragonFlowViewModel">一条龙页面 ViewModel（DI 单例注入，避免独立 new 导致状态分裂）</param>
    /// <param name="instanceService">多实例连接与独立任务运行服务</param>
    /// <param name="soloTaskRegistry">独立任务注册表</param>
    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService,
        TaskStopService taskStopService,
        OneDragonFlowViewModel oneDragonFlowViewModel, InstanceService instanceService,
        SoloTaskRegistry soloTaskRegistry)
    {
        Config = configService.Get();
        _configService = configService;
        _navigationService = navigationService;
        _taskStopService = taskStopService;
        _soloTaskRegistry = soloTaskRegistry;
        // 页面按钮按任务标识绑定，清单来自注册表中可单独运行的任务
        SoloTaskStates = soloTaskRegistry.GetByScope(SoloTaskScopes.Standalone)
            .ToDictionary(descriptor => descriptor.TaskId, descriptor => new SoloTaskButtonState(descriptor.TaskId));
        _soloTaskButtonController = new SoloTaskButtonController(instanceService, soloTaskRegistry, configService,
            SoloTaskStates.Values);
        NormalizeLeyLineOutcropType();

        // 材料目标直接读写各自任务的配置集合，候选物品随首领、秘境与地脉花类型变化
        AutoBossTargetItems = new TargetItemListEditorViewModel(Config.AutoBossConfig.TargetItems);
        AutoDomainTargetItems = new TargetItemListEditorViewModel(Config.AutoDomainConfig.TargetItems);
        AutoLeyLineTargetItems = new TargetItemListEditorViewModel(Config.AutoLeyLineOutcropConfig.TargetItems);
        RefreshAutoBossTargetItems();
        RefreshAutoDomainTargetItems();
        RefreshAutoLeyLineTargetItems();
        Config.AutoBossConfig.PropertyChanged += OnAutoBossConfigPropertyChanged;
        Config.AutoDomainConfig.PropertyChanged += OnAutoDomainConfigPropertyChanged;
        Config.AutoLeyLineOutcropConfig.PropertyChanged += OnAutoLeyLineOutcropConfigPropertyChanged;

        _scanDropsAfterRewardEnabledUi = Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled;

        //_strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));

        //_combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];

        _domainNameList = ["", AutoDomainTask.DevelopmentGuideOption, .. MapLazyAssets.Get().DomainNameList];
        _autoFightViewModel = new AutoFightViewModel(Config);
        _oneDragonFlowViewModel = oneDragonFlowViewModel;
    }

    partial void OnScanDropsAfterRewardEnabledUiChanged(bool value)
    {
        if (_suppressScanDropsAfterRewardPrompt)
        {
            return;
        }

        if (!value)
        {
            Interlocked.Increment(ref _scanDropsAfterRewardPromptVersion);
            Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled = false;
            return;
        }

        var version = Interlocked.Increment(ref _scanDropsAfterRewardPromptVersion);
        _ = ConfirmScanDropsAfterRewardRiskAsync(version);
    }

    private async Task ConfirmScanDropsAfterRewardRiskAsync(int version)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "风险提示",
            Content = "开启“领取奖励后扫描掉落物光柱”后，角色会在领奖完成后主动移动拾取。部分地脉花点位或特定配队下，可能因为移动范围较大而卡住。\n\n如果你愿意接受这个风险，请继续开启；否则将保持关闭。",
            PrimaryButtonText = "接受风险并开启",
            CloseButtonText = "不接受，保持关闭",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        WindowHelper.CenterOnVisibleOwner(messageBox);

        var result = await messageBox.ShowDialogAsync();
        var accepted = result == Wpf.Ui.Controls.MessageBoxResult.Primary;

        if (version != _scanDropsAfterRewardPromptVersion)
        {
            return;
        }

        _suppressScanDropsAfterRewardPrompt = true;
        try
        {
            ScanDropsAfterRewardEnabledUi = accepted;
            Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled = accepted;
        }
        finally
        {
            _suppressScanDropsAfterRewardPrompt = false;
        }
    }

    private void NormalizeLeyLineOutcropType()
    {
        var type = Config.AutoLeyLineOutcropConfig.LeyLineOutcropType;
        if (type == "蓝花（经验书）")
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = "启示之花";
            return;
        }

        if (type == "黄花（摩拉）")
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = "藏金之花";
            return;
        }

        if (string.IsNullOrWhiteSpace(type) || !LeyLineOutcropTypeList.Contains(type))
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = LeyLineOutcropTypeList[0];
        }
    }

    /// <summary>
    /// 首领变化后刷新自动首领讨伐的材料目标候选物品。
    /// </summary>
    private void OnAutoBossConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoBossConfig.BossName))
        {
            UIDispatcherHelper.Invoke(RefreshAutoBossTargetItems);
        }
    }

    /// <summary>
    /// 秘境变化后刷新自动秘境的材料目标候选物品。
    /// </summary>
    private void OnAutoDomainConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoDomainConfig.DomainName))
        {
            UIDispatcherHelper.Invoke(RefreshAutoDomainTargetItems);
        }
    }

    /// <summary>
    /// 地脉花类型变化后刷新自动地脉花的材料目标候选物品。
    /// </summary>
    private void OnAutoLeyLineOutcropConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoLeyLineOutcropConfig.LeyLineOutcropType))
        {
            UIDispatcherHelper.Invoke(RefreshAutoLeyLineTargetItems);
        }
    }

    /// <summary>
    /// 按当前首领刷新自动首领讨伐的材料目标候选物品。
    /// </summary>
    private void RefreshAutoBossTargetItems()
    {
        AutoBossTargetItems.ApplyChoices(BuildBossTargetItemChoices(Config.AutoBossConfig.BossName));
    }

    /// <summary>
    /// 按当前秘境刷新自动秘境的材料目标候选物品。
    /// </summary>
    private void RefreshAutoDomainTargetItems()
    {
        AutoDomainTargetItems.ApplyChoices(BuildDomainTargetItemChoices(Config.AutoDomainConfig.DomainName));
    }

    /// <summary>
    /// 按当前地脉花类型刷新自动地脉花的材料目标候选物品。
    /// </summary>
    private void RefreshAutoLeyLineTargetItems()
    {
        AutoLeyLineTargetItems.ApplyChoices(BuildLeyLineTargetItemChoices(Config.AutoLeyLineOutcropConfig.LeyLineOutcropType));
    }

    /// <summary>
    /// 构造当前首领可掉落的材料目标：首领材料与各元素宝石。
    /// </summary>
    /// <param name="bossName">首领名称。</param>
    /// <returns>可选的奖励物品；没有奖励数据时为空。</returns>
    private static IReadOnlyList<TargetItemOption> BuildBossTargetItemChoices(string bossName)
    {
        if (!TargetItemCatalog.Instance.TryGetBossProfile(bossName, out var profile))
        {
            return [];
        }

        var choices = new List<TargetItemOption>
        {
            new(profile.BossMaterial, $"首领材料｜{profile.BossMaterial}")
        };
        foreach (var family in profile.GemFamilies)
        {
            foreach (var item in family.Items)
            {
                choices.Add(new TargetItemOption(item.ItemName, item.ItemName));
            }
        }

        return choices;
    }

    /// <summary>
    /// 构造当前秘境可掉落的材料目标。
    /// </summary>
    /// <param name="domainName">秘境名称。</param>
    /// <returns>可选的奖励物品；圣遗物秘境、提升指南或未收录秘境为空。</returns>
    private static IReadOnlyList<TargetItemOption> BuildDomainTargetItemChoices(string domainName)
    {
        TargetItemCatalog.Instance.TryGetDomainItems(domainName, out var items);
        return items;
    }

    /// <summary>
    /// 构造当前地脉花类型可获得的材料目标。
    /// </summary>
    /// <param name="leyLineType">地脉花类型。</param>
    /// <returns>藏金之花为摩拉，启示之花为三档经验书；其他类型为空。</returns>
    private static IReadOnlyList<TargetItemOption> BuildLeyLineTargetItemChoices(string leyLineType)
    {
        var catalog = TargetItemCatalog.Instance;
        if (leyLineType == "藏金之花")
        {
            return [new TargetItemOption(catalog.LeylineMoraItemName, catalog.LeylineMoraItemName)];
        }

        if (leyLineType == "启示之花")
        {
            return catalog.LeylineExpItems.Select(x => new TargetItemOption(x.Name, x.Name)).ToList();
        }

        return [];
    }


    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel?.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    /// <summary>
    /// 统一处理独立任务按钮和任务专用快捷键发出的启动、停止或管理请求。
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public Task ManageSoloTask(string taskId)
    {
        return _soloTaskButtonController.ManageAsync(taskId, () => BuildSoloTaskDefinitionAsync(taskId));
    }

    /// <summary>
    /// 按页面当前配置生成任务的完整参数并序列化为启动定义；任务内部用到的其余配置仍由目标实例读取共享配置文件。
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <returns>启动定义；校验失败或用户取消输入时返回 null</returns>
    private Task<SoloTaskStartDefinition?> BuildSoloTaskDefinitionAsync(string taskId)
    {
        var param = CreateSoloTaskParam(taskId);
        return Task.FromResult(param is null ? null : _soloTaskButtonController.CreateDefinition(taskId, param));
    }

    /// <summary>
    /// 按独立任务页面的配置与临时输入创建任务参数。
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <returns>参数对象；策略未配置或用户取消输入时返回 null</returns>
    private object? CreateSoloTaskParam(string taskId)
    {
        switch (taskId)
        {
            case AutoGeniusInvokationTask.TaskId:
                return GetTcgStrategy(out var strategyContent) ? null : new GeniusInvokationTaskParam(strategyContent);
            case AutoWoodTask.TaskId:
                return new WoodTaskParam(AutoWoodRoundNum, AutoWoodDailyMaxCount);
            case AutoFightEntryTask.TaskId:
                return GetFightStrategy(out var fightStrategyPath)
                    ? null
                    : new AutoFightParam(fightStrategyPath, Config.AutoFightConfig);
            case AutoDomainTask.TaskId:
                return GetFightStrategy(out var domainStrategyPath)
                    ? null
                    : new AutoDomainParam(AutoDomainRoundNum, domainStrategyPath);
            case AutoBossTask.TaskId:
                return GetFightStrategy(Config.AutoBossConfig.StrategyName, out var bossStrategyPath)
                    ? null
                    : new AutoBossParam(bossStrategyPath);
            case AutoStygianOnslaughtTask.TaskId:
                // 无参构造读取全局幽境危战配置，再用点击时选中的策略路径覆盖
                return GetFightStrategy(Config.AutoStygianOnslaughtConfig.StrategyName, out var stygianStrategyPath)
                    ? null
                    : new AutoStygianOnslaughtParam(stygianStrategyPath);
            case AutoFishingTask.TaskId:
                return AutoFishingTaskParam.BuildFromConfig(Config.AutoFishingConfig, SaveScreenshotOnKeyTick);
            case AutoArtifactSalvageTask.TaskId:
                var salvageConfig = Config.AutoArtifactSalvageConfig;
                return new AutoArtifactSalvageTaskParam(
                    int.Parse(salvageConfig.MaxArtifactStar),
                    salvageConfig.JavaScript,
                    salvageConfig.ArtifactSetFilter,
                    salvageConfig.MaxNumToCheck,
                    salvageConfig.RecognitionFailurePolicy);
            case UseRedemptionCodeTask.TaskId:
                var codes = PromptForRedeemCodes();
                return codes is null ? null : new UseRedemptionCodeParam { Codes = codes };
            case GetGridIconsTask.TaskId:
                return new GetGridIconsParam
                {
                    GridName = Config.GetGridIconsConfig.GridName,
                    StarAsSuffix = Config.GetGridIconsConfig.StarAsSuffix,
                    MaxNumToGet = Config.GetGridIconsConfig.MaxNumToGet
                };
            case GridIconsAccuracyTestTask.TaskId:
                return new GridIconsAccuracyTestParam
                {
                    GridName = Config.GetGridIconsConfig.GridName,
                    MaxNumToTest = Config.GetGridIconsConfig.MaxNumToGet
                };
            case InventoryCountComparisonTask.TaskId:
                return new InventoryCountComparisonParam { Target = InventoryCountComparisonTarget };
            case OneDragonTask.TaskId:
                // 一条龙的配置、"从此执行"标记由一条龙页面维护（快捷键经此入口启动）
                return OneDragonFlowViewModel?.CreateOneDragonParam();
            default:
                // 地脉花、音游、烹饪等任务的无参构造已按全局配置给出参数
                return _soloTaskRegistry.Get(taskId).CreateDefaultParam();
        }
    }

    /// <summary>
    /// 弹出兑换码输入框并返回清理后的有效兑换码；取消或无有效内容时返回 null。
    /// </summary>
    private static List<string>? PromptForRedeemCodes()
    {
        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "请在此输入兑换码，每行一条记录"
        };
        var dialog = new PromptDialog("输入兑换码", "自动使用兑换码", multilineTextBox, null)
        {
            Height = 500
        };
        dialog.ShowDialog();
        if (dialog.DialogResult != true || string.IsNullOrWhiteSpace(multilineTextBox.Text))
        {
            return null;
        }

        var codes = multilineTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrEmpty(code))
            .ToList();
        if (codes.Count == 0)
        {
            Toast.Warning("没有有效的兑换码");
            return null;
        }

        return codes;
    }

    public bool GetTcgStrategy(out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(Config.AutoGeniusInvokationConfig.StrategyName))
        {
            Toast.Warning("请先选择策略");
            return true;
        }

        var path = Global.Absolute(@"User\AutoGeniusInvokation\" + Config.AutoGeniusInvokationConfig.StrategyName + ".txt");

        if (!File.Exists(path))
        {
            Toast.Error("策略文件不存在");
            return true;
        }

        content = File.ReadAllText(path);
        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoGeniusInvokationUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/tcg.html"));
    }

    [RelayCommand]
    public async Task OnGoToAutoWoodUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/felling.html"));
    }

    [RelayCommand]
    public async Task OnGoToAutoFightUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/domain.html"));
    }

    public bool GetFightStrategy(out string path)
    {
        return GetFightStrategy(Config.AutoFightConfig.StrategyName, out path);
    }

    public bool GetFightStrategy(string strategyName, out string path)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在下拉列表配置中选择战斗策略！"); });
            path = string.Empty;
            return true;
        }

        if ("根据队伍自动选择".Equals(strategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }
        else if (AutoFightParam.ComboStrategyName.Equals(strategyName))
        {
            // 固定策略：不对应策略文件，跳过存在性检查，由 ComboCombatTaskFactory 路由
            path = strategyName;
            return false;
        }
        else
        {
            (path, _) = AutoFightParam.ResolveStrategyPath(strategyName);
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Error("当前选择的自动战斗策略文件不存在"); });
            return true;
        }

        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoDomainUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    private async Task OnGoToAutoStygianOnslaughtUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/stygian.html"));
    }

    [RelayCommand]
    public async Task OnGoToAutoLeyLineOutcropUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/leyline.html"));
    }


    [RelayCommand]
    public void OnOpenFightFolder()
    {
        AutoFightViewModel?.OnOpenFightFolder();
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrack()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrack, param);
        //             SwitchAutoTrackButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     ThemedMessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    public async Task OnGoToAutoTrackUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/track.html"));
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrackPath()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackPathButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackPathParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrackPath, param);
        //             SwitchAutoTrackPathButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackPathButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     ThemedMessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    private async Task OnGoToAutoTrackPathUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/track.html"));
    }

    [RelayCommand]
    private async Task OnGoToAutoMusicGameUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/music.html"));
    }

    [RelayCommand]
    private void OnAddAvatarDescriptionOverride()
    {
        Config.AutoComboBuildConfig.AvatarDescriptionOverrides.Add(new AvatarProfile("", "", []));
    }

    [RelayCommand]
    private void OnRemoveAvatarDescriptionOverride(AvatarProfile item)
    {
        Config.AutoComboBuildConfig.AvatarDescriptionOverrides.Remove(item);
    }

    [RelayCommand]
    private async Task OnSwitchAutoComboRun()
    {
        if (_autoComboRunRunning)
        {
            // 暂停：取消 Tick 循环，行为树节点状态保留，下次点击继续
            _autoComboRunRunning = false;
            _autoComboRunPaused = true;
            SwitchAutoComboRunButtonText = "继续";
            _taskStopService.StopAll(TaskStopReason.UserRequested);
            return;
        }

        // 预检查：未建树时提示用户且不启动任务
        if (AutoComboRuntime.Session == null)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("尚未构建行为树，请先运行一次自动连招任务完成建树"); });
            return;
        }

        _autoComboRunRunning = true;
        _autoComboRunPaused = false;
        SwitchAutoComboRunButtonText = "暂停";
        try
        {
            // 消费最近一次建树任务暂存的会话；独立运行需显式开启自带战斗结束检测（AutoFightParam 默认关闭）
            var session = AutoComboRuntime.Session!;
            await new TaskRunner()
                .RunSoloTaskAsync(rt => new AutoComboRunTask(rt, Config, session),
                    new AutoFightParam { FightFinishDetectEnabled = true });
        }
        finally
        {
            _autoComboRunRunning = false;
            if (!_autoComboRunPaused)
            {
                SwitchAutoComboRunButtonText = "测试运行";
            }
        }
    }

    /// <summary>拉取期间禁用按钮，避免重复请求。</summary>
    private bool CanFetchAutoComboModelList() => !FetchingAutoComboModelList;

    /// <summary>
    /// 拉取当前服务商的模型列表填充下拉。只读查询，除候选列表外不改动任何配置。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetchAutoComboModelList))]
    private async Task OnFetchAutoComboModelList()
    {
        FetchingAutoComboModelList = true;
        try
        {
            var config = Config.AutoComboBuildConfig;
            var models = await AutoComboModelListFetcher.FetchAsync(config.Provider, config.PlanningLlmEndpoint, config.ApiKey, CancellationToken.None);
            AutoComboModelNameList = new ObservableCollection<string>(models);
            UIDispatcherHelper.Invoke(() => Toast.Success($"已拉取 {models.Count} 个模型，可在“LLM模型名”下拉选择"));
        }
        catch (Exception e)
        {
            UIDispatcherHelper.Invoke(() => Toast.Error($"拉取模型列表失败：{e.Message}"));
        }
        finally
        {
            FetchingAutoComboModelList = false;
        }
    }

    [RelayCommand]
    private async Task OnGoToAutoFishingUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/fish.html"));
    }

    [RelayCommand]
    private async Task OnGoToTorchPreviousVersionsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://pytorch.org/get-started/previous-versions"));
    }

    [RelayCommand]
    private void OnOpenLocalScriptRepo()
    {
        AutoFightViewModel?.OnOpenLocalScriptRepo();
    }

    [RelayCommand]
    private async Task OnGoToArtifactSalvageUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/artifactSalvage.html"));
    }

    [RelayCommand]
    private async Task OnOpenArtifactSalvageTestOCRWindow()
    {
        ArtifactOcrDialog ocrDialog = new ArtifactOcrDialog(0.70, 0.112, 0.275, 0.50, "圣遗物分解", this.Config.AutoArtifactSalvageConfig.JavaScript);
        if (await ocrDialog.CaptureAsync()) { ocrDialog.ShowDialog(); }
    }

    [RelayCommand]
    private async Task OnCopyArtifactSalvageJavaScriptFromRepository()
    {
        var list = ScriptControlViewModel.LoadAllJsScriptProjects();
        var stackPanel = ScriptControlViewModel.CreateJsScriptSelectionPanel(list, typeof(RadioButton));

        var result = PromptDialog.Prompt("请选择需要复制的JS脚本", "请选择需要复制的JS脚本", stackPanel, new Size(500, 600));
        if (!string.IsNullOrEmpty(result))
        {
            string? selectedFolderName = null;
            foreach (var child in ((Wpf.Ui.Controls.StackPanel)stackPanel.Content).Children)
            {
                if (child is RadioButton { IsChecked: true } radioButton && radioButton.Tag is string folderName)
                {
                    selectedFolderName = folderName;
                }
            }
            if (selectedFolderName == null)
            {
                return;
            }

            ScriptProject scriptProject = new ScriptProject(selectedFolderName);
            string jsCode = await scriptProject.LoadCode();

            var multilineTextBox = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = jsCode,
                IsReadOnly = true
            };
            var p = new PromptDialog($"{scriptProject.Manifest.Name}\r\n{scriptProject.Manifest.ShortDescription}\r\n\r\n将覆盖现有的JavaScript，是否继续？", $"预览 - {scriptProject.FolderName}", multilineTextBox, null);
            p.Height = 600;
            p.MaxWidth = 800;
            p.ShowDialog();

            if (p.DialogResult != true)
            {
                return;
            }

            this.Config.AutoArtifactSalvageConfig.JavaScript = jsCode;
        }
    }

    [RelayCommand]
    private void OnGoToGetGridIconsFolder()
    {
        var path = Global.Absolute(@"log\gridIcons\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task OnGoToGetGridIconsUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/getGridIcons.html"));
    }

    /// <summary>创建并打开数量 OCR 对比结果根目录。</summary>
    [RelayCommand]
    private void OnGoToInventoryCountComparisonFolder()
    {
        var path = Global.Absolute(@"log\InventoryCountComparison\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

}
