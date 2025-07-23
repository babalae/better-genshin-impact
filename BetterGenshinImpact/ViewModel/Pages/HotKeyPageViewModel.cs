using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.GameTask.QucikBuy;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.View;
using OpenCvSharp;
using Vanara.PInvoke;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;
    private readonly TaskSettingsPageViewModel _taskSettingsPageViewModel;
    private readonly ILocalizationService _localizationService;
    public AllConfig Config { get; set; }

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = [];

    public HotKeyPageViewModel(IConfigService configService, ILogger<HotKeyPageViewModel> logger, TaskSettingsPageViewModel taskSettingsPageViewModel, ILocalizationService localizationService)
    {
        _logger = logger;
        _taskSettingsPageViewModel = taskSettingsPageViewModel;
        _localizationService = localizationService;
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var hotKeyConfig in list)
        {
            hotKeyConfig.RegisterHotKey();
            hotKeyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is HotKeySettingModel model)
                {
                    // 反射更新配置

                    // 更新快捷键
                    if (e.PropertyName == "HotKey")
                    {
                        Debug.WriteLine($"{model.FunctionName} {_localizationService.GetString("hotkey.hotkeyChangedTo")} {model.HotKey}");
                        var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
                        if (null != pi && pi.CanWrite)
                        {
                            var str = model.HotKey.ToString();
                            if (str == "< None >")
                            {
                                str = "";
                            }

                            pi.SetValue(Config.HotKeyConfig, str, null);
                        }
                    }

                    // 更新快捷键类型
                    if (e.PropertyName == "HotKeyType")
                    {
                        Debug.WriteLine($"{model.FunctionName} {_localizationService.GetString("hotkey.hotkeyTypeChangedTo")} {model.HotKeyType.ToChineseName()}");
                        model.HotKey = HotKey.None;
                        var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName + "Type", BindingFlags.Public | BindingFlags.Instance);
                        if (null != pi && pi.CanWrite)
                        {
                            pi.SetValue(Config.HotKeyConfig, model.HotKeyType.ToString(), null);
                        }
                    }

                    RemoveDuplicateHotKey(model);
                    model.UnRegisterHotKey();
                    model.RegisterHotKey();
                }
            };
        }
    }

    /// <summary>
    /// 移除重复的快捷键配置
    /// </summary>
    /// <param name="current"></param>
    private void RemoveDuplicateHotKey(HotKeySettingModel current)
    {
        if (current.HotKey.IsEmpty)
        {
            return;
        }

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);
        foreach (var hotKeySettingModel in list)
        {
            if (hotKeySettingModel.HotKey.IsEmpty)
            {
                continue;
            }

            if (hotKeySettingModel.ConfigPropertyName != current.ConfigPropertyName && hotKeySettingModel.HotKey == current.HotKey)
            {
                hotKeySettingModel.HotKey = HotKey.None;
            }
        }
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryHotkey(IEnumerable<HotKeySettingModel> modelList)
    {
        var list = new List<HotKeySettingModel>();
        foreach (var hotKeySettingModel in modelList)
        {
            if (!hotKeySettingModel.IsDirectory)
            {
                list.Add(hotKeySettingModel);
            }

            list.AddRange(GetAllNonDirectoryChildren(hotKeySettingModel));
        }

        return list;
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryChildren(HotKeySettingModel model)
    {
        var result = new List<HotKeySettingModel>();

        if (model.Children.Count == 0)
        {
            return result;
        }

        foreach (var child in model.Children)
        {
            if (!child.IsDirectory)
            {
                result.Add(child);
            }

            // 递归调用以获取子节点中的非目录对象
            result.AddRange(GetAllNonDirectoryChildren(child));
        }

        return result;
    }

    private void BuildHotKeySettingModelList()
    {
        // 一级目录/快捷键
        var bgiEnabledHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopBetterGI"),
            nameof(Config.HotKeyConfig.BgiEnabledHotkey),
            Config.HotKeyConfig.BgiEnabledHotkey,
            Config.HotKeyConfig.BgiEnabledHotkeyType,
            (_, _) => { WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SwitchTriggerStatus", "", "")); }
        );
        HotKeySettingModels.Add(bgiEnabledHotKeySettingModel);

        var systemDirectory = new HotKeySettingModel(
            _localizationService.GetString("hotkey.systemControl")
        );
        HotKeySettingModels.Add(systemDirectory);

        var timerDirectory = new HotKeySettingModel(
            _localizationService.GetString("hotkey.realtimeTasks")
        );
        HotKeySettingModels.Add(timerDirectory);

        var soloTaskDirectory = new HotKeySettingModel(
            _localizationService.GetString("hotkey.soloTasks")
        );
        HotKeySettingModels.Add(soloTaskDirectory);

        var macroDirectory = new HotKeySettingModel(
            _localizationService.GetString("hotkey.controlAssist")
        );
        HotKeySettingModels.Add(macroDirectory);

        var devDirectory = new HotKeySettingModel(
            _localizationService.GetString("hotkey.developer")
        );
        HotKeySettingModels.Add(devDirectory);

        // 二级快捷键
        systemDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.stopCurrentTask"),
            nameof(Config.HotKeyConfig.CancelTaskHotkey),
            Config.HotKeyConfig.CancelTaskHotkey,
            Config.HotKeyConfig.CancelTaskHotkeyType,
            (_, _) => { CancellationContext.Instance.Cancel(); }
        ));
        systemDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.pauseCurrentTask"),
            nameof(Config.HotKeyConfig.SuspendHotkey),
            Config.HotKeyConfig.SuspendHotkey,
            Config.HotKeyConfig.SuspendHotkeyType,
            (_, _) => { RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend; }
        ));
        var takeScreenshotHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.takeScreenshot"),
            nameof(Config.HotKeyConfig.TakeScreenshotHotkey),
            Config.HotKeyConfig.TakeScreenshotHotkey,
            Config.HotKeyConfig.TakeScreenshotHotkeyType,
            (_, _) => { TaskTriggerDispatcher.Instance().TakeScreenshot(); }
        );
        systemDirectory.Children.Add(takeScreenshotHotKeySettingModel);

        systemDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.toggleLogStatusWindow"),
            nameof(Config.HotKeyConfig.LogBoxDisplayHotkey),
            Config.HotKeyConfig.LogBoxDisplayHotkey,
            Config.HotKeyConfig.LogBoxDisplayHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox = !TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
                // 与状态窗口同步
                TaskContext.Instance().Config.MaskWindowConfig.ShowStatus = TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
            }
        ));

        var autoPickEnabledHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.autoPickToggle"),
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            Config.HotKeyConfig.AutoPickEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoPickConfig.Enabled = !TaskContext.Instance().Config.AutoPickConfig.Enabled;
                _logger.LogInformation(_localizationService.GetString("hotkey.switchStatusTo"), _localizationService.GetString("hotkey.autoPick"), ToChinese(TaskContext.Instance().Config.AutoPickConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoPickEnabledHotKeySettingModel);

        var autoSkipEnabledHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.autoStoryToggle"),
            nameof(Config.HotKeyConfig.AutoSkipEnabledHotkey),
            Config.HotKeyConfig.AutoSkipEnabledHotkey,
            Config.HotKeyConfig.AutoSkipEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.Enabled = !TaskContext.Instance().Config.AutoSkipConfig.Enabled;
                _logger.LogInformation(_localizationService.GetString("hotkey.switchStatusTo"), _localizationService.GetString("hotkey.autoStory"), ToChinese(TaskContext.Instance().Config.AutoSkipConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoSkipEnabledHotKeySettingModel);

        timerDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.autoHangoutToggle"),
            nameof(Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey),
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey,
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled = !TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled;
                _logger.LogInformation(_localizationService.GetString("hotkey.switchStatusTo"), _localizationService.GetString("hotkey.autoHangout"), ToChinese(TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled));
            }
        ));

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.autoFishingToggle"),
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            Config.HotKeyConfig.AutoFishingEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoFishingConfig.Enabled = !TaskContext.Instance().Config.AutoFishingConfig.Enabled;
                _logger.LogInformation(_localizationService.GetString("hotkey.switchStatusTo"), _localizationService.GetString("hotkey.autoFishing"), ToChinese(TaskContext.Instance().Config.AutoFishingConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoFishingEnabledHotKeySettingModel);

        var quickTeleportEnabledHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickTeleportToggle"),
            nameof(Config.HotKeyConfig.QuickTeleportEnabledHotkey),
            Config.HotKeyConfig.QuickTeleportEnabledHotkey,
            Config.HotKeyConfig.QuickTeleportEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.QuickTeleportConfig.Enabled = !TaskContext.Instance().Config.QuickTeleportConfig.Enabled;
                _logger.LogInformation(_localizationService.GetString("hotkey.switchStatusTo"), _localizationService.GetString("hotkey.quickTeleport"), ToChinese(TaskContext.Instance().Config.QuickTeleportConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(quickTeleportEnabledHotKeySettingModel);

        var quickTeleportTickHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.manualQuickTeleportTrigger"),
            nameof(Config.HotKeyConfig.QuickTeleportTickHotkey),
            Config.HotKeyConfig.QuickTeleportTickHotkey,
            Config.HotKeyConfig.QuickTeleportTickHotkeyType,
            (_, _) => { Thread.Sleep(100); },
            true
        );
        timerDirectory.Children.Add(quickTeleportTickHotKeySettingModel);

        var turnAroundHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.holdToRotateView"),
            nameof(Config.HotKeyConfig.TurnAroundHotkey),
            Config.HotKeyConfig.TurnAroundHotkey,
            Config.HotKeyConfig.TurnAroundHotkeyType,
            (_, _) => { TurnAroundMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(turnAroundHotKeySettingModel);

        var enhanceArtifactHotKeySettingModel = new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickEnhanceArtifact"),
            nameof(Config.HotKeyConfig.EnhanceArtifactHotkey),
            Config.HotKeyConfig.EnhanceArtifactHotkey,
            Config.HotKeyConfig.EnhanceArtifactHotkeyType,
            (_, _) => { QuickEnhanceArtifactMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(enhanceArtifactHotKeySettingModel);

        macroDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickBuyShopItems"),
            nameof(Config.HotKeyConfig.QuickBuyHotkey),
            Config.HotKeyConfig.QuickBuyHotkey,
            Config.HotKeyConfig.QuickBuyHotkeyType,
            (_, _) => { QuickBuyTask.Done(); },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickSereniteaPot"),
            nameof(Config.HotKeyConfig.QuickSereniteaPotHotkey),
            Config.HotKeyConfig.QuickSereniteaPotHotkey,
            Config.HotKeyConfig.QuickSereniteaPotHotkeyType,
            (_, _) => { QuickSereniteaPotTask.Done(); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopOneDragon"),
            nameof(Config.HotKeyConfig.OnedragonHotkey),
            Config.HotKeyConfig.OnedragonHotkey,
            Config.HotKeyConfig.OnedragonHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SOneDragonFlowCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoTCG"),
            nameof(Config.HotKeyConfig.AutoGeniusInvokationHotkey),
            Config.HotKeyConfig.AutoGeniusInvokationHotkey,
            Config.HotKeyConfig.AutoGeniusInvokationHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoGeniusInvokationCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoWood"),
            nameof(Config.HotKeyConfig.AutoWoodHotkey),
            Config.HotKeyConfig.AutoWoodHotkey,
            Config.HotKeyConfig.AutoWoodHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoWoodCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoFight"),
            nameof(Config.HotKeyConfig.AutoFightHotkey),
            Config.HotKeyConfig.AutoFightHotkey,
            Config.HotKeyConfig.AutoFightHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFightCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoDomain"),
            nameof(Config.HotKeyConfig.AutoDomainHotkey),
            Config.HotKeyConfig.AutoDomainHotkey,
            Config.HotKeyConfig.AutoDomainHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoDomainCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoMusicGame"),
            nameof(Config.HotKeyConfig.AutoMusicGameHotkey),
            Config.HotKeyConfig.AutoMusicGameHotkey,
            Config.HotKeyConfig.AutoMusicGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoMusicGameCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopAutoFishingGame"),
            nameof(Config.HotKeyConfig.AutoFishingGameHotkey),
            Config.HotKeyConfig.AutoFishingGameHotkey,
            Config.HotKeyConfig.AutoFishingGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFishingCommand); }
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickClickConfirmButton"),
            nameof(Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey),
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey,
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkeyType,
            (_, _) =>
            {
                if (Bv.ClickConfirmButton(TaskControl.CaptureToRectArea()))
                {
                    TaskControl.Logger.LogInformation(_localizationService.GetString("hotkey.quickClickButtonSuccess"), _localizationService.GetString("common.confirm"));
                }
                else
                {
                    TaskControl.Logger.LogInformation(_localizationService.GetString("hotkey.quickClickButtonNotFound"), _localizationService.GetString("common.confirm"));
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.quickClickCancelButton"),
            nameof(Config.HotKeyConfig.ClickGenshinCancelButtonHotkey),
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkey,
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkeyType,
            (_, _) =>
            {
                if (Bv.ClickCancelButton(TaskControl.CaptureToRectArea()))
                {
                    TaskControl.Logger.LogInformation(_localizationService.GetString("hotkey.quickClickButtonSuccess"), _localizationService.GetString("common.cancel"));
                }
                else
                {
                    TaskControl.Logger.LogInformation(_localizationService.GetString("hotkey.quickClickButtonNotFound"), _localizationService.GetString("common.cancel"));
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.oneKeyFightMacro"),
            nameof(Config.HotKeyConfig.OneKeyFightHotkey),
            Config.HotKeyConfig.OneKeyFightHotkey,
            Config.HotKeyConfig.OneKeyFightHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) => { OneKeyFightTask.Instance.KeyDown(); },
            OnKeyUpAction = (_, _) => { OneKeyFightTask.Instance.KeyUp(); }
        });

        devDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopKeyMouseRecord"),
            nameof(Config.HotKeyConfig.KeyMouseMacroRecordHotkey),
            Config.HotKeyConfig.KeyMouseMacroRecordHotkey,
            Config.HotKeyConfig.KeyMouseMacroRecordHotkeyType, async (_, _) =>
            {
                var vm = App.GetService<KeyMouseRecordPageViewModel>();
                if (vm == null)
                {
                    _logger.LogError(_localizationService.GetString("hotkey.cannotFindViewModel"));
                    return;
                }

                if (GlobalKeyMouseRecord.Instance.Status == KeyMouseRecorderStatus.Stop)
                {
                    Thread.Sleep(300); // 防止录进快捷键进去
                    await vm.OnStartRecord();
                }
                else
                {
                    vm.OnStopRecord();
                }
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.devGetBigMapPosition"),
            nameof(Config.HotKeyConfig.RecBigMapPosHotkey),
            Config.HotKeyConfig.RecBigMapPosHotkey,
            Config.HotKeyConfig.RecBigMapPosHotkeyType,
            (_, _) =>
            {
                var p = new TpTask(CancellationToken.None).GetPositionFromBigMap(MapTypes.Teyvat.ToString());
                _logger.LogInformation(_localizationService.GetString("hotkey.bigMapPosition"), p);
            }
        ));

        var pathRecorder = PathRecorder.Instance;
        var pathRecording = false;

        devDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.startStopPathRecorder"),
            nameof(Config.HotKeyConfig.PathRecorderHotkey),
            Config.HotKeyConfig.PathRecorderHotkey,
            Config.HotKeyConfig.PathRecorderHotkeyType,
            (_, _) =>
            {
                if (pathRecording)
                {
                    pathRecorder.Save();
                }
                else
                {
                    Task.Run(() => { pathRecorder.Start(); });
                }

                pathRecording = !pathRecording;
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            _localizationService.GetString("hotkey.addWaypoint"),
            nameof(Config.HotKeyConfig.AddWaypointHotkey),
            Config.HotKeyConfig.AddWaypointHotkey,
            Config.HotKeyConfig.AddWaypointHotkeyType,
            (_, _) =>
            {
                if (pathRecording)
                {
                    Task.Run(() => { pathRecorder.AddWaypoint(); });
                    
                }
            }
        ));

        // DEBUG
        if (RuntimeHelper.IsDebug)
        {
            var debugDirectory = new HotKeySettingModel(
                _localizationService.GetString("hotkey.internalTest")
            );
            HotKeySettingModels.Add(debugDirectory);


            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）启动/停止自动追踪",
            //     nameof(Config.HotKeyConfig.AutoTrackHotkey),
            //     Config.HotKeyConfig.AutoTrackHotkey,
            //     Config.HotKeyConfig.AutoTrackHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrack();
            //     }
            // ));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）地图路线录制",
            //     nameof(Config.HotKeyConfig.MapPosRecordHotkey),
            //     Config.HotKeyConfig.MapPosRecordHotkey,
            //     Config.HotKeyConfig.MapPosRecordHotkeyType,
            //     (_, _) =>
            //     {
            //         PathPointRecorder.Instance.Switch();
            //     }));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）自动寻路",
            //     nameof(Config.HotKeyConfig.AutoTrackPathHotkey),
            //     Config.HotKeyConfig.AutoTrackPathHotkey,
            //     Config.HotKeyConfig.AutoTrackPathHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrackPath();
            //     }
            // ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                _localizationService.GetString("hotkey.testTest"),
                nameof(Config.HotKeyConfig.Test1Hotkey),
                Config.HotKeyConfig.Test1Hotkey,
                Config.HotKeyConfig.Test1HotkeyType,
                (_, _) =>
                {
                }
            ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                _localizationService.GetString("hotkey.testTest2"),
                nameof(Config.HotKeyConfig.Test2Hotkey),
                Config.HotKeyConfig.Test2Hotkey,
                Config.HotKeyConfig.Test2HotkeyType,
                (_, _) =>
                {
                    SetTimeTask setTimeTask = new SetTimeTask();
                    Task.Run(async () => { await setTimeTask.Start(12, 05, new CancellationToken()); });

                    // var pName = SystemControl.GetActiveProcessName();
                    // Debug.WriteLine($"当前处于前台的程序：{pName}，原神是否位于前台：{SystemControl.IsGenshinImpactActive()}");
                    // TaskControl.Logger.LogInformation($"当前处于前台的程序：{pName}");
                }
            ));

            debugDirectory.Children.Add(new HotKeySettingModel(
                _localizationService.GetString("hotkey.testPlayMemoryPath"),
                nameof(Config.HotKeyConfig.ExecutePathHotkey),
                Config.HotKeyConfig.ExecutePathHotkey,
                Config.HotKeyConfig.ExecutePathHotkeyType,
                (_, _) =>
                {
                    // if (pathRecording)
                    // {
                    //     new TaskRunner(DispatcherTimerOperationEnum.UseCacheImageWithTrigger)
                    //        .FireAndForget(async () => await new PathExecutor(CancellationContext.Instance.Cts).Pathing(pathRecorder._pathingTask));
                    // }
                }
            ));
        }
    }

    private void SwitchSoloTask(IAsyncRelayCommand asyncRelayCommand)
    {
        if (asyncRelayCommand.IsRunning)
        {
            CancellationContext.Instance.Cancel();
        }
        else
        {
            asyncRelayCommand.Execute(null);
        }
    }

    private string ToChinese(bool enabled)
    {
        return enabled.ToChinese();
    }
}