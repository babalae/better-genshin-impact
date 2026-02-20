using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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

    private readonly INavigationService _navigationService;
    private readonly TaskTriggerDispatcher _taskDispatcher;

    private CancellationTokenSource? _cts;
    private static readonly object _locker = new();

    // [ObservableProperty]
    // private string[] _strategyList;

    [ObservableProperty]
    private bool _switchAutoGeniusInvokationEnabled;

    [ObservableProperty]
    private string _switchAutoGeniusInvokationButtonText = "启动";

    [ObservableProperty]
    private int _autoWoodRoundNum;

    [ObservableProperty]
    private int _autoWoodDailyMaxCount = 2000;

    [ObservableProperty]
    private bool _switchAutoWoodEnabled;

    [ObservableProperty]
    private string _switchAutoWoodButtonText = "启动";

    //[ObservableProperty]
    //private string[] _combatStrategyList;

    [ObservableProperty]
    private int _autoDomainRoundNum;

    [ObservableProperty]
    private bool _switchAutoDomainEnabled;

    [ObservableProperty]
    private string _switchAutoDomainButtonText = "启动";

    [ObservableProperty]
    private int _autoStygianOnslaughtRoundNum;

    [ObservableProperty]
    private bool _switchAutoStygianOnslaughtEnabled;

    [ObservableProperty]
    private string _switchAutoStygianOnslaughtButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoFightEnabled;

    [ObservableProperty]
    private string _switchAutoFightButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackPathButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoMusicGameEnabled;

    [ObservableProperty]
    private string _switchAutoMusicGameButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoAlbumEnabled;

    [ObservableProperty]
    private string _switchAutoAlbumButtonText = "启动";

    [ObservableProperty]
    private List<string> _domainNameList;

    public static List<string> ArtifactSalvageStarList = ["4", "3", "2", "1"];

    public static List<int> BossNumList = [1, 2, 3];

    public static List<string> AvatarIndexList = ["", "1", "2", "3", "4"];
    public static List<string> LeyLineOutcropTypeList = ["启示之花", "藏金之花"];
    public static List<string> LeyLineOutcropCountryList = ["蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱"];

    [ObservableProperty]
    private List<string> _autoMusicLevelList = ["传说", "大师", "困难", "普通", "所有"];

    [ObservableProperty]
    private AutoFightViewModel? _autoFightViewModel;

    [ObservableProperty]
    private OneDragonFlowViewModel? _oneDragonFlowViewModel;

    [ObservableProperty]
    private bool _switchAutoFishingEnabled;

    [ObservableProperty]
    private string _switchAutoFishingButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoLeyLineOutcropEnabled;

    [ObservableProperty]
    private string _switchAutoLeyLineOutcropButtonText = "启动";

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
    public bool SaveScreenshotOnKeyTick
    {
        get => Config.CommonConfig.ScreenshotEnabled && saveScreenshotOnKeyTick;
        set => SetProperty(ref saveScreenshotOnKeyTick, value);
    }

    [ObservableProperty]
    private bool _switchArtifactSalvageEnabled;

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
    private bool _switchGetGridIconsEnabled;
    [ObservableProperty]
    private string _switchGetGridIconsButtonText = "启动";
    [ObservableProperty]
    private FrozenDictionary<Enum, string> _gridNameDict = Enum.GetValues(typeof(GridScreenName))
        .Cast<GridScreenName>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    [ObservableProperty]
    private string _switchGridIconsAccuracyTestButtonText = "运行模型准确率测试";

    [ObservableProperty]
    private bool _switchAutoRedeemCodeEnabled;

    [ObservableProperty]
    private string _switchAutoRedeemCodeButtonText = "启动";

    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _taskDispatcher = taskTriggerDispatcher;
        NormalizeLeyLineOutcropType();

        //_strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));

        //_combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];

        _domainNameList = ["", .. MapLazyAssets.Instance.DomainNameList];
        _autoFightViewModel = new AutoFightViewModel(Config);
        _oneDragonFlowViewModel = new OneDragonFlowViewModel();
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


    [RelayCommand]
    private async Task OnSOneDragonFlow()
    {
        if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
        {
            OneDragonFlowViewModel.OnNavigatedTo();
            if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
            {
                Toast.Warning("未设置任务!");
                return;
            }
        }
        await OneDragonFlowViewModel.OnOneKeyExecute();
    }

    [RelayCommand]
    private async Task OnStopSoloTask()
    {
        CancellationContext.Instance.Cancel();
        SwitchAutoGeniusInvokationEnabled = false;
        SwitchAutoWoodEnabled = false;
        SwitchAutoDomainEnabled = false;
        SwitchAutoFightEnabled = false;
        SwitchAutoMusicGameEnabled = false;
        SwitchAutoAlbumEnabled = false;
        SwitchAutoFishingEnabled = false;
        SwitchAutoLeyLineOutcropEnabled = false;
        SwitchArtifactSalvageEnabled = false;
        SwitchAutoRedeemCodeEnabled = false;
        SwitchAutoStygianOnslaughtEnabled = false;
        SwitchGetGridIconsEnabled = false;
        await Task.Delay(800);
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

    [RelayCommand]
    public async Task OnSwitchAutoGeniusInvokation()
    {
        if (GetTcgStrategy(out var content))
        {
            return;
        }

        SwitchAutoGeniusInvokationEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)));
        SwitchAutoGeniusInvokationEnabled = false;
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
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/tcg.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoWood()
    {
        SwitchAutoWoodEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoWoodTask(new WoodTaskParam(AutoWoodRoundNum, AutoWoodDailyMaxCount)));
        SwitchAutoWoodEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoWoodUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/felling.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoFight()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }

        var param = new AutoFightParam(path, Config.AutoFightConfig);

        SwitchAutoFightEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFightTask(param));
        SwitchAutoFightEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoFightUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoDomain()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }

        SwitchAutoDomainEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoDomainTask(new AutoDomainParam(AutoDomainRoundNum, path)));
        SwitchAutoDomainEnabled = false;
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

        path = Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        if ("根据队伍自动选择".Equals(strategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
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
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoStygianOnslaught()
    {
        if (GetFightStrategy(Config.AutoStygianOnslaughtConfig.StrategyName, out var path))
        {
            return;
        }

        SwitchAutoStygianOnslaughtEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoStygianOnslaughtTask(Config.AutoStygianOnslaughtConfig, path));
        SwitchAutoStygianOnslaughtEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoStygianOnslaughtUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/stygian.html"));
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
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/track.html"));
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
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/track.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoMusicGame()
    {
        SwitchAutoMusicGameEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoMusicGameTask(new AutoMusicGameParam()));
        SwitchAutoMusicGameEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoMusicGameUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/music.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoAlbum()
    {
        SwitchAutoAlbumEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoAlbumTask(new AutoMusicGameParam()));
        SwitchAutoAlbumEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoFishing()
    {
        SwitchAutoFishingEnabled = true;
        var param = AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig, SaveScreenshotOnKeyTick);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFishingTask(param));
        SwitchAutoFishingEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoLeyLineOutcrop()
    {
        SwitchAutoLeyLineOutcropEnabled = true;
        AutoLeyLineOutcropParam autoLeyLineOutcropParam = new AutoLeyLineOutcropParam();
        autoLeyLineOutcropParam.SetAutoLeyLineOutcropConfig(Config.AutoLeyLineOutcropConfig);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoLeyLineOutcropTask(autoLeyLineOutcropParam));
        SwitchAutoLeyLineOutcropEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoFishingUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/fish.html"));
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
    private async Task OnSwitchArtifactSalvage()
    {
        SwitchArtifactSalvageEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(
                int.Parse(Config.AutoArtifactSalvageConfig.MaxArtifactStar),
                Config.AutoArtifactSalvageConfig.JavaScript,
                Config.AutoArtifactSalvageConfig.ArtifactSetFilter,
                Config.AutoArtifactSalvageConfig.MaxNumToCheck,
                Config.AutoArtifactSalvageConfig.RecognitionFailurePolicy
                )));
        SwitchArtifactSalvageEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToArtifactSalvageUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/artifactSalvage.html"));
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
    private async Task OnSwitchGetGridIcons()
    {
        try
        {
            SwitchGetGridIconsEnabled = true;
            await new TaskRunner().RunSoloTaskAsync(new GetGridIconsTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.StarAsSuffix, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
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
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/dev/getGridIcons.html"));
    }

    [RelayCommand]
    private async Task OnSwitchGridIconsModelAccuracyTest()
    {
        try
        {
            SwitchGetGridIconsEnabled = true;
            await new TaskRunner().RunSoloTaskAsync(new GridIconsAccuracyTestTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
        }
    }

    [RelayCommand]
    private async Task OnSwitchAutoRedeemCode()
    {
        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "请在此输入兑换码，每行一条记录"
        };
        var p = new PromptDialog(
            "输入兑换码",
            "自动使用兑换码",
            multilineTextBox,
            null);
        p.Height = 500;
        p.ShowDialog();
        if (p.DialogResult == true && !string.IsNullOrWhiteSpace(multilineTextBox.Text))
        {
            char[] separators = ['\r', '\n'];
            var codes = multilineTextBox.Text.Split(separators, StringSplitOptions.RemoveEmptyEntries)

           .Select(code => code.Trim())
           .Where(code => !string.IsNullOrEmpty(code))
           .ToList();

            if (codes.Count == 0)
            {
                Toast.Warning("没有有效的兑换码");
                return;
            }

            SwitchAutoRedeemCodeEnabled = true;
            await new TaskRunner()
                .RunSoloTaskAsync(new UseRedemptionCodeTask(codes));
            SwitchAutoRedeemCodeEnabled = false;
        }
    }
}
