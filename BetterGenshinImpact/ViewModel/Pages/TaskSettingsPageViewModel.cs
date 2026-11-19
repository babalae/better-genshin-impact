using BetterGenshinImpact.GameTask.AutoFight.Strategy;
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
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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

    /// <summary>秘境树脂入口，与旧一条龙秘境入口同步同一配置。</summary>
    public ResinUsageEntryViewModel DomainResin { get; }
    /// <summary>首领树脂入口。</summary>
    public ResinUsageEntryViewModel BossResin { get; }
    /// <summary>幽境树脂入口。</summary>
    public ResinUsageEntryViewModel StygianResin { get; }
    /// <summary>地脉花树脂入口。</summary>
    public ResinUsageEntryViewModel LeyLineResin { get; }

    /// <summary>
    /// 自动首领讨伐的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoBossTargetItems { get; }

    /// <summary>
    /// 自动秘境的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoDomainTargetItems { get; }

    /// <summary>
    /// 自动秘境的秘境来源：跟随培养计划或指定秘境，读写全局秘境配置的秘境名。
    /// </summary>
    public DomainSourceViewModel AutoDomainSource { get; }

    /// <summary>
    /// 合成收益预留率是否可编辑：刷取偏好为“充分刷取”时任务固定按 0% 计算，此项不生效。
    /// </summary>
    public bool IsTrainingGuideReservePercentEditable => Config.AutoDomainConfig.TrainingGuideRunPreference == 0;

    /// <summary>
    /// 自动地脉花的材料目标编辑器。
    /// </summary>
    public TargetItemListEditorViewModel AutoLeyLineTargetItems { get; }

    private readonly INavigationService _navigationService;

    /// <summary>
    /// 任务设置页面日志，用于记录模型列表拉取失败详情。
    /// </summary>
    private readonly ILogger<TaskSettingsPageViewModel> _logger = App.GetLogger<TaskSettingsPageViewModel>();

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
    private string _switchAutoTrackButtonText = I18nService.Instance.Translate("启动");

    [ObservableProperty]
    private string _switchAutoTrackPathButtonText = I18nService.Instance.Translate("启动");

    [ObservableProperty]
    private string _switchAutoComboRunButtonText = I18nService.Instance.Translate("测试运行");

    private bool _autoComboRunRunning;

    private bool _autoComboRunPaused;

    /// <summary>“拉取模型列表”得到的候选模型名；不写入配置，重启后需要重新拉取</summary>
    [ObservableProperty]
    private ObservableCollection<string> _autoComboModelNameList = [];

    /// <summary>正在拉取模型列表，用于禁用按钮避免重复请求</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchAutoComboModelListCommand))]
    private bool _fetchingAutoComboModelList;

    /// <summary>当前“自动连招”服务商为发鱼站：地址与提示词类配置由网关接管，设置页不再展示</summary>
    [ObservableProperty]
    private bool _isAutoComboFishRelief;

    [ObservableProperty]
    private List<string> _domainNameList;

    public static List<string> ArtifactSalvageStarList = ["4", "3", "2", "1"];

    public static List<int> BossNumList = [1, 2, 3];

    public static List<string> AutoBossNameList = [.. AutoBossData.SupportedBossNames];

    public static List<string> AvatarIndexList = ["", "1", "2", "3", "4"];
    public static List<string> CombatAvatarNameList = [.. AvatarProfiles.GetProfileNames()];
    // 地脉花类型与国家是任务逻辑比较的配置值，只标记为翻译 Key，界面通过 {i18n:TBinding} 展示译文
    public static List<string> LeyLineOutcropTypeList = [I18nService.Key("启示之花"), I18nService.Key("藏金之花")];
    public static List<string> LeyLineOutcropCountryList = [I18nService.Key("蒙德"), I18nService.Key("璃月"), I18nService.Key("稻妻"), I18nService.Key("须弥"), I18nService.Key("枫丹"), I18nService.Key("纳塔"), I18nService.Key("挪德卡莱"), I18nService.Key("至冬")];
    public static List<string> LeyLineOutcropTypeListWithEmpty = ["", .. LeyLineOutcropTypeList];
    public static List<string> LeyLineOutcropCountryListWithEmpty = ["", .. LeyLineOutcropCountryList];

    [ObservableProperty]
    private List<string> _autoMusicLevelList = [I18nService.Key("传说"), I18nService.Key("大师"), I18nService.Key("困难"), I18nService.Key("普通"), I18nService.Key("所有")];

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
    /// <param name="dialogs">公共树脂弹窗服务。</param>
    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService,
        TaskStopService taskStopService,
        OneDragonFlowViewModel oneDragonFlowViewModel, InstanceService instanceService,
        SoloTaskRegistry soloTaskRegistry, IResinSettingsDialogs? dialogs = null)
    {
        Config = configService.Get();
        // 入口只整体替换配置；同配置上的其他页面入口会收到属性通知并刷新摘要。
        var domainConfig = Config.AutoDomainConfig;
        DomainResin = ResinUsageConfigBinding.Create(I18nService.Key("自动秘境"), AutoDomainTask.ResinCapability, domainConfig,
            domainConfig.EnsureResinUsage, value => domainConfig.ResinUsage = value, dialogs);
        var bossConfig = Config.AutoBossConfig;
        BossResin = ResinUsageConfigBinding.Create(I18nService.Key("自动首领讨伐"), AutoBossTask.ResinCapability, bossConfig,
            bossConfig.EnsureResinUsage, value => bossConfig.ResinUsage = value, dialogs);
        var stygianConfig = Config.AutoStygianOnslaughtConfig;
        StygianResin = ResinUsageConfigBinding.Create(I18nService.Key("自动幽境危战"), AutoStygianOnslaughtTask.ResinCapability, stygianConfig,
            stygianConfig.EnsureResinUsage, value => stygianConfig.ResinUsage = value, dialogs);
        var leyLineConfig = Config.AutoLeyLineOutcropConfig;
        LeyLineResin = ResinUsageConfigBinding.Create(I18nService.Key("自动地脉花"), AutoLeyLineOutcropTask.ResinCapability, leyLineConfig,
            leyLineConfig.EnsureResinUsage, value => leyLineConfig.ResinUsage = value, dialogs);
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
        // 秘境来源只拆分界面选项，跟随培养计划仍保存为 domain_name 的提升指南选项值
        AutoDomainSource = new DomainSourceViewModel(() => Config.AutoDomainConfig.DomainName,
            value => Config.AutoDomainConfig.DomainName = value);
        RefreshAutoBossTargetItems();
        RefreshAutoDomainTargetItems();
        RefreshAutoLeyLineTargetItems();
        Config.AutoBossConfig.PropertyChanged += OnAutoBossConfigPropertyChanged;
        Config.AutoDomainConfig.PropertyChanged += OnAutoDomainConfigPropertyChanged;
        Config.AutoLeyLineOutcropConfig.PropertyChanged += OnAutoLeyLineOutcropConfigPropertyChanged;

        _scanDropsAfterRewardEnabledUi = Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled;

        //_strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));

        //_combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];

        _domainNameList = ["", AutoDomainTask.TrainingGuideOption, .. MapLazyAssets.Get().DomainNameList];
        _autoFightViewModel = new AutoFightViewModel(Config);
        _oneDragonFlowViewModel = oneDragonFlowViewModel;
        Config.AutoComboBuildConfig.PropertyChanged += OnAutoComboBuildConfigPropertyChanged;
        UpdateAutoComboFishRelief();
    }

    /// <summary>自动连招服务商变更时刷新对应配置项的可见性。</summary>
    private void OnAutoComboBuildConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoComboBuildConfig.Provider))
        {
            UpdateAutoComboFishRelief();
        }
    }

    /// <summary>服务商切换后刷新可见性状态：发鱼站下服务地址与提示词类配置不再适用</summary>
    private void UpdateAutoComboFishRelief()
    {
        IsAutoComboFishRelief = AutoComboLlmProvider.Normalize(Config.AutoComboBuildConfig.Provider) == AutoComboLlmProvider.FishRelief;
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
            Title = I18nService.Instance.Translate("风险提示"),
            Content = I18nService.Instance.Translate("开启“领取奖励后扫描掉落物光柱”后，角色会在领奖完成后主动移动拾取。部分地脉花点位或特定配队下，可能因为移动范围较大而卡住。\n\n如果你愿意接受这个风险，请继续开启；否则将保持关闭。"),
            PrimaryButtonText = I18nService.Instance.Translate("接受风险并开启"),
            CloseButtonText = I18nService.Instance.Translate("不接受，保持关闭"),
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
    /// 秘境变化后刷新自动秘境的材料目标候选物品与秘境来源；刷取偏好变化后刷新合成收益预留率的可编辑状态。
    /// </summary>
    private void OnAutoDomainConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoDomainConfig.DomainName))
        {
            UIDispatcherHelper.Invoke(() =>
            {
                RefreshAutoDomainTargetItems();
                AutoDomainSource.Refresh();
            });
        }
        else if (e.PropertyName == nameof(AutoDomainConfig.TrainingGuideRunPreference))
        {
            UIDispatcherHelper.Invoke(() => OnPropertyChanged(nameof(IsTrainingGuideReservePercentEditable)));
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
            new(profile.BossMaterial, string.Format(I18nService.Instance.Translate("首领材料｜{0}"), profile.BossMaterial))
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
            PlaceholderText = I18nService.Instance.Translate("请在此输入兑换码，每行一条记录")
        };
        var dialog = new PromptDialog(I18nService.Instance.Translate("输入兑换码"), I18nService.Instance.Translate("自动使用兑换码"), multilineTextBox, null)
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
            Toast.Warning(I18nService.Instance.Translate("没有有效的兑换码"));
            return null;
        }

        return codes;
    }

    public bool GetTcgStrategy(out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(Config.AutoGeniusInvokationConfig.StrategyName))
        {
            Toast.Warning(I18nService.Instance.Translate("请先选择策略"));
            return true;
        }

        var path = Global.Absolute(@"User\AutoGeniusInvokation\" + Config.AutoGeniusInvokationConfig.StrategyName + ".txt");

        if (!File.Exists(path))
        {
            Toast.Error(I18nService.Instance.Translate("策略文件不存在"));
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
            UIDispatcherHelper.Invoke(() => { Toast.Warning(I18nService.Instance.Translate("请先在下拉列表配置中选择战斗策略！")); });
            path = string.Empty;
            return true;
        }

        // 固定策略不对应文件；所有名称、前缀和路径统一由解析器检查。
        var strategy = CombatStrategyResolver.Parse(strategyName);
        path = strategy.Path;
        if (!strategy.Exists)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Error(I18nService.Instance.Translate("当前选择的自动战斗策略文件不存在")); });
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
            SwitchAutoComboRunButtonText = I18nService.Instance.Translate("继续");
            _taskStopService.StopAll(TaskStopReason.UserRequested);
            return;
        }

        // 预检查：未建树时提示用户且不启动任务
        if (AutoComboRuntime.Session == null)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning(I18nService.Instance.Translate("尚未构建行为树，请先运行一次自动连招任务完成建树")); });
            return;
        }

        _autoComboRunRunning = true;
        _autoComboRunPaused = false;
        SwitchAutoComboRunButtonText = I18nService.Instance.Translate("暂停");
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
                SwitchAutoComboRunButtonText = I18nService.Instance.Translate("测试运行");
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
        var config = Config.AutoComboBuildConfig;
        try
        {
            var models = await AutoComboModelListFetcher.FetchAsync(config.Provider, config.PlanningLlmEndpoint, config.ApiKey, CancellationToken.None);

            // 失败时不清空既有候选列表与用户已选模型；只有成功才替换
            AutoComboModelNameList = new ObservableCollection<string>(models);

            // 尚未选择模型时用列表首项补位，避免空模型名直接建树失败；已有选择保持不动（拉取只提供候选项，不覆盖用户选择）
            var autoSelected = string.IsNullOrWhiteSpace(config.ModelName);
            if (autoSelected)
            {
                config.ModelName = models[0];
            }

            UIDispatcherHelper.Invoke(() => Toast.Success(autoSelected
                ? string.Format(I18nService.Instance.Translate("已拉取 {0} 个模型，已自动选择 {1}"), models.Count, models[0])
                : string.Format(I18nService.Instance.Translate("已拉取 {0} 个模型，可在“LLM模型名”下拉选择"), models.Count)));
        }
        catch (Exception e)
        {
            // 失败详情写日志：Toast 是一次性的，不能作为排查依据
            _logger.LogError(e, "拉取自动连招模型列表失败（服务商：{Provider}）", config.Provider);
            UIDispatcherHelper.Invoke(() => Toast.Error(string.Format(I18nService.Instance.Translate("拉取模型列表失败：{0}"), e.Message)));
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
        ArtifactOcrDialog ocrDialog = new ArtifactOcrDialog(0.70, 0.112, 0.275, 0.50, I18nService.Instance.Translate("圣遗物分解"), this.Config.AutoArtifactSalvageConfig.JavaScript);
        if (await ocrDialog.CaptureAsync()) { ocrDialog.ShowDialog(); }
    }

    [RelayCommand]
    private async Task OnCopyArtifactSalvageJavaScriptFromRepository()
    {
        var list = ScriptControlViewModel.LoadAllJsScriptProjects();
        var stackPanel = ScriptControlViewModel.CreateJsScriptSelectionPanel(list, typeof(RadioButton));

        var result = PromptDialog.Prompt(I18nService.Instance.Translate("请选择需要复制的JS脚本"), I18nService.Instance.Translate("请选择需要复制的JS脚本"), stackPanel, new Size(500, 600));
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
            var p = new PromptDialog($"{scriptProject.Manifest.Name}\r\n{scriptProject.Manifest.ShortDescription}\r\n\r\n{I18nService.Instance.Translate("将覆盖现有的JavaScript，是否继续？")}", string.Format(I18nService.Instance.Translate("预览 - {0}"), scriptProject.FolderName), multilineTextBox, null);
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
