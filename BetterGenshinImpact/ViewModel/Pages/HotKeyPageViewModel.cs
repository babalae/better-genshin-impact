using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using BetterGenshinImpact.GameTask.QucikBuy;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using Vanara.PInvoke;
using BetterGenshinImpact.GameTask.Model.Enum;
using static Vanara.PInvoke.User32;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;
    private readonly TaskSettingsPageViewModel _taskSettingsPageViewModel;
    public AllConfig Config { get; set; }

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = new();

    public HotKeyPageViewModel(IConfigService configService, ILogger<HotKeyPageViewModel> logger, TaskSettingsPageViewModel taskSettingsPageViewModel)
    {
        _logger = logger;
        _taskSettingsPageViewModel = taskSettingsPageViewModel;
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        foreach (var hotKeyConfig in HotKeySettingModels)
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
                        Debug.WriteLine($"{model.FunctionName} 快捷键变更为 {model.HotKey}");
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
                        Debug.WriteLine($"{model.FunctionName} 快捷键类型变更为 {model.HotKeyType.ToChineseName()}");
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

        foreach (var hotKeySettingModel in HotKeySettingModels)
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

    private void BuildHotKeySettingModelList()
    {
        var bgiEnabledHotKeySettingModel = new HotKeySettingModel(
            "启动停止 BetterGI",
            nameof(Config.HotKeyConfig.BgiEnabledHotkey),
            Config.HotKeyConfig.BgiEnabledHotkey,
            Config.HotKeyConfig.BgiEnabledHotkeyType,
            (_, _) => { WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SwitchTriggerStatus", "", "")); }
        );
        HotKeySettingModels.Add(bgiEnabledHotKeySettingModel);

        var takeScreenshotHotKeySettingModel = new HotKeySettingModel(
            "游戏截图（开发者）",
            nameof(Config.HotKeyConfig.TakeScreenshotHotkey),
            Config.HotKeyConfig.TakeScreenshotHotkey,
            Config.HotKeyConfig.TakeScreenshotHotkeyType,
            (_, _) => { TaskTriggerDispatcher.Instance().TakeScreenshot(); }
        );
        HotKeySettingModels.Add(takeScreenshotHotKeySettingModel);

        HotKeySettingModels.Add(new HotKeySettingModel(
            "日志与状态窗口展示开关",
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
            "自动拾取开关",
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            Config.HotKeyConfig.AutoPickEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoPickConfig.Enabled = !TaskContext.Instance().Config.AutoPickConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动拾取", ToChinese(TaskContext.Instance().Config.AutoPickConfig.Enabled));
            }
        );
        HotKeySettingModels.Add(autoPickEnabledHotKeySettingModel);

        var autoSkipEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动剧情开关",
            nameof(Config.HotKeyConfig.AutoSkipEnabledHotkey),
            Config.HotKeyConfig.AutoSkipEnabledHotkey,
            Config.HotKeyConfig.AutoSkipEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.Enabled = !TaskContext.Instance().Config.AutoSkipConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动剧情", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.Enabled));
            }
        );
        HotKeySettingModels.Add(autoSkipEnabledHotKeySettingModel);

        HotKeySettingModels.Add(new HotKeySettingModel(
            "自动邀约开关",
            nameof(Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey),
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey,
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled = !TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动邀约", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled));
            }
        ));

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动钓鱼开关",
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            Config.HotKeyConfig.AutoFishingEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoFishingConfig.Enabled = !TaskContext.Instance().Config.AutoFishingConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动钓鱼", ToChinese(TaskContext.Instance().Config.AutoFishingConfig.Enabled));
            }
        );
        HotKeySettingModels.Add(autoFishingEnabledHotKeySettingModel);

        var quickTeleportEnabledHotKeySettingModel = new HotKeySettingModel(
            "快速传送开关",
            nameof(Config.HotKeyConfig.QuickTeleportEnabledHotkey),
            Config.HotKeyConfig.QuickTeleportEnabledHotkey,
            Config.HotKeyConfig.QuickTeleportEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.QuickTeleportConfig.Enabled = !TaskContext.Instance().Config.QuickTeleportConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "快速传送", ToChinese(TaskContext.Instance().Config.QuickTeleportConfig.Enabled));
            }
        );
        HotKeySettingModels.Add(quickTeleportEnabledHotKeySettingModel);

        var quickTeleportTickHotKeySettingModel = new HotKeySettingModel(
            "手动触发快速传送触发快捷键（按住起效）",
            nameof(Config.HotKeyConfig.QuickTeleportTickHotkey),
            Config.HotKeyConfig.QuickTeleportTickHotkey,
            Config.HotKeyConfig.QuickTeleportTickHotkeyType,
            (_, _) => { Thread.Sleep(100); },
            true
        );
        HotKeySettingModels.Add(quickTeleportTickHotKeySettingModel);

        var turnAroundHotKeySettingModel = new HotKeySettingModel(
            "长按旋转视角 - 那维莱特转圈",
            nameof(Config.HotKeyConfig.TurnAroundHotkey),
            Config.HotKeyConfig.TurnAroundHotkey,
            Config.HotKeyConfig.TurnAroundHotkeyType,
            (_, _) => { TurnAroundMacro.Done(); },
            true
        );
        HotKeySettingModels.Add(turnAroundHotKeySettingModel);

        var enhanceArtifactHotKeySettingModel = new HotKeySettingModel(
            "按下快速强化圣遗物",
            nameof(Config.HotKeyConfig.EnhanceArtifactHotkey),
            Config.HotKeyConfig.EnhanceArtifactHotkey,
            Config.HotKeyConfig.EnhanceArtifactHotkeyType,
            (_, _) => { QuickEnhanceArtifactMacro.Done(); },
            true
        );
        HotKeySettingModels.Add(enhanceArtifactHotKeySettingModel);

        HotKeySettingModels.Add(new HotKeySettingModel(
            "按下快速购买商店物品",
            nameof(Config.HotKeyConfig.QuickBuyHotkey),
            Config.HotKeyConfig.QuickBuyHotkey,
            Config.HotKeyConfig.QuickBuyHotkeyType,
            (_, _) => { QuickBuyTask.Done(); },
            true
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "按下快速进出尘歌壶",
            nameof(Config.HotKeyConfig.QuickSereniteaPotHotkey),
            Config.HotKeyConfig.QuickSereniteaPotHotkey,
            Config.HotKeyConfig.QuickSereniteaPotHotkeyType,
            (_, _) => { QuickSereniteaPotTask.Done(); }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止自动七圣召唤",
            nameof(Config.HotKeyConfig.AutoGeniusInvokationHotkey),
            Config.HotKeyConfig.AutoGeniusInvokationHotkey,
            Config.HotKeyConfig.AutoGeniusInvokationHotkeyType,
            (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoGeniusInvokation(); }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止自动伐木",
            nameof(Config.HotKeyConfig.AutoWoodHotkey),
            Config.HotKeyConfig.AutoWoodHotkey,
            Config.HotKeyConfig.AutoWoodHotkeyType,
            (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoWood(); }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止自动战斗",
            nameof(Config.HotKeyConfig.AutoFightHotkey),
            Config.HotKeyConfig.AutoFightHotkey,
            Config.HotKeyConfig.AutoFightHotkeyType,
            (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoFight(); }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止自动秘境",
            nameof(Config.HotKeyConfig.AutoDomainHotkey),
            Config.HotKeyConfig.AutoDomainHotkey,
            Config.HotKeyConfig.AutoDomainHotkeyType,
            (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoDomain(); }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "快捷点击原神内确认按钮",
            nameof(Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey),
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey,
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkeyType,
            (_, _) =>
            {
                if (Bv.ClickConfirmButton(TaskControl.CaptureToRectArea()))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "确认");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "确认");
                }
            }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "快捷点击原神内取消按钮",
            nameof(Config.HotKeyConfig.ClickGenshinCancelButtonHotkey),
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkey,
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkeyType,
            (_, _) =>
            {
                if (Bv.ClickCancelButton(TaskControl.CaptureToRectArea()))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "取消");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "取消");
                }
            }
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "一键战斗宏快捷键",
            nameof(Config.HotKeyConfig.OneKeyFightHotkey),
            Config.HotKeyConfig.OneKeyFightHotkey,
            Config.HotKeyConfig.OneKeyFightHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) => { OneKeyFightTask.Instance.KeyDown(); },
            OnKeyUpAction = (_, _) => { OneKeyFightTask.Instance.KeyUp(); }
        });

        var keyMouseRecordRunning = false;
        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止键鼠录制",
            nameof(Config.HotKeyConfig.KeyMouseMacroRecordHotkey),
            Config.HotKeyConfig.KeyMouseMacroRecordHotkey,
            Config.HotKeyConfig.KeyMouseMacroRecordHotkeyType,
            (_, _) =>
            {
                var vm = App.GetService<KeyMouseRecordPageViewModel>();
                if (vm == null)
                {
                    _logger.LogError("无法找到 KeyMouseRecordPageViewModel 单例对象！");
                    return;
                }
                if (!keyMouseRecordRunning)
                {
                    keyMouseRecordRunning = true;
                    Thread.Sleep(300); // 防止录进快捷键进去
                    vm.OnStartRecord();
                }
                else
                {
                    keyMouseRecordRunning = false;
                    vm.OnStopRecord();
                }
            }
        ));

        if (RuntimeHelper.IsDebug)
        {
            HotKeySettingModels.Add(new HotKeySettingModel(
                "启动/停止自动活动音游",
                nameof(Config.HotKeyConfig.AutoMusicGameHotkey),
                Config.HotKeyConfig.AutoMusicGameHotkey,
                Config.HotKeyConfig.AutoMusicGameHotkeyType,
                (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoMusicGame(); }
            ));
            HotKeySettingModels.Add(new HotKeySettingModel(
                "（测试）启动/停止自动追踪",
                nameof(Config.HotKeyConfig.AutoTrackHotkey),
                Config.HotKeyConfig.AutoTrackHotkey,
                Config.HotKeyConfig.AutoTrackHotkeyType,
                (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoTrack(); }
            ));
            HotKeySettingModels.Add(new HotKeySettingModel(
                "（测试）地图路线录制",
                nameof(Config.HotKeyConfig.MapPosRecordHotkey),
                Config.HotKeyConfig.MapPosRecordHotkey,
                Config.HotKeyConfig.MapPosRecordHotkeyType,
                (_, _) =>
                {
                    PathPointRecorder.Instance.Switch();
                }));
            HotKeySettingModels.Add(new HotKeySettingModel(
                "（测试）自动寻路",
                nameof(Config.HotKeyConfig.AutoTrackPathHotkey),
                Config.HotKeyConfig.AutoTrackPathHotkey,
                Config.HotKeyConfig.AutoTrackPathHotkeyType,
                (_, _) => { _taskSettingsPageViewModel.OnSwitchAutoTrackPath(); }
            ));

            var flag = false;
            var m = "";
            HotKeySettingModels.Add(new HotKeySettingModel(
                "（测试）测试",
                nameof(Config.HotKeyConfig.Test1Hotkey),
                Config.HotKeyConfig.Test1Hotkey,
                Config.HotKeyConfig.Test1HotkeyType,
                (_, _) =>
                {
                    // if (postMessageSimulator == null)
                    // {
                    //     postMessageSimulator = Simulation.PostMessage(TaskContext.Instance().GameHandle);
                    // }
                    // User32.GetCursorPos(out var p);
                    // Debug.WriteLine($"鼠标位置：{p.X},{p.Y}");
                    // // postMessageSimulator.KeyPressBackground(User32.VK.VK_W);
                    // GameCaptureRegion.GameRegion1080PPosMove(1340, 655);
                    // postMessageSimulator.LeftButtonClickBackground(1340, 655);
                    // Thread.Sleep(5);
                    // DesktopRegion.DesktopRegionMove(p.X, p.Y);

                    // 大地图位置
                    // TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
                    // Thread.Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待缓存图像
                    // AutoTrackPathTask.GetPositionFromBigMap();

                    // Simulation.SendInput.Mouse.MoveMouseBy(400, 0).Sleep(200)
                    //     .Keyboard.KeyPress(User32.VK.VK_W).Sleep(500);

                    // HWND hWnd = GetForegroundWindow();
                    //
                    // uint threadid = GetWindowThreadProcessId(hWnd, out var _);
                    //
                    // GUITHREADINFO lpgui = new GUITHREADINFO();
                    // lpgui.cbSize = (uint)Marshal.SizeOf(lpgui);
                    //
                    // if (GetGUIThreadInfo(threadid, ref lpgui))
                    // {
                    // if (lpgui.hwndCaret != 0)
                    // {
                    //     _logger.LogInformation("输入状态");
                    //     return;
                    // }
                    // }
                    // _logger.LogInformation("非输入状态");
                }
            ));

            HotKeySettingModels.Add(new HotKeySettingModel(
                "（测试）测试2",
                nameof(Config.HotKeyConfig.Test2Hotkey),
                Config.HotKeyConfig.Test2Hotkey,
                Config.HotKeyConfig.Test2HotkeyType,
                (_, _) =>
                {
                    // _logger.LogInformation("开始重放脚本");
                    // Task.Run(async () =>
                    // {
                    //     await KeyMouseMacroPlayer.PlayMacro(m);
                    //     _logger.LogInformation("播放脚本结束");
                    // });
                }
            ));
        }
    }

    private string ToChinese(bool enabled)
    {
        return enabled.ToChinese();
    }
}
